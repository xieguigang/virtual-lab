Imports System.IO
Imports System.Media

Namespace Audio

    ''' <summary>
    ''' The sound effects of the playground, played with the wav player of the
    ''' framework.
    ''' </summary>
    ''' <remarks>
    ''' The browser build kept one audio context with three buses, a spatialiser
    ''' and a pile of oscillators. The desktop build keeps it simple, as asked: the
    ''' wav files of the assets are played by <see cref="SoundPlayer"/>, which is a
    ''' fire and forget player without a volume, a pan or a pitch. Sounds that the
    ''' browser synthesised (the sweep of the crt, the hum, the roller rumble) are
    ''' generated into a pcm wav in memory by <see cref="WavSynth"/> instead of
    ''' being dropped.
    '''
    ''' A pool of players is kept so that a burst of footsteps does not cut itself
    '''     off: a player is reused only after its sound had the time to finish.
    ''' </remarks>
    Public Class SoundBank : Implements IDisposable

        Private Const MaxPlayers As Integer = 12

        Private ReadOnly m_files As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly m_generated As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly m_active As New List(Of PlayerSlot)()
        Private ReadOnly m_lock As New Object()

        ''' <summary>turn every sound off</summary>
        Public Property Enabled As Boolean = True
        ''' <summary>how many wav files are cached</summary>
        Public ReadOnly Property LoadedCount As Integer
            Get
                Return m_files.Count
            End Get
        End Property

        Private Class PlayerSlot
            Public Player As SoundPlayer
            Public Expires As Long
        End Class

        ''' <summary>
        ''' read the wav files of a folder into memory, so that a footstep never
        ''' touches the disk
        ''' </summary>
        Public Sub Preload(folder As String, ParamArray patterns As String())
            If Not Directory.Exists(folder) Then
                Return
            End If

            For Each pattern As String In patterns
                For Each entry As String In Directory.GetFiles(folder, pattern, SearchOption.AllDirectories)
                    Dim key As String = Relative(folder, entry)

                    If m_files.ContainsKey(key) Then
                        Continue For
                    End If

                    Try
                        m_files(key) = File.ReadAllBytes(entry)
                    Catch ex As IOException
                        ' a missing sound is not worth a failed boot
                    End Try
                Next
            Next
        End Sub

        Private Shared Function Relative(folder As String, file As String) As String
            Dim path As String = file.Substring(folder.Length).TrimStart("\"c, "/"c)

            Return path.Replace("\"c, "/"c)
        End Function

        ''' <summary>
        ''' play one of the cached wav files, the name is relative to the folder
        ''' that was preloaded
        ''' </summary>
        Public Sub Play(name As String)
            If Not Enabled OrElse String.IsNullOrEmpty(name) Then
                Return
            End If

            Dim bytes As Byte() = Nothing

            If Not m_files.TryGetValue(name, bytes) Then
                If Not m_generated.TryGetValue(name, bytes) Then
                    Return
                End If
            End If

            Call PlayBytes(bytes, 0)
        End Sub

        ''' <summary>
        ''' play one of the generated sounds
        ''' </summary>
        Public Sub PlayGenerated(name As String)
            Dim bytes As Byte() = Nothing

            If m_generated.TryGetValue(name, bytes) Then
                Call PlayBytes(bytes, 0)
            End If
        End Sub

        ''' <summary>
        ''' register a pcm wav that was generated in memory
        ''' </summary>
        Public Sub RegisterGenerated(name As String, bytes As Byte())
            m_generated(name) = bytes
        End Sub

        Public ReadOnly Property HasGenerated As Boolean
            Get
                Return m_generated.Count > 0
            End Get
        End Property

        Private Sub PlayBytes(bytes As Byte(), lengthMs As Integer)
            If Not Enabled OrElse bytes Is Nothing OrElse bytes.Length = 0 Then
                Return
            End If

            SyncLock m_lock
                Dim now As Long = Environment.TickCount64
                Dim slot As PlayerSlot = Nothing

                For Each candidate As PlayerSlot In m_active
                    If candidate.Expires <= now Then
                        slot = candidate

                        Exit For
                    End If
                Next

                If slot Is Nothing Then
                    If m_active.Count >= MaxPlayers Then
                        slot = m_active(0)
                        Call m_active.RemoveAt(0)
                    Else
                        slot = New PlayerSlot()
                    End If
                End If

                Try
                    If slot.Player IsNot Nothing Then
                        slot.Player.Stop()
                        slot.Player.Dispose()
                    End If

                    slot.Player = New SoundPlayer(New MemoryStream(bytes, False))
                    slot.Player.Load()
                    slot.Player.Play()
                    slot.Expires = now + Math.Max(lengthMs, 260)
                Catch ex As Exception
                    ' a broken audio device must not stop the game
                    slot.Expires = now
                End Try

                Call m_active.Remove(slot)
                Call m_active.Add(slot)
            End SyncLock
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            SyncLock m_lock
                For Each slot As PlayerSlot In m_active
                    Try
                        slot.Player?.Stop()
                        slot.Player?.Dispose()
                    Catch ex As Exception
                    End Try
                Next

                Call m_active.Clear()
            End SyncLock
        End Sub
    End Class
End Namespace
