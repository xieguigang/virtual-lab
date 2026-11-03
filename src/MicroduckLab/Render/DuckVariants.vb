Imports System.Drawing
Imports MicroduckLab.Assets

Namespace Render

    ''' <summary>
    ''' One colour scheme of the duck, the semantic slots of ``variants.js``.
    ''' </summary>
    Public Class DuckVariant
        Public Property Key As String
        Public Property Label As String
        ''' <summary>the body shell</summary>
        Public Property Shell As Color
        ''' <summary>the head and the beak surround</summary>
        Public Property Head As Color
        ''' <summary>the beak and the feet</summary>
        Public Property Beak As Color
        ''' <summary>the joints, the motors and the bearings</summary>
        Public Property Joint As Color
        ''' <summary>the paws of the renderer</summary>
        Public Property Wheel As Color
        ''' <summary>the voice library of this colour</summary>
        Public Property Voice As String
    End Class

    ''' <summary>
    ''' The four official colour schemes of the duck and the mapping from a mesh
    ''' name to the slot that it takes.
    ''' </summary>
    Public Module DuckVariants

        Public ReadOnly Classic As New DuckVariant With {
            .Key = "classic",
            .Label = "Classic",
            .Shell = Color.FromArgb(255, 244, 238, 226),
            .Head = Color.FromArgb(255, 250, 246, 238),
            .Beak = Color.FromArgb(255, 255, 138, 46),
            .Joint = Color.FromArgb(255, 58, 58, 64),
            .Wheel = Color.FromArgb(255, 42, 42, 48),
            .Voice = "duck1"
        }

        Public ReadOnly Charcoal As New DuckVariant With {
            .Key = "charcoal",
            .Label = "Charcoal",
            .Shell = Color.FromArgb(255, 78, 80, 88),
            .Head = Color.FromArgb(255, 96, 98, 108),
            .Beak = Color.FromArgb(255, 245, 196, 81),
            .Joint = Color.FromArgb(255, 26, 26, 30),
            .Wheel = Color.FromArgb(255, 20, 20, 24),
            .Voice = "duck2"
        }

        Public ReadOnly Purple As New DuckVariant With {
            .Key = "purple",
            .Label = "Purple",
            .Shell = Color.FromArgb(255, 142, 112, 214),
            .Head = Color.FromArgb(255, 164, 136, 232),
            .Beak = Color.FromArgb(255, 124, 208, 255),
            .Joint = Color.FromArgb(255, 44, 36, 64),
            .Wheel = Color.FromArgb(255, 34, 28, 50),
            .Voice = "duck3"
        }

        Public ReadOnly Blue As New DuckVariant With {
            .Key = "blue",
            .Label = "Blue",
            .Shell = Color.FromArgb(255, 92, 156, 232),
            .Head = Color.FromArgb(255, 122, 180, 246),
            .Beak = Color.FromArgb(255, 255, 122, 47),
            .Joint = Color.FromArgb(255, 30, 40, 58),
            .Wheel = Color.FromArgb(255, 24, 32, 46),
            .Voice = "duck4"
        }

        Public ReadOnly All As DuckVariant() = {Classic, Charcoal, Purple, Blue}

        ''' <summary>
        ''' the colour of one mesh of the rig
        ''' </summary>
        ''' <remarks>
        ''' The document already carries a colour per geom (the colour of the CAD
        ''' material). The scheme replaces the semantic ones - the shells, the beak,
        ''' the joints - and keeps the neutral ones so that the duck does not turn
        ''' into a single flat blob of paint.
        ''' </remarks>
        Public Function ColorOf(scheme As DuckVariant, meshName As String, fallback As Color) As Color
            Dim name As String = IO.Path.GetFileNameWithoutExtension(meshName).ToLowerInvariant()

            If name.Contains("shell") OrElse name.Contains("trunk") OrElse name.Contains("yaw2roll") Then
                Return scheme.Shell
            End If

            If name.Contains("jaw") OrElse name.Contains("head") Then
                Return scheme.Head
            End If

            If name.Contains("sole") OrElse name.Contains("beak") Then
                Return scheme.Beak
            End If

            If name.Contains("tire") OrElse name.Contains("rim") OrElse name.Contains("wheel") Then
                Return scheme.Wheel
            End If

            If name.Contains("xl330") OrElse name.Contains("bearing") OrElse name.Contains("hip") OrElse
               name.Contains("leg") OrElse name.Contains("ankle") OrElse name.Contains("pcb") OrElse
               name.Contains("power") OrElse name.Contains("np_f970") Then
                Return scheme.Joint
            End If

            Return fallback
        End Function

        Public Function Find(key As String) As DuckVariant
            For Each scheme As DuckVariant In All
                If String.Equals(scheme.Key, key, StringComparison.OrdinalIgnoreCase) Then
                    Return scheme
                End If
            Next

            Return Classic
        End Function
    End Module
End Namespace
