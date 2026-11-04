Namespace Core

    ''' <summary>
    ''' Every number of the sim, lifted from ``constants.js`` of the browser
    ''' build. The coordinates are the MJCF ones (z up, metres), which is the
    ''' frame of the model, of the render rig and of the observation.
    ''' </summary>
    Public Module SimConstants

        ' ── the policy interface ────────────────────────────────────────────
        Public ReadOnly JointNames As String() = {
            "left_hip_yaw", "left_hip_roll", "left_hip_pitch", "left_knee", "left_ankle",
            "neck_pitch", "head_pitch", "head_yaw", "head_roll",
            "right_hip_yaw", "right_hip_roll", "right_hip_pitch", "right_knee", "right_ankle"
        }

        ''' <summary>the standing joint angles that the policies were trained around</summary>
        ''' <remarks>
        ''' These are the angles of the STAND keyframe of the document, and they must
        ''' not be "improved": pre-bending the knee by 0.25 rad to lower the centre of
        ''' mass was measured and it destroys the gait - upright 55 -> 24 open loop,
        ''' 244 -> 50 standing, 243 -> 42 walking, because the policies were trained
        ''' around exactly this pose and treat anything else as a disturbance.
        ''' </remarks>
        Public ReadOnly DefaultPose As Double() = {
            0, -0.08726646259971647, -0.457924, -0.00494, 0.452984,
            0.3490658503988659, 0.3490658503988659, 0, 0,
            0, 0.08726646259971647, 0.457924, 0.00494, -0.452984
        }

        Public Const NumJoints As Integer = 14
        Public Const ObsSize As Integer = 61
        Public Const CmdSize As Integer = 13
        Public Const ActionScale As Double = 1.0

        ''' <summary>
        ''' the largest joint speed that the observation carries, rad/s
        ''' </summary>
        ''' <remarks>
        ''' 原来这里是 1e9（等于不裁剪），因为旧的 <c>JointDriveHook</c> 是速度伺服，
        ''' 关节速度被驱动到 <c>error × gain</c>，量级远超训练分布。
        ''' 换成标准位置伺服之后速度是真实的，但在接触冲击的瞬间仍会冒出几十 rad/s
        ''' 的尖峰；一个尖峰就足以让策略输出一个相反的大动作，于是越摆越歪。
        ''' 20 rad/s 是这批策略训练时见过的上限，直接把尖峰切掉。
        ''' </remarks>
        Public Const ObservedJointSpeed As Double = 20.0

        ''' <summary>the physics sub step of the model, seconds</summary>
        Public Const Timestep As Double = 0.005
        ''' <summary>the physics steps per control step</summary>
        Public Const Decimation As Integer = 4
        ''' <summary>the control (and inference) period, 50 Hz</summary>
        Public Const CtrlDt As Double = Timestep * Decimation

        ''' <summary>
        ''' how far above the keyframe the duck is dropped in, metres
        ''' </summary>
        ''' <remarks>
        ''' The convex proxies of the feet reach a little below the visual sole: a
        ''' duck that is spawned exactly on the keyframe starts a few millimetres
        ''' inside the floor, the contact solver answers with one large Baumgarte
        ''' impulse and the whole robot is thrown into the air with no contact at
        ''' all (measured: the feet at 180 mm after one second). Dropping it in
        ''' from a few millimetres lets the contacts settle instead.
        ''' </remarks>
        Public Const SpawnLift As Double = 0.0005

        ''' <summary>
        ''' the reflected inertia of the motor of a hinge, from the default class of
        ''' the joint of the MJCF (``armature="0.0018"``)
        ''' </summary>
        Public Const DefaultArmature As Double = 0.0018

        ''' <summary>
        ''' how much of the gravity feed forward a leg keeps while its foot is in the
        ''' air, 0 = none
        ''' </summary>
        ''' <remarks>
        ''' A swinging leg must be free to lift, so the support is mostly released; a
        ''' little is kept so that the limb does not simply hang off the hip.
        ''' </remarks>
        Public Const SwingSupportFactor As Double = 0.2

        ''' <summary>
        ''' how far the walk policy may pull a joint away from the stand pose while the
        ''' duck is not being driven, rad
        ''' </summary>
        ''' <remarks>
        ''' The policy asks for a -1.48 rad knee (a deep crouch) when it runs on this
        ''' solver, most likely because the joint speeds it observes come from a
        ''' velocity servo and do not look like the reference. Until that observation
        ''' is aligned, a standing duck is kept inside a window around the stand pose.
        ''' </remarks>
        Public Const StandTargetWindow As Double = 0.60

        ''' <summary>the joint damping of the default class of the MJCF hinges</summary>
        Public Const DefaultJointDamping As Double = 0.053

        ''' <summary>the dry friction of the default class of the MJCF hinges</summary>
        Public Const DefaultJointFrictionLoss As Double = 0.0048

        ' ── velocity commands ───────────────────────────────────────────────
        Public Const VelForward As Double = 0.25
        Public Const VelBackward As Double = -0.2
        Public Const VelAngular As Double = 1.0

        Public Const RollerVelForward As Double = 0.6
        Public Const RollerVelBackward As Double = -0.5
        Public Const RollerVelAngular As Double = 0.3

        ' ── one shot gestures ───────────────────────────────────────────────
        Public Const CrouchPeriodS As Double = 5.0
        Public Const CrouchEndPhase As Double = 0.7
        Public Const GroundPickPeriodS As Double = 4.0
        Public Const GroundPickEndPhase As Double = 0.7

        Public Const KickSteps As Integer = 25
        Public Const PostKickLockSteps As Integer = 20
        Public Const RollMaxSteps As Integer = 150

        ' ── head ────────────────────────────────────────────────────────────
        Public Const HeadMax As Double = 2.5
        Public Const HeadAlpha As Double = 0.2
        Public ReadOnly HeadSigns As Double() = {1, -1, 1, -1}

        ' ── fall recovery ───────────────────────────────────────────────────
        Public Const FallDebounceSteps As Integer = 10
        Public Const FallSettleSteps As Integer = 15
        Public Const RecoverUprightSteps As Integer = 50
        Public Const RecoverGiveUpSteps As Integer = 300
        Public Const FallGraceMs As Integer = 1000
        Public Const RollFallGraceMs As Integer = 5000

        ' ── arena ───────────────────────────────────────────────────────────
        Public Const ArenaHalf As Double = 1.5
        Public Const ArenaWallHeight As Double = 0.25
        Public Const ArenaWallThickness As Double = 0.05
        Public Const GridSection As Double = (2 * ArenaHalf) / 5

        Public Const SpawnX As Double = -ArenaHalf + 1.5 * GridSection
        Public Const SpawnY As Double = 0
        Public Const SpawnZ As Double = 0.12

        ' ── ball ────────────────────────────────────────────────────────────
        Public Const BallRadius As Double = 0.05
        Public Const BallMass As Double = 0.03
        Public Const BallParkX As Double = 50
        Public Const BallParkY As Double = 0
        Public Const BallParkZ As Double = 0.05
        Public Const BallFriction As Double = 0.4
        Public Const BallRestitution As Double = 0.12

        ' ── relief (the prototype height field of the arena) ────────────────
        ''' <summary>[cx, cy, height, radius] of the three cosine bumps</summary>
        Public ReadOnly ReliefBumps As Double()() = {
            New Double() {0.3, -0.9, 0.06, 0.55},
            New Double() {-0.9, 0.75, 0.05, 0.5},
            New Double() {0.6, 0.9, 0.07, 0.55}
        }

        Public Const ReliefMax As Double = 0.07
        Public Const ReliefGrid As Integer = 65
        Public Const ReliefSink As Double = 0.003
        Public Const ReliefRate As Double = 0.6

        ' ── the policies of the nine slots ──────────────────────────────────
        Public Const PolicyWalk As String = "BEST_alpha_walking.onnx"
        Public Const PolicySitStand As String = "BEST_alpha_sitstand.onnx"
        Public Const PolicyRoll As String = "roulade.onnx"
        Public Const PolicyKickLeft As String = "ball_kick_left.onnx"
        Public Const PolicyKickRight As String = "ball_kick_right.onnx"
        Public Const PolicyGroundPick As String = "alpha_ground_pick.onnx"
        Public Const PolicyStand As String = "BEST_alpha_stand.onnx"
        Public Const PolicyDrive As String = "BEST_roller.onnx"
        Public Const PolicyCrouch As String = "BEST_roller_crouch.onnx"

        ''' <summary>the velocity limits of the two locomotion variants</summary>
        Public Function LimitsOf(loco As Locomotion) As Double()
            If loco = Locomotion.Rollers Then
                Return New Double() {RollerVelForward, RollerVelBackward, RollerVelAngular}
            End If

            Return New Double() {VelForward, VelBackward, VelAngular}
        End Function

        ''' <summary>
        ''' the analytic height of the cosine relief, shared by the physics
        ''' proxies and by the grid shader
        ''' </summary>
        Public Function ReliefHeight(x As Double, y As Double) As Double
            Dim height As Double = 0

            For Each bump As Double() In ReliefBumps
                Dim dx As Double = x - bump(0)
                Dim dy As Double = y - bump(1)
                Dim distance As Double = Math.Sqrt(dx * dx + dy * dy)

                If distance >= bump(3) Then
                    Continue For
                End If

                Dim wave As Double = 0.5 * (1 + Math.Cos(Math.PI * distance / bump(3)))
                Dim h As Double = bump(2) * wave

                If h > height Then
                    height = h
                End If
            Next

            Return height
        End Function
    End Module

    ''' <summary>the two locomotion variants of the robot</summary>
    Public Enum Locomotion
        Legs = 0
        Rollers = 1
    End Enum

    ''' <summary>the game modes of the policy slot machine</summary>
    Public Enum GameMode
        Walk
        SitStand
        Roll
        KickLeft
        KickRight
        Crouch
        GroundPick
    End Enum

    ''' <summary>the recovery phase of a fallen duck</summary>
    Public Enum RecoveryState
        None = 0
        Settle = 1
        Recovering = 2
    End Enum
End Namespace
