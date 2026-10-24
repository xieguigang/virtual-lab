Imports Microsoft.VisualBasic.Imaging.Drawing3D

Namespace Model

    ''' <summary>
    ''' 一个三角面（连杆局部坐标），带材质颜色。
    ''' </summary>
    ''' <remarks>
    ''' 顶点用 <see cref="Point3D"/>（结构体），避免每帧变换时产生托管分配。
    ''' </remarks>
    Public Structure MeshTriangle

        Public A As Point3D
        Public B As Point3D
        Public C As Point3D

        ''' <summary>材质颜色（hex，例如 "#B8BDC7"）。</summary>
        Public Color As String

        Sub New(a As Point3D, b As Point3D, c As Point3D, color As String)
            Me.A = a
            Me.B = b
            Me.C = c
            Me.Color = color
        End Sub

        ''' <summary>三个顶点的平均位置（用于快速求包围盒中心）。</summary>
        Public ReadOnly Property Center As Point3D
            Get
                Return New Point3D((A.X + B.X + C.X) / 3.0,
                                    (A.Y + B.Y + C.Y) / 3.0,
                                    (A.Z + B.Z + C.Z) / 3.0)
            End Get
        End Property
    End Structure

    ''' <summary>
    ''' 一个连杆的视觉网格：一组局部坐标三角面 + 包围盒。
    ''' </summary>
    ''' <remarks>
    ''' 局部坐标与 URDF 的 link 局部系一致（Z-up、单位米），因此渲染时只需把
    ''' 刚体的位姿（Position + Orientation）作用到这些顶点上。
    ''' </remarks>
    Public Class LinkMesh

        ''' <summary>对应的 URDF link 名称。</summary>
        Public Property LinkName As String

        ''' <summary>在 glTF 节点树中的路径（调试用）。</summary>
        Public Property NodePath As String

        Public Property Triangles As New List(Of MeshTriangle)()

        ''' <summary>局部包围盒最小角。</summary>
        Public Property BoundsMin As Point3D

        ''' <summary>局部包围盒最大角。</summary>
        Public Property BoundsMax As Point3D

        Public ReadOnly Property TriangleCount As Integer
            Get
                Return Triangles.Count
            End Get
        End Property

        Public Sub Add(a As Point3D, b As Point3D, c As Point3D, color As String)
            Triangles.Add(New MeshTriangle(a, b, c, color))
        End Sub

        ''' <summary>重新计算局部包围盒。</summary>
        Public Sub RecalculateBounds()
            If Triangles.Count = 0 Then
                BoundsMin = New Point3D(0, 0, 0)
                BoundsMax = New Point3D(0, 0, 0)
                Return
            End If

            Dim minX As Double = Double.MaxValue
            Dim minY As Double = Double.MaxValue
            Dim minZ As Double = Double.MaxValue
            Dim maxX As Double = Double.MinValue
            Dim maxY As Double = Double.MinValue
            Dim maxZ As Double = Double.MinValue

            For Each triangle As MeshTriangle In Triangles
                Accumulate(triangle.A, minX, minY, minZ, maxX, maxY, maxZ)
                Accumulate(triangle.B, minX, minY, minZ, maxX, maxY, maxZ)
                Accumulate(triangle.C, minX, minY, minZ, maxX, maxY, maxZ)
            Next

            BoundsMin = New Point3D(minX, minY, minZ)
            BoundsMax = New Point3D(maxX, maxY, maxZ)
        End Sub

        Private Shared Sub Accumulate(p As Point3D,
                                      ByRef minX As Double, ByRef minY As Double, ByRef minZ As Double,
                                      ByRef maxX As Double, ByRef maxY As Double, ByRef maxZ As Double)
            If p.X < minX Then minX = p.X
            If p.Y < minY Then minY = p.Y
            If p.Z < minZ Then minZ = p.Z
            If p.X > maxX Then maxX = p.X
            If p.Y > maxY Then maxY = p.Y
            If p.Z > maxZ Then maxZ = p.Z
        End Sub

        Public Overrides Function ToString() As String
            Return $"{LinkName} ({Triangles.Count} triangles)"
        End Function
    End Class

End Namespace
