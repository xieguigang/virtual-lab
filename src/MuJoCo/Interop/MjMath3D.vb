Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports EngMath = Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Interop.Math3D

    ''' <summary>
    ''' MuJoCo 场景层的姿态四元数，分量顺序为 <c>(w, x, y, z)</c>。
    ''' </summary>
    Public Structure Quaternion

        Public w As Double
        Public x As Double
        Public y As Double
        Public z As Double

        Public Sub New(w As Double, x As Double, y As Double, z As Double)
            Me.w = w
            Me.x = x
            Me.y = y
            Me.z = z
        End Sub

        Public Shared ReadOnly Property Identity As Quaternion
            Get
                Return New Quaternion(1.0, 0.0, 0.0, 0.0)
            End Get
        End Property

        Public ReadOnly Property LengthSquared As Double
            Get
                Return w * w + x * x + y * y + z * z
            End Get
        End Property

        Public Shared Function FromAxisAngle(axis As Vector3, angle As Double) As Quaternion
            Return MjInterop.Q(EngMath.Quaternion.FromAxisAngle(MjInterop.V(axis), angle))
        End Function

        Public Shared Function Compose(a As Quaternion, b As Quaternion) As Quaternion
            Return MjInterop.Q(EngMath.Quaternion.Compose(MjInterop.Q(a), MjInterop.Q(b)))
        End Function

        Public Shared Function Slerp(a As Quaternion, b As Quaternion, t As Double) As Quaternion
            Return MjInterop.Q(EngMath.Quaternion.Slerp(MjInterop.Q(a), MjInterop.Q(b), t))
        End Function

        Public Shared Function AngleBetween(a As Quaternion, b As Quaternion) As Double
            Return EngMath.Quaternion.AngleBetween(MjInterop.Q(a), MjInterop.Q(b))
        End Function

        Public Shared Function FromForwardUp(forward As Vector3, up As Vector3) As Quaternion
            Return MjInterop.Q(EngMath.Quaternion.FromForwardUp(MjInterop.V(forward), MjInterop.V(up)))
        End Function

        Public Function Inverse() As Quaternion
            Return MjInterop.Q(MjInterop.Q(Me).Inverse())
        End Function

        Public Function Conjugate() As Quaternion
            Return MjInterop.Q(MjInterop.Q(Me).Conjugate())
        End Function

        Public Function Normalize() As Quaternion
            Return MjInterop.Q(MjInterop.Q(Me).Normalize())
        End Function

        Public Function Rotate(v As Vector3) As Vector3
            Return MjInterop.V(MjInterop.Q(Me).Rotate(MjInterop.V(v)))
        End Function

        Public Function Unrotate(v As Vector3) As Vector3
            Return MjInterop.V(MjInterop.Q(Me).Unrotate(MjInterop.V(v)))
        End Function

        Public Function ToMatrix3x3() As Matrix3x3
            Return MjInterop.M3(MjInterop.Q(Me).ToMatrix3x3())
        End Function

        Public Sub ToAxisAngle(ByRef axis As Vector3, ByRef angle As Double)
            Dim engAxis As Eng.Vector3 = MjInterop.V(axis)
            Dim engAngle As Double = angle

            MjInterop.Q(Me).ToAxisAngle(engAxis, engAngle)

            axis = MjInterop.V(engAxis)
            angle = engAngle
        End Sub

        Public Function Integrate(angularVelocity As Vector3, dt As Double) As Quaternion
            Return MjInterop.Q(MjInterop.Q(Me).Integrate(MjInterop.V(angularVelocity), dt))
        End Function

        Public Shared Operator *(a As Quaternion, b As Quaternion) As Quaternion
            Return MjInterop.Q(MjInterop.Q(a) * MjInterop.Q(b))
        End Operator

        Public Shared Operator *(q As Quaternion, v As Vector3) As Vector3
            Return q.Rotate(v)
        End Operator

        Public Overrides Function ToString() As String
            Return $"({w:F4}, {x:F4}, {y:F4}, {z:F4})"
        End Function

    End Structure

    ''' <summary>3×3 矩阵，行主序，字段与物理内核一致。</summary>
    Public Structure Matrix3x3

        Public m11 As Double
        Public m12 As Double
        Public m13 As Double
        Public m21 As Double
        Public m22 As Double
        Public m23 As Double
        Public m31 As Double
        Public m32 As Double
        Public m33 As Double

        Public Sub New(m11 As Double, m12 As Double, m13 As Double,
                       m21 As Double, m22 As Double, m23 As Double,
                       m31 As Double, m32 As Double, m33 As Double)
            Me.m11 = m11 : Me.m12 = m12 : Me.m13 = m13
            Me.m21 = m21 : Me.m22 = m22 : Me.m23 = m23
            Me.m31 = m31 : Me.m32 = m32 : Me.m33 = m33
        End Sub

        Public Shared ReadOnly Property Zero As Matrix3x3
            Get
                Return New Matrix3x3()
            End Get
        End Property

        Public Shared ReadOnly Property Identity As Matrix3x3
            Get
                Return Diagonal(1.0, 1.0, 1.0)
            End Get
        End Property

        Public Shared Function Diagonal(x As Double, y As Double, z As Double) As Matrix3x3
            Return New Matrix3x3(x, 0, 0, 0, y, 0, 0, 0, z)
        End Function

        Public Shared Function Outer(a As Vector3, b As Vector3) As Matrix3x3
            Return M3(EngMath.Matrix3x3.Outer(MjInterop.V(a), MjInterop.V(b)))
        End Function

        Public Function Transpose() As Matrix3x3
            Return M3(M3(Me).Transpose())
        End Function

        Public Function Determinant() As Double
            Return M3(Me).Determinant()
        End Function

        Public Function Inverse() As Matrix3x3
            Return M3(M3(Me).Inverse())
        End Function

        ''' <summary>行向量约定 <c>v·M</c>。</summary>
        Public Function Multiply(v As Vector3) As Vector3
            Return MjInterop.V(M3(Me).Multiply(MjInterop.V(v)))
        End Function

        ''' <summary>列向量约定 <c>M·v</c>。</summary>
        Public Function MultiplyLeft(v As Vector3) As Vector3
            Return MjInterop.V(M3(Me).MultiplyLeft(MjInterop.V(v)))
        End Function

        Public Function Multiply(other As Matrix3x3) As Matrix3x3
            Return M3(M3(Me).Multiply(M3(other)))
        End Function

        Public Shared Operator *(a As Matrix3x3, b As Matrix3x3) As Matrix3x3
            Return a.Multiply(b)
        End Operator

        Public Shared Operator *(m As Matrix3x3, s As Double) As Matrix3x3
            Return M3(M3(m) * s)
        End Operator

        Public Shared Operator +(a As Matrix3x3, b As Matrix3x3) As Matrix3x3
            Return M3(M3(a) + M3(b))
        End Operator

        Friend Shared Function M3(m As Matrix3x3) As EngMath.Matrix3x3
            Return MjInterop.M3(m)
        End Function

        Friend Shared Function M3(m As EngMath.Matrix3x3) As Matrix3x3
            Return MjInterop.M3(m)
        End Function

    End Structure

    ''' <summary>向量运算工具，签名与内核 Math3D.Vector3Math 一致。</summary>
    Public Module Vector3Math

        Public Function Length(v As Vector3) As Double
            Return EngMath.Vector3Math.Length(MjInterop.V(v))
        End Function

        Public Function LengthSquared(v As Vector3) As Double
            Return EngMath.Vector3Math.LengthSquared(MjInterop.V(v))
        End Function

        Public Function Normalize(v As Vector3) As Vector3
            Return MjInterop.V(EngMath.Vector3Math.Normalize(MjInterop.V(v)))
        End Function

        Public Function Dot(a As Vector3, b As Vector3) As Double
            Return EngMath.Vector3Math.Dot(MjInterop.V(a), MjInterop.V(b))
        End Function

        Public Function Cross(a As Vector3, b As Vector3) As Vector3
            Return MjInterop.V(EngMath.Vector3Math.Cross(MjInterop.V(a), MjInterop.V(b)))
        End Function

        Public Function Distance(a As Vector3, b As Vector3) As Double
            Return EngMath.Vector3Math.Distance(MjInterop.V(a), MjInterop.V(b))
        End Function

        Public Function Lerp(a As Vector3, b As Vector3, t As Double) As Vector3
            Return MjInterop.V(EngMath.Vector3Math.Lerp(MjInterop.V(a), MjInterop.V(b), t))
        End Function

        Public Function ProjectOnPlane(v As Vector3, unitAxis As Vector3) As Vector3
            Return MjInterop.V(EngMath.Vector3Math.ProjectOnPlane(MjInterop.V(v), MjInterop.V(unitAxis)))
        End Function

        Public Function RotateAroundAxis(v As Vector3, axis As Vector3, angle As Double) As Vector3
            Return MjInterop.V(EngMath.Vector3Math.RotateAroundAxis(MjInterop.V(v), MjInterop.V(axis), angle))
        End Function

        Public Function Vec(x As Double, y As Double, z As Double) As Vector3
            Return New Vector3(x, y, z)
        End Function

    End Module

End Namespace
