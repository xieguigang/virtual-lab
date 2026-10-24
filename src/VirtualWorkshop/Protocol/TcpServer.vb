Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks
Imports VirtualWorkshop.Simulation

Namespace Protocol

    ''' <summary>一个客户端连接。</summary>
    Friend Class TcpClientSession

        Public Property Client As TcpClient
        Public Property Writer As StreamWriter
        Public Property SessionId As Integer
        Public Property SubscribeIntervalMs As Integer
        Public Property LastPush As Long
        Public Property Closed As Boolean

        Public Function SendLine(text As String) As Boolean
            If Closed OrElse Writer Is Nothing Then Return False

            Try
                Writer.WriteLine(text)
                Writer.Flush()
                Return True
            Catch ex As IOException
                Closed = True
                Return False
            Catch ex As ObjectDisposedException
                Closed = True
                Return False
            Catch ex As Exception
                Closed = True
                Return False
            End Try
        End Function
    End Class

    ''' <summary>
    ''' NDJSON over TCP 服务器：每行一条 JSON 请求，每行一条 JSON 响应；
    ''' 订阅后按固定间隔推送 <c>{"event":"state"}</c>。
    ''' </summary>
    Public Class TcpServer

        Public ReadOnly Property Host As SimulationHost
        Public ReadOnly Property Port As Integer

        Public Event Log As Action(Of String)

        Private _listener As TcpListener
        Private _running As Boolean
        Private _acceptThread As Thread
        Private _publishThread As Thread
        Private _sessionCounter As Integer
        Private ReadOnly _sessions As New List(Of TcpClientSession)()
        Private ReadOnly _sync As New Object()

        Public Sub New(host As SimulationHost, port As Integer)
            Me.Host = host
            Me.Port = port
        End Sub

        Public ReadOnly Property ClientCount As Integer
            Get
                SyncLock _sync
                    Return _sessions.Count
                End SyncLock
            End Get
        End Property

        Public Sub Start()
            If _running Then Return

            _listener = New TcpListener(IPAddress.Loopback, Port)
            _listener.Start()
            _running = True

            _acceptThread = New Thread(AddressOf AcceptLoop) With {
                .IsBackground = True,
                .Name = "vlab-tcp-accept"
            }
            _acceptThread.Start()

            _publishThread = New Thread(AddressOf PublishLoop) With {
                .IsBackground = True,
                .Name = "vlab-tcp-publish"
            }
            _publishThread.Start()

            RaiseEvent Log($"TCP 服务已启动: 127.0.0.1:{Port}")
        End Sub

        Public Sub [Stop]()
            _running = False

            Try
                _listener?.Stop()
            Catch ex As Exception
                ' 忽略关闭异常
            End Try

            SyncLock _sync
                For Each session As TcpClientSession In _sessions
                    Call CloseSession(session)
                Next

                _sessions.Clear()
            End SyncLock

            RaiseEvent Log("TCP 服务已停止")
        End Sub

        Private Sub AcceptLoop()
            While _running
                Try
                    Dim client As TcpClient = _listener.AcceptTcpClient()

                    client.NoDelay = True

                    Dim session As New TcpClientSession With {
                        .Client = client,
                        .Writer = New StreamWriter(client.GetStream(), New UTF8Encoding(False)),
                        .SessionId = Interlocked.Increment(_sessionCounter)
                    }

                    session.Writer.AutoFlush = True

                    SyncLock _sync
                        _sessions.Add(session)
                    End SyncLock

                    Dim worker As New Thread(Sub() ClientLoop(session)) With {
                        .IsBackground = True,
                        .Name = "vlab-tcp-client-" & session.SessionId
                    }
                    worker.Start()

                    RaiseEvent Log($"客户端 #{session.SessionId} 已连接")
                Catch ex As SocketException
                    Exit While
                Catch ex As ObjectDisposedException
                    Exit While
                Catch ex As Exception
                    If _running Then RaiseEvent Log("accept 失败: " & ex.Message)
                End Try
            End While
        End Sub

        Private Sub ClientLoop(session As TcpClientSession)
            Try
                Using reader As New StreamReader(session.Client.GetStream(), New UTF8Encoding(False))
                    While _running AndAlso Not session.Closed
                        Dim line As String = reader.ReadLine()

                        If line Is Nothing Then Exit While
                        If String.IsNullOrWhiteSpace(line) Then Continue While

                        Dim response As String = HandleLine(line, session)

                        If response IsNot Nothing Then
                            If Not session.SendLine(response) Then Exit While
                        End If
                    End While
                End Using
            Catch ex As Exception
                If _running Then RaiseEvent Log($"客户端 #{session.SessionId} 异常: {ex.Message}")
            Finally
                SyncLock _sync
                    Call CloseSession(session)
                    _sessions.Remove(session)
                End SyncLock

                RaiseEvent Log($"客户端 #{session.SessionId} 已断开")
            End Try
        End Sub

        Private Function HandleLine(line As String, session As TcpClientSession) As String
            Dim request As JsonObject = Nothing

            Try
                request = TryCast(JsonNode.Parse(line), JsonObject)
            Catch ex As Exception
                request = Nothing
            End Try

            If request Is Nothing Then
                Return JsonText.Stringify(New JsonObject From {
                    {ProtocolText.FieldOk, False},
                    {ProtocolText.FieldError, ProtocolText.ErrorNode(ProtocolText.ErrBadParams, "不是合法的 JSON 对象")}
                })
            End If

            Dim requestId As JsonNode = request(ProtocolText.FieldId)
            Dim command As String = JsonText.Str(request, ProtocolText.FieldCmd)
            Dim args As JsonObject = JsonText.ObjectOf(request, ProtocolText.FieldParams)

            If args Is Nothing Then args = New JsonObject()

            ' 订阅控制
            If String.Equals(command, "subscribe", StringComparison.OrdinalIgnoreCase) Then
                session.SubscribeIntervalMs = CInt(System.Math.Max(10, JsonText.Num(args, "interval_ms", 33)))
                session.LastPush = 0

                Return WrapResponse(requestId, New JsonObject From {
                    {"subscribed", True},
                    {"intervalMs", session.SubscribeIntervalMs}
                })
            End If

            If String.Equals(command, "unsubscribe", StringComparison.OrdinalIgnoreCase) Then
                session.SubscribeIntervalMs = 0

                Return WrapResponse(requestId, New JsonObject From {{"subscribed", False}})
            End If

            If String.IsNullOrEmpty(command) Then
                Return WrapResponse(requestId, ErrorBody(ProtocolText.ErrBadParams, "缺少 cmd 字段"))
            End If

            Dim result As JsonObject

            Try
                Dim task As Task(Of JsonObject) = Host.Post(command, args)

                If Not task.Wait(30000) Then
                    result = ErrorBody(ProtocolText.ErrTimeout, "指令执行超时: " & command)
                Else
                    result = task.Result
                End If
            Catch ex As AggregateException
                result = ErrorBody(ProtocolText.ErrInternal,
                                   If(ex.InnerException Is Nothing, ex.Message, ex.InnerException.Message))
            Catch ex As Exception
                result = ErrorBody(ProtocolText.ErrInternal, ex.Message)
            End Try

            Return WrapResponse(requestId, result)
        End Function

        Private Shared Function ErrorBody(code As String, message As String) As JsonObject
            Return New JsonObject From {
                {ProtocolText.FieldOk, False},
                {ProtocolText.FieldError, ProtocolText.ErrorNode(code, message)}
            }
        End Function

        ''' <summary>把 id 合并进 result；result 没有 ok 字段时补上 ok=true 并把内容放进 result。</summary>
        Private Shared Function WrapResponse(requestId As JsonNode, body As JsonObject) As String
            Dim response As New JsonObject()

            If requestId IsNot Nothing Then
                response(ProtocolText.FieldId) = requestId.DeepClone()
            End If

            If body IsNot Nothing Then
                If body.ContainsKey(ProtocolText.FieldOk) Then
                    For Each pair As KeyValuePair(Of String, JsonNode) In body
                        response(pair.Key) = If(pair.Value Is Nothing, Nothing, pair.Value.DeepClone())
                    Next
                Else
                    response(ProtocolText.FieldOk) = True
                    response(ProtocolText.FieldResult) = body.DeepClone()
                End If
            End If

            Return JsonText.Stringify(response)
        End Function

        Private Sub PublishLoop()
            Dim sequence As Long = 0

            While _running
                Try
                    Dim snapshot As StateSnapshot = Host.State
                    Dim now As Long = Environment.TickCount64
                    Dim due As New List(Of TcpClientSession)()

                    SyncLock _sync
                        For Each session As TcpClientSession In _sessions
                            If session.Closed OrElse session.SubscribeIntervalMs <= 0 Then Continue For
                            If now - session.LastPush < session.SubscribeIntervalMs Then Continue For

                            session.LastPush = now
                            due.Add(session)
                        Next
                    End SyncLock

                    If due.Count > 0 AndAlso snapshot IsNot Nothing Then
                        sequence += 1

                        Dim payload As New JsonObject From {
                            {ProtocolText.FieldEvent, ProtocolText.EventState},
                            {"seq", sequence},
                            {"data", JsonText.NodeOf(snapshot)}
                        }

                        Dim text As String = JsonText.Stringify(payload)

                        For Each session As TcpClientSession In due
                            If Not session.SendLine(text) Then
                                SyncLock _sync
                                    Call CloseSession(session)
                                    _sessions.Remove(session)
                                End SyncLock
                            End If
                        Next
                    End If

                    Thread.Sleep(5)
                Catch ex As Exception
                    If _running Then RaiseEvent Log("推送失败: " & ex.Message)
                End Try
            End While
        End Sub

        Private Sub CloseSession(session As TcpClientSession)
            session.Closed = True

            Try
                session.Writer?.Dispose()
            Catch ex As Exception
                ' 忽略
            End Try

            Try
                session.Client?.Close()
            Catch ex As Exception
                ' 忽略
            End Try
        End Sub

    End Class

End Namespace
