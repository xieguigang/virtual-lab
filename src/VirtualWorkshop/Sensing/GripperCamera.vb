Imports System.Diagnostics
Imports System.Threading
Imports Microsoft.VisualBasic.Engine.Physics
Imports VirtualWorkshop.Simulation

Namespace Sensing

    ''' <summary>
    ''' 夹爪第一视角相机：负责解析外参、按帧率节流、调用光栅化并发布最新帧。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' <b>线程约定</b>：<see cref="MaybeCapture"/> 与 <see cref="CaptureNow"/> 只能在仿真线程调用
    ''' （GUI 的定时器 / headless 循环 / 指令队列处理都在这个线程上），
    ''' UI 与网络线程只读 <see cref="Latest"/>，因此不需要任何锁。
    ''' </para>
    ''' <para>
    ''' 渲染异常一律被吞掉并记入 <see cref="LastError"/>，保留上一帧，绝不让相机把仿真搞崩。
    ''' </para>
    ''' </remarks>
    Public Class GripperCamera

        Public ReadOnly Property Config As CameraConfig

        Private ReadOnly _buffer As New SceneTriangleBuffer()
        Private ReadOnly _rasterizer As New SoftwareRasterizer()

        Private _rgb As Byte()
        Private _depth As Single()

        Private _latest As CameraFrame
        Private _lastCaptureTime As Double = Double.MinValue
        Private _lastFrameTime As Double
        Private _fps As Double
        Private _captureMs As Double
        Private _captures As Long
        Private _faulted As Boolean
        Private _lastError As String

        Public Sub New(Optional config As CameraConfig = Nothing)
            Me.Config = If(config, New CameraConfig())

            Call Me.Config.Clamp()
            Call EnsureBuffers()
        End Sub

        ''' <summary>最新一帧（可能为 Nothing）。</summary>
        Public ReadOnly Property Latest As CameraFrame
            Get
                Return Volatile.Read(_latest)
            End Get
        End Property

        ''' <summary>最近一次渲染耗时（毫秒）。</summary>
        Public ReadOnly Property LastCaptureMs As Double
            Get
                Return _captureMs
            End Get
        End Property

        ''' <summary>实际帧率（按仿真时间平滑）。</summary>
        Public ReadOnly Property Fps As Double
            Get
                Return _fps
            End Get
        End Property

        Public ReadOnly Property CaptureCount As Long
            Get
                Return _captures
            End Get
        End Property

        Public ReadOnly Property Faulted As Boolean
            Get
                Return _faulted
            End Get
        End Property

        Public ReadOnly Property LastError As String
            Get
                Return _lastError
            End Get
        End Property

        Public ReadOnly Property TriangleCount As Integer
            Get
                Return _buffer.Count
            End Get
        End Property

        ' /********************************************************************************/
        '  捕获
        ' /********************************************************************************/

        ''' <summary>按配置的帧率节流捕获；不该出帧时返回 Nothing。</summary>
        Public Function MaybeCapture(simulation As ArmSimulation) As CameraFrame
            If Not Config.Enabled OrElse simulation Is Nothing Then Return Nothing
            If Config.Rate <= 0 Then Return Nothing

            Dim interval As Double = 1.0 / Config.Rate

            If _latest IsNot Nothing AndAlso (simulation.Time - _lastCaptureTime) < interval Then
                Return Nothing
            End If

            Return CaptureNow(simulation)
        End Function

        ''' <summary>立刻渲染一帧（忽略节流），并把它设为最新帧。</summary>
        Public Function CaptureNow(simulation As ArmSimulation) As CameraFrame
            If simulation Is Nothing Then Return Nothing

            Try
                Call EnsureBuffers()

                Dim pose As CameraPose = ResolvePose(simulation)

                If pose Is Nothing Then Return _latest

                _buffer.Build(simulation)

                Dim watch As Stopwatch = Stopwatch.StartNew()

                _rasterizer.Render(_buffer, pose, Config, _rgb, _depth)

                _captureMs = watch.Elapsed.TotalMilliseconds
                _captures += 1

                Dim frame As New CameraFrame With {
                    .Width = Config.Width,
                    .Height = Config.Height,
                    .Rgb = _rgb,
                    .Depth = _depth,
                    .Time = simulation.Time,
                    .StepIndex = simulation.StepCount,
                    .Pose = pose
                }

                Volatile.Write(_latest, frame)

                Call UpdateRate(simulation.Time)

                _lastCaptureTime = simulation.Time
                _faulted = False
                _lastError = Nothing

                Return frame
            Catch ex As Exception
                _faulted = True
                _lastError = ex.Message

                Return _latest
            End Try
        End Function

        ''' <summary>配置被改动后调用：重新夹取参数并强制重建缓冲。</summary>
        Public Sub Invalidate()
            Call Config.Clamp()
            Call EnsureBuffers(force:=True)
        End Sub

        ' /********************************************************************************/
        '  内部
        ' /********************************************************************************/

        Private Sub EnsureBuffers(Optional force As Boolean = False)
            Dim pixels As Integer = Config.Pixels

            If Not force AndAlso _rgb IsNot Nothing AndAlso _rgb.Length = pixels * 3 Then Return

            _rgb = New Byte(pixels * 3 - 1) {}
            _depth = New Single(pixels - 1) {}
        End Sub

        ''' <summary>取 link6 的刚体位姿，叠加安装偏移。</summary>
        Private Function ResolvePose(simulation As ArmSimulation) As CameraPose
            Dim body As RigidBody3D = Nothing

            If Not String.IsNullOrEmpty(simulation.Assembly.TcpLink) Then
                simulation.LinkBodies.TryGetValue(simulation.Assembly.TcpLink, body)
            End If

            If body Is Nothing Then
                simulation.LinkBodies.TryGetValue("link6", body)
            End If

            If body Is Nothing AndAlso simulation.LinkBodies.Count > 0 Then
                For Each pair As KeyValuePair(Of String, RigidBody3D) In simulation.LinkBodies
                    body = pair.Value
                Next
            End If

            If body Is Nothing Then Return Nothing

            Return CameraPose.FromLink(body.Position, body.Orientation, Config.Offset)
        End Function

        Private Sub UpdateRate(simulationTime As Double)
            If _lastFrameTime > 0 Then
                Dim delta As Double = simulationTime - _lastFrameTime

                If delta > 1.0E-6 Then
                    Dim instant As Double = 1.0 / delta

                    _fps = If(_fps <= 0, instant, _fps * 0.85 + instant * 0.15)
                End If
            End If

            _lastFrameTime = simulationTime
        End Sub

        Public Function Describe() As String
            Dim pose As CameraFrame = _latest

            If pose Is Nothing Then
                Return $"{Config.Width}x{Config.Height} · fov {Config.FieldOfView:F0}° · 未捕获"
            End If

            Return $"{Config.Width}x{Config.Height} · {_fps:F1} fps · {_captureMs:F1} ms"
        End Function

    End Class

End Namespace
