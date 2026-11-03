Imports std = System.Math
Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports EngVec = Microsoft.VisualBasic.Engine.Physics.Vector3
Imports EngMath = Microsoft.VisualBasic.Engine.Physics.Math3D.Vector3Math
Imports Joints3D = Microsoft.VisualBasic.Engine.Physics.Joints3D

''' <summary>
''' 一个执行器在求解器里的绑定：语义类型、目标关节、力律参数。
''' </summary>
Public Class MjActuatorBinding

    Public Property Kind As mjtActuator = mjtActuator.mjACTUATOR_GENERAL
    Public Property TrnType As mjtTrn = mjtTrn.mjTRN_JOINT

    ''' <summary>目标关节 id，-1 表示不是关节传动</summary>
    Public Property Joint As Integer = -1
    ''' <summary>目标自由度 id，-1 表示没有自由度</summary>
    Public Property Dof As Integer = -1
    ''' <summary>是否为平移自由度</summary>
    Public Property Linear As Boolean = False

    ''' <summary><c>gainprm[0]</c>：力律的增益</summary>
    Public Property Gain As Double = 0.0
    ''' <summary><c>biasprm[2]</c>：速度偏置</summary>
    Public Property BiasVelocity As Double = 0.0

    Public Property ForceMin As Double = -1000000000.0
    Public Property ForceMax As Double = 1000000000.0

    ''' <summary>位置伺服的约束对象（仅 <c>position</c> 执行器）</summary>
    Public Property Position As MjPositionActuatorConstraint = Nothing
    ''' <summary>速度伺服的约束对象（仅 <c>velocity</c> / <c>damper</c> 执行器）</summary>
    Public Property Velocity As MjVelocityActuatorConstraint = Nothing

End Class

