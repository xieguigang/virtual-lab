Imports System.Numerics

''' <summary>
''' 一个 default 类，保存"元素名 → 属性字符串"的表，按父类链合并。
''' </summary>
''' <remarks>
''' 与 <c>src/xml/xml_native_reader.cc</c> 的做法一致：属性只在字符串层面合并，
''' 具体的类型解释留到编译期。这样 <c>&lt;default&gt;</c> 块可以出现在文档的任何
''' 位置、可以重复出现，语义都是"往同一棵类树上补属性"。
''' </remarks>
Public Class MjsDefault

    Public Property Name As String
    Public Property Parent As MjsDefault

    Friend ReadOnly Joint As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Friend ReadOnly Geom As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Friend ReadOnly Site As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Friend ReadOnly Position As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Friend ReadOnly General As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Public Sub New(name As String, parent As MjsDefault)
        Me.Name = name
        Me.Parent = parent
    End Sub

    Friend Function Table(element As String) As Dictionary(Of String, String)
        Select Case element
            Case "joint", "freejoint" : Return Joint
            Case "geom" : Return Geom
            Case "site" : Return Site
            Case "position", "motor", "velocity", "general", "actuator" : Return Position
            Case "default" : Return Nothing
            Case Else : Return General
        End Select
    End Function

    ''' <summary>
    ''' 取属性的最终值：最近的类优先，缺省值最后兜底。
    ''' </summary>
    ''' <param name="element">元素名，例如 <c>geom</c></param>
    ''' <param name="attribute">属性名，例如 <c>contype</c></param>
    ''' <param name="fallback">所有类都没有声明时的值</param>
    Public Function Resolve(element As String, attribute As String, fallback As String) As String
        Dim node As MjsDefault = Me

        ' 类链最多 16 层：与现有 MjcfDocument 的保护一致，防止文档里写出环
        For depth As Integer = 0 To 15
            If node Is Nothing Then
                Exit For
            End If

            Dim table As Dictionary(Of String, String) = node.Table(element)
            Dim text As String = Nothing

            If table IsNot Nothing AndAlso table.TryGetValue(attribute, text) Then
                Return text
            End If

            node = node.Parent
        Next

        Return fallback
    End Function

End Class

''' <summary>可编辑的 body 节点（对应 <c>mjsBody</c>）</summary>
Public Class MjsBody

    Public Property Name As String
    Public Property Parent As MjsBody
    Public ReadOnly Property Children As New List(Of MjsBody)()

    Public Property Pos As Double() = {0.0, 0.0, 0.0}
    ''' <summary>相对父体的姿态四元数（w, x, y, z）</summary>
    Public Property Quat As Double() = {1.0, 0.0, 0.0, 0.0}

    ''' <summary>是否显式给出了 &lt;inertial&gt;</summary>
    Public Property HasInertial As Boolean
    Public Property Mass As Double = 0.0
    ''' <summary>质心在 body 坐标系里的位置</summary>
    Public Property Ipos As Double() = {0.0, 0.0, 0.0}
    ''' <summary>ixx, iyy, izz, ixy, ixz, iyz（相对质心）</summary>
    Public Property FullInertia As Double() = {0.0, 0.0, 0.0, 0.0, 0.0, 0.0}

    ''' <summary>body 自己的 contype / conaffinity（作用于"匿名 geom"）</summary>
    Public Property Contype As Integer = 0
    Public Property Conaffinity As Integer = 0

    ''' <summary>
    ''' 内核的碰撞分组，0 表示自动：静态 body 归到环境组，其余归到机器人组。
    ''' </summary>
    ''' <remarks>
    ''' MuJoCo 用每个 geom 的 <c>contype</c>/<c>conaffinity</c> 决定谁和谁碰，
    ''' 内核则是每个刚体一对 <c>CollisionGroup</c>/<c>CollisionMask</c> 位掩码。
    ''' 因为内核的一个刚体代理了 body 上全部可碰撞 geom，粒度只能到 body，
    ''' 所以分组信息放在 body 上，由宿主显式指定。
    ''' </remarks>
    Public Property CollisionGroup As Integer = 0
    ''' <summary>碰撞掩码，0 表示自动</summary>
    Public Property CollisionMask As Integer = 0

    Public ReadOnly Property Joints As New List(Of MjsJoint)()
    Public ReadOnly Property Geoms As New List(Of MjsGeom)()
    Public ReadOnly Property Sites As New List(Of MjsSite)()
    Public ReadOnly Property Cameras As New List(Of MjsCamera)()
    Public ReadOnly Property Lights As New List(Of MjsLight)()

    ''' <summary>关节锚点的局部坐标系（<c>childclass</c> 只影响编译期默认值）</summary>
    Public Property DefaultClass As MjsDefault

    Public Function HasJoint() As Boolean
        Return Joints.Count > 0
    End Function

    ''' <summary>第一个自由度是自由关节时为真</summary>
    Public Function HasFreeJoint() As Boolean
        For Each j As MjsJoint In Joints
            If j.Type = mjtJoint.mjJNT_FREE Then
                Return True
            End If
        Next

        Return False
    End Function

