# -*- coding: utf-8 -*-
"""
资源自检：
  1. URDF 可解析、关节树连通、引用的 mesh 文件都存在
  2. GLB 结构正确
  3. **关键**：按 GlbLinkLoader 的方式（node 世界矩阵求逆 × Y-up→Z-up）
     还原每个 link 的顶点，与建模时的局部坐标逐点比对
  4. 检查足底是否落在 z=0、总质量、自由度
"""

import json
import os
import struct
import sys
import xml.etree.ElementTree as ET
from math import pi, sin, cos

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import model
from geom import m_point, m_mul

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
GLB = os.path.join(ROOT, "humanoid_robot.glb")
URDF = os.path.join(ROOT, "humanoid_robot.urdf")

ok = True


def fail(msg):
    global ok
    ok = False
    print("  [FAIL] " + msg)


def note(msg):
    print("  [ ok ] " + msg)


# --------------------------------------------------------------------------- #
#  GLB 读取
# --------------------------------------------------------------------------- #

def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, ver, total = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, "not a glb"
    off = 12
    js = None
    bin_ = None
    while off < total:
        clen, ctype = struct.unpack_from("<II", data, off)
        chunk = data[off + 8: off + 8 + clen]
        if ctype == 0x4E4F534A:
            js = json.loads(chunk.decode("utf-8").rstrip("\x00 "))
        elif ctype == 0x004E4942:
            bin_ = chunk
        off += 8 + clen + ((4 - clen % 4) % 4 if clen % 4 else 0)
    return js, bin_


def accessor_f32(gltf, buf, idx, n):
    acc = gltf["accessors"][idx]
    bv = gltf["bufferViews"][acc["bufferView"]]
    base = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    cnt = acc["count"] * n
    return list(struct.unpack_from("<%df" % cnt, buf, base))


def accessor_u32(gltf, buf, idx):
    acc = gltf["accessors"][idx]
    bv = gltf["bufferViews"][acc["bufferView"]]
    base = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    return list(struct.unpack_from("<%dI" % acc["count"], buf, base))


def node_matrix(node):
    """TRS → 4x4（列向量约定），与 glTF 规范一致。"""
    t = node.get("translation", [0, 0, 0])
    q = node.get("rotation", [0, 0, 0, 1])
    s = node.get("scale", [1, 1, 1])
    x, y, z, w = q
    r = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    m = [[r[i][j] * s[j] for j in range(3)] + [t[i]] for i in range(3)]
    m.append([0.0, 0.0, 0.0, 1.0])
    return m


