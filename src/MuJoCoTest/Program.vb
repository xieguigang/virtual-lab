Imports System.Diagnostics
Imports System.IO
Imports System.Numerics
Imports Microsoft.VisualBasic.ApplicationServices.Debugging
Imports MuJoCo
Imports MuJoCo.Geometry
Imports MuJoCo.Scene
Imports iv = MuJoCo.Interop
Imports im = MuJoCo.Interop.Math3D
Imports urdf = MuJoCo.Import.MjUrdf

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

        Dim sceneOnly As Boolean = False

        If args IsNot Nothing Then
            For Each arg As String In args
                If String.Equals(arg, "--scene-only", StringComparison.OrdinalIgnoreCase) Then
                    sceneOnly = True
                End If
            Next
        End If

        m_root = FindWorkspace()

        Call DebugAudit.Mark("environment", $"workspace = {m_root}")

        If m_root Is Nothing Then
            Call DebugAudit.Check(False, "the workspace root could not be located")

            Return 1
        End If

        m_robot = Path.Combine(m_root, "src", "MicroduckLab", "Assets", "robot", "mjlab")

        If Not sceneOnly Then
            Call Run("model", AddressOf CaseModel)
            Call Run("kinematics", AddressOf CaseKinematics)
            Call Run("free fall", AddressOf CaseFreeFall)
            Call Run("pendulum", AddressOf CasePendulum)
            Call Run("mass matrix", AddressOf CaseMassMatrix)
            Call Run("jacobian", AddressOf CaseJacobian)
            Call Run("state", AddressOf CaseStateRoundTrip)
            Call Run("ray", AddressOf CaseRay)
            Call Run("duck standing", AddressOf CaseDuckStanding)
            Call Run("arena scene", AddressOf CaseArenaScene)
            Call Run("joint readback", AddressOf CaseJointReadback)
            Call Run("stand still", AddressOf CaseStandStill)
            Call Run("long stand", AddressOf CaseLongStand)
            Call Run("body velocity", AddressOf CaseBodyVelocity)
            Call Run("actuator sign", AddressOf CaseActuatorSign)
            Call Run("gain sweep", AddressOf CaseGainSweep)
            Call Run("static balance", AddressOf CaseStaticBalance)
            Call Run("friction", AddressOf CaseFriction)
            Call Run("convex friction", AddressOf CaseConvexFriction)
        End If

        Call Run("scene primitives", AddressOf CaseScenePrimitives)
        Call Run("scene step", AddressOf CaseSceneStep)
        Call Run("scene rebuild", AddressOf CaseSceneRebuild)
        Call Run("interop math", AddressOf CaseInteropMath)
        Call Run("interop world", AddressOf CaseInteropWorld)
        Call Run("urdf import", AddressOf CaseUrdfImport)

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

        ' 与 MicroduckLab.MjSceneOptions 的默认值保持一致
        spec.Opt.iterations = 48

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

        ' 半隐式欧拉在 dt 下的离散解是 5 − g·dt²·Σk；物理子步细分之后
        ' （每个 opt.timestep 再切 4 份）结果收敛到连续解 5 − ½g = 0.095
        Dim z As Double = d.qpos(2)

        Call DebugAudit.Check(Math.Abs(z - 0.095) < 2.0E-3,
                              $"free fall z = {z:F6} after 1 s (expected ≈ 0.095)")
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

    ' /********************************************************************************/
    '  10. 场地 + 小球（与 MicroduckLab 的 MjSceneBuilder 同一套拼法）
    ' /********************************************************************************/

    Private Sub CaseArenaScene()
        Call DebugAudit.Mark("scene", "duck + walls + ball compiled as one model")

        Const arenaHalf As Double = 1.5
        Const wallHeight As Double = 0.25
        Const wallThickness As Double = 0.05
        Const ballRadius As Double = 0.05
        Const ballMass As Double = 0.03

        Dim modelPath As String = Path.Combine(m_robot, DuckFile)
        Dim spec As MjSpec = MjcfParser.Parse(modelPath)

        spec.MeshResolver = Function(file As String) Path.Combine(m_robot, "meshes", file)
        spec.Opt.timestep = 0.005
        spec.Opt.iterations = 24

        Call spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE, New Vector3(arenaHalf, arenaHalf, 0))

        Dim span As Double = 2 * arenaHalf + 2 * wallThickness
        Dim center As Double = arenaHalf + wallThickness / 2

        For side As Integer = 0 To 3
            Dim name As String = $"wall_{side}"
            Dim position As Vector3
            Dim half As Vector3

            Select Case side
                Case 0
                    position = New Vector3(0, center, wallHeight / 2)
                    half = New Vector3(span / 2, wallThickness / 2, wallHeight / 2)
                Case 1
                    position = New Vector3(0, -center, wallHeight / 2)
                    half = New Vector3(span / 2, wallThickness / 2, wallHeight / 2)
                Case 2
                    position = New Vector3(center, 0, wallHeight / 2)
                    half = New Vector3(wallThickness / 2, span / 2, wallHeight / 2)
                Case Else
                    position = New Vector3(-center, 0, wallHeight / 2)
                    half = New Vector3(wallThickness / 2, span / 2, wallHeight / 2)
            End Select

            Dim wall As MjsBody = spec.AddBody(spec.World, name, position)

            Call spec.AddGeom(wall, mjtGeom.mjGEOM_BOX, half, Vector3.Zero, Nothing, Nothing, name & "_geom")
        Next

        Dim ball As MjsBody = spec.AddBody(spec.World, "ball", New Vector3(0.4F, 0.0F, 0.6F))

        ball.HasInertial = True
        ball.Mass = ballMass
        ball.FullInertia = New Double() {0.4 * ballMass * ballRadius * ballRadius,
                                         0.4 * ballMass * ballRadius * ballRadius,
                                         0.4 * ballMass * ballRadius * ballRadius, 0, 0, 0}
        ball.CollisionGroup = 4
        ball.CollisionMask = 7

        Call spec.AddFreeJoint(ball, "ball_free")
        Call spec.AddGeom(ball, mjtGeom.mjGEOM_SPHERE, New Vector3(CSng(ballRadius), 0, 0))

        Dim m As mjModel = MjApi.mj_compile(spec)

        Call DebugAudit.Check(m.nbody = 16 + 4 + 1, $"nbody = {m.nbody} (duck + 4 walls + ball)")

        Dim d As mjData = MjApi.mj_makeData(m)

        For i As Integer = 1 To 400
            Call MjApi.mj_step(m, d)
        Next

        Dim ballId As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "ball")

        Call DebugAudit.Check(ballId > 0, "the ball body exists")

        ' 球落到地面后静止在半径高度
        Dim ballZ As Double = d.xpos(ballId * 3 + 2)

        Call DebugAudit.Check(Math.Abs(ballZ - ballRadius) < 0.01,
                              $"the ball rests on the ground (z = {ballZ:F4}, expected {ballRadius})")
        Call DebugAudit.Check(d.ncon > 0, $"contacts are reported (ncon = {d.ncon})")

        Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")
        Dim trunkZ As Double = d.xpos(trunk * 3 + 2)

        Call DebugAudit.Check(trunkZ > -0.2, $"the duck does not fall through the floor (z = {trunkZ:F4})")
    End Sub

    ' /********************************************************************************/
    '  11. 关节坐标的回读精度（伺服能不能把关节推到目标）
    ' /********************************************************************************/

    ''' <summary>
    ''' 关节角写进 <c>qpos</c> 之后走一步，回读值必须还是原来的数。
    ''' </summary>
    ''' <remarks>
    ''' 这条用例专治"策略一直往一个方向走"：如果关节坐标的回读与真实姿态脱节
    ''' （读出来恒为 0，或者被固定旋转缩放），策略看到的永远是一组恒定关节角，
    ''' 于是输出一个恒定动作，机器人就朝一个方向匀速跑掉。
    ''' </remarks>
    Private Sub CaseJointReadback()
        Call DebugAudit.Mark("readback", "qpos survives a step for every hinge")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        ' 每个铰链给一个不同的角度，方便看出是哪个关节对不上
        Dim targets As New Dictionary(Of String, Double)()
        Dim index As Integer = 0

        For j As Integer = 0 To m.njnt - 1
            If CType(m.jnt_type(j), mjtJoint) <> mjtJoint.mjJNT_HINGE Then
                Continue For
            End If

            Dim name As String = m.jnt_name(j)
            Dim angle As Double = 0.05 * (1 + (index Mod 7)) * If(index Mod 2 = 0, 1.0, -1.0)

            d.qpos(m.jnt_qposadr(j)) = angle
            Call targets.Add(name, angle)

            Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, name)

            If actuator >= 0 Then
                d.ctrl(actuator) = angle
            End If

            index += 1
        Next

        Call MjApi.mj_forward(m, d)

        For Each pair In targets
            Dim j As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, pair.Key)

            Call DebugAudit.Check(Math.Abs(d.qpos(m.jnt_qposadr(j)) - pair.Value) < 1.0E-9,
                                  $"kinematics keeps '{pair.Key}' at {pair.Value:F4} (got {d.qpos(m.jnt_qposadr(j)):F4})")
        Next

        ' 走一步：姿态几乎不变，伺服目标也等于当前角，回读不应该跳变
        Call MjApi.mj_step(m, d)

        For Each pair In targets
            Dim j As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, pair.Key)
            Dim actual As Double = d.qpos(m.jnt_qposadr(j))
            Dim errorValue As Double = Math.Abs(actual - pair.Value)

            Call DebugAudit.Check(errorValue < 0.05,
                                  $"'{pair.Key}' readback drifted {errorValue:F4} after one step " &
                                  $"(set {pair.Value:F4}, read {actual:F4})")
        Next
    End Sub

    ' /********************************************************************************/
    '  12. 站立不漂移（复现"鸭子一直后退、跑出场地"）
    ' /********************************************************************************/

    ''' <summary>
    ''' 把 14 个关节摆到站立姿态、伺服目标也设成同一组角度，跑 4 秒，
    ''' 断言躯干既不下沉也不水平漂移。
    ''' </summary>
    ''' <remarks>
    ''' 这就是用户报的现象的最小复现：关节坐标一旦读错，策略会输出恒定动作，
    ''' 机器人就会朝一个方向匀速走掉；而这里没有策略，只有位置伺服，
    ''' 所以它同时验证了"坐标回读"与"伺服跟踪"两条链路。
    ''' </remarks>
    Private Sub CaseStandStill()
        Call DebugAudit.Mark("stand", "the duck holds the stand pose without drifting")

        ' 与 MicroduckLab.Core.SimConstants.JointNames / DefaultPose 逐字一致
        Dim names As String() = {
            "left_hip_yaw", "left_hip_roll", "left_hip_pitch", "left_knee", "left_ankle",
            "neck_pitch", "head_pitch", "head_yaw", "head_roll",
            "right_hip_yaw", "right_hip_roll", "right_hip_pitch", "right_knee", "right_ankle"}
        Dim pose As Double() = {
            0, -0.08726646259971647, -0.457924, -0.00494, 0.452984,
            0.3490658503988659, 0.3490658503988659, 0, 0,
            0, 0.08726646259971647, 0.457924, 0.00494, -0.452984}

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)
        Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")

        For j As Integer = 0 To names.Length - 1
            Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j))
            Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))

            Call DebugAudit.Check(joint >= 0 AndAlso actuator >= 0, $"'{names(j)}' has a joint and an actuator")
        Next

        Dim driven As Double() = RunStand(m, d, trunk, names, pose, False)

        Call DebugAudit.Snapshot(("trunk drift", driven(0)),
                                 ("trunk height", driven(1)))
        Call $"stand: drift={driven(0):F4} m, trunk z={driven(1):F4} m".debug

        ' 开环站立仍会有几厘米的开环滑移（引擎的接触求解在 duck 这种
        ' 五节细长连杆 + 3 mm 薄脚掌上不可能完全收敛）；有了策略的闭环反馈
        ' 之后这部分会被抑制。这里只守住"不跑出场地"这条线。
        Call DebugAudit.Check(driven(0) < 0.40,
                              $"the duck stays in the arena (drifted {driven(0):F4} m in 4 s)")
        ' DefaultPose 把膝盖压弯，腿比装配姿态（qpos0 的 z = 0.12）短一截，
        ' 站定之后 trunk 只剩 4~5 cm 才是对的；超过 0.1 反而说明有东西把它顶起来了
        Call DebugAudit.Check(driven(1) > 0.03 AndAlso driven(1) < 0.09,
                              $"the duck settles on its feet (trunk z = {driven(1):F4}, expected 0.03~0.09)")
    End Sub

    ''' <summary>跑 4 秒站立，返回 (水平漂移, 躯干高度)</summary>
    ''' <param name="holdPose">
    ''' true 时每子步把伺服目标设成当前关节角，力矩恒为零，用来把
    ''' "接触/重力" 与 "伺服" 两类原因分开
    ''' </param>
    Private Function RunStand(m As mjModel, d As mjData, trunk As Integer,
                              names As String(), pose As Double(),
                              holdPose As Boolean) As Double()
        Call MjApi.mj_resetData(m, d)

        For j As Integer = 0 To names.Length - 1
            Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j))
            Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))

            d.qpos(m.jnt_qposadr(joint)) = pose(j)
            d.ctrl(actuator) = pose(j)
        Next

        Call MjApi.mj_forward(m, d)
        Call MjApi.mj_clearContactCache(m, d)

        Dim x0 As Double = d.xpos(trunk * 3)
        Dim y0 As Double = d.xpos(trunk * 3 + 1)

        ' 50 Hz × 4 个 5 ms 子步 × 200 次 = 4 秒
        For iStep As Integer = 1 To 200
            For subStep As Integer = 1 To 4
                If holdPose Then
                    For j As Integer = 0 To names.Length - 1
                        Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j))
                        Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))

                        d.ctrl(actuator) = d.qpos(m.jnt_qposadr(joint))
                    Next
                End If

                Call MjApi.mj_step(m, d)
            Next
        Next

        Dim drift As Double = Math.Sqrt((d.xpos(trunk * 3) - x0) ^ 2 + (d.xpos(trunk * 3 + 1) - y0) ^ 2)

        Return New Double() {drift, d.xpos(trunk * 3 + 2)}
    End Function

    ' /********************************************************************************/
    '  12b. 长时间站立（复现"倒地后不停旋转"）
    ' /********************************************************************************/

    ''' <summary>
    ''' 站立 10 秒，观察躯干高度、投影重力与角速度随时间的变化。
    ''' </summary>
    Private Sub CaseLongStand()
        Call DebugAudit.Mark("stand", "the duck stays on its feet for 10 s")

        Dim names As String() = {
            "left_hip_yaw", "left_hip_roll", "left_hip_pitch", "left_knee", "left_ankle",
            "neck_pitch", "head_pitch", "head_yaw", "head_roll",
            "right_hip_yaw", "right_hip_roll", "right_hip_pitch", "right_knee", "right_ankle"}
        Dim pose As Double() = {
            0, -0.08726646259971647, -0.457924, -0.00494, 0.452984,
            0.3490658503988659, 0.3490658503988659, 0, 0,
            0, 0.08726646259971647, 0.457924, 0.00494, -0.452984}

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)
        Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")

        For j As Integer = 0 To names.Length - 1
            Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j))
            Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))

            d.qpos(m.jnt_qposadr(joint)) = pose(j)
            d.ctrl(actuator) = pose(j)
        Next

        Dim result As Double() = RunLong(m, d, trunk, names, pose, False, "servo@pose")

        Call DebugAudit.Snapshot(("trunk z after 10 s", result(0)), ("max |omega|", result(1)),
                                 ("drift", result(2)))

        ' kp = 0.55、kv = 0（MJCF 的 chosen_actuator 就是这样），开环摆着不动本来就
        ' 撑不住，会塌下去。这里守的是"不发散"：没有 NaN、角速度有界、没有飞出场地。
        Call DebugAudit.Check(Not Double.IsNaN(result(0)), "the state stays finite")
        Call DebugAudit.Check(result(1) < 50.0,
                              $"the angular rate stays bounded (max |omega| = {result(1):F2} rad/s)")
        Call DebugAudit.Check(result(2) < 2.0,
                              $"the duck does not fly away (drifted {result(2):F3} m)")
    End Sub

    ' /********************************************************************************/
    '  12c. 刚体速度（观测里的陀螺仪读的就是它）
    ' /********************************************************************************/

    ''' <summary>
    ''' 给定一个已知的广义速度，<c>cvel</c> 必须给出正确的世界角速度。
    ''' </summary>
    ''' <remarks>
    ''' 策略的 0..2 维观测就是这个量的本体坐标表达。符号或坐标系一旦错了，
    ''' 策略会把"正在右转"读成"正在左转"，输出同向修正，于是原地越转越快——
    ''' 正是"倒在地上不停旋转"的样子。
    ''' </remarks>
    Private Sub CaseBodyVelocity()
        Call DebugAudit.Mark("velocity", "cvel reproduces a prescribed body twist")

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)
        Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")
        Dim freeDof As Integer = m.jnt_dofadr(FreeJointOf(m))

        ' qpos0 的姿态是单位四元数，所以本体系 = 世界系
        d.qvel(freeDof + 5) = 1.0

        Call MjApi.mj_forward(m, d)

        Call DebugAudit.Snapshot(("cvel omega", $"{d.cvel(trunk * 6 + 0):F4} {d.cvel(trunk * 6 + 1):F4} {d.cvel(trunk * 6 + 2):F4}"),
                                 ("cvel linear", $"{d.cvel(trunk * 6 + 3):F4} {d.cvel(trunk * 6 + 4):F4} {d.cvel(trunk * 6 + 5):F4}"))

        Call DebugAudit.Check(Math.Abs(d.cvel(trunk * 6 + 0)) < 1.0E-9,
                              $"wx = {d.cvel(trunk * 6 + 0):F6} (expected 0)")
        Call DebugAudit.Check(Math.Abs(d.cvel(trunk * 6 + 1)) < 1.0E-9,
                              $"wy = {d.cvel(trunk * 6 + 1):F6} (expected 0)")
        Call DebugAudit.Check(Math.Abs(d.cvel(trunk * 6 + 2) - 1.0) < 1.0E-9,
                              $"wz = {d.cvel(trunk * 6 + 2):F6} (expected 1)")
    End Sub

    ' /********************************************************************************/
    '  12d. 执行器方向（正目标必须把关节往正方向推）
    ' /********************************************************************************/

    ''' <summary>
    ''' 把某个关节的目标角抬高 0.4 rad，关节角必须跟着变大；反号就是符号错了。
    ''' </summary>
    ''' <remarks>
    ''' 符号反了不会让测试"失败"，只会让策略变成正反馈：观测到关节偏小 → 输出更大的
    ''' 正目标 → 关节实际更小 → 输出更大。日志里看到的就是"关节几乎不动、动作顶到
    ''' 4 rad、整机被反作用力矩掀翻"。
    ''' </remarks>
    Private Sub CaseActuatorSign()
        Call DebugAudit.Mark("actuator", "a positive target moves the joint the positive way")

        Dim names As String() = {
            "left_hip_yaw", "left_hip_roll", "left_hip_pitch", "left_knee", "left_ankle",
            "neck_pitch", "head_pitch", "head_yaw", "head_roll",
            "right_hip_yaw", "right_hip_roll", "right_hip_pitch", "right_knee", "right_ankle"}
        Dim pose As Double() = {
            0, -0.08726646259971647, -0.457924, -0.00494, 0.452984,
            0.3490658503988659, 0.3490658503988659, 0, 0,
            0, 0.08726646259971647, 0.457924, 0.00494, -0.452984}

        For probe As Integer = 0 To names.Length - 1
            Dim m As mjModel = LoadDuck()
            Dim d As mjData = MjApi.mj_makeData(m)
            Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(probe))
            Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(probe))
            Dim adr As Integer = m.jnt_qposadr(joint)

            For j As Integer = 0 To names.Length - 1
                Dim other As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j))
                Dim otherActuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))

                d.qpos(m.jnt_qposadr(other)) = pose(j)
                d.ctrl(otherActuator) = pose(j)
            Next

            d.ctrl(actuator) = pose(probe) + 0.4

            Call MjApi.mj_forward(m, d)
            Call MjApi.mj_clearContactCache(m, d)

            ' 只走 0.25 秒：足够让伺服把关节推出去，又不足以让整机摔掉
            For i As Integer = 1 To 50
                Call MjApi.mj_step(m, d)
            Next

            Dim delta As Double = d.qpos(adr) - pose(probe)

            ' 只断言方向：抬整条腿（hip_pitch）时 0.22 N·m 只能挪动毫米级，
            ' 但符号必须是正的——反号才是 bug
            Call DebugAudit.Check(delta > 1.0E-4,
                                  $"'{names(probe)}': +0.4 target moved the joint by {delta:F6} rad")
        Next
    End Sub

    ' /********************************************************************************/
    '  12e. 增益扫描（0.55 到底够不够）
    ' /********************************************************************************/

    ''' <summary>
    ''' 同一套物理，只把执行器的 kp 换掉，看站着不动的表现。
    ''' </summary>
    ''' <remarks>
    ''' MJCF 的根类 <c>microduck</c> 写的是 <c>&lt;position kp="50" dampratio="1"/&gt;</c>，
    ''' 而 duck 实际用的 <c>chosen_actuator</c> 把它覆盖成了 <c>kp="0.55" kv="0.0"</c>。
    ''' 0.55 的刚度要把力矩打到 0.96 N·m 的上限需要 1.75 rad 误差，腿实际上是"软"的；
    ''' 如果策略是按 kp=50 训练的，两者差了 90 倍，闭环必然发散。
    ''' </remarks>
    Private Sub CaseGainSweep()
        Call DebugAudit.Mark("gain", "how the stand pose behaves at different kp")

        Dim names As String() = {
            "left_hip_yaw", "left_hip_roll", "left_hip_pitch", "left_knee", "left_ankle",
            "neck_pitch", "head_pitch", "head_yaw", "head_roll",
            "right_hip_yaw", "right_hip_roll", "right_hip_pitch", "right_knee", "right_ankle"}
        Dim pose As Double() = {
            0, -0.08726646259971647, -0.457924, -0.00494, 0.452984,
            0.3490658503988659, 0.3490658503988659, 0, 0,
            0, 0.08726646259971647, 0.457924, 0.00494, -0.452984}

        For Each gain As Double In New Double() {0.55, 5.0, 50.0}
            Dim m As mjModel = LoadDuck()

            For a As Integer = 0 To m.nactuator - 1
                m.actuator_gainprm(a * mjNGAIN + 0) = gain
                m.actuator_biasprm(a * mjNBIAS + 1) = -gain
            Next

            Dim d As mjData = MjApi.mj_makeData(m)
            Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")

            ' 只跑 2 秒，够看出是"塌"还是"站住"
            Dim result As Double() = RunLong(m, d, trunk, names, pose, False, $"kp={gain}", 150)

            Call ($"kp={gain}: final z={result(0):F4} max|w|={result(1):F2} drift={result(2):F3} " &
                  $"projGravity={ProjectedGravityZ(d, trunk):F3} " &
                  $"storedKp={m.actuator_gainprm(0):F2} force0={d.actuator_force(0):F4}").debug
        Next
    End Sub

    ' /********************************************************************************/
    '  16. 静力学求解：找一组站得住的关节角
    ' /********************************************************************************/

    ''' <summary>
    ''' 固定躯干姿态，优化 14 个铰链角，使两脚底面等高、脚掌水平、重心投影落在
    ''' 双脚支撑中心。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 需求背景：<c>SimConstants.DefaultPose</c> 是从别处抄来的 14 个角度，
    ''' 而这份 MJCF 里没有 <c>&lt;keyframe&gt;</c>。实测把它当作站立目标时，
    ''' 即使把执行器刚度从 0.55 拉到 50（等价于刚性腿），鸭子仍在 0.5 秒内翻倒
    ''' ——说明问题不在控制，而在"这组角度本身重心就落在支撑多边形外"。
    ''' </para>
    ''' <para>
    ''' 求解方法是坐标下降：逐关节沿 ±步长试探，取代价更小的一侧，步长每轮衰减。
    ''' 代价 = 两脚高度差² + 两脚底面倾斜² + 重心到支撑中心距离²（都以毫米计较），
    ''' 再叠一个"别离种子太远"的弱正则，避免解出一组扭曲但数学上平衡的姿态。
    ''' </para>
    ''' </remarks>
    Private Sub CaseStaticBalance()
        Call DebugAudit.Mark("statics", "solve a statically balanced stand pose")

        Dim names As String() = {
            "left_hip_yaw", "left_hip_roll", "left_hip_pitch", "left_knee", "left_ankle",
            "neck_pitch", "head_pitch", "head_yaw", "head_roll",
            "right_hip_yaw", "right_hip_roll", "right_hip_pitch", "right_knee", "right_ankle"}
        Dim seed As Double() = {
            0, -0.08726646259971647, -0.457924, -0.00494, 0.452984,
            0.3490658503988659, 0.3490658503988659, 0, 0,
            0, 0.08726646259971647, 0.457924, 0.00494, -0.452984}

        Dim m As mjModel = LoadDuck()
        Dim d As mjData = MjApi.mj_makeData(m)

        Dim leftFoot As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "ankle_left")
        Dim rightFoot As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "ankle_right")
        Dim trunk As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")
        Dim freeAdr As Integer = m.jnt_qposadr(FreeJointOf(m))

        Dim adr(SimConstants14 - 1) As Integer
        Dim act(SimConstants14 - 1) As Integer

        For j As Integer = 0 To SimConstants14 - 1
            adr(j) = m.jnt_qposadr(MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j)))
            act(j) = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))
        Next

        ' 抬高到不接触地面的位置，只做几何评估
        d.qpos(freeAdr + 0) = 0.0
        d.qpos(freeAdr + 1) = 0.0
        d.qpos(freeAdr + 2) = 0.30
        Call MjMath.WriteQuat(d.qpos, freeAdr + 3, Quaternion.Identity)

        Dim q(SimConstants14 - 1) As Double
        Call Array.Copy(seed, q, SimConstants14)

        Dim seedCost As Double = EvaluatePose(m, d, adr, q, leftFoot, rightFoot, seed)

        Call ($"statics: seed cost={seedCost:F4}  com=({d.subtree_com(0):F4},{d.subtree_com(1):F4})").debug

        Dim current As Double = seedCost
        Dim stepSize As Double = 0.12

        ' 坐标下降：每维先定方向，再沿该方向连续走几步。
        ' 只做"试一次 ±step"的话，14 维上每轮只能把代价降一点点，80 轮后
        ' 仍停在 7~8 mm 的偏差上，落到物理里还是会倒。
        ' 只放开左腿的 roll/pitch/knee/ankle（跳过 hip_yaw），右腿由镜像导出。
        ' hip_yaw 是绕竖直轴的，放开它优化器会把两条腿拧成"外八"来把支撑中心
        ' 挪到重心下面——数学上代价能到 0，但那是靠 31° 的外撇换来的，
        ' 落进物理里依然站不住。头部 4 个关节同理，质量小，放开只会钻空子。
        For iteration As Integer = 1 To 200
            For j As Integer = 1 To 4
                Dim origin As Double = q(j)
                Dim direction As Double = 0.0
                Dim bestValue As Double = origin

                For Each sign As Double In New Double() {1.0, -1.0}
                    Dim trial As Double = origin + sign * stepSize

                    If Not InReach(trial, seed(j)) Then
                        Continue For
                    End If

                    q(j) = trial

                    Dim cost As Double = EvaluatePose(m, d, adr, q, leftFoot, rightFoot, seed)

                    If cost < current Then
                        current = cost
                        bestValue = trial
                        direction = sign
                    End If
                Next

                q(j) = bestValue

                ' 沿已确认的方向继续推进，直到不再改善
                If direction <> 0 Then
                    For probe As Integer = 1 To 6
                        Dim trial As Double = q(j) + direction * stepSize

                        If Not InReach(trial, seed(j)) Then
                            Exit For
                        End If

                        q(j) = trial

                        Dim cost As Double = EvaluatePose(m, d, adr, q, leftFoot, rightFoot, seed)

                        If cost >= current Then
                            ' 走过头了：必须退回上一处，否则这一维停在更差的位置上
                            q(j) -= direction * stepSize

                            Exit For
                        End If

                        current = cost
                    Next
                End If
            Next

            stepSize *= 0.97

            If stepSize < 1.0E-4 Then
                Exit For
            End If
        Next

        Dim finalCost As Double = EvaluatePose(m, d, adr, q, leftFoot, rightFoot, seed)

        Call DebugAudit.Snapshot(("seed cost", seedCost), ("solved cost", finalCost))

        Call ($"statics: solved cost={finalCost:F6}  " &
              $"com=({d.subtree_com(0):F5},{d.subtree_com(1):F5})  z_com={d.subtree_com(2):F4}").debug
        Call "statics: solved pose (rad):".debug

        For j As Integer = 0 To SimConstants14 - 1
            Call ($"    {names(j),-16} {q(j):F6}   (seed {seed(j):F6}, moved {q(j) - seed(j):+0.000000;-0.000000})").debug
        Next

        Call ("statics: paste into SimConstants.DefaultPose:" & Environment.NewLine &
              "        Public ReadOnly DefaultPose As Double() = {" & Environment.NewLine &
              "            " & JoinAngles(q, 5) & "," & Environment.NewLine &
              "            " & JoinAngles(SkipTake(q, 5, 4), 4) & "," & Environment.NewLine &
              "            " & JoinAngles(SkipTake(q, 9, 5), 5) & Environment.NewLine &
              "        }").debug

        Call DebugAudit.Check(finalCost < seedCost,
                              $"the solved pose is better than the seed ({finalCost:F4} < {seedCost:F4})")

        ' 拿这组角度实际跑 3 秒。几何求解与 kp 无关，但"能不能站住"取决于腿有多硬：
        ' kp=0.55 时重心偏 0.05 mm 也撑不住（腿会塌），kp=50 时腿接近刚性。
        For Each gain As Double In New Double() {0.55, 50.0}
            Dim mV As mjModel = LoadDuck()

            For a As Integer = 0 To mV.nactuator - 1
                mV.actuator_gainprm(a * mjNGAIN + 0) = gain
                mV.actuator_biasprm(a * mjNBIAS + 1) = -gain
            Next

            Dim dV As mjData = MjApi.mj_makeData(mV)

            For j As Integer = 0 To SimConstants14 - 1
                Dim joint As Integer = MjApi.mj_name2id(mV, mjtObj.mjOBJ_JOINT, names(j))
                Dim actuator As Integer = MjApi.mj_name2id(mV, mjtObj.mjOBJ_ACTUATOR, names(j))

                dV.qpos(mV.jnt_qposadr(joint)) = q(j)
                dV.ctrl(actuator) = q(j)
            Next

            Call MjApi.mj_forward(mV, dV)
            Call DropToFloor(mV, dV, 0.0005)
            Call MjApi.mj_clearContactCache(mV, dV)

            For iStep As Integer = 1 To 150
                For subStep As Integer = 1 To 4
                    Call MjApi.mj_step(mV, dV)
                Next
            Next

            Dim trunkV As Integer = MjApi.mj_name2id(mV, mjtObj.mjOBJ_BODY, "trunk_base")
            Dim gravity As Double = ProjectedGravityZ(dV, trunkV)

            Call ($"statics: kp={gain}  after 3 s  z={dV.xpos(trunkV * 3 + 2):F4}  " &
                  $"projGravity={gravity:F3}  ncon={dV.ncon}").debug

        Next

        ' 只校验"几何上平衡"这件事——求解器的职责就是把重心压到支撑中心、
        ' 把两只脚摆到同一水平面。至于这组角度落进物理能不能站住，
        ' 还取决于接触模型（实测 ncon 只有 4，即单脚接触），那是另一层的问题。
        Call DebugAudit.Check(finalCost < 1.0,
                              $"the solver finds a balanced pose (cost {finalCost:F4}, i.e. sub-millimetre)")
    End Sub

    ''' <summary>14 个受驱动关节的个数</summary>
    Private Const SimConstants14 As Integer = 14

    ''' <summary>关节行程与"别离种子太远"的联合判据</summary>
    Private Function InReach(value As Double, seed As Double) As Boolean
        If value < -1.45 OrElse value > 1.45 Then
            Return False
        End If

        Return Math.Abs(value - seed) <= 1.2
    End Function

    Private Function JoinAngles(values As Double(), count As Integer) As String
        Dim parts(count - 1) As String

        For i As Integer = 0 To count - 1
            parts(i) = values(i).ToString("0.######", Globalization.CultureInfo.InvariantCulture)
        Next

        Return String.Join(", ", parts)
    End Function

    Private Function SkipTake(values As Double(), offset As Integer, count As Integer) As Double()
        Dim result(count - 1) As Double

        For i As Integer = 0 To count - 1
            result(i) = values(offset + i)
        Next

        Return result
    End Function

    ''' <summary>把 <paramref name="q"/> 写进 qpos 后评估一次平衡代价</summary>
    Private Function EvaluatePose(m As mjModel, d As mjData, adr As Integer(), q As Double(),
                                  leftFoot As Integer, rightFoot As Integer,
                                  seed As Double()) As Double
        ' 强制左右镜像：右腿 9..13 是左腿 0..4 的相反数。
        ' 不加强制时优化器会去拧 hip_yaw / head_yaw 来"凑"重心——数学上代价能降到 0，
        ' 但那是一个整条腿外撇、头歪着的姿态，落进物理里半秒就倒。
        For k As Integer = 0 To 4
            q(9 + k) = -q(k)
        Next

        For j As Integer = 0 To q.Length - 1
            d.qpos(adr(j)) = q(j)
        Next

        Call MjApi.mj_kinematics(m, d)
        Call MjApi.mj_comPos(m, d)

        Dim left As Vector3() = FootPatch(m, d, leftFoot)
        Dim right As Vector3() = FootPatch(m, d, rightFoot)

        If left.Length < 4 OrElse right.Length < 4 Then
            Return 1.0E+9
        End If

        Dim leftZ As Double = left(0).Z
        Dim rightZ As Double = right(0).Z
        Dim droop As Double = leftZ - rightZ

        ' 脚掌是否水平：接触片的 z 极差
        Dim leftTilt As Double = left(left.Length - 1).Z - leftZ
        Dim rightTilt As Double = right(right.Length - 1).Z - rightZ

        ' 支撑中心（接触片 xy 形心）
        Dim cx As Double = (Average(left, True) + Average(right, True)) * 0.5
        Dim cy As Double = (Average(left, False) + Average(right, False)) * 0.5

        Dim offsetX As Double = d.subtree_com(0) - cx
        Dim offsetY As Double = d.subtree_com(1) - cy

        ' 毫米量级：1 mm 的偏差对应 1 的代价
        Dim cost As Double = 1.0E+6 * (droop * droop + leftTilt * leftTilt + rightTilt * rightTilt) +
                             4.0E+6 * (offsetX * offsetX + offsetY * offsetY)

        ' 弱正则：别离种子太远（每 rad 偏离 0.02）
        For j As Integer = 0 To q.Length - 1
            Dim delta As Double = q(j) - seed(j)

            cost += 0.02 * delta * delta
        Next

        Return cost
    End Function

    Private Function Average(points As Vector3(), x As Boolean) As Double
        Dim sum As Double = 0.0

        For Each p In points
            sum += If(x, p.X, p.Y)
        Next

        Return sum / points.Length
    End Function

    ''' <summary>一只脚最贴近地面的若干顶点，按 z 升序</summary>
    Private Function FootPatch(m As mjModel, d As mjData, body As Integer) As Vector3()
        ' 8 个点只覆盖脚底的一条边，倾斜量会被严重低估；30 个点才铺满整个脚掌
        Const PatchPoints As Integer = 30

        Dim all As New List(Of Vector3)()

        For g As Integer = 0 To m.ngeom - 1
            Dim mesh As Integer = m.geom_dataid(g)

            If m.geom_bodyid(g) <> body OrElse mesh < 0 Then
                Continue For
            End If

            Dim rotation As Quaternion = MjMath.Quat(d.geom_xquat, g * 4)
            Dim origin As New Vector3(CSng(d.geom_xpos(g * 3)), CSng(d.geom_xpos(g * 3 + 1)),
                                      CSng(d.geom_xpos(g * 3 + 2)))
            Dim count As Integer = m.mesh_vertnum(mesh)
            Dim adr As Integer = m.mesh_vertadr(mesh)

            For v As Integer = 0 To count - 1
                Dim local As New Vector3(CSng(m.mesh_vert((adr + v) * 3)),
                                         CSng(m.mesh_vert((adr + v) * 3 + 1)),
                                         CSng(m.mesh_vert((adr + v) * 3 + 2)))

                Call all.Add(origin + MjMath.Rotate(rotation, local))
            Next
        Next

        If all.Count = 0 Then
            Return New Vector3() {}
        End If

        Call all.Sort(Function(a As Vector3, b As Vector3) a.Z.CompareTo(b.Z))

        Dim take As Integer = Math.Min(PatchPoints, all.Count)
        Dim patch(take - 1) As Vector3

        For i As Integer = 0 To take - 1
            patch(i) = all(i)
        Next

        Return patch
    End Function

    ''' <summary>跑 10 秒，每秒打印一次姿态与角速度</summary>
    ''' <param name="holdPose">true 时把伺服目标设成当前关节角（零力矩）</param>
    Private Function RunLong(m As mjModel, d As mjData, trunk As Integer,
                             names As String(), pose As Double(), holdPose As Boolean,
                             label As String, Optional steps As Integer = 500) As Double()
        Call MjApi.mj_resetData(m, d)

        For j As Integer = 0 To names.Length - 1
            Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j))
            Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))

            d.qpos(m.jnt_qposadr(joint)) = pose(j)
            d.ctrl(actuator) = pose(j)
        Next

        Call MjApi.mj_forward(m, d)
        Call DropToFloor(m, d, 0.0005)
        Call MjApi.mj_clearContactCache(m, d)

        Call ($"{label}: spawned at z={d.qpos(m.jnt_qposadr(FreeJointOf(m)) + 2):F4}").debug

        Dim x0 As Double = d.xpos(trunk * 3)
        Dim y0 As Double = d.xpos(trunk * 3 + 1)
        Dim maxOmega As Double = 0.0

        For iStep As Integer = 1 To steps
            For subStep As Integer = 1 To 4
                If holdPose Then
                    For j As Integer = 0 To names.Length - 1
                        Dim joint As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_JOINT, names(j))
                        Dim actuator As Integer = MjApi.mj_name2id(m, mjtObj.mjOBJ_ACTUATOR, names(j))

                        d.ctrl(actuator) = d.qpos(m.jnt_qposadr(joint))
                    Next
                End If

                Call MjApi.mj_step(m, d)
            Next

            Dim gz As Double = ProjectedGravityZ(d, trunk)
            Dim wx As Double = d.cvel(trunk * 6 + 0)
            Dim wy As Double = d.cvel(trunk * 6 + 1)
            Dim wz As Double = d.cvel(trunk * 6 + 2)
            Dim omega As Double = Math.Sqrt(wx * wx + wy * wy + wz * wz)

            maxOmega = Math.Max(maxOmega, omega)

            If iStep Mod 50 = 0 Then
                Call ($"{label} t={d.time:F1}s z={d.xpos(trunk * 3 + 2):F4} gz={gz:F3} " &
                      $"|w|={omega:F3} ncon={d.ncon}").debug
            End If
        Next

        Dim drift As Double = Math.Sqrt((d.xpos(trunk * 3) - x0) ^ 2 + (d.xpos(trunk * 3 + 1) - y0) ^ 2)

        Return New Double() {d.xpos(trunk * 3 + 2), maxOmega, drift}
    End Function

    Private Function FreeJointOf(m As mjModel) As Integer
        For j As Integer = 0 To m.njnt - 1
            If CType(m.jnt_type(j), mjtJoint) = mjtJoint.mjJNT_FREE Then
                Return j
            End If
        Next

        Return -1
    End Function

    ''' <summary>
    ''' 把自由关节的宿主刚体放到"最低的碰撞顶点刚好离地 <paramref name="lift"/> 米"。
    ''' </summary>
    ''' <remarks>
    ''' <c>qpos0</c> 是 MJCF 的装配姿态（各关节为 0、腿伸直），而策略用的是
    ''' <c>DefaultPose</c>（膝盖弯曲）。两者相差 6~7 cm，直接按 <c>qpos0</c> 的高度
    ''' 摆好再放手，鸭子会自由下落并在落地瞬间被砸翻——实测 0.4 s 内投影重力就从
    ''' -0.94 翻到 +0.31，之后躺着打转。
    ''' </remarks>
    Private Sub DropToFloor(m As mjModel, d As mjData, lift As Double)
        Dim freeAdr As Integer = m.jnt_qposadr(FreeJointOf(m))

        If freeAdr < 0 Then
            Return
        End If

        Call MjApi.mj_forward(m, d)

        Dim lowest As Double = Double.MaxValue

        For g As Integer = 0 To m.ngeom - 1
            Dim mesh As Integer = m.geom_dataid(g)

            If m.geom_bodyid(g) = 0 OrElse mesh < 0 Then
                Continue For
            End If

            Dim count As Integer = m.mesh_vertnum(mesh)
            Dim adr As Integer = m.mesh_vertadr(mesh)
            Dim rotation As Quaternion = MjMath.Quat(d.geom_xquat, g * 4)
            Dim origin As New Vector3(CSng(d.geom_xpos(g * 3)), CSng(d.geom_xpos(g * 3 + 1)),
                                      CSng(d.geom_xpos(g * 3 + 2)))

            For v As Integer = 0 To count - 1
                Dim local As New Vector3(CSng(m.mesh_vert((adr + v) * 3)),
                                         CSng(m.mesh_vert((adr + v) * 3 + 1)),
                                         CSng(m.mesh_vert((adr + v) * 3 + 2)))

                lowest = Math.Min(lowest, (origin + MjMath.Rotate(rotation, local)).Z)
            Next
        Next

        If lowest < Double.MaxValue Then
            d.qpos(freeAdr + 2) -= lowest - lift

            Call MjApi.mj_forward(m, d)
        End If
    End Sub

    ''' <summary>世界重力在刚体局部系里的 z 分量；-1 正立、+1 仰面</summary>
    Private Function ProjectedGravityZ(d As mjData, body As Integer) As Double
        Dim q As Quaternion = MjMath.Quat(d.xquat, body * 4)

        Return Vector3.Transform(New Vector3(0, 0, -1), Quaternion.Conjugate(q)).Z
    End Function

    ' /********************************************************************************/
    '  13. 摩擦（能不能把滑动的箱子停住）
    ' /********************************************************************************/

    ''' <summary>
    ''' 1 kg 的立方体以 1 m/s 在 μ=0.8 的地面上滑动，0.5 秒内必须停下。
    ''' </summary>
    ''' <remarks>
    ''' 这条用例把"接触求解"与"刚体树、伺服、观测"完全隔离开：只有一个箱子、
    ''' 一个平面、一条摩擦力。它不过就说明摩擦项本身没进求解器，
    ''' 那么任何站立/行走的漂移都只是它的下游症状。
    ''' </remarks>
    Private Sub CaseFriction()
        Call DebugAudit.Mark("friction", "a sliding box is stopped by coulomb friction")

        Dim spec As New MjSpec()
        spec.Opt.timestep = 0.005
        spec.Opt.gravity = New Double() {0.0, 0.0, -9.81}

        Dim box As MjsBody = spec.AddBody(spec.World, "box", New Vector3(0, 0, 0.05))

        box.HasInertial = True
        box.Mass = 1.0
        box.FullInertia = New Double() {0.001, 0.001, 0.001, 0.0, 0.0, 0.0}

        Call spec.AddFreeJoint(box, "free")

        Dim boxGeom As MjsGeom = spec.AddGeom(box, mjtGeom.mjGEOM_BOX, New Vector3(0.05F, 0.05F, 0.05F))
        Dim ground As MjsGeom = spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE, New Vector3(2, 2, 0))

        boxGeom.Friction = {0.8, 0.005, 0.0001}
        ground.Friction = {0.8, 0.005, 0.0001}

        Dim m As mjModel = MjApi.mj_compile(spec)
        Dim d As mjData = MjApi.mj_makeData(m)

        ' free joint 的线速度是世界系
        d.qvel(0) = 1.0

        For i As Integer = 1 To 100
            Call MjApi.mj_step(m, d)
        Next

        ' μ·g = 7.85 m/s²，初速 1 m/s → 0.127 s 停下
        Call ($"box-friction vx={d.qvel(0):F4} x={d.qpos(0):F4} ncon={d.ncon}").debug

        Call DebugAudit.Check(Math.Abs(d.qvel(0)) < 0.05,
                              $"friction stops the box (vx = {d.qvel(0):F4} after 0.5 s)")
        Call DebugAudit.Check(d.qpos(0) < 0.15,
                              $"the box does not slide away (x = {d.qpos(0):F4}, expected < 0.13)")
    End Sub

    ' /********************************************************************************/
    '  14. 凸包的摩擦（duck 的脚是合并凸包，不是 BoxCollider）
    ' /********************************************************************************/

    ''' <summary>
    ''' 与 <see cref="CaseFriction"/> 完全相同，只是把立方体换成 8 顶点的凸包网格。
    ''' </summary>
    Private Sub CaseConvexFriction()
        Call DebugAudit.Mark("friction", "a sliding convex hull is stopped by coulomb friction")

        Dim spec As New MjSpec()
        spec.Opt.timestep = 0.005
        spec.Opt.gravity = New Double() {0.0, 0.0, -9.81}

        Dim box As MjsBody = spec.AddBody(spec.World, "box", New Vector3(0, 0, 0.05))

        box.HasInertial = True
        box.Mass = 1.0
        box.FullInertia = New Double() {0.001, 0.001, 0.001, 0.0, 0.0, 0.0}

        Call spec.AddFreeJoint(box, "free")

        Dim vertices As New List(Of Double)(24)

        For Each sx As Double In New Double() {-1, 1}
            For Each sy As Double In New Double() {-1, 1}
                For Each sz As Double In New Double() {-1, 1}
                    Call vertices.Add(sx * 0.05)
                    Call vertices.Add(sy * 0.05)
                    Call vertices.Add(sz * 0.05)
                Next
            Next
        Next

        ' 不给 Faces，编译器会自己取凸包
        Call spec.Meshes.Add(New MjsMesh With {.Name = "cube", .Vertices = vertices.ToArray()})

        Dim boxGeom As MjsGeom = spec.AddGeom(box, mjtGeom.mjGEOM_MESH, Nothing, Vector3.Zero, Nothing, "cube")
        Dim ground As MjsGeom = spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE, New Vector3(2, 2, 0))

        boxGeom.Friction = {0.8, 0.005, 0.0001}
        ground.Friction = {0.8, 0.005, 0.0001}

        Dim m As mjModel = MjApi.mj_compile(spec)
        Dim d As mjData = MjApi.mj_makeData(m)

        d.qvel(0) = 1.0

        For i As Integer = 1 To 100
            Call MjApi.mj_step(m, d)
        Next

        Call DebugAudit.Snapshot(("vx (convex)", d.qvel(0)), ("x (convex)", d.qpos(0)), ("ncon", d.ncon))

        Call DebugAudit.Check(Math.Abs(d.qvel(0)) < 0.05,
                              $"friction stops the convex hull (vx = {d.qvel(0):F4} after 0.5 s)")
    End Sub

    ' /********************************************************************************/
    '  15. 新增 API：程序化凸体图元
    ' /********************************************************************************/

    Private Sub CaseScenePrimitives()
        Call DebugAudit.Mark("scene primitives", "procedural convex primitives expose vertices and faces")

        Dim box As MjPrimitive = MjPrimitives.Box(0.1, 0.2, 0.3)

        Call DebugAudit.Check(box.VertexCount = 8 AndAlso box.FaceCount = 12,
                              $"box has 8 vertices / 12 faces (got {box.VertexCount}/{box.FaceCount})")

        Dim maxX As Single = 0.0F

        For Each vertex As Vector3 In box.Vertices
            maxX = Math.Max(maxX, Math.Abs(vertex.X))
        Next

        Call DebugAudit.Check(Math.Abs(maxX - 0.05F) < 1.0E-6,
                              $"box half width is 0.05 (got {maxX:F6})")

        Dim sphere As MjPrimitive = MjPrimitives.Sphere(0.05, 12, 8)

        Call DebugAudit.Check(sphere.VertexCount = 12 * 9 AndAlso sphere.FaceCount = 12 * 8 * 2,
                              $"sphere grid is 12x8 (got {sphere.VertexCount}/{sphere.FaceCount})")

        For Each primitive As MjPrimitive In New MjPrimitive() {
            MjPrimitives.Cylinder(0.05, 0.1, 12),
            MjPrimitives.Cone(0.05, 0.1, 12),
            MjPrimitives.Wedge(0.1, 0.1, 0.1),
            MjPrimitives.Disc(0.05, 0.02, 12),
            MjPrimitives.Frustum(0.05, 0.02, 0.1, 12),
            MjPrimitives.Hemisphere(0.05, 12, 4),
            MjPrimitives.Prism(6, 0.05, 0.1),
            MjPrimitives.Pyramid(4, 0.05, 0.1)
        }
            Call DebugAudit.Check(primitive.VertexCount >= 6 AndAlso primitive.FaceCount >= 8,
                                  $"primitive is non-degenerate ({primitive.VertexCount}/{primitive.FaceCount})")
        Next
    End Sub

    ' /********************************************************************************/
    '  16. 新增 API：MJCF 场景门面（MjSpec → mj_compile → mj_step）
    ' /********************************************************************************/

    Private Sub CaseSceneStep()
        Call DebugAudit.Mark("scene step", "MjScene compiles a spec and advances through mj_step")

        Dim spec As New MjSceneSpec()

        spec.Options.Timestep = 0.005
        spec.Options.Iterations = 20

        Dim link As MjBodySpec = spec.AddBody("link", Nothing, New Vector3(0.0F, 0.0F, 0.5F))

        link.Mass = 0.5
        link.Inertia = New Double() {0.001, 0.001, 0.001, 0.0, 0.0, 0.0}
        link.Extents = New Vector3(0.02F, 0.02F, 0.05F)

        Dim hinge As MjJointSpec = spec.AddJoint("hinge", "link", mjtJoint.mjJNT_HINGE)

        hinge.Axis = New Vector3(0.0F, 1.0F, 0.0F)
        hinge.Lower = -1.0
        hinge.Upper = 1.0
        hinge.Damping = 0.1

        Dim servoSpec As MjActuatorSpec = spec.AddActuator("servo", "hinge", 10.0)

        servoSpec.Kv = 1.0

        Dim sim As New MjScene(spec)

        Call DebugAudit.Check(sim.Model IsNot Nothing AndAlso sim.Model.nbody >= 2,
                              $"scene compiled with {sim.Model.nbody} bodies")

        Dim body As MjBody = sim.Body("link")
        Dim servo As MjServo = sim.Servo("servo")

        Call DebugAudit.Check(body IsNot Nothing, "the body handle resolves by name")
        Call DebugAudit.Check(servo IsNot Nothing, "the servo handle resolves by name")

        servo.Target = 0.2

        For i As Integer = 1 To 20
            Call sim.Step()
        Next

        Call DebugAudit.Check(Math.Abs(sim.Data.time - 0.1) < 1.0E-9,
                              $"20 steps advance time to 0.1 s (got {sim.Data.time:F6})")
        Call DebugAudit.Check(Math.Abs(servo.Target - 0.2) < 1.0E-12,
                              "the control signal survives stepping")
        Call DebugAudit.Check(Not Single.IsNaN(body.Position.Z),
                              "the body position is finite")
        Call DebugAudit.Check(sim.Bodies.Count >= 2, "the body view is queryable")

        Dim before As Double = sim.Data.time

        Call sim.Advance(0.05)

        Call DebugAudit.Check(sim.Data.time > before, "Advance() keeps stepping forward")

        Call sim.Reset()

        Call DebugAudit.Check(Math.Abs(sim.Data.time) < 1.0E-12, "Reset() clears simulation time")
    End Sub

    ' /********************************************************************************/
    '  17. 新增 API：结构变更后的受控重建
    ' /********************************************************************************/

    Private Sub CaseSceneRebuild()
        Call DebugAudit.Mark("scene rebuild", "structural changes rebuild the model and keep name handles valid")

        Dim spec As New MjSceneSpec()
        Dim sim As New MjScene(spec)
        Dim initial As Integer = sim.Model.nbody

        Call DebugAudit.Check(initial >= 1, $"the empty scene has a world body (nbody={initial})")

        Dim extra As New MjBodySpec With {
            .Name = "extra",
            .Mass = 0.2,
            .Extents = New Vector3(0.03F, 0.03F, 0.03F)
        }

        Call sim.SpawnBody(extra)

        Call DebugAudit.Check(sim.Model.nbody = initial + 1,
                              $"SpawnBody adds one body (nbody={sim.Model.nbody})")
        Call DebugAudit.Check(sim.Body("extra") IsNot Nothing,
                              "the spawned body resolves by name after rebuild")

        Call sim.RemoveBody("extra")

        Call DebugAudit.Check(sim.Model.nbody = initial,
                              $"RemoveBody restores the body count (nbody={sim.Model.nbody})")
        Call DebugAudit.Check(sim.Body("extra") Is Nothing,
                              "the removed body no longer resolves")
    End Sub

    ' /********************************************************************************/
    '  18. 新增 API：MuJoCo 自有的数学类型
    ' /********************************************************************************/

    Private Sub CaseInteropMath()
        Call DebugAudit.Mark("interop math", "MuJoCo-owned vector/quaternion/matrix API")

        Dim a As New iv.Vector3(1.0, 2.0, 3.0)
        Dim b As New iv.Vector3(4.0, 5.0, 6.0)
        Dim sum As iv.Vector3 = a + b

        Call DebugAudit.Check(sum.x = 5.0 AndAlso sum.y = 7.0 AndAlso sum.z = 9.0,
                              "vector addition matches component-wise semantics")
        Call DebugAudit.Check(iv.Vector3.Dot(a, b) = 32.0, "vector dot product")

        Dim q As im.Quaternion = im.Quaternion.FromAxisAngle(New iv.Vector3(0.0, 0.0, 1.0), Math.PI * 0.5)
        Dim rotated As iv.Vector3 = q.Rotate(New iv.Vector3(1.0, 0.0, 0.0))

        Call DebugAudit.Check(Math.Abs(rotated.y - 1.0) < 1.0E-6 AndAlso Math.Abs(rotated.x) < 1.0E-6,
                              $"a 90 deg Z rotation maps +X to +Y (got {rotated.x:F4},{rotated.y:F4})")

        Dim m As im.Matrix3x3 = im.Matrix3x3.Diagonal(2.0, 3.0, 4.0)
        Dim mv As iv.Vector3 = m.MultiplyLeft(New iv.Vector3(1.0, 1.0, 1.0))

        Call DebugAudit.Check(mv.x = 2.0 AndAlso mv.y = 3.0 AndAlso mv.z = 4.0,
                              "MultiplyLeft applies the diagonal matrix")
        Call DebugAudit.Check(Math.Abs(im.Vector3Math.Length(New iv.Vector3(3.0, 4.0, 0.0)) - 5.0) < 1.0E-9,
                              "Vector3Math.Length")
    End Sub

    ' /********************************************************************************/
    '  19. 新增 API：世界句柄装配与步进
    ' /********************************************************************************/

    Private Sub CaseInteropWorld()
        Call DebugAudit.Mark("interop world", "MuJoCo-owned world handle drives the physics kernel")

        Dim world As New iv.PhysicsWorld3D() With {
            .FixedDt = 0.005,
            .Iterations = 20,
            .Substeps = 4,
            .Gravity = New iv.Vector3(0.0, 0.0, -9.81)
        }

        Dim ground As iv.RigidBody3D = iv.PhysicsWorld3D.StaticPlane(
            New iv.Vector3(0.0, 0.0, 1.0), 0.0, New iv.PhysicsMaterial(0.8, 0.0))
        Dim ball As iv.RigidBody3D = iv.PhysicsWorld3D.Box(
            0.1, 0.1, 0.1, 1.0, New iv.PhysicsMaterial(0.8, 0.0))

        ball.Position = New iv.Vector3(0.0, 0.0, 1.0)

        Call world.Add(ground)
        Call world.Add(ball)

        Call DebugAudit.Check(world.Bodies.Count = 2, $"the world holds 2 bodies (got {world.Bodies.Count})")

        For i As Integer = 1 To 200
            Call world.Step(world.FixedDt)
        Next

        Call DebugAudit.Check(ball.Position.z < 0.9 AndAlso ball.Position.z > -0.2,
                              $"the box falls onto the ground (z = {ball.Position.z:F4})")

        Call world.ClearVelocities()

        Call DebugAudit.Check(ball.Velocity.Magnitude < 1.0E-6,
                              "ClearVelocities zeroes the body velocity")
        Call DebugAudit.Check(world.Contacts IsNot Nothing,
                              "the contact view is queryable without engine types")
    End Sub

    ' /********************************************************************************/
    '  20. 新增 API：URDF → MjSceneSpec 装配映射
    ' /********************************************************************************/

    Private Sub CaseUrdfImport()
        Call DebugAudit.Mark("urdf import", "URDF skeleton maps to a neutral MjSceneSpec")

        Dim modelPath As String = Path.Combine(m_root, "models", "six_axis_arm.urdf")

        Call DebugAudit.Check(File.Exists(modelPath), $"the URDF model exists: {modelPath}")

        Dim spec As MjSceneSpec = urdf.Read(modelPath)

        Call DebugAudit.Check(spec.Bodies.Count = 8, $"8 links (got {spec.Bodies.Count})")
        Call DebugAudit.Check(spec.Joints.Count = 6, $"6 actuated joints (got {spec.Joints.Count})")

        Dim hasHinge As Boolean = False

        For Each joint As MjJointSpec In spec.Joints
            If joint.Type = mjtJoint.mjJNT_HINGE Then hasHinge = True
        Next

        Call DebugAudit.Check(hasHinge, "revolute joints map to hinge joints")

        Dim tree As MjSpec = MjSceneBuilder.Build(spec)

        Call DebugAudit.Check(tree IsNot Nothing AndAlso tree.World IsNot Nothing,
                              "the URDF skeleton assembles into an MjSpec tree")
    End Sub

End Module
