Namespace Protocol

    ''' <summary>
    ''' 虚拟硬件协议的字符串常量（指令名、错误码、事件名、字段名）。
    ''' </summary>
    ''' <remarks>
    ''' 这里是单一事实源：TCP 适配器、HTTP 适配器与 Controller 的 REPL 都引用同一份指令名，
    ''' 避免两边拼写漂移。
    ''' </remarks>
    Public Module ProtocolText

        ''' <summary>协议版本。</summary>
        Public Const Version As String = "1.0"

        ' ---- 指令 ----
        Public Const CmdHello As String = "hello"
        Public Const CmdPing As String = "ping"
        Public Const CmdInfo As String = "info"
        Public Const CmdState As String = "state"
        Public Const CmdObs As String = "obs"
        Public Const CmdEnable As String = "enable"
        Public Const CmdHome As String = "home"
        Public Const CmdMoveJ As String = "movej"
        Public Const CmdMoveJRel As String = "movej_rel"
        Public Const CmdMoveL As String = "movel"
        Public Const CmdMoveTo As String = "move_to"
        Public Const CmdServo As String = "servo"
        Public Const CmdWaitMotion As String = "wait_motion"
        Public Const CmdStop As String = "stop"
        Public Const CmdEstop As String = "estop"
        Public Const CmdReset As String = "reset"
        Public Const CmdGrip As String = "grip"
        Public Const CmdRelease As String = "release"
        Public Const CmdSpawn As String = "spawn"
        Public Const CmdRemove As String = "remove"
        Public Const CmdClear As String = "clear"
        Public Const CmdObjects As String = "objects"
        Public Const CmdConfig As String = "config"
        Public Const CmdSimStep As String = "sim_step"

        ''' <summary>取一帧夹爪相机图像。</summary>
        Public Const CmdFrame As String = "frame"
        ''' <summary>查询/修改夹爪相机参数。</summary>
        Public Const CmdCamera As String = "camera"

        ' ---- 错误码 ----
        Public Const ErrUnknownCommand As String = "unknown_command"
        Public Const ErrBadParams As String = "bad_params"
        Public Const ErrNotEnabled As String = "not_enabled"
        Public Const ErrEstop As String = "estop_active"
        Public Const ErrIkUnreachable As String = "ik_unreachable"
        Public Const ErrJointLimit As String = "joint_limit"
        Public Const ErrGroundViolation As String = "ground_violation"
        Public Const ErrBusy As String = "busy"
        Public Const ErrTimeout As String = "timeout"
        Public Const ErrNotFound As String = "not_found"
        Public Const ErrSimFault As String = "sim_fault"
        Public Const ErrInternal As String = "internal"

        ' ---- 事件 ----
        Public Const EventState As String = "state"
        Public Const EventHello As String = "hello"

        ' ---- 字段 ----
        Public Const FieldOk As String = "ok"
        Public Const FieldId As String = "id"
        Public Const FieldCmd As String = "cmd"
        Public Const FieldParams As String = "params"
        Public Const FieldResult As String = "result"
        Public Const FieldError As String = "error"
        Public Const FieldCode As String = "code"
        Public Const FieldMessage As String = "message"
        Public Const FieldEvent As String = "event"
        Public Const FieldData As String = "data"
        Public Const FieldMotionId As String = "motionId"
        Public Const FieldAccepted As String = "accepted"
        Public Const FieldVersion As String = "version"
        Public Const FieldJoints As String = "joints"
        Public Const FieldModel As String = "model"
        Public Const FieldSimMode As String = "simMode"

        ' ---- 相机字段 ----
        Public Const FieldWidth As String = "width"
        Public Const FieldHeight As String = "height"
        Public Const FieldFormat As String = "format"
        Public Const FieldDepth As String = "depth"
        Public Const FieldCamera As String = "camera"
        Public Const FieldRgb As String = "rgb"

        ''' <summary>把异常包装成协议错误对象。</summary>
        Public Function ErrorNode(code As String, message As String) As System.Text.Json.Nodes.JsonObject
            Return New System.Text.Json.Nodes.JsonObject From {
                {FieldCode, code},
                {FieldMessage, message}
            }
        End Function
    End Module

End Namespace