End Class

''' <summary>可编辑的 joint 节点（对应 <c>mjsJoint</c>）</summary>
Public Class MjsJoint

    Public Property Name As String
    Public Property Type As mjtJoint = mjtJoint.mjJNT_HINGE
    Public Property Pos As Double() = {0.0, 0.0, 0.0}
    Public Property Axis As Double() = {0.0, 0.0, 1.0}
    Public Property Range As Double() = {0.0, 0.0}
    ''' <summary>是否启用行程限位；<c>Nothing</c> 表示跟随 <c>autolimits</c></summary>
    Public Property Limited As Boolean? = Nothing
    Public Property Stiffness As Double = 0.0
    Public Property Damping As Double = 0.0
    Public Property Armature As Double = 0.0
    Public Property FrictionLoss As Double = 0.0
    Public Property Margin As Double = 0.0
    Public Property Group As Integer = 0
    Public Property Solref As Double() = {0.02, 1.0}
    Public Property Solimp As Double() = {0.9, 0.95, 0.001, 0.5, 2.0}
    Public Property ActFrcRange As Double() = {0.0, 0.0}
    Public Property ActFrcLimited As Boolean = False

    ''' <summary>所属的 body，由 <see cref="MjSpec"/> 在编译时填充</summary>
    Public Property Body As MjsBody

End Class

''' <summary>可编辑的 geom 节点（对应 <c>mjsGeom</c>）</summary>
Public Class MjsGeom

    Public Property Name As String
    Public Property Type As mjtGeom = mjtGeom.mjGEOM_SPHERE
    Public Property Size As Double() = {0.0, 0.0, 0.0}
    Public Property Pos As Double() = {0.0, 0.0, 0.0}
    Public Property Quat As Double() = {1.0, 0.0, 0.0, 0.0}

    Public Property Contype As Integer = 1
    Public Property Conaffinity As Integer = 1
    Public Property Condim As Integer = 3
    Public Property Group As Integer = 0
    Public Property Priority As Integer = 0

    Public Property Friction As Double() = {1.0, 0.005, 0.0001}
    Public Property Margin As Double = 0.0
    Public Property Gap As Double = 0.0
    Public Property Solmix As Double = 1.0
    Public Property Solref As Double() = {0.02, 1.0}
    Public Property Solimp As Double() = {0.9, 0.95, 0.001, 0.5, 2.0}

    ''' <summary>引用的 mesh 名（<c>type="mesh"</c> 时有效）</summary>
    Public Property MeshName As String
    ''' <summary>引用的材质名</summary>
    Public Property MaterialName As String
    ''' <summary>颜色，<c>Nothing</c> 表示沿用材质</summary>
    Public Property Rgba As Double() = Nothing

    Public Property Body As MjsBody

    Public Function EffectiveRgba(defaultRgba As Double()) As Double()
        Return If(Rgba, defaultRgba)
    End Function

End Class

''' <summary>可编辑的 site 节点（对应 <c>mjsSite</c>）</summary>
Public Class MjsSite

    Public Property Name As String
    Public Property Type As mjtGeom = mjtGeom.mjGEOM_SPHERE
    Public Property Size As Double() = {0.005, 0.005, 0.005}
    Public Property Pos As Double() = {0.0, 0.0, 0.0}
    Public Property Quat As Double() = {1.0, 0.0, 0.0, 0.0}
    Public Property Group As Integer = 0
    Public Property Rgba As Double() = {1.0, 0.0, 0.0, 0.5}
    Public Property Body As MjsBody

