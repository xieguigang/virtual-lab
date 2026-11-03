Imports System.Drawing
Imports System.Numerics
Imports MicroduckLab.Core
Imports Dx = Microsoft.VisualBasic.Drawing.DirectX.Scene3D

Namespace Render

    ''' <summary>
    ''' 把三角形汤（每 9 个数一个三角形）变成渲染管线要的顶点列表。
    ''' </summary>
    ''' <remarks>
    ''' 原来这里吃的是物理引擎的 <c>ConvexMesh3D</c> 多边形面，需要自己扇形化。
    ''' 现在碰撞与渲染都不再经过引擎的网格类型：小球直接用
    ''' <c>MuJoCo.Geometry.StlReader.SphereTriangles</c> 生成三角形，
    ''' 这个模块只负责补法向与颜色。
    ''' </remarks>
    Public Module SceneMeshBuilder

        Public Function FromTriangles(vertices As Double(), color As Color) As Dx.SceneMeshVertex()
            Dim triangles As New List(Of Dx.SceneMeshVertex)()

            If vertices Is Nothing OrElse vertices.Length < 9 Then
                Return New Dx.SceneMeshVertex() {}
            End If

            For i As Integer = 0 To vertices.Length \ 9 - 1
                Dim offset As Integer = i * 9
                Dim a As New Vector3(CSng(vertices(offset + 0)), CSng(vertices(offset + 1)), CSng(vertices(offset + 2)))
                Dim b As New Vector3(CSng(vertices(offset + 3)), CSng(vertices(offset + 4)), CSng(vertices(offset + 5)))
                Dim c As New Vector3(CSng(vertices(offset + 6)), CSng(vertices(offset + 7)), CSng(vertices(offset + 8)))
                Dim normal As Vector3 = Vector3.Cross(b - a, c - a)

                If normal.LengthSquared() <= 0 Then
                    Continue For
                End If

                normal = Vector3.Normalize(normal)

                Call triangles.Add(New Dx.SceneMeshVertex(a, normal, color))
                Call triangles.Add(New Dx.SceneMeshVertex(b, normal, color))
                Call triangles.Add(New Dx.SceneMeshVertex(c, normal, color))
            Next

            Return triangles.ToArray()
        End Function

    End Module
End Namespace
