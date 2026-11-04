Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports EngMath = Microsoft.VisualBasic.Engine.Physics.Math3D
Imports EngCollision = Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports EngJoints = Microsoft.VisualBasic.Engine.Physics.Joints3D

Namespace Interop

    ''' <summary>
    ''' MuJoCo 场景层与物理内核之间的唯一转换边界。
    ''' </summary>
    ''' <remarks>
    ''' 本模块是 <c>Friend</c>：内核类型只在本程序集内部出现，调用方
    ''' （例如 VirtualWorkshop）看到的全部是 MuJoCo 自有类型。
    ''' </remarks>
    Friend Module MjInterop

        Public Function V(value As Vector3) As Eng.Vector3
            If value Is Nothing Then Return Eng.Vector3.Zero

            Return New Eng.Vector3(value.x, value.y, value.z)
        End Function

        Public Function V(value As Eng.Vector3) As Vector3
            If value Is Nothing Then Return Nothing

            Return New Vector3(value.x, value.y, value.z)
        End Function

        Public Function VA(src As Vector3()) As Eng.Vector3()
            If src Is Nothing Then Return Nothing
            If src.Length = 0 Then Return New Eng.Vector3() {}

            Dim dst(src.Length - 1) As Eng.Vector3

            For i As Integer = 0 To src.Length - 1
                dst(i) = V(src(i))
            Next

            Return dst
        End Function

        Public Function VA(src As Eng.Vector3()) As Vector3()
            If src Is Nothing Then Return Nothing
            If src.Length = 0 Then Return New Vector3() {}

            Dim dst(src.Length - 1) As Vector3

            For i As Integer = 0 To src.Length - 1
                dst(i) = V(src(i))
            Next

            Return dst
        End Function

        Public Function Q(value As Math3D.Quaternion) As EngMath.Quaternion
            Return New EngMath.Quaternion(value.w, value.x, value.y, value.z)
        End Function

        Public Function Q(value As EngMath.Quaternion) As Math3D.Quaternion
            Return New Math3D.Quaternion(value.w, value.x, value.y, value.z)
        End Function

        Public Function M3(value As Math3D.Matrix3x3) As EngMath.Matrix3x3
            Return New EngMath.Matrix3x3(value.m11, value.m12, value.m13,
                                         value.m21, value.m22, value.m23,
                                         value.m31, value.m32, value.m33)
        End Function

        Public Function M3(value As EngMath.Matrix3x3) As Math3D.Matrix3x3
            Return New Math3D.Matrix3x3(value.m11, value.m12, value.m13,
                                        value.m21, value.m22, value.m23,
                                        value.m31, value.m32, value.m33)
        End Function

        Public Function Material(value As PhysicsMaterial) As Eng.PhysicsMaterial
            If value Is Nothing Then Return Nothing

            Return value.EngineMaterial
        End Function

        Public Function WrapBody(body As Eng.RigidBody3D) As RigidBody3D
            If body Is Nothing Then Return Nothing

            Return New RigidBody3D(body)
        End Function

        Public Function WrapMesh(mesh As EngCollision.ConvexMesh3D) As Collision3D.ConvexMesh3D
            If mesh Is Nothing Then Return Nothing

            Return New Collision3D.ConvexMesh3D(mesh)
        End Function

        Public Function WrapCollider(shape As EngCollision.Collider3D) As Collision3D.Collider3D
            If shape Is Nothing Then Return Nothing

            Dim box As EngCollision.BoxCollider3D = TryCast(shape, EngCollision.BoxCollider3D)
            If box IsNot Nothing Then Return New Collision3D.BoxCollider3D(box)

            Dim sphere As EngCollision.SphereCollider3D = TryCast(shape, EngCollision.SphereCollider3D)
            If sphere IsNot Nothing Then Return New Collision3D.SphereCollider3D(sphere)

            Dim convex As EngCollision.ConvexPolyhedronCollider3D =
                TryCast(shape, EngCollision.ConvexPolyhedronCollider3D)
            If convex IsNot Nothing Then Return New Collision3D.ConvexPolyhedronCollider3D(convex)

            Dim plane As EngCollision.PlaneCollider3D = TryCast(shape, EngCollision.PlaneCollider3D)
            If plane IsNot Nothing Then Return New Collision3D.PlaneCollider3D(plane)

            Dim capsule As EngCollision.CapsuleCollider3D = TryCast(shape, EngCollision.CapsuleCollider3D)
            If capsule IsNot Nothing Then Return New Collision3D.CapsuleCollider3D(capsule)

            Throw New NotSupportedException($"unsupported collider: {shape.GetType().Name}")
        End Function

        ''' <summary>把场景层的约束对象还原成内核约束。</summary>
        Public Function ConstraintOf(value As Joints3D.IConstraint3D) As EngJoints.IConstraint3D
            If value Is Nothing Then Return Nothing

            Dim spring As Joints3D.SpringJoint3D = TryCast(value, Joints3D.SpringJoint3D)
            If spring IsNot Nothing Then Return spring.EngineConstraint

            Dim hinge As Joints3D.RevoluteJoint3D = TryCast(value, Joints3D.RevoluteJoint3D)
            If hinge IsNot Nothing Then Return hinge.EngineConstraint

            Dim slide As Joints3D.PrismaticJoint3D = TryCast(value, Joints3D.PrismaticJoint3D)
            If slide IsNot Nothing Then Return slide.EngineConstraint

            Return Nothing
        End Function

        ''' <summary>把场景层的关节对象还原成内核自由度接口。</summary>
        Public Function DofOf(value As Joints3D.IJointDof3D) As EngJoints.IJointDof3D
            If value Is Nothing Then Return Nothing

            Dim hinge As Joints3D.RevoluteJoint3D = TryCast(value, Joints3D.RevoluteJoint3D)
            If hinge IsNot Nothing Then Return hinge.EngineDof

            Dim slide As Joints3D.PrismaticJoint3D = TryCast(value, Joints3D.PrismaticJoint3D)
            If slide IsNot Nothing Then Return slide.EngineDof

            Return Nothing
        End Function

    End Module

End Namespace
