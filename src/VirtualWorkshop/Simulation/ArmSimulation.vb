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
        Public Property Substeps As Integer = 4
        Public Property Iterations As Integer = 20

        ''' <summary>
        ''' 位置伺服的位置环增益（1/s）。
        ''' </summary>
        ''' <remarks>
        ''' 位置环 × 速度环的乘积就是等效的力矩刚度。六轴臂在零位时 j2 需要
        ''' 约 15 N·m 才能撑住前臂，若刚度不足就会出现明显的重力下垂：
        ''' <c>40 × 25 = 1000 N·m/rad</c> 把静差压到 0.015 rad（≈0.9°）。
        ''' </remarks>
        Public Property ServoStiffness As Double = 40.0
        ''' <summary>位置伺服的阻尼（速度环增益，N·m/(rad/s)）。</summary>
        Public Property ServoDamping As Double = 25.0
        ''' <summary>关节速度上限的缩放（相对 URDF 的 velocity）。</summary>
        Public Property VelocityScale As Double = 0.8

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
                .Slop = 0.002
            }

            _world.Gravity = If(Options.EnableGravity, New Vector3(0, 0, -Options.Gravity), Vector3.Zero)

            Call LinkBodies.Clear()
            Call RevoluteJoints.Clear()
            Call JointServos.Clear()
            Call JointDefs.Clear()
            Call FingerBodies.Clear()
            Call FingerJoints.Clear()
            Call FingerServos.Clear()
            Call FingerMeshes.Clear()
            Call FingerNames.Clear()
            Call Objects.Clear()
            Call LastCollisions.Clear()

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

            ' ---- 关节 + 伺服 ----
            For Each definition As ArmJointDef In Assembly.Joints
                Dim parentBody As RigidBody3D = Nothing
                Dim childBody As RigidBody3D = Nothing

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
                JointDefs.Add(definition)

                Dim maxVelocity As Double = System.Math.Max(0.2, definition.Velocity * Options.VelocityScale)
                Dim maxForce As Double = System.Math.Max(1.0, definition.Effort)

                ' 速度环增益必须与"力矩上限 / 速度上限"匹配：若增益远超
                ' MaxForce/(MaxVelocity)，一点速度误差就让输出饱和，伺服退化成
                ' 开关控制并出现极限环（实测末三轴会持续抖动 ±0.2 rad）。
                Dim damping As Double = System.Math.Min(
                    Options.ServoDamping,
                    maxForce / maxVelocity * 0.5)

                damping = System.Math.Max(0.5, damping)

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

            ' ---- 夹爪 ----
            Call BuildGripper(poses)

            ' ---- 默认积木 ----
            If Options.SpawnDefaultBlocks Then
                Call SpawnDefaultBlocks()
            End If
        End Sub

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

            ' 用 URDF 给出的主惯量替换均匀盒惯量
            If link.Inertia IsNot Nothing AndAlso link.Inertia.x > 0 AndAlso link.Inertia.y > 0 AndAlso link.Inertia.z > 0 Then
                body.InertiaLocal = Matrix3x3.Diagonal(link.Inertia.x, link.Inertia.y, link.Inertia.z)
                Call body.InvalidateInertiaCache()
            End If

            If String.Equals(link.Name, Assembly.BaseLink, StringComparison.OrdinalIgnoreCase) Then
                Call body.SetStatic()
            End If

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

                _world.Add(body)
                FingerBodies.Add(body)
                FingerNames.Add(fingerName)
                centers.Add(center)

                ' 闭合方向：glb 里 Finger_L 在 link6 的 -Y 侧、Finger_R 在 +Y 侧，
                ' 因此开合轴就是 link6 的局部 Y 轴
                Dim inward As Vector3 = If(fingerName.EndsWith("L", StringComparison.Ordinal),
                                           New Vector3(0, 1, 0),
                                           New Vector3(0, -1, 0))
                Dim anchorA As Vector3 = center - Assembly.InertialOriginOf(wristLink)
                Dim joint As New PrismaticJoint3D(wristBody, body, anchorA, Vector3.Zero,
                                                  inward, -0.03, 0.022, fingerName & "_slide")

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

            Call _world.Step(_world.FixedDt)

            Time += _world.FixedDt
            StepCount += 1

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

        Private Sub RefreshCollisions()
            Call LastCollisions.Clear()

            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)

            For Each manifold As Manifold3D In _world.Contacts
                Dim a As String = If(manifold.A?.Label, "")
                Dim b As String = If(manifold.B?.Label, "")

                If a.Length = 0 OrElse b.Length = 0 Then Continue For

                Dim key As String = If(String.CompareOrdinal(a, b) <= 0, a & "|" & b, b & "|" & a)

                If seen.Add(key) Then
                    LastCollisions.Add(key)
                End If
            Next
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

            For i As Integer = 0 To System.Math.Min(JointServos.Count, q.Length) - 1
                JointServos(i).Target = q(i)
                JointServos(i).Mode = ServoMode3D.Position
            Next
        End Sub

        ''' <summary>设置单个关节的目标角。</summary>
        Public Sub SetJointTarget(index As Integer, value As Double)
            If index < 0 OrElse index >= JointServos.Count Then Return

            JointServos(index).Target = value
            JointServos(index).Mode = ServoMode3D.Position
        End Sub

        ''' <summary>当前关节角。</summary>
        Public Function JointPositions() As Double()
            Dim out(RevoluteJoints.Count - 1) As Double

            For i As Integer = 0 To RevoluteJoints.Count - 1
                out(i) = RevoluteJoints(i).Position
            Next

            Return out
        End Function

        ''' <summary>当前关节角速度。</summary>
        Public Function JointSpeeds() As Double()
            Dim out(RevoluteJoints.Count - 1) As Double

            For i As Integer = 0 To RevoluteJoints.Count - 1
                out(i) = RevoluteJoints(i).Speed
            Next

            Return out
        End Function

        ''' <summary>保持当前位置（把目标设为当前角）。</summary>
        Public Sub HoldCurrent()
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

        ''' <summary>急停：所有伺服失能、速度清零。</summary>
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

        ''' <summary>夹紧：<paramref name="force"/> 是每个手指的最大夹持力（N）。</summary>
        Public Sub Grip(force As Double)
            For Each servo As JointServo3D In FingerServos
                servo.MaxForce = System.Math.Max(1.0, force)
                servo.Target = System.Math.Max(1.0, force)
                servo.Mode = ServoMode3D.Force
            Next
        End Sub

        ''' <summary>松开（反向小力推开手指）。</summary>
        Public Sub ReleaseGripper(Optional force As Double = 8.0)
            For Each servo As JointServo3D In FingerServos
                servo.MaxForce = System.Math.Max(1.0, force)
                servo.Target = -System.Math.Max(1.0, force) * 0.6
                servo.Mode = ServoMode3D.Force
            Next
        End Sub

        ''' <summary>停止手指（保持当前力为 0，手指自由）。</summary>
        Public Sub IdleGripper()
            For Each servo As JointServo3D In FingerServos
                servo.Target = 0.0
                servo.Mode = ServoMode3D.Force
            Next
        End Sub

        ''' <summary>当前开口宽度（米）。</summary>
        Public ReadOnly Property GripperOpening As Double
            Get
                If FingerJoints.Count < 2 Then Return FingerBaseGap

                Return FingerBaseGap - (FingerJoints(0).Position + FingerJoints(1).Position)
            End Get
        End Property

        ''' <summary>当前夹持力（N）。</summary>
        Public ReadOnly Property GripperForce As Double
            Get
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

            For i As Integer = 0 To RevoluteJoints.Count - 1
                Dim servo As JointServo3D = If(i < JointServos.Count, JointServos(i), Nothing)

                joints.Add(New JointState With {
                    .Name = If(i < JointDefs.Count, JointDefs(i).Name, "j" & (i + 1)),
                    .Angle = q(i),
                    .Speed = qd(i),
                    .Target = If(servo Is Nothing, 0.0, servo.Target),
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
                    .Closing = FingerServos.Count > 0 AndAlso FingerServos(0).Target > 0.0,
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
