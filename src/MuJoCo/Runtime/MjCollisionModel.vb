Imports System.Numerics
Imports Collision3D = Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports EngVec = Microsoft.VisualBasic.Engine.Physics.Vector3
Imports Geometry = MuJoCo.Geometry

''' <summary>
''' 把 MJCF 的 geom 翻译成计算内核的碰撞形状。
''' </summary>
''' <remarks>
''' <para>
''' 这是整个移植里唯一无法做到与原生 MuJoCo 等价的一层，取舍如下：
''' </para>
''' <list type="bullet">
''' <item><description>
''' 内核是"一个刚体一个形状"，MuJoCo 是"一个 body 多个 geom"。因此每个
''' body 上所有参与碰撞的 geom 会被**合并成一个凸包**，作为这个 body 的代理。
''' 合并会把凹处填平（例如两条腿之间的空隙），也会让细长零件变"胖"一点点。
''' </description></item>
''' <item><description>
''' <c>mjGEOM_PLANE</c> 单独生成一个无限的 <c>PlaneCollider3D</c>，不参与合并。
''' </description></item>
''' <item><description>
''' <c>mjGEOM_MESH</c> 本来就是凸包参与碰撞（MuJoCo 也要求凸网格），
''' 所以点云 → <see cref="Geometry.ConvexHull3D"/> 的结果与原版一致到浮点误差。
''' </description></item>
''' <item><description>
''' 因此 <see cref="mjData.ncon"/> 是"body 对级"的接触数，
''' 不能与原生 MuJoCo 的 geom 对级接触逐个对照。
''' </description></item>
''' </list>
''' </remarks>
Public Module MjCollisionModel

    ''' <summary>机器人自身的碰撞分组</summary>
    Public Const GroupRobot As Integer = 1
    ''' <summary>场地（地面、墙、地形）的碰撞分组</summary>
    Public Const GroupEnvironment As Integer = 2
    ''' <summary>可交互道具（球）的碰撞分组</summary>
    Public Const GroupProp As Integer = 4

    ' /********************************************************************************/
    '  geom → 点云
    ' /********************************************************************************/

    ''' <summary>
    ''' 取得一个 geom 的点云，坐标在 **geom 自己的局部系** 里。
    ''' </summary>
    ''' <remarks>
    ''' 图元被离散成点云而不是直接构造对应的 collider，原因是 geom 有
    ''' <c>pos</c>/<c>quat</c> 偏移而内核的 collider 只能以刚体原点为中心；
    ''' 统一走点云 → 凸包之后，偏移和旋转都能一次性消化掉。
    ''' </remarks>
    Public Function GeomPoints(model As mjModel, g As Integer) As Vector3()
        Dim size As Double() = model.geom_size
        Dim s0 As Double = size(g * 3 + 0)
        Dim s1 As Double = size(g * 3 + 1)
        Dim s2 As Double = size(g * 3 + 2)

        Select Case CType(model.geom_type(g), mjtGeom)
            Case mjtGeom.mjGEOM_SPHERE
                Return SpherePoints(s0, 12, 8)

            Case mjtGeom.mjGEOM_BOX
                Return BoxPoints(s0, s1, s2)

            Case mjtGeom.mjGEOM_CAPSULE
                Return CapsulePoints(s0, s1, 12, 6)

            Case mjtGeom.mjGEOM_CYLINDER
                Return CylinderPoints(s0, s1, 12)

            Case mjtGeom.mjGEOM_ELLIPSOID
                Return EllipsoidPoints(s0, s1, s2, 12, 8)

            Case mjtGeom.mjGEOM_MESH
                Return MeshPoints(model, g)

            Case Else
                Return New Vector3() {}
        End Select
    End Function

    Private Function MeshPoints(model As mjModel, g As Integer) As Vector3()
        Dim mesh As Integer = model.geom_dataid(g)

        If mesh < 0 Then
            Return New Vector3() {}
        End If

        Dim scale As Double = If(model.geom_size(g * 3) > 0, model.geom_size(g * 3), 1.0)
        Dim adr As Integer = model.mesh_vertadr(mesh)
        Dim count As Integer = model.mesh_vertnum(mesh)

        If count <= 0 Then
            Return New Vector3() {}
        End If

        Dim points(count - 1) As Vector3

        For v As Integer = 0 To count - 1
            points(v) = New Vector3(CSng(model.mesh_vert((adr + v) * 3) * scale),
                                    CSng(model.mesh_vert((adr + v) * 3 + 1) * scale),
                                    CSng(model.mesh_vert((adr + v) * 3 + 2) * scale))
        Next

        Return points
    End Function

    Public Function SpherePoints(radius As Double, Optional segmentsU As Integer = 12,
                                 Optional segmentsV As Integer = 8) As Vector3()
        Dim u As Integer = Math.Max(4, segmentsU)
        Dim v As Integer = Math.Max(3, segmentsV)
        Dim points As New List(Of Vector3)(u * v)

        For iv As Integer = 1 To v - 1
            Dim phi As Double = Math.PI * iv / v
            Dim ring As Double = Math.Sin(phi)

            For iu As Integer = 0 To u - 1
                Dim theta As Double = 2.0 * Math.PI * iu / u

                Call points.Add(New Vector3(CSng(radius * ring * Math.Cos(theta)),
                                            CSng(radius * ring * Math.Sin(theta)),
                                            CSng(radius * Math.Cos(phi))))
            Next
        Next

        Call points.Add(New Vector3(0, 0, CSng(radius)))
        Call points.Add(New Vector3(0, 0, CSng(-radius)))

        Return points.ToArray()
    End Function

    Private Function EllipsoidPoints(a As Double, b As Double, c As Double,
                                     segmentsU As Integer, segmentsV As Integer) As Vector3()
        Dim source As Vector3() = SpherePoints(1.0, segmentsU, segmentsV)
        Dim points(source.Length - 1) As Vector3

        For i As Integer = 0 To source.Length - 1
            points(i) = New Vector3(CSng(source(i).X * a), CSng(source(i).Y * b), CSng(source(i).Z * c))
        Next

        Return points
    End Function

    Private Function BoxPoints(hx As Double, hy As Double, hz As Double) As Vector3()
        Dim points(7) As Vector3
        Dim index As Integer = 0

        For Each sx As Integer In New Integer() {-1, 1}
            For Each sy As Integer In New Integer() {-1, 1}
                For Each sz As Integer In New Integer() {-1, 1}
                    points(index) = New Vector3(CSng(sx * hx), CSng(sy * hy), CSng(sz * hz))
                    index += 1
                Next
            Next
        Next

        Return points
    End Function

    Private Function CylinderPoints(radius As Double, halfLength As Double, segments As Integer) As Vector3()
        Dim u As Integer = Math.Max(4, segments)
        Dim points As New List(Of Vector3)(2 * u)

        For iu As Integer = 0 To u - 1
            Dim theta As Double = 2.0 * Math.PI * iu / u
            Dim x As Single = CSng(radius * Math.Cos(theta))
            Dim y As Single = CSng(radius * Math.Sin(theta))

            Call points.Add(New Vector3(x, y, CSng(halfLength)))
            Call points.Add(New Vector3(x, y, CSng(-halfLength)))
        Next

        Return points.ToArray()
    End Function

    Private Function CapsulePoints(radius As Double, halfLength As Double,
                                   segments As Integer, caps As Integer) As Vector3()
        Dim points As New List(Of Vector3)(256)
        Dim u As Integer = Math.Max(4, segments)
        Dim c As Integer = Math.Max(2, caps)

        ' 圆柱段
        For iu As Integer = 0 To u - 1
            Dim theta As Double = 2.0 * Math.PI * iu / u
            Dim x As Single = CSng(radius * Math.Cos(theta))
            Dim y As Single = CSng(radius * Math.Sin(theta))

            Call points.Add(New Vector3(x, y, CSng(halfLength)))
            Call points.Add(New Vector3(x, y, CSng(-halfLength)))
        Next

        ' 两端的半球
        For ic As Integer = 1 To c
            Dim phi As Double = 0.5 * Math.PI * ic / (c + 1)
            Dim ring As Double = Math.Sin(phi)
            Dim offset As Double = Math.Cos(phi)

            For iu As Integer = 0 To u - 1
                Dim theta As Double = 2.0 * Math.PI * iu / u
                Dim x As Double = radius * ring * Math.Cos(theta)
                Dim y As Double = radius * ring * Math.Sin(theta)

                Call points.Add(New Vector3(CSng(x), CSng(y), CSng(halfLength + radius * offset)))
                Call points.Add(New Vector3(CSng(x), CSng(y), CSng(-halfLength - radius * offset)))
            Next
        Next

        Call points.Add(New Vector3(0, 0, CSng(halfLength + radius)))
        Call points.Add(New Vector3(0, 0, CSng(-halfLength - radius)))

        Return points.ToArray()
    End Function

    ' /********************************************************************************/
    '  body → 凸包
    ' /********************************************************************************/

    ''' <summary>这个 geom 是否参与碰撞（<c>contype</c> 与 <c>conaffinity</c> 都非零）</summary>
    Public Function IsCollidable(model As mjModel, g As Integer) As Boolean
        If model.geom_contype(g) = 0 OrElse model.geom_conaffinity(g) = 0 Then
            Return False
        End If

        ' contype = conaffinity = 2 是"只做自碰撞"的组：内核不启用 self collision，
        ' 这些 geom 只在视觉上有意义，参与合并会把脚印撑大
        If model.geom_contype(g) = 2 AndAlso model.geom_conaffinity(g) = 2 Then
            Return False
        End If

        Return CType(model.geom_type(g), mjtGeom) <> mjtGeom.mjGEOM_PLANE
    End Function

    ''' <summary>
    ''' 收集一个 body 上全部可碰撞 geom 的点，变换到 **以质心为原点** 的 body 系。
    ''' </summary>
    Public Function BodyPoints(model As mjModel, body As Integer) As Vector3()
        Dim points As New List(Of Vector3)(1024)
        Dim ipos As New Vector3(CSng(model.body_ipos(body * 3)), CSng(model.body_ipos(body * 3 + 1)),
                                CSng(model.body_ipos(body * 3 + 2)))
        Dim start As Integer = model.body_geomadr(body)
        Dim count As Integer = model.body_geomnum(body)

        For k As Integer = 0 To count - 1
            Dim g As Integer = start + k

            If Not IsCollidable(model, g) Then
                Continue For
            End If

            Dim local As Vector3() = GeomPoints(model, g)

            If local.Length < 4 Then
                Continue For
            End If

            Dim position As New Vector3(CSng(model.geom_pos(g * 3)), CSng(model.geom_pos(g * 3 + 1)),
                                        CSng(model.geom_pos(g * 3 + 2)))
            Dim rotation As Quaternion = MjMath.Quat(model.geom_quat, g * 4)

            For Each p In local
                Call points.Add(Vector3.Transform(p, rotation) + position - ipos)
            Next
        Next

        Return points.ToArray()
    End Function

    ''' <summary>
    ''' 把一个 body 的全部碰撞 geom 合并成一个凸包 collider；没有可碰撞 geom 时返回
    ''' 一个与任何东西都不相交的极小包围球。
    ''' </summary>
    Public Function BodyCollider(model As mjModel, body As Integer) As Collision3D.Collider3D
        Dim points As Vector3() = BodyPoints(model, body)

        If points.Length < 4 Then
            Return New Collision3D.SphereCollider3D(0.0005)
        End If

        Dim hull As Geometry.ConvexHullResult = Geometry.ConvexHull3D.Compute(points)

        If hull Is Nothing OrElse hull.IsEmpty Then
            ' 所有点在一条线上或一个面上：退化成一个薄盒，保证仍有碰撞体积
            Dim bound As Vector3 = Bounds(points)

            Return New Collision3D.BoxCollider3D(Math.Max(bound.X, 0.001),
                                                 Math.Max(bound.Y, 0.001),
                                                 Math.Max(bound.Z, 0.001))
        End If

        Return New Collision3D.ConvexPolyhedronCollider3D(BuildMesh(hull))
    End Function

    ''' <summary>凸包结果 → 内核的 <c>ConvexMesh3D</c></summary>
    Public Function BuildMesh(hull As Geometry.ConvexHullResult) As Collision3D.ConvexMesh3D
        Dim vertices(hull.Vertices.Length - 1) As EngVec

        For i As Integer = 0 To hull.Vertices.Length - 1
            Dim p As Vector3 = hull.Vertices(i)
            vertices(i) = New EngVec(p.X, p.Y, p.Z)
        Next

        Dim faces As Integer()() = New Integer(hull.Faces.Length - 1)() {}

        For i As Integer = 0 To hull.Faces.Length - 1
            faces(i) = hull.Faces(i)
        Next

        Return New Collision3D.ConvexMesh3D(vertices, faces)
    End Function

    ''' <summary>点云的轴对齐包围盒尺寸</summary>
    Public Function Bounds(points As Vector3()) As Vector3
        If points Is Nothing OrElse points.Length = 0 Then
            Return New Vector3(0.001F, 0.001F, 0.001F)
        End If

        Dim min As Vector3 = points(0)
        Dim max As Vector3 = points(0)

        For Each p In points
            min = Vector3.Min(min, p)
            max = Vector3.Max(max, p)
        Next

        Return max - min
    End Function

    ''' <summary>
    ''' 一个 geom 单独构成的 collider（世界几何体用），点已经在 body 系里。
    ''' </summary>
    Public Function GeomCollider(model As mjModel, g As Integer) As Collision3D.Collider3D
        If CType(model.geom_type(g), mjtGeom) = mjtGeom.mjGEOM_PLANE Then
            ' 平面的 z 轴是法向，size[0]/size[1] 是可视化尺寸，动力学上是无限平面
            Dim rotation As Quaternion = MjMath.Quat(model.geom_quat, g * 4)
            Dim normal As Vector3 = Vector3.Transform(Vector3.UnitZ, rotation)
            Dim position As New Vector3(CSng(model.geom_pos(g * 3)), CSng(model.geom_pos(g * 3 + 1)),
                                        CSng(model.geom_pos(g * 3 + 2)))
            Dim distance As Double = Vector3.Dot(normal, position)

            Return New Collision3D.PlaneCollider3D(New EngVec(normal.X, normal.Y, normal.Z), distance)
        End If

        Dim local As Vector3() = GeomPoints(model, g)

        If local.Length < 4 Then
            Return New Collision3D.SphereCollider3D(0.0005)
        End If

        Dim position2 As New Vector3(CSng(model.geom_pos(g * 3)), CSng(model.geom_pos(g * 3 + 1)),
                                     CSng(model.geom_pos(g * 3 + 2)))
        Dim rotation2 As Quaternion = MjMath.Quat(model.geom_quat, g * 4)
        Dim points(local.Length - 1) As Vector3

        For i As Integer = 0 To local.Length - 1
            points(i) = Vector3.Transform(local(i), rotation2) + position2
        Next

        Dim hull As Geometry.ConvexHullResult = Geometry.ConvexHull3D.Compute(points)

        If hull Is Nothing OrElse hull.IsEmpty Then
            Return New Collision3D.SphereCollider3D(0.0005)
        End If

        Return New Collision3D.ConvexPolyhedronCollider3D(BuildMesh(hull))
    End Function

End Module