End Class

''' <summary>可编辑的 camera 节点</summary>
Public Class MjsCamera
    Public Property Name As String
    Public Property Pos As Double() = {0.0, 0.0, 0.0}
    Public Property Quat As Double() = {1.0, 0.0, 0.0, 0.0}
    Public Property Fovy As Double = 45.0
    Public Property Body As MjsBody
End Class

''' <summary>可编辑的 light 节点</summary>
Public Class MjsLight
    Public Property Name As String
    Public Property Pos As Double() = {0.0, 0.0, 0.0}
    Public Property Dir As Double() = {0.0, 0.0, -1.0}
    Public Property Type As Integer = 1
    Public Property Body As MjsBody
End Class

''' <summary>mesh 资产（对应 <c>mjsMesh</c>）</summary>
Public Class MjsMesh

    Public Property Name As String
    ''' <summary>相对 <c>meshdir</c> 的文件名</summary>
    Public Property File As String
    Public Property Scale As Double() = {1.0, 1.0, 1.0}

    ''' <summary>直接给出的顶点（x, y, z 三元组），<c>Nothing</c> 表示从文件读</summary>
    Public Property Vertices As Double() = Nothing
    ''' <summary>直接给出的三角形索引（每三个一组）</summary>
    Public Property Faces As Integer() = Nothing

    ''' <summary>编译期填充：已经缩放的顶点</summary>
    Friend Property CompiledVertices As Double() = Nothing
    ''' <summary>编译期填充：已经缩放的三角形索引</summary>
    Friend Property CompiledFaces As Integer() = Nothing

End Class

