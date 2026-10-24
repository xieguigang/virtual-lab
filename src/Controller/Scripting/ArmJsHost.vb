Imports System.Text.Json.Nodes
Imports System.Threading

''' <summary>
''' 注入到 JavaScript 全局作用域里的 <c>arm</c> 对象。
''' </summary>
''' <remarks>
''' <para>
''' LiteJs 通过 <c>Interpreter.DefineGlobal</c> 把 CLR 对象包成反射代理，
''' Public 方法自动成为 JS 成员，异常被规范化为可 <c>catch</c> 的脚本错误。
''' </para>
''' <para>
''' <b>返回值格式很重要</b>：只有返回 <c>Dictionary(Of String, Object)</c> /
''' <c>List(Of Object)</c>，JS 侧才能用 <c>.map/.length/属性访问</c>；
''' 返回 <c>JsonObject</c> 只会得到一个只能索引的 CLR 代理。
''' </para>
''' <para>
''' 方法名刻意用小写开头，让脚本里写成 <c>arm.moveJ([...])</c> 这种自然的 JS 风格。
''' </para>
''' </remarks>
Public Class ArmJsHost

    Private ReadOnly _client As ArmClient

    Public Sub New(client As ArmClient)
        If client Is Nothing Then Throw New ArgumentNullException(NameOf(client))

        _client = client
    End Sub

    ''' <summary>底层客户端（调试用）。</summary>
    Public ReadOnly Property client As ArmClient
        Get
            Return _client
        End Get
    End Property

    ' /********************************************************************************/
    '  连接
    ' /********************************************************************************/

    Public Function connect(Optional host As String = Nothing, Optional port As Double = 0) As Dictionary(Of String, Object)
        ' 幂等：命令行可能已经连上了，脚本里的 arm.connect() 不应该把连接拆掉重来
        If Not _client.Connected Then
            Call _client.Connect(host, CInt(port))
        End If

        Return ToDictionary(_client.Invoke("hello", New JsonObject()))
    End Function

    Public Function disconnect() As Dictionary(Of String, Object)
        Call _client.Close()

        Return OkMessage("disconnected")
    End Function

    Public Function state() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("state", New JsonObject()))
    End Function

    Public Function info() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("info", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  运动
    ' /********************************************************************************/

    Public Function moveJ(q As Double(), Optional speed As Double = 0.35) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movej", New JsonObject From {
            {"q", ToArray(q)},
            {"speed", speed}
        }))
    End Function

    Public Function moveJRel(dq As Double(), Optional speed As Double = 0.35) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movej_rel", New JsonObject From {
            {"dq", ToArray(dq)},
            {"speed", speed}
        }))
    End Function

    Public Function moveL(x As Double, y As Double, z As Double,
                          Optional speed As Double = 0.25) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movel", New JsonObject From {
            {"x", x}, {"y", y}, {"z", z}, {"speed", speed}
        }))
    End Function

    Public Function home(Optional speed As Double = 0.3) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("home", New JsonObject From {{"speed", speed}}))
    End Function

    ''' <summary>等待一条运动指令结束；返回 {done, success, message}。</summary>
    Public Function waitMotion(id As Double, Optional timeoutMs As Double = 30000) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("wait_motion", New JsonObject From {
            {"id", CInt(id)},
            {"timeout_ms", CInt(timeoutMs)}
        }, CInt(timeoutMs) + 5000))
    End Function

    Public Function stopMotion() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("stop", New JsonObject()))
    End Function

    Public Function estop() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("estop", New JsonObject()))
    End Function

    Public Function reset() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("reset", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  夹爪
    ' /********************************************************************************/

    Public Function grip(Optional force As Double = 35.0) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("grip", New JsonObject From {{"force", force}}))
    End Function

    Public Function release() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("release", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  场景
    ' /********************************************************************************/

    Public Function spawn(shape As String, size As Double(), position As Double(),
                          Optional mass As Double = 0.2,
                          Optional friction As Double = 0.75,
                          Optional color As String = Nothing) As Dictionary(Of String, Object)

        Dim args As New JsonObject From {
            {"shape", shape},
            {"size", ToArray(size)},
            {"position", ToArray(position)},
            {"mass", mass},
            {"friction", friction}
        }

        If Not String.IsNullOrEmpty(color) Then
            args("color") = color
        End If

        Return ToDictionary(_client.Invoke("spawn", args))
    End Function

    Public Function objects() As List(Of Object)
        Dim result As Dictionary(Of String, Object) = ToDictionary(_client.Invoke("objects", New JsonObject()))
        Dim items As Object = Nothing

        If result.TryGetValue("objects", items) Then
            Dim list As List(Of Object) = TryCast(items, List(Of Object))

            If list IsNot Nothing Then Return list
        End If

        Return New List(Of Object)()
    End Function

    Public Function remove(id As String) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("remove", New JsonObject From {{"id", id}}))
    End Function

    Public Function clear() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("clear", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  配置
    ' /********************************************************************************/

    Public Function config(key As String, value As Double) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("config", New JsonObject From {
            {"key", key}, {"value", value}
        }))
    End Function

    Public Function setRealtime(factor As Double) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("config", New JsonObject From {
            {"key", "realtime"}, {"value", factor}
        }))
    End Function

    ''' <summary>通用入口：直接发一条原始指令，paramsJson 是 JSON 文本。</summary>
    Public Function send(command As String, Optional paramsJson As String = "{}") As Dictionary(Of String, Object)
        Dim args As JsonObject = Nothing

        Try
            args = TryCast(JsonNode.Parse(If(String.IsNullOrWhiteSpace(paramsJson), "{}", paramsJson)), JsonObject)
        Catch ex As Exception
            args = New JsonObject()
        End Try

        If args Is Nothing Then args = New JsonObject()

        Return ToDictionary(_client.Invoke(command, args))
    End Function

    ' /********************************************************************************/
    '  辅助
    ' /********************************************************************************/

    Public Function sleep(ms As Double) As Dictionary(Of String, Object)
        Thread.Sleep(CInt(System.Math.Max(0.0, System.Math.Min(600000.0, ms))))

        Return OkMessage("slept")
    End Function

    Public Function log(text As String) As Dictionary(Of String, Object)
        Console.WriteLine(text)

        Return OkMessage("logged")
    End Function

    Private Shared Function OkMessage(text As String) As Dictionary(Of String, Object)
        Return New Dictionary(Of String, Object)(StringComparer.Ordinal) From {{"ok", True}, {"message", text}}
    End Function

    Private Shared Function ToArray(values As Double()) As JsonArray
        Dim array As New JsonArray()

        If values IsNot Nothing Then
            For Each value As Double In values
                array.Add(JsonValue.Create(value))
            Next
        End If

        Return array
    End Function

    ''' <summary>JsonObject → Dictionary(Of String, Object)（递归），保证 JS 侧拿到原生对象。</summary>
    Private Shared Function ToDictionary(node As JsonObject) As Dictionary(Of String, Object)
        Dim value As Object = JsonBridge.ToJs(node)

        If value Is Nothing Then Return New Dictionary(Of String, Object)(StringComparer.Ordinal)

        Dim result As Dictionary(Of String, Object) = TryCast(value, Dictionary(Of String, Object))

        If result Is Nothing Then
            result = New Dictionary(Of String, Object)(StringComparer.Ordinal)
            result("value") = value
        End If

        Return result
    End Function

End Class
