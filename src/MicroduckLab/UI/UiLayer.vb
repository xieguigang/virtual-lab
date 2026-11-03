Imports System.Drawing
Imports System.IO
Imports MicroduckLab.Core
Imports Dx = Microsoft.VisualBasic.Drawing.DirectX
Imports Img = Microsoft.VisualBasic.Imaging

Namespace UI

    ''' <summary>the screens of the shell</summary>
    Public Enum UiScreen
        Boot
        Title
        Play
    End Enum

    ''' <summary>the panels of the title screen</summary>
    Public Enum TitlePanel
        None
        Controls
        Colours
        Soundboard
        Credits
    End Enum

    ''' <summary>what a click on the layer asked for</summary>
    Public Enum UiAction
        None
        EnterGame
        ResumeGame
        Pause
        Title
        Controls
        Colours
        Soundboard
        Credits
        ClosePanel
        Reset
        SpawnBall
        Quit
        Colour0
        Colour1
        Colour2
        Colour3
    End Enum

    ''' <summary>
    ''' The whole 2d shell of the application: the boot console, the title screen,
    ''' the hud, the soundboard and the crt of the overlay, drawn straight on top
    ''' of the 3d frame with the direct2d canvas of the library.
    ''' </summary>
    ''' <remarks>
    ''' The browser build spread the same content over a react tree of a dozen
    ''' components with MUI and css. There is one surface here: the canvas already
    ''' hands a 2d graphics object to the paint of the frame (after the 3d pass has
    ''' unbound its state), so every layer is a method of this class and the hit
    ''' testing is a list of rectangles that the same methods collect while they
    ''' draw. That keeps the layout and the click in one place, which is the one
    '''     thing that a hand written ui must get right.
    ''' </remarks>
    Public Class UiLayer

        ' ── the palette of the playground ───────────────────────────────────
        Public Shared ReadOnly Ink As Color = Color.FromArgb(255, 8, 8, 12)
        Public Shared ReadOnly PanelBack As Color = Color.FromArgb(190, 14, 15, 22)
        Public Shared ReadOnly Edge As Color = Color.FromArgb(255, 48, 50, 62)
        Public Shared ReadOnly Accent As Color = Color.FromArgb(255, 255, 122, 47)
        Public Shared ReadOnly AccentSoft As Color = Color.FromArgb(255, 255, 161, 92)
        Public Shared ReadOnly Text As Color = Color.FromArgb(255, 255, 255, 255)
        Public Shared ReadOnly TextDim As Color = Color.FromArgb(255, 150, 150, 162)
        Public Shared ReadOnly Good As Color = Color.FromArgb(255, 56, 224, 138)
        Public Shared ReadOnly Warn As Color = Color.FromArgb(255, 245, 196, 81)
        Public Shared ReadOnly Bad As Color = Color.FromArgb(255, 255, 77, 77)

        ' ── the screen state ────────────────────────────────────────────────
        Public Property Screen As UiScreen = UiScreen.Boot
        Public Property Panel As TitlePanel = TitlePanel.None
        Public Property Paused As Boolean = False
        ''' <summary>the name of the colour scheme, shown by the title screen</summary>
        Public Property SchemeName As String = "Classic"
        Public Property Toast As String = ""
        Public Property ToastExpires As Long

        Public ReadOnly Property Console As New BootConsole()
        ''' <summary>the boot progress in [0,1]</summary>
        Public Property BootProgress As Double
        Public Property BootFailed As Boolean
        Public Property BootMessage As String = ""

        ' ── the animation clock ─────────────────────────────────────────────
        Private m_clock As Long
        Private m_hover As UiAction = UiAction.None

        ' ── the fonts (created once, the paint must not allocate) ───────────
        Private ReadOnly m_osd As New Img.Font("Consolas", 9.0F, FontStyle.Regular, GraphicsUnit.Point)
        Private ReadOnly m_osdBold As New Img.Font("Consolas", 10.0F, FontStyle.Bold, GraphicsUnit.Point)
        Private ReadOnly m_small As New Img.Font("Consolas", 8.0F, FontStyle.Regular, GraphicsUnit.Point)
        Private ReadOnly m_title As New Img.Font("Segoe UI", 42.0F, FontStyle.Bold, GraphicsUnit.Point)
        Private ReadOnly m_subtitle As New Img.Font("Segoe UI", 13.0F, FontStyle.Regular, GraphicsUnit.Point)
        Private ReadOnly m_button As New Img.Font("Segoe UI", 14.0F, FontStyle.Bold, GraphicsUnit.Point)
        Private ReadOnly m_chip As New Img.Font("Consolas", 9.0F, FontStyle.Regular, GraphicsUnit.Point)

        ' ── the brushes and the pens are cached as well ─────────────────────
        Private ReadOnly m_textBrush As New Img.SolidBrush(Text)
        Private ReadOnly m_dimBrush As New Img.SolidBrush(TextDim)
        Private ReadOnly m_accentBrush As New Img.SolidBrush(Accent)
        Private ReadOnly m_inkBrush As New Img.SolidBrush(Ink)
        Private ReadOnly m_panelBrush As New Img.SolidBrush(PanelBack)
        Private ReadOnly m_goodBrush As New Img.SolidBrush(Good)
        Private ReadOnly m_badBrush As New Img.SolidBrush(Bad)
        Private ReadOnly m_warnBrush As New Img.SolidBrush(Warn)
        Private ReadOnly m_edgePen As New Img.Pen(Edge, 1.0F)
        Private ReadOnly m_accentPen As New Img.Pen(Accent, 1.0F)
        Private ReadOnly m_dimPen As New Img.Pen(TextDim, 1.0F)

        ' ── the hit targets of the last frame ───────────────────────────────
        Public ReadOnly Property Targets As New List(Of HitTarget)()

        Public Class HitTarget
            Public Rect As RectangleF
            Public Action As UiAction
            Public Label As String
        End Class

        Public Sub New()
            m_clock = System.Environment.TickCount64
        End Sub

        ''' <summary>the seconds since the layer was created, for the animations</summary>
        Private ReadOnly Property Now As Double
            Get
                Return (System.Environment.TickCount64 - m_clock) / 1000.0
            End Get
        End Property

        Public Sub ShowToast(text As String, Optional milliseconds As Integer = 2400)
            Toast = text
            ToastExpires = System.Environment.TickCount64 + milliseconds
        End Sub

        ' /********************************************************************************/
        '   the paint
        ' /********************************************************************************/

        ''' <summary>
        ''' draw the whole shell for one frame
        ''' </summary>
        Public Sub Paint(g As Dx.DxGraphics, size As Size, snapshot As PoseSnapshot)
            Call Targets.Clear()

            Select Case Screen
                Case UiScreen.Boot
                    Call PaintBoot(g, size)
                Case UiScreen.Title
                    Call PaintTitle(g, size)
                Case Else
                    Call PaintHud(g, size, snapshot)

                    If Paused Then
                        Call PaintPause(g, size)
                    End If
            End Select

            If Screen <> UiScreen.Boot Then
                Call PaintToast(g, size)
            End If
        End Sub

        ''' <summary>
        ''' the crt of the overlay: the scan lines and the vignette of the browser
        ''' build, drawn after the 3d frame and before the hud
        ''' </summary>
        Public Sub PaintCrt(g As Dx.DxGraphics, size As Size)
            Dim pen As New Img.Pen(Color.FromArgb(34, 0, 0, 0), 1.0F)

            For y As Integer = 0 To size.Height Step 3
                Call g.DrawLine(pen, 0, y, size.Width, y)
            Next

            pen.Dispose()

            Dim corner As New Img.SolidBrush(Color.FromArgb(110, 0, 0, 0))
            Dim band As Integer = Math.Max(24, size.Height \ 9)

            Call g.FillRectangle(corner, 0, 0, size.Width, 2)
            Call g.FillRectangle(corner, 0, size.Height - 2, size.Width, 2)
            Call g.FillRectangle(corner, 0, 0, 2, size.Height)
            Call g.FillRectangle(corner, size.Width - 2, 0, 2, size.Height)

            For i As Integer = 0 To 6
                Dim alpha As Integer = 40 - i * 6

                If alpha <= 0 Then
                    Exit For
                End If

                corner.Color = Color.FromArgb(alpha, 0, 0, 0)

                Call g.FillRectangle(corner, 0, i * 6, size.Width, 6)
                Call g.FillRectangle(corner, 0, size.Height - (i + 1) * 6, size.Width, 6)
            Next

            corner.Dispose()
        End Sub

        ' /********************************************************************************/
        '   boot
        ' /********************************************************************************/

        Private Sub PaintBoot(g As Dx.DxGraphics, size As Size)
            Call g.FillRectangle(m_inkBrush, 0, 0, size.Width, size.Height)

            Dim left As Single = size.Width * 0.12F
            Dim top As Single = size.Height * 0.16F
            Dim lineHeight As Single = 17.0F
            Dim lines As List(Of BootLine) = Console.Lines
            Dim visible As Integer = Math.Min(lines.Count, CInt((size.Height * 0.68F) / lineHeight))

            Call g.DrawString("MICRODUCK BIOS v1.0", m_osdBold, m_accentBrush, left, top - 26)
            Call g.DrawString("(c) sciBASIC.NET FOUNDATION - DIRECTX 11 DEVICE", m_small, m_dimBrush, left, top - 10)

            For i As Integer = 0 To visible - 1
                Dim line As BootLine = lines(lines.Count - visible + i)
                Dim brush As Img.Brush = If(line.IsError, m_badBrush, If(line.IsWarning, m_warnBrush, If(line.IsOk, m_goodBrush, m_dimBrush)))
                Dim y As Single = top + i * lineHeight

                ' the typing of the browser console: the last line grows into
                ' place instead of appearing at once
                Dim text As String = line.Text

                If i = visible - 1 AndAlso Not Console.Complete Then
                    Dim revealed As Integer = CInt((Now * 60) Mod Math.Max(text.Length, 1))

                    If revealed < text.Length Then
                        text = text.Substring(0, Math.Max(revealed, 0)) & "_"
                    End If
                End If

                Call g.DrawString(line.Tag, m_osd, brush, left, y)
                Call g.DrawString(text, m_osd, m_textBrush, left + 62, y)
            Next

            ' the progress bar of the loader
            Dim bar As New RectangleF(left, size.Height * 0.86F, size.Width * 0.5F, 6)

            Call g.DrawRectangle(m_edgePen, bar.X, bar.Y, bar.Width, bar.Height)
            Call g.FillRectangle(m_accentBrush, bar.X + 1, bar.Y + 1, (bar.Width - 2) * CSng(Phys.Clamp(BootProgress, 0, 1)), bar.Height - 2)
            Call g.DrawString($"{BootProgress * 100:0}%", m_osd, m_accentBrush, bar.Right + 12, bar.Y - 4)

            If BootFailed Then
                Call g.FillRectangle(m_badBrush, 0, size.Height - 44, size.Width, 44)
                Call g.DrawString($"SYSTEM HALTED  {BootMessage}", m_osdBold, m_inkBrush, left, size.Height - 30)
            End If
        End Sub

        ' /********************************************************************************/
        '   title
        ' /********************************************************************************/

        Private Sub PaintTitle(g As Dx.DxGraphics, size As Size)
            Dim veil As New Img.SolidBrush(Color.FromArgb(215, 8, 8, 12))

            Call g.FillRectangle(veil, 0, 0, size.Width, size.Height)
            veil.Dispose()

            Dim centerX As Single = size.Width * 0.5F
            Dim top As Single = size.Height * 0.16F

            Call g.DrawString("MICRODUCK RL PLAYGROUND", m_chip, m_dimBrush, centerX - 118, top - 30)

            Dim title As String = "MICRODUCK"
            Dim measure As SizeF = g.MeasureString(title, m_title)

            ' the outlined word mark of the browser build: the same text twice,
            ' once as the outline and once as the fill
            Call g.DrawString(title, m_title, m_accentBrush, centerX - measure.Width * 0.5F - 2, top)
            Call g.DrawString(title, m_title, m_textBrush, centerX - measure.Width * 0.5F, top - 2)

            Call g.DrawString("a small robot duck that really walks - trained policies, real physics",
                              m_subtitle, m_dimBrush, centerX - 250, top + measure.Height + 6)

            ' the one button of the entrance
            Dim main As New RectangleF(centerX - 130, top + 120, 260, 46)
            Dim pulse As Double = 0.5 + 0.5 * Math.Sin(Now * 2.4)

            Call g.FillRectangle(If(m_hover = UiAction.EnterGame, m_accentBrush, m_panelBrush), AsRect(main))
            Call g.DrawRectangle(m_accentPen, main.X, main.Y, main.Width, main.Height)
            Call g.DrawString("WADDLE IN", m_button, If(m_hover = UiAction.EnterGame, m_inkBrush, m_accentBrush),
                              main.X + 74, main.Y + 12)

            Call AddTarget(main, UiAction.EnterGame, "WADDLE IN")

            ' the four chips of the secondary panels
            Dim chips As UiAction() = {UiAction.Controls, UiAction.Colours, UiAction.Soundboard, UiAction.Credits}
            Dim labels As String() = {"CONTROLS", "COLOURS", "SOUNDBOARD", "CREDITS"}
            Dim width As Single = 132
            Dim gap As Single = 12
            Dim startX As Single = centerX - (width * 4 + gap * 3) * 0.5F
            Dim chipTop As Single = main.Bottom + 26

            For i As Integer = 0 To 3
                Dim rect As New RectangleF(startX + i * (width + gap), chipTop, width, 30)

                Call g.DrawRectangle(If(m_hover = chips(i), m_accentPen, m_edgePen), rect.X, rect.Y, rect.Width, rect.Height)
                Call g.DrawString(labels(i), m_chip, If(m_hover = chips(i), m_accentBrush, m_dimBrush), rect.X + 26, rect.Y + 8)
                Call AddTarget(rect, chips(i), labels(i))
            Next

            Call g.DrawString($"SHAPE {If(SchemeName.Length > 0, "", "")}  COLOUR {SchemeName.ToUpperInvariant()}   -   press arrows / wasd to steer once inside",
                              m_small, m_dimBrush, centerX - 250, chipTop + 50)

            Select Case Panel
                Case TitlePanel.None
                    Return
                Case TitlePanel.Soundboard
                    Call PaintSoundboard(g, size)
                Case Else
                    Call PaintInfoPanel(g, size, Panel)
            End Select
        End Sub

        Private Sub PaintInfoPanel(g As Dx.DxGraphics, size As Size, panel As TitlePanel)
            Dim box As New RectangleF(size.Width * 0.5F - 260, size.Height * 0.5F - 150, 520, 300)

            Call g.FillRectangle(m_panelBrush, AsRect(box))
            Call g.DrawRectangle(m_accentPen, box.X, box.Y, box.Width, box.Height)

            Dim title As String = panel.ToString().ToUpperInvariant()
            Dim lines As String()

            Select Case panel
                Case TitlePanel.Controls
                    lines = {
                        "ARROWS / WASD    walk and turn",
                        "SPACE            reset the sim",
                        "C                toggle the chase camera",
                        "G                ground pick",
                        "M                legs <-> rollers",
                        "R                sit / stand (crouch on rollers)",
                        "Q / E / F        kick left / right / alternate",
                        "MOUSE            drag to orbit, wheel to zoom",
                        "GAMEPAD          left stick walks, right stick orbits"
                    }
                Case TitlePanel.Colours
                    lines = {
                        "1  CLASSIC   warm white shell, orange beak",
                        "2  CHARCOAL  graphite shell, yellow beak",
                        "3  PURPLE    lilac shell, cyan beak",
                        "4  BLUE      sky shell, orange beak"
                    }
                Case Else
                    lines = {
                        "MICRODUCK RL PLAYGROUND",
                        "a port of the pollen-robotics microduck demo",
                        "policies trained with mujoco + mjx",
                        "physics  MuJoCo (managed)",
                        "render   Direct3D 11 + direct2d",
                        "ui       hand drawn, no toolkit"
                    }
            End Select

            Call g.DrawString(title, m_osdBold, m_accentBrush, box.X + 22, box.Y + 18)

            For i As Integer = 0 To lines.Length - 1
                Call g.DrawString(lines(i), m_osd, m_textBrush, box.X + 22, box.Y + 54 + i * 22)
            Next

            Dim close As New RectangleF(box.Right - 96, box.Bottom - 40, 76, 26)

            Call g.DrawRectangle(m_edgePen, close.X, close.Y, close.Width, close.Height)
            Call g.DrawString("CLOSE", m_chip, m_dimBrush, close.X + 18, close.Y + 7)
            Call AddTarget(close, UiAction.ClosePanel, "CLOSE")
        End Sub

        Private Sub PaintSoundboard(g As Dx.DxGraphics, size As Size)
            Dim box As New RectangleF(size.Width * 0.5F - 300, size.Height * 0.5F - 160, 600, 320)

            Call g.FillRectangle(m_panelBrush, AsRect(box))
            Call g.DrawRectangle(m_accentPen, box.X, box.Y, box.Width, box.Height)
            Call g.DrawString("SOUNDBOARD - the four voices of the flock", m_osdBold, m_accentBrush, box.X + 22, box.Y + 18)

            Dim voices As String() = {"duck1", "duck2", "duck3", "duck4"}
            Dim names As String() = {"CLASSIC", "CHARCOAL", "PURPLE", "BLUE"}

            For v As Integer = 0 To 3
                For c As Integer = 0 To 2
                    Dim rect As New RectangleF(box.X + 22 + v * 138, box.Y + 54 + c * 66, 126, 54)
                    Dim action As UiAction = DirectCast(CInt(UiAction.Colour0) + v, UiAction)

                    Call g.DrawRectangle(If(m_hover = action, m_accentPen, m_edgePen), rect.X, rect.Y, rect.Width, rect.Height)
                    Call g.DrawString($"{names(v)}", m_chip, m_textBrush, rect.X + 12, rect.Y + 8)
                    Call g.DrawString($"chirp {ChrW(97 + c * 4)}-{ChrW(100 + c * 4)}", m_small, m_dimBrush, rect.X + 12, rect.Y + 30)
                    Call AddTarget(rect, action, names(v))
                Next
            Next

            Dim close As New RectangleF(box.Right - 96, box.Bottom - 40, 76, 26)

            Call g.DrawRectangle(m_edgePen, close.X, close.Y, close.Width, close.Height)
            Call g.DrawString("CLOSE", m_chip, m_dimBrush, close.X + 18, close.Y + 7)
            Call AddTarget(close, UiAction.ClosePanel, "CLOSE")
        End Sub

        ' /********************************************************************************/
        '   hud
        ' /********************************************************************************/

        Private Sub PaintHud(g As Dx.DxGraphics, size As Size, snapshot As PoseSnapshot)
            Dim telemetry As TelemetryFrame = snapshot.Telemetry

            If telemetry Is Nothing Then
                Return
            End If

            ' the telemetry plate, top left
            Dim plate As New RectangleF(16, 16, 236, 108)

            Call Panel2D(g, plate)
            Call g.DrawString("MICRODUCK", m_osdBold, m_accentBrush, plate.X + 12, plate.Y + 9)

            Dim rows As String()() = {
                New String() {"MODE", telemetry.ModeLabel},
                New String() {"POLICY", telemetry.PolicyName},
                New String() {"SPEED", $"{telemetry.Speed:0.00} m/s"},
                New String() {"TRUNK", $"{telemetry.TrunkHeight * 1000:0} mm"}
            }

            For i As Integer = 0 To rows.Length - 1
                Call g.DrawString(rows(i)(0), m_small, m_dimBrush, plate.X + 12, plate.Y + 30 + i * 18)
                Call g.DrawString(rows(i)(1), m_osd, m_textBrush, plate.X + 74, plate.Y + 29 + i * 18)
            Next

            ' the osd of the camera, top right
            Dim osd As New RectangleF(size.Width - 176, 16, 160, 62)

            Call Panel2D(g, osd)
            Call g.DrawString("CAM  CHASE" & If(telemetry.Recovering, " REC", ""), m_small, m_dimBrush, osd.X + 10, osd.Y + 8)
            Call g.DrawString($"{telemetry.GravityZ:0.00} gz", m_osd, m_textBrush, osd.X + 10, osd.Y + 24)
            Call g.DrawString($"{telemetry.ControlHz:0.0} Hz  {telemetry.InferenceMs:0.0} ms", m_small, m_dimBrush, osd.X + 10, osd.Y + 42)

            ' the quick bar, bottom left
            Dim labels As String() = {"KICK", "SIT", "ROLL", "PICK"}
            Dim actions As UiAction() = {UiAction.None, UiAction.None, UiAction.None, UiAction.None}
            Dim radius As Single = 22

            For i As Integer = 0 To 3
                Dim center As New PointF(46 + i * 58, size.Height - 52)
                Dim active As Boolean = (i = 0 AndAlso telemetry.ModeLabel = "Kick") OrElse
                                        (i = 1 AndAlso telemetry.ModeLabel = "Sit") OrElse
                                        (i = 2 AndAlso (telemetry.ModeLabel = "Roll" OrElse telemetry.ModeLabel = "Crouch")) OrElse
                                        (i = 3 AndAlso telemetry.ModeLabel = "Pick")

                If active Then
                    Call g.FillEllipse(m_accentBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2)
                Else
                    Call g.DrawEllipse(m_edgePen, center.X - radius, center.Y - radius, radius * 2, radius * 2)
                End If

                Call g.DrawString(labels(i), m_small, If(active, m_inkBrush, m_dimBrush), center.X - 14, center.Y - 5)
            Next

            ' the hints, bottom centre
            Dim hints As String = "WASD WALK   M SHAPE   R SIT   Q/E KICK   G PICK   SPACE RESET   C CAM   ESC MENU"

            Call g.DrawString(hints, m_small, m_dimBrush, size.Width * 0.5F - 250, size.Height - 26)

            ' the colour dots, bottom right
            For i As Integer = 0 To 3
                Dim center As New PointF(size.Width - 34 - i * 30, size.Height - 52)
                Dim brush As New Img.SolidBrush(Render.DuckVariants.All(i).Beak)

                Call g.FillEllipse(brush, center.X - 8, center.Y - 8, 16, 16)
                brush.Dispose()

                Dim rect As New RectangleF(center.X - 12, center.Y - 12, 24, 24)

                Call AddTarget(rect, DirectCast(CInt(UiAction.Colour0) + i, UiAction), "col")
            Next
        End Sub

        Private Sub PaintPause(g As Dx.DxGraphics, size As Size)
            Dim veil As New Img.SolidBrush(Color.FromArgb(190, 8, 8, 12))

            Call g.FillRectangle(veil, 0, 0, size.Width, size.Height)
            veil.Dispose()

            Dim box As New RectangleF(size.Width * 0.5F - 180, size.Height * 0.5F - 110, 360, 220)

            Call g.FillRectangle(m_panelBrush, AsRect(box))
            Call g.DrawRectangle(m_accentPen, box.X, box.Y, box.Width, box.Height)
            Call g.DrawString("PAUSED", m_osdBold, m_accentBrush, box.X + 20, box.Y + 16)

            Dim items As String() = {"RESUME", "RESET THE DUCK", "BACK TO TITLE", "QUIT"}
            Dim actions As UiAction() = {UiAction.ResumeGame, UiAction.Reset, UiAction.Title, UiAction.Quit}

            For i As Integer = 0 To items.Length - 1
                Dim rect As New RectangleF(box.X + 20, box.Y + 50 + i * 40, box.Width - 40, 32)

                Call g.DrawRectangle(If(m_hover = actions(i), m_accentPen, m_edgePen), rect.X, rect.Y, rect.Width, rect.Height)
                Call g.DrawString(items(i), m_osd, If(m_hover = actions(i), m_accentBrush, m_textBrush), rect.X + 14, rect.Y + 8)
                Call AddTarget(rect, actions(i), items(i))
            Next
        End Sub

        Private Sub PaintToast(g As Dx.DxGraphics, size As Size)
            If String.IsNullOrEmpty(Toast) OrElse System.Environment.TickCount64 > ToastExpires Then
                Return
            End If

            Dim width As Single = Math.Max(160, Toast.Length * 8.0F)

            Call g.FillRectangle(m_panelBrush, size.Width * 0.5F - width * 0.5F, 82, width, 26)
            Call g.DrawRectangle(m_accentPen, size.Width * 0.5F - width * 0.5F, 82, width, 26)
            Call g.DrawString(Toast, m_osd, m_accentBrush, size.Width * 0.5F - width * 0.5F + 12, 88)
        End Sub

        Private Sub Panel2D(g As Dx.DxGraphics, rect As RectangleF)
            Call g.FillRectangle(m_panelBrush, AsRect(rect))
            Call g.DrawRectangle(m_edgePen, rect.X, rect.Y, rect.Width, rect.Height)
        End Sub

        Private Shared Function AsRect(rect As RectangleF) As Rectangle
            Return New Rectangle(CInt(rect.X), CInt(rect.Y), CInt(rect.Width), CInt(rect.Height))
        End Function

        Private Sub AddTarget(rect As RectangleF, action As UiAction, label As String)
            Call Targets.Add(New HitTarget With {.Rect = rect, .Action = action, .Label = label})
        End Sub

        ' /********************************************************************************/
        '   the mouse
        ' /********************************************************************************/

        ''' <summary>
        ''' the action that the given point asks for, and the hover of the next
        ''' frame
        ''' </summary>
        Public Function HitTest(point As Point) As UiAction
            For Each target As HitTarget In Targets
                If target.Rect.Contains(point) Then
                    m_hover = target.Action

                    Return target.Action
                End If
            Next

            m_hover = UiAction.None

            Return UiAction.None
        End Function

        Public Sub ClearHover()
            m_hover = UiAction.None
        End Sub

        ' /********************************************************************************/
        '   the boot console
        ' /********************************************************************************/

        Public Class BootLine
            Public Property Tag As String = "     "
            Public Property Text As String
            Public Property IsOk As Boolean
            Public Property IsWarning As Boolean
            Public Property IsError As Boolean
        End Class

        Public Class BootConsole
            Public ReadOnly Property Lines As New List(Of BootLine)()
            Public Property Complete As Boolean

            Public Sub Ok(text As String)
                Call Add("[ ok ]", text, False, False)
            End Sub

            Public Sub Info(text As String)
                Call Add("     ", text, False, False)
            End Sub

            Public Sub Warn(text As String)
                Call Add("[warn]", text, False, True)
            End Sub

            Public Sub [Error](text As String)
                Call Add("[fail]", text, False, False, True)
            End Sub

            Private Sub Add(tag As String, text As String, ok As Boolean, warn As Boolean, Optional fail As Boolean = False)
                Call Lines.Add(New BootLine With {
                    .Tag = tag,
                    .Text = text,
                    .IsOk = ok,
                    .IsWarning = warn,
                    .IsError = fail
                })

                If Lines.Count > 200 Then
                    Call Lines.RemoveAt(0)
                End If
            End Sub
        End Class
    End Class
End Namespace
