# -*- coding: utf-8 -*-
"""
导出器：GLB（整机，Y-up，一节点一 link）+ OBJ/MTL（按 link）+ URDF。

GLB 坐标约定与 src/VirtualWorkshop/Model/GlbLinkLoader.vb 一致：
    * glTF 原生 **Y-up**：URDF 的 (x, y, z) → glTF 的 (x, z, -y)
    * 节点名 = URDF link 名，节点层次 = URDF 关节树
    * 网格顶点存在"各自 link 的局部系"里，并已做同样的 Y-up 旋转
      → 加载器 nodeToLink * YupToZup 之后正好还原成 URDF 局部坐标
"""

import json
import os
import struct
from math import pi

from geom import m_ident, m_mul, m_rot_axis, m_inv_rigid, m_point, m_dir, m_to_quat

R_UP = m_rot_axis((1, 0, 0), -pi / 2)        # URDF(Z-up) → glTF(Y-up)
R_UP_INV = m_inv_rigid(R_UP)


# --------------------------------------------------------------------------- #
#  GLB
# --------------------------------------------------------------------------- #

def _pad4(b):
    return b + b"\x00" * ((4 - len(b) % 4) % 4)


def write_glb(path, rb, worlds, geoms, materials, node_extra=None, extensions_used=None):
    """geoms[link] = (pos, nrm, faces, fmat)，均为 URDF 局部坐标。"""
    buf = bytearray()
    accessors = []
    buffer_views = []
    meshes = []
    nodes = []

    # ---- 先在 buffer 里铺好所有 link 的几何，同时记录 mesh 索引 ----
    mesh_index_of = {}
    for link in rb.links:
        g = geoms.get(link.name)
        if not g:
            continue
        pos, nrm, faces, fmat = g
        if not faces:
            continue

        # 按材质切分图元
        groups = {}
        for f, m in zip(faces, fmat):
            groups.setdefault(m, []).append(f)
        if not groups:
            continue

        primitives = []
        for mat_id in sorted(groups):
            fl = groups[mat_id]
            # 顶点重排为紧凑数组
            remap = {}
            vp = []
            vn = []
            idx = []
            for (a, b, c) in fl:
                for vi in (a, b, c):
                    j = remap.get(vi)
                    if j is None:
                        j = len(vp)
                        remap[vi] = j
                        vp.append(m_point(R_UP, pos[vi]))
                        vn.append(m_dir(R_UP, nrm[vi]))
                    idx.append(j)

            # --- POSITION ---
            pbytes = b"".join(struct.pack("<fff", *p) for p in vp)
            while len(buf) % 4:
                buf += b"\x00"
            off = len(buf)
            buf += pbytes
            buffer_views.append({"buffer": 0, "byteOffset": off,
                                 "byteLength": len(pbytes), "target": 34962})
            mins = [min(p[i] for p in vp) for i in range(3)]
            maxs = [max(p[i] for p in vp) for i in range(3)]
            acc_pos = len(accessors)
            accessors.append({"bufferView": len(buffer_views) - 1,
                              "componentType": 5126, "count": len(vp),
                              "type": "VEC3", "min": mins, "max": maxs})

            # --- NORMAL ---
            nbytes = b"".join(struct.pack("<fff", *n) for n in vn)
            while len(buf) % 4:
                buf += b"\x00"
            off = len(buf)
            buf += nbytes
            buffer_views.append({"buffer": 0, "byteOffset": off,
                                 "byteLength": len(nbytes), "target": 34962})
            acc_nrm = len(accessors)
            accessors.append({"bufferView": len(buffer_views) - 1,
                              "componentType": 5126, "count": len(vn),
                              "type": "VEC3"})

            # --- INDICES ---
            ibytes = b"".join(struct.pack("<I", i) for i in idx)
            while len(buf) % 4:
                buf += b"\x00"
            off = len(buf)
            buf += ibytes
            buffer_views.append({"buffer": 0, "byteOffset": off,
                                 "byteLength": len(ibytes), "target": 34963})
            acc_idx = len(accessors)
            accessors.append({"bufferView": len(buffer_views) - 1,
                              "componentType": 5125, "count": len(idx),
                              "type": "SCALAR"})

            primitives.append({
                "attributes": {"POSITION": acc_pos, "NORMAL": acc_nrm},
                "indices": acc_idx,
                "material": mat_id if mat_id < len(materials) else 0,
                "mode": 4,
            })

        mesh_index_of[link.name] = len(meshes)
        meshes.append({"name": link.name, "primitives": primitives})

    # ---- 节点（层次 = 关节树） ----
    by_name = {}
    children = {}
    roots = []
    parent_of = {}
    for j in rb.joints:
        parent_of[j.child] = j

    node_index = {}
    for link in rb.links:
        node = {"name": link.name}
        if link.name in mesh_index_of:
            node["mesh"] = mesh_index_of[link.name]
        node_index[link.name] = len(nodes)
        nodes.append(node)
        by_name[link.name] = node

    for link in rb.links:
        j = parent_of.get(link.name)
        if j is None:
            roots.append(link.name)
            continue
        children.setdefault(j.parent, []).append(link.name)

    for parent, kids in children.items():
        pn = by_name.get(parent)
        if pn is None:
            continue
        pn["children"] = [node_index[k] for k in kids]

    # 局部变换：R_local = R * L_urdf * R^-1
    for link in rb.links:
        j = parent_of.get(link.name)
        if j is None:
            continue
        L = m_mul(m_trans_local(j), m_rot_rpy_local(j))
        M = m_mul(m_mul(R_UP, L), R_UP_INV)
        t = (M[0][3], M[1][3], M[2][3])
        node = by_name[link.name]
        if any(abs(v) > 1e-9 for v in t):
            node["translation"] = [t[0], t[1], t[2]]
        rot = [[M[i][k] for k in range(3)] for i in range(3)]
        q = m_to_quat(rot)
        if abs(abs(q[3]) - 1.0) > 1e-9:
            node["rotation"] = [q[0], q[1], q[2], q[3]]

    # ---- 组 JSON ----
    gltf = {
        "asset": {"version": "2.0", "generator": "virtual-lab humanoid_robot builder"},
        "scene": 0,
        "scenes": [{"nodes": [node_index[r] for r in roots]}],
        "nodes": nodes,
        "meshes": meshes,
        "materials": materials,
        "accessors": accessors,
        "bufferViews": buffer_views,
        "buffers": [{"byteLength": len(buf)}],
    }
    if node_extra:
        gltf["extras"] = node_extra
    if extensions_used:
        gltf["extensionsUsed"] = list(extensions_used)

    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    if len(js) % 4:
        js = js + b" " * (4 - len(js) % 4)
    while len(buf) % 4:
        buf += b"\x00"

    total = 12 + 8 + len(js) + 8 + len(buf)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total))
        f.write(struct.pack("<II", len(js), 0x4E4F534A))
        f.write(js)
        f.write(struct.pack("<II", len(buf), 0x004E4942))
        f.write(bytes(buf))
    return total


