Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Collision3D
Imports Microsoft.VisualBasic.Engine.Physics.ForceFields3D
Imports Microsoft.VisualBasic.Engine.Physics.Joints3D
Imports Microsoft.VisualBasic.Engine.Physics.Math3D
Imports VirtualWorkshop.Model

Namespace Simulation

    ''' <summary>
    ''' 四角弹簧支撑的正方形木板（弹簧跷跷板）。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 木板是一块普通的动态刚体，四角各挂一根 <see cref="Spring3D"/>（固定端在地面）。
    ''' 载荷偏心时四根簧的压缩量不同，弹簧力差产生恢复力矩，木板就倾斜到一个新的平衡角；
    ''' 把这个力矩配平掉（把重块挪到翘起的一侧）木板就会回正。
    ''' </para>
    ''' <para>
    ''' 倾斜时放在板上的重块会同时受重力沿斜面的分量与库仑摩擦：
    ''' <c>tanθ &gt; μ</c> 时就会真的滑下去，不需要额外做任何"滑动模拟"。
    ''' </para>
    ''' <para>
    ''' <b>参数选取</b>：倾斜角大致满足 <c>θ ≈ W·e / (k·d²)</c>（W 为偏心载荷、e 为偏心距、
    ''' d 为弹簧间距）。要看到几度的倾斜，需要比较软的簧 + 比较重的块，
    ''' 这里默认 k=200 N/m、板 0.3 m、重块 1.5 kg 量级。
    ''' </para>
    ''' </remarks>
    Public Class SpringPlatform

        ''' <summary>木板刚体。</summary>
        Public Property Plate As RigidBody3D

        ''' <summary>木板在场景中的 id（同时也是刚体 Label）。</summary>
        Public Property Id As String = "plate-1"

        ''' <summary>木板尺寸（米）。</summary>
        Public Property Size As Vector3 = New Vector3(0.30, 0.30, 0.03)

        ''' <summary>弹簧自然长度（米）。</summary>
        Public Property RestLength As Double = 0.16

        ''' <summary>刚度（N/m）。</summary>
        Public Property Stiffness As Double = 200.0

        ''' <summary>阻尼（N·s/m）。</summary>
        Public Property Damping As Double = 30.0

        ''' <summary>
        ''' 板下方的静态挡块（行程限位）。
        ''' </summary>
        ''' <remarks>
        ''' 弹簧是显式力，重块砸下来时板可能被一次性压穿行程、直接贴到地面，
        ''' 之后很难再弹回来。加一个只在下方阻挡的挡块，既能限制最大行程，
        ''' 又不会影响板向上回弹。
        ''' </remarks>
        Public Property Stopper As RigidBody3D

        ''' <summary>木板的渲染网格（板是直接建在世界里的刚体，不在 Objects 列表里，必须自己带网格）。</summary>
        Public Property PlateMesh As LinkMesh

        ''' <summary>行程限位挡块的渲染网格。</summary>
        Public Property StopperMesh As LinkMesh

        ''' <summary>
        ''' 四根角簧（约束式关节，一端世界锚点、一端板底角点）。
        ''' </summary>
        ''' <remarks>
        ''' 早期用 <c>ForceFields3D.Spring3D</c>（显式力），刚度/阻尼稍大就发散、只能靠
        ''' 限幅硬压，参数极其敏感。改成 <see cref="SpringJoint3D"/> 后由速度层的柔度求解，
        ''' 大刚度也稳定，且天然双向（拉伸时也会把板拉回），木板不会"脱离"弹簧。
        ''' </remarks>
        Public ReadOnly Property Springs As New List(Of SpringJoint3D)()

        ''' <summary>弹簧在板上的挂点（板局部坐标），与 <see cref="Springs"/> 一一对应。</summary>
        Public ReadOnly Property LocalPoints As New List(Of Vector3)()

        ''' <summary>弹簧的固定端（世界坐标），与 <see cref="Springs"/> 一一对应。</summary>
        Public ReadOnly Property Anchors As New List(Of Vector3)()

        ' /********************************************************************************/
        '  倾角
        ' /********************************************************************************/

        ''' <summary>木板法向（世界坐标，局部 +Z 旋转到世界）。</summary>
        Public ReadOnly Property Normal As Vector3
            Get
                If Plate Is Nothing Then Return New Vector3(0, 0, 1)

                Return Plate.Orientation.Rotate(New Vector3(0, 0, 1))
            End Get
        End Property

        ''' <summary>
        ''' 倾角（度）：绕世界 X 轴的倾角、绕世界 Y 轴的倾角、以及总倾角。
        ''' </summary>
        ''' <remarks>
        ''' 法向 <c>n</c> 与竖直方向的偏离：<c>tiltX = atan2(−n.y, n.z)</c>、
        ''' <c>tiltY = atan2(n.x, n.z)</c>、<c>tiltMag = acos(n.z)</c>。
        ''' </remarks>
        Public Function Tilt() As Double()
            Dim n As Vector3 = Normal
            Dim tiltX As Double = System.Math.Atan2(-n.y, n.z)
            Dim tiltY As Double = System.Math.Atan2(n.x, n.z)
            Dim tiltMag As Double = System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, n.z)))

            Return New Double() {
                tiltX * 180.0 / System.Math.PI,
                tiltY * 180.0 / System.Math.PI,
                tiltMag * 180.0 / System.Math.PI
            }
        End Function

        ''' <summary>板面中心高度（米）。</summary>
        Public ReadOnly Property Height As Double
            Get
                If Plate Is Nothing Then Return 0.0

                Return Plate.Position.z
            End Get
        End Property

        ' /********************************************************************************/
        '  构建 / 拆除
        ' /********************************************************************************/

        ''' <summary>建一块由四角弹簧支撑的木板。</summary>
        Public Shared Function Create(world As PhysicsWorld3D, id As String,
                                     size As Vector3, center As Vector3,
                                     Optional mass As Double = 0.6,
                                     Optional friction As Double = 0.6,
                                     Optional stiffness As Double = 200.0,
                                     Optional damping As Double = 30.0,
                                     Optional restLength As Double = 0.16,
                                     Optional springCount As Integer = 4) As SpringPlatform

            If world Is Nothing Then Return Nothing
            If size Is Nothing Then size = New Vector3(0.30, 0.30, 0.03)

            Dim width As Double = System.Math.Max(0.05, size.x)
            Dim depth As Double = System.Math.Max(0.05, size.y)
            Dim thickness As Double = System.Math.Max(0.005, size.z)

            Dim plate As RigidBody3D = PhysicsWorld3D.Box(
                width, depth, thickness,
                System.Math.Max(0.05, mass),
                New PhysicsMaterial(friction, 0.0))

            plate.Position = center
            plate.Label = id
            plate.CollisionGroup = BlockFactory.GroupEnvironment
            plate.CollisionMask = BlockFactory.MaskEnvironment
            plate.AngularDamping = 0.35
            plate.LinearDamping = 0.05
            plate.MaxAngularSpeed = 8.0

            world.Add(plate)

            ' 行程限位：板下方一个小的静态挡块（中心 0.02 m 高，顶面 0.03 m）
            Dim stopper As RigidBody3D = PhysicsWorld3D.StaticBox(
                width * 0.45, depth * 0.45, 0.06,
                New Vector3(center.x, center.y, 0.03),
                New PhysicsMaterial(0.9, 0.0))

            stopper.Label = id & "-stopper"
            stopper.CollisionGroup = BlockFactory.GroupEnvironment
            stopper.CollisionMask = BlockFactory.MaskEnvironment
            world.Add(stopper)

            ' 板与挡块都是直接建在世界里的刚体，不在 Objects 列表里，
            ' 所以必须自己带上渲染网格，否则视口里只能看到弹簧、看不到木板
            Dim platform As New SpringPlatform With {
                .Stopper = stopper,
                .Plate = plate,
                .Id = id,
                .Size = New Vector3(width, depth, thickness),
                .Stiffness = stiffness,
                .Damping = damping,
                .PlateMesh = Model.MeshTessellator.ToLinkMesh(
                    ConvexMesh3D.CreateBox(width, depth, thickness), "#C99A5B", id),
                .StopperMesh = Model.MeshTessellator.ToLinkMesh(
                    ConvexMesh3D.CreateBox(width * 0.45, depth * 0.45, 0.06), "#6B7280", id & "-stopper"),
                .RestLength = If(restLength > 0.0, restLength, System.Math.Max(0.02, center.z - thickness * 0.5))
            }

            ' 四角（往里收 15%，避免簧正好压在板边上把板顶翻）
            Dim hx As Double = width * 0.5 * 0.85
            Dim hy As Double = depth * 0.5 * 0.85
            Dim hz As Double = thickness * 0.5

            Dim corners As Vector3() = If(springCount >= 4,
                New Vector3() {
                    New Vector3(-hx, -hy, -hz),
                    New Vector3(hx, -hy, -hz),
                    New Vector3(-hx, hy, -hz),
                    New Vector3(hx, hy, -hz)
                },
                New Vector3() {New Vector3(0, 0, -hz)})

            For i As Integer = 0 To corners.Length - 1
                Dim local As Vector3 = corners(i)
                Dim anchor As New Vector3(center.x + local.x, center.y + local.y, 0.0)

                ' 一端固定在世界上（世界锚点），一端铰接在板底角点。
                ' 轴向用软约束（k/c），横向用 0.5k + 1 mm 死区：
                ' 死区让板倾斜时挂点的几毫米横向错位不产生恢复力矩，
                ' 否则剪切刚度会把负载力矩抵掉、倾斜角被压掉一个数量级。
                Dim spring As New SpringJoint3D(Nothing, plate, anchor, local,
                                               platform.RestLength, stiffness, damping) With {
                    .Label = id & "-spring-" & (i + 1),
                    .MaxForce = 4000.0,
                    .Bidirectional = True,
                    .ShearStiffness = stiffness * 0.5,
                    .ShearDamping = damping,
                    .ShearSlop = 0.001
                }

                Call world.Add(spring)

                platform.Springs.Add(spring)
                platform.LocalPoints.Add(local)
                platform.Anchors.Add(anchor)
            Next

            Return platform
        End Function

        ''' <summary>把木板与弹簧一起从世界里移除。</summary>
        Public Sub Detach(world As PhysicsWorld3D)
            If world Is Nothing Then Return

            For Each spring As SpringJoint3D In Springs
                If world.Constraints.Contains(spring) Then world.Constraints.Remove(spring)
            Next

            If Stopper IsNot Nothing Then world.Remove(Stopper)
            If Plate IsNot Nothing Then world.Remove(Plate)

            Stopper = Nothing

            Call Springs.Clear()
            Call LocalPoints.Clear()
            Call Anchors.Clear()

            Plate = Nothing
        End Sub

        ''' <summary>各弹簧的压缩量（米）。</summary>
        Public Function Compressions() As Double()
            Dim out(Springs.Count - 1) As Double

            For i As Integer = 0 To Springs.Count - 1
                out(i) = Springs(i).Compression
            Next

            Return out
        End Function

        ''' <summary>各弹簧的弹力（N）。</summary>
        Public Function Forces() As Double()
            Dim out(Springs.Count - 1) As Double

            For i As Integer = 0 To Springs.Count - 1
                out(i) = Springs(i).CurrentForce
            Next

            Return out
        End Function

        ''' <summary>一行摘要（日志用）。</summary>
        Public Function Describe() As String
            ' 注意：局部变量不能叫 tilt —— 会和 Tilt() 方法同名（VB 大小写不敏感），
            ' 编译器会把 Tilt() 当成对局部数组的索引
            Dim angles As Double() = Tilt()

            Return $"{Id} 倾角 {angles(2):F2}° (x {angles(0):F2}° / y {angles(1):F2}°)  高度 {Height:F3} m"
        End Function

    End Class

End Namespace
