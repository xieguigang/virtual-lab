Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports VirtualWorkshop.Model
Imports std = System.Math

Namespace Simulation

    ''' <summary>三角瓶的几何资产：渲染网格 + 凸碰撞体 + 尺寸信息。</summary>
    Public Class FlaskGeometry

        ''' <summary>渲染网格（顶点已相对质心）。</summary>
        Public Property RenderMesh As LinkMesh

        ''' <summary>碰撞用的凸网格（旋转体，顶点相对质心）。</summary>
        Public Property Collider As ConvexMesh3D

        ''' <summary>瓶高（米）。</summary>
        Public Property Height As Double

        ''' <summary>瓶底半径（米）。</summary>
        Public Property BaseRadius As Double

        ''' <summary>瓶颈半径（米）。</summary>
        Public Property NeckRadius As Double

        ''' <summary>质心相对"瓶底中心"的偏移（局部坐标，Z-up）。</summary>
        Public Property PivotOffset As Vector3

        ''' <summary>夹持时 TCP 相对瓶底的高度（米）。</summary>
        Public Property GripHeight As Double

        ''' <summary>来源文件。</summary>
        Public Property SourceFile As String

        Public Overrides Function ToString() As String
            Return $"flask h={Height:F3} baseR={BaseRadius:F4} neckR={NeckRadius:F4} gripZ={GripHeight:F3}"
        End Function

    End Class

    ''' <summary>
    ''' 从 <c>models\erlenmeyer_flask.glb</c> 载入三角瓶几何。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 渲染用 glb 的**原始三角面**（外观与真实三角瓶一致），碰撞用按高度分层的
    ''' **旋转体凸包**：先按层取最大半径，再自下而上做单调包络（半径不允许变大），
    ''' 这样生成的旋转体一定是凸的 —— 瓶口外张的原始网格如果不做包络，
    ''' <c>ConvexPolyhedronCollider3D</c> 会出现穿透。
    ''' </para>
    ''' <para>
    ''' 只解析一次并缓存：5 个瓶子共用同一份网格与碰撞体，spawn 时只 new 一个刚体。
    ''' </para>
    ''' </remarks>
    Public Module FlaskAssets

        ''' <summary>模型文件名（在 models 目录下找）。</summary>
        Public Const ModelFileName As String = "erlenmeyer_flask.glb"

        ''' <summary>统一后的瓶高（米）。</summary>
        Public Const TargetHeight As Double = 0.13

        ''' <summary>旋转体的圆周分段数。</summary>
        Private Const Sides As Integer = 18

        ''' <summary>沿高度分层的层数。</summary>
        Private Const Layers As Integer = 24

        Private _geometry As FlaskGeometry
        Private _attempted As Boolean

        ''' <summary>按颜色缓存的染色渲染网格（5 个瓶子按溶液体积取不同深浅）。</summary>
        Private ReadOnly _tinted As New Dictionary(Of String, LinkMesh)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' 取一份染成指定颜色的渲染网格（缓存）。
        ''' </summary>
        ''' <remarks>
        ''' glb 自带的玻璃材质色是固定的，而"不同溶液体积"要看颜色深浅才好分辨，
        ''' 所以按颜色复制一份网格；同类颜色只复制一次。
        ''' </remarks>
        Public Function Tinted(geometry As FlaskGeometry, color As String) As LinkMesh
            If geometry Is Nothing OrElse geometry.RenderMesh Is Nothing Then Return Nothing

            Dim key As String = If(String.IsNullOrEmpty(color), "#DCEEF8", color)
            Dim cached As LinkMesh = Nothing

            If _tinted.TryGetValue(key, cached) Then Return cached

            Dim source As LinkMesh = geometry.RenderMesh
            Dim copy As New LinkMesh With {
                .LinkName = source.LinkName,
                .OwnerLink = source.OwnerLink,
                .NodePath = source.NodePath
            }

            For Each triangle As MeshTriangle In source.Triangles
                copy.Triangles.Add(New MeshTriangle(triangle.A, triangle.B, triangle.C, key))
            Next

            Call copy.RecalculateBounds()

            _tinted(key) = copy

            Return copy
        End Function

        ''' <summary>取得三角瓶几何；模型缺失或解析失败时返回 <c>Nothing</c>（调用方退回程序化锥台）。</summary>
        Public Function Geometry() As FlaskGeometry
            If _geometry IsNot Nothing Then Return _geometry
            If _attempted Then Return Nothing

            _attempted = True

            Try
                Dim file As String = ResolveFile()

                If file Is Nothing Then
                    Console.WriteLine("[flask] 找不到 " & ModelFileName & "，三角瓶将退回程序化锥台")
                    Return Nothing
                End If

                _geometry = Build(file)
            Catch ex As Exception
                Console.WriteLine("[flask] 载入失败: " & ex.Message)
                _geometry = Nothing
            End Try

            Return _geometry
        End Function

        ''' <summary>在常见目录里找三角瓶模型。</summary>
        Private Function ResolveFile() As String
            Dim bases As String() = {
                IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"),
                IO.Path.Combine(Environment.CurrentDirectory, "models"),
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                "G:\virtual-lab\models"
            }

            For Each basePath As String In bases
                Dim candidate As String = IO.Path.Combine(basePath, ModelFileName)

                If IO.File.Exists(candidate) Then Return IO.Path.GetFullPath(candidate)
            Next

            Return Nothing
        End Function

        Private Function Build(file As String) As FlaskGeometry
            Dim raw As LinkMesh = GlbLinkLoader.LoadProp(file)

            If raw Is Nothing OrElse raw.TriangleCount = 0 Then
                Console.WriteLine("[flask] glb 里没有三角形: " & file)
                Return Nothing
            End If

            Dim low As Point3D = raw.BoundsMin
            Dim high As Point3D = raw.BoundsMax
            Dim rawHeight As Double = high.Z - low.Z

            If rawHeight < 1.0E-6 Then Return Nothing

            Dim scale As Double = TargetHeight / rawHeight
            Dim cx As Double = (low.X + high.X) * 0.5
            Dim cy As Double = (low.Y + high.Y) * 0.5

            ' 缩放并把"瓶底中心"摆到 (0,0,0)、瓶轴沿 +Z
            Dim transformed As New List(Of Vector3)(raw.TriangleCount * 3)

            For Each triangle As MeshTriangle In raw.Triangles
                transformed.Add(New Vector3(
                    CSng((triangle.A.X - cx) * scale),
                    CSng((triangle.A.Y - cy) * scale),
                    CSng((triangle.A.Z - low.Z) * scale)))
                transformed.Add(New Vector3(
                    CSng((triangle.B.X - cx) * scale),
                    CSng((triangle.B.Y - cy) * scale),
                    CSng((triangle.B.Z - low.Z) * scale)))
                transformed.Add(New Vector3(
                    CSng((triangle.C.X - cx) * scale),
                    CSng((triangle.C.Y - cy) * scale),
                    CSng((triangle.C.Z - low.Z) * scale)))
            Next

            ' ---- 轴向半径剖面 ----
            Dim profile(Layers - 1) As Double
            Dim sampleColor As String = Nothing

            For Each vertex As Vector3 In transformed
                Dim layer As Integer = CInt(std.Floor(vertex.z / TargetHeight * (Layers - 1)))

                layer = std.Max(0, std.Min(Layers - 1, layer))

                Dim radius As Double = std.Sqrt(vertex.x * vertex.x + vertex.y * vertex.y)

                If radius > profile(layer) Then profile(layer) = radius
            Next

            ' 单调包络：自下而上半径不允许变大，保证旋转体凸
            For i As Integer = 1 To Layers - 1
                If profile(i) > profile(i - 1) Then profile(i) = profile(i - 1)
            Next

            ' ---- 旋转体 ----
            Dim vertices As New List(Of Vector3)()

            For i As Integer = 0 To Layers - 1
                Dim z As Double = TargetHeight * i / (Layers - 1)
                Dim radius As Double = std.Max(profile(i), 0.0015)

                For j As Integer = 0 To Sides - 1
                    Dim angle As Double = 2.0 * std.PI * j / Sides

                    vertices.Add(New Vector3(
                        CSng(radius * std.Cos(angle)),
                        CSng(radius * std.Sin(angle)),
                        CSng(z)))
                Next
            Next

            Dim faces As New List(Of Integer())()

            For i As Integer = 0 To Layers - 2
                For j As Integer = 0 To Sides - 1
                    Dim nextIndex As Integer = (j + 1) Mod Sides
                    Dim b0 As Integer = i * Sides + j
                    Dim b1 As Integer = i * Sides + nextIndex
                    Dim t0 As Integer = (i + 1) * Sides + j
                    Dim t1 As Integer = (i + 1) * Sides + nextIndex

                    faces.Add(New Integer() {b0, b1, t1, t0})
                Next
            Next

            Dim bottom(Sides - 1) As Integer
            Dim top(Sides - 1) As Integer

            For j As Integer = 0 To Sides - 1
                bottom(Sides - 1 - j) = j
                top(j) = (Layers - 1) * Sides + j
            Next

            faces.Add(bottom)
            faces.Add(top)

            ' 质心取"瓶底中心 + 半个瓶高"的几何中心：
            ' 不用 ConvexMesh3D.Centered() —— 它的 VolumeCentroid() 对这种
            ' "下粗上细"的旋转体算出来的 z 只有 8 mm（正确值约 65 mm），
            ' 导致刚体几乎把碰撞体全部塞到地面以下。
            Dim pivot As New Vector3(0, 0, CSng(TargetHeight * 0.5))
            Dim centered(vertices.Count - 1) As Vector3

            For i As Integer = 0 To vertices.Count - 1
                centered(i) = vertices(i) - pivot
            Next

            Dim collider As New ConvexMesh3D(centered, faces.ToArray()) With {
                .PivotOffset = pivot
            }

            ' ---- 渲染网格（与碰撞体用同一个质心）----
            Dim render As New LinkMesh With {
                .LinkName = "flask",
                .OwnerLink = "flask",
                .NodePath = file
            }

            For Each triangle As MeshTriangle In raw.Triangles
                If sampleColor Is Nothing AndAlso Not String.IsNullOrEmpty(triangle.Color) Then sampleColor = triangle.Color
            Next

            If sampleColor Is Nothing Then sampleColor = "#DCEEF8"

            Dim cursor As Integer = 0

            For Each triangle As MeshTriangle In raw.Triangles
                Dim a As Vector3 = transformed(cursor) - pivot
                Dim b As Vector3 = transformed(cursor + 1) - pivot
                Dim c As Vector3 = transformed(cursor + 2) - pivot

                cursor += 3

                Dim color As String = If(String.IsNullOrEmpty(triangle.Color), sampleColor, triangle.Color)

                render.Triangles.Add(New MeshTriangle(
                    New Point3D(CSng(a.x), CSng(a.y), CSng(a.z)),
                    New Point3D(CSng(b.x), CSng(b.y), CSng(b.z)),
                    New Point3D(CSng(c.x), CSng(c.y), CSng(c.z)),
                    color))
            Next

            Call render.RecalculateBounds()

            Dim geometry As New FlaskGeometry With {
                .RenderMesh = render,
                .Collider = collider,
                .Height = TargetHeight,
                .BaseRadius = profile(0),
                .NeckRadius = profile(Layers - 1),
                .PivotOffset = pivot,
                .GripHeight = TargetHeight * 0.55,
                .SourceFile = file
            }

            Console.WriteLine($"[flask] {IO.Path.GetFileName(file)}: {raw.TriangleCount} tri, 原始高 {rawHeight:F4} m → 缩放 {scale:F3}")
            Console.WriteLine($"        瓶高 {geometry.Height:F3} m, 底半径 {geometry.BaseRadius:F4}, 颈半径 {geometry.NeckRadius:F4}, 质心 z {pivot.z:F4}")

            Return geometry
        End Function

    End Module

End Namespace
