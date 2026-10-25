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
        components = New ComponentModel.Container()
        m_top = New Panel()
        Panel1 = New Panel()
        m_btnStart = New Button()
        m_btnStopServer = New Button()
        m_numRealtime = New NumericUpDown()
        m_btnEnable = New Button()
        realtimeLabel = New Label()
        m_btnHome = New Button()
        m_btnReset = New Button()
        m_btnHold = New Button()
        m_btnEstop = New Button()
        brand = New Label()
        tcpCard = New Panel()
        m_lblTcp = New Label()
        tcpCardHeader = New Label()
        m_lblService = New Label()
        m_side = New FlowLayoutPanel()
        cameraCard = New Panel()
        cameraCardHeader = New Label()
        m_picCamera = New PictureBox()
        m_lblCamera = New Label()
        jointCard = New Panel()
        jointCardHeader = New Label()
        jointsHost = New Panel()
        m_jointBar4 = New TrackBar()
        m_jointName0 = New Label()
        m_jointBar0 = New TrackBar()
        m_jointLabel0 = New Label()
        m_jointName1 = New Label()
        m_jointBar1 = New TrackBar()
        m_jointLabel1 = New Label()
        m_jointName2 = New Label()
        m_jointBar2 = New TrackBar()
        m_jointLabel2 = New Label()
        m_jointName3 = New Label()
        m_jointBar3 = New TrackBar()
        m_jointLabel3 = New Label()
        m_jointName4 = New Label()
        m_jointLabel4 = New Label()
        m_jointName5 = New Label()
        m_jointBar5 = New TrackBar()
        m_jointLabel5 = New Label()
        gripCard = New Panel()
        gripFlow = New FlowLayoutPanel()
        m_lblGripper = New Label()
        m_btnRelease = New Button()
        m_btnGrip = New Button()
        m_numGripForce = New NumericUpDown()
        forceLabel = New Label()
        gripCardHeader = New Label()
        objectCard = New Panel()
        objectCardHeader = New Label()
        m_lstObjects = New ListBox()
        m_bottom = New Panel()
        m_log = New RichTextBox()
        m_viewport = New Panel()
        m_canvas = New Drawing.DirectX.DxScene3DCanvas()
        m_timer = New Timer(components)
        m_status = New StatusStrip()
        m_statusLabel = New ToolStripStatusLabel()
        m_lblStats = New Label()
        m_top.SuspendLayout()
        Panel1.SuspendLayout()
        CType(m_numRealtime, ComponentModel.ISupportInitialize).BeginInit()
        tcpCard.SuspendLayout()
        m_side.SuspendLayout()
        cameraCard.SuspendLayout()
        CType(m_picCamera, ComponentModel.ISupportInitialize).BeginInit()
        jointCard.SuspendLayout()
        jointsHost.SuspendLayout()
        CType(m_jointBar4, ComponentModel.ISupportInitialize).BeginInit()
        CType(m_jointBar0, ComponentModel.ISupportInitialize).BeginInit()
        CType(m_jointBar1, ComponentModel.ISupportInitialize).BeginInit()
        CType(m_jointBar2, ComponentModel.ISupportInitialize).BeginInit()
        CType(m_jointBar3, ComponentModel.ISupportInitialize).BeginInit()
        CType(m_jointBar5, ComponentModel.ISupportInitialize).BeginInit()
        gripCard.SuspendLayout()
        gripFlow.SuspendLayout()
        CType(m_numGripForce, ComponentModel.ISupportInitialize).BeginInit()
        objectCard.SuspendLayout()
        m_bottom.SuspendLayout()
        m_viewport.SuspendLayout()
        m_status.SuspendLayout()
        SuspendLayout()
        ' 
        ' m_top
        ' 
        m_top.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_top.Controls.Add(Panel1)
        m_top.Controls.Add(brand)
        m_top.Controls.Add(tcpCard)
        m_top.Controls.Add(m_lblService)
        m_top.Dock = DockStyle.Top
        m_top.Location = New Point(0, 0)
        m_top.Name = "m_top"
        m_top.Padding = New Padding(12, 0, 12, 0)
        m_top.Size = New Size(1608, 97)
        m_top.TabIndex = 3
        ' 
        ' Panel1
        ' 
        Panel1.Controls.Add(m_btnStart)
        Panel1.Controls.Add(m_btnStopServer)
        Panel1.Controls.Add(m_numRealtime)
        Panel1.Controls.Add(m_btnEnable)
        Panel1.Controls.Add(realtimeLabel)
        Panel1.Controls.Add(m_btnHome)
        Panel1.Controls.Add(m_btnReset)
        Panel1.Controls.Add(m_btnHold)
        Panel1.Controls.Add(m_btnEstop)
        Panel1.Location = New Point(196, 25)
        Panel1.Name = "Panel1"
        Panel1.Size = New Size(803, 45)
        Panel1.TabIndex = 1
        ' 
        ' m_btnStart
        ' 
        m_btnStart.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        m_btnStart.Cursor = Cursors.Hand
        m_btnStart.FlatAppearance.BorderSize = 0
        m_btnStart.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(29), CByte(78), CByte(216))
        m_btnStart.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(29), CByte(78), CByte(216))
        m_btnStart.FlatStyle = FlatStyle.Flat
        m_btnStart.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnStart.ForeColor = Color.White
        m_btnStart.Location = New Point(8, 6)
        m_btnStart.Margin = New Padding(4, 0, 4, 0)
        m_btnStart.Name = "m_btnStart"
        m_btnStart.Size = New Size(92, 32)
        m_btnStart.TabIndex = 0
        m_btnStart.TabStop = False
        m_btnStart.Text = "启动服务"
        m_btnStart.UseVisualStyleBackColor = False
        ' 
        ' m_btnStopServer
        ' 
        m_btnStopServer.BackColor = Color.White
        m_btnStopServer.Cursor = Cursors.Hand
        m_btnStopServer.FlatAppearance.BorderColor = Color.FromArgb(CByte(245), CByte(158), CByte(11))
        m_btnStopServer.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnStopServer.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnStopServer.FlatStyle = FlatStyle.Flat
        m_btnStopServer.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnStopServer.ForeColor = Color.FromArgb(CByte(245), CByte(158), CByte(11))
        m_btnStopServer.Location = New Point(108, 6)
        m_btnStopServer.Margin = New Padding(4, 0, 4, 0)
        m_btnStopServer.Name = "m_btnStopServer"
        m_btnStopServer.Size = New Size(92, 30)
        m_btnStopServer.TabIndex = 1
        m_btnStopServer.TabStop = False
        m_btnStopServer.Text = "停止服务"
        m_btnStopServer.UseVisualStyleBackColor = False
        ' 
        ' m_numRealtime
        ' 
        m_numRealtime.BackColor = Color.White
        m_numRealtime.BorderStyle = BorderStyle.FixedSingle
        m_numRealtime.DecimalPlaces = 1
        m_numRealtime.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_numRealtime.Increment = New Decimal(New Integer() {5, 0, 0, 65536})
        m_numRealtime.Location = New Point(732, 8)
        m_numRealtime.Margin = New Padding(0, 2, 0, 0)
        m_numRealtime.Maximum = New Decimal(New Integer() {20, 0, 0, 0})
        m_numRealtime.Name = "m_numRealtime"
        m_numRealtime.Size = New Size(62, 23)
        m_numRealtime.TabIndex = 8
        m_numRealtime.Value = New Decimal(New Integer() {1, 0, 0, 0})
        ' 
        ' m_btnEnable
        ' 
        m_btnEnable.BackColor = Color.White
        m_btnEnable.Cursor = Cursors.Hand
        m_btnEnable.FlatAppearance.BorderColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_btnEnable.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnEnable.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnEnable.FlatStyle = FlatStyle.Flat
        m_btnEnable.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnEnable.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_btnEnable.Location = New Point(208, 6)
        m_btnEnable.Margin = New Padding(4, 0, 4, 0)
        m_btnEnable.Name = "m_btnEnable"
        m_btnEnable.Size = New Size(92, 30)
        m_btnEnable.TabIndex = 2
        m_btnEnable.TabStop = False
        m_btnEnable.Text = "伺服使能"
        m_btnEnable.UseVisualStyleBackColor = False
        ' 
        ' realtimeLabel
        ' 
        realtimeLabel.AutoSize = True
        realtimeLabel.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        realtimeLabel.Location = New Point(696, 12)
        realtimeLabel.Margin = New Padding(12, 6, 4, 0)
        realtimeLabel.Name = "realtimeLabel"
        realtimeLabel.Size = New Size(32, 17)
        realtimeLabel.TabIndex = 7
        realtimeLabel.Text = "倍率"
        ' 
        ' m_btnHome
        ' 
        m_btnHome.BackColor = Color.White
        m_btnHome.Cursor = Cursors.Hand
        m_btnHome.FlatAppearance.BorderColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_btnHome.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnHome.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnHome.FlatStyle = FlatStyle.Flat
        m_btnHome.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnHome.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_btnHome.Location = New Point(308, 6)
        m_btnHome.Margin = New Padding(4, 0, 4, 0)
        m_btnHome.Name = "m_btnHome"
        m_btnHome.Size = New Size(92, 30)
        m_btnHome.TabIndex = 3
        m_btnHome.TabStop = False
        m_btnHome.Text = "回零"
        m_btnHome.UseVisualStyleBackColor = False
        ' 
        ' m_btnReset
        ' 
        m_btnReset.BackColor = Color.White
        m_btnReset.Cursor = Cursors.Hand
        m_btnReset.FlatAppearance.BorderColor = Color.FromArgb(CByte(245), CByte(158), CByte(11))
        m_btnReset.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnReset.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnReset.FlatStyle = FlatStyle.Flat
        m_btnReset.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnReset.ForeColor = Color.FromArgb(CByte(245), CByte(158), CByte(11))
        m_btnReset.Location = New Point(588, 6)
        m_btnReset.Margin = New Padding(4, 0, 4, 0)
        m_btnReset.Name = "m_btnReset"
        m_btnReset.Size = New Size(92, 30)
        m_btnReset.TabIndex = 6
        m_btnReset.TabStop = False
        m_btnReset.Text = "复位场景"
        m_btnReset.UseVisualStyleBackColor = False
        ' 
        ' m_btnHold
        ' 
        m_btnHold.BackColor = Color.White
        m_btnHold.Cursor = Cursors.Hand
        m_btnHold.FlatAppearance.BorderColor = Color.FromArgb(CByte(100), CByte(116), CByte(139))
        m_btnHold.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnHold.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnHold.FlatStyle = FlatStyle.Flat
        m_btnHold.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnHold.ForeColor = Color.FromArgb(CByte(100), CByte(116), CByte(139))
        m_btnHold.Location = New Point(408, 6)
        m_btnHold.Margin = New Padding(4, 0, 4, 0)
        m_btnHold.Name = "m_btnHold"
        m_btnHold.Size = New Size(92, 30)
        m_btnHold.TabIndex = 4
        m_btnHold.TabStop = False
        m_btnHold.Text = "保持"
        m_btnHold.UseVisualStyleBackColor = False
        ' 
        ' m_btnEstop
        ' 
        m_btnEstop.BackColor = Color.White
        m_btnEstop.Cursor = Cursors.Hand
        m_btnEstop.FlatAppearance.BorderColor = Color.FromArgb(CByte(220), CByte(38), CByte(38))
        m_btnEstop.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnEstop.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnEstop.FlatStyle = FlatStyle.Flat
        m_btnEstop.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnEstop.ForeColor = Color.FromArgb(CByte(220), CByte(38), CByte(38))
        m_btnEstop.Location = New Point(508, 6)
        m_btnEstop.Margin = New Padding(4, 0, 4, 0)
        m_btnEstop.Name = "m_btnEstop"
        m_btnEstop.Size = New Size(72, 30)
        m_btnEstop.TabIndex = 5
        m_btnEstop.TabStop = False
        m_btnEstop.Text = "急停"
        m_btnEstop.UseVisualStyleBackColor = False
        ' 
        ' brand
        ' 
        brand.BackColor = Color.Transparent
        brand.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        brand.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        brand.Location = New Point(12, 25)
        brand.Name = "brand"
        brand.Size = New Size(190, 23)
        brand.TabIndex = 1
        brand.Text = "VIRTUAL WORKSHOP"
        brand.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' tcpCard
        ' 
        tcpCard.BackColor = Color.White
        tcpCard.Controls.Add(m_lblTcp)
        tcpCard.Controls.Add(tcpCardHeader)
        tcpCard.Location = New Point(1020, 6)
        tcpCard.Margin = New Padding(0, 0, 0, 8)
        tcpCard.Name = "tcpCard"
        tcpCard.Padding = New Padding(12, 10, 12, 10)
        tcpCard.Size = New Size(302, 86)
        tcpCard.TabIndex = 2
        ' 
        ' m_lblTcp
        ' 
        m_lblTcp.BackColor = Color.Transparent
        m_lblTcp.Dock = DockStyle.Fill
        m_lblTcp.Font = New Font("Consolas", 9F)
        m_lblTcp.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_lblTcp.Location = New Point(12, 32)
        m_lblTcp.Name = "m_lblTcp"
        m_lblTcp.Size = New Size(278, 44)
        m_lblTcp.TabIndex = 1
        m_lblTcp.Text = "x 0.000  y 0.000  z 0.000" & vbCrLf & "rx 0.0  ry 0.0  rz 0.0"
        ' 
        ' tcpCardHeader
        ' 
        tcpCardHeader.BackColor = Color.Transparent
        tcpCardHeader.Dock = DockStyle.Top
        tcpCardHeader.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        tcpCardHeader.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        tcpCardHeader.Location = New Point(12, 10)
        tcpCardHeader.Name = "tcpCardHeader"
        tcpCardHeader.Size = New Size(278, 22)
        tcpCardHeader.TabIndex = 0
        tcpCardHeader.Text = "TCP 位姿"
        ' 
        ' m_lblService
        ' 
        m_lblService.BackColor = Color.Transparent
        m_lblService.Dock = DockStyle.Right
        m_lblService.Font = New Font("Consolas", 8F)
        m_lblService.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lblService.Location = New Point(1346, 0)
        m_lblService.Name = "m_lblService"
        m_lblService.Size = New Size(250, 97)
        m_lblService.TabIndex = 2
        m_lblService.Text = "离线"
        m_lblService.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_side
        ' 
        m_side.AutoScroll = True
        m_side.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_side.Controls.Add(cameraCard)
        m_side.Controls.Add(jointCard)
        m_side.Controls.Add(gripCard)
        m_side.Controls.Add(objectCard)
        m_side.Dock = DockStyle.Right
        m_side.FlowDirection = FlowDirection.TopDown
        m_side.Location = New Point(1222, 97)
        m_side.Name = "m_side"
        m_side.Padding = New Padding(8)
        m_side.Size = New Size(386, 826)
        m_side.TabIndex = 1
        m_side.WrapContents = False
        ' 
        ' cameraCard
        ' 
        cameraCard.BackColor = Color.White
        cameraCard.Controls.Add(cameraCardHeader)
        cameraCard.Controls.Add(m_picCamera)
        cameraCard.Controls.Add(m_lblCamera)
        cameraCard.Location = New Point(8, 8)
        cameraCard.Margin = New Padding(0, 0, 0, 8)
        cameraCard.Name = "cameraCard"
        cameraCard.Padding = New Padding(12, 10, 12, 10)
        cameraCard.Size = New Size(352, 210)
        cameraCard.TabIndex = 0
        ' 
        ' cameraCardHeader
        ' 
        cameraCardHeader.BackColor = Color.Transparent
        cameraCardHeader.Dock = DockStyle.Top
        cameraCardHeader.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        cameraCardHeader.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        cameraCardHeader.Location = New Point(12, 10)
        cameraCardHeader.Name = "cameraCardHeader"
        cameraCardHeader.Size = New Size(328, 22)
        cameraCardHeader.TabIndex = 0
        cameraCardHeader.Text = "夹爪相机 · 第一视角"
        ' 
        ' m_picCamera
        ' 
        m_picCamera.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        m_picCamera.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_picCamera.BorderStyle = BorderStyle.FixedSingle
        m_picCamera.Location = New Point(12, 32)
        m_picCamera.Margin = New Padding(0)
        m_picCamera.Name = "m_picCamera"
        m_picCamera.Size = New Size(328, 138)
        m_picCamera.SizeMode = PictureBoxSizeMode.Zoom
        m_picCamera.TabIndex = 1
        m_picCamera.TabStop = False
        ' 
        ' m_lblCamera
        ' 
        m_lblCamera.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        m_lblCamera.BackColor = Color.Transparent
        m_lblCamera.Font = New Font("Consolas", 8F)
        m_lblCamera.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lblCamera.Location = New Point(12, 173)
        m_lblCamera.Name = "m_lblCamera"
        m_lblCamera.Size = New Size(328, 37)
        m_lblCamera.TabIndex = 2
        m_lblCamera.Text = "等待相机第一帧…"
        m_lblCamera.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' jointCard
        ' 
        jointCard.BackColor = Color.White
        jointCard.Controls.Add(jointCardHeader)
        jointCard.Controls.Add(jointsHost)
        jointCard.Location = New Point(8, 226)
        jointCard.Margin = New Padding(0, 0, 0, 8)
        jointCard.Name = "jointCard"
        jointCard.Padding = New Padding(12, 10, 12, 10)
        jointCard.Size = New Size(352, 338)
        jointCard.TabIndex = 1
        ' 
        ' jointCardHeader
        ' 
        jointCardHeader.BackColor = Color.Transparent
        jointCardHeader.Dock = DockStyle.Top
        jointCardHeader.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        jointCardHeader.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        jointCardHeader.Location = New Point(12, 10)
        jointCardHeader.Name = "jointCardHeader"
        jointCardHeader.Size = New Size(328, 22)
        jointCardHeader.TabIndex = 0
        jointCardHeader.Text = "关节状态"
        ' 
        ' jointsHost
        ' 
        jointsHost.AutoScroll = True
        jointsHost.BackColor = Color.Transparent
        jointsHost.Controls.Add(m_jointBar4)
        jointsHost.Controls.Add(m_jointName0)
        jointsHost.Controls.Add(m_jointBar0)
        jointsHost.Controls.Add(m_jointLabel0)
        jointsHost.Controls.Add(m_jointName1)
        jointsHost.Controls.Add(m_jointBar1)
        jointsHost.Controls.Add(m_jointLabel1)
        jointsHost.Controls.Add(m_jointName2)
        jointsHost.Controls.Add(m_jointBar2)
        jointsHost.Controls.Add(m_jointLabel2)
        jointsHost.Controls.Add(m_jointName3)
        jointsHost.Controls.Add(m_jointBar3)
        jointsHost.Controls.Add(m_jointLabel3)
        jointsHost.Controls.Add(m_jointName4)
        jointsHost.Controls.Add(m_jointLabel4)
        jointsHost.Controls.Add(m_jointName5)
        jointsHost.Controls.Add(m_jointBar5)
        jointsHost.Controls.Add(m_jointLabel5)
        jointsHost.Dock = DockStyle.Fill
        jointsHost.Location = New Point(12, 10)
        jointsHost.Name = "jointsHost"
        jointsHost.Padding = New Padding(0, 4, 0, 0)
        jointsHost.Size = New Size(328, 318)
        jointsHost.TabIndex = 1
        ' 
        ' m_jointBar4
        ' 
        m_jointBar4.LargeChange = 50
        m_jointBar4.Location = New Point(54, 205)
        m_jointBar4.Maximum = 1000
        m_jointBar4.Minimum = -1000
        m_jointBar4.Name = "m_jointBar4"
        m_jointBar4.Size = New Size(219, 45)
        m_jointBar4.SmallChange = 5
        m_jointBar4.TabIndex = 13
        m_jointBar4.TickStyle = TickStyle.None
        ' 
        ' m_jointName0
        ' 
        m_jointName0.BackColor = Color.Transparent
        m_jointName0.Font = New Font("Consolas", 8F)
        m_jointName0.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_jointName0.Location = New Point(3, 37)
        m_jointName0.Name = "m_jointName0"
        m_jointName0.Size = New Size(52, 23)
        m_jointName0.TabIndex = 0
        m_jointName0.Text = "J1 腰"
        ' 
        ' m_jointBar0
        ' 
        m_jointBar0.LargeChange = 50
        m_jointBar0.Location = New Point(54, 25)
        m_jointBar0.Maximum = 1000
        m_jointBar0.Minimum = -1000
        m_jointBar0.Name = "m_jointBar0"
        m_jointBar0.Size = New Size(219, 45)
        m_jointBar0.SmallChange = 5
        m_jointBar0.TabIndex = 1
        m_jointBar0.TickStyle = TickStyle.None
        ' 
        ' m_jointLabel0
        ' 
        m_jointLabel0.BackColor = Color.Transparent
        m_jointLabel0.Font = New Font("Consolas", 8F)
        m_jointLabel0.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_jointLabel0.Location = New Point(270, 37)
        m_jointLabel0.Name = "m_jointLabel0"
        m_jointLabel0.Size = New Size(58, 23)
        m_jointLabel0.TabIndex = 2
        m_jointLabel0.Text = "0.000"
        m_jointLabel0.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_jointName1
        ' 
        m_jointName1.BackColor = Color.Transparent
        m_jointName1.Font = New Font("Consolas", 8F)
        m_jointName1.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_jointName1.Location = New Point(4, 88)
        m_jointName1.Name = "m_jointName1"
        m_jointName1.Size = New Size(52, 23)
        m_jointName1.TabIndex = 3
        m_jointName1.Text = "J2 肩"
        ' 
        ' m_jointBar1
        ' 
        m_jointBar1.LargeChange = 50
        m_jointBar1.Location = New Point(54, 76)
        m_jointBar1.Maximum = 1000
        m_jointBar1.Minimum = -1000
        m_jointBar1.Name = "m_jointBar1"
        m_jointBar1.Size = New Size(219, 45)
        m_jointBar1.SmallChange = 5
        m_jointBar1.TabIndex = 4
        m_jointBar1.TickStyle = TickStyle.None
        ' 
        ' m_jointLabel1
        ' 
        m_jointLabel1.BackColor = Color.Transparent
        m_jointLabel1.Font = New Font("Consolas", 8F)
        m_jointLabel1.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_jointLabel1.Location = New Point(270, 88)
        m_jointLabel1.Name = "m_jointLabel1"
        m_jointLabel1.Size = New Size(58, 23)
        m_jointLabel1.TabIndex = 5
        m_jointLabel1.Text = "0.000"
        m_jointLabel1.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_jointName2
        ' 
        m_jointName2.BackColor = Color.Transparent
        m_jointName2.Font = New Font("Consolas", 8F)
        m_jointName2.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_jointName2.Location = New Point(4, 133)
        m_jointName2.Name = "m_jointName2"
        m_jointName2.Size = New Size(52, 23)
        m_jointName2.TabIndex = 6
        m_jointName2.Text = "J3 肘"
        ' 
        ' m_jointBar2
        ' 
        m_jointBar2.LargeChange = 50
        m_jointBar2.Location = New Point(54, 122)
        m_jointBar2.Maximum = 1000
        m_jointBar2.Minimum = -1000
        m_jointBar2.Name = "m_jointBar2"
        m_jointBar2.Size = New Size(219, 45)
        m_jointBar2.SmallChange = 5
        m_jointBar2.TabIndex = 7
        m_jointBar2.TickStyle = TickStyle.None
        ' 
        ' m_jointLabel2
        ' 
        m_jointLabel2.BackColor = Color.Transparent
        m_jointLabel2.Font = New Font("Consolas", 8F)
        m_jointLabel2.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_jointLabel2.Location = New Point(267, 133)
        m_jointLabel2.Name = "m_jointLabel2"
        m_jointLabel2.Size = New Size(58, 23)
        m_jointLabel2.TabIndex = 8
        m_jointLabel2.Text = "0.000"
        m_jointLabel2.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_jointName3
        ' 
        m_jointName3.BackColor = Color.Transparent
        m_jointName3.Font = New Font("Consolas", 8F)
        m_jointName3.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_jointName3.Location = New Point(4, 179)
        m_jointName3.Name = "m_jointName3"
        m_jointName3.Size = New Size(52, 23)
        m_jointName3.TabIndex = 9
        m_jointName3.Text = "J4 腕转"
        ' 
        ' m_jointBar3
        ' 
        m_jointBar3.LargeChange = 50
        m_jointBar3.Location = New Point(54, 171)
        m_jointBar3.Maximum = 1000
        m_jointBar3.Minimum = -1000
        m_jointBar3.Name = "m_jointBar3"
        m_jointBar3.Size = New Size(219, 45)
        m_jointBar3.SmallChange = 5
        m_jointBar3.TabIndex = 10
        m_jointBar3.TickStyle = TickStyle.None
        ' 
        ' m_jointLabel3
        ' 
        m_jointLabel3.BackColor = Color.Transparent
        m_jointLabel3.Font = New Font("Consolas", 8F)
        m_jointLabel3.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_jointLabel3.Location = New Point(267, 179)
        m_jointLabel3.Name = "m_jointLabel3"
        m_jointLabel3.Size = New Size(58, 23)
        m_jointLabel3.TabIndex = 11
        m_jointLabel3.Text = "0.000"
        m_jointLabel3.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_jointName4
        ' 
        m_jointName4.BackColor = Color.Transparent
        m_jointName4.Font = New Font("Consolas", 8F)
        m_jointName4.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_jointName4.Location = New Point(4, 219)
        m_jointName4.Name = "m_jointName4"
        m_jointName4.Size = New Size(52, 23)
        m_jointName4.TabIndex = 12
        m_jointName4.Text = "J5 腕摆"
        ' 
        ' m_jointLabel4
        ' 
        m_jointLabel4.BackColor = Color.Transparent
        m_jointLabel4.Font = New Font("Consolas", 8F)
        m_jointLabel4.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_jointLabel4.Location = New Point(267, 219)
        m_jointLabel4.Name = "m_jointLabel4"
        m_jointLabel4.Size = New Size(58, 23)
        m_jointLabel4.TabIndex = 14
        m_jointLabel4.Text = "0.000"
        m_jointLabel4.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_jointName5
        ' 
        m_jointName5.BackColor = Color.Transparent
        m_jointName5.Font = New Font("Consolas", 8F)
        m_jointName5.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_jointName5.Location = New Point(4, 253)
        m_jointName5.Name = "m_jointName5"
        m_jointName5.Size = New Size(52, 23)
        m_jointName5.TabIndex = 15
        m_jointName5.Text = "J6 腕旋"
        ' 
        ' m_jointBar5
        ' 
        m_jointBar5.LargeChange = 50
        m_jointBar5.Location = New Point(54, 253)
        m_jointBar5.Maximum = 1000
        m_jointBar5.Minimum = -1000
        m_jointBar5.Name = "m_jointBar5"
        m_jointBar5.Size = New Size(219, 45)
        m_jointBar5.SmallChange = 5
        m_jointBar5.TabIndex = 16
        m_jointBar5.TickStyle = TickStyle.None
        ' 
        ' m_jointLabel5
        ' 
        m_jointLabel5.BackColor = Color.Transparent
        m_jointLabel5.Font = New Font("Consolas", 8F)
        m_jointLabel5.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_jointLabel5.Location = New Point(270, 264)
        m_jointLabel5.Name = "m_jointLabel5"
        m_jointLabel5.Size = New Size(58, 23)
        m_jointLabel5.TabIndex = 17
        m_jointLabel5.Text = "0.000"
        m_jointLabel5.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' gripCard
        ' 
        gripCard.BackColor = Color.White
        gripCard.Controls.Add(gripFlow)
        gripCard.Controls.Add(gripCardHeader)
        gripCard.Location = New Point(8, 572)
        gripCard.Margin = New Padding(0, 0, 0, 8)
        gripCard.Name = "gripCard"
        gripCard.Padding = New Padding(12, 10, 12, 10)
        gripCard.Size = New Size(352, 116)
        gripCard.TabIndex = 3
        ' 
        ' gripFlow
        ' 
        gripFlow.BackColor = Color.Transparent
        gripFlow.Controls.Add(m_lblGripper)
        gripFlow.Controls.Add(m_btnRelease)
        gripFlow.Controls.Add(m_btnGrip)
        gripFlow.Controls.Add(m_numGripForce)
        gripFlow.Controls.Add(forceLabel)
        gripFlow.Dock = DockStyle.Fill
        gripFlow.Location = New Point(12, 32)
        gripFlow.Name = "gripFlow"
        gripFlow.Padding = New Padding(0, 4, 0, 0)
        gripFlow.Size = New Size(328, 74)
        gripFlow.TabIndex = 1
        ' 
        ' m_lblGripper
        ' 
        m_lblGripper.BackColor = Color.Transparent
        m_lblGripper.Font = New Font("Consolas", 8F)
        m_lblGripper.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_lblGripper.Location = New Point(3, 4)
        m_lblGripper.Name = "m_lblGripper"
        m_lblGripper.Size = New Size(312, 22)
        m_lblGripper.TabIndex = 0
        m_lblGripper.Text = "开口 0.036 m   夹持力 0 N"
        ' 
        ' m_btnRelease
        ' 
        m_btnRelease.BackColor = Color.White
        m_btnRelease.Cursor = Cursors.Hand
        m_btnRelease.FlatAppearance.BorderColor = Color.FromArgb(CByte(100), CByte(116), CByte(139))
        m_btnRelease.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnRelease.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnRelease.FlatStyle = FlatStyle.Flat
        m_btnRelease.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnRelease.ForeColor = Color.FromArgb(CByte(100), CByte(116), CByte(139))
        m_btnRelease.Location = New Point(4, 26)
        m_btnRelease.Margin = New Padding(4, 0, 4, 0)
        m_btnRelease.Name = "m_btnRelease"
        m_btnRelease.Size = New Size(76, 30)
        m_btnRelease.TabIndex = 1
        m_btnRelease.TabStop = False
        m_btnRelease.Text = "张开"
        m_btnRelease.UseVisualStyleBackColor = False
        ' 
        ' m_btnGrip
        ' 
        m_btnGrip.BackColor = Color.White
        m_btnGrip.Cursor = Cursors.Hand
        m_btnGrip.FlatAppearance.BorderColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_btnGrip.FlatAppearance.MouseDownBackColor = Color.FromArgb(CByte(224), CByte(242), CByte(254))
        m_btnGrip.FlatAppearance.MouseOverBackColor = Color.FromArgb(CByte(240), CByte(249), CByte(255))
        m_btnGrip.FlatStyle = FlatStyle.Flat
        m_btnGrip.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnGrip.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_btnGrip.Location = New Point(88, 26)
        m_btnGrip.Margin = New Padding(4, 0, 4, 0)
        m_btnGrip.Name = "m_btnGrip"
        m_btnGrip.Size = New Size(76, 30)
        m_btnGrip.TabIndex = 2
        m_btnGrip.TabStop = False
        m_btnGrip.Text = "夹紧"
        m_btnGrip.UseVisualStyleBackColor = False
        ' 
        ' m_numGripForce
        ' 
        m_numGripForce.BackColor = Color.White
        m_numGripForce.BorderStyle = BorderStyle.FixedSingle
        m_numGripForce.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_numGripForce.Increment = New Decimal(New Integer() {5, 0, 0, 0})
        m_numGripForce.Location = New Point(174, 28)
        m_numGripForce.Margin = New Padding(6, 2, 4, 0)
        m_numGripForce.Maximum = New Decimal(New Integer() {120, 0, 0, 0})
        m_numGripForce.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        m_numGripForce.Name = "m_numGripForce"
        m_numGripForce.Size = New Size(66, 23)
        m_numGripForce.TabIndex = 3
        m_numGripForce.Value = New Decimal(New Integer() {35, 0, 0, 0})
        ' 
        ' forceLabel
        ' 
        forceLabel.AutoSize = True
        forceLabel.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        forceLabel.Location = New Point(244, 32)
        forceLabel.Margin = New Padding(0, 6, 0, 0)
        forceLabel.Name = "forceLabel"
        forceLabel.Size = New Size(18, 17)
        forceLabel.TabIndex = 4
        forceLabel.Text = "N"
        ' 
        ' gripCardHeader
        ' 
        gripCardHeader.BackColor = Color.Transparent
        gripCardHeader.Dock = DockStyle.Top
        gripCardHeader.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        gripCardHeader.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        gripCardHeader.Location = New Point(12, 10)
        gripCardHeader.Name = "gripCardHeader"
        gripCardHeader.Size = New Size(328, 22)
        gripCardHeader.TabIndex = 0
        gripCardHeader.Text = "夹爪"
        ' 
        ' objectCard
        ' 
        objectCard.BackColor = Color.White
        objectCard.Controls.Add(m_lstObjects)
        objectCard.Controls.Add(objectCardHeader)
        objectCard.Location = New Point(8, 696)
        objectCard.Margin = New Padding(0, 0, 0, 8)
        objectCard.Name = "objectCard"
        objectCard.Padding = New Padding(12, 10, 12, 10)
        objectCard.Size = New Size(352, 124)
        objectCard.TabIndex = 4
        ' 
        ' objectCardHeader
        ' 
        objectCardHeader.BackColor = Color.Transparent
        objectCardHeader.Dock = DockStyle.Top
        objectCardHeader.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        objectCardHeader.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        objectCardHeader.Location = New Point(12, 10)
        objectCardHeader.Name = "objectCardHeader"
        objectCardHeader.Size = New Size(328, 22)
        objectCardHeader.TabIndex = 0
        objectCardHeader.Text = "场景物体"
        ' 
        ' m_lstObjects
        ' 
        m_lstObjects.BackColor = Color.White
        m_lstObjects.BorderStyle = BorderStyle.None
        m_lstObjects.Dock = DockStyle.Fill
        m_lstObjects.Font = New Font("Consolas", 8F)
        m_lstObjects.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_lstObjects.IntegralHeight = False
        m_lstObjects.Location = New Point(12, 32)
        m_lstObjects.Name = "m_lstObjects"
        m_lstObjects.Size = New Size(328, 82)
        m_lstObjects.TabIndex = 1
        ' 
        ' m_bottom
        ' 
        m_bottom.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_bottom.Controls.Add(m_log)
        m_bottom.Dock = DockStyle.Bottom
        m_bottom.Location = New Point(0, 923)
        m_bottom.Name = "m_bottom"
        m_bottom.Padding = New Padding(10, 6, 10, 6)
        m_bottom.Size = New Size(1608, 140)
        m_bottom.TabIndex = 2
        ' 
        ' m_log
        ' 
        m_log.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_log.BorderStyle = BorderStyle.None
        m_log.DetectUrls = False
        m_log.Dock = DockStyle.Fill
        m_log.Font = New Font("Consolas", 8F)
        m_log.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_log.Location = New Point(10, 6)
        m_log.Name = "m_log"
        m_log.ReadOnly = True
        m_log.ScrollBars = RichTextBoxScrollBars.Vertical
        m_log.Size = New Size(1588, 128)
        m_log.TabIndex = 0
        m_log.Text = ""
        m_log.WordWrap = False
        ' 
        ' m_viewport
        ' 
        m_viewport.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_viewport.Controls.Add(m_canvas)
        m_viewport.Dock = DockStyle.Fill
        m_viewport.Location = New Point(0, 97)
        m_viewport.Name = "m_viewport"
        m_viewport.Size = New Size(1222, 826)
        m_viewport.TabIndex = 0
        ' 
        ' m_canvas
        ' 
        m_canvas.AutoClear = False
        m_canvas.BackColor = Color.LightSkyBlue
        m_canvas.ColorScheme = Imaging.Drawing2D.Colors.ScalerPalette.viridis
        m_canvas.Dock = DockStyle.Fill
        m_canvas.Location = New Point(0, 0)
        m_canvas.Name = "m_canvas"
        m_canvas.Size = New Size(1222, 826)
        m_canvas.TabIndex = 0
        ' 
        ' m_timer
        ' 
        m_timer.Interval = 16
        ' 
        ' m_status
        ' 
        m_status.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_status.Font = New Font("Consolas", 8F)
        m_status.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_status.Items.AddRange(New ToolStripItem() {m_statusLabel})
        m_status.Location = New Point(0, 1063)
        m_status.Name = "m_status"
        m_status.Size = New Size(1608, 22)
        m_status.SizingGrip = False
        m_status.TabIndex = 4
        ' 
        ' m_statusLabel
        ' 
        m_statusLabel.Name = "m_statusLabel"
        m_statusLabel.Size = New Size(1593, 17)
        m_statusLabel.Spring = True
        m_statusLabel.Text = "就绪"
        m_statusLabel.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' m_lblStats
        ' 
        m_lblStats.AutoSize = True
        m_lblStats.BackColor = Color.Transparent
        m_lblStats.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lblStats.Location = New Point(0, 0)
        m_lblStats.Name = "m_lblStats"
        m_lblStats.Size = New Size(100, 23)
        m_lblStats.TabIndex = 0
        m_lblStats.Text = "FPS 0 · 步/秒 0 · 三角面 0"
        ' 
        ' FormVirtualLab
        ' 
        AutoScaleDimensions = New SizeF(7F, 17F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = Color.White
        ClientSize = New Size(1608, 1085)
        Controls.Add(m_viewport)
        Controls.Add(m_side)
        Controls.Add(m_bottom)
        Controls.Add(m_top)
        Controls.Add(m_status)
        DoubleBuffered = True
        Font = New Font("Microsoft YaHei UI", 9F)
        ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        MinimumSize = New Size(1180, 780)
        Name = "FormVirtualLab"
        Text = "VirtualWorkshop · 六轴机械臂虚拟硬件仿真工作台"
        m_top.ResumeLayout(False)
        Panel1.ResumeLayout(False)
        Panel1.PerformLayout()
        CType(m_numRealtime, ComponentModel.ISupportInitialize).EndInit()
        tcpCard.ResumeLayout(False)
        m_side.ResumeLayout(False)
        cameraCard.ResumeLayout(False)
        CType(m_picCamera, ComponentModel.ISupportInitialize).EndInit()
        jointCard.ResumeLayout(False)
        jointsHost.ResumeLayout(False)
        jointsHost.PerformLayout()
        CType(m_jointBar4, ComponentModel.ISupportInitialize).EndInit()
        CType(m_jointBar0, ComponentModel.ISupportInitialize).EndInit()
        CType(m_jointBar1, ComponentModel.ISupportInitialize).EndInit()
        CType(m_jointBar2, ComponentModel.ISupportInitialize).EndInit()
        CType(m_jointBar3, ComponentModel.ISupportInitialize).EndInit()
        CType(m_jointBar5, ComponentModel.ISupportInitialize).EndInit()
        gripCard.ResumeLayout(False)
        gripFlow.ResumeLayout(False)
        gripFlow.PerformLayout()
        CType(m_numGripForce, ComponentModel.ISupportInitialize).EndInit()
        objectCard.ResumeLayout(False)
        m_bottom.ResumeLayout(False)
        m_viewport.ResumeLayout(False)
        m_status.ResumeLayout(False)
        m_status.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents Panel1 As Panel

End Class
