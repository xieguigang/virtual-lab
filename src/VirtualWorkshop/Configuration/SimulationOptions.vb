Imports System.IO
Imports Microsoft.VisualBasic.Engine.Physics

Namespace Configuration

    ''' <summary>命令行选项。</summary>
    Public Class SimulationOptions

        ''' <summary>glb 模型路径（为空时自动在 models 目录里找）。</summary>
        Public Property ModelFile As String
        ''' <summary>URDF 路径。</summary>
        Public Property UrdfFile As String

        Public Property TcpPort As Integer = 8900
        Public Property HttpPort As Integer = 8901

        ''' <summary>无界面高速仿真。</summary>
        Public Property Headless As Boolean
        ''' <summary>运行自检断言后退出。</summary>
        Public Property SelfTest As Boolean
        ''' <summary>只解析并打印模型信息后退出。</summary>
        Public Property Inspect As Boolean
        ''' <summary>显示帮助。</summary>
        Public Property Help As Boolean

        ''' <summary>实时倍率（headless 加速用）。</summary>
        Public Property RealtimeFactor As Double = 1.0

        ''' <summary>是否启动 TCP 服务。</summary>
        Public Property EnableTcp As Boolean = True
        ''' <summary>是否启动 HTTP 服务。</summary>
        Public Property EnableHttp As Boolean = True

        ''' <summary>默认积木数量。</summary>
        Public Property BlockCount As Integer = 12

        ''' <summary>是否使用运动学机械臂（默认是）。</summary>
        Public Property KinematicArm As Boolean = True

        ' ---- 夹爪相机 ----

        ''' <summary>是否启用夹爪第一视角相机。</summary>
        Public Property CameraEnabled As Boolean = True
        Public Property CameraWidth As Integer = 320
        Public Property CameraHeight As Integer = 240
        ''' <summary>垂直视场角（度）。</summary>
        Public Property CameraFov As Double = 60.0
        ''' <summary>自动捕获帧率（Hz）。</summary>
        Public Property CameraRate As Double = 15.0
        ''' <summary>相机光心在 link6 局部系中的偏移。</summary>
        Public Property CameraOffset As New Vector3(0.045, 0.0, 0.0)

        Public ReadOnly Property Args As New List(Of String)()

        Public Shared Function Parse(args As String()) As SimulationOptions
            Dim options As New SimulationOptions()

            If args Is Nothing Then Return options

            Dim i As Integer = 0

            While i < args.Length
                Dim token As String = args(i)

                Select Case token.ToLowerInvariant()
                    Case "--headless", "-h"
                        options.Headless = True
                    Case "--selftest"
                        options.SelfTest = True
                    Case "--inspect"
                        options.Inspect = True
                    Case "--help", "-?", "/?"
                        options.Help = True
                    Case "--model"
                        options.ModelFile = NextValue(args, i)
                        i += 1
                    Case "--urdf"
                        options.UrdfFile = NextValue(args, i)
                        i += 1
                    Case "--port", "--tcp-port"
                        options.TcpPort = ParseInt(NextValue(args, i), options.TcpPort)
                        i += 1
                    Case "--http-port"
                        options.HttpPort = ParseInt(NextValue(args, i), options.HttpPort)
                        i += 1
                    Case "--realtime"
                        options.RealtimeFactor = ParseDouble(NextValue(args, i), options.RealtimeFactor)
                        i += 1
                    Case "--blocks"
                        options.BlockCount = ParseInt(NextValue(args, i), options.BlockCount)
                        i += 1
                    Case "--no-server"
                        options.EnableTcp = False
                        options.EnableHttp = False
                    Case "--no-tcp"
                        options.EnableTcp = False
                    Case "--no-http"
                        options.EnableHttp = False
                    Case "--dynamic"
                        options.KinematicArm = False
                    Case "--camera"
                        options.CameraEnabled = Not IsOff(NextValue(args, i))
                        i += 1
                    Case "--camera-size"
                        Dim size As String = NextValue(args, i)

                        If Not String.IsNullOrEmpty(size) Then
                            Dim parts As String() = size.ToLowerInvariant().Split("x"c)

                            If parts.Length >= 2 Then
                                options.CameraWidth = ParseInt(parts(0), options.CameraWidth)
                                options.CameraHeight = ParseInt(parts(1), options.CameraHeight)
                            End If
                        End If

                        i += 1
                    Case "--camera-fov"
                        options.CameraFov = ParseDouble(NextValue(args, i), options.CameraFov)
                        i += 1
                    Case "--camera-rate"
                        options.CameraRate = ParseDouble(NextValue(args, i), options.CameraRate)
                        i += 1
                    Case "--camera-offset"
                        options.CameraOffset = ParseVector(NextValue(args, i), options.CameraOffset)
                        i += 1
                    Case Else
                        options.Args.Add(token)
                End Select

                i += 1
            End While

            Return options
        End Function

        Private Shared Function IsOff(text As String) As Boolean
            If String.IsNullOrEmpty(text) Then Return False

            Return String.Equals(text, "off", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(text, "false", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(text, "0", StringComparison.Ordinal)
        End Function

        ''' <summary>解析 "x,y,z" 或 "x y z" 形式的向量。</summary>
        Private Shared Function ParseVector(text As String, defaultValue As Vector3) As Vector3
            If String.IsNullOrWhiteSpace(text) Then Return defaultValue

            Dim parts As String() = text.Split(New Char() {","c, " "c, ";"c}, StringSplitOptions.RemoveEmptyEntries)

            If parts.Length < 3 Then Return defaultValue

            Dim x As Double = ParseDouble(parts(0), defaultValue.x)
            Dim y As Double = ParseDouble(parts(1), defaultValue.y)
            Dim z As Double = ParseDouble(parts(2), defaultValue.z)

            Return New Vector3(x, y, z)
        End Function

        Private Shared Function NextValue(args As String(), index As Integer) As String
            If index + 1 < args.Length Then Return args(index + 1)

            Return Nothing
        End Function

        Private Shared Function ParseInt(text As String, defaultValue As Integer) As Integer
            Dim value As Integer

            If Integer.TryParse(text, value) Then Return value

            Return defaultValue
        End Function

        Private Shared Function ParseDouble(text As String, defaultValue As Double) As Double
            Dim value As Double

            If Double.TryParse(text, Globalization.NumberStyles.Float,
                               Globalization.CultureInfo.InvariantCulture, value) Then
                Return value
            End If

            Return defaultValue
        End Function

        ''' <summary>在常见位置查找模型文件（输出目录 → 工作目录 → 仓库 models 目录）。</summary>
        Public Function ResolveModel(extension As String, explicitPath As String) As String
            If Not String.IsNullOrEmpty(explicitPath) Then
                If File.Exists(explicitPath) Then Return Path.GetFullPath(explicitPath)
            End If

            Dim name As String = "six_axis_arm" & extension
            Dim bases As String() = {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"),
                Path.Combine(Environment.CurrentDirectory, "models"),
                "G:\virtual-lab\models"
            }

            For Each basePath As String In bases
                Dim candidate As String = Path.Combine(basePath, name)

                If File.Exists(candidate) Then Return Path.GetFullPath(candidate)
            Next

            Return Nothing
        End Function

        Public Shared Sub PrintHelp()
            Console.WriteLine("VirtualWorkshop - 六轴机械臂虚拟硬件仿真工作台")
            Console.WriteLine()
            Console.WriteLine("用法: VirtualWorkshop [options]")
            Console.WriteLine()
            Console.WriteLine("  --headless           无界面高速仿真（只跑物理 + 协议服务）")
            Console.WriteLine("  --selftest           运行自检断言后退出")
            Console.WriteLine("  --inspect            打印模型与运动学信息后退出")
            Console.WriteLine("  --model <path>       glb 模型路径")
            Console.WriteLine("  --urdf <path>        URDF 路径")
            Console.WriteLine("  --port <n>           TCP 端口（默认 8900）")
            Console.WriteLine("  --http-port <n>      HTTP 端口（默认 8901）")
            Console.WriteLine("  --realtime <x>       仿真速度倍率（默认 1.0）")
            Console.WriteLine("  --blocks <n>         默认积木数量（默认 12）")
            Console.WriteLine("  --dynamic            使用动力学伺服驱动（默认运动学）")
            Console.WriteLine("  --camera <on|off>    启用/关闭夹爪第一视角相机（默认 on）")
            Console.WriteLine("  --camera-size <WxH>  相机分辨率（默认 320x240）")
            Console.WriteLine("  --camera-fov <deg>   垂直视场角（默认 60）")
            Console.WriteLine("  --camera-rate <hz>   自动捕获帧率（默认 15，0 表示只在请求时渲染）")
            Console.WriteLine("  --camera-offset x,y,z 相机在 link6 上的安装偏移（默认 0.045,0,0）")
            Console.WriteLine("  --no-server          不启动任何网络服务")
            Console.WriteLine("  --help               显示本帮助")
        End Sub

    End Class

End Namespace
