Imports System.Runtime.CompilerServices

''' <summary>
''' 仿真库的调试输出通道：把 <c>"message".debug</c> 转发到
''' <c>Microsoft.VisualBasic.VBDebugger</c>。
''' </summary>
''' <remarks>
''' <para>
''' 转发而不是直接调用，有两个原因：
''' </para>
''' <list type="number">
''' <item><description>
''' 直接 <c>Imports Microsoft.VisualBasic</c> 会让文件里的 <c>Math</c> 解析成
''' <c>Microsoft.VisualBasic.Math</c>（没有 <c>PI</c>/<c>Sin</c>/<c>Max</c> 的那些重载），
''' 把每一处 <c>Math.</c> 都改写成别名代价太大；
''' </description></item>
''' <item><description>
''' 扩展方法定义在库自己的命名空间里，调用方（<c>MuJoCo.*</c>）不需要额外
''' <c>Imports</c> 就能用 <c>Call $"...".debug</c> 的写法。
''' </description></item>
''' </list>
''' <para>
''' 输出带上当前进程的工作集与线程数，用来观察内存是否有异常增长。
''' </para>
''' </remarks>
Public Module MjDebug

    <Extension>
    Public Sub debug(msg As String, Optional mute As Boolean = False)
        Call Microsoft.VisualBasic.VBDebugger.debug(msg, mute)
    End Sub

End Module
