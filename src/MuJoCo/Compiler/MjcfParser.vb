Imports System.Globalization
Imports System.IO
Imports System.Numerics
Imports System.Xml
Imports System.Xml.Linq

''' <summary>MJCF 文本到值的解析工具（对应 <c>src/xml/xml_util.cc</c> 的那一组 helper）</summary>
Friend Module MjcfText

    Private ReadOnly Culture As CultureInfo = CultureInfo.InvariantCulture

    ''' <summary>取元素的属性，缺失时返回 <paramref name="fallback"/></summary>
    Public Function Attr(element As XElement, name As String, Optional fallback As String = Nothing) As String
        Dim value As XAttribute = element.Attribute(name)

        If value Is Nothing Then
            Return fallback
        End If

        Return value.Value
    End Function

    ''' <summary>
    ''' 先看元素自己的属性，再沿 default 类链查找，最后用默认值。
    ''' </summary>
    Public Function Resolve(element As XElement, defaults As MjsDefault, elementName As String,
                            name As String, Optional fallback As String = Nothing) As String
        Dim value As XAttribute = element.Attribute(name)

        If value IsNot Nothing Then
            Return value.Value
        End If

        If defaults Is Nothing Then
            Return fallback
        End If

        Return defaults.Resolve(elementName, name, fallback)
    End Function

    Public Function Number(text As String, Optional fallback As Double = 0.0) As Double
        If String.IsNullOrEmpty(text) Then
            Return fallback
        End If

        Dim value As Double

        If Double.TryParse(text, NumberStyles.Float, Culture, value) Then
            Return value
        End If

        Return fallback
    End Function

    Public Function Integer_(text As String, Optional fallback As Integer = 0) As Integer
        If String.IsNullOrEmpty(text) Then
            Return fallback
        End If

        Dim value As Integer

        If System.Int32.TryParse(text, NumberStyles.Integer, Culture, value) Then
            Return value
        End If

        ' 允许 "1.0" 这种写法
        Dim real As Double

        If Double.TryParse(text, NumberStyles.Float, Culture, real) Then
            Return CInt(real)
        End If

        Return fallback
    End Function

    Public Function Flag(text As String, Optional fallback As Boolean = False) As Boolean
        If String.IsNullOrEmpty(text) Then
            Return fallback
        End If

        Select Case text.Trim().ToLowerInvariant()
            Case "true", "1", "yes" : Return True
            Case "false", "0", "no" : Return False
            Case "auto" : Return fallback
            Case Else : Return fallback
        End Select
    End Function

    ''' <summary>把 "1 2 3" / "1,2,3" 拆成数值数组</summary>
    Public Function Values(text As String) As Double()
        If String.IsNullOrEmpty(text) Then
            Return New Double() {}
        End If

        Dim parts As String() = text.Split(New Char() {" "c, ChrW(9), ","c, ChrW(10), ChrW(13)},
                                           StringSplitOptions.RemoveEmptyEntries)
        Dim result(parts.Length - 1) As Double

        For i As Integer = 0 To parts.Length - 1
            result(i) = Number(parts(i))
        Next

        Return result
    End Function

    Public Function Decimals(text As String) As Integer()
        If String.IsNullOrEmpty(text) Then
            Return New Integer() {}
        End If

        Dim parts As String() = text.Split(New Char() {" "c, ChrW(9), ","c, ChrW(10), ChrW(13)},
                                           StringSplitOptions.RemoveEmptyEntries)
        Dim result(parts.Length - 1) As Integer

        For i As Integer = 0 To parts.Length - 1
            result(i) = Integer_(parts(i))
        Next

        Return result
    End Function

    ''' <summary>三元素向量，长度不够时保留 <paramref name="fallback"/></summary>
    Public Function Vec3(text As String, fallback As Double()) As Double()
        Dim numbers As Double() = Values(text)

        If numbers.Length >= 3 Then
            Return {numbers(0), numbers(1), numbers(2)}
        End If

        Return CType(fallback.Clone(), Double())
    End Function

    ''' <summary>两元素（下限/上限），长度不够时返回 <paramref name="fallback"/></summary>
    Public Function Pair(text As String, fallback As Double()) As Double()
        Dim numbers As Double() = Values(text)

        If numbers.Length >= 2 Then
            Return {numbers(0), numbers(1)}
        End If

        Return CType(fallback.Clone(), Double())
    End Function

    ''' <summary>
    ''' 读任意一种姿态表示：<c>quat</c>（w x y z）、<c>axisangle</c>（x y z 角度）、
    ''' <c>euler</c>（按 <c>eulerseq</c>，默认 xyz）、<c>zaxis</c>。<c>xyaxes</c> 不支持。
    ''' </summary>
    Public Function Orientation(element As XElement, defaults As MjsDefault, elementName As String,
                                degreeToRadian As Double) As Double()
        Dim text As String = Resolve(element, defaults, elementName, "quat", Nothing)

        If Not String.IsNullOrEmpty(text) Then
            Dim q As Double() = Values(text)

            If q.Length >= 4 Then
                Return NormalizeQuat({q(0), q(1), q(2), q(3)})
            End If
        End If

        text = Resolve(element, defaults, elementName, "axisangle", Nothing)

        If Not String.IsNullOrEmpty(text) Then
            Dim a As Double() = Values(text)

            If a.Length >= 4 Then
                Return FromAxisAngle(a(0), a(1), a(2), a(3) * degreeToRadian)
            End If
        End If

        text = Resolve(element, defaults, elementName, "euler", Nothing)

        If Not String.IsNullOrEmpty(text) Then
            Dim e As Double() = Values(text)

            If e.Length >= 3 Then
                Dim seq As String = Resolve(element, defaults, elementName, "eulerseq", "xyz")

                Return FromEuler(e, seq, degreeToRadian)
            End If
        End If

        text = Resolve(element, defaults, elementName, "zaxis", Nothing)

        If Not String.IsNullOrEmpty(text) Then
            Dim z As Double() = Values(text)

            If z.Length >= 3 Then
                Return FromZAxis(z(0), z(1), z(2))
            End If
        End If

        Return {1.0, 0.0, 0.0, 0.0}
    End Function

    Public Function NormalizeQuat(q As Double()) As Double()
        Dim len As Double = Math.Sqrt(q(0) * q(0) + q(1) * q(1) + q(2) * q(2) + q(3) * q(3))

        If len < 1.0E-12 Then
            Return {1.0, 0.0, 0.0, 0.0}
        End If

        Return {q(0) / len, q(1) / len, q(2) / len, q(3) / len}
    End Function

    Public Function FromAxisAngle(x As Double, y As Double, z As Double, angle As Double) As Double()
        Dim len As Double = Math.Sqrt(x * x + y * y + z * z)

        If len < 1.0E-12 Then
            Return {1.0, 0.0, 0.0, 0.0}
        End If

        Dim half As Double = 0.5 * angle
        Dim s As Double = Math.Sin(half) / len

        Return {Math.Cos(half), x * s, y * s, z * s}
    End Function

    ''' <summary>按内旋顺序（MuJoCo 的 <c>eulerseq</c>）复合三个欧拉角</summary>
    Public Function FromEuler(angles As Double(), sequence As String, degreeToRadian As Double) As Double()
        Dim q As Quaternion = Quaternion.Identity
        Dim seq As String = If(sequence, "xyz").PadRight(3, "x"c).Substring(0, 3)

        For i As Integer = 0 To 2
            Dim axis As Vector3

            Select Case Char.ToLowerInvariant(seq(i))
                Case "x"c : axis = Vector3.UnitX
                Case "y"c : axis = Vector3.UnitY
                Case Else : axis = Vector3.UnitZ
            End Select

            Dim turn As Quaternion = Quaternion.CreateFromAxisAngle(axis, CSng(angles(i) * degreeToRadian))
            q = Quaternion.Concatenate(q, turn)
        Next

        Return {q.W, q.X, q.Y, q.Z}
    End Function

    ''' <summary>把一个方向贴到 z 轴上，再随机取一个滚转（MuJoCo 的 <c>zaxis</c>）</summary>
    Public Function FromZAxis(x As Double, y As Double, z As Double) As Double()
        Dim axis As New Vector3(CSng(x), CSng(y), CSng(z))

        If axis.Length() < 1.0E-9 Then
            Return {1.0, 0.0, 0.0, 0.0}
        End If

        axis = Vector3.Normalize(axis)

        Dim dot As Double = axis.Z
        Dim q As Quaternion

        If dot > 0.999999 Then
            q = Quaternion.Identity
        ElseIf dot < -0.999999 Then
            q = New Quaternion(1, 0, 0, 0)
        Else
            Dim cross As Vector3 = Vector3.Cross(Vector3.UnitZ, axis)
            Dim angle As Double = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)))

            q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(cross), CSng(angle))
        End If

        Return {q.W, q.X, q.Y, q.Z}
    End Function

