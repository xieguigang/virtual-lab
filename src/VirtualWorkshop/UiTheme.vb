Imports System.Drawing
Imports System.Windows.Forms

''' <summary>深色工业控制台的配色与字体。</summary>
Public Module UiTheme

    Public ReadOnly Background As Color = Color.FromArgb(11, 15, 20)
    Public ReadOnly PanelColor As Color = Color.FromArgb(19, 26, 34)
    Public ReadOnly PanelAlt As Color = Color.FromArgb(27, 36, 48)
    Public ReadOnly BorderColor As Color = Color.FromArgb(38, 52, 66)
    Public ReadOnly Accent As Color = Color.FromArgb(34, 211, 238)
    Public ReadOnly AccentDim As Color = Color.FromArgb(14, 116, 144)
    Public ReadOnly Warn As Color = Color.FromArgb(245, 158, 11)
    Public ReadOnly Danger As Color = Color.FromArgb(239, 68, 68)
    Public ReadOnly Good As Color = Color.FromArgb(34, 197, 94)
    Public ReadOnly TextPrimary As Color = Color.FromArgb(229, 231, 235)
    Public ReadOnly TextMuted As Color = Color.FromArgb(148, 163, 184)

    Public ReadOnly FontUi As New Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular)
    Public ReadOnly FontTitle As New Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold)
    Public ReadOnly FontMono As New Font("Consolas", 9.5F, FontStyle.Regular)
    Public ReadOnly FontMonoSmall As New Font("Consolas", 8.5F, FontStyle.Regular)

    Public Function CreateButton(text As String, accent As Color, Optional width As Integer = 92) As Button
        Dim button As New Button With {
            .Text = text,
            .Width = width,
            .Height = 30,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = PanelAlt,
            .ForeColor = TextPrimary,
            .Font = FontUi,
            .Cursor = Cursors.Hand,
            .Margin = New Padding(4, 0, 4, 0),
            .TabStop = False
        }

        button.FlatAppearance.BorderColor = accent
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 48, 62)
        button.FlatAppearance.MouseDownBackColor = accent

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

    Public Function CreateCard(title As String) As Panel
        Dim card As New Panel With {
            .BackColor = PanelColor,
            .Dock = DockStyle.Top,
            .Padding = New Padding(12, 10, 12, 10),
            .Height = 100
        }

        Dim header As New Label With {
            .Text = title,
            .Dock = DockStyle.Top,
            .Height = 22,
            .ForeColor = Accent,
            .Font = New Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
            .BackColor = Color.Transparent
        }

        card.Controls.Add(header)

        Return card
    End Function

End Module
