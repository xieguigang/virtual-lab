Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Sensing

    ''' <summary>
    ''' 纯托管软件光栅化：世界三角面 → 相机空间 → 近裁剪 → 透视投影 → z-buffer 填充。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 相机坐标系直接取 link6 的局部系：<c>x</c> 沿光轴向前、<c>z</c> 朝上、<c>y</c> 指向画面左侧，
    ''' 所以屏幕横坐标是 <c>cx - (y/d)·f</c>、纵坐标是 <c>cy - (z/d)·f</c>。
    ''' </para>
    ''' <para>
    ''' 深度缓冲内部存 <c>1/d</c>（线性插值无需透视校正），渲染结束后再转回米。
    ''' </para>
    ''' <para>
    ''' 着色是单方向光的平面着色（Lambert + 环境光），背面用 |dot| 衰减，与三维视口的观感接近。
    ''' </para>
    ''' </remarks>
    Public NotInheritable Class SoftwareRasterizer

        ''' <summary>近裁剪面（米）。</summary>
        Public Const NearPlane As Single = 0.02F

        ''' <summary>指向光源的方向（世界坐标，已归一化）。</summary>
        Private Shared ReadOnly LightDirection As New Vector3(-0.35, -0.45, 0.82)

        ''' <summary>渲染一帧到给定的 RGB 与深度缓冲。</summary>
        Public Sub Render(buffer As SceneTriangleBuffer, pose As CameraPose, config As CameraConfig,
                          rgb As Byte(), depth As Single())

            Dim width As Integer = config.Width
            Dim height As Integer = config.Height

            Call Clear(rgb, depth, config)

            If buffer Is Nothing OrElse buffer.Count = 0 OrElse pose Is Nothing Then Return

            Dim focal As Double = config.FocalPixels
            Dim cx As Double = width / 2.0
            Dim cy As Double = height / 2.0
            Dim ambient As Double = config.Ambient
            Dim maxDistance As Double = config.MaxDistance

            Dim rotation As Quaternion = pose.Orientation
            Dim origin As Vector3 = pose.Position

            Dim vertices As Single() = buffer.Vertices
            Dim colors As Integer() = buffer.Colors

            ' 相机空间顶点（3 个）+ 裁剪输出（最多 4 个）
            Dim inX(2), inY(2), inZ(2) As Double
            Dim outX(3), outY(3), outZ(3), outIz(3) As Double

            For t As Integer = 0 To buffer.Count - 1
                Dim offset As Integer = t * 9

                Dim a As New Vector3(vertices(offset), vertices(offset + 1), vertices(offset + 2))
                Dim b As New Vector3(vertices(offset + 3), vertices(offset + 4), vertices(offset + 5))
                Dim c As New Vector3(vertices(offset + 6), vertices(offset + 7), vertices(offset + 8))

                ' 世界法线（用于着色），退化三角形直接跳过
                Dim normal As Vector3 = Vector3.Cross(b - a, c - a)
                Dim normalLength As Double = System.Math.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z)

                If normalLength < 1.0E-9 Then Continue For

                normal = New Vector3(normal.x / normalLength, normal.y / normalLength, normal.z / normalLength)

                Dim lambert As Double = normal.x * LightDirection.x + normal.y * LightDirection.y + normal.z * LightDirection.z

                If lambert < 0 Then lambert = -lambert * 0.45

                Dim intensity As Double = ambient + (1.0 - ambient) * lambert

                If intensity > 1.0 Then intensity = 1.0
                If intensity < 0 Then intensity = 0

                ' 世界 → 相机
                Dim va As Vector3 = rotation.Unrotate(a - origin)
                Dim vb As Vector3 = rotation.Unrotate(b - origin)
                Dim vc As Vector3 = rotation.Unrotate(c - origin)

                ' 整块太远就跳过（用最近顶点粗略判断）
                If va.x > maxDistance AndAlso vb.x > maxDistance AndAlso vc.x > maxDistance Then Continue For

                inX(0) = va.x : inY(0) = va.y : inZ(0) = va.z
                inX(1) = vb.x : inY(1) = vb.y : inZ(1) = vb.z
                inX(2) = vc.x : inY(2) = vc.y : inZ(2) = vc.z

                ' 近平面裁剪（Sutherland-Hodgman，单平面）
                Dim count As Integer = 0

                For i As Integer = 0 To 2
                    Dim j As Integer = (i + 1) Mod 3
                    Dim di As Double = inX(i)
                    Dim dj As Double = inX(j)
                    Dim insideI As Boolean = di >= NearPlane
                    Dim insideJ As Boolean = dj >= NearPlane

                    If insideI Then
                        outX(count) = inX(i)
                        outY(count) = inY(i)
                        outZ(count) = inZ(i)
                        count += 1
                    End If

                    If insideI <> insideJ Then
                        Dim ratio As Double = (NearPlane - di) / (dj - di)

                        If count < 4 Then
                            outX(count) = di + (dj - di) * ratio
                            outY(count) = inY(i) + (inY(j) - inY(i)) * ratio
                            outZ(count) = inZ(i) + (inZ(j) - inZ(i)) * ratio
                            count += 1
                        End If
                    End If

                    If count >= 4 Then Exit For
                Next

                If count < 3 Then Continue For

                ' 投影到屏幕（同时算 1/z）
                Dim behindNear As Boolean = False

                For i As Integer = 0 To count - 1
                    Dim d As Double = outX(i)

                    If d < NearPlane Then
                        behindNear = True
                        Exit For
                    End If

                    Dim invDepth As Double = 1.0 / d

                    outIz(i) = invDepth
                    outX(i) = cx - (outY(i) / d) * focal
                    outY(i) = cy - (outZ(i) / d) * focal
                Next

                If behindNear OrElse count < 3 Then Continue For

                Dim color As Integer = colors(t)
                Dim red As Integer = (color >> 16) And &HFF
                Dim green As Integer = (color >> 8) And &HFF
                Dim blue As Integer = color And &HFF

                ' 扇形三角化
                For k As Integer = 1 To count - 2
                    Call RasterizeTriangle(outX(0), outY(0), outIz(0),
                                           outX(k), outY(k), outIz(k),
                                           outX(k + 1), outY(k + 1), outIz(k + 1),
                                           width, height, red, green, blue, intensity, rgb, depth)
                Next
            Next

            ' 1/z 缓冲 → 米
            For i As Integer = 0 To depth.Length - 1
                Dim invDepth As Single = depth(i)

                depth(i) = If(invDepth > 0, CSng(1.0 / invDepth), 0.0F)
            Next
        End Sub

        ''' <summary>填充背景色并把 z 缓冲清零（1/d = 0 表示无穷远）。</summary>
        Private Shared Sub Clear(rgb As Byte(), depth As Single(), config As CameraConfig)
            Dim red As Byte = CByte((config.BackgroundColor >> 16) And &HFF)
            Dim green As Byte = CByte((config.BackgroundColor >> 8) And &HFF)
            Dim blue As Byte = CByte(config.BackgroundColor And &HFF)

            For i As Integer = 0 To depth.Length - 1
                depth(i) = 0.0F
            Next

            For i As Integer = 0 To rgb.Length - 3 Step 3
                rgb(i) = red
                rgb(i + 1) = green
                rgb(i + 2) = blue
            Next
        End Sub

        ''' <summary>光栅化一个屏幕空间三角形（含 1/z 深度测试）。</summary>
        Private Shared Sub RasterizeTriangle(x0 As Double, y0 As Double, iz0 As Double,
                                            x1 As Double, y1 As Double, iz1 As Double,
                                            x2 As Double, y2 As Double, iz2 As Double,
                                            width As Integer, height As Integer,
                                            red As Integer, green As Integer, blue As Integer,
                                            intensity As Double,
                                            rgb As Byte(), zbuffer As Single())

            Dim area As Double = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)

            If System.Math.Abs(area) < 1.0E-9 Then Return

            ' 统一成正向，便于用重心坐标判断
            If area < 0 Then
                Dim tx As Double = x1 : x1 = x2 : x2 = tx
                Dim ty As Double = y1 : y1 = y2 : y2 = ty
                Dim tiz As Double = iz1 : iz1 = iz2 : iz2 = tiz

                area = -area
            End If

            Dim minX As Integer = CInt(System.Math.Floor(System.Math.Min(x0, System.Math.Min(x1, x2))))
            Dim maxX As Integer = CInt(System.Math.Ceiling(System.Math.Max(x0, System.Math.Max(x1, x2))))
            Dim minY As Integer = CInt(System.Math.Floor(System.Math.Min(y0, System.Math.Min(y1, y2))))
            Dim maxY As Integer = CInt(System.Math.Ceiling(System.Math.Max(y0, System.Math.Max(y1, y2))))

            If minX < 0 Then minX = 0
            If minY < 0 Then minY = 0
            If maxX > width - 1 Then maxX = width - 1
            If maxY > height - 1 Then maxY = height - 1

            If minX > maxX OrElse minY > maxY Then Return

            Dim shadeR As Double = System.Math.Min(255.0, red * intensity)
            Dim shadeG As Double = System.Math.Min(255.0, green * intensity)
            Dim shadeB As Double = System.Math.Min(255.0, blue * intensity)

            Dim byteR As Byte = CByte(shadeR)
            Dim byteG As Byte = CByte(shadeG)
            Dim byteB As Byte = CByte(shadeB)

            Dim inverseArea As Double = 1.0 / area

            For py As Integer = minY To maxY
                Dim sampleY As Double = py + 0.5

                For px As Integer = minX To maxX
                    Dim sampleX As Double = px + 0.5

                    Dim w0 As Double = ((x1 - sampleX) * (y2 - sampleY) - (x2 - sampleX) * (y1 - sampleY)) * inverseArea

                    If w0 < 0 Then Continue For

                    Dim w1 As Double = ((x2 - sampleX) * (y0 - sampleY) - (x0 - sampleX) * (y2 - sampleY)) * inverseArea

                    If w1 < 0 Then Continue For

                    Dim w2 As Double = 1.0 - w0 - w1

                    If w2 < 0 Then Continue For

                    Dim invDepth As Double = w0 * iz0 + w1 * iz1 + w2 * iz2
                    Dim index As Integer = py * width + px

                    If invDepth <= zbuffer(index) Then Continue For

                    zbuffer(index) = CSng(invDepth)

                    Dim colorIndex As Integer = index * 3
                    rgb(colorIndex) = byteR
                    rgb(colorIndex + 1) = byteG
                    rgb(colorIndex + 2) = byteB
                Next
            Next
        End Sub

    End Class

End Namespace
