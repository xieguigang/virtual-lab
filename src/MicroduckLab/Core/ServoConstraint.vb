Imports Engine = Microsoft.VisualBasic.Engine

Namespace Core

    ''' <summary>
    ''' the target speed of one hinge, solved together with the joints and the
    ''' contacts of the world
    ''' </summary>
    ''' <remarks>
    ''' The drive used to set the joint speed once per sub step, before the
    ''' integration, the contacts and the hinge constraints ran. Everything that
    ''' happened after that pushed the speed away again, so the loop had to fight
    ''' the solver instead of converging with it, and the same gain read 52/200 in
    ''' one configuration and 244/250 in another.
    '''
    ''' As a constraint the servo takes part in every iteration of the solver, in
    ''' the same pass as the hinges and the ground, which is what the mujoco
    ''' actuator does implicitly: one degree of freedom, one scalar effective mass,
    ''' and the projection of the other constraints applied on top of it.
    ''' </remarks>
    Public Class ServoConstraint3D
        Implements Engine.Physics.Joints3D.IConstraint3D

        ''' <summary>the drive that holds the gains and the targets</summary>
        Public Property Drive As JointDriveHook
        ''' <summary>the actuator index of the hinge inside the drive</summary>
        Public Property Index As Integer

        Public Sub SolveVelocity(dt As Double) Implements Engine.Physics.Joints3D.IConstraint3D.SolveVelocity
            If Drive Is Nothing Then
                Return
            End If

            Call Drive.SolveJointVelocity(Index, dt)
        End Sub

        ' the servo has no position level correction: the velocity pass already
        ' drives the error to zero, and writing into Orientation here would add
        ' energy the same way the axis correction of the hinge did
        Public Sub SolvePosition(dt As Double) Implements Engine.Physics.Joints3D.IConstraint3D.SolvePosition
        End Sub
    End Class
End Namespace
