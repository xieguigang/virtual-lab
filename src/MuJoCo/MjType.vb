''' <summary>
''' 与 MuJoCo 的 <c>include/mujoco/mjtype.h</c> 对应的托管类型层。
''' </summary>
''' <remarks>
''' <para>
''' 数值与 C 版保持一致，方便把 C 源码里的常量、索引和分支直接搬过来：
''' <c>mjtNum</c> 映射为 <see cref="Double"/>，<c>mjtSize</c> 映射为 <see cref="Integer"/>
''' （托管层的数组下标是 32 位，模型规模远小于 2^31），<c>mjtByte</c> 映射为
''' <see cref="Boolean"/>。
''' </para>
''' <para>
''' 本模块只放枚举与常量，不放任何逻辑；逻辑分散在 <c>MjModel.vb</c>、
''' <c>MjData.vb</c>、<c>MjSpec.vb</c> 与 <c>Runtime/</c> 目录下。
''' </para>
''' </remarks>
Public Module MjType

    ' /********************************************************************************/
    '  数值常量（对应 mujoco.h 的顶部宏与 mjmodel.h 的维度宏）
    ' /********************************************************************************/

    Public Const mjPI As Double = 3.14159265358979323846
    Public Const mjMAXVAL As Double = 1.0E+10
    Public Const mjMINVAL As Double = 1.0E-15
    ''' <summary>solref 的长度</summary>
    Public Const mjNREF As Integer = 2
    ''' <summary>solimp 的长度</summary>
    Public Const mjNIMP As Integer = 5
    ''' <summary>equality 约束的 data 长度</summary>
    Public Const mjNEQDATA As Integer = 11
    ''' <summary>执行器 dynprm 的长度</summary>
    Public Const mjNDYN As Integer = 10
    ''' <summary>执行器 gainprm 的长度</summary>
    Public Const mjNGAIN As Integer = 10
    ''' <summary>执行器 biasprm 的长度</summary>
    Public Const mjNBIAS As Integer = 10
    ''' <summary>geom 的摩擦分量个数（滑动、扭转、滚动）</summary>
    Public Const mjNFRICTION As Integer = 3

    ' /********************************************************************************/
    '  对象类型（mjtObj）
    ' /********************************************************************************/

    ''' <summary>对象类型，用于 <c>mj_name2id</c> / <c>mj_id2name</c>。</summary>
    Public Enum mjtObj
        mjOBJ_UNKNOWN = 0
        mjOBJ_BODY = 1
        mjOBJ_XBODY = 2
        mjOBJ_JOINT = 3
        mjOBJ_DOF = 4
        mjOBJ_GEOM = 5
        mjOBJ_SITE = 6
        mjOBJ_CAMERA = 7
        mjOBJ_LIGHT = 8
        mjOBJ_FLEX = 9
        mjOBJ_MESH = 10
        mjOBJ_SKIN = 11
        mjOBJ_HFIELD = 12
        mjOBJ_TEXTURE = 13
        mjOBJ_MATERIAL = 14
        mjOBJ_PAIR = 15
        mjOBJ_EXCLUDE = 16
        mjOBJ_EQUALITY = 17
        mjOBJ_TENDON = 18
        mjOBJ_ACTUATOR = 19
        mjOBJ_SENSOR = 20
        mjOBJ_NUMERIC = 21
        mjOBJ_TEXT = 22
        mjOBJ_TUPLE = 23
        mjOBJ_KEY = 24
        mjOBJ_PLUGIN = 25

        ''' <summary>对象类型的总个数</summary>
        mjNOBJECT = 26

        ' spec 层的元元素，不出现在 mjModel 之中
        mjOBJ_FRAME = 100
        mjOBJ_DEFAULT = 101
        mjOBJ_MODEL = 102
    End Enum

    ' /********************************************************************************/
    '  关节类型（mjtJoint）
    ' /********************************************************************************/

    ''' <summary>关节类型。括号里是它占用的 qpos / qvel 维数。</summary>
    Public Enum mjtJoint
        ''' <summary>全局的自由浮动（7 个坐标 / 6 个速度）</summary>
        mjJNT_FREE = 0
        ''' <summary>球铰（4 / 3）</summary>
        mjJNT_BALL = 1
        ''' <summary>滑动副（1 / 1）</summary>
        mjJNT_SLIDE = 2
        ''' <summary>铰链（1 / 1）</summary>
        mjJNT_HINGE = 3
    End Enum

    ''' <summary>关节类型占用的广义坐标维数</summary>
    Public Function mj_jntQposDim(type As mjtJoint) As Integer
        Select Case type
            Case mjtJoint.mjJNT_FREE : Return 7
            Case mjtJoint.mjJNT_BALL : Return 4
            Case Else : Return 1
        End Select
    End Function

    ''' <summary>关节类型占用的自由度维数</summary>
    Public Function mj_jntDofDim(type As mjtJoint) As Integer
        Select Case type
            Case mjtJoint.mjJNT_FREE : Return 6
            Case mjtJoint.mjJNT_BALL : Return 3
            Case Else : Return 1
        End Select
    End Function

    ' /********************************************************************************/
    '  几何体类型（mjtGeom）
    ' /********************************************************************************/

    ''' <summary>几何体类型。</summary>
    Public Enum mjtGeom
        mjGEOM_PLANE = 0
        mjGEOM_HFIELD = 1
        mjGEOM_SPHERE = 2
        mjGEOM_CAPSULE = 3
        mjGEOM_ELLIPSOID = 4
        mjGEOM_CYLINDER = 5
        mjGEOM_BOX = 6
        mjGEOM_MESH = 7
        mjGEOM_SDF = 8

        ' 仅用于渲染的类型
        mjGEOM_ARROW = 100
        mjGEOM_ARROW1 = 101
        mjGEOM_ARROW2 = 102
        mjGEOM_LINE = 103
        mjGEOM_FLEX = 104
        mjGEOM_SKIN = 105
        mjGEOM_LABEL = 106
        mjGEOM_TRIANGLE = 107

        mjGEOM_NONE = 1001
    End Enum

    ' /********************************************************************************/
    '  执行器（mjtTrn / mjtDyn / mjtGain / mjtBias / mjtActuator）
    ' /********************************************************************************/

    ''' <summary>执行器的传动类型（作用在什么对象上）。</summary>
    Public Enum mjtTrn
        mjTRN_JOINT = 0
        mjTRN_JOINTINPARENT = 1
        mjTRN_SLIDERCRANK = 2
        mjTRN_TENDON = 3
        mjTRN_SITE = 4
        mjTRN_BODY = 5
        mjTRN_SO3 = 6

        mjTRN_UNDEFINED = 1000
    End Enum

    ''' <summary>执行器的内部状态（激活变量）类型。</summary>
    Public Enum mjtDyn
        mjDYN_NONE = 0
        mjDYN_INTEGRATOR = 1
        mjDYN_FILTER = 2
        mjDYN_FILTEREXACT = 3
        mjDYN_MUSCLE = 4
        mjDYN_DCMOTOR = 5
        mjDYN_PID = 6
        mjDYN_USER = 7
    End Enum

    ''' <summary>执行器的增益类型。</summary>
    Public Enum mjtGain
        mjGAIN_FIXED = 0
        mjGAIN_AFFINE = 1
        mjGAIN_MUSCLE = 2
        mjGAIN_DCMOTOR = 3
        mjGAIN_SO3 = 4
        mjGAIN_PID = 5
        mjGAIN_USER = 6
    End Enum

    ''' <summary>执行器的偏置类型。</summary>
    Public Enum mjtBias
        mjBIAS_NONE = 0
        mjBIAS_AFFINE = 1
        mjBIAS_MUSCLE = 2
        mjBIAS_DCMOTOR = 3
        mjBIAS_SO3 = 4
        mjBIAS_USER = 5
    End Enum

    ''' <summary>执行器的语义类型（mjspec.h 的 mjtActuator）。</summary>
    Public Enum mjtActuator
        mjACTUATOR_GENERAL = 0
        mjACTUATOR_MOTOR = 1
        mjACTUATOR_POSITION = 2
        mjACTUATOR_VELOCITY = 3
        mjACTUATOR_INTVELOCITY = 4
        mjACTUATOR_DAMPER = 5
        mjACTUATOR_CYLINDER = 6
        mjACTUATOR_MUSCLE = 7
        mjACTUATOR_ADHESION = 8
        mjACTUATOR_PID = 9
        mjACTUATOR_ORIENTATION = 10
        mjACTUATOR_DCMOTOR = 11
    End Enum

    ' /********************************************************************************/
    '  求解器选项（mjtIntegrator / mjtCone / mjtJacobian / mjtSolver）
    ' /********************************************************************************/

    ''' <summary>积分器类型。</summary>
    Public Enum mjtIntegrator
        mjINT_EULER = 0
        mjINT_RK4 = 1
        mjINT_IMPLICIT = 2
        mjINT_IMPLICITFAST = 3
        mjINT_DISCRETE = 4
    End Enum

    ''' <summary>摩擦锥类型。</summary>
    Public Enum mjtCone
        mjCONE_PYRAMIDAL = 0
        mjCONE_ELLIPTIC = 1
    End Enum

    ''' <summary>雅可比矩阵的存储类型。</summary>
    Public Enum mjtJacobian
        mjJAC_DENSE = 0
        mjJAC_SPARSE = 1
        mjJAC_AUTO = 2
    End Enum

    ''' <summary>约束求解器类型。</summary>
    Public Enum mjtSolver
        mjSOL_PGS = 0
        mjSOL_CG = 1
        mjSOL_NEWTON = 2
    End Enum

    ' /********************************************************************************/
    '  等式约束（mjtEq）
    ' /********************************************************************************/

    ''' <summary>等式约束的类型。</summary>
    Public Enum mjtEq
        mjEQ_CONNECT = 0
        mjEQ_WELD = 1
        mjEQ_JOINT = 2
        mjEQ_TENDON = 3
        mjEQ_FLEX = 4
        mjEQ_FLEXVERT = 5
        mjEQ_FLEXSTRAIN = 6
        mjEQ_DISTANCE = 7
    End Enum

    ' /********************************************************************************/
    '  腱（mjtTendon / mjtWrap）
    ' /********************************************************************************/

    ''' <summary>腱的空间类型。</summary>
    Public Enum mjtTendon
        mjTENDON_FIXED = 0
        mjTENDON_SPATIAL = 1
    End Enum

    ''' <summary>腱的绕行几何类型。</summary>
    Public Enum mjtWrap
        mjWRAP_NONE = 0
        mjWRAP_JOINT = 1
        mjWRAP_PULLEY = 2
        mjWRAP_SITE = 3
        mjWRAP_SPHERE = 4
        mjWRAP_CYLINDER = 5
    End Enum

    ' /********************************************************************************/
    '  传感器（mjtSensor / mjtStage / mjtDataType）
    ' /********************************************************************************/

    ''' <summary>传感器类型，数值与 mjtype.h 一致。</summary>
    Public Enum mjtSensor
        mjSENS_TOUCH = 0
        mjSENS_ACCELEROMETER = 1
        mjSENS_VELOCIMETER = 2
        mjSENS_GYRO = 3
        mjSENS_FORCE = 4
        mjSENS_TORQUE = 5
        mjSENS_MAGNETOMETER = 6
        mjSENS_RANGEFINDER = 7
        mjSENS_CAMPROJECTION = 8

        mjSENS_JOINTPOS = 9
        mjSENS_JOINTVEL = 10
        mjSENS_TENDONPOS = 11
        mjSENS_TENDONVEL = 12
        mjSENS_ACTUATORPOS = 13
        mjSENS_ACTUATORVEL = 14
        mjSENS_ACTUATORFRC = 15
        mjSENS_JOINTACTFRC = 16
        mjSENS_TENDONACTFRC = 17

        mjSENS_BALLQUAT = 18
        mjSENS_BALLANGVEL = 19

        mjSENS_JOINTLIMITPOS = 20
        mjSENS_JOINTLIMITVEL = 21
        mjSENS_JOINTLIMITFRC = 22
        mjSENS_TENDONLIMITPOS = 23
        mjSENS_TENDONLIMITVEL = 24
        mjSENS_TENDONLIMITFRC = 25

        mjSENS_FRAMEPOS = 26
        mjSENS_FRAMEQUAT = 27
        mjSENS_FRAMEXAXIS = 28
        mjSENS_FRAMEYAXIS = 29
        mjSENS_FRAMEZAXIS = 30
        mjSENS_FRAMELINVEL = 31
        mjSENS_FRAMEANGVEL = 32
        mjSENS_FRAMELINACC = 33
        mjSENS_FRAMEANGACC = 34

        mjSENS_SUBTREECOM = 35
        mjSENS_SUBTREELINVEL = 36
        mjSENS_SUBTREEANGMOM = 37

        mjSENS_INSIDESITE = 38
        mjSENS_GEOMDIST = 39
        mjSENS_GEOMNORMAL = 40
        mjSENS_GEOMFROMTO = 41

        mjSENS_CONTACT = 42

        mjSENS_E_POTENTIAL = 43
        mjSENS_E_KINETIC = 44
        mjSENS_CLOCK = 45

        mjSENS_TACTILE = 46
        mjSENS_PLUGIN = 47
        mjSENS_USER = 48
    End Enum

    ''' <summary>传感器的计算阶段。</summary>
    Public Enum mjtStage
        mjSTAGE_NONE = 0
        mjSTAGE_POS = 1
        mjSTAGE_VEL = 2
        mjSTAGE_ACC = 3
    End Enum

    ''' <summary>传感器的数据类型。</summary>
    Public Enum mjtDataType
        mjDATATYPE_REAL = 0
        mjDATATYPE_POSITIVE = 1
        mjDATATYPE_AXIS = 2
        mjDATATYPE_QUATERNION = 3
    End Enum

    ' /********************************************************************************/
    '  状态快照位（mjSTATE_*）
    ' /********************************************************************************/

    ''' <summary>
    ''' <c>mj_getState</c> / <c>mj_setState</c> 的状态选择位，与 mjtype.h 的
    ''' <c>mjSTATE_*</c> 一一对应。
    ''' </summary>
    <Flags>
    Public Enum MjStateSig
        mjSTATE_TIME = 1
        mjSTATE_QPOS = 2
        mjSTATE_QVEL = 4
        mjSTATE_ACT = 8
        mjSTATE_HISTORY = 16
        mjSTATE_WARMSTART = 32
        mjSTATE_CTRL = 64
        mjSTATE_QFRC_APPLIED = 128
        mjSTATE_XFRC_APPLIED = 256
        mjSTATE_EQ_ACTIVE = 512
        mjSTATE_MOCAP_POS = 1024
        mjSTATE_MOCAP_QUAT = 2048
        mjSTATE_USERDATA = 4096
        mjSTATE_PLUGIN = 8192
        mjSTATE_PLASTIC = 16384

        ''' <summary>qpos/qvel/act/history/warmstart</summary>
        mjSTATE_PHYSICS = 2 + 4 + 8 + 16 + 32
        ''' <summary>物理状态 + 时间 + 控制量 + 外力 + 等式开关 + mocap + user</summary>
        mjSTATE_FULLPHYSICS = 2 + 4 + 8 + 16 + 32 + 1 + 64 + 128 + 256 + 512 + 1024 + 2048 + 4096 + 8192 + 16384
        ''' <summary>用户可见的状态（不含纯物理量）</summary>
        mjSTATE_USER = 1 + 64 + 128 + 256 + 512 + 1024 + 2048 + 4096 + 8192
        ''' <summary>积分所需的最小状态集合</summary>
        mjSTATE_INTEGRATION = 1 + 2 + 4 + 8 + 16 + 32 + 64 + 128 + 256 + 512 + 1024 + 2048 + 4096 + 8192 + 16384
    End Enum

    ' /********************************************************************************/
    '  警告与计时器
    ' /********************************************************************************/

    ''' <summary>mjData 的警告位。</summary>
    Public Enum mjtWarning
        mjWARN_BADQPOS = 0
        mjWARN_BADQVEL = 1
        mjWARN_BADQACC = 2
        mjWARN_BADCTRL = 3
        mjWARN_INERTIA = 4
        mjWARN_CONTACTFULL = 5
        mjWARN_CNSTRFULL = 6
        mjWARN_VGEOMFULL = 7
        mjWARN_BADQACC_LEVEL = 8
        mjNWARNING = 9
    End Enum

    ''' <summary>mjData 的计时器位。</summary>
    Public Enum mjtTimer
        mjTIMER_STEP = 0
        mjTIMER_FORWARD = 1
        mjTIMER_INVERSE = 2
        mjTIMER_POSITION = 3
        mjTIMER_VELOCITY = 4
        mjTIMER_ACTUATION = 5
        mjTIMER_CONSTRAINT = 6
        mjTIMER_ADVANCE = 7
        mjTIMER_POS_KINEMATICS = 8
        mjTIMER_POS_INERTIA = 9
        mjTIMER_POS_COLLISION = 10
        mjTIMER_POS_MAKE = 11
        mjTIMER_POS_PROJECT = 12
        mjTIMER_COL_BROAD = 13
        mjTIMER_COL_NARROW = 14
        mjNTIMER = 15
    End Enum

    ''' <summary>约束行的类型（用于 <c>mjData.efc_type</c> 与调试输出）。</summary>
    Public Enum mjtConstraint
        mjCNSTR_EQUALITY = 0
        mjCNSTR_FRICTION_DOF = 1
        mjCNSTR_FRICTION_TENDON = 2
        mjCNSTR_LIMIT_JOINT = 3
        mjCNSTR_LIMIT_TENDON = 4
        mjCNSTR_CONTACT_FRICTIONLESS = 5
        mjCNSTR_CONTACT_PYRAMIDAL = 6
        mjCNSTR_CONTACT_ELLIPTIC = 7
    End Enum

    ' /********************************************************************************/
    '  小工具
    ' /********************************************************************************/

    ''' <summary>把 <c>mjtObj</c> 转成用于日志的名字</summary>
    Public Function mj_objName(type As mjtObj) As String
        Select Case type
            Case mjtObj.mjOBJ_BODY : Return "body"
            Case mjtObj.mjOBJ_JOINT : Return "joint"
            Case mjtObj.mjOBJ_DOF : Return "dof"
            Case mjtObj.mjOBJ_GEOM : Return "geom"
            Case mjtObj.mjOBJ_SITE : Return "site"
            Case mjtObj.mjOBJ_CAMERA : Return "camera"
            Case mjtObj.mjOBJ_LIGHT : Return "light"
            Case mjtObj.mjOBJ_MESH : Return "mesh"
            Case mjtObj.mjOBJ_MATERIAL : Return "material"
            Case mjtObj.mjOBJ_ACTUATOR : Return "actuator"
            Case mjtObj.mjOBJ_SENSOR : Return "sensor"
            Case mjtObj.mjOBJ_EQUALITY : Return "equality"
            Case mjtObj.mjOBJ_TENDON : Return "tendon"
            Case mjtObj.mjOBJ_KEY : Return "key"
            Case Else : Return type.ToString()
        End Select
    End Function

End Module
