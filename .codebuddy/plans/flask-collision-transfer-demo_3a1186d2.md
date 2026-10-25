---
name: flask-collision-transfer-demo
overview: 修好"相机漏渲染模型"的缺陷（SceneTriangleBuffer 缺平台/静态桌/地面），并用 models\erlenmeyer_flask.glb 做出真正的三角瓶（凸包碰撞 + 原网格渲染、按溶液体积给不同质量）；补上静态桌面支持与"正立/倾倒/落地"姿态判定；最后新增两个 demo 脚本——flask_sweep.js（机械臂横扫瓶口，观察瓶子被推倒还是被推移）与 flask_transfer.js（把左桌 5 个瓶子逐个搬到右桌，全程真实碰撞）。
todos:
  - id: camera-render-fix
    content: 修复相机漏渲染：SceneTriangleBuffer 补齐平台与地面，并让静态桌走 Objects 通道
    status: completed
  - id: flask-assets
    content: 新增 FlaskAssets：解析 erlenmeyer_flask.glb、提取轴向剖面、生成旋转凸碰撞体与渲染网格
    status: completed
  - id: flask-static-spawn
    content: 新增 BlockShape.Flask 与 BlockFactory 分支，spawn 支持 static 参数生成静态桌面
    status: completed
    dependencies:
      - flask-assets
  - id: posture-state
    content: 实现 ObjectPosture 判定并把 posture/tiltDeg/displacement/static 写入 state.objects
    status: completed
    dependencies:
      - flask-static-spawn
  - id: flask-sweep
    content: 编写 flask_sweep.js：桌面 5 瓶 + 机械臂横扫瓶口，输出推移/扫倒/扫落结论
    status: completed
    dependencies:
      - posture-state
  - id: flask-transfer
    content: 编写 flask_transfer.js：把 5 个瓶子从左桌逐个搬到右桌并判定成败
    status: completed
    dependencies:
      - flask-sweep
  - id: verify-camera
    content: 扩充 SimSelfTest 相机断言，并端到端跑通两个脚本 + balance_beam 木板可见性
    status: completed
    dependencies:
      - flask-transfer
---

## 用户需求

### 一、碰撞测试脚本（先做）

在机械臂左侧的桌面上摆放 5 个三角瓶，机械臂**横向扫过瓶口**，验证物理碰撞是否能正确地把瓶子从桌面上推倒或扫落。需要能区分：

- 力度不够 → 瓶子只是**被推移**（位置变化但仍正立）
- 力度足够 → 瓶子**翻倒**，进而从桌面**掉落到地面**
- 掉到地面后要判定是**正立落地**（底部先着地、重心稳定）还是**倾倒**（角度过大、重心导致侧倒）

### 二、搬运脚本

机械臂左右各一张桌子，左桌 5 个三角瓶，瓶内溶液体积不同 → 质量不同。要求机械臂依次把 5 个瓶子从左桌搬到右桌，全程真实物理：

- 机械臂与桌面、机械臂与瓶、瓶与瓶、瓶与地面之间都要有碰撞
- 操作不当会撞倒瓶子、把瓶子扫下桌
- **不做碎裂、不删除模型**：瓶子即使掉到地上也保留在场景里，只记录并输出其姿态（正立/倾倒/已落地）与位移

### 三、修复相机漏渲染（缺陷修复）

`balance_beam.js` 里相机画面中**看不到弹簧上的木板**。经定位，这不是视野问题，而是相机渲染链路漏了模型：视口用的 `SceneBuilder` 已经会画平台，而相机用的 `SceneTriangleBuffer` 只画了连杆/手指/积木，**没有画平台**（也没有地面、桌面）。需要把该路径补齐，保证"应该被看见的模型都出现在画面里"。

### 四、新 demo 的相机可见性

两个新 demo 中，三维场景里的对象（左右两张桌、5 个三角瓶、机械臂）都必须能出现在相机画面中；脚本要能输出"本次取帧可见 N/5 个瓶子"这样的量化结论，而不是只截图了事。

### 视觉与交互效果

- 三角瓶使用项目内已有模型 `models\erlenmeyer_flask.glb`，外观与真实三角瓶一致（锥形瓶身 + 细颈）
- 5 个瓶子按溶液体积呈不同深浅的液体色，质量随体积递增
- 玻璃瓶摩擦较低，机械臂扫过时会被推走或推倒，倒下的瓶子会滚落桌面、摔到地面
- 相机画面里能看到浅灰桌面、白底、彩色瓶身与它们在地面上的最终姿态

## 技术栈

沿用现有工程：VB.NET（net10.0 / WinForms）`VirtualWorkshop` + `PhysicsEngine`（自研刚体物理）+ LiteJs 脚本（`Controller`）。
不引入新依赖；glb 解析复用工程内已有的 `GlbLinkLoader`（`ReadContainer` / `GltfRoot` / `WalkNode` / `BuildMesh`）。

## 实现方案

### 1. 修复相机漏渲染（根因修复）