End Module

''' <summary>
''' MJCF 读取器：XML 文档 → <see cref="MjSpec"/>。
''' </summary>
''' <remarks>
''' <para>
''' 与 C 版的 <c>src/xml/xml_native_reader.cc</c> 同构：先把 <c>&lt;default&gt;</c>
''' 块收集成一棵类树，再在读每个元素时按"元素属性 → 最近的类 → 更远的类 → 默认值"
''' 的顺序解析。这样文档里重复出现的 <c>&lt;default&gt;</c> 块可以叠加，
''' 与原生 MuJoCo 的行为一致。
''' </para>
''' <para>
''' 不支持的元素会被忽略而不是报错（例如 <c>&lt;flex&gt;</c>、<c>&lt;skin&gt;</c>、
''' <c>&lt;hfield&gt;</c>），但支持的元素的非法属性会抛出
''' <see cref="MjException"/>，带上元素名与行号。
''' </para>
''' </remarks>
Public Module MjcfParser

    ''' <summary>从文件读一个 MJCF 文档</summary>
    Public Function Parse(path As String) As MjSpec
        If Not File.Exists(path) Then
            Throw New MjException($"the MJCF file '{path}' does not exist")
        End If

        Dim document As XDocument

        Try
            document = XDocument.Load(path)
        Catch ex As XmlException
            Throw New MjException($"the MJCF file is not valid XML: {ex.Message}", Nothing, 0, path)
        End Try

        Dim spec As MjSpec = ParseDocument(document, path)
        spec.SourceDirectory = IO.Path.GetDirectoryName(IO.Path.GetFullPath(path))

        Return spec
    End Function

    ''' <summary>从字符串读一个 MJCF 文档</summary>
    Public Function ParseString(xml As String, Optional baseDirectory As String = Nothing) As MjSpec
        Dim document As XDocument

        Try
            document = XDocument.Parse(xml)
        Catch ex As XmlException
            Throw New MjException($"the MJCF text is not valid XML: {ex.Message}")
        End Try

        Dim spec As MjSpec = ParseDocument(document, Nothing)
        spec.SourceDirectory = If(baseDirectory, "")

        Return spec
    End Function

    Private Function ParseDocument(document As XDocument, source As String) As MjSpec
        Dim root As XElement = document.Root

        If root Is Nothing Then
            Throw New MjException("the MJCF document is empty")
        End If

        If Not String.Equals(root.Name.LocalName, "mujoco", StringComparison.Ordinal) Then
            Throw New MjException($"the MJCF root element must be <mujoco>, found <{root.Name.LocalName}>")
        End If

        Dim spec As New MjSpec()
        spec.ModelName = If(MjcfText.Attr(root, "model"), "")

        Call ParseCompiler(spec, root.Element("compiler"))
        Call ParseOption(spec, root.Element("option"))

        ' default 块可以重复出现，全部叠加到同一棵类树上
        For Each block As XElement In root.Elements("default")
            Call ParseDefaultBlock(spec, block, spec.Defaults)
        Next

        Call ParseAsset(spec, root.Element("asset"))

        ' 预处理阶段把 mesh 编译好，后面的 geom 引用它时才有索引可查
        Call MjcfCompiler.PrepareMeshes(spec)

        Dim worldbody As XElement = root.Element("worldbody")

        If worldbody Is Nothing Then
            Throw New MjException("the MJCF document has no <worldbody>")
        End If

        For Each child As XElement In worldbody.Elements()
            Call ParseWorldChild(spec, spec.World, child, Nothing)
        Next

        Call ParseActuators(spec, root.Element("actuator"))
        Call ParseSensors(spec, root.Element("sensor"))
        Call ParseEqualities(spec, root.Element("equality"))
        Call ParseTendons(spec, root.Element("tendon"))
        Call ParseContact(spec, root.Element("contact"))
        Call ParseKeyframes(spec, root.Element("keyframe"))

        Return spec
    End Function

    ' /********************************************************************************/
    '  compiler / option
    ' /********************************************************************************/

    Private Sub ParseCompiler(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        Dim angle As String = MjcfText.Attr(element, "angle", "degree")
        spec.AngleInDegrees = Not String.Equals(angle, "radian", StringComparison.OrdinalIgnoreCase)

        ' 注意：托管层从不在运行时把角度制悄悄当成弧度制用，
        ' 而是在编译期一次性换算，避免"行程被放大 57 倍"这类事故
        spec.MeshDir = If(MjcfText.Attr(element, "meshdir"), "")
        spec.AutoLimits = MjcfText.Flag(MjcfText.Attr(element, "autolimits"), True)
        spec.InertiaFromGeom = MjcfText.Flag(MjcfText.Attr(element, "inertiafromgeom"), False)
    End Sub

    Private Sub ParseOption(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        Dim opt As mjOption = spec.Opt

        opt.timestep = MjcfText.Number(MjcfText.Attr(element, "timestep"), opt.timestep)
        opt.gravity = MjcfText.Vec3(MjcfText.Attr(element, "gravity"), opt.gravity)
        opt.wind = MjcfText.Vec3(MjcfText.Attr(element, "wind"), opt.wind)
        opt.magnetic = MjcfText.Vec3(MjcfText.Attr(element, "magnetic"), opt.magnetic)
        opt.impratio = MjcfText.Number(MjcfText.Attr(element, "impratio"), opt.impratio)
        opt.viscosity = MjcfText.Number(MjcfText.Attr(element, "viscosity"), opt.viscosity)
        opt.density = MjcfText.Number(MjcfText.Attr(element, "density"), opt.density)
        opt.iterations = MjcfText.Integer_(MjcfText.Attr(element, "iterations"), opt.iterations)
        opt.ls_iterations = MjcfText.Integer_(MjcfText.Attr(element, "ls_iterations"), opt.ls_iterations)

        Dim friction As Double() = MjcfText.Values(MjcfText.Attr(element, "o_friction"))

        If friction.Length >= 3 Then
            opt.o_friction = {friction(0), friction(1), friction(2), 0.0, 0.0}
        End If

        Dim solref As Double() = MjcfText.Pair(MjcfText.Attr(element, "o_solref"), opt.o_solref)

        If Not String.IsNullOrEmpty(MjcfText.Attr(element, "o_solref")) Then
            opt.o_solref = solref
        End If

        opt.integrator = ParseIntegrator(MjcfText.Attr(element, "integrator"), opt.integrator)
        opt.cone = ParseCone(MjcfText.Attr(element, "cone"), opt.cone)
        opt.solver = ParseSolver(MjcfText.Attr(element, "solver"), opt.solver)
    End Sub

    Private Function ParseIntegrator(text As String, fallback As mjtIntegrator) As mjtIntegrator
        If String.IsNullOrEmpty(text) Then
            Return fallback
        End If

        Select Case text.Trim().ToLowerInvariant()
            Case "euler" : Return mjtIntegrator.mjINT_EULER
            Case "rk4" : Return mjtIntegrator.mjINT_RK4
            Case "implicit" : Return mjtIntegrator.mjINT_IMPLICIT
            Case "implicitfast" : Return mjtIntegrator.mjINT_IMPLICITFAST
            Case "discrete" : Return mjtIntegrator.mjINT_DISCRETE
            Case Else : Return fallback
        End Select
    End Function

    Private Function ParseCone(text As String, fallback As mjtCone) As mjtCone
        If String.IsNullOrEmpty(text) Then
            Return fallback
        End If

        Select Case text.Trim().ToLowerInvariant()
            Case "pyramidal" : Return mjtCone.mjCONE_PYRAMIDAL
            Case "elliptic" : Return mjtCone.mjCONE_ELLIPTIC
            Case Else : Return fallback
        End Select
    End Function

    Private Function ParseSolver(text As String, fallback As mjtSolver) As mjtSolver
        If String.IsNullOrEmpty(text) Then
            Return fallback
        End If

        Select Case text.Trim().ToLowerInvariant()
            Case "pgs" : Return mjtSolver.mjSOL_PGS
            Case "cg" : Return mjtSolver.mjSOL_CG
            Case "newton" : Return mjtSolver.mjSOL_NEWTON
            Case Else : Return fallback
        End Select
    End Function

    ' /********************************************************************************/
    '  default 类
    ' /********************************************************************************/

    Private Sub ParseDefaultBlock(spec As MjSpec, element As XElement, parent As MjsDefault)
        Dim name As String = MjcfText.Attr(element, "class")
        Dim node As MjsDefault

        If String.IsNullOrEmpty(name) OrElse String.Equals(name, "main", StringComparison.Ordinal) Then
            node = spec.Defaults
        Else
            node = spec.ClassOf(name)
            node.Parent = parent
        End If

        For Each child As XElement In element.Elements()
            Dim local As String = child.Name.LocalName

            If String.Equals(local, "default", StringComparison.Ordinal) Then
                Call ParseDefaultBlock(spec, child, node)

                Continue For
            End If

            Dim table As Dictionary(Of String, String) = node.Table(local)

            If table Is Nothing Then
                Continue For
            End If

            For Each attribute As XAttribute In child.Attributes()
                table(attribute.Name.LocalName) = attribute.Value
            Next
        Next
    End Sub

    ' /********************************************************************************/
    '  asset
    ' /********************************************************************************/

    Private Sub ParseAsset(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        For Each child As XElement In element.Elements()
            Dim local As String = child.Name.LocalName

            If String.Equals(local, "mesh", StringComparison.Ordinal) Then
                Dim mesh As New MjsMesh With {
                    .Name = If(MjcfText.Attr(child, "name"), StripExtension(MjcfText.Attr(child, "file"))),
                    .File = MjcfText.Attr(child, "file")
                }

                Dim scale As Double() = MjcfText.Vec3(MjcfText.Attr(child, "scale"), {1.0, 1.0, 1.0})
                mesh.Scale = scale

                Dim vertexText As String = MjcfText.Attr(child, "vertex")

                If Not String.IsNullOrEmpty(vertexText) Then
                    mesh.Vertices = MjcfText.Values(vertexText)
                End If

                Dim faceText As String = MjcfText.Attr(child, "face")

                If Not String.IsNullOrEmpty(faceText) Then
                    mesh.Faces = MjcfText.Decimals(faceText)
                End If

                Call spec.Meshes.Add(mesh)

            ElseIf String.Equals(local, "material", StringComparison.Ordinal) Then
                Dim material As New MjsMaterial With {.Name = If(MjcfText.Attr(child, "name"), "")}
                Dim rgba As Double() = MjcfText.Values(MjcfText.Attr(child, "rgba"))

                If rgba.Length >= 4 Then
                    material.Rgba = {rgba(0), rgba(1), rgba(2), rgba(3)}
                ElseIf rgba.Length = 3 Then
                    material.Rgba = {rgba(0), rgba(1), rgba(2), 1.0}
                End If

                Call spec.Materials.Add(material)
            End If
        Next
    End Sub

    Private Function StripExtension(text As String) As String
        If String.IsNullOrEmpty(text) Then
            Return ""
        End If

        Return Path.GetFileNameWithoutExtension(text)
    End Function

    ' /********************************************************************************/
    '  worldbody
    ' /********************************************************************************/

    Private Sub ParseWorldChild(spec As MjSpec, parent As MjsBody, element As XElement, childClass As String)
        Select Case element.Name.LocalName
            Case "body"
                Call ParseBody(spec, parent, element, childClass)
            Case "geom"
                Call ParseGeom(spec, parent, element, childClass)
            Case "site"
                Call ParseSite(spec, parent, element, childClass)
            Case "camera"
                Call ParseCamera(parent, element, childClass)
            Case "light"
                Call ParseLight(parent, element, childClass)
            Case "joint", "freejoint"
                Call ParseJoint(spec, parent, element, childClass)
        End Select
    End Sub

    Private Sub ParseBody(spec As MjSpec, parent As MjsBody, element As XElement, inheritedClass As String)
        Dim className As String = MjcfText.Attr(element, "class", inheritedClass)
        Dim cls As MjsDefault = spec.ClassOf(className)

        Dim body As MjsBody = spec.AddBody(parent, MjcfText.Attr(element, "name"))
        body.DefaultClass = cls
        body.Pos = MjcfText.Vec3(MjcfText.Attr(element, "pos"), {0.0, 0.0, 0.0})
        body.Quat = MjcfText.Orientation(element, Nothing, "body", DegreeFactor(spec))
        body.Contype = MjcfText.Integer_(MjcfText.Attr(element, "contype"), 0)
        body.Conaffinity = MjcfText.Integer_(MjcfText.Attr(element, "conaffinity"), 0)

        Dim childClass As String = MjcfText.Attr(element, "childclass", className)

        For Each child As XElement In element.Elements()
            Select Case child.Name.LocalName
                Case "inertial"
                    Call ParseInertial(spec, body, child)
                Case "joint", "freejoint"
                    Call ParseJoint(spec, body, child, childClass)
                Case "geom"
                    Call ParseGeom(spec, body, child, childClass)
                Case "site"
                    Call ParseSite(spec, body, child, childClass)
                Case "camera"
                    Call ParseCamera(body, child, childClass)
                Case "light"
                    Call ParseLight(body, child, childClass)
                Case "body"
                    Call ParseBody(spec, body, child, childClass)
            End Select
        Next
    End Sub

    Private Sub ParseInertial(spec As MjSpec, body As MjsBody, element As XElement)
        body.HasInertial = True
        body.Ipos = MjcfText.Vec3(MjcfText.Attr(element, "pos"), {0.0, 0.0, 0.0})
        body.Mass = MjcfText.Number(MjcfText.Attr(element, "mass"), 0.0)

        Dim full As Double() = MjcfText.Values(MjcfText.Attr(element, "fullinertia"))

        If full.Length >= 6 Then
            body.FullInertia = {full(0), full(1), full(2), full(3), full(4), full(5)}

            Return
        End If

        Dim diag As Double() = MjcfText.Values(MjcfText.Attr(element, "diaginertia"))

        If diag.Length >= 3 Then
            body.FullInertia = {diag(0), diag(1), diag(2), 0.0, 0.0, 0.0}
        End If
    End Sub

    Private Sub ParseJoint(spec As MjSpec, body As MjsBody, element As XElement, inheritedClass As String)
        Dim className As String = MjcfText.Attr(element, "class", inheritedClass)
        Dim cls As MjsDefault = spec.ClassOf(className)
        Dim isFree As Boolean = String.Equals(element.Name.LocalName, "freejoint", StringComparison.Ordinal)

        Dim joint As New MjsJoint With {
            .Name = If(MjcfText.Attr(element, "name"), ""),
            .Body = body,
            .Type = If(isFree, mjtJoint.mjJNT_FREE,
                       ParseJointType(MjcfText.Resolve(element, cls, "joint", "type", "hinge")))
        }

        joint.Pos = MjcfText.Vec3(MjcfText.Resolve(element, cls, "joint", "pos"), {0.0, 0.0, 0.0})
        joint.Axis = MjcfText.Vec3(MjcfText.Resolve(element, cls, "joint", "axis"), {0.0, 0.0, 1.0})

        Dim rangeText As String = MjcfText.Resolve(element, cls, "joint", "range")

        If Not String.IsNullOrEmpty(rangeText) Then
            Dim range As Double() = MjcfText.Values(rangeText)

            If range.Length >= 2 Then
                Dim factor As Double = If(joint.Type = mjtJoint.mjJNT_HINGE, DegreeFactor(spec), 1.0)
                joint.Range = {range(0) * factor, range(1) * factor}
            End If
        End If

        ' autolimits：给了 range 就自动开启限位，除非元素显式写了 limited
        Dim limitedText As String = MjcfText.Attr(element, "limited")

        If Not String.IsNullOrEmpty(limitedText) Then
            joint.Limited = MjcfText.Flag(limitedText, False)
        ElseIf String.IsNullOrEmpty(rangeText) Then
            joint.Limited = False
        Else
            joint.Limited = spec.AutoLimits
        End If

        joint.Stiffness = MjcfText.Number(MjcfText.Resolve(element, cls, "joint", "stiffness"), 0.0)
        joint.Damping = MjcfText.Number(MjcfText.Resolve(element, cls, "joint", "damping"), 0.0)
        joint.Armature = MjcfText.Number(MjcfText.Resolve(element, cls, "joint", "armature"), 0.0)
        joint.FrictionLoss = MjcfText.Number(MjcfText.Resolve(element, cls, "joint", "frictionloss"), 0.0)
        joint.Margin = MjcfText.Number(MjcfText.Resolve(element, cls, "joint", "margin"), 0.0)
        joint.Group = MjcfText.Integer_(MjcfText.Resolve(element, cls, "joint", "group"), 0)

        Dim solref As Double() = MjcfText.Values(MjcfText.Resolve(element, cls, "joint", "solref"))

        If solref.Length >= 2 Then
            joint.Solref = {solref(0), solref(1)}
        End If

        Dim actfrcrange As Double() = MjcfText.Values(MjcfText.Resolve(element, cls, "joint", "actfrcrange"))

        If actfrcrange.Length >= 2 Then
            joint.ActFrcRange = {actfrcrange(0), actfrcrange(1)}
            joint.ActFrcLimited = True
        End If

        Call body.Joints.Add(joint)
    End Sub

    Private Function ParseJointType(text As String) As mjtJoint
        If String.IsNullOrEmpty(text) Then
            Return mjtJoint.mjJNT_HINGE
        End If

        Select Case text.Trim().ToLowerInvariant()
            Case "free" : Return mjtJoint.mjJNT_FREE
            Case "ball" : Return mjtJoint.mjJNT_BALL
            Case "slide" : Return mjtJoint.mjJNT_SLIDE
            Case Else : Return mjtJoint.mjJNT_HINGE
        End Select
    End Function

    Private Sub ParseGeom(spec As MjSpec, body As MjsBody, element As XElement, inheritedClass As String)
        Dim className As String = MjcfText.Attr(element, "class", inheritedClass)
        Dim cls As MjsDefault = spec.ClassOf(className)

        Dim geom As New MjsGeom With {
            .Name = If(MjcfText.Attr(element, "name"), ""),
            .Body = body,
            .Type = ParseGeomType(MjcfText.Resolve(element, cls, "geom", "type", "sphere"))
        }

        geom.Size = MjcfText.Vec3(MjcfText.Resolve(element, cls, "geom", "size"), {0.0, 0.0, 0.0})
        geom.Pos = MjcfText.Vec3(MjcfText.Resolve(element, cls, "geom", "pos"), {0.0, 0.0, 0.0})
        geom.Quat = MjcfText.Orientation(element, cls, "geom", DegreeFactor(spec))

        geom.Contype = MjcfText.Integer_(MjcfText.Resolve(element, cls, "geom", "contype"), 1)
        geom.Conaffinity = MjcfText.Integer_(MjcfText.Resolve(element, cls, "geom", "conaffinity"), 1)
        geom.Condim = MjcfText.Integer_(MjcfText.Resolve(element, cls, "geom", "condim"), 3)
        geom.Group = MjcfText.Integer_(MjcfText.Resolve(element, cls, "geom", "group"), 0)
        geom.Priority = MjcfText.Integer_(MjcfText.Resolve(element, cls, "geom", "priority"), 0)
        geom.Margin = MjcfText.Number(MjcfText.Resolve(element, cls, "geom", "margin"), 0.0)
        geom.Gap = MjcfText.Number(MjcfText.Resolve(element, cls, "geom", "gap"), 0.0)
        geom.Solmix = MjcfText.Number(MjcfText.Resolve(element, cls, "geom", "solmix"), 1.0)

        ' density 只影响没有 <inertial> 的 body，记录在 geom 上以便估算
        Dim friction As Double() = MjcfText.Values(MjcfText.Resolve(element, cls, "geom", "friction"))

        If friction.Length >= 3 Then
            geom.Friction = {friction(0), friction(1), friction(2)}
        ElseIf friction.Length = 1 Then
            geom.Friction = {friction(0), 0.005, 0.0001}
        End If

        Dim rgba As Double() = MjcfText.Values(MjcfText.Resolve(element, cls, "geom", "rgba"))

        If rgba.Length >= 4 Then
            geom.Rgba = {rgba(0), rgba(1), rgba(2), rgba(3)}
        End If

        geom.MeshName = MjcfText.Resolve(element, cls, "geom", "mesh")
        geom.MaterialName = MjcfText.Resolve(element, cls, "geom", "material")

        Call body.Geoms.Add(geom)
    End Sub

    Private Function ParseGeomType(text As String) As mjtGeom
        If String.IsNullOrEmpty(text) Then
            Return mjtGeom.mjGEOM_SPHERE
        End If

        Select Case text.Trim().ToLowerInvariant()
            Case "plane" : Return mjtGeom.mjGEOM_PLANE
            Case "hfield" : Return mjtGeom.mjGEOM_HFIELD
            Case "sphere" : Return mjtGeom.mjGEOM_SPHERE
            Case "capsule" : Return mjtGeom.mjGEOM_CAPSULE
            Case "ellipsoid" : Return mjtGeom.mjGEOM_ELLIPSOID
            Case "cylinder" : Return mjtGeom.mjGEOM_CYLINDER
            Case "box" : Return mjtGeom.mjGEOM_BOX
            Case "mesh" : Return mjtGeom.mjGEOM_MESH
            Case Else : Return mjtGeom.mjGEOM_SPHERE
        End Select
    End Function

    Private Sub ParseSite(spec As MjSpec, body As MjsBody, element As XElement, inheritedClass As String)
        Dim className As String = MjcfText.Attr(element, "class", inheritedClass)
        Dim cls As MjsDefault = spec.ClassOf(className)

        Dim site As New MjsSite With {
            .Name = If(MjcfText.Attr(element, "name"), ""),
            .Body = body,
            .Type = ParseGeomType(MjcfText.Resolve(element, cls, "site", "type", "sphere"))
        }

        site.Size = MjcfText.Vec3(MjcfText.Resolve(element, cls, "site", "size"), site.Size)
        site.Pos = MjcfText.Vec3(MjcfText.Resolve(element, cls, "site", "pos"), {0.0, 0.0, 0.0})
        site.Quat = MjcfText.Orientation(element, cls, "site", DegreeFactor(spec))
        site.Group = MjcfText.Integer_(MjcfText.Resolve(element, cls, "site", "group"), 0)

        Dim rgba As Double() = MjcfText.Values(MjcfText.Resolve(element, cls, "site", "rgba"))

        If rgba.Length >= 4 Then
            site.Rgba = {rgba(0), rgba(1), rgba(2), rgba(3)}
        End If

        Call body.Sites.Add(site)
    End Sub

    Private Sub ParseCamera(body As MjsBody, element As XElement, inheritedClass As String)
        Dim camera As New MjsCamera With {
            .Name = If(MjcfText.Attr(element, "name"), ""),
            .Body = body,
            .Pos = MjcfText.Vec3(MjcfText.Attr(element, "pos"), {0.0, 0.0, 0.0}),
            .Fovy = MjcfText.Number(MjcfText.Attr(element, "fovy"), 45.0)
        }

        camera.Quat = MjcfText.Orientation(element, Nothing, "camera", 1.0)

        Call body.Cameras.Add(camera)
    End Sub

    Private Sub ParseLight(body As MjsBody, element As XElement, inheritedClass As String)
        Dim light As New MjsLight With {
            .Name = If(MjcfText.Attr(element, "name"), ""),
            .Body = body,
            .Pos = MjcfText.Vec3(MjcfText.Attr(element, "pos"), {0.0, 0.0, 0.0}),
            .Dir = MjcfText.Vec3(MjcfText.Attr(element, "dir"), {0.0, 0.0, -1.0})
        }

        Call body.Lights.Add(light)
    End Sub

    ' /********************************************************************************/
    '  actuator / sensor / equality / tendon / contact / keyframe
    ' /********************************************************************************/

    Private Sub ParseActuators(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        For Each child As XElement In element.Elements()
            Dim local As String = child.Name.LocalName
            Dim kind As mjtActuator

            Select Case local
                Case "position" : kind = mjtActuator.mjACTUATOR_POSITION
                Case "motor" : kind = mjtActuator.mjACTUATOR_MOTOR
                Case "velocity" : kind = mjtActuator.mjACTUATOR_VELOCITY
                Case "general" : kind = mjtActuator.mjACTUATOR_GENERAL
                Case "damper" : kind = mjtActuator.mjACTUATOR_DAMPER
                Case Else : Continue For
            End Select

            Dim className As String = MjcfText.Attr(child, "class")
            Dim cls As MjsDefault = spec.ClassOf(className)
            Dim tableName As String = If(String.Equals(local, "general", StringComparison.Ordinal), "general", "position")

            Dim actuator As New MjsActuator With {
                .Kind = kind,
                .Name = If(MjcfText.Attr(child, "name"), "")
            }

            Dim jointName As String = MjcfText.Attr(child, "joint")
            Dim siteName As String = MjcfText.Attr(child, "site")
            Dim bodyName As String = MjcfText.Attr(child, "body")
            Dim tendonName As String = MjcfText.Attr(child, "tendon")

            If Not String.IsNullOrEmpty(jointName) Then
                actuator.Trntype = mjtTrn.mjTRN_JOINT
                actuator.Target = jointName
            ElseIf Not String.IsNullOrEmpty(tendonName) Then
                actuator.Trntype = mjtTrn.mjTRN_TENDON
                actuator.Target = tendonName
            ElseIf Not String.IsNullOrEmpty(siteName) Then
                actuator.Trntype = mjtTrn.mjTRN_SITE
                actuator.Target = siteName
            ElseIf Not String.IsNullOrEmpty(bodyName) Then
                actuator.Trntype = mjtTrn.mjTRN_BODY
                actuator.Target = bodyName
            End If

            actuator.Kp = MjcfText.Number(MjcfText.Resolve(child, cls, tableName, "kp"), 0.0)
            actuator.Kv = MjcfText.Number(MjcfText.Resolve(child, cls, tableName, "kv"), 0.0)

            Dim gain As Double() = MjcfText.Values(MjcfText.Resolve(child, cls, tableName, "gear"))

            If gain.Length >= 1 Then
                actuator.Gear = gain(0)
            End If

            Dim forcerange As Double() = MjcfText.Values(MjcfText.Resolve(child, cls, tableName, "forcerange"))

            If forcerange.Length >= 2 Then
                actuator.ForceRange = {forcerange(0), forcerange(1)}
                actuator.ForceLimited = True
            End If

            Dim ctrlrange As Double() = MjcfText.Values(MjcfText.Resolve(child, cls, tableName, "ctrlrange"))

            If ctrlrange.Length >= 2 Then
                actuator.CtrlRange = {ctrlrange(0), ctrlrange(1)}
                actuator.CtrlLimited = True
            End If

            actuator.Group = MjcfText.Integer_(MjcfText.Resolve(child, cls, tableName, "group"), 0)

            Call spec.Actuators.Add(actuator)
        Next
    End Sub

    Private Sub ParseSensors(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        For Each child As XElement In element.Elements()
            Dim type As mjtSensor

            If Not TryParseSensorType(child.Name.LocalName, type) Then
                Continue For
            End If

            Dim sensor As New MjsSensor With {
                .Name = If(MjcfText.Attr(child, "name"), ""),
                .Type = type,
                .Noise = MjcfText.Number(MjcfText.Attr(child, "noise"), 0.0),
                .Cutoff = MjcfText.Number(MjcfText.Attr(child, "cutoff"), 0.0)
            }

            ' objtype 缺省时按元素名推断：joint 型传感器默认 joint，frame 型默认 site
            Dim objTypeText As String = MjcfText.Attr(child, "objtype")
            Dim objName As String = MjcfText.Attr(child, "objname")

            If String.IsNullOrEmpty(objName) Then
                objName = MjcfText.Attr(child, "site")

                If String.IsNullOrEmpty(objName) Then
                    objTypeText = "site"
                Else
                    objTypeText = "site"
                End If
            End If

            If String.IsNullOrEmpty(objName) Then
                objName = MjcfText.Attr(child, "joint")

                If Not String.IsNullOrEmpty(objName) Then
                    objTypeText = "joint"
                End If
            End If

            If String.IsNullOrEmpty(objName) Then
                objName = MjcfText.Attr(child, "body")

                If Not String.IsNullOrEmpty(objName) Then
                    objTypeText = "body"
                End If
            End If

            If String.IsNullOrEmpty(objName) Then
                objName = MjcfText.Attr(child, "tendon")

                If Not String.IsNullOrEmpty(objName) Then
                    objTypeText = "tendon"
                End If
            End If

            If String.IsNullOrEmpty(objName) Then
                objName = MjcfText.Attr(child, "actuator")

                If Not String.IsNullOrEmpty(objName) Then
                    objTypeText = "actuator"
                End If
            End If

            sensor.ObjType = ParseObjType(objTypeText, DefaultObjTypeOf(type))
            sensor.ObjName = If(objName, "")

            Dim refName As String = MjcfText.Attr(child, "refname")

            If Not String.IsNullOrEmpty(refName) Then
                sensor.RefType = ParseObjType(MjcfText.Attr(child, "reftype"), mjtObj.mjOBJ_UNKNOWN)
                sensor.RefName = refName
            End If

            Call spec.Sensors.Add(sensor)
        Next
    End Sub

    Private Function DefaultObjTypeOf(type As mjtSensor) As mjtObj
        Select Case type
            Case mjtSensor.mjSENS_JOINTPOS, mjtSensor.mjSENS_JOINTVEL,
                 mjtSensor.mjSENS_JOINTACTFRC, mjtSensor.mjSENS_JOINTLIMITPOS,
                 mjtSensor.mjSENS_JOINTLIMITVEL, mjtSensor.mjSENS_JOINTLIMITFRC
                Return mjtObj.mjOBJ_JOINT
            Case mjtSensor.mjSENS_TENDONPOS, mjtSensor.mjSENS_TENDONVEL,
                 mjtSensor.mjSENS_TENDONACTFRC, mjtSensor.mjSENS_TENDONLIMITPOS,
                 mjtSensor.mjSENS_TENDONLIMITVEL, mjtSensor.mjSENS_TENDONLIMITFRC
                Return mjtObj.mjOBJ_TENDON
            Case mjtSensor.mjSENS_ACTUATORPOS, mjtSensor.mjSENS_ACTUATORVEL, mjtSensor.mjSENS_ACTUATORFRC
                Return mjtObj.mjOBJ_ACTUATOR
            Case mjtSensor.mjSENS_SUBTREECOM, mjtSensor.mjSENS_SUBTREELINVEL, mjtSensor.mjSENS_SUBTREEANGMOM
                Return mjtObj.mjOBJ_BODY
            Case Else
                Return mjtObj.mjOBJ_SITE
        End Select
    End Function

    Private Function TryParseSensorType(name As String, ByRef type As mjtSensor) As Boolean
        Select Case name
            Case "touch" : type = mjtSensor.mjSENS_TOUCH
            Case "accelerometer" : type = mjtSensor.mjSENS_ACCELEROMETER
            Case "velocimeter" : type = mjtSensor.mjSENS_VELOCIMETER
            Case "gyro" : type = mjtSensor.mjSENS_GYRO
            Case "force" : type = mjtSensor.mjSENS_FORCE
            Case "torque" : type = mjtSensor.mjSENS_TORQUE
            Case "magnetometer" : type = mjtSensor.mjSENS_MAGNETOMETER
            Case "rangefinder" : type = mjtSensor.mjSENS_RANGEFINDER
            Case "jointpos" : type = mjtSensor.mjSENS_JOINTPOS
            Case "jointvel" : type = mjtSensor.mjSENS_JOINTVEL
            Case "tendonpos" : type = mjtSensor.mjSENS_TENDONPOS
            Case "tendonvel" : type = mjtSensor.mjSENS_TENDONVEL
            Case "actuatorpos" : type = mjtSensor.mjSENS_ACTUATORPOS
            Case "actuatorvel" : type = mjtSensor.mjSENS_ACTUATORVEL
            Case "actuatorfrc" : type = mjtSensor.mjSENS_ACTUATORFRC
            Case "jointactfrc" : type = mjtSensor.mjSENS_JOINTACTFRC
            Case "tendonactfrc" : type = mjtSensor.mjSENS_TENDONACTFRC
            Case "ballquat" : type = mjtSensor.mjSENS_BALLQUAT
            Case "ballangvel" : type = mjtSensor.mjSENS_BALLANGVEL
            Case "jointlimitpos" : type = mjtSensor.mjSENS_JOINTLIMITPOS
            Case "jointlimitvel" : type = mjtSensor.mjSENS_JOINTLIMITVEL
            Case "jointlimitfrc" : type = mjtSensor.mjSENS_JOINTLIMITFRC
            Case "framepos" : type = mjtSensor.mjSENS_FRAMEPOS
            Case "framequat" : type = mjtSensor.mjSENS_FRAMEQUAT
            Case "framexaxis" : type = mjtSensor.mjSENS_FRAMEXAXIS
            Case "frameyaxis" : type = mjtSensor.mjSENS_FRAMEYAXIS
            Case "framezaxis" : type = mjtSensor.mjSENS_FRAMEZAXIS
            Case "framelinvel" : type = mjtSensor.mjSENS_FRAMELINVEL
            Case "frameangvel" : type = mjtSensor.mjSENS_FRAMEANGVEL
            Case "framelinacc" : type = mjtSensor.mjSENS_FRAMELINACC
            Case "frameangacc" : type = mjtSensor.mjSENS_FRAMEANGACC
            Case "subtreecom" : type = mjtSensor.mjSENS_SUBTREECOM
            Case "subtreelinvel" : type = mjtSensor.mjSENS_SUBTREELINVEL
            Case "subtreeangmom" : type = mjtSensor.mjSENS_SUBTREEANGMOM
            Case "e_potential" : type = mjtSensor.mjSENS_E_POTENTIAL
            Case "e_kinetic" : type = mjtSensor.mjSENS_E_KINETIC
            Case "clock" : type = mjtSensor.mjSENS_CLOCK
            Case Else : type = mjtSensor.mjSENS_FRAMEPOS : Return False
        End Select

        Return True
    End Function

    Private Function ParseObjType(text As String, fallback As mjtObj) As mjtObj
        If String.IsNullOrEmpty(text) Then
            Return fallback
        End If

        Select Case text.Trim().ToLowerInvariant()
            Case "body" : Return mjtObj.mjOBJ_BODY
            Case "xbody" : Return mjtObj.mjOBJ_XBODY
            Case "joint" : Return mjtObj.mjOBJ_JOINT
            Case "dof" : Return mjtObj.mjOBJ_DOF
            Case "geom" : Return mjtObj.mjOBJ_GEOM
            Case "site" : Return mjtObj.mjOBJ_SITE
            Case "camera" : Return mjtObj.mjOBJ_CAMERA
            Case "light" : Return mjtObj.mjOBJ_LIGHT
            Case "mesh" : Return mjtObj.mjOBJ_MESH
            Case "material" : Return mjtObj.mjOBJ_MATERIAL
            Case "tendon" : Return mjtObj.mjOBJ_TENDON
            Case "actuator" : Return mjtObj.mjOBJ_ACTUATOR
            Case "sensor" : Return mjtObj.mjOBJ_SENSOR
            Case "equality" : Return mjtObj.mjOBJ_EQUALITY
            Case "key" : Return mjtObj.mjOBJ_KEY
            Case Else : Return fallback
        End Select
    End Function

    Private Sub ParseEqualities(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        For Each child As XElement In element.Elements()
            Dim type As mjtEq

            Select Case child.Name.LocalName
                Case "connect" : type = mjtEq.mjEQ_CONNECT
                Case "weld" : type = mjtEq.mjEQ_WELD
                Case "joint" : type = mjtEq.mjEQ_JOINT
                Case "distance" : type = mjtEq.mjEQ_DISTANCE
                Case "tendon" : type = mjtEq.mjEQ_TENDON
                Case Else : Continue For
            End Select

            Dim eq As New MjsEquality With {
                .Type = type,
                .Name = If(MjcfText.Attr(child, "name"), "")
            }

            Dim body1 As String = MjcfText.Attr(child, "body1")
            Dim body2 As String = MjcfText.Attr(child, "body2")
            Dim joint1 As String = MjcfText.Attr(child, "joint1")
            Dim joint2 As String = MjcfText.Attr(child, "joint2")

            If Not String.IsNullOrEmpty(joint1) Then
                eq.Obj1Type = mjtObj.mjOBJ_JOINT
                eq.Obj1Name = joint1
                eq.Obj2Type = mjtObj.mjOBJ_JOINT
                eq.Obj2Name = If(joint2, "")
            Else
                eq.Obj1Type = mjtObj.mjOBJ_BODY
                eq.Obj1Name = If(body1, "")
                eq.Obj2Type = mjtObj.mjOBJ_BODY
                eq.Obj2Name = If(body2, "")
            End If

            eq.Active = MjcfText.Flag(MjcfText.Attr(child, "active"), True)

            Dim anchor As Double() = MjcfText.Values(MjcfText.Attr(child, "anchor"))

            If anchor.Length >= 3 Then
                eq.Data(0) = anchor(0)
                eq.Data(1) = anchor(1)
                eq.Data(2) = anchor(2)
            End If

            Dim relpose As Double() = MjcfText.Values(MjcfText.Attr(child, "relpose"))

            If relpose.Length >= 7 Then
                For i As Integer = 0 To 6
                    eq.Data(3 + i) = relpose(i)
                Next
            End If

            Dim solref As Double() = MjcfText.Values(MjcfText.Attr(child, "solref"))

            If solref.Length >= 2 Then
                eq.Solref = {solref(0), solref(1)}
            End If

            Call spec.Equalities.Add(eq)
        Next
    End Sub

    Private Sub ParseTendons(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        For Each child As XElement In element.Elements()
            Dim kind As String = child.Name.LocalName

            If Not String.Equals(kind, "fixed", StringComparison.Ordinal) AndAlso
               Not String.Equals(kind, "spatial", StringComparison.Ordinal) Then
                Continue For
            End If

            Dim tendon As New MjsTendon With {
                .Name = If(MjcfText.Attr(child, "name"), ""),
                .Type = If(String.Equals(kind, "fixed", StringComparison.Ordinal),
                           mjtTendon.mjTENDON_FIXED, mjtTendon.mjTENDON_SPATIAL)
            }

            For Each joint As XElement In child.Elements("joint")
                Call tendon.Joints.Add(If(MjcfText.Attr(joint, "joint"), ""))
            Next

            For Each site As XElement In child.Elements("site")
                Call tendon.Sites.Add(If(MjcfText.Attr(site, "site"), ""))
            Next

            tendon.Stiffness = MjcfText.Number(MjcfText.Attr(child, "stiffness"), 0.0)
            tendon.Damping = MjcfText.Number(MjcfText.Attr(child, "damping"), 0.0)
            tendon.Armature = MjcfText.Number(MjcfText.Attr(child, "armature"), 0.0)
            tendon.FrictionLoss = MjcfText.Number(MjcfText.Attr(child, "frictionloss"), 0.0)

            Dim range As Double() = MjcfText.Values(MjcfText.Attr(child, "range"))

            If range.Length >= 2 Then
                tendon.Range = {range(0), range(1)}
                tendon.Limited = True
            End If

            Call spec.Tendons.Add(tendon)
        Next
    End Sub

    Private Sub ParseContact(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        For Each child As XElement In element.Elements("exclude")
            Call spec.AddExclude(MjcfText.Attr(child, "body1"), MjcfText.Attr(child, "body2"))
        Next
    End Sub

    Private Sub ParseKeyframes(spec As MjSpec, element As XElement)
        If element Is Nothing Then
            Return
        End If

        For Each child As XElement In element.Elements("key")
            Dim key As New MjsKeyframe With {
                .Name = If(MjcfText.Attr(child, "name"), ""),
                .Time = MjcfText.Number(MjcfText.Attr(child, "time"), 0.0)
            }

            Dim qpos As String = MjcfText.Attr(child, "qpos")

            If Not String.IsNullOrEmpty(qpos) Then
                key.Qpos = MjcfText.Values(qpos)
            End If

            Dim qvel As String = MjcfText.Attr(child, "qvel")

            If Not String.IsNullOrEmpty(qvel) Then
                key.Qvel = MjcfText.Values(qvel)
            End If

            Dim ctrl As String = MjcfText.Attr(child, "ctrl")

            If Not String.IsNullOrEmpty(ctrl) Then
                key.Ctrl = MjcfText.Values(ctrl)
            End If

            Call spec.Keys.Add(key)
        Next
    End Sub

    Private Function DegreeFactor(spec As MjSpec) As Double
        Return If(spec.AngleInDegrees, Math.PI / 180.0, 1.0)
    End Function

End Module
