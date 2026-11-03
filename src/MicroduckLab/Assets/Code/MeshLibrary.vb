Imports System.Drawing
Imports System.Numerics
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D

Namespace Assets

    ''' <summary>
    ''' One triangle of a mesh in its own local space.
    ''' </summary>
    Public Structure MeshTriangle
        Public A As Vector3
        Public B As Vector3
        Public C As Vector3
        Public Normal As Vector3
    End Structure

    ''' <summary>
    ''' The STL meshes of the robot, parsed once and reused by the render rig and
    ''' by the collision proxies.
    ''' </summary>
    ''' <remarks>
    ''' An STL file is a triangle soup without any index and without a topology,
    ''' so it is kept as a triangle list here: the renderer wants per face normals
    ''' (flat shading) which is exactly what a triangle soup gives, and the
    ''' collision proxy wants the vertices, which the hull of the soup reduces to
    ''' a few dozen corners.
    '''
    ''' Every mesh carries the local placement of its geom: the original model
    ''' places one mesh file several times with a different rotation, so the mesh
    ''' is baked into the space of the body once at boot and the render rig then
    ''' only needs the world matrix of the body.
    ''' </remarks>
    Public Class MeshLibrary

        Private ReadOnly m_meshes As New Dictionary(Of String, MeshTriangle())(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly m_hulls As New Dictionary(Of String, ConvexHullResult)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>how many triangles the loaded meshes hold in total</summary>
        Public Property TriangleCount As Integer
        ''' <summary>how many mesh files were parsed</summary>
        Public ReadOnly Property MeshCount As Integer
            Get
                Return m_meshes.Count
            End Get
        End Property

        ''' <summary>
        ''' the triangles of one mesh file, parsed on first use
        ''' </summary>
        ''' <remarks>
        ''' The two documents name a mesh differently: the kinematics document
        ''' keeps the file name with its extension, the MJCF strips it. The name is
        ''' normalized here so that both spellings share one cache entry.
        ''' </remarks>
        ''' <exception cref="IO.FileNotFoundException">the mesh file does not exist</exception>
        Public Function GetMesh(meshName As String) As MeshTriangle()
            Dim key As String = NormalizeName(meshName)
            Dim triangles As MeshTriangle() = Nothing

            If m_meshes.TryGetValue(key, triangles) Then
                Return triangles
            End If

            Dim path As String = AssetPath.MeshFile(key & ".stl")

            If Not IO.File.Exists(path) Then
                Throw New IO.FileNotFoundException($"the mesh '{meshName}' is missing from {AssetPath.MeshDirectory}", path)
            End If

            triangles = Parse(path)

            m_meshes(key) = triangles
            TriangleCount += triangles.Length

            Return triangles
        End Function

        ''' <summary>
        ''' the normalized cache name of a mesh, without the folder and without
        ''' the extension, lower case
        ''' </summary>
        Public Function NormalizeName(meshName As String) As String
            If String.IsNullOrEmpty(meshName) Then
                Return ""
            End If

            Return IO.Path.GetFileNameWithoutExtension(meshName).ToLowerInvariant()
        End Function

        ''' <summary>
        ''' the convex hull of one mesh file, computed on first use
        ''' </summary>
        ''' <remarks>
        ''' A hull that does not converge falls back to the bounding box of the
        ''' mesh: a coarse proxy is always better than a boot that stops, and the
        ''' reason is kept in <see cref="FallbackReason"/> for the boot console.
        ''' </remarks>
        Public Function GetHull(meshName As String) As ConvexHullResult
            Dim key As String = NormalizeName(meshName)
            Dim hull As ConvexHullResult = Nothing

            If m_hulls.TryGetValue(key, hull) Then
                Return hull
            End If

            Dim triangles As MeshTriangle() = GetMesh(meshName)
            Dim points As New List(Of Vector3)()

            For Each triangle As MeshTriangle In triangles
                Call points.Add(triangle.A)
                Call points.Add(triangle.B)
                Call points.Add(triangle.C)
            Next

            Try
                hull = ConvexHull.Compute(points.ToArray())
            Catch ex As InvalidOperationException
                FallbackReason = $"the hull of '{key}' fell back to its bounding box: {ex.Message}"
                hull = BoundingBoxHull(points)
            End Try

            m_hulls(key) = hull

            Return hull
        End Function

        ''' <summary>
        ''' the reason of the last hull that had to fall back to a bounding box,
        ''' empty when every hull converged
        ''' </summary>
        Public Property FallbackReason As String = ""

        Private Shared Function BoundingBoxHull(points As List(Of Vector3)) As ConvexHullResult
            Dim min As Vector3 = points(0)
            Dim max As Vector3 = points(0)

            For Each point As Vector3 In points
                min = Vector3.Min(min, point)
                max = Vector3.Max(max, point)
            Next

            Dim corners As Vector3() = {
                New Vector3(min.X, min.Y, min.Z),
                New Vector3(max.X, min.Y, min.Z),
                New Vector3(max.X, max.Y, min.Z),
                New Vector3(min.X, max.Y, min.Z),
                New Vector3(min.X, min.Y, max.Z),
                New Vector3(max.X, min.Y, max.Z),
                New Vector3(max.X, max.Y, max.Z),
                New Vector3(min.X, max.Y, max.Z)
            }

            Return ConvexHull.Compute(corners, 1.0E-06)
        End Function

        ''' <summary>
        ''' build the render vertices of one geom: the mesh is baked into the
        ''' local space of its body and every face carries the color of the geom
        ''' </summary>
        Public Function BuildVisualMesh(meshName As String, position As Vector3, rotation As Quaternion,
                                        color As Color) As SceneMeshVertex()
            Dim triangles As MeshTriangle() = GetMesh(meshName)
            Dim vertices As SceneMeshVertex() = New SceneMeshVertex(triangles.Length * 3 - 1) {}

            For i As Integer = 0 To triangles.Length - 1
                Dim triangle As MeshTriangle = triangles(i)
                Dim normal As Vector3 = Vector3.Transform(triangle.Normal, rotation)
                Dim index As Integer = i * 3

                vertices(index) = New SceneMeshVertex(Vector3.Transform(triangle.A, rotation) + position, normal, color)
                vertices(index + 1) = New SceneMeshVertex(Vector3.Transform(triangle.B, rotation) + position, normal, color)
                vertices(index + 2) = New SceneMeshVertex(Vector3.Transform(triangle.C, rotation) + position, normal, color)
            Next

            Return vertices
        End Function

        ''' <summary>
        ''' build the vertex cloud of the convex hull of one geom, in the local
        ''' space of the body and shifted by <paramref name="offset"/>
        ''' </summary>
        ''' <remarks>
        ''' A rigid transform keeps a convex body convex, so the hull is computed
        ''' once in the space of the mesh file and only its corners are moved
        ''' here. The offset is used to bring the corners from the body frame
        ''' into the center of mass frame of the body (the physics engine measures
        ''' every shape from the center of mass).
        ''' </remarks>
        Public Function BuildCollisionHull(meshName As String, position As Vector3, rotation As Quaternion,
                                           offset As Vector3) As Vector3()
            Dim hull As ConvexHullResult = GetHull(meshName)
            Dim vertices As Vector3() = New Vector3(hull.Vertices.Length - 1) {}

            For i As Integer = 0 To hull.Vertices.Length - 1
                vertices(i) = Vector3.Transform(hull.Vertices(i), rotation) + position + offset
            Next

            Return vertices
        End Function

        ''' <summary>
        ''' the triangle indices of the convex hull of one mesh file
        ''' </summary>
        Public Function GetHullFaces(meshName As String) As Integer()()
            Return GetHull(meshName).Faces
        End Function

        ' /********************************************************************************/
        '   parsing
        ' /********************************************************************************/

        Private Shared Function Parse(path As String) As MeshTriangle()
            Dim model As Microsoft.VisualBasic.Imaging.Landscape.Data.SceneModel =
                Microsoft.VisualBasic.Imaging.Landscape.Data.ModelLoader.LoadModel(path)
            Dim triangles As New List(Of MeshTriangle)()

            If model Is Nothing OrElse model.Surfaces Is Nothing Then
                Return New MeshTriangle() {}
            End If

            For Each surface As Microsoft.VisualBasic.Imaging.Landscape.Data.Surface In model.Surfaces
                Dim corners As Microsoft.VisualBasic.Imaging.Landscape.Data.Vertex() = surface.vertices

                If corners Is Nothing OrElse corners.Length < 3 Then
                    Continue For
                End If

                ' an stl triangle has three corners, a ply or an obj polygon is
                ' fanned out around its first corner
                Dim first As Point3D = corners(0).PointData

                For i As Integer = 1 To corners.Length - 2
                    Dim b As Point3D = corners(i).PointData
                    Dim c As Point3D = corners(i + 1).PointData
                    Dim a3 As New Vector3(CSng(first.X), CSng(first.Y), CSng(first.Z))
                    Dim b3 As New Vector3(CSng(b.X), CSng(b.Y), CSng(b.Z))
                    Dim c3 As New Vector3(CSng(c.X), CSng(c.Y), CSng(c.Z))
                    Dim cross As Vector3 = Vector3.Cross(b3 - a3, c3 - a3)
                    Dim length As Double = cross.Length()

                    If length <= 1.0E-12 Then
                        Continue For
                    End If

                    Call triangles.Add(New MeshTriangle With {
                        .A = a3,
                        .B = b3,
                        .C = c3,
                        .Normal = cross / CSng(length)
                    })
                Next
            Next

            Return triangles.ToArray()
        End Function
    End Class
End Namespace
