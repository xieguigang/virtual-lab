Imports System.Runtime.InteropServices

Namespace Inputs

    ''' <summary>
    ''' The state of one xinput controller, polled once per frame.
    ''' </summary>
    Public Structure PadState
        Public Connected As Boolean
        Public Forward As Double
        Public Turn As Double
        Public CameraX As Double
        Public CameraY As Double
        Public TriggerRight As Double
        Public TriggerLeft As Double
        Public Buttons As UInteger
    End Structure

    ''' <summary>
    ''' A very small wrapper around the xinput api of windows: the sticks, the two
    ''' triggers and the button bit mask of one controller.
    ''' </summary>
    ''' <remarks>
    ''' The browser build read the same device through the gamepad api of the
    ''' browser, so the mapping of the buttons is the one of the real microduck
    '''     runtime and is re-applied by the window.
    ''' </remarks>
    Public Module XInputPad

        Private Const PadA As UInteger = &H1000
        Private Const PadB As UInteger = &H2000
        Private Const PadX As UInteger = &H4000
        Private Const PadY As UInteger = &H8000
        Private Const PadLeftShoulder As UInteger = &H100
        Private Const PadRightShoulder As UInteger = &H200
        Private Const PadBack As UInteger = &H20
        Private Const PadStart As UInteger = &H10
        Private Const PadDpadUp As UInteger = &H1
        Private Const PadDpadDown As UInteger = &H2
        Private Const PadDpadLeft As UInteger = &H4
        Private Const PadDpadRight As UInteger = &H8

        <StructLayout(LayoutKind.Sequential)>
        Private Structure XInputState
            Public PacketNumber As UInteger
            Public Gamepad As XInputGamepad
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure XInputGamepad
            Public Buttons As UShort
            Public LeftTrigger As Byte
            Public RightTrigger As Byte
            Public ThumbLX As Short
            Public ThumbLY As Short
            Public ThumbRX As Short
            Public ThumbRY As Short
        End Structure

        <DllImport("xinput1_4.dll", EntryPoint:="XInputGetState")>
        Private Function XInputGetState14(index As UInteger, ByRef state As XInputState) As UInteger
        End Function

        <DllImport("xinput9_1_0.dll", EntryPoint:="XInputGetState")>
        Private Function XInputGetState9(index As UInteger, ByRef state As XInputState) As UInteger
        End Function

        Private m_way As Integer = 0

        ''' <summary>
        ''' pad 0 or 1, nothing of them is connected
        ''' </summary>
        Public Function Poll(index As Integer) As PadState
            Dim state As New XInputState()
            Dim code As UInteger

            Try
                code = If(m_way = 0, XInputGetState14(CUInt(index), state), XInputGetState9(CUInt(index), state))
            Catch ex As DllNotFoundException
                m_way = 1

                Try
                    code = XInputGetState9(CUInt(index), state)
                Catch ex2 As DllNotFoundException
                    Return New PadState()
                End Try
            End Try

            If code <> 0 Then
                Return New PadState()
            End If

            Dim result As New PadState With {.Connected = True, .Buttons = CUInt(state.Gamepad.Buttons)}

            result.Forward = DeadZone(state.Gamepad.ThumbLY / 32767.0)
            result.Turn = DeadZone(state.Gamepad.ThumbLX / 32767.0)
            result.CameraX = DeadZone(state.Gamepad.ThumbRX / 32767.0)
            result.CameraY = DeadZone(state.Gamepad.ThumbRY / 32767.0)
            result.TriggerRight = state.Gamepad.RightTrigger / 255.0
            result.TriggerLeft = state.Gamepad.LeftTrigger / 255.0

            Return result
        End Function

        Private Function DeadZone(value As Double) As Double
            Const zone As Double = 0.16

            If Math.Abs(value) < zone Then
                Return 0
            End If

            Return (value - Math.Sign(value) * zone) / (1 - zone)
        End Function

        Public Function Has(state As PadState, button As Integer) As Boolean
            ' the bit of the button in the xinput mask
            Dim mask As UInteger = CUInt(1) << button

            Return (state.Buttons And mask) <> 0
        End Function

        Public ReadOnly Property ButtonA As UInteger
            Get
                Return PadA
            End Get
        End Property

        Public ReadOnly Property ButtonB As UInteger
            Get
                Return PadB
            End Get
        End Property

        Public ReadOnly Property ButtonX As UInteger
            Get
                Return PadX
            End Get
        End Property

        Public ReadOnly Property ButtonY As UInteger
            Get
                Return PadY
            End Get
        End Property

        Public ReadOnly Property ButtonLeftShoulder As UInteger
            Get
                Return PadLeftShoulder
            End Get
        End Property

        Public ReadOnly Property ButtonRightShoulder As UInteger
            Get
                Return PadRightShoulder
            End Get
        End Property

        Public ReadOnly Property ButtonStart As UInteger
            Get
                Return PadStart
            End Get
        End Property

        Public ReadOnly Property ButtonBack As UInteger
            Get
                Return PadBack
            End Get
        End Property

        Public ReadOnly Property ButtonDpadUp As UInteger
            Get
                Return PadDpadUp
            End Get
        End Property

        Public ReadOnly Property ButtonDpadDown As UInteger
            Get
                Return PadDpadDown
            End Get
        End Property
    End Module
End Namespace