def m_trans_local(j):
    from geom import m_trans
    return m_trans(j.xyz)


def m_rot_rpy_local(j):
    from geom import m_rot_rpy
    return m_rot_rpy(*j.rpy)


# --------------------------------------------------------------------------- #
#  OBJ / MTL
# --------------------------------------------------------------------------- #

def write_mtl(path, materials):
    lines = []
    for (name, rgba, rough, metal, emis, estr) in materials:
        lines.append("newmtl %s" % name)
        lines.append("Ka 0.10 0.10 0.11")
        lines.append("Kd %.4f %.4f %.4f" % (rgba[0], rgba[1], rgba[2]))
        if estr > 0.0:
            k = min(1.0, estr)
            lines.append("Ke %.4f %.4f %.4f" % (emis[0] * k, emis[1] * k, emis[2] * k))
        ks = 1.0 - rough * 0.6
        lines.append("Ks %.3f %.3f %.3f" % (ks, ks, ks))
        lines.append("Ns %d" % int(max(1.0, (1.0 - rough) ** 2 * 900)))
        lines.append("Ni 1.45")
        lines.append("d 1.0")
        lines.append("illum 2")
        lines.append("")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def write_obj(path, name, geom, materials, mtl_file="robot.mtl"):
    pos, nrm, faces, fmat = geom
    out = []
    out.append("# %s  (URDF local frame, Z-up, unit = m)" % name)
    out.append("mtllib %s" % mtl_file)
    out.append("o %s" % name)
    for p in pos:
        out.append("v %.6f %.6f %.6f" % p)
    for n in nrm:
        out.append("vn %.6f %.6f %.6f" % n)

    cur = None
    for f, m in zip(faces, fmat):
        if m != cur:
            cur = m
            out.append("usemtl %s" % materials[m][0])
        a, b, c = f
        out.append("f %d//%d %d//%d %d//%d" % (a + 1, a + 1, b + 1, b + 1, c + 1, c + 1))
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")


# --------------------------------------------------------------------------- #
#  URDF
# --------------------------------------------------------------------------- #

def _f(v, nd=6):
    return ("%." + str(nd) + "f") % v


def _v3(t, nd=6):
    return "%s %s %s" % (_f(t[0], nd), _f(t[1], nd), _f(t[2], nd))


def _inertia(mass, ext):
    sx, sy, sz = ext
    k = 0.9
    ixx = mass * k / 12.0 * (sy * sy + sz * sz)
    iyy = mass * k / 12.0 * (sx * sx + sz * sz)
    izz = mass * k / 12.0 * (sx * sx + sy * sy)
    return ixx, iyy, izz


