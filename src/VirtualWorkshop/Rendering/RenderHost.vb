Imports System.Diagnostics
Imports System.Drawing
Imports Microsoft.VisualBasic.Drawing.DirectX
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports VirtualWorkshop.Simulation

Namespace Rendering

    ''' <summary>
    ''' 三维视口的宿主：负责相机、每帧几何提交与叠加层。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' <b>不要</b>调用 <see cref="DxScene3DCanvas.LoadSurfaces"/>：它会重置相机并重新 FitView，
    ''' 动画时画面会不停跳。这里直接用 <see cref="DxScene3DCanvas.Scene"/> 的 LoadSurfaces，
    ''' 然后自己 <c>RequestRender</c>。
    ''' </para>
    ''' <para>
    ''' <see cref="Scene.LoadSurfaces"/> 会把顶点平移成"质心在原点"，而 GPU 管线不使用
    ''' <see cref="Scene.Center"/>，所以提交后必须按 Center 把顶点加回去，
    ''' 才能让渲染坐标恒等于"世界坐标 − 锚点"，否则整机每帧都会随质心漂移。
    ''' </para>
    ''' </remarks>
    Public Class RenderHost

        Public ReadOnly Property Builder As New SceneBuilder()
        Public ReadOnly Property Overlay As New OverlayRenderer()

        Public Property Canvas As DxScene3DCanvas

        ''' <summary>
        ''' 渲染锚点：世界坐标减去它以后再交给相机（让相机绕工作中心转）。
        ''' 六轴臂基座在原点、向 +X 伸展约 0.6 m，所以锚点取在整机几何中心附近。
        ''' </summary>
        Public Property Anchor As Vector3 = New Vector3(0.26, 0, 0.16)

        Public Property Enabled As Boolean = True
        Public Property LastRenderMs As Double
        Public Property Frames As Long

        Private _cameraInitialized As Boolean

        Public Sub Attach(canvas As DxScene3DCanvas)
            Me.Canvas = canvas

            If canvas Is Nothing Then Return

            With canvas
                .AutoClear = False
                .BackgroundColor = UiTheme.ViewportBackground
                .RenderMode = SceneRenderMode.Surface

                ' 不要屏幕空间的背景网格，也不要 ShowGround：
                ' Scene.GroundZ 是"顶点质心平移后的最低点"，与真正的底座底面差一个质心。
                ' 地面网格改由 OverlayRenderer 用动态线通道自己画（世界 z=0，正好是底座底面）。
                .ShowGround = False
                .ShowBackgroundGrid = False
                .CullBackFaces = False
                .ShowDebugOverlay = False
                .EnableKeyboardShortcuts = True
                .PointSize = 3
            End With

            Call ApplyCamera()
        End Sub

        ''' <summary>设置一个能看清整台机械臂的初始视角。</summary>
        Public Sub ApplyCamera()
            If Canvas Is Nothing Then Return

            Dim controller As OrbitCameraController = Canvas.Controller

            controller.Camera.AngleX = 24
            controller.Camera.AngleY = -36
            controller.Camera.AngleZ = 0
            controller.Camera.ViewDistance = 1.1F
            controller.Camera.FieldOfView = CSng(256 * 6)
        End Sub

        Public Sub ResetView()
            Call ApplyCamera()
            _cameraInitialized = False
        End Sub

        ''' <summary>提交一帧。</summary>
        Public Sub Render(simulation As ArmSimulation, snapshot As StateSnapshot)
            If Not Enabled OrElse Canvas Is Nothing OrElse simulation Is Nothing Then Return

            Dim watch As Stopwatch = Stopwatch.StartNew()
            Dim faces As List(Of Surface) = Builder.BuildSurfaces(simulation, Anchor)

            Canvas.Scene.LoadSurfaces(faces)
            Call CompensateCenter()

            If Not _cameraInitialized Then
                Canvas.FitView()
                _cameraInitialized = True
            End If

            If snapshot IsNot Nothing Then
                Call Overlay.Sample(snapshot)
            End If

            Call Overlay.Render(Canvas, simulation, snapshot, Anchor)
            Canvas.RequestRender()

            LastRenderMs = watch.Elapsed.TotalMilliseconds
            Frames += 1
        End Sub

        ''' <summary>把 LoadSurfaces 减掉的质心加回去，让渲染坐标等于"世界 − 锚点"。</summary>
        Private Sub CompensateCenter()
            Dim center As Point3D = Canvas.Scene.Center

            If center.X = 0 AndAlso center.Y = 0 AndAlso center.Z = 0 Then Return

            For Each face As Surface In Canvas.Scene.Surfaces
                Dim vertices As Point3D() = face.vertices

                If vertices Is Nothing Then Continue For

                For i As Integer = 0 To vertices.Length - 1
                    vertices(i) = New Point3D(vertices(i).X + center.X,
                                              vertices(i).Y + center.Y,
                                              vertices(i).Z + center.Z)
                Next

                face.vertices = vertices
            Next
        End Sub

    End Class

End Namespace
