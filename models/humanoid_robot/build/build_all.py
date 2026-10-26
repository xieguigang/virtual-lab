# -*- coding: utf-8 -*-
"""
一键生成 humanoid_robot 的全部资源。

用法：
    python build_all.py            # 生成 meshes/*.obj + humanoid_robot.glb + .urdf
"""

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import model
import export
from geom import m_point

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                 # models/humanoid_robot
MESH_DIR = os.path.join(ROOT, "meshes")


def gltf_materials():
    out = []
    for (name, rgba, rough, metal, emis, estr) in model.MATERIALS:
        mat = {
            "name": name,
            "doubleSided": True,
            "pbrMetallicRoughness": {
                "baseColorFactor": [rgba[0], rgba[1], rgba[2], rgba[3]],
                "metallicFactor": metal,
                "roughnessFactor": rough,
            },
        }
        if estr > 0.0:
            mat["emissiveFactor"] = [emis[0], emis[1], emis[2]]
            if abs(estr - 1.0) > 1e-6:
                mat["extensions"] = {
                    "KHR_materials_emissive_strength": {"emissiveStrength": estr}
                }
        out.append(mat)
    return out


def main():
    t0 = time.time()
    if not os.path.isdir(MESH_DIR):
        os.makedirs(MESH_DIR)

    rb = model.build()
    print("links=%d joints=%d dof=%d" % (len(rb.links), len(rb.joints), rb.dof()))

    # ---- finalize 每个 link 的网格 ----
    geoms = {}
    tris = 0
    verts = 0
    for link in rb.links:
        if link.mesh is None:
            continue
        g = link.mesh.finalize()
        geoms[link.name] = g
        tris += len(g[2])
        verts += len(g[0])

    print("tris=%d verts=%d" % (tris, verts))

    # ---- 包围盒自检 ----
    world = rb.forward()
    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    for name, (pos, nrm, faces, fmat) in geoms.items():
        W = world.get(name)
        if W is None:
            continue
        for p in pos:
            w = m_point(W, p)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    print("bbox  x[%.3f %.3f]  y[%.3f %.3f]  z[%.3f %.3f]  height=%.3f"
          % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], hi[2] - lo[2]))

    # ---- MTL + OBJ ----
    export.write_mtl(os.path.join(MESH_DIR, "robot.mtl"), model.MATERIALS)
    for name, g in geoms.items():
        export.write_obj(os.path.join(MESH_DIR, name + ".obj"), name, g, model.MATERIALS)

    # ---- GLB（整机） ----
    glb_path = os.path.join(ROOT, "humanoid_robot.glb")
    size = export.write_glb(glb_path, rb, world, geoms, gltf_materials(),
                            node_extra={"generator": "humanoid_robot", "dof": rb.dof()},
                            extensions_used=["KHR_materials_emissive_strength"])
    print("glb  %.2f MB" % (size / 1048576.0))

    # ---- URDF ----
    urdf_path = os.path.join(ROOT, "humanoid_robot.urdf")
    mass = export.write_urdf(urdf_path, rb, geoms, model.MATERIALS)
    print("urdf total mass = %.2f kg" % mass)

    # ---- 预设姿态 ----
    poses = model.presets()
    meta = {
        "robot": "humanoid_robot",
        "units": "degrees",
        "dof": rb.dof(),
        "pelvis_height": model.PELVIS_Z,
        "note": "joints 中未出现的关节按 0° 处理；角度为 URDF 关节角（度，逆时针为正）",
    }
    with open(os.path.join(ROOT, "poses.json"), "w", encoding="utf-8") as f:
        json.dump({"meta": meta, "poses": poses}, f, ensure_ascii=False, indent=2)

    print("done in %.1fs" % (time.time() - t0))


if __name__ == "__main__":
    main()
