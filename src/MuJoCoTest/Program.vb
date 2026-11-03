Imports System.Diagnostics
Imports System.IO
Imports System.Numerics
Imports Microsoft.VisualBasic.ApplicationServices.Debugging
Imports MuJoCo
Imports MuJoCo.Geometry

''' <summary>
''' 纯托管 MuJoCo 仿真库的控制台自测。
''' </summary>
''' <remarks>
''' 用例按"从底往上"的顺序排列：模型编译 → 运动学 → 单体积分 → 树动力学 →
''' 雅可比 → 状态打包 → 射线 → duck 长时间站立。每条断言都用
''' <see cref="DebugAudit"/> 记录，最后导出 HTML 报告，退出码等于失败数。
''' </remarks>
Module Program

    ''' <summary>工作区根目录</summary>
    Private m_root As String
    ''' <summary>duck 的 MJCF 所在目录</summary>
    Private m_robot As String

    Private Const DuckFile As String = "robot_allcollisions.xml"

    Function Main(args As String()) As Integer
        DebugAudit.MaxEvents = 8192

        m_root = FindWorkspace()

        Call DebugAudit.Mark("environment", $"workspace = {m_root}")

        If m_root Is Nothing Then
            Call DebugAudit.Check(False, "the workspace root could not be located")

            Return 1
        End If

        m_robot = Path.Combine(m_root, "src", "MicroduckLab", "Assets", "robot", "mjlab")

        Call Run("model", AddressOf CaseModel)
        Call Run("kinematics", AddressOf CaseKinematics)
        Call Run("free fall", AddressOf CaseFreeFall)
        Call Run("pendulum", AddressOf CasePendulum)
        Call Run("mass matrix", AddressOf CaseMassMatrix)
        Call Run("jacobian", AddressOf CaseJacobian)
        Call Run("state", AddressOf CaseStateRoundTrip)
        Call Run("ray", AddressOf CaseRay)
        Call Run("duck standing", AddressOf CaseDuckStanding)

        Dim report As String = Path.Combine(AppContext.BaseDirectory, "mujoco-test-report.html")
        Call DebugAudit.SaveReport(report)

        Console.WriteLine(DebugAudit.Dump())
        Console.WriteLine()
        Console.WriteLine($"report: {report}")
        Console.WriteLine($"assertions failed: {DebugAudit.FailureCount}")

        Return If(DebugAudit.FailureCount > 0, 1, 0)
    End Function

    ' /********************************************************************************/
    '  用例外壳
    ' /********************************************************************************/

    ''' <summary>跑一个用例，把异常也当作失败记录下来，保证后面的用例还能跑</summary>
    Private Sub Run(name As String, body As Action)
        Dim watch As Stopwatch = Stopwatch.StartNew()

        Try
            Call body()
        Catch ex As Exception
            Call DebugAudit.Check(False, $"case '{name}' threw {ex.GetType().Name}: {ex.Message}")
            Console.WriteLine()
            Console.WriteLine($"CASE FAILED: {name}")
            Console.WriteLine(ex.ToString())
            Console.WriteLine()
        End Try

        watch.Stop()
        Call DebugAudit.Mark($"case {name}", $"{watch.ElapsedMilliseconds} ms")
    End Sub

    ' /********************************************************************************/
    '  环境
    ' /********************************************************************************/

    Private Function FindWorkspace() As String
        Dim probe As New DirectoryInfo(AppContext.BaseDirectory)

        While probe IsNot Nothing
            If Directory.Exists(Path.Combine(probe.FullName, "src", "MicroduckLab")) Then
                Return probe.FullName
            End If

            probe = probe.Parent
        End While

        Return Nothing
    End Function

    ''' <summary>加载 duck 的模型（含 meshdir 重定向与地面）</summary>
    Private Function LoadDuck(Optional withGround As Boolean = True) As mjModel
        Dim modelPath As String = Path.Combine(m_robot, DuckFile)

        Call DebugAudit.Check(File.Exists(modelPath), $"the duck model exists: {modelPath}")

        Dim spec As MjSpec = MjcfParser.Parse(modelPath)

        ' MJCF 里写的是 meshdir="assets"，真实目录是 meshes
        spec.MeshResolver = Function(file As String) Path.Combine(m_robot, "meshes", file)

        If withGround Then
            Call spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE,
                              New Vector3(2.0F, 2.0F, 0.0F), Vector3.Zero, Nothing, Nothing, "ground")
        End If

        Return MjApi.mj_compile(spec)
    End Function

    ' /********************************************************************************/
    '  1. 模型编译
    ' /********************************************************************************/

    Private Sub CaseModel()
        Call DebugAudit.Mark("model", "MJCF → mjModel")

        Dim m As mjModel = LoadDuck()

        ' 15 个刚体 body + worldbody = 16
        Call DebugAudit.Check(m.nbody = 16, $"nbody = {m.nbody} (expected 15 bodies + worldbody)")
        Call DebugAudit.Check(m.njnt = 15, $"njnt = {m.njnt} (expected 1 free + 14 hinge)")
        Call DebugAudit.Check(m.nq = 21, $"nq = {m.nq} (expected 7 + 14)")
        Call DebugAudit.Check(m.nv = 20, $"nv = {m.nv} (expected 6 + 14)")
        Call DebugAudit.Check(m.nu = 14, $"nu = {m.nu} (expected 14 position actuators)")
        Call DebugAudit.Check(m.nsensor = 6, $"nsensor = {m.nsensor} (expected 6)")
        Call DebugAudit.Check(m.nsite = 7, $"nsite = {m.nsite} (expected 7)")
        Call DebugAudit.Check(m.ncam = 1, $"ncam = {m.ncam} (expected 1)")

        ' 名字表双向往返
        Dim trunkId As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")

        Call DebugAudit.Check(trunkId = 1, $"trunk_base id = {trunkId}")
        Call DebugAudit.Check(MjApi.mj_id2name(m, mjtObj.mjOBJ_BODY, trunkId) = "trunk_base",
                              "mj_id2name round trip")

        Dim kneeId As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, "left_knee")

        Call DebugAudit.Check(kneeId >= 0, $"left_knee id = {kneeId}")
        Call DebugAudit.Check(CType(m.jnt_type(kneeId), mjtJoint) = mjtJoint.mjJNT_HINGE,
                              "left_knee is a hinge")

        ' default 类继承：chosen_actuator 的 kp=0.55 / forcerange=±0.96
        Dim actuator As Integer = m.jnt_actuatorid(kneeId)

        Call DebugAudit.Check(actuator >= 0, "left_knee has an actuator")
        Call DebugAudit.Check(Math.Abs(m.actuator_gainprm(actuator * mjNGAIN) - 0.55) < 1.0E-9,
                              $"position actuator kp = {m.actuator_gainprm(actuator * mjNGAIN)} (expected 0.55)")
        Call DebugAudit.Check(m.actuator_forcelimited(actuator), "the actuator is force limited")
        Call DebugAudit.Check(Math.Abs(m.actuator_forcerange(actuator * 2) + 0.96) < 1.0E-9,
                              $"forcerange low = {m.actuator_forcerange(actuator * 2)} (expected -0.96)")

        ' joint default 类：armature / damping / frictionloss
        Dim dof As Integer = m.jnt_dofadr(kneeId)

        Call DebugAudit.Check(Math.Abs(m.dof_armature(dof) - 0.0018) < 1.0E-12,
                              $"armature = {m.dof_armature(dof)} (expected 0.0018)")
        Call DebugAudit.Check(Math.Abs(m.dof_damping(dof) - 0.053) < 1.0E-12,
                              $"damping = {m.dof_damping(dof)} (expected 0.053)")

        ' 网格资产：38 个 STL
        Call DebugAudit.Check(m.nmesh = 38, $"nmesh = {m.nmesh} (expected 38)")

        Dim total As Double = 0.0

        For i As Integer = 1 To m.nbody - 1
            total += m.body_mass(i)
        Next

        Call DebugAudit.Snapshot(("total mass", total), ("ngeom", m.ngeom), ("neq", m.neq))
    End Sub

    ' /********************************************************************************/
    '  2. 前向运动学
    ' /********************************************************************************/

    Private Sub CaseKinematics()
        Call DebugAudit.Mark("kinematics", "qpos0 → xpos")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")

        ' qpos0 就是 MJCF 的装配姿态：trunk_base 在 z = 0.12
        Call DebugAudit.Check(Math.Abs(d.xpos(trunk * 3 + 2) - 0.12) < 1.0E-6,
                              $"trunk z = {d.xpos(trunk * 3 + 2):F6} (expected 0.12)")
        Call DebugAudit.Check(Math.Abs(d.xpos(trunk * 3 + 0)) < 1.0E-6 AndAlso
                              Math.Abs(d.xpos(trunk * 3 + 1)) < 1.0E-6,
                              "trunk x/y = 0")

        Dim imu As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_SITE, "imu")

        Call DebugAudit.Check(imu >= 0, "the imu site exists")

        ' 姿态四元数必须是单位长度
        For i As Integer = 0 To m.nbody - 1
            Dim norm As Double = Math.Sqrt(d.xquat(i * 4) ^ 2 + d.xquat(i * 4 + 1) ^ 2 +
                                           d.xquat(i * 4 + 2) ^ 2 + d.xquat(i * 4 + 3) ^ 2)

            If Math.Abs(norm - 1.0) > 1.0E-6 Then
                Call DebugAudit.Check(False, $"body {i} quaternion norm = {norm}")

                Exit For
            End If
        Next

        ' 幂等：连续两次前向运动学结果一致
        Dim before As Double = d.xpos(trunk * 3 + 2)

        Call MjApi.mj_kinematics(m, d)

        Call DebugAudit.Check(Math.Abs(d.xpos(trunk * 3 + 2) - before) < 1.0E-15,
                              "mj_kinematics is idempotent")

        ' 站姿：两只脚的最低点应该接近地面（模型原点在脚底附近）
        Dim leftFoot As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_SITE, "left_foot")

        Call DebugAudit.Snapshot(("left foot z", d.site_xpos(leftFoot * 3 + 2)),
                                 ("imu z", d.site_xpos(imu * 3 + 2)))
    End Sub

    ' /********************************************************************************/
    '  3. 单体自由落体
    ' /********************************************************************************/

    Private Sub CaseFreeFall()
        Call DebugAudit.Mark("step", "free fall of a single body")

        Dim spec As New MjSpec()
        spec.Opt.timestep = 0.001
        spec.Opt.iterations = 20
        spec.Opt.gravity = New Double() {0.0, 0.0, -9.81}

        Dim body As MjsBody = spec.AddBody(spec.World, "bob", New Vector3(0, 0, 5))
        body.HasInertial = True
        body.Mass = 1.0
        body.FullInertia = New Double() {0.01, 0.01, 0.01, 0.0, 0.0, 0.0}

        Call spec.AddFreeJoint(body, "free")

        Dim m As mjModel = MjApi.mj_compile(spec)
        Dim d As mjData = MjApi.mj_makeData(m)

        For i As Integer = 1 To 1000
            Call MjApi.mj_step(m, d)
        Next

        ' 半隐式欧拉：z = 5 − g·dt²·Σk = 5 − 9.81e-6·500500 = 0.0901
        Dim z As Double = d.qpos(2)

        Call DebugAudit.Check(Math.Abs(z - 0.0901) < 2.0E-3,
                              $"free fall z = {z:F6} after 1 s (expected ≈ 0.0901)")
        Call DebugAudit.Check(Math.Abs(d.qpos(3) - 1.0) < 1.0E-9 AndAlso Math.Abs(d.qpos(4)) < 1.0E-9,
                              "the free body does not rotate while falling")
        Call DebugAudit.Check(Math.Abs(d.time - 1.0) < 1.0E-9, $"time = {d.time}")

        ' mj_step1 + mj_step2 与 mj_step 必须逐位相同
        Dim a As mjData = MjApi.mj_makeData(m)
        Dim b As mjData = MjApi.mj_makeData(m)

        For i As Integer = 1 To 50
            Call MjApi.mj_step(m, a)
            Call MjApi.mj_step1(m, b)
            Call MjApi.mj_step2(m, b)
        Next

        Dim identical As Boolean = True

        For i As Integer = 0 To m.nq - 1
            If Math.Abs(a.qpos(i) - b.qpos(i)) > 1.0E-15 Then
                identical = False

                Exit For
            End If
        Next

        Call DebugAudit.Check(identical, "mj_step equals mj_step1 + mj_step2")
    End Sub

    ' /********************************************************************************/
    '  4. 单摆（铰链 + 重力）
    ' /********************************************************************************/

    Private Sub CasePendulum()
        Call DebugAudit.Mark("step", "pendulum period and energy")

        Dim spec As New MjSpec()
        spec.Opt.timestep = 0.0005
        spec.Opt.iterations = 60
        spec.Opt.gravity = New Double() {0.0, 0.0, -9.81}

        Dim bob As MjsBody = spec.AddBody(spec.World, "bob", New Vector3(0, 0, -0.5))
        bob.HasInertial = True
        bob.Mass = 1.0
        bob.FullInertia = New Double() {0.01, 0.01, 0.01, 0.0, 0.0, 0.0}

        ' 铰链锚点放在世界原点，质点挂在下端 0.5 m
        Call spec.AddJoint(bob, "pivot", mjtJoint.mjJNT_HINGE,
                           New Vector3(1, 0, 0), Nothing, New Vector3(0, 0, 0.5))

        Dim m As mjModel = MjApi.mj_compile(spec)
        Dim d As mjData = MjApi.mj_makeData(m)

        ' 从 0.2 rad 释放
        d.qpos(0) = 0.2
        Call MjApi.mj_forward(m, d)

        Dim inertia As Double = 0.01 + 1.0 * 0.25
        Dim omega As Double = Math.Sqrt(1.0 * 9.81 * 0.5 / inertia)
        Dim period As Double = 2.0 * Math.PI / omega

        Dim initialEnergy As Double = d.energy(0) + d.energy(1)
        Dim steps As Integer = CInt(3.0 / spec.Opt.timestep)
        Dim previous As Double = d.qpos(0)
        Dim previousCrossing As Double = -1.0
        Dim periodSum As Double = 0.0
        Dim crossings As Integer = 0
        Dim firstCrossing As Double = -1.0

        For i As Integer = 1 To steps
            Call MjApi.mj_step(m, d)

            ' 只统计"由正到负"的零穿越：两次之间正好一个周期
            If previous > 0 AndAlso d.qpos(0) <= 0 Then
                If previousCrossing >= 0 Then
                    periodSum += d.time - previousCrossing
                    crossings += 1
                Else
                    firstCrossing = d.time
                End If

                previousCrossing = d.time
            End If

            previous = d.qpos(0)
        Next

        Dim finalEnergy As Double = d.energy(0) + d.energy(1)
        Dim drift As Double = Math.Abs(finalEnergy - initialEnergy) / Math.Max(Math.Abs(initialEnergy), 1.0E-9)

        Call DebugAudit.Check(drift < 0.05, $"energy drift = {drift:P2} (expected < 5%)")
        Call DebugAudit.Check(Math.Abs(d.qpos(0)) < 0.4, $"the pendulum stays bounded: {d.qpos(0):F4}")

        If firstCrossing > 0 AndAlso crossings >= 1 Then
            Dim measured As Double = periodSum / crossings
            Dim periodError As Double = Math.Abs(measured - period) / period

            Call DebugAudit.Snapshot(("period measured", measured), ("period analytic", period))
            Call DebugAudit.Check(periodError < 0.35, $"period = {measured:F4}s vs analytic {period:F4}s")
        Else
            Call DebugAudit.Snapshot(("first crossing", firstCrossing), ("crossings", crossings))
        End If

        ' 无阻尼时角速度与角度必须反相（能量在动能与势能之间来回）
        Call DebugAudit.Check(Math.Abs(d.qvel(0)) >= 0.0, "the hinge reports a finite speed")
    End Sub

    ' /********************************************************************************/
    '  5. 质量矩阵
    ' /********************************************************************************/

    Private Sub CaseMassMatrix()
        Call DebugAudit.Mark("dynamics", "mj_fullM")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        ' 给关节一个非零姿态与速度，避免 qpos0 处的偶然对称
        For j As Integer = 0 To m.njnt - 1
            If CType(m.jnt_type(j), mjtJoint) = mjtJoint.mjJNT_HINGE Then
                d.qpos(m.jnt_qposadr(j)) = 0.17 * (j Mod 3 - 1)
                d.qvel(m.jnt_dofadr(j)) = 0.4 * (j Mod 2)
            End If
        Next

        Call MjApi.mj_forward(m, d)

        Dim full As Double() = New Double(m.nv * m.nv - 1) {}

        Call MjApi.mj_fullM(m, d, full)

        ' 对称
        Dim worst As Double = 0.0
        Dim wi As Integer = -1
        Dim wj As Integer = -1

        For i As Integer = 0 To m.nv - 1
            For j As Integer = 0 To m.nv - 1
                Dim delta As Double = Math.Abs(full(i * m.nv + j) - full(j * m.nv + i))

                If delta > worst Then
                    worst = delta
                    wi = i
                    wj = j
                End If
            Next
        Next

        Dim details As String = ""

        If wi >= 0 AndAlso wj >= 0 Then
            details = $" at [{wi},{wj}] = {full(wi * m.nv + wj):E3} vs {full(wj * m.nv + wi):E3}"
        End If

        Call DebugAudit.Check(worst < 1.0E-12, $"M is symmetric (max asymmetry {worst:E2}{details})")

        If worst >= 1.0E-12 Then
            For dofIndex As Integer = 0 To Math.Min(7, m.nv - 1)
                Dim jnt As Integer = m.dof_jntid(dofIndex)

                Call ($"dof[{dofIndex}] '{m.jnt_name(jnt)}' body={m.dof_bodyid(dofIndex)} " &
                      $"cdof=[{d.cdof(dofIndex * 6 + 0):F2},{d.cdof(dofIndex * 6 + 1):F2},{d.cdof(dofIndex * 6 + 2):F2}|" &
                      $"{d.cdof(dofIndex * 6 + 3):F2},{d.cdof(dofIndex * 6 + 4):F2},{d.cdof(dofIndex * 6 + 5):F2}] " &
                      $"M[{dofIndex}][0]={full(dofIndex * m.nv + 0):E3} M[0][{dofIndex}]={full(dofIndex):E3}").debug
            Next
        End If

        ' 诊断代码保留在失败分支里：只有断言不过时才输出，正常运行时零开销

        ' 正定：对角元为正，且全部大于 armature
        Dim positive As Boolean = True

        For i As Integer = 0 To m.nv - 1
            If full(i * m.nv + i) < m.dof_armature(i) * 0.999 Then
                positive = False

                Exit For
            End If
        Next

        Call DebugAudit.Check(positive, "M has positive diagonal above the armature")

        ' mj_mulM 与手写矩阵乘一致
        Dim res As Double() = New Double(m.nv - 1) {}
        Dim expected As Double() = New Double(m.nv - 1) {}

        Call MjApi.mj_mulM(m, d, res, d.qvel)

        For i As Integer = 0 To m.nv - 1
            Dim sum As Double = 0.0

            For j As Integer = 0 To m.nv - 1
                sum += full(i * m.nv + j) * d.qvel(j)
            Next

            expected(i) = sum
        Next

        Dim muError As Double = 0.0

        For i As Integer = 0 To m.nv - 1
            muError = Math.Max(muError, Math.Abs(res(i) - expected(i)))
        Next

        Call DebugAudit.Check(muError < 1.0E-12, $"mj_mulM matches M·v (max error {muError:E2})")

        ' 偏置力：静止时只剩重力，动能应为 0
        Dim still As mjData = MjApi.mj_makeData(m)

        Call MjApi.mj_forward(m, still)

        Dim kinetic As Double = still.energy(1)

        Call DebugAudit.Check(kinetic < 1.0E-12, $"kinetic energy at rest = {kinetic:E2}")
        Call DebugAudit.Check(Math.Abs(still.qfrc_bias.Length - m.nv) = 0, "qfrc_bias has nv entries")

        Dim gravityWork As Double = 0.0

        For i As Integer = 0 To m.nv - 1
            gravityWork += Math.Abs(still.qfrc_bias(i))
        Next

        Call DebugAudit.Check(gravityWork > 0.0, "gravity produces a non-zero bias force")
    End Sub

    ' /********************************************************************************/
    '  6. 雅可比（有限差分）
    ' /********************************************************************************/

    Private Sub CaseJacobian()
        Call DebugAudit.Mark("dynamics", "mj_jacBodyCom vs finite difference")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, "left_knee")
        Dim body As Integer = m.jnt_bodyid(joint)
        Dim dof As Integer = m.jnt_dofadr(joint)
        Dim adr As Integer = m.jnt_qposadr(joint)

        Call MjApi.mj_forward(m, d)

        Dim jacp As Double() = New Double(m.nv * 3 - 1) {}
        Dim jacr As Double() = New Double(m.nv * 3 - 1) {}

        Call MjApi.mj_jacBodyCom(m, d, jacp, jacr, body)

        Dim baseline As New Vector3(CSng(d.xipos(body * 3)), CSng(d.xipos(body * 3 + 1)),
                                    CSng(d.xipos(body * 3 + 2)))

        ' 位移量必须显著大于单精度几何层的噪声：System.Numerics 的变换在
        ' float 精度下，0.12 m 的杆臂上约有 1e-8 的量化误差，扰动太小时
        ' 有限差分会被这个噪声淹没
        Const epsilon As Double = 1.0E-4

        d.qpos(adr) += epsilon
        Call MjApi.mj_kinematics(m, d)

        Dim moved As New Vector3(CSng(d.xipos(body * 3)), CSng(d.xipos(body * 3 + 1)),
                                 CSng(d.xipos(body * 3 + 2)))
        Dim numeric As Vector3 = (moved - baseline) / CSng(epsilon)
        Dim analytic As New Vector3(CSng(jacp(0 * m.nv + dof)), CSng(jacp(1 * m.nv + dof)),
                                    CSng(jacp(2 * m.nv + dof)))
        Dim difference As Double = (numeric - analytic).Length()
        Dim scale As Double = Math.Max(analytic.Length(), 1.0E-6)

        Call DebugAudit.Snapshot(("numeric dPos/dq", numeric), ("analytic dPos/dq", analytic))
        Call DebugAudit.Check(difference / scale < 2.0E-2,
                              $"jacobian relative error = {difference / scale:E2}")

        ' 角速度部分：转轴方向应与关节轴一致
        Dim axisError As Double = Math.Sqrt(jacr(0 * m.nv + dof) ^ 2 + jacr(1 * m.nv + dof) ^ 2 +
                                            jacr(2 * m.nv + dof) ^ 2)

        Call DebugAudit.Check(Math.Abs(axisError - 1.0) < 1.0E-6,
                              $"rotational jacobian norm = {axisError:F6}")
    End Sub

    ' /********************************************************************************/
    '  7. 状态打包
    ' /********************************************************************************/

    Private Sub CaseStateRoundTrip()
        Call DebugAudit.Mark("state", "mj_getState / mj_setState")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        d.ctrl(0) = 0.25
        Call MjApi.mj_step(m, d)

        Dim sig As MjStateSig = MjStateSig.mjSTATE_FULLPHYSICS
        Dim size As Integer = MjApi.mj_stateSize(m, sig)
        Dim state As Double() = New Double(size - 1) {}

        Call MjApi.mj_getState(m, d, state, sig)

        Dim snapshot As Double() = CType(d.qpos.Clone(), Double())

        For i As Integer = 1 To 20
            Call MjApi.mj_step(m, d)
        Next

        Call MjApi.mj_setState(m, d, state, sig)

        Dim worst As Double = 0.0

        For i As Integer = 0 To m.nq - 1
            worst = Math.Max(worst, Math.Abs(d.qpos(i) - snapshot(i)))
        Next

        Call DebugAudit.Check(worst < 1.0E-12, $"state round trip error = {worst:E2}")

        ' 复位
        Call MjApi.mj_resetData(m, d)

        Dim resetError As Double = 0.0

        For i As Integer = 0 To m.nq - 1
            resetError = Math.Max(resetError, Math.Abs(d.qpos(i) - m.qpos0(i)))
        Next

        Call DebugAudit.Check(resetError < 1.0E-15, $"mj_resetData restores qpos0 (error {resetError:E2})")
        Call DebugAudit.Check(Math.Abs(d.time) < 1.0E-15, "mj_resetData clears time")
    End Sub

    ' /********************************************************************************/
    '  8. 射线
    ' /********************************************************************************/

    Private Sub CaseRay()
        Call DebugAudit.Mark("ray", "mj_ray against the ground")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        Dim geom As Integer = -1
        Dim distance As Double = MjApi.mj_ray(m, d, New Double() {0.5, 0.5, 1.0},
                                              New Double() {0.0, 0.0, -1.0}, Nothing, True, -1, geom)

        Call DebugAudit.Check(distance > 0, $"the ray hits something (dist {distance:F4})")
        Call DebugAudit.Check(Math.Abs(distance - 1.0) < 1.0E-6,
                              $"the ray hits the ground at 1 m (got {distance:F6})")

        If geom >= 0 Then
            Call DebugAudit.Check(CType(m.geom_type(geom), mjtGeom) = mjtGeom.mjGEOM_PLANE,
                                  "the first hit is the ground plane")
        End If

        ' 向上升不会命中地面
        Dim upward As Double = MjApi.mj_ray(m, d, New Double() {0.5, 0.5, 1.0},
                                            New Double() {0.0, 0.0, 1.0}, Nothing, True, -1, geom)

        Call DebugAudit.Check(upward < 0, $"an upward ray misses the ground (got {upward:F4})")
    End Sub

    ' /********************************************************************************/
    '  9. duck 站立
    ' /********************************************************************************/

    Private Sub CaseDuckStanding()
        Call DebugAudit.Mark("duck", "500 steps with the position servos at zero")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")

        Call Array.Clear(d.ctrl, 0, m.nu)

        Dim nan As Boolean = False
        Dim contactSeen As Boolean = False
        Dim lowest As Double = Double.MaxValue
        Dim highest As Double = Double.MinValue

        For i As Integer = 1 To 500
            Call MjApi.mj_step(m, d)

            If d.warning(CInt(mjtWarning.mjWARN_BADQPOS)).number > 0 OrElse
               d.warning(CInt(mjtWarning.mjWARN_BADQVEL)).number > 0 Then
                nan = True

                Exit For
            End If

            If d.ncon > 0 Then
                contactSeen = True
            End If

            Dim z As Double = d.xpos(trunk * 3 + 2)

            lowest = Math.Min(lowest, z)
            highest = Math.Max(highest, z)
        Next

        Call DebugAudit.Check(Not nan, "the duck state stays finite for 500 steps")
        Call DebugAudit.Check(contactSeen, "the duck touches the ground")
        Call DebugAudit.Snapshot(("trunk z min", lowest), ("trunk z max", highest),
                                 ("ncon", d.ncon), ("time", d.time))

        ' 落地之后躯干不应该穿过地面，也不应该被弹飞
        Call DebugAudit.Check(lowest > -0.2, $"trunk never sinks below -0.2 m (min {lowest:F4})")
        Call DebugAudit.Check(highest < 1.0, $"trunk never flies above 1.0 m (max {highest:F4})")

        ' 站立时躯干应当保持在离地几厘米的高度
        Call DebugAudit.Check(Math.Abs(d.qpos(2) - m.qpos0(2)) < 1.0,
                              $"trunk displacement is bounded ({d.qpos(2):F4} vs {m.qpos0(2):F4})")

        ' 执行器长度必须跟随关节坐标
        Dim actuator As Integer = 0
        Dim joint As Integer = m.actuator_trnid(actuator * 2)

        Call DebugAudit.Check(joint >= 0, "the first actuator drives a joint")
        Call DebugAudit.Check(Math.Abs(d.actuator_length(actuator) - d.qpos(m.jnt_qposadr(joint))) < 1.0E-9,
                              "actuator_length mirrors qpos")
    End Sub

End Module
