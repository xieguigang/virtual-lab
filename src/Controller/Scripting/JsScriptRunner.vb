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

        If Not File.Exists(path) Then
            Console.Error.WriteLine("找不到脚本: " & path)
            Return 1
        End If

        Dim source As String = File.ReadAllText(path)

        Return Run(client, source, System.IO.Path.GetFileName(path), scriptArgs)
    End Function

    ''' <summary>在常见位置查找脚本（当前目录 → 输出目录 → scripts 子目录）。</summary>
    Public Function ResolveScript(name As String) As String
        If String.IsNullOrEmpty(name) Then Return Nothing
        If File.Exists(name) Then Return Path.GetFullPath(name)

        Dim bases As String() = {
            System.Environment.CurrentDirectory,
            AppDomain.CurrentDomain.BaseDirectory,
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"),
            System.IO.Path.Combine(System.Environment.CurrentDirectory, "scripts")
        }

        For Each basePath As String In bases
            Dim candidate As String = System.IO.Path.Combine(basePath, name)

            If File.Exists(candidate) Then Return Path.GetFullPath(candidate)
        Next

        Return Nothing
    End Function

End Module
