Imports System.Collections
Imports System.Globalization
Imports System.Text
Imports Ollama.JSON.FunctionCall

''' <summary>
''' 把虚拟实验室的机械臂 API 封装成 LLM 可调用的工具集。
''' </summary>
''' <remarks>
''' <para>
''' 设计原则：**只暴露「世界状态 + 动作」，不暴露像素**。
''' LLM 拿到的 <see cref="DescribeWorld"/> 是一份紧凑的文本世界模型
''' （TCP、关节角、每个物体的位置/姿态/是否倾倒），够它规划搬运；
''' 之后接视觉时只要再挂一个 <c>look()</c> 工具即可。
''' </para>
''' <para>
''' 参数全部是字符串（<see cref="FunctionCall.arguments"/> 是
''' <c>Dictionary(Of String, String)</c>），所以这里做了容错的数字/向量解析。
''' </para>
''' </remarks>
Public Class LabTools

    ''' <summary>手指中心相对 TCP 的垂直偏移（米）：抓取时 TCP 要比物体中心高这么多。</summary>
    Public Const FingerDrop As Double = 0.027

    ''' <summary>安全高位（米）：水平搬运都在这个高度以上完成，肘部才不会压到桌面。</summary>
    Public Const CarryHeight As Double = 0.44

    ''' <summary>单次运动等待的上限（毫秒）。</summary>
    Public Const MotionTimeout As Integer = 180000

    Private ReadOnly _arm As ArmJsHost

    Public Sub New(arm As ArmJsHost)
        _arm = arm
    End Sub

    ' /********************************************************************************/
    '  工具清单
    ' /********************************************************************************/

    ''' <summary>给 LLM 的工具定义（JSON schema）。</summary>
    Public Function Catalog() As FunctionTool()
        Dim models As New List(Of FunctionModel)()

        Call models.Add(Model("world",
            "读取三维场景当前状态：TCP 位姿、关节角、夹爪开度、所有物体的位置/尺寸/姿态（是否倾倒、是否已落地）、弹簧平台倾角。每次行动前后都应调用。"))

        Call models.Add(Arg(Arg(Arg(Arg(Model("move_to",
            "关节点位运动到指定位姿（末端固定朝下）。走关节插值，快但末端轨迹是弧线；低处水平搬运请用 move_linear。"),
            "x", "目标 TCP 的 x（米）", "number"),
            "y", "目标 TCP 的 y（米）", "number"),
            "z", "目标 TCP 的 z（米）", "number"),
            "speed", "速度倍率 0.1~1.0，默认 0.5", "number", False))

        Call models.Add(Arg(Arg(Arg(Arg(Model("move_linear",
            "笛卡尔直线运动到指定位置，末端姿态保持不变。夹着工件搬运、需要精确竖直升降时用它。"),
            "x", "x（米）", "number"),
            "y", "y（米）", "number"),
            "z", "z（米）", "number"),
            "speed", "速度倍率，默认 0.2", "number", False))

        Call models.Add(Arg(Arg(Model("move_joints",
            "直接给定 6 个关节角（弧度）运动，用于回零或避开奇异位形。q 形如 [0,0,0,0,0,0]。"),
            "q", "6 个关节角（弧度）的数组", "array"),
            "speed", "速度倍率，默认 0.3", "number", False))

        Call models.Add(Arg(Model("home", "机械臂回零位（所有关节角 0）。"),
            "speed", "速度倍率，默认 0.5", "number", False))

        Call models.Add(Arg(Model("grip", "闭合夹爪夹住物体，并等待手指真正压紧（约 2.5 秒）。"),
            "force", "夹持力 0~60 N，默认 60", "number", False))

        Call models.Add(Model("release", "张开夹爪释放物体。"))

        Call models.Add(Arg(Model("pick_object",
            "推荐用它抓取：自动完成「抬到物体上方 → 分段竖直下降 → 夹紧 → 抬到安全高位」，抓取高度按物体当前位置计算。"),
            "id", "物体 id，例如 block-11（先用 world 查）", "string"))

        Call models.Add(Arg(Arg(Arg(Model("place_on_table",
            "推荐用它放置：自动完成「移到桌面上方 → 分段下降 → 释放 → 抬起」。"),
            "table", "目标桌子的物体 id（static 的 box）", "string"),
            "dx", "相对桌面中心的 x 偏移（米），默认 0", "number", False),
            "dy", "相对桌面中心的 y 偏移（米），默认 0", "number", False))

        Call models.Add(Arg(Arg(Arg(Arg(Arg(Arg(Model("spawn_table",
            "生成一张静态桌子（不会被推动）。桌面顶面高度 = z + thickness/2。"),
            "x", "桌面中心 x（米）", "number"),
            "y", "桌面中心 y（米）", "number"),
            "z", "桌面中心 z（米）", "number"),
            "width", "x 方向宽度，默认 0.30", "number", False),
            "depth", "y 方向深度，默认 0.46", "number", False),
            "thickness", "厚度，默认 0.04", "number", False))

        Call models.Add(Arg(Arg(Arg(Arg(Arg(Model("spawn_flask",
            "在指定位置生成一个三角瓶（液体体积不同→质量不同）。position 是瓶底中心，瓶子自动立起。"),
            "x", "瓶底中心 x（米）", "number"),
            "y", "瓶底中心 y（米）", "number"),
            "z", "瓶底所在高度（米），通常等于桌面顶面高度", "number"),
            "mass", "质量（kg），0.10~0.34", "number", False),
            "color", "颜色，例如 #FDE68A", "string", False))

        Call models.Add(Arg(Model("remove_object", "移除一个物体（例如打翻在地的瓶子）。"),
            "id", "物体 id", "string"))

        Call models.Add(Arg(Model("wait", "等待指定毫秒，让物理稳定下来（放置后建议 800~1500）。"),
            "ms", "毫秒", "number"))

        Call models.Add(Model("reset_scene", "清空所有物体并回零，把场景恢复到初始状态。"))

        Call models.Add(Arg(Model("save_frame",
            "把夹爪第一视角相机的一帧保存成 PNG（视觉尚未接入，这里只用于留证）。"),
            "path", "保存路径", "string", False))

        Return FunctionTool.CreateToolSet(models.ToArray())
    End Function

    Private Shared Function Model(name As String, description As String) As FunctionModel
        Return New FunctionModel With {
            .name = name,
            .description = description,
            .parameters = New FunctionParameters With {
                .properties = New Dictionary(Of String, ParameterProperties)(StringComparer.Ordinal),
                .required = New String() {}
            }
        }
    End Function

    Private Shared Function Arg(model As FunctionModel, name As String, description As String,
                                type As String, Optional required As Boolean = True) As FunctionModel
        Call model.parameters.properties.Add(name, New ParameterProperties With {
            .name = name,
            .description = description,
            .type = type
        })

        If required Then
            Dim list As New List(Of String)(model.parameters.required)

            Call list.Add(name)

            model.parameters.required = list.ToArray()
        End If

        Return model
    End Function

    ' /********************************************************************************/
    '  执行
    ' /********************************************************************************/

    ''' <summary>执行一次工具调用，返回给 LLM 的文本结果。</summary>
    Public Function Invoke(fc As FunctionCall) As String
        If fc Is Nothing Then Return "[error] 空的工具调用"

        Try
            Select Case LCaseInvariant(fc.name)
                Case "world" : Return DescribeWorld()
                Case "move_to" : Return MoveTo(Num(fc, "x"), Num(fc, "y"), Num(fc, "z"), Num(fc, "speed", 0.5))
                Case "move_linear" : Return MoveLinear(Num(fc, "x"), Num(fc, "y"), Num(fc, "z"), Num(fc, "speed", 0.2))
                Case "move_joints" : Return MoveJoints(Vector(fc, "q"), Num(fc, "speed", 0.3))
                Case "home" : Return Home(Num(fc, "speed", 0.5))
                Case "grip" : Return Grip(Num(fc, "force", 60.0))
                Case "release" : Return Release()
                Case "pick_object" : Return PickObject(ArgText(fc, "id"))
                Case "place_on_table" : Return PlaceOnTable(ArgText(fc, "table"), Num(fc, "dx"), Num(fc, "dy"))
                Case "spawn_table" : Return SpawnTable(fc)
                Case "spawn_flask" : Return SpawnFlask(fc)
                Case "remove_object" : Return RemoveObject(ArgText(fc, "id"))
                Case "wait" : Return WaitMs(Num(fc, "ms", 1000.0))
                Case "reset_scene" : Return ResetScene()
                Case "save_frame" : Return SaveFrame(ArgText(fc, "path"))
                Case Else : Return $"[error] 未知工具 [{fc.name}]"
            End Select
        Catch ex As Exception
            Return "[error] 工具执行失败: " & ex.Message
        End Try
    End Function

    ' /********************************************************************************/
    '  世界状态
    ' /********************************************************************************/

    ''' <summary>
    ''' 把三维场景压成一段 LLM 可读的文本。
    ''' </summary>
    ''' <remarks>
    ''' 用文本而不是 JSON：同样的信息占的 token 少得多，模型也更容易读懂「哪瓶倒了」。
    ''' </remarks>
    Public Function DescribeWorld() As String
        Dim snapshot As Dictionary(Of String, Object)

        Try
            snapshot = _arm.state()
        Catch ex As Exception
            Return "[error] 读不到仿真状态（服务端是否已启动？）: " & ex.Message
        End Try

        If snapshot Is Nothing Then Return "[error] 读不到仿真状态"

        Dim sb As New StringBuilder()
        Dim tcp As Object = GetValue(snapshot, "tcp")

        If tcp IsNot Nothing Then
            Dim p As Double() = ToVector(tcp)

            Call sb.AppendLine($"tcp=({p(0):F3}, {p(1):F3}, {p(2):F3})  rpy=({p(3):F1}, {p(4):F1}, {p(5):F1}) deg")
        End If

        Dim gripper As Dictionary(Of String, Object) = TryCast(GetValue(snapshot, "gripper"), Dictionary(Of String, Object))

        If gripper IsNot Nothing Then
            Call sb.AppendLine($"gripper open={NumOf(gripper, "opening"):F3} m  contact={FlagOf(gripper, "contact")}  holding={TextOf(gripper, "holdingId")}")
        End If

        Dim q As Object = GetValue(snapshot, "q")

        If q IsNot Nothing Then
            Dim joints As Double() = ToVector(q)
            Dim parts As New List(Of String)()

            For i As Integer = 0 To joints.Length - 1
                Call parts.Add(joints(i).ToString("F2"))
            Next

            Call sb.AppendLine("joints=[" & String.Join(", ", parts) & "] rad")
        End If

        Dim items As IList = TryCast(GetValue(snapshot, "objects"), IList)

        If items IsNot Nothing Then
            Call sb.AppendLine($"objects ({items.Count}):")

            For Each raw As Object In items
                Dim item As Dictionary(Of String, Object) = TryCast(raw, Dictionary(Of String, Object))

                If item Is Nothing Then Continue For

                Dim p As Double() = ToVector(GetValue(item, "position"))
                Dim s As Double() = ToVector(GetValue(item, "size"))

                Call sb.AppendLine($"  {TextOf(item, "id"),-14} {TextOf(item, "kind"),-8} pos=({p(0):F3},{p(1):F3},{p(2):F3}) size=({s(0):F3},{s(1):F3},{s(2):F3})")

                If FlagOf(item, "isStatic") Then
                    Call sb.AppendLine($"  {"",-14} static 桌面，顶面 z={p(2) + s(2) * 0.5:F3}")
                Else
                    Call sb.AppendLine($"  {"",-14} tilt={NumOf(item, "tiltDeg"):F1}° posture={TextOf(item, "posture")} fallen={FlagOf(item, "fallen")} mass={NumOf(item, "mass"):F2}kg")
                End If
            Next
        End If

        Dim platform As Dictionary(Of String, Object) = TryCast(GetValue(snapshot, "platform"), Dictionary(Of String, Object))

        If platform IsNot Nothing AndAlso FlagOf(platform, "present") Then
            Call sb.AppendLine($"platform: tilt={NumOf(platform, "tiltMag"):F2}°  height={NumOf(platform, "height"):F3} m")
        Else
            Call sb.AppendLine("platform: none")
        End If

        Return sb.ToString()
    End Function

    ' /********************************************************************************/
    '  动作
    ' /********************************************************************************/

    Private Function MoveTo(x As Double, y As Double, z As Double, speed As Double) As String
        If Not Plausible(x, y, z) Then Return "[error] 目标坐标超出合理范围（|x|,|y| < 1 m，0 < z < 1 m）"

        Return Wait(_arm.moveTo(x, y, z, 0, 90, 0, speed))
    End Function

    Private Function MoveLinear(x As Double, y As Double, z As Double, speed As Double) As String
        If Not Plausible(x, y, z) Then Return "[error] 目标坐标超出合理范围"

        Return Wait(_arm.moveLHold(x, y, z, speed))
    End Function

    Private Function MoveJoints(q As Double(), speed As Double) As String
        If q Is Nothing OrElse q.Length <> 6 Then Return "[error] q 必须是 6 个关节角"

        Return Wait(_arm.moveJ(q, speed))
    End Function

    Private Function Home(speed As Double) As String
        Return Wait(_arm.home(speed))
    End Function

    Private Function Grip(force As Double) As String
        Call _arm.grip(force)
        Call _arm.sleep(2500)

        Dim snapshot As Dictionary(Of String, Object) = _arm.state()
        Dim gripper As Dictionary(Of String, Object) = TryCast(GetValue(snapshot, "gripper"), Dictionary(Of String, Object))

        If gripper Is Nothing Then Return "gripped"

        Return $"gripped open={NumOf(gripper, "opening"):F3} contact={FlagOf(gripper, "contact")} holding={TextOf(gripper, "holdingId")}"
    End Function

    Private Function Release() As String
        Call _arm.release()
        Call _arm.sleep(1200)

        Return "released"
    End Function

    ''' <summary>
    ''' 抓取：抬到物体上方 → 分段下落到「物中心 + 手指偏移」 → 夹紧 → 抬到安全高位。
    ''' </summary>
    Public Function PickObject(id As String) As String
        If String.IsNullOrEmpty(id) Then Return "[error] 缺少物体 id"

        Dim item As Dictionary(Of String, Object) = FindItem(id)

        If item Is Nothing Then Return $"[error] 场景里没有 [{id}]，先用 world 查一下 id"

        Dim p As Double() = ToVector(GetValue(item, "position"))
        Dim graspZ As Double = p(2) + FingerDrop

        Call MoveTo(p(0), p(1), CarryHeight, 0.5)
        Call Descend(p(0), p(1), CarryHeight, graspZ, 5)

        Dim gripped As String = Grip(60)

        Call Ascend(p(0), p(1), graspZ, CarryHeight, 6)

        Return $"picked {id} from ({p(0):F3}, {p(1):F3}, {p(2):F3}); {gripped}"
    End Function

    ''' <summary>
    ''' 放置：移到桌面目标点上方 → 分段下降 → 释放 → 抬起。
    ''' </summary>
    Public Function PlaceOnTable(tableId As String, dx As Double, dy As Double) As String
        Dim table As Dictionary(Of String, Object) = FindItem(tableId)

        If table Is Nothing Then Return $"[error] 场景里没有桌子 [{tableId}]"

        Dim p As Double() = ToVector(GetValue(table, "position"))
        Dim s As Double() = ToVector(GetValue(table, "size"))
        Dim top As Double = p(2) + s(2) * 0.5

        Dim tx As Double = p(0) + dx
        Dim ty As Double = p(1) + dy
        Dim releaseZ As Double = top + FingerDrop + 0.012

        Call MoveTo(tx, ty, CarryHeight, 0.5)
        Call Descend(tx, ty, CarryHeight, releaseZ, 5)

        Call Release()
        Call Ascend(tx, ty, releaseZ, 0.42, 5)

        Return $"placed on {tableId} at ({tx:F3}, {ty:F3}); table top z={top:F3}"
    End Function

    Private Function SpawnTable(fc As FunctionCall) As String
        Dim thickness As Double = Num(fc, "thickness", 0.04)

        Dim result As Dictionary(Of String, Object) = _arm.spawnStatic("box",
            New Double() {Num(fc, "width", 0.30), Num(fc, "depth", 0.46), thickness},
            New Double() {Num(fc, "x"), Num(fc, "y"), Num(fc, "z")},
            50.0, 0.8, "#CBD5E1")

        Return "table: " & DescribeSpawn(result) & $"; 顶面 z={Num(fc, "z") + thickness * 0.5:F3}"
    End Function

    Private Function SpawnFlask(fc As FunctionCall) As String
        Dim result As Dictionary(Of String, Object) = _arm.spawn("flask",
            New Double() {0.07, 0.07, 0.13},
            New Double() {Num(fc, "x"), Num(fc, "y"), Num(fc, "z")},
            Num(fc, "mass", 0.2),
            0.45,
            ArgText(fc, "color", "#FDE68A"))

        Return "flask: " & DescribeSpawn(result) & $"（瓶底 z={Num(fc, "z"):F3}，瓶口 z={Num(fc, "z") + 0.13:F3}）"
    End Function

    Private Function RemoveObject(id As String) As String
        If String.IsNullOrEmpty(id) Then Return "[error] 缺少 id"

        Call _arm.remove(id)

        Return "removed " & id
    End Function

    Private Function WaitMs(ms As Double) As String
        Dim clamped As Double = System.Math.Max(0.0, System.Math.Min(60000.0, ms))

        Call _arm.sleep(clamped)

        Return $"waited {clamped:F0} ms"
    End Function

    Private Function ResetScene() As String
        Call _arm.clear()
        Call _arm.release()
        Call _arm.sleep(600)

        Call Wait(_arm.home(0.6))

        Return "scene reset"
    End Function

    Private Function SaveFrame(path As String) As String
        If String.IsNullOrEmpty(path) Then path = "agent-frame.png"

        Call _arm.saveFrame(path, True)

        Return "saved " & path
    End Function

    ' /********************************************************************************/
    '  运动基元
    ' /********************************************************************************/

    ''' <summary>竖直下降（分段，避免 moveTo 的关节插值走出弧线）。</summary>
    Private Sub Descend(x As Double, y As Double, fromZ As Double, toZ As Double, steps As Integer)
        For i As Integer = 1 To steps
            Call MoveLinear(x, y, fromZ + (toZ - fromZ) * i / steps, 0.12)
        Next
    End Sub

    Private Sub Ascend(x As Double, y As Double, fromZ As Double, toZ As Double, steps As Integer)
        For i As Integer = 1 To steps
            Call MoveLinear(x, y, fromZ + (toZ - fromZ) * i / steps, 0.14)
        Next
    End Sub

    ''' <summary>等待运动完成，返回 <c>ok</c> 或 <c>[error] ...</c>。</summary>
    Private Function Wait(motion As Dictionary(Of String, Object)) As String
        If motion Is Nothing Then Return "[error] 运动指令没有返回结果"

        Dim id As Object = GetValue(motion, "motionId")

        If id Is Nothing Then Return "[error] 运动指令缺少 motionId"

        Dim done As Dictionary(Of String, Object) = _arm.waitMotion(ToDouble(id), MotionTimeout)

        If done Is Nothing Then Return "[error] 等待运动超时"

        If Not done.ContainsKey("success") Then Return "ok"
        If FlagOf(done, "success") Then Return "ok"

        Dim message As Object = GetValue(done, "message")

        Return "[error] 运动失败: " & If(message Is Nothing, "(无说明)", message.ToString())
    End Function

    ' /********************************************************************************/
    '  解析辅助
    ' /********************************************************************************/

    Private Function FindItem(id As String) As Dictionary(Of String, Object)
        If String.IsNullOrEmpty(id) Then Return Nothing

        Dim list As List(Of Object) = _arm.objects()

        If list Is Nothing Then Return Nothing

        For Each raw As Object In list
            Dim item As Dictionary(Of String, Object) = TryCast(raw, Dictionary(Of String, Object))

            If item Is Nothing Then Continue For
            If String.Equals(TextOf(item, "id"), id, StringComparison.OrdinalIgnoreCase) Then Return item
        Next

        Return Nothing
    End Function

    Private Shared Function DescribeSpawn(result As Dictionary(Of String, Object)) As String
        If result Is Nothing Then Return "(no result)"

        Dim p As Double() = ToVector(GetValue(result, "position"))

        Return $"id={TextOf(result, "id")} pos=({p(0):F3},{p(1):F3},{p(2):F3})"
    End Function

    Private Shared Function Plausible(x As Double, y As Double, z As Double) As Boolean
        If Double.IsNaN(x) OrElse Double.IsNaN(y) OrElse Double.IsNaN(z) Then Return False
        If System.Math.Abs(x) > 1.0 OrElse System.Math.Abs(y) > 1.0 Then Return False

        Return z > 0.0 AndAlso z < 1.0
    End Function

    Private Shared Function GetValue(source As Dictionary(Of String, Object), key As String) As Object
        If source Is Nothing Then Return Nothing

        Dim value As Object = Nothing

        If source.TryGetValue(key, value) Then Return value

        Return Nothing
    End Function

    Private Shared Function TextOf(source As Dictionary(Of String, Object), key As String) As String
        Dim value As Object = GetValue(source, key)

        If value Is Nothing Then Return Nothing

        Return value.ToString()
    End Function

    Private Shared Function NumOf(source As Dictionary(Of String, Object), key As String, Optional fallback As Double = 0.0) As Double
        Return ToDouble(GetValue(source, key), fallback)
    End Function

    Private Shared Function FlagOf(source As Dictionary(Of String, Object), key As String) As Boolean
        Dim value As Object = GetValue(source, key)

        If value Is Nothing Then Return False
        If TypeOf value Is Boolean Then Return CBool(value)

        Return String.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function ToDouble(value As Object, Optional fallback As Double = 0.0) As Double
        If value Is Nothing Then Return fallback
        If TypeOf value Is Double Then Return CDbl(value)
        If TypeOf value Is Integer Then Return CDbl(value)

        Dim parsed As Double

        If Double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, parsed) Then Return parsed

        Return fallback
    End Function

    ''' <summary>把 <c>Object()</c> / <c>List(Of Object)</c> / <c>"x y z"</c> 统一转成数字数组。</summary>
    Private Shared Function ToVector(value As Object) As Double()
        If value Is Nothing Then Return New Double() {0, 0, 0}

        Dim text As String = TryCast(value, String)

        If text IsNot Nothing Then Return ParseNumbers(text)

        Dim list As IList = TryCast(value, IList)

        If list IsNot Nothing Then
            Dim out(list.Count - 1) As Double

            For i As Integer = 0 To list.Count - 1
                out(i) = ToDouble(list(i))
            Next

            Return out
        End If

        Return New Double() {0, 0, 0}
    End Function

    Private Shared Function ParseNumbers(text As String) As Double()
        If String.IsNullOrEmpty(text) Then Return New Double() {0, 0, 0}

        Dim clean As String = text.Replace("[", " ").Replace("]", " ").Replace(",", " ")
        Dim parts As String() = clean.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
        Dim values As New List(Of Double)()

        For Each part As String In parts
            Dim parsed As Double

            If Double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, parsed) Then values.Add(parsed)
        Next

        If values.Count = 0 Then Return New Double() {0, 0, 0}

        Return values.ToArray()
    End Function

    Private Shared Function ArgText(fc As FunctionCall, name As String, Optional fallback As String = Nothing) As String
        If fc Is Nothing OrElse fc.arguments Is Nothing Then Return fallback

        Dim value As String = Nothing

        If fc.arguments.TryGetValue(name, value) AndAlso Not String.IsNullOrEmpty(value) Then Return value

        Return fallback
    End Function

    Private Shared Function Num(fc As FunctionCall, name As String, Optional fallback As Double = 0.0) As Double
        Dim text As String = ArgText(fc, name)

        If String.IsNullOrEmpty(text) Then Return fallback

        Return ToDouble(text, fallback)
    End Function

    Private Shared Function Vector(fc As FunctionCall, name As String) As Double()
        Dim text As String = ArgText(fc, name)

        If String.IsNullOrEmpty(text) Then Return Nothing

        Return ParseNumbers(text)
    End Function

    Private Shared Function LCaseInvariant(text As String) As String
        If text Is Nothing Then Return ""

        Return text.Trim().ToLowerInvariant()
    End Function

End Class
