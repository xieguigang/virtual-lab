Imports System.Numerics

''' <summary>
''' 前向运动学与质心/速度的传播，对应 <c>mj_kinematics</c>、<c>mj_comPos</c>、
''' <c>mj_comVel</c>。
''' </summary>
''' <remarks>
''' <para>
''' 这里是托管层自己实现的部分：计算内核只有"刚体 + 成对约束"，
''' 没有 body 树，也没有 <c>qpos → 世界位姿</c> 的映射。整棵树必须从
''' <see cref="mjModel"/> 的偏移数组推出来。
''' </para>
''' <para>
''' 位姿约定与 MuJoCo 一致：
''' </para>
''' <code>
'''   xpos[i] = xpos[parent] + R(parent)·body_pos[i]
'''   xquat[i] = xquat[parent] ⊗ body_quat[i] ⊗ 关节旋转
'''   xipos[i] = xpos[i] + R(xquat[i])·body_ipos[i]
''' </code>
''' <para>
''' 自由关节的 <c>qpos[0..3]</c> 是 body 帧的世界坐标，<c>qpos[3..6]</c> 是世界姿态；
''' 球关节的 <c>qpos</c> 是相对父体的四元数（4 个数）；铰链与滑动各 1 个数。
''' </para>
''' </remarks>
Public Module MjKinematics

    ''' <summary>位置相关的量：<c>xpos/xquat/xmat</c>、质心、锚点、geom/site/cam</summary>
    Public Sub RunPosition(m As mjModel, d As mjData)
        Dim nbody As Integer = m.nbody
        Dim xpos As Double() = d.xpos
        Dim xquat As Double() = d.xquat

        ' 世界坐标系的原点
        xpos(0) = 0.0 : xpos(1) = 0.0 : xpos(2) = 0.0
        xquat(0) = 1.0 : xquat(1) = 0.0 : xquat(2) = 0.0 : xquat(3) = 0.0

        For i As Integer = 1 To nbody - 1
            Dim parent As Integer = m.body_parentid(i)
            Dim parentQuat As Quaternion = MjMath.Quat(xquat, parent * 4)
            Dim localPos As New Vector3(CSng(m.body_pos(i * 3)), CSng(m.body_pos(i * 3 + 1)),
                                        CSng(m.body_pos(i * 3 + 2)))
            Dim position As Vector3 = New Vector3(CSng(xpos(parent * 3)), CSng(xpos(parent * 3 + 1)),
                                                  CSng(xpos(parent * 3 + 2))) +
                                      MjMath.Rotate(parentQuat, localPos)
            Dim quat As Quaternion = MjMath.Mul(parentQuat, MjMath.Quat(m.body_quat, i * 4))

            Dim start As Integer = m.body_jntadr(i)
            Dim count As Integer = m.body_jntnum(i)

            For k As Integer = 0 To count - 1
                Dim j As Integer = start + k
                Dim adr As Integer = m.jnt_qposadr(j)

                Select Case CType(m.jnt_type(j), mjtJoint)
                    Case mjtJoint.mjJNT_FREE
                        position = New Vector3(CSng(d.qpos(adr + 0)), CSng(d.qpos(adr + 1)),
                                               CSng(d.qpos(adr + 2)))
                        quat = MjMath.Quat(d.qpos, adr + 3)

                    Case mjtJoint.mjJNT_BALL
                        quat = MjMath.Mul(quat, MjMath.Quat(d.qpos, adr))

                    Case mjtJoint.mjJNT_HINGE
                        Dim axis As New Vector3(CSng(m.jnt_axis(j * 3)), CSng(m.jnt_axis(j * 3 + 1)),
                                                CSng(m.jnt_axis(j * 3 + 2)))
                        quat = MjMath.Mul(quat, MjMath.AxisAngle(axis, d.qpos(adr)))

                    Case mjtJoint.mjJNT_SLIDE
                        Dim axis2 As New Vector3(CSng(m.jnt_axis(j * 3)), CSng(m.jnt_axis(j * 3 + 1)),
                                                 CSng(m.jnt_axis(j * 3 + 2)))
                        position = position + MjMath.Rotate(quat, axis2) * CSng(d.qpos(adr))
                End Select
            Next

            quat = MjMath.Normalize(quat)

            xpos(i * 3 + 0) = position.X
            xpos(i * 3 + 1) = position.Y
            xpos(i * 3 + 2) = position.Z

            Call MjMath.WriteQuat(xquat, i * 4, quat)
        Next

        Call UpdateDerived(m, d)
    End Sub

    ''' <summary>
    ''' 由 <c>xpos/xquat</c> 推出 <c>xmat/xipos/ximat</c>、关节锚点与轴向、
    ''' 以及 geom/site/camera 的世界位姿。
    ''' </summary>
    ''' <remarks>
    ''' 单独拆出来是因为求解器推进之后 <c>xpos/xquat</c> 来自刚体（真值），
    ''' 而其他派生量仍要重新算一遍；此时不应再从 <c>qpos</c> 走一遍树，
    ''' 否则渲染会出现"物理位置与显示位置不一致"的抖动。
    ''' </remarks>
    Public Sub UpdateDerived(m As mjModel, d As mjData)
        For i As Integer = 0 To m.nbody - 1
            Dim quat As Quaternion = MjMath.Quat(d.xquat, i * 4)

            Call MjMath.Mat9(d.xmat, i * 9, quat)
            Call MjMath.Mat9(d.ximat, i * 9, quat)

            Dim ipos As New Vector3(CSng(m.body_ipos(i * 3)), CSng(m.body_ipos(i * 3 + 1)),
                                    CSng(m.body_ipos(i * 3 + 2)))
            Dim com As Vector3 = New Vector3(CSng(d.xpos(i * 3)), CSng(d.xpos(i * 3 + 1)),
                                             CSng(d.xpos(i * 3 + 2))) + MjMath.Rotate(quat, ipos)

            d.xipos(i * 3 + 0) = com.X
            d.xipos(i * 3 + 1) = com.Y
            d.xipos(i * 3 + 2) = com.Z

            Call MjMath.WriteQuat(d.xiquat, i * 4, MjMath.Mul(quat, MjMath.Quat(m.body_iquat, i * 4)))
        Next

        ' 关节锚点与轴向
        For j As Integer = 0 To m.njnt - 1
            Dim body As Integer = m.jnt_bodyid(j)
            Dim quat As Quaternion = MjMath.Quat(d.xquat, body * 4)
            Dim position As New Vector3(CSng(d.xpos(body * 3)), CSng(d.xpos(body * 3 + 1)),
                                        CSng(d.xpos(body * 3 + 2)))
            Dim local As New Vector3(CSng(m.jnt_pos(j * 3)), CSng(m.jnt_pos(j * 3 + 1)),
                                     CSng(m.jnt_pos(j * 3 + 2)))
            Dim anchor As Vector3 = position + MjMath.Rotate(quat, local)
            Dim axis As Vector3 = MjMath.SafeNormalize(
                MjMath.Rotate(quat, New Vector3(CSng(m.jnt_axis(j * 3)), CSng(m.jnt_axis(j * 3 + 1)),
                                                CSng(m.jnt_axis(j * 3 + 2)))))

            d.xanchor(j * 3 + 0) = anchor.X
            d.xanchor(j * 3 + 1) = anchor.Y
            d.xanchor(j * 3 + 2) = anchor.Z

            d.xaxis(j * 3 + 0) = axis.X
            d.xaxis(j * 3 + 1) = axis.Y
            d.xaxis(j * 3 + 2) = axis.Z
        Next

        ' geom / site / camera
        For g As Integer = 0 To m.ngeom - 1
            Dim body As Integer = m.geom_bodyid(g)
            Dim frame As Pose = FrameOf(m.geom_pos, m.geom_quat, g, d, body)

            d.geom_xpos(g * 3 + 0) = frame.Position.X
            d.geom_xpos(g * 3 + 1) = frame.Position.Y
            d.geom_xpos(g * 3 + 2) = frame.Position.Z

            Call MjMath.WriteQuat(d.geom_xquat, g * 4, frame.Rotation)
            Call MjMath.Mat9(d.geom_xmat, g * 9, frame.Rotation)
        Next

        For s As Integer = 0 To m.nsite - 1
            Dim body As Integer = m.site_bodyid(s)
            Dim frame As Pose = FrameOf(m.site_pos, m.site_quat, s, d, body)

            d.site_xpos(s * 3 + 0) = frame.Position.X
            d.site_xpos(s * 3 + 1) = frame.Position.Y
            d.site_xpos(s * 3 + 2) = frame.Position.Z

            Call MjMath.WriteQuat(d.site_xquat, s * 4, frame.Rotation)
            Call MjMath.Mat9(d.site_xmat, s * 9, frame.Rotation)
        Next

        For c As Integer = 0 To m.ncam - 1
            Dim body As Integer = m.cam_bodyid(c)
            Dim frame As Pose = FrameOf(m.cam_pos, m.cam_quat, c, d, body)

            d.cam_xpos(c * 3 + 0) = frame.Position.X
            d.cam_xpos(c * 3 + 1) = frame.Position.Y
            d.cam_xpos(c * 3 + 2) = frame.Position.Z

            Call MjMath.Mat9(d.cam_xmat, c * 9, frame.Rotation)
        Next
    End Sub

    Private Structure Pose
        Public Position As Vector3
        Public Rotation As Quaternion
    End Structure

    Private Function FrameOf(localPos As Double(), localQuat As Double(), index As Integer,
                             d As mjData, body As Integer) As Pose
        Dim parentQuat As Quaternion = MjMath.Quat(d.xquat, body * 4)
        Dim offset As New Vector3(CSng(localPos(index * 3)), CSng(localPos(index * 3 + 1)),
                                  CSng(localPos(index * 3 + 2)))
        Dim origin As New Vector3(CSng(d.xpos(body * 3)), CSng(d.xpos(body * 3 + 1)),
                                  CSng(d.xpos(body * 3 + 2)))

        Dim frame As Pose
        frame.Position = origin + MjMath.Rotate(parentQuat, offset)
        frame.Rotation = MjMath.Mul(parentQuat, MjMath.Quat(localQuat, index * 4))

        Return frame
    End Function

    ' /********************************************************************************/
    '  质心与复合惯量
    ' /********************************************************************************/

    ''' <summary>
    ''' 子树质心与复合惯量（<c>subtree_com</c> / <c>cinert</c> / <c>crb</c>）。
    ''' </summary>
    ''' <remarks>
    ''' <c>cinert</c> 与 <c>crb</c> 用 MuJoCo 的 10 元组布局：
    ''' <c>(质量, 质量·质心xyz, 惯量 ixx iyy izz ixy ixz iyz)</c>，惯量相对 body 原点。
    ''' </remarks>
    Public Sub RunInertia(m As mjModel, d As mjData)
        ' 自底向上累加子树质量与质心
        For i As Integer = 0 To m.nbody - 1
            Dim mass As Double = m.body_mass(i)
            d.subtree_com(i * 3 + 0) = d.xipos(i * 3 + 0) * mass
            d.subtree_com(i * 3 + 1) = d.xipos(i * 3 + 1) * mass
            d.subtree_com(i * 3 + 2) = d.xipos(i * 3 + 2) * mass
        Next

        For i As Integer = m.nbody - 1 To 1 Step -1
            Dim parent As Integer = m.body_parentid(i)

            If parent < 0 Then
                Continue For
            End If

            d.subtree_com(parent * 3 + 0) += d.subtree_com(i * 3 + 0)
            d.subtree_com(parent * 3 + 1) += d.subtree_com(i * 3 + 1)
            d.subtree_com(parent * 3 + 2) += d.subtree_com(i * 3 + 2)
        Next

        For i As Integer = 0 To m.nbody - 1
            Dim mass As Double = m.body_mass(i)

            If mass > 0 Then
                d.subtree_com(i * 3 + 0) /= mass
                d.subtree_com(i * 3 + 1) /= mass
                d.subtree_com(i * 3 + 2) /= mass
            Else
                d.subtree_com(i * 3 + 0) = d.xipos(i * 3 + 0)
                d.subtree_com(i * 3 + 1) = d.xipos(i * 3 + 1)
                d.subtree_com(i * 3 + 2) = d.xipos(i * 3 + 2)
            End If
        Next

        ' cinert：把 body 的惯量换算到 body 原点
        For i As Integer = 0 To m.nbody - 1
            Call FillCinert(m, d, i)
        Next

        ' crb：复合刚体惯量，自底向上按平行轴定理搬移
        For i As Integer = 0 To m.nbody - 1
            Call CopyCinert(d, i, i)
        Next

        For i As Integer = m.nbody - 1 To 1 Step -1
            Dim parent As Integer = m.body_parentid(i)

            If parent < 0 Then
                Continue For
            End If

            Call ShiftCinert(d, parent, i)
        Next

        ' cdof：每个自由度的运动轴（角在前、线在后，线速度取质心处）
        For i As Integer = 0 To m.nbody - 1
            Dim start As Integer = m.body_dofadr(i)
            Dim count As Integer = m.body_dofnum(i)

            For k As Integer = 0 To count - 1
                Dim dof As Integer = start + k
                Dim joint As Integer = m.dof_jntid(dof)
                Dim axis As New Vector3(CSng(d.xaxis(joint * 3)), CSng(d.xaxis(joint * 3 + 1)),
                                        CSng(d.xaxis(joint * 3 + 2)))

                Dim angular As Vector3
                Dim linear As Vector3

                Select Case CType(m.jnt_type(joint), mjtJoint)
                    Case mjtJoint.mjJNT_FREE
                        ' 前三个是全局平移，后三个是 body 局部旋转
                        Select Case dof - m.body_dofadr(i)
                            Case 0, 1, 2
                                angular = Vector3.Zero
                                linear = If(dof - m.body_dofadr(i) = 0, Vector3.UnitX,
                                            If(dof - m.body_dofadr(i) = 1, Vector3.UnitY, Vector3.UnitZ))
                            Case Else
                                Dim local As Vector3 = If(dof - m.body_dofadr(i) = 3, Vector3.UnitX,
                                                          If(dof - m.body_dofadr(i) = 4, Vector3.UnitY, Vector3.UnitZ))
                                angular = MjMath.Rotate(MjMath.Quat(d.xquat, i * 4), local)
                                linear = Vector3.Cross(angular, ComOf(d, i) - BodyOrigin(d, i))
                        End Select

                    Case mjtJoint.mjJNT_BALL
                        Dim local As Vector3 = If(dof - m.body_dofadr(i) = 0, Vector3.UnitX,
                                                  If(dof - m.body_dofadr(i) = 1, Vector3.UnitY, Vector3.UnitZ))
                        angular = MjMath.Rotate(MjMath.Quat(d.xquat, i * 4), local)
                        linear = Vector3.Cross(angular, ComOf(d, i) - AnchorOf(d, joint))

                    Case mjtJoint.mjJNT_SLIDE
                        angular = Vector3.Zero
                        linear = axis

                    Case Else
                        angular = axis
                        linear = Vector3.Cross(angular, ComOf(d, i) - AnchorOf(d, joint))
                End Select

                d.cdof(dof * 6 + 0) = angular.X
                d.cdof(dof * 6 + 1) = angular.Y
                d.cdof(dof * 6 + 2) = angular.Z
                d.cdof(dof * 6 + 3) = linear.X
                d.cdof(dof * 6 + 4) = linear.Y
                d.cdof(dof * 6 + 5) = linear.Z
            Next
        Next
    End Sub

    Public Function ComOf(d As mjData, body As Integer) As Vector3
        Return New Vector3(CSng(d.xipos(body * 3)), CSng(d.xipos(body * 3 + 1)), CSng(d.xipos(body * 3 + 2)))
    End Function

    Public Function BodyOrigin(d As mjData, body As Integer) As Vector3
        Return New Vector3(CSng(d.xpos(body * 3)), CSng(d.xpos(body * 3 + 1)), CSng(d.xpos(body * 3 + 2)))
    End Function

    Public Function AnchorOf(d As mjData, joint As Integer) As Vector3
        Return New Vector3(CSng(d.xanchor(joint * 3)), CSng(d.xanchor(joint * 3 + 1)),
                           CSng(d.xanchor(joint * 3 + 2)))
    End Function

    Private Sub FillCinert(m As mjModel, d As mjData, body As Integer)
        Dim mass As Double = m.body_mass(body)
        Dim r As Vector3 = ComOf(d, body) - BodyOrigin(d, body)
        Dim rotation As Quaternion = MjMath.Quat(d.xquat, body * 4)

        ' 局部对角惯量 → 世界张量
        Dim local As New Matrix3x3D(rotation,
                                    m.body_inertia(body * 3), m.body_inertia(body * 3 + 1),
                                    m.body_inertia(body * 3 + 2))

        Dim offset As Integer = body * 10

        d.cinert(offset + 0) = mass
        d.cinert(offset + 1) = mass * r.X
        d.cinert(offset + 2) = mass * r.Y
        d.cinert(offset + 3) = mass * r.Z
        d.cinert(offset + 4) = local.XX
        d.cinert(offset + 5) = local.YY
        d.cinert(offset + 6) = local.ZZ
        d.cinert(offset + 7) = local.XY
        d.cinert(offset + 8) = local.XZ
        d.cinert(offset + 9) = local.YZ
    End Sub

    Private Sub CopyCinert(d As mjData, target As Integer, source As Integer)
        For k As Integer = 0 To 9
            d.crb(target * 10 + k) = d.cinert(source * 10 + k)
        Next
    End Sub

    ''' <summary>把子 body 的复合惯量按平行轴定理搬到父 body 的原点</summary>
    Private Sub ShiftCinert(d As mjData, parent As Integer, child As Integer)
        Dim r As Vector3 = BodyOrigin(d, child) - BodyOrigin(d, parent)
        Dim mass As Double = d.crb(child * 10 + 0)

        If mass <= 0 Then
            Return
        End If

        Dim comX As Double = d.crb(child * 10 + 1)
        Dim comY As Double = d.crb(child * 10 + 2)
        Dim comZ As Double = d.crb(child * 10 + 3)

        ' 惯量搬移：I' = I + m·(|r|²E − r rᵀ) − 交叉项
        Dim ixx As Double = d.crb(child * 10 + 4) + mass * (r.Y * r.Y + r.Z * r.Z)
        Dim iyy As Double = d.crb(child * 10 + 5) + mass * (r.X * r.X + r.Z * r.Z)
        Dim izz As Double = d.crb(child * 10 + 6) + mass * (r.X * r.X + r.Y * r.Y)
        Dim ixy As Double = d.crb(child * 10 + 7) - mass * r.X * r.Y
        Dim ixz As Double = d.crb(child * 10 + 8) - mass * r.X * r.Z
        Dim iyz As Double = d.crb(child * 10 + 9) - mass * r.Y * r.Z

        ' 交叉项 <c>m·c</c> 也随之平移
        d.crb(parent * 10 + 0) += mass
        d.crb(parent * 10 + 1) += comX + mass * r.X
        d.crb(parent * 10 + 2) += comY + mass * r.Y
        d.crb(parent * 10 + 3) += comZ + mass * r.Z
        d.crb(parent * 10 + 4) += ixx
        d.crb(parent * 10 + 5) += iyy
        d.crb(parent * 10 + 6) += izz
        d.crb(parent * 10 + 7) += ixy
        d.crb(parent * 10 + 8) += ixz
        d.crb(parent * 10 + 9) += iyz
    End Sub

    ' /********************************************************************************/
    '  速度
    ' /********************************************************************************/

    ''' <summary>刚体速度传播（<c>cvel</c>，角在前、线在后，线速度取质心处）</summary>
    Public Sub RunVelocity(m As mjModel, d As mjData)
        Dim angular(m.nbody * 3 - 1) As Double
        Dim linear(m.nbody * 3 - 1) As Double

        For i As Integer = 1 To m.nbody - 1
            Dim parent As Integer = m.body_parentid(i)
            Dim parentAngular As Vector3 = New Vector3(CSng(angular(parent * 3)), CSng(angular(parent * 3 + 1)),
                                                       CSng(angular(parent * 3 + 2)))
            Dim parentLinear As Vector3 = New Vector3(CSng(linear(parent * 3)), CSng(linear(parent * 3 + 1)),
                                                      CSng(linear(parent * 3 + 2)))

            Dim w As Vector3 = parentAngular
            Dim v As Vector3 = parentLinear + Vector3.Cross(parentAngular, BodyOrigin(d, i) - BodyOrigin(d, parent))

            Dim start As Integer = m.body_jntadr(i)
            Dim count As Integer = m.body_jntnum(i)
            Dim hasJoint As Boolean = count > 0

            For k As Integer = 0 To count - 1
                Dim j As Integer = start + k
                Dim dofAdr As Integer = m.jnt_dofadr(j)
                Dim quat As Quaternion = MjMath.Quat(d.xquat, i * 4)

                Select Case CType(m.jnt_type(j), mjtJoint)
                    Case mjtJoint.mjJNT_FREE
                        v = New Vector3(CSng(d.qvel(dofAdr + 0)), CSng(d.qvel(dofAdr + 1)),
                                        CSng(d.qvel(dofAdr + 2)))
                        w = MjMath.Rotate(quat, New Vector3(CSng(d.qvel(dofAdr + 3)), CSng(d.qvel(dofAdr + 4)),
                                                            CSng(d.qvel(dofAdr + 5))))

                    Case mjtJoint.mjJNT_BALL
                        w += MjMath.Rotate(quat, New Vector3(CSng(d.qvel(dofAdr + 0)), CSng(d.qvel(dofAdr + 1)),
                                                             CSng(d.qvel(dofAdr + 2))))

                    Case mjtJoint.mjJNT_HINGE
                        Dim axis As Vector3 = MjMath.SafeNormalize(New Vector3(CSng(d.xaxis(j * 3)),
                                                                               CSng(d.xaxis(j * 3 + 1)),
                                                                               CSng(d.xaxis(j * 3 + 2))))
                        Dim spin As Vector3 = axis * CSng(d.qvel(dofAdr))
                        w += spin
                        v += Vector3.Cross(spin, BodyOrigin(d, i) - AnchorOf(d, j))

                    Case mjtJoint.mjJNT_SLIDE
                        Dim axis2 As Vector3 = MjMath.SafeNormalize(New Vector3(CSng(d.xaxis(j * 3)),
                                                                                CSng(d.xaxis(j * 3 + 1)),
                                                                                CSng(d.xaxis(j * 3 + 2))))
                        v += axis2 * CSng(d.qvel(dofAdr))
                End Select

                hasJoint = True
            Next

            angular(i * 3 + 0) = w.X
            angular(i * 3 + 1) = w.Y
            angular(i * 3 + 2) = w.Z

            linear(i * 3 + 0) = v.X
            linear(i * 3 + 1) = v.Y
            linear(i * 3 + 2) = v.Z

            ' 质心速度 = body 原点速度 + ω × (质心 − 原点)
            Dim comVelocity As Vector3 = v + Vector3.Cross(w, ComOf(d, i) - BodyOrigin(d, i))

            d.cvel(i * 6 + 0) = w.X
            d.cvel(i * 6 + 1) = w.Y
            d.cvel(i * 6 + 2) = w.Z
            d.cvel(i * 6 + 3) = comVelocity.X
            d.cvel(i * 6 + 4) = comVelocity.Y
            d.cvel(i * 6 + 5) = comVelocity.Z

            If Not hasJoint AndAlso parent >= 0 Then
                ' 焊接 body：只继承父体的运动
            End If
        Next
    End Sub

    ''' <summary>世界张量的小工具：把 body 局部的对角惯量转到世界系</summary>
    Friend Structure Matrix3x3D
        Public XX As Double
        Public YY As Double
        Public ZZ As Double
        Public XY As Double
        Public XZ As Double
        Public YZ As Double

        Public Sub New(rotation As Quaternion, ixx As Double, iyy As Double, izz As Double)
            Dim r As Double() = New Double(8) {}

            Call MjMath.Mat9(r, 0, rotation)

            ' I_world = R · diag(I) · Rᵀ
            Dim d0 As Double = ixx, d1 As Double = iyy, d2 As Double = izz

            Me.XX = r(0) * d0 * r(0) + r(1) * d1 * r(1) + r(2) * d2 * r(2)
            Me.YY = r(3) * d0 * r(3) + r(4) * d1 * r(4) + r(5) * d2 * r(5)
            Me.ZZ = r(6) * d0 * r(6) + r(7) * d1 * r(7) + r(8) * d2 * r(8)
            Me.XY = r(0) * d0 * r(3) + r(1) * d1 * r(4) + r(2) * d2 * r(5)
            Me.XZ = r(0) * d0 * r(6) + r(1) * d1 * r(7) + r(2) * d2 * r(8)
            Me.YZ = r(3) * d0 * r(6) + r(4) * d1 * r(7) + r(5) * d2 * r(8)
        End Sub
    End Structure

End Module
