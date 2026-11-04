Imports num = System.Numerics

Namespace Scene

    ''' <summary>MJCF 场景的全局选项。</summary>
    Public Class MjSceneOptions

        Public Property Timestep As Double = 1.0 / 120.0
        Public Property Gravity As num.Vector3 = New num.Vector3(0.0F, 0.0F, -9.81F)
        Public Property Iterations As Integer = 20
        Public Property GroundPlane As Boolean = True
        Public Property GroundSize As Single = 2.0F
    End Class

    ''' <summary>一个 body 的中性描述（不依赖 MJCF 解析器）。</summary>
    Public Class MjBodySpec

        Public Property Name As String
        ''' <summary>父 body 名；空表示挂在 worldbody 下。</summary>
        Public Property Parent As String
        Public Property Pos As num.Vector3 = num.Vector3.Zero
        Public Property Quat As num.Quaternion = num.Quaternion.Identity
        Public Property Mass As Double = 0.0
        ''' <summary>ixx, iyy, izz, ixy, ixz, iyz</summary>
        Public Property Inertia As Double()
        Public Property GeomType As mjtGeom = mjtGeom.mjGEOM_BOX
        ''' <summary>geom 的半尺寸（box）或半径/长度。</summary>
        Public Property Extents As num.Vector3 = New num.Vector3(0.02F, 0.02F, 0.02F)
        Public Property MeshName As String
        Public Property CollisionGroup As Integer = 0
        Public Property CollisionMask As Integer = 0
        ''' <summary>是否为运动学体（不参与动力学积分，只作为碰撞几何）。</summary>
        Public Property Kinematic As Boolean = False
    End Class

    ''' <summary>一个关节的中性描述。</summary>
    Public Class MjJointSpec

        Public Property Name As String
        Public Property Body As String
        Public Property Type As mjtJoint = mjtJoint.mjJNT_HINGE
        Public Property Axis As num.Vector3 = num.Vector3.UnitZ
        Public Property Pos As num.Vector3 = num.Vector3.Zero
        Public Property Lower As Double = 0.0
        Public Property Upper As Double = 0.0
        Public Property Stiffness As Double = 0.0
        Public Property Damping As Double = 0.0
        Public Property Armature As Double = 0.0
    End Class

    ''' <summary>一个执行器的中性描述。</summary>
    Public Class MjActuatorSpec

        Public Property Name As String
        Public Property Joint As String
        Public Property Kind As mjtActuator = mjtActuator.mjACTUATOR_POSITION
        Public Property Kp As Double = 0.0
        Public Property Kv As Double = 0.0
        Public Property CtrlLower As Double = 0.0
        Public Property CtrlUpper As Double = 0.0
        Public Property ForceLower As Double = 0.0
        Public Property ForceUpper As Double = 0.0
    End Class

    ''' <summary>
    ''' 场景的中性描述：宿主把 URDF / GLB / 程序化几何映射成这些 DTO，
    ''' 再由 <see cref="MjSceneBuilder"/> 编译成 <see cref="MjSpec"/>。
    ''' </summary>
    Public Class MjSceneSpec

        Public ReadOnly Property Options As New MjSceneOptions()
        Public ReadOnly Property Bodies As New List(Of MjBodySpec)()
        Public ReadOnly Property Joints As New List(Of MjJointSpec)()
        Public ReadOnly Property Actuators As New List(Of MjActuatorSpec)()

        Public Function AddBody(name As String,
                                Optional parent As String = Nothing,
                                Optional pos As num.Vector3 = Nothing) As MjBodySpec
            Dim body As New MjBodySpec With {
                .Name = name,
                .Parent = parent
            }

            If pos <> num.Vector3.Zero Then body.Pos = pos

            Call Bodies.Add(body)

            Return body
        End Function

        Public Function AddJoint(name As String, body As String,
                                 Optional type As mjtJoint = mjtJoint.mjJNT_HINGE) As MjJointSpec
            Dim joint As New MjJointSpec With {
                .Name = name,
                .Body = body,
                .Type = type
            }

            Call Joints.Add(joint)

            Return joint
        End Function

        Public Function AddActuator(name As String, joint As String,
                                    Optional kp As Double = 0.0) As MjActuatorSpec
            Dim actuator As New MjActuatorSpec With {
                .Name = name,
                .Joint = joint,
                .Kp = kp
            }

            Call Actuators.Add(actuator)

            Return actuator
        End Function

        Public Function FindBody(name As String) As MjBodySpec
            For Each body As MjBodySpec In Bodies
                If String.Equals(body.Name, name, StringComparison.Ordinal) Then Return body
            Next

            Return Nothing
        End Function
    End Class

End Namespace
