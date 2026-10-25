// ---------------------------------------------------------------------------
// 三角瓶搬运：左桌 5 瓶 → 右桌
//
// 用法: Controller.exe --js scripts\flask_transfer.js
//
// 场景:
//   * 机械臂左右各一张静态桌子（桌面 z = 0.28 m）
//   * 左桌 5 个三角瓶，溶液体积不同 → 质量 0.10 ~ 0.34 kg
//   * 逐个夹住瓶身、抬升、平移到右桌、放下
//
// 判定（不删除物体，失败的瓶子留在场景里）:
//   * 成功：落到右桌范围且姿态为 upright / tilted
//   * 倾倒：倾角 ≥ 55°
//   * 掉地：质心显著低于桌面
//
// 物理说明:
//   瓶子是动态刚体，机械臂是运动学刚体，碰撞与倾倒由物理引擎算出。
//   搬运用笛卡尔直线 + 锁定姿态（moveLHold），不可达时自动退回关节插值。
// ---------------------------------------------------------------------------

var TABLE_TOP = 0.28;
var TABLE_Z = 0.26;
var TABLE_SIZE = [0.30, 0.46, 0.04];
var LEG_SIZE = [0.03, 0.03, 0.24];

var LEFT = { x: 0.24, y: -0.19 };
var RIGHT = { x: 0.24, y: 0.19 };

var FLASK_MASS = [0.10, 0.16, 0.22, 0.28, 0.34];
var FLASK_COLOR = ['#FEF3C7', '#FDE68A', '#FCD34D', '#FBBF24', '#F59E0B'];
var FLASK_FRICTION = 0.45;

// 瓶身夹持高度（相对瓶底）：瓶高 0.13 → 夹在中上部
// 夹具中心落在瓶身中部时 TCP 的高度（实测 0.28 + 0.072 = 0.352 能稳住）
var GRASP_TCP = 0.352;

var TILT_TIP = 55;
var CARRY_SPEED = 0.16;
var LIFT_Z = 0.44;

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
        throw new Error('运动未完成: ' + done.message);
    }
}

/** 笛卡尔直线 + 锁定姿态；不可达时退回关节插值（侧向低处常见） */
function moveStraight(x, y, z, speed) {
    try {
        var motion = arm.moveLHold(x, y, z, speed);
        var done = arm.waitMotion(motion.motionId, 180000);

        if (!done.done || !done.success) throw new Error(done.message);

        return;
    } catch (err) {
        console.log('   · 严格姿态不可达，退回关节插值');
    }

    try {
        moveTo(x, y, z, speed);
    } catch (err2) {
        console.log('   · 关节插值也够不到: ' + err2.message);
    }
}

function buildTable(centerY, label) {
    arm.spawnStatic('box', TABLE_SIZE, [LEFT.x, centerY, TABLE_Z], 50, 0.8, '#CBD5E1');

    var legZ = LEG_SIZE[2] * 0.5;
    var dx = TABLE_SIZE[0] * 0.5 - LEG_SIZE[0];
    var dy = TABLE_SIZE[1] * 0.5 - LEG_SIZE[1];

    arm.spawnStatic('box', LEG_SIZE, [LEFT.x - dx, centerY - dy, legZ], 10, 0.8, '#94A3B8');
    arm.spawnStatic('box', LEG_SIZE, [LEFT.x + dx, centerY - dy, legZ], 10, 0.8, '#94A3B8');
    arm.spawnStatic('box', LEG_SIZE, [LEFT.x - dx, centerY + dy, legZ], 10, 0.8, '#94A3B8');
    arm.spawnStatic('box', LEG_SIZE, [LEFT.x + dx, centerY + dy, legZ], 10, 0.8, '#94A3B8');

    console.log('   ' + label + ': 桌面中心 y=' + centerY.toFixed(2));
}

function flasks() {
    var all = arm.objects();
    var out = [];

    for (var i = 0; i < all.length; i++) {
        if (all[i].kind === 'flask') out.push(all[i]);
    }

    return out;
}

function isOnRightTable(item) {
    var p = parseVec(item.position);

    return Math.abs(p[1] - RIGHT.y) < TABLE_SIZE[1] * 0.5 + 0.02 &&
           Math.abs(p[0] - RIGHT.x) < TABLE_SIZE[0] * 0.5 + 0.02;
}

