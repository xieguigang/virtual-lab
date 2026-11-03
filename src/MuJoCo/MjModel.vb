''' <summary>
''' 仿真选项，对应 <c>mjmodel.h</c> 的 <c>mjOption</c>。
''' </summary>
''' <remarks>
''' 只有一部分选项会影响托管内核的行为：<see cref="timestep"/>、
''' <see cref="gravity"/>、<see cref="iterations"/>、<see cref="o_solref"/> 与
''' <see cref="o_friction"/> 会被 <c>MjEngineBridge</c> 映射到
''' <c>PhysicsWorld3D</c>；<see cref="integrator"/> 与 <see cref="solver"/> 只被记录，
''' 因为内核固定使用半隐式欧拉 + 顺序冲量。这一差异在
''' <c>Docs/MjApiMapping.md</c> 里列明。
''' </remarks>
Public Class mjOption

    ''' <summary>仿真步长，秒</summary>
    Public Property timestep As Double = 0.002
    ''' <summary>摩擦锥的阻抗比（内核用库仑椭圆锥近似）</summary>
    Public Property impratio As Double = 1.0
    ''' <summary>重力加速度，米/秒²（MuJoCo 是 z 轴向上，默认 -9.81）</summary>
    Public Property gravity As Double() = {0.0, 0.0, -9.81}
    ''' <summary>风力，米/秒</summary>
    Public Property wind As Double() = {0.0, 0.0, 0.0}
    ''' <summary>磁场，高斯</summary>
    Public Property magnetic As Double() = {0.0, -0.5, 0.0}
    ''' <summary>介质密度（不参与动力学，仅记录）</summary>
    Public Property density As Double = 0.0
    ''' <summary>介质粘性（不参与动力学，仅记录）</summary>
    Public Property viscosity As Double = 0.0
    ''' <summary>接触的额外余量，米</summary>
    Public Property o_margin As Double = 0.0
    ''' <summary>接触的 solref：<c>(时间常数, 阻尼比)</c></summary>
    Public Property o_solref As Double() = {0.02, 1.0}
    ''' <summary>接触的 solimp：<c>(dmin, dmax, width, midpoint, power)</c></summary>
    Public Property o_solimp As Double() = {0.9, 0.95, 0.001, 0.5, 2.0}
    ''' <summary>接触的默认摩擦：<c>(滑动, 扭转, 滚动, 螺旋, 滚动2)</c></summary>
    Public Property o_friction As Double() = {1.0, 0.005, 0.0001, 0.0, 0.0}

    ''' <summary>积分器类型（内核固定 EULER，仅记录）</summary>
    Public Property integrator As mjtIntegrator = mjtIntegrator.mjINT_EULER
    ''' <summary>摩擦锥类型（内核固定 ELLIPTIC，仅记录）</summary>
    Public Property cone As mjtCone = mjtCone.mjCONE_PYRAMIDAL
    ''' <summary>雅可比存储类型（托管层始终输出平铺数组）</summary>
    Public Property jacobian As mjtJacobian = mjtJacobian.mjJAC_DENSE
    ''' <summary>求解器类型（内核固定 PGS，仅记录）</summary>
    Public Property solver As mjtSolver = mjtSolver.mjSOL_PGS
    ''' <summary>约束求解的迭代次数</summary>
    Public Property iterations As Integer = 100
    ''' <summary>线搜索的迭代次数（仅记录）</summary>
    Public Property ls_iterations As Integer = 50
    ''' <summary>noslip 的迭代次数（仅记录）</summary>
    Public Property noslip_iterations As Integer = 0
    ''' <summary>禁用的特性位</summary>
    Public Property disableflags As Integer = 0
    ''' <summary>启用的特性位</summary>
    Public Property enableflags As Integer = 0
    ''' <summary>禁用的执行器位图</summary>
    Public Property disableactuator As Integer = 0

    Public Function Clone() As mjOption
        Return New mjOption With {
            .timestep = timestep,
            .impratio = impratio,
            .gravity = CType(gravity.Clone(), Double()),
            .wind = CType(wind.Clone(), Double()),
            .magnetic = CType(magnetic.Clone(), Double()),
            .density = density,
            .viscosity = viscosity,
            .o_margin = o_margin,
            .o_solref = CType(o_solref.Clone(), Double()),
            .o_solimp = CType(o_solimp.Clone(), Double()),
            .o_friction = CType(o_friction.Clone(), Double()),
            .integrator = integrator,
            .cone = cone,
            .jacobian = jacobian,
            .solver = solver,
            .iterations = iterations,
            .ls_iterations = ls_iterations,
            .noslip_iterations = noslip_iterations,
            .disableflags = disableflags,
            .enableflags = enableflags,
            .disableactuator = disableactuator
        }
    End Function
