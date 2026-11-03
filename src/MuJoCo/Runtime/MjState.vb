''' <summary>
''' 状态打包与恢复，对应 <c>mj_stateSize</c> / <c>mj_getState</c> / <c>mj_setState</c>。
''' </summary>
''' <remarks>
''' 用途是把"一个完整的仿真状态"压成一个 <c>Double()</c>：
''' 存档、回滚、把同一初始状态喂给多条并行的推理链。
''' 位的语义与 <c>mjSTATE_*</c> 一一对应，组合常量
''' （<see cref="MjStateSig.mjSTATE_PHYSICS"/> 等）可以直接按位或。
''' </remarks>
Public Module MjState

    ''' <summary>某个状态组合需要的元素个数</summary>
    Public Function mj_stateSize(m As mjModel, sig As MjStateSig) As Integer
        Dim total As Integer = 0

        If (sig And MjStateSig.mjSTATE_TIME) <> 0 Then total += 1
        If (sig And MjStateSig.mjSTATE_QPOS) <> 0 Then total += m.nq
        If (sig And MjStateSig.mjSTATE_QVEL) <> 0 Then total += m.nv
        If (sig And MjStateSig.mjSTATE_ACT) <> 0 Then total += m.na
        If (sig And MjStateSig.mjSTATE_WARMSTART) <> 0 Then total += m.nv
        If (sig And MjStateSig.mjSTATE_CTRL) <> 0 Then total += m.nu
        If (sig And MjStateSig.mjSTATE_QFRC_APPLIED) <> 0 Then total += m.nv
        If (sig And MjStateSig.mjSTATE_XFRC_APPLIED) <> 0 Then total += m.nbody * 6
        If (sig And MjStateSig.mjSTATE_EQ_ACTIVE) <> 0 Then total += m.neq
        If (sig And MjStateSig.mjSTATE_MOCAP_POS) <> 0 Then total += m.nmocap * 3
        If (sig And MjStateSig.mjSTATE_MOCAP_QUAT) <> 0 Then total += m.nmocap * 4
        If (sig And MjStateSig.mjSTATE_USERDATA) <> 0 Then total += 0

        Return total
    End Function

    ''' <summary>把状态写进 <paramref name="state"/></summary>
    Public Sub mj_getState(m As mjModel, d As mjData, state As Double(), sig As MjStateSig)
        MjAssert.RequireSize(state, mj_stateSize(m, sig), NameOf(state))

        Dim at As Integer = 0

        If (sig And MjStateSig.mjSTATE_TIME) <> 0 Then
            state(at) = d.time
            at += 1
        End If

        at = CopyOut(d.qpos, state, at, m.nq, (sig And MjStateSig.mjSTATE_QPOS) <> 0)
        at = CopyOut(d.qvel, state, at, m.nv, (sig And MjStateSig.mjSTATE_QVEL) <> 0)
        at = CopyOut(d.act, state, at, m.na, (sig And MjStateSig.mjSTATE_ACT) <> 0)
        at = CopyOut(d.qacc, state, at, m.nv, (sig And MjStateSig.mjSTATE_WARMSTART) <> 0)
        at = CopyOut(d.ctrl, state, at, m.nu, (sig And MjStateSig.mjSTATE_CTRL) <> 0)
        at = CopyOut(d.qfrc_applied, state, at, m.nv, (sig And MjStateSig.mjSTATE_QFRC_APPLIED) <> 0)
        at = CopyOut(d.xfrc_applied, state, at, m.nbody * 6, (sig And MjStateSig.mjSTATE_XFRC_APPLIED) <> 0)

        If (sig And MjStateSig.mjSTATE_EQ_ACTIVE) <> 0 Then
            For i As Integer = 0 To m.neq - 1
                state(at) = If(d.eq_active(i), 1.0, 0.0)
                at += 1
            Next
        End If
    End Sub

    ''' <summary>从 <paramref name="state"/> 恢复状态</summary>
    Public Sub mj_setState(m As mjModel, d As mjData, state As Double(), sig As MjStateSig)
        MjAssert.RequireSize(state, mj_stateSize(m, sig), NameOf(state))

        Dim at As Integer = 0

        If (sig And MjStateSig.mjSTATE_TIME) <> 0 Then
            d.time = state(at)
            at += 1
        End If

        at = CopyIn(d.qpos, state, at, m.nq, (sig And MjStateSig.mjSTATE_QPOS) <> 0)
        at = CopyIn(d.qvel, state, at, m.nv, (sig And MjStateSig.mjSTATE_QVEL) <> 0)
        at = CopyIn(d.act, state, at, m.na, (sig And MjStateSig.mjSTATE_ACT) <> 0)
        at = CopyIn(d.qacc, state, at, m.nv, (sig And MjStateSig.mjSTATE_WARMSTART) <> 0)
        at = CopyIn(d.ctrl, state, at, m.nu, (sig And MjStateSig.mjSTATE_CTRL) <> 0)
        at = CopyIn(d.qfrc_applied, state, at, m.nv, (sig And MjStateSig.mjSTATE_QFRC_APPLIED) <> 0)
        at = CopyIn(d.xfrc_applied, state, at, m.nbody * 6, (sig And MjStateSig.mjSTATE_XFRC_APPLIED) <> 0)

        If (sig And MjStateSig.mjSTATE_EQ_ACTIVE) <> 0 Then
            For i As Integer = 0 To m.neq - 1
                d.eq_active(i) = state(at) <> 0
                at += 1
            Next
        End If

        Call MjKinematics.RunPosition(m, d)

        If d.Engine IsNot Nothing Then
            Call d.Engine.SyncToEngine()
            Call d.Engine.InvalidateContacts()
        End If
    End Sub

    Private Function CopyOut(source As Double(), target As Double(), at As Integer,
                             count As Integer, enabled As Boolean) As Integer
        If Not enabled OrElse source Is Nothing Then
            Return at
        End If

        Call Array.Copy(source, 0, target, at, count)

        Return at + count
    End Function

    Private Function CopyIn(target As Double(), source As Double(), at As Integer,
                            count As Integer, enabled As Boolean) As Integer
        If Not enabled OrElse target Is Nothing Then
            Return at
        End If

        Call Array.Copy(source, at, target, 0, count)

        Return at + count
    End Function

End Module
