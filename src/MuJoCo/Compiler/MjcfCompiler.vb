Imports System.IO
Imports System.Numerics
Imports Geometry = MuJoCo.Geometry

''' <summary>
''' 网格资产的加载与凸包缓存。
''' </summary>
''' <remarks>
''' 编译期把每个 STL 读成三角形汤，并缓存到 <see cref="MjsMesh"/> 的
''' <c>CompiledVertices</c> / <c>CompiledFaces</c> 上。运行期建碰撞代理时
''' 再按需取凸包——凸包只算一次，之后按 geom 的内存引用缓存。
''' </remarks>
Public Module MjMeshLibrary

    ''' <summary>默认密度，千克/立方米（与 MuJoCo 的 <c>density</c> 默认值一致）</summary>
    Public Const DefaultDensity As Double = 1000.0

    ''' <summary>
    ''' 解析网格文件的绝对路径：先问 <see cref="MjSpec.MeshResolver"/>，
    ''' 再按 <c>SourceDirectory / meshdir / file</c> 拼。
    ''' </summary>
    Public Function ResolvePath(spec As MjSpec, file As String) As String
        If String.IsNullOrEmpty(file) Then
            Return Nothing
        End If

        If spec.MeshResolver IsNot Nothing Then
            Dim resolved As String = spec.MeshResolver(file)

            If Not String.IsNullOrEmpty(resolved) Then
                Return resolved
            End If
        End If

        If Path.IsPathRooted(file) Then
            Return file
        End If

        Dim root As String = If(spec.SourceDirectory, "")

        If Not String.IsNullOrEmpty(root) Then
            root = Path.Combine(root, If(spec.MeshDir, ""))

            Return Path.GetFullPath(Path.Combine(root, file))
        End If

        Return file
    End Function

    ''' <summary>读一个网格资产，结果缓存回 <paramref name="mesh"/></summary>
    Public Sub Load(spec As MjSpec, mesh As MjsMesh, errors As MjErrorBuffer)
        If mesh.CompiledVertices IsNot Nothing Then
            Return
        End If

        Dim vertices As Double()
        Dim faces As Integer()

        If mesh.Vertices IsNot Nothing AndAlso mesh.Vertices.Length >= 9 Then
            vertices = CType(mesh.Vertices.Clone(), Double())

            If mesh.Faces IsNot Nothing AndAlso mesh.Faces.Length >= 3 Then
                faces = CType(mesh.Faces.Clone(), Integer())
            Else
                faces = Sequential(mesh.Vertices.Length \ 3)
            End If
        Else
            Dim path As String = ResolvePath(spec, mesh.File)

            If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then
                errors.Append($"the mesh '{mesh.Name}' needs the file '{mesh.File}' but it was not found")

                Return
            End If

            Try
                Dim soup As Double() = Geometry.StlReader.Read(path)

                If soup.Length < 9 Then
                    errors.Append($"the mesh file '{path}' holds no triangle")

                    Return
                End If

                vertices = soup
                faces = Sequential(soup.Length \ 3)
            Catch ex As Exception
                errors.Append($"the mesh file '{path}' could not be read: {ex.Message}")

                Return
            End Try
        End If

        Dim scale As Double() = mesh.Scale

        If scale IsNot Nothing AndAlso scale.Length >= 3 AndAlso
           (scale(0) <> 1.0 OrElse scale(1) <> 1.0 OrElse scale(2) <> 1.0) Then
            For i As Integer = 0 To vertices.Length \ 3 - 1
                vertices(i * 3 + 0) *= scale(0)
                vertices(i * 3 + 1) *= scale(1)
                vertices(i * 3 + 2) *= scale(2)
            Next
        End If

        mesh.CompiledVertices = vertices
        mesh.CompiledFaces = faces
    End Sub

    Private Function Sequential(count As Integer) As Integer()
        Dim faces(count - 1) As Integer

        For i As Integer = 0 To count - 1
            faces(i) = i
        Next

        Return faces
    End Function

    ''' <summary>网格的顶点列表（用于建凸包）</summary>
    Public Function PointsOf(mesh As MjsMesh) As Vector3()
        Dim vertices As Double() = mesh.CompiledVertices

        If vertices Is Nothing OrElse vertices.Length < 9 Then
            Return New Vector3() {}
        End If

        Return Geometry.StlReader.PointsOf(vertices)
    End Function

    ''' <summary>
    ''' 由几何体的形状估算质量与惯量（用于没有 <c>&lt;inertial&gt;</c> 的 body）。
    ''' </summary>
    ''' <remarks>
    ''' 只做到"量级正确"：体积按解析公式算，惯量按包围盒的实心盒子算。
    ''' 需要精确值时应该显式写 <c>&lt;inertial&gt;</c>——这与 MuJoCo 在
    ''' <c>inertiafromgeom="false"</c> 时的要求一致。
    ''' </remarks>
    Public Function EstimateInertial(geom As MjsGeom, mass As Double) As Double()
        Dim size As Double() = geom.Size

        Select Case geom.Type
            Case mjtGeom.mjGEOM_SPHERE
                Dim i As Double = 0.4 * mass * size(0) * size(0)

                Return {i, i, i, 0, 0, 0}

            Case mjtGeom.mjGEOM_BOX
                Dim hx As Double = size(0), hy As Double = size(1), hz As Double = size(2)

                Return {mass / 3.0 * (hy * hy + hz * hz),
                        mass / 3.0 * (hx * hx + hz * hz),
                        mass / 3.0 * (hx * hx + hy * hy), 0, 0, 0}

            Case mjtGeom.mjGEOM_CAPSULE, mjtGeom.mjGEOM_CYLINDER
                Dim r As Double = size(0), h As Double = 2.0 * size(1)
                Dim lateral As Double = mass * (3.0 * r * r + h * h) / 12.0

                Return {lateral, 0.5 * mass * r * r, lateral, 0, 0, 0}

            Case mjtGeom.mjGEOM_ELLIPSOID
                Dim a As Double = size(0), b As Double = size(1), c As Double = size(2)

                Return {0.2 * mass * (b * b + c * c), 0.2 * mass * (a * a + c * c),
                        0.2 * mass * (a * a + b * b), 0, 0, 0}

            Case Else
                Dim extent As Double = Math.Max(size(0), Math.Max(size(1), size(2)))

                If extent <= 0 Then
                    extent = 0.01
                End If

                Dim i As Double = 0.4 * mass * extent * extent

                Return {i, i, i, 0, 0, 0}
        End Select
    End Function

    ''' <summary>几何体的体积（用于按密度折算质量）</summary>
    Public Function Volume(geom As MjsGeom) As Double
        Dim size As Double() = geom.Size

        Select Case geom.Type
            Case mjtGeom.mjGEOM_SPHERE
                Return 4.0 / 3.0 * Math.PI * size(0) ^ 3
            Case mjtGeom.mjGEOM_BOX
                Return 8.0 * size(0) * size(1) * size(2)
            Case mjtGeom.mjGEOM_CAPSULE
                Return Math.PI * size(0) ^ 2 * 2.0 * size(1) + 4.0 / 3.0 * Math.PI * size(0) ^ 3
            Case mjtGeom.mjGEOM_CYLINDER
                Return Math.PI * size(0) ^ 2 * 2.0 * size(1)
            Case mjtGeom.mjGEOM_ELLIPSOID
                Return 4.0 / 3.0 * Math.PI * size(0) * size(1) * size(2)
            Case Else
                Return 0.0
        End Select
    End Function

