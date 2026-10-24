// ---------------------------------------------------------------------------
// 搭积木房子：把桌面上的积木逐块搬到指定位置，堆成两层
//
// 用法: Controller.exe --js scripts/build_house.js
//
// 这个脚本演示的是"给神经网络做示范轨迹"的形状：
// 每个动作都是 pick(拾取点) → place(放置点)，两个点由上层规划给出。
// ---------------------------------------------------------------------------

var GRIP_FORCE = 45;
var LIFT_Z = 0.24;

// 桌面上的取件点（与默认积木的初始排布对应）
var picks = [
    { x: 0.300, y: -0.100 },
    { x: 0.375, y: -0.100 },
    { x: 0.450, y: -0.100 }
];

// 房子：两块并排做地基，第三块压在上面
var places = [
    { x: 0.380, y: -0.020, z: 0.035 },
    { x: 0.435, y: -0.020, z: 0.035 },
    { x: 0.408, y: -0.020, z: 0.080 }
];

function state() {
    return arm.state();
}

function move(x, y, z, speed) {
    var motion = arm.moveL(x, y, z, speed);
    var done = arm.waitMotion(motion.motionId, 60000);

    if (!done.done || !done.success) {
        throw new Error('运动失败: ' + done.message);
    }
}

function pick(appoach, zDown) {
    move(appoach.x, appoach.y, LIFT_Z, 0.3);
    move(appoach.x, appoach.y, zDown, 0.18);

    var result = arm.grip(GRIP_FORCE);
    arm.sleep(350);

    var s = state();
    console.log('   夹持力 ' + result.force + 'N  接触=' +
                (s.gripper.contact ? s.gripper.holdingId : '无'));

    move(appoach.x, appoach.y, LIFT_Z, 0.2);
}

function place(target) {
    move(target.x, target.y, LIFT_Z, 0.22);
    move(target.x, target.y, target.z, 0.15);
    arm.release();
    arm.sleep(300);
    move(target.x, target.y, LIFT_Z, 0.22);
}

console.log('=== 搭积木房子 ===');
arm.connect();

console.log('回零');
var home = arm.home(0.4);
arm.waitMotion(home.motionId, 60000);

for (var i = 0; i < picks.length; i++) {
    console.log('第 ' + (i + 1) + ' 块');

    arm.release();
    pick(picks[i], 0.085);

    var s = state();
    if (!s.gripper.contact) {
        console.log('   没夹到积木，跳过这一块');
        continue;
    }

    place(places[i]);
    console.log('   已放到 (' + places[i].x.toFixed(3) + ', ' +
                places[i].y.toFixed(3) + ', ' + places[i].z.toFixed(3) + ')');
}

arm.home(0.4);

var objects = arm.objects();
console.log('场景里现有 ' + objects.length + ' 个物体');

for (var k = 0; k < objects.length; k++) {
    var o = objects[k];
    console.log('  ' + o.id + '  ' + o.kind + '  z=' + o.position[2].toFixed(3));
}

console.log('=== 完成 ===');
