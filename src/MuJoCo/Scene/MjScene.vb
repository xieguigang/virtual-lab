Imports num = System.Numerics

Namespace Scene

    ''' <summary>
    ''' MJCF 场景门面：以 <see cref="MjSpec"/> 为场景真源、<see cref="mjData"/> 为状态真源，
    ''' 每次推进调用 <see cref="MjApi.mj_step"/>。对外只暴露按名字寻址的句柄。
    ''' </summary>
    Public Class MjScene

        Private ReadOnly _scene As MjSceneSpec
        Private _spec As MjSpec
        Private _model As mjModel
        Private _data As mjData

        Public Sub New(scene As MjSceneSpec)
            If scene Is Nothing Then Throw New ArgumentNullException(NameOf(scene))

            _scene = scene
            Call Build()
        End Sub

        Public ReadOnly Property SceneSpec As MjSceneSpec
            Get
                Return _scene
            End Get
        End Property

        Public ReadOnly Property Spec As MjSpec
            Get
                Return _spec
            End Get
        End Property

        Public ReadOnly Property Model As mjModel
            Get
                Return _model
            End Get
        End Property

        Public ReadOnly Property Data As mjData
            Get
                Return _data
            End Get
        End Property

        Public ReadOnly Property FixedDt As Double
            Get
                Return _model.opt.timestep
            End Get
        End Property

        Public Property Gravity As num.Vector3
            Get
                Dim g As Double() = _model.opt.gravity

                Return New num.Vector3(CSng(g(0)), CSng(g(1)), CSng(g(2)))
            End Get
            Set(value As num.Vector3)
                _model.opt.gravity = New Double() {value.X, value.Y, value.Z}
                Call MjApi.mj_forward(_model, _data)
            End Set
        End Property

        Public ReadOnly Property Bodies As IReadOnlyList(Of MjBody)
            Get
                Dim result As New List(Of MjBody)()

                For i As Integer = 0 To _model.nbody - 1
                    result.Add(New MjBody(Me, i))
                Next

                Return result
            End Get
        End Property

        Public ReadOnly Property Contacts As IReadOnlyList(Of MjSceneContact)
            Get
                Dim result As New List(Of MjSceneContact)()

                For i As Integer = 0 To Math.Min(_data.ncon, mjData.MaxContacts) - 1
                    result.Add(New MjSceneContact(_model, _data.contact(i)))
                Next

                Return result
            End Get
        End Property

        ''' <summary>把中性描述编译成模型并分配状态。</summary>
        Public Sub Build()
            _spec = MjSceneBuilder.Build(_scene)
            _model = MjApi.mj_compile(_spec)

            If _model Is Nothing Then
                Throw New MjException("the scene could not be compiled")
            End If

            _data = MjApi.mj_makeData(_model)
        End Sub

        ''' <summary>推进一个固定步长（唯一的积分入口）。</summary>
        Public Sub [Step]()
            Call MjApi.mj_step(_model, _data)
        End Sub

        Public Sub Advance(seconds As Double)
            Dim steps As Integer = CInt(Math.Ceiling(seconds / Math.Max(FixedDt, 1.0E-9)))

            For i As Integer = 1 To Math.Max(0, steps)
                Call [Step]()
            Next
        End Sub

        Public Sub Reset()
            Call MjApi.mj_resetData(_model, _data)
            Call MjApi.mj_forward(_model, _data)
        End Sub

        Public Sub ClearVelocities()
            If _model.nv > 0 Then Call Array.Clear(_data.qvel, 0, _model.nv)

            Call MjApi.mj_forward(_model, _data)
        End Sub

        Public Function Body(name As String) As MjBody
            Dim id As Integer = MjApi.mj_name2id(_model, mjtObj.mjOBJ_BODY, name)

            If id < 0 Then Return Nothing

            Return New MjBody(Me, id)
        End Function

        Public Function Servo(name As String) As MjServo
            Dim id As Integer = MjApi.mj_name2id(_model, mjtObj.mjOBJ_ACTUATOR, name)

            If id < 0 Then Return Nothing

            Return New MjServo(Me, id)
        End Function

        ''' <summary>结构变更：加入一个 body 后受控重建，并按名恢复已有状态。</summary>
        Public Function SpawnBody(bodySpec As MjBodySpec) As MjBody
            If bodySpec Is Nothing Then Throw New ArgumentNullException(NameOf(bodySpec))

            Call _scene.Bodies.Add(bodySpec)
            Call Rebuild()

            Return Body(bodySpec.Name)
        End Function

        Public Sub RemoveBody(name As String)
            Dim target As MjBodySpec = _scene.FindBody(name)

            If target Is Nothing Then Return

            Call _scene.Bodies.Remove(target)
            Call Rebuild()
        End Sub

        Public Sub ClearObjects()
            Call _scene.Bodies.Clear()
            Call Rebuild()
        End Sub

        ''' <summary>重建模型时保存/恢复状态，避免热路径之外的状态丢失。</summary>
        Private Sub Rebuild()
            Dim qpos As Double() = CType(_data.qpos.Clone(), Double())
            Dim qvel As Double() = CType(_data.qvel.Clone(), Double())
            Dim ctrl As Double() = CType(_data.ctrl.Clone(), Double())
            Dim time As Double = _data.time

            Call Build()

            If _model.nq > 0 Then Call Array.Copy(qpos, _data.qpos, Math.Min(qpos.Length, _model.nq))
            If _model.nv > 0 Then Call Array.Copy(qvel, _data.qvel, Math.Min(qvel.Length, _model.nv))
            If _model.nu > 0 Then Call Array.Copy(ctrl, _data.ctrl, Math.Min(ctrl.Length, _model.nu))

            _data.time = time

            Call MjApi.mj_clearContactCache(_model, _data)
            Call MjApi.mj_forward(_model, _data)
        End Sub

        Friend Function BodyName(id As Integer) As String
            Return MjApi.mj_id2name(_model, mjtObj.mjOBJ_BODY, id)
        End Function
    End Class

    ''' <summary>按名字寻址的刚体句柄，读写 <c>mjData</c> 的位姿数组。</summary>
    Public Class MjBody

        Private ReadOnly _scene As MjScene
        Private ReadOnly _id As Integer

        Friend Sub New(scene As MjScene, id As Integer)
            _scene = scene
            _id = id
        End Sub

        Public ReadOnly Property Id As Integer
            Get
                Return _id
            End Get
        End Property

        Public ReadOnly Property Name As String
            Get
                Return _scene.BodyName(_id)
            End Get
        End Property

        Public ReadOnly Property Label As String
            Get
                Return Name
            End Get
        End Property

        Public ReadOnly Property Mass As Double
            Get
                Return _scene.Model.body_mass(_id)
            End Get
        End Property

        Public ReadOnly Property Position As num.Vector3
            Get
                Dim x As Double() = _scene.Data.xpos
                Dim o As Integer = _id * 3

                Return New num.Vector3(CSng(x(o)), CSng(x(o + 1)), CSng(x(o + 2)))
            End Get
        End Property

        ''' <summary>世界姿态，System.Numerics 的 (x,y,z,w) 顺序。</summary>
        Public ReadOnly Property Orientation As num.Quaternion
            Get
                Dim q As Double() = _scene.Data.xquat
                Dim o As Integer = _id * 4

                Return New num.Quaternion(CSng(q(o + 1)), CSng(q(o + 2)),
                                          CSng(q(o + 3)), CSng(q(o)))
            End Get
        End Property

        ''' <summary>质心线速度（cvel 的角分量在前，线分量在后）。</summary>
        Public ReadOnly Property Velocity As num.Vector3
            Get
                Dim v As Double() = _scene.Data.cvel
                Dim o As Integer = _id * 6 + 3

                Return New num.Vector3(CSng(v(o)), CSng(v(o + 1)), CSng(v(o + 2)))
            End Get
        End Property

        Public ReadOnly Property AngularVelocity As num.Vector3
            Get
                Dim v As Double() = _scene.Data.cvel
                Dim o As Integer = _id * 6

                Return New num.Vector3(CSng(v(o)), CSng(v(o + 1)), CSng(v(o + 2)))
            End Get
        End Property
    End Class

    ''' <summary>位置/速度执行器句柄，控制量写入 <c>mjData.ctrl</c>。</summary>
    Public Class MjServo

        Private ReadOnly _scene As MjScene
        Private ReadOnly _id As Integer

        Friend Sub New(scene As MjScene, id As Integer)
            _scene = scene
            _id = id
        End Sub

        Public ReadOnly Property Name As String
            Get
                Return MjApi.mj_id2name(_scene.Model, mjtObj.mjOBJ_ACTUATOR, _id)
            End Get
        End Property

        Public Property Target As Double
            Get
                Return _scene.Data.ctrl(_id)
            End Get
            Set(value As Double)
                _scene.Data.ctrl(_id) = value
            End Set
        End Property

        Public ReadOnly Property CurrentPosition As Double
            Get
                Return _scene.Data.actuator_length(_id)
            End Get
        End Property

        Public ReadOnly Property LastForce As Double
            Get
                Return _scene.Data.actuator_force(_id)
            End Get
        End Property

        Public Property Enabled As Boolean
            Get
                Return _scene.Data.ctrl(_id) <> 0.0 OrElse _scene.Data.actuator_force(_id) <> 0.0
            End Get
            Set(value As Boolean)
                If Not value Then
                    _scene.Data.ctrl(_id) = _scene.Data.actuator_length(_id)
                End If
            End Set
        End Property
    End Class

    ''' <summary>命名接触对，把 geom 索引还原成 body 名。</summary>
    Public Class MjSceneContact

        Private ReadOnly _a As String
        Private ReadOnly _b As String
        Private ReadOnly _position As num.Vector3
        Private ReadOnly _distance As Double

        Friend Sub New(model As mjModel, contact As mjContact)
            _a = BodyOf(model, contact.geom1)
            _b = BodyOf(model, contact.geom2)
            _distance = contact.dist

            If contact.pos IsNot Nothing AndAlso contact.pos.Length >= 3 Then
                _position = New num.Vector3(CSng(contact.pos(0)), CSng(contact.pos(1)),
                                            CSng(contact.pos(2)))
            Else
                _position = num.Vector3.Zero
            End If
        End Sub

        Public ReadOnly Property A As String
            Get
                Return _a
            End Get
        End Property

        Public ReadOnly Property B As String
            Get
                Return _b
            End Get
        End Property

        Public ReadOnly Property Position As num.Vector3
            Get
                Return _position
            End Get
        End Property

        Public ReadOnly Property Distance As Double
            Get
                Return _distance
            End Get
        End Property

        Private Shared Function BodyOf(model As mjModel, geom As Integer) As String
            If model.geom_bodyid Is Nothing OrElse geom < 0 OrElse
               geom >= model.geom_bodyid.Length Then Return Nothing

            Return MjApi.mj_id2name(model, mjtObj.mjOBJ_BODY, model.geom_bodyid(geom))
        End Function
    End Class

End Namespace
