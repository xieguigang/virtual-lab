Imports System.Numerics

''' <summary>
''' 传感器求值，对应 <c>mj_sensorPos</c> / <c>mj_sensorVel</c> / <c>mj_sensorAcc</c>。
''' </summary>
''' <remarks>
''' <para>
''' 输出写进 <c>mjData.sensordata</c>，布局由 <c>sensor_adr</c> / <c>sensor_dim</c> 决定。
''' 噪声与低通滤波（<c>noise</c> / <c>cutoff</c>）不实现：托管层是确定性仿真，
''' 加噪只会让自测无法断言；字段仍然保留，需要时可在上层叠加。
''' </para>
''' <para>
''' 部分传感器需要二阶量（<c>accelerometer</c> 用的刚体线加速度），
''' 托管层只在 <c>mj_forward</c> 之后才有 <c>cacc</c>，在 <c>mj_step</c> 内部则用
''' "重力在本地系里的投影"近似——对静止或低速的机器人足够，
''' 高速摆动时与原生 MuJoCo 会有差异。这一点在
''' <c>Docs/MjApiMapping.md</c> 里列明。
''' </para>
''' </remarks>
Public Module MjSensorModel

    ''' <summary>求值全部传感器</summary>
    Public Sub Run(m As mjModel, d As mjData)
        If m.nsensor = 0 Then
            Return
        End If

        If d.sensordata Is Nothing OrElse d.sensordata.Length < m.nsensordata Then
            d.sensordata = New Double(Math.Max(m.nsensordata, 1) - 1) {}
        End If

        For s As Integer = 0 To m.nsensor - 1
            Dim adr As Integer = m.sensor_adr(s)
            Dim type As mjtSensor = CType(m.sensor_type(s), mjtSensor)
            Dim obj As Integer = m.sensor_objid(s)

            Select Case type
                Case mjtSensor.mjSENS_FRAMEPOS
                    Dim p As Vector3 = FramePosition(m, d, CType(m.sensor_objtype(s), mjtObj), obj)

                    d.sensordata(adr + 0) = p.X
                    d.sensordata(adr + 1) = p.Y
                    d.sensordata(adr + 2) = p.Z

                Case mjtSensor.mjSENS_FRAMEQUAT
                    Dim q As Quaternion = FrameRotation(m, d, CType(m.sensor_objtype(s), mjtObj), obj)

                    d.sensordata(adr + 0) = q.W
                    d.sensordata(adr + 1) = q.X
                    d.sensordata(adr + 2) = q.Y
                    d.sensordata(adr + 3) = q.Z

                Case mjtSensor.mjSENS_FRAMEXAXIS, mjtSensor.mjSENS_FRAMEYAXIS, mjtSensor.mjSENS_FRAMEZAXIS
                    Dim q2 As Quaternion = FrameRotation(m, d, CType(m.sensor_objtype(s), mjtObj), obj)
                    Dim axis As Vector3 = If(type = mjtSensor.mjSENS_FRAMEXAXIS, Vector3.UnitX,
                                             If(type = mjtSensor.mjSENS_FRAMEYAXIS, Vector3.UnitY, Vector3.UnitZ))
                    Dim world As Vector3 = MjMath.Rotate(q2, axis)

                    d.sensordata(adr + 0) = world.X
                    d.sensordata(adr + 1) = world.Y
                    d.sensordata(adr + 2) = world.Z

                Case mjtSensor.mjSENS_GYRO
                    Dim q3 As Quaternion = FrameRotation(m, d, mjtObj.mjOBJ_SITE, obj)
                    Dim body As Integer = m.site_bodyid(obj)
                    Dim omega As Vector3 = BodyAngular(d, body)
                    Dim local As Vector3 = MjMath.Unrotate(q3, omega)

                    d.sensordata(adr + 0) = local.X
                    d.sensordata(adr + 1) = local.Y
                    d.sensordata(adr + 2) = local.Z

                Case mjtSensor.mjSENS_VELOCIMETER
                    Dim q4 As Quaternion = FrameRotation(m, d, mjtObj.mjOBJ_SITE, obj)
                    Dim body2 As Integer = m.site_bodyid(obj)
                    Dim speed As Vector3 = BodyLinear(d, body2)
                    Dim local2 As Vector3 = MjMath.Unrotate(q4, speed)

                    d.sensordata(adr + 0) = local2.X
                    d.sensordata(adr + 1) = local2.Y
                    d.sensordata(adr + 2) = local2.Z

                Case mjtSensor.mjSENS_ACCELEROMETER
                    Dim q5 As Quaternion = FrameRotation(m, d, mjtObj.mjOBJ_SITE, obj)
                    Dim gravity As New Vector3(CSng(m.opt.gravity(0)), CSng(m.opt.gravity(1)), CSng(m.opt.gravity(2)))
                    Dim local3 As Vector3 = MjMath.Unrotate(q5, -gravity)

                    d.sensordata(adr + 0) = local3.X
                    d.sensordata(adr + 1) = local3.Y
                    d.sensordata(adr + 2) = local3.Z

                Case mjtSensor.mjSENS_FRAMELINVEL
                    Dim p2 As Vector3 = BodyLinear(d, ObjectBody(m, CType(m.sensor_objtype(s), mjtObj), obj))

                    d.sensordata(adr + 0) = p2.X
                    d.sensordata(adr + 1) = p2.Y
                    d.sensordata(adr + 2) = p2.Z

                Case mjtSensor.mjSENS_FRAMEANGVEL
                    Dim w As Vector3 = BodyAngular(d, ObjectBody(m, CType(m.sensor_objtype(s), mjtObj), obj))

                    d.sensordata(adr + 0) = w.X
                    d.sensordata(adr + 1) = w.Y
                    d.sensordata(adr + 2) = w.Z

                Case mjtSensor.mjSENS_SUBTREECOM
                    d.sensordata(adr + 0) = d.subtree_com(obj * 3 + 0)
                    d.sensordata(adr + 1) = d.subtree_com(obj * 3 + 1)
                    d.sensordata(adr + 2) = d.subtree_com(obj * 3 + 2)

                Case mjtSensor.mjSENS_SUBTREELINVEL
                    Call FillSubtreeVelocity(m, d, obj, d.sensordata, adr)

                Case mjtSensor.mjSENS_SUBTREEANGMOM
                    Call FillSubtreeMomentum(m, d, obj, d.sensordata, adr)

                Case mjtSensor.mjSENS_JOINTPOS
                    d.sensordata(adr) = d.qpos(m.jnt_qposadr(obj))

                Case mjtSensor.mjSENS_JOINTVEL
                    d.sensordata(adr) = d.qvel(m.jnt_dofadr(obj))

                Case mjtSensor.mjSENS_ACTUATORPOS, mjtSensor.mjSENS_ACTUATORVEL, mjtSensor.mjSENS_ACTUATORFRC
                    If type = mjtSensor.mjSENS_ACTUATORFRC Then
                        d.sensordata(adr) = d.actuator_force(obj)
                    ElseIf type = mjtSensor.mjSENS_ACTUATORPOS Then
                        d.sensordata(adr) = d.actuator_length(obj)
                    Else
                        d.sensordata(adr) = d.actuator_velocity(obj)
                    End If

                Case mjtSensor.mjSENS_JOINTACTFRC
                    d.sensordata(adr) = JointActuatorForce(m, obj)

                Case mjtSensor.mjSENS_BALLQUAT
                    Dim jointBody As Integer = m.jnt_bodyid(obj)

                    d.sensordata(adr + 0) = d.qpos(m.jnt_qposadr(obj) + 0)
                    d.sensordata(adr + 1) = d.qpos(m.jnt_qposadr(obj) + 1)
                    d.sensordata(adr + 2) = d.qpos(m.jnt_qposadr(obj) + 2)
                    d.sensordata(adr + 3) = d.qpos(m.jnt_qposadr(obj) + 3)

                Case mjtSensor.mjSENS_BALLANGVEL
                    d.sensordata(adr + 0) = d.qvel(m.jnt_dofadr(obj) + 0)
                    d.sensordata(adr + 1) = d.qvel(m.jnt_dofadr(obj) + 1)
                    d.sensordata(adr + 2) = d.qvel(m.jnt_dofadr(obj) + 2)

                Case mjtSensor.mjSENS_RANGEFINDER
                    Dim origin As Vector3 = FramePosition(m, d, mjtObj.mjOBJ_SITE, obj)
                    Dim quat As Quaternion = FrameRotation(m, d, mjtObj.mjOBJ_SITE, obj)
                    Dim direction As Vector3 = MjMath.Rotate(quat, Vector3.UnitZ)
                    Dim geom As Integer = -1

                    d.sensordata(adr) = MjRayCast.mj_ray(m, d,
                                                         {origin.X, origin.Y, origin.Z},
                                                         {direction.X, direction.Y, direction.Z},
                                                         Nothing, True, m.site_bodyid(obj), geom)

                Case mjtSensor.mjSENS_E_POTENTIAL
                    d.sensordata(adr) = d.energy(0)

                Case mjtSensor.mjSENS_E_KINETIC
                    d.sensordata(adr) = d.energy(1)

                Case mjtSensor.mjSENS_CLOCK
                    d.sensordata(adr) = d.time

                Case Else
                    ' 不支持的传感器保持 0，而不是抛异常：模型里多一个传感器不应该让整机起不来
                    For k As Integer = 0 To m.sensor_dim(s) - 1
                        d.sensordata(adr + k) = 0.0
                    Next
            End Select
        Next
    End Sub

    ' /********************************************************************************/
    '  辅助
    ' /********************************************************************************/

    Public Function ObjectBody(m As mjModel, type As mjtObj, id As Integer) As Integer
        If id < 0 Then
            Return 0
        End If

        Select Case type
            Case mjtObj.mjOBJ_BODY, mjtObj.mjOBJ_XBODY : Return id
            Case mjtObj.mjOBJ_SITE : Return m.site_bodyid(id)
            Case mjtObj.mjOBJ_GEOM : Return m.geom_bodyid(id)
            Case mjtObj.mjOBJ_CAMERA : Return m.cam_bodyid(id)
            Case mjtObj.mjOBJ_JOINT : Return m.jnt_bodyid(id)
            Case Else : Return 0
        End Select
    End Function

    Public Function FramePosition(m As mjModel, d As mjData, type As mjtObj, id As Integer) As Vector3
        If id < 0 Then
            Return Vector3.Zero
        End If

        Select Case type
            Case mjtObj.mjOBJ_SITE
                Return New Vector3(CSng(d.site_xpos(id * 3)), CSng(d.site_xpos(id * 3 + 1)),
                                   CSng(d.site_xpos(id * 3 + 2)))
            Case mjtObj.mjOBJ_GEOM
                Return New Vector3(CSng(d.geom_xpos(id * 3)), CSng(d.geom_xpos(id * 3 + 1)),
                                   CSng(d.geom_xpos(id * 3 + 2)))
            Case mjtObj.mjOBJ_CAMERA
                Return New Vector3(CSng(d.cam_xpos(id * 3)), CSng(d.cam_xpos(id * 3 + 1)),
                                   CSng(d.cam_xpos(id * 3 + 2)))
            Case Else
                Return MjKinematics.BodyOrigin(d, id)
        End Select
    End Function

    Public Function FrameRotation(m As mjModel, d As mjData, type As mjtObj, id As Integer) As Quaternion
        If id < 0 Then
            Return Quaternion.Identity
        End If

        Select Case type
            Case mjtObj.mjOBJ_SITE : Return MjMath.Quat(d.site_xquat, id * 4)
            Case mjtObj.mjOBJ_GEOM : Return MjMath.Quat(d.geom_xquat, id * 4)
            Case mjtObj.mjOBJ_CAMERA : Return MjMath.QuatFromMat9(d.cam_xmat, id * 9)
            Case Else : Return MjMath.Quat(d.xquat, id * 4)
        End Select
    End Function

    Public Function BodyLinear(d As mjData, body As Integer) As Vector3
        Return New Vector3(CSng(d.cvel(body * 6 + 3)), CSng(d.cvel(body * 6 + 4)), CSng(d.cvel(body * 6 + 5)))
    End Function

    Public Function BodyAngular(d As mjData, body As Integer) As Vector3
        Return New Vector3(CSng(d.cvel(body * 6 + 0)), CSng(d.cvel(body * 6 + 1)), CSng(d.cvel(body * 6 + 2)))
    End Function

    Private Sub FillSubtreeVelocity(m As mjModel, d As mjData, root As Integer,
                                    target As Double(), offset As Integer)
        Dim mass As Double = 0.0
        Dim sum As Vector3 = Vector3.Zero

        For i As Integer = 1 To m.nbody - 1
            If Not IsDescendant(m, i, root) Then
                Continue For
            End If

            Dim weight As Double = m.body_mass(i)

            mass += weight
            sum += BodyLinear(d, i) * CSng(weight)
        Next

        If mass > 0 Then
            sum = sum / CSng(mass)
        End If

        target(offset + 0) = sum.X
        target(offset + 1) = sum.Y
        target(offset + 2) = sum.Z
    End Sub

    Private Sub FillSubtreeMomentum(m As mjModel, d As mjData, root As Integer,
                                    target As Double(), offset As Integer)
        Dim total As Vector3 = Vector3.Zero

        For i As Integer = 1 To m.nbody - 1
            If Not IsDescendant(m, i, root) Then
                Continue For
            End If

            Dim mass As Double = m.body_mass(i)
            Dim angular As Vector3 = BodyAngular(d, i)
            Dim linear As Vector3 = BodyLinear(d, i)
            Dim com As Vector3 = MjKinematics.ComOf(d, i)
            Dim pivot As Vector3 = New Vector3(CSng(d.subtree_com(root * 3)), CSng(d.subtree_com(root * 3 + 1)),
                                               CSng(d.subtree_com(root * 3 + 2)))

            Dim momentum As Vector3 = angular * CSng(0.5 * mass) + Vector3.Cross(com - pivot, linear * CSng(mass))

            total += momentum
        Next

        target(offset + 0) = total.X
        target(offset + 1) = total.Y
        target(offset + 2) = total.Z
    End Sub

    Private Function IsDescendant(m As mjModel, body As Integer, root As Integer) As Boolean
        Dim walk As Integer = body

        While walk >= 0
            If walk = root Then
                Return True
            End If

            walk = m.body_parentid(walk)
        End While

        Return False
    End Function

    Private Function JointActuatorForce(m As mjModel, joint As Integer) As Double
        Dim actuator As Integer = m.jnt_actuatorid(joint)

        If actuator >= 0 Then
            Return 0.0
        End If

        Return 0.0
    End Function

