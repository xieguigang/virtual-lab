// ---------------------------------------------------------------------------
// 积木形状演示：在桌面上方生成各种形状的积木，让它们在真实重力/摩擦下落下
//
// 用法: Controller.exe --js scripts/blocks_demo.js
// ---------------------------------------------------------------------------

var shapes = [
    { shape: 'box', size: [0.05, 0.05, 0.05], mass: 0.25, color: '#F59E0B' },
    { shape: 'sphere', size: [0.045], mass: 0.12, color: '#22D3EE' },
    { shape: 'cylinder', size: [0.04, 0.05], mass: 0.18, color: '#34D399' },
    { shape: 'cone', size: [0.045, 0.05], mass: 0.12, color: '#F472B6' },
    { shape: 'frustum', size: [0.055, 0.025, 0.045], mass: 0.16, color: '#A78BFA' },
    { shape: 'hemisphere', size: [0.05], mass: 0.14, color: '#FBBF24' },
    { shape: 'prism', size: [6, 0.05, 0.045], mass: 0.2, color: '#60A5FA' },
    { shape: 'pyramid', size: [4, 0.055, 0.05], mass: 0.15, color: '#22C55E' },
    { shape: 'disc', size: [0.055, 0.014], mass: 0.06, color: '#38BDF8' }
];

console.log('=== 积木形状演示 ===');
arm.connect();
arm.setRealtime(2.0);

console.log('清空场景');
arm.clear();

console.log('生成 ' + shapes.length + ' 块积木（离桌面 15 cm，让它们自由下落）');

for (var i = 0; i < shapes.length; i++) {
    var spec = shapes[i];
    var x = 0.28 + (i % 3) * 0.075;
    var y = -0.10 + Math.floor(i / 3) * 0.075;

    var created = arm.spawn(spec.shape, spec.size, [x, y, 0.15], spec.mass, 0.75, spec.color);

    console.log('  ' + created.id + '  ' + created.kind + '  质量 ' + spec.mass + ' kg');
}

console.log();
console.log('等待 2 秒让物理稳定…');
arm.sleep(2000);

var objects = arm.objects();

console.log('落地结果:');

for (var k = 0; k < objects.length; k++) {
    var o = objects[k];

    console.log('  ' + o.id + '  ' + o.kind +
                '  z=' + o.position[2].toFixed(4) +
                '  |v|=' + Math.sqrt(
                    o.velocity[0] * o.velocity[0] +
                    o.velocity[1] * o.velocity[1] +
                    o.velocity[2] * o.velocity[2]).toFixed(4));
}

console.log('=== 完成 ===');
