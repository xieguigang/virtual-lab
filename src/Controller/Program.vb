Imports System.Text.Json.Nodes
Imports System.Threading

''' <summary>
''' 上位机：TCP REPL + LiteJs 脚本执行器。
''' </summary>
''' <remarks>
''' 用法：
''' <code>
''' Controller.exe                              进入 REPL（默认连接 127.0.0.1:8900）
''' Controller.exe --js scripts/pick_and_place.js
''' Controller.exe --eval "console.log(arm.state().q)"
''' Controller.exe --connect 127.0.0.1:8900 --js scripts/build_house.js
''' </code>
''' </remarks>
Module Program

    Private ReadOnly Client As New ArmClient()
    Private _deviceHost As String = "127.0.0.1"
    Private _devicePort As Integer = 8900

    Function Main(args As String()) As Integer
        Dim scriptPath As String = Nothing
        Dim evalCode As String = Nothing
        Dim showHelp As Boolean = False
        Dim scriptArgs As New List(Of String)()

        Dim i As Integer = 0

        While i < args.Length
            Select Case args(i).ToLowerInvariant()
                Case "--connect", "--host"
                    Dim endpoint As String = NextArg(args, i)
                    i += 1
                    Call ParseEndpoint(endpoint)
                Case "--js", "--script"
                    scriptPath = NextArg(args, i)
                    i += 1
                Case "--eval", "-e"
                    evalCode = NextArg(args, i)
                    i += 1
                Case "--help", "-h", "-?"
                    showHelp = True
                Case Else
                    If args(i).StartsWith("--", StringComparison.Ordinal) Then
                        Console.Error.WriteLine("未知参数: " & args(i))
                    Else
                        scriptArgs.Add(args(i))
                    End If
            End Select

            i += 1
        End While

        If showHelp Then
            PrintHelp()
            Return 0
        End If

        AddHandler Client.Notification,
            Sub(message As JsonObject)
                Dim data As JsonObject = TryCast(message("data"), JsonObject)
                Dim state As Dictionary(Of String, Object) = TryCast(JsonBridge.ToJs(data), Dictionary(Of String, Object))

                If state IsNot Nothing Then Call StatusView.PrintCompact(state)
            End Sub

        If Not String.IsNullOrEmpty(scriptPath) Then
            Return RunScript(scriptPath, scriptArgs.ToArray())
        End If

        If Not String.IsNullOrEmpty(evalCode) Then
            Return RunEval(evalCode)
        End If

        Return RunRepl()
    End Function

    Private Function NextArg(args As String(), index As Integer) As String
        If index + 1 < args.Length Then Return args(index + 1)

        Return Nothing
    End Function

    Private Sub ParseEndpoint(endpoint As String)
        If String.IsNullOrEmpty(endpoint) Then Return

        Dim parts As String() = endpoint.Split(":"c)

        _deviceHost = parts(0)

        If parts.Length > 1 Then
            Dim port As Integer

            If Integer.TryParse(parts(1), port) Then _devicePort = port
        End If

        ' 脚本里 arm.connect() 不带参数时会复用客户端的当前地址，所以这里必须同步
        Client.Host = _deviceHost
        Client.Port = _devicePort
    End Sub

    Private Function EnsureConnected() As Boolean
        If Client.Connected Then Return True

        Try
            Call Client.Connect(_deviceHost, _devicePort)
            Console.WriteLine($"已连接 {_deviceHost}:{_devicePort}")
            Return True
        Catch ex As Exception
            Console.Error.WriteLine("连接失败: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function RunScript(path As String, scriptArgs As String()) As Integer
        Dim resolved As String = JsScriptRunner.ResolveScript(path)

        If String.IsNullOrEmpty(resolved) Then
            Console.Error.WriteLine("找不到脚本: " & JsScriptRunner.NormalizePath(path))
            Call PrintScriptList()
            Return 1
        End If

        If Not EnsureConnected() Then Return 2

        Try
            Return JsScriptRunner.RunFile(Client, resolved, scriptArgs)
        Finally
            Call Client.Close()
        End Try
    End Function

    Private Function RunEval(code As String) As Integer
        If Not EnsureConnected() Then Return 2

        Try
            Return JsScriptRunner.Run(Client, code, "<eval>")
        Finally
            Call Client.Close()
        End Try
    End Function

    Private Function RunRepl() As Integer
        Console.WriteLine("VirtualWorkshop 虚拟硬件 REPL  ·  输入 help 查看命令，quit 退出")

        Call EnsureConnected()
        Call PrintHelp()

        While True
            Console.Write("vlab> ")

            Dim line As String = CleanInput(Console.ReadLine())

            If line Is Nothing Then Exit While
            If line.Length = 0 Then Continue While

            If Not HandleCommand(line) Then Exit While
        End While

        Call Client.Close()

        Return 0
    End Function

    ''' <summary>去掉首尾空白以及 BOM、零宽字符（从管道/文件粘进来的命令常带这些）。</summary>
    Private Function CleanInput(text As String) As String
        If text Is Nothing Then Return ""

        Dim junk As Char() = {ChrW(&HFEFF), ChrW(&H200B), ChrW(&H200C), ChrW(&H200D), ChrW(&HA0)}

        Return text.Trim().Trim(junk).Trim()
    End Function

    ''' <summary>处理一条 REPL 命令；返回 False 表示退出。</summary>
    Private Function HandleCommand(line As String) As Boolean
        Dim parts As String() = line.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
        Dim command As String = parts(0).ToLowerInvariant()

        Try
            Select Case command
                Case "quit", "exit", "q"
                    Return False

                Case "help", "?"
                    Call PrintHelp()

                Case "connect"
                    If parts.Length > 1 Then Call ParseEndpoint(parts(1))

                    Call Client.Close()
                    Call EnsureConnected()

                Case "disconnect"
                    Call Client.Close()
                    Console.WriteLine("已断开")

                Case "state", "s"
                    Dim snapshot As Object = JsonBridge.ToJs(Client.Invoke("state", New JsonObject()))

                    Call StatusView.Print(TryCast(snapshot, Dictionary(Of String, Object)))

                Case "info"
                    Call PrintJson(Client.Invoke("info", New JsonObject()))

                Case "watch"
                    Dim interval As Integer = If(parts.Length > 1, Integer.Parse(parts(1)), 100)

                    Call Client.Subscribe(interval)
                    Console.WriteLine($"订阅状态流（{interval} ms），按任意键返回…")
                    Console.ReadKey(True)
                    Call Client.Unsubscribe()

                Case "movej"
                    Dim q As Double() = ParseNumbers(parts, 1, 6)
                    Dim speed As Double = If(parts.Length > 7, Double.Parse(parts(7)), 0.35)

                    Call Report(Client.Invoke("movej", New JsonObject From {
                        {"q", ArrayOf(q)}, {"speed", speed}
                    }))
                    Call WaitLastMotion()

                Case "movel"
                    Dim x As Double = Double.Parse(parts(1))
                    Dim y As Double = Double.Parse(parts(2))
                    Dim z As Double = Double.Parse(parts(3))
                    Dim speed As Double = If(parts.Length > 4, Double.Parse(parts(4)), 0.25)

                    Call Report(Client.Invoke("movel", New JsonObject From {
                        {"x", x}, {"y", y}, {"z", z}, {"speed", speed}
                    }))
                    Call WaitLastMotion()

                Case "home"
                    Call Report(Client.Invoke("home", New JsonObject()))
                    Call WaitLastMotion()

                Case "grip"
                    Dim force As Double = If(parts.Length > 1, Double.Parse(parts(1)), 35.0)

                    Call Report(Client.Invoke("grip", New JsonObject From {{"force", force}}))

                Case "release"
                    Call Report(Client.Invoke("release", New JsonObject()))

                Case "spawn"
                    Call HandleSpawn(parts)

                Case "objects"
                    Call PrintJson(Client.Invoke("objects", New JsonObject()))

                Case "clear"
                    Call Report(Client.Invoke("clear", New JsonObject()))

                Case "estop"
                    Call Report(Client.Invoke("estop", New JsonObject()))

                Case "reset"
                    Call Report(Client.Invoke("reset", New JsonObject()))

                Case "sleep"
                    Call Thread.Sleep(If(parts.Length > 1, Integer.Parse(parts(1)), 500))

                Case "frame"
                    Dim mode As String = If(parts.Length > 1, parts(1).ToLowerInvariant(), "png")
                    Dim output As String = If(parts.Length > 2, JsScriptRunner.NormalizePath(parts(2)), Nothing)

                    Call SaveFrame(mode, output)

                Case "js"
                    ' 路径可能带引号、也可能含空格，所以取 "js" 之外的整行再出去引号
                    Dim scriptArg As String = JsScriptRunner.NormalizePath(line.Substring(2))

                    If scriptArg.Length = 0 Then
                        Console.WriteLine("用法: js <脚本路径或脚本名>")
                        Call PrintScriptList()
                    Else
                        Dim resolved As String = JsScriptRunner.ResolveScript(scriptArg)

                        If String.IsNullOrEmpty(resolved) Then
                            Console.WriteLine("找不到脚本: " & scriptArg)
                            Call PrintScriptList()
                        ElseIf EnsureConnected() Then
                            Call JsScriptRunner.RunFile(Client, resolved)
                        End If
                    End If

                Case "raw"
                    Dim json As String = line.Substring(3).Trim()
                    Dim request As JsonObject = Nothing

                    Try
                        request = TryCast(JsonNode.Parse(json), JsonObject)
                    Catch ex As Exception
                        request = Nothing
                    End Try

                    If request Is Nothing Then
                        Console.WriteLine("用法: raw {""cmd"":""ping""}")
                    Else
                        Dim rawCommand As String = If(request("cmd")?.ToString(), "")
                        Dim rawArgs As JsonObject = TryCast(request("params"), JsonObject)

                        If rawArgs Is Nothing Then rawArgs = New JsonObject()

                        Call PrintJson(Client.Invoke(rawCommand, rawArgs))
                    End If

                Case Else
                    Console.WriteLine("未知命令: " & command & "（help 查看帮助）")
            End Select
        Catch ex As Exception
            Console.ForegroundColor = ConsoleColor.Red
            Console.WriteLine("错误: " & ex.Message)
            Console.ResetColor()
        End Try

        Return True
    End Function

    Private Sub HandleSpawn(parts As String())
        If parts.Length < 2 Then
            Console.WriteLine("用法: spawn <box|sphere|cylinder|cone|frustum|hemisphere|prism|pyramid|disc> [尺寸...]")
            Return
        End If

        Dim shape As String = parts(1)
        Dim size As New List(Of Double)()

        For i As Integer = 2 To System.Math.Min(parts.Length - 1, 5)
            size.Add(Double.Parse(parts(i)))
        Next

        Dim args As New JsonObject From {
            {"shape", shape},
            {"size", ArrayOf(size.ToArray())}
        }

        Call Report(Client.Invoke("spawn", args))
    End Sub

    Private Sub WaitLastMotion()
        ' 从最近的响应里拿不到 motionId 时直接跳过；脚本里用 arm.waitMotion(id) 更精确
    End Sub

    Private Function ParseNumbers(parts As String(), start As Integer, count As Integer) As Double()
        Dim values As New List(Of Double)()

        For i As Integer = start To System.Math.Min(parts.Length - 1, start + count - 1)
            Dim value As Double

            If Double.TryParse(parts(i), value) Then values.Add(value)
        Next

        Return values.ToArray()
    End Function

    Private Function ArrayOf(values As Double()) As JsonArray
        Dim array As New JsonArray()

        For Each value As Double In values
            array.Add(JsonValue.Create(value))
        Next

        Return array
    End Function

    Private Sub Report(result As JsonObject)
        If result Is Nothing Then
            Console.WriteLine("ok")
            Return
        End If

        Console.WriteLine(result.ToJsonString())
    End Sub

    Private Sub PrintJson(node As JsonObject)
        If node Is Nothing Then
            Console.WriteLine("null")
            Return
        End If

        Console.WriteLine(node.ToJsonString(New System.Text.Json.JsonSerializerOptions With {
            .WriteIndented = True,
            .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
    End Sub

    ''' <summary>
    ''' 取一帧夹爪相机图像并写盘。
    ''' </summary>
    ''' <param name="mode">png / rgb / depth</param>
    ''' <param name="output">输出路径；为空时写到当前目录 frame-step.png</param>
    Private Sub SaveFrame(mode As String, output As String)
        Dim args As New JsonObject From {{"format", "png"}}

        Select Case mode
            Case "rgb"
                args("format") = "rgb"
            Case "depth", "f32"
                args("format") = "png"
                args("depth") = "f32"
            Case Else
                args("format") = "png"
        End Select

        Dim result As JsonObject = Client.Invoke("frame", args, 60000)

        If result Is Nothing Then
            Console.WriteLine("没有拿到帧数据")
            Return
        End If

        Dim data As String = If(result("data")?.ToString(), "")

        If data.Length = 0 Then
            Console.WriteLine("没有拿到帧数据")
            Return
        End If

        Dim bytes As Byte() = Convert.FromBase64String(data)
        Dim info As Dictionary(Of String, Object) = ToDictionary(result)
        Dim width As Double = StatusView.Num(info, "width")
        Dim height As Double = StatusView.Num(info, "height")
        Dim captureMs As Double = StatusView.Num(info, "captureMs")
        Dim stepIndex As Double = StatusView.Num(info, "step")
        Dim extension As String = If(mode = "rgb", ".rgb", ".png")

        If String.IsNullOrEmpty(output) Then
            output = System.IO.Path.Combine(System.Environment.CurrentDirectory,
                                             $"frame-{CInt(stepIndex)}{extension}")
        End If

        Call System.IO.File.WriteAllBytes(output, bytes)

        Console.WriteLine($"已保存 {output}  {CInt(width)}x{CInt(height)}  {bytes.Length} 字节  渲染 {captureMs:F1} ms")

        ' 深度另存一份（与图像像素一对一）
        Dim depthNode As JsonObject = TryCast(result("depth"), JsonObject)

        If depthNode IsNot Nothing Then
            Dim depthData As String = If(depthNode("data")?.ToString(), "")

            If depthData.Length > 0 Then
                Dim depthPath As String = System.IO.Path.ChangeExtension(output, ".depth.f32")

                Call System.IO.File.WriteAllBytes(depthPath, Convert.FromBase64String(depthData))
                Console.WriteLine($"深度图已保存 {depthPath}  ({CInt(width)}x{CInt(height)} float32 米)")
            End If
        End If
    End Sub

    Private Function ToDictionary(node As JsonObject) As Dictionary(Of String, Object)
        Return TryCast(JsonBridge.ToJs(node), Dictionary(Of String, Object))
    End Function

    ''' <summary>列出随程序发布的示例脚本。</summary>
    Private Sub PrintScriptList()
        Dim files As String() = JsScriptRunner.ListScripts()

        If files.Length = 0 Then
            Dim folder As String = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts")

            Console.WriteLine("（" & folder & " 下没有 .js 脚本）")
            Return
        End If

        Console.WriteLine("可用脚本:")

        For Each file As String In files
            Console.WriteLine("  js " & file)
        Next
    End Sub

    Private Sub PrintHelp()
        Console.WriteLine()
        Console.WriteLine("  connect [host:port]   连接虚拟硬件")
        Console.WriteLine("  state                 打印状态表")
        Console.WriteLine("  watch [ms]            订阅状态流（按任意键返回）")
        Console.WriteLine("  movej q1..q6 [speed]  关节空间运动")
        Console.WriteLine("  movel x y z [speed]   笛卡尔直线运动")
        Console.WriteLine("  home                  回零")
        Console.WriteLine("  grip [force]          夹紧    release  张开")
        Console.WriteLine("  spawn <shape> [size]  生成积木    objects  列出物体    clear  清空")
        Console.WriteLine("  estop                 急停        reset    复位场景")
        Console.WriteLine("  frame [png|rgb|depth] [文件]  抓取夹爪相机画面并保存")
        Console.WriteLine("  js <脚本>             执行 JavaScript 演示脚本")
        Console.WriteLine("  sleep <ms>            等待")
        Console.WriteLine("  quit                  退出")
        Console.WriteLine()
    End Sub

End Module
