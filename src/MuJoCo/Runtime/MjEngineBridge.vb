Imports System.Numerics
Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports Collision3D = Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Joints3D = Microsoft.VisualBasic.Engine.Physics.Joints3D
Imports EngVec = Microsoft.VisualBasic.Engine.Physics.Vector3
Imports EngQuat = Microsoft.VisualBasic.Engine.Physics.Math3D.Quaternion
Imports EngMat3 = Microsoft.VisualBasic.Engine.Physics.Math3D.Matrix3x3

''' <summary>
''' 模型与计算内核之间的桥：把 <see cref="mjModel"/> 的 body 树装配成
''' <c>RigidBody3D</c> + 约束，并在每次推进前后同步 <c>qpos/qvel</c>。
''' </summary>
''' <remarks>
''' <para>
''' 装配顺序不能调换：
''' </para>
''' <list type="number">
''' <item><description>按 <c>qpos0</c> 摆好全部刚体；</description></item>
''' <item><description>再创建关节约束——<c>RevoluteJoint3D</c> 在构造时把当前相对姿态
''' 记为"零位"，如果先建约束再摆姿态，所有关节角度都会带一个常量偏置；</description></item>
''' <item><description>最后写回真实状态。</description></item>
''' </list>
''' <para>
''' 同步方向：
''' </para>
''' <list type="bullet">
''' <item><description>
''' <see cref="SyncToEngine"/>：<c>qpos/qvel</c> → 树的前向运动学 → 刚体的位姿与速度；
''' </description></item>
''' <item><description>
''' <see cref="SyncFromEngine"/>：刚体（求解器产出的真值）→ <c>qpos/qvel</c> 与
''' <c>xpos/xquat</c>，再由 <see cref="MjKinematics.UpdateDerived"/> 补齐派生量。
''' </description></item>
''' </list>
''' </remarks>
Public Class MjEngineBridge

    ''' <summary>位置伺服的 Baumgarte 系数（映射自 <c>o_solimp</c> 的经验值）</summary>
    ''' <remarks>
    ''' 取内核自己的默认值 0.2。Nuogou 的 0.05 会让接触点的法向冲量收敛太慢，
    ''' 脚支撑不足，整机会以匀速"滑"出去。
    ''' </remarks>
    Public Shared Property DefaultBaumgarte As Double = 0.2
    ''' <summary>
    ''' 一个 <c>opt.timestep</c> 内切给内核的物理子步数。
    ''' </summary>
    ''' <remarks>
    ''' 这个数值直接决定接触约束的收敛质量：duck 的脚是 3 mm 厚的合并凸包，
    ''' 挂在五节细长连杆的末端，5 ms 一个子步时接触冲量来不及在 100 次迭代内
    ''' 与 14 个铰链约束达成一致，脚会"跟着躯干一起滑"（μ 抬到 3 也拦不住）。
    ''' </remarks>
    Public Shared Property PhysicsSubsteps As Integer = 8

    ''' <summary>允许的穿透量，米</summary>
    ''' <remarks>
    ''' 脚底只有 3 mm 厚，5 mm 的 slop 会让脚整只陷进地面，摩擦接触点位置失真。
    ''' </remarks>
    Public Shared Property DefaultSlop As Double = 0.001

    Public ReadOnly Property Model As mjModel
    Public ReadOnly Property World As New Eng.PhysicsWorld3D()

    ''' <summary>每个 body 对应的刚体（下标即 body id；worldbody 为 <c>Nothing</c>）</summary>
    ''' <remarks>
    ''' 具体实现见文件末尾的 <c>Bodies</c> 属性；这里只留注释位置，
    ''' 因为属性需要在装配完成之后才有值。
    ''' </remarks>
    Private m_bodies As Eng.RigidBody3D()

    ''' <summary>每子步施加执行器与被动力的钩子</summary>
    Public ReadOnly Property Hook As New MjActuatorHook()

    ''' <summary>世界几何体（地面、墙等静态形状）占用的刚体</summary>
    Public ReadOnly Property StaticGeoms As New List(Of Eng.RigidBody3D)()

    ''' <summary>关节的父体是 worldbody 时使用的静态锚点刚体</summary>
    Private m_worldAnchor As Eng.RigidBody3D

    ''' <summary>拿到真正凸包代理的 body 个数（诊断用）</summary>
    Private m_convexProxies As Integer

    Private ReadOnly m_data As mjData

    Public Sub New(model As mjModel, data As mjData)
        Me.Model = model
        m_data = data

        Call ConfigureWorld()
        Call CreateBodies()
        Call CreateJointConstraints()
        Call CreateActuators()
        Call CreateEqualities()

        Call ($"bridge ready: {Model.nbody} bodies, {Model.nv} dofs, {Model.ngeom} geoms, " &
              $"{Model.nactuator} actuators, {StaticGeoms.Count} static geoms, " &
              $"{m_convexProxies} convex proxies").debug

        Hook.Model = model
        Hook.Data = data
        Hook.Bridge = Me

        Call World.Add(Hook)
    End Sub

    ' /********************************************************************************/
    '  装配
    ' /********************************************************************************/

    Private Sub ConfigureWorld()
        World.Gravity = MjMath.Vec(Model.opt.gravity(0), Model.opt.gravity(1), Model.opt.gravity(2))
        World.Substeps = PhysicsSubsteps
        World.FixedDt = Model.opt.timestep / PhysicsSubsteps
        World.Iterations = Math.Max(4, Model.opt.iterations)
        World.Baumgarte = DefaultBaumgarte
        World.Slop = DefaultSlop
        ' 不要把 MJCF 的 o_solref 直接塞进 ContactTimeConstant：
        ' MuJoCo 的 solref 是"时间常数 0.02 s + 阻尼比 1"，而内核的同名参数是
        ' 软约束的弹簧时间常数 —— 填 0.02 会让接触变成一根很软的弹簧，
        ' 支撑力不够，机器人缓慢下沉（穿透 3.4 → 6.9 mm）并贴着地面匀速滑行
        ' （实测 0.5 m/s，把摩擦抬到 μ=3 也拦不住）。
        ' 内核默认的硬接触（时间常数 0 = 纯 Baumgarte + slop）才与 MuJoCo 的
        ' 默认 solref 在稳定性上等价。
        World.ContactTimeConstant = 0.0
        World.ContactDampingRatio = 1.0
        World.HooksAfterGravity = False
        World.MarginOnlyContacts = False
        ' 不早停：实测残差到不了可用的阈值，早停只会白算一遍残差
        World.SolverTolerance = 0.0

        ' 走路时每个落地瞬间都会瞬间静止，睡眠会冻结推进
        World.AllowSleep = False
        World.WarmStarting = True
    End Sub

    Private Sub CreateBodies()
        Dim nbody As Integer = Model.nbody
        Dim bodies(nbody - 1) As Eng.RigidBody3D

        For i As Integer = 1 To nbody - 1
            Dim mass As Double = Model.body_mass(i)
            Dim shape As Collision3D.Collider3D = MjCollisionModel.BodyCollider(Model, i)

            If shape.Kind = Collision3D.ShapeKind3D.Convex Then
                m_convexProxies += 1
            End If
            Dim friction As Double = FrictionOf(i)
            Dim material As New Eng.PhysicsMaterial(friction, 0.0)

            Dim body As New Eng.RigidBody3D(shape, If(mass > 0, mass, 0.001), material) With {
                .Label = If(Model.body_name(i), $"body{i}"),
                .LinearDamping = 0.0,
                .AngularDamping = 0.0,
                .MaxSpeed = 0.0,
                .MaxAngularSpeed = 0.0,
                .CollisionGroup = Model.body_group(i),
                .CollisionMask = Model.body_mask(i)
            }

            body.InertiaLocal = InertiaOf(i)
            Call body.InvalidateInertiaCache()

            ' 没有任何自由度的 body：要么是静态几何体，要么靠焊接约束跟随父体
            Dim isStaticBody As Boolean = (Model.body_dofnum(i) = 0 AndAlso Model.body_parentid(i) = 0)

            If isStaticBody OrElse mass <= 0 Then
                Call body.SetStatic()
            End If

            Call World.Add(body)
            bodies(i) = body
        Next

        ' 世界几何体（地面、墙、地形）：每个可碰撞 geom 一个静态刚体
        For g As Integer = 0 To Model.ngeom - 1
            If Model.geom_bodyid(g) <> 0 Then
                Continue For
            End If

            If Model.geom_contype(g) = 0 OrElse Model.geom_conaffinity(g) = 0 Then
                Continue For
            End If

            Dim shape As Collision3D.Collider3D = MjCollisionModel.GeomCollider(Model, g)
            Dim material As New Eng.PhysicsMaterial(Model.geom_friction(g * 3), 0.0)
            Dim body As New Eng.RigidBody3D(shape, 1.0, material) With {
                .Label = If(Model.geom_name(g), $"world_geom{g}"),
                .LinearDamping = 0.0,
                .AngularDamping = 0.0,
                .CollisionGroup = MjCollisionModel.GroupEnvironment,
                .CollisionMask = -1
            }

            If CType(Model.geom_type(g), mjtGeom) = mjtGeom.mjGEOM_PLANE Then
                Dim rotation As Quaternion = MjMath.Quat(Model.geom_quat, g * 4)
                body.Orientation = MjMath.ToEngine(MjMath.Normalize(rotation))
            End If

            Call body.SetStatic()
            Call World.Add(body)
            Call StaticGeoms.Add(body)
        Next

        ' 关节锚点落在 worldbody 上时需要一个"世界刚体"提供反作用质量
        Dim anchorShape As Collision3D.Collider3D = New Collision3D.SphereCollider3D(0.0005)
        m_worldAnchor = New Eng.RigidBody3D(anchorShape, 1.0, New Eng.PhysicsMaterial(0.0, 0.0)) With {
            .Label = "world_anchor",
            .CollisionGroup = 0,
            .CollisionMask = 0
        }

        Call m_worldAnchor.SetStatic()
        Call World.Add(m_worldAnchor)

        ' 按 qpos0 摆好姿态（此时 qpos 还是初值）
        Call MjKinematics.RunPosition(Model, m_data)

        For i As Integer = 1 To nbody - 1
            bodies(i).Position = MjMath.Vec(m_data.xipos(i * 3), m_data.xipos(i * 3 + 1), m_data.xipos(i * 3 + 2))
            bodies(i).Orientation = MjMath.ToEngine(MjMath.Quat(m_data.xquat, i * 4))
            bodies(i).Velocity = New EngVec(0, 0, 0)
            bodies(i).AngularVelocity = New EngVec(0, 0, 0)
        Next

        m_bodies = bodies
    End Sub

    ''' <summary>刚体的世界惯量：<c>fullinertia</c> 加上沿关节轴的 armature</summary>
    Private Function InertiaOf(body As Integer) As EngMat3
        Dim tensor As EngMat3 = MjMath.Tensor(Model.body_fullinertia)

        ' fullinertia 在补偿之后可能只有对角元；退化时用 body_inertia 兜底
        If tensor.m11 = 0 AndAlso tensor.m22 = 0 AndAlso tensor.m33 = 0 Then
            tensor = EngMat3.Diagonal(Model.body_inertia(body * 3), Model.body_inertia(body * 3 + 1),
                                      Model.body_inertia(body * 3 + 2))
        End If

        Dim start As Integer = Model.body_jntadr(body)
        Dim count As Integer = Model.body_jntnum(body)

        For k As Integer = 0 To count - 1
            Dim joint As Integer = start + k
            Dim dof As Integer = Model.jnt_dofadr(joint)
            Dim armature As Double = If(dof >= 0, Model.dof_armature(dof), 0.0)

            If armature <= 0 Then
                Continue For
            End If

            Select Case CType(Model.jnt_type(joint), mjtJoint)
                Case mjtJoint.mjJNT_HINGE
                    Dim axis As New Vector3(CSng(Model.jnt_axis(joint * 3)), CSng(Model.jnt_axis(joint * 3 + 1)),
                                            CSng(Model.jnt_axis(joint * 3 + 2)))
                    tensor = MjMath.WithArmature(tensor, axis, armature)

                Case mjtJoint.mjJNT_FREE, mjtJoint.mjJNT_BALL
                    ' 三个转动方向都补上
                    tensor = New EngMat3(tensor.m11 + armature, tensor.m12, tensor.m13,
                                         tensor.m21, tensor.m22 + armature, tensor.m23,
                                         tensor.m31, tensor.m32, tensor.m33 + armature)
            End Select
        Next

        ' 极小的惯量会让求解器数值爆炸：给一个与质量相称的下限
        Dim mass As Double = Model.body_mass(body)
        Dim floor As Double = Math.Max(mass, 1.0E-6) * 1.0E-6

        If tensor.m11 < floor Then tensor.m11 = floor
        If tensor.m22 < floor Then tensor.m22 = floor
        If tensor.m33 < floor Then tensor.m33 = floor

        Return tensor
    End Function

    Private Function FrictionOf(body As Integer) As Double
        Dim start As Integer = Model.body_geomadr(body)
        Dim count As Integer = Model.body_geomnum(body)

        For k As Integer = 0 To count - 1
            Dim g As Integer = start + k

            If MjCollisionModel.IsCollidable(Model, g) Then
                Return Model.geom_friction(g * 3)
            End If
        Next

        Return Model.opt.o_friction(0)
    End Function

    Private Sub CreateJointConstraints()
        Dim constraints(Model.njnt - 1) As Joints3D.IConstraint3D
        Dim revolutes(Model.njnt - 1) As Joints3D.RevoluteJoint3D
        Dim prismatics(Model.njnt - 1) As Joints3D.PrismaticJoint3D
        Dim balls(Model.njnt - 1) As Joints3D.BallJoint3D
        Dim welds(Model.nbody - 1) As MjWeldConstraint

        For j As Integer = 0 To Model.njnt - 1
            Dim body As Integer = Model.jnt_bodyid(j)
            Dim child As Eng.RigidBody3D = m_bodies(body)
            Dim parentId As Integer = Model.body_parentid(body)
            Dim parent As Eng.RigidBody3D = If(parentId >= 1, m_bodies(parentId), m_worldAnchor)

            ' 关节锚点在父体系与子体系里的位置（都相对各自的质心）
            Dim childQuat As Quaternion = MjMath.Quat(Model.body_quat, body * 4)
            Dim jointPos As New Vector3(CSng(Model.jnt_pos(j * 3)), CSng(Model.jnt_pos(j * 3 + 1)),
                                        CSng(Model.jnt_pos(j * 3 + 2)))
            Dim anchorB As Vector3 = jointPos - New Vector3(CSng(Model.body_ipos(body * 3)),
                                                            CSng(Model.body_ipos(body * 3 + 1)),
                                                            CSng(Model.body_ipos(body * 3 + 2)))
            Dim anchorA As Vector3 = MjMath.Rotate(childQuat, jointPos)

            If parentId >= 1 Then
                anchorA += New Vector3(CSng(Model.body_pos(body * 3)), CSng(Model.body_pos(body * 3 + 1)),
                                       CSng(Model.body_pos(body * 3 + 2))) -
                           New Vector3(CSng(Model.body_ipos(parentId * 3)), CSng(Model.body_ipos(parentId * 3 + 1)),
                                       CSng(Model.body_ipos(parentId * 3 + 2)))
            Else
                anchorA += New Vector3(CSng(Model.body_pos(body * 3)), CSng(Model.body_pos(body * 3 + 1)),
                                       CSng(Model.body_pos(body * 3 + 2)))
            End If

            Dim axisLocal As New Vector3(CSng(Model.jnt_axis(j * 3)), CSng(Model.jnt_axis(j * 3 + 1)),
                                         CSng(Model.jnt_axis(j * 3 + 2)))
            Dim axisA As Vector3 = MjMath.SafeNormalize(MjMath.Rotate(childQuat, axisLocal))
            Dim axisB As Vector3 = MjMath.SafeNormalize(axisLocal)

            Dim lower As Double = Model.jnt_range(j * 2)
            Dim upper As Double = Model.jnt_range(j * 2 + 1)

            Select Case CType(Model.jnt_type(j), mjtJoint)
                Case mjtJoint.mjJNT_FREE
                    ' 不加约束的刚体本身就是 6 个自由度

                Case mjtJoint.mjJNT_HINGE
                    Dim hinge As New Joints3D.RevoluteJoint3D(
                        parent, child,
                        MjMath.Vec(anchorA), MjMath.Vec(anchorB),
                        MjMath.Vec(axisA), MjMath.Vec(axisB),
                        lower, upper, Model.jnt_name(j))

                    ' 内核的限位判据用的是它自己的 Position 读数，而那个读数在
                    ' 父子固定旋转非恒等时是错的（见 MeasureJoint），所以这里一律关闭，
                    ' 关节行程改由 MjActuatorHook 用反算出来的坐标做速度级约束
                    hinge.EnableLimit = False
                    hinge.AngularCorrection = 0.0
                    hinge.Damping = Model.dof_damping(Model.jnt_dofadr(j))

                    Call World.Add(hinge)

                    revolutes(j) = hinge
                    constraints(j) = hinge

                Case mjtJoint.mjJNT_SLIDE
                    Dim slide As New Joints3D.PrismaticJoint3D(
                        parent, child,
                        MjMath.Vec(anchorA), MjMath.Vec(anchorB),
                        MjMath.Vec(axisA), lower, upper, Model.jnt_name(j))

                    slide.EnableLimit = False

                    Call World.Add(slide)

                    prismatics(j) = slide
                    constraints(j) = slide

                Case mjtJoint.mjJNT_BALL
                    Dim ball As New Joints3D.BallJoint3D(parent, child, MjMath.Vec(anchorA), MjMath.Vec(anchorB))

                    Call World.Add(ball)

                    balls(j) = ball
                    constraints(j) = ball
            End Select
        Next

        ' 没有关节、但挂在别的 body 下面的 body：焊接
        For i As Integer = 1 To Model.nbody - 1
            If Model.body_jntnum(i) > 0 OrElse Model.body_parentid(i) <= 0 Then
                Continue For
            End If

            Dim parentId As Integer = Model.body_parentid(i)
            Dim parent As Eng.RigidBody3D = If(parentId >= 1, m_bodies(parentId), m_worldAnchor)
            Dim bodyQuat As Quaternion = MjMath.Quat(Model.body_quat, i * 4)
            Dim anchorB As Vector3 = -New Vector3(CSng(Model.body_ipos(i * 3)), CSng(Model.body_ipos(i * 3 + 1)),
                                                  CSng(Model.body_ipos(i * 3 + 2)))
            Dim anchorA As Vector3 = New Vector3(CSng(Model.body_pos(i * 3)), CSng(Model.body_pos(i * 3 + 1)),
                                                 CSng(Model.body_pos(i * 3 + 2))) +
                                      MjMath.Rotate(bodyQuat, anchorB)

            If parentId >= 1 Then
                anchorA -= New Vector3(CSng(Model.body_ipos(parentId * 3)), CSng(Model.body_ipos(parentId * 3 + 1)),
                                       CSng(Model.body_ipos(parentId * 3 + 2)))
            End If

            Dim weld As New MjWeldConstraint(parent, m_bodies(i), MjMath.Vec(anchorA), MjMath.Vec(anchorB)) With {
                .Label = Model.body_name(i)
            }

            Call World.Add(weld)
            welds(i) = weld
        Next

        m_jointConstraints = constraints
        m_revolutes = revolutes
        m_prismatics = prismatics
        m_balls = balls
        m_welds = welds
    End Sub

    Private m_jointConstraints As Joints3D.IConstraint3D()
    Private m_revolutes As Joints3D.RevoluteJoint3D()
    Private m_prismatics As Joints3D.PrismaticJoint3D()
    Private m_balls As Joints3D.BallJoint3D()
    Private m_welds As MjWeldConstraint()

    Public ReadOnly Property JointConstraints As Joints3D.IConstraint3D()
        Get
            Return m_jointConstraints
        End Get
    End Property

    Public ReadOnly Property Revolutes As Joints3D.RevoluteJoint3D()
        Get
            Return m_revolutes
        End Get
    End Property

    Public ReadOnly Property Prismatics As Joints3D.PrismaticJoint3D()
        Get
            Return m_prismatics
        End Get
    End Property

    Public ReadOnly Property Balls As Joints3D.BallJoint3D()
        Get
            Return m_balls
        End Get
    End Property

    Public ReadOnly Property Welds As MjWeldConstraint()
        Get
            Return m_welds
        End Get
    End Property

    Public ReadOnly Property Bodies As Eng.RigidBody3D()
        Get
            Return m_bodies
        End Get
    End Property

    Private Sub CreateActuators()
        Dim bindings(Model.nactuator - 1) As MjActuatorBinding

        For a As Integer = 0 To Model.nactuator - 1
            Dim binding As New MjActuatorBinding With {
                .Kind = mjtActuator.mjACTUATOR_GENERAL,
                .TrnType = CType(Model.actuator_trntype(a), mjtTrn),
                .Gain = Model.actuator_gainprm(a * mjNGAIN + 0),
                .BiasVelocity = Model.actuator_biasprm(a * mjNBIAS + 2),
                .ForceMin = Model.actuator_forcerange(a * 2 + 0),
                .ForceMax = Model.actuator_forcerange(a * 2 + 1)
            }

            If Not Model.actuator_forcelimited(a) Then
                binding.ForceMin = -1.0E+10
                binding.ForceMax = 1.0E+10
            End If

            If binding.TrnType <> mjtTrn.mjTRN_JOINT AndAlso binding.TrnType <> mjtTrn.mjTRN_JOINTINPARENT Then
                bindings(a) = binding
                Continue For
            End If

            Dim joint As Integer = Model.actuator_trnid(a * 2 + 0)
            binding.Joint = joint

            If joint < 0 OrElse joint >= Model.njnt Then
                bindings(a) = binding
                Continue For
            End If

            binding.Dof = Model.jnt_dofadr(joint)
            binding.Linear = CType(Model.jnt_type(joint), mjtJoint) = mjtJoint.mjJNT_SLIDE

            ' 语义类型从 gain/bias 的组合还原（编译器写的三种确定组合）
            Dim gainType As Integer = Model.actuator_gaintype(a)
            Dim biasType As Integer = Model.actuator_biastype(a)
            Dim kind As mjtActuator

            If biasType = CInt(mjtBias.mjBIAS_AFFINE) AndAlso Model.actuator_biasprm(a * mjNBIAS + 1) <> 0 Then
                kind = mjtActuator.mjACTUATOR_POSITION
            ElseIf biasType = CInt(mjtBias.mjBIAS_AFFINE) Then
                kind = mjtActuator.mjACTUATOR_VELOCITY
            Else
                kind = mjtActuator.mjACTUATOR_MOTOR
            End If

            binding.Kind = kind

            Dim body As Integer = Model.jnt_bodyid(joint)
            Dim parentId As Integer = Model.body_parentid(body)
            Dim child As Eng.RigidBody3D = m_bodies(body)
            Dim parent As Eng.RigidBody3D = If(parentId >= 1, m_bodies(parentId), m_worldAnchor)

            If binding.Linear Then
                ' 平移关节的伺服沿关节轴施加线性冲量
                Select Case kind
                    Case mjtActuator.mjACTUATOR_POSITION
                        Dim servo As New MjPositionActuatorConstraint With {
                            .A = parent, .B = child, .Linear = True,
                            .Kp = Model.actuator_gainprm(a * mjNGAIN + 0),
                            .Kv = -Model.actuator_biasprm(a * mjNBIAS + 2),
                            .ForceMin = binding.ForceMin, .ForceMax = binding.ForceMax
                        }

                        Call World.Add(servo)
                        binding.Position = servo

                    Case Else
                        Dim servo2 As New MjVelocityActuatorConstraint With {
                            .A = parent, .B = child, .Linear = True,
                            .ForceMin = binding.ForceMin, .ForceMax = binding.ForceMax
                        }

                        Call World.Add(servo2)
                        binding.Velocity = servo2
                End Select
            Else
                Select Case kind
                    Case mjtActuator.mjACTUATOR_POSITION
                        Dim servo As New MjPositionActuatorConstraint With {
                            .A = parent, .B = child,
                            .Kp = Model.actuator_gainprm(a * mjNGAIN + 0),
                            .Kv = -Model.actuator_biasprm(a * mjNBIAS + 2),
                            .ForceMin = binding.ForceMin, .ForceMax = binding.ForceMax
                        }

                        Call World.Add(servo)
                        binding.Position = servo

                    Case mjtActuator.mjACTUATOR_VELOCITY, mjtActuator.mjACTUATOR_DAMPER
                        Dim servo2 As New MjVelocityActuatorConstraint With {
                            .A = parent, .B = child,
                            .ForceMin = binding.ForceMin, .ForceMax = binding.ForceMax
                        }

                        Call World.Add(servo2)
                        binding.Velocity = servo2
                End Select
            End If

            bindings(a) = binding
        Next

        m_actuatorBindings = bindings
    End Sub

    Private m_actuatorBindings As MjActuatorBinding()

    Public ReadOnly Property ActuatorBindings As MjActuatorBinding()
        Get
            Return m_actuatorBindings
        End Get
    End Property

    Private Sub CreateEqualities()
        For e As Integer = 0 To Model.neq - 1
            If Not m_data.eq_active(e) AndAlso Model.eq_active0(e) Then
                m_data.eq_active(e) = True
            End If

            Dim body1 As Integer = If(CType(Model.eq_objtype(e), mjtObj) = mjtObj.mjOBJ_BODY,
                                      Model.eq_obj1id(e), -1)
            Dim body2 As Integer = If(CType(Model.eq_objtype(e), mjtObj) = mjtObj.mjOBJ_BODY,
                                      Model.eq_obj2id(e), -1)

            If body1 < 0 Then
                Continue For
            End If

            Dim a As Eng.RigidBody3D = If(body1 >= 1, m_bodies(body1), m_worldAnchor)
            Dim b As Eng.RigidBody3D = If(body2 >= 1, m_bodies(body2), m_worldAnchor)

            Dim anchor1 As New Vector3(CSng(Model.eq_data(e * mjNEQDATA + 0)),
                                       CSng(Model.eq_data(e * mjNEQDATA + 1)),
                                       CSng(Model.eq_data(e * mjNEQDATA + 2)))
            Dim anchor2 As New Vector3(CSng(Model.eq_data(e * mjNEQDATA + 4)),
                                       CSng(Model.eq_data(e * mjNEQDATA + 5)),
                                       CSng(Model.eq_data(e * mjNEQDATA + 6)))

            Select Case CType(Model.eq_type(e), mjtEq)
                Case mjtEq.mjEQ_CONNECT
                    Dim connect As New Joints3D.BallJoint3D(a, b, MjMath.Vec(anchor1), MjMath.Vec(anchor2))

                    Call World.Add(connect)

                Case mjtEq.mjEQ_WELD
                    Dim weld As New MjWeldConstraint(a, b, MjMath.Vec(anchor1), MjMath.Vec(anchor2))

                    Call World.Add(weld)

                Case Else
                    Dim row As New MjRowConstraint With {
                        .A = a, .B = b,
                        .AnchorA = MjMath.Vec(anchor1), .AnchorB = MjMath.Vec(anchor2)
                    }

                    Call World.Add(row)
            End Select
        Next
    End Sub

    ' /********************************************************************************/
    '  同步
    ' /********************************************************************************/

    ''' <summary>
    ''' <c>qpos/qvel</c> → 引擎：先跑一遍前向运动学，再把位姿与速度写进刚体。
    ''' </summary>
    Public Sub SyncToEngine()
        Call MjKinematics.RunPosition(Model, m_data)
        Call MjKinematics.RunVelocity(Model, m_data)

        For i As Integer = 1 To Model.nbody - 1
            Dim body As Eng.RigidBody3D = m_bodies(i)

            If body Is Nothing OrElse body.IsStatic Then
                Continue For
            End If

            ' mu -> 内核：内核的 Position 是质心，MuJoCo 的 xipos 也是质心
            body.Position = MjMath.Vec(m_data.xipos(i * 3), m_data.xipos(i * 3 + 1), m_data.xipos(i * 3 + 2))
            body.Orientation = MjMath.ToEngine(MjMath.Quat(m_data.xquat, i * 4))

            body.Velocity = MjMath.Vec(m_data.cvel(i * 6 + 3), m_data.cvel(i * 6 + 4), m_data.cvel(i * 6 + 5))
            body.AngularVelocity = MjMath.Vec(m_data.cvel(i * 6 + 0), m_data.cvel(i * 6 + 1), m_data.cvel(i * 6 + 2))

            Call body.Wake()
        Next
    End Sub

    ''' <summary>
    ''' 引擎 → <c>qpos/qvel</c>：求解器产出的刚体位姿是真值，关节坐标由它反解。
    ''' </summary>
    Public Sub SyncFromEngine()
        Dim qpos As Double() = m_data.qpos
        Dim qvel As Double() = m_data.qvel

        For j As Integer = 0 To Model.njnt - 1
            Dim body As Integer = Model.jnt_bodyid(j)
            Dim adr As Integer = Model.jnt_qposadr(j)
            Dim dof As Integer = Model.jnt_dofadr(j)
            Dim rb As Eng.RigidBody3D = m_bodies(body)

            If rb Is Nothing Then
                Continue For
            End If

            Select Case CType(Model.jnt_type(j), mjtJoint)
                Case mjtJoint.mjJNT_HINGE, mjtJoint.mjJNT_SLIDE
                    ' 从刚体姿态反算，不用内核的 Position/Speed（见 MeasureJoint 的说明）
                    Dim axisWorld As EngVec = Nothing
                    Dim length As Double = 0.0
                    Dim speed As Double = 0.0

                    If Not MeasureJoint(j, axisWorld, length, speed) Then
                        Continue For
                    End If

                    qpos(adr) = length
                    qvel(dof) = speed

                Case mjtJoint.mjJNT_BALL
                    Dim parentId As Integer = Model.body_parentid(body)
                    ' 必须用刚体的真值姿态。m_data.xquat 此刻还是**上一帧**的缓存，
                    ' 用它会和下一步 SyncToEngine 的正变换互相拉扯，见 FREE 分支的说明。
                    Dim parentQuat As Quaternion = If(parentId >= 1,
                                                      MjMath.QuatOf(m_bodies(parentId).Orientation),
                                                      Quaternion.Identity)
                    Dim childQuat As Quaternion = MjMath.QuatOf(rb.Orientation)
                    Dim reference As Quaternion = MjMath.Mul(parentQuat, MjMath.Quat(Model.body_quat, body * 4))
                    Dim relative As Quaternion = MjMath.Normalize(MjMath.Mul(MjMath.Conj(reference), childQuat))

                    Call MjMath.WriteQuat(qpos, adr, relative)

                    Dim angular As Vector3 = New Vector3(CSng(rb.AngularVelocity.x), CSng(rb.AngularVelocity.y),
                                                         CSng(rb.AngularVelocity.z))

                    If parentId >= 1 Then
                        Dim parentBody As Eng.RigidBody3D = m_bodies(parentId)
                        angular -= New Vector3(CSng(parentBody.AngularVelocity.x), CSng(parentBody.AngularVelocity.y),
                                               CSng(parentBody.AngularVelocity.z))
                    End If

                    Dim local As Vector3 = MjMath.Unrotate(childQuat, angular)

                    qvel(dof + 0) = local.X
                    qvel(dof + 1) = local.Y
                    qvel(dof + 2) = local.Z

                Case mjtJoint.mjJNT_FREE
                    Dim ipos As Vector3 = New Vector3(CSng(Model.body_ipos(body * 3)),
                                                      CSng(Model.body_ipos(body * 3 + 1)),
                                                      CSng(Model.body_ipos(body * 3 + 2)))
                    ' 用刚体的真值姿态，不能用 m_data.xquat：
                    ' xquat 要到本方法末尾才刷新，此刻还是上一帧的值。拿旧姿态反算
                    ' origin = com − R_旧·ipos，下一步 SyncToEngine 又用 R_新 正算回
                    ' Position，每步就把刚体挪动 (R_新 − R_旧)·ipos。duck 的 trunk 质心
                    ' 偏移 2.26 cm，躯干微转就会累积成持续漂移 —— 表现为"斜着飘走"。
                    Dim rotation As Quaternion = MjMath.QuatOf(rb.Orientation)
                    Dim com As Vector3 = New Vector3(CSng(rb.Position.x), CSng(rb.Position.y), CSng(rb.Position.z))
                    Dim origin As Vector3 = com - MjMath.Rotate(rotation, ipos)

                    qpos(adr + 0) = origin.X
                    qpos(adr + 1) = origin.Y
                    qpos(adr + 2) = origin.Z

                    Call MjMath.WriteQuat(qpos, adr + 3, rotation)

                    Dim angular2 As Vector3 = New Vector3(CSng(rb.AngularVelocity.x), CSng(rb.AngularVelocity.y),
                                                          CSng(rb.AngularVelocity.z))
                    Dim linear As Vector3 = New Vector3(CSng(rb.Velocity.x), CSng(rb.Velocity.y),
                                                        CSng(rb.Velocity.z))
                    Dim originVelocity As Vector3 = linear - Vector3.Cross(angular2, MjMath.Rotate(rotation, ipos))
                    Dim local2 As Vector3 = MjMath.Unrotate(rotation, angular2)

                    qvel(dof + 0) = originVelocity.X
                    qvel(dof + 1) = originVelocity.Y
                    qvel(dof + 2) = originVelocity.Z
                    qvel(dof + 3) = local2.X
                    qvel(dof + 4) = local2.Y
                    qvel(dof + 5) = local2.Z
            End Select
        Next

        ' 位姿直接用刚体的（求解器真值），再补齐派生量
        For i As Integer = 1 To Model.nbody - 1
            Dim rb As Eng.RigidBody3D = m_bodies(i)

            If rb Is Nothing Then
                Continue For
            End If

            Dim quat As Quaternion = MjMath.QuatOf(rb.Orientation)
            Dim ipos As New Vector3(CSng(Model.body_ipos(i * 3)), CSng(Model.body_ipos(i * 3 + 1)),
                                    CSng(Model.body_ipos(i * 3 + 2)))
            Dim origin As Vector3 = MjMath.Vec(rb.Position) - MjMath.Rotate(quat, ipos)

            m_data.xpos(i * 3 + 0) = origin.X
            m_data.xpos(i * 3 + 1) = origin.Y
            m_data.xpos(i * 3 + 2) = origin.Z

            Call MjMath.WriteQuat(m_data.xquat, i * 4, quat)
        Next

        Call MjKinematics.UpdateDerived(Model, m_data)
    End Sub

    ' /********************************************************************************/
    '  关节访问
    ' /********************************************************************************/

    ''' <summary>读取一个关节的当前坐标、速度与作用轴（世界系）；不支持的关节返回 <c>False</c></summary>
    ''' <remarks>
    ''' <para>
    ''' <b>不</b>使用内核的 <c>RevoluteJoint3D.Position</c> / <c>Speed</c>。
    ''' 内核的 <c>ComputeAngle</c> 把"绕 <c>hingeAxisA</c>（**父**体系）的分量"
    ''' 与"增量四元数（**子**体系表达）"直接点乘，两个量不在同一坐标系里：
    ''' 当子体相对父体带固定旋转（duck 的每个 body 都是 ±90°）时，点乘结果被
    ''' <c>cos(两轴夹角)</c> 缩放，读数于是要么恒为 0、要么整体反号。
    ''' 实测 <c>left_hip_roll</c> 恒为 0、<c>left_knee</c> 反号 —— 策略看到的关节角
    ''' 因此全错，输出恒定动作，机器人朝一个方向匀速跑出场地。
    ''' </para>
    ''' <para>
    ''' 这里改为从父子刚体的世界姿态反算，正好是
    ''' <see cref="MjKinematics.RunPosition"/> 正变换的逆：
    ''' <c>Q = conj(parentQuat · body_quat) · childQuat</c>，
    ''' 关节角 = <c>2·atan2(u·xyz(Q), w(Q))</c>，<c>u</c> 是子体系里的关节轴。
    ''' </para>
    ''' </remarks>
    Public Function JointState(joint As Integer, ByRef axis As EngVec, ByRef length As Double,
                               ByRef speed As Double) As Boolean
        Return MeasureJoint(joint, axis, length, speed)
    End Function

    ''' <summary>从刚体姿态与角速度反算关节坐标</summary>
    Private Function MeasureJoint(joint As Integer, ByRef axis As EngVec, ByRef length As Double,
                                  ByRef speed As Double) As Boolean
        If joint < 0 OrElse joint >= Model.njnt Then
            Return False
        End If

        Dim kind As mjtJoint = CType(Model.jnt_type(joint), mjtJoint)

        If kind <> mjtJoint.mjJNT_HINGE AndAlso kind <> mjtJoint.mjJNT_SLIDE Then
            Return False
        End If

        Dim body As Integer = Model.jnt_bodyid(joint)
        Dim child As Eng.RigidBody3D = m_bodies(body)

        If child Is Nothing Then
            Return False
        End If

        Dim parentId As Integer = Model.body_parentid(body)
        Dim parent As Eng.RigidBody3D = If(parentId >= 1, m_bodies(parentId), m_worldAnchor)
        Dim parentQuat As Quaternion = If(parentId >= 1, MjMath.QuatOf(parent.Orientation), Quaternion.Identity)
        Dim childQuat As Quaternion = MjMath.QuatOf(child.Orientation)
        Dim localAxis As New Vector3(CSng(Model.jnt_axis(joint * 3)), CSng(Model.jnt_axis(joint * 3 + 1)),
                                     CSng(Model.jnt_axis(joint * 3 + 2)))
        Dim axisWorld As Vector3 = MjMath.SafeNormalize(MjMath.Rotate(childQuat, localAxis))

        axis = MjMath.Vec(axisWorld)

        Dim childAngular As Vector3 = MjMath.Vec(child.AngularVelocity)
        Dim parentAngular As Vector3 = MjMath.Vec(parent.AngularVelocity)

        If kind = mjtJoint.mjJNT_HINGE Then
            Dim reference As Quaternion = MjMath.Mul(parentQuat, MjMath.Quat(Model.body_quat, body * 4))
            Dim delta As Quaternion = MjMath.Mul(MjMath.Conj(reference), childQuat)

            If delta.W < 0 Then
                delta = New Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W)
            End If

            Dim sine As Double = delta.X * localAxis.X + delta.Y * localAxis.Y + delta.Z * localAxis.Z

            length = 2.0 * Math.Atan2(sine, delta.W)
            speed = Vector3.Dot(childAngular - parentAngular, axisWorld)
        Else
            Dim bodyPos As New Vector3(CSng(Model.body_pos(body * 3)), CSng(Model.body_pos(body * 3 + 1)),
                                       CSng(Model.body_pos(body * 3 + 2)))
            Dim ipos As New Vector3(CSng(Model.body_ipos(body * 3)), CSng(Model.body_ipos(body * 3 + 1)),
                                    CSng(Model.body_ipos(body * 3 + 2)))
            Dim reference As Vector3 = MjMath.Vec(parent.Position) + MjMath.Rotate(parentQuat, bodyPos)
            Dim origin As Vector3 = MjMath.Vec(child.Position) - MjMath.Rotate(childQuat, ipos)

            length = Vector3.Dot(origin - reference, axisWorld)
            speed = Vector3.Dot(MjMath.Vec(child.Velocity) - MjMath.Vec(parent.Velocity), axisWorld)
        End If

        Return True
    End Function

    ''' <summary>沿关节轴施加一个力矩（铰链）或力（滑动）</summary>
    Public Sub ApplyJointForce(joint As Integer, value As Double)
        Dim axis As EngVec
        Dim length As Double
        Dim speed As Double

        If Not JointState(joint, axis, length, speed) Then
            Return
        End If

        Select Case CType(Model.jnt_type(joint), mjtJoint)
            Case mjtJoint.mjJNT_HINGE
                Dim hinge As Joints3D.RevoluteJoint3D = m_revolutes(joint)
                Call hinge.ApplyControlForce(value)

            Case mjtJoint.mjJNT_SLIDE
                Dim body As Integer = Model.jnt_bodyid(joint)
                Dim parentId As Integer = Model.body_parentid(body)
                Dim child As Eng.RigidBody3D = m_bodies(body)
                Dim parent As Eng.RigidBody3D = If(parentId >= 1, m_bodies(parentId), m_worldAnchor)
                Dim impulse As EngVec = axis * CSng(value)

                Call child.Wake()
                Call parent.Wake()

                Call child.ApplyForce(impulse)
                Call parent.ApplyForce(impulse * -1.0)
        End Select
    End Sub

    ''' <summary>把 <c>opt.iterations</c> 等运行时选项同步到世界</summary>
    Public Sub ApplyOptions()
        World.Iterations = Math.Max(4, Model.opt.iterations)
        World.Gravity = MjMath.Vec(Model.opt.gravity(0), Model.opt.gravity(1), Model.opt.gravity(2))

        Dim fixedDt As Double = Model.opt.timestep / PhysicsSubsteps

        If World.FixedDt <> fixedDt Then
            World.FixedDt = fixedDt
        End If
    End Sub

    ''' <summary>瞬移之后清掉暖启动缓存，否则继承的支撑冲量会把整机弹飞</summary>
    Public Sub InvalidateContacts()
        Call World.ClearContactCache()
        Call World.ClearVelocities()
    End Sub

End Class
