Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports VirtualWorkshop.Configuration
Imports VirtualWorkshop.Simulation

''' <summary>
''' 导出确定性的仿真状态快照序列，作为重构前后「行为一致性」对比的基准。
''' 两种驱动模式 × 三个累计步数，全部使用 StepFixed，不依赖真实时钟。
''' </summary>
Module Program

    Private ReadOnly JsonOptions As New JsonSerializerOptions With {
        .WriteIndented = True,
        .NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    }

    Function Main(args As String()) As Integer
        Dim output As String = If(args.Length > 0, args(0),
                                  "g:\virtual-lab\tests\baselines\virtualworkshop-baseline.json")
        Dim scenarios As New List(Of Object)()

        For Each kinematic As Boolean In New Boolean() {True, False}
            Dim options As New SimulationOptions With {
                .KinematicArm = kinematic,
                .CameraEnabled = False,
                .EnableTcp = False,
                .EnableHttp = False,
                .BlockCount = 12
            }

            Dim preset As SimulationPreset = SimulationPreset.Create(options)
            Dim cumulative As Integer = 0

            For Each delta As Integer In New Integer() {0, 120, 600}
                If delta > 0 Then
                    preset.Host.StepFixed(delta)
                    cumulative += delta
                End If

                scenarios.Add(New With {
                    .mode = If(kinematic, "kinematic", "dynamic"),
                    .stepIndex = cumulative,
                    .snapshot = preset.Host.State
                })
            Next

            Console.WriteLine($"{If(kinematic, "kinematic", "dynamic")}: {cumulative} steps captured")
        Next

        Dim root As New Dictionary(Of String, Object) From {
            {"generated", "baseline"},
            {"scenarios", scenarios}
        }

        Dim folder As String = Path.GetDirectoryName(Path.GetFullPath(output))

        If Not Directory.Exists(folder) Then Directory.CreateDirectory(folder)

        File.WriteAllText(output, JsonSerializer.Serialize(root, JsonOptions))
        Console.WriteLine($"baseline written: {output}")

        Return 0
    End Function

End Module
