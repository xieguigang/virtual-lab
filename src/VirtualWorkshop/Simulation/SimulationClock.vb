Namespace Simulation

    ''' <summary>
    ''' 把"真实经过的时间"换算成"应该推进多少个固定物理步"。
    ''' </summary>
    ''' <remarks>
    ''' GUI 的定时器与 headless 的循环都用它驱动同一份内核：
    ''' <c>RealtimeFactor = 1</c> 为实时，<c>10</c> 表示 10 倍速；
    ''' <c>0</c> 表示暂停（只渲染不推进）。
    ''' </remarks>
    Public Class SimulationClock

        ''' <summary>仿真速度倍率（1 = 实时）。</summary>
        Public Property RealtimeFactor As Double = 1.0

        ''' <summary>单次调用最多推进的物理步数（防止卡顿后的"死亡螺旋"）。</summary>
        Public Property MaxStepsPerTick As Integer = 12

        ''' <summary>累计的"欠账"时间（秒）。</summary>
        Public Property Pending As Double

        Public ReadOnly Property StepCount As Long
            Get
                Return _steps
            End Get
        End Property

        Public ReadOnly Property LastBatch As Integer
            Get
                Return _lastBatch
            End Get
        End Property

        Private _steps As Long
        Private _lastBatch As Integer

        ''' <summary>
        ''' 消费 <paramref name="realSeconds"/> 的真实时间，返回本轮应推进的物理步数。
        ''' </summary>
        ''' <param name="realSeconds">真实经过的时间（秒）</param>
        ''' <param name="stepSeconds">一个物理步的时间（秒）</param>
        Public Function Consume(realSeconds As Double, stepSeconds As Double) As Integer
            If stepSeconds <= 0.0 OrElse RealtimeFactor <= 0.0 Then
                _lastBatch = 0
                Return 0
            End If

            If realSeconds < 0.0 Then realSeconds = 0.0

            Pending += realSeconds * RealtimeFactor

            Dim steps As Integer = CInt(System.Math.Floor(Pending / stepSeconds))

            If steps > MaxStepsPerTick Then
                steps = MaxStepsPerTick
                Pending = 0.0
            Else
                Pending -= steps * stepSeconds
            End If

            If steps < 0 Then steps = 0

            _steps += steps
            _lastBatch = steps

            Return steps
        End Function

        Public Sub Reset()
            Pending = 0.0
            _steps = 0
            _lastBatch = 0
        End Sub

    End Class

End Namespace
