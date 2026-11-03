Imports System.Text

''' <summary>
''' 编译期与运行期的错误对象，对应 <c>mujoco.h</c> 的 <c>mjError</c> 缓冲与
''' <c>mju_error</c> 通道。
''' </summary>
''' <remarks>
''' C 版把错误写进调用方提供的 <c>char* error</c> 缓冲并返回 <c>Nothing</c>。
''' 托管层保留同样的 <c>ByRef errorText</c> 重载（便于逐行对照 C 代码），
''' 同时提供抛异常的便捷重载 <c>mj_loadXML(path)</c>。
''' 错误文本里带上对象名与期望/实际值，避免只看到 "invalid model" 这种无从下手的提示。
''' </remarks>
Public Class MjException
    Inherits Exception

    ''' <summary>MXCF 元素名，例如 <c>geom</c>；空表示与具体元素无关</summary>
    Public Property Element As String
    ''' <summary>出错的 XML 行号，0 表示未知</summary>
    Public Property Line As Integer
    ''' <summary>出错的源文件</summary>
    Public Property Source_ As String

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, elementName As String, lineNumber As Integer, sourceFile As String)
        MyBase.New(Describe(message, elementName, lineNumber, sourceFile))

        Me.Element = elementName
        Me.Line = lineNumber
        Me.Source_ = sourceFile
    End Sub

    Public Sub New(message As String, inner As Exception)
        MyBase.New(message, inner)
    End Sub

    Private Shared Function Describe(message As String, elementName As String,
                                     lineNumber As Integer, sourceFile As String) As String
        Dim text As New StringBuilder(message)

        If Not String.IsNullOrEmpty(elementName) Then
            Call text.Append(" [element <").Append(elementName).Append(">]")
        End If

        If Not String.IsNullOrEmpty(sourceFile) Then
            Call text.Append(" in ").Append(sourceFile)
        End If

        If lineNumber > 0 Then
            Call text.Append(" (line ").Append(lineNumber).Append(")")
        End If

        Return text.ToString()
    End Function

    Public Overrides Function ToString() As String
        Return Message
    End Function
End Class

''' <summary>
''' 一个可复用的错误缓冲，模拟 C 版的 <c>char* error</c> 参数。
''' </summary>
Public Class MjErrorBuffer

    Private ReadOnly m_text As New StringBuilder()

    Public ReadOnly Property HasError As Boolean
        Get
            Return m_text.Length > 0
        End Get
    End Property

    Public ReadOnly Property Text As String
        Get
            Return m_text.ToString()
        End Get
    End Property

    Public Sub Clear()
        Call m_text.Clear()
    End Sub

    Public Sub Append(message As String)
        If m_text.Length > 0 Then
            Call m_text.Append("; ")
        End If

        Call m_text.Append(message)
    End Sub

    ''' <summary>把缓冲内容写回可选的 <c>ByRef</c> 参数</summary>
    Public Sub Export(ByRef errorText As String)
        errorText = Text
    End Sub

    Public Overrides Function ToString() As String
        Return Text
    End Function
End Class

''' <summary>错误与断言工具</summary>
Public Module MjAssert

    ''' <summary>
    ''' 参数检查：非法时抛 <see cref="ArgumentOutOfRangeException"/>。
    ''' </summary>
    ''' <remarks>
    ''' 仿真库的原则是「绝不静默返回 0」：C 版在 Release 下可能读到别人的内存，
    ''' 托管层宁可抛异常，让调用方立刻看到越界的索引。
    ''' </remarks>
    Public Sub RequireIndex(index As Integer, count As Integer, name As String)
        If index < 0 OrElse index >= count Then
            Throw New ArgumentOutOfRangeException(name,
                $"index {index} is outside [0, {count - 1}]")
        End If
    End Sub

    Public Function RequireNotNull(Of T)(value As T, name As String) As T
        If value Is Nothing Then
            Throw New ArgumentNullException(name)
        End If

        Return value
    End Function

    ''' <summary>检查数组长度，不够时抛 <see cref="ArgumentException"/></summary>
    Public Sub RequireSize(array As Double(), required As Integer, name As String)
        If array Is Nothing Then
            Throw New ArgumentNullException(name, $"the buffer '{name}' is required")
        End If

        If array.Length < required Then
            Throw New ArgumentException(
                $"the buffer '{name}' needs {required} elements but holds {array.Length}", name)
        End If
    End Sub

    Public Sub RequireSize(array As Integer(), required As Integer, name As String)
        If array Is Nothing Then
            Throw New ArgumentNullException(name, $"the buffer '{name}' is required")
        End If

        If array.Length < required Then
            Throw New ArgumentException(
                $"the buffer '{name}' needs {required} elements but holds {array.Length}", name)
        End If
    End Sub

    ''' <summary>模型编译完成之后的内部一致性检查</summary>
    Public Sub CheckModel(m As mjModel)
        If m Is Nothing Then
            Throw New ArgumentNullException(NameOf(m))
        End If

        If m.qpos0 Is Nothing OrElse m.qpos0.Length <> m.nq Then
            Throw New MjException($"qpos0 must hold nq={m.nq} values")
        End If

        If m.body_parentid Is Nothing OrElse m.body_parentid.Length <> m.nbody Then
            Throw New MjException($"body_parentid must hold nbody={m.nbody} values")
        End If

        If m.nbody < 1 Then
            Throw New MjException("a model needs at least the world body")
        End If

        ' 每个关节的 qpos / dof 地址必须落在 [0, nq) / [0, nv) 内并且互不重叠，
        ' 这是编译器最容易写错的地方，出现重叠时表现为"两个关节共用一个角度"
        Dim used(Math.Max(m.nq, 1) - 1) As Boolean

        For j As Integer = 0 To m.njnt - 1
            Dim dim_ As Integer = mj_jntQposDim(CType(m.jnt_type(j), mjtJoint))

            For k As Integer = 0 To dim_ - 1
                Dim adr As Integer = m.jnt_qposadr(j) + k

                If adr < 0 OrElse adr >= m.nq Then
                    Throw New MjException($"joint {j} ({m.jnt_name(j)}) qpos address {adr} is outside nq={m.nq}")
                End If

                If used(adr) Then
                    Throw New MjException($"joint {j} ({m.jnt_name(j)}) reuses qpos address {adr}")
                End If

                used(adr) = True
            Next
        Next
    End Sub

End Module
