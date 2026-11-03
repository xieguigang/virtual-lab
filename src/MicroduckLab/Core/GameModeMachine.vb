Namespace Core

    ''' <summary>
    ''' The mode machine of the playground: the walking mode, the one shot gestures
    ''' and the fall recovery.
    ''' </summary>
    ''' <remarks>
    ''' The browser build drove the same machine with ``setTimeout`` (the 800 ms
    ''' sit delay and the 2000 ms stand hand back) and with a step counter. Both
    ''' are step counters here, which is the cadence the machine actually runs at:
    ''' the control loop never runs faster or slower than 50 Hz, so a delay in
    ''' frames is a delay in seconds.
    ''' </remarks>
    Public Class GameModeMachine

        ' the two hand overs of the sit gesture, in control steps (50 Hz)
        Public Const SitDelaySteps As Integer = 40
        Public Const StandDelaySteps As Integer = 100
        Public Const RollTippedGravity As Double = -0.3
        Public Const RollUprightGravity As Double = -0.85
        Public Const RollMinSteps As Integer = 40

        Public Property Mode As GameMode = GameMode.Walk
        Public Property Loco As Locomotion = Locomotion.Legs

        ''' <summary>0 = standing, 1 = sitting (the sitstand command word)</summary>
        Public Property SitFlag As Integer = 0
        ''' <summary>true while the head sticks steer the head</summary>
        Public Property HeadMode As Boolean = False
        Public Property InputLocked As Boolean = False
        Public Property PostKickLock As Integer = 0

        ''' <summary>the pending hand over of the sit gesture, in steps</summary>
        Public Property SitTimer As Integer = -1
        Public Property StandTimer As Integer = -1

        Public Property Recovery As RecoveryState = RecoveryState.None
        Public Property RecoverySteps As Integer = 0
        Public Property UprightSteps As Integer = 0
        Public Property FallDebounce As Integer = 0
        Public Property FallenFrames As Integer = -1

        ' the one shot runs, null when the mode is not playing one
        Public Property KickSteps As Integer = -1
        Public Property RollSteps As Integer = -1
        Public Property RollTipped As Boolean = False
        Public Property RollMaxSteps As Integer = SimConstants.RollMaxSteps
        Public Property CrouchPhase As Double = -1
        Public Property PickPhase As Double = -1

        Public ReadOnly Property IsKick As Boolean
            Get
                Return Mode = GameMode.KickLeft OrElse Mode = GameMode.KickRight
            End Get
        End Property

        Public ReadOnly Property IsSitting As Boolean
            Get
                Return Mode = GameMode.SitStand AndAlso SitFlag = 1
            End Get
        End Property

        Public ReadOnly Property IsBusy As Boolean
            Get
                Return (Mode = GameMode.Roll AndAlso RollSteps >= 0) OrElse
                       (IsKick AndAlso KickSteps >= 0) OrElse
                       (Mode = GameMode.Crouch AndAlso CrouchPhase >= 0) OrElse
                       (Mode = GameMode.GroundPick AndAlso PickPhase >= 0)
            End Get
        End Property

        ''' <summary>the label of the hud chip</summary>
        Public ReadOnly Property Label As String
            Get
                If Recovery <> RecoveryState.None Then
                    Return "Recovery"
                End If

                Select Case Mode
                    Case GameMode.Roll
                        Return "Roll"
                    Case GameMode.Crouch
                        Return "Crouch"
                    Case GameMode.GroundPick
                        Return "Pick"
                    Case GameMode.KickLeft, GameMode.KickRight
                        Return "Kick"
                End Select

                If HeadMode Then
                    Return "Head"
                End If

                If IsSitting Then
                    Return "Sit"
                End If

                Return If(Loco = Locomotion.Rollers, "Drive", "Run")
            End Get
        End Property

        ''' <summary>
        ''' the twist that the duck may follow: zero while a one shot runs, while
        ''' the recovery owns it or while the entrance holds the inputs
        ''' </summary>
        Public Function AllowsTwist() As Boolean
            If InputLocked OrElse Recovery <> RecoveryState.None Then
                Return False
            End If

            If Mode <> GameMode.Walk Then
                Return False
            End If

            Return Not IsBusy
        End Function

        Public Sub SetMode(target As GameMode, Optional force As Boolean = False)
            If Not force AndAlso InputLocked Then
                Return
            End If

            If Recovery <> RecoveryState.None Then
                Return
            End If

            If IsBusy Then
                Return
            End If

            ' the two hand overs of the sit gesture
            If SitTimer >= 0 OrElse StandTimer >= 0 Then
                Return
            End If

            If target = GameMode.SitStand AndAlso Loco = Locomotion.Rollers Then
                Return
            End If

            HeadMode = False
            KickSteps = -1
            RollSteps = -1
            CrouchPhase = -1
            PickPhase = -1

            If target <> GameMode.SitStand Then
                If Mode = GameMode.SitStand AndAlso SitFlag = 1 Then
                    ' stand the duck up first, then hand over
                    SitFlag = 0
                    StandTimer = StandDelaySteps

                    Return
                End If

                Mode = target
            Else
                ' hold the stand under the sitstand policy for a moment before
                ' commanding the sit, or the abrupt switch knocks the duck over
                Mode = GameMode.SitStand
                SitFlag = 0
                SitTimer = SitDelaySteps
            End If
        End Sub

        Public Sub ToggleSit()
            If Mode <> GameMode.Walk AndAlso Mode <> GameMode.SitStand Then
                Return
            End If

            If IsSitting Then
                Call SetMode(GameMode.Walk)
            Else
                Call SetMode(GameMode.SitStand)
            End If
        End Sub

        Public Function TriggerRoll() As Boolean
            If Loco = Locomotion.Rollers Then
                Return TriggerCrouch()
            End If

            If InputLocked OrElse Mode <> GameMode.Walk OrElse StandTimer >= 0 OrElse Recovery <> RecoveryState.None Then
                Return False
            End If

            HeadMode = False
            Mode = GameMode.Roll
            SitFlag = 0
            RollSteps = 0
            RollTipped = False
            RollMaxSteps = SimConstants.RollMaxSteps

            Return True
        End Function

        Public Function TriggerCrouch() As Boolean
            If InputLocked OrElse Mode <> GameMode.Walk OrElse Recovery <> RecoveryState.None Then
                Return False
            End If

            HeadMode = False
            Mode = GameMode.Crouch
            CrouchPhase = 0

            Return True
        End Function

        Public Function TriggerGroundPick() As Boolean
            If Loco <> Locomotion.Legs Then
                Return False
            End If

            If InputLocked OrElse Mode <> GameMode.Walk OrElse StandTimer >= 0 OrElse Recovery <> RecoveryState.None Then
                Return False
            End If

            HeadMode = False
            Mode = GameMode.GroundPick
            SitFlag = 0
            PickPhase = 0

            Return True
        End Function

        Public Function TriggerKick(left As Boolean) As Boolean
            If Loco = Locomotion.Rollers Then
                Return False
            End If

            If InputLocked OrElse Mode <> GameMode.Walk OrElse StandTimer >= 0 OrElse Recovery <> RecoveryState.None Then
                Return False
            End If

            HeadMode = False
            Mode = If(left, GameMode.KickLeft, GameMode.KickRight)
            SitFlag = 0
            KickSteps = 0

            Return True
        End Function

        Public Sub ToggleHead()
            If HeadMode Then
                HeadMode = False

                Return
            End If

            If InputLocked OrElse (Mode <> GameMode.Walk AndAlso Mode <> GameMode.SitStand) Then
                Return
            End If

            If PostKickLock > 0 OrElse StandTimer >= 0 OrElse Recovery <> RecoveryState.None Then
                Return
            End If

            HeadMode = True
        End Sub

        ''' <summary>
        ''' reset the machine to the walking mode of a fresh world
        ''' </summary>
        Public Sub Reset()
            Mode = GameMode.Walk
            SitFlag = 0
            HeadMode = False
            PostKickLock = 0
            SitTimer = -1
            StandTimer = -1
            Recovery = RecoveryState.None
            RecoverySteps = 0
            UprightSteps = 0
            FallDebounce = 0
            FallenFrames = -1
            KickSteps = -1
            RollSteps = -1
            RollTipped = False
            CrouchPhase = -1
            PickPhase = -1
        End Sub

        ''' <summary>
        ''' advance the step counters of the machine by one control step
        ''' </summary>
        ''' <param name="gravityZ">the projected gravity of the trunk, index 5 of the observation</param>
        ''' <returns>true when the duck has to be reset</returns>
        Public Function Advance(gravityZ As Double) As Boolean
            ' the sit hand overs
            If SitTimer >= 0 Then
                SitTimer -= 1

                If SitTimer < 0 Then
                    If Mode = GameMode.SitStand Then
                        SitFlag = 1
                    End If
                End If
            End If

            If StandTimer >= 0 Then
                StandTimer -= 1

                If StandTimer < 0 Then
                    Mode = GameMode.Walk
                End If
            End If

            If PostKickLock > 0 AndAlso Mode = GameMode.Walk Then
                PostKickLock -= 1
            End If

            ' the one shot windows
            If IsKick AndAlso KickSteps >= 0 Then
                KickSteps += 1

                If KickSteps >= SimConstants.KickSteps Then
                    KickSteps = -1
                    Mode = GameMode.Walk
                    PostKickLock = SimConstants.PostKickLockSteps
                End If
            End If

            If Mode = GameMode.Crouch AndAlso CrouchPhase >= 0 Then
                CrouchPhase += SimConstants.CtrlDt / SimConstants.CrouchPeriodS

                If CrouchPhase >= SimConstants.CrouchEndPhase Then
                    CrouchPhase = -1
                    Mode = GameMode.Walk
                End If
            End If

            If Mode = GameMode.GroundPick AndAlso PickPhase >= 0 Then
                PickPhase += SimConstants.CtrlDt / SimConstants.GroundPickPeriodS

                If PickPhase >= SimConstants.GroundPickEndPhase Then
                    PickPhase = -1
                    Mode = GameMode.Walk
                End If
            End If

            If Mode = GameMode.Roll AndAlso RollSteps >= 0 Then
                RollSteps += 1

                If gravityZ > RollTippedGravity Then
                    RollTipped = True
                End If

                Dim upright As Boolean = gravityZ < RollUprightGravity
                Dim done As Boolean = RollTipped AndAlso upright AndAlso RollSteps >= RollMinSteps
                Dim expired As Boolean = RollSteps >= RollMaxSteps

                If done OrElse expired Then
                    RollSteps = -1
                    Mode = GameMode.Walk

                    If Not upright Then
                        Return True
                    End If
                End If
            End If

            Return False
        End Function

        ''' <summary>
        ''' the recovery state machine: a settled wait, then the stand policy, then
        ''' a hysteresis exit
        ''' </summary>
        ''' <returns>true when the duck has to be reset</returns>
        Public Function AdvanceRecovery(gravityZ As Double) As Boolean
            If Recovery = RecoveryState.None Then
                Return False
            End If

            RecoverySteps += 1

            If Recovery = RecoveryState.Settle Then
                If RecoverySteps >= SimConstants.FallSettleSteps Then
                    Recovery = RecoveryState.Recovering
                    RecoverySteps = 0
                    UprightSteps = 0
                End If

                Return False
            End If

            UprightSteps = If(gravityZ < RollUprightGravity, UprightSteps + 1, 0)

            If UprightSteps >= SimConstants.RecoverUprightSteps Then
                Recovery = RecoveryState.None
                Mode = GameMode.Walk
                RecoverySteps = 0

                Return False
            End If

            Return RecoverySteps >= SimConstants.RecoverGiveUpSteps
        End Function

        ''' <summary>
        ''' the mode allows a fall to be recovered by the stand policy
        ''' </summary>
        Public ReadOnly Property CanRecover As Boolean
            Get
                Return Loco = Locomotion.Legs AndAlso Mode = GameMode.Walk AndAlso
                       Not InputLocked AndAlso PostKickLock = 0 AndAlso StandTimer < 0 AndAlso
                       Recovery = RecoveryState.None
            End Get
        End Property
    End Class
End Namespace