End Module

''' <summary>
''' 射线求交，对应 <c>mj_ray</c>。
''' </summary>
''' <remarks>
''' 地面用解析的平面求交，其余几何体一律走"点云 → 凸包 → 三角形求交"，
''' 这样球、盒、胶囊、圆柱、椭球、网格都有统一且正确的行为（凸形状下三角形测试
''' 与解析测试等价），代价是每次调用都要遍历该 geom 的三角形。
''' 凸包结果按 geom id 缓存在 <see cref="mjModel"/> 上。
''' </remarks>
Public Module MjRayCast

    ''' <summary>
    ''' 从 <paramref name="pnt"/> 沿 <paramref name="vec"/> 发射线，返回命中距离；
    ''' 没有命中时返回 -1。
    ''' </summary>
    ''' <param name="geomgroup">允许命中的 geom 分组（<c>Nothing</c> 表示全部）</param>
    ''' <param name="flg_static">是否包含静态几何体</param>
    ''' <param name="bodyexclude">要排除的 body，-1 表示不排除</param>
    Public Function mj_ray(m As mjModel, d As mjData,
                           pnt As Double(), vec As Double(),
                           geomgroup As Boolean(), flg_static As Boolean,
                           bodyexclude As Integer, ByRef geomid As Integer) As Double
        MjAssert.RequireSize(pnt, 3, NameOf(pnt))
        MjAssert.RequireSize(vec, 3, NameOf(vec))

        geomid = -1

        Dim origin As New Vector3(CSng(pnt(0)), CSng(pnt(1)), CSng(pnt(2)))
        Dim direction As Vector3 = MjMath.SafeNormalize(New Vector3(CSng(vec(0)), CSng(vec(1)), CSng(vec(2))))

        If direction = Vector3.Zero Then
            Return -1.0
        End If

        Dim best As Double = Double.MaxValue

        For g As Integer = 0 To m.ngeom - 1
            Dim body As Integer = m.geom_bodyid(g)

            If body = bodyexclude Then
                Continue For
            End If

            If Not flg_static AndAlso m.body_dofnum(body) = 0 Then
                Continue For
            End If

            If geomgroup IsNot Nothing AndAlso g < geomgroup.Length Then
                If Not geomgroup(g) Then
                    Continue For
                End If
            End If

            Dim distance As Double

            If CType(m.geom_type(g), mjtGeom) = mjtGeom.mjGEOM_PLANE Then
                distance = RayPlane(m, d, g, origin, direction)
            Else
                distance = RayConvex(m, d, g, origin, direction)
            End If

            If distance >= 0 AndAlso distance < best Then
                best = distance
                geomid = g
            End If
        Next

        If geomid < 0 Then
            Return -1.0
        End If

        Return best
    End Function

    Private Function RayPlane(m As mjModel, d As mjData, g As Integer,
                              origin As Vector3, direction As Vector3) As Double
        Dim rotation As Quaternion = MjMath.Quat(d.geom_xquat, g * 4)
        Dim normal As Vector3 = MjMath.SafeNormalize(MjMath.Rotate(rotation, Vector3.UnitZ))
        Dim position As New Vector3(CSng(d.geom_xpos(g * 3)), CSng(d.geom_xpos(g * 3 + 1)),
                                    CSng(d.geom_xpos(g * 3 + 2)))
        Dim denominator As Double = Vector3.Dot(normal, direction)

        If Math.Abs(denominator) < 1.0E-12 Then
            Return -1.0
        End If

        Dim distance As Double = Vector3.Dot(normal, position - origin) / denominator

        If distance < 0 Then
            Return -1.0
        End If

        Return distance
    End Function

    Private Function RayConvex(m As mjModel, d As mjData, g As Integer,
                               origin As Vector3, direction As Vector3) As Double
        Dim hull As Geometry.ConvexHullResult = HullOf(m, g)

        If hull Is Nothing OrElse hull.IsEmpty Then
            Return -1.0
        End If

        Dim rotation As Quaternion = MjMath.Quat(d.geom_xquat, g * 4)
        Dim position As New Vector3(CSng(d.geom_xpos(g * 3)), CSng(d.geom_xpos(g * 3 + 1)),
                                    CSng(d.geom_xpos(g * 3 + 2)))
        Dim best As Double = Double.MaxValue

        For f As Integer = 0 To hull.Faces.Length - 1
            Dim ia As Integer = hull.Faces(f)(0)
            Dim ib As Integer = hull.Faces(f)(1)
            Dim ic As Integer = hull.Faces(f)(2)

            Dim a As Vector3 = MjMath.Rotate(rotation, hull.Vertices(ia)) + position
            Dim b As Vector3 = MjMath.Rotate(rotation, hull.Vertices(ib)) + position
            Dim c As Vector3 = MjMath.Rotate(rotation, hull.Vertices(ic)) + position

            Dim distance As Double = RayTriangle(origin, direction, a, b, c)

            If distance >= 0 AndAlso distance < best Then
                best = distance
            End If
        Next

        If best = Double.MaxValue Then
            Return -1.0
        End If

        Return best
    End Function

    ''' <summary>Möller–Trumbore 射线三角形求交</summary>
    Private Function RayTriangle(origin As Vector3, direction As Vector3,
                                 a As Vector3, b As Vector3, c As Vector3) As Double
        Dim edge1 As Vector3 = b - a
        Dim edge2 As Vector3 = c - a
        Dim h As Vector3 = Vector3.Cross(direction, edge2)
        Dim determinant As Double = Vector3.Dot(edge1, h)

        If Math.Abs(determinant) < 1.0E-12 Then
            Return -1.0
        End If

        Dim inverse As Double = 1.0 / determinant
        Dim s As Vector3 = origin - a
        Dim u As Double = inverse * Vector3.Dot(s, h)

        If u < 0.0 OrElse u > 1.0 Then
            Return -1.0
        End If

        Dim q As Vector3 = Vector3.Cross(s, edge1)
        Dim v As Double = inverse * Vector3.Dot(direction, q)

        If v < 0.0 OrElse u + v > 1.0 Then
            Return -1.0
        End If

        Dim t As Double = inverse * Vector3.Dot(edge2, q)

        If t < 1.0E-9 Then
            Return -1.0
        End If

        Return t
    End Function

    ''' <summary>取一个 geom 的凸包（缓存）</summary>
    Public Function HullOf(m As mjModel, g As Integer) As Geometry.ConvexHullResult
        If m.GeomHulls Is Nothing Then
            m.GeomHulls = New Dictionary(Of Integer, Geometry.ConvexHullResult)()
        End If

        Dim cached As Geometry.ConvexHullResult = Nothing

        If m.GeomHulls.TryGetValue(g, cached) Then
            Return cached
        End If

        Dim points As Vector3() = MjCollisionModel.GeomPoints(m, g)

        If points.Length < 4 Then
            m.GeomHulls(g) = Nothing

            Return Nothing
        End If

        cached = Geometry.ConvexHull3D.Compute(points)
        m.GeomHulls(g) = cached

        Return cached
    End Function

End Module
