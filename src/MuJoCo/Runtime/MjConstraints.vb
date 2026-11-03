Imports std = System.Math
Imports EngVec = Microsoft.VisualBasic.Engine.Physics.Vector3
Imports EngQuat = Microsoft.VisualBasic.Engine.Physics.Math3D.Quaternion
Imports EngMat3 = Microsoft.VisualBasic.Engine.Physics.Math3D.Matrix3x3
Imports EngMath = Microsoft.VisualBasic.Engine.Physics.Math3D.Vector3Math
Imports Joints3D = Microsoft.VisualBasic.Engine.Physics.Joints3D

''' <summary>
''' 焊接约束：把两个刚体在给定锚点处刚性连接（3 个平移行 + 3 个转动行）。
''' </summary>
''' <remarks>
''' 计算内核没有 3D 的固定关节（<c>Joints\WeldJoint.vb</c> 只有 2D 版本），
''' 而 MuJoCo 里"没有关节的 body"是刚性焊在父体上的，所以必须补一个。
''' 写法照抄内核里 <c>HingeJoint3D.SolveAngularAxis</c> 的标量行：
''' 每个轴独立解 <c>j = (target − v_rel)/k</c>，`k` 是沿着该轴的有效质量
''' （含两端的力臂项）。
''' </remarks>
Public Class MjWeldConstraint
    Implements Joints3D.IConstraint3D

    Public Property A As Microsoft.VisualBasic.Engine.Physics.RigidBody3D
    Public Property B As Microsoft.VisualBasic.Engine.Physics.RigidBody3D

    ''' <summary>A 局部系的锚点（相对质心）</summary>
    Public Property AnchorA As EngVec = New EngVec(0, 0, 0)
    ''' <summary>B 局部系的锚点（相对质心）</summary>
    Public Property AnchorB As EngVec = New EngVec(0, 0, 0)

    ''' <summary>位置误差的收敛强度</summary>
    Public Property Baumgarte As Double = 0.1
    ''' <summary>姿态误差的收敛强度</summary>
    Public Property AngularCorrection As Double = 0.1
    ''' <summary>修正速度的上限，rad/s 与 m/s</summary>
    Public Property BiasLimit As Double = 0.5

    ''' <summary>关节名，供调试</summary>
    Public Property Label As String

    Private ReadOnly m_reference As EngQuat

    Public Sub New(a As Microsoft.VisualBasic.Engine.Physics.RigidBody3D,
                   b As Microsoft.VisualBasic.Engine.Physics.RigidBody3D,
                   anchorA As EngVec, anchorB As EngVec)
        Me.A = a
        Me.B = b
        Me.AnchorA = anchorA
        Me.AnchorB = anchorB

        m_reference = EngQuat.Compose(a.Orientation.Inverse(), b.Orientation)
    End Sub

    Public Sub SolveVelocity(dt As Double) Implements Joints3D.IConstraint3D.SolveVelocity
        If A Is Nothing OrElse B Is Nothing OrElse dt <= 0 Then
            Return
        End If

        Dim rA As EngVec = A.Orientation.Rotate(AnchorA)
        Dim rB As EngVec = B.Orientation.Rotate(AnchorB)

        ' 平移行（error 是 VB 的保留语句，这里用 deviation）
        Dim deviation As EngVec = (B.Position + rB) - (A.Position + rA)
        Dim bias As EngVec = deviation * (Baumgarte / dt)

        Call SolveLinearRow(rA, rB, New EngVec(1, 0, 0), bias.x, dt)
        Call SolveLinearRow(rA, rB, New EngVec(0, 1, 0), bias.y, dt)
        Call SolveLinearRow(rA, rB, New EngVec(0, 0, 1), bias.z, dt)

        ' 转动行
        Dim relative As EngQuat = EngQuat.Compose(A.Orientation.Inverse(), B.Orientation)
        Dim delta As EngQuat = EngQuat.Compose(m_reference.Inverse(), relative)

        If delta.w < 0 Then
            delta = New EngQuat(-delta.w, -delta.x, -delta.y, -delta.z)
        End If

        Dim sine As Double = std.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z)
        Dim angularBias As EngVec

        If sine < 1.0E-12 Then
            angularBias = New EngVec(0, 0, 0)
        Else
            Dim angle As Double = 2.0 * std.Atan2(sine, delta.w)

            angularBias = New EngVec(delta.x / sine * angle, delta.y / sine * angle, delta.z / sine * angle) *
                          (AngularCorrection / dt)
        End If

        Call SolveAngularRow(New EngVec(1, 0, 0), angularBias.x, dt)
        Call SolveAngularRow(New EngVec(0, 1, 0), angularBias.y, dt)
        Call SolveAngularRow(New EngVec(0, 0, 1), angularBias.z, dt)
    End Sub

    Public Sub SolvePosition(dt As Double) Implements Joints3D.IConstraint3D.SolvePosition
        ' 位置层不做"传送式"修正：那会只改位置不改速度，等于给关节加大阻尼
    End Sub

    Private Sub SolveLinearRow(rA As EngVec, rB As EngVec, axis As EngVec, bias As Double, dt As Double)
        Dim raxn As EngVec = EngMath.Cross(rA, axis)
        Dim rbxn As EngVec = EngMath.Cross(rB, axis)

        Dim k As Double = A.InvMass + B.InvMass +
                          EngMath.Dot(axis, EngMath.Cross(A.InvInertiaWorld().MultiplyLeft(raxn), rA)) +
                          EngMath.Dot(axis, EngMath.Cross(B.InvInertiaWorld().MultiplyLeft(rbxn), rB))

        If k < 1.0E-12 Then
            Return
        End If

        Dim relative As EngVec = (B.Velocity + EngMath.Cross(B.AngularVelocity, rB)) -
                                 (A.Velocity + EngMath.Cross(A.AngularVelocity, rA))
        Dim speed As Double = EngMath.Dot(relative, axis)
        Dim target As Double = -ClampBias(bias)
        Dim j As Double = (target - speed) / k

        Dim impulse As EngVec = axis * j

        A.Velocity = A.Velocity - impulse * A.InvMass
        A.AngularVelocity = A.AngularVelocity - A.InvInertiaWorld().MultiplyLeft(EngMath.Cross(rA, impulse))
        B.Velocity = B.Velocity + impulse * B.InvMass
        B.AngularVelocity = B.AngularVelocity + B.InvInertiaWorld().MultiplyLeft(EngMath.Cross(rB, impulse))
    End Sub

    Private Sub SolveAngularRow(axis As EngVec, bias As Double, dt As Double)
        Dim k As Double = EngMath.Dot(axis, A.InvInertiaWorld().MultiplyLeft(axis)) +
                          EngMath.Dot(axis, B.InvInertiaWorld().MultiplyLeft(axis))

        If k < 1.0E-12 Then
            Return
        End If

        Dim relative As EngVec = B.AngularVelocity - A.AngularVelocity
        Dim speed As Double = EngMath.Dot(relative, axis)
        Dim j As Double = (-ClampBias(bias) - speed) / k
        Dim impulse As EngVec = axis * j

        A.AngularVelocity = A.AngularVelocity - A.InvInertiaWorld().MultiplyLeft(impulse)
        B.AngularVelocity = B.AngularVelocity + B.InvInertiaWorld().MultiplyLeft(impulse)
    End Sub

    Private Function ClampBias(value As Double) As Double
        If BiasLimit <= 0 Then
            Return value
        End If

        Return std.Min(BiasLimit, std.Max(-BiasLimit, value))
    End Function