End Class

''' <summary>模型统计量，对应 <c>mjmodel.h</c> 的 <c>mjStatistic</c></summary>
Public Class mjStatistic
    ''' <summary>平均惯量（对角元素均值）</summary>
    Public Property meaninertia As Double = 0.0
    ''' <summary>平均质量</summary>
    Public Property meanmass As Double = 0.0
    ''' <summary>平均尺寸</summary>
    Public Property meansize As Double = 0.0
    ''' <summary>包围球半径</summary>
    Public Property extent As Double = 0.0
    ''' <summary>包围球中心</summary>
    Public Property center As Double() = {0.0, 0.0, 0.0}
End Class

''' <summary>
''' 编译之后的只读模型，对应 <c>mjmodel.h</c> 的 <c>mjModel</c>。
''' </summary>
''' <remarks>
''' <para>
''' 全部是平铺数组，索引关系在编译期就固定下来：运行时不查字典、不做字符串比较，
''' 只有 <c>mj_name2id</c> / <c>mj_id2name</c> 会走名称表。
''' </para>
''' <para>
''' 与 C 版的差异（都在 <c>Docs/MjApiMapping.md</c> 里说明）：
''' </para>
''' <list type="bullet">
''' <item><description>
''' 计数使用 <see cref="Integer"/> 而不是 <c>mjtSize</c>；
''' </description></item>
''' <item><description>
''' 执行器个数用 <see cref="nactuator"/>，控制量维数用 <see cref="nu"/>，
''' 两者在有多个 <c>ctrladr</c> 的执行器上才会不同，本实现里始终相等；
''' </description></item>
''' <item><description>
''' 额外维护 <see cref="body_fullinertia"/>（完整的 6 个惯性张量分量），
''' 因为内核要的是 3×3 张量，而 <see cref="body_inertia"/> 只有对角元。
''' </description></item>
''' </list>
''' </remarks>
Public Class mjModel

    ' /********************************************************************************/
    '  尺寸
    ' /********************************************************************************/

    Public Property nq As Integer
    Public Property nv As Integer
    Public Property nu As Integer
    Public Property nactuator As Integer
    Public Property nout As Integer
    Public Property na As Integer
    Public Property nbody As Integer
    Public Property njnt As Integer
    Public Property ngeom As Integer
    Public Property nsite As Integer
    Public Property ncam As Integer
    Public Property nlight As Integer
    Public Property nmesh As Integer
    Public Property nmat As Integer
    Public Property npair As Integer
    Public Property nexclude As Integer
    Public Property neq As Integer
    Public Property ntendon As Integer
    Public Property nwrap As Integer
    Public Property nsensor As Integer
    Public Property nsensordata As Integer
    Public Property nkey As Integer
    Public Property nmocap As Integer
    Public Property nJmom As Integer
    Public Property ngravcomp As Integer

    ''' <summary>模型名（MJCF 的 <c>&lt;mujoco model="..."&gt;</c>）</summary>
    Public Property modelname As String = ""
    ''' <summary>编译时使用的资产目录（<c>compiler meshdir</c>）</summary>
    Public Property meshdir As String = ""

    Public Property opt As New mjOption()
    Public Property stat As New mjStatistic()

    ' /********************************************************************************/
    '  初始状态
    ' /********************************************************************************/

    ''' <summary>默认的广义坐标（nq）</summary>
    Public Property qpos0 As Double()
    ''' <summary>弹簧参考位置（nq），用于 <c>qpos_spring</c></summary>
    Public Property qpos_spring As Double()

    ' /********************************************************************************/
    '  body（nbody 组）
    ' /********************************************************************************/

    Public Property body_parentid As Integer()
    Public Property body_rootid As Integer()
    Public Property body_weldid As Integer()
    Public Property body_jntnum As Integer()
    Public Property body_jntadr As Integer()
    Public Property body_dofnum As Integer()
    Public Property body_dofadr As Integer()
    Public Property body_geomnum As Integer()
    Public Property body_geomadr As Integer()
    Public Property body_simple As Boolean()
    Public Property body_sameframe As Boolean()
    Public Property body_pos As Double()
    Public Property body_quat As Double()
    Public Property body_ipos As Double()
    Public Property body_iquat As Double()
    Public Property body_mass As Double()
    Public Property body_subtreemass As Double()
    ''' <summary>惯量的对角元（在 <see cref="body_iquat"/> 旋转后的坐标系里）</summary>
    Public Property body_inertia As Double()
    ''' <summary>完整的惯性张量（ixx, iyy, izz, ixy, ixz, iyz），内核使用</summary>
    Public Property body_fullinertia As Double()
    Public Property body_invweight0 As Double()
    Public Property body_gravcomp As Double()
    Public Property body_margin As Double()
    Public Property body_contype As Integer()
    Public Property body_conaffinity As Integer()
    Public Property body_name As String()

    ''' <summary>
    ''' 内核的碰撞分组位（托管层扩展，C 版没有）。
    ''' </summary>
    ''' <remarks>
    ''' 原生 MuJoCo 按 geom 的 <c>contype</c>/<c>conaffinity</c> 决定碰撞对；
    ''' 计算内核是"每个刚体一个形状 + 一对 group/mask 位掩码"，粒度只能到 body。
    ''' 这两个数组保存换算之后的分组，供 <c>MjCollisionModel</c> 使用。
    ''' </remarks>
    Public Property body_group As Integer()
    ''' <summary>内核的碰撞掩码位（托管层扩展）</summary>
    Public Property body_mask As Integer()

    ' /********************************************************************************/
    '  joint（njnt 组）
    ' /********************************************************************************/

    Public Property jnt_type As Integer()
    Public Property jnt_qposadr As Integer()
    Public Property jnt_dofadr As Integer()
    Public Property jnt_bodyid As Integer()
    Public Property jnt_actuatorid As Integer()
    Public Property jnt_group As Integer()
    Public Property jnt_limited As Boolean()
    Public Property jnt_actfrclimited As Boolean()
    Public Property jnt_solref As Double()
    Public Property jnt_solimp As Double()
    Public Property jnt_pos As Double()
    Public Property jnt_axis As Double()
    Public Property jnt_stiffness As Double()
    Public Property jnt_range As Double()
    Public Property jnt_actfrcrange As Double()
    Public Property jnt_margin As Double()
    Public Property jnt_name As String()

    ' /********************************************************************************/
    '  dof（nv 组）
    ' /********************************************************************************/

    Public Property dof_bodyid As Integer()
    Public Property dof_jntid As Integer()
    Public Property dof_parentid As Integer()
    Public Property dof_solref As Double()
    Public Property dof_solimp As Double()
    Public Property dof_frictionloss As Double()
    Public Property dof_armature As Double()
    Public Property dof_damping As Double()
    Public Property dof_invweight0 As Double()
    Public Property dof_M0 As Double()

    ' /********************************************************************************/
    '  geom（ngeom 组）
    ' /********************************************************************************/

    Public Property geom_type As Integer()
    Public Property geom_contype As Integer()
    Public Property geom_conaffinity As Integer()
    Public Property geom_condim As Integer()
    Public Property geom_bodyid As Integer()
    Public Property geom_dataid As Integer()
    Public Property geom_matid As Integer()
    Public Property geom_group As Integer()
    Public Property geom_priority As Integer()
    Public Property geom_solmix As Double()
    Public Property geom_solref As Double()
    Public Property geom_solimp As Double()
    Public Property geom_size As Double()
    Public Property geom_rbound As Double()
    Public Property geom_pos As Double()
    Public Property geom_quat As Double()
    Public Property geom_friction As Double()
    Public Property geom_margin As Double()
    Public Property geom_gap As Double()
    Public Property geom_rgba As Double()
    Public Property geom_name As String()

    ' /********************************************************************************/
    '  site（nsite 组）
    ' /********************************************************************************/

    Public Property site_type As Integer()
    Public Property site_bodyid As Integer()
    Public Property site_matid As Integer()
    Public Property site_group As Integer()
    Public Property site_size As Double()
    Public Property site_pos As Double()
    Public Property site_quat As Double()
    Public Property site_rgba As Double()
    Public Property site_name As String()

    ' /********************************************************************************/
    '  camera / light
    ' /********************************************************************************/

    Public Property cam_mode As Integer()
    Public Property cam_bodyid As Integer()
    Public Property cam_targetbodyid As Integer()
    Public Property cam_pos As Double()
    Public Property cam_quat As Double()
    Public Property cam_fovy As Double()
    Public Property cam_name As String()

    Public Property light_bodyid As Integer()
    Public Property light_targetbodyid As Integer()
    Public Property light_type As Integer()
    Public Property light_pos As Double()
    Public Property light_dir As Double()
    Public Property light_name As String()

    ' /********************************************************************************/
    '  mesh / material
    ' /********************************************************************************/

    Public Property mesh_vertadr As Integer()
    Public Property mesh_vertnum As Integer()
    Public Property mesh_faceadr As Integer()
    Public Property mesh_facenum As Integer()
    Public Property mesh_vert As Double()
    Public Property mesh_face As Integer()
    Public Property mesh_name As String()

    Public Property mat_rgba As Double()
    Public Property mat_name As String()

    ' /********************************************************************************/
    '  actuator（nactuator 组）
    ' /********************************************************************************/

    Public Property actuator_trntype As Integer()
    Public Property actuator_dyntype As Integer()
    Public Property actuator_gaintype As Integer()
    Public Property actuator_biastype As Integer()
    Public Property actuator_ctrladr As Integer()
    ''' <summary>执行器的传动目标 id（每个执行器两项：目标、第二目标）</summary>
    Public Property actuator_trnid As Integer()
    Public Property actuator_dynprm As Double()
    Public Property actuator_gainprm As Double()
    Public Property actuator_biasprm As Double()
    Public Property actuator_forcelimited As Boolean()
    Public Property actuator_forcerange As Double()
    Public Property actuator_ctrllimited As Boolean()
    Public Property actuator_ctrlrange As Double()
    Public Property actuator_gear As Double()
    Public Property actuator_group As Integer()
    Public Property actuator_name As String()

    ' /********************************************************************************/
    '  sensor（nsensor 组）
    ' /********************************************************************************/

    Public Property sensor_type As Integer()
    Public Property sensor_datatype As Integer()
    Public Property sensor_objtype As Integer()
    Public Property sensor_objid As Integer()
    Public Property sensor_reftype As Integer()
    Public Property sensor_refid As Integer()
    Public Property sensor_dim As Integer()
    Public Property sensor_adr As Integer()
    Public Property sensor_cutoff As Double()
    Public Property sensor_noise As Double()
    Public Property sensor_name As String()

    ' /********************************************************************************/
    '  equality / tendon / exclude
    ' /********************************************************************************/

    Public Property eq_type As Integer()
    Public Property eq_obj1id As Integer()
    Public Property eq_obj2id As Integer()
    Public Property eq_objtype As Integer()
    Public Property eq_active0 As Boolean()
    Public Property eq_solref As Double()
    Public Property eq_solimp As Double()
    Public Property eq_data As Double()
    Public Property eq_name As String()

    Public Property tendon_type As Integer()
    Public Property tendon_adr As Integer()
    Public Property tendon_num As Integer()
    Public Property tendon_limited As Boolean()
    Public Property tendon_range As Double()
    Public Property tendon_stiffness As Double()
    Public Property tendon_damping As Double()
    Public Property tendon_armature As Double()
    Public Property tendon_frictionloss As Double()
    Public Property tendon_length0 As Double()
    Public Property tendon_lengthspring As Double()
    Public Property tendon_name As String()
    ''' <summary>腱的 wrap 列表（ntendon × nwrap 的扁平表）</summary>
    Public Property wrap_type As Integer()
    Public Property wrap_objid As Integer()
    Public Property wrap_prm As Double()

    Public Property exclude_signature As Long()

    ' /********************************************************************************/
    '  keyframe（nkey 组）
    ' /********************************************************************************/

    Public Property key_time As Double()
    Public Property key_qpos As Double()
    Public Property key_qvel As Double()
    Public Property key_act As Double()
    Public Property key_ctrl As Double()
    Public Property key_name As String()

    ''' <summary>按 geom id 缓存的凸包（<c>mj_ray</c> 用），惰性填充</summary>
    Friend Property GeomHulls As Dictionary(Of Integer, Geometry.ConvexHullResult)

    ' /********************************************************************************/
    '  名称表
    ' /********************************************************************************/

    Private ReadOnly m_names As New Dictionary(Of mjtObj, Dictionary(Of String, Integer))

    ''' <summary>注册一个名字，重名时保留第一次注册的索引（与 C 版一致）</summary>
    Public Sub RegisterName(type As mjtObj, id As Integer, name As String)
        If String.IsNullOrEmpty(name) Then
            Return
        End If

        Dim table As Dictionary(Of String, Integer) = Nothing

        If Not m_names.TryGetValue(type, table) Then
            table = New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            m_names(type) = table
        End If

        If Not table.ContainsKey(name) Then
            table(name) = id
        End If
    End Sub

    ''' <summary>名字 → 索引，找不到时返回 -1（与 <c>mj_name2id</c> 一致）</summary>
    Public Function FindId(type As mjtObj, name As String) As Integer
        If String.IsNullOrEmpty(name) Then
            Return -1
        End If

        Dim table As Dictionary(Of String, Integer) = Nothing

        If m_names.TryGetValue(type, table) Then
            Dim id As Integer

            If table.TryGetValue(name, id) Then
                Return id
            End If
        End If

        ' mjOBJ_XBODY 是 mjOBJ_BODY 的别名
        If type = mjtObj.mjOBJ_XBODY Then
            Return FindId(mjtObj.mjOBJ_BODY, name)
        End If

        Return -1
    End Function

    ''' <summary>索引 → 名字，找不到时返回 <c>Nothing</c>（与 <c>mj_id2name</c> 一致）</summary>
    Public Function FindName(type As mjtObj, id As Integer) As String
        Select Case type
            Case mjtObj.mjOBJ_BODY, mjtObj.mjOBJ_XBODY : Return Pick(body_name, id)
            Case mjtObj.mjOBJ_JOINT : Return Pick(jnt_name, id)
            Case mjtObj.mjOBJ_GEOM : Return Pick(geom_name, id)
            Case mjtObj.mjOBJ_SITE : Return Pick(site_name, id)
            Case mjtObj.mjOBJ_CAMERA : Return Pick(cam_name, id)
            Case mjtObj.mjOBJ_LIGHT : Return Pick(light_name, id)
            Case mjtObj.mjOBJ_MESH : Return Pick(mesh_name, id)
            Case mjtObj.mjOBJ_MATERIAL : Return Pick(mat_name, id)
            Case mjtObj.mjOBJ_ACTUATOR : Return Pick(actuator_name, id)
            Case mjtObj.mjOBJ_SENSOR : Return Pick(sensor_name, id)
            Case mjtObj.mjOBJ_EQUALITY : Return Pick(eq_name, id)
            Case mjtObj.mjOBJ_TENDON : Return Pick(tendon_name, id)
            Case mjtObj.mjOBJ_KEY : Return Pick(key_name, id)
            Case Else : Return Nothing
        End Select
    End Function

    Private Shared Function Pick(items As String(), index As Integer) As String
        If items Is Nothing OrElse index < 0 OrElse index >= items.Length Then
            Return Nothing
        End If

        Return items(index)
    End Function

    ' /********************************************************************************/
    '  分配
    ' /********************************************************************************/

    ''' <summary>
    ''' 按已确定的尺寸一次性分配全部数组。编译器在写完计数之后调用一次。
    ''' </summary>
    Public Sub Allocate()
        qpos0 = Alloc(nq)
        qpos_spring = Alloc(nq)

        body_parentid = AllocInt(nbody)
        body_rootid = AllocInt(nbody)
        body_weldid = AllocInt(nbody)
        body_jntnum = AllocInt(nbody)
        body_jntadr = AllocInt(nbody)
        body_dofnum = AllocInt(nbody)
        body_dofadr = AllocInt(nbody)
        body_geomnum = AllocInt(nbody)
        body_geomadr = AllocInt(nbody)
        body_simple = AllocBool(nbody)
        body_sameframe = AllocBool(nbody)
        body_pos = Alloc(nbody * 3)
        body_quat = Alloc(nbody * 4)
        body_ipos = Alloc(nbody * 3)
        body_iquat = Alloc(nbody * 4)
        body_mass = Alloc(nbody)
        body_subtreemass = Alloc(nbody)
        body_inertia = Alloc(nbody * 3)
        body_fullinertia = Alloc(nbody * 6)
        body_invweight0 = Alloc(nbody * 2)
        body_gravcomp = Alloc(nbody)
        body_margin = Alloc(nbody)
        body_contype = AllocInt(nbody)
        body_conaffinity = AllocInt(nbody)
        body_group = AllocInt(nbody)
        body_mask = AllocInt(nbody)
        body_name = AllocStr(nbody)

        jnt_type = AllocInt(njnt)
        jnt_qposadr = AllocInt(njnt)
        jnt_dofadr = AllocInt(njnt)
        jnt_bodyid = AllocInt(njnt)
        jnt_actuatorid = AllocInt(njnt)
        jnt_group = AllocInt(njnt)
        jnt_limited = AllocBool(njnt)
        jnt_actfrclimited = AllocBool(njnt)
        jnt_solref = Alloc(njnt * mjNREF)
        jnt_solimp = Alloc(njnt * mjNIMP)
        jnt_pos = Alloc(njnt * 3)
        jnt_axis = Alloc(njnt * 3)
        jnt_stiffness = Alloc(njnt)
        jnt_range = Alloc(njnt * 2)
        jnt_actfrcrange = Alloc(njnt * 2)
        jnt_margin = Alloc(njnt)
        jnt_name = AllocStr(njnt)

        dof_bodyid = AllocInt(nv)
        dof_jntid = AllocInt(nv)
        dof_parentid = AllocInt(nv)
        dof_solref = Alloc(nv * mjNREF)
        dof_solimp = Alloc(nv * mjNIMP)
        dof_frictionloss = Alloc(nv)
        dof_armature = Alloc(nv)
        dof_damping = Alloc(nv)
        dof_invweight0 = Alloc(nv)
        dof_M0 = Alloc(nv)

        geom_type = AllocInt(ngeom)
        geom_contype = AllocInt(ngeom)
        geom_conaffinity = AllocInt(ngeom)
        geom_condim = AllocInt(ngeom)
        geom_bodyid = AllocInt(ngeom)
        geom_dataid = AllocInt(ngeom)
        geom_matid = AllocInt(ngeom)
        geom_group = AllocInt(ngeom)
        geom_priority = AllocInt(ngeom)
        geom_solmix = Alloc(ngeom)
        geom_solref = Alloc(ngeom * mjNREF)
        geom_solimp = Alloc(ngeom * mjNIMP)
        geom_size = Alloc(ngeom * 3)
        geom_rbound = Alloc(ngeom)
        geom_pos = Alloc(ngeom * 3)
        geom_quat = Alloc(ngeom * 4)
        geom_friction = Alloc(ngeom * mjNFRICTION)
        geom_margin = Alloc(ngeom)
        geom_gap = Alloc(ngeom)
        geom_rgba = Alloc(ngeom * 4)
        geom_name = AllocStr(ngeom)

        site_type = AllocInt(nsite)
        site_bodyid = AllocInt(nsite)
        site_matid = AllocInt(nsite)
        site_group = AllocInt(nsite)
        site_size = Alloc(nsite * 3)
        site_pos = Alloc(nsite * 3)
        site_quat = Alloc(nsite * 4)
        site_rgba = Alloc(nsite * 4)
        site_name = AllocStr(nsite)

        cam_mode = AllocInt(ncam)
        cam_bodyid = AllocInt(ncam)
        cam_targetbodyid = AllocInt(ncam)
        cam_pos = Alloc(ncam * 3)
        cam_quat = Alloc(ncam * 4)
        cam_fovy = Alloc(ncam)
        cam_name = AllocStr(ncam)

        light_bodyid = AllocInt(nlight)
        light_targetbodyid = AllocInt(nlight)
        light_type = AllocInt(nlight)
        light_pos = Alloc(nlight * 3)
        light_dir = Alloc(nlight * 3)
        light_name = AllocStr(nlight)

        mesh_vertadr = AllocInt(nmesh)
        mesh_vertnum = AllocInt(nmesh)
        mesh_faceadr = AllocInt(nmesh)
        mesh_facenum = AllocInt(nmesh)
        mesh_name = AllocStr(nmesh)

        mat_rgba = Alloc(nmat * 4)
        mat_name = AllocStr(nmat)

        actuator_trntype = AllocInt(nactuator)
        actuator_dyntype = AllocInt(nactuator)
        actuator_gaintype = AllocInt(nactuator)
        actuator_biastype = AllocInt(nactuator)
        actuator_ctrladr = AllocInt(nactuator)
        actuator_trnid = AllocInt(nactuator * 2)
        actuator_dynprm = Alloc(nactuator * mjNDYN)
        actuator_gainprm = Alloc(nactuator * mjNGAIN)
        actuator_biasprm = Alloc(nactuator * mjNBIAS)
        actuator_forcelimited = AllocBool(nactuator)
        actuator_forcerange = Alloc(nactuator * 2)
        actuator_ctrllimited = AllocBool(nactuator)
        actuator_ctrlrange = Alloc(nactuator * 2)
        actuator_gear = Alloc(nactuator * 6)
        actuator_group = AllocInt(nactuator)
        actuator_name = AllocStr(nactuator)

        sensor_type = AllocInt(nsensor)
        sensor_datatype = AllocInt(nsensor)
        sensor_objtype = AllocInt(nsensor)
        sensor_objid = AllocInt(nsensor)
        sensor_reftype = AllocInt(nsensor)
        sensor_refid = AllocInt(nsensor)
        sensor_dim = AllocInt(nsensor)
        sensor_adr = AllocInt(nsensor)
        sensor_cutoff = Alloc(nsensor)
        sensor_noise = Alloc(nsensor)
        sensor_name = AllocStr(nsensor)
        nsensordata = 0

        eq_type = AllocInt(neq)
        eq_obj1id = AllocInt(neq)
        eq_obj2id = AllocInt(neq)
        eq_objtype = AllocInt(neq)
        eq_active0 = AllocBool(neq)
        eq_solref = Alloc(neq * mjNREF)
        eq_solimp = Alloc(neq * mjNIMP)
        eq_data = Alloc(neq * mjNEQDATA)
        eq_name = AllocStr(neq)

        tendon_type = AllocInt(ntendon)
        tendon_adr = AllocInt(ntendon)
        tendon_num = AllocInt(ntendon)
        tendon_limited = AllocBool(ntendon)
        tendon_range = Alloc(ntendon * 2)
        tendon_stiffness = Alloc(ntendon)
        tendon_damping = Alloc(ntendon)
        tendon_armature = Alloc(ntendon)
        tendon_frictionloss = Alloc(ntendon)
        tendon_length0 = Alloc(ntendon)
        tendon_lengthspring = Alloc(ntendon)
        tendon_name = AllocStr(ntendon)
        wrap_type = AllocInt(nwrap)
        wrap_objid = AllocInt(nwrap)
        wrap_prm = Alloc(nwrap)

        key_time = Alloc(nkey)
        key_qpos = Alloc(nkey * nq)
        key_qvel = Alloc(nkey * nv)
        key_act = Alloc(nkey * na)
        key_ctrl = Alloc(nkey * nu)
        key_name = AllocStr(nkey)

        ' 默认值：单位四元数
        For i As Integer = 0 To nbody - 1
            body_quat(i * 4) = 1.0
            body_iquat(i * 4) = 1.0
        Next

        For i As Integer = 0 To ngeom - 1
            geom_quat(i * 4) = 1.0
            geom_rgba(i * 4 + 0) = 0.5
            geom_rgba(i * 4 + 1) = 0.5
            geom_rgba(i * 4 + 2) = 0.5
            geom_rgba(i * 4 + 3) = 1.0
        Next

        For i As Integer = 0 To nsite - 1
            site_quat(i * 4) = 1.0
            site_rgba(i * 4 + 3) = 1.0
        Next

        For i As Integer = 0 To ncam - 1
            cam_quat(i * 4) = 1.0
        Next
    End Sub

    Private Shared Function Alloc(n As Integer) As Double()
        If n <= 0 Then Return New Double() {}

        Return New Double(n - 1) {}
    End Function

    Private Shared Function AllocInt(n As Integer) As Integer()
        If n <= 0 Then Return New Integer() {}

        Return New Integer(n - 1) {}
    End Function

    Private Shared Function AllocBool(n As Integer) As Boolean()
        If n <= 0 Then Return New Boolean() {}

        Return New Boolean(n - 1) {}
    End Function

    Private Shared Function AllocStr(n As Integer) As String()
        If n <= 0 Then Return New String() {}

        Return New String(n - 1) {}
    End Function

End Class
