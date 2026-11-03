Imports System.IO
Imports System.Numerics
Imports MuJoCo

Namespace Core

    ''' <summary>
    ''' 仿真的门面：持有编译好的 <see cref="mjModel"/> 与 <see cref="mjData"/>，
    ''' 把 duck、场地与小球作为一个整体推进。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' 取代原来的 <c>DuckPhysics</c> + <c>JointDriveHook</c> + <c>Arena</c> + <c>BallBody</c>：
    ''' 刚体树、铰链、执行器现在全部来自 MJCF 编译出来的模型，这里只做三件事：
    ''' </para>
    ''' <list type="number">
    ''' <item><description>
    ''' 把策略输出的目标角度写进 <c>ctrl</c>；
    ''' </description></item>
    ''' <item><description>
    ''' 每个控制周期推进 <see cref="SimConstants.Decimation"/> 个物理子步；
    ''' </description></item>
    ''' <item><description>
    ''' 按预先解析好的下标把 <c>qpos/qvel/xpos/xquat</c> 读成渲染与观测需要的量。
    ''' </description></item>
    ''' </list>
    ''' <para>
    ''' 所有下标（关节、执行器、body）在 <see cref="Build"/> 里一次性解析，运行期不做
    ''' 字符串查找。
    ''' </para>
    ''' </remarks>
    Public Class MjWorld : Implements IDisposable

        ''' <summary>渲染与网格信息的来源（kinematics.json + MJCF）</summary>
        Public ReadOnly Property Skeleton As DuckSkeleton
        ''' <summary>场景构建参数</summary>
        Public ReadOnly Property Options As MjSceneOptions
        ''' <summary>duck 的 MJCF 路径</summary>
        Public ReadOnly Property ModelPath As String

        Public Property Model As mjModel = Nothing
        Public Property Data As mjData = Nothing

        Private ReadOnly m_nodeBody As Integer()
        Private ReadOnly m_actuatorIndex As Integer()
        Private ReadOnly m_qposAdr As Integer()
        Private ReadOnly m_dofAdr As Integer()
        Private ReadOnly m_leftAnkleNode As Integer
        Private ReadOnly m_rightAnkleNode As Integer
        Private m_trunkBody As Integer
        Private m_ballBody As Integer
        Private m_freeQposAdr As Integer

        ''' <summary>渲染节点 → MuJoCo body id</summary>
        Public ReadOnly Property NodeBody As Integer()
            Get
                Return m_nodeBody
            End Get
        End Property

        ''' <summary>策略关节序号 → 执行器 id</summary>
        Public ReadOnly Property ActuatorIndex As Integer()
            Get
                Return m_actuatorIndex
            End Get
        End Property

        ''' <summary>策略关节序号 → qpos 地址</summary>
        Public ReadOnly Property QposAdr As Integer()
            Get
                Return m_qposAdr
            End Get
        End Property

        ''' <summary>策略关节序号 → dof 地址</summary>
        Public ReadOnly Property DofAdr As Integer()
            Get
                Return m_dofAdr
            End Get
        End Property

        ''' <summary>trunk 的 body id</summary>
        Public ReadOnly Property TrunkBody As Integer
            Get
                Return m_trunkBody
            End Get
        End Property

        ''' <summary>小球的 body id</summary>
        Public ReadOnly Property BallBody As Integer
            Get
                Return m_ballBody
            End Get
        End Property

        ''' <summary>trunk 的自由关节的 qpos 地址，-1 表示没有</summary>
        Public ReadOnly Property FreeQposAdr As Integer
            Get
                Return m_freeQposAdr
            End Get
        End Property

        ''' <summary>地形矮丘是否在场</summary>
        Public Property ReliefEnabled As Boolean = False
        ''' <summary>按住当前姿态（跌倒恢复的"稳住"阶段）</summary>
        Public Property Frozen As Boolean = False
        ''' <summary>小球是否在场</summary>
        Public Property BallActive As Boolean = False
        ''' <summary>策略给出的绝对目标角度</summary>
        Public Property Targets As Double() = Nothing

        ''' <summary>碰撞代理的个数（诊断用）</summary>
        Public Property ColliderCount As Integer = 0

        Private ReadOnly m_meshDirectory As String

        Public Sub New(skeleton As DuckSkeleton, modelPath As String, meshDirectory As String,
                       options As MjSceneOptions)
            Me.Skeleton = skeleton
            Me.ModelPath = modelPath
            Me.m_meshDirectory = meshDirectory
            Me.Options = options.Clone()

            m_leftAnkleNode = IndexOfNode("ankle_left")
            m_rightAnkleNode = IndexOfNode("ankle_right")

            Dim nodes As New List(Of Integer)()

            For i As Integer = 0 To Skeleton.Nodes.Count - 1
                Call nodes.Add(0)
            Next

            Dim actuators(Skeleton.Driven.Count - 1) As Integer
            Dim qpos(Skeleton.Driven.Count - 1) As Integer
            Dim dof(Skeleton.Driven.Count - 1) As Integer

            m_nodeBody = nodes.ToArray()
            m_actuatorIndex = actuators
            m_qposAdr = qpos
            m_dofAdr = dof

            Call Build()
        End Sub

        ' /********************************************************************************/
        '  构建
        ' /********************************************************************************/

        Private Sub Build()
            If Data IsNot Nothing Then
                Me.Data = Nothing
            End If

            Me.Model = MjSceneBuilder.Build(ModelPath, m_meshDirectory, Options)
            Me.Data = MjApi.mj_makeData(Model)

            ' 渲染节点 → body id
            For i As Integer = 0 To Skeleton.Nodes.Count - 1
                m_nodeBody(i) = MjApi.mj_name2id(Model, mjtObj.mjOBJ_BODY, Skeleton.Nodes(i).Name)
            Next

            ' 策略关节 → 执行器 / qpos / dof：按 SimConstants.JointNames 的名字解析，
            ' 不依赖 MJCF 里 <actuator> 的书写顺序
            For j As Integer = 0 To SimConstants.NumJoints - 1
                Dim actuator As Integer = MjApi.mj_name2id(Model, mjtObj.mjOBJ_ACTUATOR, SimConstants.JointNames(j))

                m_actuatorIndex(j) = actuator

                If actuator < 0 Then
                    Throw New MjException($"the actuator '{SimConstants.JointNames(j)}' is missing from the model")
                End If

                Dim joint As Integer = CInt(Model.actuator_trnid(actuator * 2))

                If joint < 0 Then
                    Throw New MjException($"the actuator '{SimConstants.JointNames(j)}' does not drive a joint")
                End If

                m_qposAdr(j) = Model.jnt_qposadr(joint)
                m_dofAdr(j) = Model.jnt_dofadr(joint)
            Next

            m_trunkBody = MjApi.mj_name2id(Model, mjtObj.mjOBJ_BODY, DuckSkeleton.TrunkName)
            m_ballBody = MjApi.mj_name2id(Model, mjtObj.mjOBJ_BODY, "ball")
            m_freeQposAdr = -1

            For j As Integer = 0 To Model.njnt - 1
                If CType(Model.jnt_type(j), mjtJoint) = mjtJoint.mjJNT_FREE AndAlso
                   Model.jnt_bodyid(j) = m_trunkBody Then
                    m_freeQposAdr = Model.jnt_qposadr(j)
                End If
            Next

            Targets = CType(SimConstants.DefaultPose.Clone(), Double())

            Call CountColliders()
            Call Spawn()
        End Sub

        Private Function IndexOfNode(name As String) As Integer
            For i As Integer = 0 To Skeleton.Nodes.Count - 1
                If String.Equals(Skeleton.Nodes(i).Name, name, StringComparison.Ordinal) Then
                    Return i
                End If
            Next

            Return -1
        End Function

        Private Sub CountColliders()
            Dim count As Integer = 0

            For i As Integer = 1 To Model.nbody - 1
                If Model.body_dofnum(i) > 0 OrElse Model.body_parentid(i) > 0 Then
                    count += 1
                End If
            Next

            Me.ColliderCount = count
        End Sub

        ' /********************************************************************************/
        '  推进
        ' /********************************************************************************/

        ''' <summary>把一个控制周期（<c>Decimation</c> 个物理子步）推进完</summary>
        Public Sub Step()
            Call ApplyControl()

            For k As Integer = 1 To SimConstants.Decimation
                Call MjApi.mj_step(Model, Data)
            Next
        End Sub

        ''' <summary>把目标角度写进 <c>ctrl</c>；<see cref="Frozen"/> 时按住当前姿态</summary>
        Private Sub ApplyControl()
            For j As Integer = 0 To SimConstants.NumJoints - 1
                Dim actuator As Integer = m_actuatorIndex(j)

                If actuator < 0 Then
                    Continue For
                End If

                Data.ctrl(actuator) = If(Frozen, Data.qpos(m_qposAdr(j)), Targets(j))
            Next
        End Sub

        ''' <summary>设置策略的目标角度（绝对角度，弧度）</summary>
        Public Sub SetTargets(angles As Double())
            If angles Is Nothing Then
                Return
            End If

            For j As Integer = 0 To Math.Min(SimConstants.NumJoints, angles.Length) - 1
                Targets(j) = angles(j)
            Next
        End Sub

        ' /********************************************************************************/
        '  读数
        ' /********************************************************************************/

        Public Function JointAngle(index As Integer) As Double
            Return Data.qpos(m_qposAdr(index))
        End Function

        Public Function JointSpeed(index As Integer) As Double
            Return Data.qvel(m_dofAdr(index))
        End Function

        ''' <summary>trunk 帧原点的世界坐标</summary>
        Public ReadOnly Property TrunkPosition As Vector3
            Get
                Return BodyPosition(m_trunkBody)
            End Get
        End Property

        ''' <summary>trunk 帧的世界姿态</summary>
        Public ReadOnly Property TrunkRotation As Quaternion
            Get
                Return BodyRotation(m_trunkBody)
            End Get
        End Property

        ''' <summary>trunk 的世界角速度，rad/s</summary>
        Public ReadOnly Property TrunkAngularVelocity As Vector3
            Get
                Return BodyAngularVelocity(m_trunkBody)
            End Get
        End Property

        ''' <summary>trunk 质心的世界速度，m/s</summary>
        Public Function TrunkVelocity() As Vector3
            Return BodyVelocity(m_trunkBody)
        End Function

        ''' <summary>
        ''' 世界重力在 trunk 帧里的 z 分量：-1 正立、+1 仰面，跌倒检测用它
        ''' </summary>
        Public Function ProjectedGravityZ() As Double
            Dim inverse As Quaternion = Quaternion.Conjugate(TrunkRotation)

            Return Vector3.Transform(New Vector3(0, 0, -1), inverse).Z
        End Function

        ''' <summary>一个渲染节点的世界矩阵（行向量约定）</summary>
        Public Function NodeWorld(index As Integer) As Matrix4x4
            Dim body As Integer = m_nodeBody(index)

            If body < 0 Then
                Return Matrix4x4.Identity
            End If

            Return Matrix4x4.CreateFromQuaternion(BodyRotation(body)) *
                   Matrix4x4.CreateTranslation(BodyPosition(body))
        End Function

        ''' <summary>踝关节的世界位置，用于脚步声的启发式</summary>
        Public Function AnklePosition(leftSide As Boolean) As Vector3
            Dim node As Integer = If(leftSide, m_leftAnkleNode, m_rightAnkleNode)

            If node < 0 Then
                Return Vector3.Zero
            End If

            Return BodyPosition(m_nodeBody(node))
        End Function

        Public Function BodyPosition(body As Integer) As Vector3
            If body < 0 Then
                Return Vector3.Zero
            End If

            Return New Vector3(CSng(Data.xpos(body * 3)), CSng(Data.xpos(body * 3 + 1)),
                               CSng(Data.xpos(body * 3 + 2)))
        End Function

        Public Function BodyRotation(body As Integer) As Quaternion
            If body < 0 Then
                Return Quaternion.Identity
            End If

            Return MjMath.Quat(Data.xquat, body * 4)
        End Function

        Private Function BodyVelocity(body As Integer) As Vector3
            If body < 0 Then
                Return Vector3.Zero
            End If

            Return New Vector3(CSng(Data.cvel(body * 6 + 3)), CSng(Data.cvel(body * 6 + 4)),
                               CSng(Data.cvel(body * 6 + 5)))
        End Function

        Private Function BodyAngularVelocity(body As Integer) As Vector3
            If body < 0 Then
                Return Vector3.Zero
            End If

            Return New Vector3(CSng(Data.cvel(body * 6 + 0)), CSng(Data.cvel(body * 6 + 1)),
                               CSng(Data.cvel(body * 6 + 2)))
        End Function

        ' /********************************************************************************/
        '  复位与生成
        ' /********************************************************************************/

        ''' <summary>
        ''' 把 duck 放回出生点，关节角取 <see cref="SimConstants.DefaultPose"/>，
        ''' 并把脚底压到地面上
        ''' </summary>
        Public Sub Spawn()
            Call MjApi.mj_resetData(Model, Data)

            For j As Integer = 0 To SimConstants.NumJoints - 1
                Data.qpos(m_qposAdr(j)) = SimConstants.DefaultPose(j)
            Next

            If m_freeQposAdr >= 0 Then
                Data.qpos(m_freeQposAdr + 0) = SimConstants.SpawnX
                Data.qpos(m_freeQposAdr + 1) = SimConstants.SpawnY
                Data.qpos(m_freeQposAdr + 2) = SimConstants.SpawnZ

                Call MjMath.WriteQuat(Data.qpos, m_freeQposAdr + 3, Quaternion.Identity)
            End If

            Call MjApi.mj_forward(Model, Data)

            ' 脚底的碰撞代理可能比"标称"高度高几毫米：直接按出生高度落地会
            ' 悬空一截，落地时那一下冲击会把整机弹起来。这里量一次真实的最低点。
            If m_freeQposAdr >= 0 Then
                Dim lowest As Double = LowestCollisionPoint()

                If Not Double.IsInfinity(lowest) Then
                    Data.qpos(m_freeQposAdr + 2) -= lowest - SimConstants.SpawnLift

                    Call MjApi.mj_forward(Model, Data)
                End If
            End If

            Call MjApi.mj_clearContactCache(Model, Data)

            Me.Frozen = False
            Me.Targets = CType(SimConstants.DefaultPose.Clone(), Double())
        End Sub

        ''' <summary>duck 所有碰撞代理里的最低点（世界 z）</summary>
        Private Function LowestCollisionPoint() As Double
            Dim lowest As Double = Double.MaxValue

            For g As Integer = 0 To Model.ngeom - 1
                If Model.geom_bodyid(g) = 0 Then
                    Continue For
                End If

                If Not MjCollisionModel.IsCollidable(Model, g) Then
                    Continue For
                End If

                Dim mesh As Integer = Model.geom_dataid(g)

                If mesh >= 0 Then
                    Dim count As Integer = Model.mesh_vertnum(mesh)
                    Dim adr As Integer = Model.mesh_vertadr(mesh)

                    For v As Integer = 0 To count - 1
                        Dim local As New Vector3(CSng(Model.mesh_vert((adr + v) * 3)),
                                                 CSng(Model.mesh_vert((adr + v) * 3 + 1)),
                                                 CSng(Model.mesh_vert((adr + v) * 3 + 2)))
                        Dim world As Vector3 = New Vector3(CSng(Data.geom_xpos(g * 3)),
                                                           CSng(Data.geom_xpos(g * 3 + 1)),
                                                           CSng(Data.geom_xpos(g * 3 + 2))) +
                                               MjMath.Rotate(MjMath.Quat(Data.geom_xquat, g * 4), local)

                        lowest = Math.Min(lowest, world.Z)
                    Next
                Else
                    lowest = Math.Min(lowest, Data.geom_xpos(g * 3 + 2) - Model.geom_rbound(g))
                End If
            Next

            Return lowest
        End Function

        ' /********************************************************************************/
        '  小球
        ' /********************************************************************************/

        Public ReadOnly Property BallPosition As Vector3
            Get
                Return BodyPosition(m_ballBody)
            End Get
        End Property

        Public ReadOnly Property BallVelocity As Vector3
            Get
                Return BodyVelocity(m_ballBody)
            End Get
        End Property

        Public ReadOnly Property BallSpeed As Double
            Get
                Return BallVelocity.Length()
            End Get
        End Property

        Public ReadOnly Property BallWorld As Matrix4x4
            Get
                If m_ballBody < 0 Then
                    Return Matrix4x4.Identity
                End If

                Return Matrix4x4.CreateFromQuaternion(BodyRotation(m_ballBody)) *
                       Matrix4x4.CreateTranslation(BodyPosition(m_ballBody))
            End Get
        End Property

        ''' <summary>把小球收回停车点（场地外，谁也碰不到）</summary>
        Public Sub ParkBall()
            Me.BallActive = False

            If m_ballBody < 0 Then
                Return
            End If

            Call PlaceBall(CSng(SimConstants.BallParkX), CSng(SimConstants.BallParkY),
                           CSng(SimConstants.BallParkZ))
        End Sub

        ''' <summary>在 duck 面朝的一侧丢下小球，带一点随机方位</summary>
        Public Sub SpawnBall(near As Vector3, heading As Double, random As Random)
            If m_ballBody < 0 Then
                Return
            End If

            Dim distance As Double = 0.55
            Dim angle As Double = heading + (random.NextDouble() - 0.5) * 0.7
            Dim x As Double = near.X + distance * Math.Cos(angle)
            Dim y As Double = near.Y + distance * Math.Sin(angle)
            Dim half As Double = SimConstants.ArenaHalf - 0.12

            x = Math.Max(-half, Math.Min(half, x))
            y = Math.Max(-half, Math.Min(half, y))

            Me.BallActive = True

            Call PlaceBall(CSng(x), CSng(y), CSng(SimConstants.SpawnZ))
        End Sub

        Private Sub PlaceBall(x As Single, y As Single, z As Single)
            Dim joint As Integer = -1

            For j As Integer = 0 To Model.njnt - 1
                If Model.jnt_bodyid(j) = m_ballBody AndAlso
                   CType(Model.jnt_type(j), mjtJoint) = mjtJoint.mjJNT_FREE Then
                    joint = j
                End If
            Next

            If joint < 0 Then
                Return
            End If

            Dim adr As Integer = Model.jnt_qposadr(joint)

            Data.qpos(adr + 0) = x
            Data.qpos(adr + 1) = y
            Data.qpos(adr + 2) = z

            Call MjMath.WriteQuat(Data.qpos, adr + 3, Quaternion.Identity)

            Dim dof As Integer = Model.jnt_dofadr(joint)

            For k As Integer = 0 To 5
                Data.qvel(dof + k) = 0.0
            Next

            Call MjApi.mj_forward(Model, Data)
            Call MjApi.mj_clearContactCache(Model, Data)
        End Sub

        ' /********************************************************************************/
        '  地形
        ' /********************************************************************************/

        ''' <summary>
        ''' 开关地形矮丘。矮丘是模型的一部分，所以这里重建整个模型与状态，
        ''' 然后把 duck 放回出生点。
        ''' </summary>
        Public Sub SetRelief(enabled As Boolean)
            If enabled = ReliefEnabled Then
                Return
            End If

            Me.ReliefEnabled = enabled
            Options.ReliefEnabled = enabled

            Call Build()

            If enabled Then
                ' 出生点不在任何矮丘上：矮丘的圆心都在场地中部，
                ' 出生点在 (SpawnX, 0) 附近，半径 0.55 的矮丘够不到
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Me.Data = Nothing
            Me.Model = Nothing
        End Sub

    End Class

End Namespace