// ---------------------------------------------------------------------------

console.log('=== 三角瓶搬运（左桌 → 右桌）===');
arm.connect();
arm.clear();
arm.release();
arm.sleep(600);

var home = arm.home(0.6);
arm.waitMotion(home.motionId, 120000);

console.log('1) 搭左右两张桌子');
buildTable(LEFT.y, '左桌');
buildTable(RIGHT.y, '右桌');

console.log('2) 左桌摆 5 个三角瓶');
var slots = [];

for (var i = 0; i < FLASK_MASS.length; i++) {
    var y = LEFT.y - 0.18 + i * 0.09;

    slots.push(y);

    arm.spawn('flask', [0.07, 0.07, 0.13], [LEFT.x, y, TABLE_TOP],
              FLASK_MASS[i], FLASK_FRICTION, FLASK_COLOR[i]);
}

arm.sleep(1500);

console.log('3) 逐个搬运');

var success = 0;
var tipped = 0;
var dropped = 0;

for (var n = 0; n < slots.length; n++) {
    var list = flasks();

    if (n >= list.length) break;

    var item = list[n];
    var at = parseVec(item.position);
    var targetY = RIGHT.y + (at[1] - LEFT.y);

    console.log('--- 第 ' + (n + 1) + ' 个 ' + item.id + ' (mass ' + FLASK_MASS[n].toFixed(2) + ' kg) ---');
    console.log('   起点 (' + at[0].toFixed(3) + ', ' + at[1].toFixed(3) + ', ' + at[2].toFixed(3) + ')');
    console.log('   终点 (' + RIGHT.x.toFixed(3) + ', ' + targetY.toFixed(3) + ')');

    arm.release();
    arm.sleep(500);

    moveTo(at[0], at[1], TABLE_TOP + 0.20, 0.5);
    moveStraight(at[0], at[1], GRASP_TCP, 0.10);

    arm.grip(60);
    arm.sleep(2500);

    var gs = arm.state().gripper;

    console.log('   夹持: contact=' + gs.contact + '  open=' + gs.opening.toFixed(3));

    moveStraight(at[0], at[1], LIFT_Z, 0.14);

    // 平移到右桌（分 4 段，避免一次关节插值走大弧线）
    for (var seg = 1; seg <= 4; seg++) {
        var mx = at[0] + (RIGHT.x - at[0]) * seg / 4;
        var my = at[1] + (targetY - at[1]) * seg / 4;

        moveStraight(mx, my, LIFT_Z, CARRY_SPEED);
    }

    moveStraight(RIGHT.x, targetY, GRASP_TCP + 0.012, 0.10);

    arm.release();
    arm.sleep(1200);

    moveStraight(RIGHT.x, targetY, LIFT_Z, 0.20);
    arm.sleep(800);

    // ---- 判定 ----
    var after = flasks()[n];
    var where = parseVec(after.position);
    var tilt = num(after.tiltDeg);

    if (after.fallen === true) {
        dropped++;
        console.log('   ✗ 掉到地面  tilt=' + tilt.toFixed(1) + '°  z=' + where[2].toFixed(3));
    } else if (tilt >= TILT_TIP) {
        tipped++;
        console.log('   ✗ 倾倒  tilt=' + tilt.toFixed(1) + '°');
    } else if (isOnRightTable(after)) {
        success++;
        console.log('   ✓ 成功搬到右桌  tilt=' + tilt.toFixed(1) + '°');
    } else {
        tipped++;
        console.log('   ✗ 没到右桌  pos=(' + where[0].toFixed(2) + ', ' + where[1].toFixed(2) + ', ' + where[2].toFixed(2) + ')');
    }
}

console.log('4) 汇总');
console.log('   成功 ' + success + ' / 倾倒 ' + tipped + ' / 掉地 ' + dropped +
            ' / 共 ' + FLASK_MASS.length + ' 个');

try {
    arm.saveFrame('flask-transfer.png', true);
    console.log('   相机画面已保存: flask-transfer.png');
} catch (err) {
    console.log('   取帧失败: ' + err.message);
}

console.log('=== 完成 ===');
