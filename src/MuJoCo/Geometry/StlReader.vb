Imports System.Globalization
Imports System.IO
Imports System.Numerics
Imports System.Text

Namespace Geometry

    ''' <summary>
    ''' 纯托管的 STL 读取器。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 刻意不依赖 <c>Microsoft.VisualBasic.Imaging</c>：仿真库不应该把 GDI 与绘图库
    ''' 拖进运行期。STL 的两种格式（二进制与 ASCII）都很简单，自己读更可控，
    ''' 也方便在测试里构造内存中的三角形汤。
    ''' </para>
    ''' <para>
    ''' 输出是"三角形汤"：每 9 个数是一个三角形的三个顶点，不做索引化、
    ''' 不做焊接。需要凸包时由 <see cref="ConvexHull3D"/> 处理重复点。
    ''' </para>
    ''' </remarks>
    Public Module StlReader

        ''' <summary>读一个 STL 文件，返回三角形汤（每 9 个数一个三角形）</summary>
        Public Function Read(path As String) As Double()
            If Not File.Exists(path) Then
                Throw New MjException($"the mesh file '{path}' does not exist")
            End If

            Dim bytes As Byte() = File.ReadAllBytes(path)

            If bytes.Length >= 84 Then
                Dim count As Integer = BitConverter.ToInt32(bytes, 80)
                Dim expected As Long = 84L + 50L * count

                ' 二进制格式的长度是精确的；ASCII 的 "solid" 头也可能被误判，
                ' 所以长度对得上才按二进制读
                If expected = bytes.Length AndAlso count > 0 Then
                    Return ReadBinary(bytes, count)
                End If
            End If

            Return ReadAscii(Encoding.ASCII.GetString(bytes))
        End Function

        ''' <summary>读内存中的字节（用于测试与内嵌资源）</summary>
        Public Function ReadBytes(bytes As Byte()) As Double()
            If bytes Is Nothing OrElse bytes.Length < 15 Then
                Return New Double() {}
            End If

            If bytes.Length >= 84 Then
                Dim count As Integer = BitConverter.ToInt32(bytes, 80)

                If 84L + 50L * count = bytes.Length AndAlso count > 0 Then
                    Return ReadBinary(bytes, count)
                End If
            End If

            Return ReadAscii(Encoding.ASCII.GetString(bytes))
        End Function

        Private Function ReadBinary(bytes As Byte(), count As Integer) As Double()
            Dim result(count * 9 - 1) As Double
            Dim offset As Integer = 84

            For t As Integer = 0 To count - 1
                ' 跳过 12 字节的法向（几何上由顶点重新计算，读进来也不用）
                offset += 12

                For v As Integer = 0 To 2
                    For c As Integer = 0 To 2
                        result(t * 9 + v * 3 + c) = BitConverter.ToSingle(bytes, offset)
                        offset += 4
                    Next
                Next

                ' 2 字节的属性计数
                offset += 2
            Next

            Return result
        End Function

        Private Function ReadAscii(text As String) As Double()
            Dim items As New List(Of Double)()
            Dim index As Integer = 0
            Dim length As Integer = text.Length

            While index < length
                ' 找到下一个 "vertex" 关键字
                Dim at As Integer = text.IndexOf("vertex", index, StringComparison.OrdinalIgnoreCase)

                If at < 0 Then
                    Exit While
                End If

                index = at + 6

                For c As Integer = 0 To 2
                    Call SkipSpace(text, index)
                    Dim start As Integer = index

                    While index < length AndAlso Not IsSpace(text(index))
                        index += 1
                    End While

                    Dim value As Double = 0.0
                    Dim token As String = text.Substring(start, index - start)

                    If Not Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then
                        value = 0.0
                    End If

                    Call items.Add(value)
                Next
            End While

            ' 丢掉不完整的尾巴（文件被截断时会出现）
            Dim triangles As Integer = items.Count \ 9
            Dim result(Math.Max(triangles, 1) * 9 - 1) As Double

            For i As Integer = 0 To triangles * 9 - 1
                result(i) = items(i)
            Next

            Return result
        End Function

        Private Sub SkipSpace(text As String, ByRef index As Integer)
            While index < text.Length AndAlso IsSpace(text(index))
                index += 1
            End While
        End Sub

        Private Function IsSpace(c As Char) As Boolean
            Return c = " "c OrElse c = ChrW(9) OrElse c = ChrW(10) OrElse c = ChrW(13)
        End Function

        ''' <summary>
        ''' 生成一个球体的三角形汤，供渲染或凸包使用。
        ''' </summary>
        Public Function SphereTriangles(radius As Double,
                                        Optional segmentU As Integer = 16,
                                        Optional segmentV As Integer = 10) As Double()
            Dim u As Integer = Math.Max(3, segmentU)
            Dim v As Integer = Math.Max(2, segmentV)
            Dim items As New List(Of Double)(u * v * 9)

            For iv As Integer = 0 To v - 1
                Dim phi0 As Double = Math.PI * iv / v
                Dim phi1 As Double = Math.PI * (iv + 1) / v

                For iu As Integer = 0 To u - 1
                    Dim theta0 As Double = 2.0 * Math.PI * iu / u
                    Dim theta1 As Double = 2.0 * Math.PI * (iu + 1) / u

                    Dim a As Vector3 = PointOnSphere(radius, phi0, theta0)
                    Dim b As Vector3 = PointOnSphere(radius, phi1, theta0)
                    Dim c As Vector3 = PointOnSphere(radius, phi1, theta1)
                    Dim d As Vector3 = PointOnSphere(radius, phi0, theta1)

                    Call Push(items, a, b, c)

                    If iv > 0 AndAlso iv < v - 1 Then
                        Call Push(items, a, c, d)
                    End If
                Next
            Next

            Return items.ToArray()
        End Function

        Private Function PointOnSphere(radius As Double, phi As Double, theta As Double) As Vector3
            Dim s As Double = Math.Sin(phi)

            Return New Vector3(CSng(radius * s * Math.Cos(theta)),
                               CSng(radius * Math.Cos(phi)),
                               CSng(radius * s * Math.Sin(theta)))
        End Function

        Private Sub Push(items As List(Of Double), a As Vector3, b As Vector3, c As Vector3)
            Call items.Add(a.X) : Call items.Add(a.Y) : Call items.Add(a.Z)
            Call items.Add(b.X) : Call items.Add(b.Y) : Call items.Add(b.Z)
            Call items.Add(c.X) : Call items.Add(c.Y) : Call items.Add(c.Z)
        End Sub

        ''' <summary>把三角形汤转成点列表（不做去重）</summary>
        Public Function PointsOf(triangles As Double()) As Vector3()
            If triangles Is Nothing OrElse triangles.Length < 9 Then
                Return New Vector3() {}
            End If

            Dim count As Integer = triangles.Length \ 3
            Dim points(count - 1) As Vector3

            For i As Integer = 0 To count - 1
                points(i) = New Vector3(CSng(triangles(i * 3)), CSng(triangles(i * 3 + 1)), CSng(triangles(i * 3 + 2)))
            Next

            Return points
        End Function

    End Module

End Namespace
