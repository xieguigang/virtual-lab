Imports System.Collections.Concurrent
Imports System.IO
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' 虚拟六轴机械臂的 NDJSON-over-TCP 客户端。
''' </summary>
''' <remarks>
''' 契约（与 VirtualWorkshop 的 <c>TcpServer</c> 一致）：
''' <code>
''' 请求: {"id":1,"cmd":"movej","params":{"q":[...]}}
''' 响应: {"id":1,"ok":true,"result":{...}}  或  {"id":1,"ok":false,"error":{...}}
''' 事件: {"event":"state","seq":9,"data":{...}}
''' </code>
''' </remarks>
Public Class ArmClient
    Implements IDisposable

    Public Property Host As String = "127.0.0.1"
    Public Property Port As Integer = 8900

    Public Event Notification As Action(Of JsonObject)

    Private _client As TcpClient
    Private _writer As StreamWriter
    Private _readerThread As Thread
    Private _running As Boolean
    Private _counter As Integer
    Private ReadOnly _pending As New ConcurrentDictionary(Of Integer, TaskCompletionSource(Of JsonObject))()
    Private ReadOnly _writeLock As New Object()

    Public ReadOnly Property Connected As Boolean
        Get
            Return _client IsNot Nothing AndAlso _client.Connected AndAlso _running
        End Get
    End Property

    Public Function Connect(Optional host As String = Nothing, Optional port As Integer = 0) As Boolean
        If Not String.IsNullOrEmpty(host) Then Me.Host = host
        If port > 0 Then Me.Port = port

        Call Close()

        Try
            _client = New TcpClient()
            _client.NoDelay = True
            _client.Connect(Me.Host, Me.Port)

            _writer = New StreamWriter(_client.GetStream(), New UTF8Encoding(False)) With {
                .AutoFlush = True
            }

            _running = True
            _readerThread = New Thread(AddressOf Pump) With {
                .IsBackground = True,
                .Name = "arm-client-reader"
            }
            _readerThread.Start()

            Return True
        Catch ex As Exception
            Call Close()
            Throw New InvalidOperationException($"连接 {Me.Host}:{Me.Port} 失败: {ex.Message}", ex)
        End Try
    End Function

    Public Sub Close()
        _running = False

        Try
            _writer?.Dispose()
        Catch ex As Exception
            ' 忽略
        End Try

        Try
            _client?.Close()
        Catch ex As Exception
            ' 忽略
        End Try

        _writer = Nothing
        _client = Nothing

        For Each pair As KeyValuePair(Of Integer, TaskCompletionSource(Of JsonObject)) In _pending
            pair.Value.TrySetException(New InvalidOperationException("连接已关闭"))
        Next

        _pending.Clear()
    End Sub

    ''' <summary>发送一条指令并等待响应。</summary>
    Public Function Send(command As String, args As JsonObject, Optional timeoutMs As Integer = 30000) As JsonObject
        If Not Connected Then
            Throw New InvalidOperationException("尚未连接到虚拟硬件")
        End If

        Dim id As Integer = Interlocked.Increment(_counter)
        Dim request As New JsonObject()

        request("id") = id
        request("cmd") = command

        If args IsNot Nothing Then
            request("params") = args.DeepClone()
        End If

        Dim completion As New TaskCompletionSource(Of JsonObject)()
        _pending(id) = completion

        Dim line As String = request.ToJsonString()

        SyncLock _writeLock
            _writer.WriteLine(line)
        End SyncLock

        If Not completion.Task.Wait(timeoutMs) Then
            Dim ignored As TaskCompletionSource(Of JsonObject) = Nothing

            _pending.TryRemove(id, ignored)

            Throw New TimeoutException($"指令 {command} 等待响应超时（{timeoutMs} ms）")
        End If

        Return completion.Task.Result
    End Function

    ''' <summary>发送并返回 result 节点；失败抛异常。</summary>
    Public Function Invoke(command As String, args As JsonObject, Optional timeoutMs As Integer = 30000) As JsonObject
        Dim response As JsonObject = Send(command, args, timeoutMs)

        If response Is Nothing Then Return New JsonObject()

        Dim ok As JsonNode = Nothing

        If response.TryGetPropertyValue("ok", ok) AndAlso ok IsNot Nothing Then
            Dim succeeded As Boolean = False

            Try
                succeeded = ok.GetValue(Of Boolean)()
            Catch ex As Exception
                succeeded = False
            End Try

            If Not succeeded Then
                Dim errorNode As JsonObject = TryCast(response("error"), JsonObject)
                Dim code As String = "error"
                Dim message As String = response.ToJsonString()

                If errorNode IsNot Nothing Then
                    code = If(errorNode("code")?.ToString(), code)
                    message = If(errorNode("message")?.ToString(), message)
                End If

                Throw New InvalidOperationException($"[{code}] {message}")
            End If
        End If

        Dim result As JsonNode = Nothing

        If response.TryGetPropertyValue("result", result) Then
            Return TryCast(result, JsonObject)
        End If

        ' 没有 result 包装时（例如 ping / state）整个响应就是结果
        Return response
    End Function

    Public Sub Subscribe(Optional intervalMs As Integer = 50)
        Call Send("subscribe", New JsonObject From {{"interval_ms", intervalMs}})
    End Sub

    Public Sub Unsubscribe()
        Call Send("unsubscribe", New JsonObject())
    End Sub

    Private Sub Pump()
        Try
            Using reader As New StreamReader(_client.GetStream(), New UTF8Encoding(False))
                While _running
                    Dim line As String = reader.ReadLine()

                    If line Is Nothing Then Exit While
                    If String.IsNullOrWhiteSpace(line) Then Continue While

                    Call Dispatch(line)
                End While
            End Using
        Catch ex As Exception
            ' 连接断了，让正在等待的请求失败
        Finally
            _running = False
        End Try
    End Sub

    Private Sub Dispatch(line As String)
        Dim node As JsonNode = Nothing

        Try
            node = JsonNode.Parse(line)
        Catch ex As Exception
            Return
        End Try

        Dim message As JsonObject = TryCast(node, JsonObject)

        If message Is Nothing Then Return

        If message.ContainsKey("event") Then
            RaiseEvent Notification(message)
            Return
        End If

        Dim idNode As JsonNode = Nothing

        If Not message.TryGetPropertyValue("id", idNode) OrElse idNode Is Nothing Then Return

        Dim id As Integer

        If Not Integer.TryParse(idNode.ToString(), id) Then Return

        Dim completion As TaskCompletionSource(Of JsonObject) = Nothing

        If _pending.TryRemove(id, completion) Then
            completion.TrySetResult(message)
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Call Close()
    End Sub

End Class

''' <summary>JSON ↔ CLR 集合的互转（给 LiteJs 用）。</summary>
Public Module JsonBridge

    ''' <summary>
    ''' 转成 LiteJs 的原生值：对象 → Dictionary、数组 → List、数字 → Double、布尔 → Boolean。
    ''' </summary>
    ''' <remarks>
    ''' 这一步非常关键：只有返回 <c>Dictionary(Of String, Object)</c> / <c>List(Of Object)</c>，
    ''' JS 端才能用 <c>.map</c>、<c>.length</c>、属性访问；返回 <c>JsonObject</c> 会是一个
    ''' 只能索引的 CLR 代理。
    ''' </remarks>
    Public Function ToJs(node As JsonNode) As Object
        If node Is Nothing Then Return Nothing

        Dim obj As JsonObject = TryCast(node, JsonObject)

        If obj IsNot Nothing Then
            Dim result As New Dictionary(Of String, Object)(StringComparer.Ordinal)

            For Each pair As KeyValuePair(Of String, JsonNode) In obj
                result(pair.Key) = ToJs(pair.Value)
            Next

            Return result
        End If

        Dim array As JsonArray = TryCast(node, JsonArray)

        If array IsNot Nothing Then
            Dim result As New List(Of Object)()

            For Each item As JsonNode In array
                result.Add(ToJs(item))
            Next

            Return result
        End If

        Dim value As JsonValue = TryCast(node, JsonValue)

        If value IsNot Nothing Then
            Dim text As String = value.ToJsonString()

            If String.Equals(text, "true", StringComparison.OrdinalIgnoreCase) Then Return True
            If String.Equals(text, "false", StringComparison.OrdinalIgnoreCase) Then Return False
            If String.Equals(text, "null", StringComparison.OrdinalIgnoreCase) Then Return Nothing

            If text.StartsWith("""", StringComparison.Ordinal) Then
                Try
                    Return value.GetValue(Of String)()
                Catch ex As Exception
                    Return text.Trim(""""c)
                End Try
            End If

            Dim number As Double

            If Double.TryParse(text, Globalization.NumberStyles.Float,
                               Globalization.CultureInfo.InvariantCulture, number) Then
                Return number
            End If

            Return text
        End If

        Return node.ToJsonString()
    End Function

    ''' <summary>把 CLR 值转回 JSON（用于把 JS 侧参数塞回请求）。</summary>
    Public Function FromJs(value As Object) As JsonNode
        If value Is Nothing Then Return Nothing

        Dim dictionary As Dictionary(Of String, Object) = TryCast(value, Dictionary(Of String, Object))

        If dictionary IsNot Nothing Then
            Dim obj As New JsonObject()

            For Each pair As KeyValuePair(Of String, Object) In dictionary
                obj(pair.Key) = FromJs(pair.Value)
            Next

            Return obj
        End If

        Dim list As List(Of Object) = TryCast(value, List(Of Object))

        If list IsNot Nothing Then
            Dim array As New JsonArray()

            For Each item As Object In list
                array.Add(FromJs(item))
            Next

            Return array
        End If

        If TypeOf value Is Double Then Return JsonValue.Create(CDbl(value))
        If TypeOf value Is Boolean Then Return JsonValue.Create(CBool(value))
        If TypeOf value Is String Then Return JsonValue.Create(CStr(value))
        If TypeOf value Is Integer Then Return JsonValue.Create(CInt(value))
        If TypeOf value Is Long Then Return JsonValue.Create(CLng(value))

        Return JsonValue.Create(value.ToString())
    End Function

End Module
