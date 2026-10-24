Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.Joints3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model

Namespace Simulation

    ''' <summary>仿真内核的可调参数。</summary>
    Public Class ArmSimulationOptions

        Public Property Gravity As Double = 9.81
        Public Property EnableGravity As Boolean = True
        Public Property StepsPerSecond As Double = 120.0
        ''' <summary>
        ''' 每个固定步内的子步数。子步长越小，速度环增益的稳定上限越高
        ''' （<c>Kd &lt; 2·I/dt</c>），六轴臂的重力前馈刚度也就越足。
        ''' </summary>
        Public Property Substeps As Integer = 8
        Public Property Iterations As Integer = 20

        ''' <summary>
        ''' 位置伺服的位置环增益（1/s）。
        ''' </summary>
        ''' <remarks>
        ''' 位置环 × 速度环的乘积就是等效的力矩刚度。六轴臂在零位时 j2 需要
        ''' 约 15 N·m 才能撑住前臂。速度环增益被"自身惯量 / 子步长"的稳定性条件卡住
        ''' （见 <c>EstimateJointInertia</c>），所以刚度只能靠提高位置环增益来补：
        ''' <c>200 × 5.5 ≈ 1100 N·m/rad</c> 把 j2 的静差压到 0.013 rad（≈0.8°）。
        ''' </remarks>
        Public Property ServoStiffness As Double = 200.0
        ''' <summary>位置伺服的阻尼（速度环增益，N·m/(rad/s)）。</summary>
        Public Property ServoDamping As Double = 25.0
        ''' <summary>关节速度上限的缩放（相对 URDF 的 velocity）。</summary>
        Public Property VelocityScale As Double = 0.8

        ''' <summary>
        ''' 折算到连杆上的电机/减速器转子惯量（kg·m²），加到每个连杆的主惯量上。
        ''' </summary>
        ''' <remarks>
        ''' URDF 给的只是连杆本身的惯量：腕部三个轴只有 1e-4 量级，在 120 Hz 的仿真步长下
        ''' 伺服带宽被稳定性条件卡得很低，快速运动会把末端"甩"出接近 1 rad 的漂移。
        ''' 真实机械臂的谐波减速器（1:50~1:100）会把电机转子惯量折算放大到连杆侧，
        ''' 所以这里加一个球形转子惯量，既符合硬件事实，也让腕部在物理上站得住。
        ''' </remarks>
        Public Property MotorInertia As Double = 0.002

        ''' <summary>
        ''' 机械臂的驱动方式：True = 运动学（连杆是 kinematic 刚体，轨迹精确、可重复），
        ''' False = 动力学（连杆是动态刚体，由 JointServo3D + RevoluteJoint3D 驱动）。
        ''' </summary>
        ''' <remarks>
        ''' 默认使用运动学：<see cref="PhysicsWorld3D"/> 的铰链约束是"锚点 + 轴对齐"的逐轴冲量实现，
        ''' 当锚点偏离质心时（六轴臂的每个关节都如此），约束冲量会产生绕轴的角冲量，
        ''' 表现为明显的数值阻尼——实测 j5 在 6.9 N·m 下完全转不动、稳定在两轴偏差 0.14 rad。
        ''' 运动学模式下连杆仍作为**静态碰撞体**参与物理，积木会被真实地推、夹、压，
        ''' 而六轴轨迹严格等于指令（对神经网络训练反而更理想）。
        ''' </remarks>
        Public Property KinematicArm As Boolean = True

        ''' <summary>夹爪最大夹持力（N）。</summary>
        Public Property GripperMaxForce As Double = 60.0
        ''' <summary>夹爪手指质量（kg）。</summary>
        Public Property FingerMass As Double = 0.05

        ''' <summary>启动时是否生成默认积木。</summary>
        Public Property SpawnDefaultBlocks As Boolean = True
        ''' <summary>默认积木数量上限。</summary>
        Public Property DefaultBlockCount As Integer = 12

    End Class

    ''' <summary>
    ''' 六轴机械臂的物理仿真内核：刚体链 + 转轴关节 + 速度/力矩伺服 + 夹爪 + 积木。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 完全不依赖 WinForms：GUI 的定时器与 headless 模式共用同一份内核。
    ''' 世界坐标 Z-up，重力 <c>(0, 0, -g)</c>，基座底面在 z = 0。
    ''' </para>
    ''' <para>
    ''' 碰撞分组：机械臂连杆（含手指、基座）是 group 1 / mask 2，只与环境碰撞；
    ''' 积木与地面是 group 2 / mask 3。这样相邻连杆之间不会互相顶开，
    ''' 但连杆可以真实地推动、夹持积木。
    ''' </para>
    ''' </remarks>
    Public Class ArmSimulation

        Public ReadOnly Property Assembly As ArmAssembly
        Public ReadOnly Property Options As ArmSimulationOptions

        Private _world As PhysicsWorld3D

        ''' <summary>当前的物理世界（Reset 后会换成新实例）。</summary>
        Public ReadOnly Property World As PhysicsWorld3D
            Get
                Return _world
            End Get
        End Property

        ''' <summary>link 名 → 刚体。</summary>
        Public ReadOnly Property LinkBodies As New Dictionary(Of String, RigidBody3D)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>6 个转轴关节。</summary>
        Public ReadOnly Property RevoluteJoints As New List(Of RevoluteJoint3D)()

        ''' <summary>6 个关节伺服。</summary>
        Public ReadOnly Property JointServos As New List(Of JointServo3D)()

        Public ReadOnly Property JointDefs As New List(Of ArmJointDef)()

        ''' <summary>每个关节的有效惯量估算（调试用）。</summary>
        Public ReadOnly Property JointInertias As New List(Of Double)()

        ''' <summary>两根手指的刚体（顺序与 <see cref="FingerNames"/> 一致）。</summary>
        Public ReadOnly Property FingerBodies As New List(Of RigidBody3D)()

        ''' <summary>手指的移动副。</summary>
        Public ReadOnly Property FingerJoints As New List(Of PrismaticJoint3D)()

        ''' <summary>手指伺服（力模式）。</summary>
        Public ReadOnly Property FingerServos As New List(Of JointServo3D)()

        ''' <summary>手指的渲染网格（顶点已相对各自质心）。</summary>
        Public ReadOnly Property FingerMeshes As New List(Of LinkMesh)()

        Public ReadOnly Property FingerNames As New List(Of String)()

        ''' <summary>张开状态下的两指内间距（米）。</summary>
        Public Property FingerBaseGap As Double = 0.036

        ''' <summary>场景中的积木。</summary>
        Public ReadOnly Property Objects As New List(Of BlockInstance)()

        Public Property Time As Double
        Public Property StepCount As Long

        ''' <summary>急停：伺服全部失能，机械臂自由落体。</summary>
        Public Property Estop As Boolean
        ''' <summary>伺服使能。</summary>
        Public Property Enabled As Boolean = True

        ''' <summary>最近一次 Step 时检测到的接触对（已去重）。</summary>
        Public ReadOnly Property LastCollisions As New List(Of String)()

        ''' <summary>
        ''' 最近一次子步里"插进地面"的机械臂部件（形如 <c>link3|ground</c>）。
        ''' </summary>
        ''' <remarks>
        ''' 运动学与动力学两种模式下机械臂都是静态刚体（kinematic 驱动），
        ''' 而物理引擎不做静态-静态碰撞，所以地面穿透必须自己算：取连杆碰撞盒的 8 个角点，
        ''' 只要有角点低于 <see cref="GroundZ"/> 就算穿地。
        ''' </remarks>
        Public ReadOnly Property GroundContacts As New List(Of String)()

        ''' <summary>地面高度（世界 Z），机械臂基座底面就在这个高度。</summary>
        Public Property GroundZ As Double = 0.0

        ''' <summary>连杆允许的最低离地间隙（米），小于它就判定为撞地。</summary>
        Public Property GroundClearance As Double = 0.002

        ''' <summary>
        ''' 手指允许的最低离地间隙（米）。
        ''' </summary>
        ''' <remarks>夹爪要能夹起躺在地面上的积木，所以手指允许贴到地面。</remarks>
        Public Property FingerClearance As Double = 0.0

        Private _jointQ As Double()
        Private _jointQd As Double()
        Private _previousQ As Double()
        Private _fingerExtension As Double
        Private _fingerTarget As Double
        Private _gripForce As Double

        ''' <summary>手指压到物体后允许再挤入的最大深度（米，两侧合计）。</summary>
        ''' <remarks>
        ''' 只要超过 <c>Slop</c>(2 mm)，接触求解器就会产生法向冲量与摩擦；取 4 mm 既能夹住
        ''' 32 mm 的积木，又不会让 Baumgarte 偏置速度大到把积木弹飞。
        ''' </remarks>
        Private Const MaxFingerSqueeze As Double = 0.0035

        ''' <summary>手指是否已经压到了物体（用的是上一步的接触信息）。</summary>
        Private _fingerContact As Boolean

        ''' <summary>刚接触到物体时的手指行程，挤入深度以它为基准。</summary>
        Private _fingerContactExtension As Double

        ''' <summary>本次夹紧允许的挤入深度（由夹持力决定）。</summary>
        Private _fingerSqueeze As Double
        Private ReadOnly _fingerCenters As New List(Of Vector3)()

        ''' <summary>各连杆碰撞盒的局部尺寸（来自 URDF collision box）。</summary>
        Private ReadOnly _linkBoxes As New Dictionary(Of String, Vector3)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>手指碰撞盒的尺寸，与 <see cref="FingerBodies"/> 一一对应。</summary>
        Private ReadOnly _fingerBoxes As New List(Of Vector3)()
        Private ReadOnly _fingerAxes As New List(Of Vector3)()

        ''' <summary>当前关节角（运动学模式下的真实值）。</summary>
        Public ReadOnly Property JointAngleQ As Double()
            Get
                Return _jointQ
            End Get
        End Property

        Public Sub New(assembly As ArmAssembly, Optional options As ArmSimulationOptions = Nothing)
            If assembly Is Nothing Then Throw New ArgumentNullException(NameOf(assembly))

            Me.Assembly = assembly
            Me.Options = If(options, New ArmSimulationOptions())

            Call Build()
        End Sub

        ' /********************************************************************************/
        '  构建
        ' /********************************************************************************/

        Private Sub Build()
            _world = New PhysicsWorld3D With {
                .Iterations = Options.Iterations,
                .Substeps = Options.Substeps,
                .FixedDt = 1.0 / System.Math.Max(30.0, Options.StepsPerSecond),
                .AllowSleep = False,
                .WarmStarting = True,
                .Baumgarte = 0.2,

                ' Slop 是"允许多少穿透不修正"，它同时决定了夹持力的下限：
                ' 手指压进积木的深度必须超过 Slop，接触求解器才会给出法向冲量与摩擦。
                ' 取 0.5 mm（原来 2 mm 时，夹持需要的挤压量会把 32 mm 的积木压变形）
                .Slop = 0.0005
            }

            _world.Gravity = If(Options.EnableGravity, New Vector3(0, 0, -Options.Gravity), Vector3.Zero)

            Call LinkBodies.Clear()
            Call RevoluteJoints.Clear()
            Call JointServos.Clear()
            Call JointDefs.Clear()
            Call JointInertias.Clear()
            Call FingerBodies.Clear()
            Call FingerJoints.Clear()
            Call FingerServos.Clear()
            Call FingerMeshes.Clear()
            Call FingerNames.Clear()
            Call _fingerCenters.Clear()
            Call _fingerAxes.Clear()
            Call Objects.Clear()
            Call LastCollisions.Clear()
            Call GroundContacts.Clear()
            Call _linkBoxes.Clear()
            Call _fingerBoxes.Clear()

            Time = 0.0
            StepCount = 0
            Estop = False
            Enabled = True

            ' ---- 地面 ----
            Dim ground As RigidBody3D = PhysicsWorld3D.StaticPlane(
                New Vector3(0, 0, 1), 0.0, New PhysicsMaterial(0.8, 0.02))

            ground.Label = "ground"
            ground.CollisionGroup = BlockFactory.GroupEnvironment
            ground.CollisionMask = BlockFactory.MaskEnvironment
            _world.Add(ground)

            ' ---- 连杆刚体 ----
            Dim zeroQ As Double() = New Double(System.Math.Max(0, Assembly.JointCount - 1)) {}
            Dim poses As Dictionary(Of String, Pose3D) = Assembly.ForwardKinematics(zeroQ)

            ' 只给"运动链上的 link"建刚体：URDF 的 tcp 通过 fixed joint 挂在手腕上，
            ' 它只是一个标记点，若建成自由刚体会掉到地上并挡住夹爪
            Dim activeLinks As New List(Of String) From {Assembly.BaseLink}

            For Each definition As ArmJointDef In Assembly.Joints
                activeLinks.Add(definition.ChildLink)
            Next

            For Each linkName As String In activeLinks
                Dim link As UrdfLink = Assembly.Robot.FindLink(linkName)

                If link Is Nothing Then Continue For

                Dim body As RigidBody3D = CreateLinkBody(link, poses)

                LinkBodies(link.Name) = body
                _world.Add(body)
            Next

            ' ---- 夹爪（先建，关节的惯量估算要把手指算进下游质量）----
            Call BuildGripper(poses)

            ' ---- 关节 + 伺服 ----
            Call InitKinematicState()

            For i As Integer = 0 To Assembly.Joints.Count - 1
                Dim definition As ArmJointDef = Assembly.Joints(i)
                Dim parentBody As RigidBody3D = Nothing
                Dim childBody As RigidBody3D = Nothing

                JointDefs.Add(definition)

                ' 运动学模式：连杆是 kinematic 碰撞体，只按 FK 摆放，不需要关节约束与伺服
                If Options.KinematicArm Then
                    Continue For
                End If

                If Not LinkBodies.TryGetValue(definition.ParentLink, parentBody) Then Continue For
                If Not LinkBodies.TryGetValue(definition.ChildLink, childBody) Then Continue For

                Dim joint As New RevoluteJoint3D(
                    parentBody, childBody,
                    definition.AnchorA, definition.AnchorB,
                    definition.Axis, definition.Axis,
                    definition.Lower, definition.Upper,
                    definition.Name)

                joint.Baumgarte = 0.2
                joint.LimitBaumgarte = 0.3

                _world.Add(joint)
                RevoluteJoints.Add(joint)

                Dim maxVelocity As Double = System.Math.Max(0.2, definition.Velocity * Options.VelocityScale)
                Dim maxForce As Double = System.Math.Max(1.0, definition.Effort)

                ' 速度环增益必须按"关节有效惯量"限幅：显式积分下 Kd·dt/I 超过 2 就会发散。
                ' 末三轴（j4/j5/j6）的等效惯量只有 1e-3 量级，若沿用统一的 25 N·m/(rad/s)，
                ' 一步之内速度就被反向 30 倍，表现为 ±1 rad 的低频摆动。
                Dim inertia As Double = EstimateJointInertia(i)
                Dim substepDt As Double = 1.0 / (System.Math.Max(30.0, Options.StepsPerSecond) * System.Math.Max(1, Options.Substeps))
                Dim dampingLimit As Double = 0.7 * 2.0 * inertia / substepDt

                JointInertias.Add(inertia)

                Dim damping As Double = System.Math.Min(Options.ServoDamping,
                                                        System.Math.Min(dampingLimit, maxForce / maxVelocity))

                damping = System.Math.Max(0.05, damping)

                Dim servo As New JointServo3D(joint, ServoMode3D.Position) With {
                    .Target = 0.0,
                    .Stiffness = Options.ServoStiffness,
                    .Damping = damping,
                    .MaxVelocity = maxVelocity,
                    .MaxForce = maxForce,
                    .Tolerance = 0.01,
                    .Label = definition.Name
                }

                _world.Add(servo)
                JointServos.Add(servo)
            Next

            ' ---- 默认积木 ----
            If Options.SpawnDefaultBlocks Then
                Call SpawnDefaultBlocks()
            End If
        End Sub

        ''' <summary>
        ''' 关节自身的转动惯量（子连杆沿关节轴的主惯量）。
        ''' </summary>
        ''' <remarks>
        ''' 这里刻意取"被驱动刚体自身"的惯量，而不是"关节下游的等效惯量"：
        ''' 伺服力矩先进入子连杆，与下游的耦合要等约束求解器在**同一子步内更晚**才传递，
        ''' 对显式积分来说，稳定性取决于前者（<c>Kd·dt/I_self</c>）。
        ''' 用等效惯量会把阻尼放大 5~10 倍，实测让 j3 在假平衡点上持续振荡。
        ''' </remarks>
        Private Function EstimateJointInertia(index As Integer) As Double
            If index < 0 OrElse index >= Assembly.Joints.Count Then Return 1.0E-4

            Dim definition As ArmJointDef = Assembly.Joints(index)
            Dim body As RigidBody3D = Nothing

            If Not LinkBodies.TryGetValue(definition.ChildLink, body) Then Return 1.0E-4

            Dim axis As Vector3 = Vector3Math.Normalize(definition.Axis)
            Dim along As Double = Vector3Math.Dot(axis, body.InertiaLocal.MultiplyLeft(axis))

            If along <= 0.0 OrElse Double.IsNaN(along) Then
                along = System.Math.Max(body.InertiaLocal.m11,
                                        System.Math.Max(body.InertiaLocal.m22, body.InertiaLocal.m33))
            End If

            Return System.Math.Max(along, 1.0E-6)
        End Function

        Private Function CreateLinkBody(link As UrdfLink, poses As Dictionary(Of String, Pose3D)) As RigidBody3D
            Dim pose As Pose3D = Assembly.BodyPose(poses, link.Name)
            Dim size As Vector3 = link.CollisionSize

            Dim body As RigidBody3D = PhysicsWorld3D.Box(
                System.Math.Max(0.005, size.x),
                System.Math.Max(0.005, size.y),
                System.Math.Max(0.005, size.z),
                System.Math.Max(0.01, link.Mass),
                New PhysicsMaterial(0.6, 0.0))

            body.Position = pose.Position
            body.Orientation = pose.Rotation
            body.Label = link.Name
            body.CollisionGroup = 1
            body.CollisionMask = BlockFactory.GroupEnvironment
            body.AngularDamping = 0.15
            body.LinearDamping = 0.0
            body.MaxAngularSpeed = 25.0

            ' 用 URDF 给出的主惯量替换均匀盒惯量，再叠加折算到连杆侧的转子惯量
            If link.Inertia IsNot Nothing AndAlso link.Inertia.x > 0 AndAlso link.Inertia.y > 0 AndAlso link.Inertia.z > 0 Then
                Dim motor As Double = System.Math.Max(0.0, Options.MotorInertia)

                body.InertiaLocal = Matrix3x3.Diagonal(
                    link.Inertia.x + motor,
                    link.Inertia.y + motor,
                    link.Inertia.z + motor)
                Call body.InvalidateInertiaCache()
            End If

            If Options.KinematicArm OrElse
               String.Equals(link.Name, Assembly.BaseLink, StringComparison.OrdinalIgnoreCase) Then
                Call body.SetStatic()
            End If

            ' 记下碰撞盒尺寸：地面穿透检测要用它算 8 个角点的世界高度
            _linkBoxes(link.Name) = New Vector3(
                System.Math.Max(0.005, size.x),
                System.Math.Max(0.005, size.y),
                System.Math.Max(0.005, size.z))

            Return body
        End Function

        Private Sub BuildGripper(poses As Dictionary(Of String, Pose3D))
            If Assembly.JointCount = 0 Then Return
            If Assembly.Meshes Is Nothing Then Return

            Dim wristLink As String = Assembly.Joints(Assembly.Joints.Count - 1).ChildLink
            Dim wristBody As RigidBody3D = Nothing

            If Not LinkBodies.TryGetValue(wristLink, wristBody) Then Return

            Dim wristPose As Pose3D = Assembly.BodyPose(poses, wristLink)
            Dim names As String() = {"Finger_L", "Finger_R"}
            Dim centers As New List(Of Vector3)()

            For Each fingerName As String In names
                Dim mesh As LinkMesh = Assembly.Meshes.GetMesh(fingerName)

                If mesh Is Nothing OrElse mesh.TriangleCount = 0 Then Continue For

                Dim center As New Vector3(
                    (mesh.BoundsMin.X + mesh.BoundsMax.X) * 0.5,
                    (mesh.BoundsMin.Y + mesh.BoundsMax.Y) * 0.5,
                    (mesh.BoundsMin.Z + mesh.BoundsMax.Z) * 0.5)

                Dim size As New Vector3(
                    System.Math.Max(0.004, mesh.BoundsMax.X - mesh.BoundsMin.X),
                    System.Math.Max(0.004, mesh.BoundsMax.Y - mesh.BoundsMin.Y),
                    System.Math.Max(0.004, mesh.BoundsMax.Z - mesh.BoundsMin.Z))

                Dim fingerPose As New Pose3D(wristPose.Transform(center), wristPose.Rotation)
                Dim body As RigidBody3D = PhysicsWorld3D.Box(
                    size.x, size.y, size.z,
                    System.Math.Max(0.01, Options.FingerMass),
                    New PhysicsMaterial(1.1, 0.0))

                body.Position = fingerPose.Position
                body.Orientation = fingerPose.Rotation
                body.Label = fingerName
                body.CollisionGroup = 1
                body.CollisionMask = BlockFactory.GroupEnvironment
                body.AngularDamping = 0.6
                body.MaxSpeed = 1.0
                body.MaxAngularSpeed = 8.0

                If Options.KinematicArm Then
                    Call body.SetStatic()
                End If

                _world.Add(body)
                FingerBodies.Add(body)
                FingerNames.Add(fingerName)
                _fingerBoxes.Add(size)
                centers.Add(center)
                _fingerCenters.Add(center)

                ' 闭合方向：glb 里 Finger_L 在 link6 的 -Y 侧、Finger_R 在 +Y 侧，
                ' 因此开合轴就是 link6 的局部 Y 轴
                Dim inward As Vector3 = If(fingerName.EndsWith("L", StringComparison.Ordinal),
                                           New Vector3(0, 1, 0),
                                           New Vector3(0, -1, 0))

                _fingerAxes.Add(inward)

                If Not Options.KinematicArm Then
                    Dim anchorA As Vector3 = center - Assembly.InertialOriginOf(wristLink)
                    Dim joint As New PrismaticJoint3D(wristBody, body, anchorA, Vector3.Zero,
                                                      inward, -GripperOpenExtension, GripperMaxExtension,
                                                      fingerName & "_slide")

                    _world.Add(joint)
                    FingerJoints.Add(joint)

                    Dim servo As New JointServo3D(joint, ServoMode3D.Force) With {
                        .Target = 0.0,
                        .MaxForce = Options.GripperMaxForce,
                        .ForceDamping = 30.0,
                        .Label = fingerName
                    }

                    _world.Add(servo)
                    FingerServos.Add(servo)
                End If

                FingerMeshes.Add(MeshTessellator.Offset(mesh, center))
            Next

            If centers.Count = 2 Then
                Dim gap As Double = System.Math.Abs(centers(1).y - centers(0).y)

                If FingerBodies.Count = 2 Then
                    gap -= FingerBodies(0).Shape.GetAABB(FingerBodies(0).Position, FingerBodies(0).Orientation).Extents.y +
                           FingerBodies(1).Shape.GetAABB(FingerBodies(1).Position, FingerBodies(1).Orientation).Extents.y
                End If

                FingerBaseGap = System.Math.Max(0.005, gap)
            End If
        End Sub

        ' /********************************************************************************/
        '  推进
        ' /********************************************************************************/

        ''' <summary>推进一个固定物理步长。</summary>
        Public Sub StepOnce()
            If _world Is Nothing Then Return

            ' 手指必须在物理步进**之前**就摆到本步的位置：
            ' 之前放在 Step 之后，接触检测用的是上一帧的手指姿态，
            ' 夹爪闭合时永远"慢一拍"，压到积木的那一步检测不到接触
            If Options.KinematicArm Then
                Call UpdateFingerMotion()
            End If

            Call _world.Step(_world.FixedDt)

            Time += _world.FixedDt
            StepCount += 1

            If Options.KinematicArm Then
                Call UpdateKinematicSpeeds()
            End If

            Call DetectGroundContacts()
            Call RefreshCollisions()
        End Sub

        ''' <summary>推进 <paramref name="seconds"/> 秒（按固定步长取整）。</summary>
        Public Sub Advance(seconds As Double)
            Dim dt As Double = _world.FixedDt

            If dt <= 0.0 Then Return

            Dim steps As Integer = CInt(System.Math.Round(seconds / dt))

            For i As Integer = 1 To System.Math.Max(1, steps)
                Call StepOnce()
            Next
        End Sub

        ''' <summary>
        ''' 检测机械臂（连杆 + 手指）是否插进了地面。
        ''' </summary>
        ''' <remarks>
        ''' 不能依赖物理引擎的接触：机械臂在两种模式下都是静态刚体，
        ''' 而 <c>BroadPhase3D</c> 会跳过 static-static 配对，连杆与地面之间根本不会产生
        ''' <see cref="Manifold3D"/>。所以这里直接用碰撞盒的 8 个角点与地面高度比较。
        ''' </remarks>
        Private Sub DetectGroundContacts()
            Call GroundContacts.Clear()

            Dim baseName As String = If(Assembly?.BaseLink, "")

            For Each pair As KeyValuePair(Of String, RigidBody3D) In LinkBodies
                Dim name As String = pair.Key

                ' 基座本来就坐在地面上，不算碰撞
                If baseName.Length > 0 AndAlso String.Equals(name, baseName, StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim size As Vector3 = Nothing

                If Not _linkBoxes.TryGetValue(name, size) Then Continue For

                Dim lowest As Double = LowestBoxZ(pair.Value.Position, pair.Value.Orientation, size)

                If lowest < GroundZ + GroundClearance Then
                    GroundContacts.Add(name & "|ground")
                End If
            Next

            For i As Integer = 0 To FingerBodies.Count - 1
                If i >= _fingerBoxes.Count Then Continue For

                Dim body As RigidBody3D = FingerBodies(i)
                Dim lowest As Double = LowestBoxZ(body.Position, body.Orientation, _fingerBoxes(i))

                If lowest < GroundZ + FingerClearance Then
                    Dim label As String = If(i < FingerNames.Count, FingerNames(i), "Finger")

                    GroundContacts.Add(label & "|ground")
                End If
            Next
        End Sub

        ''' <summary>
        ''' 给定关节角，预测哪个连杆会插进地面；不会插入则返回 <c>Nothing</c>。
        ''' </summary>
        ''' <param name="q">候选关节角（弧度）</param>
        ''' <returns>第一个会穿地的连杆名</returns>
        ''' <remarks>用于运动指令的事前校验与插值过程中的前瞻，让机械臂根本不会下探到地面以下。</remarks>
        Public Function GroundViolation(q As Double()) As String
            If q Is Nothing OrElse Assembly Is Nothing OrElse _linkBoxes.Count = 0 Then Return Nothing

            Dim baseName As String = If(Assembly.BaseLink, "")
            Dim poses As Dictionary(Of String, Pose3D) = Assembly.ForwardKinematics(q)

            For Each pair As KeyValuePair(Of String, Vector3) In _linkBoxes
                If baseName.Length > 0 AndAlso String.Equals(pair.Key, baseName, StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim pose As Pose3D = Assembly.BodyPose(poses, pair.Key)
                Dim lowest As Double = LowestBoxZ(pose.Position, pose.Rotation, pair.Value)

                If lowest < GroundZ + GroundClearance Then Return pair.Key
            Next

            Return Nothing
        End Function

        ''' <summary>盒体（以 center 为中心、随 rotation 旋转）8 个角点里最低的那个世界 Z。</summary>
        Public Shared Function LowestBoxZ(center As Vector3, rotation As Quaternion, size As Vector3) As Double
            Dim halfX As Double = size.x * 0.5
            Dim halfY As Double = size.y * 0.5
            Dim halfZ As Double = size.z * 0.5
            Dim lowest As Double = Double.MaxValue

            For sx As Integer = -1 To 1 Step 2
                For sy As Integer = -1 To 1 Step 2
                    For sz As Integer = -1 To 1 Step 2
                        Dim corner As New Vector3(sx * halfX, sy * halfY, sz * halfZ)
                        Dim world As Vector3 = center + rotation.Rotate(corner)

                        If world.z < lowest Then lowest = world.z
                    Next
                Next
            Next

            Return lowest
        End Function

        ''' <summary>机械臂（含手指）当前最低点的世界 Z，用于诊断与自检。</summary>
        Public Function LowestArmZ() As Double
            Dim lowest As Double = Double.MaxValue
            Dim baseName As String = If(Assembly?.BaseLink, "")

            For Each pair As KeyValuePair(Of String, RigidBody3D) In LinkBodies
                If baseName.Length > 0 AndAlso String.Equals(pair.Key, baseName, StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim size As Vector3 = Nothing

                If Not _linkBoxes.TryGetValue(pair.Key, size) Then Continue For

                lowest = System.Math.Min(lowest, LowestBoxZ(pair.Value.Position, pair.Value.Orientation, size))
            Next

            For i As Integer = 0 To FingerBodies.Count - 1
                If i >= _fingerBoxes.Count Then Continue For

                Dim body As RigidBody3D = FingerBodies(i)

                lowest = System.Math.Min(lowest, LowestBoxZ(body.Position, body.Orientation, _fingerBoxes(i)))
            Next

            Return If(lowest = Double.MaxValue, GroundZ, lowest)
        End Function

        Private Sub RefreshCollisions()
            Call LastCollisions.Clear()

            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)

            ' 机械臂与地面之间靠几何检测（物理引擎不做静态-静态碰撞），
            ' 这里合并进同一份接触列表，状态里就能看到 "link3|ground"
            For Each key As String In GroundContacts
                If seen.Add(key) Then LastCollisions.Add(key)
            Next

            For Each manifold As Manifold3D In _world.Contacts
                Dim a As String = If(manifold.A?.Label, "")
                Dim b As String = If(manifold.B?.Label, "")

                If a.Length = 0 OrElse b.Length = 0 Then Continue For

                Dim key As String = If(String.CompareOrdinal(a, b) <= 0, a & "|" & b, b & "|" & a)

                If seen.Add(key) Then
                    LastCollisions.Add(key)
                End If
            Next

            Call UpdateFingerContact()
        End Sub

        ''' <summary>
        ''' 从接触对里判断手指是否压到了积木，并记录接触瞬间的手指行程。
        ''' </summary>
        ''' <remarks>
        ''' 只看手指与积木的配对（手指碰地面不算），接触对写法是
        ''' <c>Finger_L|block-3</c> 或 <c>block-3|Finger_L</c>。
        ''' </remarks>
        Private Sub UpdateFingerContact()
            Dim touching As Boolean = False

            For Each text As String In LastCollisions
                If String.IsNullOrEmpty(text) Then Continue For
                If text.IndexOf("Finger", StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                If text.IndexOf("block-", StringComparison.OrdinalIgnoreCase) < 0 Then Continue For

                touching = True
                Exit For
            Next

            If touching AndAlso Not _fingerContact Then
                _fingerContactExtension = _fingerExtension
            End If

            _fingerContact = touching
        End Sub

        ''' <summary>重建整个世界（场景复位）。</summary>
        Public Sub Reset()
            Call Build()
        End Sub

        ' /********************************************************************************/
        '  关节控制
        ' /********************************************************************************/

        ''' <summary>设置全部关节的目标角（弧度）。</summary>
        Public Sub SetJointTargets(q As Double())
            If q Is Nothing Then Return

            If Options.KinematicArm Then
                If _jointQ Is Nothing OrElse _jointQ.Length <> q.Length Then
                    _jointQ = New Double(q.Length - 1) {}
                    _previousQ = New Double(q.Length - 1) {}
                    _jointQd = New Double(q.Length - 1) {}
                End If

                Array.Copy(_jointQ, _previousQ, _jointQ.Length)
                Array.Copy(q, _jointQ, System.Math.Min(q.Length, _jointQ.Length))
                Call ApplyKinematicPose()
                Return
            End If

            For i As Integer = 0 To System.Math.Min(JointServos.Count, q.Length) - 1
                JointServos(i).Target = q(i)
                JointServos(i).Mode = ServoMode3D.Position
            Next
        End Sub

        ''' <summary>设置单个关节的目标角。</summary>
        Public Sub SetJointTarget(index As Integer, value As Double)
            Dim q As Double() = JointPositions()

            If index < 0 OrElse index >= q.Length Then Return

            q(index) = value
            Call SetJointTargets(q)
        End Sub

        ''' <summary>当前关节角。</summary>
        Public Function JointPositions() As Double()
            If Options.KinematicArm Then
                If _jointQ Is Nothing Then Return New Double() {}

                Return CType(_jointQ.Clone(), Double())
            End If

            Dim out(RevoluteJoints.Count - 1) As Double

            For i As Integer = 0 To RevoluteJoints.Count - 1
                out(i) = RevoluteJoints(i).Position
            Next

            Return out
        End Function

        ''' <summary>当前关节角速度。</summary>
        Public Function JointSpeeds() As Double()
            If Options.KinematicArm Then
                If _jointQd Is Nothing Then Return New Double() {}

                Return CType(_jointQd.Clone(), Double())
            End If

            Dim out(RevoluteJoints.Count - 1) As Double

            For i As Integer = 0 To RevoluteJoints.Count - 1
                out(i) = RevoluteJoints(i).Speed
            Next

            Return out
        End Function

        ' ---- 运动学模式 ----

        ''' <summary>夹爪手指的最大闭合行程（米）。</summary>
        Public Const GripperMaxExtension As Double = 0.022
        ''' <summary>夹爪手指的最大张开行程（米）。</summary>
        Public Const GripperOpenExtension As Double = 0.03

        Private Sub InitKinematicState()
            Dim count As Integer = System.Math.Max(1, Assembly.Joints.Count)

            _jointQ = New Double(count - 1) {}
            _previousQ = New Double(count - 1) {}
            _jointQd = New Double(count - 1) {}
            _fingerExtension = 0.0
            _fingerTarget = 0.0
            _gripForce = 0.0

            Call ApplyKinematicPose()
        End Sub

        ''' <summary>按当前关节角把连杆与手指摆到 FK 位置（运动学模式）。</summary>
        Private Sub ApplyKinematicPose()
            If Not Options.KinematicArm Then Return
            If _jointQ Is Nothing Then Return

            Dim poses As Dictionary(Of String, Pose3D) = Assembly.ForwardKinematics(_jointQ)

            For Each pair As KeyValuePair(Of String, RigidBody3D) In LinkBodies
                Dim pose As Pose3D = Assembly.BodyPose(poses, pair.Key)
                Dim body As RigidBody3D = pair.Value

                body.Position = pose.Position
                body.Orientation = pose.Rotation
                body.Velocity = Vector3.Zero
                body.AngularVelocity = Vector3.Zero
            Next

            Call ApplyFingerPose(poses)
        End Sub

        Private Sub ApplyFingerPose(poses As Dictionary(Of String, Pose3D))
            If FingerBodies.Count = 0 OrElse Assembly.JointCount = 0 Then Return
            If _fingerCenters.Count < FingerBodies.Count Then Return

            Dim wristLink As String = Assembly.Joints(Assembly.Joints.Count - 1).ChildLink
            Dim wristPose As Pose3D = Assembly.BodyPose(poses, wristLink)

            For i As Integer = 0 To FingerBodies.Count - 1
                ' _fingerAxes 是"闭合方向"（指向另一根手指），所以正的行程是把手指往里推
                Dim local As Vector3 = _fingerCenters(i) + _fingerAxes(i) * _fingerExtension
                Dim pose As New Pose3D(wristPose.Transform(local), wristPose.Rotation)
                Dim body As RigidBody3D = FingerBodies(i)

                body.Position = pose.Position
                body.Orientation = pose.Rotation
                body.Velocity = Vector3.Zero
                body.AngularVelocity = Vector3.Zero
            Next
        End Sub

        ''' <summary>
        ''' 夹爪按有限速度闭合/张开。
        ''' </summary>
        ''' <remarks>
        ''' 如果 <see cref="Grip"/> 直接把手指瞬移到闭合位置，一帧内就会移动 2 cm，
        ''' kinematic 手指撞上积木时会把动量全部传给它——实测积木被弹到 0.86 m 外，
        ''' 之后再也检测不到接触。真实夹爪有闭合速度，这里限制在 25 mm/s。
        ''' </remarks>
        Private Sub UpdateFingerMotion()
            If _fingerCenters.Count = 0 Then Return

            Dim maxDelta As Double = 0.025 * _world.FixedDt
            Dim diff As Double = _fingerTarget - _fingerExtension

            If System.Math.Abs(diff) < 1.0E-6 Then Return

            Dim move As Double = System.Math.Max(-maxDelta, System.Math.Min(maxDelta, diff))

            ' 夹紧时一旦压到物体就只再挤入一点点：
            ' 继续照着"最大行程"推会把积木压进去十几毫米，Baumgarte 偏置速度
            ' 会达到几米每秒，积木不是被夹住而是被射出去
            If diff > 0.0 AndAlso _gripForce > 0.0 AndAlso _fingerContact Then
                Dim limit As Double = _fingerContactExtension + _fingerSqueeze

                If _fingerExtension + move > limit Then
                    move = limit - _fingerExtension

                    If move < 0.0 Then move = 0.0
                End If
            End If

            If System.Math.Abs(move) < 1.0E-9 Then Return

            _fingerExtension += move

            Call ApplyKinematicPose()
        End Sub

        Private Sub UpdateKinematicSpeeds()
            If _jointQ Is Nothing OrElse _jointQd Is Nothing Then Return

            Dim dt As Double = _world.FixedDt

            If dt <= 0.0 Then Return

            For i As Integer = 0 To _jointQ.Length - 1
                _jointQd(i) = (_jointQ(i) - _previousQ(i)) / dt
                _previousQ(i) = _jointQ(i)
            Next
        End Sub

        ''' <summary>保持当前位置（把目标设为当前角）。</summary>
        Public Sub HoldCurrent()
            If Options.KinematicArm Then
                Call SetJointTargets(JointPositions())
                Return
            End If

            For Each servo As JointServo3D In JointServos
                servo.Target = servo.CurrentPosition
                servo.Mode = ServoMode3D.Position
            Next
        End Sub

        ''' <summary>所有关节切换到自由状态（失能）。</summary>
        Public Sub ReleaseServos()
            For Each servo As JointServo3D In JointServos
                servo.Mode = ServoMode3D.Disabled
            Next
        End Sub

        Public Sub SetServoEnabled(value As Boolean)
            Enabled = value

            For Each servo As JointServo3D In JointServos
                servo.Enabled = value
            Next

            For Each servo As JointServo3D In FingerServos
                servo.Enabled = value
            Next
        End Sub

        ''' <summary>急停：伺服失能、速度清零。</summary>
        Public Sub EmergencyStop()
            Estop = True

            Call ReleaseServos()

            For Each servo As JointServo3D In FingerServos
                servo.Mode = ServoMode3D.Disabled
            Next

            If _world IsNot Nothing Then
                Call _world.ClearVelocities()
            End If
        End Sub

        ' /********************************************************************************/
        '  夹爪
        ' /********************************************************************************/

        ''' <summary>
        ''' 夹紧：<paramref name="force"/> 是期望夹持力（N）。
        ''' </summary>
        ''' <remarks>
        ''' <para>
        ''' 运动学模式下手指是位置驱动的，所以夹持力被映射成"压到物体里多深"：
        ''' 接触求解器的 Baumgarte 项会按 <c>bias = Baumgarte/dt * (穿透 − Slop)</c>
        ''' 给出分离速度，穿透越深、法向冲量与库仑摩擦上限越大，也就夹得越紧。
        ''' </para>
        ''' <para>
        ''' <b>关键</b>：这个深度必须被限制住。早期实现直接把手指推到最大行程，
        ''' 夹 32 mm 的积木时会压进去 ~18 mm，bias 高达 3 m/s 量级——积木不是被夹住，
        ''' 而是被"射"出去。现在改成：手指以有限速度闭合，一旦接触到物体就只再挤入
        ''' <see cref="MaxFingerSqueeze"/> × 力比例 的深度（默认最多 4 mm）。
        ''' </para>
        ''' </remarks>
        Public Sub Grip(force As Double)
            _gripForce = System.Math.Max(0.0, force)

            If Options.KinematicArm Then
                Dim ratio As Double = 0.0

                If Options.GripperMaxForce > 0.0 Then ratio = _gripForce / Options.GripperMaxForce
                ratio = System.Math.Max(0.0, System.Math.Min(1.0, ratio))

                _fingerTarget = GripperMaxExtension * ratio

                ' 新的一次夹紧：重新记录接触时刻的行程
                _fingerContact = False
                _fingerContactExtension = 0.0
                _fingerSqueeze = MaxFingerSqueeze * ratio

                Return
            End If

            For Each servo As JointServo3D In FingerServos
                servo.MaxForce = System.Math.Max(1.0, force)
                servo.Target = System.Math.Max(1.0, force)
                servo.Mode = ServoMode3D.Force
            Next
        End Sub

        ''' <summary>松开（张开手指）。</summary>
        Public Sub ReleaseGripper(Optional force As Double = 8.0)
            _gripForce = 0.0

            If Options.KinematicArm Then
                _fingerTarget = -GripperOpenExtension
                _fingerContact = False

                Return
            End If

            For Each servo As JointServo3D In FingerServos
                servo.MaxForce = System.Math.Max(1.0, force)
                servo.Target = -System.Math.Max(1.0, force) * 0.6
                servo.Mode = ServoMode3D.Force
            Next
        End Sub

        ''' <summary>停止手指（保持当前力为 0，手指自由）。</summary>
        Public Sub IdleGripper()
            _gripForce = 0.0

            If Options.KinematicArm Then Return

            For Each servo As JointServo3D In FingerServos
                servo.Target = 0.0
                servo.Mode = ServoMode3D.Force
            Next
        End Sub

        ''' <summary>当前开口宽度（米）。</summary>
        Public ReadOnly Property GripperOpening As Double
            Get
                If Options.KinematicArm Then
                    Return FingerBaseGap - 2.0 * _fingerExtension
                End If

                If FingerJoints.Count < 2 Then Return FingerBaseGap

                Return FingerBaseGap - (FingerJoints(0).Position + FingerJoints(1).Position)
            End Get
        End Property

        ''' <summary>两个手指的世界坐标。</summary>
        Public Function FingerPositions() As List(Of Double())
            Dim out As New List(Of Double())()

            For Each body As RigidBody3D In FingerBodies
                out.Add(StateSnapshot.ToArray(body.Position))
            Next

            Return out
        End Function

        ''' <summary>当前指令夹持力（N）。</summary>
        Public ReadOnly Property GripperForce As Double
            Get
                If Options.KinematicArm Then Return _gripForce

                Dim max As Double = 0.0

                For Each servo As JointServo3D In FingerServos
                    max = System.Math.Max(max, System.Math.Abs(servo.LastForce))
                Next

                Return max
            End Get
        End Property

        ' /********************************************************************************/
        '  场景物体
        ' /********************************************************************************/

        Public Function SpawnDefaultBlocks() As List(Of BlockInstance)
            Dim specs As List(Of BlockSpec) = BlockFactory.DefaultScene()
            Dim count As Integer = System.Math.Min(System.Math.Max(0, Options.DefaultBlockCount), specs.Count)
            Dim out As New List(Of BlockInstance)()

            For i As Integer = 0 To count - 1
                Dim spec As BlockSpec = specs(i)
                Dim column As Integer = i Mod 4
                Dim row As Integer = i \ 4
                Dim x As Double = 0.30 + column * 0.075
                Dim y As Double = -0.10 + row * 0.075

                Dim position As Vector3 = BlockFactory.RestingPosition(spec, x, y, 0.0, 0.0)
                Dim instance As BlockInstance = BlockFactory.Create(
                    "block-" & (i + 1), spec, position,
                    BuildPlacementRotation(spec, 0.0))

                _world.Add(instance.Body)
                Objects.Add(instance)
                out.Add(instance)
            Next

            Return out
        End Function

        Private Function BuildPlacementRotation(spec As BlockSpec, yaw As Double) As Quaternion
            Dim yawRotation As Quaternion = Quaternion.FromAxisAngle(New Vector3(0, 0, 1), yaw)

            If BlockFactory.NeedsUpright(spec.Shape) Then
                Return Quaternion.Compose(yawRotation, BlockFactory.UprightRotation())
            End If

            Return yawRotation
        End Function

        ''' <summary>生成一块积木。</summary>
        ''' <param name="shape">形状名（box/sphere/cylinder/cone/frustum/hemisphere/prism/pyramid/disc/wedge）</param>
        ''' <param name="size">尺寸数组（按形状解释）</param>
        ''' <param name="position">质心世界位置；为 Nothing 时自动放在地面上</param>
        Public Function SpawnBlock(shape As String, size As Double(), position As Vector3,
                                   Optional rotation As Quaternion? = Nothing,
                                   Optional mass As Double = 0.2,
                                   Optional friction As Double = 0.75,
                                   Optional color As String = Nothing) As BlockInstance

            Dim spec As New BlockSpec With {
                .Shape = BlockFactory.ParseShape(shape),
                .Size = size,
                .Mass = mass,
                .Friction = friction,
                .Color = If(String.IsNullOrEmpty(color), NextColor(), color)
            }

            Dim placement As Quaternion

            If rotation.HasValue Then
                placement = rotation.Value
            Else
                placement = BuildPlacementRotation(spec, 0.0)
            End If

            Dim center As Vector3 = position

            If center Is Nothing Then
                center = BlockFactory.RestingPosition(spec, 0.35, 0.0, 0.0, 0.0)
            End If

            Dim id As String = "block-" & (Objects.Count + 1)
            Dim instance As BlockInstance = BlockFactory.Create(id, spec, center, placement)

            _world.Add(instance.Body)
            Objects.Add(instance)

            Return instance
        End Function

        Private Function NextColor() As String
            Dim palette As String() = {"#F59E0B", "#22D3EE", "#34D399", "#F472B6", "#A78BFA", "#FBBF24", "#60A5FA"}

            Return palette(Objects.Count Mod palette.Length)
        End Function

        Public Function FindObject(id As String) As BlockInstance
            For Each item As BlockInstance In Objects
                If String.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) Then Return item
            Next

            Return Nothing
        End Function

        Public Function RemoveObject(id As String) As Boolean
            Dim item As BlockInstance = FindObject(id)

            If item Is Nothing Then Return False

            Call Objects.Remove(item)

            If _world IsNot Nothing Then
                Call _world.Remove(item.Body)
            End If

            Return True
        End Function

        Public Sub ClearObjects()
            For Each item As BlockInstance In Objects.ToArray()
                If _world IsNot Nothing Then
                    Call _world.Remove(item.Body)
                End If
            Next

            Call Objects.Clear()
        End Sub

        ' /********************************************************************************/
        '  状态
        ' /********************************************************************************/

        ''' <summary>生成一份不可变的状态快照。</summary>
        Public Function CaptureState() As StateSnapshot
            Dim q As Double() = JointPositions()
            Dim qd As Double() = JointSpeeds()
            Dim tcp As Pose3D = Assembly.TcpPose(q)
            Dim rpy As Double() = StateSnapshot.ToRpy(tcp.Rotation)

            Dim joints As New List(Of JointState)()
            Dim jointCount As Integer = If(Options.KinematicArm, JointDefs.Count, RevoluteJoints.Count)

            For i As Integer = 0 To jointCount - 1
                Dim servo As JointServo3D = If(i < JointServos.Count, JointServos(i), Nothing)

                joints.Add(New JointState With {
                    .Name = If(i < JointDefs.Count, JointDefs(i).Name, "j" & (i + 1)),
                    .Angle = If(i < q.Length, q(i), 0.0),
                    .Speed = If(i < qd.Length, qd(i), 0.0),
                    .Target = If(servo Is Nothing, If(i < q.Length, q(i), 0.0), servo.Target),
                    .Torque = If(servo Is Nothing, 0.0, servo.LastForce),
                    .Lower = If(i < JointDefs.Count, JointDefs(i).Lower, -System.Math.PI),
                    .Upper = If(i < JointDefs.Count, JointDefs(i).Upper, System.Math.PI)
                })
            Next

            Dim objectStates As New List(Of ObjectState)()
            Dim maxSpeed As Double = 0.0

            For Each item As BlockInstance In Objects
                Dim speed As Double = Vector3Math.Length(item.Body.Velocity)
                maxSpeed = System.Math.Max(maxSpeed, speed)

                objectStates.Add(New ObjectState With {
                    .Id = item.Id,
                    .Kind = BlockFactory.ShapeName(item.Spec.Shape),
                    .Position = StateSnapshot.ToArray(item.Body.Position),
                    .Rotation = StateSnapshot.ToArray(item.Body.Orientation),
                    .Velocity = StateSnapshot.ToArray(item.Body.Velocity),
                    .Size = StateSnapshot.ToArray(item.Size),
                    .Mass = item.Body.Mass,
                    .Color = item.Spec.Color
                })
            Next

            Dim holdingId As String = Nothing

            For Each pairText As String In LastCollisions
                Dim parts As String() = pairText.Split("|"c)

                For Each part As String In parts
                    If part.StartsWith("Finger", StringComparison.OrdinalIgnoreCase) Then
                        For Each other As String In parts
                            If other.StartsWith("block", StringComparison.OrdinalIgnoreCase) Then
                                holdingId = other
                            End If
                        Next
                    End If
                Next
            Next

            Return New StateSnapshot With {
                .Time = Time,
                .StepIndex = StepCount,
                .Q = q,
                .Qd = qd,
                .Tcp = {tcp.Position.x, tcp.Position.y, tcp.Position.z, rpy(0), rpy(1), rpy(2)},
                .TcpQuaternion = StateSnapshot.ToArray(tcp.Rotation),
                .Joints = joints,
                .Gripper = New GripperState With {
                    .Opening = GripperOpening,
                    .Force = GripperForce,
                    .Fingers = FingerPositions(),
                    .Closing = If(Options.KinematicArm,
                                  _fingerExtension > 0.0,
                                  FingerServos.Count > 0 AndAlso FingerServos(0).Target > 0.0),
                    .Contact = holdingId IsNot Nothing,
                    .HoldingId = holdingId
                },
                .Collisions = LastCollisions.ToList(),
                .Objects = objectStates,
                .Estop = Estop,
                .Enabled = Enabled,
                .SimMode = "physics",
                .MaxObjectSpeed = maxSpeed
            }
        End Function

    End Class

End Namespace
