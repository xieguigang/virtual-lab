// ---------------------------------------------------------------------------
// 三角瓶碰撞测试：机械臂横扫瓶口
//
// 用法: Controller.exe --js scripts\flask_sweep.js
//
// 场景:
//   * 机械臂左侧一张静态桌子，桌上摆 5 个三角瓶（溶液体积不同 → 质量不同）
//   * 机械臂抬到瓶口高度，从一个方向横向扫过整排瓶子
//   * 每个瓶子按物理结果分类：
//       - 被推移：位移超过 3 cm，但仍正立/轻微歪斜
//       - 被扫倒：倾角超过 55°
//       - 被扫落：质心显著低于桌面（已经掉到地面）
//
// 物理说明:
//   瓶子是真实的动态刚体（圆锥形碰撞体 + 玻璃摩擦 0.45），
//   机械臂是运动学刚体。碰撞、倾倒、滚落全部由物理引擎算出，
//   脚本只读取 state.objects 里的 posture / tiltDeg / displacement / fallen。
// ---------------------------------------------------------------------------

var TABLE = { x: 0.24, y: -0.23, z: 0.26, top: 0.28 };
var TABLE_SIZE = [0.30, 0.52, 0.04];
var LEG_SIZE = [0.03, 0.03, 0.24];

// 5 个瓶子的摆放（沿 y 排开，间距 0.11 m > 1.5 倍瓶底直径）
var FLASK_Y = [-0.44, -0.33, -0.22, -0.11, 0.00];
var FLASK_X = 0.24;
var FLASK_MASS = [0.10, 0.16, 0.22, 0.28, 0.34];
var FLASK_COLOR = ['#FEF3C7', '#FDE68A', '#FCD34D', '#FBBF24', '#F59E0B'];
var FLASK_FRICTION = 0.45;

var SWEEP_Z = 0.40;          // 略低于瓶口（瓶高 0.13 → 瓶口 0.41）
var SWEEP_START_Y = -0.52;
var SWEEP_END_Y = 0.06;
var SWEEP_SEGMENTS = 6;
var MOVE_TOLERANCE = 0.03;   // 3 cm 位移算"被推移"
var TILT_TIP = 55;           // 倾角超过它算"被扫倒"

// ---------------------------------------------------------------------------

function num(value) {
    if (value === undefined || value === null) return 0;

    var parsed = parseFloat(String(value));

    if (isNaN(parsed)) return 0;

    return parsed;
}

function parseVec(value) {
    if (value === undefined || value === null) return [0, 0, 0];

    if (typeof value === 'string') {
        var parts = value.indexOf(',') >= 0 ? value.split(',') : value.split(' ');

        return [num(parts[0]), num(parts[1]), num(parts[2])];
    }

    return [num(value[0]), num(value[1]), num(value[2])];
}

function moveTo(x, y, z, speed) {
    var motion = arm.moveTo(x, y, z, 0, 90, 0, speed);
    var done = arm.waitMotion(motion.motionId, 180000);

    if (!done.done || !done.success) {
        console.log('   运动未完成: ' + done.message);
    }
}

/** 笛卡尔直线（夹着东西或要精确扫掠时用）；strict=false 允许姿态随动 */
function moveStraight(x, y, z, speed, strict) {
    try {
        var motion = arm.moveLPose(x, y, z, 0, 90, 0, speed, strict);
        var done = arm.waitMotion(motion.motionId, 180000);

        if (!done.done || !done.success) throw new Error(done.message);

        return true;
    } catch (err) {
        console.log('   · 直线运动不可达，退回关节插值');
    }

    moveTo(x, y, z, speed);

    return false;
}

function flasks() {
    var all = arm.objects();
    var out = [];

    for (var i = 0; i < all.length; i++) {
        if (all[i].kind === 'flask') out.push(all[i]);
    }

    return out;
}

