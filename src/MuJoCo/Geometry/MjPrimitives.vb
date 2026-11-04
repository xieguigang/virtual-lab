Imports num = System.Numerics

Namespace Geometry

    ''' <summary>纯托管的凸网格数据：顶点与三角面索引。</summary>
    Public Class MjPrimitive

        Public Property Vertices As num.Vector3()
        Public Property Faces As Integer()()

        Public ReadOnly Property VertexCount As Integer
            Get
                Return If(Vertices Is Nothing, 0, Vertices.Length)
            End Get
        End Property

        Public ReadOnly Property FaceCount As Integer
            Get
                Return If(Faces Is Nothing, 0, Faces.Length)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 程序化凸体图元。全部使用 <see cref="System.Numerics"/>，
    ''' 不引用物理内核，供场景装配与渲染网格共用。
    ''' </summary>
    Public Module MjPrimitives

        Public Function Box(width As Double, height As Double, depth As Double) As MjPrimitive
            Dim x As Single = CSng(width * 0.5)
            Dim y As Single = CSng(height * 0.5)
            Dim z As Single = CSng(depth * 0.5)

            Dim v As num.Vector3() = {
                New num.Vector3(-x, -y, -z), New num.Vector3(x, -y, -z),
                New num.Vector3(x, y, -z), New num.Vector3(-x, y, -z),
                New num.Vector3(-x, -y, z), New num.Vector3(x, -y, z),
                New num.Vector3(x, y, z), New num.Vector3(-x, y, z)
            }

            Dim f As Integer()() = {
                New Integer() {0, 2, 1}, New Integer() {0, 3, 2},
                New Integer() {4, 5, 6}, New Integer() {4, 6, 7},
                New Integer() {0, 1, 5}, New Integer() {0, 5, 4},
                New Integer() {3, 7, 6}, New Integer() {3, 6, 2},
                New Integer() {0, 4, 7}, New Integer() {0, 7, 3},
                New Integer() {1, 2, 6}, New Integer() {1, 6, 5}
            }

            Return New MjPrimitive With {.Vertices = v, .Faces = f}
        End Function

        Public Function Wedge(width As Double, height As Double, depth As Double) As MjPrimitive
            Dim x As Single = CSng(width * 0.5)
            Dim y As Single = CSng(height * 0.5)
            Dim z As Single = CSng(depth * 0.5)

            Dim v As num.Vector3() = {
                New num.Vector3(-x, -y, -z), New num.Vector3(x, -y, -z),
                New num.Vector3(-x, y, -z), New num.Vector3(-x, -y, z),
                New num.Vector3(x, -y, z), New num.Vector3(-x, y, z)
            }

            Dim f As Integer()() = {
                New Integer() {0, 2, 1}, New Integer() {3, 4, 5},
                New Integer() {0, 1, 4}, New Integer() {0, 4, 3},
                New Integer() {0, 3, 5}, New Integer() {0, 5, 2},
                New Integer() {1, 2, 5}, New Integer() {1, 5, 4}
            }

            Return New MjPrimitive With {.Vertices = v, .Faces = f}
        End Function

        ''' <summary>绕 Z 轴旋转一条 (半径, 高度) 轮廓，生成闭合的旋转体网格。</summary>
        Public Function Revolve(profile As num.Vector2(), segment As Integer) As MjPrimitive
            If profile Is Nothing OrElse profile.Length < 2 Then Throw New ArgumentException("profile needs at least two points", NameOf(profile))
            If segment < 3 Then segment = 3

            Dim rings As Integer = profile.Length
            Dim vertices As New List(Of num.Vector3)()
            Dim faces As New List(Of Integer())()

            For i As Integer = 0 To segment - 1
                Dim theta As Double = 2.0 * Math.PI * i / segment
                Dim c As Single = CSng(Math.Cos(theta))
                Dim s As Single = CSng(Math.Sin(theta))

                For j As Integer = 0 To rings - 1
                    vertices.Add(New num.Vector3(profile(j).X * c, profile(j).X * s, profile(j).Y))
                Next
            Next

            For i As Integer = 0 To segment - 1
                Dim ni As Integer = (i + 1) Mod segment

                For j As Integer = 0 To rings - 2
                    Dim a As Integer = i * rings + j
                    Dim b As Integer = ni * rings + j
                    Dim cc As Integer = ni * rings + j + 1
                    Dim d As Integer = i * rings + j + 1

                    Call faces.Add(New Integer() {a, b, cc})
                    Call faces.Add(New Integer() {a, cc, d})
                Next
            Next

            Return New MjPrimitive With {
                .Vertices = vertices.ToArray(),
                .Faces = faces.ToArray()
            }
        End Function

        Public Function Cylinder(radius As Double, height As Double,
                                 Optional segment As Integer = 16) As MjPrimitive
            Dim r As Single = CSng(radius)
            Dim h As Single = CSng(height * 0.5)

            Return Revolve(New num.Vector2() {
                New num.Vector2(r, -h), New num.Vector2(r, h)
            }, segment)
        End Function

        Public Function Cone(radius As Double, height As Double,
                             Optional segment As Integer = 16) As MjPrimitive
            Dim r As Single = CSng(radius)
            Dim h As Single = CSng(height * 0.5)

            Return Revolve(New num.Vector2() {
                New num.Vector2(r, -h), New num.Vector2(0.0F, h)
            }, segment)
        End Function

        Public Function Disc(radius As Double, thickness As Double,
                             Optional segment As Integer = 20) As MjPrimitive
            Dim r As Single = CSng(radius)
            Dim h As Single = CSng(thickness * 0.5)

            Return Revolve(New num.Vector2() {
                New num.Vector2(r, -h), New num.Vector2(r, h)
            }, segment)
        End Function

        Public Function Frustum(bottomRadius As Double, topRadius As Double, height As Double,
                                Optional segment As Integer = 16) As MjPrimitive
            Dim h As Single = CSng(height * 0.5)

            Return Revolve(New num.Vector2() {
                New num.Vector2(CSng(bottomRadius), -h),
                New num.Vector2(CSng(topRadius), h)
            }, segment)
        End Function

        Public Function Prism(sides As Integer, radius As Double, height As Double) As MjPrimitive
            Return Cylinder(radius, height, Math.Max(3, sides))
        End Function

        Public Function Pyramid(sides As Integer, radius As Double, height As Double) As MjPrimitive
            Return Cone(radius, height, Math.Max(3, sides))
        End Function

        Public Function Hemisphere(radius As Double, Optional segment As Integer = 16,
                                   Optional rings As Integer = 6) As MjPrimitive
            If rings < 2 Then rings = 2

            Dim profile(rings) As num.Vector2

            For j As Integer = 0 To rings
                Dim phi As Double = 0.5 * Math.PI * j / rings

                profile(j) = New num.Vector2(CSng(radius * Math.Cos(phi)),
                                             CSng(radius * Math.Sin(phi)))
            Next

            Return Revolve(profile, Math.Max(3, segment))
        End Function

        Public Function Sphere(radius As Double,
                               Optional segmentU As Integer = 12,
                               Optional segmentV As Integer = 8) As MjPrimitive
            If segmentU < 3 Then segmentU = 3
            If segmentV < 2 Then segmentV = 2

            Dim vertices As New List(Of num.Vector3)()
            Dim faces As New List(Of Integer())()
            Dim r As Single = CSng(radius)

            For j As Integer = 0 To segmentV
                Dim phi As Double = Math.PI * j / segmentV
                Dim y As Single = CSng(r * Math.Cos(phi))
                Dim rr As Single = CSng(r * Math.Sin(phi))

                For i As Integer = 0 To segmentU - 1
                    Dim theta As Double = 2.0 * Math.PI * i / segmentU

                    vertices.Add(New num.Vector3(CSng(rr * Math.Cos(theta)), y,
                                                 CSng(rr * Math.Sin(theta))))
                Next
            Next

            For j As Integer = 0 To segmentV - 1
                For i As Integer = 0 To segmentU - 1
                    Dim ni As Integer = (i + 1) Mod segmentU
                    Dim a As Integer = j * segmentU + i
                    Dim b As Integer = j * segmentU + ni
                    Dim c As Integer = (j + 1) * segmentU + ni
                    Dim d As Integer = (j + 1) * segmentU + i

                    Call faces.Add(New Integer() {a, b, c})
                    Call faces.Add(New Integer() {a, c, d})
                Next
            Next

            Return New MjPrimitive With {
                .Vertices = vertices.ToArray(),
                .Faces = faces.ToArray()
            }
        End Function

    End Module

End Namespace
