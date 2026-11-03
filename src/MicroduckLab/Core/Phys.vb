Namespace Core

    ''' <summary>
    ''' 应用层的几个纯数学小工具。
    ''' </summary>
    ''' <remarks>
    ''' 原来这里还负责 <c>System.Numerics</c> 与物理内核 <c>Math3D</c> 之间的
    ''' 向量/四元数/惯量张量转换。仿真改成用 <c>mjModel</c>/<c>mjData</c> 之后，
    ''' 那些转换全部搬进了 <c>MuJoCo.MjMath</c>（库内部的数学边界），
    ''' 应用层不再直接接触引擎的类型，这里只剩与引擎无关的夹紧函数。
    ''' </remarks>
    Public Module Phys

        Public Function Clamp(value As Double, low As Double, high As Double) As Double
            If value < low Then Return low
            If value > high Then Return high

            Return value
        End Function

    End Module
End Namespace
