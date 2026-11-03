''' <summary>
''' 一个接触点，对应 <c>mjdata.h</c> 的 <c>mjContact</c>。
''' </summary>
''' <remarks>
''' <see cref="frame"/> 的前三个数是接触法向（从 <see cref="geom1"/> 指向
''' <see cref="geom2"/>）。托管层没有"每 geom 一次窄相位"，所以
''' <see cref="geom1"/> / <see cref="geom2"/> 记录的是两个刚体各自的**代表 geom**：
''' 一个 body 上的多个可碰撞 geom 会合并成一个凸包代理参与接触，
''' 这与原生 MuJoCo 的 <c>ncon</c> 不是同一个量，不能逐点对照。
''' </remarks>
Public Class mjContact

    ''' <summary>有符号距离（负值表示相互穿透）</summary>
    Public Property dist As Double
    ''' <summary>接触点的世界坐标</summary>
    Public Property pos As Double() = {0, 0, 0}
    ''' <summary>接触坐标系，前三个数是法向（行主序 3×3）</summary>
    Public Property frame As Double() = {0, 0, 1, 1, 0, 0, 0, 1, 0}
    ''' <summary>包含余量</summary>
    Public Property includemargin As Double
    ''' <summary>摩擦系数：滑动、扭转、滚动、螺旋、滚动2</summary>
    Public Property friction As Double() = {1, 0.005, 0.0001, 0, 0}
    ''' <summary>接触的 solref</summary>
    Public Property solref As Double() = {0.02, 1.0}
    ''' <summary>接触的 solimp</summary>
    Public Property solimp As Double() = {0.9, 0.95, 0.001, 0.5, 2.0}
    ''' <summary>接触维度</summary>
    Public Property [Dim] As Integer = 3
    ''' <summary>第一个几何体的 id（托管层里是代表 geom）</summary>
    Public Property geom1 As Integer = -1
    ''' <summary>第二个几何体的 id</summary>
    Public Property geom2 As Integer = -1
    ''' <summary>该接触在约束行里的地址，-1 表示没有进入约束</summary>
    Public Property efc_address As Integer = -1

End Class

''' <summary>警告计数器，对应 <c>mjdata.h</c> 的 <c>mjWarningStat</c></summary>
Public Class mjWarningStat
    ''' <summary>上一次触发的仿真步编号</summary>
    Public Property lastinfo As Integer = 0
    ''' <summary>累计触发次数</summary>
    Public Property number As Integer = 0

    Public Sub Reset()
        lastinfo = 0
        number = 0
    End Sub
End Class

''' <summary>计时器，对应 <c>mjdata.h</c> 的 <c>mjTimerStat</c></summary>
Public Class mjTimerStat
    ''' <summary>该计时器对应的仿真步编号</summary>
    Public Property number As Integer = 0
    ''' <summary>持续时长，秒</summary>
    Public Property duration As Double = 0.0
    ''' <summary>累计时长，秒</summary>
    Public Property total As Double = 0.0

    Public Sub Reset()
        number = 0
        duration = 0.0
        total = 0.0
    End Sub
End Class

