Imports System.Text.Json.Nodes
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model
Imports VirtualWorkshop.Sensing
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

        ''' <summary>夹爪第一视角相机（未启用时为 Nothing）。</summary>
        Public Property Camera As GripperCamera

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
                Case ProtocolText.CmdPlatform : Return Platform(args)
                Case ProtocolText.CmdFrame : Return Frame(args)
                Case ProtocolText.CmdCamera : Return CameraControl(args)
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
                Dim code As String = If(String.IsNullOrEmpty(command.FailureCode),
                                        ProtocolText.ErrJointLimit, command.FailureCode)

                Throw New ProtocolException(code, command.Message)
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

            ' 实时伺服直接下发关节角，同样要先过一遍地面检查
            Dim violation As String = Simulation.GroundViolation(q)

            If violation IsNot Nothing Then
                Throw New ProtocolException(ProtocolText.ErrGroundViolation,
                    $"目标位姿会让 {violation} 低于地面（{Simulation.GroundZ + Simulation.GroundClearance:F3} m），已拒绝")
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

                    ' /********************************************************************************/
                    '  弹簧木板平台
                    ' /********************************************************************************/

                    ''' <summary>
                    ''' 弹簧木板平台：不带参数时查询，<c>{"remove":true}</c> 拆除，否则创建/重建。
                    ''' </summary>
                    ''' <remarks>
                    ''' 参数：<c>size</c>（[宽, 深, 厚]）、<c>position</c>（板中心世界坐标）、
                    ''' <c>mass</c>、<c>friction</c>、<c>stiffness</c>（N/m）、<c>damping</c>（N·s/m）、
                    ''' <c>rest</c>（弹簧自然长度）、<c>springs</c>（4 或 1）。
                    ''' </remarks>
                    Private Function Platform(args As JsonObject) As JsonObject
                        If args Is Nothing OrElse args.Count = 0 Then Return PlatformInfo()

                        If JsonText.Bool(args, "remove", False) Then
                            Return New JsonObject From {{"removed", Simulation.RemovePlatform()}}
                        End If

                        Dim width As Double = JsonText.Num(args, "width", 0.30)
                        Dim depth As Double = JsonText.Num(args, "depth", 0.30)
                        Dim thickness As Double = JsonText.Num(args, "thickness", 0.03)

                        Dim size As Double() = JsonText.Numbers(args, "size")

                        If size IsNot Nothing AndAlso size.Length >= 3 Then
                            width = size(0)
                            depth = size(1)
                            thickness = size(2)
                        End If

                        Dim x As Double = JsonText.Num(args, "x", 0.36)
                        Dim y As Double = JsonText.Num(args, "y", 0.0)
                        Dim z As Double = JsonText.Num(args, "z", 0.16)

                        Dim position As Double() = JsonText.Numbers(args, "position")

                        If position IsNot Nothing AndAlso position.Length >= 3 Then
                            x = position(0)
                            y = position(1)
                            z = position(2)
                        End If

                        Dim stiffness As Double = JsonText.Num(args, "stiffness", 200.0)
                        Dim damping As Double = JsonText.Num(args, "damping", 30.0)
                        Dim rest As Double = JsonText.Num(args, "rest", System.Math.Max(0.02, z - thickness * 0.5))
                        Dim springs As Integer = CInt(JsonText.Num(args, "springs", 4))
                        Dim mass As Double = JsonText.Num(args, "mass", 0.6)
                        Dim friction As Double = JsonText.Num(args, "friction", 0.6)

                        If width < 0.05 OrElse depth < 0.05 OrElse thickness < 0.004 Then
                            Throw New ProtocolException(ProtocolText.ErrBadParams, "木板尺寸太小")
                        End If

                        If stiffness <= 0.0 Then
                            Throw New ProtocolException(ProtocolText.ErrBadParams, "stiffness 必须大于 0")
                        End If

                        Dim plate As SpringPlatform = Simulation.CreatePlatform(
                            New Vector3(width, depth, thickness),
                            New Vector3(x, y, z),
                            mass, friction, stiffness, damping, rest, springs)

                        If plate Is Nothing Then
                            Throw New ProtocolException(ProtocolText.ErrInternal, "创建平台失败")
                        End If

                        Return PlatformInfo()
                    End Function

                    ''' <summary>平台信息：木板 id、倾角、各弹簧压缩量与弹力。</summary>
                    Private Function PlatformInfo() As JsonObject
                        Dim info As StateSnapshot.PlatformState = Simulation.CapturePlatformState()

                        If info Is Nothing OrElse Not info.Present Then
                            Return New JsonObject From {{"present", False}}
                        End If

                        Dim springs As New JsonArray()
                        Dim compressions As Double() = If(info.Compressions, New Double() {})
                        Dim forces As Double() = If(info.Forces, New Double() {})

                        For i As Integer = 0 To compressions.Length - 1
                            springs.Add(New JsonObject From {
                                {"index", i},
                                {"compression", compressions(i)},
                                {"force", If(i < forces.Length, forces(i), 0.0)}
                            })
                        Next

                        Return New JsonObject From {
                            {"present", True},
                            {"plateId", info.PlateId},
                            {"height", info.Height},
                            {"tilt", New JsonObject From {
                                {"x", info.TiltX},
                                {"y", info.TiltY},
                                {"mag", info.TiltMag}
                            }},
                            {"springs", springs}
                        }
                    End Function

                    ' /********************************************************************************/
                    '  夹爪相机
                    ' /********************************************************************************/

                    ''' <summary>
                    ''' 取一帧夹爪相机图像。
                    ''' </summary>
                    ''' <remarks>
                    ''' 参数：<c>format</c>（png / rgb / both / all）、<c>depth</c>（f32 / u16mm / true / false）、
                    ''' <c>width</c>、<c>height</c>、<c>fov</c>（临时覆盖，仅本次请求生效）。
                    ''' </remarks>
                    Private Function Frame(args As JsonObject) As JsonObject
                    If Camera Is Nothing Then
                    Throw New ProtocolException(ProtocolText.ErrNotEnabled, "相机未启用（用 --camera on 或 camera {enabled:true} 启动）")
                    End If

                    Dim originalWidth As Integer = Camera.Config.Width
                    Dim originalHeight As Integer = Camera.Config.Height
                    Dim originalFov As Double = Camera.Config.FieldOfView
                    Dim originalEnabled As Boolean = Camera.Config.Enabled
                    Dim changed As Boolean = False

                    Try
                    Dim width As Integer = CInt(JsonText.Num(args, ProtocolText.FieldWidth, originalWidth))
                    Dim height As Integer = CInt(JsonText.Num(args, ProtocolText.FieldHeight, originalHeight))

                    If width <> originalWidth OrElse height <> originalHeight Then
                    Camera.Config.Width = width
                    Camera.Config.Height = height
                    Call Camera.Config.Clamp()
                    Call Camera.Invalidate()
                    changed = True
                    End If

                    If args.ContainsKey("fov") Then
                    Camera.Config.FieldOfView = JsonText.Num(args, "fov", originalFov)
                    Call Camera.Config.Clamp()
                    Call Camera.Invalidate()
                    changed = True
                    End If

                Dim enabled As Boolean = JsonText.Bool(args, "enabled", True)
                Dim wasEnabled As Boolean = Camera.Config.Enabled

                If enabled <> wasEnabled Then
                    Camera.Config.Enabled = enabled
                    changed = True
                End If

                Dim capture As CameraFrame = Camera.CaptureNow(Simulation)

                If capture Is Nothing Then
                    Throw New ProtocolException(ProtocolText.ErrSimFault, "相机没有渲染出帧")
                End If

                Return BuildFrameResult(capture, JsonText.Str(args, ProtocolText.FieldFormat, "png"),
                                        JsonText.Str(args, ProtocolText.FieldDepth, ""))
            Finally
                If changed Then
                    Camera.Config.Width = originalWidth
                    Camera.Config.Height = originalHeight
                    Camera.Config.FieldOfView = originalFov
                    Camera.Config.Enabled = originalEnabled
                    Call Camera.Invalidate()
                End If
            End Try
        End Function

                    Private Function BuildFrameResult(frame As CameraFrame, format As String, depthSpec As String) As JsonObject
                    Dim mode As String = If(String.IsNullOrEmpty(format), "png", format.Trim().ToLowerInvariant())
                    Dim wantPng As Boolean = mode = "png" OrElse mode = "both" OrElse mode = "all"
                    Dim wantRgb As Boolean = mode = "rgb" OrElse mode = "both" OrElse mode = "all"

                    If Not wantPng AndAlso Not wantRgb Then
                    Throw New ProtocolException(ProtocolText.ErrBadParams, "format 只能是 png / rgb / both / all")
                    End If

                    Dim config As CameraConfig = Camera.Config

                    Dim result As New JsonObject From {
                    {ProtocolText.FieldWidth, frame.Width},
                    {ProtocolText.FieldHeight, frame.Height},
                    {ProtocolText.FieldFormat, If(wantPng, "png", "rgb")},
                    {"time", frame.Time},
                    {"step", frame.StepIndex},
                    {"captureMs", Camera.LastCaptureMs}
                    }

                    If wantPng Then
                    result("data") = frame.ToPngBase64()
                    Else
                    result("data") = frame.ToRgbBase64()
                    End If

                    If wantRgb Then
                    result(ProtocolText.FieldRgb) = frame.ToRgbBase64()
                    result("pixelFormat") = "rgb24"
                    End If

                    ' ---- 深度 ----
                    Dim depthMode As String = If(String.IsNullOrEmpty(depthSpec), "", depthSpec.Trim().ToLowerInvariant())

                    Select Case depthMode
                    Case "f32", "float", "1", "true"
                    result(ProtocolText.FieldDepth) = New JsonObject From {
                       {"format", "f32"},
                       {"encoding", "base64"},
                       {"width", frame.Width},
                       {"height", frame.Height},
                       {"data", frame.ToDepthF32Base64()}
                    }
                    Case "u16mm", "u16", "mm"
                    result(ProtocolText.FieldDepth) = New JsonObject From {
                       {"format", "u16mm"},
                       {"encoding", "base64"},
                       {"width", frame.Width},
                       {"height", frame.Height},
                       {"data", frame.ToDepthU16MmBase64()}
                    }
                    End Select

                    ' ---- 内外参 ----
                    result(ProtocolText.FieldCamera) = BuildCameraExtrinsics(frame, config)

                    Return result
                    End Function

                    ''' <summary>查询/修改相机参数；不带参数时返回当前配置与外参。</summary>
                    Private Function CameraControl(args As JsonObject) As JsonObject
                    If Camera Is Nothing Then
                    Throw New ProtocolException(ProtocolText.ErrNotEnabled, "相机未启用（用 --camera on 启动）")
                    End If

                    If args Is Nothing OrElse args.Count = 0 Then Return CameraSummary()

                    Dim config As CameraConfig = Camera.Config

                    If args.ContainsKey("enabled") Then config.Enabled = JsonText.Bool(args, "enabled", True)
                    If args.ContainsKey(ProtocolText.FieldWidth) Then config.Width = CInt(JsonText.Num(args, ProtocolText.FieldWidth, config.Width))
                    If args.ContainsKey(ProtocolText.FieldHeight) Then config.Height = CInt(JsonText.Num(args, ProtocolText.FieldHeight, config.Height))
                    If args.ContainsKey("fov") Then config.FieldOfView = JsonText.Num(args, "fov", config.FieldOfView)
                    If args.ContainsKey("rate") Then config.Rate = JsonText.Num(args, "rate", config.Rate)
                    If args.ContainsKey("ambient") Then config.Ambient = JsonText.Num(args, "ambient", config.Ambient)

                    Dim offset As Double() = JsonText.Numbers(args, "offset")

                    If offset IsNot Nothing AndAlso offset.Length >= 3 Then
                    config.Offset = New Vector3(offset(0), offset(1), offset(2))
                    End If

                    Call Camera.Invalidate()

                    Return CameraSummary()
                    End Function

                    Private Function CameraSummary() As JsonObject
                    Dim frame As CameraFrame = Camera.Latest

                    If frame Is Nothing OrElse frame.Width <> Camera.Config.Width Then
                    frame = Camera.CaptureNow(Simulation)
                    End If

                    Dim config As CameraConfig = Camera.Config

                    Dim summary As New JsonObject From {
                    {"enabled", config.Enabled},
                    {ProtocolText.FieldWidth, config.Width},
                    {ProtocolText.FieldHeight, config.Height},
                    {"fov", config.FieldOfView},
                    {"hfov", config.HorizontalFieldOfView},
                    {"rate", config.Rate},
                    {"fps", Camera.Fps},
                    {"captureMs", Camera.LastCaptureMs},
                    {"triangles", Camera.TriangleCount},
                    {"offset", JsonText.ArrayOf(New Double() {config.Offset.x, config.Offset.y, config.Offset.z})}
                    }

                    If frame IsNot Nothing Then
                    summary(ProtocolText.FieldCamera) = BuildCameraExtrinsics(frame, config)
                    End If

                    Return summary
                    End Function

                    Private Shared Function BuildCameraExtrinsics(frame As CameraFrame, config As CameraConfig) As JsonObject
                    Dim node As New JsonObject From {
                    {"fov", config.FieldOfView},
                    {"fx", config.FocalPixels},
                    {"fy", config.FocalPixels},
                    {"cx", frame.Width / 2.0},
                    {"cy", frame.Height / 2.0},
                    {"near", SoftwareRasterizer.NearPlane}
                    }

                    If frame.Pose IsNot Nothing Then
                    node("position") = JsonText.ArrayOf(StateSnapshot.ToArray(frame.Pose.Position))
                    node("rpy") = JsonText.ArrayOf(frame.Pose.RpyDegrees())
                    End If

                    Return node
                    End Function

                    End Class

End Namespace
