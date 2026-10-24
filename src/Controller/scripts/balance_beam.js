// ---------------------------------------------------------------------------
// 弹簧木板配平演示
//
// 用法: Controller.exe --js scripts/balance_beam.js
//
// 场景:
//   * 地面上放一块由四角弹簧支撑的正方形木板（弹簧会画成螺旋线）
//   * 4 块重块散落在板外侧的地面上
//   * 机械臂依次把它们搬到板上；每搬一块前先读一次木板倾角，
//     把下一块放到"翘起的一侧"，最后再做几轮微调把倾角压到阈值内
//
// 物理:
//   载荷偏心时四根簧压缩量不同（compressions 数组），弹簧力差产生恢复力矩，
//   木板停在一个新的平衡角上；摆上重块时它会因为 tanθ > μ 而真的滑动。
// ---------------------------------------------------------------------------

// 板放在机械臂正前方、y ≈ 0 的直线上：
// 这样 j1 几乎不转，夹爪的开合方向始终与世界 Y 轴一致，能平平地夹住方块的侧面。
// 放在侧面（y = ±0.3）时 j1 要转 40°，手指变成"斜着夹"，方块很容易滑掉。
var PLATE_SIZE = [0.30, 0.30, 0.03];
var PLATE_POS = [0.36, 0.0, 0.12];
var PLATE_HALF = 0.15;
var PLATE_TOP = 0.015;          // 板中心 → 板顶

// 块取 32 mm：夹爪闭合时能夹住它的中上部
var BLOCK_SIZE = [0.032, 0.032, 0.032];
var BLOCK_MASS = 0.6;
var COLORS = ['#F59E0B', '#22D3EE', '#34D399', '#F472B6'];

var FINGER_DROP = 0.027;        // 手指中心在 TCP 下方约 2.7 cm
var DOWN_RX = 0;
var DOWN_RY = 90;
var DOWN_RZ = 0;

// 都摆在板外侧、贴近 y = 0 的位置（j1 转角小，夹持稳）
var PICK_SPOTS = [
    { x: 0.55, y: -0.06 },
    { x: 0.55, y: 0.06 },
    { x: 0.49, y: -0.12 },
    { x: 0.49, y: 0.12 }
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
        console.log('   运动未完成: ' + done.message);
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

function pick(px, py, pz) {
    arm.release();
    arm.sleep(600);

    movePose(px, py, 0.26, 0.7);
    movePose(px, py, pz + FINGER_DROP, 0.06);
    arm.grip(60);
    arm.sleep(2500);
    movePose(px, py, 0.27, 0.35);
}

function place(qx, qy) {
    var t = arm.tilt();
    var top = t.height + PLATE_TOP;

    movePose(qx, qy, top + 0.16, 0.5);
    movePose(qx, qy, top + FINGER_DROP + 0.03, 0.08);

    arm.release();
    arm.sleep(800);

    movePose(qx, qy, top + 0.18, 0.4);
}

function dropPoint(tilt, index) {
    if (index === 0) return { x: 0, y: 0 };

    return {
        x: clamp(tilt.tiltY * GAIN, -0.10, 0.10),
        y: clamp(-tilt.tiltX * GAIN, -0.10, 0.10)
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
    console.log('   拾取点 (' + at[0].toFixed(3) + ', ' + at[1].toFixed(3) + ')');

    pick(at[0], at[1], at[2]);

    var targetX = PLATE_POS[0] + offset.x;
    var targetY = PLATE_POS[1] + offset.y;

    console.log('   放到板上 (' + targetX.toFixed(3) + ', ' + targetY.toFixed(3) + ')' +
                '  补偿 dx=' + offset.x.toFixed(3) + ' dy=' + offset.y.toFixed(3));

    place(targetX, targetY);
    arm.sleep(900);
    report('   放置后:');
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
    console.log('   结论: BALANCED ✓');
} else {
    console.log('   结论: 仍偏斜 ' + last.tiltMag.toFixed(2) + '°（阈值 ' + BALANCED_DEG + '°）');
}

console.log('=== 完成 ===');
