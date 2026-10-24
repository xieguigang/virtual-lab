Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model

Namespace Simulation

    ''' <summary>运动类型。</summary>
    Public Enum MotionKind
        ''' <summary>关节空间插补（movej）。</summary>
        Joint = 0
        ''' <summary>笛卡尔直线（movel）。</summary>
        Linear = 1
        ''' <summary>笛卡尔点位（move_to）。</summary>
        Pose = 2
    End Enum

    ''' <summary>一条运动指令的执行状态。</summary>
    Public Class MotionCommand

        Public Property Id As Integer
        Public Property Kind As MotionKind
        Public Property TargetQ As Double()
        Public Property StartTime As Double
        Public Property Duration As Double
        Public Property Started As Boolean
        Public Property Finished As Boolean
        Public Property Failed As Boolean
        Public Property Message As String

        ''' <summary>规划出来的关节空间路点（每段一个关节角向量）。</summary>
        Public Property Waypoints As List(Of Double())

        Public ReadOnly Property IsActive As Boolean
            Get
                Return Started AndAlso Not Finished AndAlso Not Failed
            End Get
        End Property

        Public ReadOnly Property IsDone As Boolean
            Get
                Return Finished OrElse Failed
            End Get
        End Property

    End Class

    ''' <summary>
    ''' 虚拟硬件的语义层：伺服使能、急停、运动队列、夹爪力控、场景操作、状态发布。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 这是"虚拟硬件"对外的行为边界：协议层（<c>CommandDispatcher</c>）只调用这里的方法，
    ''' GUI 也只读 <see cref="State"/>，因此 TCP / HTTP / 界面看到的是同一套语义。
    ''' </para>
    ''' <para>
    ''' 运动是"预规划 + 时间插值"：<c>movej</c> 直接线性插值关节角，<c>movel</c> 先把直线
    ''' 分解成若干点、每点解一次数值 IK 得到路点，再按时间插值。这样上位机的
    ''' <c>wait</c> 语义只依赖仿真时间，不受渲染帧率影响。
    ''' </para>
    ''' </remarks>
    Public Class ArmHardware

        Public ReadOnly Property Simulation As ArmSimulation

        ''' <summary>最新状态快照（每 Tick 刷新）。</summary>
        Public Property State As StateSnapshot

        ''' <summary>当前正在执行的运动（没有则为 Nothing）。</summary>
        Public ReadOnly Property ActiveMotion As MotionCommand
            Get
                Return _active
            End Get
        End Property

        ''' <summary>最近一次失败/错误信息。</summary>
        Public Property LastError As String

        Private _active As MotionCommand
        Private _motionCounter As Integer
        Private ReadOnly _history As New Dictionary(Of Integer, MotionCommand)()

        Public Sub New(simulation As ArmSimulation)
            If simulation Is Nothing Then Throw New ArgumentNullException(NameOf(simulation))

            Me.Simulation = simulation
            Me.State = simulation.CaptureState()
        End Sub

        ' /********************************************************************************/
        '  推进
        ' /********************************************************************************/

        ''' <summary>推进一个物理步：先套用运动插值，再步进物理，最后发布新快照。</summary>
        Public Sub Tick()
            Call ApplyMotion()
            Call Simulation.StepOnce()
            Me.State = Simulation.CaptureState()
        End Sub

        Private Sub ApplyMotion()
            If _active Is Nothing Then Return

            Dim now As Double = Simulation.Time

            If Not _active.Started Then
                _active.Started = True
                _active.StartTime = now
            End If

            Dim elapsed As Double = now - _active.StartTime

            If _active.Duration <= 0.0 Then
                _active.Duration = 0.001
            End If

            Dim progress As Double = elapsed / _active.Duration

            If progress > 1.0 Then progress = 1.0

            ' 余弦缓动（smoothstep）：起停阶段的速度与加速度都连续，
            ' 否则线性插值会在起步/停止瞬间给腕部一个冲击
            Dim eased As Double = progress * progress * (3.0 - 2.0 * progress)
            Dim q As Double() = InterpolateAt(_active, eased)

            If q IsNot Nothing Then
                Call Simulation.SetJointTargets(q)
            End If

            If progress >= 1.0 Then
                Dim gapError As Double = MaxJointError(_active.TargetQ)

                _active.Finished = True

                If gapError > 0.25 Then
                    _active.Failed = True
                    _active.Message = $"到位误差偏大 ({gapError:F3} rad)，可能被碰撞阻挡"
                Else
                    _active.Message = "ok"
                End If

                _active = Nothing
            End If
        End Sub

        Private Function InterpolateAt(command As MotionCommand, progress As Double) As Double()
            Dim points As List(Of Double()) = command.Waypoints

            If points Is Nothing OrElse points.Count = 0 Then Return command.TargetQ
            If points.Count = 1 Then Return points(0)

            Dim span As Double = (points.Count - 1) * progress
            Dim index As Integer = CInt(System.Math.Floor(span))

            If index >= points.Count - 1 Then Return points(points.Count - 1)
            If index < 0 Then index = 0

            Dim frac As Double = span - index
            Dim a As Double() = points(index)
            Dim b As Double() = points(index + 1)
            Dim out(a.Length - 1) As Double

            For i As Integer = 0 To a.Length - 1
                out(i) = a(i) + (b(i) - a(i)) * frac
            Next

            Return out
        End Function

        Private Function MaxJointError(target As Double()) As Double
            Dim current As Double() = Simulation.JointPositions()
            Dim max As Double = 0.0

            For i As Integer = 0 To System.Math.Min(current.Length, target.Length) - 1
                max = System.Math.Max(max, System.Math.Abs(current(i) - target(i)))
            Next

            Return max
        End Function

        ' /********************************************************************************/
        '  运动指令
        ' /********************************************************************************/

        ''' <summary>关节空间运动（movej）。</summary>
        ''' <param name="q">目标关节角（弧度，长度 = 关节数）</param>
        ''' <param name="speed">速度倍率（0.01 ~ 1）</param>
        Public Function MoveJ(q As Double(), Optional speed As Double = 0.35) As MotionCommand
            If State IsNot Nothing AndAlso State.Estop Then
                Return FailedMotion(MotionKind.Joint, "急停中，需要先 reset")
            End If

            Dim count As Integer = Simulation.JointDefs.Count

            If q Is Nothing OrElse q.Length <> count Then
                Return FailedMotion(MotionKind.Joint, $"目标关节角数量应为 {count}")
            End If

            Dim target(count - 1) As Double

            For i As Integer = 0 To count - 1
                Dim limit As ArmJointDef = Simulation.JointDefs(i)

                If q(i) < limit.Lower - 0.001 OrElse q(i) > limit.Upper + 0.001 Then
                    Return FailedMotion(MotionKind.Joint,
                        $"关节 {limit.Name} 目标 {q(i):F3} 超出限位 [{limit.Lower:F3}, {limit.Upper:F3}]")
                End If

                target(i) = System.Math.Max(limit.Lower, System.Math.Min(limit.Upper, q(i)))
            Next

            Dim current As Double() = Simulation.JointPositions()
            Dim duration As Double = EstimateDuration(current, target, speed)

            Dim waypoints As New List(Of Double()) From {CType(current.Clone(), Double()), target}

            Return StartMotion(New MotionCommand With {
                .Kind = MotionKind.Joint,
                .TargetQ = target,
                .Waypoints = waypoints,
                .Duration = duration
            })
        End Function

        ''' <summary>笛卡尔直线运动（movel）：沿直线插补并用数值 IK 求解路点。</summary>
        Public Function MoveL(x As Double, y As Double, z As Double,
                              Optional speed As Double = 0.25) As MotionCommand

            Dim start As Pose3D = Simulation.Assembly.TcpPose(Simulation.JointPositions())
            Dim goal As New Pose3D(New Vector3(x, y, z), start.Rotation)

            Return MoveTo(goal, speed)
        End Function

        ''' <summary>笛卡尔点位运动：直线插补到目标位姿。</summary>
        Public Function MoveTo(targetPose As Pose3D, Optional speed As Double = 0.25) As MotionCommand
            If State IsNot Nothing AndAlso State.Estop Then
                Return FailedMotion(MotionKind.Pose, "急停中，需要先 reset")
            End If

            Dim startQ As Double() = Simulation.JointPositions()
            Dim start As Pose3D = Simulation.Assembly.TcpPose(startQ)
            Dim distance As Double = Vector3Math.Length(targetPose.Position - start.Position)

            Dim steps As Integer = System.Math.Max(2, CInt(System.Math.Ceiling(distance / 0.01)))
            steps = System.Math.Min(steps, 200)

            Dim waypoints As New List(Of Double())()
            Dim seed As Double() = CType(startQ.Clone(), Double())

            waypoints.Add(CType(startQ.Clone(), Double()))

            For i As Integer = 1 To steps
                Dim t As Double = i / steps
                Dim position As Vector3 = start.Position + (targetPose.Position - start.Position) * t
                Dim rotation As Quaternion = Quaternion.Slerp(start.Rotation, targetPose.Rotation, t)

                Dim goal As New Pose3D(position, rotation)

                ' 先按"位置 + 姿态"求解；六轴臂在保持末端水平的情况下往往到不了桌面高度，
                ' 这时退化为只约束位置（姿态随动），否则 moveL 会被姿态约束卡死
                Dim ik As IkResult = Kinematics.Solve(
                    Simulation.Assembly, goal, seed,
                    positionOnly:=False, maxIterations:=80, tolerance:=0.001)

                If Not ik.Success Then
                    ik = Kinematics.Solve(
                        Simulation.Assembly, goal, seed,
                        positionOnly:=True, maxIterations:=80, tolerance:=0.003)
                End If

                If Not ik.Success Then
                    Return FailedMotion(MotionKind.Pose, $"逆解失败: {ik.Message}（误差 {ik.TotalError:F3}）")
                End If

                seed = ik.Q
                waypoints.Add(ik.Q)
            Next

            Dim targetQ As Double() = waypoints(waypoints.Count - 1)
            Dim duration As Double = EstimateDuration(startQ, targetQ, speed)

            Return StartMotion(New MotionCommand With {
                .Kind = MotionKind.Pose,
                .TargetQ = targetQ,
                .Waypoints = waypoints,
                .Duration = System.Math.Max(duration, 0.35)
            })
        End Function

        ''' <summary>回零（所有关节角 = 0）。</summary>
        Public Function Home(Optional speed As Double = 0.3) As MotionCommand
            Return MoveJ(New Double(Simulation.JointDefs.Count - 1) {}, speed)
        End Function

        ''' <summary>停止当前运动并保持当前位置。</summary>
        Public Sub StopMotion()
            If _active IsNot Nothing Then
                _active.Finished = True
                _active.Message = "stopped"
                _active = Nothing
            End If

            Call Simulation.HoldCurrent()
        End Sub

        Private Function StartMotion(command As MotionCommand) As MotionCommand
            If _active IsNot Nothing Then
                _active.Finished = True
                _active.Message = "superseded"
            End If

            _motionCounter += 1
            command.Id = _motionCounter
            command.StartTime = Simulation.Time
            command.Started = False

            _history(command.Id) = command
            _active = command

            Return command
        End Function

        Private Function FailedMotion(kind As MotionKind, message As String) As MotionCommand
            _motionCounter += 1
            LastError = message

            Dim command As New MotionCommand With {
                .Id = _motionCounter,
                .Kind = kind,
                .Failed = True,
                .Finished = True,
                .Message = message,
                .Duration = 0.0
            }

            Return command
        End Function

        ''' <summary>查询运动是否结束。</summary>
        Public Function IsMotionDone(id As Integer) As Boolean
            If _active IsNot Nothing AndAlso _active.Id = id Then Return False

            Dim command As MotionCommand = Nothing

            If _history.TryGetValue(id, command) Then Return command.IsDone

            Return True
        End Function

        ''' <summary>运动是否成功完成。</summary>
        Public Function MotionSucceeded(id As Integer) As Boolean
            Dim command As MotionCommand = Nothing

            If _history.TryGetValue(id, command) Then Return command.Finished AndAlso Not command.Failed

            Return False
        End Function

        ''' <summary>查询运动状态文本。</summary>
        Public Function MotionMessage(id As Integer) As String
            Dim command As MotionCommand = Nothing

            If _history.TryGetValue(id, command) Then Return command.Message

            Return "unknown_motion"
        End Function

        Private Function EstimateDuration(fromQ As Double(), toQ As Double(), speed As Double) As Double
            Dim factor As Double = System.Math.Max(0.02, System.Math.Min(1.0, speed))
            Dim maxDelta As Double = 0.0
            Dim maxSpeed As Double = 1.0

            For i As Integer = 0 To System.Math.Min(fromQ.Length, toQ.Length) - 1
                maxDelta = System.Math.Max(maxDelta, System.Math.Abs(toQ(i) - fromQ(i)))

                If i < Simulation.JointDefs.Count Then
                    maxSpeed = System.Math.Min(maxSpeed, Simulation.JointDefs(i).Velocity)
                End If
            Next

            maxSpeed = System.Math.Max(0.1, maxSpeed * factor)

            Return System.Math.Max(0.15, maxDelta / maxSpeed)
        End Function

        ' /********************************************************************************/
        '  硬件语义
        ' /********************************************************************************/

        Public Sub SetEnabled(value As Boolean)
            Call Simulation.SetServoEnabled(value)
            Me.State = Simulation.CaptureState()
        End Sub

        Public Function EmergencyStop() As StateSnapshot
            Call Simulation.EmergencyStop()

            If _active IsNot Nothing Then
                _active.Failed = True
                _active.Message = "estop"
                _active = Nothing
            End If

            Me.State = Simulation.CaptureState()
            Return Me.State
        End Function

        ''' <summary>复位整个仿真（重新构建世界）。</summary>
        Public Function ResetSimulation() As StateSnapshot
            _active = Nothing
            Call _history.Clear()

            Call Simulation.Reset()
            Call Simulation.SetServoEnabled(True)

            Me.State = Simulation.CaptureState()
            Return Me.State
        End Function

        Public Sub Grip(force As Double)
            Call Simulation.Grip(force)
        End Sub

        Public Sub Release()
            Call Simulation.ReleaseGripper()
        End Sub

        Public Function Spawn(shape As String, size As Double(), position As Vector3,
                              Optional rotation As Quaternion? = Nothing,
                              Optional mass As Double = 0.2,
                              Optional friction As Double = 0.75,
                              Optional color As String = Nothing) As BlockInstance
            Dim instance As BlockInstance = Simulation.SpawnBlock(shape, size, position, rotation, mass, friction, color)

            Me.State = Simulation.CaptureState()

            Return instance
        End Function

        Public Function RemoveObject(id As String) As Boolean
            Dim ok As Boolean = Simulation.RemoveObject(id)

            Me.State = Simulation.CaptureState()

            Return ok
        End Function

        Public Sub ClearObjects()
            Call Simulation.ClearObjects()
            Me.State = Simulation.CaptureState()
        End Sub

    End Class

End Namespace
