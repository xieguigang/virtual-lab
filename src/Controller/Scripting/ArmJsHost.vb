Imports System.IO
Imports System.Text.Json.Nodes
Imports System.Threading

''' <summary>
''' 注入到 JavaScript 全局作用域里的 <c>arm</c> 对象。
''' </summary>
''' <remarks>
''' <para>
''' LiteJs 通过 <c>Interpreter.DefineGlobal</c> 把 CLR 对象包成反射代理，
''' Public 方法自动成为 JS 成员，异常被规范化为可 <c>catch</c> 的脚本错误。
''' </para>
''' <para>
''' <b>返回值格式很重要</b>：只有返回 <c>Dictionary(Of String, Object)</c> /
''' <c>List(Of Object)</c>，JS 侧才能用 <c>.map/.length/属性访问</c>；
''' 返回 <c>JsonObject</c> 只会得到一个只能索引的 CLR 代理。
''' </para>
''' <para>
''' 方法名刻意用小写开头，让脚本里写成 <c>arm.moveJ([...])</c> 这种自然的 JS 风格。
''' </para>
''' </remarks>
Public Class ArmJsHost

    Private ReadOnly _client As ArmClient

    Public Sub New(client As ArmClient)
        If client Is Nothing Then Throw New ArgumentNullException(NameOf(client))

        _client = client
    End Sub

    ''' <summary>底层客户端（调试用）。</summary>
    Public ReadOnly Property client As ArmClient
        Get
            Return _client
        End Get
    End Property

    ' /********************************************************************************/
    '  连接
    ' /********************************************************************************/

    Public Function connect(Optional host As String = Nothing, Optional port As Double = 0) As Dictionary(Of String, Object)
        ' 幂等：命令行可能已经连上了，脚本里的 arm.connect() 不应该把连接拆掉重来
        If Not _client.Connected Then
            Call _client.Connect(host, CInt(port))
        End If

        Return ToDictionary(_client.Invoke("hello", New JsonObject()))
    End Function

    Public Function disconnect() As Dictionary(Of String, Object)
        Call _client.Close()

        Return OkMessage("disconnected")
    End Function

    Public Function state() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("state", New JsonObject()))
    End Function

    Public Function info() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("info", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  运动
    ' /********************************************************************************/

    Public Function moveJ(q As Double(), Optional speed As Double = 0.35) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movej", New JsonObject From {
            {"q", ToArray(q)},
            {"speed", speed}
        }))
    End Function

    Public Function moveJRel(dq As Double(), Optional speed As Double = 0.35) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movej_rel", New JsonObject From {
            {"dq", ToArray(dq)},
            {"speed", speed}
        }))
    End Function

    ''' <summary>
    ''' 笛卡尔直线运动（位置直线，姿态尽量保持当前值）。
    ''' </summary>
    Public Function moveL(x As Double, y As Double, z As Double,
                          Optional speed As Double = 0.25) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movel", New JsonObject From {
            {"x", x}, {"y", y}, {"z", z}, {"speed", speed}
        }))
    End Function

    ''' <summary>
    ''' 笛卡尔直线运动 + **严格保持末端姿态**（夹着工件搬运专用）。
    ''' </summary>
    ''' <remarks>
    ''' 普通 <see cref="moveL"/> 在低处解不出完整位姿时会退化成"只约束位置、姿态随动"，
    ''' 抬臂过程中夹爪会转头，方块被甩到一米之外。这个版本不会退化：
    ''' 整条路径的每个插补点都锁定当前姿态，解不出就直接报错。
    ''' </remarks>
    Public Function moveLHold(x As Double, y As Double, z As Double,
                              Optional speed As Double = 0.2) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movel", New JsonObject From {
            {"x", x}, {"y", y}, {"z", z}, {"speed", speed}, {"strict", True}
        }))
    End Function

    ''' <summary>带指定姿态（角度制 rpy）的笛卡尔直线运动。</summary>
    Public Function moveLPose(x As Double, y As Double, z As Double,
                              rx As Double, ry As Double, rz As Double,
                              Optional speed As Double = 0.2,
                              Optional strict As Boolean = True) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("movel", New JsonObject From {
            {"x", x}, {"y", y}, {"z", z},
            {"rx", rx}, {"ry", ry}, {"rz", rz},
            {"speed", speed}, {"strict", strict}
        }))
    End Function

    ''' <summary>
    ''' 点位运动（带姿态）。rpy 用角度制。
    ''' </summary>
    ''' <remarks>
    ''' 抓桌面上的积木时必须把末端转成"朝下"（ry≈90），否则夹爪是横着伸过去的，
    ''' 手指根本碰不到积木。
    ''' </remarks>
    Public Function moveTo(x As Double, y As Double, z As Double,
                           Optional rx As Double = 0, Optional ry As Double = 0, Optional rz As Double = 0,
                           Optional speed As Double = 0.3) As Dictionary(Of String, Object)

        Dim pose As New JsonObject From {
            {"x", x}, {"y", y}, {"z", z},
            {"rx", rx}, {"ry", ry}, {"rz", rz}
        }

        Return ToDictionary(_client.Invoke("move_to", New JsonObject From {
            {"pose", pose},
            {"speed", speed}
        }))
    End Function

    Public Function home(Optional speed As Double = 0.3) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("home", New JsonObject From {{"speed", speed}}))
    End Function

    ''' <summary>等待一条运动指令结束；返回 {done, success, message}。</summary>
    Public Function waitMotion(id As Double, Optional timeoutMs As Double = 30000) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("wait_motion", New JsonObject From {
            {"id", CInt(id)},
            {"timeout_ms", CInt(timeoutMs)}
        }, CInt(timeoutMs) + 5000))
    End Function

    Public Function stopMotion() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("stop", New JsonObject()))
    End Function

    Public Function estop() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("estop", New JsonObject()))
    End Function

    Public Function reset() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("reset", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  夹爪
    ' /********************************************************************************/

    Public Function grip(Optional force As Double = 35.0) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("grip", New JsonObject From {{"force", force}}))
    End Function

    Public Function release() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("release", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  场景
    ' /********************************************************************************/

    Public Function spawn(shape As String, size As Double(), position As Double(),
                          Optional mass As Double = 0.2,
                          Optional friction As Double = 0.75,
                          Optional color As String = Nothing) As Dictionary(Of String, Object)

        Dim args As New JsonObject From {
            {"shape", shape},
            {"size", ToArray(size)},
            {"position", ToArray(position)},
            {"mass", mass},
            {"friction", friction}
        }

        If Not String.IsNullOrEmpty(color) Then
            args("color") = color
        End If

        Return ToDictionary(_client.Invoke("spawn", args))
    End Function

    ''' <summary>
    ''' 生成一个**静态**道具（桌子、桌腿等）：不会被推动、也不受重力。
    ''' </summary>
    ''' <remarks>
    ''' 单独开一个方法而不是给 <see cref="spawn"/> 加可选参数：
    ''' LiteJs 通过反射调用 CLR 方法，可选参数不一定能正确填默认值，
    ''' 给已有方法加参数有把现有脚本（pick_and_place 等）调崩的风险。
    ''' </remarks>
    Public Function spawnStatic(shape As String, size As Double(), position As Double(),
                                Optional mass As Double = 50.0,
                                Optional friction As Double = 0.8,
                                Optional color As String = Nothing) As Dictionary(Of String, Object)

        Dim args As New JsonObject From {
            {"shape", shape},
            {"size", ToArray(size)},
            {"position", ToArray(position)},
            {"mass", mass},
            {"friction", friction},
            {"static", True}
        }

        If Not String.IsNullOrEmpty(color) Then
            args("color") = color
        End If

        Return ToDictionary(_client.Invoke("spawn", args))
    End Function

    Public Function objects() As List(Of Object)
        Dim result As Dictionary(Of String, Object) = ToDictionary(_client.Invoke("objects", New JsonObject()))
        Dim items As Object = Nothing

        If result.TryGetValue("objects", items) Then
            Dim list As List(Of Object) = TryCast(items, List(Of Object))

            If list IsNot Nothing Then Return list
        End If

        Return New List(Of Object)()
    End Function

    Public Function remove(id As String) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("remove", New JsonObject From {{"id", id}}))
    End Function

    Public Function clear() As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("clear", New JsonObject()))
    End Function

    ' /********************************************************************************/
    '  配置
    ' /********************************************************************************/

    Public Function config(key As String, value As Double) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("config", New JsonObject From {
            {"key", key}, {"value", value}
        }))
    End Function

    Public Function setRealtime(factor As Double) As Dictionary(Of String, Object)
        Return ToDictionary(_client.Invoke("config", New JsonObject From {
            {"key", "realtime"}, {"value", factor}
        }))
    End Function

    ''' <summary>
    ''' 弹簧木板平台：不带参数时查询，传 <c>{remove:true}</c> 拆除，否则创建/重建。
    ''' </summary>
    ''' <remarks>
    ''' 常用参数：<c>size:[w,d,thickness]</c>、<c>position:[x,y,z]</c>、<c>stiffness</c>、
    ''' <c>damping</c>、<c>springs</c>、<c>mass</c>、<c>friction</c>、<c>rest</c>。
    ''' </remarks>
    Public Function platform(Optional options As Dictionary(Of String, Object) = Nothing) As Dictionary(Of String, Object)
        Dim args As New JsonObject()

        If options IsNot Nothing Then
            For Each pair As KeyValuePair(Of String, Object) In options
                args(pair.Key) = JsonBridge.FromJs(pair.Value)
            Next
        End If

        Return ToDictionary(_client.Invoke("platform", args, 30000))
    End Function

    ''' <summary>木板倾角（度）：{tiltX, tiltY, tiltMag}，没有平台时全为 0。</summary>
    Public Function tilt() As Dictionary(Of String, Object)
        ' 注意：局部变量不能叫 state —— 会和 state() 方法同名（VB 大小写不敏感）
        Dim snapshot As Dictionary(Of String, Object) = state()
        Dim node As Object = Nothing

        Dim result As New Dictionary(Of String, Object)(StringComparer.Ordinal) From {
            {"tiltX", 0.0}, {"tiltY", 0.0}, {"tiltMag", 0.0}, {"present", False}
        }

        If snapshot Is Nothing Then Return result
        If Not snapshot.TryGetValue("platform", node) Then Return result

        Dim info As Dictionary(Of String, Object) = TryCast(node, Dictionary(Of String, Object))

        If info Is Nothing Then Return result

        Dim present As Object = Nothing

        result("present") = info.TryGetValue("present", present) AndAlso TypeOf present Is Boolean AndAlso CBool(present)
        result("tiltX") = NumberOf(info, "tiltX")
        result("tiltY") = NumberOf(info, "tiltY")
        result("tiltMag") = NumberOf(info, "tiltMag")
        result("height") = NumberOf(info, "height")

        Return result
    End Function

    Private Shared Function NumberOf(source As Dictionary(Of String, Object), key As String) As Double
        Dim value As Object = Nothing

        If Not source.TryGetValue(key, value) OrElse value Is Nothing Then Return 0.0
        If TypeOf value Is Double Then Return CDbl(value)

        Dim parsed As Double

        If Double.TryParse(value.ToString(), parsed) Then Return parsed

        Return 0.0
    End Function

    ''' <summary>通用入口：直接发一条原始指令，paramsJson 是 JSON 文本。</summary>
    Public Function send(command As String, Optional paramsJson As String = "{}") As Dictionary(Of String, Object)
        Dim args As JsonObject = Nothing

        Try
            args = TryCast(JsonNode.Parse(If(String.IsNullOrWhiteSpace(paramsJson), "{}", paramsJson)), JsonObject)
        Catch ex As Exception
            args = New JsonObject()
        End Try

        If args Is Nothing Then args = New JsonObject()

        Return ToDictionary(_client.Invoke(command, args))
    End Function

    ' /********************************************************************************/
    '  辅助
    ' /********************************************************************************/

    ' /********************************************************************************/
    '  夹爪相机
    ' /********************************************************************************/

    ''' <summary>
    ''' 取一帧夹爪第一视角图像。
    ''' </summary>
    ''' <param name="format">png（base64 PNG）或 rgb（base64 原始 RGB24）</param>
    ''' <param name="depth">留空不带深度；"f32" 或 "u16mm" 时附带深度图</param>
    Public Function frame(Optional format As String = "png",
                          Optional depth As String = Nothing) As Dictionary(Of String, Object)

        Dim args As New JsonObject From {{"format", format}}

        If Not String.IsNullOrEmpty(depth) Then args("depth") = depth

        Return ToDictionary(_client.Invoke("frame", args, 60000))
    End Function

    ''' <summary>
    ''' 取一帧并保存成 PNG（可选同时保存 .depth.f32）。
    ''' </summary>
    Public Function saveFrame(path As String, Optional depth As Boolean = False) As Dictionary(Of String, Object)
        Dim args As New JsonObject From {{"format", "png"}}

        If depth Then args("depth") = "f32"

        Dim result As Dictionary(Of String, Object) = ToDictionary(_client.Invoke("frame", args, 60000))
        Dim data As String = Nothing

        If Not result.TryGetValue("data", data) OrElse String.IsNullOrEmpty(data) Then
            Throw New InvalidOperationException("相机没有返回图像数据")
        End If

        Dim bytes As Byte() = Convert.FromBase64String(data)

        Call File.WriteAllBytes(path, bytes)

        If depth Then
            Dim node As Object = Nothing

            If result.TryGetValue("depth", node) Then
                Dim depthNode As Dictionary(Of String, Object) = TryCast(node, Dictionary(Of String, Object))
                Dim payload As String = Nothing

                If depthNode IsNot Nothing AndAlso depthNode.TryGetValue("data", payload) AndAlso
                   Not String.IsNullOrEmpty(payload) Then

                    Call File.WriteAllBytes(System.IO.Path.ChangeExtension(path, ".depth.f32"),
                                            Convert.FromBase64String(payload))
                End If
            End If
        End If

        Return New Dictionary(Of String, Object)(StringComparer.Ordinal) From {
            {"ok", True},
            {"path", path},
            {"bytes", bytes.Length}
        }
    End Function

    Public Function sleep(ms As Double) As Dictionary(Of String, Object)
        Thread.Sleep(CInt(System.Math.Max(0.0, System.Math.Min(600000.0, ms))))

        Return OkMessage("slept")
    End Function

    Public Function log(text As String) As Dictionary(Of String, Object)
        Console.WriteLine(text)

        Return OkMessage("logged")
    End Function

    Private Shared Function OkMessage(text As String) As Dictionary(Of String, Object)
        Return New Dictionary(Of String, Object)(StringComparer.Ordinal) From {{"ok", True}, {"message", text}}
    End Function

    Private Shared Function ToArray(values As Double()) As JsonArray
        Dim array As New JsonArray()

        If values IsNot Nothing Then
            For Each value As Double In values
                array.Add(JsonValue.Create(value))
            Next
        End If

        Return array
    End Function

    ''' <summary>JsonObject → Dictionary(Of String, Object)（递归），保证 JS 侧拿到原生对象。</summary>
    Private Shared Function ToDictionary(node As JsonObject) As Dictionary(Of String, Object)
        Dim value As Object = JsonBridge.ToJs(node)

        If value Is Nothing Then Return New Dictionary(Of String, Object)(StringComparer.Ordinal)

        Dim result As Dictionary(Of String, Object) = TryCast(value, Dictionary(Of String, Object))

        If result Is Nothing Then
            result = New Dictionary(Of String, Object)(StringComparer.Ordinal)
            result("value") = value
        End If

        Return result
    End Function

End Class