''' <summary>求解器统计，对应 <c>mjdata.h</c> 的 <c>mjSolverStat</c></summary>
Public Class mjSolverStat
    Public Property improvement As Double = 0.0
    Public Property gradient As Double = 0.0
    Public Property lineslope As Double = 0.0
    Public Property nactive As Integer = 0
    Public Property nchange As Integer = 0
    Public Property neval As Integer = 0
    Public Property nupdate As Integer = 0
End Class

''' <summary>
''' 仿真状态与全部派生量，对应 <c>mjdata.h</c> 的 <c>mjData</c>。
''' </summary>
''' <remarks>
''' <para>
''' 所有数组在 <see cref="Allocate"/> 里一次性分配，<c>mj_step</c> 的推进路径上
''' 不再分配内存。<see cref="Engine"/> 是通往计算内核的桥，
''' 对使用 <c>mj_*</c> API 的调用方不可见。
''' </para>
''' <para>
''' 与 C 版一致的约定：<see cref="cvel"/>、<see cref="cdof"/>、
''' <see cref="cinert"/>、<see cref="crb"/>、<see cref="cacc"/>、
''' <see cref="cfrc_ext"/>、<see cref="cfrc_int"/> 的 6 元组都是
''' <b>角分量在前、线分量在后</b>；<see cref="xmat"/> 是行主序 3×3，
''' 满足 <c>global = xmat · local</c>。
''' </para>
''' </remarks>
Public Class mjData

    ''' <summary>接触点的上限（超过时置 <c>mjWARN_CONTACTFULL</c>）</summary>
    Public Const MaxContacts As Integer = 512

    ' /********************************************************************************/
    '  全局
    ' /********************************************************************************/

    ''' <summary>仿真时间，秒</summary>
    Public Property time As Double
    ''' <summary>动能与势能</summary>
    Public Property energy As Double() = {0.0, 0.0}

    ' /********************************************************************************/
    '  状态
    ' /********************************************************************************/

    ''' <summary>广义坐标（nq）</summary>
    Public Property qpos As Double()
    ''' <summary>广义速度（nv）</summary>
    Public Property qvel As Double()
    ''' <summary>广义加速度（nv）</summary>
    Public Property qacc As Double()
    ''' <summary>执行器的内部状态（na）</summary>
    Public Property act As Double()
    ''' <summary>控制量（nu）</summary>
    Public Property ctrl As Double()
    ''' <summary>施加在自由度上的外力（nv）</summary>
    Public Property qfrc_applied As Double()
    ''' <summary>施加在刚体上的外力（nbody × 6，角分量在前）</summary>
    Public Property xfrc_applied As Double()
    ''' <summary>等式约束的开关（neq）</summary>
    Public Property eq_active As Boolean()

    ' /********************************************************************************/
    '  位置相关
    ' /********************************************************************************/

    ''' <summary>刚体帧原点的世界坐标（nbody × 3）</summary>
    Public Property xpos As Double()
    ''' <summary>刚体帧的世界姿态四元数（nbody × 4，w x y z）</summary>
    Public Property xquat As Double()
    ''' <summary>刚体帧的世界旋转矩阵（nbody × 9，行主序）</summary>
    Public Property xmat As Double()
    ''' <summary>刚体质心的世界坐标（nbody × 3）</summary>
    Public Property xipos As Double()
    ''' <summary>刚体惯性坐标系的世界姿态（nbody × 4）</summary>
    Public Property xiquat As Double()
    ''' <summary>刚体惯性坐标系的世界旋转矩阵（nbody × 9）</summary>
    Public Property ximat As Double()
    ''' <summary>关节锚点的世界坐标（njnt × 3）</summary>
    Public Property xanchor As Double()
    ''' <summary>关节轴的世界方向（njnt × 3）</summary>
    Public Property xaxis As Double()
    ''' <summary>几何体帧的世界坐标（ngeom × 3）</summary>
    Public Property geom_xpos As Double()
    ''' <summary>几何体帧的世界姿态（ngeom × 4）</summary>
    Public Property geom_xquat As Double()
    ''' <summary>几何体帧的世界旋转矩阵（ngeom × 9）</summary>
    Public Property geom_xmat As Double()
    ''' <summary>站点的世界坐标（nsite × 3）</summary>
    Public Property site_xpos As Double()
    ''' <summary>站点的世界姿态（nsite × 4）</summary>
    Public Property site_xquat As Double()
    ''' <summary>站点的世界旋转矩阵（nsite × 9）</summary>
    Public Property site_xmat As Double()
    ''' <summary>相机的世界坐标（ncam × 3）</summary>
    Public Property cam_xpos As Double()
    ''' <summary>相机的世界旋转矩阵（ncam × 9）</summary>
    Public Property cam_xmat As Double()

    ' /********************************************************************************/
    '  惯量与质量矩阵
    ' /********************************************************************************/

    ''' <summary>子树质心（nbody × 3）</summary>
    Public Property subtree_com As Double()
    ''' <summary>自由度的运动轴（nv × 6，角分量在前）</summary>
    Public Property cdof As Double()
    ''' <summary>复合惯量（nv × 10）</summary>
    Public Property cinert As Double()
    ''' <summary>复合刚体惯量（nbody × 10）</summary>
    Public Property crb As Double()
    ''' <summary>质量矩阵（nv × nv，行主序）</summary>
    Public Property M As Double()
    ''' <summary>质量矩阵的 Cholesky 分解（nv × nv）</summary>
    Public Property qLD As Double()
    ''' <summary>Cholesky 对角元的逆（nv）</summary>
    Public Property qLDiagInv As Double()

    ' /********************************************************************************/
    '  速度相关
    ' /********************************************************************************/

    ''' <summary>刚体质心速度（nbody × 6，角分量在前）</summary>
    Public Property cvel As Double()
    ''' <summary>偏置力 <c>qfrc_bias = C(q,q̇)</c>（nv）</summary>
    Public Property qfrc_bias As Double()
    ''' <summary>弹簧力（nv）</summary>
    Public Property qfrc_spring As Double()
    ''' <summary>阻尼力（nv）</summary>
    Public Property qfrc_damper As Double()
    ''' <summary>被动力之和（nv）</summary>
    Public Property qfrc_passive As Double()

    ' /********************************************************************************/
    '  控制与加速度相关
    ' /********************************************************************************/

    ''' <summary>执行器长度（nu）</summary>
    Public Property actuator_length As Double()
    ''' <summary>执行器速度（nu）</summary>
    Public Property actuator_velocity As Double()
    ''' <summary>执行器的力矩（nu × nv）</summary>
    Public Property actuator_moment As Double()
    ''' <summary>执行器产生的力（nu）</summary>
    Public Property actuator_force As Double()
    ''' <summary>执行器在广义坐标上的合力（nv）</summary>
    Public Property qfrc_actuator As Double()
    ''' <summary>平滑力（nv）：被动力 + 执行器力</summary>
    Public Property qfrc_smooth As Double()
    ''' <summary>无约束加速度（nv）</summary>
    Public Property qacc_smooth As Double()
    ''' <summary>约束力（nv）</summary>
    Public Property qfrc_constraint As Double()
    ''' <summary>逆动力学的结果（nv）</summary>
    Public Property qfrc_inverse As Double()
    ''' <summary>刚体质心的加速度（nbody × 6，角分量在前）</summary>
    Public Property cacc As Double()
    ''' <summary>外部力（nbody × 6，角分量在前）</summary>
    Public Property cfrc_ext As Double()
    ''' <summary>内部力（nbody × 6，角分量在前）</summary>
    Public Property cfrc_int As Double()

    ' /********************************************************************************/
    '  腱
    ' /********************************************************************************/

    ''' <summary>腱的长度（ntendon）</summary>
    Public Property ten_length As Double()
    ''' <summary>腱的力矩矩阵（ntendon × nv）</summary>
    Public Property ten_J As Double()

    ' /********************************************************************************/
    '  传感器（nsensordata）
    ' /********************************************************************************/

    Public Property sensordata As Double()

    ' /********************************************************************************/
    '  接触与约束
    ' /********************************************************************************/

    ''' <summary>检出的接触个数</summary>
    Public Property ncon As Integer
    ''' <summary>接触列表（容量 <see cref="MaxContacts"/>）</summary>
    Public Property contact As mjContact()
    ''' <summary>约束行个数</summary>
    Public Property nefc As Integer
    ''' <summary>约束行的类型（nefc）</summary>
    Public Property efc_type As Integer()
    ''' <summary>约束行的编号（nefc）</summary>
    Public Property efc_id As Integer()
    ''' <summary>约束行的位置误差（nefc）</summary>
    Public Property efc_pos As Double()
    ''' <summary>约束行的力（nefc）</summary>
    Public Property efc_force As Double()
    ''' <summary>约束力映射回广义坐标的结果（nv）</summary>
    Public Property efc_qfrc As Double()

    ' /********************************************************************************/
    '  诊断
    ' /********************************************************************************/

    ''' <summary>求解器迭代次数</summary>
    Public Property solver_iterations As Integer
    ''' <summary>求解器残差（上一轮迭代里速度的最大变化）</summary>
    Public Property solver_residual As Double
    Public Property warning As mjWarningStat()
    Public Property timer As mjTimerStat()

    ' /********************************************************************************/
    '  内核桥（对调用方不可见）
    ' /********************************************************************************/

    ''' <summary>通往计算内核的桥，由 <c>mj_makeData</c> 建立</summary>
    Friend Property Engine As MjEngineBridge

    ''' <summary>模型的引用（用于检查尺寸）</summary>
    Friend Property Model As mjModel

    ' /********************************************************************************/
    '  分配
    ' /********************************************************************************/

    ''' <summary>按模型尺寸分配全部数组</summary>
    Public Sub Allocate(model As mjModel)
        Me.Model = model

        Dim m As mjModel = model

        qpos = Alloc(m.nq)
        qvel = Alloc(m.nv)
        qacc = Alloc(m.nv)
        act = Alloc(m.na)
        ctrl = Alloc(m.nu)
        qfrc_applied = Alloc(m.nv)
        xfrc_applied = Alloc(m.nbody * 6)
        eq_active = New Boolean(Math.Max(m.neq, 1) - 1) {}

        xpos = Alloc(m.nbody * 3)
        xquat = Alloc(m.nbody * 4)
        xmat = Alloc(m.nbody * 9)
        xipos = Alloc(m.nbody * 3)
        xiquat = Alloc(m.nbody * 4)
        ximat = Alloc(m.nbody * 9)
        xanchor = Alloc(m.njnt * 3)
        xaxis = Alloc(m.njnt * 3)
        geom_xpos = Alloc(m.ngeom * 3)
        geom_xquat = Alloc(m.ngeom * 4)
        geom_xmat = Alloc(m.ngeom * 9)
        site_xpos = Alloc(m.nsite * 3)
        site_xquat = Alloc(m.nsite * 4)
        site_xmat = Alloc(m.nsite * 9)
        cam_xpos = Alloc(m.ncam * 3)
        cam_xmat = Alloc(m.ncam * 9)

        subtree_com = Alloc(m.nbody * 3)
        cdof = Alloc(m.nv * 6)
        cinert = Alloc(m.nv * 10)
        crb = Alloc(m.nbody * 10)
        Me.M = Alloc(m.nv * m.nv)
        qLD = Alloc(m.nv * m.nv)
        qLDiagInv = Alloc(m.nv)

        cvel = Alloc(m.nbody * 6)
        qfrc_bias = Alloc(m.nv)
        qfrc_spring = Alloc(m.nv)
        qfrc_damper = Alloc(m.nv)
        qfrc_passive = Alloc(m.nv)

        actuator_length = Alloc(m.nu)
        actuator_velocity = Alloc(m.nu)
        actuator_moment = Alloc(m.nu * m.nv)
        actuator_force = Alloc(m.nu)
        qfrc_actuator = Alloc(m.nv)
        qfrc_smooth = Alloc(m.nv)
        qacc_smooth = Alloc(m.nv)
        qfrc_constraint = Alloc(m.nv)
        qfrc_inverse = Alloc(m.nv)
        cacc = Alloc(m.nbody * 6)
        cfrc_ext = Alloc(m.nbody * 6)
        cfrc_int = Alloc(m.nbody * 6)

        ten_length = Alloc(m.ntendon)
        ten_J = Alloc(m.ntendon * m.nv)

        sensordata = Alloc(m.nsensordata)

        contact = New mjContact(MaxContacts - 1) {}
        efc_type = New Integer(Math.Max(m.nv + MaxContacts, 1) - 1) {}
        efc_id = New Integer(Math.Max(m.nv + MaxContacts, 1) - 1) {}
        efc_pos = Alloc(Math.Max(m.nv + MaxContacts, 1))
        efc_force = Alloc(Math.Max(m.nv + MaxContacts, 1))
        efc_qfrc = Alloc(m.nv)

        warning = New mjWarningStat(CInt(mjtWarning.mjNWARNING) - 1) {}
        timer = New mjTimerStat(CInt(mjtTimer.mjNTIMER) - 1) {}

        For i As Integer = 0 To warning.Length - 1
            warning(i) = New mjWarningStat()
        Next

        For i As Integer = 0 To timer.Length - 1
            timer(i) = New mjTimerStat()
        Next

        For i As Integer = 0 To contact.Length - 1
            contact(i) = New mjContact()
        Next

        ' 单位四元数
        For i As Integer = 0 To m.nbody - 1
            xquat(i * 4) = 1.0
            xiquat(i * 4) = 1.0
        Next

        For i As Integer = 0 To m.ngeom - 1
            geom_xquat(i * 4) = 1.0
        Next

        For i As Integer = 0 To m.nsite - 1
            site_xquat(i * 4) = 1.0
        Next
    End Sub

    ''' <summary>把状态与派生量清零（保留 <see cref="time"/> 与 <see cref="Model"/>）</summary>
    Public Sub ClearState()
        Clear(qpos) : Clear(qvel) : Clear(qacc) : Clear(act)
        Clear(ctrl) : Clear(qfrc_applied) : Clear(xfrc_applied)
        Clear(qfrc_bias) : Clear(qfrc_spring) : Clear(qfrc_damper) : Clear(qfrc_passive)
        Clear(qfrc_actuator) : Clear(qfrc_smooth) : Clear(qacc_smooth)
        Clear(qfrc_constraint) : Clear(qfrc_inverse) : Clear(qfrc_inverse)
        Clear(actuator_force) : Clear(actuator_length) : Clear(actuator_velocity)
        Clear(sensordata) : Clear(efc_qfrc)
        Clear(energy)
        energy(0) = 0.0
        energy(1) = 0.0

        ncon = 0
        nefc = 0
        solver_iterations = 0
        solver_residual = 0.0

        For i As Integer = 0 To warning.Length - 1
            warning(i).Reset()
        Next
    End Sub

    Private Shared Sub Clear(items As Double())
        If items IsNot Nothing Then
            Array.Clear(items, 0, items.Length)
        End If
    End Sub

    Private Shared Function Alloc(n As Integer) As Double()
        If n <= 0 Then Return New Double() {}

        Return New Double(n - 1) {}
    End Function

End Class
