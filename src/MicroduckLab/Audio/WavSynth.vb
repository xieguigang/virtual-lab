Imports System.IO
Imports System.Text

Namespace Audio

    ''' <summary>
    ''' Generates the sounds of the browser build that were never a file: the sweep
    ''' of the crt, the hum of the arcade and the rumble of the rollers.
    ''' </summary>
    ''' <remarks>
    ''' The output is a complete 16 bit mono wav in memory, so the very same
    ''' <see cref="SoundBank"/> that plays the recorded footsteps plays these as
    ''' well and no second audio path is needed.
    ''' </remarks>
    Public Module WavSynth

        Public Const SampleRate As Integer = 22050

        ''' <summary>
        ''' a sine tone that may glide from one frequency to another
        ''' </summary>
        ''' <param name="fromHz">the frequency at the start</param>
        ''' <param name="toHz">the frequency at the end</param>
        ''' <param name="milliseconds">the length of the sound</param>
        ''' <param name="volume">the amplitude in [0,1]</param>
        Public Function Sweep(fromHz As Double, toHz As Double, milliseconds As Integer,
                              Optional volume As Double = 0.25, Optional decay As Boolean = True) As Byte()
            Dim count As Integer = Math.Max(1, SampleRate * milliseconds \ 1000)
            Dim samples(count - 1) As Short
            Dim phase As Double = 0

            For i As Integer = 0 To count - 1
                Dim t As Double = i / CDbl(count)
                Dim frequency As Double = fromHz + (toHz - fromHz) * t
                Dim envelope As Double = If(decay, (1 - t) ^ 1.5, 1.0)

                phase += 2 * Math.PI * frequency / SampleRate

                samples(i) = CShort(Math.Round(Math.Sin(phase) * envelope * volume * Short.MaxValue))
            Next

            Return Pack(samples)
        End Function

        ''' <summary>
        ''' a short noise burst with a low pass, the thud of a bump
        ''' </summary>
        Public Function Thump(milliseconds As Integer, Optional volume As Double = 0.3,
                              Optional cutoff As Double = 0.12) As Byte()
            Dim count As Integer = Math.Max(1, SampleRate * milliseconds \ 1000)
            Dim samples(count - 1) As Short
            Dim random As New Random(7)
            Dim low As Double = 0

            For i As Integer = 0 To count - 1
                Dim t As Double = i / CDbl(count)
                Dim white As Double = random.NextDouble() * 2 - 1

                low += (white - low) * cutoff
                samples(i) = CShort(Math.Round(low * (1 - t) ^ 2 * volume * Short.MaxValue))
            Next

            Return Pack(samples)
        End Function

        ''' <summary>
        ''' a low rumble that can be looped, the rollers on the floor
        ''' </summary>
        Public Function Rumble(milliseconds As Integer, Optional volume As Double = 0.18) As Byte()
            Dim count As Integer = Math.Max(1, SampleRate * milliseconds \ 1000)
            Dim samples(count - 1) As Short
            Dim random As New Random(11)
            Dim low As Double = 0

            For i As Integer = 0 To count - 1
                Dim white As Double = random.NextDouble() * 2 - 1

                low += (white - low) * 0.05
                samples(i) = CShort(Math.Round(low * volume * Short.MaxValue))
            Next

            Return Pack(samples)
        End Function

        Private Function Pack(samples As Short()) As Byte()
            Using stream As New MemoryStream()
                Using writer As New BinaryWriter(stream, Encoding.ASCII, True)
                    Dim dataBytes As Integer = samples.Length * 2

                    Call writer.Write(Encoding.ASCII.GetBytes("RIFF"))
                    Call writer.Write(36 + dataBytes)
                    Call writer.Write(Encoding.ASCII.GetBytes("WAVE"))
                    Call writer.Write(Encoding.ASCII.GetBytes("fmt "))
                    Call writer.Write(16)
                    Call writer.Write(CShort(1))
                    Call writer.Write(CShort(1))
                    Call writer.Write(SampleRate)
                    Call writer.Write(SampleRate * 2)
                    Call writer.Write(CShort(2))
                    Call writer.Write(CShort(16))
                    Call writer.Write(Encoding.ASCII.GetBytes("data"))
                    Call writer.Write(dataBytes)

                    For Each sample As Short In samples
                        Call writer.Write(sample)
                    Next
                End Using

                Return stream.ToArray()
            End Using
        End Function
    End Module
End Namespace

Namespace Audio

    ''' <summary>
    ''' Maps the events of the simulation onto the sounds of the assets.
    ''' </summary>
    Public Class AudioDirector : Implements IDisposable

        Private ReadOnly m_bank As SoundBank
        Private ReadOnly m_random As New Random(4242)
        Private ReadOnly m_steps As String() = {"sfx/step_a.wav", "sfx/step_b.wav", "sfx/step_c.wav", "sfx/step_d.wav", "sfx/step_e.wav"}
        Private ReadOnly m_thumps As String() = {"sfx/thump_a.wav", "sfx/thump_b.wav", "sfx/thump_c.wav"}
        Private ReadOnly m_clicks As String() = {"sfx/click_a.wav", "sfx/click_b.wav"}

        Public Property Enabled As Boolean = True

        Public Sub New(bank As SoundBank)
            m_bank = bank
        End Sub

        ''' <summary>
        ''' register the generated sounds and the recorded voices of the four
        ''' ducks
        ''' </summary>
        Public Sub Prepare()
            Call m_bank.RegisterGenerated("synth/sweep.wav", WavSynth.Sweep(1800, 220, 900, 0.22))
            Call m_bank.RegisterGenerated("synth/hum.wav", WavSynth.Thump(1200, 0.12, 0.02))
            Call m_bank.RegisterGenerated("synth/rumble.wav", WavSynth.Rumble(900, 0.16))
            Call m_bank.RegisterGenerated("synth/boot.wav", WavSynth.Sweep(120, 2400, 420, 0.18, False))
        End Sub

        Public Sub Footstep(left As Boolean, strength As Single)
            If Not Enabled Then
                Return
            End If

            Call m_bank.Play(m_steps(m_random.Next(m_steps.Length)))
        End Sub

        Public Sub Impact(strength As Single)
            If Not Enabled Then
                Return
            End If

            Call m_bank.Play(m_thumps(m_random.Next(m_thumps.Length)))
        End Sub

        Public Sub Bump(strength As Single)
            If Not Enabled Then
                Return
            End If

            Call m_bank.Play(m_thumps(m_random.Next(m_thumps.Length)))
        End Sub

        Public Sub Click()
            If Not Enabled Then
                Return
            End If

            Call m_bank.Play(m_clicks(m_random.Next(m_clicks.Length)))
        End Sub

        ''' <summary>
        ''' one chirp of the voice library of the given colour
        ''' </summary>
        Public Sub Quack(voice As String)
            If Not Enabled Then
                Return
            End If

            Dim name As String = $"voices/{voice}/chirp_{(ChrW(97 + m_random.Next(12)))}.wav"

            Call m_bank.Play(name)
        End Sub

        ''' <summary>
        ''' one chirp of a voice library by index, for the soundboard
        ''' </summary>
        Public Sub Chirp(voice As String, index As Integer)
            Dim name As String = $"voices/{voice}/chirp_{(ChrW(97 + (index Mod 12)))}.wav"

            Call m_bank.Play(name)
        End Sub

        Public Sub Boot()
            Call m_bank.PlayGenerated("synth/boot.wav")
        End Sub

        Public Sub Sweep()
            Call m_bank.PlayGenerated("synth/sweep.wav")
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
        End Sub
    End Class
End Namespace