def main():
    print("== 1. URDF ==")
    tree = ET.parse(URDF)
    root = tree.getroot()
    links = {e.get("name") for e in root.findall("link")}
    joints = root.findall("joint")
    children = {j.find("child").get("link") for j in joints}
    roots = links - children
    if len(roots) != 1:
        fail("根连杆数量 = %d（应为 1）: %s" % (len(roots), roots))
    else:
        note("根连杆 = %s，共 %d link / %d joint" % (list(roots)[0], len(links), len(joints)))

    if len(links) != len(joints) + 1:
        fail("link 数与 joint 数不符合树结构")
    else:
        note("关节树：link = joint + 1 ✓")

    missing = []
    for e in root.iter("mesh"):
        fn = e.get("filename")
        p = os.path.join(ROOT, fn.replace("/", os.sep))
        if not os.path.isfile(p):
            missing.append(fn)
    if missing:
        fail("缺少 %d 个 mesh：%s" % (len(missing), missing[:4]))
    else:
        note("全部 mesh 文件存在")

    mass = sum(float(e.find("mass").get("value")) for e in root.iter("inertial"))
    dof = sum(1 for j in joints if j.get("type") in ("revolute", "continuous"))
    note("总质量 %.2f kg，自由度 %d" % (mass, dof))

    print("== 2. GLB 结构 ==")
    gltf, buf = read_glb(GLB)
    names = [n.get("name") for n in gltf["nodes"]]
    note("%d nodes / %d meshes / %d materials / %.2f MB"
         % (len(names), len(gltf["meshes"]), len(gltf["materials"]),
            os.path.getsize(GLB) / 1048576.0))
    absent = links - set(names)
    if absent:
        fail("GLB 缺少 URDF link 节点：%s" % sorted(absent)[:5])
    else:
        note("GLB 节点名与 URDF link 一一对应 ✓")

    print("== 3. 坐标往返（模拟 GlbLinkLoader）==")
    # 加载器对"携带 mesh 的 link 节点"算出的 nodeToLink 恰为单位矩阵，
    # 所以顶点本身必须已经是 **link 局部坐标 + Y-up**。
    # 检验：YupToZup(原始顶点) 必须精确等于建模时的 URDF 局部顶点。
    parent = {}
    for i, n in enumerate(gltf["nodes"]):
        for c in n.get("children", []):
            parent[c] = i

    def world_of(i):
        m = node_matrix(gltf["nodes"][i])
        p = parent.get(i)
        while p is not None:
            m = m_mul(node_matrix(gltf["nodes"][p]), m)
            p = parent.get(p)
        return m

    rb = model.build()
    ref = {}
    for link in rb.links:
        if link.mesh is not None:
            ref[link.name] = link.mesh.finalize()

    def up2z(p):
        return (p[0], -p[2], p[1])

    import math
    worst = 0.0
    checked = 0
    for i, n in enumerate(gltf["nodes"]):
        if "mesh" not in n:
            continue
        name = n.get("name")
        if name not in ref:
            continue
        pos_l = ref[name][0]
        prim = gltf["meshes"][n["mesh"]]["primitives"][0]
        vs = accessor_f32(gltf, buf, prim["attributes"]["POSITION"], 3)
        for k in range(0, min(len(vs), 900), 3):
            q = up2z((vs[k], vs[k + 1], vs[k + 2]))
            d = min(math.dist(q, rp) for rp in pos_l)
            worst = max(worst, d)
            checked += 1

    if worst > 1e-4:
        fail("顶点往返最大偏差 %.6f m（应 <0.0001）" % worst)
    else:
        note("%d 个顶点：YupToZup 后与建模局部坐标一致，最大偏差 %.2e m ✓" % (checked, worst))

    # 节点世界变换必须等于 R_up × URDF 正运动学，保证 three.js 等标准渲染器位置正确
    ang = rb.pose_degrees({})
    fk = rb.forward(ang)
    R_UP = [[1, 0, 0], [0, 0, 1], [0, -1, 0]]     # (x,y,z) → (x, z, -y)
    worst2 = 0.0
    for i, n in enumerate(gltf["nodes"]):
        name = n.get("name")
        if name not in fk:
            continue
        W = fk[name]
        u = (W[0][3], W[1][3], W[2][3])
        expect = (u[0], u[2], -u[1])
        got = world_of(i)
        g = (got[0][3], got[1][3], got[2][3])
        worst2 = max(worst2, math.dist(expect, g))
    if worst2 > 1e-4:
        fail("节点世界位置与 FK 偏差 %.6f m" % worst2)
    else:
        note("节点世界位置与 URDF 正运动学一致（最大偏差 %.2e m）✓" % worst2)

    print("== 4. 姿态 / 落地 ==")
    w = rb.forward()
    lo = 1e9
    for name, (pos, nrm, faces, fmat) in ref.items():
        W = w[name]
        for p in pos:
            lo = min(lo, m_point(W, p)[2])
    if abs(lo) < 0.004:
        note("静止站立时足底 z = %.4f m（贴地）" % lo)
    else:
        fail("足底 z = %.4f（应 ≈0）" % lo)

    poses = json.load(open(os.path.join(ROOT, "poses.json"), encoding="utf-8"))
    known = {j.get("name") for j in joints}
    bad = []
    for pname, p in poses["poses"].items():
        for k in p["joints"]:
            if k not in known:
                bad.append((pname, k))
    if bad:
        fail("姿态里出现未知关节：%s" % bad[:4])
    else:
        note("%d 个姿态，全部关节名有效" % len(poses["poses"]))

    print("\n结果：" + ("全部通过" if ok else "存在问题"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
