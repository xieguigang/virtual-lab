Imports System.Diagnostics
Imports System.Numerics
Imports System.Threading
Imports MicroduckLab.Assets
Imports Eng = Microsoft.VisualBasic.Engine.Physics

Namespace Core

    ''' <summary>
    ''' The whole simulation: the assets, the two worlds (legs and rollers), the
    ''' nine policies, the mode machine and the fifty hertz control loop.
    ''' </summary>
    ''' <remarks>
    ''' Everything that the browser build kept in ``game.js`` lives here. The loop
    ''' runs on a thread of its own and publishes one <see cref="PoseSnapshot"/> per
    ''' control step; the window only ever reads that snapshot, so neither the
    ''' inference nor the solver can stall the renderer, and the renderer can not
    ''' tear a step in half.
    ''' </remarks>
    Public Class SimSession : Implements IDisposable

        Public ReadOnly Property Meshes As New MeshLibrary()
        Public ReadOnly Property Policies As New PolicyLibrary()
        Public ReadOnly Property Machine As New GameModeMachine()
        Public ReadOnly Property Tuning As New PhysicsTuning()

        Public Property Skeleton As DuckSkeleton
        Public Property Duck As DuckPhysics
        Public Property Arena As Arena
        Public Property Ball As BallBody
        Public Property Observer As ObservationBuilder

        ''' <summary>the shape that is loaded right now</summary>
        Public Property Shape As Locomotion = Locomotion.Legs

        ' ── the machine state ───────────────────────────────────────────────
        Private ReadOnly m_lastAction As Double() = New Double(SimConstants.NumJoints - 1) {}
        Private ReadOnly m_targets As Double() = New Double(SimConstants.NumJoints - 1) {}
        Private ReadOnly m_command As Double() = New Double(SimConstants.CmdSize - 1) {}
        Private ReadOnly m_headTarget As Double() = New Double(3) {}
        Private ReadOnly m_headSmooth As Double() = New Double(3) {}
        Private ReadOnly m_random As New Random(20261103)

        Private m_forward As Double
        Private m_turn As Double
        Private m_jaw As Double
        Private m_jawSmooth As Double

        ' ── the control loop ────────────────────────────────────────────────
        Private ReadOnly m_sync As New Object()
        Private m_thread As Thread
        Private m_running As Boolean
        Private ReadOnly m_published As New PoseSnapshot()
        Private ReadOnly m_working As New PoseSnapshot()
        Private m_booted As Boolean
        Private m_steps As Long
        Private m_ctrlHz As Double
        Private m_lastStepMs As Double
        Private m_lastInferenceMs As Double
        Private m_revision As Long

        ' ── the audio heuristics of the browser build ───────────────────────
        Private ReadOnly m_footAir As Boolean() = {False, False}
        Private ReadOnly m_footHeight As Double() = {0, 0}
        Private ReadOnly m_footAt As Long() = {0, 0}
        Private ReadOnly m_ballPrev As Double() = {0, 0, 0}
        Private m_ballPrevValid As Boolean
        Private m_ballAt As Long
        Private ReadOnly m_bumpPrev As Double() = {0, 0, 0}
        Private m_bumpPrevValid As Boolean
        Private m_bumpAt As Long
        Private ReadOnly m_clock As Stopwatch = Stopwatch.StartNew()

        ''' <summary>a line for the boot console</summary>
        Public Event BootTrace(line As String)
        ''' <summary>a footstep, the strength is in [0,1]</summary>
        Public Event Footstep(left As Boolean, strength As Single)
        ''' <summary>a ball impact, the strength is in [0,1]</summary>
        Public Event Impact(strength As Single)
        ''' <summary>the trunk hit a wall, the strength is in [0,1]</summary>
        Public Event Bump(strength As Single)
        ''' <summary>the duck was put back on its feet</summary>
        Public Event Respawned()
        ''' <summary>the mode or the shape changed</summary>
        Public Event ModeChanged()
        ''' <summary>a quack was requested</summary>
        Public Event Quacked()

        Public ReadOnly Property IsRunning As Boolean
            Get
                Return m_running
            End Get
        End Property

        Public ReadOnly Property Booted As Boolean
            Get
                Return m_booted
            End Get
        End Property

        ' /********************************************************************************/
        '   boot
        ' /********************************************************************************/

        ''' <summary>
        ''' load the assets, build the world and load the legs policies
        ''' </summary>
        Public Sub Boot()
            Call Trace("reading the robot rig")

            Dim kinetics As KinematicsDocument = KinematicsDocument.Load(AssetPath.RobotFile(KinematicsFile(Shape)))
            Dim model As MjcfDocument = MjcfDocument.Load(AssetPath.RobotFile(MjcfFile(Shape)))

            Call Trace($"kinematics: {kinetics.Bodies.Count} links, MJCF: {model.Bodies.Count} bodies, {model.Actuators.Count} actuators")

            Machine.Loco = Shape
            Skeleton = DuckSkeleton.Build(Shape, kinetics, model)

            Call Trace($"skeleton: {Skeleton.Nodes.Count} links, {Skeleton.Driven.Count} hinges, {Skeleton.TotalMass * 1000:0.0} g")

            Call Trace("building the collision proxies")
            Call BuildWorld()

            Call Trace($"physics: {Duck.Links.Count} bodies, {Duck.ColliderCount} convex proxies, {Meshes.TriangleCount} triangles")

            Call Trace("loading the policies")
            Call Policies.LoadLegs(AddressOf Trace)

            Observer = New ObservationBuilder(Duck)

            m_booted = True

            Call Capture()
            Call ResetSim()
        End Sub

        Private Sub BuildWorld()
            Duck = New DuckPhysics(Skeleton, Meshes, Tuning)
            Arena = New Arena(Duck.World, Tuning)
            Ball = New BallBody(Duck.World, Tuning)
            Observer = New ObservationBuilder(Duck)
        End Sub

        Public Shared Function KinematicsFile(loco As Locomotion) As String
            Return If(loco = Locomotion.Rollers, "kinematics_rollers.json", "kinematics.json")
        End Function

        Public Shared Function MjcfFile(loco As Locomotion) As String
            Return If(loco = Locomotion.Rollers, "robot_allcollisions_rollers.xml", "robot_allcollisions.xml")
        End Function

        Private Sub Trace(line As String)
            RaiseEvent BootTrace(line)
        End Sub

        ' /********************************************************************************/
        '   the loop
        ' /********************************************************************************/

        Public Sub Start()
            If Not m_booted OrElse m_running Then
                Return
            End If

            m_running = True

            m_thread = New Thread(AddressOf LoopBody) With {
                .IsBackground = True,
                .Name = "microduck control",
                .Priority = ThreadPriority.AboveNormal
            }

            Call m_thread.Start()
        End Sub

        Public Sub [Stop]()
            m_running = False

            Dim thread As Thread = m_thread

            If thread IsNot Nothing AndAlso thread.IsAlive Then
                Call thread.Join(500)
            End If

            m_thread = Nothing
        End Sub

        Private Sub LoopBody()
            Dim nextAt As Double = 0
            Dim windowStart As Double = 0
            Dim windowSteps As Integer = 0

            While m_running
                Dim now As Double = m_clock.Elapsed.TotalSeconds

                If now < nextAt Then
                    Dim wait As Integer = CInt((nextAt - now) * 1000)

                    Thread.Sleep(If(wait > 2, 2, 1))

                    Continue While
                End If

                Dim started As Double = m_clock.Elapsed.TotalSeconds

                Try
                    Call ControlStep()
                Catch ex As Exception
                    ' a policy that throws must not kill the loop silently
                    Call Trace($"control step failed: {ex.GetType().Name}: {ex.Message}")
                    m_running = False

                    Exit While
                End Try

                m_lastStepMs = (m_clock.Elapsed.TotalSeconds - started) * 1000
                nextAt += SimConstants.CtrlDt

                ' never replay the backlog of a stalled frame: the browser build
                ' drops it as well
                If m_clock.Elapsed.TotalSeconds - nextAt > SimConstants.CtrlDt Then
                    nextAt = m_clock.Elapsed.TotalSeconds
                End If

                windowSteps += 1

                If m_clock.Elapsed.TotalSeconds - windowStart >= 0.5 Then
                    m_ctrlHz = windowSteps / (m_clock.Elapsed.TotalSeconds - windowStart)
                    windowStart = m_clock.Elapsed.TotalSeconds
                    windowSteps = 0
                End If
            End While
        End Sub

        ''' <summary>
        ''' one control step: inference, four physics sub steps, the mode machine
        ''' and the audio heuristics
        ''' </summary>
        Private Sub ControlStep()
            Dim machine As GameModeMachine = Me.Machine

            ' hold the last target while the duck settles after a fall
            Duck.Drive.Frozen = machine.Recovery = RecoveryState.Settle

            ' standing and walking want opposite things from the weight feed forward
            ' (bench: 244/250 standing against 245/250 walking, each with the other
            ' setting), so the stick decides which of the two this control step is
            Dim driving As Boolean = Math.Abs(m_forward) > 0.01 OrElse Math.Abs(m_turn) > 0.01

            Duck.Drive.GravityCompensation = If(driving, 1.0, 0.0)

            If Not Duck.Drive.Frozen Then
                Dim command As Double() = ComposeCommand()
                Dim observation As Double() = Observer.Build(m_lastAction, command)
                Dim session As PolicySession = Policies.Active(machine.Mode, Machine.Loco, machine.Recovery)

                If session IsNot Nothing Then
                    Dim action As Double() = session.Run(observation)
                    Dim finite As Boolean = action.Length >= SimConstants.NumJoints

                    If finite Then
                        For j As Integer = 0 To SimConstants.NumJoints - 1
                            If Double.IsNaN(action(j)) OrElse Double.IsInfinity(action(j)) Then
                                finite = False

                                Exit For
                            End If
                        Next
                    End If

                    If finite Then
                        For j As Integer = 0 To SimConstants.NumJoints - 1
                            m_lastAction(j) = action(j)
                            m_targets(j) = SimConstants.DefaultPose(j) + action(j) * Policies.ActionScale
                        Next
                    End If

                    m_lastInferenceMs = session.LastInferenceMs
                End If
            End If

            Call Duck.Drive.SetTargets(m_targets)
            Call Duck.Advance()

            m_steps += 1

            Dim gravityZ As Double = Duck.ProjectedGravityZ()
            Dim trunkHeight As Double = Duck.Trunk.FramePosition.Z
            Dim reset As Boolean = False

            ' the solver exploded
            If Double.IsNaN(trunkHeight) OrElse Double.IsNaN(gravityZ) Then
                Call ResetSim()

                Return
            End If

            If machine.Recovery <> RecoveryState.None Then
                reset = machine.AdvanceRecovery(gravityZ)
            Else
                ' a duck that crouches is not a duck that fell: the walk policy brings
                ' the trunk down to 11 mm while keeping the back level, and the old
                ' 20 mm floor declared that a fall, which froze the duck into the
                ' recovery state and made it ignore every stick input afterwards
                Dim fallen As Boolean = gravityZ > -0.5 OrElse trunkHeight < 0.005

                If fallen Then
                    If machine.CanRecover Then
                        machine.FallDebounce += 1

                        If machine.FallDebounce >= SimConstants.FallDebounceSteps Then
                            machine.FallDebounce = 0
                            machine.HeadMode = False
                            machine.Recovery = RecoveryState.Settle
                            machine.RecoverySteps = 0
                            machine.UprightSteps = 0
                            Call Array.Clear(m_lastAction, 0, m_lastAction.Length)
                            RaiseEvent ModeChanged()
                        End If
                    Else
                        machine.FallDebounce = 0

                        If machine.FallenFrames < 0 Then
                            machine.FallenFrames = 0
                        End If

                        machine.FallenFrames += 1

                        Dim grace As Integer = If(machine.Mode = GameMode.Roll,
                                                  SimConstants.RollFallGraceMs / 20,
                                                  SimConstants.FallGraceMs / 20)

                        reset = machine.FallenFrames > grace
                    End If
                Else
                    machine.FallDebounce = 0
                    machine.FallenFrames = -1
                End If
            End If

            ' the one shot counters and the respawn of a duck that a roll timed out
            If Not reset Then
                reset = machine.Advance(gravityZ)
            End If

            If reset Then
                Call ResetSim()

                Return
            End If

            ' a ball that left the arena through a solver glitch comes back
            If Ball.Active Then
                Dim position As Vector3 = Ball.Position

                If Math.Abs(position.X) > SimConstants.ArenaHalf + 0.1 OrElse
                   Math.Abs(position.Y) > SimConstants.ArenaHalf + 0.1 OrElse
                   position.Z < -0.5 Then
                    Call SpawnBall()
                End If
            End If

            Call StepAudio(trunkHeight)
            Call Capture()
        End Sub

        ''' <summary>
        ''' assemble the thirteen command words of this control step
        ''' </summary>
        Private Function ComposeCommand() As Double()
            Dim machine As GameModeMachine = Me.Machine

            Call Array.Clear(m_command, 0, m_command.Length)

            Select Case machine.Mode
                Case GameMode.SitStand
                    m_command(0) = machine.SitFlag
                Case GameMode.Crouch
                    If machine.CrouchPhase >= 0 Then
                        Dim phase As Double = 2 * Math.PI * machine.CrouchPhase

                        m_command(0) = Math.Cos(phase)
                        m_command(1) = Math.Sin(phase)
                    End If
                Case GameMode.GroundPick
                    If machine.PickPhase >= 0 Then
                        Dim phase As Double = 2 * Math.PI * machine.PickPhase

                        m_command(0) = Math.Cos(phase)
                        m_command(1) = Math.Sin(phase)
                    End If
                Case Else
                    If machine.AllowsTwist() Then
                        Dim limits As Double() = SimConstants.LimitsOf(machine.Loco)
                        Dim forward As Double = m_forward
                        Dim turn As Double = m_turn

                        m_command(0) = If(forward >= 0, forward * limits(0), forward * -limits(1))
                        m_command(2) = turn * limits(2)
                    End If
            End Select

            ' the head offsets are smoothed once per control step, like the
            ' runtime, and are zero padded while the pick or the recovery owns the
            ' duck (the trained policies expect an all zero command there)
            For h As Integer = 0 To 3
                m_headSmooth(h) += SimConstants.HeadAlpha * (m_headTarget(h) - m_headSmooth(h))
            Next

            Dim zeroPad As Boolean = machine.Mode = GameMode.GroundPick OrElse machine.Recovery <> RecoveryState.None

            For h As Integer = 0 To 3
                m_command(3 + h) = If(zeroPad, 0, m_headSmooth(h))
            Next

            Return m_command
        End Function

        ' /********************************************************************************/
        '   the audio heuristics
        ' /********************************************************************************/

        Private Sub StepAudio(trunkHeight As Double)
            Dim machine As GameModeMachine = Me.Machine
            Dim elapsed As Long = m_clock.ElapsedMilliseconds

            If machine.Loco = Locomotion.Legs AndAlso Not machine.InputLocked Then
                Dim left As BodyLink = Duck.LeftAnkle
                Dim right As BodyLink = Duck.RightAnkle

                If left IsNot Nothing AndAlso right IsNot Nothing Then
                    Dim zl As Double = left.FramePosition.Z
                    Dim zr As Double = right.FramePosition.Z
                    Dim ground As Double = Math.Min(zl, zr)

                    For foot As Integer = 0 To 1
                        Dim z As Double = If(foot = 0, zl, zr)
                        Dim relative As Double = z - ground
                        Dim vz As Double = (z - m_footHeight(foot)) / SimConstants.CtrlDt

                        m_footHeight(foot) = z

                        If relative > 0.012 Then
                            m_footAir(foot) = True
                        ElseIf m_footAir(foot) AndAlso relative < 0.005 AndAlso vz < -0.02 AndAlso
                               elapsed - m_footAt(foot) > 130 Then
                            m_footAir(foot) = False
                            m_footAt(foot) = elapsed

                            Dim strength As Single = CSng(Math.Min(1, Math.Abs(vz) / 0.35))

                            RaiseEvent Footstep(foot = 0, strength)
                        End If
                    Next
                End If
            End If

            ' the trunk hit something nearby
            If Not machine.InputLocked Then
                Dim velocity As Vector3 = Duck.TrunkVelocity()

                If m_bumpPrevValid Then
                    Dim dv As Double = (velocity - New Vector3(CSng(m_bumpPrev(0)), CSng(m_bumpPrev(1)), CSng(m_bumpPrev(2)))).Length()

                    If dv > 0.35 AndAlso elapsed - m_bumpAt > 300 AndAlso NearObstacle(Duck.Trunk.FramePosition) Then
                        m_bumpAt = elapsed
                        RaiseEvent Bump(CSng(Math.Min(1, (dv - 0.35) / 1.5)))
                    End If
                End If

                m_bumpPrev(0) = velocity.X
                m_bumpPrev(1) = velocity.Y
                m_bumpPrev(2) = velocity.Z
                m_bumpPrevValid = True
            Else
                m_bumpPrevValid = False
            End If

            ' the ball thumps on the velocity delta between two control steps
            If Ball.Active Then
                Dim body As Eng.RigidBody3D = Ball.Body
                Dim vx As Double = body.Velocity.x
                Dim vy As Double = body.Velocity.y
                Dim vz As Double = body.Velocity.z

                If m_ballPrevValid Then
                    Dim dx As Double = vx - m_ballPrev(0)
                    Dim dy As Double = vy - m_ballPrev(1)
                    Dim dz As Double = vz - m_ballPrev(2)
                    Dim dv As Double = Math.Sqrt(dx * dx + dy * dy + dz * dz)

                    If dv > 0.5 AndAlso elapsed - m_ballAt > 90 Then
                        m_ballAt = elapsed
                        RaiseEvent Impact(CSng(Math.Min(1, (dv - 0.5) / 3.5)))
                    End If
                End If

                m_ballPrev(0) = vx
                m_ballPrev(1) = vy
                m_ballPrev(2) = vz
                m_ballPrevValid = True
            Else
                m_ballPrevValid = False
            End If
        End Sub

        Private Shared Function NearObstacle(position As Vector3) As Boolean
            Return Math.Max(Math.Abs(position.X), Math.Abs(position.Y)) > SimConstants.ArenaHalf - 0.16
        End Function

        ' /********************************************************************************/
        '   the pose that the renderer reads
        ' /********************************************************************************/

        Private Sub Capture()
          SyncLock m_sync
            Dim working As PoseSnapshot = m_working

            Dim links As List(Of BodyLink) = Duck.Links

            For i As Integer = 0 To Math.Min(links.Count, PoseSnapshot.MaxLinks) - 1
                working.BodyWorld(i) = links(i).World
            Next

            working.BodyCount = links.Count

            For j As Integer = 0 To SimConstants.NumJoints - 1
                working.JointAngles(j) = Duck.JointAngle(j)
                working.JointSpeeds(j) = Duck.JointSpeed(j)
            Next

            m_jawSmooth += (m_jaw - m_jawSmooth) * 0.35

            working.JawOpen = CSng(m_jawSmooth)
            working.BallVisible = Ball.Active
            working.BallMatrix = Ball.World
            working.Mode = Machine.Mode
            working.Recovering = Machine.Recovery <> RecoveryState.None
            working.TrunkPosition = Duck.Trunk.FramePosition
            working.TrunkYaw = YawOf(Duck.Trunk.FrameRotation)

            Dim telemetry As New TelemetryFrame With {
                .ModeLabel = Machine.Label,
                .Mode = Machine.Mode,
                .Loco = Machine.Loco,
                .ControlHz = m_ctrlHz,
                .Speed = Duck.TrunkVelocity().Length(),
                .TurnRate = Duck.Trunk.Body.AngularVelocity.z,
                .TrunkHeight = Duck.Trunk.FramePosition.Z,
                .GravityZ = Duck.ProjectedGravityZ(),
                .PolicyName = PolicyLabel(),
                .InferenceMs = m_lastInferenceMs,
                .ControlStepMs = m_lastStepMs,
                .BallActive = Ball.Active,
                .HeadMode = Machine.HeadMode,
                .Recovering = Machine.Recovery <> RecoveryState.None,
                .Driving = Machine.AllowsTwist() AndAlso (Math.Abs(m_forward) > 0.01 OrElse Math.Abs(m_turn) > 0.01)
            }

            m_revision += 1

            working.Telemetry = telemetry
            working.Revision = m_revision

            m_published.CopyFrom(working)
          End SyncLock
        End Sub

        Private Function PolicyLabel() As String
            Dim session As PolicySession = Policies.Active(Machine.Mode, Machine.Loco, Machine.Recovery)

            Return If(session Is Nothing, "(none)", session.Name)
        End Function

        Private Shared Function YawOf(rotation As Quaternion) As Double
            Dim forward As Vector3 = Vector3.Transform(New Vector3(1, 0, 0), rotation)

            Return Math.Atan2(forward.Y, forward.X)
        End Function

        ''' <summary>
        ''' copy the last frame into the given snapshot, the renderer calls this
        ''' once per paint
        ''' </summary>
        Public Sub Snapshot(target As PoseSnapshot)
            SyncLock m_sync
                target.CopyFrom(m_published)
            End SyncLock
        End Sub

        ' /********************************************************************************/
        '   the intents of the ui
        ' /********************************************************************************/

        ''' <summary>the twist of the walking mode, both in [-1, 1]</summary>
        Public Sub SetTwist(forward As Double, turn As Double)
            m_forward = Phys.Clamp(forward, -1, 1)
            m_turn = Phys.Clamp(turn, -1, 1)
        End Sub

        ''' <summary>the four head offsets of the head mode, each in [-1, 1]</summary>
        Public Sub SetHead(neckPitch As Double, pitch As Double, yaw As Double, roll As Double)
            m_headTarget(0) = SimConstants.HeadSigns(0) * neckPitch * SimConstants.HeadMax
            m_headTarget(1) = SimConstants.HeadSigns(1) * pitch * SimConstants.HeadMax
            m_headTarget(2) = SimConstants.HeadSigns(2) * yaw * SimConstants.HeadMax
            m_headTarget(3) = SimConstants.HeadSigns(3) * roll * SimConstants.HeadMax
        End Sub

        ''' <summary>the mouth of the duck, 0 closed to 1 wide open</summary>
        Public Sub SetJaw(open As Double)
            m_jaw = Phys.Clamp(open, 0, 1)
        End Sub

        Public Sub SetInputLocked(locked As Boolean)
            Machine.InputLocked = locked
        End Sub

        Public Sub SetMode(mode As GameMode)
            Machine.SetMode(mode)
            RaiseEvent ModeChanged()
        End Sub

        Public Sub ToggleSit()
            Machine.ToggleSit()
            RaiseEvent ModeChanged()
        End Sub

        Public Sub ToggleHead()
            Machine.ToggleHead()
            RaiseEvent ModeChanged()
        End Sub

        Public Sub TriggerRoll()
            If Machine.TriggerRoll() Then
                RaiseEvent ModeChanged()
            ElseIf Machine.Loco = Locomotion.Rollers Then
                RaiseEvent ModeChanged()
            End If
        End Sub

        Public Sub TriggerKick(left As Boolean)
            If Machine.TriggerKick(left) Then
                RaiseEvent ModeChanged()
            End If
        End Sub

        Public Sub TriggerGroundPick()
            If Machine.TriggerGroundPick() Then
                RaiseEvent ModeChanged()
            End If
        End Sub

        Public Sub TriggerCrouch()
            If Machine.TriggerCrouch() Then
                RaiseEvent ModeChanged()
            End If
        End Sub

        ''' <summary>the one shot gesture that the shape of the duck supports</summary>
        Public Sub TriggerRollOrCrouch()
            If Machine.Loco = Locomotion.Rollers Then
                Call TriggerCrouch()
            Else
                Call TriggerRoll()
            End If
        End Sub

        Public Sub Quack()
            RaiseEvent Quacked()
        End Sub

        ''' <summary>
        ''' put the duck back on its feet and respawn the ball
        ''' </summary>
        Public Sub ResetSim()
            Dim locked As Boolean = Machine.InputLocked

            Machine.Reset()
            Machine.InputLocked = locked
            Machine.Loco = Shape

            Call Array.Clear(m_lastAction, 0, m_lastAction.Length)
            Call Array.Clear(m_headTarget, 0, m_headTarget.Length)
            Call Array.Clear(m_headSmooth, 0, m_headSmooth.Length)

            For j As Integer = 0 To SimConstants.NumJoints - 1
                m_targets(j) = SimConstants.DefaultPose(j)
            Next

            Call Duck.Spawn()
            Call Duck.Drive.SetTargets(m_targets)
            Call Ball.Park()
            Call SpawnBall()

            m_footAir(0) = False
            m_footAir(1) = False
            m_ballPrevValid = False
            m_bumpPrevValid = False

            Call Capture()
            RaiseEvent Respawned()
        End Sub

        ''' <summary>
        ''' drop the ball next to the duck
        ''' </summary>
        Public Sub SpawnBall()
            If Ball Is Nothing Then
                Return
            End If

            Ball.Spawn(Duck.Trunk.FramePosition, YawOf(Duck.Trunk.FrameRotation), m_random)
        End Sub

        ''' <summary>
        ''' switch between the legs and the rollers, rebuilding the world of the
        ''' other shape
        ''' </summary>
        Public Sub SetShape(loco As Locomotion)
            If loco = Shape AndAlso Duck IsNot Nothing Then
                Return
            End If

            Dim wasRunning As Boolean = m_running

            Call [Stop]()

            Try
                Shape = loco
                Machine.Loco = loco

                If loco = Locomotion.Rollers Then
                    Call Policies.LoadRollers(AddressOf Trace)
                End If

                Dim kinetics As KinematicsDocument = KinematicsDocument.Load(AssetPath.RobotFile(KinematicsFile(loco)))
                Dim model As MjcfDocument = MjcfDocument.Load(AssetPath.RobotFile(MjcfFile(loco)))

                Skeleton = DuckSkeleton.Build(loco, kinetics, model)

                Call BuildWorld()
                Call ResetSim()

                RaiseEvent ModeChanged()
            Catch ex As Exception
                Call Trace($"the switch to {loco} failed: {ex.Message}")
            End Try

            If wasRunning Then
                Call Start()
            End If
        End Sub

        ''' <summary>
        ''' add or remove the cosine relief of the arena
        ''' </summary>
        Public Sub SetRelief(enabled As Boolean)
            If Arena Is Nothing Then
                Return
            End If

            Call Arena.SetRelief(Duck.World, enabled)
        End Sub

        Public ReadOnly Property ReliefEnabled As Boolean
            Get
                Return Arena IsNot Nothing AndAlso Arena.ReliefEnabled
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            Call [Stop]()

            Call Policies.Dispose()
            Call Duck?.Dispose()
            Call Arena?.Dispose()

            Duck = Nothing
            Arena = Nothing
            Ball = Nothing
        End Sub
    End Class
End Namespace
