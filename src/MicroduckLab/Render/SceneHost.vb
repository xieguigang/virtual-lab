Imports System.Drawing
Imports System.Numerics
Imports MicroduckLab.Core
Imports Drawing3D = Microsoft.VisualBasic.Imaging.Drawing3D
Imports Dx = Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports DxWin = Microsoft.VisualBasic.Drawing.DirectX

Namespace Render

    ''' <summary>
    ''' The 3d view of the playground: it owns the direct3d back end, the camera
    ''' rig and the two geometry producers (the duck and the arena) and draws them
    ''' into the canvas of the window.
    ''' </summary>
    ''' <remarks>
    ''' The canvas is a plain <see cref="DxWin.DxCanvas"/>, not the interactive
    ''' scene control of the library: the scene of the playground is animated by
    ''' the physics and not by a model viewer, so the rig submits its own mesh
    ''' instances and its own line list and the control only has to hand over the
    ''' direct3d device once per paint.
    ''' </remarks>
    Public Class SceneHost : Implements IDisposable

        Public Property Canvas As DxWin.DxCanvas
        Public Property Session As SimSession
        Public Property Scheme As DuckVariant = DuckVariants.Classic
        Public ReadOnly Property Rig As New CameraRig()
        Public ReadOnly Property Duck As New DuckRenderer()
        Public ReadOnly Property Arena As New ArenaRenderer()

        ''' <summary>how much of the entrance draw in is shown, 0 to 1</summary>
        Public Property Reveal As Double = 1.0
        ''' <summary>the materialization plane of the duck, nothing when it is solid</summary>
        Public Property ScanY As Single? = Nothing
        ''' <summary>the opacity of the duck</summary>
        Public Property DuckAlpha As Single = 1.0F
        ''' <summary>the colour of the empty space, the ink of the ui</summary>
        Public Property Background As Color = Color.FromArgb(255, 8, 8, 12)

        Public ReadOnly Property Snapshot As New PoseSnapshot()
        Public Property Frames As Long
        ''' <summary>why the gpu back end fell back to the cpu painter, empty when it did not</summary>
        Public ReadOnly Property RenderError As String
            Get
                Return m_renderer.LastError
            End Get
        End Property

        Public ReadOnly Property DeviceInfo As String
            Get
                Return m_renderer.Description
            End Get
        End Property

        Private ReadOnly m_renderer As New Dx.Direct3D11SceneRenderer()
        Private ReadOnly m_scene As New Dx.Scene()
        Private ReadOnly m_camera As New Drawing3D.Camera()
        Private ReadOnly m_options As New Dx.SceneRenderOptions()
        Private m_ballUploaded As Boolean
        Private m_attached As Boolean
        Private m_disposed As Boolean

        ''' <summary>
        ''' bind the view to a canvas and upload the meshes of the rig
        ''' </summary>
        Public Sub Attach(canvas As DxWin.DxCanvas)
            Me.Canvas = canvas

            If m_attached Then
                Return
            End If

            m_attached = True

            AddHandler canvas.Render, AddressOf OnRender

            With m_options
                .BackgroundColor = Background
                .ShowGround = False
                .ShowBackgroundGrid = False
                .ShowConnections = True
                .CullBackFaces = False
                .MultisampleCount = 4
                .Mode = Dx.SceneRenderMode.Surface
            End With
        End Sub

        ''' <summary>
        ''' upload the meshes of the current shape, called when the session has
        ''' been booted or has switched the shape
        ''' </summary>
        Public Sub PrepareRig()
            If Session Is Nothing OrElse Session.World Is Nothing Then
                Return
            End If

            Call Duck.Register(Session.Skeleton, Session.Meshes, Scheme, m_renderer)
            Call Duck.SetJawNode(JawIndexOf(Session.Skeleton))

            If Not m_ballUploaded Then
                Call UploadBall()
            End If
        End Sub

        Private Shared Function JawIndexOf(skeleton As DuckSkeleton) As Integer
            For Each node As SkeletonNode In skeleton.Nodes
                If node.Name.Contains("jaw") Then
                    Return node.Index
                End If
            Next

            Return -1
        End Function

        Private Sub UploadBall()
            Dim sphere As Double() = MuJoCo.Geometry.StlReader.SphereTriangles(SimConstants.BallRadius, 16, 10)

            If sphere Is Nothing OrElse sphere.Length < 9 Then
                Return
            End If

            Dim color As Color = Color.FromArgb(255, 250, 246, 238)
            Dim vertices As Dx.SceneMeshVertex() = SceneMeshBuilder.FromTriangles(sphere, color)

            Call m_renderer.UploadMesh("ball", vertices)

            m_ballUploaded = True
        End Sub

        ''' <summary>
        ''' advance the camera, called once per frame by the window
        ''' </summary>
        Public Sub Update(dt As Double)
            If Session Is Nothing Then
                Return
            End If

            SyncLock Session.World
                Call Session.Snapshot(Snapshot)
            End SyncLock

            Dim focus As Vector3 = Snapshot.TrunkPosition
            Dim target As New Vector3(focus.X, focus.Y, CSng(Math.Max(focus.Z - 0.02, 0.06)))

            Call Rig.Update(target, Snapshot.TrunkYaw, dt)
        End Sub

        ''' <summary>
        ''' one frame of the scene, the canvas calls this from its paint
        ''' </summary>
        Private Sub OnRender(sender As Object, e As DxWin.DxRenderEventArgs)
            If Session Is Nothing OrElse Canvas Is Nothing Then
                Return
            End If

            Dim size As Size = Canvas.Size

            If size.Width < 8 OrElse size.Height < 8 Then
                Return
            End If

            Call Session.Snapshot(Snapshot)

            Dim translation As Matrix4x4 = Rig.ViewTranslation()

            Call ApplyCamera(size)
            Call m_renderer.ClearMeshSubmissions()

            Dim alpha As Single = DuckAlpha
            Dim clip As Boolean = ScanY.HasValue
            Dim clipY As Single = If(ScanY.HasValue, ScanY.Value, 0.0F)

            Call Duck.Submit(Snapshot, translation, m_renderer, clipY, clip, alpha)

            If Snapshot.BallVisible Then
                Call m_renderer.SubmitMesh("ball", Snapshot.BallMatrix * translation, 1.0F)
            End If

            Arena.Reveal = Reveal
            Call Arena.Build(translation, Session.ReliefEnabled)

            Call m_renderer.EnsureLineCapacity(Math.Max(Arena.VertexCount, 64))
            Call m_renderer.UploadLines(Arena.Data, Arena.VertexCount)

            m_options.BackgroundColor = Background

            Call m_renderer.Render(Canvas.Graphics, m_scene, m_camera, m_options)

            Frames += 1
        End Sub

        ''' <summary>
        ''' map the orbit of the rig onto the camera of the drawing library.
        ''' </summary>
        ''' <remarks>
        ''' The library rotates the world about the origin and projects with
        ''' ``factor = fov / (z + viewDistance)``, with the screen y axis pointing
        ''' down. The camera angles are therefore: a pitch that puts the z axis of
        ''' the model up on the screen and tilts the eye above the target, and a
        ''' yaw about the z axis for the heading of the orbit.
        ''' </remarks>
        Private Sub ApplyCamera(size As Size)
            m_camera.Screen = size
            m_camera.Offset = PointF.Empty
            ' the pitch that puts the z axis of the model up on the screen; the
            ' heading of the orbit is part of the world transform (see
            ' CameraRig.ViewTranslation)
            m_camera.AngleX = CSng(90.0 - Rig.Elevation)
            m_camera.AngleY = 0
            m_camera.AngleZ = 0
            m_camera.ViewDistance = CSng(Rig.Distance)
            m_camera.FieldOfView = CSng(Rig.PixelFactor(size.Height))
        End Sub

        ''' <summary>
        ''' the scene space point under the mouse, nothing when the ray misses the
        ''' floor
        ''' </summary>
        Public Function PickFloor(point As PointF, size As Size) As Vector3?
            ' the inverse of the pseudo perspective: the screen point fixes the
            ' direction, the z = 0 plane fixes the distance
            Dim pixel As Double = m_camera.FieldOfView
            Dim fov As Double = Math.Max(pixel, 1)

            If Math.Abs(fov) < 0.0001 Then
                Return Nothing
            End If

            Dim ndcX As Double = (point.X - size.Width * 0.5) / fov
            Dim ndcY As Double = -(point.Y - size.Height * 0.5) / fov
            Dim eye As Vector3 = Rig.Eye()
            Dim forward As Vector3 = Vector3.Normalize(Rig.Target - eye)
            Dim right As Vector3 = Vector3.Normalize(Vector3.Cross(forward, New Vector3(0, 0, 1)))
            Dim up As Vector3 = Vector3.Cross(right, forward)
            Dim direction As Vector3 = Vector3.Normalize(forward + right * CSng(ndcX) + up * CSng(ndcY))

            If Math.Abs(direction.Z) < 0.0001 Then
                Return Nothing
            End If

            Dim t As Double = -eye.Z / direction.Z

            If t <= 0 Then
                Return Nothing
            End If

            Return eye + direction * CSng(t)
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            If m_disposed Then
                Return
            End If

            m_disposed = True

            If Canvas IsNot Nothing AndAlso m_attached Then
                RemoveHandler Canvas.Render, AddressOf OnRender
            End If

            Call Duck.Reset(m_renderer)
            Call m_renderer.ClearMeshes()
            Call m_renderer.Dispose()
        End Sub
    End Class
End Namespace
