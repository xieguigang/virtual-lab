Imports System.Globalization
Imports System.IO
Imports System.Numerics
Imports System.Text

Namespace Core

    ''' <summary>
    ''' Assembles the 61 dimensional observation of the policies.
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' The layout is the one of ``microduck_rl/scripts/infer_policy.py`` and of
    ''' ``game.js``:
    ''' </para>
    ''' <code>
    '''  0.. 2  the angular velocity of the trunk, in the trunk frame
    '''  3.. 5  the projected gravity, in the trunk frame (index 5 is z)
    '''  6..19  the joint angles, measured from the stand pose
    ''' 20..33  the joint speeds
    ''' 34..47  the raw action of the previous step (not scaled)
    ''' 48..60  the 13 words of the command
    ''' </code>
    ''' <para>
    ''' Every number comes straight out of <c>mjData</c>: the gyro reading is the
    ''' body angular velocity rotated into the trunk frame, the projected gravity is
    ''' one rotation away from the trunk orientation, and the joints are read at the
    ''' <c>qpos</c>/<c>qvel</c> addresses resolved once at startup.
    ''' </para>
    ''' <para>
    ''' <see cref="DumpEnabled"/> 打开后，每个控制周期会把**送进网络的观测**和
    ''' **网络吐出的动作**一起追加到 CSV。策略行为异常时，这是唯一能把
    ''' "看到的" 与 "发出的" 分开看的办法：如果观测已经越界，问题在仿真；
    ''' 观测正常而动作顶在 ±1，问题在策略与仿真的匹配。
    ''' </para>
    ''' </remarks>
    Public Class ObservationBuilder : Implements IDisposable

        Private ReadOnly m_world As MjWorld
        Private ReadOnly m_observation As Double() = New Double(SimConstants.ObsSize - 1) {}

        Private m_dumpEnabled As Boolean = False
        Private m_dump As StreamWriter
        Private m_dumpRows As Integer
        Private m_dumpError As String

        Public Sub New(world As MjWorld)
            m_world = world
        End Sub

        ''' <summary>是否把每个控制周期的观测与动作写进 CSV</summary>
        Public Property DumpEnabled As Boolean
            Get
                Return m_dumpEnabled
            End Get
            Set(value As Boolean)
                If value = m_dumpEnabled Then
                    Return
                End If

                m_dumpEnabled = value
                m_dumpError = Nothing

                If value Then
                    ' 立刻打开，成败在按下热键的那一刻就能看到
                    Call OpenDump()
                Else
                    ' 关掉时立刻落盘并释放句柄，不然文件会一直被占用到进程退出
                    Call CloseDump()
                End If
            End Set
        End Property

        ''' <summary>CSV 的路径；<c>Nothing</c> 时写到输出目录下的 <c>observation-dump.csv</c></summary>
        Public Property DumpPath As String = Nothing

        ''' <summary>本次开关以来已经写出的行数</summary>
        Public ReadOnly Property DumpRows As Integer
            Get
                Return m_dumpRows
            End Get
        End Property

        ''' <summary>打开或写入失败时的原因；正常时为 <c>Nothing</c></summary>
        Public ReadOnly Property DumpError As String
            Get
                Return m_dumpError
            End Get
        End Property

        ''' <summary>实际使用的 CSV 路径</summary>
        Public ReadOnly Property ResolvedDumpPath As String
            Get
                If Not String.IsNullOrEmpty(DumpPath) Then
                    Return DumpPath
                End If

                Return Path.Combine(AppContext.BaseDirectory, "observation-dump.csv")
            End Get
        End Property

        ''' <summary>
        ''' fill and return the observation buffer (the same array is reused, copy
        ''' it when it has to outlive the call)
        ''' </summary>
        ''' <param name="lastAction">the raw action of the previous control step</param>
        ''' <param name="command">the thirteen words of the command</param>
        Public Function Build(lastAction As Double(), command As Double()) As Double()
            Dim rotation As Quaternion = m_world.TrunkRotation
            Dim inverse As Quaternion = Quaternion.Conjugate(rotation)
            Dim angular As Vector3 = Vector3.Transform(m_world.TrunkAngularVelocity, inverse)
            Dim gravity As Vector3 = Vector3.Transform(New Vector3(0, 0, -1), inverse)

            m_observation(0) = angular.X
            m_observation(1) = angular.Y
            m_observation(2) = angular.Z
            m_observation(3) = gravity.X
            m_observation(4) = gravity.Y
            m_observation(5) = gravity.Z

            For j As Integer = 0 To SimConstants.NumJoints - 1
                m_observation(6 + j) = m_world.JointAngle(j) - SimConstants.DefaultPose(j)
                m_observation(20 + j) = Phys.Clamp(m_world.JointSpeed(j),
                                                   -SimConstants.ObservedJointSpeed,
                                                   SimConstants.ObservedJointSpeed)
                m_observation(34 + j) = If(lastAction Is Nothing OrElse j >= lastAction.Length, 0, lastAction(j))
            Next

            For c As Integer = 0 To SimConstants.CmdSize - 1
                m_observation(48 + c) = If(command Is Nothing OrElse c >= command.Length, 0, command(c))
            Next

            Return m_observation
        End Function

        ' /********************************************************************************/
        '  观测日志
        ' /********************************************************************************/

        ''' <summary>
        ''' 把一个控制周期追加到日志：本步送进网络的观测 + 网络给出的原始动作。
        ''' </summary>
        ''' <param name="action">本步的原始动作（未乘 ActionScale）</param>
        ''' <remarks>
        ''' 必须在 <see cref="Build"/> 之后、下一次 <see cref="Build"/> 之前调用，
        ''' 因为观测缓冲是复用的。
        ''' </remarks>
        Public Sub Record(action As Double())
            If Not m_dumpEnabled Then
                Return
            End If

            ' 打开失败（目录只读等）时静默跳过，绝不能让日志把控制环带崩
            If m_dump Is Nothing Then
                Return
            End If

            Dim line As New StringBuilder(1024)

            ' 时间是仿真时间，不是墙钟：两者对不上时说明实时性已经崩了
            Call line.Append(m_world.Data.time.ToString("F5", CultureInfo.InvariantCulture))

            For i As Integer = 0 To SimConstants.ObsSize - 1
                Call line.Append(",").Append(m_observation(i).ToString("R", CultureInfo.InvariantCulture))
            Next

            For j As Integer = 0 To SimConstants.NumJoints - 1
                Dim value As Double = If(action Is Nothing OrElse j >= action.Length, 0.0, action(j))

                Call line.Append(",").Append(value.ToString("R", CultureInfo.InvariantCulture))
            Next

            Call m_dump.WriteLine(line.ToString())

            m_dumpRows += 1

            ' 每 128 行落一次盘：宁可丢最后几十行，也不让 WriteLine 成为控制环的瓶颈
            If m_dumpRows Mod 128 = 0 Then
                Call m_dump.Flush()
            End If
        End Sub

        Private Sub OpenDump()
            If m_dump IsNot Nothing Then
                Return
            End If

            Try
                Dim target As String = ResolvedDumpPath
                Dim folder As String = Path.GetDirectoryName(Path.GetFullPath(target))

                If Not String.IsNullOrEmpty(folder) AndAlso Not Directory.Exists(folder) Then
                    Call Directory.CreateDirectory(folder)
                End If

                ' 表头是手写的列名，行数是常量算出来的；两者对不上时
                ' pandas 会把整张表读错位，宁可当场报出来
                Dim header As String = BuildHeader()
                Dim expected As Integer = 1 + SimConstants.ObsSize + SimConstants.NumJoints
                Dim actual As Integer = header.Split(","c).Length

                If actual <> expected Then
                    m_dumpError = $"header has {actual} columns, expected {expected}"

                    Return
                End If

                m_dump = New StreamWriter(target, False, New UTF8Encoding(False))
                m_dumpRows = 0

                Call m_dump.WriteLine(header)
            Catch ex As Exception
                m_dumpError = $"{ResolvedDumpPath}: {ex.Message}"

                If m_dump IsNot Nothing Then
                    Call m_dump.Dispose()
                    m_dump = Nothing
                End If
            End Try
        End Sub

        ''' <summary>CSV 表头：每一列都带名字，pandas 里直接 <c>df["q_left_knee"]</c></summary>
        Private Function BuildHeader() As String
            Dim columns As New List(Of String)(SimConstants.ObsSize + SimConstants.NumJoints + 1) From {
                "time",
                "ang_x", "ang_y", "ang_z",
                "grav_x", "grav_y", "grav_z"
            }

            For j As Integer = 0 To SimConstants.NumJoints - 1
                Call columns.Add($"q_{SimConstants.JointNames(j)}")
            Next

            For j As Integer = 0 To SimConstants.NumJoints - 1
                Call columns.Add($"dq_{SimConstants.JointNames(j)}")
            Next

            For j As Integer = 0 To SimConstants.NumJoints - 1
                Call columns.Add($"prev_{SimConstants.JointNames(j)}")
            Next

            For c As Integer = 0 To SimConstants.CmdSize - 1
                Call columns.Add($"cmd{c}")
            Next

            For j As Integer = 0 To SimConstants.NumJoints - 1
                Call columns.Add($"act_{SimConstants.JointNames(j)}")
            Next

            Return String.Join(",", columns)
        End Function

        Private Sub CloseDump()
            If m_dump Is Nothing Then
                Return
            End If

            Try
                Call m_dump.Flush()
                Call m_dump.Dispose()
            Catch ex As Exception
                ' 关不掉也只能放弃这次会话，不能往上抛
                m_dumpError = ex.Message
            Finally
                m_dump = Nothing
            End Try
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            m_dumpEnabled = False

            Call CloseDump()
        End Sub

    End Class
End Namespace
