Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports EngMath = Microsoft.VisualBasic.Engine.Physics.Math3D
Imports EngCollision = Microsoft.VisualBasic.Engine.Physics.Collision3D

Namespace Interop

''' <summary>接触材质：摩擦系数与恢复系数。</summary>
Public Class PhysicsMaterial

    Friend ReadOnly EngineMaterial As Eng.PhysicsMaterial

    Public Sub New(Optional friction As Double = 0.4, Optional restitution As Double = 0.0)
        Me.EngineMaterial = New Eng.PhysicsMaterial(friction, restitution)
    End Sub

    Friend Sub New(inner As Eng.PhysicsMaterial)
        Me.EngineMaterial = inner
    End Sub

    Public Property Friction As Double
        Get
            Return EngineMaterial.Friction
        End Get
        Set(value As Double)
            EngineMaterial.Friction = value
        End Set
    End Property

    Public Property Restitution As Double
        Get
            Return EngineMaterial.Restitution
        End Get
        Set(value As Double)
            EngineMaterial.Restitution = value
        End Set
    End Property
End Class

''' <summary>
''' MuJoCo 场景层的刚体句柄。所有位姿/速度读写都直接作用于内核刚体，
''' 因此运动学与渲染层拿到的是实时值，而不是过期副本。
''' </summary>
Public Class RigidBody3D

    Friend ReadOnly EngineBody As Eng.RigidBody3D

    Public Sub New(shape As Collision3D.Collider3D,
                   Optional mass As Double = 1.0,
                   Optional material As PhysicsMaterial = Nothing)

        Me.EngineBody = New Eng.RigidBody3D(
            If(shape Is Nothing, Nothing, shape.EngineShape),
            mass, MjInterop.Material(material))
    End Sub

    Friend Sub New(inner As Eng.RigidBody3D)
        Me.EngineBody = inner
    End Sub

    Public Property ID As String
        Get
            Return EngineBody.ID
        End Get
        Set(value As String)
            EngineBody.ID = value
        End Set
    End Property

    Public Property Label As String
        Get
            Return EngineBody.Label
        End Get
        Set(value As String)
            EngineBody.Label = value
        End Set
    End Property

    Public Property Position As Vector3
        Get
            Return MjInterop.V(EngineBody.Position)
        End Get
        Set(value As Vector3)
            EngineBody.Position = MjInterop.V(value)
        End Set
    End Property

    Public Property Orientation As Math3D.Quaternion
        Get
            Return MjInterop.Q(EngineBody.Orientation)
        End Get
        Set(value As Math3D.Quaternion)
            EngineBody.Orientation = MjInterop.Q(value)
        End Set
    End Property

    Public Property Velocity As Vector3
        Get
            Return MjInterop.V(EngineBody.Velocity)
        End Get
        Set(value As Vector3)
            EngineBody.Velocity = MjInterop.V(value)
        End Set
    End Property

    Public Property AngularVelocity As Vector3
        Get
            Return MjInterop.V(EngineBody.AngularVelocity)
        End Get
        Set(value As Vector3)
            EngineBody.AngularVelocity = MjInterop.V(value)
        End Set
    End Property

    Public Property Mass As Double
        Get
            Return EngineBody.Mass
        End Get
        Set(value As Double)
            EngineBody.Mass = value
        End Set
    End Property

    Public Property InertiaLocal As Math3D.Matrix3x3
        Get
            Return MjInterop.M3(EngineBody.InertiaLocal)
        End Get
        Set(value As Math3D.Matrix3x3)
            EngineBody.InertiaLocal = MjInterop.M3(value)
        End Set
    End Property

    Public Property Shape As Collision3D.Collider3D
        Get
            Return MjInterop.WrapCollider(EngineBody.Shape)
        End Get
        Set(value As Collision3D.Collider3D)
            EngineBody.Shape = If(value Is Nothing, Nothing, value.EngineShape)
        End Set
    End Property

    Public Property Material As PhysicsMaterial
        Get
            If EngineBody.Material Is Nothing Then Return Nothing
            Return New PhysicsMaterial(EngineBody.Material)
        End Get
        Set(value As PhysicsMaterial)
            EngineBody.Material = MjInterop.Material(value)
        End Set
    End Property

    Public Property IsStatic As Boolean
        Get
            Return EngineBody.IsStatic
        End Get
        Set(value As Boolean)
            EngineBody.IsStatic = value
        End Set
    End Property

    Public Property CollisionGroup As Integer
        Get
            Return EngineBody.CollisionGroup
        End Get
        Set(value As Integer)
            EngineBody.CollisionGroup = value
        End Set
    End Property

    Public Property CollisionMask As Integer
        Get
            Return EngineBody.CollisionMask
        End Get
        Set(value As Integer)
            EngineBody.CollisionMask = value
        End Set
    End Property

    Public Property LinearDamping As Double
        Get
            Return EngineBody.LinearDamping
        End Get
        Set(value As Double)
            EngineBody.LinearDamping = value
        End Set
    End Property

    Public Property AngularDamping As Double
        Get
            Return EngineBody.AngularDamping
        End Get
        Set(value As Double)
            EngineBody.AngularDamping = value
        End Set
    End Property

    Public Property MaxSpeed As Double
        Get
            Return EngineBody.MaxSpeed
        End Get
        Set(value As Double)
            EngineBody.MaxSpeed = value
        End Set
    End Property

    Public Property MaxAngularSpeed As Double
        Get
            Return EngineBody.MaxAngularSpeed
        End Get
        Set(value As Double)
            EngineBody.MaxAngularSpeed = value
        End Set
    End Property

    Public ReadOnly Property IsSleeping As Boolean
        Get
            Return EngineBody.IsSleeping
        End Get
    End Property

    Public ReadOnly Property InvMass As Double
        Get
            Return EngineBody.InvMass
        End Get
    End Property

    Public Sub SetStatic()
        Call EngineBody.SetStatic()
    End Sub

    Public Sub SetKinematic()
        Call EngineBody.SetKinematic()
    End Sub

    Public Sub Wake()
        Call EngineBody.Wake()
    End Sub

    Public Sub InvalidateInertiaCache()
        Call EngineBody.InvalidateInertiaCache()
    End Sub

    Public Sub ApplyForce(f As Vector3)
        Call EngineBody.ApplyForce(MjInterop.V(f))
    End Sub

    Public Sub ApplyTorque(t As Vector3)
        Call EngineBody.ApplyTorque(MjInterop.V(t))
    End Sub

    Public Sub ClearForces()
        Call EngineBody.ClearForces()
    End Sub

    Public Function ToWorld(localPoint As Vector3) As Vector3
        Return MjInterop.V(EngineBody.ToWorld(MjInterop.V(localPoint)))
    End Function

    Public Function ToLocal(worldPoint As Vector3) As Vector3
        Return MjInterop.V(EngineBody.ToLocal(MjInterop.V(worldPoint)))
    End Function

    Public Function GetAABB() As Collision3D.AABB3D
        Dim box As EngCollision.AABB3D = EngineBody.GetAABB()

        Return New Collision3D.AABB3D(MjInterop.V(box.min), MjInterop.V(box.max))
    End Function

    Public Overrides Function ToString() As String
        Return EngineBody.ToString()
    End Function
End Class

End Namespace
