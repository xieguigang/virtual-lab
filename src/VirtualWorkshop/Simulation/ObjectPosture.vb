Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Simulation

    ''' <summary>物体相对世界竖直方向的姿态类别。</summary>
    Public Enum Posture
        ''' <summary>正立（倾角小于 20°）。</summary>
        Upright = 0
        ''' <summary>歪斜（20°~55°），通常是被推了一下还没倒。</summary>
        Tilted = 1
        ''' <summary>倾倒（大于 55°），躺倒或翻倒。</summary>
        FallOver = 2
    End Enum

    ''' <summary>
    ''' 姿态与位移判定：把刚体位姿翻译成"正立 / 歪斜 / 倾倒 + 是否已掉下桌面"。
    ''' </summary>
    ''' <remarks>
    ''' 三角瓶这类细高物体，"被推移"和"被推倒"是两种完全不同的结果：
    ''' 水平位移能区分前者，倾角能区分后者；两者都不需要在引擎里做额外解算，
    ''' 从刚体的姿态四元数与出生位置就能算出来。
    ''' </remarks>
    Public Module ObjectPosture

        ''' <summary>低于这个倾角算正立（度）。</summary>
        Public Const UprightMaxDeg As Double = 20.0

        ''' <summary>高于这个倾角算倾倒（度）。</summary>
        Public Const FallOverMinDeg As Double = 55.0

        ''' <summary>质心比出生时下降超过这个距离，就算已经掉下桌面（米）。</summary>
        Public Const FallDistance As Double = 0.08

        ''' <summary>物体的局部 +Z 轴与世界上方向之间的夹角（度）。</summary>
        Public Function TiltDegrees(body As RigidBody3D) As Double
            If body Is Nothing Then Return 0.0

            Dim up As Vector3 = body.Orientation.Rotate(New Vector3(0, 0, 1))
            Dim cosine As Double = System.Math.Max(-1.0, System.Math.Min(1.0, up.z))

            Return System.Math.Acos(cosine) * 180.0 / System.Math.PI
        End Function

        Public Function PostureOf(item As BlockInstance) As Posture
            If item Is Nothing OrElse item.Body Is Nothing Then Return Posture.Upright

            Dim tilt As Double = TiltDegrees(item.Body)

            If tilt < UprightMaxDeg Then Return Posture.Upright
            If tilt < FallOverMinDeg Then Return Posture.Tilted

            Return Posture.FallOver
        End Function

        ''' <summary>相对出生位置的水平位移（米）：用来区分"被推移"与"原地倒下"。</summary>
        Public Function Displacement(item As BlockInstance) As Double
            If item Is Nothing OrElse item.Body Is Nothing Then Return 0.0

            Dim delta As Vector3 = item.Body.Position - item.SpawnPosition

            Return System.Math.Sqrt(delta.x * delta.x + delta.y * delta.y)
        End Function

        ''' <summary>是否已经离开桌面（质心显著低于出生高度）。</summary>
        Public Function HasFallen(item As BlockInstance) As Boolean
            If item Is Nothing OrElse item.Body Is Nothing Then Return False

            Return (item.SpawnPosition.z - item.Body.Position.z) > FallDistance
        End Function

        Public Function PostureName(posture As Posture) As String
            Select Case posture
                Case Posture.Upright : Return "upright"
                Case Posture.Tilted : Return "tilted"
                Case Else : Return "fall_over"
            End Select
        End Function

    End Module

End Namespace