`Sensing\SceneTriangleBuffer.Build(simulation)` 目前只追加 连杆 / 手指 / `simulation.Objects`；而 `Rendering\SceneBuilder.BuildSurfaces` 是 连杆 / 手指 / Objects / **Platform**。两套路径各写一遍就是本次 bug 的结构性原因。

- 在 `SceneTriangleBuffer` 中新增 `AppendPlatform(simulation)`：追加 `Platform.PlateMesh`（位姿取 `Platform.Plate`、origin=Zero）与 `Platform.StopperMesh`（取 `Platform.Stopper`）
- 新增 `AppendGround(simulation)`：在 `GroundZ` 处追加一块大四边形（`LinkMesh`，2 个三角形，颜色 slate-200），否则俯视画面里物体像悬空
- **静态桌不单独处理**：让桌子作为 `BlockInstance` 进入 `simulation.Objects`（`Body.SetStatic()`），这样视口与相机共用同一条 `Objects` 路径，以后再加道具也不会漏
- 在 `SceneTriangleBuffer` 顶部注释里写明"新增非 Objects 刚体时必须同时改这里与 SceneBuilder"，并用一个 `AppendSceneProps` 私有方法聚合平台/地面，避免再次分叉

### 2. 三角瓶资产（`Simulation\FlaskAssets.vb`，新增）

- `Load()`：用 `GlbLinkLoader` 的新入口 `LoadProp(file)`（内部以 `currentLink = "prop"`、`robot = Nothing` 调 `WalkNode`，因为 `ResolveLinkName(Nothing, …)` 返回 `Nothing`，所有节点都会落到 `"prop"` 组）→ 得到 `LinkMesh`（**渲染用原网格，保留真实外观**）
- `BuildCollider(mesh)`：从 `LinkMesh.Triangles` 的顶点算**轴向半径剖面**：按 z 分层取 `max radius`，再自下而上做单调包络（`r(z) = min(r(z), r(z-Δz))`），保证 profile 不增 → 旋转体一定凸；用 `sides = 18` 生成 `ConvexMesh3D`（直接构造 `Vertices` / `Faces`，顶点平移到质心并记录 `PivotOffset`）
- 输出 `FlaskGeometry`：`RenderMesh`、`Collider`、`Height`、`BaseRadius`、`NeckRadius`、`PivotOffset`、`GripHeight`（夹瓶颈用的 TCP 高度）
- 缓存：按文件路径 `Lazy` 缓存，避免每次 spawn 重复解析
- 兜底：glb 缺失/解析失败时退回 `ConvexPrimitives3D.CreateFrustum` 并打印警告

### 3. 三角瓶形状与静态桌面

- `BlockShape` 增 `Flask = 10`；`BlockFactory.ParseShape` 识别 `"flask"`/`"erlenmeyer"`/`"三角瓶"`；`ShapeName` 返回 `"flask"`
- `BlockFactory.Create` 增 `Case BlockShape.Flask`：`mesh = FlaskAssets.Geometry.Collider`、`collider = New ConvexPolyhedronCollider3D(mesh)`、`render = FlaskAssets.Geometry.RenderMesh`、`size` 取 glb 包围盒；**flask 分支把 `position` 视为"瓶底中心"**，内部 `Position = position + rotation.Rotate(PivotOffset)`（其余形状保持现状，零影响）
- `ArmSimulation.SpawnBlock(..., Optional isStatic As Boolean = False)`：构造后 `If isStatic Then body.SetStatic()`
- `CommandDispatcher.Spawn` 读 `static` 布尔并透传；返回值加 `static`
- `BlockInstance` 增 `IsStatic`、`SpawnPosition`（用于算位移）

### 4. 姿态判定与状态字段

新增 `Simulation\ObjectPosture.vb`：

```
Enum Posture { Upright, Tilted, FallOver, Airborne }
Compute(item, tableTop) -> (Posture, TiltDeg, Displacement)
```

- `tilt = acos(clamp(localUp · worldUp))`，`localUp = Body.Orientation.Rotate(0,0,1)`
- `Upright`：`tilt < 20°` 且 `z ≥ tableTop - 0.03`
- `Tilted`：`20° ≤ tilt < 55°`
- `FallOver`：`tilt ≥ 55°`
- `Airborne`：`z < tableTop - 0.08`（已离开桌面）
- `Displacement` = 当前质心与 `SpawnPosition` 的水平距离

`StateSnapshot.ObjectState` 增 `Static As Boolean`、`Posture As String`、`TiltDeg As Double`、`Displacement As Double`（只加字段，向后兼容）。

### 5. 两个 demo 脚本

**`scripts\flask_sweep.js`**

