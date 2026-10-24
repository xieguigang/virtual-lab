Imports System.Text.Json.Nodes
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model
Imports VirtualWorkshop.Simulation

Namespace Protocol

    ''' <summary>带错误码的协议异常。</summary>
    Public Class ProtocolException
        Inherits Exception

        Public ReadOnly Property Code As String

        Sub New(code As String, message As String)
            MyBase.New(message)
            Me.Code = code
        End Sub
    End Class

    ''' <summary>
    ''' 所有虚拟硬件指令的唯一实现：TCP 与 HTTP 只是两个传输适配器。
    ''' </summary>
    ''' <remarks>
    ''' 所有指令都在**仿真线程**上执行（由 <see cref="SimulationHost"/> 从队列里取出），
    ''' 因此这里可以直接读写物理世界，不需要加锁。
    ''' </remarks>
    Public Class CommandDispatcher

        Public ReadOnly Property Simulation As ArmSimulation
        Public ReadOnly Property Hardware As ArmHardware

        ''' <summary>仿真速度倍率（0 表示暂停）。</summary>
        Public Property RealtimeFactor As Double = 1.0

        ''' <summary>模型名称（写入 hello 响应）。</summary>
        Public Property ModelName As String

        ''' <summary>TCP 端口（仅用于 info）。</summary>
        Public Property TcpPort As Integer
        Public Property HttpPort As Integer

        ''' <summary>累计指令计数（诊断用）。</summary>
        Public Property CommandCount As Long

        Public Sub New(simulation As ArmSimulation, hardware As ArmHardware)
            Me.Simulation = simulation
            Me.Hardware = hardware
        End Sub

        ''' <summary>执行一条指令，返回 result 节点；失败抛 <see cref="ProtocolException"/>。</summary>
        Public Function Execute(command As String, args As JsonObject) As JsonObject
            If args Is Nothing Then args = New JsonObject()

            CommandCount += 1

            Select Case If(command, "").Trim().ToLowerInvariant()
                Case ProtocolText.CmdHello : Return Hello()
                Case ProtocolText.CmdPing : Return Ping()
                Case ProtocolText.CmdInfo : Return Info()
                Case ProtocolText.CmdState : Return StateResult()
                Case ProtocolText.CmdObs : Return Observation()
                Case ProtocolText.CmdEnable : Return Enable(args)
                Case ProtocolText.CmdHome : Return Home(args)
                Case ProtocolText.CmdMoveJ : Return MoveJ(args)
                Case ProtocolText.CmdMoveJRel : Return MoveJRelative(args)
                Case ProtocolText.CmdMoveL : Return MoveLinear(args)
                Case ProtocolText.CmdMoveTo : Return MoveTo(args)
                Case ProtocolText.CmdServo : Return Servo(args)
                Case ProtocolText.CmdStop : Return StopMotion()
                Case ProtocolText.CmdEstop : Return EmergencyStop()
                Case ProtocolText.CmdReset : Return ResetSimulation()
                Case ProtocolText.CmdGrip : Return Grip(args)
                Case ProtocolText.CmdRelease : Return Release()
                Case ProtocolText.CmdSpawn : Return Spawn(args)
                Case ProtocolText.CmdRemove : Return Remove(args)
                Case ProtocolText.CmdClear : Return Clear()
                Case ProtocolText.CmdObjects : Return Objects()
                Case ProtocolText.CmdConfig : Return Config(args)
                Case Else
                    Throw New ProtocolException(ProtocolText.ErrUnknownCommand, "未知指令: " & command)
            End Select
        End Function

        ' /********************************************************************************/
        '  基础
        ' /********************************************************************************/

        Private Function Hello() As JsonObject
            Return New JsonObject From {
                {ProtocolText.FieldOk, True},
                {ProtocolText.FieldVersion, ProtocolText.Version},
                {ProtocolText.FieldModel, If(ModelName, "six_axis_arm")},
                {ProtocolText.FieldJoints, Simulation.JointDefs.Count},
                {ProtocolText.FieldSimMode, SimModeName()}
            }
        End Function

        Private Function Ping() As JsonObject
            Return New JsonObject From {
                {"pong", True},
                {"time", Simulation.Time},
                {"step", Simulation.StepCount}
            }
        End Function

        Private Function SimModeName() As String
            Return If(Simulation.Options.KinematicArm, "kinematic", "physics")
        End Function

        Private Function Info() As JsonObject
            Dim payload As New JsonObject From {
                {"model", If(ModelName, "")},
                {"version", ProtocolText.Version},
                {ProtocolText.FieldSimMode, SimModeName()},
                {"joints", Simulation.JointDefs.Count},
                {"bodies", Simulation.World.Bodies.Count},
                {"objects", Simulation.Objects.Count},
                {"triangles", If(Simulation.Assembly.Meshes Is Nothing, 0, Simulation.Assembly.Meshes.TriangleCount)},
                {"tcpPort", TcpPort},
                {"httpPort", HttpPort},
                {"commands", CommandCount},
                {"realtime", RealtimeFactor}
            }

            Dim links As New JsonArray()

            For Each pair As KeyValuePair(Of String, RigidBody3D) In Simulation.LinkBodies
                links.Add(pair.Key)
            Next

            payload("links") = links

            Return payload
        End Function

        Private Function StateResult() As JsonObject
            Return JsonText.NodeOf(Hardware.State)
        End Function

        ''' <summary>供神经网络使用的扁平观测向量。</summary>
        Private Function Observation() As JsonObject
            Dim state As StateSnapshot = Hardware.State
            Dim values As New List(Of Double)()

            If state IsNot Nothing Then
                If state.Q IsNot Nothing Then values.AddRange(state.Q)
                If state.Qd IsNot Nothing Then values.AddRange(state.Qd)
                If state.Tcp IsNot Nothing Then values.AddRange(state.Tcp)
                If state.Gripper IsNot Nothing Then
                    values.Add(state.Gripper.Opening)
                    values.Add(state.Gripper.Force)
                End If

                values.Add(If(state.Estop, 1.0, 0.0))
                values.Add(state.MaxObjectSpeed)
            End If

            Return New JsonObject From {
                {"obs", JsonText.ArrayOf(values.ToArray())},
                {"size", values.Count},
                {"time", If(state Is Nothing, 0.0, state.Time)}
            }
        End Function

        Private Function Enable(args As JsonObject) As JsonObject
            Dim enableFlag As Boolean = JsonText.Bool(args, "on", True)

            Call Hardware.SetEnabled(enableFlag)

            Return New JsonObject From {{"enabled", enableFlag}}
        End Function

        ' /********************************************************************************/
        '  运动
        ' /********************************************************************************/

        Private Function MotionResult(command As MotionCommand) As JsonObject
            If command Is Nothing Then
                Throw New ProtocolException(ProtocolText.ErrInternal, "运动指令没有返回结果")
            End If

            If command.Failed Then
                Throw New ProtocolException(ProtocolText.ErrJointLimit, command.Message)
            End If

            Return New JsonObject From {
                {ProtocolText.FieldAccepted, True},
                {ProtocolText.FieldMotionId, command.Id},
                {"duration", command.Duration},
                {"kind", command.Kind.ToString().ToLowerInvariant()}
            }
        End Function

        Private Function Home(args As JsonObject) As JsonObject
            Dim speed As Double = JsonText.Num(args, "speed", 0.3)

            Return MotionResult(Hardware.Home(speed))
        End Function

        Private Function MoveJ(args As JsonObject) As JsonObject
            Dim q As Double() = JsonText.Numbers(args, "q")

            If q Is Nothing OrElse q.Length = 0 Then
                Throw New ProtocolException(ProtocolText.ErrBadParams, "缺少关节角数组 q")
            End If

            If q.Length <> Simulation.JointDefs.Count Then
                Throw New ProtocolException(ProtocolText.ErrBadParams,
                    $"关节角数量应为 {Simulation.JointDefs.Count}，实际 {q.Length}")
            End If

            Return MotionResult(Hardware.MoveJ(q, JsonText.Num(args, "speed", 0.35)))
        End Function

        Private Function MoveJRelative(args As JsonObject) As JsonObject
            Dim delta As Double() = JsonText.Numbers(args, "dq")

            If delta Is Nothing OrElse delta.Length = 0 Then
                Throw New ProtocolException(ProtocolText.ErrBadParams, "缺少关节增量数组 dq")
            End If

            Dim current As Double() = Simulation.JointPositions()
            Dim q(current.Length - 1) As Double

            For i As Integer = 0 To current.Length - 1
                q(i) = current(i) + If(i < delta.Length, delta(i), 0.0)
            Next

            Return MotionResult(Hardware.MoveJ(q, JsonText.Num(args, "speed", 0.35)))
        End Function

        Private Function MoveLinear(args As JsonObject) As JsonObject
            Dim position As Double() = JsonText.Numbers(args, "position")

            Dim x As Double
            Dim y As Double
            Dim z As Double

            If position IsNot Nothing AndAlso position.Length >= 3 Then
                x = position(0)
                y = position(1)
                z = position(2)
            Else
                x = JsonText.Num(args, "x")
                y = JsonText.Num(args, "y")
                z = JsonText.Num(args, "z")
            End If

            Return MotionResult(Hardware.MoveL(x, y, z, JsonText.Num(args, "speed", 0.25)))
        End Function

        Private Function MoveTo(args As JsonObject) As JsonObject
            Dim poseNode As JsonObject = JsonText.ObjectOf(args, "pose")

            If poseNode Is Nothing Then
                Throw New ProtocolException(ProtocolText.ErrBadParams, "缺少 pose 对象")
            End If

            Dim position As New Vector3(
                JsonText.Num(poseNode, "x"),
                JsonText.Num(poseNode, "y"),
                JsonText.Num(poseNode, "z"))

            Dim rotation As Quaternion

            If poseNode.ContainsKey("qw") Then
                rotation = New Quaternion(
                    JsonText.Num(poseNode, "qw", 1.0),
                    JsonText.Num(poseNode, "qx"),
                    JsonText.Num(poseNode, "qy"),
                    JsonText.Num(poseNode, "qz")).Normalize()
            Else
                Dim rpy As New Vector3(
                    JsonText.Num(poseNode, "rx") * System.Math.PI / 180.0,
                    JsonText.Num(poseNode, "ry") * System.Math.PI / 180.0,
                    JsonText.Num(poseNode, "rz") * System.Math.PI / 180.0)

                rotation = ArmAssembly.RpyQuaternion(rpy)
            End If

            Return MotionResult(Hardware.MoveTo(New Pose3D(position, rotation), JsonText.Num(args, "speed", 0.25)))
        End Function

        ''' <summary>实时伺服：直接把关节目标设为给定角（无插补）。</summary>
        Private Function Servo(args As JsonObject) As JsonObject
            Dim q As Double() = JsonText.Numbers(args, "q")

            If q Is Nothing OrElse q.Length = 0 Then
                Throw New ProtocolException(ProtocolText.ErrBadParams, "缺少关节角数组 q")
            End If

            Call Simulation.SetJointTargets(q)

            Return New JsonObject From {{ProtocolText.FieldAccepted, True}}
        End Function

        Private Function StopMotion() As JsonObject
            Call Hardware.StopMotion()

            Return New JsonObject From {{"stopped", True}}
        End Function

        Private Function EmergencyStop() As JsonObject
            Call Simulation.EmergencyStop()
            Call Hardware.EmergencyStop()

            Return New JsonObject From {{"estop", True}}
        End Function

        Private Function ResetSimulation() As JsonObject
            Call Hardware.ResetSimulation()

            Return New JsonObject From {{"reset", True}, {"objects", Simulation.Objects.Count}}
        End Function

        ' /********************************************************************************/
        '  夹爪
        ' /********************************************************************************/

        Private Function Grip(args As JsonObject) As JsonObject
            Dim force As Double = JsonText.Num(args, "force", 30.0)

            Call Hardware.Grip(force)

            Return New JsonObject From {
                {"force", force},
                {"opening", Simulation.GripperOpening}
            }
        End Function

        Private Function Release() As JsonObject
            Call Hardware.Release()

            Return New JsonObject From {{"released", True}, {"opening", Simulation.GripperOpening}}
        End Function

        ' /********************************************************************************/
        '  场景
        ' /********************************************************************************/

        Private Function Spawn(args As JsonObject) As JsonObject
            Dim shape As String = JsonText.Str(args, "shape", "box")
            Dim size As Double() = JsonText.Numbers(args, "size")
            Dim position As Double() = JsonText.Numbers(args, "position")
            Dim rotation As Double() = JsonText.Numbers(args, "rotation")

            Dim location As Vector3 = Vector3.Zero

            If position IsNot Nothing AndAlso position.Length >= 3 Then
                location = New Vector3(position(0), position(1), position(2))
            Else
                location = Nothing
            End If

            Dim placement As Quaternion? = Nothing

            If rotation IsNot Nothing AndAlso rotation.Length >= 4 Then
                placement = New Quaternion(rotation(0), rotation(1), rotation(2), rotation(3)).Normalize()
            End If

            Dim instance As BlockInstance = Hardware.Spawn(
                shape, size, location, placement,
                JsonText.Num(args, "mass", 0.2),
                JsonText.Num(args, "friction", 0.75),
                JsonText.Str(args, "color"))

            Return New JsonObject From {
                {"id", instance.Id},
                {"kind", BlockFactory.ShapeName(instance.Spec.Shape)},
                {"position", JsonText.ArrayOf(StateSnapshot.ToArray(instance.Body.Position))},
                {"size", JsonText.ArrayOf(StateSnapshot.ToArray(instance.Size))}
            }
        End Function

        Private Function Remove(args As JsonObject) As JsonObject
            Dim id As String = JsonText.Str(args, "id")

            If String.IsNullOrEmpty(id) Then
                Throw New ProtocolException(ProtocolText.ErrBadParams, "缺少物体 id")
            End If

            If Not Hardware.RemoveObject(id) Then
                Throw New ProtocolException(ProtocolText.ErrNotFound, "找不到物体: " & id)
            End If

            Return New JsonObject From {{"removed", id}}
        End Function

        Private Function Clear() As JsonObject
            Call Hardware.ClearObjects()

            Return New JsonObject From {{"cleared", True}}
        End Function

        Private Function Objects() As JsonObject
            Dim state As StateSnapshot = Hardware.State
            Dim items As New JsonArray()

            If state IsNot Nothing AndAlso state.Objects IsNot Nothing Then
                For Each item As ObjectState In state.Objects
                    items.Add(JsonText.NodeOf(item))
                Next
            End If

            Return New JsonObject From {{"objects", items}, {"count", items.Count}}
        End Function

        ' /********************************************************************************/
        '  配置
        ' /********************************************************************************/

        Private Function Config(args As JsonObject) As JsonObject
            Dim key As String = JsonText.Str(args, "key")

            If String.IsNullOrEmpty(key) Then
                Throw New ProtocolException(ProtocolText.ErrBadParams, "缺少配置项 key")
            End If

            Select Case key.Trim().ToLowerInvariant()
                Case "realtime"
                    RealtimeFactor = System.Math.Max(0.0, JsonText.Num(args, "value", 1.0))
                    Return New JsonObject From {{"realtime", RealtimeFactor}}

                Case "gravity"
                    Dim enable As Boolean = JsonText.Bool(args, "value", True)
                    Dim g As Double = JsonText.Num(args, "g", 9.81)

                    Simulation.World.Gravity = If(enable, New Vector3(0, 0, -g), Vector3.Zero)
                    Return New JsonObject From {{"gravity", enable}, {"g", g}}

                Case "friction"
                    Dim friction As Double = JsonText.Num(args, "value", 0.75)

                    For Each item As BlockInstance In Simulation.Objects
                        item.Body.Material.Friction = friction
                    Next

                    Return New JsonObject From {{"friction", friction}}

                Case "clear_velocity"
                    Call Simulation.World.ClearVelocities()
                    Return New JsonObject From {{"cleared", True}}

                Case Else
                    Throw New ProtocolException(ProtocolText.ErrBadParams, "未知配置项: " & key)
            End Select
        End Function

    End Class

End Namespace
