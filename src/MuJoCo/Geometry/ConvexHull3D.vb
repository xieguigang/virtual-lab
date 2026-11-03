Imports System.Numerics

Namespace Geometry

    ''' <summary>凸包的计算结果</summary>
    Public Class ConvexHullResult

        ''' <summary>凸包的顶点（世界/局部坐标，未平移到质心）</summary>
        Public Property Vertices As Vector3()
        ''' <summary>三角形索引（每三个一组，外向逆时针）</summary>
        Public Property Faces As Integer()()

        Public ReadOnly Property TriangleCount As Integer
            Get
                Return If(Faces Is Nothing, 0, Faces.Length)
            End Get
        End Property

        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return Vertices Is Nothing OrElse Vertices.Length < 4 OrElse Faces Is Nothing OrElse Faces.Length < 4
            End Get
        End Property

    End Class

    ''' <summary>
    ''' 自包含的凸包算法（增量式 QuickHull）。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 为什么不用 <c>Microsoft.VisualBasic.Imaging.Drawing3D</c> 里现成的
    ''' <c>ConvexHull</c>：那个类型属于绘图库，仿真库只应该依赖
    ''' <c>System.Numerics</c> 与计算内核。这里的实现约 200 行，够用且没有外部依赖。
    ''' </para>
    ''' <para>
    ''' 算法分两步：先找一个非退化的初始四面体，再逐点插入——
    ''' 每个点找出它"能看见"的所有面，删掉这些面，用它们的视界边与该点重新缝合。
    ''' 退化输入（全部共线或共面）返回 <c>Nothing</c>，由调用方决定回退策略。
    ''' </para>
    ''' </remarks>
    Public Module ConvexHull3D

        Private Class Face
            Public A As Integer
            Public B As Integer
            Public C As Integer
            Public Normal As Vector3
            Public Offset As Double
            Public Alive As Boolean = True
        End Class

        ''' <summary>
        ''' 计算点集的凸包；点数不足或退化时返回 <c>Nothing</c>。
        ''' </summary>
        Public Function Compute(points As Vector3(), Optional tolerance As Double = 1.0E-9) As ConvexHullResult
            Dim unique As List(Of Vector3) = Weld(points, tolerance)

            If unique.Count < 4 Then
                Return Nothing
            End If

            Dim count As Integer = unique.Count
            Dim scale As Double = Extent(unique)

            If scale < 1.0E-12 Then
                Return Nothing
            End If

            Dim eps As Double = Math.Max(tolerance, scale * 1.0E-7)

            ' ---- 1. 初始四面体 ----
            Dim i0 As Integer = 0
            Dim i1 As Integer = -1
            Dim best As Double = -1.0

            For i As Integer = 1 To count - 1
                Dim d As Double = Vector3.Distance(unique(0), unique(i))

                If d > best Then
                    best = d
                    i1 = i
                End If
            Next

            If i1 < 0 OrElse best < eps Then
                Return Nothing
            End If

            Dim p0 As Vector3 = unique(i0)
            Dim p1 As Vector3 = unique(i1)
            Dim dir As Vector3 = Vector3.Normalize(p1 - p0)
            Dim i2 As Integer = -1
            best = -1.0

            For i As Integer = 0 To count - 1
                Dim d As Double = Vector3.Cross(p1 - p0, unique(i) - p0).Length()

                If d > best Then
                    best = d
                    i2 = i
                End If
            Next

            If i2 < 0 OrElse best < eps * eps Then
                Return Nothing
            End If

            Dim normal As Vector3 = Vector3.Normalize(Vector3.Cross(p1 - p0, unique(i2) - p0))
            Dim i3 As Integer = -1
            best = -1.0

            For i As Integer = 0 To count - 1
                Dim d As Double = Math.Abs(Vector3.Dot(normal, unique(i) - p0))

                If d > best Then
                    best = d
                    i3 = i
                End If
            Next

            If i3 < 0 OrElse best < eps Then
                ' 全部共面：没有体积，交给调用方回退
                Return Nothing
            End If

            Dim interior As Vector3 = (unique(i0) + unique(i1) + unique(i2) + unique(i3)) * 0.25F
            Dim faces As New List(Of Face)()

            Call AddOriented(faces, unique, i0, i1, i2, interior)
            Call AddOriented(faces, unique, i0, i1, i3, interior)
            Call AddOriented(faces, unique, i0, i2, i3, interior)
            Call AddOriented(faces, unique, i1, i2, i3, interior)

            If faces.Count < 4 Then
                Return Nothing
            End If

            ' ---- 2. 逐点插入 ----
            Dim seed As New HashSet(Of Integer) From {i0, i1, i2, i3}

            For i As Integer = 0 To count - 1
                If seed.Contains(i) Then
                    Continue For
                End If

                Call Insert(faces, unique, i, eps)
            Next

            Return Consolidate(faces, unique)
        End Function

        ' /********************************************************************************/
        '  插入一个点
        ' /********************************************************************************/

        Private Sub Insert(faces As List(Of Face), points As List(Of Vector3), index As Integer, eps As Double)
            Dim p As Vector3 = points(index)
            Dim visible As Boolean() = New Boolean(faces.Count - 1) {}
            Dim any As Boolean = False

            For f As Integer = 0 To faces.Count - 1
                Dim face As Face = faces(f)

                If Not face.Alive Then
                    Continue For
                End If

                If Vector3.Dot(face.Normal, p) - face.Offset > eps Then
                    visible(f) = True
                    any = True
                End If
            Next

            If Not any Then
                Return
            End If

            ' 有向边 → 所属的面，用来找视界边
            Dim owner As New Dictionary(Of Long, Integer)(faces.Count * 3)

            For f As Integer = 0 To faces.Count - 1
                Dim face As Face = faces(f)

                If Not face.Alive Then
                    Continue For
                End If

                Dim n As Long = points.Count
                owner(face.A * n + face.B) = f
                owner(face.B * n + face.C) = f
                owner(face.C * n + face.A) = f
            Next

            Dim horizon As New List(Of (A As Integer, B As Integer))()

            For f As Integer = 0 To faces.Count - 1
                If Not visible(f) Then
                    Continue For
                End If

                Dim face As Face = faces(f)
                Dim n As Long = points.Count

                Call ConsiderEdge(owner, visible, horizon, face.A, face.B, n)
                Call ConsiderEdge(owner, visible, horizon, face.B, face.C, n)
                Call ConsiderEdge(owner, visible, horizon, face.C, face.A, n)
            Next

            If horizon.Count = 0 Then
                Return
            End If

            For f As Integer = 0 To faces.Count - 1
                If visible(f) Then
                    faces(f).Alive = False
                End If
            Next

            For Each edge In horizon
                Dim face As New Face With {
                    .A = edge.A,
                    .B = edge.B,
                    .C = index
                }

                Call Finish(face, points)
                Call faces.Add(face)
            Next

            ' 已删除的面会一直留在表里，点一多就会把内存吃光；定期压紧一次。
            ' 代价是每若干个点做一次 O(F) 的拷贝，相对每点 O(F) 的可见性扫描可以忽略。
            If faces.Count > 64 Then
                Dim alive As Integer = 0

                For f As Integer = 0 To faces.Count - 1
                    If faces(f).Alive Then
                        alive += 1
                    End If
                Next

                If alive * 2 < faces.Count Then
                    Dim kept As New List(Of Face)(alive)

                    For f As Integer = 0 To faces.Count - 1
                        If faces(f).Alive Then
                            Call kept.Add(faces(f))
                        End If
                    Next

                    faces.Clear()
                    Call faces.AddRange(kept)
                End If
            End If
        End Sub

        Private Sub ConsiderEdge(owner As Dictionary(Of Long, Integer), visible As Boolean(),
                                 horizon As List(Of (A As Integer, B As Integer)),
                                 a As Integer, b As Integer, n As Long)
            Dim neighbour As Integer = -1

            ' 相邻面必须以反向边 (b, a) 出现；找不到视为边界，同样纳入视界
            If owner.TryGetValue(b * n + a, neighbour) AndAlso Not visible(neighbour) Then
                Call horizon.Add((a, b))

                Return
            End If

            If Not owner.ContainsKey(b * n + a) Then
                Call horizon.Add((a, b))
            End If
        End Sub

        ' /********************************************************************************/
        '  辅助
        ' /********************************************************************************/

        Private Sub AddOriented(faces As List(Of Face), points As List(Of Vector3),
                                a As Integer, b As Integer, c As Integer, interior As Vector3)
            Dim face As New Face With {.A = a, .B = b, .C = c}

            Call Finish(face, points)

            ' 面的法向必须背离内部点；顺序反了就交换两个顶点
            If Vector3.Dot(face.Normal, interior) - face.Offset > 0 Then
                Dim t As Integer = face.B
                face.B = face.C
                face.C = t
                Call Finish(face, points)
            End If

            Call faces.Add(face)
        End Sub

        Private Sub Finish(face As Face, points As List(Of Vector3))
            Dim a As Vector3 = points(face.A)
            Dim n As Vector3 = Vector3.Cross(points(face.B) - a, points(face.C) - a)
            Dim len As Double = n.Length()

            If len < 1.0E-20 Then
                face.Normal = Vector3.UnitZ
                face.Offset = Vector3.Dot(face.Normal, a)

                Return
            End If

            face.Normal = n / CSng(len)
            face.Offset = Vector3.Dot(face.Normal, a)
        End Sub

        ''' <summary>收集仍然存活的面，压缩顶点索引</summary>
        Private Function Consolidate(faces As List(Of Face), points As List(Of Vector3)) As ConvexHullResult
            Dim remap As New Dictionary(Of Integer, Integer)()
            Dim vertices As New List(Of Vector3)()
            Dim triangles As New List(Of Integer())()

            For Each face As Face In faces
                If Not face.Alive Then
                    Continue For
                End If

                ' 面积为零的面（插入点恰好落在边上）直接丢掉
                Dim a As Vector3 = points(face.A)
                Dim area As Double = Vector3.Cross(points(face.B) - a, points(face.C) - a).Length()

                If area < 1.0E-16 Then
                    Continue For
                End If

                Call triangles.Add({Map(remap, vertices, points, face.A),
                                    Map(remap, vertices, points, face.B),
                                    Map(remap, vertices, points, face.C)})
            Next

            If vertices.Count < 4 OrElse triangles.Count < 4 Then
                Return Nothing
            End If

            Return New ConvexHullResult With {
                .Vertices = vertices.ToArray(),
                .Faces = triangles.ToArray()
            }
        End Function

        Private Function Map(remap As Dictionary(Of Integer, Integer), vertices As List(Of Vector3),
                             points As List(Of Vector3), index As Integer) As Integer
            Dim mapped As Integer

            If remap.TryGetValue(index, mapped) Then
                Return mapped
            End If

            mapped = vertices.Count
            remap(index) = mapped
            Call vertices.Add(points(index))

            Return mapped
        End Function

        ''' <summary>焊接重合点：STL 的三角形汤里每个顶点都重复出现</summary>
        Private Function Weld(points As Vector3(), tolerance As Double) As List(Of Vector3)
            Dim result As New List(Of Vector3)()

            If points Is Nothing Then
                Return result
            End If

            Dim scale As Double = Extent(points)
            Dim cell As Double = Math.Max(scale * 1.0E-6, tolerance)
            Dim seen As New HashSet(Of (X As Long, Y As Long, Z As Long))()

            For Each p In points
                If Single.IsNaN(p.X) OrElse Single.IsNaN(p.Y) OrElse Single.IsNaN(p.Z) Then
                    Continue For
                End If

                Dim key As (X As Long, Y As Long, Z As Long) =
                    (CLng(Math.Round(p.X / cell)), CLng(Math.Round(p.Y / cell)), CLng(Math.Round(p.Z / cell)))

                If seen.Add(key) Then
                    Call result.Add(p)
                End If
            Next

            Return result
        End Function

        Private Function Extent(points As List(Of Vector3)) As Double
            If points Is Nothing OrElse points.Count = 0 Then
                Return 0.0
            End If

            Dim min As Vector3 = points(0)
            Dim max As Vector3 = points(0)

            For Each p In points
                min = Vector3.Min(min, p)
                max = Vector3.Max(max, p)
            Next

            Return (max - min).Length()
        End Function

        Private Function Extent(points As Vector3()) As Double
            If points Is Nothing OrElse points.Length = 0 Then
                Return 0.0
            End If

            Dim min As Vector3 = points(0)
            Dim max As Vector3 = points(0)

            For Each p In points
                min = Vector3.Min(min, p)
                max = Vector3.Max(max, p)
            Next

            Return (max - min).Length()
        End Function

    End Module

End Namespace
