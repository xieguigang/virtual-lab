''' <summary>
''' MuJoCo 场景层公开的三维向量。
''' </summary>
''' <remarks>
''' 保留引用类型语义与完整的运算符集合，使上层仿真/渲染代码可以用与
''' 物理内核相同的写法表达向量运算，而不必引用内核程序集。
''' 与 <see cref="System.Numerics.Vector3"/> 的互转由 <c>MjMath</c> 提供。
''' </remarks>
Namespace Interop

Public Class Vector3

    Public x As Double
    Public y As Double
    Public z As Double

    Public Sub New()
    End Sub

    Public Sub New(x As Double, y As Double, z As Double)
        Me.x = x
        Me.y = y
        Me.z = z
    End Sub

    Public Sub New(x As Single, y As Single, z As Single)
        Me.x = CDbl(x)
        Me.y = CDbl(y)
        Me.z = CDbl(z)
    End Sub

    Public Shared ReadOnly Property Zero As Vector3
        Get
            Return New Vector3(0.0, 0.0, 0.0)
        End Get
    End Property

    Public Shared ReadOnly Property One As Vector3
        Get
            Return New Vector3(1.0, 1.0, 1.0)
        End Get
    End Property

    Public ReadOnly Property Magnitude As Double
        Get
            Return System.Math.Sqrt(x * x + y * y + z * z)
        End Get
    End Property

    Default Public Property Field(offset As Integer) As Double
        Get
            Select Case offset
                Case 0 : Return x
                Case 1 : Return y
                Case 2 : Return z
                Case Else : Throw New ArgumentOutOfRangeException(NameOf(offset))
            End Select
        End Get
        Set(value As Double)
            Select Case offset
                Case 0 : x = value
                Case 1 : y = value
                Case 2 : z = value
                Case Else : Throw New ArgumentOutOfRangeException(NameOf(offset))
            End Select
        End Set
    End Property

    Public Function Clone() As Vector3
        Return New Vector3(x, y, z)
    End Function

    Public Function Normalize() As Vector3
        Dim len As Double = Magnitude

        If len < 1.0E-15 Then Return New Vector3()

        Return New Vector3(x / len, y / len, z / len)
    End Function

    Public Shared Function Dot(a As Vector3, b As Vector3) As Double
        Return a.x * b.x + a.y * b.y + a.z * b.z
    End Function

    Public Shared Function Cross(a As Vector3, b As Vector3) As Vector3
        Return New Vector3(a.y * b.z - a.z * b.y,
                           a.z * b.x - a.x * b.z,
                           a.x * b.y - a.y * b.x)
    End Function

    Public Shared Operator +(a As Vector3, b As Vector3) As Vector3
        Return New Vector3(a.x + b.x, a.y + b.y, a.z + b.z)
    End Operator

    Public Shared Operator -(a As Vector3, b As Vector3) As Vector3
        Return New Vector3(a.x - b.x, a.y - b.y, a.z - b.z)
    End Operator

    Public Shared Operator -(v As Vector3) As Vector3
        Return New Vector3(-v.x, -v.y, -v.z)
    End Operator

    Public Shared Operator *(v As Vector3, a As Double) As Vector3
        Return New Vector3(v.x * a, v.y * a, v.z * a)
    End Operator

    Public Shared Operator *(a As Double, v As Vector3) As Vector3
        Return New Vector3(v.x * a, v.y * a, v.z * a)
    End Operator

    Public Shared Operator *(a As Vector3, b As Vector3) As Vector3
        Return New Vector3(a.x * b.x, a.y * b.y, a.z * b.z)
    End Operator

    Public Shared Operator /(v As Vector3, n As Double) As Vector3
        Return New Vector3(v.x / n, v.y / n, v.z / n)
    End Operator

    Public Overrides Function ToString() As String
        Return $"({x:F4}, {y:F4}, {z:F4})"
    End Function

End Class

End Namespace
