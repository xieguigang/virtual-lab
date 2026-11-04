# MuJoCo C API → 托管 API 对照表

本库（`G:\virtual-lab\src\MuJoCo`）以 `Microsoft.VisualBasic.Engine.Physics`（下称"内核"）
为后端，用纯托管代码复刻 MuJoCo 3.17 的模型/状态/推进结构。下表的左列是
`include/mujoco/mujoco.h` 里的函数名，右列是托管实现（`MjApi` 模块）。

## 1. 命名与签名映射

| C API | 托管 API | 说明 |
|---|---|---|
| `mj_loadXML(file, vfs, err, sz)` | `mj_loadXML(file, ByRef errorText)` / `mj_loadXML(file)` | 后者失败时抛 `MjException` |
| `mj_parseXML(...)` → `mjSpec*` | `mj_parseXML(file, ByRef errorText)` → `MjSpec` | 解析与编译分离，可在两者之间替换 `MeshResolver` |
| `mj_parseXMLString(...)` | `mj_parseXMLString(xml, ByRef errorText, baseDir)` | 同上 |
| `mj_compile(spec, vfs)` | `mj_compile(spec, ByRef errorText)` / `mj_compile(spec)` | |
| `mj_makeData(m)` | `mj_makeData(m)` | 内部完成引擎装配 |
| `mj_copyData(dest, m, src)` | `mj_copyData(dest, m, src)` | |
| `mj_deleteModel` / `mj_deleteData` | 同名空实现 | 托管对象由 GC 回收，保留函数只为逐行对照 |
| `mj_step(m, d)` | `mj_step(m, d)` | |
| `mj_step1` / `mj_step2` | 同名 | 见 §2.1 |
| `mj_forward` / `mj_inverse` | 同名 | |
| `mj_resetData` / `mj_resetDataKeyframe` | 同名 | |
| `mj_setKeyframe` | 同名 | |
| `mj_kinematics` / `mj_comPos` / `mj_comVel` | 同名 | |
| `mj_jac` / `mj_jacBody` / `mj_jacBodyCom` / `mj_jacSubtreeCom` / `mj_jacGeom` / `mj_jacSite` / `mj_jacPointAxis` | 同名 | `mj_jacDot` 未实现 |
| `mj_fullM` / `mj_mulM` | 同名 | |
| `mj_stateSize` / `mj_getState` / `mj_setState` | 同名，`sig` 用 `MjStateSig`（`<Flags>`） | |
| `mj_name2id` / `mj_id2name` | 同名，`type` 用 `mjtObj` | 未知名返回 -1 / `Nothing` |
| `mj_ray` | 同名，返回距离，`ByRef geomid` | 无命中返回 -1 |
| `mj_addContact` | 未实现 | 手动注入接触不在本库范围内 |

结构体字段名与 C 版一致（`mjModel.qpos0`、`mjData.qpos`、`mjData.xpos`、`mjModel.opt.timestep` 等），
但类型是托管数组：计数用 `Integer`，数值用 `Double`，布尔用 `Boolean`。

## 2. 语义差异（必须知道）

### 2.1 `mj_step1` / `mj_step2` 的原子性

C 版把一次推进拆成"位置/速度前向 + 约束装配"和"加速度求解 + 积分"。
托管层能分开调用，但内部的"求解 + 积分"是内核的一次
`PhysicsWorld3D.Step(timestep)`，无法再细分。因此：

- `mj_step1`：`qpos/qvel → 引擎`、`mj_kinematics`、`mj_comPos`、`mj_comVel`；
- `mj_step2`：`World.Step(timestep)`、`引擎 → qpos/qvel`、刷新派生量与传感器；
- `mj_step1 + mj_step2` 与 `mj_step` 逐位相同（自测里有断言）。

### 2.2 碰撞代理是 body 级，不是 geom 级

内核是"一个刚体一个形状"，MuJoCo 是"一个 body 多个 geom"。因此一个 body 上所有
参与碰撞的 geom 会被合并成**一个凸包**：

- 凹处被填平（两条腿之间的空隙在同一个 body 内不存在，跨 body 的不受影响）；
- `mjData.ncon` 是 body 对级的接触数，不能与原生 MuJoCo 的 geom 对级接触逐个对照；
- `mjContact.geom1/geom2` 记录的是两个 body 的**代表 geom**；
- `contype/conaffinity` 的语义只能近似：`contype=0` 或 `conaffinity=0` 的 geom 被排除，
  `contype=2 且 conaffinity=2`（自碰撞组）也被排除，其余按 body 归类到
  `MjCollisionModel.GroupRobot / GroupEnvironment / GroupProp` 三组位掩码。

需要更细的碰撞分组时，在 `MjsBody.CollisionGroup/CollisionMask` 上显式指定。

### 2.3 求解器

内核是固定步长的**顺序冲量（PGS）+ 位置层修正**，所以：

- `opt.solver`、`opt.integrator`、`opt.jacobian`、`opt.cone` 只有记录意义，
  实际行为固定为"半隐式欧拉 + 顺序冲量 + 库仑椭圆锥"；
- `opt.iterations` 直接映射到 PGS 的迭代次数；
- `opt.o_solref` 映射到 `ContactTimeConstant/ContactDampingRatio`，
  `opt.o_solimp` 的一个固定经验映射到 `Baumgarte=0.05 / Slop=0.005`；
- `opt.o_friction` 作为没有 geom 时的兜底摩擦。

### 2.4 精度

