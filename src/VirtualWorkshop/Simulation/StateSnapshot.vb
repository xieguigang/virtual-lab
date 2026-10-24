Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Simulation

    ''' <summary>单个关节的状态（供状态面板与协议查询）。</summary>
    Public Class JointState

        Public Property Name As String
        Public Property Angle As Double
        Public Property Speed As Double
        Public Property Target As Double
        Public Property Torque As Double
        Public Property Lower As Double
        Public Property Upper As Double

    End Class

    ''' <summary>夹爪状态。</summary>
    Public Class GripperState

        ''' <summary>两指之间的开口宽度（米）。</summary>
        Public Property Opening As Double
        ''' <summary>当前夹持力（N，正值为夹紧）。</summary>
        Public Property Force As Double
        ''' <summary>是否处于闭合（夹紧）指令状态。</summary>
        Public Property Closing As Boolean
        ''' <summary>手指是否接触到物体。</summary>
        Public Property Contact As Boolean
        ''' <summary>正被夹住的物体 id（没有则为空）。</summary>
        Public Property HoldingId As String

    End Class

    ''' <summary>场景中的一个积木物体。</summary>
    Public Class ObjectState

        Public Property Id As String
        Public Property Kind As String
        ''' <summary>[x, y, z]</summary>
        Public Property Position As Double()
        ''' <summary>[qx, qy, qz, qw]</summary>
        Public Property Rotation As Double()
        ''' <summary>[vx, vy, vz]</summary>
        Public Property Velocity As Double()
        ''' <summary>包围盒尺寸 [sx, sy, sz]</summary>
        Public Property Size As Double()
        Public Property Mass As Double
        Public Property Color As String

    End Class

    ''' <summary>
    ''' 一份不可变的仿真状态快照：仿真线程发布，网络线程与 UI 线程读取。
    ''' </summary>
    ''' <remarks>
    ''' 所有向量都用 <see cref="Double"/> 数组表示，避免把物理层的
    ''' <see cref="Vector3"/>（Public 字段）暴露给 System.Text.Json（它默认只序列化属性）。
    ''' TCP 的 <c>state</c> 事件直接序列化这个对象即可。
    ''' </remarks>
    Public Class StateSnapshot

        Public Property Time As Double
        Public Property StepIndex As Long

        ''' <summary>6 个关节角（弧度）。</summary>
        Public Property Q As Double()
        ''' <summary>6 个关节角速度（rad/s）。</summary>
        Public Property Qd As Double()

        ''' <summary>TCP 位置与 rpy 姿态（度）：[x, y, z, rx, ry, rz]。</summary>
        Public Property Tcp As Double()
        ''' <summary>TCP 四元数：[qx, qy, qz, qw]。</summary>
        Public Property TcpQuaternion As Double()

        Public Property Joints As List(Of JointState)
        Public Property Gripper As GripperState

        ''' <summary>当前接触对，形如 "link2|block-3"。</summary>
        Public Property Collisions As List(Of String)

        Public Property Objects As List(Of ObjectState)

        Public Property Estop As Boolean
        Public Property Enabled As Boolean
        ''' <summary>"physics" 或 "kinematic"。</summary>
        Public Property SimMode As String

        ''' <summary>场景中所有物体的最大线速度（用于判断"是否静止"）。</summary>
        Public Property MaxObjectSpeed As Double

        ''' <summary>四元数 → rpy（度），ZYX 顺序，与 URDF 的 rpy 约定一致。</summary>
        Public Shared Function ToRpy(q As Quaternion) As Double()
            Dim sinrCosp As Double = 2.0 * (q.w * q.x + q.y * q.z)
            Dim cosrCosp As Double = 1.0 - 2.0 * (q.x * q.x + q.y * q.y)
            Dim roll As Double = System.Math.Atan2(sinrCosp, cosrCosp)

            Dim sinp As Double = 2.0 * (q.w * q.y - q.z * q.x)
            If sinp > 1.0 Then sinp = 1.0
            If sinp < -1.0 Then sinp = -1.0
            Dim pitch As Double = System.Math.Asin(sinp)

            Dim sinyCosp As Double = 2.0 * (q.w * q.z + q.x * q.y)
            Dim cosyCosp As Double = 1.0 - 2.0 * (q.y * q.y + q.z * q.z)
            Dim yaw As Double = System.Math.Atan2(sinyCosp, cosyCosp)

            Const rad2deg As Double = 180.0 / System.Math.PI

            Return {roll * rad2deg, pitch * rad2deg, yaw * rad2deg}
        End Function

        Public Shared Function ToArray(v As Vector3) As Double()
            If v Is Nothing Then Return {0, 0, 0}

            Return {v.x, v.y, v.z}
        End Function

        Public Shared Function ToArray(q As Quaternion) As Double()
            Return {q.x, q.y, q.z, q.w}
        End Function

    End Class

End Namespace
