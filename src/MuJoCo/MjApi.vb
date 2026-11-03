Imports System.Numerics

''' <summary>
''' 顶层 API 门面，函数名与参数顺序对齐 <c>include/mujoco/mujoco.h</c>。
''' </summary>
''' <remarks>
''' <para>
''' 用法与 C 版一样：
''' </para>
''' <code>
'''   Dim m = mj_loadXML("robot.xml")
'''   Dim d = mj_makeData(m)
'''   For step = 1 To 1000
'''       d.ctrl(0) = 0.3
'''       mj_step(m, d)
'''   Next
''' </code>
''' <para>
''' 与 C 版的差异集中在三处（详见 <c>Docs/MjApiMapping.md</c>）：
''' </para>
''' <list type="number">
''' <item><description>
''' <c>mj_step1</c>/<c>mj_step2</c> 可以分开调用，但内部的"求解 + 积分"是一次
''' 原子操作（<c>PhysicsWorld3D.Step</c>），不像 C 版那样拆成加速度求解与积分两段；
''' </description></item>
''' <item><description>
''' 错误通过 <see cref="MjException"/> 或 <c>ByRef errorText</c> 报告，
''' 没有全局的 <c>mj_error</c> 回调；
''' </description></item>
''' <item><description>
''' <c>mjModel</c> / <c>mjData</c> 是托管对象，没有 <c>mj_deleteModel</c> 的必要
''' （保留该函数只为让 C 风格的代码能直接搬过来）。
''' </description></item>
''' </list>
''' </remarks>
Public Module MjApi

    ' /********************************************************************************/
    '  解析与编译
    ' /********************************************************************************/

    ''' <summary>读一个 MJCF 文件并编译成模型；失败时把原因写进 <paramref name="errorText"/></summary>
    Public Function mj_loadXML(filename As String, ByRef errorText As String) As mjModel
        errorText = Nothing

        Dim spec As MjSpec

        Try
            spec = MjcfParser.Parse(filename)
        Catch ex As MjException
            errorText = ex.Message

            Return Nothing
        End Try

        Return mj_compile(spec, errorText)
    End Function

    ''' <summary>读一个 MJCF 文件并编译成模型；失败时抛 <see cref="MjException"/></summary>
    Public Function mj_loadXML(filename As String) As mjModel
        Dim errorText As String = Nothing
        Dim model As mjModel = mj_loadXML(filename, errorText)

        If model Is Nothing Then
            Throw New MjException(If(String.IsNullOrEmpty(errorText), $"'{filename}' could not be loaded", errorText))
        End If

        Return model
    End Function

    ''' <summary>只解析，不编译</summary>
    Public Function mj_parseXML(filename As String, ByRef errorText As String) As MjSpec
        errorText = Nothing

        Try
            Return MjcfParser.Parse(filename)
        Catch ex As MjException
            errorText = ex.Message

            Return Nothing
        End Try
    End Function

    ''' <summary>从字符串解析，不编译</summary>
    Public Function mj_parseXMLString(xml As String, ByRef errorText As String,
                                      Optional baseDirectory As String = Nothing) As MjSpec
        errorText = Nothing

        Try
            Return MjcfParser.ParseString(xml, baseDirectory)
        Catch ex As MjException
            errorText = ex.Message

            Return Nothing
        End Try
    End Function

    ''' <summary>把可编辑的模型树编译成只读模型</summary>
    Public Function mj_compile(spec As MjSpec, ByRef errorText As String) As mjModel
        errorText = Nothing

        If spec Is Nothing Then
            errorText = "the spec is Nothing"

            Return Nothing
        End If

        Dim errors As New MjErrorBuffer()
        Dim model As mjModel = MjcfCompiler.Compile(spec, errors)

        errorText = errors.Text

        Return model
    End Function

    ''' <summary>把可编辑的模型树编译成只读模型；失败时抛异常</summary>
    Public Function mj_compile(spec As MjSpec) As mjModel
        Dim errorText As String = Nothing
        Dim model As mjModel = mj_compile(spec, errorText)

        If model Is Nothing Then
            Throw New MjException(If(String.IsNullOrEmpty(errorText), "the model could not be compiled", errorText))
        End If

        Return model
    End Function

    '/********************************************************************************/
    '  数据
    ' /********************************************************************************/

    ''' <summary>按模型分配一份仿真状态</summary>
    Public Function mj_makeData(m As mjModel) As mjData
        MjAssert.RequireNotNull(m, NameOf(m))

        Dim d As New mjData()
        Call d.Allocate(m)

        If m.nq > 0 Then
            Call Array.Copy(m.qpos0, d.qpos, m.nq)
        End If

        For e As Integer = 0 To m.neq - 1
            d.eq_active(e) = m.eq_active0(e)
        Next

        d.time = 0.0

        ' 装配引擎（内部会按 qpos0 摆好姿态再建约束）
        d.Engine = New MjEngineBridge(m, d)

        Call mj_forward(m, d)

        Return d
    End Function

    ''' <summary>复制一份数据（含引擎侧的装配）</summary>
    Public Function mj_copyData(dest As mjData, m As mjModel, src As mjData) As mjData
        Dim result As mjData = If(dest, New mjData())

        If result.Engine Is Nothing Then
            Call result.Allocate(m)
            result.Engine = New MjEngineBridge(m, result)
        End If

        result.time = src.time

        Call Array.Copy(src.qpos, result.qpos, m.nq)
        Call Array.Copy(src.qvel, result.qvel, m.nv)
        Call Array.Copy(src.qacc, result.qacc, m.nv)
        Call Array.Copy(src.ctrl, result.ctrl, m.nu)
        Call Array.Copy(src.act, result.act, m.na)
        Call Array.Copy(src.qfrc_applied, result.qfrc_applied, m.nv)
        Call Array.Copy(src.xfrc_applied, result.xfrc_applied, m.nbody * 6)
        Call Array.Copy(src.energy, result.energy, 2)

        Call result.Engine.SyncToEngine()
        Call result.Engine.InvalidateContacts()
        Call mj_forward(m, result)

        Return result
    End Function

    ''' <summary>托管层不需要显式释放，保留该函数只为与 C 代码逐行对照</summary>
    Public Sub mj_deleteModel(m As mjModel)
    End Sub

    ''' <summary>托管层不需要显式释放，保留该函数只为与 C 代码逐行对照</summary>
    Public Sub mj_deleteData(d As mjData)
    End Sub

    ' /********************************************************************************/
    '  推进
    ' /********************************************************************************/

    ''' <summary>推进一个步长</summary>
    Public Sub mj_step(m As mjModel, d As mjData)
        Call mj_step1(m, d)
        Call mj_step2(m, d)
    End Sub

    ''' <summary>
    ''' 位置/速度前向 + 执行器装配。与 <see cref="mj_step2"/> 合起来等于
    ''' <see cref="mj_step"/>。
    ''' </summary>
    Public Sub mj_step1(m As mjModel, d As mjData)
        Call ClampControl(m, d)
        Call d.Engine.ApplyOptions()
        Call d.Engine.SyncToEngine()

        Call MjKinematics.RunInertia(m, d)
        Call MjKinematics.RunVelocity(m, d)
    End Sub

    ''' <summary>求解 + 积分 + 回读</summary>
    Public Sub mj_step2(m As mjModel, d As mjData)
        d.Engine.World.Step(m.opt.timestep)
        d.Engine.SyncFromEngine()

        Call MjKinematics.RunInertia(m, d)
        Call MjKinematics.RunVelocity(m, d)

        Call MjDynamics.BuildMassMatrix(m, d)
        Call MjDynamics.BuildBias(m, d)
        Call MjDynamics.BuildPassive(m, d)
        Call MjDynamics.BuildEnergy(m, d)

        Call CollectContacts(m, d)
        Call ComputeActuatorTerms(m, d)
        Call MjSensorModel.Run(m, d)

        d.time += m.opt.timestep
        d.solver_iterations = d.Engine.World.SolverIterations
        d.solver_residual = d.Engine.World.LastResidual

        Call CheckFinite(m, d)
    End Sub

    ''' <summary>只刷新派生量，不推进时间</summary>
    Public Sub mj_forward(m As mjModel, d As mjData)
        Call ClampControl(m, d)
        Call d.Engine.ApplyOptions()
        Call d.Engine.SyncToEngine()

        Call MjKinematics.RunInertia(m, d)
        Call MjKinematics.RunVelocity(m, d)

        Call MjDynamics.BuildMassMatrix(m, d)
        Call MjDynamics.BuildBias(m, d)
        Call MjDynamics.BuildPassive(m, d)
        Call MjDynamics.BuildEnergy(m, d)

        Call CollectContacts(m, d)
        Call ComputeActuatorTerms(m, d)
        Call MjSensorModel.Run(m, d)
    End Sub

    ''' <summary>
    ''' 逆动力学：给定 <c>qacc</c> 求所需的广义力。
    ''' </summary>
    ''' <remarks>
    ''' 结果写进 <c>qfrc_inverse</c>：
    ''' <c>M·qacc + qfrc_bias − qfrc_passive − qfrc_actuator − qfrc_constraint</c>。
    ''' 有接触时原生 MuJoCo 会把接触力也算进去，托管层只做树动力学部分的减法，
    ''' 因此该量的含义是"除了接触之外，还需要多少力"。
    ''' </remarks>
    Public Sub mj_inverse(m As mjModel, d As mjData)
        Call mj_forward(m, d)

        Dim work As Double() = New Double(Math.Max(m.nv, 1) - 1) {}

        Call MjDynamics.MulM(m, d, work, d.qacc)

        For i As Integer = 0 To m.nv - 1
            d.qfrc_inverse(i) = work(i) + d.qfrc_bias(i) - d.qfrc_passive(i) -
                                d.qfrc_actuator(i) - d.qfrc_constraint(i)
        Next
    End Sub

    ' /********************************************************************************/
    '  派生量
    ' /********************************************************************************/

    Public Sub mj_kinematics(m As mjModel, d As mjData)
        Call MjKinematics.RunPosition(m, d)
    End Sub

    Public Sub mj_comPos(m As mjModel, d As mjData)
        Call MjKinematics.RunInertia(m, d)
    End Sub

    Public Sub mj_comVel(m As mjModel, d As mjData)
        Call MjKinematics.RunVelocity(m, d)
    End Sub

    Public Sub mj_jac(m As mjModel, d As mjData, jacp As Double(), jacr As Double(),
                      point As Vector3, body As Integer)
        Call MjJacobian.jac(m, d, jacp, jacr, point, body)
    End Sub

    Public Sub mj_jacBody(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), body As Integer)
        Call MjJacobian.jacBody(m, d, jacp, jacr, body)
    End Sub

    Public Sub mj_jacBodyCom(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), body As Integer)
        Call MjJacobian.jacBodyCom(m, d, jacp, jacr, body)
    End Sub

    Public Sub mj_jacSubtreeCom(m As mjModel, d As mjData, jacp As Double(), body As Integer)
        Call MjJacobian.jacSubtreeCom(m, d, jacp, body)
    End Sub

    Public Sub mj_jacGeom(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), geom As Integer)
        Call MjJacobian.jacGeom(m, d, jacp, jacr, geom)
    End Sub

    Public Sub mj_jacSite(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), site As Integer)
        Call MjJacobian.jacSite(m, d, jacp, jacr, site)
    End Sub

    Public Sub mj_jacPointAxis(m As mjModel, d As mjData, jacPoint As Double(), jacAxis As Double(),
                               point As Vector3, axis As Vector3, body As Integer)
        Call MjJacobian.jacPointAxis(m, d, jacPoint, jacAxis, point, axis, body)
    End Sub

    ''' <summary>把质量矩阵拆成 <c>nv × nv</c> 的平铺数组</summary>
    Public Sub mj_fullM(m As mjModel, d As mjData, dst As Double())
        MjAssert.RequireSize(dst, m.nv * m.nv, NameOf(dst))

        Call Array.Copy(d.M, 0, dst, 0, m.nv * m.nv)
    End Sub

    Public Sub mj_mulM(m As mjModel, d As mjData, res As Double(), vec As Double())
        Call MjDynamics.MulM(m, d, res, vec)
    End Sub

    Public Function mj_ray(m As mjModel, d As mjData, pnt As Double(), vec As Double(),
                           geomgroup As Boolean(), flg_static As Boolean,
                           bodyexclude As Integer, ByRef geomid As Integer) As Double
        Return MjRayCast.mj_ray(m, d, pnt, vec, geomgroup, flg_static, bodyexclude, geomid)
    End Function

    ' /********************************************************************************/
    '  状态
    ' /********************************************************************************/

    Public Function mj_stateSize(m As mjModel, sig As MjStateSig) As Integer
        Return MjState.mj_stateSize(m, sig)
    End Function

    Public Sub mj_getState(m As mjModel, d As mjData, state As Double(), sig As MjStateSig)
        Call MjState.mj_getState(m, d, state, sig)
    End Sub

    Public Sub mj_setState(m As mjModel, d As mjData, state As Double(), sig As MjStateSig)
        Call MjState.mj_setState(m, d, state, sig)
    End Sub

    ' /********************************************************************************/
    '  名称
    ' /********************************************************************************/

    Public Function mj_name2id(m As mjModel, type As mjtObj, name As String) As Integer
        Return m.FindId(type, name)
    End Function

    Public Function mj_id2name(m As mjModel, type As mjtObj, id As Integer) As String
        Return m.FindName(type, id)
    End Function

    ' /********************************************************************************/
    '  复位
    '/********************************************************************************/

    ''' <summary>复位到默认状态；内部同时清掉暖启动缓存</summary>
    Public Sub mj_resetData(m As mjModel, d As mjData)
        d.time = 0.0
        d.ncon = 0
        d.nefc = 0

        If m.nq > 0 Then
            Call Array.Copy(m.qpos0, d.qpos, m.nq)
        End If

        Call Zero(d.qvel)
        Call Zero(d.qacc)
        Call Zero(d.act)
        Call Zero(d.ctrl)
        Call Zero(d.qfrc_applied)
        Call Zero(d.xfrc_applied)
        Call Zero(d.sensordata)

        For e As Integer = 0 To m.neq - 1
            d.eq_active(e) = m.eq_active0(e)
        Next

        Call d.Engine.SyncToEngine()
        Call d.Engine.InvalidateContacts()
        Call mj_forward(m, d)
    End Sub

    ''' <summary>复位到某个关键帧</summary>
    Public Sub mj_resetDataKeyframe(m As mjModel, d As mjData, key As Integer)
        MjAssert.RequireIndex(key, m.nkey, NameOf(key))

        d.time = m.key_time(key)
        d.ncon = 0
        d.nefc = 0

        If m.nq > 0 Then
            Call Array.Copy(m.key_qpos, key * m.nq, d.qpos, 0, m.nq)
        End If

        If m.nv > 0 Then
            Call Array.Copy(m.key_qvel, key * m.nv, d.qvel, 0, m.nv)
        End If

        If m.nu > 0 Then
            Call Array.Copy(m.key_ctrl, key * m.nu, d.ctrl, 0, m.nu)
        End If

        If m.na > 0 Then
            Call Array.Copy(m.key_act, key * m.na, d.act, 0, m.na)
        End If

        Call Zero(d.qacc)
        Call Zero(d.qfrc_applied)
        Call Zero(d.xfrc_applied)

        For e As Integer = 0 To m.neq - 1
            d.eq_active(e) = m.eq_active0(e)
        Next

        Call d.Engine.SyncToEngine()
        Call d.Engine.InvalidateContacts()
        Call mj_forward(m, d)
    End Sub

    ''' <summary>把当前状态存成一个关键帧</summary>
    Public Sub mj_setKeyframe(m As mjModel, d As mjData, key As Integer)
        MjAssert.RequireIndex(key, m.nkey, NameOf(key))

        m.key_time(key) = d.time

        If m.nq > 0 Then
            Call Array.Copy(d.qpos, 0, m.key_qpos, key * m.nq, m.nq)
        End If

        If m.nv > 0 Then
            Call Array.Copy(d.qvel, 0, m.key_qvel, key * m.nv, m.nv)
        End If

        If m.nu > 0 Then
            Call Array.Copy(d.ctrl, 0, m.key_ctrl, key * m.nu, m.nu)
        End If

        If m.na > 0 Then
            Call Array.Copy(d.act, 0, m.key_act, key * m.na, m.na)
        End If
    End Sub

    ' /********************************************************************************/
    '  内部
    ' /********************************************************************************/

    Private Sub ClampControl(m As mjModel, d As mjData)
        For a As Integer = 0 To m.nu - 1
            If Not m.actuator_ctrllimited(a) Then
                Continue For
            End If

            Dim low As Double = m.actuator_ctrlrange(a * 2 + 0)
            Dim high As Double = m.actuator_ctrlrange(a * 2 + 1)

            If d.ctrl(a) < low Then d.ctrl(a) = low
            If d.ctrl(a) > high Then d.ctrl(a) = high
        Next
    End Sub

    ''' <summary>把引擎检出的流形翻译成 <c>mjContact</c></summary>
    Private Sub CollectContacts(m As mjModel, d As mjData)
        d.ncon = 0

        Dim manifolds As List(Of Microsoft.VisualBasic.Engine.Physics.Collision3D.Manifold3D) = d.Engine.World.Contacts

        For Each manifold In manifolds
            For k As Integer = 0 To manifold.Contacts.Count - 1
                If d.ncon >= mjData.MaxContacts Then
                    d.warning(CInt(mjtWarning.mjWARN_CONTACTFULL)).number += 1

                    Exit For
                End If

                Dim point As Microsoft.VisualBasic.Engine.Physics.Collision3D.ContactPoint3D = manifold.Contacts(k)
                Dim contact As mjContact = d.contact(d.ncon)

                contact.dist = -point.Penetration
                contact.pos(0) = point.Position.x
                contact.pos(1) = point.Position.y
                contact.pos(2) = point.Position.z

                Dim normal As Microsoft.VisualBasic.Engine.Physics.Vector3 = manifold.Normal
                contact.frame(0) = normal.x
                contact.frame(1) = normal.y
                contact.frame(2) = normal.z

                contact.geom1 = RepresentativeGeom(m, BodyIndexOf(m, d, manifold.A))
                contact.geom2 = RepresentativeGeom(m, BodyIndexOf(m, d, manifold.B))
                contact.dim = 3
                contact.efc_address = -1

                d.ncon += 1
            Next
        Next
    End Sub

    Private Function BodyIndexOf(m As mjModel, d As mjData, body As Microsoft.VisualBasic.Engine.Physics.RigidBody3D) As Integer
        If body Is Nothing Then
            Return 0
        End If

        For i As Integer = 1 To m.nbody - 1
            Dim candidate As Microsoft.VisualBasic.Engine.Physics.RigidBody3D = d.Engine.Bodies(i)

            If Object.ReferenceEquals(candidate, body) Then
                Return i
            End If
        Next

        Return 0
    End Function

    ''' <summary>body 的代表 geom：第一个参与碰撞的 geom</summary>
    Private Function RepresentativeGeom(m As mjModel, body As Integer) As Integer
        Dim start As Integer = m.body_geomadr(body)
        Dim count As Integer = m.body_geomnum(body)

        For k As Integer = 0 To count - 1
            If MjCollisionModel.IsCollidable(m, start + k) Then
                Return start + k
            End If
        Next

        Return If(count > 0, start, -1)
    End Function

    ''' <summary>执行器的长度、速度与力</summary>
    Private Sub ComputeActuatorTerms(m As mjModel, d As mjData)
        Call Array.Clear(d.actuator_length, 0, m.nu)
        Call Array.Clear(d.actuator_velocity, 0, m.nu)
        Call Array.Clear(d.actuator_force, 0, m.nu)
        Call Array.Clear(d.qfrc_actuator, 0, m.nv)

        Dim bindings As MjActuatorBinding() = d.Engine.ActuatorBindings

        For a As Integer = 0 To m.nu - 1
            Dim binding As MjActuatorBinding = bindings(a)

            If binding.Joint < 0 Then
                Continue For
            End If

            Dim axis As Microsoft.VisualBasic.Engine.Physics.Vector3
            Dim length As Double
            Dim speed As Double

            If d.Engine.JointState(binding.Joint, axis, length, speed) Then
                d.actuator_length(a) = length
                d.actuator_velocity(a) = speed
            End If

            If binding.Position IsNot Nothing Then
                d.actuator_force(a) = binding.Position.LastTorque
            ElseIf binding.Kind = mjtActuator.mjACTUATOR_MOTOR Then
                d.actuator_force(a) = binding.Gain * d.ctrl(a)
            End If

            If binding.Dof >= 0 AndAlso binding.Dof < m.nv Then
                d.actuator_moment(a * m.nv + binding.Dof) = 1.0
                d.qfrc_actuator(binding.Dof) += d.actuator_force(a)
            End If
        Next

        For i As Integer = 0 To m.nv - 1
            d.qfrc_smooth(i) = d.qfrc_actuator(i) + d.qfrc_passive(i)
        Next
    End Sub

    ''' <summary>NaN / Inf 检测：把警告位置位，不做回滚</summary>
    Private Sub CheckFinite(m As mjModel, d As mjData)
        For i As Integer = 0 To m.nq - 1
            If Double.IsNaN(d.qpos(i)) OrElse Double.IsInfinity(d.qpos(i)) Then
                d.warning(CInt(mjtWarning.mjWARN_BADQPOS)).number += 1
                d.warning(CInt(mjtWarning.mjWARN_BADQPOS)).lastinfo = CInt(d.time * 1000)

                Return
            End If
        Next

        For i As Integer = 0 To m.nv - 1
            If Double.IsNaN(d.qvel(i)) OrElse Double.IsInfinity(d.qvel(i)) Then
                d.warning(CInt(mjtWarning.mjWARN_BADQVEL)).number += 1
                d.warning(CInt(mjtWarning.mjWARN_BADQVEL)).lastinfo = CInt(d.time * 1000)

                Return
            End If
        Next
    End Sub

    Private Sub Zero(items As Double())
        If items IsNot Nothing Then
            Call Array.Clear(items, 0, items.Length)
        End If
    End Sub

End Module
