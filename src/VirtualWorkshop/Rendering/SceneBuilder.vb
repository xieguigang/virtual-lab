Imports System.Diagnostics
Imports System.Drawing
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports VirtualWorkshop.Model
Imports VirtualWorkshop.Simulation

Namespace Rendering

    ''' <summary>
    ''' 把仿真中的刚体位姿转成渲染用的三角面（世界坐标）。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 每个连杆的网格顶点是"link 局部坐标"，而刚体原点是 URDF 的惯量原点，
    ''' 所以要先减去惯量原点再用刚体位姿变换：<c>p = body.Position + R·(v - inertialOrigin)</c>。
    ''' </para>
    ''' <para>
    ''' 绘制用的画刷按颜色缓存，避免每帧新建几千个 <see cref="SolidBrush"/>。
    ''' </para>
    ''' </remarks>
    Public Class SceneBuilder

        ''' <summary>当前缓存的三角面数量。</summary>
        Public Property TriangleCount As Integer

        ''' <summary>最近一次构建耗时（毫秒）。</summary>
        Public Property LastBuildMs As Double

        Private ReadOnly _brushes As New Dictionary(Of String, Microsoft.VisualBasic.Imaging.Brush)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _watch As New Stopwatch()

        ''' <summary>构建整个场景的三角面（世界坐标，已减去渲染锚点）。</summary>
        Public Function BuildSurfaces(simulation As ArmSimulation, anchor As Vector3) As List(Of Surface)
            _watch.Restart()

            Dim faces As New List(Of Surface)(4096)

            Call AppendArm(simulation, anchor, faces)
            Call AppendFingers(simulation, anchor, faces)
            Call AppendObjects(simulation, anchor, faces)

            TriangleCount = faces.Count
            LastBuildMs = _watch.Elapsed.TotalMilliseconds

            Return faces
        End Function

        Private Sub AppendArm(simulation As ArmSimulation, anchor As Vector3, faces As List(Of Surface))
            If simulation.Assembly.Meshes Is Nothing Then Return

            For Each pair As KeyValuePair(Of String, RigidBody3D) In simulation.LinkBodies
                Dim body As RigidBody3D = pair.Value
                Dim origin As Vector3 = simulation.Assembly.InertialOriginOf(pair.Key)

                For Each mesh As LinkMesh In MeshesOf(simulation, pair.Key)
                    Call AppendMesh(mesh, body.Position, body.Orientation, origin, anchor, faces)
                Next
            Next
        End Sub

        Private Sub AppendFingers(simulation As ArmSimulation, anchor As Vector3, faces As List(Of Surface))
            For i As Integer = 0 To simulation.FingerBodies.Count - 1
                If i >= simulation.FingerMeshes.Count Then Continue For

                Dim body As RigidBody3D = simulation.FingerBodies(i)

                ' 手指网格的顶点已经相对手指质心
                Call AppendMesh(simulation.FingerMeshes(i), body.Position, body.Orientation, Vector3.Zero, anchor, faces)
            Next
        End Sub

        Private Sub AppendObjects(simulation As ArmSimulation, anchor As Vector3, faces As List(Of Surface))
            For Each item As BlockInstance In simulation.Objects
                If item.Render Is Nothing Then Continue For

                Call AppendMesh(item.Render, item.Body.Position, item.Body.Orientation, Vector3.Zero, anchor, faces)
            Next
        End Sub

        ''' <summary>某个 link 上挂的全部网格（Camera / Finger 等子节点会属于同一个 link）。</summary>
        Private Function MeshesOf(simulation As ArmSimulation, linkName As String) As List(Of LinkMesh)
            Dim out As New List(Of LinkMesh)()

            For Each mesh As LinkMesh In simulation.Assembly.Meshes.Meshes.Values
                If String.Equals(mesh.OwnerLink, linkName, StringComparison.OrdinalIgnoreCase) Then
                    ' 手指网格单独渲染（它跟着手指刚体走）
                    If mesh.LinkName.StartsWith("Finger", StringComparison.OrdinalIgnoreCase) Then Continue For

                    out.Add(mesh)
                End If
            Next

            Return out
        End Function

        Private Sub AppendMesh(mesh As LinkMesh, position As Vector3, rotation As Quaternion,
                              origin As Vector3, anchor As Vector3, faces As List(Of Surface))
            If mesh Is Nothing OrElse mesh.TriangleCount = 0 Then Return

            Dim brush As Microsoft.VisualBasic.Imaging.Brush = BrushOf(GetColor(mesh))

            For Each triangle As MeshTriangle In mesh.Triangles
                Dim a As Point3D = Transform(triangle.A, position, rotation, origin, anchor)
                Dim b As Point3D = Transform(triangle.B, position, rotation, origin, anchor)
                Dim c As Point3D = Transform(triangle.C, position, rotation, origin, anchor)

                faces.Add(New Surface(New Point3D() {a, b, c}, brush))
            Next
        End Sub

        Private Function GetColor(mesh As LinkMesh) As String
            For Each triangle As MeshTriangle In mesh.Triangles
                If Not String.IsNullOrEmpty(triangle.Color) Then Return triangle.Color
            Next

            Return "#B8BDC7"
        End Function

        ''' <summary>局部点 → 世界点 → 减去渲染锚点。</summary>
        Public Shared Function Transform(p As Point3D, position As Vector3, rotation As Quaternion,
                                         origin As Vector3, anchor As Vector3) As Point3D
            Dim local As New Vector3(CSng(p.X - origin.x), CSng(p.Y - origin.y), CSng(p.Z - origin.z))
            Dim world As Vector3 = position + rotation.Rotate(local)

            Return New Point3D(CSng(world.x - anchor.x), CSng(world.y - anchor.y), CSng(world.z - anchor.z))
        End Function

        ''' <summary>
        ''' 按颜色取画刷（带缓存）。
        ''' </summary>
        ''' <remarks>
        ''' 注意这里用的是 <c>Microsoft.VisualBasic.Imaging.SolidBrush</c>（Core 程序集），
        ''' 而不是 <c>System.Drawing.SolidBrush</c>：<c>Drawing3D.Surface.brush</c> 声明的类型是
        ''' 前者，两者同名但程序集不同，混用会得到 "Brush cannot be converted to Brush"。
        ''' </remarks>
        Public Function BrushOf(color As String) As Microsoft.VisualBasic.Imaging.Brush
            Dim key As String = If(String.IsNullOrEmpty(color), "#B8BDC7", color)
            Dim cached As Microsoft.VisualBasic.Imaging.Brush = Nothing

            If _brushes.TryGetValue(key, cached) Then Return cached

            cached = New Microsoft.VisualBasic.Imaging.SolidBrush(ParseColor(key))
            _brushes(key) = cached

            Return cached
        End Function

        Public Shared Function ParseColor(text As String) As Color
            If String.IsNullOrEmpty(text) Then Return Color.FromArgb(184, 189, 199)

            Try
                Dim converted As Color = ColorTranslator.FromHtml(text)

                If converted.IsEmpty Then Return Color.FromArgb(184, 189, 199)

                Return converted
            Catch ex As Exception
                Return Color.FromArgb(184, 189, 199)
            End Try
        End Function

        Public Sub ClearBrushes()
            For Each brush As Microsoft.VisualBasic.Imaging.Brush In _brushes.Values
                Try
                    brush.Dispose()
                Catch ex As Exception
                    ' 忽略
                End Try
            Next

            _brushes.Clear()
        End Sub

    End Class

End Namespace
