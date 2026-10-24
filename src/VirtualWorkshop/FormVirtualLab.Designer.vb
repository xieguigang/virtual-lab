<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormVirtualLab
    Inherits System.Windows.Forms.Form

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                If components IsNot Nothing Then components.Dispose()
                If m_timer IsNot Nothing Then m_timer.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    Friend WithEvents m_timer As System.Windows.Forms.Timer
    Friend WithEvents m_canvas As Microsoft.VisualBasic.Drawing.DirectX.DxScene3DCanvas

    Friend WithEvents m_top As Panel
    Friend WithEvents m_side As Panel
    Friend WithEvents m_bottom As Panel
    Friend WithEvents m_viewport As Panel

    Friend WithEvents m_btnStart As Button
    Friend WithEvents m_btnStopServer As Button
    Friend WithEvents m_btnEnable As Button
    Friend WithEvents m_btnHome As Button
    Friend WithEvents m_btnHold As Button
    Friend WithEvents m_btnEstop As Button
    Friend WithEvents m_btnReset As Button
    Friend WithEvents m_numRealtime As NumericUpDown
    Friend WithEvents m_lblService As Label
    Friend WithEvents m_lblStats As Label

    Friend m_jointBars As TrackBar()
    Friend m_jointLabels As Label()
    Friend WithEvents m_lblTcp As Label
    Friend WithEvents m_lblGripper As Label
    Friend WithEvents m_numGripForce As NumericUpDown
    Friend WithEvents m_btnGrip As Button
    Friend WithEvents m_btnRelease As Button
    Friend WithEvents m_picCamera As PictureBox
    Friend WithEvents m_lblCamera As Label
    Friend WithEvents m_lstObjects As ListBox
    Friend WithEvents m_log As RichTextBox
    Friend WithEvents m_status As StatusStrip
    Friend WithEvents m_statusLabel As ToolStripStatusLabel

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()

        Me.m_top = New Panel()
        Me.m_side = New Panel()
        Me.m_bottom = New Panel()
        Me.m_viewport = New Panel()
        Me.m_canvas = New Microsoft.VisualBasic.Drawing.DirectX.DxScene3DCanvas()
        Me.m_timer = New System.Windows.Forms.Timer(Me.components)

        ' ---------------- 窗体 ----------------
        Me.SuspendLayout()
        Me.AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New Size(1440, 980)
        Me.MinimumSize = New Size(1180, 780)
        Me.BackColor = UiTheme.Background
        Me.ForeColor = UiTheme.TextPrimary
        Me.Font = UiTheme.FontUi
        Me.Text = "VirtualWorkshop · 六轴机械臂虚拟硬件仿真工作台"
        Me.DoubleBuffered = True

        ' ---------------- 顶部工具栏 ----------------
        Me.m_top.Dock = DockStyle.Top
        Me.m_top.Height = 60
        Me.m_top.BackColor = UiTheme.PanelColor
        Me.m_top.Padding = New Padding(12, 0, 12, 0)

        Dim brand As New Label With {
            .Text = "VIRTUAL WORKSHOP",
            .AutoSize = False,
            .Width = 190,
            .Dock = DockStyle.Left,
            .TextAlign = ContentAlignment.MiddleLeft,
            .ForeColor = UiTheme.Accent,
            .Font = UiTheme.FontTitle,
            .BackColor = Color.Transparent
        }

        Dim flow As New FlowLayoutPanel With {
            .Dock = DockStyle.Left,
            .Width = 780,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Padding = New Padding(0, 14, 0, 0),
            .BackColor = Color.Transparent
        }

        Me.m_btnStart = UiTheme.CreateButton("启动服务", UiTheme.Good)
        Me.m_btnStopServer = UiTheme.CreateButton("停止服务", UiTheme.Warn)
        Me.m_btnEnable = UiTheme.CreateButton("伺服使能", UiTheme.Accent)
        Me.m_btnHome = UiTheme.CreateButton("回零", UiTheme.Accent)
        Me.m_btnHold = UiTheme.CreateButton("保持", UiTheme.AccentDim)
        Me.m_btnEstop = UiTheme.CreateButton("急停", UiTheme.Danger, 72)
        Me.m_btnReset = UiTheme.CreateButton("复位场景", UiTheme.Warn)

        Dim realtimeLabel As New Label With {
            .Text = "倍率",
            .AutoSize = True,
            .ForeColor = UiTheme.TextMuted,
            .Margin = New Padding(12, 6, 4, 0)
        }

        Me.m_numRealtime = New NumericUpDown With {
            .DecimalPlaces = 1,
            .Minimum = 0D,
            .Maximum = 20D,
            .Increment = 0.5D,
            .Value = 1D,
            .Width = 62,
            .BackColor = UiTheme.PanelAlt,
            .ForeColor = UiTheme.TextPrimary,
            .BorderStyle = BorderStyle.FixedSingle,
            .Margin = New Padding(0, 2, 0, 0)
        }

        flow.Controls.Add(Me.m_btnStart)
        flow.Controls.Add(Me.m_btnStopServer)
        flow.Controls.Add(Me.m_btnEnable)
        flow.Controls.Add(Me.m_btnHome)
        flow.Controls.Add(Me.m_btnHold)
        flow.Controls.Add(Me.m_btnEstop)
        flow.Controls.Add(Me.m_btnReset)
        flow.Controls.Add(realtimeLabel)
        flow.Controls.Add(Me.m_numRealtime)

        Me.m_lblService = New Label With {
            .Text = "离线",
            .Dock = DockStyle.Right,
            .Width = 250,
            .TextAlign = ContentAlignment.MiddleRight,
            .ForeColor = UiTheme.TextMuted,
            .Font = UiTheme.FontMonoSmall,
            .BackColor = Color.Transparent
        }

        Me.m_top.Controls.Add(flow)
        Me.m_top.Controls.Add(brand)
        Me.m_top.Controls.Add(Me.m_lblService)

        ' ---------------- 右侧仪表面板 ----------------
        Me.m_side.Dock = DockStyle.Right
        Me.m_side.Width = 386
        Me.m_side.BackColor = UiTheme.Background
        Me.m_side.Padding = New Padding(8)

        ' 卡片总高会超过面板高度，用滚动条兜底（夹爪相机固定在最上方）
        Me.m_side.AutoScroll = True

        Dim jointCard As Panel = UiTheme.CreateCard("关节状态")
        jointCard.Height = 200

        Dim jointsHost As New Panel With {
            .Dock = DockStyle.Fill,
            .AutoScroll = True,
            .Padding = New Padding(0, 4, 0, 0),
            .BackColor = Color.Transparent
        }
        jointCard.Controls.Add(jointsHost)

        Me.m_jointBars = New TrackBar(5) {}
        Me.m_jointLabels = New Label(5) {}

        Dim names As String() = {"J1 腰", "J2 肩", "J3 肘", "J4 腕转", "J5 腕摆", "J6 腕旋"}
        Dim topOffset As Integer = 0

        For i As Integer = 0 To 5
            Dim nameLabel As New Label With {
                .Text = names(i),
                .Left = 0,
                .Top = topOffset + 4,
                .Width = 52,
                .ForeColor = UiTheme.TextMuted,
                .BackColor = Color.Transparent,
                .Font = UiTheme.FontMonoSmall
            }

            Dim bar As New TrackBar With {
                .Left = 54,
                .Top = topOffset,
                .Width = 232,
                .Height = 26,
                .Minimum = -1000,
                .Maximum = 1000,
                .TickStyle = TickStyle.None,
                .SmallChange = 5,
                .LargeChange = 50,
                .Value = 0
            }

            Dim valueLabel As New Label With {
                .Text = "0.000",
                .Left = 292,
                .Top = topOffset + 4,
                .Width = 58,
                .TextAlign = ContentAlignment.MiddleRight,
                .ForeColor = UiTheme.Accent,
                .BackColor = Color.Transparent,
                .Font = UiTheme.FontMonoSmall
            }

            jointsHost.Controls.Add(nameLabel)
            jointsHost.Controls.Add(bar)
            jointsHost.Controls.Add(valueLabel)

            Me.m_jointBars(i) = bar
            Me.m_jointLabels(i) = valueLabel
            topOffset += 28
        Next

        Dim tcpCard As Panel = UiTheme.CreateCard("TCP 位姿")
        tcpCard.Height = 86

        Me.m_lblTcp = New Label With {
            .Text = "x 0.000  y 0.000  z 0.000" & vbCrLf & "rx 0.0  ry 0.0  rz 0.0",
            .Dock = DockStyle.Fill,
            .ForeColor = UiTheme.TextPrimary,
            .Font = UiTheme.FontMono,
            .BackColor = Color.Transparent
        }
        tcpCard.Controls.Add(Me.m_lblTcp)

        Dim gripCard As Panel = UiTheme.CreateCard("夹爪")
        gripCard.Height = 116

        Dim gripFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = True,
            .Padding = New Padding(0, 4, 0, 0),
            .BackColor = Color.Transparent
        }

        Me.m_lblGripper = New Label With {
            .Text = "开口 0.036 m   夹持力 0 N",
            .Width = 348,
            .Height = 22,
            .ForeColor = UiTheme.TextPrimary,
            .Font = UiTheme.FontMonoSmall,
            .BackColor = Color.Transparent
        }

        Me.m_btnRelease = UiTheme.CreateButton("张开", UiTheme.AccentDim, 76)
        Me.m_btnGrip = UiTheme.CreateButton("夹紧", UiTheme.Accent, 76)

        Me.m_numGripForce = New NumericUpDown With {
            .Minimum = 1D,
            .Maximum = 120D,
            .Value = 35D,
            .Increment = 5D,
            .Width = 66,
            .BackColor = UiTheme.PanelAlt,
            .ForeColor = UiTheme.TextPrimary,
            .BorderStyle = BorderStyle.FixedSingle,
            .Margin = New Padding(6, 2, 4, 0)
        }

        Dim forceLabel As New Label With {
            .Text = "N",
            .AutoSize = True,
            .ForeColor = UiTheme.TextMuted,
            .Margin = New Padding(0, 6, 0, 0)
        }

        gripFlow.Controls.Add(Me.m_lblGripper)
        gripFlow.Controls.Add(Me.m_btnRelease)
        gripFlow.Controls.Add(Me.m_btnGrip)
        gripFlow.Controls.Add(Me.m_numGripForce)
        gripFlow.Controls.Add(forceLabel)
        gripCard.Controls.Add(gripFlow)

        ' ---------------- 夹爪相机（第一视角） ----------------
        Dim cameraCard As Panel = UiTheme.CreateCard("夹爪相机 · 第一视角")
        cameraCard.Height = 200

        Me.m_picCamera = New PictureBox With {
            .Dock = DockStyle.Top,
            .Height = 140,
            .BackColor = UiTheme.Background,
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BorderStyle = BorderStyle.FixedSingle,
            .Margin = New Padding(0)
        }

        Me.m_lblCamera = New Label With {
            .Text = "等待相机第一帧…",
            .Dock = DockStyle.Fill,
            .ForeColor = UiTheme.TextMuted,
            .Font = UiTheme.FontMonoSmall,
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleLeft
        }

        ' 先加 Top 的图片框、再加 Fill 的标签，停靠顺序才是"标题 → 画面 → 读数"
        cameraCard.Controls.Add(Me.m_picCamera)
        cameraCard.Controls.Add(Me.m_lblCamera)

        Dim objectCard As Panel = UiTheme.CreateCard("场景物体")
        objectCard.Dock = DockStyle.Top
        objectCard.Height = 124

        Me.m_lstObjects = New ListBox With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.PanelAlt,
            .ForeColor = UiTheme.TextPrimary,
            .BorderStyle = BorderStyle.None,
            .Font = UiTheme.FontMonoSmall,
            .IntegralHeight = False
        }
        objectCard.Controls.Add(Me.m_lstObjects)

        ' Dock=Top 的卡片：后添加的排在更上面。
        ' 夹爪相机放最上面，保证窗口矮的时候也一直能看见第一视角
        Me.m_side.Controls.Add(objectCard)
        Me.m_side.Controls.Add(gripCard)
        Me.m_side.Controls.Add(tcpCard)
        Me.m_side.Controls.Add(jointCard)
        Me.m_side.Controls.Add(cameraCard)

        ' ---------------- 底部日志 ----------------
        Me.m_bottom.Dock = DockStyle.Bottom
        Me.m_bottom.Height = 140
        Me.m_bottom.BackColor = UiTheme.PanelColor
        Me.m_bottom.Padding = New Padding(10, 6, 10, 6)

        Me.m_log = New RichTextBox With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.PanelColor,
            .ForeColor = UiTheme.TextPrimary,
            .BorderStyle = BorderStyle.None,
            .Font = UiTheme.FontMonoSmall,
            .ReadOnly = True,
            .WordWrap = False,
            .ScrollBars = RichTextBoxScrollBars.Vertical,
            .DetectUrls = False
        }
        Me.m_bottom.Controls.Add(Me.m_log)

        ' ---------------- 三维视口 ----------------
        Me.m_viewport.Dock = DockStyle.Fill
        Me.m_viewport.BackColor = Color.Black
        Me.m_viewport.Padding = New Padding(0)

        Me.m_canvas.Dock = DockStyle.Fill
        Me.m_viewport.Controls.Add(Me.m_canvas)

        ' ---------------- 状态栏 ----------------
        Me.m_status = New StatusStrip With {
            .BackColor = UiTheme.PanelColor,
            .ForeColor = UiTheme.TextMuted,
            .SizingGrip = False,
            .Font = UiTheme.FontMonoSmall
        }

        Me.m_statusLabel = New ToolStripStatusLabel With {
            .Text = "就绪",
            .Spring = True,
            .TextAlign = ContentAlignment.MiddleLeft
        }

        Me.m_lblStats = New Label With {
            .Text = "FPS 0 · 步/秒 0 · 三角面 0",
            .AutoSize = True,
            .ForeColor = UiTheme.TextMuted,
            .BackColor = Color.Transparent
        }

        Me.m_status.Items.Add(Me.m_statusLabel)

        ' ---------------- 组装 ----------------
        Me.Controls.Add(Me.m_viewport)
        Me.Controls.Add(Me.m_side)
        Me.Controls.Add(Me.m_bottom)
        Me.Controls.Add(Me.m_top)
        Me.Controls.Add(Me.m_status)

        Me.m_timer.Interval = 16

        Me.ResumeLayout(False)
    End Sub

End Class