End Module

''' <summary>
''' MJCF 编译器：<see cref="MjSpec"/> → <see cref="mjModel"/>。
''' </summary>
''' <remarks>
''' 对应 <c>src/xml/xml_native_reader.cc</c> 之后的
''' <c>mjCCompile</c>（<c>src/engine/engine_io.c</c> / <c>engine_util</c>）：
''' 把字符串名解析成整数索引，一次性分配全部平铺数组，算好
''' <c>qposadr</c> / <c>dofadr</c> / <c>geomadr</c> 这些偏移，
''' 之后运行期不再碰字符串。
''' </remarks>
Public Module MjcfCompiler

    ''' <summary>预处理：把 mesh 资产读进内存</summary>
    Public Sub PrepareMeshes(spec As MjSpec)
        Dim errors As New MjErrorBuffer()

        For Each mesh As MjsMesh In spec.Meshes
            Call MjMeshLibrary.Load(spec, mesh, errors)
        Next

        If errors.HasError Then
            Throw New MjException(errors.Text)
        End If
    End Sub

    ''' <summary>编译模型；失败时返回 <c>Nothing</c> 并把原因写进 <paramref name="errors"/></summary>
    Public Function Compile(spec As MjSpec, errors As MjErrorBuffer) As mjModel
        ' 网格资产在编译期才读，这样调用方可以在 Parse 之后设置 MeshResolver
        For Each mesh As MjsMesh In spec.Meshes
            Call MjMeshLibrary.Load(spec, mesh, errors)
        Next

        If errors.HasError Then
            Return Nothing
        End If

        Dim bodies As List(Of MjsBody) = spec.EnumerateBodies()
        Dim model As New mjModel()

        model.modelname = spec.ModelName
        model.meshdir = spec.MeshDir
        model.opt = spec.Opt.Clone()
        model.stat = spec.Stat

        model.nbody = bodies.Count
        model.nmesh = spec.Meshes.Count
        model.nmat = spec.Materials.Count
        model.nactuator = spec.Actuators.Count
        model.nu = spec.Actuators.Count
        model.nout = spec.Actuators.Count
        model.na = 0

        ' ---- 计数：geom / joint / site / cam / light ----'
        Dim geomCount As Integer = 0
        Dim jntCount As Integer = 0
        Dim siteCount As Integer = 0
        Dim camCount As Integer = 0
        Dim lightCount As Integer = 0

        For Each body As MjsBody In bodies
            geomCount += body.Geoms.Count
            jntCount += body.Joints.Count
            siteCount += body.Sites.Count
            camCount += body.Cameras.Count
            lightCount += body.Lights.Count
        Next

        model.ngeom = geomCount
        model.njnt = jntCount
        model.nsite = siteCount
        model.ncam = camCount
        model.nlight = lightCount
        model.neq = spec.Equalities.Count
        model.ntendon = spec.Tendons.Count
        model.nsensor = spec.Sensors.Count
        model.nkey = spec.Keys.Count
        model.nexclude = spec.Excludes.Count
        model.npair = 0
        model.nmocap = 0

        ' ---- 关节的 qpos / dof 地址 ----'
        Dim nq As Integer = 0
        Dim nv As Integer = 0

        For Each body As MjsBody In bodies
            For Each joint As MjsJoint In body.Joints
                Dim dimQ As Integer = mj_jntQposDim(joint.Type)
                Dim dimV As Integer = mj_jntDofDim(joint.Type)

                If joint.Type = mjtJoint.mjJNT_FREE AndAlso body.Joints.Count > 1 Then
                    errors.Append($"body '{body.Name}' declares a free joint together with other joints")

                    Return Nothing
                End If

                nq += dimQ
                nv += dimV
            Next
        Next

        model.nq = nq
        model.nv = nv

        Dim wrapCount As Integer = 0

        For Each tendon As MjsTendon In spec.Tendons
            wrapCount += tendon.Joints.Count + tendon.Sites.Count
        Next

        model.nwrap = wrapCount

        Call model.Allocate()

        ' ---- 名称表：先把 id 与名字写进去，后面的引用都能查到 ----'
        For i As Integer = 0 To bodies.Count - 1
            model.body_name(i) = If(bodies(i).Name, "")
            model.RegisterName(mjtObj.mjOBJ_BODY, i, bodies(i).Name)
        Next

        ' ---- body 基本属性 ----'
        Dim geomAdr As Integer = 0
        Dim jntAdr As Integer = 0
        Dim siteAdr As Integer = 0
        Dim camAdr As Integer = 0
        Dim lightAdr As Integer = 0

        Dim bodyId As New Dictionary(Of MjsBody, Integer)()

        For i As Integer = 0 To bodies.Count - 1
            bodyId(bodies(i)) = i
        Next

        For i As Integer = 0 To bodies.Count - 1
            Dim body As MjsBody = bodies(i)

            model.body_parentid(i) = If(i = 0 OrElse body.Parent Is Nothing, -1, bodyId(body.Parent))
            model.body_pos(i * 3 + 0) = body.Pos(0)
            model.body_pos(i * 3 + 1) = body.Pos(1)
            model.body_pos(i * 3 + 2) = body.Pos(2)
            Call CopyQuat(body.Quat, model.body_quat, i * 4)

            model.body_ipos(i * 3 + 0) = body.Ipos(0)
            model.body_ipos(i * 3 + 1) = body.Ipos(1)
            model.body_ipos(i * 3 + 2) = body.Ipos(2)
            model.body_iquat(i * 4) = 1.0

            model.body_mass(i) = body.Mass

            For k As Integer = 0 To 5
                model.body_fullinertia(i * 6 + k) = body.FullInertia(k)
            Next

            model.body_inertia(i * 3 + 0) = body.FullInertia(0)
            model.body_inertia(i * 3 + 1) = body.FullInertia(1)
            model.body_inertia(i * 3 + 2) = body.FullInertia(2)

            model.body_contype(i) = body.Contype
            model.body_conaffinity(i) = body.Conaffinity

            ' 碰撞分组：0 表示自动。静态 body（世界下的无自由度 body）归入环境组，
            ' 其余归入机器人组；两者互相碰撞，机器人之间不碰撞（与 duck 的 MJCF 一致）
            Dim isStaticBody As Boolean = (i > 0 AndAlso model.body_parentid(i) = 0 AndAlso body.Joints.Count = 0)

            model.body_group(i) = If(body.CollisionGroup <> 0, body.CollisionGroup,
                                     If(isStaticBody, MjCollisionModel.GroupEnvironment, MjCollisionModel.GroupRobot))
            model.body_mask(i) = If(body.CollisionMask <> 0, body.CollisionMask,
                                    If(isStaticBody, -1,
                                       MjCollisionModel.GroupEnvironment Or MjCollisionModel.GroupProp))

            model.body_geomadr(i) = geomAdr
            model.body_geomnum(i) = body.Geoms.Count
            model.body_jntadr(i) = jntAdr
            model.body_jntnum(i) = body.Joints.Count

            For Each geom As MjsGeom In body.Geoms
                geomAdr += 1
            Next

            For Each joint As MjsJoint In body.Joints
                model.jnt_name(jntAdr) = If(joint.Name, "")
                model.RegisterName(mjtObj.mjOBJ_JOINT, jntAdr, joint.Name)
                jntAdr += 1
            Next

            For Each site As MjsSite In body.Sites
                model.site_name(siteAdr) = If(site.Name, "")
                model.RegisterName(mjtObj.mjOBJ_SITE, siteAdr, site.Name)
                siteAdr += 1
            Next

            For Each camera As MjsCamera In body.Cameras
                model.cam_name(camAdr) = If(camera.Name, "")
                model.RegisterName(mjtObj.mjOBJ_CAMERA, camAdr, camera.Name)
                camAdr += 1
            Next

            For Each light As MjsLight In body.Lights
                model.light_name(lightAdr) = If(light.Name, "")
                model.RegisterName(mjtObj.mjOBJ_LIGHT, lightAdr, light.Name)
                lightAdr += 1
            Next
        Next

        ' ---- root / weld id ----'
        For i As Integer = 0 To bodies.Count - 1
            Dim parent As Integer = model.body_parentid(i)

            If i = 0 OrElse parent = 0 Then
                model.body_rootid(i) = i
            Else
                model.body_rootid(i) = model.body_rootid(parent)
            End If
        Next

        ' ---- mesh 数据 ----'
        Dim meshVertAdr As Integer = 0
        Dim meshFaceAdr As Integer = 0
        Dim totalVert As Integer = 0
        Dim totalFace As Integer = 0

        For Each mesh As MjsMesh In spec.Meshes
            totalVert += (If(mesh.CompiledVertices, New Double() {}).Length \ 3)
            totalFace += (If(mesh.CompiledFaces, New Integer() {}).Length \ 3)
        Next

        Dim meshVert(totalVert * 3 - 1) As Double
        Dim meshFace(totalFace * 3 - 1) As Integer

        For m As Integer = 0 To spec.Meshes.Count - 1
            Dim mesh As MjsMesh = spec.Meshes(m)
            Dim vertices As Double() = If(mesh.CompiledVertices, New Double() {})
            Dim faces As Integer() = If(mesh.CompiledFaces, New Integer() {})

            model.mesh_name(m) = If(mesh.Name, "")
            model.RegisterName(mjtObj.mjOBJ_MESH, m, mesh.Name)

            model.mesh_vertadr(m) = meshVertAdr
            model.mesh_vertnum(m) = vertices.Length \ 3
            model.mesh_faceadr(m) = meshFaceAdr
            model.mesh_facenum(m) = faces.Length \ 3

            If vertices.Length > 0 AndAlso meshVert.Length > 0 Then
                Call Array.Copy(vertices, 0, meshVert, meshVertAdr * 3, Math.Min(vertices.Length, meshVert.Length - meshVertAdr * 3))
            End If

            If faces.Length > 0 AndAlso meshFace.Length > 0 Then
                Call Array.Copy(faces, 0, meshFace, meshFaceAdr * 3, Math.Min(faces.Length, meshFace.Length - meshFaceAdr * 3))
            End If

            meshVertAdr += vertices.Length \ 3
            meshFaceAdr += faces.Length \ 3
        Next

        model.mesh_vert = meshVert
        model.mesh_face = meshFace

        ' ---- 材质 ----'
        For m As Integer = 0 To spec.Materials.Count - 1
            Dim material As MjsMaterial = spec.Materials(m)

            model.mat_name(m) = If(material.Name, "")
            model.RegisterName(mjtObj.mjOBJ_MATERIAL, m, material.Name)

            For c As Integer = 0 To 3
                model.mat_rgba(m * 4 + c) = material.Rgba(c)
            Next
        Next

        ' ---- geom ----'
        Dim g As Integer = 0

        For i As Integer = 0 To bodies.Count - 1
            For Each geom As MjsGeom In bodies(i).Geoms
                model.geom_name(g) = If(geom.Name, "")
                model.RegisterName(mjtObj.mjOBJ_GEOM, g, geom.Name)

                model.geom_type(g) = CInt(geom.Type)
                model.geom_bodyid(g) = i
                model.geom_contype(g) = geom.Contype
                model.geom_conaffinity(g) = geom.Conaffinity
                model.geom_condim(g) = geom.Condim
                model.geom_group(g) = geom.Group
                model.geom_priority(g) = geom.Priority
                model.geom_solmix(g) = geom.Solmix
                model.geom_margin(g) = geom.Margin
                model.geom_gap(g) = geom.Gap

                model.geom_pos(g * 3 + 0) = geom.Pos(0)
                model.geom_pos(g * 3 + 1) = geom.Pos(1)
                model.geom_pos(g * 3 + 2) = geom.Pos(2)
                Call CopyQuat(geom.Quat, model.geom_quat, g * 4)

                For c As Integer = 0 To 2
                    model.geom_size(g * 3 + c) = geom.Size(c)
                    model.geom_friction(g * 3 + c) = geom.Friction(c)
                Next

                For c As Integer = 0 To 1
                    model.geom_solref(g * 2 + c) = geom.Solref(c)
                Next

                For c As Integer = 0 To 4
                    model.geom_solimp(g * 5 + c) = geom.Solimp(c)
                Next

                ' mesh 引用
                If geom.Type = mjtGeom.mjGEOM_MESH Then
                    Dim meshId As Integer = -1

                    If Not String.IsNullOrEmpty(geom.MeshName) Then
                        meshId = model.FindId(mjtObj.mjOBJ_MESH, geom.MeshName)
                    End If

                    If meshId < 0 Then
                        errors.Append($"geom '{geom.Name}' of body '{bodies(i).Name}' references the unknown mesh '{geom.MeshName}'")

                        Return Nothing
                    End If

                    ' 老版本的 MJCF 允许 <geom mesh="x"/> 而不写 type="mesh"
                    model.geom_dataid(g) = meshId
                    model.geom_type(g) = CInt(mjtGeom.mjGEOM_MESH)
                Else
                    model.geom_dataid(g) = -1
                End If

                ' material 引用
                If Not String.IsNullOrEmpty(geom.MaterialName) Then
                    Dim matId As Integer = model.FindId(mjtObj.mjOBJ_MATERIAL, geom.MaterialName)

                    If matId >= 0 Then
                        model.geom_matid(g) = matId

                        For c As Integer = 0 To 3
                            model.geom_rgba(g * 4 + c) = model.mat_rgba(matId * 4 + c)
                        Next
                    Else
                        model.geom_matid(g) = -1
                    End If
                Else
                    model.geom_matid(g) = -1
                End If

                If geom.Rgba IsNot Nothing Then
                    For c As Integer = 0 To 3
                        model.geom_rgba(g * 4 + c) = geom.Rgba(c)
                    Next
                End If

                model.geom_rbound(g) = BoundingRadius(model, g)
                g += 1
            Next
        Next

        ' ---- site / camera / light ----'
        Dim s As Integer = 0

        For i As Integer = 0 To bodies.Count - 1
            For Each site As MjsSite In bodies(i).Sites
                model.site_name(s) = If(site.Name, "")
                model.RegisterName(mjtObj.mjOBJ_SITE, s, site.Name)

                model.site_type(s) = CInt(site.Type)
                model.site_bodyid(s) = i
                model.site_group(s) = site.Group
                model.site_matid(s) = -1

                For c As Integer = 0 To 2
                    model.site_size(s * 3 + c) = site.Size(c)
                    model.site_pos(s * 3 + c) = site.Pos(c)
                Next

                Call CopyQuat(site.Quat, model.site_quat, s * 4)

                For c As Integer = 0 To 3
                    model.site_rgba(s * 4 + c) = site.Rgba(c)
                Next

                s += 1
            Next
        Next

        Dim camIndex As Integer = 0

        For i As Integer = 0 To bodies.Count - 1
            For Each camera As MjsCamera In bodies(i).Cameras
                model.cam_bodyid(camIndex) = i
                model.cam_mode(camIndex) = 0
                model.cam_targetbodyid(camIndex) = -1
                model.cam_fovy(camIndex) = camera.Fovy

                For k As Integer = 0 To 2
                    model.cam_pos(camIndex * 3 + k) = camera.Pos(k)
                Next

                Call CopyQuat(camera.Quat, model.cam_quat, camIndex * 4)
                camIndex += 1
            Next
        Next

        Dim lightIndex As Integer = 0

        For i As Integer = 0 To bodies.Count - 1
            For Each light As MjsLight In bodies(i).Lights
                model.light_bodyid(lightIndex) = i
                model.light_targetbodyid(lightIndex) = -1
                model.light_type(lightIndex) = light.Type

                For k As Integer = 0 To 2
                    model.light_pos(lightIndex * 3 + k) = light.Pos(k)
                    model.light_dir(lightIndex * 3 + k) = light.Dir(k)
                Next

                lightIndex += 1
            Next
        Next

        ' ---- 关节与自由度 ----'
        Dim j As Integer = 0
        Dim dof As Integer = 0
        Dim qposAdr As Integer = 0
        Dim lastDofOfBody(bodies.Count - 1) As Integer

        For i As Integer = 0 To bodies.Count - 1
            lastDofOfBody(i) = -1
        Next

        For i As Integer = 0 To bodies.Count - 1
            Dim parent As Integer = model.body_parentid(i)
            Dim parentDof As Integer = If(parent >= 0, lastDofOfBody(parent), -1)

            model.body_dofadr(i) = dof
            model.body_dofnum(i) = 0

            For Each joint As MjsJoint In bodies(i).Joints
                model.jnt_type(j) = CInt(joint.Type)
                model.jnt_bodyid(j) = i
                model.jnt_qposadr(j) = qposAdr
                model.jnt_dofadr(j) = dof
                model.jnt_actuatorid(j) = -1
                model.jnt_group(j) = joint.Group
                model.jnt_limited(j) = If(joint.Limited, False)
                model.jnt_stiffness(j) = joint.Stiffness
                model.jnt_margin(j) = joint.Margin

                For k As Integer = 0 To 2
                    model.jnt_pos(j * 3 + k) = joint.Pos(k)
                    model.jnt_axis(j * 3 + k) = joint.Axis(k)
                Next

                For k As Integer = 0 To 1
                    model.jnt_range(j * 2 + k) = joint.Range(k)
                    model.jnt_actfrcrange(j * 2 + k) = joint.ActFrcRange(k)
                    model.jnt_solref(j * 2 + k) = joint.Solref(k)
                Next

                For k As Integer = 0 To 4
                    model.jnt_solimp(j * 5 + k) = joint.Solimp(k)
                Next

                model.jnt_actfrclimited(j) = joint.ActFrcLimited

                Dim dofCount As Integer = mj_jntDofDim(joint.Type)

                For k As Integer = 0 To dofCount - 1
                    model.dof_bodyid(dof) = i
                    model.dof_jntid(dof) = j
                    model.dof_parentid(dof) = parentDof
                    model.dof_armature(dof) = joint.Armature
                    model.dof_damping(dof) = joint.Damping
                    model.dof_frictionloss(dof) = joint.FrictionLoss
                    model.dof_solref(dof * 2 + 0) = joint.Solref(0)
                    model.dof_solref(dof * 2 + 1) = joint.Solref(1)

                    For m As Integer = 0 To 4
                        model.dof_solimp(dof * 5 + m) = joint.Solimp(m)
                    Next

                    dof += 1
                Next

                lastDofOfBody(i) = dof - 1
                qposAdr += mj_jntQposDim(joint.Type)
                j += 1
            Next
        Next

        ' ---- 没有 <inertial> 的 body：按 geom 估算 ----'
        Call EstimateMissingInertia(spec, model, bodies, errors)

        If errors.HasError Then
            Return Nothing
        End If

        ' ---- qpos0 ----'
        Call ComputeReferencePose(model, bodies)

        ' ---- weld id / simple 标记 ----'
        For i As Integer = 0 To bodies.Count - 1
            If model.body_dofnum(i) > 0 Then
                model.body_weldid(i) = i
            ElseIf model.body_parentid(i) <= 0 Then
                model.body_weldid(i) = i
            Else
                model.body_weldid(i) = model.body_weldid(model.body_parentid(i))
            End If

            model.body_simple(i) = model.body_jntnum(i) <= 1
        Next

        ' ---- 执行器 ----'
        Call CompileActuators(spec, model, errors)

        ' ---- 传感器 ----'
        Call CompileSensors(spec, model, errors)

        ' ---- 等式约束 ----'
        Call CompileEqualities(spec, model, errors)

        ' ---- 腱 ----'
        Call CompileTendons(spec, model, errors)

        ' ---- 关键帧 ----'
        Call CompileKeyframes(spec, model)

        ' ---- 统计 ----'
        Call ComputeStatistics(model)

        If errors.HasError Then
            Return Nothing
        End If

        Call MjAssert.CheckModel(model)

        Return model
    End Function

    ' /********************************************************************************/
    '  惯性估算
    ' /********************************************************************************/

    Private Sub EstimateMissingInertia(spec As MjSpec, model As mjModel, bodies As List(Of MjsBody),
                                       errors As MjErrorBuffer)
        For i As Integer = 1 To bodies.Count - 1
            Dim body As MjsBody = bodies(i)

            If body.HasInertial Then
                Continue For
            End If

            Dim mass As Double = 0.0
            Dim boxMin As New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
            Dim boxMax As New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)
            Dim any As Boolean = False

            For Each geom As MjsGeom In body.Geoms
                Dim volume As Double = MjMeshLibrary.Volume(geom)

                If volume <= 0 Then
                    Continue For
                End If

                mass += volume * MjMeshLibrary.DefaultDensity

                Dim half As New Vector3(CSng(geom.Size(0)), CSng(geom.Size(1)), CSng(geom.Size(2)))
                Dim center As New Vector3(CSng(geom.Pos(0)), CSng(geom.Pos(1)), CSng(geom.Pos(2)))

                boxMin = Vector3.Min(boxMin, center - half)
                boxMax = Vector3.Max(boxMax, center + half)
                any = True
            Next

            If Not any Then
                mass = body.Mass
            End If

            If mass <= 0 Then
                Continue For
            End If

            model.body_mass(i) = mass

            Dim extent As Vector3 = If(any, boxMax - boxMin, New Vector3(0.02F, 0.02F, 0.02F))
            Dim hx As Double = Math.Max(extent.X * 0.5, 1.0E-4)
            Dim hy As Double = Math.Max(extent.Y * 0.5, 1.0E-4)
            Dim hz As Double = Math.Max(extent.Z * 0.5, 1.0E-4)

            Dim ixx As Double = mass / 3.0 * (hy * hy + hz * hz)
            Dim iyy As Double = mass / 3.0 * (hx * hx + hz * hz)
            Dim izz As Double = mass / 3.0 * (hx * hx + hy * hy)

            model.body_inertia(i * 3 + 0) = ixx
            model.body_inertia(i * 3 + 1) = iyy
            model.body_inertia(i * 3 + 2) = izz

            For k As Integer = 0 To 5
                model.body_fullinertia(i * 6 + k) = 0.0
            Next

            model.body_fullinertia(i * 6 + 0) = ixx
            model.body_fullinertia(i * 6 + 1) = iyy
            model.body_fullinertia(i * 6 + 2) = izz

            If any Then
                model.body_ipos(i * 3 + 0) = (boxMin.X + boxMax.X) * 0.5
                model.body_ipos(i * 3 + 1) = (boxMin.Y + boxMax.Y) * 0.5
                model.body_ipos(i * 3 + 2) = (boxMin.Z + boxMax.Z) * 0.5
            End If
        Next
    End Sub

    ' /********************************************************************************/
    '  参考姿态（qpos0）
    ' /********************************************************************************/

    Private Sub ComputeReferencePose(model As mjModel, bodies As List(Of MjsBody))
        Dim position(model.nbody * 3 - 1) As Double
        Dim rotation(model.nbody * 4 - 1) As Double

        For i As Integer = 0 To model.nbody - 1
            rotation(i * 4) = 1.0
        Next

        For i As Integer = 1 To model.nbody - 1
            Dim parent As Integer = model.body_parentid(i)
            Dim parentQuat As Quaternion = If(parent >= 1,
                                              MjMath.Quat(rotation, parent * 4),
                                              Quaternion.Identity)
            Dim localQuat As Quaternion = MjMath.Quat(model.body_quat, i * 4)
            Dim world As Quaternion = MjMath.Normalize(MjMath.Mul(parentQuat, localQuat))

            Call MjMath.WriteQuat(rotation, i * 4, world)

            Dim parentPos As Vector3 = If(parent >= 1,
                                          New Vector3(CSng(position(parent * 3)), CSng(position(parent * 3 + 1)), CSng(position(parent * 3 + 2))),
                                          Vector3.Zero)
            Dim localPos As New Vector3(CSng(model.body_pos(i * 3)), CSng(model.body_pos(i * 3 + 1)), CSng(model.body_pos(i * 3 + 2)))
            Dim world3 As Vector3 = parentPos + MjMath.Rotate(parentQuat, localPos)

            position(i * 3 + 0) = world3.X
            position(i * 3 + 1) = world3.Y
            position(i * 3 + 2) = world3.Z

            ' 自由关节的 qpos0 就是 body 帧的世界位姿
            For Each joint As MjsJoint In bodies(i).Joints
                If joint.Type <> mjtJoint.mjJNT_FREE Then
                    Continue For
                End If

                Dim adr As Integer = model.jnt_qposadr(IndexOfJoint(model, i, joint))

                model.qpos0(adr + 0) = world3.X
                model.qpos0(adr + 1) = world3.Y
                model.qpos0(adr + 2) = world3.Z

                Call MjMath.WriteQuat(model.qpos0, adr + 3, world)
            Next
        Next

        ' 球关节的 qpos0 是单位四元数
        For j As Integer = 0 To model.njnt - 1
            If CType(model.jnt_type(j), mjtJoint) = mjtJoint.mjJNT_BALL Then
                model.qpos0(model.jnt_qposadr(j) + 3) = 1.0
            End If
        Next

        Call Array.Copy(model.qpos0, model.qpos_spring, model.qpos0.Length)
    End Sub

    Private Function IndexOfJoint(model As mjModel, body As Integer, joint As MjsJoint) As Integer
        Dim start As Integer = model.body_jntadr(body)

        For k As Integer = 0 To model.body_jntnum(body) - 1
            Dim index As Integer = start + k

            If String.Equals(model.jnt_name(index), If(joint.Name, ""), StringComparison.Ordinal) Then
                Return index
            End If
        Next

        Return start
    End Function

    ' /********************************************************************************/
    '  执行器
    ' /********************************************************************************/

    Private Sub CompileActuators(spec As MjSpec, model As mjModel, errors As MjErrorBuffer)
        For a As Integer = 0 To model.nactuator - 1
            Dim actuator As MjsActuator = spec.Actuators(a)

            model.actuator_name(a) = If(actuator.Name, "")
            model.RegisterName(mjtObj.mjOBJ_ACTUATOR, a, actuator.Name)

            model.actuator_trntype(a) = CInt(actuator.Trntype)
            model.actuator_dyntype(a) = CInt(actuator.DynType)
            model.actuator_ctrladr(a) = a
            model.actuator_group(a) = actuator.Group
            model.actuator_gear(a * 6 + 0) = actuator.Gear

            For k As Integer = 0 To 1
                model.actuator_ctrlrange(a * 2 + k) = actuator.CtrlRange(k)
                model.actuator_forcerange(a * 2 + k) = actuator.ForceRange(k)
            Next

            model.actuator_ctrllimited(a) = actuator.CtrlLimited
            model.actuator_forcelimited(a) = actuator.ForceLimited

            Dim target As Integer = -1

            Select Case actuator.Trntype
                Case mjtTrn.mjTRN_JOINT, mjtTrn.mjTRN_JOINTINPARENT
                    target = model.FindId(mjtObj.mjOBJ_JOINT, actuator.Target)

                    If target < 0 Then
                        errors.Append($"actuator '{actuator.Name}' references the unknown joint '{actuator.Target}'")

                        Continue For
                    End If

                    model.actuator_trnid(a * 2 + 0) = target

                    If model.jnt_actuatorid(target) < 0 Then
                        model.jnt_actuatorid(target) = a
                    End If

                Case mjtTrn.mjTRN_TENDON
                    target = model.FindId(mjtObj.mjOBJ_TENDON, actuator.Target)

                Case mjtTrn.mjTRN_SITE
                    target = model.FindId(mjtObj.mjOBJ_SITE, actuator.Target)

                Case mjtTrn.mjTRN_BODY
                    target = model.FindId(mjtObj.mjOBJ_BODY, actuator.Target)
            End Select

            If target >= 0 Then
                model.actuator_trnid(a * 2 + 0) = target
            End If

            model.actuator_trnid(a * 2 + 1) = -1
            actuator.TrnId = target

            ' gainprm / biasprm：三种语义都落到同一个仿射力律
            '   force = gain·ctrl + bias[0] + bias[1]·length + bias[2]·velocity
            Select Case actuator.Kind
                Case mjtActuator.mjACTUATOR_POSITION
                    model.actuator_gaintype(a) = CInt(mjtGain.mjGAIN_FIXED)
                    model.actuator_biastype(a) = CInt(mjtBias.mjBIAS_AFFINE)
                    model.actuator_gainprm(a * mjNGAIN + 0) = actuator.Kp
                    model.actuator_biasprm(a * mjNBIAS + 0) = 0.0
                    model.actuator_biasprm(a * mjNBIAS + 1) = -actuator.Kp
                    model.actuator_biasprm(a * mjNBIAS + 2) = -actuator.Kv

                Case mjtActuator.mjACTUATOR_VELOCITY, mjtActuator.mjACTUATOR_DAMPER
                    model.actuator_gaintype(a) = CInt(mjtGain.mjGAIN_FIXED)
                    model.actuator_biastype(a) = CInt(mjtBias.mjBIAS_AFFINE)
                    model.actuator_gainprm(a * mjNGAIN + 0) = If(actuator.Kind = mjtActuator.mjACTUATOR_DAMPER, 0.0, actuator.Kv)
                    model.actuator_biasprm(a * mjNBIAS + 2) = -actuator.Kv

                Case Else
                    ' motor / general：纯增益，无偏置
                    model.actuator_gaintype(a) = CInt(mjtGain.mjGAIN_FIXED)
                    model.actuator_biastype(a) = CInt(mjtBias.mjBIAS_NONE)
                    model.actuator_gainprm(a * mjNGAIN + 0) = If(actuator.Gear = 0.0, 1.0, actuator.Gear)
            End Select
        Next
    End Sub

    ' /********************************************************************************/
    '  传感器
    ' /********************************************************************************/

    Private Sub CompileSensors(spec As MjSpec, model As mjModel, errors As MjErrorBuffer)
        Dim adr As Integer = 0

        For s As Integer = 0 To model.nsensor - 1
            Dim sensor As MjsSensor = spec.Sensors(s)

            model.sensor_name(s) = If(sensor.Name, "")
            model.RegisterName(mjtObj.mjOBJ_SENSOR, s, sensor.Name)

            model.sensor_type(s) = CInt(sensor.Type)
            model.sensor_objtype(s) = CInt(sensor.ObjType)
            model.sensor_reftype(s) = CInt(sensor.RefType)
            model.sensor_noise(s) = sensor.Noise
            model.sensor_cutoff(s) = sensor.Cutoff

            sensor.ObjId = model.FindId(sensor.ObjType, sensor.ObjName)

            If sensor.ObjId < 0 AndAlso Not String.IsNullOrEmpty(sensor.ObjName) Then
                errors.Append($"sensor '{sensor.Name}' references the unknown {mj_objName(sensor.ObjType)} '{sensor.ObjName}'")
            End If

            sensor.RefId = If(String.IsNullOrEmpty(sensor.RefName), -1,
                              model.FindId(sensor.RefType, sensor.RefName))

            model.sensor_objid(s) = sensor.ObjId
            model.sensor_refid(s) = sensor.RefId

            Dim dim_ As Integer = SensorDimOf(sensor.Type)

            If dim_ <= 0 Then
                errors.Append($"sensor '{sensor.Name}' has the unsupported type {sensor.Type}")

                Continue For
            End If

            model.sensor_dim(s) = dim_
            model.sensor_adr(s) = adr
            model.sensor_datatype(s) = CInt(SensorDataTypeOf(sensor.Type))

            adr += dim_
        Next

        model.nsensordata = adr
    End Sub

    ''' <summary>各类传感器的数据维数</summary>
    Public Function SensorDimOf(type As mjtSensor) As Integer
        Select Case type
            Case mjtSensor.mjSENS_TOUCH, mjtSensor.mjSENS_RANGEFINDER,
                 mjtSensor.mjSENS_JOINTPOS, mjtSensor.mjSENS_JOINTVEL,
                 mjtSensor.mjSENS_TENDONPOS, mjtSensor.mjSENS_TENDONVEL,
                 mjtSensor.mjSENS_ACTUATORPOS, mjtSensor.mjSENS_ACTUATORVEL,
                 mjtSensor.mjSENS_ACTUATORFRC, mjtSensor.mjSENS_JOINTACTFRC,
                 mjtSensor.mjSENS_TENDONACTFRC,
                 mjtSensor.mjSENS_JOINTLIMITPOS, mjtSensor.mjSENS_JOINTLIMITVEL,
                 mjtSensor.mjSENS_JOINTLIMITFRC, mjtSensor.mjSENS_TENDONLIMITPOS,
                 mjtSensor.mjSENS_TENDONLIMITVEL, mjtSensor.mjSENS_TENDONLIMITFRC,
                 mjtSensor.mjSENS_INSIDESITE, mjtSensor.mjSENS_GEOMDIST,
                 mjtSensor.mjSENS_E_POTENTIAL, mjtSensor.mjSENS_E_KINETIC, mjtSensor.mjSENS_CLOCK
                Return 1

            Case mjtSensor.mjSENS_ACCELEROMETER, mjtSensor.mjSENS_VELOCIMETER,
                 mjtSensor.mjSENS_GYRO, mjtSensor.mjSENS_FORCE, mjtSensor.mjSENS_TORQUE,
                 mjtSensor.mjSENS_MAGNETOMETER, mjtSensor.mjSENS_BALLANGVEL,
                 mjtSensor.mjSENS_FRAMEPOS, mjtSensor.mjSENS_FRAMEXAXIS,
                 mjtSensor.mjSENS_FRAMEYAXIS, mjtSensor.mjSENS_FRAMEZAXIS,
                 mjtSensor.mjSENS_FRAMELINVEL, mjtSensor.mjSENS_FRAMEANGVEL,
                 mjtSensor.mjSENS_FRAMELINACC, mjtSensor.mjSENS_FRAMEANGACC,
                 mjtSensor.mjSENS_SUBTREECOM, mjtSensor.mjSENS_SUBTREELINVEL,
                 mjtSensor.mjSENS_SUBTREEANGMOM, mjtSensor.mjSENS_GEOMNORMAL
                Return 3

            Case mjtSensor.mjSENS_BALLQUAT, mjtSensor.mjSENS_FRAMEQUAT
                Return 4

            Case mjtSensor.mjSENS_GEOMFROMTO
                Return 6

            Case mjtSensor.mjSENS_CAMPROJECTION
                Return 2

            Case Else
                Return 0
        End Select
    End Function

    Private Function SensorDataTypeOf(type As mjtSensor) As mjtDataType
        Select Case type
            Case mjtSensor.mjSENS_FRAMEQUAT, mjtSensor.mjSENS_BALLQUAT
                Return mjtDataType.mjDATATYPE_QUATERNION
            Case mjtSensor.mjSENS_FRAMEXAXIS, mjtSensor.mjSENS_FRAMEYAXIS,
                 mjtSensor.mjSENS_FRAMEZAXIS, mjtSensor.mjSENS_GEOMNORMAL
                Return mjtDataType.mjDATATYPE_AXIS
            Case mjtSensor.mjSENS_TOUCH, mjtSensor.mjSENS_RANGEFINDER, mjtSensor.mjSENS_INSIDESITE
                Return mjtDataType.mjDATATYPE_POSITIVE
            Case Else
                Return mjtDataType.mjDATATYPE_REAL
        End Select
    End Function

    ' /********************************************************************************/
    '  等式约束 / 腱 / 关键帧
    ' /********************************************************************************/

    Private Sub CompileEqualities(spec As MjSpec, model As mjModel, errors As MjErrorBuffer)
        For e As Integer = 0 To model.neq - 1
            Dim eq As MjsEquality = spec.Equalities(e)

            model.eq_name(e) = If(eq.Name, "")
            model.RegisterName(mjtObj.mjOBJ_EQUALITY, e, eq.Name)

            model.eq_type(e) = CInt(eq.Type)
            model.eq_objtype(e) = CInt(eq.Obj1Type)
            model.eq_active0(e) = eq.Active

            eq.Obj1Id = model.FindId(eq.Obj1Type, eq.Obj1Name)
            eq.Obj2Id = If(String.IsNullOrEmpty(eq.Obj2Name), -1,
                           model.FindId(eq.Obj2Type, eq.Obj2Name))

            model.eq_obj1id(e) = eq.Obj1Id
            model.eq_obj2id(e) = eq.Obj2Id

            If eq.Obj1Id < 0 Then
                errors.Append($"equality '{eq.Name}' references the unknown {mj_objName(eq.Obj1Type)} '{eq.Obj1Name}'")
            End If

            For k As Integer = 0 To 1
                model.eq_solref(e * 2 + k) = eq.Solref(k)
            Next

            For k As Integer = 0 To 4
                model.eq_solimp(e * 5 + k) = eq.Solimp(k)
            Next

            For k As Integer = 0 To mjNEQDATA - 1
                model.eq_data(e * mjNEQDATA + k) = eq.Data(k)
            Next
        Next
    End Sub

    Private Sub CompileTendons(spec As MjSpec, model As mjModel, errors As MjErrorBuffer)
        Dim wrapIndex As Integer = 0

        For t As Integer = 0 To model.ntendon - 1
            Dim tendon As MjsTendon = spec.Tendons(t)

            model.tendon_name(t) = If(tendon.Name, "")
            model.RegisterName(mjtObj.mjOBJ_TENDON, t, tendon.Name)

            model.tendon_type(t) = CInt(tendon.Type)
            model.tendon_stiffness(t) = tendon.Stiffness
            model.tendon_damping(t) = tendon.Damping
            model.tendon_armature(t) = tendon.Armature
            model.tendon_frictionloss(t) = tendon.FrictionLoss
            model.tendon_range(t * 2 + 0) = tendon.Range(0)
            model.tendon_range(t * 2 + 1) = tendon.Range(1)
            model.tendon_limited(t) = tendon.Limited

            model.tendon_adr(t) = wrapIndex

            For Each jointName As String In tendon.Joints
                Dim id As Integer = model.FindId(mjtObj.mjOBJ_JOINT, jointName)

                If id < 0 Then
                    errors.Append($"tendon '{tendon.Name}' references the unknown joint '{jointName}'")

                    Continue For
                End If

                model.wrap_type(wrapIndex) = CInt(mjtWrap.mjWRAP_JOINT)
                model.wrap_objid(wrapIndex) = id
                model.wrap_prm(wrapIndex) = 1.0
                wrapIndex += 1
            Next

            For Each siteName As String In tendon.Sites
                Dim id As Integer = model.FindId(mjtObj.mjOBJ_SITE, siteName)

                If id < 0 Then
                    errors.Append($"tendon '{tendon.Name}' references the unknown site '{siteName}'")

                    Continue For
                End If

                model.wrap_type(wrapIndex) = CInt(mjtWrap.mjWRAP_SITE)
                model.wrap_objid(wrapIndex) = id
                model.wrap_prm(wrapIndex) = 0.0
                wrapIndex += 1
            Next

            model.tendon_num(t) = wrapIndex - model.tendon_adr(t)
        Next

        If wrapIndex <> model.nwrap Then
            ' nwrap 是先算出来的；数量不符时补足数组不影响正确性，只是有些项为 0
            errors.Append($"the tendon wrap table needs {wrapIndex} entries but was sized {model.nwrap}")
        End If
    End Sub

    Private Sub CompileKeyframes(spec As MjSpec, model As mjModel)
        For k As Integer = 0 To model.nkey - 1
            Dim key As MjsKeyframe = spec.Keys(k)

            model.key_name(k) = If(key.Name, "")
            model.RegisterName(mjtObj.mjOBJ_KEY, k, key.Name)
            model.key_time(k) = key.Time

            If key.Qpos IsNot Nothing Then
                Call Array.Copy(key.Qpos, 0, model.key_qpos, k * model.nq,
                                Math.Min(key.Qpos.Length, model.nq))
            Else
                Call Array.Copy(model.qpos0, 0, model.key_qpos, k * model.nq, model.nq)
            End If

            If key.Qvel IsNot Nothing Then
                Call Array.Copy(key.Qvel, 0, model.key_qvel, k * model.nv,
                                Math.Min(key.Qvel.Length, model.nv))
            End If

            If key.Ctrl IsNot Nothing Then
                Call Array.Copy(key.Ctrl, 0, model.key_ctrl, k * model.nu,
                                Math.Min(key.Ctrl.Length, model.nu))
            End If
        Next
    End Sub

    ' /********************************************************************************/
    '  统计与辅助
    ' /********************************************************************************/

    Private Sub ComputeStatistics(model As mjModel)
        Dim massSum As Double = 0.0
        Dim massCount As Integer = 0
        Dim inertiaSum As Double = 0.0
        Dim inertiaCount As Integer = 0

        For i As Integer = 1 To model.nbody - 1
            If model.body_mass(i) > 0 Then
                massSum += model.body_mass(i)
                massCount += 1
            End If

            Dim trace As Double = (model.body_inertia(i * 3) + model.body_inertia(i * 3 + 1) +
                                   model.body_inertia(i * 3 + 2)) / 3.0

            If trace > 0 Then
                inertiaSum += trace
                inertiaCount += 1
            End If
        Next

        model.stat.meanmass = If(massCount > 0, massSum / massCount, 1.0)
        model.stat.meaninertia = If(inertiaCount > 0, inertiaSum / inertiaCount, 1.0E-3)

        Dim extent As Double = 0.0

        For i As Integer = 0 To model.nbody - 1
            extent = Math.Max(extent, Math.Abs(model.body_pos(i * 3)) +
                                     Math.Abs(model.body_pos(i * 3 + 1)) +
                                     Math.Abs(model.body_pos(i * 3 + 2)))
        Next

        model.stat.extent = Math.Max(extent, 0.1)
        model.stat.meansize = model.stat.extent * 0.05
        model.stat.center = {0.0, 0.0, 0.0}
    End Sub

    ''' <summary>几何体的包围球半径（在 body 坐标系里）</summary>
    Private Function BoundingRadius(model As mjModel, g As Integer) As Double
        Select Case CType(model.geom_type(g), mjtGeom)
            Case mjtGeom.mjGEOM_PLANE
                Return 1.0E+9
            Case mjtGeom.mjGEOM_SPHERE
                Return model.geom_size(g * 3)
            Case mjtGeom.mjGEOM_CAPSULE, mjtGeom.mjGEOM_CYLINDER
                Return model.geom_size(g * 3 + 1) + model.geom_size(g * 3)
            Case mjtGeom.mjGEOM_BOX, mjtGeom.mjGEOM_ELLIPSOID
                Dim x As Double = model.geom_size(g * 3)
                Dim y As Double = model.geom_size(g * 3 + 1)
                Dim z As Double = model.geom_size(g * 3 + 2)

                Return Math.Sqrt(x * x + y * y + z * z)
            Case mjtGeom.mjGEOM_MESH
                Dim mesh As Integer = model.geom_dataid(g)

                If mesh < 0 Then
                    Return 0.1
                End If

                Dim adr As Integer = model.mesh_vertadr(mesh)
                Dim count As Integer = model.mesh_vertnum(mesh)
                Dim scale As Double = If(model.geom_size(g * 3) > 0, model.geom_size(g * 3), 1.0)
                Dim best As Double = 0.0

                For v As Integer = 0 To count - 1
                    Dim x As Double = model.mesh_vert((adr + v) * 3 + 0) * scale
                    Dim y As Double = model.mesh_vert((adr + v) * 3 + 1) * scale
                    Dim z As Double = model.mesh_vert((adr + v) * 3 + 2) * scale

                    best = Math.Max(best, Math.Sqrt(x * x + y * y + z * z))
                Next

                Return Math.Max(best, 0.001)
            Case Else
                Return 0.05
        End Select
    End Function

    Private Sub CopyQuat(source As Double(), target As Double(), offset As Integer)
        Dim q As Double() = MjcfText.NormalizeQuat(source)

        target(offset + 0) = q(0)
        target(offset + 1) = q(1)
        target(offset + 2) = q(2)
        target(offset + 3) = q(3)
    End Sub

End Module
