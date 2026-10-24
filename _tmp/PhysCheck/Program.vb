Imports System
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.Joints3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model
Imports VirtualWorkshop.Simulation

Module Program

    Private failures As Integer = 0

    Sub Main(args As String())
        Call TestModel()
        Call TestSimulation()

        Call TestPendulum()
        Call TestLimit()
        Call TestGrip(40.0, "strong")
        Call TestGrip(2.0, "weak")

        Console.WriteLine()
        Console.WriteLine($"FAILURES={failures}")

        If failures > 0 Then
            Environment.Exit(1)
        End If
    End Sub

    Private Sub Check(ok As Boolean, message As String)
        If ok Then
            Console.WriteLine($"  [PASS] {message}")
        Else
            Console.WriteLine($"  [FAIL] {message}")
            failures += 1
        End If
    End Sub

    Private Function NewWorld() As PhysicsWorld3D
        Return New PhysicsWorld3D With {
            .Gravity = New Vector3(0, 0, -9.81),
            .Iterations = 20,
            .Substeps = 8,
            .FixedDt = 1.0 / 120.0,
            .AllowSleep = False
        }
    End Function

    Private Function LoadAssembly() As ArmAssembly
        Dim robot As UrdfRobot = UrdfRobot.Load("G:\virtual-lab\models\six_axis_arm.urdf")
        Dim scene As GlbScene = GlbLinkLoader.Load("G:\virtual-lab\models\six_axis_arm.glb", robot)

        Return ArmAssembly.Build(robot, scene)
    End Function

    Private Sub TestModel()
        Console.WriteLine("== model ==")

        Dim robot As UrdfRobot = UrdfRobot.Load("G:\virtual-lab\models\six_axis_arm.urdf")

        Console.WriteLine($"  links={robot.Links.Count} joints={robot.Joints.Count} movable={robot.MovableJoints.Count} root={robot.RootLink.Name}")
        Check(robot.Links.Count = 8, "8 links parsed")
        Check(robot.MovableJoints.Count = 6, "6 revolute joints parsed")

        Dim scene As GlbScene = GlbLinkLoader.Load("G:\virtual-lab\models\six_axis_arm.glb", robot)

        Console.WriteLine($"  triangles={scene.TriangleCount} landscape={scene.LandscapeTriangleCount} nodes={scene.NodeCount} meshes={scene.MeshCount}")

        For Each name As String In scene.MeshNames()
            Dim mesh As LinkMesh = scene.GetMesh(name)
            Console.WriteLine($"    {name}: {mesh.TriangleCount} tris owner={mesh.OwnerLink}")
        Next

        Check(scene.TriangleCount = 2704, "2704 triangles")
        Check(scene.TriangleCount = scene.LandscapeTriangleCount, "Landscape agrees on triangle count")

        Dim assembly As ArmAssembly = ArmAssembly.Build(robot, scene)

        Console.WriteLine("  " & assembly.Describe())
        Check(assembly.JointCount = 6, "6 joint definitions")

        Dim zeroQ As Double() = {0, 0, 0, 0, 0, 0}
        Dim zero As Pose3D = assembly.TcpPose(zeroQ)

        Console.WriteLine($"  TCP zero = ({zero.Position.x:F4}, {zero.Position.y:F4}, {zero.Position.z:F4})")
        Check(Math.Abs(zero.Position.x - 0.617) < 0.001 AndAlso Math.Abs(zero.Position.z - 0.178) < 0.001,
              "TCP position at zero pose matches the URDF chain")

        Dim target As New Pose3D(New Vector3(0.45, 0.1, 0.3), zero.Rotation)
        Dim ik = Kinematics.Solve(assembly, target, zeroQ)

        Console.WriteLine($"  IK success={ik.Success} posErr={ik.PositionError:F6} rotErr={ik.RotationError:F6} iters={ik.Iterations}")
        Check(ik.Success, "IK reports success for a reachable pose")
        Check(Vector3Math.Length(assembly.TcpPose(ik.Q).Position - target.Position) < 0.001,
              "IK result reproduces the target through FK")
    End Sub

    Private Sub TestSimulation()
        Console.WriteLine("== simulation ==")

        Dim assembly As ArmAssembly = LoadAssembly()
        Dim sim As New ArmSimulation(assembly, New ArmSimulationOptions())
        Dim hardware As New ArmHardware(sim)

        Console.WriteLine($"  bodies={sim.World.Bodies.Count} joints={sim.JointDefs.Count} fingers={sim.FingerBodies.Count} objects={sim.Objects.Count} gap={sim.FingerBaseGap:F4} kinematic={sim.Options.KinematicArm}")
        Check(sim.JointDefs.Count = 6, "6 joint definitions")
        Check(sim.FingerBodies.Count = 2, "2 gripper fingers created")
        Check(sim.Objects.Count = 12, "12 default blocks spawned")

        For i As Integer = 1 To 240
            sim.StepOnce()
        Next

        Dim hold As StateSnapshot = sim.CaptureState()

        Console.WriteLine($"  hold: q=({String.Join(", ", hold.Q.Select(Function(v) v.ToString("F3")))}) tcp=({hold.Tcp(0):F3}, {hold.Tcp(1):F3}, {hold.Tcp(2):F3})")
        Check(hold.Q.All(Function(v) Math.Abs(v) < 0.01), "kinematic arm holds the zero pose exactly")

        Dim command As MotionCommand = hardware.MoveJ({0.3, -0.2, 0.4, 0.0, 0.4, 0.0}, 0.6)

        Console.WriteLine($"  movej id={command.Id} duration={command.Duration:F2} failed={command.Failed}")

        For i As Integer = 1 To 480
            hardware.Tick()
        Next

        Dim moved As StateSnapshot = hardware.State

        Console.WriteLine($"  after movej: q=({String.Join(", ", moved.Q.Select(Function(v) v.ToString("F3")))})")
        Console.WriteLine($"  tcp=({moved.Tcp(0):F3}, {moved.Tcp(1):F3}, {moved.Tcp(2):F3})")

        Check(Not command.Failed, "movej accepted")
        Check(hardware.MotionSucceeded(command.Id), "motion reported success")
        Check(Math.Abs(moved.Q(0) - 0.3) < 0.02 AndAlso Math.Abs(moved.Q(2) - 0.4) < 0.02, "joints reach the commanded angles")

        ' ---- 笛卡尔直线 ----
        Dim startPose As Pose3D = assembly.TcpPose(sim.JointPositions())
        Dim cart As MotionCommand = hardware.MoveL(startPose.Position.x - 0.08,
                                                   startPose.Position.y + 0.05,
                                                   startPose.Position.z + 0.04, 0.3)

        Console.WriteLine($"  movel id={cart.Id} failed={cart.Failed} msg={cart.Message}")

        For i As Integer = 1 To 480
            hardware.Tick()
        Next

        Dim afterCart As Pose3D = assembly.TcpPose(sim.JointPositions())

        Console.WriteLine($"  tcp after movel=({afterCart.Position.x:F3}, {afterCart.Position.y:F3}, {afterCart.Position.z:F3})")

        Check(Not cart.Failed, "movel accepted")
        Check(Math.Abs(afterCart.Position.x - (startPose.Position.x - 0.08)) < 0.012, "movel reaches the cartesian target")

        ' ---- 夹爪 ----
        sim.ReleaseGripper()
        Dim wide As Double = sim.GripperOpening

        sim.Grip(45.0)
        Dim narrow As Double = sim.GripperOpening

        Console.WriteLine($"  gripper wide={wide:F4} narrow={narrow:F4}")

        Check(wide > narrow + 0.01, "gripper closes with a force command")
    End Sub

    ''' <summary>单摆：静态基座 + 一根连杆 + 转轴关节 + 位置伺服，检查重力下垂量是否有限。</summary>
    Private Sub TestPendulum()
        Console.WriteLine("== pendulum servo ==")

        Dim world As PhysicsWorld3D = NewWorld()
        Dim anchor As RigidBody3D = PhysicsWorld3D.StaticBox(0.1, 0.1, 0.1, New Vector3(0, 0, 0.5))
        Dim link As RigidBody3D = PhysicsWorld3D.Box(0.4, 0.05, 0.05, 1.0)

        link.Position = New Vector3(0.2, 0, 0.5)
        link.Label = "link"
        world.Add(anchor)
        world.Add(link)

        Dim joint As New RevoluteJoint3D(anchor, link,
                                         New Vector3(0, 0, 0.5), New Vector3(-0.2, 0, 0),
                                         New Vector3(0, 1, 0), New Vector3(0, 1, 0),
                                         -1.5, 1.5, "j1")
        world.Add(joint)

        Dim servo As New JointServo3D(joint, ServoMode3D.Position) With {
            .Target = 0.0,
            .Stiffness = 40.0,
            .Damping = 25.0,
            .MaxForce = 50.0,
            .MaxVelocity = 2.0,
            .Tolerance = 0.02
        }
        world.Add(servo)

        For i As Integer = 1 To 240
            world.Step(world.FixedDt)
        Next

        Console.WriteLine($"  angle={joint.Position:F4} rad  speed={joint.Speed:F4}  force={servo.LastForce:F3}  z={link.Position.z:F4}")
        Check(Math.Abs(joint.Position) < 0.15, "servo holds the link within 0.15 rad under gravity")
    End Sub

    ''' <summary>限位：目标超过上限时关节应停在行程内。</summary>
    Private Sub TestLimit()
        Console.WriteLine("== revolute limit ==")

        Dim world As PhysicsWorld3D = NewWorld()
        Dim anchor As RigidBody3D = PhysicsWorld3D.StaticBox(0.1, 0.1, 0.1, New Vector3(0, 0, 0.5))
        Dim link As RigidBody3D = PhysicsWorld3D.Box(0.4, 0.05, 0.05, 1.0)

        link.Position = New Vector3(0.2, 0, 0.5)
        world.Add(anchor)
        world.Add(link)

        Dim joint As New RevoluteJoint3D(anchor, link,
                                         New Vector3(0, 0, 0.5), New Vector3(-0.2, 0, 0),
                                         New Vector3(0, 1, 0), New Vector3(0, 1, 0),
                                         -0.5, 0.5, "j1")
        world.Add(joint)

        Dim servo As New JointServo3D(joint, ServoMode3D.Position) With {
            .Target = 1.5,
            .Stiffness = 40.0,
            .Damping = 25.0,
            .MaxForce = 50.0,
            .MaxVelocity = 3.0
        }
        world.Add(servo)

        For i As Integer = 1 To 240
            world.Step(world.FixedDt)
        Next

        Console.WriteLine($"  angle={joint.Position:F4} rad  limitError={joint.LimitError:F4}")
        Check(joint.Position <= 0.56, "joint stops at the upper limit")
    End Sub

    ''' <summary>夹持：两个力控手指夹一块积木，力足够时夹住、力不足时滑落。</summary>
    Private Sub TestGrip(force As Double, tag As String)
        Console.WriteLine($"== gripper {tag} (force={force:N1} N) ==")

        Dim world As PhysicsWorld3D = NewWorld()
        Dim palm As RigidBody3D = PhysicsWorld3D.StaticBox(0.4, 0.12, 0.12, New Vector3(0, 0, 1.0))

        palm.CollisionMask = 0
        palm.Label = "palm"
        world.Add(palm)

        For side As Integer = -1 To 1 Step 2
            Dim finger As RigidBody3D = PhysicsWorld3D.Box(0.012, 0.06, 0.06, 0.05, New PhysicsMaterial(0.8, 0.0))

            finger.Position = New Vector3(side * 0.06, 0, 1.0)
            finger.MaxSpeed = 0.4
            finger.Label = "finger" & side
            world.Add(finger)

            Dim inward As New Vector3(-side, 0, 0)
            Dim joint As New PrismaticJoint3D(palm, finger,
                                               New Vector3(side * 0.06, 0, 0), New Vector3(0, 0, 0),
                                               inward, -0.05, 0.05, "grip" & side)
            world.Add(joint)

            Dim servo As New JointServo3D(joint, ServoMode3D.Force) With {
                .Target = force,
                .MaxForce = force * 1.5,
                .ForceDamping = 30.0
            }
            world.Add(servo)
        Next

        Dim block As RigidBody3D = PhysicsWorld3D.Box(0.09, 0.09, 0.09, 0.5, New PhysicsMaterial(0.8, 0.0))

        block.Position = New Vector3(0, 0, 1.0)
        block.Label = "block"
        world.Add(block)

        world.Add(PhysicsWorld3D.StaticPlane(New Vector3(0, 0, 1), 0.0, New PhysicsMaterial(0.7, 0.0)))

        For i As Integer = 1 To 360
            world.Step(world.FixedDt)
        Next

        Dim held As Boolean = block.Position.z > 0.9

        Console.WriteLine($"  block z={block.Position.z:F4}  vz={block.Velocity.z:F4}  held={held}")

        If force >= 40.0 Then
            Check(held, "strong grip keeps the block")
        Else
            Check(Not held, "weak grip lets the block slip out")
        End If
    End Sub

End Module
