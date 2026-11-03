Imports System.IO
Imports System.Numerics
Imports MicroduckLab.Assets

Namespace Core

    ''' <summary>
    ''' One link of the robot: the placement of its frame inside the parent, the
    ''' meshes it renders and the hinge (or the rigid weld) that connects it.
    ''' </summary>
    Public Class SkeletonNode
        Public Property Name As String
        Public Property Index As Integer
        Public Property ParentIndex As Integer = -1

        ''' <summary>the origin of the body frame inside the parent frame</summary>
        Public Property FramePosition As Vector3 = Vector3.Zero
        ''' <summary>the rotation of the body frame relative to the parent frame</summary>
        Public Property FrameRotation As Quaternion = Quaternion.Identity

        ''' <summary>the center of mass inside the body frame</summary>
        Public Property InertialPosition As Vector3 = Vector3.Zero
        Public Property Mass As Double = 0.01
        ''' <summary>ixx, iyy, izz, ixy, ixz, iyz about the center of mass</summary>
        Public Property Inertia As Double() = {1.0E-06, 1.0E-06, 1.0E-06, 0, 0, 0}

        Public Property JointName As String
        Public Property JointAxis As Vector3 = Vector3.UnitZ
        Public Property JointLimits As Double() = {-Math.PI, Math.PI}
        Public Property JointDamping As Double = 0
        Public Property JointArmature As Double = 0
        Public Property JointFrictionLoss As Double = 0
        Public Property ActuatorIndex As Integer = -1

        ''' <summary>the position servo of the hinge, from the MJCF actuator</summary>
        Public Property Kp As Double = 0
        Public Property Kv As Double = 0
        Public Property ForceMin As Double = -1.0E+09
        Public Property ForceMax As Double = 1.0E+09

        ''' <summary>the render meshes of the body, in the render order of the document</summary>
        Public ReadOnly Property Geoms As New List(Of KinematicGeom)()
        ''' <summary>the collision geoms of the body, without the self collision ones</summary>
        Public ReadOnly Property CollisionGeoms As New List(Of MjcfGeom)()
        Public ReadOnly Property Children As New List(Of Integer)()

        Public ReadOnly Property HasJoint As Boolean
            Get
                Return ActuatorIndex >= 0
            End Get
        End Property
    End Class

    ''' <summary>
    ''' The merged view of ``kinematics.json`` and of the MJCF: the render meshes
    ''' and the body frames of the first, the mass, the inertia, the hinges and
    ''' the actuators of the second.
    ''' </summary>
    Public Class DuckSkeleton

        Public Property Loco As Locomotion = Locomotion.Legs
        Public ReadOnly Property Nodes As New List(Of SkeletonNode)()
        ''' <summary>the links that are driven by a policy output, in actuator order</summary>
        Public ReadOnly Property Driven As New List(Of SkeletonNode)()

        Public Property Root As SkeletonNode

        Public Const TrunkName As String = "trunk_base"

        Public ReadOnly Property TotalMass As Double
            Get
                Dim mass As Double = 0

                For Each node As SkeletonNode In Nodes
                    mass += node.Mass
                Next

                Return mass
            End Get
        End Property

        Public Shared Function Build(loco As Locomotion, kinetics As KinematicsDocument,
                                     model As MjcfDocument) As DuckSkeleton
            Dim skeleton As New DuckSkeleton With {.Loco = loco}

            For Each body As KinematicBody In kinetics.Bodies
                Dim source As MjcfBody = model.Bodies.FirstOrDefault(
                    Function(x) String.Equals(x.Name, body.Name, StringComparison.Ordinal))

                If source Is Nothing Then
                    Throw New InvalidDataException($"the body '{body.Name}' of the kinematics document is missing from the MJCF")
                End If

                Dim node As New SkeletonNode With {
                    .Name = body.Name,
                    .Index = skeleton.Nodes.Count,
                    .ParentIndex = body.ParentIndex,
                    .FramePosition = body.Position,
                    .FrameRotation = body.Rotation,
                    .InertialPosition = source.Inertial.Position,
                    .Mass = Math.Max(source.Inertial.Mass, 1.0E-06),
                    .Inertia = source.Inertial.Inertia,
                    .JointName = source.JointName,
                    .JointLimits = source.JointRange,
                    .JointDamping = source.JointDamping,
                    .JointArmature = source.JointArmature,
                    .JointFrictionLoss = source.JointFrictionLoss
                }

                ' the hinge axis of the document is expressed in the frame of the
                ' body that owns the joint
                If body.Joint IsNot Nothing Then
                    node.JointAxis = body.Joint.Axis
                    node.ActuatorIndex = body.Joint.ActuatorIndex

                    If body.Joint.Limits.Length >= 2 Then
                        node.JointLimits = body.Joint.Limits
                    End If
                ElseIf source.JointName IsNot Nothing Then
                    node.JointAxis = source.JointAxis
                End If

                Call node.Geoms.AddRange(body.Geoms)
                Call node.CollisionGeoms.AddRange(source.Geoms)

                If source.JointName IsNot Nothing Then
                    Dim actuator As MjcfActuator = model.ActuatorOf(source.JointName)

                    If actuator IsNot Nothing Then
                        node.Kp = actuator.Kp
                        node.Kv = actuator.Kv
                        node.ForceMin = actuator.ForceMin
                        node.ForceMax = actuator.ForceMax
                    End If
                End If

                Call skeleton.Nodes.Add(node)
            Next

            For Each node As SkeletonNode In skeleton.Nodes
                If node.ParentIndex >= 0 Then
                    Call skeleton.Nodes(node.ParentIndex).Children.Add(node.Index)
                Else
                    skeleton.Root = node
                End If
            Next

            ' the policy sees the joints in the order of the actuators, which is
            ' the order of JOINT_NAMES (the document numbers them already)
            Dim driven As List(Of SkeletonNode) = skeleton.Nodes.
                Where(Function(x) x.ActuatorIndex >= 0).
                OrderBy(Function(x) x.ActuatorIndex).
                ToList()

            If driven.Count <> SimConstants.NumJoints Then
                Throw New InvalidDataException($"the model drives {driven.Count} joints, expected {SimConstants.NumJoints}")
            End If

            For i As Integer = 0 To driven.Count - 1
                If driven(i).ActuatorIndex <> i Then
                    Throw New InvalidDataException("the actuator indices of the model are not contiguous")
                End If
            Next

            Call skeleton.Driven.AddRange(driven)

            Call skeleton.Validate()

            Return skeleton
        End Function

        ''' <summary>
        ''' check the two documents against each other before a physics body is
        ''' created out of them
        ''' </summary>
        Private Sub Validate()
            If Root Is Nothing Then
                Throw New InvalidDataException("the skeleton has no trunk")
            End If

            For i As Integer = 0 To SimConstants.NumJoints - 1
                Dim node As SkeletonNode = Driven(i)

                If node.Mass <= 0 Then
                    Throw New InvalidDataException($"the link '{node.Name}' has no mass")
                End If

                If node.JointLimits.Length < 2 OrElse node.JointLimits(0) >= node.JointLimits(1) Then
                    Throw New InvalidDataException($"the hinge '{node.Name}' has no travel")
                End If
            Next
        End Sub

        ''' <summary>
        ''' the link of the given joint name
        ''' </summary>
        Public Function Find(name As String) As SkeletonNode
            Return Nodes.FirstOrDefault(Function(x) String.Equals(x.Name, name, StringComparison.Ordinal))
        End Function
    End Class
End Namespace
