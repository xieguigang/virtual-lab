Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks
Imports VirtualWorkshop.Simulation

Namespace Protocol

    ''' <summary>
    ''' 极简 HTTP/1.1 服务器：只实现三个端点，避免 <see cref="HttpListener"/> 的 URL ACL 权限问题。
    ''' </summary>
    ''' <remarks>
    ''' <code>
    ''' GET  /health              → {"ok":true}
    ''' GET  /api/v1/state        → 状态快照
    ''' POST /api/v1/command      → body 即一条 {"cmd":..,"args":..}
    ''' </code>
    ''' 每个请求处理完就关闭连接（<c>Connection: close</c>），足够 REPL / 脚本 / curl 使用。
    ''' </remarks>
    Public Class HttpServer

        Public ReadOnly Property Host As SimulationHost
        Public ReadOnly Property Port As Integer

        Public Event Log As Action(Of String)

        Private _listener As TcpListener
        Private _running As Boolean
        Private _acceptThread As Thread
        Private _connections As Integer

        Public Sub New(host As SimulationHost, port As Integer)
            Me.Host = host
            Me.Port = port
        End Sub

        Public ReadOnly Property ConnectionCount As Integer
            Get
                Return _connections
            End Get
        End Property

        Public Sub Start()
            If _running Then Return

            _listener = New TcpListener(IPAddress.Loopback, Port)
            _listener.Start()
            _running = True

            _acceptThread = New Thread(AddressOf AcceptLoop) With {
                .IsBackground = True,
                .Name = "vlab-http-accept"
            }
            _acceptThread.Start()

            RaiseEvent Log($"HTTP 服务已启动: http://127.0.0.1:{Port}/")
        End Sub

        Public Sub [Stop]()
            _running = False

            Try
                _listener?.Stop()
            Catch ex As Exception
                ' 忽略
            End Try

            RaiseEvent Log("HTTP 服务已停止")
        End Sub

        Private Sub AcceptLoop()
            While _running
                Try
                    Dim client As TcpClient = _listener.AcceptTcpClient()

                    ThreadPool.QueueUserWorkItem(Sub(state) HandleClient(client))
                Catch ex As SocketException
                    Exit While
                Catch ex As ObjectDisposedException
                    Exit While
                Catch ex As Exception
                    If _running Then RaiseEvent Log("HTTP accept 失败: " & ex.Message)
                End Try
            End While
        End Sub

        Private Sub HandleClient(client As TcpClient)
            Interlocked.Increment(_connections)

            Try
                Using client
                    client.ReceiveTimeout = 10000

                    Dim stream As NetworkStream = client.GetStream()

                    Using reader As New StreamReader(stream, New UTF8Encoding(False), False, 8192, True)
                        Dim requestLine As String = reader.ReadLine()

                        If String.IsNullOrEmpty(requestLine) Then Return

                        Dim parts As String() = requestLine.Split(" "c)

                        If parts.Length < 2 Then Return

                        Dim method As String = parts(0).ToUpperInvariant()
                        Dim rawPath As String = parts(1)

                        Dim question As Integer = rawPath.IndexOf("?"c)
                        Dim query As String = ""
                        Dim path As String = rawPath

                        If question >= 0 Then
                            query = rawPath.Substring(question + 1)
                            path = rawPath.Substring(0, question)
                        End If

                        Dim contentLength As Integer = 0

                        While True
                            Dim header As String = reader.ReadLine()

                            If String.IsNullOrEmpty(header) Then Exit While

                            If header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) Then
                                Integer.TryParse(header.Substring(15).Trim(), contentLength)
                            End If
                        End While

                        Dim body As String = ""

                        If contentLength > 0 Then
                            Dim buffer(contentLength - 1) As Char
                            Dim read As Integer = 0

                            While read < contentLength
                                Dim got As Integer = reader.Read(buffer, read, contentLength - read)

                                If got <= 0 Then Exit While

                                read += got
                            End While

                            body = New String(buffer, 0, read)
                        End If

                        Dim status As Integer = 200
                        Dim payload As String = Nothing
                        Dim binary As Byte() = Nothing
                        Dim contentType As String = JsonContentType

                        Select Case path.TrimEnd("/"c)
                            Case "/health", "/api/v1/health"
                                payload = JsonText.Stringify(New JsonObject From {{"ok", True}})

                            Case "/api/v1/state", "/state"
                                payload = JsonText.Stringify(JsonText.NodeOf(Host.State))

                            Case "/api/v1/command", "/command"
                                If Not String.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) Then
                                    status = 405
                                    payload = JsonText.Stringify(ErrorBody(ProtocolText.ErrBadParams, "请使用 POST"))
                                Else
                                    payload = ExecuteCommand(body)
                                End If

                            Case "/api/v1/frame"
                                ' 直接返回二进制图像，浏览器/curl 可以直接存成文件
                                Dim frameArgs As JsonObject = ParseQuery(query)
                                Dim format As String = JsonText.Str(frameArgs, ProtocolText.FieldFormat, "png")

                                Dim frame As JsonObject = CaptureFrame(frameArgs)

                                If frame Is Nothing Then
                                    status = 500
                                    payload = JsonText.Stringify(ErrorBody(ProtocolText.ErrTimeout, "取帧超时"))
                                Else
                                    Dim data As String = JsonText.Str(frame, "data")

                                    If String.IsNullOrEmpty(data) Then
                                        status = 400
                                        payload = JsonText.Stringify(ErrorBody(ProtocolText.ErrBadParams, "没有图像数据"))
                                    Else
                                        binary = Convert.FromBase64String(data)
                                        contentType = If(String.Equals(format, "png", StringComparison.OrdinalIgnoreCase),
                                                          "image/png", "application/octet-stream")
                                    End If
                                End If

                            Case "/api/v1/frame.json"
                                Dim frameArgs As JsonObject = ParseQuery(query)
                                Dim frame As JsonObject = CaptureFrame(frameArgs)

                                If frame Is Nothing Then
                                    status = 500
                                    payload = JsonText.Stringify(ErrorBody(ProtocolText.ErrTimeout, "取帧超时"))
                                Else
                                    payload = JsonText.Stringify(frame)
                                End If

                            Case ""
                                payload = JsonText.Stringify(New JsonObject From {
                                    {"name", "VirtualWorkshop 虚拟六轴机械臂"},
                                    {"version", ProtocolText.Version},
                                    {"endpoints", New JsonArray From {
                                        JsonValue.Create("GET /health"),
                                        JsonValue.Create("GET /api/v1/state"),
                                        JsonValue.Create("POST /api/v1/command"),
                                        JsonValue.Create("GET /api/v1/frame?format=png&depth=f32"),
                                        JsonValue.Create("GET /api/v1/frame.json")
                                    }}
                                })

                            Case Else
                                status = 404
                                payload = JsonText.Stringify(ErrorBody(ProtocolText.ErrNotFound, "未知路径: " & path))
                        End Select

                        If binary IsNot Nothing Then
                            Call WriteBinaryResponse(stream, status, contentType, binary)
                        Else
                            Call WriteResponse(stream, status, payload, contentType)
                        End If
                    End Using
                End Using
            Catch ex As Exception
                ' 客户端断开属于正常现象
            Finally
                Interlocked.Decrement(_connections)
            End Try
        End Sub

        Private Function ExecuteCommand(body As String) As String
            Dim request As JsonObject = Nothing

            Try
                request = TryCast(JsonNode.Parse(body), JsonObject)
            Catch ex As Exception
                request = Nothing
            End Try

            If request Is Nothing Then
                Return JsonText.Stringify(ErrorBody(ProtocolText.ErrBadParams, "请求体不是合法的 JSON 对象"))
            End If

            Dim command As String = JsonText.Str(request, ProtocolText.FieldCmd)
            Dim args As JsonObject = JsonText.ObjectOf(request, ProtocolText.FieldParams)

            If args Is Nothing Then args = New JsonObject()

            If String.IsNullOrEmpty(command) Then
                Return JsonText.Stringify(ErrorBody(ProtocolText.ErrBadParams, "缺少 cmd 字段"))
            End If

            Try
                Dim task As Task(Of JsonObject) = Host.Post(command, args)

                If Not task.Wait(30000) Then
                    Return JsonText.Stringify(ErrorBody(ProtocolText.ErrTimeout, "指令执行超时"))
                End If

                Return JsonText.Stringify(task.Result)
            Catch ex As AggregateException
                Return JsonText.Stringify(ErrorBody(ProtocolText.ErrInternal,
                                                    If(ex.InnerException Is Nothing, ex.Message, ex.InnerException.Message)))
            Catch ex As Exception
                Return JsonText.Stringify(ErrorBody(ProtocolText.ErrInternal, ex.Message))
            End Try
        End Function

        Private Shared Function ErrorBody(code As String, message As String) As JsonObject
            Return New JsonObject From {
                {ProtocolText.FieldOk, False},
                {ProtocolText.FieldError, ProtocolText.ErrorNode(code, message)}
            }
        End Function

        Private Const JsonContentType As String = "application/json; charset=utf-8"

        ''' <summary>把 "a=1&amp;b=abc" 解析成参数对象（数字/布尔自动转换）。</summary>
        Private Shared Function ParseQuery(query As String) As JsonObject
            Dim args As New JsonObject()

            If String.IsNullOrEmpty(query) Then Return args

            For Each pair As String In query.Split("&"c)
                If pair.Length = 0 Then Continue For

                Dim kv As String() = pair.Split("="c)
                Dim key As String = Uri.UnescapeDataString(kv(0))
                Dim value As String = If(kv.Length > 1, Uri.UnescapeDataString(kv(1)), "")

                If key.Length = 0 Then Continue For

                Dim number As Double

                If Double.TryParse(value, number) Then
                    args(key) = JsonValue.Create(number)
                ElseIf String.Equals(value, "true", StringComparison.OrdinalIgnoreCase) Then
                    args(key) = JsonValue.Create(True)
                ElseIf String.Equals(value, "false", StringComparison.OrdinalIgnoreCase) Then
                    args(key) = JsonValue.Create(False)
                Else
                    args(key) = JsonValue.Create(value)
                End If
            Next

            Return args
        End Function

        ''' <summary>在仿真线程上取一帧；失败返回 Nothing。</summary>
        Private Function CaptureFrame(args As JsonObject) As JsonObject
            Try
                Dim task As Task(Of JsonObject) = Host.Post(ProtocolText.CmdFrame, args)

                If Not task.Wait(30000) Then Return Nothing

                Return task.Result
            Catch ex As Exception
                Return Nothing
            End Try
        End Function

        Private Shared Sub WriteResponse(stream As NetworkStream, status As Integer, payload As String,
                                        Optional contentType As String = JsonContentType)

            If payload Is Nothing Then payload = "{}"

            Dim bytes As Byte() = Encoding.UTF8.GetBytes(payload)

            Call WriteBinaryResponse(stream, status, contentType, bytes)
        End Sub

        Private Shared Sub WriteBinaryResponse(stream As NetworkStream, status As Integer,
                                               contentType As String, bytes As Byte())

            Dim reason As String = If(status = 200, "OK",
                                      If(status = 404, "Not Found",
                                         If(status = 405, "Method Not Allowed",
                                            If(status = 500, "Internal Server Error", "Bad Request"))))

            Dim header As String =
                $"HTTP/1.1 {status} {reason}{vbCrLf}" &
                $"Content-Type: {If(contentType, JsonContentType)}{vbCrLf}" &
                $"Content-Length: {bytes.Length}{vbCrLf}" &
                $"Access-Control-Allow-Origin: *{vbCrLf}" &
                $"Connection: close{vbCrLf}{vbCrLf}"

            Dim headerBytes As Byte() = Encoding.ASCII.GetBytes(header)

            stream.Write(headerBytes, 0, headerBytes.Length)
            stream.Write(bytes, 0, bytes.Length)
            stream.Flush()
        End Sub

    End Class

End Namespace
