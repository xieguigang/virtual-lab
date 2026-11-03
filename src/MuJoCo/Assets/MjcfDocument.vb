Imports System.IO
Imports System.Numerics

Namespace Assets

    ''' <summary>
    ''' The parts of the MJCF model that the port needs: the body tree with its
    ''' inertial properties and its hinges, the collision geoms and the position
    ''' actuators.
    ''' </summary>
    ''' <remarks>
    ''' The browser build rewrote the MJCF and let MuJoCo parse it. The desktop
    ''' build has no MuJoCo, so the same numbers are read here and turned into
    ''' <c>PhysicsWorld3D</c> bodies and hinges by <c>DuckPhysics</c>.
    '''
    ''' The ``default`` class tree is resolved the way the MuJoCo compiler does
    ''' it: an attribute of the element wins over the class it belongs to, a
    ''' class inherits from its enclosing class and every class inherits from
    ''' ``main``.
    ''' </remarks>
    Public Class MjcfDocument

        Public ReadOnly Property Bodies As New List(Of MjcfBody)()
        Public ReadOnly Property Actuators As New List(Of MjcfActuator)()

        ''' <summary>the physics time step of the document, seconds</summary>
        Public Property Timestep As Double = 0.002

        Private ReadOnly m_defaults As New Dictionary(Of String, DefaultClass)(StringComparer.Ordinal)

        ''' <summary>
        ''' one node of the ``default`` class tree: the attributes that it
        ''' contributes to the elements of its class
        ''' </summary>
        Private Class DefaultClass
            Public Parent As String
            Public ReadOnly Joint As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Public ReadOnly Geom As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Public ReadOnly Position As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Public ReadOnly General As New Dictionary(Of String, String)(StringComparer.Ordinal)
        End Class

        Private Const MainClass As String = "main"

        Public Shared Function Load(path As String) As MjcfDocument
            Dim document As XDocument = XDocument.Load(path)
            Dim model As New MjcfDocument()

            Call model.Parse(document)

            Return model
        End Function

        ''' <summary>
        ''' the actuator of the given joint, nothing when the joint is passive
        ''' </summary>
        Public Function ActuatorOf(jointName As String) As MjcfActuator
            For Each actuator As MjcfActuator In Actuators
                If String.Equals(actuator.Joint, jointName, StringComparison.Ordinal) Then
                    Return actuator
                End If
            Next

            Return Nothing
        End Function

        Private Sub Parse(document As XDocument)
            Dim root As XElement = document.Root

            If root Is Nothing Then
                Throw New InvalidDataException("the MJCF document is empty")
            End If

            Dim compiler As XElement = root.Element("compiler")

            If compiler IsNot Nothing Then
                Dim angle As String = Attribute(compiler, "angle", "degree")

                If Not String.Equals(angle, "radian", StringComparison.OrdinalIgnoreCase) Then
                    ' the duck model is in radians; a degree model would silently
                    ' produce hinges with a 57x travel
                    Throw New InvalidDataException($"the MJCF uses angle=""{angle}"", only radian models are supported")
                End If
            End If

            Dim optionElement As XElement = root.Element("option")

            If optionElement IsNot Nothing Then
                Timestep = ParseDouble(Attribute(optionElement, "timestep", "0.002"))
            End If

            ' the document repeats the default block: every block contributes its
            ' classes to the same tree, exactly like the mujoco compiler merges
            ' them
            For Each block As XElement In root.Elements("default")
                Call RegisterDefault(block, MainClass)
            Next

            Dim worldbody As XElement = root.Element("worldbody")

            If worldbody Is Nothing Then
                Throw New InvalidDataException("the MJCF document has no worldbody")
            End If

            For Each body As XElement In worldbody.Elements("body")
                Call ParseBody(body, Nothing, Attribute(body, "childclass"))
            Next

            Call ParseActuators(root.Element("actuator"))
        End Sub

        ' /********************************************************************************/
        '   default classes
        ' /********************************************************************************/

        Private Sub RegisterDefault(element As XElement, className As String)
            Dim node As DefaultClass = Nothing

            If Not m_defaults.TryGetValue(className, node) Then
                node = New DefaultClass()
                m_defaults(className) = node
            End If

            ' the main class is the root of every chain
            If Not String.Equals(className, MainClass, StringComparison.Ordinal) Then
                node.Parent = MainClass
            End If

            For Each child As XElement In element.Elements()
                Dim table As Dictionary(Of String, String) = TableOf(node, child.Name.LocalName)

                If table IsNot Nothing Then
                    For Each attribute As XAttribute In child.Attributes()
                        table(attribute.Name.LocalName) = attribute.Value
                    Next
                End If
            Next

            For Each child As XElement In element.Elements("default")
                Dim name As String = Attribute(child, "class")

                If String.IsNullOrEmpty(name) Then
                    name = MainClass
                End If

                Call RegisterDefault(child, name)

                Dim node2 As DefaultClass = Nothing

                If m_defaults.TryGetValue(name, node2) AndAlso
                   Not String.Equals(name, className, StringComparison.Ordinal) Then
                    node2.Parent = className
                End If
            Next
        End Sub

        Private Shared Function TableOf(node As DefaultClass, element As String) As Dictionary(Of String, String)
            Select Case element
                Case "joint", "freejoint"
                    Return node.Joint
                Case "geom"
                    Return node.Geom
                Case "position", "motor", "general"
                    Return node.Position
                Case "default"
                    Return Nothing
                Case Else
                    Return node.General
            End Select
        End Function

        ''' <summary>
        ''' collect the attributes of a class and of every class of its chain,
        ''' the closest class wins
        ''' </summary>
        Private Function Collect(className As String, element As String) As Dictionary(Of String, String)
            Dim chain As New List(Of String)()
            Dim current As String = If(String.IsNullOrEmpty(className), MainClass, className)

            ' a class that the document does not declare simply inherits main
            For i As Integer = 0 To 16
                Call chain.Insert(0, current)

                Dim node As DefaultClass = Nothing

                If Not m_defaults.TryGetValue(current, node) Then
                    Exit For
                End If

                If String.IsNullOrEmpty(node.Parent) Then
                    Exit For
                End If

                current = node.Parent
            Next

            Dim result As New Dictionary(Of String, String)(StringComparer.Ordinal)

            For Each name As String In chain
                Dim node As DefaultClass = Nothing

                If m_defaults.TryGetValue(name, node) Then
                    Dim table As Dictionary(Of String, String) = TableOf(node, element)

                    If table IsNot Nothing Then
                        For Each pair As KeyValuePair(Of String, String) In table
                            result(pair.Key) = pair.Value
                        Next
                    End If
                End If
            Next

            Return result
        End Function

        ' /********************************************************************************/
        '   the body tree
        ' /********************************************************************************/

        Private Sub ParseBody(element As XElement, parent As String, childClass As String)
            Dim className As String = Attribute(element, "class", childClass)
            Dim body As New MjcfBody With {
                .Name = Attribute(element, "name"),
                .Parent = parent,
                .Position = Vector3Value(element, "pos"),
                .Rotation = QuaternionValue(element, "quat")
            }

            Call ParseInertial(element.Element("inertial"), body.Inertial)

            ' the hinge of the link: the first joint child wins, a freejoint is
            ' the floating base of the trunk and is not a motorised hinge
            Dim jointElement As XElement = Nothing

            For Each child As XElement In element.Elements()
                If child.Name.LocalName = "joint" Then
                    jointElement = child
                    Exit For
                End If
            Next

            If jointElement IsNot Nothing Then
                Dim jointClass As String = Attribute(jointElement, "class", className)
                Dim defaults As Dictionary(Of String, String) = Collect(jointClass, "joint")

                body.JointName = Attribute(jointElement, "name")
                body.JointType = Attribute(jointElement, "type", "hinge")
                body.JointAxis = Vector3Value(jointElement, "axis", New Vector3(0, 0, 1))

                Dim range As Double() = Values(jointElement, "range")

                If range.Length >= 2 Then
                    body.JointRange = {range(0), range(1)}
                End If

                body.JointDamping = Resolve(jointElement, defaults, "damping", 0)
                body.JointArmature = Resolve(jointElement, defaults, "armature", 0)
                body.JointFrictionLoss = Resolve(jointElement, defaults, "frictionloss", 0)
            End If

            For Each geomElement As XElement In element.Elements("geom")
                Dim geomClass As String = Attribute(geomElement, "class", className)
                Dim defaults As Dictionary(Of String, String) = Collect(geomClass, "geom")
                Dim contype As Integer = CInt(Resolve(geomElement, defaults, "contype", 1))
                Dim conaffinity As Integer = CInt(Resolve(geomElement, defaults, "conaffinity", 1))

                ' a self collision group never touches the world, so it can not
                ' contribute to the collision proxy of a link
                If contype = 2 AndAlso conaffinity = 2 Then
                    Continue For
                End If

                If contype = 0 OrElse conaffinity = 0 Then
                    Continue For
                End If

                Dim geom As New MjcfGeom With {
                    .Name = Attribute(geomElement, "name"),
                    .Mesh = Attribute(geomElement, "mesh"),
                    .Type = Attribute(geomElement, "type", ResolveText(geomElement, defaults, "type", "sphere")),
                    .Position = Vector3Value(geomElement, "pos"),
                    .Rotation = QuaternionValue(geomElement, "quat"),
                    .Contype = contype,
                    .Conaffinity = conaffinity,
                    .ClassName = geomClass
                }

                If Not String.IsNullOrEmpty(geom.Mesh) Then
                    Call body.Geoms.Add(geom)
                End If
            Next

            Call Bodies.Add(body)

            For Each child As XElement In element.Elements("body")
                Call ParseBody(child, body.Name, className)
            Next
        End Sub

        Private Shared Sub ParseInertial(element As XElement, inertial As MjcfInertial)
            If element Is Nothing Then
                Return
            End If

            inertial.Position = Vector3Value(element, "pos")
            inertial.Mass = ParseDouble(Attribute(element, "mass", "0"))

            Dim full As Double() = Values(element, "fullinertia")

            If full.Length >= 6 Then
                inertial.Inertia = {full(0), full(1), full(2), full(3), full(4), full(5)}

                Return
            End If

            ' a diagonal inertia is spelled out as three separate attributes
            Dim diag As Double() = Values(element, "diaginertia")

            If diag.Length >= 3 Then
                inertial.Inertia = {diag(0), diag(1), diag(2), 0, 0, 0}
            End If
        End Sub

        Private Sub ParseActuators(actuatorElement As XElement)
            If actuatorElement Is Nothing Then
                Return
            End If

            For Each element As XElement In actuatorElement.Elements("position")
                Dim className As String = Attribute(element, "class")
                Dim defaults As Dictionary(Of String, String) = Collect(className, "position")
                Dim actuator As New MjcfActuator With {
                    .Name = Attribute(element, "name"),
                    .Joint = Attribute(element, "joint"),
                    .Kp = Resolve(element, defaults, "kp", 1),
                    .Kv = Resolve(element, defaults, "kv", 0)
                }
                ' the range comes from the class of the actuator, the element
                ' itself only names the joint
                Dim range As Double() = ParseValues(ResolveText(element, defaults, "forcerange", Nothing))

                If range.Length < 2 Then
                    range = ParseValues(ResolveText(element, defaults, "ctrlrange", Nothing))
                End If

                If range.Length >= 2 Then
                    actuator.ForceMin = range(0)
                    actuator.ForceMax = range(1)
                End If

                Call Actuators.Add(actuator)
            Next
        End Sub

        ' /********************************************************************************/
        '   attribute helpers
        ' /********************************************************************************/

        Private Shared Function Resolve(element As XElement, defaults As Dictionary(Of String, String),
                                        name As String, fallback As Double) As Double
            Dim text As String = ResolveText(element, defaults, name, Nothing)

            If String.IsNullOrEmpty(text) Then
                Return fallback
            End If

            Return ParseDouble(text)
        End Function

        Private Shared Function ResolveText(element As XElement, defaults As Dictionary(Of String, String),
                                            name As String, fallback As String) As String
            Dim value As XAttribute = element.Attribute(name)

            If value IsNot Nothing Then
                Return value.Value
            End If

            Dim text As String = Nothing

            If defaults IsNot Nothing AndAlso defaults.TryGetValue(name, text) Then
                Return text
            End If

            Return fallback
        End Function

        Private Shared Function Attribute(element As XElement, name As String, Optional fallback As String = Nothing) As String
            Dim value As XAttribute = element.Attribute(name)

            If value Is Nothing Then
                Return fallback
            End If

            Return value.Value
        End Function

        Private Shared Function ParseDouble(text As String, Optional fallback As Double = 0) As Double
            Dim value As Double

            If String.IsNullOrEmpty(text) Then
                Return fallback
            End If

            If Double.TryParse(text, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, value) Then
                Return value
            End If

            Return fallback
        End Function

        Private Shared Function Values(element As XElement, name As String) As Double()
            Return ParseValues(Attribute(element, name))
        End Function

        Private Shared Function ParseValues(text As String) As Double()
            If String.IsNullOrEmpty(text) Then
                Return New Double() {}
            End If

            Dim parts As String() = text.Split(New Char() {" "c, ChrW(9), ","c}, StringSplitOptions.RemoveEmptyEntries)
            Dim items As New List(Of Double)()

            For Each part As String In parts
                Call items.Add(ParseDouble(part))
            Next

            Return items.ToArray()
        End Function

        Private Shared Function Vector3Value(element As XElement, name As String,
                                             Optional fallback As Vector3 = Nothing) As Vector3
            Dim numbers As Double() = Values(element, name)

            If numbers.Length >= 3 Then
                Return New Vector3(CSng(numbers(0)), CSng(numbers(1)), CSng(numbers(2)))
            End If

            Return fallback
        End Function

        ''' <summary>
        ''' read an MJCF quaternion (w, x, y, z)
        ''' </summary>
        Private Shared Function QuaternionValue(element As XElement, name As String) As Quaternion
            Dim numbers As Double() = Values(element, name)

            If numbers.Length >= 4 Then
                Return New Quaternion(CSng(numbers(1)), CSng(numbers(2)), CSng(numbers(3)), CSng(numbers(0)))
            End If

            Return Quaternion.Identity
        End Function
    End Class
End Namespace