''' <summary>
''' 每子步把执行器与被动力写进引擎的钩子。
''' </summary>
''' <remarks>
''' <para>
''' 计算内核在 <c>StepSub</c> 里先清力、再调钩子、然后加重力、最后积分速度，
''' 所以钩子里施加的力矩与重力进入同一个加速度方程——这正是 MuJoCo 把
''' <c>qfrc_actuator</c> 与 <c>qfrc_bias</c> 加在一起再求解的结构。
''' </para>
''' <para>
''' 分工：
''' </para>
''' <list type="bullet">
''' <item><description>
''' <c>position</c> 执行器是求解器里的软约束（<see cref="MjPositionActuatorConstraint"/>），
''' 这里只负责每子步刷新它的当前坐标、作用轴与目标值；
''' </description></item>
''' <item><description>
''' <c>velocity</c>/<c>damper</c> 同理，交给 <see cref="MjVelocityActuatorConstraint"/>；
''' </description></item>
''' <item><description>
''' <c>motor</c> 是纯力矩，直接沿关节轴施加给两端刚体；
''' </description></item>
''' <item><description>
''' 关节干摩擦（<c>dof_frictionloss</c>）与弹簧（<c>jnt_stiffness</c>）作为被动力施加；
''' 黏性阻尼不在这里做，它被写成铰链约束的隐式衰减（<c>RevoluteJoint3D.Damping</c>），
''' 那样在小惯量连杆上才不会震荡。
''' </description></item>
''' </list>
''' </remarks>
Public Class MjActuatorHook
    Implements Eng.IStepHook

    Friend Model As mjModel
    Friend Data As mjData
    Friend Bridge As MjEngineBridge

    ''' <summary>关掉之后执行器与被动力都不再施加（用于"冻结"的调试工况）</summary>
    Public Property Enabled As Boolean = True
    ''' <summary>干摩擦的死区，m/s 或 rad/s</summary>
    Public Property FrictionDeadZone As Double = 1.0E-4
    ''' <summary>干摩擦的缩放</summary>
    Public Property FrictionScale As Double = 1.0
    ''' <summary>关节行程修正的最大速度，rad/s 或 m/s</summary>
    Public Property BiasSpeedLimit As Double = 0.5

    Public Sub BeforeSubstep(dt As Double) Implements Eng.IStepHook.BeforeSubstep
        If Not Enabled OrElse Model Is Nothing OrElse Bridge Is Nothing Then
            Return
        End If

        Dim bindings As MjActuatorBinding() = Bridge.ActuatorBindings

        For a As Integer = 0 To bindings.Length - 1
            Dim binding As MjActuatorBinding = bindings(a)

            If binding.Joint < 0 Then
                Continue For
            End If

            Dim control As Double = If(a < Data.ctrl.Length, Data.ctrl(a), 0.0)
            Dim axis As EngVec
            Dim length As Double
            Dim speed As Double

            If Not Bridge.JointState(binding.Joint, axis, length, speed) Then
                Continue For
            End If

            Select Case binding.Kind
                Case mjtActuator.mjACTUATOR_POSITION
                    Dim servo As MjPositionActuatorConstraint = binding.Position

                    If servo Is Nothing Then
                        Continue For
                    End If

                    servo.Axis = axis
                    servo.Length = length
                    servo.Target = control
                    servo.Reset()

                Case mjtActuator.mjACTUATOR_VELOCITY, mjtActuator.mjACTUATOR_DAMPER
                    Dim servo As MjVelocityActuatorConstraint = binding.Velocity

                    If servo Is Nothing Then
                        Continue For
                    End If

                    servo.Axis = axis
                    servo.Target = If(binding.Kind = mjtActuator.mjACTUATOR_DAMPER, 0.0, control)
                    servo.Reset()

                Case Else
                    ' motor / general：force = gain·ctrl
                    Dim force As Double = binding.Gain * control

                    If std.Abs(force) > 1.0E-12 Then
                        Call Bridge.ApplyJointForce(binding.Joint, force)
                    End If
            End Select
        Next

        Call ApplyPassive(dt)
    End Sub

    ''' <summary>干摩擦、关节弹簧与关节行程</summary>
    Private Sub ApplyPassive(dt As Double)
        For j As Integer = 0 To Model.njnt - 1
            Dim dof As Integer = Model.jnt_dofadr(j)

            If dof < 0 OrElse dof >= Model.nv Then
                Continue For
            End If

            Dim friction As Double = Model.dof_frictionloss(dof)
            Dim stiffness As Double = Model.jnt_stiffness(j)
            Dim limited As Boolean = Model.jnt_limited(j)

            If friction <= 0 AndAlso stiffness <= 0 AndAlso Not limited Then
                Continue For
            End If

            Dim axis As EngVec = Nothing
            Dim length As Double = 0.0
            Dim speed As Double = 0.0

            If Not Bridge.JointState(j, axis, length, speed) Then
                Continue For
            End If

            Dim torque As Double = 0.0

            If stiffness > 0 Then
                Dim reference As Double = Model.qpos_spring(Model.jnt_qposadr(j))
                torque -= stiffness * (length - reference)
            End If

            If friction > 0 AndAlso std.Abs(speed) > FrictionDeadZone Then
                torque -= std.Sign(speed) * friction * FrictionScale
            End If

            If limited Then
                torque += LimitTorque(j, dof, length, speed, dt)
            End If

            If std.Abs(torque) > 1.0E-12 Then
                Call Bridge.ApplyJointForce(j, torque)
            End If
        Next
    End Sub

    ''' <summary>
    ''' 关节行程的速度级约束：越界时把沿轴的速度推向"回到界内"的方向。
    ''' </summary>
    ''' <remarks>
    ''' 内核 <c>RevoluteJoint3D</c> 的限位是用它自己的 <c>Position</c> 判据做的，
    ''' 而那个读数在父子固定旋转非恒等时是错的（见 <c>MjEngineBridge.MeasureJoint</c>），
    ''' 所以限位在桥接层关闭、由这里用反算出来的坐标重新实现。
    ''' 冲量按质量矩阵的对角元估计，等效于一层不带耦合的单行 PGS。
    ''' </remarks>
    Private Function LimitTorque(joint As Integer, dof As Integer,
                                 length As Double, speed As Double, dt As Double) As Double
        If dt <= 0 Then
            Return 0.0
        End If

        Dim lower As Double = Model.jnt_range(joint * 2)
        Dim upper As Double = Model.jnt_range(joint * 2 + 1)

        If lower >= upper Then
            Return 0.0
        End If

        Dim target As Double

        If length < lower Then
            target = (lower - length) / dt
        ElseIf length > upper Then
            target = (upper - length) / dt
        Else
            Return 0.0
        End If

        ' 修正速度不超过 BiasSpeedLimit，避免深穿透时炸开
        target = std.Min(BiasSpeedLimit, std.Max(-BiasSpeedLimit, target))

        ' 只做单向推：已经在往界内走就不再加力
        If (target > 0 AndAlso speed >= target) OrElse (target < 0 AndAlso speed <= target) Then
            Return 0.0
        End If

        Dim effective As Double = EffectiveInertia(dof)

        Return (target - speed) * effective / dt
    End Function

    ''' <summary>质量矩阵的对应对角元，取不到时退回 1</summary>
    Private Function EffectiveInertia(dof As Integer) As Double
        Dim nv As Integer = Model.nv

        If Data Is Nothing OrElse Data.M Is Nothing OrElse nv <= 0 Then
            Return 1.0
        End If

        Dim index As Integer = dof * nv + dof

        If index < 0 OrElse index >= Data.M.Length Then
            Return 1.0
        End If

        Dim value As Double = Data.M(index)

        Return If(value > 0, value, 1.0)
    End Function

End Class
