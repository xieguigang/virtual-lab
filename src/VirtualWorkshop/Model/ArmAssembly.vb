Imports System.IO
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Model

    ''' <summary>一个刚体姿态（世界坐标）。</summary>
    Public Structure Pose3D

        Public Position As Vector3
        Public Rotation As Quaternion

        Sub New(position As Vector3, rotation As Quaternion)
            Me.Position = position
            Me.Rotation = rotation
        End Sub

        Public Shared Function Identity() As Pose3D
            Return New Pose3D(Vector3.Zero, Quaternion.Identity)
        End Function

        ''' <summary>把局部点变换到世界坐标。</summary>
        Public Function Transform(localPoint As Vector3) As Vector3
            Return Position + Rotation.Rotate(localPoint)
        End Function

        Public Overrides Function ToString() As String
            Return $"({Position.x:F3}, {Position.y:F3}, {Position.z:F3})"
        End Function
    End Structure

    ''' <summary>机械臂的一个可动关节（由 URDF joint 派生，附带刚体锚点与轴）。</summary>
    Public Class ArmJointDef

        ''' <summary>在整条链中的序号（0 = 第一个关节）。</summary>
        Public Property Index As Integer

        Public Property Name As String
        Public Property ParentLink As String
        Public Property ChildLink As String

        ''' <summary>关节原点（父 link 局部系）。</summary>
        Public Property Origin As Vector3

        ''' <summary>关节原点的 rpy 旋转。</summary>
        Public Property Rpy As Vector3

        ''' <summary>单位转轴（子 link 局部系）。</summary>
        Public Property Axis As Vector3

        ''' <summary>关节在父刚体局部系中的锚点（父 link 局部系 → 父刚体局部系，已减去父惯量原点）。</summary>
        Public Property AnchorA As Vector3

        ''' <summary>关节在子刚体局部系中的锚点（= -子惯量原点）。</summary>
        Public Property AnchorB As Vector3

        Public Property Lower As Double = -System.Math.PI
        Public Property Upper As Double = System.Math.PI
        Public Property Effort As Double = 100.0
        Public Property Velocity As Double = 2.0
        Public Property Damping As Double = 0.1

        ''' <summary>
        ''' 关节角的正方向与物理轴的关系。
        ''' </summary>
        ''' <remarks>
        ''' URDF 的 axis 在父子两侧是同一方向；<see cref="RevoluteJoint3D"/> 的角度定义
        ''' 也以 A 侧轴的正旋转为正，因此这里恒为 +1（保留字段便于以后适配反向装配）。
        ''' </remarks>
        Public Property Direction As Double = 1.0

        Public Overrides Function ToString() As String
            Return $"{Name}: {ParentLink} -> {ChildLink} axis=({Axis.x:F2}, {Axis.y:F2}, {Axis.z:F2})"
        End Function
    End Class

    ''' <summary>
    ''' 由 URDF + 按连杆分组的 GLB 网格构建的机械臂装配体。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' URDF 是运动学 / 惯性 / 限位的唯一事实源，glb 只提供视觉网格。
    ''' 世界坐标统一 Z-up，基座底面在 z = 0。
    ''' </para>
    ''' <para>
    ''' 刚体原点取 URDF 的 **惯量原点**（`&lt;inertial&gt;&lt;origin&gt;`），因为
    ''' <see cref="PhysicsWorld3D"/> 的 <c>RigidBody3D.Position</c> 就是质心位置。
    ''' 关节锚点因此要做一次换算：<c>anchorA = origin - 父惯量原点</c>、
    ''' <c>anchorB = -子惯量原点</c>。
    ''' </para>
    ''' </remarks>
    Public Class ArmAssembly

        Public Property Robot As UrdfRobot
        Public Property Meshes As GlbScene

        ''' <summary>按链顺序排列的可动关节。</summary>
        Public ReadOnly Property Joints As New List(Of ArmJointDef)()

        ''' <summary>TCP 在 link6 局部系中的位置。</summary>
        Public Property TcpOffset As Vector3 = New Vector3(0.095, 0, 0)

        ''' <summary>TCP 相对哪个 link（通常是最后一个连杆）。</summary>
        Public Property TcpLink As String

        ''' <summary>基座 link 名称。</summary>
        Public Property BaseLink As String

        Public ReadOnly Property JointCount As Integer
            Get
                Return Joints.Count
            End Get
        End Property

        Public ReadOnly Property LowerLimits As Double()
            Get
                Return Joints.Select(Function(j) j.Lower).ToArray()
            End Get
        End Property

        Public ReadOnly Property UpperLimits As Double()
            Get
                Return Joints.Select(Function(j) j.Upper).ToArray()
            End Get
        End Property

        ''' <summary>零位姿态下每个 link 的世界位姿。</summary>
        Public Function ZeroPoses() As Dictionary(Of String, Pose3D)
            Return ForwardKinematics(New Double(JointCount - 1) {})
        End Function

        ''' <summary>前向运动学：给定关节角（弧度），返回每个 link 的世界位姿。</summary>
        Public Function ForwardKinematics(q As Double()) As Dictionary(Of String, Pose3D)
            Dim poses As New Dictionary(Of String, Pose3D)(StringComparer.OrdinalIgnoreCase)

            poses(BaseLink) = Pose3D.Identity()

            For Each joint As ArmJointDef In Joints
                Dim parent As Pose3D = Pose3D.Identity()

                If Not poses.TryGetValue(joint.ParentLink, parent) Then
                    Continue For
                End If

                Dim angle As Double = 0.0

                If q IsNot Nothing AndAlso joint.Index < q.Length Then
                    angle = q(joint.Index)
                End If

                Dim rpy As Quaternion = RpyQuaternion(joint.Rpy)
                Dim spin As Quaternion = Quaternion.FromAxisAngle(joint.Axis, angle * joint.Direction)
                Dim rotation As Quaternion = Quaternion.Compose(
                    parent.Rotation,
                    Quaternion.Compose(rpy, spin)).Normalize()

                Dim position As Vector3 = parent.Position + parent.Rotation.Rotate(joint.Origin)

                poses(joint.ChildLink) = New Pose3D(position, rotation)
            Next

            Return poses
        End Function

        ''' <summary>由关节角计算 TCP 的世界位姿。</summary>
        Public Function TcpPose(q As Double()) As Pose3D
            Dim poses As Dictionary(Of String, Pose3D) = ForwardKinematics(q)
            Dim last As Pose3D = Pose3D.Identity()

            If String.IsNullOrEmpty(TcpLink) OrElse Not poses.TryGetValue(TcpLink, last) Then
                Return Pose3D.Identity()
            End If

            Return New Pose3D(last.Transform(TcpOffset), last.Rotation)
        End Function

        ''' <summary>由关节角计算 TCP 的世界位姿。</summary>
        Public Function TcpPose(poses As Dictionary(Of String, Pose3D)) As Pose3D
            Dim last As Pose3D = Pose3D.Identity()

            If String.IsNullOrEmpty(TcpLink) OrElse Not poses.TryGetValue(TcpLink, last) Then
                Return Pose3D.Identity()
            End If

            Return New Pose3D(last.Transform(TcpOffset), last.Rotation)
        End Function

        ''' <summary>某个 link 的惯量原点（刚体原点在 link 局部系中的位置）。</summary>
        Public Function InertialOriginOf(linkName As String) As Vector3
            Dim link As UrdfLink = Robot.FindLink(linkName)

            If link Is Nothing Then Return Vector3.Zero

            Return link.InertialOrigin
        End Function

        ''' <summary>
        ''' 由零位姿态下的 link 位姿与关节角，算出"刚体"的世界位姿。
        ''' 刚体原点 = link 位姿作用在惯量原点上。
        ''' </summary>
        Public Function BodyPose(poses As Dictionary(Of String, Pose3D), linkName As String) As Pose3D
            Dim pose As Pose3D = Pose3D.Identity()

            If Not poses.TryGetValue(linkName, pose) Then Return Pose3D.Identity()

            Return New Pose3D(pose.Transform(InertialOriginOf(linkName)), pose.Rotation)
        End Function

        ''' <summary>rpy（弧度）→ 四元数，URDF 约定 R = Rz(yaw)·Ry(pitch)·Rx(roll)。</summary>
        Public Shared Function RpyQuaternion(rpy As Vector3) As Quaternion
            If rpy Is Nothing Then Return Quaternion.Identity
            If rpy.x = 0.0 AndAlso rpy.y = 0.0 AndAlso rpy.z = 0.0 Then Return Quaternion.Identity

            Dim rx As Quaternion = Quaternion.FromAxisAngle(New Vector3(1, 0, 0), rpy.x)
            Dim ry As Quaternion = Quaternion.FromAxisAngle(New Vector3(0, 1, 0), rpy.y)
            Dim rz As Quaternion = Quaternion.FromAxisAngle(New Vector3(0, 0, 1), rpy.z)

            ' Compose(a, b) = 先施加 b 再施加 a，故 R = Rz·Ry·Rx
            Return Quaternion.Compose(rz, Quaternion.Compose(ry, rx)).Normalize()
        End Function

        ''' <summary>从 URDF 与网格构建装配体，并按父子关系排出关节链顺序。</summary>
        Public Shared Function Build(robot As UrdfRobot, meshes As GlbScene) As ArmAssembly
            Dim assembly As New ArmAssembly With {
                .Robot = robot,
                .Meshes = meshes
            }

            Dim rootLink As UrdfLink = robot.RootLink

            If rootLink Is Nothing Then
                Throw New InvalidDataException("URDF 中找不到基座 link")
            End If

            assembly.BaseLink = rootLink.Name

            ' ---- 按父子关系把关节排成链（广度优先）----
            Dim children As New Dictionary(Of String, List(Of UrdfJoint))(StringComparer.OrdinalIgnoreCase)

            For Each joint As UrdfJoint In robot.Joints
                If joint.IsFixed Then Continue For

                Dim list As List(Of UrdfJoint) = Nothing

                If Not children.TryGetValue(joint.Parent, list) Then
                    list = New List(Of UrdfJoint)()
                    children.Add(joint.Parent, list)
                End If

                list.Add(joint)
            Next

            Dim queue As New Queue(Of String)()
            Dim index As Integer = 0

            queue.Enqueue(rootLink.Name)

            While queue.Count > 0
                Dim linkName As String = queue.Dequeue()
                Dim list As List(Of UrdfJoint) = Nothing

                If Not children.TryGetValue(linkName, list) Then Continue While

                For Each joint As UrdfJoint In list
                    Dim parentLink As UrdfLink = robot.FindLink(joint.Parent)
                    Dim childLink As UrdfLink = robot.FindLink(joint.Child)

                    If parentLink Is Nothing OrElse childLink Is Nothing Then Continue For

                    Dim definition As New ArmJointDef With {
                        .Index = index,
                        .Name = joint.Name,
                        .ParentLink = joint.Parent,
                        .ChildLink = joint.Child,
                        .Origin = joint.Origin,
                        .Rpy = joint.Rpy,
                        .Axis = Vector3Math.Normalize(joint.Axis),
                        .AnchorA = joint.Origin - parentLink.InertialOrigin,
                        .AnchorB = childLink.InertialOrigin * -1.0,
                        .Lower = joint.Lower,
                        .Upper = joint.Upper,
                        .Effort = joint.Effort,
                        .Velocity = joint.Velocity,
                        .Damping = joint.Damping
                    }

                    assembly.Joints.Add(definition)
                    index += 1
                    queue.Enqueue(joint.Child)
                Next
            End While

            ' ---- TCP 偏移：从最后一个可动 link 出发，沿 fixed joint 链找到 tcp ----
            Dim lastLink As String = If(assembly.Joints.Count > 0, assembly.Joints(assembly.Joints.Count - 1).ChildLink, rootLink.Name)
            Dim tcpOffset As Vector3 = Vector3.Zero
            Dim cursor As String = lastLink
            Dim guard As Integer = 0

            While guard < 16
                guard += 1

                Dim nextJoint As UrdfJoint = Nothing

                For Each joint As UrdfJoint In robot.Joints
                    If joint.IsFixed AndAlso String.Equals(joint.Parent, cursor, StringComparison.OrdinalIgnoreCase) Then
                        nextJoint = joint
                        Exit For
                    End If
                Next

                If nextJoint Is Nothing Then Exit While

                tcpOffset = tcpOffset + nextJoint.Origin
                cursor = nextJoint.Child

                If String.Equals(cursor, "tcp", StringComparison.OrdinalIgnoreCase) Then Exit While
            End While

            assembly.TcpOffset = tcpOffset
            assembly.TcpLink = lastLink

            Return assembly
        End Function

        ''' <summary>描述整个装配体的一行摘要（写入日志）。</summary>
        Public Function Describe() As String
            Return $"机械臂 '{Robot.Name}': {Robot.Links.Count} links / {JointCount} joints / " &
                   $"{If(Meshes Is Nothing, 0, Meshes.TriangleCount)} triangles / TCP offset={TcpOffset}"
        End Function

    End Class

End Namespace
