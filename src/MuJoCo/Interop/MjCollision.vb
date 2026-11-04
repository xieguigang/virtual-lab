Imports EngCollision = Microsoft.VisualBasic.Engine.Physics.Collision3D

Namespace Interop.Collision3D

    Public Enum ShapeKind3D
        Sphere = 0
        Capsule = 1
        Box = 2
        Plane = 3
        Convex = 4
    End Enum

    ''' <summary>碰撞形状的公开基类，内核形状只保存在 Friend 字段里。</summary>
    Public MustInherit Class Collider3D

        Friend ReadOnly EngineShape As EngCollision.Collider3D

        Friend Sub New(inner As EngCollision.Collider3D)
            Me.EngineShape = inner
        End Sub

        Public ReadOnly Property Kind As ShapeKind3D
            Get
                Return CType(EngineShape.Kind, ShapeKind3D)
            End Get
        End Property

        Public Function GetAABB(position As Vector3, rotation As Math3D.Quaternion) As AABB3D
            Dim box As EngCollision.AABB3D =
                EngineShape.GetAABB(MjInterop.V(position), MjInterop.Q(rotation))

            Return New AABB3D(MjInterop.V(box.min), MjInterop.V(box.max))
        End Function

        Public Function Support(dir As Vector3) As Vector3
            Return MjInterop.V(EngineShape.Support(MjInterop.V(dir)))
        End Function

        Public Function ComputeInertia(mass As Double) As Math3D.Matrix3x3
            Return MjInterop.M3(EngineShape.ComputeInertia(mass))
        End Function

    End Class

    Public Class SphereCollider3D
        Inherits Collider3D

        Public Sub New(radius As Double)
            MyBase.New(New EngCollision.SphereCollider3D(radius))
        End Sub

        Friend Sub New(inner As EngCollision.SphereCollider3D)
            MyBase.New(inner)
        End Sub

        Public Property Radius As Double
            Get
                Return DirectCast(EngineShape, EngCollision.SphereCollider3D).Radius
            End Get
            Set(value As Double)
                DirectCast(EngineShape, EngCollision.SphereCollider3D).Radius = value
            End Set
        End Property
    End Class

    Public Class BoxCollider3D
        Inherits Collider3D

        Public Sub New(width As Double, height As Double, depth As Double)
            MyBase.New(New EngCollision.BoxCollider3D(width, height, depth))
        End Sub

        Friend Sub New(inner As EngCollision.BoxCollider3D)
            MyBase.New(inner)
        End Sub

        Public ReadOnly Property HalfExtents As Vector3
            Get
                Return MjInterop.V(DirectCast(EngineShape, EngCollision.BoxCollider3D).HalfExtents)
            End Get
        End Property

        Public ReadOnly Property Size As Vector3
            Get
                Return MjInterop.V(DirectCast(EngineShape, EngCollision.BoxCollider3D).Size)
            End Get
        End Property

        Public ReadOnly Property AsConvex As ConvexMesh3D
            Get
                Return MjInterop.WrapMesh(DirectCast(EngineShape, EngCollision.BoxCollider3D).AsConvex)
            End Get
        End Property
    End Class

    Public Class CapsuleCollider3D
        Inherits Collider3D

        Public Sub New(radius As Double, height As Double)
            MyBase.New(New EngCollision.CapsuleCollider3D(radius, height))
        End Sub

        Friend Sub New(inner As EngCollision.CapsuleCollider3D)
            MyBase.New(inner)
        End Sub
    End Class

    Public Class PlaneCollider3D
        Inherits Collider3D

        Public Sub New(normal As Vector3, constant As Double)
            MyBase.New(New EngCollision.PlaneCollider3D(MjInterop.V(normal), constant))
        End Sub

        Friend Sub New(inner As EngCollision.PlaneCollider3D)
            MyBase.New(inner)
        End Sub

        Public Property Normal As Vector3
            Get
                Return MjInterop.V(DirectCast(EngineShape, EngCollision.PlaneCollider3D).Normal)
            End Get
            Set(value As Vector3)
                DirectCast(EngineShape, EngCollision.PlaneCollider3D).Normal = MjInterop.V(value)
            End Set
        End Property
    End Class

    Public Class ConvexPolyhedronCollider3D
        Inherits Collider3D

        Public Sub New(mesh As ConvexMesh3D)
            MyBase.New(New EngCollision.ConvexPolyhedronCollider3D(
                       If(mesh Is Nothing, Nothing, mesh.EngineMesh)))
        End Sub

        Friend Sub New(inner As EngCollision.ConvexPolyhedronCollider3D)
            MyBase.New(inner)
        End Sub

        Public ReadOnly Property Mesh As ConvexMesh3D
            Get
                Return MjInterop.WrapMesh(DirectCast(EngineShape, EngCollision.ConvexPolyhedronCollider3D).Mesh)
            End Get
        End Property
    End Class

    ''' <summary>轴对齐包围盒。</summary>
    Public Structure AABB3D

        Public min As Vector3
        Public max As Vector3

        Public Sub New(min As Vector3, max As Vector3)
            Me.min = min
            Me.max = max
        End Sub

        Public ReadOnly Property Center As Vector3
            Get
                Return New Vector3((min.x + max.x) * 0.5,
                                   (min.y + max.y) * 0.5,
                                   (min.z + max.z) * 0.5)
            End Get
        End Property

        Public ReadOnly Property Extents As Vector3
            Get
                Return New Vector3((max.x - min.x) * 0.5,
                                   (max.y - min.y) * 0.5,
                                   (max.z - min.z) * 0.5)
            End Get
        End Property

        Public Shared Function FromPoint(p As Vector3) As AABB3D
            Return New AABB3D(p, p)
        End Function

    End Structure

    ''' <summary>一个接触点。</summary>
    Public Class ContactPoint3D

        Friend ReadOnly EnginePoint As EngCollision.ContactPoint3D

        Public Sub New(position As Vector3, penetration As Double)
            Me.EnginePoint = New EngCollision.ContactPoint3D(MjInterop.V(position), penetration)
        End Sub

        Friend Sub New(inner As EngCollision.ContactPoint3D)
            Me.EnginePoint = inner
        End Sub

        Public Property Position As Vector3
            Get
                Return MjInterop.V(EnginePoint.Position)
            End Get
            Set(value As Vector3)
                EnginePoint.Position = MjInterop.V(value)
            End Set
        End Property

        Public Property Penetration As Double
            Get
                Return EnginePoint.Penetration
            End Get
            Set(value As Double)
                EnginePoint.Penetration = value
            End Set
        End Property
    End Class

    ''' <summary>一对刚体之间的接触流形。</summary>
    Public Class Manifold3D

        Friend ReadOnly EngineManifold As EngCollision.Manifold3D

        Friend Sub New(inner As EngCollision.Manifold3D)
            Me.EngineManifold = inner
        End Sub

        Public Property A As RigidBody3D
            Get
                Return MjInterop.WrapBody(EngineManifold.A)
            End Get
            Set(value As RigidBody3D)
                EngineManifold.A = If(value Is Nothing, Nothing, value.EngineBody)
            End Set
        End Property

        Public Property B As RigidBody3D
            Get
                Return MjInterop.WrapBody(EngineManifold.B)
            End Get
            Set(value As RigidBody3D)
                EngineManifold.B = If(value Is Nothing, Nothing, value.EngineBody)
            End Set
        End Property

        Public Property Normal As Vector3
            Get
                Return MjInterop.V(EngineManifold.Normal)
            End Get
            Set(value As Vector3)
                EngineManifold.Normal = MjInterop.V(value)
            End Set
        End Property

        Public ReadOnly Property Contacts As List(Of ContactPoint3D)
            Get
                Dim result As New List(Of ContactPoint3D)()

                If EngineManifold.Contacts Is Nothing Then Return result

                For Each point As EngCollision.ContactPoint3D In EngineManifold.Contacts
                    result.Add(New ContactPoint3D(point))
                Next

                Return result
            End Get
        End Property

        Public ReadOnly Property HasContacts As Boolean
            Get
                Return EngineManifold.HasContacts
            End Get
        End Property

        Public ReadOnly Property Penetration As Double
            Get
                Return EngineManifold.Penetration
            End Get
        End Property
    End Class

    ''' <summary>凸网格：顶点、三角面与质心偏移。</summary>
    Public Class ConvexMesh3D

        Friend ReadOnly EngineMesh As EngCollision.ConvexMesh3D

        Public Sub New()
            Me.EngineMesh = New EngCollision.ConvexMesh3D()
        End Sub

        Public Sub New(vertices As Vector3(), faces As Integer()())
            Me.EngineMesh = New EngCollision.ConvexMesh3D(MjInterop.VA(vertices), faces)
        End Sub

        Friend Sub New(inner As EngCollision.ConvexMesh3D)
            Me.EngineMesh = inner
        End Sub

        Public Property Vertices As Vector3()
            Get
                Return MjInterop.VA(EngineMesh.Vertices)
            End Get
            Set(value As Vector3())
                EngineMesh.Vertices = MjInterop.VA(value)
            End Set
        End Property

        Public Property Faces As Integer()()
            Get
                Return EngineMesh.Faces
            End Get
            Set(value As Integer()())
                EngineMesh.Faces = value
            End Set
        End Property

        Public Property PivotOffset As Vector3
            Get
                Return MjInterop.V(EngineMesh.PivotOffset)
            End Get
            Set(value As Vector3)
                EngineMesh.PivotOffset = MjInterop.V(value)
            End Set
        End Property

        Public ReadOnly Property BoundRadius As Double
            Get
                Return EngineMesh.BoundRadius
            End Get
        End Property

        Public Function Volume() As Double
            Return EngineMesh.Volume()
        End Function

        Public Function VolumeCentroid() As Vector3
            Return MjInterop.V(EngineMesh.VolumeCentroid())
        End Function

        Public Function Centered() As ConvexMesh3D
            Return MjInterop.WrapMesh(EngineMesh.Centered())
        End Function

        Public Function WorldVertices(position As Vector3, rotation As Math3D.Quaternion) As Vector3()
            Return MjInterop.VA(EngineMesh.WorldVertices(MjInterop.V(position), MjInterop.Q(rotation)))
        End Function

        Public Shared Function CreateBox(width As Double, height As Double, depth As Double) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexMesh3D.CreateBox(width, height, depth))
        End Function

        Public Shared Function CreateSphereMesh(radius As Double,
                                                Optional segmentU As Integer = 12,
                                                Optional segmentV As Integer = 8) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexMesh3D.CreateSphereMesh(radius, segmentU, segmentV))
        End Function

        Public Shared Function CreateCylinder(radius As Double, height As Double,
                                              Optional segment As Integer = 12) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexMesh3D.CreateCylinder(radius, height, segment))
        End Function

        Public Shared Function CreateCone(radius As Double, height As Double,
                                          Optional segment As Integer = 12) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexMesh3D.CreateCone(radius, height, segment))
        End Function

        Public Shared Function CreateWedge(width As Double, height As Double, depth As Double) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexMesh3D.CreateWedge(width, height, depth))
        End Function

        Public Shared Function CreateTetrahedron(edge As Double) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexMesh3D.CreateTetrahedron(edge))
        End Function
    End Class

    ''' <summary>扩展凸体图元。</summary>
    Public Module ConvexPrimitives3D

        Public Function CreateFrustum(bottomRadius As Double, topRadius As Double, height As Double,
                                      Optional segment As Integer = 16) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexPrimitives3D.CreateFrustum(bottomRadius, topRadius, height, segment))
        End Function

        Public Function CreateHemisphere(radius As Double, Optional segment As Integer = 16,
                                         Optional rings As Integer = 6) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexPrimitives3D.CreateHemisphere(radius, segment, rings))
        End Function

        Public Function CreatePrism(sides As Integer, radius As Double, height As Double) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexPrimitives3D.CreatePrism(sides, radius, height))
        End Function

        Public Function CreatePyramid(sides As Integer, radius As Double, height As Double) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexPrimitives3D.CreatePyramid(sides, radius, height))
        End Function

        Public Function CreateDisc(radius As Double, thickness As Double,
                                   Optional segment As Integer = 20) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexPrimitives3D.CreateDisc(radius, thickness, segment))
        End Function

        Public Function CreatePlank(width As Double, thickness As Double, depth As Double) As ConvexMesh3D
            Return MjInterop.WrapMesh(EngCollision.ConvexPrimitives3D.CreatePlank(width, thickness, depth))
        End Function
    End Module

End Namespace
