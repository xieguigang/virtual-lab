Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' 工作台的配色与字体。
''' </summary>
''' <remarks>
''' <para>
''' 这一套样式与 <c>G:\Moira\win32_desktop\Pages\PanelPlayerLeft.Designer.vb</c> 保持一致：
''' 浅色底 + slate 灰阶文字 + sky/blue 强调色，字体统一 Microsoft YaHei UI（等宽用 Consolas）。
''' </para>
''' <list type="table">
'''   <item><term>窗体/侧栏背景</term><description>纯白</description></item>
'''   <item><term>卡片/输入底色</term><description>#F8FAFC（slate-50）</description></item>
'''   <item><term>分区标题</term><description>Microsoft YaHei UI 10pt Bold，#1E293B（slate-800）</description></item>
'''   <item><term>说明文字</term><description>#475569（slate-600）</description></item>
'''   <item><term>数值强调</term><description>#0EA5E9（sky-500）</description></item>
'''   <item><term>主按钮</term><description>#2563EB（blue-600）实心白字，无边框</description></item>
''' </list>
''' </remarks>
Public Module UiTheme

    ' ---- 背景与描边 ----
    Public ReadOnly Background As Color = Color.White
    Public ReadOnly PanelColor As Color = Color.FromArgb(248, 250, 252)
    Public ReadOnly PanelAlt As Color = Color.White
    Public ReadOnly BorderColor As Color = Color.FromArgb(226, 232, 240)

    ''' <summary>三维视口的底色（浅灰白，与参考面板的浅色背景一致）。</summary>
    Public ReadOnly ViewportBackground As Color = Color.FromArgb(248, 250, 252)

    ''' <summary>视口背景网格的颜色（slate-200）。</summary>
    Public ReadOnly ViewportGridColor As Color = Color.FromArgb(203, 213, 225)

    ''' <summary>地面网格的细线（slate-300）。</summary>
    Public ReadOnly GroundGridMinor As Color = Color.FromArgb(203, 213, 225)

    ''' <summary>地面网格的主线（slate-400，每 5 格一条）。</summary>
    Public ReadOnly GroundGridMajor As Color = Color.FromArgb(148, 163, 184)

    ''' <summary>地面网格的基准高度：机械臂基座底面所在的世界 Z（米）。</summary>
    Public Const GroundZ As Double = 0.0

    ' ---- 强调色 ----
    Public ReadOnly Accent As Color = Color.FromArgb(14, 165, 233)
    Public ReadOnly AccentDim As Color = Color.FromArgb(100, 116, 139)
    Public ReadOnly Warn As Color = Color.FromArgb(245, 158, 11)
    Public ReadOnly Danger As Color = Color.FromArgb(220, 38, 38)
    Public ReadOnly Good As Color = Color.FromArgb(22, 163, 74)

    ''' <summary>主按钮底色（blue-600，与参考面板的"加载数据文件夹"一致）。</summary>
    Public ReadOnly PrimaryButton As Color = Color.FromArgb(37, 99, 235)

    ''' <summary>主按钮按下/悬停（blue-700）。</summary>
    Public ReadOnly PrimaryButtonHover As Color = Color.FromArgb(29, 78, 216)

    ' ---- 文字 ----
    Public ReadOnly TextPrimary As Color = Color.FromArgb(30, 41, 59)
    Public ReadOnly TextMuted As Color = Color.FromArgb(71, 85, 105)
    Public ReadOnly TextOnAccent As Color = Color.White

    ' ---- 字体 ----
    Public ReadOnly FontUi As New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
    Public ReadOnly FontTitle As New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
    Public ReadOnly FontBrand As New Font("Microsoft YaHei UI", 11.0F, FontStyle.Bold)
    Public ReadOnly FontMono As New Font("Consolas", 9.0F, FontStyle.Regular)
    Public ReadOnly FontMonoSmall As New Font("Consolas", 8.0F, FontStyle.Regular)

    ' /********************************************************************************/
    '  控件工厂
    ' /********************************************************************************/

    ''' <summary>描边式按钮（白底 + 强调色文字/描边）。</summary>
    Public Function CreateButton(text As String, accent As Color, Optional width As Integer = 92) As Button
        Dim button As New Button With {
            .Text = text,
            .Width = width,
            .Height = 30,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.White,
            .ForeColor = accent,
            .Font = FontUi,
            .Cursor = Cursors.Hand,
            .Margin = New Padding(4, 0, 4, 0),
            .TabStop = False
        }

        button.FlatAppearance.BorderSize = 1
        button.FlatAppearance.BorderColor = accent
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 249, 255)
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 242, 254)

        Return button
    End Function

    ''' <summary>实心主按钮（blue-600 底 + 白字 + 无边框），对应参考面板的主操作按钮。</summary>
    Public Function CreatePrimaryButton(text As String, Optional width As Integer = 140,
                                        Optional accent As Color = Nothing) As Button

        Dim back As Color = If(accent.IsEmpty, PrimaryButton, accent)

        Dim button As New Button With {
            .Text = text,
            .Width = width,
            .Height = 32,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = back,
            .ForeColor = TextOnAccent,
            .Font = FontUi,
            .Cursor = Cursors.Hand,
            .Margin = New Padding(4, 0, 4, 0),
            .TabStop = False,
            .UseVisualStyleBackColor = False
        }

        button.FlatAppearance.BorderSize = 0
        button.FlatAppearance.MouseOverBackColor = PrimaryButtonHover
        button.FlatAppearance.MouseDownBackColor = PrimaryButtonHover

        Return button
    End Function

    ''' <summary>参考面板里的次级按钮：Flat、8pt、无边框。</summary>
    Public Function CreateSmallButton(text As String, Optional width As Integer = 62) As Button
        Dim button As New Button With {
            .Text = text,
            .Width = width,
            .Height = 24,
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Microsoft YaHei UI", 8.0F, FontStyle.Regular),
            .Cursor = Cursors.Hand,
            .Margin = New Padding(4, 0, 4, 0),
            .TabStop = False
        }

        button.FlatAppearance.BorderSize = 1
        button.FlatAppearance.BorderColor = BorderColor
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(241, 245, 249)

        Return button
    End Function

    Public Function CreateLabel(text As String, Optional color As Color = Nothing, Optional font As Font = Nothing) As Label
        Return New Label With {
            .Text = text,
            .AutoSize = True,
            .ForeColor = If(color.IsEmpty, TextPrimary, color),
            .Font = If(font, FontUi),
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 6, 0, 0)
        }
    End Function

    ''' <summary>分区卡片：白底 + slate-800 的 10pt 粗体标题（与参考面板的分组标题一致）。</summary>
    Public Function CreateCard(title As String) As Panel
        Dim card As New Panel With {
            .BackColor = Color.White,
            .Padding = New Padding(12, 10, 12, 10),
            .Height = 100
        }

        Dim header As New Label With {
            .Text = title,
            .Dock = DockStyle.Top,
            .Height = 22,
            .ForeColor = TextPrimary,
            .Font = FontTitle,
            .BackColor = Color.Transparent
        }

        card.Controls.Add(header)

        Return card
    End Function

End Module