几何层用 `System.Numerics.Vector3`（单精度）表达姿态与位置，动力学层（CRBA/RNEA/
雅可比）用 `Double` 计算。由此：

- `xpos/xquat` 的绝对误差约 `1e-8 m`；
- 有限差分校验雅可比时扰动必须大于 `1e-4`，否则会被单精度噪声淹没；
- 无接触工况下 `M` 与 `qfrc_bias` 与解析解一致到 `1e-12`（自测断言）。

### 2.5 传感器

已实现：`framepos`、`framequat`、`framexaxis/yaxis/zaxis`、`framelinvel`、`frameangvel`、
`gyro`、`velocimeter`、`accelerometer`、`subtreecom`、`subtreelinvel`、`subtreeangmom`、
`jointpos`、`jointvel`、`actuatorpos/vel/frc`、`ballquat`、`ballangvel`、`e_potential`、
`e_kinetic`、`clock`、`rangefinder`。

- `noise` 与 `cutoff` 不实现（确定性仿真，加噪会让自测无法断言）；
- `accelerometer` 在 `mj_step` 内部用"重力在本地系的投影"近似，
  高速摆动时与原生 MuJoCo 有差异；
- 其余类型输出 0，不抛异常（多一个传感器不应该让整机起不来）。

### 2.6 未实现的元素

| 元素 | 行为 |
|---|---|
| `<flex>` / `<skin>` / `<hfield>` | 忽略 |
| `<tendon>` 的 `sphere/cylinder/pulley` 绕行 | 忽略绕行几何，只保留直线路径 |
| equality 的 `joint` / `distance` | 退化为一维行约束，语义近似 |
| `mj_jacDot` / `mj_addContact` / `mj_recompile` | 未实现 |

## 3. 最小示例

```vb
Imports MuJoCo

Dim spec As MjSpec = MjcfParser.Parse("robot.xml")
spec.MeshResolver = Function(f) IO.Path.Combine(meshDir, f)   ' 可选：重定向网格目录
spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE, New Vector3(2, 2, 0))   ' 加地面

Dim m As mjModel = mj_compile(spec)
Dim d As mjData = mj_makeData(m)

For step As Integer = 1 To 1000
    d.ctrl(0) = 0.3          ' 位置执行器的目标角度
    mj_step(m, d)
Next

Dim trunk As Integer = mj_name2id(m, mjtObj.mjOBJ_BODY, "trunk_base")
Console.WriteLine($"trunk z = {d.xpos(trunk * 3 + 2)}")
```

用代码从零拼场景：

```vb
Dim spec As New MjSpec()
spec.Opt.timestep = 0.002
Dim body As MjsBody = spec.AddBody(spec.World, "ball", New Vector3(0, 0, 1))
body.HasInertial = True
body.Mass = 0.03
body.FullInertia = New Double() {1.2E-5, 1.2E-5, 1.2E-5, 0, 0, 0}
spec.AddFreeJoint(body, "ball_free")
spec.AddGeom(body, mjtGeom.mjGEOM_SPHERE, New Vector3(0.05F, 0, 0))
spec.AddGeom(spec.World, mjtGeom.mjGEOM_PLANE, New Vector3(2, 2, 0))

Dim m = mj_compile(spec)
Dim d = mj_makeData(m)
```

## 4. 场景门面与对象式兼容层

宿主（例如 VirtualWorkshop）不再直接引用 `PhysicsEngine.vbproj`，物理能力统一由本程序集提供。

| 命名空间 | 用途 | 主要 API |
|---|---|---|
| `MuJoCo.Scene` | MJCF 风格场景门面：`MjSceneSpec` → `MjSpec` → `mjModel` / `mjData` → `mj_step` | `MjSceneSpec`、`MjSceneBuilder.Build`、`MjScene.Step/Advance/Reset/SpawnBody/RemoveBody`、`MjBody`、`MjServo`、`MjSceneContact` |
| `MuJoCo.Interop` | 与内核同形的对象式世界/刚体句柄 | `Vector3`、`PhysicsMaterial`、`RigidBody3D`、`PhysicsWorld3D` |
| `MuJoCo.Interop.Math3D` | 姿态与张量 | `Quaternion`、`Matrix3x3`、`Vector3Math` |
| `MuJoCo.Interop.Collision3D` | 碰撞形状、凸网格、接触 | `Collider3D`、`ConvexMesh3D`、`ConvexPrimitives3D`、`Manifold3D`、`ContactPoint3D` |
| `MuJoCo.Interop.Joints3D` | 关节、伺服与弹簧 | `RevoluteJoint3D`、`PrismaticJoint3D`、`JointServo3D`、`SpringJoint3D` |
| `MuJoCo.Geometry` | 纯托管凸体图元 | `MjPrimitives.Box/Sphere/Cylinder/Cone/Wedge/...` |
| `MuJoCo.Import` | URDF → 中性场景描述 | `MjUrdf.Read/ReadXml` |

语义说明：

- `Interop` 层的**公开签名**只使用 MuJoCo 自有类型；内核类型仅出现在 `Friend` 转换模块
  `MjInterop` 中，外部程序集不可见。
- `Scene` 层是纯 MuJoCo 风格路径：结构变更走 `MjSpec` 重建并按名恢复 `qpos/qvel`，
  热路径只调用 `MjApi.mj_step`。
- VirtualWorkshop 为保证既有物理行为与状态快照逐位一致，主仿真链使用 `Interop` 世界句柄；
  `Scene` 门面供新建场景与后续渐进迁移使用。两者共用同一个物理内核。

