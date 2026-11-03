Imports System.IO
Imports System.Numerics
Imports MuJoCo
Imports Geometry = MuJoCo.Geometry

Namespace Core

    ''' <summary>
    ''' 场景的构建参数：时间步、场地几何、可选地形与小球。
    ''' </summary>
    Public Class MjSceneOptions

        ''' <summary>物理子步长，秒</summary>
        Public Property Timestep As Double = SimConstants.Timestep
        ''' <summary>求解器迭代次数</summary>
        Public Property Iterations As Integer = 24
        ''' <summary>是否加入四面矮墙</summary>
        Public Property IncludeWalls As Boolean = True
        ''' <summary>是否加入余弦矮丘（地形原型）</summary>
        Public Property ReliefEnabled As Boolean = False
        ''' <summary>是否加入可踢的小球</summary>
        Public Property IncludeBall As Boolean = True
        ''' <summary>场地摩擦</summary>
        Public Property EnvironmentFriction As Double = 0.7

        Public Function Clone() As MjSceneOptions
            Return New MjSceneOptions With {
                .Timestep = Timestep,
                .Iterations = Iterations,
                .IncludeWalls = IncludeWalls,
                .ReliefEnabled = ReliefEnabled,
                .IncludeBall = IncludeBall,
                .EnvironmentFriction = EnvironmentFriction
            }
        End Function
    End Class

    ''' <summary>
    ''' 把 duck 的 MJCF 与场地、小球拼成一个完整的 <see cref="mjModel"/>。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 这正是 MuJoCo 的工作流：模型树在运行时就能编辑，编译一次得到只读的模型。
    ''' 场地与小球都用 <see cref="MjSpec"/> 的建模 API 加进去，不需要额外的 XML 文件。
    ''' </para>
    ''' <para>
    ''' 碰撞分组：机器人 = 1、环境 = 2、道具 = 4。机器人只与环境/道具碰撞，
    ''' 因此默认不启用自碰撞（与 duck 的 MJCF 一致，它只打开了颌/头的自碰撞，
    ''' 而计算内核是 body 级碰撞代理，做不了这种细粒度）。
    ''' </para>
    ''' </remarks>
    Public Module MjSceneBuilder

        ''' <summary>机器人自身的碰撞分组</summary>
        Public Const GroupRobot As Integer = 1
        ''' <summary>场地（地面、墙、地形）的碰撞分组</summary>
        Public Const GroupEnvironment As Integer = 2
        ''' <summary>可交互道具（球）的碰撞分组</summary>
        Public Const GroupProp As Integer = 4

        Public Function Build(mjcfPath As String, meshDirectory As String, options As MjSceneOptions) As mjModel
            If Not File.Exists(mjcfPath) Then
                Throw New MjException($"the duck model '{mjcfPath}' does not exist")
            End If

            Dim spec As MjSpec = MjcfParser.Parse(mjcfPath)

            ' MJCF 里写的是 meshdir="assets"，实际资产在 meshes 目录
            spec.MeshResolver = Function(file As String) Path.Combine(meshDirectory, file)
            spec.Opt.timestep = options.Timestep
            spec.Opt.iterations = options.Iterations

            Call AddGround(spec, options)

            If options.IncludeWalls Then
                Call AddWalls(spec, options)
            End If

            If options.ReliefEnabled Then
                Call AddRelief(spec, options)
            End If

            If options.IncludeBall Then
                Call AddBall(spec, options)
            End If

            Return MjApi.mj_compile(spec)
        End Function

        ' /********************************************************************************/
        '  地面与墙
        ' /********************************************************************************/

        Private Sub AddGround(spec As MjSpec, options As MjSceneOptions)
            Dim ground As MjsGeom = spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE,
                                                 New Vector3(CSng(SimConstants.ArenaHalf),
                                                             CSng(SimConstants.ArenaHalf), 0.0F),
                                                 Vector3.Zero, Nothing, Nothing, "ground")

            Call SetFriction(ground, options.EnvironmentFriction)
        End Sub

        Private Sub AddWalls(spec As MjSpec, options As MjSceneOptions)
            Dim half As Double = SimConstants.ArenaHalf
            Dim height As Double = SimConstants.ArenaWallHeight
            Dim thickness As Double = SimConstants.ArenaWallThickness
            Dim span As Double = 2 * half + 2 * thickness
            Dim center As Double = half + thickness / 2
            Dim z As Double = height / 2

            Call AddWall(spec, options, "wall_south", span / 2, thickness / 2, height / 2, 0, center, z)
            Call AddWall(spec, options, "wall_north", span / 2, thickness / 2, height / 2, 0, -center, z)
            Call AddWall(spec, options, "wall_east", thickness / 2, span / 2, height / 2, center, 0, z)
            Call AddWall(spec, options, "wall_west", thickness / 2, span / 2, height / 2, -center, 0, z)
        End Sub

        Private Sub AddWall(spec As MjSpec, options As MjSceneOptions, name As String,
                            hx As Double, hy As Double, hz As Double,
                            x As Double, y As Double, z As Double)
            ' 没有关节、父体是世界：编译出来就是一个静态 body
            Dim body As MjsBody = spec.AddBody(spec.World, name, New Vector3(CSng(x), CSng(y), CSng(z)))
            Dim geom As MjsGeom = spec.AddGeom(body, mjtGeom.mjGEOM_BOX,
                                               New Vector3(CSng(hx), CSng(hy), CSng(hz)),
                                               Vector3.Zero, Nothing, Nothing, name & "_geom")

            Call SetFriction(geom, options.EnvironmentFriction)
        End Sub

        Private Sub SetFriction(geom As MjsGeom, friction As Double)
            geom.Friction = {friction, 0.005, 0.0001}
        End Sub

        ' /********************************************************************************/
        '  地形矮丘
        ' /********************************************************************************/

        Private Sub AddRelief(spec As MjSpec, options As MjSceneOptions)
            Dim index As Integer = 0

            For Each bump As Double() In SimConstants.ReliefBumps
                Dim meshName As String = $"relief_{index}"
                Dim mesh As MjsMesh = BuildBumpMesh(meshName, bump)

                If mesh Is Nothing Then
                    Continue For
                End If

                Call spec.Meshes.Add(mesh)

                Dim body As MjsBody = spec.AddBody(spec.World, meshName,
                                                   New Vector3(CSng(bump(0)), CSng(bump(1)),
                                                               CSng(-SimConstants.ReliefSink)))
                Dim geom As MjsGeom = spec.AddGeom(body, mjtGeom.mjGEOM_MESH, Nothing, Vector3.Zero,
                                                   Nothing, meshName, meshName & "_geom")

                Call SetFriction(geom, options.EnvironmentFriction)

                index += 1
            Next
        End Sub

        ''' <summary>
        ''' 一个余弦矮丘的凸包：解析曲面在同心圆上采样，取样本的凸包
        ''' </summary>
        Private Function BuildBumpMesh(name As String, bump As Double()) As MjsMesh
            Dim radius As Double = bump(3)
            Dim height As Double = bump(2)
            Dim points As New List(Of Vector3)()

            For ring As Integer = 0 To 4
                Dim r As Double = radius * ring / 4.0

                For tick As Integer = 0 To 15
                    Dim angle As Double = 2 * Math.PI * tick / 16
                    Dim x As Double = r * Math.Cos(angle)
                    Dim y As Double = r * Math.Sin(angle)
                    Dim wave As Double = 0.5 * (1 + Math.Cos(Math.PI * r / radius))

                    Call points.Add(New Vector3(CSng(x), CSng(y), CSng(height * wave)))
                Next
            Next

            Dim hull As Geometry.ConvexHullResult = Geometry.ConvexHull3D.Compute(points.ToArray())

            If hull Is Nothing OrElse hull.IsEmpty Then
                Return Nothing
            End If

            Dim vertices As New List(Of Double)(hull.Vertices.Length * 3)
            Dim faces As New List(Of Integer)(hull.Faces.Length * 3)

            For Each p In hull.Vertices
                Call vertices.Add(p.X)
                Call vertices.Add(p.Y)
                Call vertices.Add(p.Z)
            Next

            For Each f In hull.Faces
                Call faces.Add(f(0))
                Call faces.Add(f(1))
                Call faces.Add(f(2))
            Next

            Return New MjsMesh With {
                .Name = name,
                .Vertices = vertices.ToArray(),
                .Faces = faces.ToArray()
            }
        End Function

        ' /********************************************************************************/
        '  小球
        ' /********************************************************************************/

        Private Sub AddBall(spec As MjSpec, options As MjSceneOptions)
            Dim radius As Double = SimConstants.BallRadius
            Dim mass As Double = SimConstants.BallMass
            Dim inertia As Double = 0.4 * mass * radius * radius

            Dim body As MjsBody = spec.AddBody(spec.World, "ball",
                                               New Vector3(CSng(SimConstants.BallParkX),
                                                           CSng(SimConstants.BallParkY),
                                                           CSng(SimConstants.BallParkZ)))

            body.HasInertial = True
            body.Mass = mass
            body.FullInertia = {inertia, inertia, inertia, 0.0, 0.0, 0.0}
            body.CollisionGroup = GroupProp
            body.CollisionMask = GroupRobot Or GroupEnvironment Or GroupProp

            Call spec.AddFreeJoint(body, "ball_free")

            Dim geom As MjsGeom = spec.AddGeom(body, mjtGeom.mjGEOM_SPHERE,
                                               New Vector3(CSng(radius), 0.0F, 0.0F),
                                               Vector3.Zero, Nothing, Nothing, "ball_geom")

            Call SetFriction(geom, SimConstants.BallFriction)
        End Sub

    End Module
End Namespace
