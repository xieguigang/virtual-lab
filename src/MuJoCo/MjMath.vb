Imports System.Numerics
Imports EngVec = Microsoft.VisualBasic.Engine.Physics.Vector3
Imports EngQuat = Microsoft.VisualBasic.Engine.Physics.Math3D.Quaternion
Imports EngMat3 = Microsoft.VisualBasic.Engine.Physics.Math3D.Matrix3x3
Imports EngMath = Microsoft.VisualBasic.Engine.Physics.Math3D.Vector3Math

''' <summary>
''' 全库唯一的数学边界：<see cref="System.Numerics"/> 与计算内核
''' <c>Microsoft.VisualBasic.Engine.Physics.Math3D</c> 之间的转换，以及
''' MuJoCo 的数组式布局（<c>Double()</c> 平铺）与四元数/矩阵之间的换算。
''' </summary>
''' <remarks>
''' <para>
''' 必须注意两处约定差异：
''' </para>
''' <list type="bullet">
''' <item><description>
''' 内核的 <c>Vector3</c> 是 <b>引用类型</b>，而 <see cref="System.Numerics.Vector3"/> 是值类型。
''' 库对外只暴露 <see cref="System.Numerics"/> 的类型，转换全部集中在这里，
''' 避免调用方拿到一个可以原地修改的共享向量。
''' </description></item>
''' <item><description>
''' MuJoCo 的四元数是 <c>(w, x, y, z)</c> 顺序的四个浮点数，
''' <see cref="System.Numerics.Quaternion"/> 的构造函数是 <c>(x, y, z, w)</c>。
''' 所有读写都经过本模块的 <see cref="Quat"/> / <see cref="WriteQuat"/>。
''' </description></item>
''' </list>
''' </remarks>
Public Module MjMath

    ' /********************************************************************************/
    '  内核互转
    ' /********************************************************************************/

    Public Function Vec(x As Double, y As Double, z As Double) As EngVec
        Return New EngVec(x, y, z)
    End Function

    Public Function Vec(v As Vector3) As EngVec
        Return New EngVec(v.X, v.Y, v.Z)
    End Function

    Public Function Vec(v As EngVec) As Vector3
        If v Is Nothing Then
            Return Vector3.Zero
        End If

        Return New Vector3(CSng(v.x), CSng(v.y), CSng(v.z))
    End Function

    ''' <summary>把 <c>Double()</c> 的 xyz 三元组转成内核向量</summary>
    Public Function Vec(src As Double(), offset As Integer) As EngVec
        Return New EngVec(src(offset), src(offset + 1), src(offset + 2))
    End Function

    ''' <summary>托管四元数 → 内核四元数</summary>
    Public Function ToEngine(q As Quaternion) As EngQuat
        Return New EngQuat(q.W, q.X, q.Y, q.Z)
    End Function

    Public Function QuatOf(q As EngQuat) As Quaternion
        Return New Quaternion(CSng(q.x), CSng(q.y), CSng(q.z), CSng(q.w))
    End Function

    ''' <summary>
    ''' 由 MJCF 的 <c>fullinertia="ixx iyy izz ixy ixz iyz"</c> 构造 3×3 张量
    ''' </summary>
    Public Function Tensor(full As Double()) As EngMat3
        If full Is Nothing OrElse full.Length < 6 Then
            Return EngMat3.Zero
        End If

        Return New EngMat3(
            full(0), full(3), full(4),
            full(3), full(1), full(5),
            full(4), full(5), full(2))
    End Function

    ''' <summary>
    ''' 把转子惯量（MJCF 的 <c>armature</c>）沿关节轴叠加进张量。
    ''' </summary>
    ''' <remarks>
    ''' 内核没有独立的 armature 字段，只能改 <c>RigidBody3D.InertiaLocal</c>。
    ''' 叠加项是轴的二项积 <c>a ⊗ a · armature</c>：一个六克的连杆自身的张量是
    ''' 1e-6 量级，而 armature 是 1.8e-3，差三个数量级；没有它，0.06 N·m 的
    ''' 伺服力矩会在一个子步内把连杆转到上千 rad/s，铰链约束直接炸开。
    ''' </remarks>
    Public Function WithArmature(tensor As EngMat3, axis As Vector3, armature As Double) As EngMat3
        If armature <= 0 Then
            Return tensor
        End If

        Dim a As Vector3 = SafeNormalize(axis)

        Return New EngMat3(
            tensor.m11 + armature * a.X * a.X, tensor.m12 + armature * a.X * a.Y, tensor.m13 + armature * a.X * a.Z,
            tensor.m21 + armature * a.Y * a.X, tensor.m22 + armature * a.Y * a.Y, tensor.m23 + armature * a.Y * a.Z,
            tensor.m31 + armature * a.Z * a.X, tensor.m32 + armature * a.Z * a.Y, tensor.m33 + armature * a.Z * a.Z)
    End Function

    ' /********************************************************************************/
    '  四元数
    ' /********************************************************************************/

    ''' <summary>读一个 MJCF 四元数（w, x, y, z）</summary>
    Public Function Quat(src As Double(), offset As Integer) As Quaternion
        Return New Quaternion(CSng(src(offset + 1)), CSng(src(offset + 2)),
                              CSng(src(offset + 3)), CSng(src(offset)))
    End Function

    ''' <summary>写一个 MJCF 四元数（w, x, y, z）</summary>
    Public Sub WriteQuat(dst As Double(), offset As Integer, q As Quaternion)
        dst(offset) = q.W
        dst(offset + 1) = q.X
        dst(offset + 2) = q.Y
        dst(offset + 3) = q.Z
    End Sub

    ''' <summary>Hamilton 乘积，满足 <c>R(a⊗b) = R(a)·R(b)</c></summary>
    Public Function Mul(a As Quaternion, b As Quaternion) As Quaternion
        Return New Quaternion(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z)
    End Function

    Public Function Conj(q As Quaternion) As Quaternion
        Return New Quaternion(-q.X, -q.Y, -q.Z, q.W)
    End Function

    ''' <summary>绕任意轴旋转 <paramref name="angle"/> 弧度</summary>
    Public Function AxisAngle(axis As Vector3, angle As Double) As Quaternion
        Dim n As Vector3 = SafeNormalize(axis)

        If Math.Abs(angle) < 1.0E-14 OrElse n = Vector3.Zero Then
            Return Quaternion.Identity
        End If

        Return Quaternion.CreateFromAxisAngle(n, CSng(angle))
    End Function

    Public Function Rotate(q As Quaternion, v As Vector3) As Vector3
        Return Vector3.Transform(v, q)
    End Function

    Public Function Unrotate(q As Quaternion, v As Vector3) As Vector3
        Return Vector3.Transform(v, Conj(q))
    End Function

    ''' <summary>按局部角速度积分四元数：<c>q ← q ⊗ exp(ω·dt/2)</c></summary>
    Public Function Integrate(q As Quaternion, omegaLocal As Vector3, dt As Double) As Quaternion
        Dim speed As Double = omegaLocal.Length()

        If speed < 1.0E-14 OrElse dt <= 0 Then
            Return q
        End If

        Dim half As Double = 0.5 * speed * dt
        Dim scale As Double = Math.Sin(half) / speed

        Dim delta As New Quaternion(omegaLocal.X * scale, omegaLocal.Y * scale,
                                    omegaLocal.Z * scale, Math.Cos(half))

        Return Normalize(Mul(q, delta))
    End Function

    Public Function Normalize(q As Quaternion) As Quaternion
        Dim len As Double = Math.Sqrt(q.W * q.W + q.X * q.X + q.Y * q.Y + q.Z * q.Z)

        If len < 1.0E-12 Then
            Return Quaternion.Identity
        End If

        Return New Quaternion(q.X / len, q.Y / len, q.Z / len, q.W / len)
    End Function

    ''' <summary>两个四元数之间"最短"的旋转向量（世界系），用于位置修正</summary>
    Public Function RotationError(target As Quaternion, current As Quaternion) As Vector3
        Dim delta As Quaternion = Mul(target, Conj(current))

        If delta.W < 0 Then
            delta = New Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W)
        End If

        Dim sine As Double = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z)

        If sine < 1.0E-12 Then
            Return Vector3.Zero
        End If

        Dim angle As Double = 2.0 * Math.Atan2(sine, delta.W)

        Return New Vector3(CSng(delta.X / sine * angle), CSng(delta.Y / sine * angle), CSng(delta.Z / sine * angle))
    End Function

    ' /********************************************************************************/
    '  3×3 旋转矩阵（MuJoCo 的 xmat 布局：行主序，global = M · local）
    ' /********************************************************************************/

    ''' <summary>四元数 → 9 个数的行主序旋转矩阵</summary>
    Public Sub Mat9(dst As Double(), offset As Integer, q As Quaternion)
        Dim x As Double = q.X, y As Double = q.Y, z As Double = q.Z, w As Double = q.W
        Dim xx As Double = x * x, yy As Double = y * y, zz As Double = z * z
        Dim xy As Double = x * y, xz As Double = x * z, yz As Double = y * z
        Dim wx As Double = w * x, wy As Double = w * y, wz As Double = w * z

        dst(offset + 0) = 1.0 - 2.0 * (yy + zz)
        dst(offset + 1) = 2.0 * (xy - wz)
        dst(offset + 2) = 2.0 * (xz + wy)

        dst(offset + 3) = 2.0 * (xy + wz)
        dst(offset + 4) = 1.0 - 2.0 * (xx + zz)
        dst(offset + 5) = 2.0 * (yz - wx)

        dst(offset + 6) = 2.0 * (xz - wy)
        dst(offset + 7) = 2.0 * (yz + wx)
        dst(offset + 8) = 1.0 - 2.0 * (xx + yy)
    End Sub

    ''' <summary>行主序 3×3 矩阵 → 四元数</summary>
    Public Function QuatFromMat9(src As Double(), offset As Integer) As Quaternion
        Dim m00 As Double = src(offset + 0), m01 As Double = src(offset + 1), m02 As Double = src(offset + 2)
        Dim m10 As Double = src(offset + 3), m11 As Double = src(offset + 4), m12 As Double = src(offset + 5)
        Dim m20 As Double = src(offset + 6), m21 As Double = src(offset + 7), m22 As Double = src(offset + 8)

        Dim trace As Double = m00 + m11 + m22
        Dim q As Quaternion

        If trace > 0 Then
            Dim s As Double = Math.Sqrt(trace + 1.0) * 2.0

            q = New Quaternion(CSng((m21 - m12) / s), CSng((m02 - m20) / s), CSng((m10 - m01) / s), CSng(0.25 * s))
        ElseIf m00 > m11 AndAlso m00 > m22 Then
            Dim s As Double = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0

            q = New Quaternion(CSng(0.25 * s), CSng((m01 + m10) / s), CSng((m02 + m20) / s), CSng((m21 - m12) / s))
        ElseIf m11 > m22 Then
            Dim s As Double = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0

            q = New Quaternion(CSng((m01 + m10) / s), CSng(0.25 * s), CSng((m12 + m21) / s), CSng((m02 - m20) / s))
        Else
            Dim s As Double = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0

            q = New Quaternion(CSng((m02 + m20) / s), CSng((m12 + m21) / s), CSng(0.25 * s), CSng((m10 - m01) / s))
        End If

        Return Normalize(q)
    End Function

    ''' <summary>行主序矩阵与向量相乘：<c>dst = M · v</c></summary>
    Public Function MatVec(m As Double(), offset As Integer, v As Vector3) As Vector3
        Return New Vector3(
            CSng(m(offset + 0) * v.X + m(offset + 1) * v.Y + m(offset + 2) * v.Z),
            CSng(m(offset + 3) * v.X + m(offset + 4) * v.Y + m(offset + 5) * v.Z),
            CSng(m(offset + 6) * v.X + m(offset + 7) * v.Y + m(offset + 8) * v.Z))
    End Function

    ''' <summary>行主序矩阵的转置与向量相乘：<c>dst = Mᵀ · v</c></summary>
    Public Function MatTVec(m As Double(), offset As Integer, v As Vector3) As Vector3
        Return New Vector3(
            CSng(m(offset + 0) * v.X + m(offset + 3) * v.Y + m(offset + 6) * v.Z),
            CSng(m(offset + 1) * v.X + m(offset + 4) * v.Y + m(offset + 7) * v.Z),
            CSng(m(offset + 2) * v.X + m(offset + 5) * v.Y + m(offset + 8) * v.Z))
    End Function

    ' /********************************************************************************/
    '  杂项
    ' /********************************************************************************/

    Public Function SafeNormalize(v As Vector3) As Vector3
        Dim len As Double = v.Length()

        If len < 1.0E-12 Then
            Return Vector3.Zero
        End If

        Return New Vector3(CSng(v.X / len), CSng(v.Y / len), CSng(v.Z / len))
    End Function

    Public Function Clamp(value As Double, low As Double, high As Double) As Double
        If value < low Then Return low
        If value > high Then Return high

        Return value
    End Function

    Public Function ClampInt(value As Integer, low As Integer, high As Integer) As Integer
        If value < low Then Return low
        If value > high Then Return high

        Return value
    End Function

    ''' <summary>行向量约定的世界矩阵（渲染层沿用这一约定）</summary>
    Public Function WorldOf(position As Vector3, rotation As Quaternion) As Matrix4x4
        Return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position)
    End Function

    ''' <summary>取两个向量的叉乘矩阵，<c>[a]× · b = a × b</c></summary>
    Public Function Skew(v As Vector3) As EngMat3
        Return New EngMat3(
            0, -v.Z, v.Y,
            v.Z, 0, -v.X,
            -v.Y, v.X, 0)
    End Function

    ''' <summary>把一个 9 元素行主序矩阵乘到向量上（内核类型版本）</summary>
    Public Function Mul(m As EngMat3, v As EngVec) As EngVec
        Return New EngVec(
            m.m11 * v.x + m.m12 * v.y + m.m13 * v.z,
            m.m21 * v.x + m.m22 * v.y + m.m23 * v.z,
            m.m31 * v.x + m.m32 * v.y + m.m33 * v.z)
    End Function

    Public Function Dot(a As EngVec, b As EngVec) As Double
        Return a.x * b.x + a.y * b.y + a.z * b.z
    End Function

    Public Function Cross(a As EngVec, b As EngVec) As EngVec
        Return New EngVec(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x)
    End Function

    ''' <summary>保证数组至少这么长（不够就新建），用于工作缓冲区复用</summary>
    Public Function Ensure(buf As Double(), size As Integer) As Double()
        If buf IsNot Nothing AndAlso buf.Length >= size Then
            Return buf
        End If

        Return New Double(Math.Max(size, 1) - 1) {}
    End Function

    Public Function Ensure(buf As Integer(), size As Integer) As Integer()
        If buf IsNot Nothing AndAlso buf.Length >= size Then
            Return buf
        End If

        Return New Integer(Math.Max(size, 1) - 1) {}
    End Function

End Module
