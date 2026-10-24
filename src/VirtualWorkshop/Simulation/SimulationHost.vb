Imports System.Collections.Concurrent
Imports System.Diagnostics
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks
Imports VirtualWorkshop.Protocol
Imports VirtualWorkshop.Sensing

Namespace Simulation

    ''' <summary>一条等待仿真线程执行的指令。</summary>
    Public Class PendingCall

        Public Property Command As String
        Public Property Arguments As JsonObject
        Public ReadOnly Property Completion As New TaskCompletionSource(Of JsonObject)()

    End Class

    ''' <summary>
    ''' 仿真宿主：把"指令队列 + 固定步长推进 + 状态快照"粘在一起。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 网络线程只调用 <see cref="Post"/> 把指令塞进并发队列，然后等 Task；
    ''' 仿真线程（GUI 定时器或 headless 循环）在 <see cref="Tick"/> 里批量取出并执行。
    ''' 这样物理世界永远只被一个线程访问，不需要任何锁。
    ''' </para>
    ''' <para>
    ''' <c>wait_motion</c> 例外：它只轮询 <see cref="ArmHardware.IsMotionDone"/>，
    ''' 不改变世界状态，因此直接在调用线程上等待，避免"等待一条指令时仿真停住"的死锁。
    ''' </para>
    ''' </remarks>
    Public Class SimulationHost

        Public ReadOnly Property Simulation As ArmSimulation
        Public ReadOnly Property Hardware As ArmHardware
        Public ReadOnly Property Dispatcher As CommandDispatcher
        Public ReadOnly Property Clock As New SimulationClock()

        Private ReadOnly _camera As GripperCamera

        ''' <summary>夹爪第一视角相机（可能为空：用 --camera off 关闭时）。</summary>
        Public ReadOnly Property Camera As GripperCamera
            Get
                Return _camera
            End Get
        End Property

        ''' <summary>最新的状态快照（仿真线程发布）。</summary>
        Public Property State As StateSnapshot

        ''' <summary>平滑后的物理步/秒。</summary>
        Public Property StepsPerSecond As Double

        ''' <summary>上一次 Tick 推进的物理步数。</summary>
        Public Property LastFrameSteps As Integer

        ''' <summary>仿真线程累计执行过的指令数。</summary>
        Public Property ExecutedCommands As Long

        Private ReadOnly _pending As New ConcurrentQueue(Of PendingCall)()
        Private _rateAccumulator As Double
        Private _rateSteps As Integer

        Public Sub New(simulation As ArmSimulation, hardware As ArmHardware,
                       Optional camera As GripperCamera = Nothing)

            If simulation Is Nothing Then Throw New ArgumentNullException(NameOf(simulation))

            Me.Simulation = simulation
            Me.Hardware = If(hardware, New ArmHardware(simulation))
            Me.Dispatcher = New CommandDispatcher(simulation, Me.Hardware)
            Me.State = Me.Hardware.State

            _camera = camera

            ' 让指令层也能访问相机（frame / camera 指令）
            Me.Dispatcher.Camera = camera

            Call AttachCamera()
        End Sub

        ''' <summary>从任意线程投递一条指令，返回结果 Task。</summary>
        Public Function Post(command As String, args As JsonObject) As Task(Of JsonObject)
            If String.Equals(command, ProtocolText.CmdWaitMotion, StringComparison.OrdinalIgnoreCase) Then
                Return Task.FromResult(WaitMotion(args))
            End If

            Dim pending As New PendingCall With {
                .Command = command,
                .Arguments = args
            }

            _pending.Enqueue(pending)

            Return pending.Completion.Task
        End Function

        ''' <summary>在仿真线程内直接执行（GUI 按钮、自检使用）。</summary>
        Public Function ExecuteNow(command As String, args As JsonObject) As JsonObject
            Return Dispatcher.Execute(command, args)
        End Function

        ''' <summary>仿真线程：处理指令 → 按真实经过时间推进物理 → 发布快照。</summary>
        Public Sub Tick(realSeconds As Double)
            Call ProcessPending()

            Clock.RealtimeFactor = Dispatcher.RealtimeFactor

            Dim steps As Integer = Clock.Consume(realSeconds, Simulation.World.FixedDt)

            For i As Integer = 1 To steps
                Call Hardware.Tick()
            Next

            LastFrameSteps = steps

            Call CaptureCamera()

            Me.State = Hardware.State
            Call AttachCamera()

            Call UpdateRate(realSeconds, steps)
        End Sub

        ''' <summary>精确推进 N 个物理步（headless 的加速/单步接口）。</summary>
        Public Sub StepFixed(steps As Integer)
            Call ProcessPending()

            For i As Integer = 1 To System.Math.Max(0, steps)
                Call Hardware.Tick()
            Next

            Call CaptureCamera()

            Me.State = Hardware.State
            Call AttachCamera()
        End Sub

        ' /********************************************************************************/
        '  夹爪相机
        ' /********************************************************************************/

        ''' <summary>按相机配置的帧率捕获一帧（不满足间隔时什么都不做）。</summary>
        Private Sub CaptureCamera()
            If _camera Is Nothing Then Return

            Try
                Call _camera.MaybeCapture(Simulation)
            Catch ex As Exception
                ' 相机永远不能让仿真 tick 失败
            End Try
        End Sub

        ''' <summary>把相机摘要写进状态快照。</summary>
        Private Sub AttachCamera()
            If _camera Is Nothing OrElse Me.State Is Nothing Then Return

            Try
                Me.State.Camera = BuildCameraState()
            Catch ex As Exception
                ' 忽略摘要失败
            End Try
        End Sub

        Private Function BuildCameraState() As CameraState
            Dim config As CameraConfig = _camera.Config

            Dim summary As New CameraState With {
                .Enabled = config.Enabled,
                .Width = config.Width,
                .Height = config.Height,
                .FieldOfView = config.FieldOfView,
                .FocalPixels = config.FocalPixels,
                .Rate = config.Rate,
                .Fps = _camera.Fps,
                .CaptureMs = _camera.LastCaptureMs
            }

            Dim frame As CameraFrame = _camera.Latest

            If frame IsNot Nothing Then
                summary.Time = frame.Time
                summary.StepIndex = frame.StepIndex

                If frame.Pose IsNot Nothing Then
                    summary.Position = StateSnapshot.ToArray(frame.Pose.Position)
                    summary.Rpy = frame.Pose.RpyDegrees()
                End If
            End If

            Return summary
        End Function

        Private Sub ProcessPending()
            Dim pending As PendingCall = Nothing
            Dim guard As Integer = 0

            While _pending.TryDequeue(pending)
                guard += 1

                If guard > 256 Then Exit While

                Dim result As JsonObject

                Try
                    result = Dispatcher.Execute(pending.Command, pending.Arguments)
                Catch ex As ProtocolException
                    result = ErrorResponse(ex.Code, ex.Message)
                Catch ex As Exception
                    result = ErrorResponse(ProtocolText.ErrInternal, ex.Message)
                End Try

                ExecutedCommands += 1
                pending.Completion.TrySetResult(result)
            End While
        End Sub

        Private Shared Function ErrorResponse(code As String, message As String) As JsonObject
            Return New JsonObject From {
                {ProtocolText.FieldOk, False},
                {ProtocolText.FieldError, ProtocolText.ErrorNode(code, message)}
            }
        End Function

        Private Function WaitMotion(args As JsonObject) As JsonObject
            Dim id As Integer = CInt(JsonText.Num(args, "id", 0))
            Dim timeout As Integer = CInt(JsonText.Num(args, "timeout_ms", 30000))
            Dim watch As Stopwatch = Stopwatch.StartNew()

            While Not Hardware.IsMotionDone(id)
                If watch.ElapsedMilliseconds > timeout Then
                    Return New JsonObject From {
                        {"id", id},
                        {"done", False},
                        {"reason", "timeout"}
                    }
                End If

                Thread.Sleep(2)
            End While

            Return New JsonObject From {
                {"id", id},
                {"done", True},
                {"success", Hardware.MotionSucceeded(id)},
                {"message", Hardware.MotionMessage(id)}
            }
        End Function

        Private Sub UpdateRate(realSeconds As Double, steps As Integer)
            _rateAccumulator += System.Math.Max(1.0E-4, realSeconds)
            _rateSteps += steps

            If _rateAccumulator >= 0.5 Then
                StepsPerSecond = _rateSteps / _rateAccumulator
                _rateAccumulator = 0.0
                _rateSteps = 0
            End If
        End Sub

    End Class

End Namespace
