# MJCF 支持矩阵

`MjcfParser`（读取）与 `MjcfCompiler`（编译）覆盖的范围。
未列出的元素会被**忽略**；列出的元素属性若非可选，缺省时用 MuJoCo 的默认值。

## 1. 顶层

| 元素 | 支持的属性 | 备注 |
|---|---|---|
| `<mujoco model>` | `model` | |
| `<compiler>` | `angle`、`meshdir`、`autolimits`、`inertiafromgeom` | `angle="degree"` 在**编译期**换算成弧度；`coordinate`/`eulerseq` 只影响 `euler` 的解释 |
| `<option>` | `timestep`、`gravity`、`wind`、`magnetic`、`impratio`、`viscosity`、`density`、`iterations`、`ls_iterations`、`o_friction`、`o_solref`、`integrator`、`cone`、`solver` | `integrator`/`cone`/`solver` 只有记录意义（见 `MjApiMapping.md` §2.3） |
| `<size>`、`<visual>`、`<statistic>` | 忽略 | |
| `<default>` | 可重复出现、可任意嵌套；支持 `<joint>`、`<geom>`、`<site>`、`<position>`、`<motor>`、`<velocity>`、`<general>` 及其它元素的通用属性表 | 类链最多 16 层，最近的类优先，根类是 `main` |
| `<asset>` | `<mesh file|name|scale|vertex|face>`、`<material name|rgba>` | 纹理/高度场/皮肤忽略 |
| `<worldbody>` | 见 §2 | |
| `<actuator>` | 见 §5 | |
| `<sensor>` | 见 §6 | |
| `<equality>` | 见 §7 | |
| `<tendon>` | 见 §8 | |
| `<contact>` | `<exclude body1 body2>` | 只记录，不影响内核（内核按 body 分组） |
| `<keyframe>` | `<key name|time|qpos|qvel|ctrl>` | |

## 2. body 树

| 元素 | 支持的属性 |
|---|---|
| `<body>` | `name`、`pos`、`quat`/`axisangle`/`euler`/`zaxis`、`childclass`、`class`、`contype`、`conaffinity` |
| `<inertial>` | `pos`、`mass`、`fullinertia`（6）、`diaginertia`（3） |
| `<freejoint>` | 等价于 `<joint type="free">` |
| `<joint>` | `name`、`type`（`hinge`/`slide`/`ball`/`free`）、`pos`、`axis`、`range`、`limited`、`stiffness`、`damping`、`armature`、`frictionloss`、`margin`、`group`、`solref`、`actfrcrange` |
| `<geom>` | `name`、`type`（`plane`/`sphere`/`capsule`/`ellipsoid`/`cylinder`/`box`/`mesh`）、`size`、`pos`、`quat`/`axisangle`/`euler`/`zaxis`、`contype`、`conaffinity`、`condim`、`group`、`priority`、`margin`、`gap`、`solmix`、`friction`、`rgba`、`mesh`、`material` |
| `<site>` | `name`、`type`、`size`、`pos`、`quat`、`group`、`rgba` |
| `<camera>` | `name`、`pos`、`quat`、`fovy`（只记录，渲染不用） |
| `<light>` | `name`、`pos`、`dir`（只记录） |

### 惯性缺失时的处理

`<body>` 上没有 `<inertial>` 时，编译器按 geom 的体积 × 默认密度 1000 kg/m³
估算质量，并按包围盒的实心盒子估算惯量。这是"量级正确"的估计，
需要精确值时应当显式写 `<inertial>`。

### `autolimits`

`autolimits="true"`（默认）时，关节只要写了 `range` 就自动 `limited="true"`；
显式写 `limited` 时以显式值为准。

## 3. 角度与姿态

- `angle="radian"` 时所有角度原样使用；`degree` 时关节 `range` 与
  `euler`/`axisangle` 的角度在编译期乘以 π/180。
- 姿态支持 `quat`（w x y z）、`axisangle`（x y z 角度）、`euler`（按 `eulerseq`，
  默认 xyz 内旋）、`zaxis`。`xyaxes` 不支持。
- 四元数在写入模型前统一归一化。

## 4. mesh

- 文件按 `<compiler meshdir>` 拼接；宿主可以在 `MjSpec.MeshResolver` 上
  覆盖这个解析（duck 的 MJCF 写的是 `assets`，实际目录是 `meshes`）。
- 支持二进制与 ASCII STL，以及 `<mesh vertex face>` 内联数据。
- `scale` 在编译期乘到顶点上。
- 网格参与碰撞时取**凸包**（MuJoCo 同样要求凸网格）；凸包的点数上限是
  600（`ConvexHull3D.MaxInputPoints`），超过时按包围盒网格抽稀，
  以限制 SAT 的代价。

## 5. 执行器

| 元素 | 语义 | 写入的字段 |
|---|---|---|
| `<position>` | `force = kp·ctrl − kp·q − kv·q̇` | `gainprm[0]=kp`、`biasprm[1]=−kp`、`biasprm[2]=−kv` |
| `<velocity>` | `force = kv·ctrl − kv·q̇` | `gainprm[0]=kv`、`biasprm[2]=−kv` |
| `<damper>` | `force = −kv·q̇` | `gainprm[0]=0`、`biasprm[2]=−kv` |
| `<motor>` | `force = gear·ctrl` | `gainprm[0]=gear` |
| `<general>` | 按 `general` 默认类的 `kp/kv/gear` | 同上 |

支持的属性：`name`、`class`、`joint`、`site`、`body`、`tendon`、`kp`、`kv`、`gear`、
`forcerange`、`ctrlrange`、`group`。传动类型仅 `joint`（`site`/`body`/`tendon`
只记录，不参与求解）。

`ctrlrange` 在 `mj_step` 入口夹紧，`forcerange` 在约束求解里截断。

## 6. 传感器

见 `MjApiMapping.md` §2.5 的清单。属性：`name`、`noise`、`cutoff`、
`objtype`/`objname`、`site`、`joint`、`body`、`tendon`、`actuator`、`reftype`/`refname`。

## 7. equality

支持 `connect`、`weld`、`joint`、`distance` 四种元素名。
属性：`name`、`body1`、`body2`、`joint1`、`joint2`、`anchor`、`relpose`、
`solref`、`active`。

- `connect` → 引擎的 `BallJoint3D`（3 行点对点）；
- `weld` → `MjWeldConstraint`（3 平移 + 3 转动）；
- `joint`/`distance` → `MjRowConstraint`（1 行），语义近似。

## 8. tendon

只支持 `<fixed>`（关节列表）与 `<spatial>`（站点直线路径）。
`sphere`/`cylinder`/`pulley` 绕行几何被忽略。属性：`name`、`stiffness`、
`damping`、`armature`、`frictionloss`、`range`。

## 9. 报错行为

- 引用了不存在的 mesh / joint / site / material / geometry 名 → `mj_compile` 返回
  `Nothing`，`errorText` 里列出全部问题（不是只报第一处）。
- mesh 文件缺失 → 同上，错误文本带完整路径。
- 关节的 `qpos` 地址重叠 → `MjAssert.CheckModel` 抛 `MjException`。
- 运行期的越界索引 → 抛 `ArgumentOutOfRangeException`，绝不静默返回 0。
