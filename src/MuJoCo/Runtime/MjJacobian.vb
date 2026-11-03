Imports System.Numerics

''' <summary>
''' 雅可比矩阵，对应 <c>mj_jac</c> 系列。
''' </summary>
''' <remarks>
''' 输出是平铺数组：<c>jacp(3·nv)</c> 与 <c>jacr(3·nv)</c>，按
''' <c>[x 分量 0..nv-1][y 分量][z 分量]</c> 排列（与 MuJoCo 的
''' <c>mj_jac</c> 一致：每个 3 维行向量在 nv 上展开）。
''' </remarks>
Public Module MjJacobian

    ''' <summary>位置 <paramref name="point"/> 处、随刚体 <paramref name="body"/> 运动的雅可比</summary>
    Public Sub jac(m As mjModel, d As mjData, jacp As Double(), jacr As Double(),
                   point As Vector3, body As Integer)
        MjAssert.RequireSize(jacp, m.nv * 3, NameOf(jacp))
        MjAssert.RequireSize(jacr, m.nv * 3, NameOf(jacr))

        Call Array.Clear(jacp, 0, m.nv * 3)
        Call Array.Clear(jacr, 0, m.nv * 3)

        Dim walk As Integer = body

        While walk >= 1
            Dim com As Vector3 = MjKinematics.ComOf(d, walk)
            Dim start As Integer = m.body_dofadr(walk)
            Dim count As Integer = m.body_dofnum(walk)

            For k As Integer = 0 To count - 1
                Dim dof As Integer = start + k
                Dim omega As New Vector3(CSng(d.cdof(dof * 6 + 0)), CSng(d.cdof(dof * 6 + 1)),
                                         CSng(d.cdof(dof * 6 + 2)))
                Dim linear As New Vector3(CSng(d.cdof(dof * 6 + 3)), CSng(d.cdof(dof * 6 + 4)),
                                          CSng(d.cdof(dof * 6 + 5)))
                Dim pointLinear As Vector3 = linear + Vector3.Cross(omega, point - com)

                jacp(0 * m.nv + dof) = pointLinear.X
                jacp(1 * m.nv + dof) = pointLinear.Y
                jacp(2 * m.nv + dof) = pointLinear.Z

                jacr(0 * m.nv + dof) = omega.X
                jacr(1 * m.nv + dof) = omega.Y
                jacr(2 * m.nv + dof) = omega.Z
            Next

            walk = m.body_parentid(walk)
        End While
    End Sub

    ''' <summary>刚体帧原点处的雅可比</summary>
    Public Sub jacBody(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), body As Integer)
        MjAssert.RequireIndex(body, m.nbody, NameOf(body))

        Call jac(m, d, jacp, jacr, MjKinematics.BodyOrigin(d, body), body)
    End Sub

    ''' <summary>刚体质心处的雅可比</summary>
    Public Sub jacBodyCom(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), body As Integer)
        MjAssert.RequireIndex(body, m.nbody, NameOf(body))

        Call jac(m, d, jacp, jacr, MjKinematics.ComOf(d, body), body)
    End Sub

    ''' <summary>子树质心处的雅可比（只填位置部分）</summary>
    Public Sub jacSubtreeCom(m As mjModel, d As mjData, jacp As Double(), body As Integer)
        MjAssert.RequireIndex(body, m.nbody, NameOf(body))

        Dim point As New Vector3(CSng(d.subtree_com(body * 3)), CSng(d.subtree_com(body * 3 + 1)),
                                 CSng(d.subtree_com(body * 3 + 2)))
        Dim jacr(Math.Max(m.nv * 3, 1) - 1) As Double

        Call jac(m, d, jacp, jacr, point, body)
    End Sub

    ''' <summary>几何体帧原点处的雅可比</summary>
    Public Sub jacGeom(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), geom As Integer)
        MjAssert.RequireIndex(geom, m.ngeom, NameOf(geom))

        Dim point As New Vector3(CSng(d.geom_xpos(geom * 3)), CSng(d.geom_xpos(geom * 3 + 1)),
                                 CSng(d.geom_xpos(geom * 3 + 2)))

        Call jac(m, d, jacp, jacr, point, m.geom_bodyid(geom))
    End Sub

    ''' <summary>站点处的雅可比</summary>
    Public Sub jacSite(m As mjModel, d As mjData, jacp As Double(), jacr As Double(), site As Integer)
        MjAssert.RequireIndex(site, m.nsite, NameOf(site))

        Dim point As New Vector3(CSng(d.site_xpos(site * 3)), CSng(d.site_xpos(site * 3 + 1)),
                                 CSng(d.site_xpos(site * 3 + 2)))

        Call jac(m, d, jacp, jacr, point, m.site_bodyid(site))
    End Sub

    ''' <summary>任意点与任意轴：额外给出轴的角速度雅可比</summary>
    Public Sub jacPointAxis(m As mjModel, d As mjData, jacPoint As Double(), jacAxis As Double(),
                            point As Vector3, axis As Vector3, body As Integer)
        Dim jacr(Math.Max(m.nv * 3, 1) - 1) As Double

        Call jac(m, d, jacPoint, jacr, point, body)

        MjAssert.RequireSize(jacAxis, m.nv * 3, NameOf(jacAxis))
        Call Array.Clear(jacAxis, 0, m.nv * 3)

        Dim u As Vector3 = MjMath.SafeNormalize(axis)
        Dim walk As Integer = body

        While walk >= 1
            Dim start As Integer = m.body_dofadr(walk)
            Dim count As Integer = m.body_dofnum(walk)

            For k As Integer = 0 To count - 1
                Dim dof As Integer = start + k
                Dim omega As New Vector3(CSng(d.cdof(dof * 6 + 0)), CSng(d.cdof(dof * 6 + 1)),
                                         CSng(d.cdof(dof * 6 + 2)))
                Dim derivative As Vector3 = Vector3.Cross(omega, u)

                jacAxis(0 * m.nv + dof) = derivative.X
                jacAxis(1 * m.nv + dof) = derivative.Y
                jacAxis(2 * m.nv + dof) = derivative.Z
            Next

            walk = m.body_parentid(walk)
        End While
    End Sub

    ''' <summary>
    ''' 把雅可比乘上一组速度，得到该点的线速度与角速度（自测与传感器都会用到）。
    ''' </summary>
    Public Function Apply(jacp As Double(), jacr As Double(), nv As Integer,
                          velocity As Double(), ByRef linear As Vector3, ByRef angular As Vector3) As Boolean
        Dim l As Vector3 = Vector3.Zero
        Dim a As Vector3 = Vector3.Zero

        For i As Integer = 0 To nv - 1
            Dim v As Single = CSng(velocity(i))

            l += New Vector3(CSng(jacp(0 * nv + i)) * v, CSng(jacp(1 * nv + i)) * v, CSng(jacp(2 * nv + i)) * v)
            a += New Vector3(CSng(jacr(0 * nv + i)) * v, CSng(jacr(1 * nv + i)) * v, CSng(jacr(2 * nv + i)) * v)
        Next

        linear = l
        angular = a

        Return True
    End Function

End Module
