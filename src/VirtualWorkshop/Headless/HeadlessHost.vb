Imports System.Diagnostics
Imports System.Threading
Imports VirtualWorkshop.Configuration
Imports VirtualWorkshop.Protocol
Imports VirtualWorkshop.Simulation

Namespace Headless

    ''' <summary>
    ''' 无界面运行：只跑固定步长物理 + 协议服务，不做任何渲染。
    ''' </summary>
    ''' <remarks>
    ''' 神经网络训练通常需要几千赫兹的仿真步进与大批量并发连接，
    ''' 这个模式把整个渲染链路跳过，并且支持 <c>--realtime</c> 加速。
    ''' </remarks>
    Public Module HeadlessHost

        Public Function Run(preset As SimulationPreset, options As SimulationOptions) As Integer
            If preset Is Nothing OrElse Not preset.Ready Then
                Console.Error.WriteLine("仿真环境未就绪")
                Return 2
            End If

            Dim tcp As TcpServer = Nothing
            Dim http As HttpServer = Nothing

            Console.WriteLine("== VirtualWorkshop headless ==")
            Console.WriteLine("  " & preset.Assembly.Describe())
            Console.WriteLine($"  model  = {preset.ModelFile}")
            Console.WriteLine($"  urdf   = {preset.UrdfFile}")
            Console.WriteLine($"  mode   = {If(preset.Simulation.Options.KinematicArm, "kinematic", "physics")}")
            Console.WriteLine($"  blocks = {preset.Simulation.Objects.Count}")
            Console.WriteLine($"  step   = {preset.Simulation.World.FixedDt * 1000.0:F2} ms")

            Dim log As Action(Of String) =
                Sub(text As String) Console.WriteLine("  [net] " & text)

            Try
                If options.EnableTcp Then
                    tcp = New TcpServer(preset.Host, options.TcpPort)
                    AddHandler tcp.Log, log
                    tcp.Start()
                End If

                If options.EnableHttp Then
                    http = New HttpServer(preset.Host, options.HttpPort)
                    AddHandler http.Log, log
                    http.Start()
                End If
            Catch ex As Exception
                Console.Error.WriteLine("网络服务启动失败: " & ex.Message)
                Call StopServers(tcp, http)
                Return 3
            End Try

            Console.WriteLine("  按 Ctrl+C 退出")

            Dim running As Boolean = True

            Dim cancelHandler As ConsoleCancelEventHandler =
                Sub(sender As Object, e As ConsoleCancelEventArgs)
                    e.Cancel = True
                    running = False
                End Sub

            AddHandler Console.CancelKeyPress, cancelHandler

            Dim watch As Stopwatch = Stopwatch.StartNew()
            Dim previous As Double = 0.0
            Dim reportWatch As Stopwatch = Stopwatch.StartNew()

            While running
                Dim now As Double = watch.Elapsed.TotalSeconds
                Dim elapsed As Double = now - previous

                previous = now

                Try
                    preset.Host.Tick(elapsed)
                Catch ex As Exception
                    Console.Error.WriteLine("仿真步进异常: " & ex.Message)
                End Try

                If reportWatch.ElapsedMilliseconds >= 5000 Then
                    reportWatch.Restart()

                    Dim state As StateSnapshot = preset.Host.State

                    Console.WriteLine(
                        $"  t={state.Time:F1}s step={state.StepIndex} 步/秒={preset.Host.StepsPerSecond:F0} " &
                        $"TCP客户端={If(tcp Is Nothing, 0, tcp.ClientCount)} 指令={preset.Host.ExecutedCommands}")
                End If

                Thread.Sleep(1)
            End While

            RemoveHandler Console.CancelKeyPress, cancelHandler
            Call StopServers(tcp, http)

            Console.WriteLine("已退出")

            Return 0
        End Function

        Private Sub StopServers(tcp As TcpServer, http As HttpServer)
            Try
                tcp?.Stop()
            Catch ex As Exception
                ' 忽略
            End Try

            Try
                http?.Stop()
            Catch ex As Exception
                ' 忽略
            End Try
        End Sub

    End Module

End Namespace
