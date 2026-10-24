Imports System
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.Joints3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Module Program

    Private failures As Integer = 0

    Sub Main(args As String())
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