def _collision_xml(coll):
    out = []
    for c in coll:
        kind = c[0]
        if kind == "box":
            size, org = c[1], c[2]
            out.append('      <origin xyz="%s" rpy="0 0 0"/>' % _v3(org))
            out.append('      <geometry><box size="%s"/></geometry>' % _v3(size, 4))
        elif kind == "cyl":
            r, ln, org = c[1], c[2], c[3]
            out.append('      <origin xyz="%s" rpy="0 0 0"/>' % _v3(org))
            out.append('      <geometry><cylinder radius="%s" length="%s"/></geometry>'
                       % (_f(r, 4), _f(ln, 4)))
        elif kind == "sphere":
            r, org = c[1], c[2]
            out.append('      <origin xyz="%s" rpy="0 0 0"/>' % _v3(org))
            out.append('      <geometry><sphere radius="%s"/></geometry>' % _f(r, 4))
    return out


def write_urdf(path, rb, geoms, materials, mesh_dir="meshes", ext=".obj",
               base_floating=False):
    total_mass = 0.0
    L = []
    L.append('<?xml version="1.0" encoding="utf-8"?>')
    L.append('<!-- humanoid_robot · 参数化生成 (Z-up, +X 前, +Y 左, 单位 m/rad) -->')
    L.append('<!-- 自由度: %d（含 5 指 19 关节/手）；总质量约见各 link -->' % rb.dof())
    L.append('<robot name="%s">' % rb.name)
    L.append('')
    L.append('  <!-- ================= 材质 ================= -->')
    for (name, rgba, _r, _m, _e, _es) in materials:
        L.append('  <material name="%s"><color rgba="%.3f %.3f %.3f %.3f"/></material>'
                 % (name, rgba[0], rgba[1], rgba[2], rgba[3]))
    L.append('')

    for link in rb.links:
        total_mass += link.mass
        L.append('  <link name="%s">' % link.name)
        g = geoms.get(link.name)
        bext = (0.06, 0.06, 0.06)
        com = (0.0, 0.0, 0.0)
        if g and g[0]:
            xs = [p[0] for p in g[0]]
            ys = [p[1] for p in g[0]]
            zs = [p[2] for p in g[0]]
            bext = (max(0.004, max(xs) - min(xs)),
                    max(0.004, max(ys) - min(ys)),
                    max(0.004, max(zs) - min(zs)))
            com = ((max(xs) + min(xs)) * 0.5,
                   (max(ys) + min(ys)) * 0.5,
                   (max(zs) + min(zs)) * 0.5)

        ixx, iyy, izz = _inertia(link.mass, bext)
        L.append('    <inertial>')
        L.append('      <origin xyz="%s" rpy="0 0 0"/>' % _v3(com))
        L.append('      <mass value="%.4f"/>' % link.mass)
        L.append('      <inertia ixx="%.6f" ixy="0" ixz="0" iyy="%.6f" iyz="0" izz="%.6f"/>'
                 % (ixx, iyy, izz))
        L.append('    </inertial>')

        if g and g[2]:
            L.append('    <visual>')
            L.append('      <origin xyz="0 0 0" rpy="0 0 0"/>')
            L.append('      <geometry>')
            L.append('        <mesh filename="%s/%s%s"/>' % (mesh_dir, link.name, ext))
            L.append('      </geometry>')
            L.append('      <material name="%s"/>' % materials[link.material][0])
            L.append('    </visual>')

        if link.collision:
            L.append('    <collision>')
            L.extend(_collision_xml(link.collision[:1]))
            L.append('    </collision>')
        L.append('  </link>')

    L.append('')
    L.append('  <!-- ================= 关节 ================= -->')
    for j in rb.joints:
        if j.kind == "fixed":
            L.append('  <joint name="%s" type="fixed">' % j.name)
        else:
            kind = "continuous" if (j.lower <= -3.14159 and j.upper >= 3.14159
                                    and "yaw" in j.name) else "revolute"
            L.append('  <joint name="%s" type="%s">' % (j.name, kind))
        L.append('    <parent link="%s"/>' % j.parent)
        L.append('    <child link="%s"/>' % j.child)
        L.append('    <origin xyz="%s" rpy="%s"/>' % (_v3(j.xyz), _v3(j.rpy)))
        if j.kind != "fixed":
            L.append('    <axis xyz="%s"/>' % _v3(j.axis))
            L.append('    <limit lower="%.6f" upper="%.6f" effort="%.1f" velocity="%.2f"/>'
                     % (j.lower, j.upper, j.effort, j.velocity))
            L.append('    <dynamics damping="%.3f" friction="%.3f"/>' % (j.damping, j.friction))
        L.append('  </joint>')

    L.append('')
    L.append('</robot>')
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(L))
    return total_mass
