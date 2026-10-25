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
    Friend WithEvents m_side As FlowLayoutPanel
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
    Friend m_jointNames As Label()

    Friend WithEvents m_jointBar0 As TrackBar
    Friend WithEvents m_jointBar1 As TrackBar
    Friend WithEvents m_jointBar2 As TrackBar
    Friend WithEvents m_jointBar3 As TrackBar
    Friend WithEvents m_jointBar4 As TrackBar
    Friend WithEvents m_jointBar5 As TrackBar
    Friend WithEvents m_jointLabel0 As Label
    Friend WithEvents m_jointLabel1 As Label
    Friend WithEvents m_jointLabel2 As Label
    Friend WithEvents m_jointLabel3 As Label
    Friend WithEvents m_jointLabel4 As Label
    Friend WithEvents m_jointLabel5 As Label
    Friend WithEvents m_jointName0 As Label
    Friend WithEvents m_jointName1 As Label
    Friend WithEvents m_jointName2 As Label
    Friend WithEvents m_jointName3 As Label
    Friend WithEvents m_jointName4 As Label
    Friend WithEvents m_jointName5 As Label
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

    Friend WithEvents brand As Label
    Friend WithEvents flow As FlowLayoutPanel
    Friend WithEvents realtimeLabel As Label
    Friend WithEvents jointCard As Panel
    Friend WithEvents jointsHost As Panel
    Friend WithEvents jointCardHeader As Label
    Friend WithEvents tcpCard As Panel
    Friend WithEvents tcpCardHeader As Label
    Friend WithEvents gripCard As Panel
    Friend WithEvents gripCardHeader As Label
    Friend WithEvents gripFlow As FlowLayoutPanel
    Friend WithEvents forceLabel As Label
    Friend WithEvents cameraCard As Panel
    Friend WithEvents cameraCardHeader As Label
    Friend WithEvents objectCard As Panel
    Friend WithEvents objectCardHeader As Label

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        ' ---------------- 控件创建 ----------------
        Me.components = New System.ComponentModel.Container()

        Me.m_top = New Panel()
        Me.m_side = New FlowLayoutPanel()
        Me.m_bottom = New Panel()
        Me.m_viewport = New Panel()
        Me.m_canvas = New Microsoft.VisualBasic.Drawing.DirectX.DxScene3DCanvas()
        Me.m_timer = New System.Windows.Forms.Timer(Me.components)

        Me.m_btnStart = New Button()
        Me.m_btnStopServer = New Button()
        Me.m_btnEnable = New Button()
        Me.m_btnHome = New Button()
        Me.m_btnHold = New Button()
        Me.m_btnEstop = New Button()
        Me.m_btnReset = New Button()

        Me.brand = New Label()
        Me.flow = New FlowLayoutPanel()
        Me.realtimeLabel = New Label()
        Me.m_numRealtime = New NumericUpDown()
        Me.m_lblService = New Label()

        Me.jointCard = New Panel()
        Me.jointCardHeader = New Label()
        Me.jointsHost = New Panel()
        Me.m_jointBar0 = New TrackBar()
        Me.m_jointLabel0 = New Label()
        Me.m_jointName0 = New Label()
        Me.m_jointBar1 = New TrackBar()
        Me.m_jointLabel1 = New Label()
        Me.m_jointName1 = New Label()
        Me.m_jointBar2 = New TrackBar()
        Me.m_jointLabel2 = New Label()
        Me.m_jointName2 = New Label()
        Me.m_jointBar3 = New TrackBar()
        Me.m_jointLabel3 = New Label()
        Me.m_jointName3 = New Label()
        Me.m_jointBar4 = New TrackBar()
        Me.m_jointLabel4 = New Label()
        Me.m_jointName4 = New Label()
        Me.m_jointBar5 = New TrackBar()
        Me.m_jointLabel5 = New Label()
        Me.m_jointName5 = New Label()

        Me.tcpCard = New Panel()
        Me.tcpCardHeader = New Label()
        Me.m_lblTcp = New Label()

        Me.gripCard = New Panel()
        Me.gripCardHeader = New Label()
        Me.gripFlow = New FlowLayoutPanel()
        Me.m_lblGripper = New Label()
        Me.m_btnRelease = New Button()
        Me.m_btnGrip = New Button()
        Me.m_numGripForce = New NumericUpDown()
        Me.forceLabel = New Label()

        Me.cameraCard = New Panel()
        Me.cameraCardHeader = New Label()
        Me.m_picCamera = New PictureBox()
        Me.m_lblCamera = New Label()

        Me.objectCard = New Panel()
        Me.objectCardHeader = New Label()
        Me.m_lstObjects = New ListBox()

        Me.m_log = New RichTextBox()

        Me.m_status = New StatusStrip()
        Me.m_statusLabel = New ToolStripStatusLabel()
        Me.m_lblStats = New Label()

        ' ---------------- 窗体 ----------------
        Me.SuspendLayout()
        Me.AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New Size(1440, 980)
        Me.MinimumSize = New Size(1180, 780)
        Me.BackColor = Color.White
        Me.ForeColor = Color.FromArgb(30, 41, 59)
        Me.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.Text = "VirtualWorkshop · 六轴机械臂虚拟硬件仿真工作台"
        Me.DoubleBuffered = True

        ' ---------------- 顶部工具栏 ----------------
        Me.m_top.Dock = DockStyle.Top
        Me.m_top.Height = 60
        Me.m_top.BackColor = Color.FromArgb(248, 250, 252)
        Me.m_top.Padding = New Padding(12, 0, 12, 0)

        Me.brand.Text = "VIRTUAL WORKSHOP"
        Me.brand.AutoSize = False
        Me.brand.Width = 190
        Me.brand.Dock = DockStyle.Left
        Me.brand.TextAlign = ContentAlignment.MiddleLeft
        Me.brand.ForeColor = Color.FromArgb(14, 165, 233)
        Me.brand.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        Me.brand.BackColor = Color.Transparent

        Me.flow.Dock = DockStyle.Left
        Me.flow.Width = 780
        Me.flow.FlowDirection = FlowDirection.LeftToRight
        Me.flow.WrapContents = False
        Me.flow.Padding = New Padding(0, 14, 0, 0)
        Me.flow.BackColor = Color.Transparent

        ' m_btnStart（主按钮：blue-600 实心白字，无边框）
        Me.m_btnStart.Text = "启动服务"
        Me.m_btnStart.Width = 92
        Me.m_btnStart.Height = 32
        Me.m_btnStart.FlatStyle = FlatStyle.Flat
        Me.m_btnStart.BackColor = Color.FromArgb(37, 99, 235)
        Me.m_btnStart.ForeColor = Color.White
        Me.m_btnStart.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnStart.Cursor = Cursors.Hand
        Me.m_btnStart.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnStart.TabStop = False
        Me.m_btnStart.UseVisualStyleBackColor = False
        Me.m_btnStart.FlatAppearance.BorderSize = 0
        Me.m_btnStart.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216)
        Me.m_btnStart.FlatAppearance.MouseDownBackColor = Color.FromArgb(29, 78, 216)

        ' m_btnStopServer（描边按钮：Warn）
        Me.m_btnStopServer.Text = "停止服务"
        Me.m_btnStopServer.Width = 92
        Me.m_btnStopServer.Height = 30
        Me.m_btnStopServer.FlatStyle = FlatStyle.Flat
        Me.m_btnStopServer.BackColor = Color.White
        Me.m_btnStopServer.ForeColor = Color.FromArgb(245, 158, 11)
        Me.m_btnStopServer.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnStopServer.Cursor = Cursors.Hand
        Me.m_btnStopServer.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnStopServer.TabStop = False
        Me.m_btnStopServer.FlatAppearance.BorderSize = 1
        Me.m_btnStopServer.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11)
        Me.m_btnStopServer.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnStopServer.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        ' m_btnEnable（描边按钮：Accent）
        Me.m_btnEnable.Text = "伺服使能"
        Me.m_btnEnable.Width = 92
        Me.m_btnEnable.Height = 30
        Me.m_btnEnable.FlatStyle = FlatStyle.Flat
        Me.m_btnEnable.BackColor = Color.White
        Me.m_btnEnable.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_btnEnable.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnEnable.Cursor = Cursors.Hand
        Me.m_btnEnable.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnEnable.TabStop = False
        Me.m_btnEnable.FlatAppearance.BorderSize = 1
        Me.m_btnEnable.FlatAppearance.BorderColor = Color.FromArgb(14, 165, 233)
        Me.m_btnEnable.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnEnable.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        ' m_btnHome（描边按钮：Accent）
        Me.m_btnHome.Text = "回零"
        Me.m_btnHome.Width = 92
        Me.m_btnHome.Height = 30
        Me.m_btnHome.FlatStyle = FlatStyle.Flat
        Me.m_btnHome.BackColor = Color.White
        Me.m_btnHome.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_btnHome.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnHome.Cursor = Cursors.Hand
        Me.m_btnHome.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnHome.TabStop = False
        Me.m_btnHome.FlatAppearance.BorderSize = 1
        Me.m_btnHome.FlatAppearance.BorderColor = Color.FromArgb(14, 165, 233)
        Me.m_btnHome.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnHome.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        ' m_btnHold（描边按钮：AccentDim）
        Me.m_btnHold.Text = "保持"
        Me.m_btnHold.Width = 92
        Me.m_btnHold.Height = 30
        Me.m_btnHold.FlatStyle = FlatStyle.Flat
        Me.m_btnHold.BackColor = Color.White
        Me.m_btnHold.ForeColor = Color.FromArgb(100, 116, 139)
        Me.m_btnHold.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnHold.Cursor = Cursors.Hand
        Me.m_btnHold.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnHold.TabStop = False
        Me.m_btnHold.FlatAppearance.BorderSize = 1
        Me.m_btnHold.FlatAppearance.BorderColor = Color.FromArgb(100, 116, 139)
        Me.m_btnHold.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnHold.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        ' m_btnEstop（描边按钮：Danger，72 宽）
        Me.m_btnEstop.Text = "急停"
        Me.m_btnEstop.Width = 72
        Me.m_btnEstop.Height = 30
        Me.m_btnEstop.FlatStyle = FlatStyle.Flat
        Me.m_btnEstop.BackColor = Color.White
        Me.m_btnEstop.ForeColor = Color.FromArgb(220, 38, 38)
        Me.m_btnEstop.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnEstop.Cursor = Cursors.Hand
        Me.m_btnEstop.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnEstop.TabStop = False
        Me.m_btnEstop.FlatAppearance.BorderSize = 1
        Me.m_btnEstop.FlatAppearance.BorderColor = Color.FromArgb(220, 38, 38)
        Me.m_btnEstop.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnEstop.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        ' m_btnReset（描边按钮：Warn）
        Me.m_btnReset.Text = "复位场景"
        Me.m_btnReset.Width = 92
        Me.m_btnReset.Height = 30
        Me.m_btnReset.FlatStyle = FlatStyle.Flat
        Me.m_btnReset.BackColor = Color.White
        Me.m_btnReset.ForeColor = Color.FromArgb(245, 158, 11)
        Me.m_btnReset.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnReset.Cursor = Cursors.Hand
        Me.m_btnReset.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnReset.TabStop = False
        Me.m_btnReset.FlatAppearance.BorderSize = 1
        Me.m_btnReset.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11)
        Me.m_btnReset.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnReset.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        Me.realtimeLabel.Text = "倍率"
        Me.realtimeLabel.AutoSize = True
        Me.realtimeLabel.ForeColor = Color.FromArgb(71, 85, 105)
        Me.realtimeLabel.Margin = New Padding(12, 6, 4, 0)

        Me.m_numRealtime.DecimalPlaces = 1
        Me.m_numRealtime.Minimum = 0D
        Me.m_numRealtime.Maximum = 20D
        Me.m_numRealtime.Increment = 0.5D
        Me.m_numRealtime.Value = 1D
        Me.m_numRealtime.Width = 62
        Me.m_numRealtime.BackColor = Color.White
        Me.m_numRealtime.ForeColor = Color.FromArgb(30, 41, 59)
        Me.m_numRealtime.BorderStyle = BorderStyle.FixedSingle
        Me.m_numRealtime.Margin = New Padding(0, 2, 0, 0)

        Me.flow.Controls.Add(Me.m_btnStart)
        Me.flow.Controls.Add(Me.m_btnStopServer)
        Me.flow.Controls.Add(Me.m_btnEnable)
        Me.flow.Controls.Add(Me.m_btnHome)
        Me.flow.Controls.Add(Me.m_btnHold)
        Me.flow.Controls.Add(Me.m_btnEstop)
        Me.flow.Controls.Add(Me.m_btnReset)
        Me.flow.Controls.Add(Me.realtimeLabel)
        Me.flow.Controls.Add(Me.m_numRealtime)

        Me.m_lblService.Text = "离线"
        Me.m_lblService.Dock = DockStyle.Right
        Me.m_lblService.Width = 250
        Me.m_lblService.TextAlign = ContentAlignment.MiddleRight
        Me.m_lblService.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_lblService.Font = New Font("Consolas", 8.0F, FontStyle.Regular)
        Me.m_lblService.BackColor = Color.Transparent

        Me.m_top.Controls.Add(Me.flow)
        Me.m_top.Controls.Add(Me.brand)
        Me.m_top.Controls.Add(Me.m_lblService)

        ' ---------------- 右侧仪表面板 ----------------
        Me.m_side.Dock = DockStyle.Right
        Me.m_side.Width = 386
        Me.m_side.BackColor = Color.FromArgb(248, 250, 252)
        Me.m_side.Padding = New Padding(8)
        Me.m_side.AutoScroll = True
        Me.m_side.FlowDirection = FlowDirection.TopDown
        Me.m_side.WrapContents = False

        ' 关节状态卡片
        Me.jointCard.Dock = DockStyle.None
        Me.jointCard.Width = 352
        Me.jointCard.Height = 200
        Me.jointCard.Margin = New Padding(0, 0, 0, 8)
        Me.jointCard.BackColor = Color.White
        Me.jointCard.Padding = New Padding(12, 10, 12, 10)

        Me.jointCardHeader.Text = "关节状态"
        Me.jointCardHeader.Dock = DockStyle.Top
        Me.jointCardHeader.Height = 22
        Me.jointCardHeader.ForeColor = Color.FromArgb(30, 41, 59)
        Me.jointCardHeader.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        Me.jointCardHeader.BackColor = Color.Transparent
        Me.jointCard.Controls.Add(Me.jointCardHeader)

        Me.jointsHost.Dock = DockStyle.Fill
        Me.jointsHost.AutoScroll = True
        Me.jointsHost.Padding = New Padding(0, 4, 0, 0)
        Me.jointsHost.BackColor = Color.Transparent
        Me.jointCard.Controls.Add(Me.jointsHost)

        ' 关节 1 (J1 腰)
        Me.m_jointName0.Text = "J1 腰"
        Me.m_jointName0.Left = 0
        Me.m_jointName0.Top = 4
        Me.m_jointName0.Width = 52
        Me.m_jointName0.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_jointName0.BackColor = Color.Transparent
        Me.m_jointName0.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.m_jointBar0.Left = 54
        Me.m_jointBar0.Top = 0
        Me.m_jointBar0.Width = 232
        Me.m_jointBar0.Height = 26
        Me.m_jointBar0.Minimum = -1000
        Me.m_jointBar0.Maximum = 1000
        Me.m_jointBar0.TickStyle = TickStyle.None
        Me.m_jointBar0.SmallChange = 5
        Me.m_jointBar0.LargeChange = 50
        Me.m_jointBar0.Value = 0

        Me.m_jointLabel0.Text = "0.000"
        Me.m_jointLabel0.Left = 292
        Me.m_jointLabel0.Top = 4
        Me.m_jointLabel0.Width = 58
        Me.m_jointLabel0.TextAlign = ContentAlignment.MiddleRight
        Me.m_jointLabel0.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_jointLabel0.BackColor = Color.Transparent
        Me.m_jointLabel0.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.jointsHost.Controls.Add(Me.m_jointName0)
        Me.jointsHost.Controls.Add(Me.m_jointBar0)
        Me.jointsHost.Controls.Add(Me.m_jointLabel0)

        ' 关节 2 (J2 肩)
        Me.m_jointName1.Text = "J2 肩"
        Me.m_jointName1.Left = 0
        Me.m_jointName1.Top = 32
        Me.m_jointName1.Width = 52
        Me.m_jointName1.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_jointName1.BackColor = Color.Transparent
        Me.m_jointName1.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.m_jointBar1.Left = 54
        Me.m_jointBar1.Top = 28
        Me.m_jointBar1.Width = 232
        Me.m_jointBar1.Height = 26
        Me.m_jointBar1.Minimum = -1000
        Me.m_jointBar1.Maximum = 1000
        Me.m_jointBar1.TickStyle = TickStyle.None
        Me.m_jointBar1.SmallChange = 5
        Me.m_jointBar1.LargeChange = 50
        Me.m_jointBar1.Value = 0

        Me.m_jointLabel1.Text = "0.000"
        Me.m_jointLabel1.Left = 292
        Me.m_jointLabel1.Top = 32
        Me.m_jointLabel1.Width = 58
        Me.m_jointLabel1.TextAlign = ContentAlignment.MiddleRight
        Me.m_jointLabel1.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_jointLabel1.BackColor = Color.Transparent
        Me.m_jointLabel1.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.jointsHost.Controls.Add(Me.m_jointName1)
        Me.jointsHost.Controls.Add(Me.m_jointBar1)
        Me.jointsHost.Controls.Add(Me.m_jointLabel1)

        ' 关节 3 (J3 肘)
        Me.m_jointName2.Text = "J3 肘"
        Me.m_jointName2.Left = 0
        Me.m_jointName2.Top = 60
        Me.m_jointName2.Width = 52
        Me.m_jointName2.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_jointName2.BackColor = Color.Transparent
        Me.m_jointName2.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.m_jointBar2.Left = 54
        Me.m_jointBar2.Top = 56
        Me.m_jointBar2.Width = 232
        Me.m_jointBar2.Height = 26
        Me.m_jointBar2.Minimum = -1000
        Me.m_jointBar2.Maximum = 1000
        Me.m_jointBar2.TickStyle = TickStyle.None
        Me.m_jointBar2.SmallChange = 5
        Me.m_jointBar2.LargeChange = 50
        Me.m_jointBar2.Value = 0

        Me.m_jointLabel2.Text = "0.000"
        Me.m_jointLabel2.Left = 292
        Me.m_jointLabel2.Top = 60
        Me.m_jointLabel2.Width = 58
        Me.m_jointLabel2.TextAlign = ContentAlignment.MiddleRight
        Me.m_jointLabel2.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_jointLabel2.BackColor = Color.Transparent
        Me.m_jointLabel2.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.jointsHost.Controls.Add(Me.m_jointName2)
        Me.jointsHost.Controls.Add(Me.m_jointBar2)
        Me.jointsHost.Controls.Add(Me.m_jointLabel2)

        ' 关节 4 (J4 腕转)
        Me.m_jointName3.Text = "J4 腕转"
        Me.m_jointName3.Left = 0
        Me.m_jointName3.Top = 88
        Me.m_jointName3.Width = 52
        Me.m_jointName3.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_jointName3.BackColor = Color.Transparent
        Me.m_jointName3.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.m_jointBar3.Left = 54
        Me.m_jointBar3.Top = 84
        Me.m_jointBar3.Width = 232
        Me.m_jointBar3.Height = 26
        Me.m_jointBar3.Minimum = -1000
        Me.m_jointBar3.Maximum = 1000
        Me.m_jointBar3.TickStyle = TickStyle.None
        Me.m_jointBar3.SmallChange = 5
        Me.m_jointBar3.LargeChange = 50
        Me.m_jointBar3.Value = 0

        Me.m_jointLabel3.Text = "0.000"
        Me.m_jointLabel3.Left = 292
        Me.m_jointLabel3.Top = 88
        Me.m_jointLabel3.Width = 58
        Me.m_jointLabel3.TextAlign = ContentAlignment.MiddleRight
        Me.m_jointLabel3.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_jointLabel3.BackColor = Color.Transparent
        Me.m_jointLabel3.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.jointsHost.Controls.Add(Me.m_jointName3)
        Me.jointsHost.Controls.Add(Me.m_jointBar3)
        Me.jointsHost.Controls.Add(Me.m_jointLabel3)

        ' 关节 5 (J5 腕摆)
        Me.m_jointName4.Text = "J5 腕摆"
        Me.m_jointName4.Left = 0
        Me.m_jointName4.Top = 116
        Me.m_jointName4.Width = 52
        Me.m_jointName4.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_jointName4.BackColor = Color.Transparent
        Me.m_jointName4.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.m_jointBar4.Left = 54
        Me.m_jointBar4.Top = 112
        Me.m_jointBar4.Width = 232
        Me.m_jointBar4.Height = 26
        Me.m_jointBar4.Minimum = -1000
        Me.m_jointBar4.Maximum = 1000
        Me.m_jointBar4.TickStyle = TickStyle.None
        Me.m_jointBar4.SmallChange = 5
        Me.m_jointBar4.LargeChange = 50
        Me.m_jointBar4.Value = 0

        Me.m_jointLabel4.Text = "0.000"
        Me.m_jointLabel4.Left = 292
        Me.m_jointLabel4.Top = 116
        Me.m_jointLabel4.Width = 58
        Me.m_jointLabel4.TextAlign = ContentAlignment.MiddleRight
        Me.m_jointLabel4.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_jointLabel4.BackColor = Color.Transparent
        Me.m_jointLabel4.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.jointsHost.Controls.Add(Me.m_jointName4)
        Me.jointsHost.Controls.Add(Me.m_jointBar4)
        Me.jointsHost.Controls.Add(Me.m_jointLabel4)

        ' 关节 6 (J6 腕旋)
        Me.m_jointName5.Text = "J6 腕旋"
        Me.m_jointName5.Left = 0
        Me.m_jointName5.Top = 144
        Me.m_jointName5.Width = 52
        Me.m_jointName5.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_jointName5.BackColor = Color.Transparent
        Me.m_jointName5.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.m_jointBar5.Left = 54
        Me.m_jointBar5.Top = 140
        Me.m_jointBar5.Width = 232
        Me.m_jointBar5.Height = 26
        Me.m_jointBar5.Minimum = -1000
        Me.m_jointBar5.Maximum = 1000
        Me.m_jointBar5.TickStyle = TickStyle.None
        Me.m_jointBar5.SmallChange = 5
        Me.m_jointBar5.LargeChange = 50
        Me.m_jointBar5.Value = 0

        Me.m_jointLabel5.Text = "0.000"
        Me.m_jointLabel5.Left = 292
        Me.m_jointLabel5.Top = 144
        Me.m_jointLabel5.Width = 58
        Me.m_jointLabel5.TextAlign = ContentAlignment.MiddleRight
        Me.m_jointLabel5.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_jointLabel5.BackColor = Color.Transparent
        Me.m_jointLabel5.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.jointsHost.Controls.Add(Me.m_jointName5)
        Me.jointsHost.Controls.Add(Me.m_jointBar5)
        Me.jointsHost.Controls.Add(Me.m_jointLabel5)

        ' TCP 位姿卡片
        Me.tcpCard.Dock = DockStyle.None
        Me.tcpCard.Width = 352
        Me.tcpCard.Height = 86
        Me.tcpCard.Margin = New Padding(0, 0, 0, 8)
        Me.tcpCard.BackColor = Color.White
        Me.tcpCard.Padding = New Padding(12, 10, 12, 10)

        Me.tcpCardHeader.Text = "TCP 位姿"
        Me.tcpCardHeader.Dock = DockStyle.Top
        Me.tcpCardHeader.Height = 22
        Me.tcpCardHeader.ForeColor = Color.FromArgb(30, 41, 59)
        Me.tcpCardHeader.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        Me.tcpCardHeader.BackColor = Color.Transparent
        Me.tcpCard.Controls.Add(Me.tcpCardHeader)

        Me.m_lblTcp.Text = "x 0.000  y 0.000  z 0.000" & vbCrLf & "rx 0.0  ry 0.0  rz 0.0"
        Me.m_lblTcp.Dock = DockStyle.Fill
        Me.m_lblTcp.ForeColor = Color.FromArgb(30, 41, 59)
        Me.m_lblTcp.Font = New Font("Consolas", 9.0F, FontStyle.Regular)
        Me.m_lblTcp.BackColor = Color.Transparent
        Me.tcpCard.Controls.Add(Me.m_lblTcp)

        ' 夹爪卡片
        Me.gripCard.Dock = DockStyle.None
        Me.gripCard.Width = 352
        Me.gripCard.Height = 116
        Me.gripCard.Margin = New Padding(0, 0, 0, 8)
        Me.gripCard.BackColor = Color.White
        Me.gripCard.Padding = New Padding(12, 10, 12, 10)

        Me.gripCardHeader.Text = "夹爪"
        Me.gripCardHeader.Dock = DockStyle.Top
        Me.gripCardHeader.Height = 22
        Me.gripCardHeader.ForeColor = Color.FromArgb(30, 41, 59)
        Me.gripCardHeader.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        Me.gripCardHeader.BackColor = Color.Transparent
        Me.gripCard.Controls.Add(Me.gripCardHeader)

        Me.gripFlow.Dock = DockStyle.Fill
        Me.gripFlow.FlowDirection = FlowDirection.LeftToRight
        Me.gripFlow.WrapContents = True
        Me.gripFlow.Padding = New Padding(0, 4, 0, 0)
        Me.gripFlow.BackColor = Color.Transparent

        Me.m_lblGripper.Text = "开口 0.036 m   夹持力 0 N"
        Me.m_lblGripper.Width = 348
        Me.m_lblGripper.Height = 22
        Me.m_lblGripper.ForeColor = Color.FromArgb(30, 41, 59)
        Me.m_lblGripper.Font = New Font("Consolas", 8.0F, FontStyle.Regular)
        Me.m_lblGripper.BackColor = Color.Transparent

        ' m_btnRelease（描边按钮：AccentDim，76 宽）
        Me.m_btnRelease.Text = "张开"
        Me.m_btnRelease.Width = 76
        Me.m_btnRelease.Height = 30
        Me.m_btnRelease.FlatStyle = FlatStyle.Flat
        Me.m_btnRelease.BackColor = Color.White
        Me.m_btnRelease.ForeColor = Color.FromArgb(100, 116, 139)
        Me.m_btnRelease.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnRelease.Cursor = Cursors.Hand
        Me.m_btnRelease.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnRelease.TabStop = False
        Me.m_btnRelease.FlatAppearance.BorderSize = 1
        Me.m_btnRelease.FlatAppearance.BorderColor = Color.FromArgb(100, 116, 139)
        Me.m_btnRelease.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnRelease.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        ' m_btnGrip（描边按钮：Accent，76 宽）
        Me.m_btnGrip.Text = "夹紧"
        Me.m_btnGrip.Width = 76
        Me.m_btnGrip.Height = 30
        Me.m_btnGrip.FlatStyle = FlatStyle.Flat
        Me.m_btnGrip.BackColor = Color.White
        Me.m_btnGrip.ForeColor = Color.FromArgb(14, 165, 233)
        Me.m_btnGrip.Font = New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
        Me.m_btnGrip.Cursor = Cursors.Hand
        Me.m_btnGrip.Margin = New Padding(4, 0, 4, 0)
        Me.m_btnGrip.TabStop = False
        Me.m_btnGrip.FlatAppearance.BorderSize = 1
        Me.m_btnGrip.FlatAppearance.BorderColor = Color.FromArgb(14, 165, 233)
        Me.m_btnGrip.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        Me.m_btnGrip.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        Me.m_numGripForce.Minimum = 1D
        Me.m_numGripForce.Maximum = 120D
        Me.m_numGripForce.Value = 35D
        Me.m_numGripForce.Increment = 5D
        Me.m_numGripForce.Width = 66
        Me.m_numGripForce.BackColor = Color.White
        Me.m_numGripForce.ForeColor = Color.FromArgb(30, 41, 59)
        Me.m_numGripForce.BorderStyle = BorderStyle.FixedSingle
        Me.m_numGripForce.Margin = New Padding(6, 2, 4, 0)

        Me.forceLabel.Text = "N"
        Me.forceLabel.AutoSize = True
        Me.forceLabel.ForeColor = Color.FromArgb(71, 85, 105)
        Me.forceLabel.Margin = New Padding(0, 6, 0, 0)

        Me.gripFlow.Controls.Add(Me.m_lblGripper)
        Me.gripFlow.Controls.Add(Me.m_btnRelease)
        Me.gripFlow.Controls.Add(Me.m_btnGrip)
        Me.gripFlow.Controls.Add(Me.m_numGripForce)
        Me.gripFlow.Controls.Add(Me.forceLabel)
        Me.gripCard.Controls.Add(Me.gripFlow)

        ' 夹爪相机卡片
        Me.cameraCard.Dock = DockStyle.None
        Me.cameraCard.Width = 352
        Me.cameraCard.Height = 200
        Me.cameraCard.Margin = New Padding(0, 0, 0, 8)
        Me.cameraCard.BackColor = Color.White
        Me.cameraCard.Padding = New Padding(12, 10, 12, 10)

        Me.cameraCardHeader.Text = "夹爪相机 · 第一视角"
        Me.cameraCardHeader.Dock = DockStyle.Top
        Me.cameraCardHeader.Height = 22
        Me.cameraCardHeader.ForeColor = Color.FromArgb(30, 41, 59)
        Me.cameraCardHeader.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        Me.cameraCardHeader.BackColor = Color.Transparent
        Me.cameraCard.Controls.Add(Me.cameraCardHeader)

        Me.m_picCamera.Location = New Point(12, 32)
        Me.m_picCamera.Size = New Size(328, 138)
        Me.m_picCamera.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        Me.m_picCamera.BackColor = Color.FromArgb(248, 250, 252)
        Me.m_picCamera.SizeMode = PictureBoxSizeMode.Zoom
        Me.m_picCamera.BorderStyle = BorderStyle.FixedSingle
        Me.m_picCamera.Margin = New Padding(0)
        Me.cameraCard.Controls.Add(Me.m_picCamera)

        Me.m_lblCamera.Text = "等待相机第一帧…"
        Me.m_lblCamera.Location = New Point(12, 173)
        Me.m_lblCamera.Size = New Size(328, 16)
        Me.m_lblCamera.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        Me.m_lblCamera.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_lblCamera.Font = New Font("Consolas", 8.0F, FontStyle.Regular)
        Me.m_lblCamera.BackColor = Color.Transparent
        Me.m_lblCamera.TextAlign = ContentAlignment.MiddleLeft
        Me.cameraCard.Controls.Add(Me.m_lblCamera)

        ' 场景物体卡片
        Me.objectCard.Dock = DockStyle.None
        Me.objectCard.Width = 352
        Me.objectCard.Height = 124
        Me.objectCard.Margin = New Padding(0, 0, 0, 8)
        Me.objectCard.BackColor = Color.White
        Me.objectCard.Padding = New Padding(12, 10, 12, 10)

        Me.objectCardHeader.Text = "场景物体"
        Me.objectCardHeader.Dock = DockStyle.Top
        Me.objectCardHeader.Height = 22
        Me.objectCardHeader.ForeColor = Color.FromArgb(30, 41, 59)
        Me.objectCardHeader.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        Me.objectCardHeader.BackColor = Color.Transparent
        Me.objectCard.Controls.Add(Me.objectCardHeader)

        Me.m_lstObjects.Dock = DockStyle.Fill
        Me.m_lstObjects.BackColor = Color.White
        Me.m_lstObjects.ForeColor = Color.FromArgb(30, 41, 59)
        Me.m_lstObjects.BorderStyle = BorderStyle.None
        Me.m_lstObjects.Font = New Font("Consolas", 8.0F, FontStyle.Regular)
        Me.m_lstObjects.IntegralHeight = False
        Me.objectCard.Controls.Add(Me.m_lstObjects)

        ' FlowLayoutPanel 按添加顺序自上而下排列；相机放最前，保证一直可见
        Me.m_side.Controls.Add(Me.cameraCard)
        Me.m_side.Controls.Add(Me.jointCard)
        Me.m_side.Controls.Add(Me.tcpCard)
        Me.m_side.Controls.Add(Me.gripCard)
        Me.m_side.Controls.Add(Me.objectCard)

        ' ---------------- 底部日志 ----------------
        Me.m_bottom.Dock = DockStyle.Bottom
        Me.m_bottom.Height = 140
        Me.m_bottom.BackColor = Color.FromArgb(248, 250, 252)
        Me.m_bottom.Padding = New Padding(10, 6, 10, 6)

        Me.m_log.Dock = DockStyle.Fill
        Me.m_log.BackColor = Color.FromArgb(248, 250, 252)
        Me.m_log.ForeColor = Color.FromArgb(30, 41, 59)
        Me.m_log.BorderStyle = BorderStyle.None
        Me.m_log.Font = New Font("Consolas", 8.0F, FontStyle.Regular)
        Me.m_log.ReadOnly = True
        Me.m_log.WordWrap = False
        Me.m_log.ScrollBars = RichTextBoxScrollBars.Vertical
        Me.m_log.DetectUrls = False
        Me.m_bottom.Controls.Add(Me.m_log)

        ' ---------------- 三维视口 ----------------
        Me.m_viewport.Dock = DockStyle.Fill
        Me.m_viewport.BackColor = Color.FromArgb(248, 250, 252)
        Me.m_viewport.Padding = New Padding(0)

        Me.m_canvas.Dock = DockStyle.Fill
        Me.m_viewport.Controls.Add(Me.m_canvas)

        ' ---------------- 状态栏 ----------------
        Me.m_status.BackColor = Color.FromArgb(248, 250, 252)
        Me.m_status.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_status.SizingGrip = False
        Me.m_status.Font = New Font("Consolas", 8.0F, FontStyle.Regular)

        Me.m_statusLabel.Text = "就绪"
        Me.m_statusLabel.Spring = True
        Me.m_statusLabel.TextAlign = ContentAlignment.MiddleLeft

        Me.m_lblStats.Text = "FPS 0 · 步/秒 0 · 三角面 0"
        Me.m_lblStats.AutoSize = True
        Me.m_lblStats.ForeColor = Color.FromArgb(71, 85, 105)
        Me.m_lblStats.BackColor = Color.Transparent

        Me.m_status.Items.Add(Me.m_statusLabel)

        ' ---------------- 组装 ----------------
        Me.Controls.Add(Me.m_viewport)
        Me.Controls.Add(Me.m_side)
        Me.Controls.Add(Me.m_bottom)
        Me.Controls.Add(Me.m_top)
        Me.Controls.Add(Me.m_status)

        Me.m_timer.Interval = 16

        Me.ResumeLayout(False)
        Me.PerformLayout()
    End Sub

End Class
