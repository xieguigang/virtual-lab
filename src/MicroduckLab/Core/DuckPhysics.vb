Imports System.Numerics
Imports MicroduckLab.Assets
Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports Collision3D = Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Joints3D = Microsoft.VisualBasic.Engine.Physics.Joints3D

Namespace Core

    ''' <summary>
    ''' One link of the rigid body tree: the physics body, the hinge to its parent
    ''' and the cached pose of its body frame.
    ''' </summary>
    Public Class BodyLink
        Public Property Node As SkeletonNode
        Public Property Body As Eng.RigidBody3D
        Public Property Hinge As Joints3D.RevoluteJoint3D
        Public Property Parent As BodyLink
        Public ReadOnly Property Children As New List(Of BodyLink)()

        ''' <summary>the origin of the body frame in the world, z up</summary>
        Public Property FramePosition As Vector3 = Vector3.Zero
        ''' <summary>the rotation of the body frame in the world</summary>
        Public Property FrameRotation As Quaternion = Quaternion.Identity
        ''' <summary>the world matrix of the body frame, for the renderer</summary>
        Public Property World As Matrix4x4 = Matrix4x4.Identity

        ''' <summary>the collision proxy of the link, nothing when it has none</summary>
        Public Property Collider As Collision3D.ConvexMesh3D

        Public Sub UpdatePose()
            FrameRotation = Phys.Quat(Body.Orientation)
            FramePosition = Phys.Vec(Body.Position) - Vector3.Transform(Node.InertialPosition, FrameRotation)
            World = Phys.WorldOf(FramePosition, FrameRotation)
        End Sub

        ''' <summary>
        ''' the angle of the hinge of this link, radians, in the MJCF convention.
        ''' </summary>
        ''' <remarks>
        ''' The rotation of the child relative to the parent, pulled back into the
        ''' frame of the hinge, is a rotation about the axis of the hinge. The
        ''' scalar part of that quaternion is therefore the cosine of half the
        ''' angle and the projection of its vector part onto the axis is the sine,
        ''' which is exactly the pair that the joint angle is read from - without
        ''' any assumption about the orientation of the parent.
        ''' </remarks>
        Public Function MeasureAngle() As Double
            If Parent Is Nothing Then
                Return 0
            End If

            ' the world orientation of a link is parent * quat * joint (the
            ' row vector order of the quaternion product, see ApplyPose), so the
            ' joint rotation is whatever is left after the parent and the quat of
            ' the body are cancelled from the left
            Dim parentRotation As Quaternion = Phys.Quat(Parent.Body.Orientation)
            Dim rotation As Quaternion = Phys.Quat(Body.Orientation)
            Dim relative As Quaternion = Quaternion.Conjugate(parentRotation) * rotation
            Dim hinge As Quaternion = Quaternion.Conjugate(Node.FrameRotation) * relative
            Dim axis As Vector3 = Node.JointAxis
            Dim sine As Double = hinge.X * axis.X + hinge.Y * axis.Y + hinge.Z * axis.Z
            Dim cosine As Double = hinge.W

            ' q and -q are the same rotation, and atan2(sin, cos) reports the angle of
            ' the negated quaternion as an angle beyond pi: a hinge that is a quarter
            ' of a turn away from its target can read 6.6 rad instead of -0.3, the
            ' servo saturates in the wrong direction and the leg flips. Pick the
            ' representative with a non negative scalar part first.
            If cosine < 0 Then
                sine = -sine
                cosine = -cosine
            End If

            Return 2.0 * Math.Atan2(sine, cosine)
        End Function

        ''' <summary>
        ''' the angular speed of the hinge, rad/s, positive when the angle grows
        ''' </summary>
        Public Function MeasureSpeed() As Double
            If Parent Is Nothing Then
                Return 0
            End If

            Dim worldAxis As Vector3 = Vector3.Transform(Node.JointAxis, Phys.Quat(Body.Orientation))
            Dim relative As Vector3 = Phys.Vec(Body.AngularVelocity) - Phys.Vec(Parent.Body.AngularVelocity)

            Return Vector3.Dot(relative, worldAxis)
        End Function

        ''' <summary>
        ''' apply a torque about the axis of the hinge, on the child and on the
        ''' parent with the opposite sign
        ''' </summary>
        ''' <remarks>
        ''' The torque is delivered as the angular impulse of the sub step
        ''' (``dω = I⁻¹ · τ · dt``) instead of being accumulated through
        ''' <c>RigidBody3D.ApplyTorque</c>.
        '''
        ''' The engine clears the accumulators at the start of every sub step, and a
        ''' hook that runs before that clearing contributes nothing; even inside the
        ''' hook the torque never reached the integration of the duck, which left
        ''' every hinge frozen at the pose it was assembled in - a duck that stands
        ''' perfectly still and answers neither the keyboard nor the policy. The
        ''' impulse form is applied directly to the angular velocity of the two
        ''' bodies, which the sub step integrates and the hinge constraint then
        ''' projects back onto the axis, so the joint keeps exactly one degree of
        ''' freedom.
        ''' </remarks>
        Public Sub ApplyTorque(torque As Double, dt As Double)
            If Parent Is Nothing OrElse torque = 0 Then
                Return
            End If

            Dim worldAxis As Vector3 = Vector3.Transform(Node.JointAxis, Phys.Quat(Body.Orientation))

            Call Body.Wake()
            Call Parent.Body.Wake()

            Call Body.ApplyTorque(Phys.Vec(worldAxis * CSng(torque)))
            Call Parent.Body.ApplyTorque(Phys.Vec(worldAxis * CSng(-torque)))
        End Sub
    End Class

    ''' <summary>
    ''' The tuning of the physics world and of the servo, externalised so that a
    ''' gait that the ported solver makes unstable can be corrected without a
    ''' rebuild.
    ''' </summary>
    Public Class PhysicsTuning
        ''' <summary>the solver iterations of one sub step</summary>
        Public Property Iterations As Integer = 24
        ''' <summary>the sub steps of one control step</summary>
        Public Property Substeps As Integer = 4
        ''' <summary>a uniform scale on the actuator force, 1 = the MJCF value</summary>
        ''' <remarks>
        ''' The MJCF asks for 0.96 Nm and the stand pose needs most of it: the duck
        ''' weighs 737 g and its feet are 54 x 41 mm convex hulls, so a solver that
        ''' spreads the weight over a handful of contact points needs more authority
        ''' than the reference run to keep the knees from folding. The scale is the
        ''' one knob that trades fidelity for a duck that can actually be steered.
        ''' </remarks>
        Public Property ForceScale As Double = 1.0
        ''' <summary>a uniform scale on the joint damping, 1 = the MJCF value</summary>
        Public Property DampingScale As Double = 1.0
        ''' <summary>
        ''' run the drive hook after the gravity of the sub step instead of before it
        ''' </summary>
        ''' <remarks>
        ''' The reference solver puts the actuator in the same acceleration equation as
        ''' the bias force. With the hook in front of the gravity the drive corrects a
        ''' velocity that the gravity then changes again, which is one of the reasons
        ''' the servo of this port never settles at the rest state of the reference.
        ''' </remarks>
        Public Property HooksAfterGravity As Boolean = False

        ''' <summary>
        ''' the sub step hooks run after the gravity of that sub step
        ''' </summary>
        ''' <remarks>
        ''' The velocity servo writes a joint speed, and the step used to apply the
        ''' gravity after it: every sub step the servo's number was replaced by the
        ''' gravity of the links it had just placed, so the joints slowly sagged no
        ''' matter the gain (measured: hip pitch 0.02 -> 0.78 rad of error in ten
        ''' steps at gain 30, and 0.81 rad at gain 300).
        ''' </remarks>

        ''' <summary>
        ''' drive the hinges with an actuator constraint of the solver instead of the
        ''' velocity servo of the hook
        ''' </summary>
        ''' <remarks>
        ''' This is the structure the reference model uses: the servo is a force that
        ''' enters the same equation as the gravity and the joints, so the speeds it
        ''' produces are physical and the observation of the policy stops being
        ''' saturated by a value that no reference run ever shows.
        ''' </remarks>
        ''' <remarks>
        ''' Off by default because the duck still needs the velocity servo to stay on
        ''' its feet (250/250 against 22/250). With it on the trajectory is far closer
        ''' to the reference - the worst joint angle over four seconds drops from
        ''' 2.9561 rad to 0.1642 and the worst joint speed from 21.3114 rad/s to
        ''' 0.1326 - which says the structure is right and what is missing next is the
        ''' balance of the duck itself, not the actuator.
        ''' </remarks>
        Public Property UseActuatorConstraint As Boolean = False
        ''' <summary>
        ''' the derivative gain of the servo, as a fraction of the proportional
        ''' one, used when the document asks for kv = 0
        ''' </summary>
        ''' <remarks>
        ''' The MJCF asks for kv = 0 and leaves the damping to the implicit
        ''' integration of the reference solver. An explicit PD at a 5 ms sub step
        ''' needs a real derivative term: 0.5 of the proportional gain puts the loop
        ''' well past critical damping for an armature of 1.8e-3, which is what keeps
        ''' a force servo from ringing at this scale.
        ''' </remarks>
        Public Property VelocityGain As Double = 0.0
        ''' <summary>the Baumgarte factor of the contacts</summary>
        Public Property Baumgarte As Double = 0.05
        ''' <summary>the allowed penetration, metres</summary>
        Public Property Slop As Double = 0.005
        ''' <summary>
        ''' the time constant of the soft contact, seconds (mujoco solref[0])
        ''' </summary>
        ''' <remarks>
        ''' 0.02 s is the default solref of the reference model: a contact that is
        ''' stiff enough to carry the weight and soft enough not to launch a 737 g
        ''' robot into the air on the first millimetre of penetration.
        ''' </remarks>
        Public Property ContactTimeConstant As Double = 0.02
        ''' <summary>the damping ratio of the soft contact (mujoco solref[1])</summary>
        Public Property ContactDampingRatio As Double = 1.0

        ''' <summary>
        ''' keep a plane contact whose points are all inside the margin (no penetration
        ''' yet), so that the patch spans the whole sole before the first touch
        ''' </summary>
        Public Property MarginOnlyContacts As Boolean = False

        ''' <summary>
        ''' stop the constraint iterations once two rounds no longer change any
        ''' velocity by more than this, 0 = a fixed count
        ''' </summary>
        ''' <remarks>
        ''' Measured at 0.01: the loop stops after one round and the policy phases get
        ''' worse (standing 250 -> 108), which says the iterations were not the
        ''' bottleneck - the rows converge, the structure does not. Left at 0 so the
        ''' count stays a diagnostic instead of a tuning knob.
        ''' </remarks>
        Public Property SolverTolerance As Double = 0.0
        ''' <summary>the friction of the floor and of the walls</summary>
        Public Property EnvironmentFriction As Double = 0.7
        ''' <summary>the friction of the duck itself</summary>
        Public Property DuckFriction As Double = 1.0
    End Class

    ''' <summary>
    ''' The programmatic rebuild of the robot in the physics engine: fifteen rigid
    ''' bodies, fourteen hinges, one force servo and one convex collision proxy per
    ''' link. MuJoCo compiles the MJCF into an articulated body tree with a reduced
    ''' coordinate solver; the engine has rigid bodies and constraints only, so the
    ''' tree is rebuilt as fifteen bodies chained by fourteen hinges, and the
    ''' gravity, the joint damping, the armature and the position servo are
    ''' re-applied by hand. Both frames are the MJCF ones (z up): the engine is told
    ''' that gravity points along minus z and that the floor is the plane z = 0.
    ''' </summary>
    Public Class DuckPhysics : Implements IDisposable

        Public ReadOnly Property Skeleton As DuckSkeleton
        Public ReadOnly Property World As New Eng.PhysicsWorld3D()
        Public ReadOnly Property Links As New List(Of BodyLink)()
        Public ReadOnly Property Drive As New JointDriveHook()
        Public Property Tuning As New PhysicsTuning()

        ' the evaluation order of the tree: every parent before its children
        Private ReadOnly m_order As New List(Of Integer)()

        Public Property Trunk As BodyLink
        Public Property LeftAnkle As BodyLink
        Public Property RightAnkle As BodyLink

        ''' <summary>how many links got a real collision proxy</summary>
        Public Property ColliderCount As Integer

        Public Sub New(skeleton As DuckSkeleton, meshes As MeshLibrary, Optional tuning As PhysicsTuning = Nothing)
            Me.Skeleton = skeleton

            If tuning IsNot Nothing Then
                Me.Tuning = tuning
            End If

            Call ConfigureWorld()
            Call CreateBodies(meshes)

            ' the zero angle of a hinge is captured at its construction, so the
            ' tree has to stand in the pose of the MJCF document (every joint at
            ' zero) before the hinges are created, otherwise every angle of every
            ' link that sits below a rotated parent is measured against a wrong
            ' reference
            Call ApplyPose(SpawnTrunk(), Quaternion.Identity, New Double(SimConstants.NumJoints - 1) {})

            Call CreateHinges()

            World.Add(Drive)

            ' the relation between the axis the servo pushes along and the angle it
            ' reads is a property of the assembly, not of the pose: measure it once
            Call Drive.CalibrateSigns()

            Call Spawn()
        End Sub

        Private Sub ConfigureWorld()
            World.Gravity = Phys.Vec(0, 0, -9.81)
            World.FixedDt = SimConstants.CtrlDt
            World.Substeps = Math.Max(1, Tuning.Substeps)
            World.Iterations = Math.Max(4, Tuning.Iterations)
            World.Baumgarte = Tuning.Baumgarte
            World.Slop = Tuning.Slop
            World.ContactTimeConstant = Tuning.ContactTimeConstant
            World.ContactDampingRatio = Tuning.ContactDampingRatio
            World.HooksAfterGravity = Tuning.HooksAfterGravity
            World.SolverTolerance = Tuning.SolverTolerance
            World.MarginOnlyContacts = Tuning.MarginOnlyContacts
            ' a walking duck comes to rest for a moment on every step: letting it
            ' sleep would freeze the very servo that keeps it upright
            World.AllowSleep = False
            World.WarmStarting = True
        End Sub

        Private Sub CreateBodies(meshes As MeshLibrary)
            Dim duckMaterial As New Eng.PhysicsMaterial With {
                .Friction = Tuning.DuckFriction,
                .Restitution = 0.0
            }

            For Each node As SkeletonNode In Skeleton.Nodes
                Dim link As New BodyLink With {.Node = node}
                Dim shape As Collision3D.Collider3D = Nothing
                Dim proxy As Collision3D.ConvexMesh3D = Nothing

                If node.CollisionGeoms.Count > 0 Then
                    proxy = BuildProxy(node, meshes)

                    If proxy IsNot Nothing AndAlso proxy.Vertices.Length >= 4 Then
                        shape = New Collision3D.ConvexPolyhedronCollider3D(proxy)
                        link.Collider = proxy
                        ColliderCount += 1
                    End If
                End If

                If shape Is Nothing Then
                    ' a link without a collision geom still has to own a shape:
                    ' a millimetre sphere that collides with nothing at all
                    shape = New Collision3D.SphereCollider3D(0.001)
                End If

                ' MuJoCo has no global damping: the joint damping of the MJCF is
                ' applied by the servo hook, while the per body damping of the
                ' engine would silently brake the gait
                Dim body As New Eng.RigidBody3D(shape, node.Mass, duckMaterial) With {
                    .Label = node.Name,
                    .Position = Phys.Vec(0, 0, 0),
                    .Orientation = Phys.Quat(node.FrameRotation),
                    .LinearDamping = 0,
                    .AngularDamping = 0,
                    .MaxSpeed = 0,
                    .MaxAngularSpeed = 0,
                    .InertiaLocal = Phys.WithArmature(Phys.Inertia(node.Inertia), node.JointAxis, ArmatureOf(node))
                }

                ' the duck collides with the arena and with the ball, never with
                ' itself (the MJCF only enables the jaw/head self collision)
                body.CollisionGroup = Groups.Duck
                body.CollisionMask = If(node.CollisionGeoms.Count > 0, Groups.Environment Or Groups.Ball, 0)

                Call World.Add(body)

                link.Body = body
                Call Links.Add(link)
            Next

            For Each link As BodyLink In Links
                If link.Node.ParentIndex >= 0 Then
                    link.Parent = Links(link.Node.ParentIndex)
                    Call link.Parent.Children.Add(link)
                Else
                    Me.Trunk = link
                End If

                If String.Equals(link.Node.Name, "ankle_left", StringComparison.Ordinal) Then
                    Me.LeftAnkle = link
                ElseIf String.Equals(link.Node.Name, "ankle_right", StringComparison.Ordinal) Then
                    Me.RightAnkle = link
                End If
            Next

            Call BuildOrder()
        End Sub

        ''' <summary>
        ''' the reflected inertia of the rotor of a hinge.
        ''' </summary>
        ''' <remarks>
        ''' The MJCF writes it in the default class of the joints; when the parser
        ''' does not reach a class (or a document leaves it out) the shared value of
        ''' the four motors of this robot is used instead, because a hinge without
        ''' armature is unusable at this scale: the link inertia is 1e-6 and the
        ''' servo torque 0.06 Nm.
        ''' </remarks>
        Private Shared Function ArmatureOf(node As SkeletonNode) As Double
            If node.JointArmature > 0 Then
                Return node.JointArmature
            End If

            Return SimConstants.DefaultArmature
        End Function

        ''' <summary>the viscous damping of the hinge, with the same fallback</summary>
        Private Shared Function DampingOf(node As SkeletonNode) As Double
            If node.JointDamping > 0 Then
                Return node.JointDamping
            End If

            Return SimConstants.DefaultJointDamping
        End Function

        ''' <summary>the dry friction of the hinge, with the same fallback</summary>
        Private Shared Function FrictionOf(node As SkeletonNode) As Double
            If node.JointFrictionLoss > 0 Then
                Return node.JointFrictionLoss
            End If

            Return SimConstants.DefaultJointFrictionLoss
        End Function

        ''' <summary>
        ''' the convex hull of every collision geom of a link, merged into one
        ''' proxy that is measured from the center of mass of the link
        ''' </summary>
        Private Shared Function BuildProxy(node As SkeletonNode, meshes As MeshLibrary) As Collision3D.ConvexMesh3D
            ' tried keeping only the hull of the sole plate of an ankle, so that the
            ' foot is a flat plate instead of the wedge that the merge of the plate and
            ' the motor produces. The footprint did not grow (31 mm of a 54 mm sole),
            ' the open loop stand stayed at 57/200 and the standing phase of the policy
            ' collapsed from 250 to 31, so the merge is kept and the balance problem has
            ' to be solved where it actually is: the centre of mass.
            Dim merged As New List(Of Vector3)()

            For Each geom As MjcfGeom In node.CollisionGeoms
                Dim corners As Vector3() = meshes.BuildCollisionHull(geom.Mesh, geom.Position, geom.Rotation,
                                                                     -node.InertialPosition)
                Call merged.AddRange(corners)
            Next

            ' the collision class of the MJCF only covers the parts of the CAD that
            ' the mujoco model needed, and it does not always reach the sole: the
            ' proxy of an ankle stopped 10.8 mm above the visual foot, the duck
            ' hovered that far over the floor and the contacts never touched, so
            ' the servo pushed the legs in the air and the robot bounced around
            ' instead of standing. The visual meshes are the complete CAD, so their
            ' hulls are merged in as well - a rigid transform keeps a hull convex,
            ' and the merge of a few hulls is still one convex proxy per link.
            For Each geom As KinematicGeom In node.Geoms
                Dim corners As Vector3() = meshes.BuildCollisionHull(geom.Mesh, geom.Position, geom.Rotation,
                                                                     -node.InertialPosition)
                Call merged.AddRange(corners)
            Next

            If merged.Count < 4 Then
                Return Nothing
            End If

            Dim hull As ConvexHullResult = ConvexHull.Compute(merged.ToArray())

            Return New Collision3D.ConvexMesh3D(Phys.Vertices(hull.Vertices), hull.Faces)
        End Function

        Private Sub CreateHinges()
            ' the servo either lives in the solver as a constraint, or in the hook as
            ' a velocity servo; the two can not run at the same time
            Drive.VelocityServo = Not Tuning.UseActuatorConstraint

            ' the inertia of a link is written after the body is constructed, and the
            ' inverse of it is cached the first time the solver asks for it: without
            ' this the whole tree keeps running on the linked masses without their
            ' rotor armature (measured inverse 1.2e5 against the expected 549)
            For Each link As BodyLink In Links
                If link.Body IsNot Nothing Then
                    Call link.Body.InvalidateInertiaCache()
                End If
            Next
            For i As Integer = 0 To Skeleton.Driven.Count - 1
                Dim node As SkeletonNode = Skeleton.Driven(i)
                Dim link As BodyLink = Links(node.Index)
                Dim parent As BodyLink = link.Parent

                If parent Is Nothing Then
                    Continue For
                End If

                ' the anchor of a hinge is measured from the center of mass of
                ' the two bodies, which is why both offsets are subtracted here
                Dim anchorA As Vector3 = node.FramePosition - parent.Node.InertialPosition
                Dim anchorB As Vector3 = -node.InertialPosition
                Dim axisA As Vector3 = Vector3.Transform(node.JointAxis, node.FrameRotation)
                Dim axisB As Vector3 = node.JointAxis

                Dim hinge As New Joints3D.RevoluteJoint3D(
                    parent.Body, link.Body,
                    Phys.Vec(anchorA), Phys.Vec(anchorB),
                    Phys.Vec(Vector3.Normalize(axisA)), Phys.Vec(Vector3.Normalize(axisB)),
                    node.JointLimits(0), node.JointLimits(1),
                    node.JointName)

                ' the travel is applied as a soft spring by the drive hook: the
                ' limit of the constraint would judge the angle in the reference
                ' frame of the parent, which only coincides with the joint angle
                ' while the parent is unrotated
                hinge.EnableLimit = False

                ' the viscous damping of the joint is an implicit decay inside the
                ' constraint now, the same place where mujoco keeps dof_damping.
                ' It is only armed for the force servo: the velocity servo of this
                ' port writes the joint speed outright, and a decay of 13% per sub
                ' step on top of it simply eats the command (measured: the bench
                ' dropped to 22/21/19 with the damping on)
                If Not Drive.VelocityServo Then
                    hinge.Damping = DampingOf(node) * Tuning.DampingScale
                End If

                ' the anchor half of the hinge is sound: with the servo switched off
                ' a single hinge holds its two anchors within 0.000 mm for 1.2 s of
                ' free simulation (see the G phase of the bench). What tore the duck
                ' apart was the drive pulling every joint towards Targets = 0 while
                ' the pose is assembled at DEFAULT_POSE, so the servo torque has to
                ' be treated as the actuator it is, not as a constraint

                ' the velocity pass only removes the *relative* spin across the
                ' axis, it can not undo an angle that the two axes already have:
                ' with the armature of these small motors the links drift out of
                ' alignment within a few steps and the hinge loses its degree of
                ' freedom (the duck then wanders with its legs folded). The
                ' position pass is the only thing that pulls the axes back.
                ' the soft contact (World.ContactTimeConstant) already keeps the two
                ' axes together through the velocity pass; a position level rotation
                ' on top of it writes straight into Orientation without touching the
                ' angular velocity, which is a free injection of energy every sub
                ' step and makes the duck hop
                hinge.AngularCorrection = 0.0

                Call World.Add(hinge)

                link.Hinge = hinge

                ' the mjcf asks for kv = 0 and relies on its implicit integration
                ' plus the joint damping to keep the servo calm. A plain explicit
                ' pd at this scale rings: the derivative term is added here as a
                ' fraction of the proportional one, which is the damping ratio of a
                ' critically damped drive at the armature of these motors.
                Dim kv As Double = If(node.Kv > 0, node.Kv, node.Kp * Tuning.VelocityGain)

                Call Drive.Add(link,
                               node.Kp * Tuning.ForceScale,
                               kv * Tuning.ForceScale,
                               DampingOf(node) * Tuning.DampingScale,
                               FrictionOf(node),
                               node.JointLimits(0), node.JointLimits(1),
                               node.ForceMin, node.ForceMax)

                ' tried registering the servo as a constraint of the world so that it
                ' is projected in every iteration together with the joint and the
                ' ground. It over-constrains: the constraint writes the target speed
                ' outright on each of the 24 iterations, the hinge and the contacts
                ' never get to redistribute anything, and the bench collapsed to
                ' 23/33/60 (against 55/244/118 for the plain hook). The class is kept
                ' for a version that accumulates an impulse per iteration instead of
                ' setting the speed.
                Drive.ServoRegistered = False

                If Tuning.UseActuatorConstraint Then
                    Dim actuator As New Joints3D.ActuatorConstraint3D With {
                        .A = parent.Body,
                        .B = link.Body,
                        .Kp = node.Kp * Tuning.ForceScale,
                        .Kv = kv * Tuning.ForceScale,
                        .ForceMin = node.ForceMin,
                        .ForceMax = node.ForceMax
                    }

                    Call World.Add(actuator)
                    Call Drive.ActuatorConstraints.Add(actuator)
                End If
            Next
        End Sub

        Private Sub BuildOrder()
            m_order.Clear()

            Dim visited As New HashSet(Of Integer)()

            For Each link As BodyLink In Links
                Call Visit(link, visited)
            Next
        End Sub

        Private Sub Visit(link As BodyLink, visited As HashSet(Of Integer))
            If link.Parent IsNot Nothing AndAlso Not visited.Contains(link.Parent.Node.Index) Then
                Call Visit(link.Parent, visited)
            End If

            If visited.Add(link.Node.Index) Then
                Call m_order.Add(link.Node.Index)
            End If
        End Sub

        ''' <summary>
        ''' put the duck back on its feet at the spawn point, with the joint
        ''' angles of the STAND keyframe and no velocity
        ''' </summary>
        ''' <summary>
        ''' the lowest point of any collision proxy of the duck, world z, metres
        ''' </summary>
        Private Function LowestHullPoint() As Double
            Dim low As Double = Double.MaxValue

            For Each link As BodyLink In Links
                If link.Collider Is Nothing Then
                    Continue For
                End If

                Dim world As Matrix4x4 = Phys.WorldOf(Phys.Vec(link.Body.Position), Phys.Quat(link.Body.Orientation))

                For Each corner As Engine.Physics.Vector3 In link.Collider.Vertices
                    low = Math.Min(low, Vector3.Transform(Phys.Vec(corner), world).Z)
                Next
            Next

            Return If(low = Double.MaxValue, 0, low)
        End Function

        Public Sub Spawn(Optional x As Double = SimConstants.SpawnX,
                         Optional y As Double = SimConstants.SpawnY,
                         Optional z As Double = SimConstants.SpawnZ)
            Call ApplyPose(SpawnTrunk(x, y, z), Quaternion.Identity, SimConstants.DefaultPose)

            ' put the soles exactly on the floor. The keyframe height puts the lowest
            ' collision point 10.8 mm above the ground - measured, not assumed - and a
            ' duck that starts a centimetre in the air lands with a bump that the
            ' servo has to absorb before the first control step is even done
            Dim low As Double = LowestHullPoint()

            If Math.Abs(low - SimConstants.SpawnLift) > 1.0E-06 Then
                Dim drop As Vector3 = New Vector3(0, 0, CSng(low - SimConstants.SpawnLift))

                For Each body As BodyLink In Links
                    body.Body.Position = Phys.Vec(Phys.Vec(body.Body.Position) - drop)
                Next

                For Each body As BodyLink In Links
                    Call body.UpdatePose()
                Next
            End If

            ' the targets have to match the pose that was just assembled: a fresh
            ' drive holds all zeros, and one sub step with a 0.45 rad error at
            ' kp 0.55 is a saturated 0.25 Nm on a six gram link, that is 660 rad/s².
            ' The duck would tear itself apart before the first control step ever
            ' ran - which is exactly what the free rotation of the bench showed
            Call Drive.SetTargets(SimConstants.DefaultPose)

            ' the drive needs the whole tree to walk the subtree of a hinge, and the
            ' gravity of the world to weigh it
            Drive.Bodies = Links
            Drive.Gravity = Phys.Vec(World.Gravity)

            ' the duck was teleported: the warm start cache of the previous pose
            ' would otherwise push the whole robot away with the support impulse of
            ' the footing it just left
            Call World.ClearContactCache()
            Call World.ClearVelocities()
        End Sub

        ''' <summary>
        ''' the center of mass of the trunk when the body frame stands at the
        ''' spawn point of the MJCF keyframe
        ''' </summary>
        Private Function SpawnTrunk(Optional x As Double = SimConstants.SpawnX,
                                    Optional y As Double = SimConstants.SpawnY,
                                    Optional z As Double = SimConstants.SpawnZ) As Vector3
            Return New Vector3(CSng(x), CSng(y), CSng(z)) +
                   Vector3.Transform(Skeleton.Root.InertialPosition, Quaternion.Identity)
        End Function

        ''' <summary>
        ''' place the whole tree from the pose of the trunk and the joint angles
        ''' (forward kinematics, no physics)
        ''' </summary>
        Public Sub ApplyPose(trunkCom As Vector3, trunkRotation As Quaternion, angles As Double())
            For Each index As Integer In m_order
                Dim link As BodyLink = Links(index)
                Dim parent As BodyLink = link.Parent

                If parent Is Nothing Then
                    link.Body.Position = Phys.Vec(trunkCom)
                    link.Body.Orientation = Phys.Quat(trunkRotation)
                    link.Body.Velocity = Phys.Vec(0, 0, 0)
                    link.Body.AngularVelocity = Phys.Vec(0, 0, 0)
                    Call link.UpdatePose()

                    Continue For
                End If

                Dim angle As Double = 0

                If link.Node.ActuatorIndex >= 0 AndAlso link.Node.ActuatorIndex < angles.Length Then
                    angle = angles(link.Node.ActuatorIndex)
                End If

                ' the frame of the child in the world: the parent rotation carries
                ' the placement of the body, and the rotation of the body and the
                ' turn of its hinge are composed on top of it.
                '
                ' System.Numerics multiplies quaternions so that the factor on the
                ' right is applied first (v * M(qa * qb) = (v * M(qb)) * M(qa)),
                ' which is the opposite of the column vector order of the mujoco
                ' documentation: the product that reproduces
                ' R(child) = R(parent) * R(quat) * R(joint) is written
                ' q(parent) * q(quat) * q(joint) here.
                '
                ' With the factors in the obvious order every link of the tree is
                ' laid out in a frame that is rotated by the inverse of its own
                ' quat, and the duck assembles as a heap: the feet of the two legs
                ' end up at different heights and the head tips backwards.
                Dim parentRotation As Quaternion = Phys.Quat(parent.Body.Orientation)
                Dim turn As Quaternion = Phys.AxisAngle(link.Node.JointAxis, angle)
                Dim childRotation As Quaternion = parentRotation * link.Node.FrameRotation * turn

                ' the frame origin inside the frame of the parent, then the center
                ' of mass of the link from its frame - the engine keeps the center
                ' of mass, not the origin of the frame
                Dim parentFrame As Vector3 = Phys.Vec(parent.Body.Position) -
                                             Vector3.Transform(parent.Node.InertialPosition, parentRotation)
                Dim childFrame As Vector3 = parentFrame + Vector3.Transform(link.Node.FramePosition, parentRotation)

                link.Body.Orientation = Phys.Quat(childRotation)
                link.Body.Position = Phys.Vec(childFrame + Vector3.Transform(link.Node.InertialPosition, childRotation))
                link.Body.Velocity = Phys.Vec(0, 0, 0)
                link.Body.AngularVelocity = Phys.Vec(0, 0, 0)
                Call link.UpdatePose()
            Next
        End Sub

        ''' <summary>
        ''' recompute the cached body frames from the state of the engine
        ''' </summary>
        ''' <remarks>
        ''' The cache is written when the pose is applied and after every step.
        ''' Anything that moves a body in between - the constraint solver of a
        ''' freshly added joint snaps the anchors of the two bodies together, for
        ''' instance - leaves the cache behind, so the renderer has to be able to
        ''' ask for a refresh before it reads the world matrices.
        ''' </remarks>
        Public Sub RefreshPose()
            For Each link As BodyLink In Links
                Call link.UpdatePose()
            Next
        End Sub

        ''' <summary>
        ''' advance the world by one control step (four sub steps of five
        ''' milliseconds, the decimation of the MJCF runtime)
        ''' </summary>
        ''' <summary>
        ''' the weight that every hinge has to carry, as a feed forward torque
        ''' </summary>
        ''' <remarks>
        ''' A pure position servo has to build a steady state error before it lifts
        ''' anything: the ankle of this duck needs about 0.2 N·m to hold the body up,
        ''' and at kp 0.55 that is a 0.36 rad sag - the knee folds, the trunk sinks
        ''' and the duck tips over before the gait ever starts. The reference runtime
        ''' does not show this because mujoco evaluates the actuator together with the
        ''' constraint in one implicit step; here the offset has to be cancelled
        ''' explicitly.
        '''
        ''' The torque is the gravity of the whole subtree of the hinge taken about
        ''' the hinge axis: <c>τ = Σ (r × m·g) · a</c>, r from the hinge to the center
        ''' of mass of every link below it.
        ''' </remarks>
        Private Sub UpdateGravityFeedforward()
            Dim gravity As Vector3 = Phys.Vec(World.Gravity)

            For Each link As BodyLink In Links
                Dim index As Integer = link.Node.ActuatorIndex

                If index < 0 OrElse index >= Drive.GravityFeedforward.Length Then
                    Continue For
                End If

                Dim axis As Vector3 = Vector3.Transform(link.Node.JointAxis, Phys.Quat(link.Body.Orientation))
                Dim pivot As Vector3 = link.FramePosition
                Dim torque As Double = 0

                For Each below As BodyLink In SubtreeOf(link)
                    Dim mass As Double = below.Node.Mass

                    If mass <= 0 Then
                        Continue For
                    End If

                    Dim arm As Vector3 = Phys.Vec(below.Body.Position) - pivot
                    Dim weight As Vector3 = gravity * CSng(mass)

                    torque += Vector3.Dot(Vector3.Cross(arm, weight), axis)
                Next

                Drive.GravityFeedforward(index) = torque
            Next
        End Sub

        ''' <summary>the link and every link below it in the tree</summary>
        Private Function SubtreeOf(link As BodyLink) As List(Of BodyLink)
            Dim found As New List(Of BodyLink)()

            For Each other As BodyLink In Links
                Dim walk As BodyLink = other

                While walk IsNot Nothing
                    If Object.ReferenceEquals(walk, link) Then
                        Call found.Add(other)

                        Exit While
                    End If

                    walk = walk.Parent
                End While
            Next

            Return found
        End Function

        ''' <summary>is this link touching the ground right now?</summary>
        Private Function IsFootDown(link As BodyLink) As Boolean
            If link Is Nothing Then
                Return False
            End If

            For Each m In World.Contacts
                If Object.ReferenceEquals(m.A, link.Body) OrElse Object.ReferenceEquals(m.B, link.Body) Then
                    Return True
                End If
            Next

            Return False
        End Function

        ''' <summary>
        ''' the weight of a leg is only carried while its foot is on the ground
        ''' </summary>
        Private Sub UpdateSupportFactors()
            If Not Drive.ContactAdaptive Then
                Return
            End If

            Dim leftDown As Boolean = IsFootDown(LeftAnkle)
            Dim rightDown As Boolean = IsFootDown(RightAnkle)

            For Each link As BodyLink In Links
                Dim index As Integer = link.Node.ActuatorIndex

                If index < 0 OrElse index >= Drive.SupportFactor.Length Then
                    Continue For
                End If

                Dim hasLeft As Boolean = False
                Dim hasRight As Boolean = False

                For Each below As BodyLink In SubtreeOf(link)
                    If Object.ReferenceEquals(below, LeftAnkle) Then
                        hasLeft = True
                    End If

                    If Object.ReferenceEquals(below, RightAnkle) Then
                        hasRight = True
                    End If
                Next

                Dim factor As Double = 1.0

                If hasLeft AndAlso Not hasRight Then
                    factor = If(leftDown, 1.0, SimConstants.SwingSupportFactor)
                ElseIf hasRight AndAlso Not hasLeft Then
                    factor = If(rightDown, 1.0, SimConstants.SwingSupportFactor)
                ElseIf hasLeft AndAlso hasRight Then
                    factor = If(leftDown OrElse rightDown, 1.0, SimConstants.SwingSupportFactor)
                End If

                Drive.SupportFactor(index) = factor
            Next
        End Sub

        Public Sub Advance()
            Call UpdateSupportFactors()
            Call World.Step(SimConstants.CtrlDt)

            For Each link As BodyLink In Links
                Call link.UpdatePose()
            Next
        End Sub

        ''' <summary>
        ''' the joint angle of the servo at the given actuator index
        ''' </summary>
        Public Function JointAngle(index As Integer) As Double
            Return Drive.AngleOf(index)
        End Function

        ''' <summary>
        ''' the joint speed of the servo at the given actuator index
        ''' </summary>
        Public Function JointSpeed(index As Integer) As Double
            Return Drive.SpeedOf(index)
        End Function

        ''' <summary>
        ''' the z component of the world gravity, expressed in the frame of the
        ''' trunk: -1 is upright, +1 is on its back, the fall detector of the
        ''' browser build
        ''' </summary>
        Public Function ProjectedGravityZ() As Double
            Dim rotation As Quaternion = Phys.Quat(Trunk.Body.Orientation)
            Dim gravity As Vector3 = Vector3.Transform(New Vector3(0, 0, -1), Quaternion.Conjugate(rotation))

            Return gravity.Z
        End Function

        ''' <summary>the world velocity of the trunk of the duck</summary>
        Public Function TrunkVelocity() As Vector3
            Return Phys.Vec(Trunk.Body.Velocity)
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Call World.Clear()
            Call Links.Clear()
        End Sub
    End Class

    ''' <summary>
    ''' the collision groups of the scene: the duck never collides with itself,
    ''' everything collides with the arena
    ''' </summary>
    Public Module Groups
        Public Const Duck As Integer = 1
        Public Const Ball As Integer = 2
        Public Const Environment As Integer = 4
    End Module
End Namespace
