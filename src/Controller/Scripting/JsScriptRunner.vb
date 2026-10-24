Imports System.IO
Imports Microsoft.VisualBasic.ApplicationServices.VM.JavaScript
Imports Microsoft.VisualBasic.ApplicationServices.VM.JavaScript.Runtime

''' <summary>
''' 用 LiteJs 执行 <c>.js</c> 演示脚本，并把虚拟硬件能力以 <c>arm</c> 全局对象注入。
''' </summary>
''' <remarks>
''' <para>
''' LiteJs 是同步的单线程树遍历解释器：<see cref="Interpreter.Run"/> 没有返回值，
''' 执行源码只能 <c>Run(Parser.Parse(src))</c>；宿主方法里做阻塞式网络 IO 是合法的。
''' </para>
''' <para>
''' 注意 <b>不能</b>把 JS 回调作为 <c>Func/Action</c> 参数传给 CLR 方法
''' （LiteJs 的 <c>ClrConvert</c> 会直接抛 DelegateParameterError），
''' 所以等待一律用 <c>arm.waitMotion(id)</c> 这种条件式同步方法。
''' </para>
''' </remarks>
Public Module JsScriptRunner

    ''' <summary>执行一段 JS 源码，返回进程退出码。</summary>
    Public Function Run(client As ArmClient, source As String, scriptName As String,
                        Optional scriptArgs As String() = Nothing) As Integer

        If String.IsNullOrWhiteSpace(source) Then
            Console.Error.WriteLine("脚本内容为空")
            Return 1
        End If

        Dim engine As New Interpreter(New ConsoleIO())
        Dim host As New ArmJsHost(client)

        engine.DefineGlobal("arm", host)

        If scriptArgs IsNot Nothing Then
            Dim list As New List(Of Object)()

            For Each item As String In scriptArgs
                list.Add(item)
            Next

            engine.DefineGlobal("args", list)
        End If

        Try
            engine.Run(Parser.Parse(source))
            Return 0
        Catch ex As ParseException
            Console.Error.WriteLine($"{scriptName}: SyntaxError: {ex.Message}")

            If ex.HasPosition Then
                Console.Error.WriteLine($"    at {scriptName}:{ex.Line}:{ex.Column}")
            End If

            Return 1
        Catch ex As JsRuntimeException
            Console.Error.WriteLine("Uncaught " & If(String.IsNullOrEmpty(ex.Stack), ex.Message, ex.Stack))
            Return 1
        Catch ex As Exception
            Console.Error.WriteLine($"{scriptName}: 执行失败: {ex.Message}")
            Return 1
        End Try
    End Function

    ''' <summary>从文件执行 JS 脚本。</summary>
    Public Function RunFile(client As ArmClient, path As String,
                            Optional scriptArgs As String() = Nothing) As Integer

        If String.IsNullOrEmpty(path) Then
            Console.Error.WriteLine("找不到脚本: （路径为空）")
            Return 1
        End If

        If Not File.Exists(path) Then
            Console.Error.WriteLine("找不到脚本: " & path)
            Return 1
        End If

        Dim source As String = File.ReadAllText(path)

        Return Run(client, source, System.IO.Path.GetFileName(path), scriptArgs)
    End Function

    ''' <summary>
    ''' 去掉首尾成对的引号并 Trim。
    ''' </summary>
    ''' <remarks>
    ''' REPL 是按空格切分命令的，用户输入 <c>js "scripts\a.js"</c> 时引号会留在参数里，
    ''' 直接拿去 <c>File.Exists</c> 必然失败。
    ''' </remarks>
    Public Function NormalizePath(name As String) As String
        If String.IsNullOrEmpty(name) Then Return ""

        Dim result As String = name.Trim()

        If result.Length >= 2 Then
            Dim first As Char = result(0)
            Dim last As Char = result(result.Length - 1)

            If (first = """"c AndAlso last = """"c) OrElse (first = "'"c AndAlso last = "'"c) Then
                result = result.Substring(1, result.Length - 2).Trim()
            End If
        End If

        Return result
    End Function

    ''' <summary>在常见位置查找脚本（当前目录 → 输出目录 → scripts 子目录）。</summary>
    Public Function ResolveScript(name As String) As String
        name = NormalizePath(name)

        If name.Length = 0 Then Return Nothing
        If File.Exists(name) Then Return System.IO.Path.GetFullPath(name)

        ' 允许只写脚本名（js joint_sweep 自动补 .js）
        If Not name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) Then
            Dim withExtension As String = ResolveScript(name & ".js")

            If withExtension IsNot Nothing Then Return withExtension
        End If

        Dim bases As String() = {
            System.Environment.CurrentDirectory,
            AppDomain.CurrentDomain.BaseDirectory,
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"),
            System.IO.Path.Combine(System.Environment.CurrentDirectory, "scripts")
        }

        For Each basePath As String In bases
            Dim candidate As String = System.IO.Path.Combine(basePath, name)

            If File.Exists(candidate) Then Return System.IO.Path.GetFullPath(candidate)
        Next

        ' 只给了文件名时，再逐个扫一遍 scripts 目录（忽略大小写）
        If Not name.Contains("\"c) AndAlso Not name.Contains("/"c) Then
            For Each folder As String In {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"),
                System.IO.Path.Combine(System.Environment.CurrentDirectory, "scripts")
            }
                If Not Directory.Exists(folder) Then Continue For

                For Each file As String In Directory.GetFiles(folder, "*.js")
                    If String.Equals(System.IO.Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase) Then
                        Return System.IO.Path.GetFullPath(file)
                    End If
                Next
            Next
        End If

        Return Nothing
    End Function

    ''' <summary>列出随程序发布的示例脚本。</summary>
    Public Function ListScripts() As String()
        Dim folders As String() = {
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"),
            System.IO.Path.Combine(System.Environment.CurrentDirectory, "scripts")
        }

        Dim found As New List(Of String)()

        For Each folder As String In folders
            If Not Directory.Exists(folder) Then Continue For

            For Each file As String In Directory.GetFiles(folder, "*.js")
                Dim name As String = System.IO.Path.GetFileName(file)

                If Not found.Contains(name) Then found.Add(name)
            Next
        Next

        found.Sort(StringComparer.OrdinalIgnoreCase)

        Return found.ToArray()
    End Function

End Module
