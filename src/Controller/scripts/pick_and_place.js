// ---------------------------------------------------------------------------
// 抓取-搬运-放下演示
//
// 用法: Controller.exe --js scripts/pick_and_place.js
//
// 说明:
//   * 六轴臂基座在原点、零位沿 +X 伸出；要夹桌面上的积木必须先把末端转成
//     "朝下"（ry = 90°），否则夹爪是横着伸过去的、手指碰不到积木。
//   * 目标位置**从 arm.objects() 实时读取**，而不是写死坐标——机械臂接近时
//     可能已经把积木推了几厘米，闭环读取才是正确的做法（也给神经网络做了示范）。
// ---------------------------------------------------------------------------


var DOWN_RX = 0;
var DOWN_RY = 90;
var DOWN_RZ = 0;

// 末端朝下时手指中心在 TCP 下方约 2.7 cm（手指 x=0.068、TCP x=0.095）
var FINGER_DROP = 0.027;

function show(label) {
    var s = arm.state();

    console.log(label + '  tcp=(' + s.tcp[0].toFixed(3) + ', ' + s.tcp[1].toFixed(3) +
                ', ' + s.tcp[2].toFixed(3) + ')  ' +
                '开口=' + s.gripper.opening.toFixed(4) + ' m');
}

function movePose(x, y, z, speed) {
    var motion = arm.moveTo(x, y, z, DOWN_RX, DOWN_RY, DOWN_RZ, speed);
    var done = arm.waitMotion(motion.motionId, 180000);

    if (!done.done || !done.success) {
        console.log('   运动未完成: ' + done.message);
    }

    return motion;
}

function findObject(id) {
    var objects = arm.objects();

    for (var i = 0; i < objects.length; i++) {
        if (objects[i].id === id) return objects[i];
    }

    return null;
}

console.log('=== 抓取-搬运-放下 ===');
arm.connect();
console.log('已连接: ' + arm.info().simMode);

// 先清空场景，只留一块 32 mm 的立方体当目标：
// 桌面上的其它积木会被接近过程中的连杆扫到，干扰抓取
arm.clear();

var spawned = arm.spawn('box', [0.032, 0.032, 0.032], [0.300, 0.000, 0.120], 0.12, 0.75, '#F59E0B');
var TARGET_ID = spawned.id;

console.log('目标积木: ' + TARGET_ID);
arm.sleep(900);

console.log('1) 张开夹爪');
arm.release();
arm.sleep(600);

console.log('2) 回零');
var home = arm.home(0.6);
arm.waitMotion(home.motionId, 180000);
show('   ');

var target = findObject(TARGET_ID);

if (target === null) {
    console.log('找不到 ' + TARGET_ID + '，退出');
} else {
    var px = target.position[0];
    var py = target.position[1];
    var pz = target.position[2];

    console.log('3) 目标 ' + TARGET_ID + ' 在 (' + px.toFixed(3) + ', ' + py.toFixed(3) + ', ' + pz.toFixed(3) + ')');

    console.log('4) 抬高到积木上方（末端朝下，避免连杆扫到桌面上的积木）');
    movePose(px, py, 0.36, 0.7);
    show('   ');

    console.log('5) 垂直下降到积木两侧');
    movePose(px, py, pz + FINGER_DROP, 0.06);
    show('   ');

    console.log('6) 夹紧');
    var grip = arm.grip(60);

    // 手指以 25 mm/s 闭合，从全开 96 mm 夹到 32 mm 需要 1.3 s 左右，
    // 必须等它真正压上积木再读状态
    arm.sleep(2500);

    var afterGrip = arm.state();

    console.log('   夹持力 ' + grip.force + ' N   开口 ' + grip.opening.toFixed(4) + ' m   ' +
                (afterGrip.gripper.contact ? '已接触 ' + afterGrip.gripper.holdingId : '未检测到接触'));

    console.log('7) 抬起');
    movePose(px, py, pz + 0.20, 0.2);
    show('   ');

    console.log('8) 搬运');
    movePose(0.43, 0.13, pz + 0.20, 0.6);
    show('   ');

    console.log('9) 放下');
    movePose(0.43, 0.13, pz + FINGER_DROP, 0.3);
    arm.release();
    arm.sleep(400);
    show('   ');

    var moved = findObject(TARGET_ID);

    if (moved !== null) {
        console.log('   积木最终位置 (' + moved.position[0].toFixed(3) + ', ' +
                    moved.position[1].toFixed(3) + ', ' + moved.position[2].toFixed(3) + ')');
    }
}

console.log('=== 完成 ===');
