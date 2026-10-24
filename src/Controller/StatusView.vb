''' <summary>把状态快照渲染成终端表格。</summary>
Public Module StatusView

    Public Sub Print(state As Dictionary(Of String, Object))
        If state Is Nothing Then
            Console.WriteLine("(无状态)")
            Return
        End If

        Dim time As Double = Num(state, "time")
        Dim stepIndex As Double = Num(state, "stepIndex")

        Console.WriteLine($"时间 {time,8:F3} s    步数 {stepIndex,10:F0}    " &
                          $"{If(Bool(state, "estop"), "急停", If(Bool(state, "enabled"), "伺服使能", "伺服失能"))}")

        Console.WriteLine()
        Console.WriteLine("  关节        角度(rad)   角速度(rad/s)   目标      限位")
        Console.WriteLine("  ------------------------------------------------------------------")

        Dim joints As List(Of Object) = ArrayOf(state, "joints")

        For Each item As Object In joints
            Dim joint As Dictionary(Of String, Object) = TryCast(item, Dictionary(Of String, Object))

            If joint Is Nothing Then Continue For

            Dim name As String = Text(joint, "name")

            Console.WriteLine($"  {name,-10} {Num(joint, "angle"),10:F4} {Num(joint, "speed"),14:F4} " &
                              $"{Num(joint, "target"),10:F4}   [{Num(joint, "lower"):F2}, {Num(joint, "upper"):F2}]")
        Next

        Dim tcp As List(Of Object) = ArrayOf(state, "tcp")

        If tcp IsNot Nothing AndAlso tcp.Count >= 6 Then
            Console.WriteLine()
            Console.WriteLine($"  TCP  x={ValueOf(tcp, 0):F4}  y={ValueOf(tcp, 1):F4}  z={ValueOf(tcp, 2):F4}" &
                              $"    rpy=({ValueOf(tcp, 3):F2}, {ValueOf(tcp, 4):F2}, {ValueOf(tcp, 5):F2})°")
        End If

        Dim gripper As Dictionary(Of String, Object) = TryCast(Item(state, "gripper"), Dictionary(Of String, Object))

        If gripper IsNot Nothing Then
            Console.WriteLine($"  夹爪 开口={Num(gripper, "opening"):F4} m  力={Num(gripper, "force"):F0} N" &
                              $"  {If(Bool(gripper, "closing"), "夹紧", "张开")}" &
                              $"{If(Bool(gripper, "contact"), "  抓住 " & Text(gripper, "holdingId"), "")}")
        End If

        Dim collisions As List(Of Object) = ArrayOf(state, "collisions")

        If collisions IsNot Nothing AndAlso collisions.Count > 0 Then
            Console.WriteLine($"  接触  {String.Join(", ", collisions.Take(8))}")
        End If

        Dim objects As List(Of Object) = ArrayOf(state, "objects")

        If objects IsNot Nothing AndAlso objects.Count > 0 Then
            Console.WriteLine()
            Console.WriteLine($"  物体 {objects.Count} 个")

            For Each item As Object In objects.Take(12)
                Dim obj As Dictionary(Of String, Object) = TryCast(item, Dictionary(Of String, Object))

                If obj Is Nothing Then Continue For

                Dim position As List(Of Object) = ArrayOf(obj, "position")
                Dim location As String = "?"

                If position IsNot Nothing AndAlso position.Count >= 3 Then
                    location = $"({ValueOf(position, 0):F3}, {ValueOf(position, 1):F3}, {ValueOf(position, 2):F3})"
                End If

                Console.WriteLine($"    {Text(obj, "id"),-10} {Text(obj, "kind"),-10} {location}")
            Next
        End If
    End Sub

    ''' <summary>watch 模式下的一行紧凑输出。</summary>
    Public Sub PrintCompact(state As Dictionary(Of String, Object))
        Dim q As List(Of Object) = ArrayOf(state, "q")
        Dim tcp As List(Of Object) = ArrayOf(state, "tcp")

        Dim builder As New System.Text.StringBuilder()

        If q IsNot Nothing Then
            For i As Integer = 0 To q.Count - 1
                If i > 0 Then builder.Append(" "c)
                builder.Append(ValueOf(q, i).ToString("F2"))
            Next
        End If

        Dim angles As String = builder.ToString()

        Dim position As String = ""

        If tcp IsNot Nothing AndAlso tcp.Count >= 3 Then
            position = $"({ValueOf(tcp, 0):F3}, {ValueOf(tcp, 1):F3}, {ValueOf(tcp, 2):F3})"
        End If

        Console.WriteLine($"t={Num(state, "time"),7:F2}  q=[{angles}]  tcp={position}")
    End Sub

    ' ---- 取值辅助 ----

    Public Function Item(source As Dictionary(Of String, Object), key As String) As Object
        If source Is Nothing Then Return Nothing

        Dim value As Object = Nothing

        If source.TryGetValue(key, value) Then Return value

        ' 兼容大小写差异
        For Each pair As KeyValuePair(Of String, Object) In source
            If String.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) Then Return pair.Value
        Next

        Return Nothing
    End Function

    Public Function Num(source As Dictionary(Of String, Object), key As String, Optional defaultValue As Double = 0) As Double
        Dim value As Object = Item(source, key)

        If value Is Nothing Then Return defaultValue
        If TypeOf value Is Double Then Return CDbl(value)

        Dim parsed As Double

        If Double.TryParse(value.ToString(), parsed) Then Return parsed

        Return defaultValue
    End Function

    Public Function Bool(source As Dictionary(Of String, Object), key As String, Optional defaultValue As Boolean = False) As Boolean
        Dim value As Object = Item(source, key)

        If value Is Nothing Then Return defaultValue
        If TypeOf value Is Boolean Then Return CBool(value)

        Return defaultValue
    End Function

    Public Function Text(source As Dictionary(Of String, Object), key As String) As String
        Dim value As Object = Item(source, key)

        If value Is Nothing Then Return ""

        Return value.ToString()
    End Function

    Public Function ArrayOf(source As Dictionary(Of String, Object), key As String) As List(Of Object)
        Return TryCast(Item(source, key), List(Of Object))
    End Function

    Public Function ValueOf(list As List(Of Object), index As Integer) As Double
        If list Is Nothing OrElse index < 0 OrElse index >= list.Count Then Return 0

        Dim value As Object = list(index)

        If value Is Nothing Then Return 0
        If TypeOf value Is Double Then Return CDbl(value)

        Dim parsed As Double

        If Double.TryParse(value.ToString(), parsed) Then Return parsed

        Return 0
    End Function

End Module
