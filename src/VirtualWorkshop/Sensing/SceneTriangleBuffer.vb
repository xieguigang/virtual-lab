Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports VirtualWorkshop.Model
Imports VirtualWorkshop.Simulation

Namespace Sensing

    ''' <summary>
    ''' 把仿真世界压平成"世界坐标三角面 + 颜色"的数组，供软件光栅化使用。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 变换公式与 <c>Rendering.SceneBuilder</c> 完全一致：
    ''' <c>world = body.Position + R·(local - inertialOrigin)</c>，但**不减去渲染锚点**——
    ''' 相机用的是真实世界坐标。手指与积木的网格顶点已经相对自身质心，所以 origin 取零。
    ''' </para>
    ''' <para>
    ''' 数组按需扩容并复用，避免每帧产生几万个托管对象。
    ''' </para>
    ''' </remarks>
    Public Class SceneTriangleBuffer

        ''' <summary>每 9 个 Single 是一个三角形的三个顶点。</summary>
        Public Property Vertices As Single() = New Single(-1) {}

        ''' <summary>每个三角形一个 0xRRGGBB 颜色。</summary>
        Public Property Colors As Integer() = New Integer(-1) {}

        ''' <summary>当前有效三角形数量。</summary>
        Public Property Count As Integer

        Private ReadOnly _colorCache As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _meshesByLink As New Dictionary(Of String, List(Of LinkMesh))(StringComparer.OrdinalIgnoreCase)
        Private _grouped As Boolean

        ''' <summary>重建整帧的三角面。</summary>
        Public Sub Build(simulation As ArmSimulation)
            If simulation Is Nothing Then
                Count = 0
                Return
            End If

            Count = 0

            Call GroupMeshes(simulation)

            ' 机械臂连杆（含 Camera 外壳）
            For Each pair As KeyValuePair(Of String, RigidBody3D) In simulation.LinkBodies
                Dim meshes As List(Of LinkMesh) = Nothing

                If Not _meshesByLink.TryGetValue(pair.Key, meshes) Then Continue For

                Dim origin As Vector3 = simulation.Assembly.InertialOriginOf(pair.Key)
                Dim body As RigidBody3D = pair.Value

                For Each mesh As LinkMesh In meshes
                    Call Append(mesh, body.Position, body.Orientation, origin)
                Next
            Next

            ' 手指（顶点已相对手指质心）
            For i As Integer = 0 To simulation.FingerBodies.Count - 1
                If i >= simulation.FingerMeshes.Count Then Continue For

                Dim body As RigidBody3D = simulation.FingerBodies(i)

                Call Append(simulation.FingerMeshes(i), body.Position, body.Orientation, Vector3.Zero)
            Next

            ' 积木 / 静态道具（顶点已相对质心）
            For Each item As BlockInstance In simulation.Objects
                If item.Render Is Nothing Then Continue For

                Call Append(item.Render, item.Body.Position, item.Body.Orientation, Vector3.Zero)
            Next

            ' 平台与地面不是 Objects 里的物体，必须在这里单独补上。
            ' 之前漏了这一段，导致"视口能看到弹簧木板、相机画面里却没有"（balance_beam 的相机 bug）。
            ' 以后新增任何"直接 world.Add 的刚体"，都要同时改这里与 Rendering.SceneBuilder。
            Call AppendPlatform(simulation)
            Call AppendGround(simulation)
        End Sub

        ''' <summary>弹簧木板平台（木板 + 行程限位挡块）。</summary>
        Private Sub AppendPlatform(simulation As ArmSimulation)
            Dim platform As SpringPlatform = simulation.Platform

            If platform Is Nothing Then Return

            If platform.PlateMesh IsNot Nothing AndAlso platform.Plate IsNot Nothing Then
                Call Append(platform.PlateMesh, platform.Plate.Position, platform.Plate.Orientation, Vector3.Zero)
            End If

            If platform.StopperMesh IsNot Nothing AndAlso platform.Stopper IsNot Nothing Then
                Call Append(platform.StopperMesh, platform.Stopper.Position, platform.Stopper.Orientation, Vector3.Zero)
            End If
        End Sub

        ''' <summary>
        ''' 地面（以世界原点为中心的一大块四边形）。
        ''' </summary>
        ''' <remarks>
        ''' 不画地面时，俯视画面里所有物体都像悬在背景色里，很难判断"桌子/瓶子到底在哪"。
        ''' 只加两个三角形，填充成本与原来的背景清除相当。
        ''' </remarks>
        Private Sub AppendGround(simulation As ArmSimulation)
            Const half As Double = 1.2

            Dim z As Double = simulation.GroundZ
            Dim color As Integer = ColorOf("#E2E8F0")

            Call EnsureCapacity(Count + 2)

            Dim vertices As Single() = Me.Vertices
            Dim colors As Integer() = Me.Colors
            Dim offset As Integer = Count * 9

            ' 两个三角形拼成一个正方形（逆时针，双面渲染所以顺序不敏感）
            Call WritePoint(vertices, offset, New Vector3(-half, -half, z))
            Call WritePoint(vertices, offset + 3, New Vector3(half, -half, z))
            Call WritePoint(vertices, offset + 6, New Vector3(half, half, z))

            Call WritePoint(vertices, offset + 9, New Vector3(-half, -half, z))
            Call WritePoint(vertices, offset + 12, New Vector3(half, half, z))
            Call WritePoint(vertices, offset + 15, New Vector3(-half, half, z))

            colors(Count) = color
            colors(Count + 1) = color

            Count += 2
        End Sub

        Private Shared Sub WritePoint(target As Single(), offset As Integer, point As Vector3)
            target(offset) = CSng(point.x)
            target(offset + 1) = CSng(point.y)
            target(offset + 2) = CSng(point.z)
        End Sub

        ''' <summary>按 OwnerLink 分组网格（只做一次）。</summary>
        Private Sub GroupMeshes(simulation As ArmSimulation)
            If _grouped Then Return

            _grouped = True
            _meshesByLink.Clear()

            Dim scene As GlbScene = simulation.Assembly.Meshes

            If scene Is Nothing Then Return

            For Each mesh As LinkMesh In scene.Meshes.Values
                ' 手指跟着手指刚体走，不挂在 link6 上
                If mesh.LinkName.StartsWith("Finger", StringComparison.OrdinalIgnoreCase) Then Continue For
                If String.IsNullOrEmpty(mesh.OwnerLink) Then Continue For

                Dim list As List(Of LinkMesh) = Nothing

                If Not _meshesByLink.TryGetValue(mesh.OwnerLink, list) Then
                    list = New List(Of LinkMesh)()
                    _meshesByLink(mesh.OwnerLink) = list
                End If

                list.Add(mesh)
            Next
        End Sub

        ''' <summary>把 <see cref="LinkMesh"/> 追加到缓冲（自动扩容）。</summary>
        Private Sub Append(mesh As LinkMesh, position As Vector3, rotation As Quaternion,
                           inertialOrigin As Vector3)

            If mesh Is Nothing OrElse mesh.Triangles.Count = 0 Then Return

            Call EnsureCapacity(Count + mesh.Triangles.Count)

            Dim vertices As Single() = Me.Vertices
            Dim colors As Integer() = Me.Colors

            For Each triangle As MeshTriangle In mesh.Triangles
                Dim index As Integer = Count * 9

                Call WriteVertex(vertices, index, triangle.A, position, rotation, inertialOrigin)
                Call WriteVertex(vertices, index + 3, triangle.B, position, rotation, inertialOrigin)
                Call WriteVertex(vertices, index + 6, triangle.C, position, rotation, inertialOrigin)

                colors(Count) = ColorOf(triangle.Color)
                Count += 1
            Next
        End Sub

        Private Shared Sub WriteVertex(target As Single(), offset As Integer, point As Point3D,
                                       position As Vector3, rotation As Quaternion, origin As Vector3)

            Dim local As New Vector3(CSng(point.X - origin.x), CSng(point.Y - origin.y), CSng(point.Z - origin.z))
            Dim world As Vector3 = position + rotation.Rotate(local)

            target(offset) = CSng(world.x)
            target(offset + 1) = CSng(world.y)
            target(offset + 2) = CSng(world.z)
        End Sub

        Private Sub EnsureCapacity(required As Integer)
            If Vertices.Length >= required * 9 AndAlso Colors.Length >= required Then Return

            Dim capacity As Integer = System.Math.Max(256, Count + 512)

            While capacity < required
                capacity *= 2
            End While

            Dim oldVertices As Single() = Vertices
            Dim oldColors As Integer() = Colors

            Vertices = New Single(capacity * 9 - 1) {}
            Colors = New Integer(capacity - 1) {}

            If oldVertices.Length > 0 Then
                Array.Copy(oldVertices, Vertices, System.Math.Min(oldVertices.Length, Vertices.Length))
            End If

            If oldColors.Length > 0 Then
                Array.Copy(oldColors, Colors, System.Math.Min(oldColors.Length, Colors.Length))
            End If
        End Sub

        ''' <summary>hex 颜色 → 0xRRGGBB（带缓存）。</summary>
        Private Function ColorOf(text As String) As Integer
            Dim key As String = If(String.IsNullOrEmpty(text), "#B8BDC7", text)
            Dim cached As Integer

            If _colorCache.TryGetValue(key, cached) Then Return cached

            Dim sample As System.Drawing.Color = Rendering.SceneBuilder.ParseColor(key)

            ' 注意：VB 的移位运算符**保持左侧操作数的类型**，Byte << 16 会溢出成 0，
            ' 必须先 CInt 再移位，否则红色/绿色通道全丢、所有物体都变成蓝色
            Dim parsed As Integer = (CInt(sample.R) << 16) Or (CInt(sample.G) << 8) Or CInt(sample.B)

            _colorCache(key) = parsed

            Return parsed
        End Function

        ''' <summary>重置分组缓存（模型换掉时调用）。</summary>
        Public Sub Invalidate()
            _grouped = False
            _meshesByLink.Clear()
            _colorCache.Clear()
            Count = 0
        End Sub

    End Class

End Namespace
