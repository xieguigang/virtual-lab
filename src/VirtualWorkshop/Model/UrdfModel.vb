Imports System.Globalization
Imports System.IO
Imports System.Xml.Linq
Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Model

    ''' <summary>URDF 中的一个连杆（link）：质量、惯量、碰撞盒与显示颜色。</summary>
    Public Class UrdfLink

        Public Property Name As String

        ''' <summary>质量（kg）。</summary>
        Public Property Mass As Double = 1.0

        ''' <summary>质心在 link 局部系中的位置。</summary>
        Public Property InertialOrigin As Vector3 = Vector3.Zero

        ''' <summary>主惯量 (ixx, iyy, izz)。</summary>
        Public Property Inertia As Vector3 = New Vector3(0.01, 0.01, 0.01)

        ''' <summary>碰撞盒尺寸 (width, height, depth)。</summary>
        Public Property CollisionSize As Vector3 = New Vector3(0.1, 0.1, 0.1)

        ''' <summary>碰撞盒中心在 link 局部系中的位置。</summary>
        Public Property CollisionOrigin As Vector3 = Vector3.Zero

        ''' <summary>显示颜色（hex），来自 URDF 的 material color。</summary>
        Public Property VisualColor As String

        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    ''' <summary>URDF 中的一个关节。</summary>
    Public Class UrdfJoint

        Public Property Name As String

        ''' <summary>关节类型：revolute / continuous / fixed / prismatic。</summary>
        Public Property Kind As String = "revolute"

        Public Property Parent As String
        Public Property Child As String

        ''' <summary>关节原点（父 link 局部系）。</summary>
        Public Property Origin As Vector3 = Vector3.Zero

        ''' <summary>关节原点的 rpy（弧度）。</summary>
        Public Property Rpy As Vector3 = Vector3.Zero

        ''' <summary>转轴（子 link 局部系；URDF 约定两侧相同方向）。</summary>
        Public Property Axis As Vector3 = New Vector3(0, 0, 1)

        Public Property Lower As Double = -System.Math.PI
        Public Property Upper As Double = System.Math.PI

        ''' <summary>最大力矩（N·m）。</summary>
        Public Property Effort As Double = 100.0

        ''' <summary>最大角速度（rad/s）。</summary>
        Public Property Velocity As Double = 2.0

        ''' <summary>关节阻尼。</summary>
        Public Property Damping As Double = 0.1

        ''' <summary>关节库仑摩擦。</summary>
        Public Property Friction As Double = 0.0

        Public ReadOnly Property IsFixed As Boolean
            Get
                Return String.Equals(Kind, "fixed", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public ReadOnly Property IsPrismatic As Boolean
            Get
                Return String.Equals(Kind, "prismatic", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Name} ({Parent} -> {Child})"
        End Function
    End Class

    ''' <summary>
    ''' URDF 机器人的最小完整模型：link / joint / 限位 / 惯量 / 碰撞盒。
    ''' </summary>
    ''' <remarks>
    ''' 本类只做解析与静态数据，不包含任何物理或渲染对象，因此可被 GUI 与 headless 共用。
    ''' 整个工程的世界坐标统一为 **Z-up**（与 URDF 一致），单位米 / 弧度。
    ''' </remarks>
    Public Class UrdfRobot

        Public Property Name As String

        Public ReadOnly Property Links As New List(Of UrdfLink)()
        Public ReadOnly Property Joints As New List(Of UrdfJoint)()

        Public Function FindLink(linkName As String) As UrdfLink
            For Each link As UrdfLink In Links
                If String.Equals(link.Name, linkName, StringComparison.OrdinalIgnoreCase) Then
                    Return link
                End If
            Next

            Return Nothing
        End Function

        Public Function FindJoint(jointName As String) As UrdfJoint
            For Each joint As UrdfJoint In Joints
                If String.Equals(joint.Name, jointName, StringComparison.OrdinalIgnoreCase) Then
                    Return joint
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>没有任何关节把它当作 child 的 link（机械臂基座）。</summary>
        Public ReadOnly Property RootLink As UrdfLink
            Get
                Dim children As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                For Each joint As UrdfJoint In Joints
                    If Not String.IsNullOrEmpty(joint.Child) Then
                        children.Add(joint.Child)
                    End If
                Next

                For Each link As UrdfLink In Links
                    If Not children.Contains(link.Name) Then
                        Return link
                    End If
                Next

                Return If(Links.Count > 0, Links(0), Nothing)
            End Get
        End Property

        ''' <summary>所有可动关节（保持 URDF 中的声明顺序）。</summary>
        Public ReadOnly Property MovableJoints As List(Of UrdfJoint)
            Get
                Dim out As New List(Of UrdfJoint)()

                For Each joint As UrdfJoint In Joints
                    If Not joint.IsFixed Then out.Add(joint)
                Next

                Return out
            End Get
        End Property

        ''' <summary>以 child link 名为键查找驱动它的关节。</summary>
        Public Function JointOfChild(childName As String) As UrdfJoint
            For Each joint As UrdfJoint In Joints
                If String.Equals(joint.Child, childName, StringComparison.OrdinalIgnoreCase) Then
                    Return joint
                End If
            Next

            Return Nothing
        End Function

        Public Shared Function Load(path As String) As UrdfRobot
            Return Parse(File.ReadAllText(path))
        End Function

        Public Shared Function Parse(xml As String) As UrdfRobot
            If String.IsNullOrWhiteSpace(xml) Then
                Throw New ArgumentException("URDF 内容为空")
            End If

            Dim doc As XDocument = XDocument.Parse(xml)
            Dim root As XElement = doc.Root

            If root Is Nothing OrElse Not String.Equals(root.Name.LocalName, "robot", StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("不是有效的 URDF：根元素不是 <robot>")
            End If

            Dim robot As New UrdfRobot With {
                .Name = AttributeValue(root, "name", "robot")
            }

            ' ---- links ----
            For Each element As XElement In root.Elements()
                If Not String.Equals(element.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim link As New UrdfLink With {
                    .Name = AttributeValue(element, "name", "link")
                }

                Dim inertial As XElement = Child(element, "inertial")

                If inertial IsNot Nothing Then
                    link.InertialOrigin = VectorOf(Child(inertial, "origin"), "xyz")

                    Dim mass As XElement = Child(inertial, "mass")
                    If mass IsNot Nothing Then
                        link.Mass = ParseNumber(AttributeValue(mass, "value", "1"), 1.0)
                    End If

                    Dim inertia As XElement = Child(inertial, "inertia")
                    If inertia IsNot Nothing Then
                        link.Inertia = New Vector3(
                            ParseNumber(AttributeValue(inertia, "ixx", "0.01"), 0.01),
                            ParseNumber(AttributeValue(inertia, "iyy", "0.01"), 0.01),
                            ParseNumber(AttributeValue(inertia, "izz", "0.01"), 0.01))
                    End If
                End If

                Dim collision As XElement = Child(element, "collision")

                If collision IsNot Nothing Then
                    link.CollisionOrigin = VectorOf(Child(collision, "origin"), "xyz")

                    Dim box As XElement = Child(Child(collision, "geometry"), "box")
                    If box IsNot Nothing Then
                        link.CollisionSize = VectorOf(box, "size")
                    End If
                End If

                Dim visual As XElement = Child(element, "visual")

                If visual IsNot Nothing Then
                    Dim color As XElement = Child(Child(Child(visual, "material"), "color"), "rgba")

                    If color IsNot Nothing Then
                        link.VisualColor = RgbaToHex(color.Value)
                    End If
                End If

                robot.Links.Add(link)
            Next

            ' ---- joints ----
            For Each element As XElement In root.Elements()
                If Not String.Equals(element.Name.LocalName, "joint", StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim joint As New UrdfJoint With {
                    .Name = AttributeValue(element, "name", "joint"),
                    .Kind = AttributeValue(element, "type", "revolute"),
                    .Parent = AttributeValue(Child(element, "parent"), "link", ""),
                    .Child = AttributeValue(Child(element, "child"), "link", "")
                }

                Dim origin As XElement = Child(element, "origin")
                joint.Origin = VectorOf(origin, "xyz")
                joint.Rpy = VectorOf(origin, "rpy")
                joint.Axis = VectorOf(Child(element, "axis"), "xyz", New Vector3(0, 0, 1))

                Dim limit As XElement = Child(element, "limit")

                If limit IsNot Nothing Then
                    joint.Lower = ParseNumber(AttributeValue(limit, "lower", "-3.14159265358979"), -System.Math.PI)
                    joint.Upper = ParseNumber(AttributeValue(limit, "upper", "3.14159265358979"), System.Math.PI)
                    joint.Effort = ParseNumber(AttributeValue(limit, "effort", "100"), 100.0)
                    joint.Velocity = ParseNumber(AttributeValue(limit, "velocity", "2"), 2.0)
                End If

                Dim dynamics As XElement = Child(element, "dynamics")

                If dynamics IsNot Nothing Then
                    joint.Damping = ParseNumber(AttributeValue(dynamics, "damping", "0.1"), 0.1)
                    joint.Friction = ParseNumber(AttributeValue(dynamics, "friction", "0"), 0.0)
                End If

                robot.Joints.Add(joint)
            Next

            If robot.Links.Count = 0 Then
                Throw New InvalidDataException("URDF 中没有任何 <link>")
            End If

            Return robot
        End Function

        ' /********************************************************************************/
        '  解析辅助
        ' /********************************************************************************/

        Private Shared Function Child(element As XElement, localName As String) As XElement
            If element Is Nothing Then Return Nothing

            For Each item As XElement In element.Elements()
                If String.Equals(item.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase) Then
                    Return item
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function AttributeValue(element As XElement, name As String, defaultValue As String) As String
            If element Is Nothing Then Return defaultValue

            Dim attribute As XAttribute = element.Attribute(name)

            If attribute Is Nothing Then Return defaultValue

            Return attribute.Value
        End Function

        ''' <summary>把 "x y z" 形式的属性解析成向量。</summary>
        Private Shared Function VectorOf(element As XElement, name As String,
                                         Optional defaultValue As Vector3 = Nothing) As Vector3
            If element Is Nothing Then
                Return If(defaultValue, Vector3.Zero)
            End If

            Dim attribute As XAttribute = element.Attribute(name)

            If attribute Is Nothing OrElse String.IsNullOrWhiteSpace(attribute.Value) Then
                Return If(defaultValue, Vector3.Zero)
            End If

            Dim parts As String() = attribute.Value.Split(New Char() {" "c, ControlChars.Tab, ","c},
                                                        StringSplitOptions.RemoveEmptyEntries)

            If parts.Length < 3 Then
                Return If(defaultValue, Vector3.Zero)
            End If

            Return New Vector3(ParseNumber(parts(0), 0.0),
                               ParseNumber(parts(1), 0.0),
                               ParseNumber(parts(2), 0.0))
        End Function

        Private Shared Function ParseNumber(text As String, defaultValue As Double) As Double
            Dim value As Double

            If Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then
                Return value
            End If

            Return defaultValue
        End Function

        Private Shared Function RgbaToHex(rgba As String) As String
            Dim parts As String() = rgba.Split(New Char() {" "c, ControlChars.Tab, ","c},
                                               StringSplitOptions.RemoveEmptyEntries)

            If parts.Length < 3 Then Return "#C0C0C0"

            Dim r As Double = ParseNumber(parts(0), 0.75)
            Dim g As Double = ParseNumber(parts(1), 0.75)
            Dim b As Double = ParseNumber(parts(2), 0.75)

            Return $"#{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}"
        End Function

        Private Shared Function ToByte(value As Double) As Integer
            Dim v As Double = System.Math.Max(0.0, System.Math.Min(1.0, value)) * 255.0

            Return CInt(System.Math.Round(v))
        End Function

    End Class

End Namespace
