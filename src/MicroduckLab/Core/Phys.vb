Imports System.Numerics
Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports Math3D = Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Core

    ''' <summary>
    ''' Conversions between the managed math of the app and the types of the
    ''' physics engine.
    ''' </summary>
    ''' <remarks>
    ''' The engine works in whatever frame the caller sets up: its gravity is a
    ''' plain vector and a floor is any plane equation. The app keeps the MJCF
    ''' frame (z up) everywhere - the assets, the observation and the render rig
    ''' all use it - so the engine is simply told that gravity points down -z.
    ''' This module is therefore only about the two math libraries, not about the
    ''' axis system.
    ''' </remarks>
    Public Module Phys

        Public Function Vec(v As Vector3) As Eng.Vector3
            Return New Eng.Vector3(v.X, v.Y, v.Z)
        End Function

        Public Function Vec(x As Double, y As Double, z As Double) As Eng.Vector3
            Return New Eng.Vector3(x, y, z)
        End Function

        Public Function Vec(v As Eng.Vector3) As Vector3
            Return New Vector3(CSng(v.x), CSng(v.y), CSng(v.z))
        End Function

        Public Function Vertices(points As Vector3()) As Eng.Vector3()
            Dim result As Eng.Vector3() = New Eng.Vector3(points.Length - 1) {}

            For i As Integer = 0 To points.Length - 1
                result(i) = Vec(points(i))
            Next

            Return result
        End Function

        Public Function Quat(q As Quaternion) As Math3D.Quaternion
            Return New Math3D.Quaternion(q.W, q.X, q.Y, q.Z)
        End Function

        Public Function Quat(q As Math3D.Quaternion) As Quaternion
            Return New Quaternion(CSng(q.x), CSng(q.y), CSng(q.z), CSng(q.w))
        End Function

        ''' <summary>
        ''' build the inertia tensor of an MJCF ``fullinertia`` attribute
        ''' (ixx, iyy, izz, ixy, ixz, iyz)
        ''' </summary>
        Public Function Inertia(full As Double()) As Math3D.Matrix3x3
            Dim tensor As New Math3D.Matrix3x3 With {
                .m11 = full(0),
                .m22 = full(1),
                .m33 = full(2),
                .m12 = full(3),
                .m13 = full(4),
                .m23 = full(5),
                .m21 = full(3),
                .m31 = full(4),
                .m32 = full(5)
            }

            Return tensor
        End Function

        ''' <summary>
        ''' add the reflected inertia of the rotor (the MJCF ``armature``) to the
        ''' component of the tensor along the hinge axis
        ''' </summary>
        Public Function WithArmature(tensor As Math3D.Matrix3x3, axis As Vector3, armature As Double) As Math3D.Matrix3x3
            If armature <= 0 Then
                Return tensor
            End If

            ' the reflected inertia of the rotor is the dyadic product of the axis
            ' times the armature, added element by element: the tensor of a six
            ' gram link is 1e-6 and the armature is 1.8e-3, that is three orders of
            ' magnitude larger, and without it the servo torque of 0.06 Nm spins
            ' the link at thousands of radians per second inside one sub step and
            ' the hinge constraint blows up
            Dim a As Vector3 = Vector3.Normalize(axis)
            Dim result As New Math3D.Matrix3x3()

            result.m11 = tensor.m11 + armature * a.X * a.X
            result.m22 = tensor.m22 + armature * a.Y * a.Y
            result.m33 = tensor.m33 + armature * a.Z * a.Z
            result.m12 = tensor.m12 + armature * a.X * a.Y
            result.m21 = tensor.m21 + armature * a.Y * a.X
            result.m13 = tensor.m13 + armature * a.X * a.Z
            result.m31 = tensor.m31 + armature * a.Z * a.X
            result.m23 = tensor.m23 + armature * a.Y * a.Z
            result.m32 = tensor.m32 + armature * a.Z * a.Y

            Return result
        End Function

        ''' <summary>
        ''' a rotation about an arbitrary unit axis
        ''' </summary>
        Public Function AxisAngle(axis As Vector3, angle As Double) As Quaternion
            If Math.Abs(angle) < 1.0E-12 Then
                Return Quaternion.Identity
            End If

            Return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), CSng(angle))
        End Function

        Public Function Clamp(value As Double, low As Double, high As Double) As Double
            If value < low Then Return low
            If value > high Then Return high

            Return value
        End Function

        ''' <summary>
        ''' the world matrix of a rigid pose, row vector convention
        ''' </summary>
        Public Function WorldOf(position As Vector3, rotation As Quaternion) As Matrix4x4
            Return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position)
        End Function
    End Module
End Namespace
