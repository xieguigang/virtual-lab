Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports EngCollision = Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports EngJoints = Microsoft.VisualBasic.Engine.Physics.Joints3D

Namespace Interop

''' <summary>
''' MuJoCo 场景层的物理世界句柄：装配刚体/约束、定步长推进、查询接触。
''' </summary>
''' <remarks>
''' 步进完全由本程序集驱动；<see cref="Bodies"/> 中的包装句柄按内核刚体
''' 缓存，保证渲染与协议层拿到稳定的对象身份。
''' </remarks>
Public Class PhysicsWorld3D

    Friend ReadOnly EngineWorld As Eng.PhysicsWorld3D

    Private ReadOnly _bodies As New Dictionary(Of Eng.RigidBody3D, RigidBody3D)()
    Private ReadOnly _constraints As New List(Of Joints3D.IConstraint3D)()

    Public Sub New()
        Me.EngineWorld = New Eng.PhysicsWorld3D()
    End Sub

    Public Property Iterations As Integer
        Get
            Return EngineWorld.Iterations
        End Get
        Set(value As Integer)
            EngineWorld.Iterations = value
        End Set
    End Property

    Public Property Substeps As Integer
        Get
            Return EngineWorld.Substeps
        End Get
        Set(value As Integer)
            EngineWorld.Substeps = value
        End Set
    End Property

    Public Property FixedDt As Double
        Get
            Return EngineWorld.FixedDt
        End Get
        Set(value As Double)
            EngineWorld.FixedDt = value
        End Set
    End Property

    Public Property AllowSleep As Boolean
        Get
            Return EngineWorld.AllowSleep
        End Get
        Set(value As Boolean)
            EngineWorld.AllowSleep = value
        End Set
    End Property

    Public Property WarmStarting As Boolean
        Get
            Return EngineWorld.WarmStarting
        End Get
        Set(value As Boolean)
            EngineWorld.WarmStarting = value
        End Set
    End Property

    Public Property Baumgarte As Double
        Get
            Return EngineWorld.Baumgarte
        End Get
        Set(value As Double)
            EngineWorld.Baumgarte = value
        End Set
    End Property

    Public Property Slop As Double
        Get
            Return EngineWorld.Slop
        End Get
        Set(value As Double)
            EngineWorld.Slop = value
        End Set
    End Property

    Public Property Gravity As Vector3
        Get
            Return MjInterop.V(EngineWorld.Gravity)
        End Get
        Set(value As Vector3)
            EngineWorld.Gravity = MjInterop.V(value)
        End Set
    End Property

    Public ReadOnly Property SolverIterations As Integer
        Get
            Return EngineWorld.SolverIterations
        End Get
    End Property

    Public ReadOnly Property LastResidual As Double
        Get
            Return EngineWorld.LastResidual
        End Get
    End Property

    ''' <summary>世界中的刚体（顺序与内核一致）。</summary>
    Public ReadOnly Property Bodies As List(Of RigidBody3D)
        Get
            Dim result As New List(Of RigidBody3D)()

            For Each body As Eng.RigidBody3D In EngineWorld.Bodies
                result.Add(WrapBody(body))
            Next

            Return result
        End Get
    End Property

    ''' <summary>最近一次步进产生的接触流形。</summary>
    Public ReadOnly Property Contacts As List(Of Collision3D.Manifold3D)
        Get
            Dim result As New List(Of Collision3D.Manifold3D)()

            If EngineWorld.Contacts Is Nothing Then Return result

            For Each manifold As EngCollision.Manifold3D In EngineWorld.Contacts
                result.Add(New Collision3D.Manifold3D(manifold))
            Next

            Return result
        End Get
    End Property

    ''' <summary>已装配的约束（关节与弹簧）。</summary>
    Public ReadOnly Property Constraints As List(Of Joints3D.IConstraint3D)
        Get
            Return _constraints
        End Get
    End Property

    Public Sub Add(body As RigidBody3D)
        If body Is Nothing Then Return

        Call EngineWorld.Add(body.EngineBody)

        If Not _bodies.ContainsKey(body.EngineBody) Then
            _bodies.Add(body.EngineBody, body)
        End If
    End Sub

    Public Sub Add(constraint As Joints3D.IConstraint3D)
        If constraint Is Nothing Then Return

        Dim inner As EngJoints.IConstraint3D = MjInterop.ConstraintOf(constraint)

        If inner Is Nothing Then Return

        Call EngineWorld.Add(inner)

        If Not _constraints.Contains(constraint) Then
            _constraints.Add(constraint)
        End If
    End Sub

    Public Sub Add(servo As Joints3D.JointServo3D)
        If servo Is Nothing Then Return

        Call EngineWorld.Add(servo.EngineHook)
    End Sub

    Public Function Remove(body As RigidBody3D) As Boolean
        If body Is Nothing Then Return False

        Call _bodies.Remove(body.EngineBody)

        Return EngineWorld.Remove(body.EngineBody)
    End Function

    Public Function Remove(idOrLabel As String) As Boolean
        Return EngineWorld.Remove(idOrLabel)
    End Function

    ''' <summary>移除约束并同步内核世界的约束列表。</summary>
    Public Function RemoveConstraint(constraint As Joints3D.IConstraint3D) As Boolean
        If constraint Is Nothing Then Return False

        Dim inner As EngJoints.IConstraint3D = MjInterop.ConstraintOf(constraint)

        If inner IsNot Nothing Then Call EngineWorld.Constraints.Remove(inner)

        Return _constraints.Remove(constraint)
    End Function

    Public Function Find(idOrLabel As String) As RigidBody3D
        Return WrapBody(EngineWorld.Find(idOrLabel))
    End Function

    Public Sub [Step](frameDt As Double)
        Call EngineWorld.Step(frameDt)
    End Sub

    Public Sub ClearVelocities()
        Call EngineWorld.ClearVelocities()
    End Sub

    Public Sub ClearContactCache()
        Call EngineWorld.ClearContactCache()
    End Sub

    Public Sub Clear()
        Call EngineWorld.Clear()
        Call _bodies.Clear()
        Call _constraints.Clear()
    End Sub

    Private Function WrapBody(body As Eng.RigidBody3D) As RigidBody3D
        If body Is Nothing Then Return Nothing

        Dim wrapped As RigidBody3D = Nothing

        If _bodies.TryGetValue(body, wrapped) Then Return wrapped

        wrapped = MjInterop.WrapBody(body)
        _bodies.Add(body, wrapped)

        Return wrapped
    End Function

    ' /********************************************************************************/
    '  静态工厂：与内核 PhysicsWorld3D 的工厂一一对应
    ' /********************************************************************************/

    Public Shared Function Box(width As Double, height As Double, depth As Double,
                               mass As Double,
                               Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.Box(width, height, depth, mass,
                                                      MjInterop.Material(material)))
    End Function

    Public Shared Function Sphere(radius As Double, mass As Double,
                                  Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.Sphere(radius, mass,
                                                         MjInterop.Material(material)))
    End Function

    Public Shared Function Capsule(radius As Double, height As Double, mass As Double,
                                   Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.Capsule(radius, height, mass,
                                                          MjInterop.Material(material)))
    End Function

    Public Shared Function StaticBox(width As Double, height As Double, depth As Double,
                                     position As Vector3,
                                     Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.StaticBox(width, height, depth,
                                                            MjInterop.V(position),
                                                            MjInterop.Material(material)))
    End Function

    Public Shared Function StaticPlane(normal As Vector3, constant As Double,
                                       Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.StaticPlane(MjInterop.V(normal), constant,
                                                              MjInterop.Material(material)))
    End Function

    Public Shared Function Convex(mesh As Collision3D.ConvexMesh3D, mass As Double,
                                  Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.Convex(mesh.EngineMesh, mass,
                                                         MjInterop.Material(material)))
    End Function

    Public Shared Function StaticConvex(mesh As Collision3D.ConvexMesh3D, position As Vector3,
                                        Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.StaticConvex(mesh.EngineMesh,
                                                               MjInterop.V(position),
                                                               MjInterop.Material(material)))
    End Function

    Public Shared Function GroundPlane(height As Double,
                                       Optional material As PhysicsMaterial = Nothing) As RigidBody3D
        Return New RigidBody3D(Eng.PhysicsWorld3D.GroundPlane(height,
                                                              MjInterop.Material(material)))
    End Function

End Class

End Namespace
