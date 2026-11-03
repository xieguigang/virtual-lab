Imports System.IO
Imports MicroduckLab.Core

Namespace Assets

    ''' <summary>
    ''' The nine policy slots of the playground.
    ''' </summary>
    ''' <remarks>
    ''' The legs policies are loaded at boot, the two roller policies only when
    ''' the duck is switched to the rollers for the first time (they are dead
    ''' weight otherwise). Both stay resident afterwards, so toggling between the
    ''' two forms is instant - the same lazy loading as the browser build.
    ''' </remarks>
    Public Class PolicyLibrary : Implements IDisposable

        Public Property Walk As PolicySession
        Public Property SitStand As PolicySession
        Public Property Roll As PolicySession
        Public Property KickLeft As PolicySession
        Public Property KickRight As PolicySession
        Public Property GroundPick As PolicySession
        Public Property Stand As PolicySession

        Public Property Drive As PolicySession
        Public Property Crouch As PolicySession

        ''' <summary>the scale of the raw action, 1 for every official policy</summary>
        Public Property ActionScale As Double = SimConstants.ActionScale

        ''' <summary>true when the two roller policies are resident</summary>
        Public ReadOnly Property RollersLoaded As Boolean
            Get
                Return Drive IsNot Nothing AndAlso Crouch IsNot Nothing
            End Get
        End Property

        Public ReadOnly Property LoadedCount As Integer
            Get
                Dim count As Integer = 0

                For Each session As PolicySession In All()
                    If session IsNot Nothing Then
                        count += 1
                    End If
                Next

                Return count
            End Get
        End Property

        ''' <summary>
        ''' load the legs policies, the ones that the first frame of the game
        ''' needs
        ''' </summary>
        ''' <param name="trace">a callback that the boot console logs with</param>
        Public Sub LoadLegs(Optional trace As Action(Of String) = Nothing)
            Walk = Load("walk", SimConstants.PolicyWalk, trace)
            SitStand = Load("sitstand", SimConstants.PolicySitStand, trace)
            Roll = Load("roll", SimConstants.PolicyRoll, trace)
            KickLeft = Load("kickL", SimConstants.PolicyKickLeft, trace)
            KickRight = Load("kickR", SimConstants.PolicyKickRight, trace)
            GroundPick = Load("groundpick", SimConstants.PolicyGroundPick, trace)
            Stand = Load("stand", SimConstants.PolicyStand, trace)
        End Sub

        ''' <summary>
        ''' load the two roller policies, called on the first switch of the shape
        ''' </summary>
        Public Sub LoadRollers(Optional trace As Action(Of String) = Nothing)
            If RollersLoaded Then
                Return
            End If

            Drive = Load("drive", SimConstants.PolicyDrive, trace)
            Crouch = Load("crouch", SimConstants.PolicyCrouch, trace)
        End Sub

        Private Function Load(name As String, file As String, trace As Action(Of String)) As PolicySession
            Dim path As String = AssetPath.PolicyFile(file)
            Dim session As New PolicySession(name, path, SimConstants.ObsSize, SimConstants.NumJoints)

            Call session.Validate()

            If trace IsNot Nothing Then
                Call trace($"policy {name,-10} {New FileInfo(path).Length \ 1024,4} KiB  {session.Summary}")
            End If

            Return session
        End Function

        ''' <summary>
        ''' the policy of the given mode and shape
        ''' </summary>
        Public Function Active(mode As GameMode, loco As Locomotion, recovery As RecoveryState) As PolicySession
            If recovery = RecoveryState.Recovering Then
                Return Stand
            End If

            Select Case mode
                Case GameMode.SitStand
                    Return SitStand
                Case GameMode.Roll
                    Return Roll
                Case GameMode.KickLeft
                    Return KickLeft
                Case GameMode.KickRight
                    Return KickRight
                Case GameMode.GroundPick
                    Return GroundPick
                Case GameMode.Crouch
                    Return Crouch
                Case Else
                    Return If(loco = Locomotion.Rollers, Drive, Walk)
            End Select
        End Function

        Public Iterator Function All() As IEnumerable(Of PolicySession)
            Yield Walk
            Yield SitStand
            Yield Roll
            Yield KickLeft
            Yield KickRight
            Yield GroundPick
            Yield Stand
            Yield Drive
            Yield Crouch
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Walk = Nothing
            SitStand = Nothing
            Roll = Nothing
            KickLeft = Nothing
            KickRight = Nothing
            GroundPick = Nothing
            Stand = Nothing
            Drive = Nothing
            Crouch = Nothing
        End Sub
    End Class
End Namespace
