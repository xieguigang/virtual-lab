// ---------------------------------------------------------------------------
// 弹簧木板配平演示
//
// 用法: Controller.exe --js scripts/balance_beam.js
//
// 场景:
//   * 地面上放一块由四角弹簧支撑的正方形木板（弹簧会画成螺旋线），在机械臂正前方
//   * 4 块重块摆在机械臂**左侧**的地面上（与木板隔开，取件时大臂不会扫到板）
//   * 机械臂依次把它们搬到板上；每搬一块前先读一次木板倾角，
//     把下一块放到"翘起的一侧"，最后再做几轮微调把倾角压到阈值内
//
// 物理:
//   载荷偏心时四根簧压缩量不同（compressions 数组），弹簧力差产生恢复力矩，
//   木板停在一个新的平衡角上；摆上重块时它会因为 tanθ > μ 而真的滑动。
// ---------------------------------------------------------------------------

// 板放在机械臂正前方（y ≈ 0），但**高度**必须明显低于"零位时手臂下沿"（约 0.142 m）：
// 板顶高于这个值时，放件时肘部会压到板上，实测第 1 块就会把板压到行程限位。
var PLATE_SIZE = [0.30, 0.30, 0.03];
var PLATE_POS = [0.36, 0.0, 0.09];
var PLATE_HALF = 0.15;
var PLATE_TOP = PLATE_SIZE[2] * 0.5;   // 板中心 → 板顶

// 块取 32 mm：夹爪闭合时能夹住它的中上部。
// 质量上限实测约 0.4 kg —— 位置驱动夹爪的法向力有限（摩擦 μ≈0.8），
// 0.6 kg 时抬起就会从两指之间滑落（实测 blockZ 直接掉回地面）。
var BLOCK_SIZE = [0.032, 0.032, 0.032];
var BLOCK_MASS = 0.25;
var COLORS = ['#F59E0B', '#22D3EE', '#34D399', '#F472B6'];

var FINGER_DROP = 0.027;        // 手指中心在 TCP 下方约 2.7 cm
var DOWN_RX = 0;
var DOWN_RY = 90;
var DOWN_RZ = 0;

// 4 块重块全部摆在机械臂**左侧**（世界 +Y 一侧），木板仍在正前方（y ≈ 0）。
//
// 之前把重块摆在正前方的远端（x ≈ 0.55），机械臂得完全伸出去才够得到：
// 一是接近臂展极限，二是下降取件时大臂正好扫过木板所在的位置，会把平台压塌。
// 挪到左侧后，取件点离木板有 0.35 m 以上，大臂完全不会碰到板。
//
// 取件点距基座 0.29~0.35 m、j1 转角 20°~45°。
// 不要摆得更远（x/y 再大 0.1 m 以上）：那种位置 IK 解不出"末端朝下"的姿态，
// 会回退成只约束位置，抬臂时姿态突变，夹着的方块被甩到 1.7 m 外。
var PICK_SPOTS = [
    { x: 0.20, y: 0.20 },
    { x: 0.26, y: 0.16 },
    { x: 0.16, y: 0.24 },
    { x: 0.28, y: 0.10 }
];

var BALANCED_DEG = 3.0;         // 倾角阈值
var GAIN = 0.012;               // 每 1° 倾斜补偿多少米（m/deg）

// ---------------------------------------------------------------------------

