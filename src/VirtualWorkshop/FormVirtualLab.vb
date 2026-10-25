Imports System.Diagnostics
Imports System.Drawing
Imports System.Text
Imports System.Text.Json.Nodes
Imports System.Windows.Forms
Imports VirtualWorkshop.Configuration
Imports VirtualWorkshop.Model
Imports VirtualWorkshop.Protocol
Imports VirtualWorkshop.Rendering
Imports VirtualWorkshop.Sensing
Imports VirtualWorkshop.Simulation

''' <summary>虚拟硬件仿真工作台主窗体。</summary>
Public Class FormVirtualLab

    Private ReadOnly _options As SimulationOptions
    Private ReadOnly _preset As SimulationPreset
    Private ReadOnly _render As New RenderHost()
    Private ReadOnly _watch As Stopwatch = Stopwatch.StartNew()

    Private _lastTick As Double
    Private _uiAccumulator As Double
    Private _renderAccumulator As Double
    Private _frameCount As Integer
    Private _fps As Double
    Private _suppressSlider As Boolean
    Private _objectSignature As String
    Private _servoEnabled As Boolean = True

    Private _tcp As TcpServer
    Private _http As HttpServer

    ''' <summary>上一次显示的相机帧时间（避免同帧重复解码）。</summary>
    Private _lastCameraTime As Double = -1.0

    Public Sub New(options As SimulationOptions, preset As SimulationPreset)
        InitializeComponent()

        _options = If(options, New SimulationOptions())
        _preset = preset
    End Sub

    Private ReadOnly Property Host As SimulationHost
        Get
            Return _preset?.Host
        End Get
    End Property

    Private Sub FormVirtualLab_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Me.Text = "VirtualWorkshop · 六轴机械臂虚拟硬件仿真工作台"

        ' 将设计器中逐个声明的关节控件收集回数组，供后续逻辑按索引访问
        Me.m_jointBars = {Me.m_jointBar0, Me.m_jointBar1, Me.m_jointBar2, Me.m_jointBar3, Me.m_jointBar4, Me.m_jointBar5}
        Me.m_jointLabels = {Me.m_jointLabel0, Me.m_jointLabel1, Me.m_jointLabel2, Me.m_jointLabel3, Me.m_jointLabel4, Me.m_jointLabel5}
        Me.m_jointNames = {Me.m_jointName0, Me.m_jointName1, Me.m_jointName2, Me.m_jointName3, Me.m_jointName4, Me.m_jointName5}

        Call LogLine("工作台启动", UiTheme.Accent)

        If _preset Is Nothing OrElse Not _preset.Ready Then
            Call LogLine("仿真环境未就绪（模型或 URDF 加载失败）", UiTheme.Danger)
            m_timer.Enabled = False
            Return
        End If

        Call LogLine(_preset.Assembly.Describe())
        Call LogLine("模型: " & _preset.ModelFile)

        Try
            _render.Attach(m_canvas)
        Catch ex As Exception
            Call LogLine("三维视口初始化失败: " & ex.Message, UiTheme.Danger)
        End Try

        For Each bar As TrackBar In m_jointBars
            AddHandler bar.Scroll, AddressOf JointBarScroll
        Next

        Call StartServers()
        Call UpdateJointLabels()

        m_numRealtime.Value = CDecimal(_options.RealtimeFactor)
        _lastTick = _watch.Elapsed.TotalSeconds
        m_timer.Start()
    End Sub

    Private Sub FormVirtualLab_FormClosed(sender As Object, e As FormClosedEventArgs) Handles MyBase.FormClosed
        Try
            If m_picCamera IsNot Nothing AndAlso m_picCamera.Image IsNot Nothing Then
                m_picCamera.Image.Dispose()
                m_picCamera.Image = Nothing
            End If
        Catch ex As Exception
            ' 忽略
        End Try
    End Sub

    Private Shared Function CDecimal(value As Double) As Decimal
        Dim clamped As Double = System.Math.Max(0.0, System.Math.Min(20.0, value))

        Return CDec(clamped)
    End Function

    ' /********************************************************************************/
    '  主循环
    ' /********************************************************************************/

    Private Sub m_timer_Tick(sender As Object, e As EventArgs) Handles m_timer.Tick
        If Host Is Nothing Then Return

        Dim now As Double = _watch.Elapsed.TotalSeconds
        Dim elapsed As Double = now - _lastTick
        _lastTick = now

        If elapsed < 0.0 Then elapsed = 0.0
        If elapsed > 0.25 Then elapsed = 0.25

        Host.Dispatcher.RealtimeFactor = CDbl(m_numRealtime.Value)

        Try
            Call Host.Tick(elapsed)
        Catch ex As Exception
            Call LogLine("仿真异常: " & ex.Message, UiTheme.Danger)
            m_timer.Stop()
            Return
        End Try

        _renderAccumulator += elapsed

        If _renderAccumulator >= 0.033 Then
            _renderAccumulator = 0.0

            Try
                Call _render.Render(_preset.Simulation, Host.State)
                _frameCount += 1
            Catch ex As Exception
                Call LogLine("渲染失败: " & ex.Message, UiTheme.Danger)
                _render.Enabled = False
            End Try

            Call UpdateCameraPanel()
        End If

        _uiAccumulator += elapsed

        If _uiAccumulator >= 0.1 Then
            _fps = If(_uiAccumulator > 0, _frameCount / _uiAccumulator, 0)
            _frameCount = 0
            _uiAccumulator = 0.0

            Call UpdateDashboard()
        End If
    End Sub

    ' /********************************************************************************/
    '  面板
    ' /********************************************************************************/

    Private Sub UpdateJointLabels()
        If _preset Is Nothing OrElse _preset.Simulation Is Nothing Then Return

        Dim defs As List(Of ArmJointDef) = _preset.Simulation.JointDefs

        For i As Integer = 0 To m_jointBars.Length - 1
            If i >= defs.Count Then Continue For

            ' 每根滑轨映射到该关节的行程
            m_jointBars(i).Tag = defs(i).Name
        Next
    End Sub

    Private Sub UpdateDashboard()
        Dim state As StateSnapshot = Host?.State

        If state Is Nothing Then Return

        _suppressSlider = True

        Try
            For i As Integer = 0 To m_jointLabels.Length - 1
                If state.Q Is Nothing OrElse i >= state.Q.Length Then Continue For

                m_jointLabels(i).Text = state.Q(i).ToString("F3")

                Dim definition As ArmJointDef = Nothing

                If _preset.Simulation.JointDefs.Count > i Then definition = _preset.Simulation.JointDefs(i)

                If definition IsNot Nothing Then
                    Dim span As Double = definition.Upper - definition.Lower

                    If span > 1.0E-6 Then
                        Dim normalized As Double = (state.Q(i) - definition.Lower) / span
                        Dim value As Integer = CInt(System.Math.Round(normalized * 2000.0 - 1000.0))

                        m_jointBars(i).Value = System.Math.Max(-1000, System.Math.Min(1000, value))
                        m_jointLabels(i).ForeColor = If(Math.Abs(state.Q(i) - state.Joints(i).Target) > 0.05,
                                                        UiTheme.Warn, UiTheme.Accent)
                    End If
                End If
            Next

            If state.Tcp IsNot Nothing AndAlso state.Tcp.Length >= 6 Then
                m_lblTcp.Text =
                    $"x {state.Tcp(0):F3}  y {state.Tcp(1):F3}  z {state.Tcp(2):F3}" & vbCrLf &
                    $"rx {state.Tcp(3):F1}°  ry {state.Tcp(4):F1}°  rz {state.Tcp(5):F1}°"
            End If

            If state.Gripper IsNot Nothing Then
                m_lblGripper.Text = $"开口 {state.Gripper.Opening:F4} m   夹持力 {state.Gripper.Force:F0} N"

                If state.Gripper.Contact Then
                    m_lblGripper.Text &= $"   抓住 {state.Gripper.HoldingId}"
                End If
            End If

            Call UpdateObjectList(state)

            m_statusLabel.Text =
                $"t={state.Time:F2}s  步={state.StepIndex}  碰撞={state.Collisions?.Count}  " &
                $"指令={Host.ExecutedCommands}  {If(state.Estop, "急停", If(state.Enabled, "伺服使能", "伺服失能"))}"

            m_lblService.Text =
                $"TCP {If(_tcp Is Nothing, "关闭", _tcp.Port.ToString())}  " &
                $"HTTP {If(_http Is Nothing, "关闭", _http.Port.ToString())}  " &
                $"客户端 {If(_tcp Is Nothing, 0, _tcp.ClientCount)}"
        Finally
            _suppressSlider = False
        End Try
    End Sub

    ''' <summary>
    ''' 把夹爪相机的最新帧画到右侧卡片。
    ''' </summary>
    ''' <remarks>
    ''' 只读 <see cref="GripperCamera.Latest"/>（仿真线程发布的不可变帧），
    ''' 不在 UI 线程遍历物理世界；同一帧只解码一次，旧位图立刻释放。
    ''' </remarks>
    Private Sub UpdateCameraPanel()
        Dim camera As GripperCamera = Host?.Camera

        If camera Is Nothing OrElse m_picCamera Is Nothing Then Return

        Dim frame As CameraFrame = camera.Latest

        If frame Is Nothing Then
            m_lblCamera.Text = "等待相机第一帧…"
            Return
        End If

        If frame.Time = _lastCameraTime AndAlso m_picCamera.Image IsNot Nothing Then Return

        _lastCameraTime = frame.Time

        Try
            Dim bitmap As Bitmap = frame.ToBitmap()
            Dim previous As Image = m_picCamera.Image

            m_picCamera.Image = bitmap

            previous?.Dispose()
        Catch ex As Exception
            ' 画面渲染失败不影响仿真
        End Try

        Dim rpy As Double() = If(frame.Pose Is Nothing, Nothing, frame.Pose.RpyDegrees())

        m_lblCamera.Text = $"{frame.Width}x{frame.Height} · {camera.Fps:F1} fps · {camera.LastCaptureMs:F1} ms" &
                           If(rpy Is Nothing, "", vbCrLf & $"rpy {rpy(0):F0}° / {rpy(1):F0}° / {rpy(2):F0}°")
    End Sub

    Private Sub UpdateObjectList(state As StateSnapshot)
        If state.Objects Is Nothing Then Return

        Dim signature As New StringBuilder()

        For Each item As ObjectState In state.Objects
            signature.Append(item.Id).Append("|"c)
        Next

        Dim text As String = signature.ToString()

        If String.Equals(text, _objectSignature) Then Return

        _objectSignature = text
        m_lstObjects.BeginUpdate()

        Try
            m_lstObjects.Items.Clear()

            For Each item As ObjectState In state.Objects
                Dim position As String = ""

                If item.Position IsNot Nothing AndAlso item.Position.Length >= 3 Then
                    position = $"({item.Position(0):F2}, {item.Position(1):F2}, {item.Position(2):F2})"
                End If

                m_lstObjects.Items.Add($"{item.Id}  {item.Kind}  {position}")
            Next
        Finally
            m_lstObjects.EndUpdate()
        End Try
    End Sub

    ' /********************************************************************************/
    '  交互
    ' /********************************************************************************/

    Private Sub JointBarScroll(sender As Object, e As EventArgs)
        If _suppressSlider Then Return
        If _preset Is Nothing OrElse _preset.Simulation Is Nothing Then Return

        Dim index As Integer = Array.IndexOf(m_jointBars, DirectCast(sender, TrackBar))

        If index < 0 OrElse index >= _preset.Simulation.JointDefs.Count Then Return

        Dim definition As ArmJointDef = _preset.Simulation.JointDefs(index)
        Dim bar As TrackBar = DirectCast(sender, TrackBar)
        Dim ratio As Double = (bar.Value + 1000.0) / 2000.0
        Dim angle As Double = definition.Lower + ratio * (definition.Upper - definition.Lower)

        Call Host.Hardware.StopMotion()
        Call _preset.Simulation.SetJointTarget(index, angle)
    End Sub

    Private Sub m_btnStart_Click(sender As Object, e As EventArgs) Handles m_btnStart.Click
        Call StartServers()
    End Sub

    Private Sub m_btnStopServer_Click(sender As Object, e As EventArgs) Handles m_btnStopServer.Click
        Call StopServers()
    End Sub

    Private Sub m_btnEnable_Click(sender As Object, e As EventArgs) Handles m_btnEnable.Click
        _servoEnabled = Not _servoEnabled

        Try
            Call Host.ExecuteNow(ProtocolText.CmdEnable,
                                 New JsonObject From {{"on", _servoEnabled}})
            Call LogLine($"伺服{If(_servoEnabled, "使能", "失能")}", UiTheme.Accent)
        Catch ex As Exception
            Call LogLine("使能失败: " & ex.Message, UiTheme.Danger)
        End Try
    End Sub

    Private Sub m_btnHome_Click(sender As Object, e As EventArgs) Handles m_btnHome.Click
        Call Execute("home", New JsonObject From {{"speed", 0.35}}, "回零")
    End Sub

    Private Sub m_btnHold_Click(sender As Object, e As EventArgs) Handles m_btnHold.Click
        Host?.Hardware.StopMotion()
        Call LogLine("保持当前位置", UiTheme.AccentDim)
    End Sub

    Private Sub m_btnEstop_Click(sender As Object, e As EventArgs) Handles m_btnEstop.Click
        Call Execute("estop", New JsonObject(), "急停", UiTheme.Danger)
    End Sub

    Private Sub m_btnReset_Click(sender As Object, e As EventArgs) Handles m_btnReset.Click
        _render.Overlay.Clear()
        _objectSignature = Nothing

        Call Execute("reset", New JsonObject(), "复位场景", UiTheme.Warn)
    End Sub

    Private Sub m_btnGrip_Click(sender As Object, e As EventArgs) Handles m_btnGrip.Click
        Call Execute("grip", New JsonObject From {{"force", CDbl(m_numGripForce.Value)}}, "夹紧")
    End Sub

    Private Sub m_btnRelease_Click(sender As Object, e As EventArgs) Handles m_btnRelease.Click
        Call Execute("release", New JsonObject(), "张开")
    End Sub

    Private Sub Execute(command As String, args As JsonObject, description As String,
                         Optional color As Color = Nothing)
        If Host Is Nothing Then Return

        Try
            Call Host.ExecuteNow(command, args)
            Call LogLine(description & " → 已执行", If(color.IsEmpty, UiTheme.TextMuted, color))
        Catch ex As ProtocolException
            Call LogLine($"{description} 失败 [{ex.Code}]: {ex.Message}", UiTheme.Danger)
        Catch ex As Exception
            Call LogLine($"{description} 异常: {ex.Message}", UiTheme.Danger)
        End Try
    End Sub

    ' /********************************************************************************/
    '  服务
    ' /********************************************************************************/

    Private Sub StartServers()
        If _preset Is Nothing OrElse Not _preset.Ready Then Return

        Try
            If _options.EnableTcp AndAlso _tcp Is Nothing Then
                _tcp = New TcpServer(Host, _options.TcpPort)
                AddHandler _tcp.Log, AddressOf NetworkLog
                _tcp.Start()
            End If

            If _options.EnableHttp AndAlso _http Is Nothing Then
                _http = New HttpServer(Host, _options.HttpPort)
                AddHandler _http.Log, AddressOf NetworkLog
                _http.Start()
            End If
        Catch ex As Exception
            Call LogLine("服务启动失败: " & ex.Message, UiTheme.Danger)
        End Try
    End Sub

    Private Sub StopServers()
        Try
            _tcp?.Stop()
        Catch ex As Exception
            ' 忽略
        End Try

        Try
            _http?.Stop()
        Catch ex As Exception
            ' 忽略
        End Try

        _tcp = Nothing
        _http = Nothing
    End Sub

    Private Sub NetworkLog(text As String)
        If Me.InvokeRequired Then
            Me.BeginInvoke(New Action(Of String)(AddressOf NetworkLog), text)
            Return
        End If

        Call LogLine(text, UiTheme.AccentDim)
    End Sub

    Private Sub LogLine(text As String, Optional color As Color = Nothing)
        Dim line As String = $"[{DateTime.Now:HH:mm:ss}] {text}"

        ' 同时落盘，便于在没有控制台的 GUI 模式下排查问题
        Try
            IO.File.AppendAllText(IO.Path.Combine(IO.Path.GetTempPath(), "virtualworkshop.log"), line & vbCrLf)
        Catch ex As Exception
            ' 忽略
        End Try

        If m_log Is Nothing Then Return

        If m_log.Lines.Length > 500 Then
            m_log.Clear()
        End If

        m_log.SelectionStart = m_log.TextLength
        m_log.SelectionLength = 0
        m_log.SelectionColor = If(color.IsEmpty, UiTheme.TextPrimary, color)
        m_log.AppendText(line & vbCrLf)
        m_log.SelectionColor = UiTheme.TextPrimary
        m_log.ScrollToCaret()
    End Sub

    Private Sub FormVirtualLab_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        m_timer.Stop()
        Call StopServers()
        _render.Enabled = False
        _render.Builder.ClearBrushes()
    End Sub

    Private Sub FormVirtualLab_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        Select Case e.KeyCode
            Case Keys.F5
                Call StartServers()
            Case Keys.F9
                Call Execute("estop", New JsonObject(), "急停", UiTheme.Danger)
            Case Keys.D0, Keys.NumPad0
                _render.ResetView()
        End Select
    End Sub

    Private Sub m_jointName2_Click(sender As Object, e As EventArgs) Handles m_jointName2.Click

    End Sub
End Class
