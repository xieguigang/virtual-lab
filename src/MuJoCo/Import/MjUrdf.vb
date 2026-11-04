Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Xml.Linq
Imports num = System.Numerics

Namespace Import

    ''' <summary>
    ''' 轻量 URDF 读取器：把 link / joint 骨架映射成中性的 <see cref="Scene.MjSceneSpec"/>，
    ''' 供宿主用 MuJoCo 的 MJCF 编译链路装配模型。
    ''' </summary>
    ''' <remarks>
    ''' 只解析运动学与惯性所需的最小子集（origin、axis、limit、inertial、box 碰撞尺寸）；
    ''' GLB/STL 网格仍由宿主的资产层注入，避免给 net10.0 类库引入 imaging 依赖。
    ''' </remarks>
    Public Module MjUrdf

        Public Function Read(path As String) As Scene.MjSceneSpec
            If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then
                Throw New FileNotFoundException("the URDF file could not be found", path)
            End If

            Return ReadDocument(XDocument.Load(path))
        End Function

        Public Function ReadXml(xml As String) As Scene.MjSceneSpec
            If String.IsNullOrWhiteSpace(xml) Then Throw New ArgumentException("xml is empty", NameOf(xml))

            Return ReadDocument(XDocument.Parse(xml))
        End Function

        Private Function ReadDocument(doc As XDocument) As Scene.MjSceneSpec
            Dim scene As New Scene.MjSceneSpec()
            Dim robot As XElement = If(doc Is Nothing, Nothing, doc.Root)

            If robot Is Nothing Then Return scene

            Dim parentOf As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Dim joints As New List(Of XElement)()

            For Each jointElement As XElement In robot.Elements("joint")
                Dim parent As String = ChildAttr(jointElement, "parent", "link")
                Dim child As String = ChildAttr(jointElement, "child", "link")

                If String.IsNullOrEmpty(child) Then Continue For

                ' fixed 关节只提供父子关系，不对应任何 MJCF 自由度
                If Not String.IsNullOrEmpty(parent) Then parentOf(child) = parent

                If String.Equals(Attr(jointElement, "type"), "fixed",
                                 StringComparison.OrdinalIgnoreCase) Then Continue For

                joints.Add(jointElement)
            Next

            For Each link As XElement In robot.Elements("link")
                Dim name As String = Attr(link, "name")

                If String.IsNullOrEmpty(name) Then Continue For

                Dim parent As String = Nothing
                If parentOf.ContainsKey(name) Then parent = parentOf(name)

                Dim body As Scene.MjBodySpec = scene.AddBody(name, parent)

                Dim inertial As XElement = link.Element("inertial")

                If inertial IsNot Nothing Then
                    body.Mass = ParseNumber(ChildAttr(inertial, "mass", "value"), 0.0)

                    Dim origin As XElement = inertial.Element("origin")
                    If origin IsNot Nothing Then body.Pos = Vec3(Attr(origin, "xyz"))

                    Dim tensor As XElement = inertial.Element("inertia")

                    If tensor IsNot Nothing Then
                        body.Inertia = New Double() {
                            ParseNumber(Attr(tensor, "ixx"), 0.0),
                            ParseNumber(Attr(tensor, "iyy"), 0.0),
                            ParseNumber(Attr(tensor, "izz"), 0.0),
                            ParseNumber(Attr(tensor, "ixy"), 0.0),
                            ParseNumber(Attr(tensor, "ixz"), 0.0),
                            ParseNumber(Attr(tensor, "iyz"), 0.0)
                        }
                    End If
                End If

                Dim box As XElement = link.Descendants("box").FirstOrDefault()

                If box IsNot Nothing Then
                    Dim size As num.Vector3 = Vec3(Attr(box, "size"))

                    body.GeomType = mjtGeom.mjGEOM_BOX
                    body.Extents = New num.Vector3(size.X * 0.5F, size.Y * 0.5F, size.Z * 0.5F)
                End If
            Next

            For Each jointElement As XElement In joints
                Dim name As String = Attr(jointElement, "name")
                Dim child As String = ChildAttr(jointElement, "child", "link")

                If String.IsNullOrEmpty(name) OrElse String.IsNullOrEmpty(child) Then Continue For

                Dim type As mjtJoint = mjtJoint.mjJNT_HINGE

                If String.Equals(Attr(jointElement, "type"), "prismatic", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(Attr(jointElement, "type"), "linear", StringComparison.OrdinalIgnoreCase) Then
                    type = mjtJoint.mjJNT_SLIDE
                ElseIf String.Equals(Attr(jointElement, "type"), "floating", StringComparison.OrdinalIgnoreCase) Then
                    type = mjtJoint.mjJNT_FREE
                End If

                Dim joint As Scene.MjJointSpec = scene.AddJoint(name, child, type)

                Dim axisElement As XElement = jointElement.Element("axis")
                If axisElement IsNot Nothing Then joint.Axis = Vec3(Attr(axisElement, "xyz"))

                Dim origin As XElement = jointElement.Element("origin")
                If origin IsNot Nothing Then joint.Pos = Vec3(Attr(origin, "xyz"))

                Dim limit As XElement = jointElement.Element("limit")

                If limit IsNot Nothing Then
                    joint.Lower = ParseNumber(Attr(limit, "lower"), -Math.PI)
                    joint.Upper = ParseNumber(Attr(limit, "upper"), Math.PI)
                End If
            Next

            Return scene
        End Function

        Private Function Attr(element As XElement, name As String) As String
            If element Is Nothing Then Return Nothing

            Dim attribute As XAttribute = element.Attribute(name)

            Return If(attribute Is Nothing, Nothing, attribute.Value)
        End Function

        Private Function ChildAttr(element As XElement, childName As String, attrName As String) As String
            If element Is Nothing Then Return Nothing

            Return Attr(element.Element(childName), attrName)
        End Function

        Private Function ParseNumber(text As String, fallback As Double) As Double
            Dim value As Double

            If Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then
                Return value
            End If

            Return fallback
        End Function

        Private Function Vec3(text As String) As num.Vector3
            If String.IsNullOrWhiteSpace(text) Then Return num.Vector3.Zero

            Dim parts As String() = text.Split(New Char() {" "c, ","c, ";"c},
                                               StringSplitOptions.RemoveEmptyEntries)

            If parts.Length < 3 Then Return num.Vector3.Zero

            Return New num.Vector3(CSng(ParseNumber(parts(0), 0.0)),
                                   CSng(ParseNumber(parts(1), 0.0)),
                                   CSng(ParseNumber(parts(2), 0.0)))
        End Function

    End Module

End Namespace
