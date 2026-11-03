Imports System.Drawing
Imports System.Numerics
Imports MicroduckLab.Assets
Imports MicroduckLab.Core
Imports Dx = Microsoft.VisualBasic.Drawing.DirectX.Scene3D

Namespace Render

    ''' <summary>
    ''' The render rig of the duck: one uploaded mesh per geom and one instance per
    ''' geom and frame.
    ''' </summary>
    ''' <remarks>
    ''' The browser build rebuilt a three.js skeleton and let the gpu skin it. The
    ''' desktop pipeline has no skeleton, so the same pose is expressed as one
    ''' world matrix per rigid part: every geom of every link is a mesh that is
    ''' uploaded once, and the frame only submits ``geom * body`` per part. A duck
    ''' of forty six geoms therefore costs forty six draw calls per frame and no
    ''' vertex ever travels over the bus twice.
    ''' </remarks>
    Public Class DuckRenderer

        Private Const JawMaxOpen As Single = 0.32F

        ''' <summary>the number of meshes that are uploaded</summary>
        Public Property MeshCount As Integer
        ''' <summary>how many instances the last frame submitted</summary>
        Public Property InstanceCount As Integer

        Private m_keys As String() = New String() {}
        Private m_nodes As Integer() = New Integer() {}
        Private m_locals As Matrix4x4() = New Matrix4x4() {}
        Private m_count As Integer
        Private m_signature As String = ""

        ''' <summary>
        ''' upload the meshes of the rig, once per shape and colour scheme
        ''' </summary>
        Public Sub Register(skeleton As DuckSkeleton, meshes As MeshLibrary, scheme As DuckVariant,
                            renderer As Dx.Direct3D11SceneRenderer)
            Dim signature As String = $"{skeleton.Loco}|{scheme.Key}|{skeleton.Nodes.Count}"

            If signature = m_signature Then
                Return
            End If

            m_signature = signature
            m_count = 0
            InstanceCount = 0

            Dim keys As New List(Of String)()
            Dim nodes As New List(Of Integer)()
            Dim locals As New List(Of Matrix4x4)()

            For Each node As SkeletonNode In skeleton.Nodes
                For i As Integer = 0 To node.Geoms.Count - 1
                    Dim geom As KinematicGeom = node.Geoms(i)
                    Dim color As Color = DuckVariants.ColorOf(scheme, geom.Mesh, geom.Color)
                    Dim key As String = $"{node.Index}:{i}:{scheme.Key}"

                    ' the mesh is uploaded in the space of its file: the placement
                    ' of the geom inside the body is carried by the instance
                    ' matrix of the frame, not baked into the vertices. Baking it
                    ' here as well would apply the pose of the geom twice and
                    ' every part would drift out of the body
                    Dim vertices As Dx.SceneMeshVertex() = meshes.BuildVisualMesh(geom.Mesh, Vector3.Zero, Quaternion.Identity, color)

                    Call renderer.UploadMesh(key, vertices)

                    Call keys.Add(key)
                    Call nodes.Add(node.Index)

                    Dim local As Matrix4x4 = Matrix4x4.CreateFromQuaternion(geom.Rotation) *
                                             Matrix4x4.CreateTranslation(geom.Position)

                    Call locals.Add(local)
                Next
            Next

            m_keys = keys.ToArray()
            m_nodes = nodes.ToArray()
            m_locals = locals.ToArray()
            m_count = m_keys.Length
            MeshCount = m_count
        End Sub

        '''
        ''' <summary>
        ''' submit one instance per geom for this frame
        ''' </summary>
        ''' <param name="snapshot">the pose of the duck</param>
        ''' <param name="view">the translation that puts the camera target at the origin</param>
        ''' <param name="renderer">the back end that draws the instances</param>
        ''' <param name="clipY">the materialization plane of the entrance, in world z</param>
        ''' <param name="alpha">the opacity of the whole duck</param>
        Public Sub Submit(snapshot As PoseSnapshot, view As Matrix4x4, renderer As Dx.Direct3D11SceneRenderer,
                          Optional clipY As Single = 0.0F, Optional clip As Boolean = False,
                          Optional alpha As Single = 1.0F)
            InstanceCount = 0

            If m_count = 0 Then
                Return
            End If

            Dim jaw As Single = snapshot.JawOpen
            Dim jawIndex As Integer = JawNode()

            For i As Integer = 0 To m_count - 1
                Dim node As Integer = m_nodes(i)

                If node < 0 OrElse node >= snapshot.BodyCount Then
                    Continue For
                End If

                Dim world As Matrix4x4 = m_locals(i) * snapshot.BodyWorld(node) * view

                If node = jawIndex AndAlso jaw > 0.001F Then
                    ' the beak is a separate part in the rig: the jaw hinge is
                    ' rebuilt here, the same way the browser build reconstructed
                    ' it from the meshes
                    Dim hinge As Matrix4x4 = Matrix4x4.CreateRotationX(jaw * JawMaxOpen)

                    world = m_locals(i) * hinge * snapshot.BodyWorld(node) * view
                End If

                Call renderer.SubmitMesh(m_keys(i), world, alpha, clipY, clip)

                InstanceCount += 1
            Next
        End Sub

        Private m_jawIndex As Integer = -2

        ''' <summary>
        ''' the link that carries the beak
        ''' </summary>
        Private Function JawNode() As Integer
            Return m_jawIndex
        End Function

        ''' <summary>
        ''' tell the renderer which link is the jaw of the current rig
        ''' </summary>
        Public Sub SetJawNode(index As Integer)
            m_jawIndex = index
        End Sub

        ''' <summary>forget the uploaded meshes of the previous shape</summary>
        Public Sub Reset(renderer As Dx.Direct3D11SceneRenderer)
            If m_count = 0 Then
                Return
            End If

            For Each key As String In m_keys
                Call renderer.RemoveMesh(key)
            Next

            m_signature = ""
            m_count = 0
        End Sub
    End Class
End Namespace
