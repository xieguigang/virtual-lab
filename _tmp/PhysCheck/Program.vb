Imports System
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.Joints3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

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

    Private Sub TestModel()
        Console.WriteLine("== model ==")

        Dim urdfPath As String = "G:\virtual-lab\models\six_axis_arm.urdf"
        Dim glbPath As String = "G:\virtual-lab\models\six_axis_arm.glb"
        Dim robot As VirtualWorkshop.Model.UrdfRobot = VirtualWorkshop.Model.UrdfRobot.Load(urdfPath)

        Console.WriteLine($"  links={robot.Links.Count} joints={robot.Joints.Count} movable={robot.MovableJoints.Count} root={robot.RootLink.Name}")
        Check(robot.Links.Count = 8, "8 links parsed")
        Check(robot.MovableJoints.Count = 6, "6 revolute joints parsed")

        Dim scene As VirtualWorkshop.Model.GlbScene =
            VirtualWorkshop.Model.GlbLinkLoader.Load(glbPath, robot)

        Console.WriteLine($"  triangles={scene.TriangleCount} landscape={scene.LandscapeTriangleCount} nodes={scene.NodeCount} meshes={scene.MeshCount}")

        For Each name As String In scene.MeshNames()
            Dim mesh = scene.GetMesh(name)
            Console.WriteLine($"    {name}: {mesh.TriangleCount} tris  bounds=({mesh.BoundsMin.X:F3},{mesh.BoundsMin.Y:F3},{mesh.BoundsMin.Z:F3})..({mesh.BoundsMax.X:F3},{mesh.BoundsMax.Y:F3},{mesh.BoundsMax.Z:F3})")
        Next

        Check(scene.TriangleCount = 2704, "2704 triangles")
        Check(scene.TriangleCount = scene.LandscapeTriangleCount, "Landscape agrees on triangle count")

        Dim assembly = VirtualWorkshop.Model.ArmAssembly.Build(robot, scene)
        Console.WriteLine("  " & assembly.Describe())
        Check(assembly.JointCount = 6, "6 joint definitions")

        Dim zeroQ As Double() = {0, 0, 0, 0, 0, 0}
        Dim zero = assembly.TcpPose(zeroQ)
        Console.WriteLine($"  TCP zero = ({zero.Position.x:F4}, {zero.Position.y:F4}, {zero.Position.z:F4})")
        Check(Math.Abs(zero.Position.x - 0.617) < 0.001 AndAlso Math.Abs(zero.Position.z - 0.178) < 0.001,
              "TCP position at zero pose matches the URDF chain")

        Dim target As New VirtualWorkshop.Model.Pose3D(New Vector3(0.45, 0.1, 0.3), zero.Rotation)
        Dim ik = VirtualWorkshop.Model.Kinematics.Solve(assembly, target, zeroQ)

        Console.WriteLine($"  IK success={ik.Success} posErr={ik.PositionError:F6} rotErr={ik.RotationError:F6} iters={ik.Iterations}")
        Console.WriteLine($"     q={String.Join(", ", ik.Q.Select(Function(v) v.ToString("F3")))}")

        Dim verify = assembly.TcpPose(ik.Q)
        Dim gap As Double = Vector3Math.Length(verify.Position - target.Position)

        Check(ik.Success, "IK reports success for a reachable pose")
        Check(gap < 0.001, "IK result reproduces the target through FK")
    End Sub

    Private Function NewWorld() As PhysicsWorld3D
        Return New PhysicsWorld3D With {
            .Gravity = New Vector3(0, 0, -9.81),
            .Iterations = 20,
            .Substeps = 4,
            .FixedDt = 1.0 / 120.0,
            .AllowSleep = False
        }
    End Function

    Private Sub Check(ok As Boolean, message As String)
        If ok Then
            Console.WriteLine($"  [PASS] {message}")
        Else
            Console.WriteLine($"  [FAIL] {message}")
            failures += 1
        End If
    End Sub

    ''' <summary>单摆：静态基座 + 一根连杆 + 转轴关节 + 位置伺服，检查重力下垂量是否有限。</summary>
    Private Sub TestSimulation()
        Console.WriteLine("== simulation ==")

        Dim urdfPath As String = "G:\virtual-lab\models\six_axis_arm.urdf"
        Dim glbPath As String = "G:\virtual-lab\models\six_axis_arm.glb"
        Dim robot = VirtualWorkshop.Model.UrdfRobot.Load(urdfPath)
        Dim scene = VirtualWorkshop.Model.GlbLinkLoader.Load(glbPath, robot)
        Dim assembly = VirtualWorkshop.Model.ArmAssembly.Build(robot, scene)
        Dim sim As New VirtualWorkshop.Simulation.ArmSimulation(assembly, New VirtualWorkshop.Simulation.ArmSimulationOptions())

        Console.WriteLine($"  bodies={sim.World.Bodies.Count} joints={sim.RevoluteJoints.Count} fingers={sim.FingerBodies.Count} objects={sim.Objects.Count} gap={sim.FingerBaseGap:F4}")
        Check(sim.RevoluteJoints.Count = 6, "6 revolute joints created")
        Check(sim.FingerBodies.Count = 2, "2 gripper fingers created")
        Check(sim.Objects.Count = 12, "12 default blocks spawned")

        ' ---- 诊断 ----
        For Each item In sim.RevoluteJoints
            Console.WriteLine($"    joint {item.Label}: angle={item.Position:F6} speed={item.Speed:F6} axisErr={item.AxisError:E2} anchorErr={Vector3Math.Length(item.Point.AnchorError):E2}")
        Next

        For Each item In sim.JointServos
            Console.WriteLine($"    servo {item.Label}: kp={item.Stiffness:F1} kd={item.Damping:F3} fmax={item.MaxForce:F1} vmax={item.MaxVelocity:F2}")
        Next

        For Each value As Double In sim.JointInertias
            Console.WriteLine($"    inertia={value:E3}")
        Next

        Dim names3 As String() = {"link4", "link5", "link6"}

        For Each name3 As String In names3
            Dim b = sim.LinkBodies(name3)
            Console.WriteLine($"    {name3}: m={b.Mass:F2} I=({b.InertiaLocal.m11:E2},{b.InertiaLocal.m22:E2},{b.InertiaLocal.m33:E2}) pos=({b.Position.x:F3},{b.Position.y:F3},{b.Position.z:F3})")
        Next

        For Each bf In sim.FingerBodies
            Console.WriteLine($"    {bf.Label}: m={bf.Mass:F2} I=({bf.InertiaLocal.m11:E2},{bf.InertiaLocal.m22:E2},{bf.InertiaLocal.m33:E2}) pos=({bf.Position.x:F3},{bf.Position.y:F3},{bf.Position.z:F3})")
        Next

        Console.WriteLine("  -- diagnostics: servos disabled --")

        For Each servo In sim.JointServos
            servo.Mode = Microsoft.VisualBasic.Engine.Physics.Joints3D.ServoMode3D.Disabled
        Next

        sim.StepOnce()
        Console.WriteLine($"    free step1: q4={sim.RevoluteJoints(3).Position:F4} v4={sim.RevoluteJoints(3).Speed:F4} v2={sim.RevoluteJoints(1).Speed:F4} anchorErr4={Vector3Math.Length(sim.RevoluteJoints(3).Point.AnchorError):E3}")

        For Each servo In sim.JointServos
            servo.Mode = Microsoft.VisualBasic.Engine.Physics.Joints3D.ServoMode3D.Position
        Next

        Call sim.Reset()

        For i As Integer = 1 To 240
            sim.StepOnce()

            If i <= 20 Then
                Console.WriteLine($"    step {i,3} q4={sim.RevoluteJoints(3).Position:F4} v4={sim.RevoluteJoints(3).Speed:F4} f4={sim.JointServos(3).LastForce:F2} q6={sim.RevoluteJoints(5).Position:F4} f6={sim.JointServos(5).LastForce:F2} q2={sim.RevoluteJoints(1).Position:F4}")
            End If

            If i Mod 60 = 0 Then
                Dim part = String.Join(", ", sim.JointPositions().Select(Function(v) v.ToString("F3")))
                Dim forces = String.Join(", ", sim.JointServos.Select(Function(s) s.LastForce.ToString("F1")))
                Console.WriteLine($"    t={sim.Time:F2} q=({part}) tau=({forces}) col={String.Join(";", sim.LastCollisions)}")
            End If
        Next

        Dim state = sim.CaptureState()
        Console.WriteLine($"  hold: q=({String.Join(", ", state.Q.Select(Function(v) v.ToString("F3")))})")
        Console.WriteLine($"  tcp=({state.Tcp(0):F3}, {state.Tcp(1):F3}, {state.Tcp(2):F3})  collisions={state.Collisions.Count}")
        Check(state.Q.All(Function(v) Math.Abs(v) < 0.12), "arm holds the zero pose under gravity")

        ' ---- 直接给 j1 施加恒定扭矩，验证关节能否转动 ----
        For Each servo In sim.JointServos
            servo.Mode = Microsoft.VisualBasic.Engine.Physics.Joints3D.ServoMode3D.Disabled
        Next

        For k As Integer = 1 To 120
            sim.RevoluteJoints(0).ApplyControlForce(20.0)
            sim.StepOnce()
        Next

        Console.WriteLine($"  forced j1: angle={sim.RevoluteJoints(0).Position:F4} speed={sim.RevoluteJoints(0).Speed:F4}")

        sim.LinkBodies("link1").AngularVelocity = New Vector3(0, 0, 5)

        For k As Integer = 1 To 10
            sim.StepOnce()
        Next

        Console.WriteLine($"  yaw omega after 10 steps: {sim.LinkBodies("link1").AngularVelocity.z:F3} angle={sim.RevoluteJoints(0).Position:F4}")

        ' 只保留 j1 一个约束，排除其它关节/手指的耦合
        While sim.World.Constraints.Count > 1
            sim.World.Constraints.RemoveAt(sim.World.Constraints.Count - 1)
        End While

        sim.LinkBodies("link1").AngularVelocity = New Vector3(0, 0, 5)

        For k As Integer = 1 To 10
            sim.StepOnce()
        Next

        Console.WriteLine($"  yaw omega (only j1): {sim.LinkBodies("link1").AngularVelocity.z:F3} angle={sim.RevoluteJoints(0).Position:F4}")

        For n As Integer = 1 To 6
            Call sim.Reset()

            Dim keep As New HashSet(Of Object)()

            For k As Integer = 0 To n - 1
                keep.Add(sim.RevoluteJoints(k))
            Next

            For k As Integer = sim.World.Constraints.Count - 1 To 0 Step -1
                If Not keep.Contains(sim.World.Constraints(k)) Then
                    sim.World.Constraints.RemoveAt(k)
                End If
            Next

            For Each servo In sim.JointServos
                servo.Mode = Microsoft.VisualBasic.Engine.Physics.Joints3D.ServoMode3D.Disabled
            Next

            sim.LinkBodies("link1").AngularVelocity = New Vector3(0, 0, 5)

            For k As Integer = 1 To 10
                sim.StepOnce()
            Next

            Console.WriteLine($"  joints={n} omega={sim.LinkBodies("link1").AngularVelocity.z:F3}")
        Next

        For Each servo In sim.JointServos
            servo.Mode = Microsoft.VisualBasic.Engine.Physics.Joints3D.ServoMode3D.Position
        Next

        Call sim.Reset()
        Call sim.SetServoEnabled(True)

        Dim hardware As New VirtualWorkshop.Simulation.ArmHardware(sim)
        Dim command = hardware.MoveJ({0.3, -0.2, 0.4, 0.0, 0.4, 0.0}, 0.6)

        Console.WriteLine($"  movej id={command.Id} duration={command.Duration:F2} failed={command.Failed} msg={command.Message}")

        For i As Integer = 1 To 480
            hardware.Tick()

            If i Mod 120 = 0 Then
                Dim act = hardware.ActiveMotion
                Console.WriteLine($"    tick {i} t={sim.Time:F2} active={act IsNot Nothing}")
                Console.WriteLine($"      targets=({String.Join(", ", sim.JointServos.Select(Function(s) s.Target.ToString("F2")))})")
                Console.WriteLine($"      pos=({String.Join(", ", sim.RevoluteJoints.Select(Function(j) (j.Position).ToString("F2")))})")
                Console.WriteLine($"      spd=({String.Join(", ", sim.RevoluteJoints.Select(Function(j) (j.Speed).ToString("F2")))})")
                Console.WriteLine($"      frc=({String.Join(", ", sim.JointServos.Select(Function(s) (s.LastForce).ToString("F1")))})")
            End If
        Next

        Dim moved = hardware.State
        Console.WriteLine($"  after: q=({String.Join(", ", moved.Q.Select(Function(v) v.ToString("F3")))})")
        Console.WriteLine($"  tcp=({moved.Tcp(0):F3}, {moved.Tcp(1):F3}, {moved.Tcp(2):F3})")

        Check(Not command.Failed, "movej accepted")
        Check(hardware.MotionSucceeded(command.Id), "motion reported success")
        Check(Math.Abs(moved.Q(0) - 0.3) < 0.12 AndAlso Math.Abs(moved.Q(2) - 0.4) < 0.15, "joints reach the commanded angles")
    End Sub

    Private Sub TestPendulum()
        Console.WriteLine("== pendulum servo ==")

        Dim w = NewWorld()
        Dim anchor = PhysicsWorld3D.StaticBox(0.1, 0.1, 0.1, New Vector3(0, 0, 0.5))
        Dim link = PhysicsWorld3D.Box(0.4, 0.05, 0.05, 1.0)
        link.Position = New Vector3(0.2, 0, 0.5)
        link.Label = "link"
        w.Add(anchor)
        w.Add(link)

        Dim joint As New RevoluteJoint3D(anchor, link,
                                         New Vector3(0, 0, 0.5), New Vector3(-0.2, 0, 0),
                                         New Vector3(0, 1, 0), New Vector3(0, 1, 0),
                                         -1.5, 1.5, "j1")
        w.Add(joint)

        Dim servo As New JointServo3D(joint, ServoMode3D.Position) With {
            .Target = 0.0,
            .Stiffness = 25.0,
            .Damping = 6.0,
            .MaxForce = 50.0,
            .MaxVelocity = 2.0,
            .Tolerance = 0.02
        }
        w.Add(servo)

        For i As Integer = 1 To 240
            w.Step(w.FixedDt)
        Next

        Console.WriteLine($"  angle={joint.Position:F4} rad  speed={joint.Speed:F4}  force={servo.LastForce:F3}  z={link.Position.z:F4}")
        Check(Math.Abs(joint.Position) < 0.15, "servo holds the link within 0.15 rad under gravity")
    End Sub

    ''' <summary>限位：目标超过上限时关节应停在行程内。</summary>
    Private Sub TestLimit()
        Console.WriteLine("== revolute limit ==")

        Dim w = NewWorld()
        Dim anchor = PhysicsWorld3D.StaticBox(0.1, 0.1, 0.1, New Vector3(0, 0, 0.5))
        Dim link = PhysicsWorld3D.Box(0.4, 0.05, 0.05, 1.0)
        link.Position = New Vector3(0.2, 0, 0.5)
        w.Add(anchor)
        w.Add(link)

        Dim joint As New RevoluteJoint3D(anchor, link,
                                         New Vector3(0, 0, 0.5), New Vector3(-0.2, 0, 0),
                                         New Vector3(0, 1, 0), New Vector3(0, 1, 0),
                                         -0.5, 0.5, "j1")
        w.Add(joint)

        Dim servo As New JointServo3D(joint, ServoMode3D.Position) With {
            .Target = 1.5,
            .Stiffness = 30.0,
            .Damping = 6.0,
            .MaxForce = 50.0,
            .MaxVelocity = 3.0
        }
        w.Add(servo)

        For i As Integer = 1 To 240
            w.Step(w.FixedDt)
        Next

        Console.WriteLine($"  angle={joint.Position:F4} rad  limitError={joint.LimitError:F4}")
        Check(joint.Position <= 0.56, "joint stops at the upper limit")
    End Sub

    ''' <summary>夹持：两个力控手指夹一块积木，力足够时夹住、力不足时滑落。</summary>
    Private Sub TestGrip(force As Double, tag As String)
        Console.WriteLine($"== gripper {tag} (force={force:N1} N) ==")

        Dim w = NewWorld()
        Dim palm = PhysicsWorld3D.StaticBox(0.4, 0.12, 0.12, New Vector3(0, 0, 1.0))
        palm.CollisionMask = 0
        palm.Label = "palm"
        w.Add(palm)

        For side As Integer = -1 To 1 Step 2
            Dim finger = PhysicsWorld3D.Box(0.012, 0.06, 0.06, 0.05, New PhysicsMaterial(0.8, 0.0))
            finger.Position = New Vector3(side * 0.06, 0, 1.0)
            finger.MaxSpeed = 0.4
            finger.Label = "finger" & side
            w.Add(finger)

            Dim inward As New Vector3(-side, 0, 0)
            Dim joint As New PrismaticJoint3D(palm, finger,
                                               New Vector3(side * 0.06, 0, 0), New Vector3(0, 0, 0),
                                               inward, -0.05, 0.05, "grip" & side)
            w.Add(joint)

            Dim servo As New JointServo3D(joint, ServoMode3D.Force) With {
                .Target = force,
                .MaxForce = force * 1.5,
                .ForceDamping = 30.0
            }
            w.Add(servo)
        Next

        Dim block = PhysicsWorld3D.Box(0.09, 0.09, 0.09, 0.5, New PhysicsMaterial(0.8, 0.0))
        block.Position = New Vector3(0, 0, 1.0)
        block.Label = "block"
        w.Add(block)

        w.Add(PhysicsWorld3D.StaticPlane(New Vector3(0, 0, 1), 0.0, New PhysicsMaterial(0.7, 0.0)))

        For i As Integer = 1 To 360
            w.Step(w.FixedDt)
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
