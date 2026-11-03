Imports System.Numerics

Namespace Assets

    ''' <summary>
    ''' One collision geometry of a body.
    ''' </summary>
    Public Class MjcfGeom
        Public Property Name As String
        Public Property Mesh As String
        Public Property Type As String = "mesh"
        Public Property Position As Vector3 = Vector3.Zero
        Public Property Rotation As Quaternion = Quaternion.Identity
        Public Property Contype As Integer = 1
        Public Property Conaffinity As Integer = 1
        Public Property ClassName As String
    End Class

    ''' <summary>
    ''' The inertial properties of a body: the mass, the center of mass inside
    ''' the body frame and the full inertia tensor about that center.
    ''' </summary>
    Public Class MjcfInertial
        Public Property Position As Vector3 = Vector3.Zero
        Public Property Mass As Double = 0
        ''' <summary>ixx, iyy, izz, ixy, ixz, iyz</summary>
        Public Property Inertia As Double() = {0, 0, 0, 0, 0, 0}
    End Class

    ''' <summary>
    ''' One body of the MJCF model, flattened out of the document tree.
    ''' </summary>
    Public Class MjcfBody
        Public Property Name As String
        Public Property Parent As String
        Public Property Position As Vector3 = Vector3.Zero
        Public Property Rotation As Quaternion = Quaternion.Identity
        Public Property Inertial As New MjcfInertial()

        ' the single hinge of the body (the duck has exactly one per link)
        Public Property JointName As String
        Public Property JointType As String = "hinge"
        Public Property JointAxis As Vector3 = Vector3.UnitZ
        Public Property JointRange As Double() = {-Math.PI, Math.PI}
        Public Property JointDamping As Double = 0
        Public Property JointArmature As Double = 0
        Public Property JointFrictionLoss As Double = 0

        Public ReadOnly Property Geoms As New List(Of MjcfGeom)

        ''' <summary>true when the body is welded to its parent (no joint)</summary>
        Public ReadOnly Property IsWeld As Boolean
            Get
                Return String.IsNullOrEmpty(JointName)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' One ``position`` actuator: the force servo of a joint.
    ''' </summary>
    Public Class MjcfActuator
        Public Property Name As String
        Public Property Joint As String
        Public Property Kp As Double = 0
        Public Property Kv As Double = 0
        Public Property ForceMin As Double = -1000000000.0
        Public Property ForceMax As Double = 1000000000.0
    End Class

End Namespace