Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Sensing

    ''' <summary>
    ''' 相机外参：世界位置 + 朝向，以及由朝向导出的前向/上向/右向。
    ''' </summary>
    ''' <remarks>
    ''' 相机直接沿用 link6 刚体的旋转（不额外加安装角度）：
    ''' <c>Forward = R·(1,0,0)</c>、<c>Up = R·(0,0,1)</c>、<c>Right = R·(0,-1,0)</c>。
    ''' 这样零位时朝世界 +X 平视、末端朝下时俯视桌面，符合"装在夹爪里"的直觉。
    ''' </remarks>
    Public Class CameraPose

        Public Property Position As Vector3
        Public Property Orientation As Quaternion

        Public ReadOnly Property Forward As Vector3
            Get
                Return Orientation.Rotate(New Vector3(1, 0, 0))
            End Get
        End Property

        Public ReadOnly Property Up As Vector3
            Get
                Return Orientation.Rotate(New Vector3(0, 0, 1))
            End Get
        End Property

        Public ReadOnly Property Right As Vector3
            Get
                Return Orientation.Rotate(New Vector3(0, -1, 0))
            End Get
        End Property

        ''' <summary>
        ''' 由 link6 刚体位姿与安装偏移构造。
        ''' </summary>
        Public Shared Function FromLink(linkPosition As Vector3, linkOrientation As Quaternion,
                                        offset As Vector3) As CameraPose

            Dim rotation As Quaternion = linkOrientation.Normalize()

            Return New CameraPose With {
                .Position = linkPosition + rotation.Rotate(offset),
                .Orientation = rotation
            }
        End Function

        ''' <summary>按 ZYX 顺序（URDF 的 rpy 约定）返回角度制欧拉角。</summary>
        Public Function RpyDegrees() As Double()
            Dim forward As Vector3 = Me.Forward
            Dim up As Vector3 = Me.Up
            Dim leftOrUp As Vector3 = Orientation.Rotate(New Vector3(0, 1, 0))

            Dim pitch As Double = System.Math.Asin(System.Math.Max(-1.0, System.Math.Min(1.0, -forward.z)))
            Dim yaw As Double = System.Math.Atan2(forward.y, forward.x)
            Dim roll As Double = System.Math.Atan2(leftOrUp.z, up.z)

            Return {
                roll * 180.0 / System.Math.PI,
                pitch * 180.0 / System.Math.PI,
                yaw * 180.0 / System.Math.PI
            }
        End Function

        Public Function Clone() As CameraPose
            Return New CameraPose With {
                .Position = New Vector3(Position.x, Position.y, Position.z),
                .Orientation = Orientation
            }
        End Function

        Public Overrides Function ToString() As String
            Return $"({Position.x:F3}, {Position.y:F3}, {Position.z:F3})"

        End Function

    End Class

End Namespace