End Class

''' <summary>
''' 位置伺服约束：把 MJCF 的 <c>position</c> 执行器写进求解器。
''' </summary>
''' <remarks>
''' <para>
''' 力律与 MuJoCo 的仿射执行器一致：<c>force = kp·(target − length) − kv·speed</c>，
''' 并被 <c>forcerange</c> 截断。写成一个"软约束"而不是每步施加一次力矩，
''' 是因为内核的求解器会把接触、关节、伺服放进同一个迭代里重新分配；
''' 直接写力矩在 ±0.96 N·m 的量级上是稳定的，但会漏掉隐式积分带来的数值阻尼，
''' 表现为高速时抖动。
''' </para>
''' <para>
''' 软化项照抄 Box2D 的推导（内核的 <c>ActuatorConstraint3D</c> 用的是同一套代数）：
''' <c>gamma = 1/(dt·(kv + dt·kp))</c>、<c>mass = 1/(1/k_eff + gamma)</c>。
''' 它对任意增益与步长都稳定。
''' </para>
''' </remarks>
Public Class MjPositionActuatorConstraint
    Implements Joints3D.IConstraint3D

    ''' <summary>承担反作用力的刚体</summary>
    Public Property A As Microsoft.VisualBasic.Engine.Physics.RigidBody3D
    ''' <summary>被驱动的刚体</summary>
    Public Property B As Microsoft.VisualBasic.Engine.Physics.RigidBody3D

    ''' <summary>世界系的作用轴（角速度轴或平移方向）</summary>
    Public Property Axis As EngVec = New EngVec(0, 0, 1)
    ''' <summary>当前的关节坐标</summary>
    Public Property Length As Double = 0.0
    ''' <summary>目标关节坐标</summary>
    Public Property Target As Double = 0.0

    ''' <summary>是否为平移自由度</summary>
    Public Property Linear As Boolean = False

    Public Property Kp As Double = 0.0
    Public Property Kv As Double = 0.0
    Public Property ForceMin As Double = -1000000000.0
    Public Property ForceMax As Double = 1000000000.0

    ''' <summary>本子步已经施加的冲量</summary>
    Public Property Accumulated As Double = 0.0

    Public ReadOnly Property LastTorque As Double
        Get
            Return m_lastTorque
        End Get
    End Property

    Private m_lastTorque As Double = 0.0

    Public Sub Reset()
        Accumulated = 0.0
    End Sub

    Public Sub SolveVelocity(dt As Double) Implements Joints3D.IConstraint3D.SolveVelocity
        If A Is Nothing OrElse B Is Nothing OrElse dt <= 0 OrElse Kp <= 0 Then
            Return
        End If

        Dim compliance As Double

        If Linear Then
            compliance = A.InvMass + B.InvMass
        Else
            compliance = EngMath.Dot(Axis, A.InvInertiaWorld().MultiplyLeft(Axis)) +
                         EngMath.Dot(Axis, B.InvInertiaWorld().MultiplyLeft(Axis))
        End If

        If compliance < 1.0E-12 Then
            Return
        End If

        Dim gamma As Double = 1.0 / (dt * (Kv + dt * Kp))
        Dim effectiveMass As Double = 1.0 / (compliance + gamma)
        Dim bias As Double = (Length - Target) * dt * Kp * gamma

        Dim speed As Double

        If Linear Then
            speed = EngMath.Dot(B.Velocity - A.Velocity, Axis)
        Else
            speed = EngMath.Dot(B.AngularVelocity - A.AngularVelocity, Axis)
        End If

        Dim delta As Double = -effectiveMass * (speed + bias + gamma * Accumulated)
        Dim previous As Double = Accumulated

        Accumulated = std.Min(std.Max(previous + delta, ForceMin * dt), ForceMax * dt)
        delta = Accumulated - previous

        m_lastTorque = Accumulated / dt

        If delta = 0 Then
            Return
        End If

        If Linear Then
            Dim impulse As EngVec = Axis * delta

            A.Velocity = A.Velocity - impulse * A.InvMass
            B.Velocity = B.Velocity + impulse * B.InvMass
        Else
            A.AngularVelocity = A.AngularVelocity - A.InvInertiaWorld().MultiplyLeft(Axis * delta)
            B.AngularVelocity = B.AngularVelocity + B.InvInertiaWorld().MultiplyLeft(Axis * delta)
        End If
    End Sub

    Public Sub SolvePosition(dt As Double) Implements Joints3D.IConstraint3D.SolvePosition
        ' 伺服没有位置层：速度层把误差推到力平衡允许的位置，与参考执行器一致
    End Sub

