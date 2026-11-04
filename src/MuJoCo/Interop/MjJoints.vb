Imports Eng = Microsoft.VisualBasic.Engine.Physics
Imports EngJoints = Microsoft.VisualBasic.Engine.Physics.Joints3D

Namespace Interop.Joints3D

    ''' <summary>MuJoCo 场景层的约束契约。</summary>
    Public Interface IConstraint3D
        Sub SolveVelocity(dt As Double)
        Sub SolvePosition(dt As Double)
    End Interface

    ''' <summary>一维关节自由度契约，供伺服绑定。</summary>
    Public Interface IJointDof3D
        ReadOnly Property A As RigidBody3D
        ReadOnly Property B As RigidBody3D
        ReadOnly Property WorldAxis As Vector3
        ReadOnly Property Position As Double
        ReadOnly Property Speed As Double
        ReadOnly Property LowerLimit As Double
        ReadOnly Property UpperLimit As Double
        Sub ApplyControlForce(value As Double)
        Sub SetZeroReference()
    End Interface

    Public Enum ServoMode3D
        Disabled = 0
        Position = 1
        Velocity = 2
        Force = 3
    End Enum

    ''' <summary>绕轴铰链关节。</summary>
    Public Class RevoluteJoint3D
        Implements IConstraint3D, IJointDof3D

        Private ReadOnly _inner As EngJoints.RevoluteJoint3D
        Friend ReadOnly EngineConstraint As EngJoints.IConstraint3D
        Friend ReadOnly EngineDof As EngJoints.IJointDof3D

        Public Sub New(a As RigidBody3D, b As RigidBody3D,
                       anchorA As Vector3, anchorB As Vector3,
                       axisA As Vector3, axisB As Vector3,
                       Optional lowerLimit As Double = -System.Math.PI,
                       Optional upperLimit As Double = System.Math.PI,
                       Optional label As String = Nothing)

            _inner = New EngJoints.RevoluteJoint3D(
                If(a Is Nothing, Nothing, a.EngineBody),
                If(b Is Nothing, Nothing, b.EngineBody),
                MjInterop.V(anchorA), MjInterop.V(anchorB),
                MjInterop.V(axisA), MjInterop.V(axisB),
                lowerLimit, upperLimit, label)

            EngineConstraint = _inner
            EngineDof = _inner
        End Sub

        Public ReadOnly Property A As RigidBody3D Implements IJointDof3D.A
            Get
                Return MjInterop.WrapBody(_inner.A)
            End Get
        End Property

        Public ReadOnly Property B As RigidBody3D Implements IJointDof3D.B
            Get
                Return MjInterop.WrapBody(_inner.B)
            End Get
        End Property

        Public ReadOnly Property WorldAxis As Vector3 Implements IJointDof3D.WorldAxis
            Get
                Return MjInterop.V(_inner.WorldAxis)
            End Get
        End Property

        Public ReadOnly Property Position As Double Implements IJointDof3D.Position
            Get
                Return _inner.Position
            End Get
        End Property

        Public ReadOnly Property Speed As Double Implements IJointDof3D.Speed
            Get
                Return _inner.Speed
            End Get
        End Property

        Public Property LowerLimit As Double Implements IJointDof3D.LowerLimit
            Get
                Return _inner.LowerLimit
            End Get
            Set(value As Double)
                _inner.LowerLimit = value
            End Set
        End Property

        Public Property UpperLimit As Double Implements IJointDof3D.UpperLimit
            Get
                Return _inner.UpperLimit
            End Get
            Set(value As Double)
                _inner.UpperLimit = value
            End Set
        End Property

        Public Property Baumgarte As Double
            Get
                Return _inner.Baumgarte
            End Get
            Set(value As Double)
                _inner.Baumgarte = value
            End Set
        End Property

        Public Property LimitBaumgarte As Double
            Get
                Return _inner.LimitBaumgarte
            End Get
            Set(value As Double)
                _inner.LimitBaumgarte = value
            End Set
        End Property

        Public Property EnableLimit As Boolean
            Get
                Return _inner.EnableLimit
            End Get
            Set(value As Boolean)
                _inner.EnableLimit = value
            End Set
        End Property

        Public Property Damping As Double
            Get
                Return _inner.Damping
            End Get
            Set(value As Double)
                _inner.Damping = value
            End Set
        End Property

        Public Property Label As String
            Get
                Return _inner.Label
            End Get
            Set(value As String)
                _inner.Label = value
            End Set
        End Property

        Public Sub ApplyControlForce(value As Double) Implements IJointDof3D.ApplyControlForce
            Call _inner.ApplyControlForce(value)
        End Sub

        Public Sub SetZeroReference() Implements IJointDof3D.SetZeroReference
            Call _inner.SetZeroReference()
        End Sub

        Public Sub SolveVelocity(dt As Double) Implements IConstraint3D.SolveVelocity
            Call _inner.SolveVelocity(dt)
        End Sub

        Public Sub SolvePosition(dt As Double) Implements IConstraint3D.SolvePosition
            Call _inner.SolvePosition(dt)
        End Sub
    End Class

    ''' <summary>沿轴滑台关节。</summary>
    Public Class PrismaticJoint3D
        Implements IConstraint3D, IJointDof3D

        Private ReadOnly _inner As EngJoints.PrismaticJoint3D
        Friend ReadOnly EngineConstraint As EngJoints.IConstraint3D
        Friend ReadOnly EngineDof As EngJoints.IJointDof3D

        Public Sub New(a As RigidBody3D, b As RigidBody3D,
                       anchorA As Vector3, anchorB As Vector3,
                       localAxis As Vector3,
                       Optional lowerLimit As Double = -0.05,
                       Optional upperLimit As Double = 0.05,
                       Optional label As String = Nothing)

            _inner = New EngJoints.PrismaticJoint3D(
                If(a Is Nothing, Nothing, a.EngineBody),
                If(b Is Nothing, Nothing, b.EngineBody),
                MjInterop.V(anchorA), MjInterop.V(anchorB),
                MjInterop.V(localAxis), lowerLimit, upperLimit, label)

            EngineConstraint = _inner
            EngineDof = _inner
        End Sub

        Public ReadOnly Property A As RigidBody3D Implements IJointDof3D.A
            Get
                Return MjInterop.WrapBody(_inner.A)
            End Get
        End Property

        Public ReadOnly Property B As RigidBody3D Implements IJointDof3D.B
            Get
                Return MjInterop.WrapBody(_inner.B)
            End Get
        End Property

        Public ReadOnly Property WorldAxis As Vector3 Implements IJointDof3D.WorldAxis
            Get
                Return MjInterop.V(_inner.WorldAxis)
            End Get
        End Property

        Public ReadOnly Property Position As Double Implements IJointDof3D.Position
            Get
                Return _inner.Position
            End Get
        End Property

        Public ReadOnly Property Speed As Double Implements IJointDof3D.Speed
            Get
                Return _inner.Speed
            End Get
        End Property

        Public Property LowerLimit As Double Implements IJointDof3D.LowerLimit
            Get
                Return _inner.LowerLimit
            End Get
            Set(value As Double)
                _inner.LowerLimit = value
            End Set
        End Property

        Public Property UpperLimit As Double Implements IJointDof3D.UpperLimit
            Get
                Return _inner.UpperLimit
            End Get
            Set(value As Double)
                _inner.UpperLimit = value
            End Set
        End Property

        Public Property AnchorA As Vector3
            Get
                Return MjInterop.V(_inner.AnchorA)
            End Get
            Set(value As Vector3)
                _inner.AnchorA = MjInterop.V(value)
            End Set
        End Property

        Public Property AnchorB As Vector3
            Get
                Return MjInterop.V(_inner.AnchorB)
            End Get
            Set(value As Vector3)
                _inner.AnchorB = MjInterop.V(value)
            End Set
        End Property

        Public Property LocalAxis As Vector3
            Get
                Return MjInterop.V(_inner.LocalAxis)
            End Get
            Set(value As Vector3)
                _inner.LocalAxis = MjInterop.V(value)
            End Set
        End Property

        Public Property Baumgarte As Double
            Get
                Return _inner.Baumgarte
            End Get
            Set(value As Double)
                _inner.Baumgarte = value
            End Set
        End Property

        Public Property LimitBaumgarte As Double
            Get
                Return _inner.LimitBaumgarte
            End Get
            Set(value As Double)
                _inner.LimitBaumgarte = value
            End Set
        End Property

        Public Property Label As String
            Get
                Return _inner.Label
            End Get
            Set(value As String)
                _inner.Label = value
            End Set
        End Property

        Public Sub ApplyControlForce(value As Double) Implements IJointDof3D.ApplyControlForce
            Call _inner.ApplyControlForce(value)
        End Sub

        Public Sub SetZeroReference() Implements IJointDof3D.SetZeroReference
            Call _inner.SetZeroReference()
        End Sub

        Public Sub SolveVelocity(dt As Double) Implements IConstraint3D.SolveVelocity
            Call _inner.SolveVelocity(dt)
        End Sub

        Public Sub SolvePosition(dt As Double) Implements IConstraint3D.SolvePosition
            Call _inner.SolvePosition(dt)
        End Sub
    End Class

    ''' <summary>关节伺服：位置/速度/力矩三态。</summary>
    Public Class JointServo3D

        Private ReadOnly _inner As EngJoints.JointServo3D
        Friend ReadOnly EngineHook As Eng.IStepHook

        Public Sub New(dof As IJointDof3D,
                       Optional mode As ServoMode3D = ServoMode3D.Disabled)

            _inner = New EngJoints.JointServo3D(MjInterop.DofOf(dof),
                                                CType(mode, EngJoints.ServoMode3D))
            EngineHook = _inner
        End Sub

        Public Property Mode As ServoMode3D
            Get
                Return CType(_inner.Mode, ServoMode3D)
            End Get
            Set(value As ServoMode3D)
                _inner.Mode = CType(value, EngJoints.ServoMode3D)
            End Set
        End Property

        Public Property Target As Double
            Get
                Return _inner.Target
            End Get
            Set(value As Double)
                _inner.Target = value
            End Set
        End Property

        Public Property Stiffness As Double
            Get
                Return _inner.Stiffness
            End Get
            Set(value As Double)
                _inner.Stiffness = value
            End Set
        End Property

        Public Property Damping As Double
            Get
                Return _inner.Damping
            End Get
            Set(value As Double)
                _inner.Damping = value
            End Set
        End Property

        Public Property MaxVelocity As Double
            Get
                Return _inner.MaxVelocity
            End Get
            Set(value As Double)
                _inner.MaxVelocity = value
            End Set
        End Property

        Public Property MaxForce As Double
            Get
                Return _inner.MaxForce
            End Get
            Set(value As Double)
                _inner.MaxForce = value
            End Set
        End Property

        Public Property Tolerance As Double
            Get
                Return _inner.Tolerance
            End Get
            Set(value As Double)
                _inner.Tolerance = value
            End Set
        End Property

        Public Property Label As String
            Get
                Return _inner.Label
            End Get
            Set(value As String)
                _inner.Label = value
            End Set
        End Property

        Public Property ForceDamping As Double
            Get
                Return _inner.ForceDamping
            End Get
            Set(value As Double)
                _inner.ForceDamping = value
            End Set
        End Property

        Public Property LastForce As Double
            Get
                Return _inner.LastForce
            End Get
            Set(value As Double)
                _inner.LastForce = value
            End Set
        End Property

        Public Property Enabled As Boolean
            Get
                Return _inner.Enabled
            End Get
            Set(value As Boolean)
                _inner.Enabled = value
            End Set
        End Property

        Public Property FeedForward As Double
            Get
                Return _inner.FeedForward
            End Get
            Set(value As Double)
                _inner.FeedForward = value
            End Set
        End Property

        Public ReadOnly Property CurrentPosition As Double
            Get
                Return _inner.CurrentPosition
            End Get
        End Property

        Public ReadOnly Property CurrentSpeed As Double
            Get
                Return _inner.CurrentSpeed
            End Get
        End Property

        Public ReadOnly Property IsAtTarget As Boolean
            Get
                Return _inner.IsAtTarget
            End Get
        End Property

        Public Sub Reset()
            Call _inner.Reset()
        End Sub

        Public Sub MoveTo(position As Double)
            Call _inner.MoveTo(position)
        End Sub

        Public Sub ApplyForce(force As Double)
            Call _inner.ApplyForce(force)
        End Sub
    End Class

    ''' <summary>线性弹簧约束（一端可为世界锚点）。</summary>
    Public Class SpringJoint3D
        Implements IConstraint3D

        Private ReadOnly _inner As EngJoints.SpringJoint3D
        Friend ReadOnly EngineConstraint As EngJoints.IConstraint3D

        Public Sub New()
            _inner = New EngJoints.SpringJoint3D()
            EngineConstraint = _inner
        End Sub

        Public Sub New(a As RigidBody3D, b As RigidBody3D,
                       anchorA As Vector3, anchorB As Vector3,
                       Optional restLength As Double = 0.1,
                       Optional stiffness As Double = 300.0,
                       Optional damping As Double = 30.0)

            _inner = New EngJoints.SpringJoint3D(
                If(a Is Nothing, Nothing, a.EngineBody),
                If(b Is Nothing, Nothing, b.EngineBody),
                MjInterop.V(anchorA), MjInterop.V(anchorB),
                restLength, stiffness, damping)

            EngineConstraint = _inner
        End Sub

        Public Property Label As String
            Get
                Return _inner.Label
            End Get
            Set(value As String)
                _inner.Label = value
            End Set
        End Property

        Public Property MaxForce As Double
            Get
                Return _inner.MaxForce
            End Get
            Set(value As Double)
                _inner.MaxForce = value
            End Set
        End Property

        Public Property Bidirectional As Boolean
            Get
                Return _inner.Bidirectional
            End Get
            Set(value As Boolean)
                _inner.Bidirectional = value
            End Set
        End Property

        Public Property ShearStiffness As Double
            Get
                Return _inner.ShearStiffness
            End Get
            Set(value As Double)
                _inner.ShearStiffness = value
            End Set
        End Property

        Public Property ShearDamping As Double
            Get
                Return _inner.ShearDamping
            End Get
            Set(value As Double)
                _inner.ShearDamping = value
            End Set
        End Property

        Public Property ShearSlop As Double
            Get
                Return _inner.ShearSlop
            End Get
            Set(value As Double)
                _inner.ShearSlop = value
            End Set
        End Property

        Public Property Compression As Double
            Get
                Return _inner.Compression
            End Get
            Set(value As Double)
                _inner.Compression = value
            End Set
        End Property

        Public Property CurrentForce As Double
            Get
                Return _inner.CurrentForce
            End Get
            Set(value As Double)
                _inner.CurrentForce = value
            End Set
        End Property

        Public Property Stiffness As Double
            Get
                Return _inner.Stiffness
            End Get
            Set(value As Double)
                _inner.Stiffness = value
            End Set
        End Property

        Public Property Damping As Double
            Get
                Return _inner.Damping
            End Get
            Set(value As Double)
                _inner.Damping = value
            End Set
        End Property

        Public Property RestLength As Double
            Get
                Return _inner.RestLength
            End Get
            Set(value As Double)
                _inner.RestLength = value
            End Set
        End Property

        Public Property Enabled As Boolean
            Get
                Return _inner.Enabled
            End Get
            Set(value As Boolean)
                _inner.Enabled = value
            End Set
        End Property

        Public ReadOnly Property WorldAnchorA As Vector3
            Get
                Return MjInterop.V(_inner.WorldAnchorA)
            End Get
        End Property

        Public ReadOnly Property WorldAnchorB As Vector3
            Get
                Return MjInterop.V(_inner.WorldAnchorB)
            End Get
        End Property

        Public ReadOnly Property CurrentLength As Double
            Get
                Return _inner.CurrentLength
            End Get
        End Property

        Public ReadOnly Property Direction As Vector3
            Get
                Return MjInterop.V(_inner.Direction)
            End Get
        End Property

        Public Sub SolveVelocity(dt As Double) Implements IConstraint3D.SolveVelocity
            Call _inner.SolveVelocity(dt)
        End Sub

        Public Sub SolvePosition(dt As Double) Implements IConstraint3D.SolvePosition
            Call _inner.SolvePosition(dt)
        End Sub
    End Class

End Namespace
