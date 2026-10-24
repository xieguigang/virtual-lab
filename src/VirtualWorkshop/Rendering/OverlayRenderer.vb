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
        Public Shared Function PackColor(color As Color) As Single
            Dim rgba As Integer = color.R Or (color.G << 8) Or (color.B << 16) Or (CInt(color.A) << 24)

            Return BitConverter.Int32BitsToSingle(rgba)
        End Function

    End Class

End Namespace