End Class

''' <summary>
''' 单行等式约束（<c>distance</c> / <c>joint</c> 的近似），沿一个给定轴把两点拉齐。
''' </summary>
Public Class MjRowConstraint
    Implements Joints3D.IConstraint3D

    Public Property A As Microsoft.VisualBasic.Engine.Physics.RigidBody3D
    Public Property B As Microsoft.VisualBasic.Engine.Physics.RigidBody3D
    Public Property AnchorA As EngVec = New EngVec(0, 0, 0)
    Public Property AnchorB As EngVec = New EngVec(0, 0, 0)
    ''' <summary>A 局部系的约束轴</summary>
    Public Property LocalAxis As EngVec = New EngVec(0, 0, 1)
    ''' <summary>期望的点间距离（沿约束轴）</summary>
    Public Property TargetDistance As Double = 0.0

    Public Property Baumgarte As Double = 0.2
    Public Property BiasLimit As Double = 1.0

    Public Sub SolveVelocity(dt As Double) Implements Joints3D.IConstraint3D.SolveVelocity
        If A Is Nothing OrElse B Is Nothing OrElse dt <= 0 Then
            Return
        End If

        Dim rA As EngVec = A.Orientation.Rotate(AnchorA)
        Dim rB As EngVec = B.Orientation.Rotate(AnchorB)
        Dim axis As EngVec = EngMath.Normalize(A.Orientation.Rotate(LocalAxis))
        Dim raxn As EngVec = EngMath.Cross(rA, axis)
        Dim rbxn As EngVec = EngMath.Cross(rB, axis)

        Dim k As Double = A.InvMass + B.InvMass +
                          EngMath.Dot(axis, EngMath.Cross(A.InvInertiaWorld().MultiplyLeft(raxn), rA)) +
                          EngMath.Dot(axis, EngMath.Cross(B.InvInertiaWorld().MultiplyLeft(rbxn), rB))

        If k < 1.0E-12 Then
            Return
        End If

        Dim deviation As Double = EngMath.Dot((B.Position + rB) - (A.Position + rA), axis) - TargetDistance
        Dim bias As Double = std.Min(BiasLimit, std.Max(-BiasLimit, Baumgarte / dt * deviation))

        Dim relative As EngVec = (B.Velocity + EngMath.Cross(B.AngularVelocity, rB)) -
                                 (A.Velocity + EngMath.Cross(A.AngularVelocity, rA))
        Dim j As Double = (-bias - EngMath.Dot(relative, axis)) / k
        Dim impulse As EngVec = axis * j

        A.Velocity = A.Velocity - impulse * A.InvMass
        A.AngularVelocity = A.AngularVelocity - A.InvInertiaWorld().MultiplyLeft(EngMath.Cross(rA, impulse))
        B.Velocity = B.Velocity + impulse * B.InvMass
        B.AngularVelocity = B.AngularVelocity + B.InvInertiaWorld().MultiplyLeft(EngMath.Cross(rB, impulse))
    End Sub

    Public Sub SolvePosition(dt As Double) Implements Joints3D.IConstraint3D.SolvePosition
    End Sub

End Class

''' <summary>速度伺服约束：把 MJCF 的 <c>velocity</c> 执行器写进求解器</summary>
Public Class MjVelocityActuatorConstraint
    Implements Joints3D.IConstraint3D

    Public Property A As Microsoft.VisualBasic.Engine.Physics.RigidBody3D
    Public Property B As Microsoft.VisualBasic.Engine.Physics.RigidBody3D
    Public Property Axis As EngVec = New EngVec(0, 0, 1)
    ''' <summary>目标速度，rad/s 或 m/s</summary>
    Public Property Target As Double = 0.0
    ''' <summary>力的上限，用来限制单个子步能施加的冲量</summary>
    Public Property ForceMax As Double = 1000000000.0
    Public Property ForceMin As Double = -1000000000.0
    Public Property Linear As Boolean = False

    Public Property Accumulated As Double = 0.0

    Public Sub Reset()
        Accumulated = 0.0
    End Sub

    Public Sub SolveVelocity(dt As Double) Implements Joints3D.IConstraint3D.SolveVelocity
        If A Is Nothing OrElse B Is Nothing OrElse dt <= 0 Then
            Return
        End If

        Dim compliance As Double

        If Linear Then
            compliance = A.InvMass + B.InvMass
        Else
            compliance = EngMath.Dot(Axis, A.InvInertiaWorld().MultiplyLeft(Axis)) +
                         EngMath.Dot(Axis, B.InvInertiaWorld().MultiplyLeft(Axis))
        End If

        If compliance < 1.0E-12 Then
            Return
        End If

        Dim speed As Double

        If Linear Then
            speed = EngMath.Dot(B.Velocity - A.Velocity, Axis)
        Else
            speed = EngMath.Dot(B.AngularVelocity - A.AngularVelocity, Axis)
        End If

        Dim delta As Double = (Target - speed) / compliance
        Dim previous As Double = Accumulated

        Accumulated = std.Min(std.Max(previous + delta, ForceMin * dt), ForceMax * dt)
        delta = Accumulated - previous

        If delta = 0 Then
            Return
        End If

        If Linear Then
            Dim impulse As EngVec = Axis * delta

            A.Velocity = A.Velocity - impulse * A.InvMass
            B.Velocity = B.Velocity + impulse * B.InvMass
        Else
            A.AngularVelocity = A.AngularVelocity - A.InvInertiaWorld().MultiplyLeft(Axis * delta)
            B.AngularVelocity = B.AngularVelocity + B.InvInertiaWorld().MultiplyLeft(Axis * delta)
        End If
    End Sub

    Public Sub SolvePosition(dt As Double) Implements Joints3D.IConstraint3D.SolvePosition
    End Sub

End Class