1. `clear()` → 左桌（静态 Box `[0.72,0.55,0.04]` @ `(-0.30,-0.34,0.42)`）+ 4 条静态桌腿 → 桌上 5 个 flask（mass 0.10/0.16/0.22/0.28/0.34，沿 x 等距 ≥ 1.5×瓶底直径，颜色由浅黄到深黄）
2. 机械臂抬到 `tableTop + flaskHeight - 0.02`（略低于瓶口）**横向直线扫掠**：`arm.moveLPose(...)` 从桌子一端扫到另一端（分段 6 段，`strict=false` 以免侧向姿态不可达）
3. 每段后读 `state.objects`，记录每个瓶的 `posture / tiltDeg / displacement`
4. 判定与打印：`被推移`（displacement > 3 cm 且 posture ∈ {Upright, Tilted}）、`被扫倒`（FallOver）、`被扫落`（Airborne 且 z < 0.1）
5. 汇总：`推移 N / 扫倒 M / 扫落 K`

**`scripts\flask_transfer.js`**

1. 左右两桌（`(-0.30,-0.34,0.42)` 与 `(-0.30,+0.34,0.42)`）+ 左桌 5 瓶
2. 逐瓶：`release` → 到瓶上方（`TCP = tableTop + GripHeight`，夹瓶颈）→ `descendSlowly` 直线下降 → `grip(50)` → 等 2.5 s → `ascendSlowly`（10 段 `moveLHold`）→ 平移到右桌（4 段）→ `descendHold` → `release` → 抬走
3. 每瓶后判定：`success`（在右桌上方且 posture ∈ {Upright, Tilted}）/ `tipped` / `fell`（Airborne）
4. 打印逐瓶结果 + 成功率；**失败的瓶子保留场景内**

### 6. 相机可见性验证（脚本内量化）

- 用 `state.camera` 的 `fx/fy/cx/cy/position/rpy` 把每个物体中心投影到像素坐标，统计落在 `[0,width)×[0,height)` 的数量
- `balance_beam.js` 末尾加一行：`相机内可见木板 = true/false`（投影板心）
- `flask_sweep/flask_transfer` 分别在左桌上方、右桌上方各取一帧，打印 `左桌帧可见 5/5`、`右桌帧可见 N/5`
- 60° FOV 单帧装不下左右两桌 → 采用"跟手取景"而非硬凑全景（用户已确认问题不在视角）

## 目录结构（改动清单）

```
src/VirtualWorkshop/
├── Sensing/SceneTriangleBuffer.vb      [MODIFY] 追加 AppendPlatform/AppendGround，与 SceneBuilder 对齐
├── Simulation/FlaskAssets.vb           [NEW]    glb 加载、轴向剖面提取、旋转凸体、几何缓存
├── Simulation/BlockFactory.vb          [MODIFY] BlockShape.Flask + ParseShape/ShapeName/Create 分支（PivotOffset 处理）
├── Simulation/ArmSimulation.vb         [MODIFY] SpawnBlock(isStatic)、ObjectState 填充 posture/tilt/displacement
├── Simulation/BlockInstance 定义        [MODIFY] 增 IsStatic、SpawnPosition
├── Simulation/ObjectPosture.vb         [NEW]    姿态枚举与计算（Upright/Tilted/FallOver/Airborne）
├── Simulation/StateSnapshot.vb         [MODIFY] ObjectState 增 Static/Posture/TiltDeg/Displacement
├── Model/GlbLinkLoader.vb              [MODIFY] 新增 LoadProp(file)（不依赖 URDF 的加载入口）
├── Protocol/CommandDispatcher.vb       [MODIFY] spawn 支持 static 参数
└── Diagnostics/SimSelfTest.vb          [MODIFY] 相机段断言"缓冲包含平台三角面 + 板心投影在画面内"

src/Controller/scripts/
├── flask_sweep.js                      [NEW] 横扫瓶口碰撞测试
└── flask_transfer.js                   [NEW] 左桌→右桌 5 瓶搬运
```

## 性能与关键取舍

- glb 只解析一次并缓存；每次 spawn 只做 `ConvexMesh3D` 复用 + 一个 `RigidBody3D`
- 三角瓶碰撞体用 **18 边旋转体**（约 40 顶点 / 40 面），远小于原网格三角面，接触求解开销可控
- 相机缓冲每帧重建仍走"按需扩容 + 数组复用"，不产生大量托管对象
- 桌子作为静态 `Objects` 会进入 `state.objects`，脚本用 `static` 字段过滤，不影响"瓶子计数"

## 关键风险

- **glb 朝向/单位**：加载后按包围盒把高度归一化到 0.13 m，并确保"瓶底在 -h/2、瓶口在 +h/2"；若原模型 Y-up 需绕 X 转 -90°
- **凸性**：瓶口外张会让 profile 非单调，必须做单调包络，否则 `ConvexPolyhedronCollider3D` 会出现穿透
- **夹持**：瓶颈直径约 20 mm，`GripperOpening` 需闭合到约 20 mm；实测夹持上限约 0.4 kg，5 个瓶 max 0.34 kg 在范围内，但需实测标定 `grip` 力
- **侧向姿态不可达**：\(y>0.1\) 低处 `strict` 会失败，搬运脚本必须容忍降级（已有 `moveStraight` 的 try/catch 模式）
- **不删除对象**：落地瓶保留，仅状态标记；`state.objects` 数量会一直为 5（+2 桌）