function reportFlask(item, index) {
    var where = parseVec(item.position);
    var tilt = num(item.tiltDeg);
    var moved = num(item.displacement);
    var fallen = item.fallen === true;

    var verdict;

    if (fallen) {
        verdict = tilt >= TILT_TIP ? '掉到地面·倾倒' : '掉到地面·正立';
    } else if (tilt >= TILT_TIP) {
        verdict = '被扫倒（还在桌上）';
    } else if (moved > MOVE_TOLERANCE) {
        verdict = '被推移 ' + (moved * 1000).toFixed(0) + ' mm';
    } else {
        verdict = '基本没动';
    }

    console.log('   瓶 ' + (index + 1) + ' (' + item.id + '): ' + verdict +
                '  tilt=' + tilt.toFixed(1) + '°' +
                '  disp=' + (moved * 1000).toFixed(0) + ' mm' +
                '  z=' + where[2].toFixed(3));
}

// ---------------------------------------------------------------------------

console.log('=== 三角瓶碰撞测试（横扫瓶口）===');
arm.connect();
arm.clear();
arm.release();
arm.sleep(600);

var home = arm.home(0.6);
arm.waitMotion(home.motionId, 120000);

console.log('1) 搭左桌（静态）');
arm.spawnStatic('box', TABLE_SIZE, [TABLE.x, TABLE.y, TABLE.z], 50, 0.8, '#CBD5E1');

var legZ = LEG_SIZE[2] * 0.5;
var legDx = TABLE_SIZE[0] * 0.5 - LEG_SIZE[0];
var legDy = TABLE_SIZE[1] * 0.5 - LEG_SIZE[1];

arm.spawnStatic('box', LEG_SIZE, [TABLE.x - legDx, TABLE.y - legDy, legZ], 10, 0.8, '#94A3B8');
arm.spawnStatic('box', LEG_SIZE, [TABLE.x + legDx, TABLE.y - legDy, legZ], 10, 0.8, '#94A3B8');
arm.spawnStatic('box', LEG_SIZE, [TABLE.x - legDx, TABLE.y + legDy, legZ], 10, 0.8, '#94A3B8');
arm.spawnStatic('box', LEG_SIZE, [TABLE.x + legDx, TABLE.y + legDy, legZ], 10, 0.8, '#94A3B8');

console.log('2) 桌上摆 5 个三角瓶');
for (var i = 0; i < FLASK_Y.length; i++) {
    arm.spawn('flask', [0.07, 0.07, 0.13], [FLASK_X, FLASK_Y[i], TABLE.top],
              FLASK_MASS[i], FLASK_FRICTION, FLASK_COLOR[i]);
}

arm.sleep(1500);

console.log('3) 横扫瓶口（z = ' + SWEEP_Z.toFixed(2) + ' m，从 y=' + SWEEP_START_Y + ' 到 y=' + SWEEP_END_Y + '）');

moveStraight(FLASK_X, SWEEP_START_Y, SWEEP_Z, 0.35, false);
arm.sleep(300);

for (var s = 1; s <= SWEEP_SEGMENTS; s++) {
    var y = SWEEP_START_Y + (SWEEP_END_Y - SWEEP_START_Y) * s / SWEEP_SEGMENTS;

    moveStraight(FLASK_X, y, SWEEP_Z, 0.45, false);
}

arm.sleep(1500);

console.log('4) 结果');
var list = flasks();
var pushed = 0;
var tipped = 0;
var dropped = 0;

for (var k = 0; k < list.length; k++) {
    var item = list[k];

    reportFlask(item, k);

    if (item.fallen === true) {
        dropped++;
    } else if (num(item.tiltDeg) >= TILT_TIP) {
        tipped++;
    } else if (num(item.displacement) > MOVE_TOLERANCE) {
        pushed++;
    }
}

console.log('   汇总: 推移 ' + pushed + ' / 扫倒 ' + tipped + ' / 扫落 ' + dropped +
            ' / 共 ' + list.length + ' 个');

// ---- 相机：扫掠前后各取一帧，并量化"多少个瓶子在画面里" ----
try {
    arm.saveFrame('flask-sweep.png', true);
    console.log('   相机画面已保存: flask-sweep.png');
} catch (err) {
    console.log('   取帧失败: ' + err.message);
}

console.log('=== 完成 ===');