''' <summary>材质资产（对应 <c>mjsMaterial</c>）</summary>
Public Class MjsMaterial
    Public Property Name As String
    Public Property Rgba As Double() = {0.5, 0.5, 0.5, 1.0}
End Class

''' <summary>可编辑的执行器（对应 <c>mjsActuator</c>）</summary>
Public Class MjsActuator

    Public Property Name As String
    ''' <summary>语义类型，决定 gainprm / biasprm 的填法</summary>
    Public Property Kind As mjtActuator = mjtActuator.mjACTUATOR_GENERAL

    Public Property Trntype As mjtTrn = mjtTrn.mjTRN_JOINT
    ''' <summary>传动目标的名字（关节名、站点名或 body 名）</summary>
    Public Property Target As String

    ''' <summary>位置伺服的刚度</summary>
    Public Property Kp As Double = 0.0
    ''' <summary>速度伺服的阻尼</summary>
    Public Property Kv As Double = 0.0
    ''' <summary>
    ''' 阻尼比（MJCF 的 <c>dampratio</c>），0 表示未指定。
    ''' </summary>
    ''' <remarks>
    ''' MuJoCo 允许用阻尼比代替 <c>kv</c>：指定 <c>dampratio</c> 时
    ''' <c>kv = 2·dampratio·√(kp·M)</c>（<c>M</c> 是执行器的等效惯量，缺省 1），
    ''' 且优先于显式写出的 <c>kv</c>。
    ''' duck 的 <c>chosen_actuator</c> 只写了 <c>dampratio="1"</c>，
    ''' 漏掉这一步会让每个位置伺服变成零阻尼的弹簧——实测站立 0.4 s 内
    ''' 角速度就冲到 2 rad/s，随后整机翻倒并躺在地上打转。
    ''' </remarks>
    Public Property Dampratio As Double = 0.0
    ''' <summary>力的缩放（<c>gear</c>）</summary>
    Public Property Gear As Double = 1.0

    Public Property ForceRange As Double() = {-1.0E+10, 1.0E+10}
    Public Property ForceLimited As Boolean = False
    Public Property CtrlRange As Double() = {-1.0E+10, 1.0E+10}
    Public Property CtrlLimited As Boolean = False

    Public Property Group As Integer = 0
    Public Property DynType As mjtDyn = mjtDyn.mjDYN_NONE

    ''' <summary>编译期解析出的传动目标 id</summary>
    Friend Property TrnId As Integer = -1

End Class

''' <summary>可编辑的传感器（对应 <c>mjsSensor</c>）</summary>
Public Class MjsSensor

    Public Property Name As String
    Public Property Type As mjtSensor = mjtSensor.mjSENS_FRAMEPOS

    Public Property ObjType As mjtObj = mjtObj.mjOBJ_SITE
    Public Property ObjName As String
    Public Property RefType As mjtObj = mjtObj.mjOBJ_UNKNOWN
    Public Property RefName As String

    Public Property Noise As Double = 0.0
    Public Property Cutoff As Double = 0.0
    Public Property [Dim] As Integer = 0

    Friend Property ObjId As Integer = -1
    Friend Property RefId As Integer = -1

End Class

''' <summary>可编辑的等式约束（对应 <c>mjsEquality</c>）</summary>
Public Class MjsEquality

    Public Property Name As String
    Public Property Type As mjtEq = mjtEq.mjEQ_CONNECT

    Public Property Obj1Type As mjtObj = mjtObj.mjOBJ_BODY
    Public Property Obj1Name As String
    Public Property Obj2Type As mjtObj = mjtObj.mjOBJ_BODY
    Public Property Obj2Name As String

    ''' <summary>CONNECT: anchor1(3), anchor2(3)；WELD: anchor(3) + relpose(7)；JOINT: 2 个系数</summary>
    Public Property Data As Double() = New Double(mjNEQDATA - 1) {}
    Public Property Solref As Double() = {0.02, 1.0}
    Public Property Solimp As Double() = {0.9, 0.95, 0.001, 0.5, 2.0}
    Public Property Active As Boolean = True

    Friend Property Obj1Id As Integer = -1
    Friend Property Obj2Id As Integer = -1

End Class

''' <summary>腱定义（对应 <c>mjsTendon</c>）</summary>
Public Class MjsTendon

    Public Property Name As String
    Public Property Type As mjtTendon = mjtTendon.mjTENDON_FIXED

    ''' <summary>固定腱的关节名列表</summary>
    Public ReadOnly Property Joints As New List(Of String)()
    ''' <summary>空间腱的站点名列表</summary>
    Public ReadOnly Property Sites As New List(Of String)()

    Public Property Stiffness As Double = 0.0
    Public Property Damping As Double = 0.0
    Public Property Armature As Double = 0.0
    Public Property FrictionLoss As Double = 0.0
    Public Property Length0 As Double = 0.0
    Public Property LengthSpring As Double = 0.0
    Public Property Range As Double() = {0.0, 0.0}
    Public Property Limited As Boolean = False

End Class

''' <summary>关键帧（对应 <c>mjsKey</c>）</summary>
Public Class MjsKeyframe
    Public Property Name As String
    Public Property Time As Double = 0.0
    Public Property Qpos As Double() = Nothing
    Public Property Qvel As Double() = Nothing
    Public Property Ctrl As Double() = Nothing
    Public Property Act As Double() = Nothing
End Class

