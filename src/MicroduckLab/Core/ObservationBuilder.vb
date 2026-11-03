Imports System.Numerics

Namespace Core

    ''' <summary>
    ''' Assembles the 61 dimensional observation of the policies.
    ''' </summary>
    ''' <remarks>
    ''' The layout is the one of ``microduck_rl/scripts/infer_policy.py`` and of
    ''' ``game.js``:
    '''
    ''' <code>
    '''  0.. 2  the angular velocity of the trunk, in the trunk frame
    '''  3.. 5  the projected gravity, in the trunk frame (index 5 is z)
    '''  6..19  the joint angles, measured from the stand pose
    ''' 20..33  the joint speeds
    ''' 34..47  the raw action of the previous step (not scaled)
    ''' 48..60  the 13 words of the command
    ''' </code>
    '''
    ''' The two blocks that MuJoCo took from a sensor (the gyro and the projected
    ''' gravity) are computed from the pose of the trunk: the engine has no sensor
    ''' of its own, and both quantities are one rotation away from the body pose.
    ''' </remarks>
    Public Class ObservationBuilder

        Private ReadOnly m_duck As DuckPhysics
        Private ReadOnly m_observation As Double() = New Double(SimConstants.ObsSize - 1) {}

        Public Sub New(duck As DuckPhysics)
            m_duck = duck
        End Sub

        ''' <summary>
        ''' fill and return the observation buffer (the same array is reused, copy
        ''' it when it has to outlive the call)
        ''' </summary>
        ''' <param name="lastAction">the raw action of the previous control step</param>
        ''' <param name="command">the thirteen words of the command</param>
        Public Function Build(lastAction As Double(), command As Double()) As Double()
            Dim trunk As Engine.Physics.RigidBody3D = m_duck.Trunk.Body
            Dim rotation As Quaternion = Phys.Quat(trunk.Orientation)
            Dim inverse As Quaternion = Quaternion.Conjugate(rotation)
            Dim angular As Vector3 = Vector3.Transform(Phys.Vec(trunk.AngularVelocity), inverse)
            Dim gravity As Vector3 = Vector3.Transform(New Vector3(0, 0, -1), inverse)

            m_observation(0) = angular.X
            m_observation(1) = angular.Y
            m_observation(2) = angular.Z
            m_observation(3) = gravity.X
            m_observation(4) = gravity.Y
            m_observation(5) = gravity.Z

            For j As Integer = 0 To SimConstants.NumJoints - 1
                m_observation(6 + j) = m_duck.JointAngle(j) - SimConstants.DefaultPose(j)
                ' the joint speeds are produced by a velocity servo that sets them
                ' directly to error * 30, while the reference model gets its speeds
                ' from the force balance of an implicit actuator: the numbers are an
                ' order of magnitude larger and the policy reads them as "I am
                ' rushing", answers with a deep crouch (knee target -1.48 rad against
                ' a stand pose of 0.005) and the duck ends up lying on the floor.
                ' Clipping keeps the observation in the range the policy was trained
                ' on without touching the control loop.
                m_observation(20 + j) = Phys.Clamp(m_duck.JointSpeed(j),
                                                   -SimConstants.ObservedJointSpeed,
                                                   SimConstants.ObservedJointSpeed)
                m_observation(34 + j) = If(lastAction Is Nothing OrElse j >= lastAction.Length, 0, lastAction(j))
            Next

            For c As Integer = 0 To SimConstants.CmdSize - 1
                m_observation(48 + c) = If(command Is Nothing OrElse c >= command.Length, 0, command(c))
            Next

            Return m_observation
        End Function
    End Class
End Namespace
