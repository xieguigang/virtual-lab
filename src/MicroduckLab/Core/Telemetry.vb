Imports System.Numerics

Namespace Core

    ''' <summary>
    ''' The numbers that the hud shows, refreshed at four hertz.
    ''' </summary>
    Public Class TelemetryFrame
        Public Property ModeLabel As String = "Run"
        Public Property Mode As GameMode = GameMode.Walk
        Public Property Loco As Locomotion = Locomotion.Legs
        ''' <summary>the measured control rate, hertz</summary>
        Public Property ControlHz As Double
        ''' <summary>the ground speed of the trunk, m/s</summary>
        Public Property Speed As Double
        ''' <summary>the world speed of the trunk, m/s</summary>
        Public Property TurnRate As Double
        ''' <summary>the height of the trunk above the floor, m</summary>
        Public Property TrunkHeight As Double
        ''' <summary>the projected gravity of the trunk, -1 is upright</summary>
        Public Property GravityZ As Double = -1
        ''' <summary>the policy that is answering right now</summary>
        Public Property PolicyName As String = "walk"
        ''' <summary>the wall clock time of one inference, milliseconds</summary>
        Public Property InferenceMs As Double
        ''' <summary>the wall clock time of one control step, milliseconds</summary>
        Public Property ControlStepMs As Double
        Public Property BallActive As Boolean
        Public Property HeadMode As Boolean
        Public Property Recovering As Boolean
        ''' <summary>whether any input source claims the twist</summary>
        Public Property Driving As Boolean
        Public Property Revision As Long
    End Class

    ''' <summary>
    ''' One consistent frame of the pose of the scene, handed from the control
    ''' thread to the render thread.
    ''' </summary>
    ''' <remarks>
    ''' The physics world must not be touched while it is stepping, and the render
    ''' thread must not wait for an inference. The control step therefore copies
    ''' the fifteen matrices and the state of the ball into this object under the
    ''' lock of the session, and the renderer reads the last copy.
    ''' </remarks>
    Public Class PoseSnapshot
        ''' <summary>the most links that a shape of the robot has</summary>
        Public Const MaxLinks As Integer = 32

        ''' <summary>the world matrix of the body frame of every link, in skeleton order</summary>
        Public ReadOnly BodyWorld As Matrix4x4() = New Matrix4x4(MaxLinks - 1) {}
        ''' <summary>how many of the matrices of the frame are valid</summary>
        Public Property BodyCount As Integer
        Public ReadOnly JointAngles As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly JointSpeeds As Double() = New Double(SimConstants.NumJoints - 1) {}

        Public Property JawOpen As Single
        Public Property BallMatrix As Matrix4x4 = Matrix4x4.Identity
        Public Property BallVisible As Boolean
        Public Property Telemetry As New TelemetryFrame()
        ''' <summary>a counter that grows with every captured frame</summary>
        Public Property Revision As Long
        ''' <summary>the mode of the duck, for the fx of the entrance</summary>
        Public Property Mode As GameMode = GameMode.Walk
        Public Property Recovering As Boolean

        ''' <summary>the world position of the trunk, the focus of the camera</summary>
        Public Property TrunkPosition As Vector3
        ''' <summary>the yaw of the duck, radians, the forward axis of the model</summary>
        Public Property TrunkYaw As Double

        Public Sub CopyFrom(other As PoseSnapshot)
            Array.Copy(other.BodyWorld, BodyWorld, BodyWorld.Length)
            BodyCount = other.BodyCount
            Array.Copy(other.JointAngles, JointAngles, JointAngles.Length)
            Array.Copy(other.JointSpeeds, JointSpeeds, JointSpeeds.Length)

            JawOpen = other.JawOpen
            BallMatrix = other.BallMatrix
            BallVisible = other.BallVisible
            Telemetry = other.Telemetry
            Revision = other.Revision
            Mode = other.Mode
            Recovering = other.Recovering
            TrunkPosition = other.TrunkPosition
            TrunkYaw = other.TrunkYaw
        End Sub
    End Class
End Namespace
