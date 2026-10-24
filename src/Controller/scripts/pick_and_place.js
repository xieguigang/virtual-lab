// ---------------------------------------------------------------------------
// 抓取-搬运-放下演示
//
// 用法: Controller.exe --js scripts/pick_and_place.js
//
// 说明: 虚拟硬件默认的六轴臂基座在原点、沿 +X 伸出，夹爪沿手腕侧向开合，
//       所以这里用 moveL 从正上方接近桌面上的积木。
// ---------------------------------------------------------------------------

var PICK_X = 0.30;
var PICK_Y = -0.10;
var PLACE_X = 0.42;
var PLACE_Y = 0.13;

function show(label) {
    var s = arm.state();
    var q = s.q;
    console.log(label + '  tcp=(' + s.tcp[0].toFixed(3) + ', ' +
                s.tcp[1].toFixed(3) + ', ' + s.tcp[2].toFixed(3) + ')  ' +
                '开口=' + s.gripper.opening.toFixed(4) + ' m');
}

function moveTo(x, y, z, speed) {
    var motion = arm.moveL(x, y, z, speed);
    var done = arm.waitMotion(motion.motionId, 60000);
    if (!done.done || !done.success) {
        console.log('  运动未完成: ' + done.message);
    }
    return motion;
}

console.log('=== 抓取-搬运-放下 ===');
arm.connect();
console.log('已连接: ' + arm.info().simMode);

console.log('1) 张开夹爪');
arm.release();
console.log('   开口 = ' + arm.state().gripper.opening.toFixed(4) + ' m');

console.log('2) 回零');
var home = arm.home(0.4);
arm.waitMotion(home.motionId, 60000);
show('   ');

console.log('3) 移动到积木上方');
moveTo(PICK_X, PICK_Y, 0.22, 0.25);
show('   ');

console.log('4) 下降到积木');
moveTo(PICK_X, PICK_Y, 0.085, 0.18);
show('   ');

console.log('5) 夹紧');
var grip = arm.grip(50);
console.log('   夹持力 = ' + grip.force + ' N   开口 = ' + grip.opening.toFixed(4) + ' m');
arm.sleep(500);

var afterGrip = arm.state();
if (afterGrip.gripper.contact) {
    console.log('   已接触: ' + afterGrip.gripper.holdingId);
} else {
    console.log('   未检测到接触（可能没夹到积木）');
}

console.log('6) 抬起');
moveTo(PICK_X, PICK_Y, 0.26, 0.2);
show('   ');

console.log('7) 搬运');
moveTo(PLACE_X, PLACE_Y, 0.26, 0.2);
show('   ');

console.log('8) 放下');
moveTo(PLACE_X, PLACE_Y, 0.09, 0.18);
arm.release();
arm.sleep(400);
show('   ');

console.log('=== 完成 ===');
