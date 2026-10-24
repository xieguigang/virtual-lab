Imports Microsoft.VisualBasic.Engine.Physics

Namespace Sensing

    ''' <summary>
    ''' 夹爪相机的配置：分辨率、视场角、帧率、安装偏移、开关。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 视场角按**垂直方向**定义，水平视场由宽高比自动换算，保证画面不拉伸。
    ''' 内参由此推出：<c>fx = fy = (Height / 2) / tan(fov / 2)</c>、<c>cx = Width / 2</c>、<c>cy = Height / 2</c>。
    ''' </para>
    ''' <para>
    ''' 安装偏移是 **link6 局部坐标**：link6 的 +X 指向指尖方向（也是相机光轴），
    ''' +Z 是相机画面里的"上"。零位时相机朝世界 +X 平视；末端转成朝下（ry=90°）时相机俯视桌面。
    ''' </para>
    ''' </remarks>
    Public Class CameraConfig

        ''' <summary>是否启用相机（关闭后不产生任何渲染开销）。</summary>
        Public Property Enabled As Boolean = True

        Public Property Width As Integer = 320
        Public Property Height As Integer = 240

        ''' <summary>垂直视场角（度）。</summary>
        Public Property FieldOfView As Double = 60.0

        ''' <summary>自动捕获帧率（Hz）；0 表示只在显式请求时渲染。</summary>
        Public Property Rate As Double = 15.0

        ''' <summary>相机光心在 link6 局部系中的位置。</summary>
        Public Property Offset As New Vector3(0.045, 0.0, 0.0)

        ''' <summary>
        ''' 环境光强度（0~1），其余部分由单方向光提供。
        ''' </summary>
        ''' <remarks>
        ''' 浅色背景下手指的深色材质（Rubber）容易糊成纯黑，取 0.45 保留一点层次。
        ''' </remarks>
        Public Property Ambient As Double = 0.45

        ''' <summary>超过这个距离的三角形直接丢弃（米）。</summary>
        Public Property MaxDistance As Double = 6.0

        ''' <summary>
        ''' 背景颜色（0xRRGGBB），与三维视口保持一致（slate-50）。
        ''' </summary>
        ''' <remarks>
        ''' 注意 VB 里 <c>&amp;HB0F14</c> 就是 0x0B0F14，写十六进制字面量时别漏了前导位。
        ''' </remarks>
        Public Property BackgroundColor As Integer = &HF8FAFC

        Public ReadOnly Property Pixels As Integer
            Get
                Return System.Math.Max(1, Width) * System.Math.Max(1, Height)
            End Get
        End Property

        Public ReadOnly Property Aspect As Double
            Get
                Return CSng(Width) / CSng(System.Math.Max(1, Height))
            End Get
        End Property

        ''' <summary>像素焦距（fx = fy）。</summary>
        Public ReadOnly Property FocalPixels As Double
            Get
                Dim half As Double = System.Math.Max(1.0, FieldOfView) * System.Math.PI / 360.0
                Dim tangent As Double = System.Math.Tan(half)

                If tangent < 1.0E-6 Then tangent = 1.0E-6

                Return (Height / 2.0) / tangent
            End Get
        End Property

        ''' <summary>水平视场角（度），由垂直视场与宽高比推得。</summary>
        Public ReadOnly Property HorizontalFieldOfView As Double
            Get
                Return 2.0 * System.Math.Atan(Aspect * System.Math.Tan(FieldOfView * System.Math.PI / 360.0)) * 180.0 / System.Math.PI
            End Get
        End Property

        ''' <summary>把参数夹到安全范围。</summary>
        Public Sub Clamp()
            Width = System.Math.Max(32, System.Math.Min(1280, Width))
            Height = System.Math.Max(32, System.Math.Min(1280, Height))
            FieldOfView = System.Math.Max(10.0, System.Math.Min(150.0, FieldOfView))
            Rate = System.Math.Max(0.0, System.Math.Min(120.0, Rate))
            Ambient = System.Math.Max(0.0, System.Math.Min(0.9, Ambient))
            MaxDistance = System.Math.Max(0.2, System.Math.Min(50.0, MaxDistance))
        End Sub

        Public Function Clone() As CameraConfig
            Return New CameraConfig With {
                .Enabled = Enabled,
                .Width = Width,
                .Height = Height,
                .FieldOfView = FieldOfView,
                .Rate = Rate,
                .Offset = New Vector3(Offset.x, Offset.y, Offset.z),
                .Ambient = Ambient,
                .MaxDistance = MaxDistance,
                .BackgroundColor = BackgroundColor
            }
        End Function

        Public Overrides Function ToString() As String
            Return $"{Width}x{Height} · fov {FieldOfView:F0}° · {Rate:F0} Hz"
        End Function

    End Class

End Namespace
