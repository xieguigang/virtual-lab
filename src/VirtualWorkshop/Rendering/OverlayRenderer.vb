Imports System.Drawing
Imports Microsoft.VisualBasic.Drawing.DirectX
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Simulation

Namespace Rendering

    ''' <summary>
    ''' 场景叠加层：TCP 轨迹、世界坐标轴、碰撞接触点。
    ''' </summary>
    ''' <remarks>
    ''' 这些都走 <see cref="Direct3D11SceneRenderer.UploadLines"/> 的**动态线缓冲**通道：
    ''' 每帧只做一次 buffer 写入，不会让 GPU 几何缓存失效（与 <c>Scene.LoadSurfaces</c>
    ''' 的整份重建是两个独立通道）。
    ''' </remarks>
    Public Class OverlayRenderer

        Public Property ShowTrajectory As Boolean = True
        Public Property ShowAxes As Boolean = True
        Public Property ShowContacts As Boolean = True

        ''' <summary>是否绘制地面网格（画在世界 Z = <see cref="UiTheme.GroundZ"/> 的平面上，也就是机械臂底座底面）。</summary>
        Public Property ShowGroundGrid As Boolean = True

        ''' <summary>是否把弹簧平台的弹簧画成螺旋线。</summary>
        Public Property ShowSprings As Boolean = True

        ''' <summary>地面网格的半边长（米），以世界原点为中心，默认覆盖整个工作区。</summary>
        Public Property GridHalfSize As Double = 0.65

        ''' <summary>地面网格的格距（米）。</summary>
        Public Property GridStep As Double = 0.05

        ''' <summary>每隔几格画一条主线。</summary>
        Public Property GridMajorEvery As Integer = 5

        ''' <summary>轨迹最多保留的采样点。</summary>
        Public Property TrailLength As Integer = 600

        Public ReadOnly Property Trail As New List(Of Vector3)()

        Public Property VertexCount As Integer

        Private ReadOnly _buffer As New List(Of Single)(8192)
        Private _pending As Single() = New Single(-1) {}

        Private Shared ReadOnly AxisXColor As Color = Color.FromArgb(239, 68, 68)
        Private Shared ReadOnly AxisYColor As Color = Color.FromArgb(34, 197, 94)
        Private Shared ReadOnly AxisZColor As Color = Color.FromArgb(56, 189, 248)
        Private Shared ReadOnly TrailColor As Color = Color.FromArgb(34, 211, 238)
        Private Shared ReadOnly ContactColor As Color = Color.FromArgb(248, 113, 113)

        ''' <summary>把当前 TCP 记录进轨迹。</summary>
        Public Sub Sample(snapshot As StateSnapshot)
            If snapshot Is Nothing OrElse snapshot.Tcp Is Nothing OrElse snapshot.Tcp.Length < 3 Then Return

            Dim point As New Vector3(snapshot.Tcp(0), snapshot.Tcp(1), snapshot.Tcp(2))

            If Trail.Count > 0 Then
                Dim last As Vector3 = Trail(Trail.Count - 1)

                If Vector3Math.Length(point - last) < 0.002 Then Return
            End If

            Trail.Add(point)

            While Trail.Count > TrailLength
                Trail.RemoveAt(0)
            End While
        End Sub

        Public Sub Clear()
            Call Trail.Clear()
        End Sub

        ''' <summary>绘制叠加层；GPU 后端不可用时自动跳过。</summary>
        Public Sub Render(canvas As DxScene3DCanvas, simulation As ArmSimulation,
                          snapshot As StateSnapshot, anchor As Vector3)
            If canvas Is Nothing Then Return

            Dim renderer As Direct3D11SceneRenderer = TryCast(canvas.Renderer, Direct3D11SceneRenderer)

            If renderer Is Nothing Then Return

            Call _buffer.Clear()

            If ShowGroundGrid Then
                Call AddGroundGrid(anchor)
            End If

            If ShowSprings AndAlso simulation IsNot Nothing Then
                Call AddSprings(simulation, anchor)
            End If

            If ShowAxes Then
                Call AddAxis(New Vector3(1, 0, 0), AxisXColor, anchor)
                Call AddAxis(New Vector3(0, 1, 0), AxisYColor, anchor)
                Call AddAxis(New Vector3(0, 0, 1), AxisZColor, anchor)
            End If

            If ShowTrajectory Then
                Call AddTrail(anchor)
            End If

            If ShowContacts AndAlso simulation IsNot Nothing Then
                Call AddContacts(simulation, anchor)
            End If

            Dim count As Integer = _buffer.Count \ 4

            VertexCount = count

            If count = 0 Then Return

            _pending = _buffer.ToArray()

            renderer.EnsureLineCapacity(count)
            renderer.UploadLines(_pending, count)
        End Sub

        ''' <summary>
        ''' 世界 Z = <see cref="UiTheme.GroundZ"/> 平面上的地面网格（以世界原点为中心）。
        ''' </summary>
        ''' <remarks>
        ''' <para>
        ''' 没有用 <c>DxScene3DCanvas.ShowGround</c>：它的高度取自 <c>Scene.GroundZ</c>，
        ''' 而 <see cref="Scene"/> 会先把顶点平移成"质心为原点"、<see cref="RenderHost"/> 之后又把质心加回来，
        ''' 于是 GroundZ 与真正的最低点差了一个质心，网格会浮在底座上方几十毫米处。
        ''' </para>
        ''' <para>
        ''' 这里自己把世界坐标减掉锚点，保证网格正好躺在机械臂底座底面（世界 z=0）。
        ''' </para>
        ''' </remarks>
        Private Sub AddGroundGrid(anchor As Vector3)
            Dim half As Double = System.Math.Max(0.05, GridHalfSize)
            Dim stepSize As Double = System.Math.Max(0.01, GridStep)
            Dim divisions As Integer = CInt(System.Math.Round(2.0 * half / stepSize))
            Dim z As Double = UiTheme.GroundZ - anchor.z
            Dim majorEvery As Integer = System.Math.Max(1, GridMajorEvery)

            For i As Integer = 0 To divisions
                Dim t As Double = -half + i * stepSize
                Dim color As Color = If((i Mod majorEvery) = 0, UiTheme.GroundGridMajor, UiTheme.GroundGridMinor)

                ' 平行 X 轴的一排（y = t）
                Call AddLine(New Vector3(CSng(-half - anchor.x), CSng(t - anchor.y), CSng(z)),
                             New Vector3(CSng(half - anchor.x), CSng(t - anchor.y), CSng(z)), color)

                ' 平行 Y 轴的一排（x = t）
                Call AddLine(New Vector3(CSng(t - anchor.x), CSng(-half - anchor.y), CSng(z)),
                             New Vector3(CSng(t - anchor.x), CSng(half - anchor.y), CSng(z)), color)
            Next
        End Sub

        Private Sub AddAxis(direction As Vector3, color As Color, anchor As Vector3)
            Dim origin As Vector3 = New Vector3(0, 0, 0) - anchor
            Dim tip As Vector3 = direction * 0.18 - anchor

            Call AddLine(origin, tip, color)
            Call AddLine(tip, tip + direction * -0.02 + New Vector3(0, 0, 0.01), color)
            Call AddLine(tip, tip + direction * -0.02 + New Vector3(0, 0, -0.01), color)
        End Sub

        Private Sub AddTrail(anchor As Vector3)
            If Trail.Count < 2 Then Return

            For i As Integer = 1 To Trail.Count - 1
                Call AddLine(Trail(i - 1) - anchor, Trail(i) - anchor, TrailColor)
            Next
        End Sub

        Private Sub AddContacts(simulation As ArmSimulation, anchor As Vector3)
            Dim world As PhysicsWorld3D = simulation.World

            If world Is Nothing Then Return

            Dim drawn As Integer = 0

            For Each manifold As Manifold3D In world.Contacts
                For Each contact As ContactPoint3D In manifold.Contacts
                    If drawn >= 24 Then Exit For

                    Dim p As Vector3 = contact.Position - anchor
                    Const size As Double = 0.012

                    Call AddLine(p + New Vector3(-size, 0, 0), p + New Vector3(size, 0, 0), ContactColor)
                    Call AddLine(p + New Vector3(0, -size, 0), p + New Vector3(0, size, 0), ContactColor)
                    Call AddLine(p + New Vector3(0, 0, -size), p + New Vector3(0, 0, size), ContactColor)

                    drawn += 1
                Next
            Next
        End Sub

        Private Sub AddLine(a As Vector3, b As Vector3, color As Color)
            Dim packed As Single = PackColor(color)

            _buffer.Add(CSng(a.x))
            _buffer.Add(CSng(a.y))
            _buffer.Add(CSng(a.z))
            _buffer.Add(packed)

            _buffer.Add(CSng(b.x))
            _buffer.Add(CSng(b.y))
            _buffer.Add(CSng(b.z))
            _buffer.Add(packed)
        End Sub

        ''' <summary>把颜色打包成线顶点的第 4 个 Single（内存顺序 R,G,B,A）。</summary>
        ''' <remarks>
        ''' <c>Color.R/G/B</c> 都是 <c>Byte</c>，而 VB 的移位运算符**保持左侧操作数类型**：
        ''' <c>color.G &lt;&lt; 8</c> 会直接溢出成 0，结果所有线条只剩红色通道。
        ''' 必须先 CInt 再移位。
        ''' </remarks>
        Public Shared Function PackColor(color As Color) As Single
            Dim rgba As Integer = CInt(color.R) Or (CInt(color.G) << 8) Or (CInt(color.B) << 16) Or (CInt(color.A) << 24)

            Return BitConverter.Int32BitsToSingle(rgba)
        End Function

    End Class

End Namespace
