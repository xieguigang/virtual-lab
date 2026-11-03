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
    ''' Every number now comes straight out of <c>mjData</c>: the gyro reading is the
    ''' body angular velocity rotated into the trunk frame, the projected gravity is
    ''' one rotation away from the trunk orientation, and the joints are read at the
    ''' <c>qpos</c>/<c>qvel</c> addresses resolved once at startup.
    ''' </remarks>
    Public Class ObservationBuilder

        Private ReadOnly m_world As MjWorld
        Private ReadOnly m_observation As Double() = New Double(SimConstants.ObsSize - 1) {}

        Public Sub New(world As MjWorld)
            m_world = world
        End Sub

        ''' <summary>
        ''' fill and return the observation buffer (the same array is reused, copy
        ''' it when it has to outlive the call)
        ''' </summary>
        ''' <param name="lastAction">the raw action of the previous control step</param>
        ''' <param name="command">the thirteen words of the command</param>
        Public Function Build(lastAction As Double(), command As Double()) As Double()
            Dim rotation As Quaternion = m_world.TrunkRotation
            Dim inverse As Quaternion = Quaternion.Conjugate(rotation)
            Dim angular As Vector3 = Vector3.Transform(m_world.TrunkAngularVelocity, inverse)
            Dim gravity As Vector3 = Vector3.Transform(New Vector3(0, 0, -1), inverse)

            m_observation(0) = angular.X
            m_observation(1) = angular.Y
            m_observation(2) = angular.Z
            m_observation(3) = gravity.X
            m_observation(4) = gravity.Y
            m_observation(5) = gravity.Z

            For j As Integer = 0 To SimConstants.NumJoints - 1
                m_observation(6 + j) = m_world.JointAngle(j) - SimConstants.DefaultPose(j)
                m_observation(20 + j) = Phys.Clamp(m_world.JointSpeed(j),
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
