Imports System.Threading.Tasks
Imports Ollama
Imports Ollama.JSON.FunctionCall

''' <summary>
''' 用 LLM 驱动虚拟实验室机械臂的智能体。
''' </summary>
''' <remarks>
''' <para>
''' <see cref="Ollama.LLMClient.Chat"/> 本身带工具调用循环（<c>maxRounds</c> 轮），
''' 我们只需要提供三样东西：
''' </para>
''' <list type="number">
''' <item>工具定义（<see cref="LabTools.Catalog"/>）</item>
''' <item>工具执行器（<see cref="Ollama.LLMClient.tool_invoke"/> → <see cref="LabTools.Invoke"/>）</item>
''' <item>系统提示（世界模型 + 工作流 + 安全约束）</item>
''' </list>
''' <para>
''' 现阶段只把**世界信息**（物体位置/姿态/桌面高度）交给模型，不接图像；
''' 以后加视觉只要多注册一个 <c>look()</c> 工具即可，循环本身不用改。
''' </para>
''' </remarks>
Public Class LabAgent

    Public Property Client As LLMClient
    Public Property Tools As LabTools
    Public Property Config As LLMConfig

    ''' <summary>是否把每次工具调用打印到控制台。</summary>
    Public Property Verbose As Boolean = True

    ''' <summary>已执行的工具调用次数。</summary>
    Public Property ToolCalls As Integer

    ''' <summary>
    ''' 从 ini 配置创建智能体。
    ''' </summary>
    ''' <param name="arm">已连接的 JS 宿主（内部复用同一套 TCP API）</param>
    ''' <param name="configPath">例如 <c>C:\Users\Administrator\Documents\deepseek.ini</c></param>
    Public Shared Function Create(arm As ArmJsHost, configPath As String) As LabAgent
        If Not IO.File.Exists(configPath) Then
            Throw New IO.FileNotFoundException($"找不到 LLM 配置文件: {configPath}", configPath)
        End If

        Dim config As LLMConfig = LLMConfig.Load(configPath)
        Dim client As LLMClient = config.CreateLLm()
        Dim tools As New LabTools(arm)
        Dim agent As New LabAgent With {
            .Client = client,
            .Tools = tools,
            .Config = config
        }

        ' ini 里的 max_rounds 决定一次任务最多多少轮工具调用
        Dim rounds As String = ReadIni(configPath, "max_rounds")

        If Not String.IsNullOrEmpty(rounds) Then
            Dim parsed As Integer

            If Integer.TryParse(rounds, parsed) Then
                client.maxRounds = System.Math.Max(4, System.Math.Min(400, parsed))
            End If
        End If

        client.system_message = SystemPrompt
        client.tools = tools.Catalog().ToList()
        client.tool_invoke = Function(fc) agent.InvokeTool(fc)

        Return agent
    End Function

    ''' <summary>执行一次任务，返回模型的最终答复。</summary>
    Public Function Run(task As String) As String
        If String.IsNullOrEmpty(task) Then Return "(空任务)"

        Dim response As LLMsResponse = Client.Chat(task).GetAwaiter().GetResult()

        Return If(response Is Nothing, "(无响应)", response.output)
    End Function

    ''' <summary>工具执行的统一入口：打印日志、捕获异常、统计次数。</summary>
    Private Function InvokeTool(fc As FunctionCall) As String
        ToolCalls += 1

        Dim arguments As String = ""

        If fc IsNot Nothing AndAlso fc.arguments IsNot Nothing Then
            arguments = String.Join(", ", fc.arguments.Select(Function(a) $"{a.Key}={a.Value}"))
        End If

        If Verbose Then
            Call Console.WriteLine($"   #{ToolCalls,-3} {fc?.name}({TrimTo(arguments, 120)})")
        End If

        Dim result As String

        Try
            result = Tools.Invoke(fc)
        Catch ex As Exception
            result = "[error] " & ex.Message
        End Try

        If Verbose Then
            Call Console.WriteLine("        → " & TrimTo(FirstLines(result, 6), 400))
        End If

        Return result
    End Function

    ' /********************************************************************************/
    '  提示词
    ' /********************************************************************************/

    ''' <summary>系统提示：告诉模型坐标系、工具工作流与安全约束。</summary>
    Public Shared ReadOnly Property SystemPrompt As String = String.Join(Environment.NewLine, {
        "你是虚拟实验室里的机械臂操作员。你通过工具调用控制一台六轴机械臂，把三角瓶从左边桌子搬到右边桌子上。",
        "",
        "【坐标系与单位】",
        "- 世界坐标 Z 轴向上，单位：米 / 度。",
        "- 机械臂基座在原点，水平臂展约 0.6 m。",
        "- 所有运动工具都把末端姿态固定为「朝下」（ry=90°）。",
        "- 夹爪手指中心在 TCP 下方 0.027 m；所以抓取时 TCP 高度 = 物体中心 z + 0.027。",
        "",
        "【世界信息】",
        "- 每次行动前后都调用 world，它会返回 TCP、关节角、夹爪开度，以及所有物体：",
        "  id、kind（flask/box/flask…）、pos、size、tilt、posture（upright/tilted/fall_over）、fallen、mass。",
        "- static=true 的 box 是桌子，它下面一行会给出「顶面 z」，摆瓶子时要知道这个高度。",
        "- 三角瓶的 pos 是**质心**；瓶高 0.13 m，所以瓶底 = pos.z - 0.065。",
        "",
        "【推荐工作流】",
        "1. world 读取场景，记下左侧桌子 id、右侧桌子 id、以及 5 个 flask 的 id 与位置。",
        "2. 对每个瓶子：pick_object(id) → place_on_table(右侧桌子 id, dx, dy) → wait(900) → world 复核。",
        "3. 复核标准：该 flask 的 fallen=false 且 posture 是 upright 或 tilted，并且 x/y 落在右桌范围内。",
        "4. 全部搬完后，用一句中文总结：成功几个、倒了几个、掉地几个。",
        "",
        "【安全约束（必须遵守）】",
        "- 水平移动一律在 z≈0.44 以上完成；不要在低处用 move_to 做水平移动，否则手肘会压到桌面。",
        "- 一次只搬一个瓶子；抓取前先 release 让夹爪张开。",
        "- 抓取失败（grip 返回 contact=false）时不要硬搬，先报告。",
        "- 动作失败（工具返回 [error]）时先 world 看实际情况，再决定重试或放弃，最多重试 2 次。",
        "- 模型不允许凭空捏造结果：所有「成功/失败」都必须来自 world 的真实读数。"
    })

    ' /********************************************************************************/
    '  辅助
    ' /********************************************************************************/

    Private Shared Function ReadIni(path As String, key As String) As String
        Try
            Dim currentKey As String = ""

            For Each line As String In IO.File.ReadAllLines(path)
                Dim text As String = line.Trim()

                If text.StartsWith("[") Then
                    currentKey = text.Trim("[", "]")
                    Continue For
                End If

                If Not String.Equals(currentKey, "llm", StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim index As Integer = text.IndexOf("="c)

                If index <= 0 Then Continue For

                If String.Equals(text.Substring(0, index).Trim(), key, StringComparison.OrdinalIgnoreCase) Then
                    Return text.Substring(index + 1).Trim()
                End If
            Next
        Catch ex As Exception
            ' 读不到就用默认值
        End Try

        Return Nothing
    End Function

    Private Shared Function FirstLines(text As String, count As Integer) As String
        If String.IsNullOrEmpty(text) Then Return "(空)"

        Dim lines As String() = text.Replace(vbCrLf, vbLf).Split(ControlChars.Lf)

        If lines.Length <= count Then Return text

        Return String.Join(vbLf, lines.Take(count)) & vbLf & "..."
    End Function

    Private Shared Function TrimTo(text As String, limit As Integer) As String
        If String.IsNullOrEmpty(text) Then Return ""

        If text.Length <= limit Then Return text

        Return text.Substring(0, limit) & "…"
    End Function

End Class
