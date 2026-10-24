Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D

Namespace Model

    ''' <summary>
    ''' 把物理用的凸多面体（<see cref="ConvexMesh3D"/>）扇形三角化成渲染用的
    ''' <see cref="LinkMesh"/>，保证"物理碰撞体"与"看到的模型"是同一份几何。
    ''' </summary>
    Public Module MeshTessellator

        Public Function ToLinkMesh(mesh As ConvexMesh3D, color As String,
                                   Optional name As String = Nothing) As LinkMesh

            Dim out As New LinkMesh With {
                .LinkName = name,
                .OwnerLink = name
            }

            If mesh Is Nothing Then Return out

            Dim vertices As Vector3() = mesh.Vertices
            Dim faces As Integer()() = mesh.Faces

            If vertices Is Nothing OrElse faces Is Nothing Then Return out

            For Each face As Integer() In faces
                If face Is Nothing OrElse face.Length < 3 Then Continue For

                For i As Integer = 1 To face.Length - 2
                    Dim a As Integer = face(0)
                    Dim b As Integer = face(i)
                    Dim c As Integer = face(i + 1)

                    If a >= vertices.Length OrElse b >= vertices.Length OrElse c >= vertices.Length Then Continue For

                    out.Add(ToPoint(vertices(a)), ToPoint(vertices(b)), ToPoint(vertices(c)), color)
                Next
            Next

            Call out.RecalculateBounds()

            Return out
        End Function

        ''' <summary>返回一个把所有顶点平移 <paramref name="offset"/> 后的副本。</summary>
        Public Function Offset(source As LinkMesh, delta As Vector3) As LinkMesh
            Dim out As New LinkMesh With {
                .LinkName = source.LinkName,
                .OwnerLink = source.OwnerLink,
                .NodePath = source.NodePath
            }

            For Each triangle As MeshTriangle In source.Triangles
                out.Triangles.Add(New MeshTriangle(
                    Shift(triangle.A, delta),
                    Shift(triangle.B, delta),
                    Shift(triangle.C, delta),
                    triangle.Color))
            Next

            Call out.RecalculateBounds()

            Return out
        End Function

        Public Function ToPoint(v As Vector3) As Point3D
            Return New Point3D(CSng(v.x), CSng(v.y), CSng(v.z))
        End Function

        Private Function Shift(p As Point3D, delta As Vector3) As Point3D
            Return New Point3D(CSng(p.X - delta.x), CSng(p.Y - delta.y), CSng(p.Z - delta.z))
        End Function

    End Module

End Namespace
