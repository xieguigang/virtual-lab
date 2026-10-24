Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices

Namespace Sensing

    ''' <summary>
    ''' 一帧夹爪相机图像：RGB24 + 深度（米）+ 时间戳 + 外参。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' <see cref="Rgb"/> 是 <c>Width*Height*3</c> 的 RGB 顺序字节；<see cref="Depth"/> 是
    ''' <c>Width*Height</c> 的 float32 米，0 表示背景（没有命中任何三角形）。
    ''' </para>
    ''' <para>
    ''' 编码方法都是纯内存操作，不依赖窗口/GPU，因此 headless 下同样可用。
    ''' </para>
    ''' </remarks>
    Public Class CameraFrame

        Public Property Width As Integer
        Public Property Height As Integer

        ''' <summary>RGB24，长度 = Width*Height*3。</summary>
        Public Property Rgb As Byte()

        ''' <summary>深度（米），长度 = Width*Height，0 表示背景。</summary>
        Public Property Depth As Single()

        Public Property Time As Double
        Public Property StepIndex As Long
        Public Property Pose As CameraPose

        Public ReadOnly Property Pixels As Integer
            Get
                Return System.Math.Max(1, Width) * System.Math.Max(1, Height)
            End Get
        End Property

        ' /********************************************************************************/
        '  编码
        ' /********************************************************************************/

        ''' <summary>转成 24bpp 位图（BGR 顺序，逐像素交换 R/B）。</summary>
        Public Function ToBitmap() As Bitmap
            Dim bitmap As New Bitmap(Width, Height, PixelFormat.Format24bppRgb)
            Dim data As BitmapData = bitmap.LockBits(New Rectangle(0, 0, Width, Height),
                                                     ImageLockMode.WriteOnly,
                                                     PixelFormat.Format24bppRgb)

            Try
                Dim stride As Integer = data.Stride
                Dim buffer(stride * Height - 1) As Byte
                Dim source As Byte() = Me.Rgb

                For y As Integer = 0 To Height - 1
                    Dim sourceRow As Integer = y * Width * 3
                    Dim targetRow As Integer = y * stride

                    For x As Integer = 0 To Width - 1
                        Dim offset As Integer = sourceRow + x * 3
                        Dim target As Integer = targetRow + x * 3

                        buffer(target) = source(offset + 2)      ' B
                        buffer(target + 1) = source(offset + 1)  ' G
                        buffer(target + 2) = source(offset)      ' R
                    Next
                Next

                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length)
            Finally
                bitmap.UnlockBits(data)
            End Try

            Return bitmap
        End Function

        ''' <summary>编码成 PNG 字节流。</summary>
        Public Function ToPng() As Byte()
            Using bitmap As Bitmap = ToBitmap()
                Using stream As New MemoryStream()
                    bitmap.Save(stream, ImageFormat.Png)

                    Return stream.ToArray()
                End Using
            End Using
        End Function

        Public Function ToPngBase64() As String
            Return Convert.ToBase64String(ToPng())
        End Function

        ''' <summary>原始 RGB24 的 base64（神经网络可直接 reshape 成张量）。</summary>
        Public Function ToRgbBase64() As String
            Return Convert.ToBase64String(Rgb)
        End Function

        ''' <summary>深度图 float32（小端、米）的 base64，长度 = 宽*高*4。</summary>
        Public Function ToDepthF32Base64() As String
            Dim bytes(Depth.Length * 4 - 1) As Byte

            Buffer.BlockCopy(Depth, 0, bytes, 0, bytes.Length)

            Return Convert.ToBase64String(bytes)
        End Function

        ''' <summary>深度图 u16 毫米的 base64（0 表示背景），长度 = 宽*高*2。</summary>
        Public Function ToDepthU16MmBase64() As String
            Dim bytes(Depth.Length * 2 - 1) As Byte

            For i As Integer = 0 To Depth.Length - 1
                Dim meters As Single = Depth(i)
                Dim millimeters As Integer = 0

                If meters > 0 Then
                    millimeters = CInt(System.Math.Round(meters * 1000.0))
                    millimeters = System.Math.Max(1, System.Math.Min(65535, millimeters))
                End If

                bytes(i * 2) = CByte(millimeters And &HFF)
                bytes(i * 2 + 1) = CByte((millimeters >> 8) And &HFF)
            Next

            Return Convert.ToBase64String(bytes)
        End Function

        Public Overrides Function ToString() As String
            Return $"{Width}x{Height} t={Time:F3}s step={StepIndex}"

        End Function

    End Class

End Namespace
