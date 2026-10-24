// ---------------------------------------------------------------------------
// 关节扫掠自检：逐个关节小幅摆动，验证限位、到位与状态反馈
//
// 用法: Controller.exe --js scripts/joint_sweep.js
// ---------------------------------------------------------------------------

var names = ['J1 腰', 'J2 肩', 'J3 肘', 'J4 腕转', 'J5 腕摆', 'J6 腕旋'];
var amplitudes = [0.35, 0.30, 0.35, 0.60, 0.30, 0.60];

console.log('=== 关节扫掠自检 ===');
arm.connect();

var info = arm.info();
console.log('关节数 ' + info.joints + '   模式 ' + info.simMode);

var home = arm.home(0.5);
arm.waitMotion(home.motionId, 60000);

var failed = 0;

for (var j = 0; j < 6; j++) {
    var q = [0, 0, 0, 0, 0, 0];

    q[j] = amplitudes[j];

    var motion = arm.moveJ(q, 0.5);
    var done = arm.waitMotion(motion.motionId, 60000);

    var state = arm.state();
    var reached = state.q[j];
    var error = Math.abs(reached - amplitudes[j]);

    console.log(names[j] + ': 目标 ' + amplitudes[j].toFixed(3) +
                '  实际 ' + reached.toFixed(3) +
                '  误差 ' + error.toFixed(4) +
                (done.success ? '  到位' : '  失败: ' + done.message));

    if (error > 0.02) {
        failed++;
    }

    q[j] = -amplitudes[j];

    motion = arm.moveJ(q, 0.5);
    arm.waitMotion(motion.motionId, 60000);
}

arm.home(0.5);

var finalState = arm.state();

console.log();
console.log('最终 TCP: (' + finalState.tcp[0].toFixed(3) + ', ' +
            finalState.tcp[1].toFixed(3) + ', ' + finalState.tcp[2].toFixed(3) + ')');
console.log('失败关节数: ' + failed);
console.log('=== 完成 ===');
