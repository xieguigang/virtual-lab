Imports System.Drawing
Imports System.Numerics
Imports MicroduckLab.Core
Imports Dx = Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Collision3D = Microsoft.VisualBasic.Engine.Physics.Collision3D

Namespace Render

    ''' <summary>
    ''' Turns the polygon faces of a convex mesh of the physics engine into the
    ''' triangle list of the renderer.
    ''' </summary>
    ''' <remarks>
    ''' The factory meshes of the engine (a box, a cone, a sphere) are polygon
    ''' soups: a box face is a quad. The render pipeline draws triangle lists, so
    ''' every face is fanned out around its first corner.
    ''' </remarks>
    Public Module SceneMeshBuilder

        Public Function FromConvex(mesh As Collision3D.ConvexMesh3D, color As Color) As Dx.SceneMeshVertex()
            Dim triangles As New List(Of Dx.SceneMeshVertex)()

            If mesh Is Nothing OrElse mesh.Faces Is Nothing Then
                Return New Dx.SceneMeshVertex() {}
            End If

            For Each face As Integer() In mesh.Faces
                If face Is Nothing OrElse face.Length < 3 Then
                    Continue For
                End If

                Dim a As Vector3 = Phys.Vec(mesh.Vertices(face(0)))

                For i As Integer = 1 To face.Length - 2
                    Dim b As Vector3 = Phys.Vec(mesh.Vertices(face(i)))
                    Dim c As Vector3 = Phys.Vec(mesh.Vertices(face(i + 1)))
                    Dim normal As Vector3 = Vector3.Cross(b - a, c - a)

                    If normal.LengthSquared() <= 0 Then
                        Continue For
                    End If

                    normal = Vector3.Normalize(normal)

                    Call triangles.Add(New Dx.SceneMeshVertex(a, normal, color))
                    Call triangles.Add(New Dx.SceneMeshVertex(b, normal, color))
                    Call triangles.Add(New Dx.SceneMeshVertex(c, normal, color))
                Next
            Next

            Return triangles.ToArray()
        End Function
    End Module
End Namespace
