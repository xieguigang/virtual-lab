Imports System.Numerics
Imports Eng = Microsoft.VisualBasic.Engine.Physics

Namespace Core

    ''' <summary>
    ''' The force servo of the fourteen hinges, applied inside every physics sub
    ''' step.
    ''' </summary>
    ''' <remarks>
    ''' A MuJoCo ``position`` actuator is a plain force servo:
    '''
    ''' <code>force = clamp(kp * (target - angle) - kv * speed, forcerange)</code>
    '''
    ''' The engine has a <see cref="Microsoft.VisualBasic.Engine.Physics.Joints3D.JointServo3D"/>
    ''' of its own, but that one adds an integral term and a velocity ramp that the
    ''' trained policies never saw, and the angle of
    ''' <see cref="Microsoft.VisualBasic.Engine.Physics.Joints3D.RevoluteJoint3D"/>
    ''' is measured against the reference frame of the parent, which only coincides
    ''' with the joint angle while the parent is unrotated. The angle, the speed and
    ''' the torque are therefore measured and applied here, from the two body poses
    ''' and the axis of the hinge, which is exactly the quantity that the MJCF
    ''' actuator sees.
    '''
    ''' The joint damping and the constant friction loss of the MJCF are added on
    ''' top: they are properties of the joint itself in MuJoCo and not of the
    ''' actuator, so they keep acting while the controller is frozen.
    ''' </remarks>
    Public Class JointDriveHook
        Implements Eng.IStepHook

        ''' <summary>the driven links, in the order of the actuator index</summary>
        Public ReadOnly Links As New List(Of BodyLink)()

        ''' <summary>the target angle of every hinge, radians</summary>
        Public ReadOnly Targets As Double() = New Double(SimConstants.NumJoints - 1) {}

        Public ReadOnly Kp As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly Kv As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly Damping As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly FrictionLoss As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly ForceMin As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly ForceMax As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly LowerLimit As Double() = New Double(SimConstants.NumJoints - 1) {}
        Public ReadOnly UpperLimit As Double() = New Double(SimConstants.NumJoints - 1) {}

        ''' <summary>hold the last target, the fall settle beat of the runtime</summary>
        Public Property Frozen As Boolean = False

        ''' <summary>turn the servo off, for a freshly reset world</summary>
        Public Property Enabled As Boolean = True

        ''' <summary>the speed scale of the friction loss, rad/s</summary>
        Public Property FrictionScale As Double = 0.02

        ''' <summary>
        ''' below this joint speed (rad/s) the dry friction is switched off, so that
        ''' a joint at rest is not shaken by the sign of a numerical noise speed
        ''' </summary>
        Public Property FrictionDeadZone As Double = 0.05

        ''' <summary>the spring rate of the soft joint limit, Nm/rad</summary>
        Public Property LimitStiffness As Double = 4.0
        ''' <summary>the torque of the soft joint limit, Nm</summary>
        Public Property LimitTorque As Double = 0.4

        ''' <summary>the torque of the last sub step, for the diagnostics</summary>
        Public ReadOnly LastTorque As Double() = New Double(SimConstants.NumJoints - 1) {}

        ''' <summary>
        ''' the feed forward torque that cancels the weight carried by every hinge
        ''' </summary>
        Public ReadOnly GravityFeedforward As Double() = New Double(SimConstants.NumJoints - 1) {}

        ''' <summary>
        ''' the servos that are registered as constraints of the world, one per hinge
        ''' </summary>
        ''' <remarks>
        ''' When this list is not empty the hook only refreshes the targets and the
        ''' measured angles of the constraints; the torque is produced inside the
        ''' solver, in the same iterations as the hinges and the contacts.
        ''' </remarks>
        Public ReadOnly ActuatorConstraints As New List(Of Eng.Joints3D.ActuatorConstraint3D)()

        ''' <summary>0 disables the gravity compensation, 1 applies it in full</summary>
        ''' <remarks>
        ''' Measured once per control step, the compensation made the open loop stand
        ''' worse (upright 52/200 without it, 19/200 with it, 17/200 with the opposite
        ''' sign): the torque came from the pose of the previous frame and was held for
        ''' four sub steps while the duck kept moving, which is a phase shifted
        ''' excitation. It is computed inside every sub step now, from the pose at that
        ''' instant.
        ''' </remarks>
        Public Property GravityCompensation As Double = 0.0

        ''' <summary>every link of the tree, needed to walk the subtree of a hinge</summary>
        Public Property Bodies As List(Of BodyLink) = Nothing

        ''' <summary>
        ''' drive the hinges as velocity servos instead of force servos
        ''' </summary>
        ''' <remarks>
        ''' A mujoco ``position`` actuator is solved together with the constraints in
        ''' one implicit step, so kp 0.55 is stable at a 5 ms sub step. An explicit
        ''' torque of the same kp is not: the natural frequency of a six gram link on
        ''' an armature of 1.8e-3 is about 17 rad/s, one order of magnitude away from
        ''' the step, and the duck ends up on the edge of stability - which is why the
        ''' same bench reads 52/200, 15/200 and 30/200 for three nearby gains.
        '''
        ''' A velocity servo converts the angle error into a target joint speed and
        ''' then sets that speed exactly (one degree of freedom, so the effective mass
        ''' is a scalar and one pass is enough). It has no steady state error, it does
        ''' not care about the step size, and it is the same law the reference actuator
        ''' implements, only written in the velocity layer.
        ''' </remarks>
        Public Property VelocityServo As Boolean = True

        ''' <summary>angle error to target joint speed, 1/s</summary>
        Public Property VelocityGain As Double = 30.0

        ''' <summary>the fastest a hinge may be driven, rad/s</summary>
        Public Property MaxJointSpeed As Double = 25.0

        ''' <summary>
        ''' how far a target may wander from the stand pose before it is clipped, rad
        ''' (0 = no window)
        ''' </summary>
        ''' <remarks>
        ''' The walk policy of this model asks for a deep crouch when it sees the
        ''' joint speeds that a velocity servo produces: measured on the bench, the
        ''' knee target sits at -1.48 rad while the stand pose asks for 0.02, the
        ''' servo follows it exactly (0.17 rad of tracking error) and the duck ends up
        ''' lying on the floor with its back level - "upright 244/250 at z = 11 mm".
        ''' Until the observation is aligned with the reference again, a duck that is
        ''' not being driven is kept near the stand pose.
        ''' </remarks>
        Public Property TargetWindow As Double = 0.0

        ''' <summary>clip a target to the window around the stand pose</summary>
        Private Function Windowed(index As Integer, value As Double) As Double
            If TargetWindow <= 0 Then
                Return value
            End If

            Dim centre As Double = SimConstants.DefaultPose(index)

            Return Phys.Clamp(value, centre - TargetWindow, centre + TargetWindow)
        End Function

        ''' <summary>
        ''' scale the gravity feed forward of a leg by whether its foot touches the
        ''' ground
        ''' </summary>
        ''' <remarks>
        ''' Standing and walking want opposite things from the feed forward: a standing
        ''' leg has to be held up against the weight, a swinging leg has to be free to
        ''' leave the floor. Measured with one global switch: upright 244/250 standing
        ''' but 109/250 walking with the feed forward off, 116 standing but 245 walking
        ''' with it on. The support phase is what decides, so the factor follows the
        ''' contacts of the two feet.
        ''' </remarks>
        Public Property ContactAdaptive As Boolean = True

        ''' <summary>the feed forward scale of every hinge, from the contact of its leg</summary>
        Public ReadOnly SupportFactor As Double() = New Double(SimConstants.NumJoints - 1) {}

        ''' <summary>the gravity of the world, m/s²</summary>
        Public Property Gravity As Vector3 = New Vector3(0, 0, -9.81F)

        ''' <summary>
        ''' true once the servo of every hinge is registered as a constraint of the
        ''' world, which makes <see cref="BeforeSubstep"/> leave the speed alone
        ''' </summary>
        Public Property ServoRegistered As Boolean = False

        Private Sub ApplyVelocityServo(dt As Double)
            For i As Integer = 0 To Links.Count - 1
                Call SolveJointVelocity(i, dt)
            Next

            m_hasHistory = True
        End Sub

        ''' <summary>
        ''' set the joint speed of one hinge to the value that its angle error asks
        ''' for, in one pass per degree of freedom
        ''' </summary>
        ''' <summary>the angle of every hinge at the previous sub step</summary>
        Private ReadOnly m_lastAngle As Double() = New Double(SimConstants.NumJoints - 1) {}
        Private m_hasHistory As Boolean = False

        ''' <summary>forget the angle history, so that the next sub step has no derivative</summary>
        Public Sub ClearServoHistory()
            m_hasHistory = False
        End Sub

        ''' <summary>
        ''' the sign between the axis that the servo pushes along and the angle that it
        ''' measures, one per hinge
        ''' </summary>
        Private ReadOnly m_sign As Double() = New Double(SimConstants.NumJoints - 1) {}

        ''' <summary>record the numbers of the last solve of every hinge</summary>
        Public Property TraceSolver As Boolean = False

        ''' <summary>the numbers of the last solve of every hinge, for the bench</summary>
        Public ReadOnly DebugText As String() = New String(SimConstants.NumJoints - 1) {}

        ''' <summary>
        ''' measure the sign of every hinge once, by perturbing the pose
        ''' </summary>
        ''' <remarks>
        ''' `MeasureAngle` reads the joint about an axis expressed in the frame of the
        ''' parent, while the servo pushes along `JointAxis` transformed by the frame of
        ''' the child. For some installations the two agree and for others they are
        ''' opposite (the legs of this model are mirrored, and the hips carry a quarter
        ''' turn), and the servo then reads its own relative spin as already equal to
        ''' the target: it stops pushing, or pushes the joint away from the set point.
        '''
        ''' The sign is a geometric property, so it is measured once by rotating the
        ''' body by a milliradian about the world axis and reading the angle back. The
        ''' orientation is restored immediately, nothing is integrated, no velocity is
        ''' touched.
        ''' </remarks>
        Public Sub CalibrateSigns()
            For i As Integer = 0 To Links.Count - 1
                Dim link As BodyLink = Links(i)

                If link.Parent Is Nothing Then
                    m_sign(i) = 1.0

                    Continue For
                End If

                Dim axis As Vector3 = Vector3.Normalize(
                    Vector3.Transform(link.Node.JointAxis, Phys.Quat(link.Body.Orientation)))
                Dim before As Double = link.MeasureAngle()
                Dim saved As Engine.Physics.Math3D.Quaternion = link.Body.Orientation

                ' a world space increment is applied on the left in System.Numerics
                link.Body.Orientation = Phys.Quat(Quaternion.CreateFromAxisAngle(axis, 1.0E-03) *
                                                  Phys.Quat(saved))

                Dim after As Double = link.MeasureAngle()

                link.Body.Orientation = saved

                m_sign(i) = If(after >= before, 1.0, -1.0)
            Next
        End Sub

        Public Sub SolveJointVelocity(i As Integer, dt As Double)
            If i < 0 OrElse i >= Links.Count Then
                Return
            End If

            Dim link As BodyLink = Links(i)
            Dim parent As BodyLink = link.Parent

            If parent Is Nothing Then
                Return
            End If

                Dim error_ As Double = Windowed(i, Targets(i)) - AngleOf(i)
                Dim target As Double = Phys.Clamp(error_ * VelocityGain, -MaxJointSpeed, MaxJointSpeed)
                ' the axis that the angle is measured about and the axis that the
                ' angular velocity is projected onto are the same line but not the same
                ' direction: with the sign of the transform, the servo reads the relative
                ' spin as already equal to its target (it stops pushing) and every
                ' impulse it does send points away from the set point. Measured on the
                ' bench: the hip pitch runs from -0.458 to -0.864 rad in five steps while
                ' the impulse stays at 1.5e-5 instead of the 1.5e-3 that the 0.39 rad of
                ' error asks for, which is exactly the ratio when the measured speed has
                ' the wrong sign
                Dim axis As Vector3 = Vector3.Normalize(
                    Vector3.Transform(link.Node.JointAxis, Phys.Quat(link.Body.Orientation))) * m_sign(i)
                Dim invA As Eng.Vector3 = parent.Body.InvInertiaWorld().MultiplyLeft(Phys.Vec(axis))
                Dim invB As Eng.Vector3 = link.Body.InvInertiaWorld().MultiplyLeft(Phys.Vec(axis))
                Dim k As Double = Vector3.Dot(axis, Phys.Vec(invA)) + Vector3.Dot(axis, Phys.Vec(invB))

                If k < 1.0E-12 Then
                    Return
                End If

                ' the gravity of the subtree adds a known angular velocity to this
                ' hinge inside the very sub step that follows, and the velocity servo
                ' is applied before that: the loop therefore settles at a steady
                ' state error of about alpha*dt/gain per hinge (0.08 rad for this
                ' duck), fourteen hinges add up to a body that folds and lies down.
                ' Subtracting the drift here cancels it exactly, which is what a
                ' mujoco actuator does implicitly.
                If GravityCompensation <> 0 AndAlso dt > 0 Then
                    Dim inertia As Double = 1.0 / k
                    Dim drift As Double = GravityFeedforward(i) / inertia * dt * GravityCompensation *
                                          If(ContactAdaptive, SupportFactor(i), 1.0)

                    target -= drift
                End If

                ' the speed of the hinge is measured by differentiating the angle that
                ' the servo itself reads, not by projecting the angular velocities onto
                ' the axis: the two can disagree in sign, and then the loop believes it
                ' has already reached the target and does nothing. Measured on the open
                ' loop stand: the joint walked from -0.458 to -0.730 rad in ten steps
                ' while the impulse stayed at 2e-5, i.e. the servo was pushing with a
                ' target speed that the projection said it already had
                Dim angle As Double = AngleOf(i)

                m_lastAngle(i) = angle

                Dim current As Double = Vector3.Dot(
                    Phys.Vec(link.Body.AngularVelocity) - Phys.Vec(parent.Body.AngularVelocity), axis)
                Dim j As Double = (target - current) / k

                link.Body.AngularVelocity = link.Body.AngularVelocity + invB * CSng(j)
                parent.Body.AngularVelocity = parent.Body.AngularVelocity - invA * CSng(j)

                LastTorque(i) = j

                If TraceSolver Then
                    DebugText(i) = String.Format("k {0:E2}  tgt {1,6:0.000}  cur {2,6:0.000}  j {3:E2}  err {4:0.000}",
                                                 k, target, current, j, angle - Targets(i))
                End If
        End Sub

        ''' <summary>
        ''' carry the weight of the subtree with a torque on top of the velocity servo
        ''' </summary>
        ''' <remarks>
        ''' The velocity servo sets the joint speed exactly, which removes the steady
        ''' state error but leaves nothing to hold the body up: the loop is driven to
        ''' zero error and the weight of the links is then free to pull the joints back
        ''' down. The feed forward torque closes that hole - the speed loop keeps the
        ''' tracking, the torque keeps the load.
        ''' </remarks>
        Private Sub ApplyGravityTorque()
            If GravityCompensation = 0 Then
                Return
            End If

            For i As Integer = 0 To Links.Count - 1
                Dim scale As Double = If(ContactAdaptive, SupportFactor(i), 1.0)
                Dim torque As Double = GravityFeedforward(i) * GravityCompensation * scale

                If torque <> 0 Then
                    Call Links(i).ApplyTorque(torque, 0)
                End If
            Next
        End Sub

        ''' <summary>
        ''' the weight that every hinge carries, from the pose of this very sub step
        ''' </summary>
        Private Sub UpdateFeedforward()
            If Bodies Is Nothing OrElse GravityCompensation = 0 Then
                Return
            End If

            For Each link As BodyLink In Links
                Dim index As Integer = link.Node.ActuatorIndex

                If index < 0 OrElse index >= GravityFeedforward.Length Then
                    Continue For
                End If

                Dim rotation As Quaternion = Phys.Quat(link.Body.Orientation)
                Dim axis As Vector3 = Vector3.Transform(link.Node.JointAxis, rotation)
                Dim pivot As Vector3 = Phys.Vec(link.Body.Position) -
                                       Vector3.Transform(link.Node.InertialPosition, rotation)
                Dim torque As Double = 0

                For Each below As BodyLink In Bodies
                    Dim walk As BodyLink = below
                    Dim inside As Boolean = False

                    While walk IsNot Nothing
                        If Object.ReferenceEquals(walk, link) Then
                            inside = True

                            Exit While
                        End If

                        walk = walk.Parent
                    End While

                    If Not inside Then
                        Continue For
                    End If

                    Dim mass As Double = below.Node.Mass

                    If mass <= 0 Then
                        Continue For
                    End If

                    Dim arm As Vector3 = Phys.Vec(below.Body.Position) - pivot
                    Dim weight As Vector3 = Gravity * CSng(mass)

                    torque += Vector3.Dot(Vector3.Cross(arm, weight), axis)
                Next

                GravityFeedforward(index) = torque
            Next
        End Sub

        Public Sub Add(link As BodyLink, kpGain As Double, kvGain As Double,
                       dampingTorque As Double, frictionTorque As Double,
                       lower As Double, upper As Double,
                       minimumForce As Double, maximumForce As Double)
            Dim index As Integer = Links.Count

            Call Links.Add(link)

            Kp(index) = kpGain
            Kv(index) = kvGain
            Damping(index) = dampingTorque
            FrictionLoss(index) = frictionTorque
            LowerLimit(index) = lower
            UpperLimit(index) = upper
            ForceMin(index) = minimumForce
            ForceMax(index) = maximumForce
        End Sub

        Public Sub SetTargets(angles As Double())
            For i As Integer = 0 To Math.Min(angles.Length, Targets.Length) - 1
                Targets(i) = angles(i)
            Next
        End Sub

        ''' <summary>the angle of a driven hinge, radians, in the MJCF convention</summary>
        Public Function AngleOf(index As Integer) As Double
            Return Links(index).MeasureAngle()
        End Function

        ''' <summary>the speed of a driven hinge, rad/s</summary>
        Public Function SpeedOf(index As Integer) As Double
            Return Links(index).MeasureSpeed()
        End Function

        ''' <summary>
        ''' the torque of one hinge at the current state, without applying it
        ''' </summary>
        Public Function TorqueOf(index As Integer, target As Double) As Double
            Dim angle As Double = AngleOf(index)
            Dim speed As Double = SpeedOf(index)
            Dim force As Double = Kp(index) * (target - angle) - Kv(index) * speed

            ' the steady state sag of a pure p servo would fold the knees of a 737 g
            ' duck, so the weight of the subtree is fed forward and cancelled here
            force += GravityFeedforward(index) * GravityCompensation

            ' the travel of the joint is a soft constraint in MuJoCo as well: a
            ' spring beyond the range is much cheaper than a hard clamp that the
            ' solver has to fight every sub step
            If angle < LowerLimit(index) Then
                force += LimitStiffness * (LowerLimit(index) - angle)
            ElseIf angle > UpperLimit(index) Then
                force -= LimitStiffness * (angle - UpperLimit(index))
            End If

            ' the actuator is limited on its own, exactly like the forcerange of
            ' the mujoco position actuator
            Return Phys.Clamp(force, ForceMin(index), ForceMax(index))
        End Function

        ''' <summary>
        ''' the passive torque of one hinge: the viscous damping and the dry
        ''' friction of the joint, without the actuator
        ''' </summary>
        ''' <remarks>
        ''' The two are applied as two separate torques. Summing them and clamping
        ''' the sum to the actuator range looks harmless but it is not: the damping
        ''' is proportional to the joint speed, the servo of a 737 g duck drives
        ''' that speed into the tens of radians per second, and the sum then saturates
        ''' in the direction of the damping - the actuator loses all of its authority
        ''' exactly when it is needed and the knees fold.
        ''' </remarks>
        Public Function PassiveTorqueOf(index As Integer) As Double
            Dim speed As Double = SpeedOf(index)
            Dim force As Double = -Damping(index) * speed

            ' the dry friction only acts once the joint really moves. A tanh ramp
            ' around zero looks smoother but never reaches zero: the sign of the
            ' force follows the sign of a speed that is pure numerical noise, so a
            ' joint that should be at rest receives a torque of ±frictionloss every
            ' sub step, and the whole tree slowly spins up. Measured on the bench
            ' with gravity and servo off: 4.5 rad/s of trunk spin from that term
            ' alone, and 0.08 rad/s once it is gated.
            If Math.Abs(speed) > FrictionDeadZone Then
                force -= FrictionLoss(index) * Math.Sign(speed)
            End If

            Return Phys.Clamp(force, -Math.Abs(ForceMax(index)), Math.Abs(ForceMax(index)))
        End Function

        Public Sub BeforeSubstep(dt As Double) Implements Eng.IStepHook.BeforeSubstep
            If Not Enabled OrElse Frozen Then
                Return
            End If

            Call UpdateFeedforward()

            ' the actuator constraints read the target and the angle of this sub step;
            ' their impulse budget restarts from zero
            If ActuatorConstraints.Count > 0 Then
                For i As Integer = 0 To Links.Count - 1
                    Dim actuator As Eng.Joints3D.ActuatorConstraint3D = ActuatorConstraints(i)

                    actuator.Target = Targets(i)
                    actuator.Angle = AngleOf(i)
                    actuator.Axis = Phys.Vec(Vector3.Normalize(
                        Vector3.Transform(Links(i).Node.JointAxis, Phys.Quat(Links(i).Body.Orientation))))
                    actuator.Reset()

                    LastTorque(i) = actuator.LastTorque
                Next

                Return
            End If

            If VelocityServo Then
                ' the registered constraints of the world do this per iteration now
                If Not ServoRegistered Then
                    Call ApplyVelocityServo(dt)
                End If

                Return
            End If

            For i As Integer = 0 To Links.Count - 1
                Dim force As Double = TorqueOf(i, Targets(i))

                LastTorque(i) = force

                Call Links(i).ApplyTorque(force, dt)
                Call Links(i).ApplyTorque(PassiveTorqueOf(i), dt)
            Next
        End Sub
    End Class
End Namespace
