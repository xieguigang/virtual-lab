Imports System.Windows.Forms
Imports VirtualWorkshop.Configuration
Imports VirtualWorkshop.Diagnostics
Imports VirtualWorkshop.Headless
Imports VirtualWorkshop.Simulation

''' <summary>程序入口：按命令行分派到 GUI / headless / 自检。</summary>
Public Module Program

    Public Function Main(args As String()) As Integer
        Dim options As SimulationOptions

        Try
            options = SimulationOptions.Parse(args)
        Catch ex As Exception
            Console.Error.WriteLine("参数解析失败: " & ex.Message)
            Return 1
        End Try

        If options.Help Then
            SimulationOptions.PrintHelp()
            Return 0
        End If

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Dim preset As SimulationPreset

        Try
            preset = SimulationPreset.Create(options)
        Catch ex As Exception
            Console.Error.WriteLine("加载模型失败: " & ex.Message)
            Return 2
        End Try

        If options.SelfTest Then
            Return SimSelfTest.Run(preset)
        End If

        If options.Inspect Then
            Return SimSelfTest.Inspect(preset)
        End If

        If options.Headless Then
            Return HeadlessHost.Run(preset, options)
        End If

        Application.Run(New FormVirtualLab(options, preset))

        Return 0
    End Function

End Module
