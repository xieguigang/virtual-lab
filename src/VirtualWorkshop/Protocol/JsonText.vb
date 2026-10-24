Imports System.Text.Encodings.Web
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Text.Json.Serialization

Namespace Protocol

    ''' <summary>System.Text.Json 的读写助手：属性名转小驼峰、忽略 null、允许中文原样输出。</summary>
    Public Module JsonText

        Public ReadOnly Options As New JsonSerializerOptions With {
            .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            .PropertyNameCaseInsensitive = True,
            .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            .Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            .NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        }

        Public Function NodeOf(value As Object) As JsonNode
            If value Is Nothing Then Return Nothing

            Return JsonSerializer.SerializeToNode(value, value.GetType(), Options)
        End Function

        Public Function Stringify(node As JsonNode) As String
            If node Is Nothing Then Return "null"

            Return node.ToJsonString(Options)
        End Function

        Public Function Parse(text As String) As JsonNode
            If String.IsNullOrWhiteSpace(text) Then Return Nothing

            Return JsonNode.Parse(text)
        End Function

        ' ---- 取值 ----

        Public Function Str(source As JsonObject, name As String, Optional defaultValue As String = Nothing) As String
            If source Is Nothing Then Return defaultValue

            Dim node As JsonNode = Nothing

            If Not source.TryGetPropertyValue(name, node) Then Return defaultValue
            If node Is Nothing Then Return defaultValue

            Try
                Return node.GetValue(Of String)()
            Catch ex As Exception
                Return node.ToString()
            End Try
        End Function

        Public Function Num(source As JsonObject, name As String, Optional defaultValue As Double = 0.0) As Double
            If source Is Nothing Then Return defaultValue

            Dim node As JsonNode = Nothing

            If Not source.TryGetPropertyValue(name, node) Then Return defaultValue
            If node Is Nothing Then Return defaultValue

            Dim value As Double

            If Double.TryParse(node.ToString(), Globalization.NumberStyles.Float,
                               Globalization.CultureInfo.InvariantCulture, value) Then
                Return value
            End If

            Return defaultValue
        End Function

        Public Function Bool(source As JsonObject, name As String, Optional defaultValue As Boolean = False) As Boolean
            If source Is Nothing Then Return defaultValue

            Dim node As JsonNode = Nothing

            If Not source.TryGetPropertyValue(name, node) Then Return defaultValue
            If node Is Nothing Then Return defaultValue

            Dim text As String = node.ToString()

            If String.Equals(text, "true", StringComparison.OrdinalIgnoreCase) Then Return True
            If String.Equals(text, "false", StringComparison.OrdinalIgnoreCase) Then Return False

            Return defaultValue
        End Function

        ''' <summary>读取一个数值数组；也接受单个数字（返回单元素数组）。</summary>
        Public Function Numbers(source As JsonObject, name As String) As Double()
            If source Is Nothing Then Return Nothing

            Dim node As JsonNode = Nothing

            If Not source.TryGetPropertyValue(name, node) Then Return Nothing
            If node Is Nothing Then Return Nothing

            Dim array As JsonArray = TryCast(node, JsonArray)

            If array Is Nothing Then
                Return {Num(source, name)}
            End If

            Dim out(array.Count - 1) As Double

            For i As Integer = 0 To array.Count - 1
                Dim value As Double

                If Double.TryParse(array(i)?.ToString(), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, value) Then
                    out(i) = value
                Else
                    out(i) = 0.0
                End If
            Next

            Return out
        End Function

        Public Function ObjectOf(source As JsonObject, name As String) As JsonObject
            If source Is Nothing Then Return Nothing

            Dim node As JsonNode = Nothing

            If Not source.TryGetPropertyValue(name, node) Then Return Nothing

            Return TryCast(node, JsonObject)
        End Function

        ''' <summary>把数值数组打包成 JSON 数组。</summary>
        Public Function ArrayOf(values As Double()) As JsonArray
            Dim array As New JsonArray()

            If values IsNot Nothing Then
                For Each value As Double In values
                    array.Add(JsonValue.Create(value))
                Next
            End If

            Return array
        End Function

        ''' <summary>把数值数组打包成 JSON 数组。</summary>
        Public Function ArrayOf(values As Integer()) As JsonArray
            Dim array As New JsonArray()

            If values IsNot Nothing Then
                For Each value As Integer In values
                    array.Add(JsonValue.Create(value))
                Next
            End If

            Return array
        End Function

    End Module

End Namespace
