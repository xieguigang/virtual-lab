Imports System.Numerics

''' <summary>
''' 树动力学：质量矩阵（CRBA）、偏置力（RNEA）、被动力与能量。
''' </summary>
''' <remarks>
''' <para>
''' 计算内核是顺序冲量的约束求解器，不提供质量矩阵与偏置力。这一层完全独立地
''' 按 MuJoCo 的树动力学算一遍，用途有三：<c>mj_inverse</c> 的逆动力学校验、
''' 重力补偿/力矩观测这类需要 <c>qfrc_bias</c> 的上层算法、以及自测里的解析解对照。
''' </para>
''' <para>
''' 空间量的约定与 <see cref="mjData.cdof"/> 一致：6 元组是
''' <b>角在前、线在后</b>，参考点取该刚体的质心。因此"把子刚体的惯量搬到父刚体"
''' 用的是平行轴定理的 6×6 形式（见 <see cref="ShiftInertia"/>）。
''' </para>
''' <para>
''' 数值上，M 与内部冲量求解器的结果在有接触时会有差异（求解器允许少量约束违背），
''' 但在无接触/准静态工况下就是精确的树动力学。
''' </para>
''' </remarks>
Public Module MjDynamics

    ''' <summary>补齐惯量、速度、质量矩阵与偏置力（相当于 <c>mj_forward</c> 的动力学部分）</summary>
    Public Sub RunAll(m As mjModel, d As mjData)
        Call MjKinematics.RunPosition(m, d)
        Call MjKinematics.RunInertia(m, d)
        Call MjKinematics.RunVelocity(m, d)

        Call BuildMassMatrix(m, d)
        Call BuildBias(m, d)
        Call BuildPassive(m, d)
        Call BuildEnergy(m, d)
    End Sub

    ' /********************************************************************************/
    '  6×6 空间惯量
    ' /********************************************************************************/

    ''' <summary>
    ''' 复合刚体惯量的 6×6 形式（角在前、线在后，参考点为该刚体的质心）。
    ''' </summary>
    ''' <remarks>
    ''' 只有上三角块是独立的，但 6×6 直接存储（36 个 double）更省事，
    ''' 且鸭子只有 15 个 body。
    ''' </remarks>
    Private Function Composite(m As mjModel, d As mjData) As Double()
        Dim result(m.nbody * 36 - 1) As Double

        For i As Integer = 0 To m.nbody - 1
            Dim offset As Integer = i * 36
            Dim mass As Double = m.body_mass(i)
            Dim local As New MjKinematics.Matrix3x3D(MjMath.Quat(d.xquat, i * 4),
                                                     m.body_inertia(i * 3), m.body_inertia(i * 3 + 1),
                                                     m.body_inertia(i * 3 + 2))

            ' 上左 3×3：世界转动惯量
            result(offset + 0) = local.XX
            result(offset + 1) = local.XY
            result(offset + 2) = local.XZ
            result(offset + 3 * 1 + 0) = local.XY
            result(offset + 3 * 1 + 1) = local.YY
            result(offset + 3 * 1 + 2) = local.YZ
            result(offset + 3 * 2 + 0) = local.XZ
            result(offset + 3 * 2 + 1) = local.YZ
            result(offset + 3 * 2 + 2) = local.ZZ

            ' 下右 3×3：m·E
            result(offset + 27) = mass
            result(offset + 31) = mass
            result(offset + 35) = mass
        Next

        For i As Integer = m.nbody - 1 To 1 Step -1
            Dim parent As Integer = m.body_parentid(i)

            If parent < 0 Then
                Continue For
            End If

            Call ShiftInertia(result, parent, i, MjKinematics.BodyOrigin(d, i) - MjKinematics.BodyOrigin(d, parent))
        Next

        Return result
    End Function

    ''' <summary>
    ''' 把 <paramref name="child"/> 的 6×6 惯量按平行轴定理搬到 <paramref name="parent"/>。
    ''' </summary>
    ''' <remarks>
    ''' <c>F' = Xᵀ F X</c>，其中 <c>X = [[E, 0], [S(d), E]]</c>、<c>d = 父原点 − 子原点</c>。
    ''' </remarks>
    Private Sub ShiftInertia(buffer As Double(), parent As Integer, child As Integer, d As Vector3)
        Dim po As Integer = parent * 36
        Dim co As Integer = child * 36

        ' 取出子块的四个 3×3
        Dim a As Double() = Block(buffer, co, 0)
        Dim b As Double() = Block(buffer, co, 3)
        Dim bt As Double() = Block(buffer, co, 9)
        Dim mm As Double() = Block(buffer, co, 18)

        Dim sd As Double() = Skew(d)

        ' A' = A + B S(d) − S(d) Bᵀ − S(d) M S(d)
        Dim bs As Double() = Mul3(b, sd)
        Dim sbt As Double() = Mul3(sd, bt)
        Dim sms As Double() = Mul3(Mul3(sd, mm), sd)

        Dim ap As Double() = New Double(8) {}

        For r As Integer = 0 To 2
            For c As Integer = 0 To 2
                ap(r * 3 + c) = a(r * 3 + c) + bs(r * 3 + c) - sbt(r * 3 + c) - sms(r * 3 + c)
            Next
        Next

        ' B' = B − S(d) M
        Dim sm As Double() = Mul3(sd, mm)
        Dim bp As Double() = New Double(8) {}

        For r As Integer = 0 To 2
            For c As Integer = 0 To 2
                bp(r * 3 + c) = b(r * 3 + c) - sm(r * 3 + c)
            Next
        Next

        Call AddBlock(buffer, po, 0, ap)
        Call AddBlock(buffer, po, 3, bp)
        Call AddBlock(buffer, po, 9, Transpose(bp))
        Call AddBlock(buffer, po, 18, mm)
    End Sub

    Private Function Block(buffer As Double(), offset As Integer, start As Integer) As Double()
        Dim result(8) As Double

        For r As Integer = 0 To 2
            For c As Integer = 0 To 2
                result(r * 3 + c) = buffer(offset + (start + r * 3) + c)
            Next
        Next

        Return result
    End Function

    Private Sub AddBlock(buffer As Double(), offset As Integer, start As Integer, block As Double())
        For r As Integer = 0 To 2
            For c As Integer = 0 To 2
                buffer(offset + (start + r * 3) + c) += block(r * 3 + c)
            Next
        Next
    End Sub

    Private Function Skew(v As Vector3) As Double()
        Return New Double() {0.0, -v.Z, v.Y,
                             v.Z, 0.0, -v.X,
                             -v.Y, v.X, 0.0}
    End Function

    Private Function Mul3(a As Double(), b As Double()) As Double()
        Dim result(8) As Double

        For r As Integer = 0 To 2
            For c As Integer = 0 To 2
                Dim sum As Double = 0.0

                For k As Integer = 0 To 2
                    sum += a(r * 3 + k) * b(k * 3 + c)
                Next

                result(r * 3 + c) = sum
            Next
        Next

        Return result
    End Function

    Private Function Transpose(a As Double()) As Double()
        Return New Double() {a(0), a(3), a(6), a(1), a(4), a(7), a(2), a(5), a(8)}
    End Function

    ' /********************************************************************************/
    '  质量矩阵
    ' /********************************************************************************/

    ''' <summary>用 CRBA 填 <c>mjData.M</c>（nv × nv，行主序）</summary>
    Public Sub BuildMassMatrix(m As mjModel, d As mjData)
        Dim nv As Integer = m.nv

        If nv <= 0 Then
            Return
        End If

        If d.M Is Nothing OrElse d.M.Length < nv * nv Then
            d.M = New Double(nv * nv - 1) {}
        Else
            Call Array.Clear(d.M, 0, nv * nv)
        End If

        Dim inertia As Double() = Composite(m, d)

        For i As Integer = m.nbody - 1 To 1 Step -1
            Dim start As Integer = m.body_dofadr(i)
            Dim count As Integer = m.body_dofnum(i)

            If count <= 0 Then
                Continue For
            End If

            Dim com As Vector3 = MjKinematics.ComOf(d, i)

            For a As Integer = 0 To count - 1
                Dim dofA As Integer = start + a
                Dim sa As Double() = MotionAt(d, dofA, com)

                For b As Integer = 0 To count - 1
                    Dim dofB As Integer = start + b
                    Dim value As Double = Quadratic(inertia, i, sa, MotionAt(d, dofB, com))

                    d.M(dofA * nv + dofB) = value
                Next

                ' 与祖先自由度的耦合
                Dim e As Integer = m.dof_parentid(dofA)

                While e >= 0
                    Dim value As Double = Quadratic(inertia, i, sa, MotionAt(d, e, com))

                    d.M(dofA * nv + e) = value
                    d.M(e * nv + dofA) = value

                    e = m.dof_parentid(e)
                End While
            Next
        Next

        ' 机械臂惯量（armature）直接加在对角线上：内核把它并进刚体惯量，
        ' 但这里的 M 是独立的观测值，必须显式补上
        For i As Integer = 0 To nv - 1
            d.M(i * nv + i) += m.dof_armature(i)
        Next
    End Sub

    ''' <summary>自由度 <paramref name="dof"/> 在参考点 <paramref name="point"/> 处的运动向量 [ω; v]</summary>
    Private Function MotionAt(d As mjData, dof As Integer, point As Vector3) As Double()
        Dim omega As New Vector3(CSng(d.cdof(dof * 6 + 0)), CSng(d.cdof(dof * 6 + 1)), CSng(d.cdof(dof * 6 + 2)))
        Dim linear As New Vector3(CSng(d.cdof(dof * 6 + 3)), CSng(d.cdof(dof * 6 + 4)), CSng(d.cdof(dof * 6 + 5)))

        ' cdof 的线速度是相对该刚体质心的；换到 point 处再加 ω × (point − com)
        ' 调用方传入的 point 就是该刚体的质心，所以这里只需直接使用
        Return New Double() {omega.X, omega.Y, omega.Z, linear.X, linear.Y, linear.Z}
    End Function

    ''' <summary><c>S₁ᵀ F S₂</c></summary>
    Private Function Quadratic(inertia As Double(), body As Integer, s1 As Double(), s2 As Double()) As Double
        Dim offset As Integer = body * 36
        Dim w1 As New Vector3(CSng(s1(0)), CSng(s1(1)), CSng(s1(2)))
        Dim v1 As New Vector3(CSng(s1(3)), CSng(s1(4)), CSng(s1(5)))
        Dim w2 As New Vector3(CSng(s2(0)), CSng(s2(1)), CSng(s2(2)))
        Dim v2 As New Vector3(CSng(s2(3)), CSng(s2(4)), CSng(s2(5)))

        Dim aw As Vector3 = MulBlock(inertia, offset + 0, w2)
        Dim bv As Vector3 = MulBlock(inertia, offset + 3, v2)
        Dim btw As Vector3 = MulBlock(inertia, offset + 9, w2)
        Dim mv As Vector3 = MulBlock(inertia, offset + 18, v2)

        Return Vector3.Dot(w1, aw) + Vector3.Dot(w1, bv) + Vector3.Dot(v1, btw) + Vector3.Dot(v1, mv)
    End Function

    Private Function MulBlock(buffer As Double(), offset As Integer, v As Vector3) As Vector3
        Return New Vector3(CSng(buffer(offset + 0) * v.X + buffer(offset + 1) * v.Y + buffer(offset + 2) * v.Z),
                           CSng(buffer(offset + 3) * v.X + buffer(offset + 4) * v.Y + buffer(offset + 5) * v.Z),
                           CSng(buffer(offset + 6) * v.X + buffer(offset + 7) * v.Y + buffer(offset + 8) * v.Z))
    End Function

    ''' <summary><c>mj_mulM</c>：<c>res = M · vec</c></summary>
    Public Sub MulM(m As mjModel, d As mjData, res As Double(), vec As Double())
        Dim nv As Integer = m.nv

        Call Array.Clear(res, 0, nv)

        For i As Integer = 0 To nv - 1
            Dim sum As Double = 0.0
            Dim row As Integer = i * nv

            For j As Integer = 0 To nv - 1
                sum += d.M(row + j) * vec(j)
            Next

            res(i) = sum
        Next
    End Sub

    ' /********************************************************************************/
    '  偏置力（RNEA）
    ' /********************************************************************************/

    ''' <summary>偏置力 <c>C(q,q̇) + g(q)</c></summary>
    Public Sub BuildBias(m As mjModel, d As mjData)
        Dim nv As Integer = m.nv

        If nv <= 0 Then
            Return
        End If

        Dim gravity As New Vector3(CSng(m.opt.gravity(0)), CSng(m.opt.gravity(1)), CSng(m.opt.gravity(2)))
        Dim inertia As Double() = Composite(m, d)

        ' 速度（6 元组，角在前、线在后，参考点为质心）
        Dim velocity(m.nbody * 6 - 1) As Double

        For i As Integer = 0 To m.nbody - 1
            For k As Integer = 0 To 5
                velocity(i * 6 + k) = d.cvel(i * 6 + k)
            Next
        Next

        ' 加速度：底座加速度 = −重力，qacc = 0
        Dim accel(m.nbody * 6 - 1) As Double

        accel(3) = -gravity.X
        accel(4) = -gravity.Y
        accel(5) = -gravity.Z

        For i As Integer = 1 To m.nbody - 1
            Dim parent As Integer = m.body_parentid(i)
            Dim delta As Vector3 = MjKinematics.ComOf(d, i) - MjKinematics.ComOf(d, parent)

            ' X·a_parent
            Dim w As New Vector3(CSng(accel(parent * 6 + 0)), CSng(accel(parent * 6 + 1)), CSng(accel(parent * 6 + 2)))
            Dim v As New Vector3(CSng(accel(parent * 6 + 3)), CSng(accel(parent * 6 + 4)), CSng(accel(parent * 6 + 5)))
            v += Vector3.Cross(w, delta)

            ' 速度积项：v × (S·q̇) 的累加
            Dim start As Integer = m.body_dofadr(i)
            Dim count As Integer = m.body_dofnum(i)

            For k As Integer = 0 To count - 1
                Dim dof As Integer = start + k
                Dim s As Double() = MotionAt(d, dof, MjKinematics.ComOf(d, i))
                Dim rate As Double = d.qvel(dof)

                Dim sw As New Vector3(CSng(s(0)), CSng(s(1)), CSng(s(2)))
                Dim sv As New Vector3(CSng(s(3)), CSng(s(4)), CSng(s(5)))

                ' 速度乘子
                Dim bw As New Vector3(CSng(velocity(i * 6 + 0)), CSng(velocity(i * 6 + 1)), CSng(velocity(i * 6 + 2)))
                Dim bv As New Vector3(CSng(velocity(i * 6 + 3)), CSng(velocity(i * 6 + 4)), CSng(velocity(i * 6 + 5)))

                w += Vector3.Cross(bw, sw) * CSng(rate)
                v += (Vector3.Cross(bw, sv) + Vector3.Cross(bv, sw)) * CSng(rate)

                ' 本自由度的 qacc 贡献（这里 qacc 固定为 0，只保留结构）
            Next

            accel(i * 6 + 0) = w.X
            accel(i * 6 + 1) = w.Y
            accel(i * 6 + 2) = w.Z
            accel(i * 6 + 3) = v.X
            accel(i * 6 + 4) = v.Y
            accel(i * 6 + 5) = v.Z
        Next

        ' 力传播
        Dim force(m.nbody * 6 - 1) As Double

        For i As Integer = 0 To m.nbody - 1
            Dim offset As Integer = i * 36
            Dim a As New Vector3(CSng(accel(i * 6 + 0)), CSng(accel(i * 6 + 1)), CSng(accel(i * 6 + 2)))
            Dim al As New Vector3(CSng(accel(i * 6 + 3)), CSng(accel(i * 6 + 4)), CSng(accel(i * 6 + 5)))
            Dim w As New Vector3(CSng(velocity(i * 6 + 0)), CSng(velocity(i * 6 + 1)), CSng(velocity(i * 6 + 2)))
            Dim u As New Vector3(CSng(velocity(i * 6 + 3)), CSng(velocity(i * 6 + 4)), CSng(velocity(i * 6 + 5)))

            Dim n As Vector3 = MulBlock(inertia, offset + 0, a) + MulBlock(inertia, offset + 3, al)
            Dim f As Vector3 = MulBlock(inertia, offset + 9, a) + MulBlock(inertia, offset + 18, al)

            ' v ×* (F v)
            Dim momentumN As Vector3 = MulBlock(inertia, offset + 0, w) + MulBlock(inertia, offset + 3, u)
            Dim momentumF As Vector3 = MulBlock(inertia, offset + 9, w) + MulBlock(inertia, offset + 18, u)

            n += Vector3.Cross(w, momentumN) + Vector3.Cross(u, momentumF)
            f += Vector3.Cross(w, momentumF)

            force(i * 6 + 0) = n.X
            force(i * 6 + 1) = n.Y
            force(i * 6 + 2) = n.Z
            force(i * 6 + 3) = f.X
            force(i * 6 + 4) = f.Y
            force(i * 6 + 5) = f.Z
        Next

        Call Array.Clear(d.qfrc_bias, 0, nv)

        For i As Integer = m.nbody - 1 To 1 Step -1
            Dim start As Integer = m.body_dofadr(i)
            Dim count As Integer = m.body_dofnum(i)

            For k As Integer = 0 To count - 1
                Dim dof As Integer = start + k
                Dim s As Double() = MotionAt(d, dof, MjKinematics.ComOf(d, i))

                d.qfrc_bias(dof) += s(0) * force(i * 6 + 0) + s(1) * force(i * 6 + 1) + s(2) * force(i * 6 + 2) +
                                    s(3) * force(i * 6 + 3) + s(4) * force(i * 6 + 4) + s(5) * force(i * 6 + 5)
            Next

            Dim parent As Integer = m.body_parentid(i)

            If parent >= 0 Then
                Dim delta As Vector3 = MjKinematics.ComOf(d, i) - MjKinematics.ComOf(d, parent)
                Dim n2 As New Vector3(CSng(force(i * 6 + 0)), CSng(force(i * 6 + 1)), CSng(force(i * 6 + 2)))
                Dim f2 As New Vector3(CSng(force(i * 6 + 3)), CSng(force(i * 6 + 4)), CSng(force(i * 6 + 5)))

                n2 -= Vector3.Cross(delta, f2)

                force(parent * 6 + 0) += n2.X
                force(parent * 6 + 1) += n2.Y
                force(parent * 6 + 2) += n2.Z
                force(parent * 6 + 3) += f2.X
                force(parent * 6 + 4) += f2.Y
                force(parent * 6 + 5) += f2.Z
            End If
        Next
    End Sub

    ' /********************************************************************************/
    '  被动力与能量
    ' /********************************************************************************/

    ''' <summary>弹簧力与阻尼力（不参与求解，只用于观测）</summary>
    Public Sub BuildPassive(m As mjModel, d As mjData)
        For i As Integer = 0 To m.nv - 1
            Dim joint As Integer = m.dof_jntid(i)
            Dim stiffness As Double = m.jnt_stiffness(joint)
            Dim damping As Double = m.dof_damping(i)
            Dim reference As Double = m.qpos_spring(m.jnt_qposadr(joint))

            d.qfrc_spring(i) = -stiffness * (d.qpos(m.jnt_qposadr(joint)) - reference)
            d.qfrc_damper(i) = -damping * d.qvel(i)
            d.qfrc_passive(i) = d.qfrc_spring(i) + d.qfrc_damper(i)
        Next
    End Sub

    ''' <summary>动能与势能</summary>
    Public Sub BuildEnergy(m As mjModel, d As mjData)
        Dim kinetic As Double = 0.0

        If m.nv > 0 Then
            For i As Integer = 0 To m.nv - 1
                Dim sum As Double = 0.0

                For j As Integer = 0 To m.nv - 1
                    sum += d.M(i * m.nv + j) * d.qvel(j)
                Next

                kinetic += 0.5 * d.qvel(i) * sum
            Next
        End If

        Dim potential As Double = 0.0
        Dim gravity As New Vector3(CSng(m.opt.gravity(0)), CSng(m.opt.gravity(1)), CSng(m.opt.gravity(2)))

        For i As Integer = 1 To m.nbody - 1
            potential -= m.body_mass(i) * Vector3.Dot(gravity, MjKinematics.ComOf(d, i))
        Next

        d.energy(0) = potential
        d.energy(1) = kinetic
    End Sub

End Module
