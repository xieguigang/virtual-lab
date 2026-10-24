// ---------------------------------------------------------------------------
// 夹爪第一视角取图演示
//
// 用法: Controller.exe --js scripts/camera_snapshot.js
//
// 说明：
//   arm.frame(format, depth)        取一帧（png / rgb，深度可选 f32 / u16mm）
//   arm.saveFrame(path, depth)      取一帧并写盘，depth=true 时另存 .depth.f32
// ---------------------------------------------------------------------------

console.log('=== 夹爪第一视角取图 ===');
arm.connect();

console.log('1) 手腕转朝下，俯视桌面');
var motion = arm.moveTo(0.32, 0.0, 0.18, 0, 90, 0, 1.0);
arm.waitMotion(motion.motionId, 60000);
arm.sleep(400);

console.log('2) 取一帧（PNG + 深度）');
var info = arm.frame('png', 'f32');

console.log('   ' + info.width + 'x' + info.height +
            '   渲染 ' + info.captureMs.toFixed(2) + ' ms');
console.log('   光心 ' + JSON.stringify(info.camera.position) +
            '   rpy ' + JSON.stringify(info.camera.rpy));

console.log('3) 存盘');
var saved = arm.saveFrame('camera-shot.png', true);

console.log('   ' + saved.path + '  ' + saved.bytes + ' 字节（同名 .depth.f32 为深度图）');

console.log('4) 转动手腕，连续采样 3 帧');
for (var i = 0; i < 3; i++) {
    arm.moveJRel([0, 0, 0, 0, 0, 0.2], 0.6);
    arm.sleep(500);

    var shot = arm.frame('rgb');

    console.log('   第 ' + (i + 1) + ' 帧: ' + shot.width + 'x' + shot.height +
                '  rgb base64 长度 ' + shot.data.length);
}

console.log('=== 完成 ===');