function clamp(v, lo, hi) {
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

function movePose(x, y, z, speed) {
    if (isNaN(x) || isNaN(y) || isNaN(z)) {
        console.log('   目标坐标无效 (' + x + ', ' + y + ', ' + z + ')，跳过这次移动');
        return;
    }

    var motion = arm.moveTo(x, y, z, DOWN_RX, DOWN_RY, DOWN_RZ, speed);
    var done = arm.waitMotion(motion.motionId, 180000);

    if (!done.done || !done.success) {
        throw new Error('运动未完成: ' + done.message);
    }
}

function num(value) {
    if (value === undefined || value === null) return 0;

    var parsed = parseFloat(String(value));

    if (isNaN(parsed)) return 0;

    return parsed;
}

/** position 可能是 "x y z" 字符串，也可能是 [x, y, z] 数组，两种都兼容 */
function vec(value) {
    if (value === undefined || value === null) return [0, 0, 0];

    if (typeof value === 'string') {
        var parts = value.indexOf(',') >= 0 ? value.split(',') : value.split(' ');

        return [num(parts[0]), num(parts[1]), num(parts[2])];
    }

    return [num(value[0]), num(value[1]), num(value[2])];
}

function report(label) {
    var t = arm.tilt();

    console.log(label + '  倾角 ' + t.tiltMag.toFixed(2) + '°' +
                '  (x ' + t.tiltX.toFixed(2) + '° / y ' + t.tiltY.toFixed(2) + '°)' +
                '  板高 ' + t.height.toFixed(3) + ' m');

    return t;
}

// 高位是 0.40~0.45：水平平移必须发生在高位。
// 手臂的肘部在"肩—腕"之间，只要腕部（TCP）高于板面，肘部就一定更高；
// 反过来，若在板面高度附近做水平平移，肘部会直接扫过木板、把它压到行程限位。
//
// 另外 moveTo 是关节空间插值，末端走的是弧线：一次性下降 30 cm 时弧线会明显
// 偏离竖直方向（实测把已经放到板面上的方块甩到 27 cm 外）。所以竖直段必须分段。
// 夹着工件时的移动一律走笛卡尔直线 + 严格保持末端姿态（arm.moveLHold）。
// 关节插值（arm.moveTo）的末端轨迹是弧线，而且低处姿态解不出来时会退化成
// "只约束位置、姿态随动"，夹爪一转手腕，方块就被甩出一米多。
function moveStraight(x, y, z, speed) {
    if (isNaN(x) || isNaN(y) || isNaN(z)) {
        console.log('   目标坐标无效 (' + x + ', ' + y + ', ' + z + ')，跳过这次移动');
        return;
    }

    var motion = arm.moveLHold(x, y, z, speed);
    var done = arm.waitMotion(motion.motionId, 180000);

    if (!done.done || !done.success) {
        throw new Error('直线运动未完成: ' + done.message);
    }
}

// 空手接近/下降：关节插值更灵活（低处姿态要求苛刻时也能到达）
function descendSlowly(x, y, fromZ, toZ, steps) {
    for (var s = 1; s <= steps; s++) {
        movePose(x, y, fromZ + (toZ - fromZ) * s / steps, 0.12);
    }
}

// 夹着工件下降：必须走直线 + 锁定姿态
function descendHold(x, y, fromZ, toZ, steps) {
    for (var s = 1; s <= steps; s++) {
        moveStraight(x, y, fromZ + (toZ - fromZ) * s / steps, 0.10);
    }
}

// 抬起也必须分段：夹着积木时如果一次性抬 35 cm，关节插值会让末端走出弧线，
// 手指横向运动把积木"搓"出去（实测积木飞出 5 m 远）。
function ascendSlowly(x, y, fromZ, toZ, steps) {
    for (var s = 1; s <= steps; s++) {
        moveStraight(x, y, fromZ + (toZ - fromZ) * s / steps, 0.12);
    }
}

function pick(px, py, pz) {
    arm.release();
    arm.sleep(600);

    movePose(px, py, 0.40, 0.08);
    descendSlowly(px, py, 0.40, pz + FINGER_DROP, 5);
    arm.grip(60);
    arm.sleep(2500);

    var gs = arm.state().gripper;
    console.log('     夹持: contact=' + gs.contact + '  open=' + gs.opening.toFixed(3) + '  hold=' + gs.holdingId);

    ascendSlowly(px, py, pz + FINGER_DROP, 0.40, 6);
}

function place(qx, qy) {
    var t = arm.tilt();
    var top = t.height + PLATE_TOP;

    // 水平移动也分段：从左侧取件点（j1≈67°）搬到正前方（j1≈14°）要转 50° 以上，
    // 一次性关节插值会让末端走出很大的弧线，夹着的积木被甩出两米开外
    var now = arm.state().tcp;
    var fromX = now[0];
    var fromY = now[1];

    for (var seg = 1; seg <= 4; seg++) {
        moveStraight(fromX + (qx - fromX) * seg / 4, fromY + (qy - fromY) * seg / 4, 0.40, 0.12);
    }

    descendHold(qx, qy, 0.40, top + FINGER_DROP + 0.02, 5);

    arm.release();
    arm.sleep(1500);

    ascendSlowly(qx, qy, top + FINGER_DROP + 0.02, 0.42, 5);
}

// 四块必须落在板上的四个不同象限：
// 若都放中心，第二块会直接插进第一块里（穿透 → 巨大冲量 → 板被压到行程限位），
// 这也是之前"第 1 块正常、第 2 块就崩"的真正原因。
var SLOTS = [
    { x: -0.07, y: -0.07 },
    { x: 0.07, y: 0.07 },
    { x: -0.07, y: 0.07 },
    { x: 0.07, y: -0.07 }
];

function dropPoint(tilt, index) {
    var slot = SLOTS[index % SLOTS.length];
    var gain = index === 0 ? 0.0 : GAIN;

    return {
        x: slot.x + clamp(tilt.tiltY * gain, -0.05, 0.05),
        y: slot.y + clamp(-tilt.tiltX * gain, -0.05, 0.05)
    };
}

// ---------------------------------------------------------------------------

console.log('=== 弹簧木板配平 ===');
arm.connect();
console.log('模式: ' + arm.info().simMode);

arm.clear();
arm.release();

console.log('1) 搭建四角弹簧木板');
var plate = arm.platform({
    size: PLATE_SIZE,
    position: PLATE_POS,
    stiffness: 200,
    damping: 60
});

console.log('   木板 ' + plate.plateId + '  弹簧 ' + plate.springs.length + ' 根');
arm.sleep(1500);
report('   空载:');

console.log('2) 在地面上放 4 块重块');
for (var i = 0; i < PICK_SPOTS.length; i++) {
    var spot = PICK_SPOTS[i];
    arm.spawn('box', BLOCK_SIZE, [spot.x, spot.y, 0.10], BLOCK_MASS, 0.8, COLORS[i]);
}

arm.sleep(1500);
var blocks = arm.objects();
console.log('   场景内 ' + blocks.length + ' 块重块');

console.log('3) 依次拾取并放置（按倾角反馈选点）');

for (var n = 0; n < 4; n++) {
    blocks = arm.objects();

    if (n >= blocks.length) break;

    var info = blocks[n];
    var at = vec(info.position);
    var before = arm.tilt();
    var offset = dropPoint(before, n);

    console.log('--- 第 ' + (n + 1) + ' 块 ' + info.id + ' ---');
    console.log('   拾取点 (' + at[0].toFixed(3) + ', ' + at[1].toFixed(3) + ')  z=' + at[2].toFixed(3));

    pick(at[0], at[1], at[2]);

    var targetX = PLATE_POS[0] + offset.x;
    var targetY = PLATE_POS[1] + offset.y;

    console.log('   放到板上 (' + targetX.toFixed(3) + ', ' + targetY.toFixed(3) + ')' +
                '  补偿 dx=' + offset.x.toFixed(3) + ' dy=' + offset.y.toFixed(3));

    place(targetX, targetY);
    arm.sleep(900);
    report('   放置后:');

    var landed = arm.objects()[n];
    console.log('     块落点 ' + JSON.stringify(landed.position));
}

console.log('4) 倾角微调');
var finalTilt = arm.tilt();
var rounds = 0;

while (finalTilt.tiltMag > BALANCED_DEG && rounds < 2) {
    rounds = rounds + 1;

    blocks = arm.objects();
    var worst = null;
    var worstDistance = -1;

    for (var k = 0; k < blocks.length; k++) {
        var pos = vec(blocks[k].position);

        // 只看还留在板上的块（滑落到地上的不再参与配平）
        if (Math.abs(pos[0] - PLATE_POS[0]) > PLATE_HALF) continue;
        if (Math.abs(pos[1] - PLATE_POS[1]) > PLATE_HALF) continue;

        var dx = pos[0] - PLATE_POS[0];
        var dy = pos[1] - PLATE_POS[1];
        var distance = Math.sqrt(dx * dx + dy * dy);

        if (distance > worstDistance) {
            worstDistance = distance;
            worst = blocks[k];
        }
    }

    if (worst === null || worstDistance < 0.02) break;

    var current = vec(worst.position);
    var shift = {
        x: clamp(finalTilt.tiltY * GAIN * 2.0, -0.09, 0.09),
        y: clamp(-finalTilt.tiltX * GAIN * 2.0, -0.09, 0.09)
    };

    console.log('--- 微调第 ' + rounds + ' 轮: 重新摆放 ' + worst.id + ' ---');
    console.log('   当前在 (' + current[0].toFixed(3) + ', ' + current[1].toFixed(3) + ')');

    pick(current[0], current[1], current[2]);

    var moveX = clamp(current[0] + shift.x, PLATE_POS[0] - PLATE_HALF * 0.85, PLATE_POS[0] + PLATE_HALF * 0.85);
    var moveY = clamp(current[1] + shift.y, PLATE_POS[1] - PLATE_HALF * 0.85, PLATE_POS[1] + PLATE_HALF * 0.85);

    place(moveX, moveY);
    arm.sleep(900);

    finalTilt = report('   微调后:');
}

console.log('5) 结果');
var last = arm.tilt();
var springs = arm.platform({});

for (var s = 0; s < springs.springs.length; s++) {
    var spring = springs.springs[s];

    console.log('   弹簧 ' + (s + 1) + ': 压缩 ' + (spring.compression * 1000).toFixed(1) +
                ' mm  力 ' + spring.force.toFixed(1) + ' N');
}

console.log('   最终倾角 ' + last.tiltMag.toFixed(2) + '°');

if (last.tiltMag <= BALANCED_DEG) {
    console.log('   结论: BALANCED ✓（倾角 ' + last.tiltMag.toFixed(2) + '°，阈值 ' + BALANCED_DEG + '°）');

    if (last.height < 0.07) {
        console.log('   注意: 板高 ' + last.height.toFixed(3) + ' m，已经压到行程限位附近');
    }
} else {
    console.log('   结论: 仍偏斜 ' + last.tiltMag.toFixed(2) + '°（阈值 ' + BALANCED_DEG + '°）');
}

console.log('=== 完成 ===');
