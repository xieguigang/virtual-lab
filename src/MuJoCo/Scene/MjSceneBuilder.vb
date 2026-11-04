Imports num = System.Numerics

Namespace Scene

    ''' <summary>把中性场景描述装配成可编辑的 <see cref="MjSpec"/> 树。</summary>
    Public Module MjSceneBuilder

        Public Function Build(scene As MjSceneSpec) As MjSpec
            If scene Is Nothing Then Throw New ArgumentNullException(NameOf(scene))

            Dim spec As New MjSpec()
            Dim options As MjSceneOptions = scene.Options

            spec.Opt.timestep = options.Timestep
            spec.Opt.iterations = options.Iterations
            spec.Opt.gravity = New Double() {options.Gravity.X, options.Gravity.Y, options.Gravity.Z}

            If options.GroundPlane Then
                Call spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE,
                                  New num.Vector3(options.GroundSize, options.GroundSize, 0.0F),
                                  name:="ground")
            End If

            Dim bodies As New Dictionary(Of String, MjsBody)(StringComparer.Ordinal)

            For Each bodySpec As MjBodySpec In scene.Bodies
                Dim parent As MjsBody = Nothing

                If Not String.IsNullOrEmpty(bodySpec.Parent) AndAlso bodies.ContainsKey(bodySpec.Parent) Then
                    parent = bodies(bodySpec.Parent)
                End If

                Dim body As MjsBody = spec.AddBody(parent, bodySpec.Name, bodySpec.Pos, bodySpec.Quat)

                If bodySpec.Mass > 0 Then
                    body.HasInertial = True
                    body.Mass = bodySpec.Mass

                    If bodySpec.Inertia IsNot Nothing AndAlso bodySpec.Inertia.Length >= 6 Then
                        body.FullInertia = CType(bodySpec.Inertia.Clone(), Double())
                    End If
                End If

                If bodySpec.CollisionGroup <> 0 Then body.CollisionGroup = bodySpec.CollisionGroup
                If bodySpec.CollisionMask <> 0 Then body.CollisionMask = bodySpec.CollisionMask

                ' 运动学体没有自由度：不挂关节时编译器会把它当静态几何，
                ' 仍参与碰撞但不受重力与积分影响。
                Call spec.AddGeom(body, bodySpec.GeomType, bodySpec.Extents,
                                  meshName:=bodySpec.MeshName,
                                  name:=bodySpec.Name & "-geom")

                bodies(bodySpec.Name) = body
            Next

            For Each jointSpec As MjJointSpec In scene.Joints
                If String.IsNullOrEmpty(jointSpec.Body) OrElse
                   Not bodies.ContainsKey(jointSpec.Body) Then Continue For

                Dim range As num.Vector2? = Nothing

                If jointSpec.Lower <> 0.0 OrElse jointSpec.Upper <> 0.0 Then
                    range = New num.Vector2(CSng(jointSpec.Lower), CSng(jointSpec.Upper))
                End If

                Dim joint As MjsJoint = spec.AddJoint(bodies(jointSpec.Body), jointSpec.Name,
                                                      jointSpec.Type, jointSpec.Axis, range,
                                                      jointSpec.Pos)

                joint.Stiffness = jointSpec.Stiffness
                joint.Damping = jointSpec.Damping
                joint.Armature = jointSpec.Armature
            Next

            For Each actuatorSpec As MjActuatorSpec In scene.Actuators
                Dim forceRange As num.Vector2? = Nothing
                Dim ctrlRange As num.Vector2? = Nothing

                If actuatorSpec.ForceLower <> 0.0 OrElse actuatorSpec.ForceUpper <> 0.0 Then
                    forceRange = New num.Vector2(CSng(actuatorSpec.ForceLower), CSng(actuatorSpec.ForceUpper))
                End If

                If actuatorSpec.CtrlLower <> 0.0 OrElse actuatorSpec.CtrlUpper <> 0.0 Then
                    ctrlRange = New num.Vector2(CSng(actuatorSpec.CtrlLower), CSng(actuatorSpec.CtrlUpper))
                End If

                Call spec.AddActuator(actuatorSpec.Kind, actuatorSpec.Joint,
                                      actuatorSpec.Name, actuatorSpec.Kp, actuatorSpec.Kv,
                                      forceRange, ctrlRange)
            Next

            Return spec
        End Function

    End Module

End Namespace
