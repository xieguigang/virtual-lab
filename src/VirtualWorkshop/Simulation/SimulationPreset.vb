Imports System.IO
Imports VirtualWorkshop.Configuration
Imports VirtualWorkshop.Model

Namespace Simulation

    ''' <summary>
    ''' 一份完整的运行环境：模型 → 装配体 → 仿真 → 虚拟硬件 → 宿主。
    ''' </summary>
    ''' <remarks>
    ''' GUI、headless 与自检都通过它构建，保证三者跑的是同一份内核与同一份参数。
    ''' </remarks>
    Public Class SimulationPreset

        Public Property Options As SimulationOptions
        Public Property Assembly As ArmAssembly
        Public Property Simulation As ArmSimulation
        Public Property Hardware As ArmHardware
        Public Property Host As SimulationHost

        Public Property ModelFile As String
        Public Property UrdfFile As String

        Public ReadOnly Property Ready As Boolean
            Get
                Return Host IsNot Nothing AndAlso Simulation IsNot Nothing
            End Get
        End Property

        ''' <summary>加载模型并构建完整的仿真环境。</summary>
        Public Shared Function Create(options As SimulationOptions) As SimulationPreset
            If options Is Nothing Then options = New SimulationOptions()

            Dim preset As New SimulationPreset With {
                .Options = options
            }

            Dim glb As String = options.ResolveModel(".glb", options.ModelFile)
            Dim urdf As String = options.ResolveModel(".urdf", options.UrdfFile)

            If String.IsNullOrEmpty(glb) OrElse Not File.Exists(glb) Then
                Throw New FileNotFoundException("找不到 six_axis_arm.glb；请用 --model 指定路径")
            End If

            If String.IsNullOrEmpty(urdf) OrElse Not File.Exists(urdf) Then
                Throw New FileNotFoundException("找不到 six_axis_arm.urdf；请用 --urdf 指定路径")
            End If

            preset.ModelFile = glb
            preset.UrdfFile = urdf

            Dim robot As UrdfRobot = UrdfRobot.Load(urdf)
            Dim scene As GlbScene = GlbLinkLoader.Load(glb, robot)

            preset.Assembly = ArmAssembly.Build(robot, scene)

            Dim simOptions As New ArmSimulationOptions With {
                .KinematicArm = options.KinematicArm,
                .SpawnDefaultBlocks = True,
                .DefaultBlockCount = options.BlockCount
            }

            preset.Simulation = New ArmSimulation(preset.Assembly, simOptions)
            preset.Hardware = New ArmHardware(preset.Simulation)
            preset.Host = New SimulationHost(preset.Simulation, preset.Hardware)

            preset.Host.Dispatcher.ModelName = "six_axis_arm"
            preset.Host.Dispatcher.TcpPort = options.TcpPort
            preset.Host.Dispatcher.HttpPort = options.HttpPort
            preset.Host.Dispatcher.RealtimeFactor = options.RealtimeFactor

            Return preset
        End Function

    End Class

End Namespace
