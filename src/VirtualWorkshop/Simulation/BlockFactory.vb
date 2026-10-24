Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model

Namespace Simulation

    ''' <summary>可生成的积木形状。</summary>
    Public Enum BlockShape
        Box = 0
        Sphere = 1
        Cylinder = 2
        Cone = 3
        Frustum = 4
        Hemisphere = 5
        Prism = 6
        Pyramid = 7
        Disc = 8
        Wedge = 9
    End Enum

    ''' <summary>积木规格：形状 + 尺寸 + 质量 + 摩擦 + 颜色。</summary>
    ''' <remarks>
    ''' 尺寸数组按形状解释：
    '''
    ''' * Box / Wedge：<c>[width, height, depth]</c>
    ''' * Sphere / Hemisphere / Disc：<c>[diameter]</c>（Disc 第二项是厚度）
    ''' * Cylinder / Cone：<c>[diameter, height]</c>
    ''' * Frustum：<c>[bottomDiameter, topDiameter, height]</c>
    ''' * Prism / Pyramid：<c>[sides, diameter, height]</c>
    ''' </remarks>
    Public Class BlockSpec

        Public Property Shape As BlockShape = BlockShape.Box
        Public Property Size As Double() = {0.04, 0.04, 0.04}
        Public Property Mass As Double = 0.3
        Public Property Friction As Double = 0.75
        Public Property Color As String = "#F59E0B"

    End Class

    ''' <summary>一个已实例化的积木：刚体 + 凸网格 + 渲染三角面。</summary>
    Public Class BlockInstance

        Public Property Id As String
        Public Property Spec As BlockSpec
        Public Property Body As RigidBody3D

        ''' <summary>物理与渲染共用的凸网格（球体也用它做渲染）。</summary>
        Public Property Mesh As ConvexMesh3D

        Public Property Render As LinkMesh

        ''' <summary>局部包围盒尺寸。</summary>
        Public Property Size As Vector3

    End Class

    ''' <summary>
    ''' 程序化积木工厂：圆锥、球、长方体、正方体、半球、圆台、圆柱、棱柱、棱锥等。
    ''' </summary>
    ''' <remarks>
    ''' 每块积木同时生成
    '''
    ''' 1. 物理碰撞体（球体用解析 <see cref="SphereCollider3D"/> 以保证滚动平滑，
    '''    其它形状用 <see cref="ConvexPolyhedronCollider3D"/>），
    ''' 2. 渲染三角面（由同一份凸网格扇形三角化，保证"看到的"就是"碰到的"）。
    ''' </remarks>
    Public Module BlockFactory

        ''' <summary>环境物体（积木、桌面）的碰撞分组。</summary>
        Public Const GroupEnvironment As Integer = 2
        ''' <summary>环境物体允许与"机械臂(1)"和"环境(2)"碰撞。</summary>
        Public Const MaskEnvironment As Integer = 3

        Public Function ParseShape(text As String) As BlockShape
            If String.IsNullOrEmpty(text) Then Return BlockShape.Box

            Select Case text.Trim().ToLowerInvariant()
                Case "box", "cube", "正方体", "方块" : Return BlockShape.Box
                Case "sphere", "ball", "球", "球体" : Return BlockShape.Sphere
                Case "cylinder", "圆柱" : Return BlockShape.Cylinder
                Case "cone", "圆锥" : Return BlockShape.Cone
                Case "frustum", "圆台" : Return BlockShape.Frustum
                Case "hemisphere", "半球" : Return BlockShape.Hemisphere
                Case "prism", "棱柱" : Return BlockShape.Prism
                Case "pyramid", "棱锥" : Return BlockShape.Pyramid
                Case "disc", "disk", "圆盘" : Return BlockShape.Disc
                Case "wedge", "ramp", "斜坡", "斜面" : Return BlockShape.Wedge
                Case Else : Return BlockShape.Box
            End Select
        End Function

        Public Function ShapeName(shape As BlockShape) As String
            Select Case shape
                Case BlockShape.Sphere : Return "sphere"
                Case BlockShape.Cylinder : Return "cylinder"
                Case BlockShape.Cone : Return "cone"
                Case BlockShape.Frustum : Return "frustum"
                Case BlockShape.Hemisphere : Return "hemisphere"
                Case BlockShape.Prism : Return "prism"
                Case BlockShape.Pyramid : Return "pyramid"
                Case BlockShape.Disc : Return "disc"
                Case BlockShape.Wedge : Return "wedge"
                Case Else : Return "box"
            End Select
        End Function

        ''' <summary>把局部 +Y 轴转到世界 +Z 轴（Z-up 世界里的"竖直放置"）。</summary>
        Public Function UprightRotation() As Quaternion
            Return Quaternion.FromAxisAngle(New Vector3(1, 0, 0), System.Math.PI / 2.0)
        End Function

        ''' <summary>该形状是否需要竖直放置（局部轴不是竖直轴）。</summary>
        Public Function NeedsUpright(shape As BlockShape) As Boolean
            Select Case shape
                Case BlockShape.Cylinder, BlockShape.Cone, BlockShape.Frustum,
                     BlockShape.Hemisphere, BlockShape.Prism, BlockShape.Pyramid, BlockShape.Disc
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        ''' <summary>创建一块积木（<paramref name="position"/> 是质心的世界位置）。</summary>
        Public Function Create(id As String, spec As BlockSpec, position As Vector3,
                               rotation As Quaternion) As BlockInstance

            If spec Is Nothing Then Throw New ArgumentNullException(NameOf(spec))

            Dim dims As Double() = NormalizeSize(spec)
            Dim mesh As ConvexMesh3D
            Dim collider As Collider3D

            Select Case spec.Shape
                Case BlockShape.Sphere
                    Dim radius As Double = dims(0) * 0.5

                    mesh = ConvexMesh3D.CreateSphereMesh(radius, 18, 12)
                    collider = New SphereCollider3D(radius)

                Case BlockShape.Cylinder
                    Dim radius As Double = dims(0) * 0.5

                    mesh = ConvexMesh3D.CreateCylinder(radius, dims(1), 18)
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case BlockShape.Disc
                    Dim radius As Double = dims(0) * 0.5

                    mesh = ConvexPrimitives3D.CreateDisc(radius, dims(1), 22)
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case BlockShape.Cone
                    Dim radius As Double = dims(0) * 0.5

                    mesh = ConvexMesh3D.CreateCone(radius, dims(1), 18)
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case BlockShape.Frustum
                    Dim bottom As Double = dims(0) * 0.5
                    Dim top As Double = dims(1) * 0.5

                    mesh = ConvexPrimitives3D.CreateFrustum(bottom, top, dims(2), 18)
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case BlockShape.Hemisphere
                    Dim radius As Double = dims(0) * 0.5

                    mesh = ConvexPrimitives3D.CreateHemisphere(radius, 18, 8)
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case BlockShape.Prism
                    mesh = ConvexPrimitives3D.CreatePrism(CInt(System.Math.Max(3, dims(0))), dims(1) * 0.5, dims(2))
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case BlockShape.Pyramid
                    mesh = ConvexPrimitives3D.CreatePyramid(CInt(System.Math.Max(3, dims(0))), dims(1) * 0.5, dims(2))
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case BlockShape.Wedge
                    mesh = ConvexMesh3D.CreateWedge(dims(0), dims(1), dims(2))
                    collider = New ConvexPolyhedronCollider3D(mesh)

                Case Else
                    mesh = ConvexMesh3D.CreateBox(dims(0), dims(1), dims(2))
                    collider = New BoxCollider3D(dims(0), dims(1), dims(2))
            End Select

            Dim material As New PhysicsMaterial(spec.Friction, 0.05)
            Dim body As New RigidBody3D(collider, System.Math.Max(0.01, spec.Mass), material) With {
                .Position = position,
                .Orientation = rotation.Normalize(),
                .Label = id,
                .CollisionGroup = GroupEnvironment,
                .CollisionMask = MaskEnvironment,
                .AngularDamping = 0.25,
                .MaxAngularSpeed = 40.0
            }

            Dim bounds = MeshBounds(mesh)
            Dim size As Vector3 = New Vector3(
                System.Math.Max(0.001, bounds.Max.x - bounds.Min.x),
                System.Math.Max(0.001, bounds.Max.y - bounds.Min.y),
                System.Math.Max(0.001, bounds.Max.z - bounds.Min.z))

            Return New BlockInstance With {
                .Id = id,
                .Spec = spec,
                .Body = body,
                .Mesh = mesh,
                .Render = MeshTessellator.ToLinkMesh(mesh, spec.Color, id),
                .Size = size
            }
        End Function

        ''' <summary>把一个规格竖直放在 <paramref name="surfaceZ"/> 上，返回质心位置。</summary>
        Public Function RestingPosition(spec As BlockSpec, x As Double, y As Double,
                                        surfaceZ As Double, yaw As Double) As Vector3

            Dim dims As Double() = NormalizeSize(spec)
            Dim rotation As Quaternion = If(NeedsUpright(spec.Shape),
                                            Quaternion.Compose(Quaternion.FromAxisAngle(New Vector3(0, 0, 1), yaw),
                                                               UprightRotation()),
                                            Quaternion.FromAxisAngle(New Vector3(0, 0, 1), yaw))

            Dim probe As BlockInstance = Create("probe", spec, Vector3.Zero, rotation)
            Dim lowest As Double = 0.0
            Dim first As Boolean = True

            For Each vertex As Vector3 In probe.Mesh.Vertices
                Dim rotated As Vector3 = rotation.Rotate(vertex)

                If first OrElse rotated.z < lowest Then
                    lowest = rotated.z
                    first = False
                End If
            Next

            Return New Vector3(x, y, surfaceZ - lowest + 0.001)
        End Function

        ''' <summary>默认演示场景：一组不同形状的积木，摆在机械臂前方。</summary>
        Public Function DefaultScene() As List(Of BlockSpec)
            Dim list As New List(Of BlockSpec)()
            Dim palette As String() = {"#F59E0B", "#22D3EE", "#34D399", "#F472B6", "#A78BFA", "#FBBF24", "#60A5FA"}

            ' 夹爪张开时内间距约 36 mm，所以默认积木的横向尺寸控制在 30~34 mm，
            ' 否则手指在下降阶段就会压到积木、把它顶开
            list.Add(New BlockSpec With {.Shape = BlockShape.Box, .Size = {0.032, 0.032, 0.032}, .Mass = 0.12, .Color = palette(0)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Box, .Size = {0.030, 0.024, 0.030}, .Mass = 0.08, .Color = palette(1)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Box, .Size = {0.026, 0.045, 0.026}, .Mass = 0.1, .Color = palette(2)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Box, .Size = {0.034, 0.018, 0.034}, .Mass = 0.12, .Color = palette(3)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Sphere, .Size = {0.032}, .Mass = 0.06, .Color = palette(4)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Cylinder, .Size = {0.030, 0.040}, .Mass = 0.09, .Color = palette(5)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Cone, .Size = {0.032, 0.040}, .Mass = 0.07, .Color = palette(6)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Frustum, .Size = {0.034, 0.020, 0.036}, .Mass = 0.08, .Color = palette(0)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Hemisphere, .Size = {0.034}, .Mass = 0.07, .Color = palette(1)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Prism, .Size = {6, 0.032, 0.036}, .Mass = 0.09, .Color = palette(2)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Pyramid, .Size = {4, 0.034, 0.038}, .Mass = 0.07, .Color = palette(3)})
            list.Add(New BlockSpec With {.Shape = BlockShape.Disc, .Size = {0.034, 0.012}, .Mass = 0.04, .Color = palette(4)})

            Return list
        End Function

        ''' <summary>凸网格的局部包围盒。</summary>
        Public Function MeshBounds(mesh As ConvexMesh3D) As (Min As Vector3, Max As Vector3)
            If mesh Is Nothing OrElse mesh.Vertices Is Nothing OrElse mesh.Vertices.Length = 0 Then
                Return (Vector3.Zero, Vector3.Zero)
            End If

            Dim minX As Double = Double.MaxValue
            Dim minY As Double = Double.MaxValue
            Dim minZ As Double = Double.MaxValue
            Dim maxX As Double = Double.MinValue
            Dim maxY As Double = Double.MinValue
            Dim maxZ As Double = Double.MinValue

            For Each v As Vector3 In mesh.Vertices
                minX = System.Math.Min(minX, v.x)
                minY = System.Math.Min(minY, v.y)
                minZ = System.Math.Min(minZ, v.z)
                maxX = System.Math.Max(maxX, v.x)
                maxY = System.Math.Max(maxY, v.y)
                maxZ = System.Math.Max(maxZ, v.z)
            Next

            Return (New Vector3(minX, minY, minZ), New Vector3(maxX, maxY, maxZ))
        End Function

        Private Function NormalizeSize(spec As BlockSpec) As Double()
            Dim size As Double() = spec.Size

            If size Is Nothing OrElse size.Length = 0 Then Return New Double() {0.04, 0.04, 0.04}

            Dim out(2) As Double

            For i As Integer = 0 To 2
                out(i) = If(i < size.Length, System.Math.Max(0.001, size(i)), out(System.Math.Max(0, i - 1)))
            Next

            Return out
        End Function

    End Module

End Namespace
