Imports Microsoft.VisualBasic.Engine.Physics
Imports Microsoft.VisualBasic.Engine.Physics.Math3D

Namespace Model

    ''' <summary>逆运动学求解结果。</summary>
    Public Class IkResult

        Public Property Success As Boolean
        Public Property Q As Double()
        Public Property PositionError As Double
        Public Property RotationError As Double
        Public Property Iterations As Integer
        Public Property Message As String

        Public ReadOnly Property TotalError As Double
            Get
                Return System.Math.Sqrt(PositionError * PositionError + RotationError * RotationError)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 数值逆运动学（阻尼最小二乘 / Levenberg-Marquardt 风格）。
    ''' </summary>
    ''' <remarks>
    ''' 六轴臂虽然有解析逆解，但解析式对限位、奇异位形与"部分关节不需要动"的处理很啰嗦；
    ''' 这里用数值雅可比（前向差分，每次 6 个 FK）迭代求解，规模只有 6×6，
    ''' 单次求解约 60 次 FK（每次 6 个矩阵级联），在 1 ms 量级，足够支撑 <c>movel</c>。
    '''
    ''' 姿态误差用世界系的旋转向量（轴角）表示，位置误差单位为米、姿态误差单位为弧度，
    ''' 二者在同一量级上加权，因此可以共用同一个阻尼系数。
    ''' </remarks>
    Public Module Kinematics

        ''' <summary>默认最大迭代次数。</summary>
        Public Const DefaultIterations As Integer = 60
        ''' <summary>默认收敛阈值（米 / 弧度）。</summary>
        Public Const DefaultTolerance As Double = 1.0E-4
        ''' <summary>雅可比差分的关节角步长。</summary>
        Private Const FiniteStep As Double = 1.0E-4
        ''' <summary>单次迭代的最大关节角增量。</summary>
        Private Const MaxStep As Double = 0.35

        ''' <summary>
        ''' 求解 6 维位姿逆解。
        ''' </summary>
        ''' <param name="assembly">装配体（提供 FK 与限位）</param>
        ''' <param name="target">目标 TCP 位姿</param>
        ''' <param name="seed">初始关节角（通常取当前关节角，避免大幅跳变）</param>
        ''' <param name="positionOnly">只约束位置（3 维），姿态自由</param>
        Public Function Solve(assembly As ArmAssembly, target As Pose3D, seed As Double(),
                              Optional positionOnly As Boolean = False,
                              Optional maxIterations As Integer = DefaultIterations,
                              Optional tolerance As Double = DefaultTolerance) As IkResult

            Dim n As Integer = assembly.JointCount
            Dim result As New IkResult()

            If n = 0 Then
                result.Message = "机械臂没有可动关节"
                result.Q = New Double() {}
                Return result
            End If

            Dim q(n - 1) As Double

            For i As Integer = 0 To n - 1
                Dim value As Double = 0.0

                If seed IsNot Nothing AndAlso i < seed.Length Then value = seed(i)

                q(i) = Clamp(value, assembly.Joints(i).Lower, assembly.Joints(i).Upper)
            Next

            Dim lower As Double() = assembly.LowerLimits
            Dim upper As Double() = assembly.UpperLimits
            Dim damping As Double = 0.02
            Dim positionError As Double = 0.0
            Dim rotationError As Double = 0.0
            Dim iterations As Integer = 0

            For iteration As Integer = 1 To maxIterations
                iterations = iteration

                Dim current As Pose3D = assembly.TcpPose(q)
                Dim positionDelta As Vector3 = target.Position - current.Position
                Dim rotationDelta As Vector3 = RotationVectorBetween(current.Rotation, target.Rotation)

                positionError = Vector3Math.Length(positionDelta)
                rotationError = Vector3Math.Length(rotationDelta)

                If positionError <= tolerance AndAlso (positionOnly OrElse rotationError <= tolerance) Then
                    result.Success = True
                    Exit For
                End If

                ' 数值雅可比：6 × n
                Dim jac As New Jacobian(6, n)

                For j As Integer = 0 To n - 1
                    Dim probe(n - 1) As Double
                    Array.Copy(q, probe, n)

                    Dim deltaStep As Double = FiniteStep

                    If probe(j) + deltaStep > upper(j) Then
                        deltaStep = -deltaStep
                    End If

                    probe(j) = probe(j) + deltaStep

                    Dim moved As Pose3D = assembly.TcpPose(probe)
                    Dim deltaPosition As Vector3 = (moved.Position - current.Position) * (1.0 / deltaStep)
                    Dim deltaRotation As Vector3 = RotationVectorBetween(current.Rotation, moved.Rotation)
                    deltaRotation = deltaRotation * (1.0 / deltaStep)

                    jac.Put(0, j, deltaPosition.x)
                    jac.Put(1, j, deltaPosition.y)
                    jac.Put(2, j, deltaPosition.z)

                    If Not positionOnly Then
                        jac.Put(3, j, deltaRotation.x)
                        jac.Put(4, j, deltaRotation.y)
                        jac.Put(5, j, deltaRotation.z)
                    End If
                Next

                ' 目标向量
                Dim residual(5) As Double

                residual(0) = positionDelta.x
                residual(1) = positionDelta.y
                residual(2) = positionDelta.z

                If Not positionOnly Then
                    residual(3) = rotationDelta.x
                    residual(4) = rotationDelta.y
                    residual(5) = rotationDelta.z
                End If

                ' (JᵀJ + λ²I) dq = Jᵀ e
                Dim rows As Integer = If(positionOnly, 3, 6)
                Dim a(n - 1, n - 1) As Double
                Dim b(n - 1) As Double

                For i As Integer = 0 To n - 1
                    For k As Integer = 0 To n - 1
                        Dim sum As Double = 0.0

                        For r As Integer = 0 To rows - 1
                            sum += jac.Value(r, i) * jac.Value(r, k)
                        Next

                        a(i, k) = sum
                    Next

                    Dim rhsValue As Double = 0.0

                    For r As Integer = 0 To rows - 1
                        rhsValue += jac.Value(r, i) * residual(r)
                    Next

                    b(i) = rhsValue
                    a(i, i) += damping * damping
                Next

                Dim stepVector As Double() = SolveLinear(a, b, n)

                If stepVector Is Nothing Then
                    damping *= 4.0

                    If damping > 10.0 Then
                        result.Message = "雅可比矩阵奇异，无法求解"
                        Exit For
                    End If

                    Continue For
                End If

                ' 限幅并更新
                Dim maxDelta As Double = 0.0

                For i As Integer = 0 To n - 1
                    maxDelta = System.Math.Max(maxDelta, System.Math.Abs(stepVector(i)))
                Next

                Dim scale As Double = If(maxDelta > MaxStep, MaxStep / maxDelta, 1.0)

                For i As Integer = 0 To n - 1
                    q(i) = Clamp(q(i) + stepVector(i) * scale, lower(i), upper(i))
                Next

                ' 阻尼自适应：误差下降就收紧，停滞就放宽
                damping = System.Math.Max(0.005, damping * 0.7)
            Next

            Dim final As Pose3D = assembly.TcpPose(q)

            result.Q = q
            result.Iterations = iterations
            result.PositionError = Vector3Math.Length(target.Position - final.Position)
            result.RotationError = Vector3Math.Length(RotationVectorBetween(final.Rotation, target.Rotation))

            If result.Success Then
                result.Message = "ok"
            ElseIf String.IsNullOrEmpty(result.Message) Then
                result.Message = "无法到达目标位姿"
            End If

            Return result
        End Function

        ''' <summary>只约束位置的逆解（笛卡尔直线插补的中途点）。</summary>
        Public Function SolvePosition(assembly As ArmAssembly, position As Vector3, seed As Double(),
                                      Optional maxIterations As Integer = DefaultIterations,
                                      Optional tolerance As Double = DefaultTolerance) As IkResult
            Dim target As New Pose3D(position, assembly.TcpPose(seed).Rotation)

            Return Solve(assembly, target, seed, positionOnly:=True,
                         maxIterations:=maxIterations, tolerance:=tolerance)
        End Function

        ''' <summary>把当前姿态旋转到 <paramref name="target"/> 所需的世界系旋转向量。</summary>
        Private Function RotationVectorBetween(current As Quaternion, target As Quaternion) As Vector3
            Dim delta As Quaternion = Quaternion.Compose(target, current.Inverse()).Normalize()
            Dim axis As Vector3 = New Vector3(0, 1, 0)
            Dim angle As Double = 0.0

            Call delta.ToAxisAngle(axis, angle)

            Return axis * angle
        End Function

        Private Function Clamp(value As Double, lower As Double, upper As Double) As Double
            If value < lower Then Return lower
            If value > upper Then Return upper

            Return value
        End Function

        ''' <summary>行主序的稠密矩阵（求解器内部使用）。</summary>
        Private Class Jacobian

            Private ReadOnly _data As Double(,)
            Private ReadOnly _rows As Integer
            Private ReadOnly _cols As Integer

            Sub New(rows As Integer, cols As Integer)
                _rows = rows
                _cols = cols
                ReDim _data(rows - 1, cols - 1)
            End Sub

            Public Sub Put(row As Integer, col As Integer, value As Double)
                _data(row, col) = value
            End Sub

            Public Function Value(row As Integer, col As Integer) As Double
                Return _data(row, col)
            End Function
        End Class

        ''' <summary>高斯-约当消元求解 n×n 线性方程组（部分主元）。</summary>
        Private Function SolveLinear(a As Double(,), b As Double(), n As Integer) As Double()
            Dim m(n - 1, n) As Double

            For i As Integer = 0 To n - 1
                For j As Integer = 0 To n - 1
                    m(i, j) = a(i, j)
                Next

                m(i, n) = b(i)
            Next

            For col As Integer = 0 To n - 1
                Dim pivot As Integer = col

                For r As Integer = col + 1 To n - 1
                    If System.Math.Abs(m(r, col)) > System.Math.Abs(m(pivot, col)) Then
                        pivot = r
                    End If
                Next

                If System.Math.Abs(m(pivot, col)) < 1.0E-12 Then Return Nothing

                If pivot <> col Then
                    For j As Integer = col To n
                        Dim temp As Double = m(col, j)
                        m(col, j) = m(pivot, j)
                        m(pivot, j) = temp
                    Next
                End If

                Dim diagonal As Double = m(col, col)

                For j As Integer = col To n
                    m(col, j) /= diagonal
                Next

                For r As Integer = 0 To n - 1
                    If r = col Then Continue For

                    Dim factor As Double = m(r, col)

                    If factor = 0.0 Then Continue For

                    For j As Integer = col To n
                        m(r, j) -= factor * m(col, j)
                    Next
                Next
            Next

            Dim x(n - 1) As Double

            For i As Integer = 0 To n - 1
                x(i) = m(i, n)
            Next

            Return x
        End Function

    End Module

End Namespace
