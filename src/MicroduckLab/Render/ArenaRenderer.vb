Imports System.Drawing
Imports System.Numerics
Imports MicroduckLab.Core

Namespace Render

    ''' <summary>
    ''' The tron grid of the arena and the four low walls, as one dynamic line
    ''' list.
    ''' </summary>
    ''' <remarks>
    ''' The browser build drew the floor with a shader: an infinite grid with an
    ''' anti aliased line pattern, a radial fade and, on top of it, the vertex
    ''' displacement of the relief. Without a post process pass the same look is
    ''' rebuilt from real lines here: the section lines of the grid, the edges of
    ''' the walls and the marker of the waypoint, in one buffer that the back end
    ''' refills every frame.
    ''' </remarks>
    Public Class ArenaRenderer

        Private Const MaxVertices As Integer = 2048

        ''' <summary>the line list, four singles per vertex: xyz and the packed color</summary>
        Public ReadOnly Property Data As Single() = New Single(MaxVertices * 4 - 1) {}

        Public Property VertexCount As Integer
        ''' <summary>how many lines of the draw in of the entrance are shown, 0 to 1</summary>
        Public Property Reveal As Double = 1.0
        ''' <summary>the ground marker of the waypoint, nothing when there is none</summary>
        Public Property Waypoint As Vector3? = Nothing

        Private ReadOnly m_grid As Color = Color.FromArgb(150, 255, 122, 47)
        Private ReadOnly m_wall As Color = Color.FromArgb(215, 255, 122, 47)
        Private ReadOnly m_marker As Color = Color.FromArgb(240, 124, 208, 255)

        ''' <summary>
        ''' rebuild the line list of the frame
        ''' </summary>
        ''' <param name="view">the translation that puts the camera target at the origin</param>
        ''' <param name="relief">displace the grid with the cosine bumps</param>
        Public Sub Build(view As Matrix4x4, Optional relief As Boolean = False)
            Dim count As Integer = 0
            Dim half As Double = SimConstants.ArenaHalf
            Dim stepSize As Double = SimConstants.GridSection

            ' the section lines of the floor
            For i As Integer = 0 To 5
                Dim t As Double = -half + i * stepSize

                Call Line(New Vector3(CSng(t), CSng(-half), 0), New Vector3(CSng(t), CSng(half), 0), m_grid, view, count, relief)
                Call Line(New Vector3(CSng(-half), CSng(t), 0), New Vector3(CSng(half), CSng(t), 0), m_grid, view, count, relief)
            Next

            ' the walls: the two rims and the four corners of every wall
            Dim height As Double = SimConstants.ArenaWallHeight
            Dim corners As New List(Of Vector3)()

            For Each corner As Vector3 In {
                    New Vector3(CSng(-half), CSng(-half), 0),
                    New Vector3(CSng(half), CSng(-half), 0),
                    New Vector3(CSng(half), CSng(half), 0),
                    New Vector3(CSng(-half), CSng(half), 0)}

                Call corners.Add(corner)
            Next

            For i As Integer = 0 To 3
                Dim a As Vector3 = corners(i)
                Dim b As Vector3 = corners((i + 1) Mod 4)
                Dim topA As Vector3 = New Vector3(a.X, a.Y, CSng(height))
                Dim topB As Vector3 = New Vector3(b.X, b.Y, CSng(height))

                Call Line(a, b, m_wall, view, count, False)
                Call Line(topA, topB, m_wall, view, count, False)
                Call Line(a, topA, m_wall, view, count, False)
            Next

            If Waypoint.HasValue Then
                Call Ring(Waypoint.Value, 0.14, m_marker, view, count)
                Call Line(Waypoint.Value + New Vector3(-0.2F, 0, 0), Waypoint.Value + New Vector3(0.2F, 0, 0), m_marker, view, count, False)
                Call Line(Waypoint.Value + New Vector3(0, -0.2F, 0), Waypoint.Value + New Vector3(0, 0.2F, 0), m_marker, view, count, False)
            End If

            Me.VertexCount = count
        End Sub

        ''' <summary>
        ''' append one line, the draw in of the entrance drops the lines that are
        ''' not reached yet
        ''' </summary>
        Private Sub Line(a As Vector3, b As Vector3, color As Color, view As Matrix4x4,
                         ByRef count As Integer, relief As Boolean)
            If count + 2 > MaxVertices Then
                Return
            End If

            If Reveal < 0.999 Then
                ' the draw in runs from the back wall to the front wall
                Dim progress As Double = (Math.Max(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)) + SimConstants.ArenaHalf) /
                                         (2 * SimConstants.ArenaHalf)

                If progress > Reveal Then
                    Return
                End If
            End If

            Dim start As Vector3 = a
            Dim final As Vector3 = b

            If relief Then
                start = New Vector3(start.X, start.Y, CSng(SimConstants.ReliefHeight(start.X, start.Y)) + 0.001F)
                final = New Vector3(final.X, final.Y, CSng(SimConstants.ReliefHeight(final.X, final.Y)) + 0.001F)
            Else
                start = New Vector3(start.X, start.Y, start.Z + 0.001F)
                final = New Vector3(final.X, final.Y, final.Z + 0.001F)
            End If

            Call Write(start, color, view, count)
            Call Write(final, color, view, count)
        End Sub

        Private Sub Ring(center As Vector3, radius As Single, color As Color, view As Matrix4x4, ByRef count As Integer)
            Const segments As Integer = 24
            Dim previous As Vector3 = center + New Vector3(radius, 0, 0)

            For i As Integer = 1 To segments
                Dim angle As Double = 2 * Math.PI * i / segments
                Dim point As Vector3 = center + New Vector3(CSng(radius * Math.Cos(angle)), CSng(radius * Math.Sin(angle)), 0)

                Call Line(previous, point, color, view, count, False)

                previous = point
            Next
        End Sub

        Private Sub Write(point As Vector3, color As Color, view As Matrix4x4, ByRef count As Integer)
            Dim world As Vector3 = Vector3.Transform(point, view)
            Dim offset As Integer = count * 4

            Data(offset) = world.X
            Data(offset + 1) = world.Y
            Data(offset + 2) = world.Z
            Data(offset + 3) = BitConverter.Int32BitsToSingle(Pack(color))

            count += 1
        End Sub

        ''' <summary>
        ''' pack a colour into the byte order that the line vertex expects
        ''' (R, G, B, A in memory, little endian)
        ''' </summary>
        Public Shared Function Pack(color As Color) As Integer
            ' the color channel of the line vertex is read as one 32 bit word,
            ' and the byte order of the swap chain is BGRA, so the word has to
            ' carry the channels in that order
            Return (CInt(color.A) << 24) Or (CInt(color.R) << 16) Or (CInt(color.G) << 8) Or CInt(color.B)
        End Function
    End Class
End Namespace