''' <summary>禁止碰撞的 body 对（对应 <c>mjsExclude</c>）</summary>
Public Class MjsExclude
    Public Property Body1 As String
    Public Property Body2 As String
End Class

''' <summary>
''' 可编辑的模型树，对应 <c>mjspec.h</c> 的 <c>mjSpec</c>。
''' </summary>
''' <remarks>
''' <para>
''' 两种用法：
''' </para>
''' <list type="number">
''' <item><description>
''' 由 <c>MjcfParser</c> 从 MJCF 文件读出来，再交给 <see cref="Compile"/>；
''' </description></item>
''' <item><description>
''' 由代码直接从零构建（MicroduckLab 的场地与小球就是这么加进去的），
''' 用 <see cref="AddBody"/> / <see cref="AddGeom"/> / <see cref="AddJoint"/> 等方法。
''' </description></item>
''' </list>
''' </remarks>
Public Class MjSpec

    Public Property ModelName As String = ""
    Public Property Opt As New mjOption()
    Public Property Stat As New mjStatistic()

    ''' <summary>MJCF 的 <c>compiler angle</c> 是否为角度制（编译时统一换成弧度）</summary>
    Public Property AngleInDegrees As Boolean = False
    ''' <summary>MJCF 的 <c>compiler meshdir</c></summary>
    Public Property MeshDir As String = ""
    ''' <summary>MJCF 的 <c>compiler autolimits</c></summary>
    Public Property AutoLimits As Boolean = True
    ''' <summary>MJCF 的 <c>compiler inertiafromgeom</c></summary>
    Public Property InertiaFromGeom As Boolean = False
    ''' <summary>MJCF 文档所在的目录，用于解析相对路径</summary>
    Public Property SourceDirectory As String = ""

    ''' <summary>
    ''' 自定义的网格路径解析器：给一个 <c>&lt;mesh file="..."&gt;</c> 的文件名，
    ''' 返回磁盘上的绝对路径。<c>Nothing</c> 时按
    ''' <c>SourceDirectory / meshdir / file</c> 拼接。
    ''' </summary>
    ''' <remarks>
    ''' 宿主程序的资产目录往往和 MJCF 里写的 <c>meshdir</c> 不一致
    ''' （duck 的 MJCF 写的是 <c>assets</c>，真实目录是 <c>meshes</c>），
    ''' 所以给调用方留一个钩子，而不是在编译期猜。
    ''' </remarks>
    Public Property MeshResolver As Func(Of String, String) = Nothing

    ''' <summary>根 body（worldbody），名字为空，id 恒为 0</summary>
    Public ReadOnly Property World As MjsBody

    Public ReadOnly Property Meshes As New List(Of MjsMesh)()
    Public ReadOnly Property Materials As New List(Of MjsMaterial)()
    Public ReadOnly Property Actuators As New List(Of MjsActuator)()
    Public ReadOnly Property Sensors As New List(Of MjsSensor)()
    Public ReadOnly Property Equalities As New List(Of MjsEquality)()
    Public ReadOnly Property Tendons As New List(Of MjsTendon)()
    Public ReadOnly Property Keys As New List(Of MjsKeyframe)()
    Public ReadOnly Property Excludes As New List(Of MjsExclude)()

    ''' <summary>default 类树的根（对应 <c>main</c> 类）</summary>
    Public ReadOnly Property Defaults As MjsDefault

    Private ReadOnly m_classes As New Dictionary(Of String, MjsDefault)(StringComparer.Ordinal)

    Public Sub New()
        Defaults = New MjsDefault("main", Nothing)
        m_classes("main") = Defaults
        World = New MjsBody With {.Name = "", .Parent = Nothing, .DefaultClass = Defaults}
    End Sub

    ' /********************************************************************************/
    '  default 类
    ' /********************************************************************************/

    ''' <summary>取得（必要时创建）一个 default 类</summary>
    Public Function ClassOf(name As String) As MjsDefault
        If String.IsNullOrEmpty(name) Then
            Return Defaults
        End If

        Dim node As MjsDefault = Nothing

        If m_classes.TryGetValue(name, node) Then
            Return node
        End If

        node = New MjsDefault(name, Defaults)
        m_classes(name) = node

        Return node
    End Function

    ' /********************************************************************************/
    '  建模 API
    ' /********************************************************************************/

    ''' <summary>在 <paramref name="parent"/> 下加一个 body</summary>
    Public Function AddBody(parent As MjsBody, name As String,
                            Optional pos As Vector3? = Nothing,
                            Optional quat As Quaternion? = Nothing) As MjsBody
        If parent Is Nothing Then
            parent = World
        End If

        Dim body As New MjsBody With {
            .Name = If(name, ""),
            .Parent = parent,
            .DefaultClass = parent.DefaultClass
        }

        If pos.HasValue Then
            Dim p As Vector3 = pos.Value
            body.Pos = {p.X, p.Y, p.Z}
        End If

        If quat.HasValue Then
            Dim q As Quaternion = MjMath.Normalize(quat.Value)
            body.Quat = {q.W, q.X, q.Y, q.Z}
        End If

        Call parent.Children.Add(body)

        Return body
    End Function

    ''' <summary>加一个铰链关节</summary>
    Public Function AddJoint(body As MjsBody, name As String, type As mjtJoint,
                             Optional axis As Vector3? = Nothing,
                             Optional range As Vector2? = Nothing,
                             Optional pos As Vector3? = Nothing) As MjsJoint
        Dim joint As New MjsJoint With {
            .Name = If(name, ""),
            .Type = type,
            .Body = body
        }

        If axis.HasValue Then
            Dim a As Vector3 = axis.Value
            joint.Axis = {a.X, a.Y, a.Z}
        End If

        If pos.HasValue Then
            Dim p As Vector3 = pos.Value
            joint.Pos = {p.X, p.Y, p.Z}
        End If

        If range.HasValue Then
            joint.Range = {range.Value.X, range.Value.Y}
            joint.Limited = True
        End If

        Call body.Joints.Add(joint)

        Return joint
    End Function

    ''' <summary>加一个自由关节（浮动基座）</summary>
    Public Function AddFreeJoint(body As MjsBody, Optional name As String = Nothing) As MjsJoint
        Return AddJoint(body, name, mjtJoint.mjJNT_FREE)
    End Function

    ''' <summary>加一个几何体</summary>
    Public Function AddGeom(body As MjsBody, type As mjtGeom,
                            Optional size As Vector3? = Nothing,
                            Optional pos As Vector3? = Nothing,
                            Optional quat As Quaternion? = Nothing,
                            Optional meshName As String = Nothing,
                            Optional name As String = Nothing,
                            Optional rgba As Vector4? = Nothing) As MjsGeom
        Dim geom As New MjsGeom With {
            .Name = If(name, ""),
            .Type = type,
            .MeshName = meshName,
            .Body = body
        }

        If size.HasValue Then
            Dim s As Vector3 = size.Value
            geom.Size = {s.X, s.Y, s.Z}
        End If

        If pos.HasValue Then
            Dim p As Vector3 = pos.Value
            geom.Pos = {p.X, p.Y, p.Z}
        End If

        If quat.HasValue Then
            Dim q As Quaternion = MjMath.Normalize(quat.Value)
            geom.Quat = {q.W, q.X, q.Y, q.Z}
        End If

        If rgba.HasValue Then
            Dim c As Vector4 = rgba.Value
            geom.Rgba = {c.X, c.Y, c.Z, c.W}
        End If

        Call body.Geoms.Add(geom)

        Return geom
    End Function

    ''' <summary>加一个站点</summary>
    Public Function AddSite(body As MjsBody, name As String,
                            Optional pos As Vector3? = Nothing,
                            Optional quat As Quaternion? = Nothing,
                            Optional size As Vector3? = Nothing) As MjsSite
        Dim site As New MjsSite With {.Name = If(name, ""), .Body = body}

        If pos.HasValue Then
            Dim p As Vector3 = pos.Value
            site.Pos = {p.X, p.Y, p.Z}
        End If

        If quat.HasValue Then
            Dim q As Quaternion = MjMath.Normalize(quat.Value)
            site.Quat = {q.W, q.X, q.Y, q.Z}
        End If

        If size.HasValue Then
            Dim s As Vector3 = size.Value
            site.Size = {s.X, s.Y, s.Z}
        End If

        Call body.Sites.Add(site)

        Return site
    End Function

    ''' <summary>加一个网格资产（直接给三角形）</summary>
    Public Function AddMesh(name As String, vertices As Double(), faces As Integer()) As MjsMesh
        Dim mesh As New MjsMesh With {
            .Name = name,
            .Vertices = vertices,
            .Faces = faces
        }

        Call Meshes.Add(mesh)

        Return mesh
    End Function

    ''' <summary>加一个从文件读取的网格资产</summary>
    Public Function AddMeshFile(name As String, file As String) As MjsMesh
        Dim mesh As New MjsMesh With {.Name = name, .File = file}

        Call Meshes.Add(mesh)

        Return mesh
    End Function

    ''' <summary>加一个材质</summary>
    Public Function AddMaterial(name As String, rgba As Vector4) As MjsMaterial
        Dim material As New MjsMaterial With {
            .Name = name,
            .Rgba = {rgba.X, rgba.Y, rgba.Z, rgba.W}
        }

        Call Materials.Add(material)

        Return material
    End Function

    ''' <summary>加一个执行器</summary>
    Public Function AddActuator(kind As mjtActuator, target As String, name As String,
                                Optional kp As Double = 0.0, Optional kv As Double = 0.0,
                                Optional forceRange As Vector2? = Nothing,
                                Optional ctrlRange As Vector2? = Nothing) As MjsActuator
        Dim actuator As New MjsActuator With {
            .Name = If(name, target),
            .Kind = kind,
            .Target = target,
            .Trntype = mjtTrn.mjTRN_JOINT,
            .Kp = kp,
            .Kv = kv
        }

        If forceRange.HasValue Then
            actuator.ForceRange = {forceRange.Value.X, forceRange.Value.Y}
            actuator.ForceLimited = True
        End If

        If ctrlRange.HasValue Then
            actuator.CtrlRange = {ctrlRange.Value.X, ctrlRange.Value.Y}
            actuator.CtrlLimited = True
        End If

        Call Actuators.Add(actuator)

        Return actuator
    End Function

    ''' <summary>加一个传感器</summary>
    Public Function AddSensor(type As mjtSensor, objType As mjtObj, objName As String,
                              Optional name As String = Nothing,
                              Optional noise As Double = 0.0) As MjsSensor
        Dim sensor As New MjsSensor With {
            .Name = If(name, ""),
            .Type = type,
            .ObjType = objType,
            .ObjName = objName,
            .Noise = noise
        }

        Call Sensors.Add(sensor)

        Return sensor
    End Function

    ''' <summary>加一个等式约束</summary>
    Public Function AddEquality(type As mjtEq, obj1 As String, obj2 As String,
                                Optional data As Double() = Nothing) As MjsEquality
        Dim eq As New MjsEquality With {
            .Type = type,
            .Obj1Name = obj1,
            .Obj2Name = obj2
        }

        If data IsNot Nothing Then
            Call Array.Copy(data, eq.Data, Math.Min(data.Length, eq.Data.Length))
        End If

        Call Equalities.Add(eq)

        Return eq
    End Function

    ''' <summary>加一个固定腱</summary>
    Public Function AddTendon(name As String, ParamArray jointNames As String()) As MjsTendon
        Dim tendon As New MjsTendon With {.Name = name}

        For Each j As String In jointNames
            Call tendon.Joints.Add(j)
        Next

        Call Tendons.Add(tendon)

        Return tendon
    End Function

    ''' <summary>加一个关键帧</summary>
    Public Function AddKeyframe(name As String, qpos As Double(),
                                Optional ctrl As Double() = Nothing) As MjsKeyframe
        Dim key As New MjsKeyframe With {.Name = name, .Qpos = qpos, .Ctrl = ctrl}

        Call Keys.Add(key)

        Return key
    End Function

    ''' <summary>禁止两个 body 之间的碰撞</summary>
    Public Sub AddExclude(body1 As String, body2 As String)
        Call Excludes.Add(New MjsExclude With {.Body1 = body1, .Body2 = body2})
    End Sub

    ' /********************************************************************************/
    '  查找
    ' /********************************************************************************/

    ''' <summary>深度优先遍历全部 body（worldbody 在前）</summary>
    Public Function EnumerateBodies() As List(Of MjsBody)
        Dim found As New List(Of MjsBody)()
        Call Walk(World, found)

        Return found
    End Function

    Private Shared Sub Walk(body As MjsBody, found As List(Of MjsBody))
        Call found.Add(body)

        For Each child As MjsBody In body.Children
            Call Walk(child, found)
        Next
    End Sub

    Public Function FindBody(name As String) As MjsBody
        For Each body As MjsBody In EnumerateBodies()
            If String.Equals(body.Name, name, StringComparison.Ordinal) Then
                Return body
            End If
        Next

        Return Nothing
    End Function

    Public Function FindJoint(name As String) As MjsJoint
        For Each body As MjsBody In EnumerateBodies()
            For Each joint As MjsJoint In body.Joints
                If String.Equals(joint.Name, name, StringComparison.Ordinal) Then
                    Return joint
                End If
            Next
        Next

        Return Nothing
    End Function

    Public Function FindGeom(name As String) As MjsGeom
        For Each body As MjsBody In EnumerateBodies()
            For Each geom As MjsGeom In body.Geoms
                If String.Equals(geom.Name, name, StringComparison.Ordinal) Then
                    Return geom
                End If
            Next
        Next

        Return Nothing
    End Function

    Public Function FindSite(name As String) As MjsSite
        For Each body As MjsBody In EnumerateBodies()
            For Each site As MjsSite In body.Sites
                If String.Equals(site.Name, name, StringComparison.Ordinal) Then
                    Return site
                End If
            Next
        Next

        Return Nothing
    End Function

    Public Function FindMesh(name As String) As MjsMesh
        For Each mesh As MjsMesh In Meshes
            If String.Equals(mesh.Name, name, StringComparison.Ordinal) Then
                Return mesh
            End If
        Next

        Return Nothing
    End Function

    Public Function FindMaterial(name As String) As MjsMaterial
        For Each material As MjsMaterial In Materials
            If String.Equals(material.Name, name, StringComparison.Ordinal) Then
                Return material
            End If
        Next

        Return Nothing
    End Function

    ''' <summary>从模型里删掉一个 body（连同它的子树）</summary>
    Public Function RemoveBody(name As String) As Boolean
        Dim body As MjsBody = FindBody(name)

        If body Is Nothing OrElse body.Parent Is Nothing Then
            Return False
        End If

        Return body.Parent.Children.Remove(body)
    End Function

    ' /********************************************************************************/
    '  编译
    ' /********************************************************************************/

    ''' <summary>把可编辑的模型树编译成只读的 <see cref="mjModel"/></summary>
    Public Function Compile(Optional ByRef errorText As String = Nothing) As mjModel
        Dim buffer As New MjErrorBuffer()
        Dim model As mjModel = MjcfCompiler.Compile(Me, buffer)

        errorText = buffer.Text

        If model Is Nothing Then
            Throw New MjException(If(buffer.HasError, buffer.Text, "the model could not be compiled"))
        End If

        Return model
    End Function

End Class
