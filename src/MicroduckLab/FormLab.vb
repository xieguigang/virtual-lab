Imports System.Collections.Concurrent
Imports System.Numerics
Imports MicroduckLab.Assets
Imports MicroduckLab.Audio
Imports MicroduckLab.Core
Imports MicroduckLab.Inputs
Imports MicroduckLab.Render
Imports MicroduckLab.UI
Imports DxArgs = Microsoft.VisualBasic.Drawing.DirectX.DxRenderEventArgs
Imports DxWin = Microsoft.VisualBasic.Drawing.DirectX

''' <summary>
''' The window of the playground: one directx canvas that fills it, the 2d shell
''' drawn on top of every frame and the input of the keyboard, the mouse and the
''' pad routed into the session.
''' </summary>
''' <remarks>
''' https://huggingface.co/spaces/pollen-robotics/microduck-simulator/tree/main
''' </remarks>
Public Class FormLab

    Private ReadOnly m_canvas As New DxWin.DxCanvas()
    Private ReadOnly m_session As New SimSession()
    Private ReadOnly m_host As New SceneHost()
    Private ReadOnly m_ui As New UiLayer()
    Private ReadOnly m_bank As New SoundBank()
    Private ReadOnly m_audio As AudioDirector

    Private ReadOnly m_trace As New ConcurrentQueue(Of String)()
    Private ReadOnly m_held As New HashSet(Of Keys)()
    Private ReadOnly m_frameClock As Stopwatch = Stopwatch.StartNew()

    Private ReadOnly m_toasts As New ConcurrentQueue(Of String)()
    Private m_renderTimer As System.Windows.Forms.Timer
    Private m_pollTimer As System.Windows.Forms.Timer
    Private m_booted As Boolean
    Private m_closing As Boolean

    Private m_dragging As Boolean
    Private m_dragFrom As Point
    Private m_dragDistance As Integer
    Private m_scheme As Integer
    Private m_pad As PadState
    Private m_padPrevious As PadState
    Private m_padHoldSteps As Integer
    Private m_waypoint As Vector3?
    Private m_lastFrame As Double
    ''' <summary>the clock of the entrance, one at the start and zero when it is done</summary>
    Private m_entrance As Double = 1.0

    Public Sub New()
        Call InitializeComponent()

        Me.Text = "Microduck Laboratory"
        Me.ClientSize = New Size(1280, 720)
        Me.MinimumSize = New Size(960, 600)
        Me.BackColor = Color.FromArgb(255, 8, 8, 12)
        Me.KeyPreview = True
        Me.DoubleBuffered = True
        Me.StartPosition = FormStartPosition.CenterScreen

        m_audio = New AudioDirector(m_bank)
    End Sub

    ' /********************************************************************************/
    '   the life cycle
    ' /********************************************************************************/

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)

        m_canvas.Dock = DockStyle.Fill
        m_canvas.BackColor = Color.FromArgb(255, 8, 8, 12)
        m_canvas.AutoClear = True

        Call Me.Controls.Add(m_canvas)

        Call m_host.Attach(m_canvas)

        ' the ui is drawn after the 3d pass of the host, on the same canvas
        AddHandler m_canvas.Render, AddressOf OnUiPaint
        AddHandler m_canvas.MouseDown, AddressOf OnCanvasMouseDown
        AddHandler m_canvas.MouseMove, AddressOf OnCanvasMouseMove
        AddHandler m_canvas.MouseUp, AddressOf OnCanvasMouseUp
        AddHandler m_canvas.MouseWheel, AddressOf OnCanvasMouseWheel

        m_host.Session = m_session

        AddHandler m_session.BootTrace, AddressOf OnBootTrace
        AddHandler m_session.Footstep, Sub(left, strength) m_audio.Footstep(left, strength)
        AddHandler m_session.Impact, Sub(strength) m_audio.Impact(strength)
        AddHandler m_session.Bump, Sub(strength) m_audio.Bump(strength)
        AddHandler m_session.Quacked, Sub() m_audio.Quack(DuckVariants.All(m_scheme).Voice)
        AddHandler m_session.ModeChanged, AddressOf OnModeChanged

        Call m_ui.Console.Ok("power on")
        Call m_ui.Console.Info($"assets: {AssetPath.Display(AssetPath.Root)}")

        Call m_bank.Preload(AssetPath.MediaFile("sfx"), "*.wav")
        Call m_bank.Preload(AssetPath.MediaFile("voices"), "*.wav")
        Call m_audio.Prepare()

        Call m_ui.Console.Ok($"{m_bank.LoadedCount} sound files cached")

        ' the timers of the windows forms are the ones that tick on the ui
        ' thread: the pose of the frame must not be written while it is painted
        m_renderTimer = New System.Windows.Forms.Timer With {.Interval = 16}
        m_pollTimer = New System.Windows.Forms.Timer With {.Interval = 33}

        AddHandler m_renderTimer.Tick, AddressOf OnRenderTick
        AddHandler m_pollTimer.Tick, AddressOf OnPollTick

        Call m_renderTimer.Start()
        Call m_pollTimer.Start()

        Call Task.Run(AddressOf BootWorker)
    End Sub

    Private Sub BootWorker()
        Try
            Call m_session.Boot()

            m_booted = True

            Call m_session.Start()
            Call m_audio.Boot()
        Catch ex As Exception
            m_ui.BootFailed = True
            m_ui.BootMessage = ex.Message

            Call m_ui.Console.Error(ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        m_closing = True

        Call m_renderTimer?.Dispose()
        Call m_pollTimer?.Dispose()
        Call m_session.Dispose()
        Call m_host.Dispose()
        Call m_audio.Dispose()
        Call m_bank.Dispose()

        MyBase.OnFormClosing(e)
    End Sub

    ' /********************************************************************************/
    '   the frame
    ' /********************************************************************************/

    Private Sub OnRenderTick(sender As Object, e As EventArgs)
        If m_closing OrElse Me.IsDisposed Then
            Return
        End If

        Try
            Dim now As Double = m_frameClock.Elapsed.TotalSeconds
            Dim dt As Double = Math.Min(now - m_lastFrame, 0.1)

            m_lastFrame = now

            Call DrainTrace()

            m_ui.BootProgress = If(m_booted, 1.0, Math.Min(0.95, m_ui.BootProgress + 0.01))
            m_ui.Console.Complete = m_booted

            If m_booted AndAlso m_ui.Screen = UiScreen.Boot AndAlso Not m_ui.BootFailed Then
                m_ui.Screen = UiScreen.Title
                m_ui.SchemeName = DuckVariants.All(m_scheme).Label

                Call m_host.PrepareRig()
                Call m_session.SetInputLocked(True)
            End If

            If m_ui.Screen = UiScreen.Play Then
                Call m_host.Update(dt)
                Call AnimateEntrance(dt)
            End If

            If Not m_closing Then
                Call m_canvas.Invalidate()
            End If
        Catch ex As Exception
            Call m_ui.Console.Error($"frame: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' the entrance of the browser build: the grid draws itself in and the duck
    ''' is materialised by a scan that runs up its body
    ''' </summary>
    Private Sub AnimateEntrance(dt As Double)
        If m_entrance <= 0 Then
            Return
        End If

        m_entrance = Math.Max(0, m_entrance - dt / 1.9)

        Dim progress As Double = 1.0 - m_entrance

        m_host.Reveal = Math.Min(1.0, progress * 2.0)

        If progress <= 0.5 Then
            m_host.ScanY = CSng(-0.02)
            m_host.DuckAlpha = 0.35F
        Else
            Dim scan As Double = (progress - 0.5) * 2.0
            Dim low As Single = m_host.Snapshot.TrunkPosition.Z - 0.09F

            m_host.ScanY = low + CSng(scan) * 0.24F
            m_host.DuckAlpha = Math.Min(1.0F, CSng(0.35 + scan * 0.65))
        End If

        If m_entrance <= 0 Then
            m_host.ScanY = Nothing
            m_host.DuckAlpha = 1.0F
            m_host.Reveal = 1.0
            Call m_audio.Sweep()
        End If
    End Sub

    Private Sub DrainTrace()
        Dim line As String = Nothing
        Dim toast As String = Nothing

        While m_trace.TryDequeue(line)
            Call m_ui.Console.Ok(line)
        End While

        While m_toasts.TryDequeue(toast)
            Call m_ui.ShowToast(toast)
        End While
    End Sub

    Private Sub OnBootTrace(line As String)
        Call m_trace.Enqueue(line)
    End Sub

    Private Sub OnModeChanged()
        ' the event is raised from the control thread, the ui is touched from
        ' the tick of the render timer
        Call m_toasts.Enqueue(m_session.Machine.Label.ToUpperInvariant())
    End Sub

    ''' <summary>
    ''' the 2d shell of the frame: the crt of the overlay and the ui
    ''' </summary>
    Private Sub OnUiPaint(sender As Object, e As DxArgs)
        If m_closing Then
            Return
        End If

        If m_ui.Screen <> UiScreen.Boot Then
            Call m_ui.PaintCrt(e.Graphics, e.Size)
        End If

        Call m_ui.Paint(e.Graphics, e.Size, m_host.Snapshot)
    End Sub

    ' /********************************************************************************/
    '   the input
    ' /********************************************************************************/

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)

        If e.KeyCode = Keys.Escape Then
            Call ToggleMenu()

            Return
        End If

        If m_ui.Screen = UiScreen.Title Then
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then
                Call [Enter]()

                Return
            End If
        ElseIf m_ui.Screen = UiScreen.Play Then
            If Not m_held.Add(e.KeyCode) Then
                Return
            End If

            Call HandleAction(e.KeyCode)
            Call RefreshTwist()

            Return
        End If

        Call m_held.Add(e.KeyCode)
    End Sub

    Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
        MyBase.OnKeyUp(e)

        Call m_held.Remove(e.KeyCode)
        Call RefreshTwist()
    End Sub

    Protected Overrides Sub OnDeactivate(e As EventArgs)
        MyBase.OnDeactivate(e)

        Call m_held.Clear()
        Call m_session.SetTwist(0, 0)
    End Sub

    Private Sub HandleAction(key As Keys)
        Select Case key
            Case Keys.Space
                Call m_session.ResetSim()
                Call m_audio.Sweep()
            Case Keys.C
                m_host.Rig.Chase = Not m_host.Rig.Chase
                Call m_ui.ShowToast(If(m_host.Rig.Chase, "CHASE CAMERA", "ORBIT CAMERA"))
            Case Keys.V
                Call m_host.Rig.LookDown()
                Call m_ui.ShowToast("TOP VIEW")
            Case Keys.B
                Call m_host.Rig.LookLevel()
                Call m_ui.ShowToast("LEVEL VIEW")
            Case Keys.M
                Call SwitchShape()
            Case Keys.R
                Call m_session.ToggleSit()
            Case Keys.G
                Call m_session.TriggerGroundPick()
            Case Keys.Q
                Call m_session.TriggerKick(True)
            Case Keys.E
                Call m_session.TriggerKick(False)
            Case Keys.F
                Call m_session.TriggerKick(m_ui.Toast <> "KICK RIGHT")
                m_ui.Toast = "KICK RIGHT"
            Case Keys.D1
                Call SelectScheme(0)
            Case Keys.D2
                Call SelectScheme(1)
            Case Keys.D3
                Call SelectScheme(2)
            Case Keys.D4
                Call SelectScheme(3)
        End Select
    End Sub

    Private Sub RefreshTwist()
        Dim forward As Double = 0
        Dim turn As Double = 0

        If m_held.Contains(Keys.W) OrElse m_held.Contains(Keys.Up) Then forward += 1
        If m_held.Contains(Keys.S) OrElse m_held.Contains(Keys.Down) Then forward -= 1
        If m_held.Contains(Keys.A) OrElse m_held.Contains(Keys.Left) Then turn -= 1
        If m_held.Contains(Keys.D) OrElse m_held.Contains(Keys.Right) Then turn += 1

        If forward <> 0 OrElse turn <> 0 Then
            m_waypoint = Nothing
        End If

        Call m_session.SetTwist(forward, turn)
    End Sub

    Private Sub OnCanvasMouseDown(sender As Object, e As MouseEventArgs)
        m_dragging = True
        m_dragFrom = e.Location
        m_dragDistance = 0
    End Sub

    Private Sub OnCanvasMouseMove(sender As Object, e As MouseEventArgs)
        If Not m_dragging Then
            Call m_ui.HitTest(e.Location)

            Return
        End If

        Dim dx As Integer = e.X - m_dragFrom.X
        Dim dy As Integer = e.Y - m_dragFrom.Y

        m_dragDistance += Math.Abs(dx) + Math.Abs(dy)
        m_dragFrom = e.Location

        If m_dragDistance > 4 Then
            ' the browser build drags the scene with the pointer (three.js
            ' OrbitControls): the eye swings the other way around the target. The
            ' projection of the desktop renderer is mirrored on the horizontal
            ' axis, hence the flipped sign of the yaw here. Pulling the pointer
            ' down tips the eye down onto the floor (a birds eye view), pulling it
            ' up lifts the eye until it looks along the floor.
            Call m_host.Rig.Orbit(dx * 0.35, -dy * 0.25)
        End If
    End Sub

    Private Sub OnCanvasMouseUp(sender As Object, e As MouseEventArgs)
        m_dragging = False

        If m_dragDistance > 4 Then
            Return
        End If

        Dim action As UiAction = m_ui.HitTest(e.Location)

        If action <> UiAction.None Then
            Call m_audio.Click()
            Call Execute(action)

            Return
        End If

        If m_ui.Screen <> UiScreen.Play Then
            Return
        End If

        Dim point As Vector3? = m_host.PickFloor(New PointF(e.X, e.Y), m_canvas.Size)

        If point.HasValue AndAlso Math.Abs(point.Value.X) < SimConstants.ArenaHalf Then
            m_waypoint = point

            Return
        End If

        ' a click on the duck itself is the quack of the browser build
        Call m_session.Quack()
        Call m_session.SetJaw(1)
    End Sub

    Private Sub OnCanvasMouseWheel(sender As Object, e As MouseEventArgs)
        ' a wheel notch towards the user (positive delta) zooms out in the browser
        ' build, so the step of the rig is negative here
        Call m_host.Rig.Zoom(-e.Delta / 120.0)
    End Sub

    Private Sub Execute(action As UiAction)
        Select Case action
            Case UiAction.EnterGame, UiAction.ResumeGame
                Call [Enter]()
            Case UiAction.Pause
                Call ToggleMenu()
            Case UiAction.Reset
                Call m_session.ResetSim()
                m_ui.Paused = False
            Case UiAction.Title
                m_ui.Screen = UiScreen.Title
                m_ui.Paused = False
                Call m_session.SetInputLocked(True)
            Case UiAction.Quit
                Call Me.Close()
            Case UiAction.Controls
                m_ui.Panel = TitlePanel.Controls
            Case UiAction.Colours
                m_ui.Panel = TitlePanel.Colours
            Case UiAction.Soundboard
                m_ui.Panel = TitlePanel.Soundboard
            Case UiAction.Credits
                m_ui.Panel = TitlePanel.Credits
            Case UiAction.ClosePanel
                m_ui.Panel = TitlePanel.None
            Case Else
                If action >= UiAction.Colour0 AndAlso action <= UiAction.Colour3 Then
                    Dim index As Integer = CInt(action) - CInt(UiAction.Colour0)

                    Call SelectScheme(index)
                    Call m_audio.Chirp(DuckVariants.All(index).Voice, System.Environment.TickCount Mod 12)
                End If
        End Select
    End Sub

    Private Shadows Sub [Enter]()
        If Not m_booted Then
            Return
        End If

        m_ui.Screen = UiScreen.Play
        m_ui.Panel = TitlePanel.None
        m_ui.Paused = False
        m_host.Reveal = 0
        m_entrance = 1.0

        Call m_session.SetInputLocked(False)
        Call m_audio.Click()
        Call m_host.PrepareRig()
    End Sub

    Private Sub ToggleMenu()
        If m_ui.Screen <> UiScreen.Play Then
            Return
        End If

        m_ui.Paused = Not m_ui.Paused

        Call m_session.SetInputLocked(m_ui.Paused)
        Call m_audio.Click()

        If m_ui.Paused Then
            Call m_session.SetTwist(0, 0)
        End If
    End Sub

    Private Sub SelectScheme(index As Integer)
        If index < 0 OrElse index >= DuckVariants.All.Length Then
            Return
        End If

        m_scheme = index
        m_host.Scheme = DuckVariants.All(index)
        m_ui.SchemeName = m_host.Scheme.Label

        Call m_host.PrepareRig()
    End Sub

    Private Sub SwitchShape()
        Dim nextShape As Locomotion = If(m_session.Shape = Locomotion.Legs, Locomotion.Rollers, Locomotion.Legs)

        Call m_ui.ShowToast($"SWITCHING TO {nextShape.ToString().ToUpperInvariant()}...")
        Call m_session.SetShape(nextShape)
        Call m_host.PrepareRig()
    End Sub

    ' /********************************************************************************/
    '   the pad and the waypoint follow
    ' /********************************************************************************/

    Private Sub OnPollTick(sender As Object, e As EventArgs)
        If m_closing OrElse Not m_booted Then
            Return
        End If

        Try
            m_padPrevious = m_pad
            m_pad = XInputPad.Poll(0)

            If m_pad.Connected AndAlso m_ui.Screen = UiScreen.Play AndAlso Not m_ui.Paused Then
                Call ApplyPad()
            End If

            Call FollowWaypoint()
        Catch ex As Exception
            ' the input must never take the frame down
        End Try
    End Sub

    Private Sub ApplyPad()
        Dim forward As Double = m_pad.Forward
        Dim turn As Double = -m_pad.Turn

        If Math.Abs(forward) > 0.01 OrElse Math.Abs(turn) > 0.01 Then
            m_waypoint = Nothing
            Call m_session.SetTwist(forward, turn)
        ElseIf m_waypoint Is Nothing Then
            Call m_session.SetTwist(0, 0)
        End If

        If Math.Abs(m_pad.CameraX) > 0.01 OrElse Math.Abs(m_pad.CameraY) > 0.01 Then
            Call m_host.Rig.Orbit(m_pad.CameraX * 3.0, -m_pad.CameraY * 2.0)
        End If

        Call m_session.SetJaw(m_pad.TriggerRight)

        If m_pad.TriggerRight > 0.6 AndAlso m_padPrevious.TriggerRight <= 0.6 Then
            Call m_session.Quack()
        End If

        If Pressed(XInputPad.ButtonA) Then
            Call m_session.TriggerGroundPick()
        End If

        If Pressed(XInputPad.ButtonX) Then
            Call m_session.ToggleSit()
        End If

        If Pressed(XInputPad.ButtonY) Then
            Call m_session.ToggleHead()
        End If

        If Pressed(XInputPad.ButtonLeftShoulder) Then
            Call m_session.TriggerKick(True)
        End If

        If Pressed(XInputPad.ButtonRightShoulder) Then
            Call m_session.TriggerKick(False)
        End If

        If Pressed(XInputPad.ButtonStart) Then
            Call ToggleMenu()
        End If

        ' the shape switch of the real runtime: hold the up pad for a second
        If (m_pad.Buttons And XInputPad.ButtonDpadUp) <> 0 Then
            m_padHoldSteps += 1

            If m_padHoldSteps = 20 Then
                Call SwitchShape()
            End If
        Else
            If m_padHoldSteps > 0 AndAlso m_padHoldSteps < 20 Then
                Call m_session.TriggerRollOrCrouch()
            End If

            m_padHoldSteps = 0
        End If
    End Sub

    Private Function Pressed(button As UInteger) As Boolean
        Return (m_pad.Buttons And button) <> 0 AndAlso (m_padPrevious.Buttons And button) = 0
    End Function

    Private Sub FollowWaypoint()
        If Not m_waypoint.HasValue OrElse m_ui.Screen <> UiScreen.Play OrElse m_ui.Paused Then
            Return
        End If

        If Not m_session.Machine.AllowsTwist() Then
            Return
        End If

        Dim position As Vector3 = m_host.Snapshot.TrunkPosition
        Dim delta As Vector3 = m_waypoint.Value - position
        Dim distance As Double = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y)

        If distance < 0.12 Then
            m_waypoint = Nothing
            Call m_session.SetTwist(0, 0)

            Return
        End If

        Dim wanted As Double = Math.Atan2(delta.Y, delta.X)
        Dim heading As Double = m_host.Snapshot.TrunkYaw
        Dim errorAngle As Double = wanted - heading

        While errorAngle > Math.PI
            errorAngle -= 2 * Math.PI
        End While

        While errorAngle < -Math.PI
            errorAngle += 2 * Math.PI
        End While

        Dim turn As Double = Phys.Clamp(-errorAngle * 1.6, -1, 1)
        Dim forward As Double = If(Math.Abs(errorAngle) > 0.9, 0.0, Math.Cos(errorAngle) * Math.Min(1, distance * 2))

        Call m_session.SetTwist(forward, turn)
    End Sub
End Class
