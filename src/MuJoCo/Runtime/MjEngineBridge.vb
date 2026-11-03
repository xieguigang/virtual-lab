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
    Public Shared Property DefaultBaumgarte As Double = 0.05
    ''' <summary>允许的穿透量，米</summary>
    Public Shared Property DefaultSlop As Double = 0.005

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
        World.FixedDt = Model.opt.timestep
        World.Substeps = 1
        World.Iterations = Math.Max(4, Model.opt.iterations)
        World.Baumgarte = DefaultBaumgarte
        World.Slop = DefaultSlop
        World.ContactTimeConstant = Model.opt.o_solref(0)
        World.ContactDampingRatio = Model.opt.o_solref(1)
        World.HooksAfterGravity = False
        World.MarginOnlyContacts = False
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

                    hinge.EnableLimit = Model.jnt_limited(j)
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

                    slide.EnableLimit = Model.jnt_limited(j)

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
                Case mjtJoint.mjJNT_HINGE
                    Dim hinge As Joints3D.RevoluteJoint3D = m_revolutes(j)

                    If hinge Is Nothing Then
                        Continue For
                    End If

                    qpos(adr) = hinge.Position
                    qvel(dof) = hinge.Speed

                Case mjtJoint.mjJNT_SLIDE
                    Dim slide As Joints3D.PrismaticJoint3D = m_prismatics(j)

                    If slide Is Nothing Then
                        Continue For
                    End If

                    qpos(adr) = slide.Position
                    qvel(dof) = slide.Speed

                Case mjtJoint.mjJNT_BALL
                    Dim parentId As Integer = Model.body_parentid(body)
                    Dim parentQuat As Quaternion = If(parentId >= 1,
                                                      MjMath.Quat(m_data.xquat, parentId * 4),
                                                      Quaternion.Identity)
                    Dim childQuat As Quaternion = MjMath.Quat(m_data.xquat, body * 4)
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
                    Dim rotation As Quaternion = MjMath.Quat(m_data.xquat, body * 4)
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

    ''' <summary>读取一个关节的当前坐标、速度与作用轴；不支持的关节返回 <c>False</c></summary>
    Public Function JointState(joint As Integer, ByRef axis As EngVec, ByRef length As Double,
                               ByRef speed As Double) As Boolean
        If joint < 0 OrElse joint >= Model.njnt Then
            Return False
        End If

        Select Case CType(Model.jnt_type(joint), mjtJoint)
            Case mjtJoint.mjJNT_HINGE
                Dim hinge As Joints3D.RevoluteJoint3D = m_revolutes(joint)

                If hinge Is Nothing Then
                    Return False
                End If

                axis = hinge.WorldAxis
                length = hinge.Position
                speed = hinge.Speed

                Return True

            Case mjtJoint.mjJNT_SLIDE
                Dim slide As Joints3D.PrismaticJoint3D = m_prismatics(joint)

                If slide Is Nothing Then
                    Return False
                End If

                axis = slide.WorldAxis
                length = slide.Position
                speed = slide.Speed

                Return True

            Case Else
                Return False
        End Select
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

        If World.FixedDt <> Model.opt.timestep Then
            World.FixedDt = Model.opt.timestep
        End If
    End Sub

    ''' <summary>瞬移之后清掉暖启动缓存，否则继承的支撑冲量会把整机弹飞</summary>
    Public Sub InvalidateContacts()
        Call World.ClearContactCache()
        Call World.ClearVelocities()
    End Sub

End Class
