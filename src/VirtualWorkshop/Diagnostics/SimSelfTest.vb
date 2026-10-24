Imports System.Diagnostics
Imports System.Text.Json.Nodes
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model
Imports VirtualWorkshop.Protocol
Imports VirtualWorkshop.Sensing
Imports VirtualWorkshop.Simulation

Namespace Diagnostics

    ''' <summary>
    ''' 无界面自检：模型解析、运动学、协议指令、物理与夹爪的断言套件。
    ''' </summary>
    ''' <remarks>
    ''' 运行方式：<c>VirtualWorkshop.exe --selftest</c>（退出码 0 表示全部通过）。
    ''' </remarks>
    Public Module SimSelfTest

        Private _failures As Integer

        Public Function Inspect(preset As SimulationPreset) As Integer
            If preset Is Nothing OrElse Not preset.Ready Then
                Console.Error.WriteLine("仿真环境未就绪")
                Return 2
            End If

            Dim assembly As ArmAssembly = preset.Assembly

            Console.WriteLine("== VirtualWorkshop inspect ==")
            Console.WriteLine("  " & assembly.Describe())
            Console.WriteLine($"  model = {preset.ModelFile}")
            Console.WriteLine($"  urdf  = {preset.UrdfFile}")
            Console.WriteLine()

            Console.WriteLine("  links:")
            For Each link As UrdfLink In assembly.Robot.Links
                Console.WriteLine($"    {link.Name,-12} m={link.Mass,6:F2} size=({link.CollisionSize.x:F3},{link.CollisionSize.y:F3},{link.CollisionSize.z:F3})")
            Next

            Console.WriteLine("  joints:")
            For Each joint As ArmJointDef In assembly.Joints
                Console.WriteLine($"    {joint.Name,-12} {joint.ParentLink} -> {joint.ChildLink} axis=({joint.Axis.x:F2},{joint.Axis.y:F2},{joint.Axis.z:F2}) limit=[{joint.Lower:F3}, {joint.Upper:F3}]")
            Next

            Dim zeroPose As Pose3D = assembly.TcpPose(New Double() {0, 0, 0, 0, 0, 0})

            Console.WriteLine()
            Console.WriteLine($"  TCP(zero) = ({zeroPose.Position.x:F4}, {zeroPose.Position.y:F4}, {zeroPose.Position.z:F4})")
            Console.WriteLine($"  meshes    = {assembly.Meshes.MeshNames().Count} / {assembly.Meshes.TriangleCount} triangles")

            Dim camera As Sensing.GripperCamera = preset.Host?.Camera

            Console.WriteLine()

            If camera Is Nothing Then
                Console.WriteLine("  camera    = 已关闭（--camera off）")
            Else
                Dim config As Sensing.CameraConfig = camera.Config

                Console.WriteLine($"  camera    = {config.Width}x{config.Height} · fov {config.FieldOfView:F0}° · {config.Rate:F0} Hz")
                Console.WriteLine($"  intrinsics= fx {config.FocalPixels:F2}  fy {config.FocalPixels:F2}  cx {config.Width / 2.0:F1}  cy {config.Height / 2.0:F1}")
                Console.WriteLine($"  offset    = ({config.Offset.x:F3}, {config.Offset.y:F3}, {config.Offset.z:F3})  (link6 局部坐标)")
                Console.WriteLine($"  hfov      = {config.HorizontalFieldOfView:F1}°")
            End If

            Return 0
        End Function

        Public Function Run(preset As SimulationPreset) As Integer
            _failures = 0

            Console.WriteLine("== VirtualWorkshop selftest ==")

            If preset Is Nothing OrElse Not preset.Ready Then
                Console.Error.WriteLine("仿真环境未就绪")
                Return 2
            End If

            Dim host As SimulationHost = preset.Host
            Dim simulation As ArmSimulation = preset.Simulation
            Dim assembly As ArmAssembly = preset.Assembly

            ' ---- 1. 模型 ----
            Call Section("模型")
            Call Check(assembly.Robot.Links.Count = 8, "解析出 8 个 link")
            Call Check(assembly.JointCount = 6, "解析出 6 个可动关节")
            Call Check(assembly.Meshes.TriangleCount = 2704, "按连杆分组后共 2704 个三角形")
            Call Check(assembly.Meshes.TriangleCount = assembly.Meshes.LandscapeTriangleCount,
                       "与 Landscape 整体加载的三角形数一致")

            ' ---- 2. 运动学 ----
            Call Section("运动学")

            Dim zeroQ As Double() = {0, 0, 0, 0, 0, 0}
            Dim zeroPose As Pose3D = assembly.TcpPose(zeroQ)

            Call Check(Math.Abs(zeroPose.Position.x - 0.617) < 0.001 AndAlso Math.Abs(zeroPose.Position.z - 0.178) < 0.001,
                       $"零位 TCP = ({zeroPose.Position.x:F4}, {zeroPose.Position.y:F4}, {zeroPose.Position.z:F4})")

            Dim target As New Pose3D(New Vector3(0.45, 0.1, 0.3), zeroPose.Rotation)
            Dim ik As IkResult = Kinematics.Solve(assembly, target, zeroQ)

            Call Check(ik.Success, $"逆解可达（{ik.Iterations} 次迭代，误差 {ik.PositionError:F5} m）")
            Call Check(Vector3Math.Length(assembly.TcpPose(ik.Q).Position - target.Position) < 0.001,
                       "逆解结果再正解能复现目标位置")

            ' ---- 3. 协议 ----
            Call Section("协议")

            Dim hello As JsonObject = host.ExecuteNow(ProtocolText.CmdHello, New JsonObject())
            Call Check(JsonText.Str(hello, "version") = ProtocolText.Version, "hello 返回协议版本")
            Call Check(CInt(JsonText.Num(hello, "joints", 0)) = 6, "hello 报告 6 个关节")

            Dim ping As JsonObject = host.ExecuteNow(ProtocolText.CmdPing, New JsonObject())
            Call Check(ping.ContainsKey("pong"), "ping 返回 pong")

            Dim stateNode As JsonObject = host.ExecuteNow(ProtocolText.CmdState, New JsonObject())
            Call Check(stateNode.ContainsKey("q") AndAlso stateNode.ContainsKey("tcp"), "state 返回关节角与 TCP")

            Dim info As JsonObject = host.ExecuteNow(ProtocolText.CmdInfo, New JsonObject())
            Call Check(CInt(JsonText.Num(info, "bodies", 0)) > 0, "info 报告刚体数量")

            Dim unknownRaised As Boolean = False

            Try
                Call host.ExecuteNow("no_such_command", New JsonObject())
            Catch ex As ProtocolException
                unknownRaised = String.Equals(ex.Code, ProtocolText.ErrUnknownCommand)
            End Try

            Call Check(unknownRaised, "未知指令返回 unknown_command")

            ' ---- 4. 运动 ----
            Call Section("运动")

            host.Dispatcher.RealtimeFactor = 0.0

            Dim movej As JsonObject = host.ExecuteNow(ProtocolText.CmdMoveJ,
                                                      New JsonObject From {
                                                          {"q", JsonText.ArrayOf(New Double() {0.3, -0.2, 0.4, 0.0, 0.4, 0.0})},
                                                          {"speed", 0.6}
                                                      })

            Call Check(JsonText.Bool(movej, "accepted"), "movej 被接受")

            Call host.StepFixed(300)

            Dim afterMove As Double() = simulation.JointPositions()

            Call Check(Math.Abs(afterMove(0) - 0.3) < 0.02 AndAlso Math.Abs(afterMove(2) - 0.4) < 0.02,
                       $"movej 到位（{afterMove(0):F3}, {afterMove(1):F3}, {afterMove(2):F3}）")

            Dim beforeCart As Pose3D = assembly.TcpPose(simulation.JointPositions())
            Dim movel As JsonObject = host.ExecuteNow(ProtocolText.CmdMoveL,
                                                     New JsonObject From {
                                                         {"x", beforeCart.Position.x - 0.08},
                                                         {"y", beforeCart.Position.y + 0.04},
                                                         {"z", beforeCart.Position.z + 0.03},
                                                         {"speed", 0.3}
                                                     })

            Call Check(JsonText.Bool(movel, "accepted"), "movel 被接受")

            ' 笛卡尔直线可能要求某个关节大幅转动，留足时间
            Call host.StepFixed(1200)

            Dim afterCart As Pose3D = assembly.TcpPose(simulation.JointPositions())
            Dim cartError As Double = Vector3Math.Length(afterCart.Position - New Vector3(
                beforeCart.Position.x - 0.08, beforeCart.Position.y + 0.04, beforeCart.Position.z + 0.03))

            Console.WriteLine($"      before=({beforeCart.Position.x:F3}, {beforeCart.Position.y:F3}, {beforeCart.Position.z:F3})")
            Console.WriteLine($"      target=({beforeCart.Position.x - 0.08:F3}, {beforeCart.Position.y + 0.04:F3}, {beforeCart.Position.z + 0.03:F3})")
            Console.WriteLine($"      actual=({afterCart.Position.x:F3}, {afterCart.Position.y:F3}, {afterCart.Position.z:F3})")

            Call Check(cartError < 0.02, $"movel 到达笛卡尔目标（误差 {cartError:F4} m）")

            ' ---- 5. 夹爪 ----
            Call Section("夹爪")

            Call host.ExecuteNow(ProtocolText.CmdRelease, New JsonObject())

            ' 夹爪按有限速度张开/闭合，要给它时间
            Call host.StepFixed(240)
            Dim wide As Double = simulation.GripperOpening

            Call host.ExecuteNow(ProtocolText.CmdGrip, New JsonObject From {{"force", 45.0}})
            Call host.StepFixed(480)
            Dim narrow As Double = simulation.GripperOpening

            Call Check(wide > narrow + 0.01, $"夹爪闭合（{wide:F4} → {narrow:F4} m）")

            ' ---- 6. 场景与物理 ----
            Call Section("场景与物理")

            Dim spawn As JsonObject = host.ExecuteNow(ProtocolText.CmdSpawn,
                                                     New JsonObject From {
                                                         {"shape", "sphere"},
                                                         {"size", JsonText.ArrayOf(New Double() {0.04})},
                                                         {"position", JsonText.ArrayOf(New Double() {0.5, 0.3, 0.35})},
                                                         {"mass", 0.1}
                                                     })

            Dim id As String = JsonText.Str(spawn, "id")
            Call Check(Not String.IsNullOrEmpty(id), "spawn 生成一个球体")

            Call host.StepFixed(240)

            Dim dropped As BlockInstance = simulation.FindObject(id)
            Call Check(dropped IsNot Nothing AndAlso dropped.Body.Position.z < 0.35,
                       "球体受重力下落（真实物理）")

            Dim removed As JsonObject = host.ExecuteNow(ProtocolText.CmdRemove, New JsonObject From {{"id", id}})
            Call Check(JsonText.Str(removed, "removed") = id, "remove 删除物体")
            Call Check(simulation.FindObject(id) Is Nothing, "物体已从场景中移除")

            Dim config As JsonObject = host.ExecuteNow(ProtocolText.CmdConfig,
                                                      New JsonObject From {{"key", "realtime"}, {"value", 4.0}})
            Call Check(Math.Abs(JsonText.Num(config, "realtime", 0) - 4.0) < 1.0E-9, "config 修改实时倍率")

            ' ---- 7. 相机 ----
            Call Section("夹爪相机")
            Call CheckCamera(host, simulation)

            ' ---- 8. 稳定性 ----
            Call Section("稳定性")

            Call host.ExecuteNow(ProtocolText.CmdEstop, New JsonObject())
            Call Check(simulation.Estop, "estop 置位")

            Call host.ExecuteNow(ProtocolText.CmdReset, New JsonObject())
            Call Check(Not simulation.Estop, "reset 清除急停")

            host.Dispatcher.RealtimeFactor = 1.0

            Console.WriteLine()
            Console.WriteLine($"FAILURES={_failures}")

            If _failures > 0 Then Return 1

            Console.WriteLine("SELFTEST PASS")
            Return 0
        End Function

        ''' <summary>相机断言：出帧、尺寸、非全黑、颜色层次、深度范围、PNG 魔数与状态摘要。</summary>
        Private Sub CheckCamera(host As SimulationHost, simulation As ArmSimulation)
            Dim camera As GripperCamera = host.Camera

            Call Check(camera IsNot Nothing, "相机已创建")

            If camera Is Nothing Then Return

            Call Check(camera.Config.Width = 320 AndAlso camera.Config.Height = 240, "默认分辨率 320x240")

            Dim frame As CameraFrame = camera.CaptureNow(simulation)

            Call Check(frame IsNot Nothing, "能渲染出一帧")

            If frame Is Nothing Then Return

            Call Check(frame.Rgb.Length = 320 * 240 * 3, "RGB 缓冲 = 宽*高*3 字节")
            Call Check(frame.Depth.Length = 320 * 240, "深度缓冲 = 宽*高")

            Dim background As Integer = camera.Config.BackgroundColor
            Dim bgR As Integer = (background >> 16) And &HFF
            Dim bgG As Integer = (background >> 8) And &HFF
            Dim bgB As Integer = background And &HFF

            Dim colored As Integer = 0
            Dim distinct As New HashSet(Of Integer)()
            Dim sampleColor As Integer = 0

            For i As Integer = 0 To frame.Rgb.Length - 3 Step 3
                Dim r As Integer = frame.Rgb(i)
                Dim g As Integer = frame.Rgb(i + 1)
                Dim b As Integer = frame.Rgb(i + 2)

                If r <> bgR OrElse g <> bgG OrElse b <> bgB Then
                    colored += 1

                    Dim key As Integer = (r << 16) Or (g << 8) Or b

                    If sampleColor = 0 Then sampleColor = key

                    distinct.Add(key)
                End If
            Next

            Call Check(colored > 500, $"画面有内容（{colored} 个非背景像素）")
            Call Check(distinct.Count >= 3, $"颜色有层次（{distinct.Count} 种颜色，首个 #{sampleColor:X6}）")

            Dim hits As Integer = 0

            For i As Integer = 0 To frame.Depth.Length - 1
                Dim d As Single = frame.Depth(i)

                If d > 0.005 AndAlso d <= camera.Config.MaxDistance Then hits += 1
            Next

            Call Check(hits > 500, $"深度有命中（{hits} 个像素）")

            Dim png As Byte() = frame.ToPng()

            Call Check(png.Length > 100 AndAlso png(0) = &H89 AndAlso png(1) = &H50 AndAlso
                      png(2) = &H4E AndAlso png(3) = &H47, "PNG 魔数正确")

            Dim depthBytes As Byte() = Convert.FromBase64String(frame.ToDepthF32Base64())

            Call Check(depthBytes.Length = 320 * 240 * 4, "深度 base64 长度 = 宽*高*4")

            Call host.StepFixed(2)

            Call Check(host.State IsNot Nothing AndAlso host.State.Camera IsNot Nothing, "state 含相机摘要")
            Call Check(host.State IsNot Nothing AndAlso host.State.Camera IsNot Nothing AndAlso
                       host.State.Camera.FocalPixels > 100, "相机内参已发布")

            ' 末端转朝下后应该能看到桌面上的积木
            Dim motion As JsonObject = host.ExecuteNow(ProtocolText.CmdMoveTo, New JsonObject From {
                {"pose", New JsonObject From {
                    {"x", 0.32}, {"y", 0.0}, {"z", 0.18},
                    {"rx", 0.0}, {"ry", 90.0}, {"rz", 0.0}
                }},
                {"speed", 1.0}
            })

            Dim id As Integer = CInt(JsonText.Num(motion, "motionId", 0))

            For i As Integer = 1 To 600
                Call host.StepFixed(1)

                If host.Hardware.IsMotionDone(id) Then Exit For
            Next

            Dim down As CameraFrame = camera.CaptureNow(simulation)

            If down IsNot Nothing Then
                Dim far As Integer = 0
                Dim vivid As New HashSet(Of Integer)()

                For i As Integer = 0 To down.Depth.Length - 1
                    If down.Depth(i) > 0.15 Then far += 1
                Next

                For i As Integer = 0 To down.Rgb.Length - 3 Step 3
                    Dim r As Integer = down.Rgb(i)
                    Dim g As Integer = down.Rgb(i + 1)
                    Dim b As Integer = down.Rgb(i + 2)

                    Dim maxValue As Integer = System.Math.Max(r, System.Math.Max(g, b))
                    Dim minValue As Integer = System.Math.Min(r, System.Math.Min(g, b))

                    ' 积木是彩色的（橙/青/绿…），机械臂是灰白，饱和度能区分开
                    If maxValue - minValue > 40 Then vivid.Add((r << 16) Or (g << 8) Or b)
                Next

                Call Check(far > 200, $"俯视时能看到较远场景（{far} 个远距像素）")
                Call Check(vivid.Count >= 3, $"俯视时能看到彩色积木（{vivid.Count} 种饱和色）")
            End If

            Call Check(Not camera.Faulted, "相机无异常" & If(camera.LastError Is Nothing, "", "：" & camera.LastError))
        End Sub

        Private Sub Section(title As String)
            Console.WriteLine()
            Console.WriteLine("-- " & title & " --")
        End Sub

        Private Sub Check(ok As Boolean, message As String)
            If ok Then
                Console.WriteLine("  [PASS] " & message)
            Else
                Console.WriteLine("  [FAIL] " & message)
                _failures += 1
            End If
        End Sub

    End Module

End Namespace
