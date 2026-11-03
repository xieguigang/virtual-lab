Imports System.Numerics
Imports MicroduckLab.Assets
Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports Collision3D = Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D.Math3D

Namespace Core

    ''' <summary>
    ''' The static geometry of the play area: the floor plane, the four low walls
    ''' of the square arena and, optionally, the three cosine bumps of the relief
    ''' prototype.
    ''' </summary>
    ''' <remarks>
    ''' The browser build injected the same bodies into the MJCF text and let the
    ''' MuJoCo compiler build them. They are created here instead, from the very
    ''' same numbers (ARENA_HALF, ARENA_WALL_H, ARENA_WALL_T, RELIEF_BUMPS).
    ''' </remarks>
    Public Class Arena : Implements IDisposable

        Public ReadOnly Property Floor As Eng.RigidBody3D
        Public ReadOnly Property Walls As New List(Of Eng.RigidBody3D)()
        Public ReadOnly Property Bumps As New List(Of Eng.RigidBody3D)()

        ''' <summary>whether the relief bumps are part of the world</summary>
        Public Property ReliefEnabled As Boolean = False

        Public Sub New(world As Eng.PhysicsWorld3D, tuning As PhysicsTuning)
            Dim material As New Eng.PhysicsMaterial With {
                .Friction = tuning.EnvironmentFriction,
                .Restitution = 0.0
            }

            ' the floor of the MJCF is a plane of z = 0
            Me.Floor = Eng.PhysicsWorld3D.StaticPlane(Phys.Vec(0, 0, 1), 0, material)
            Call Configure(Me.Floor)

            Call world.Add(Me.Floor)
            Call CreateWalls(world, material)
        End Sub

        Private Sub Configure(body As Eng.RigidBody3D)
            body.CollisionGroup = Groups.Environment
            body.CollisionMask = Groups.Duck Or Groups.Ball
            body.AngularDamping = 0
            body.LinearDamping = 0
        End Sub

        Private Sub CreateWalls(world As Eng.PhysicsWorld3D, material As Eng.PhysicsMaterial)
            Dim half As Double = SimConstants.ArenaHalf
            Dim height As Double = SimConstants.ArenaWallHeight
            Dim thickness As Double = SimConstants.ArenaWallThickness
            Dim span As Double = 2 * half + 2 * thickness
            Dim center As Double = half + thickness / 2

            ' the walls sit on the floor, so their center is half their height up
            Dim z As Double = height / 2

            Call AddWall(world, material, New Eng.Vector3(span, thickness, height), New Eng.Vector3(0, center, z))
            Call AddWall(world, material, New Eng.Vector3(span, thickness, height), New Eng.Vector3(0, -center, z))
            Call AddWall(world, material, New Eng.Vector3(thickness, span, height), New Eng.Vector3(center, 0, z))
            Call AddWall(world, material, New Eng.Vector3(thickness, span, height), New Eng.Vector3(-center, 0, z))
        End Sub

        Private Sub AddWall(world As Eng.PhysicsWorld3D, material As Eng.PhysicsMaterial,
                            size As Eng.Vector3, position As Eng.Vector3)
            Dim body As Eng.RigidBody3D = Eng.PhysicsWorld3D.StaticBox(size.x, size.y, size.z, position, material)

            Call Configure(body)
            Call world.Add(body)
            Call Walls.Add(body)
        End Sub

        ''' <summary>
        ''' add or remove the cosine bumps of the relief prototype
        ''' </summary>
        ''' <param name="world">the world that owns the bodies</param>
        ''' <param name="enabled">false removes the bumps again</param>
        Public Sub SetRelief(world As Eng.PhysicsWorld3D, enabled As Boolean)
            If enabled = ReliefEnabled Then
                Return
            End If

            ReliefEnabled = enabled

            If enabled Then
                Dim material As New Eng.PhysicsMaterial With {.Friction = 0.7, .Restitution = 0}

                For Each bump As Double() In SimConstants.ReliefBumps
                    Dim mesh As Collision3D.ConvexMesh3D = BumpMesh(bump)

                    If mesh Is Nothing Then
                        Continue For
                    End If

                    Dim body As Eng.RigidBody3D = Eng.PhysicsWorld3D.StaticConvex(
                        mesh, Phys.Vec(bump(0), bump(1), -SimConstants.ReliefSink), material)

                    Call Configure(body)
                    Call world.Add(body)
                    Call Bumps.Add(body)
                Next
            Else
                For Each body As Eng.RigidBody3D In Bumps
                    Call world.Remove(body)
                Next

                Call Bumps.Clear()
            End If
        End Sub

        ''' <summary>
        ''' the convex cap of one cosine bump: the analytic surface is sampled on
        ''' a disc and the hull of the samples is the collision proxy
        ''' </summary>
        Private Shared Function BumpMesh(bump As Double()) As Collision3D.ConvexMesh3D
            Dim radius As Double = bump(3)
            Dim height As Double = bump(2)
            Dim points As New List(Of Vector3)()

            ' the analytic surface, sampled on concentric rings
            For ring As Integer = 0 To 4
                Dim r As Double = radius * ring / 4.0

                For tick As Integer = 0 To 15
                    Dim angle As Double = 2 * Math.PI * tick / 16
                    Dim x As Double = r * Math.Cos(angle)
                    Dim y As Double = r * Math.Sin(angle)
                    Dim wave As Double = 0.5 * (1 + Math.Cos(Math.PI * r / radius))

                    ' the hull is computed in the local frame of the bump, which
                    ' sits on the arena floor
                    Call points.Add(New Vector3(CSng(x), CSng(y), CSng(height * wave)))
                Next
            Next

            Try
                Dim hull As ConvexHullResult = ConvexHull.Compute(points.ToArray())

                Return New Collision3D.ConvexMesh3D(Phys.Vertices(hull.Vertices), hull.Faces)
            Catch ex As Exception
                Return Nothing
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Call Walls.Clear()
            Call Bumps.Clear()
        End Sub
    End Class

    ''' <summary>
    ''' The kickable ball: a free rigid body that is parked far outside the arena
    ''' while it is not in play.
    ''' </summary>
    ''' <remarks>
    ''' The MJCF gave the ball a free joint and ``condim 6`` (rolling and spinning
    ''' friction). The engine has neither a free joint nor a six dimensional
    ''' contact, so the body is a plain dynamic sphere and the rolling resistance
    ''' is applied through the angular damping of the body.
    ''' </remarks>
    Public Class BallBody

        Public ReadOnly Property Body As Eng.RigidBody3D
        Public Property Active As Boolean = False

        Public Sub New(world As Eng.PhysicsWorld3D, tuning As PhysicsTuning)
            Dim material As New Eng.PhysicsMaterial With {
                .Friction = SimConstants.BallFriction,
                .Restitution = SimConstants.BallRestitution
            }

            Me.Body = Eng.PhysicsWorld3D.Sphere(SimConstants.BallRadius, SimConstants.BallMass, material)
            Me.Body.Label = "ball"
            Me.Body.CollisionGroup = Groups.Ball
            Me.Body.CollisionMask = Groups.Duck Or Groups.Environment
            ' the rolling resistance of the rolling friction of the MJCF contact
            Me.Body.AngularDamping = 0.35
            Me.Body.LinearDamping = 0.0
            Me.Body.MaxAngularSpeed = 0

            Call Park()

            Call world.Add(Me.Body)
        End Sub

        ''' <summary>the pose of the ball as a render matrix</summary>
        Public ReadOnly Property World As Matrix4x4
            Get
                Dim position As Vector3 = Phys.Vec(Body.Position)

                Return Matrix4x4.CreateFromQuaternion(Phys.Quat(Body.Orientation)) *
                       Matrix4x4.CreateTranslation(position)
            End Get
        End Property

        Public ReadOnly Property Position As Vector3
            Get
                Return Phys.Vec(Body.Position)
            End Get
        End Property

        Public ReadOnly Property Speed As Double
            Get
                Dim v As Eng.Vector3 = Body.Velocity

                Return Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z)
            End Get
        End Property

        ''' <summary>
        ''' park the ball far outside of the arena, where nothing can reach it
        ''' </summary>
        Public Sub Park()
            Active = False
            Body.Position = Phys.Vec(SimConstants.BallParkX, SimConstants.BallParkY, SimConstants.BallParkZ)
            Body.Orientation = Phys.Quat(Quaternion.Identity)
            Body.Velocity = Phys.Vec(0, 0, 0)
            Body.AngularVelocity = Phys.Vec(0, 0, 0)
        End Sub

        ''' <summary>
        ''' drop the ball next to the duck, on the side that it faces, with a
        ''' little random heading
        ''' </summary>
        Public Sub Spawn(near As Vector3, heading As Double, random As Random)
            Dim distance As Double = 0.55
            Dim angle As Double = heading + (random.NextDouble() - 0.5) * 0.7
            Dim x As Double = near.X + distance * Math.Cos(angle)
            Dim y As Double = near.Y + distance * Math.Sin(angle)
            Dim half As Double = SimConstants.ArenaHalf - 0.12

            x = Math.Max(-half, Math.Min(half, x))
            y = Math.Max(-half, Math.Min(half, y))

            Active = True
            Body.Position = Phys.Vec(x, y, SimConstants.SpawnZ)
            Body.Orientation = Phys.Quat(Quaternion.Identity)
            Body.Velocity = Phys.Vec(0, 0, 0)
            Body.AngularVelocity = Phys.Vec(0, 0, 0)
        End Sub
    End Class
End Namespace
