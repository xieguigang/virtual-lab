# -*- coding: utf-8 -*-
"""
极简软件渲染器（纯标准库）：把模型渲染成 PNG，用于校核几何。

    python preview.py                      # 全部预设视角
    python preview.py front hand           # 指定视角
"""

import os
import struct
import sys
import time
import zlib
from math import pi, tan, sqrt, sin, cos, radians

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import model
from geom import vsub, vadd, vmul, vdot, vcross, vnorm, m_point, m_dir

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "preview")

# --------------------------------------------------------------------------- #
#  场景收集
# --------------------------------------------------------------------------- #


def collect(rb, pose_deg=None, palette=None):
    ang = rb.pose_degrees(pose_deg or {})
    world = rb.forward(ang)
    tris = []
    order = [l.name for l in rb.links]
    for link in rb.links:
        if link.mesh is None:
            continue
        W = world[link.name]
        pos, nrm, faces, fmat = link.mesh.finalize()
        wp = [m_point(W, p) for p in pos]
        wn = [vnorm(m_dir(W, n)) for n in nrm]
        for f, m in zip(faces, fmat):
            a, b, c = f
            key = m
            if palette is not None:
                key = palette[order.index(link.name) % len(palette)]
            tris.append((wp[a], wp[b], wp[c], wn[a], wn[b], wn[c], key))
    return tris


# --------------------------------------------------------------------------- #
#  摄像机 / 光栅化
# --------------------------------------------------------------------------- #

def look_at(eye, target, up=(0, 0, 1)):
    f = vnorm(vsub(target, eye))
    r = vnorm(vcross(f, up))
    u = vcross(r, f)
    return f, r, u


def render(tris, width, height, eye, target, fov=30.0, ss=2, bg=(0.11, 0.125, 0.145),
           floor=True):
    W, H = width * ss, height * ss
    f, right, up = look_at(eye, target)
    aspect = W / float(H)
    tv = tan(radians(fov) * 0.5)
    near = 0.05

    # 背景：竖直渐变 + 轻微径向亮化
    img = [[0.0] * (W * 3) for _ in range(H)]
    cx, cy = W * 0.5, H * 0.42
    rmax = sqrt(W * W + H * H) * 0.62
    for y in range(H):
        k = y / float(H - 1)
        base = (bg[0] * (1.12 - 0.30 * k),
                bg[1] * (1.12 - 0.30 * k),
                bg[2] * (1.15 - 0.30 * k))
        row = img[y]
        dy = y - cy
        for x in range(W):
            d = sqrt((x - cx) ** 2 + dy * dy) / rmax
            g = 1.0 - 0.55 * min(1.0, d * d)
            row[x * 3] = base[0] * g
            row[x * 3 + 1] = base[1] * g
            row[x * 3 + 2] = base[2] * g

    zbuf = [1e18] * (W * H)

    lights = [
        (vnorm((-0.42, 0.62, 0.66)), (1.00, 0.99, 0.96), 1.00),
        (vnorm((0.78, -0.18, 0.30)), (0.72, 0.80, 0.95), 0.42),
        (vnorm((0.10, 0.55, -0.80)), (1.00, 0.96, 0.90), 0.34),
    ]

    def shade(n, view_dir, albedo, emis, rough):
        r = 0.0
        g = 0.0
        b = 0.0
        amb = 0.30 + 0.10 * n[2]
        r += albedo[0] * amb * 0.85
        g += albedo[1] * amb * 0.88
        b += albedo[2] * amb * 0.95
        for (ld, lc, li) in lights:
            ndl = vdot(n, ld)
            if ndl > 0.0:
                r += albedo[0] * lc[0] * ndl * li
                g += albedo[1] * lc[1] * ndl * li
                b += albedo[2] * lc[2] * ndl * li
            hv = vnorm((ld[0] + view_dir[0], ld[1] + view_dir[1], ld[2] + view_dir[2]))
            ndh = vdot(n, hv)
            if ndh > 0.0:
                s = ndh ** (8.0 + (1.0 - rough) * 90.0) * 0.9
                r += lc[0] * s * li
                g += lc[1] * s * li
                b += lc[2] * s * li
        # 自发光（导光条 / 能量核心）
        r += emis[0]
        g += emis[1]
        b += emis[2]
        return r, g, b

    # 与 model.MATERIALS 顺序一致：白漆 / 面罩 / 发光 / 石墨
    MATS = [
        ((0.930, 0.932, 0.940), (0.00, 0.00, 0.00), 0.26),
        ((0.026, 0.030, 0.040), (0.02, 0.07, 0.11), 0.10),
        ((0.180, 0.880, 1.000), (0.16, 0.72, 0.92), 0.20),
        ((0.070, 0.077, 0.090), (0.00, 0.00, 0.00), 0.42),
    ]

    for (p0, p1, p2, n0, n1, n2, mat) in tris:
        # 世界 → 相机
        v0 = (vdot(vsub(p0, eye), right), vdot(vsub(p0, eye), up), -vdot(vsub(p0, eye), f))
        v1 = (vdot(vsub(p1, eye), right), vdot(vsub(p1, eye), up), -vdot(vsub(p1, eye), f))
        v2 = (vdot(vsub(p2, eye), right), vdot(vsub(p2, eye), up), -vdot(vsub(p2, eye), f))

        poly = [(v0, n0), (v1, n1), (v2, n2)]
        # 近平面裁剪
        out = []
        n_ = len(poly)
        ok = True
        for i in range(n_):
            a, na = poly[i]
            b, nb = poly[(i + 1) % n_]
            ina = a[2] < -near
            inb = b[2] < -near
            if ina:
                out.append((a, na))
            if ina != inb:
                t = (-near - a[2]) / (b[2] - a[2])
                pa = (a[0] + (b[0] - a[0]) * t,
                      a[1] + (b[1] - a[1]) * t,
                      -near)
                nn = (na[0] + (nb[0] - na[0]) * t,
                      na[1] + (nb[1] - na[1]) * t,
                      na[2] + (nb[2] - na[2]) * t)
                out.append((pa, vnorm(nn)))
        if len(out) < 3:
            continue

        # 投影
        pts = []
        for (a, na) in out:
            iz = 1.0 / (-a[2])
            sx = (a[0] * iz / (tv * aspect) * 0.5 + 0.5) * W
            sy = (0.5 - a[1] * iz / tv * 0.5) * H
            pts.append((sx, sy, iz, na, a))

        if isinstance(mat, tuple):
            albedo, emis, rough = mat, (0.0, 0.0, 0.0), 0.45
        else:
            albedo, emis, rough = MATS[mat % len(MATS)]

        for k in range(1, len(pts) - 1):
            _raster(img, zbuf, W, H, pts[0], pts[k], pts[k + 1],
                    albedo, emis, rough, shade, eye)

    # 辉光：把高亮部分抽到 1/8 分辨率做三次盒式模糊，再叠回
    _bloom(img, W, H, threshold=1.00, strength=0.30, passes=2)

    # 下采样 + 输出
    out = bytearray(W // ss * H // ss * 3)
    i = 0
    for y in range(0, H, ss):
        for x in range(0, W, ss):
            r = g = b = 0.0
            for dy in range(ss):
                row = img[y + dy]
                base = (x) * 3
                for dx in range(ss):
                    r += row[base + dx * 3]
                    g += row[base + dx * 3 + 1]
                    b += row[base + dx * 3 + 2]
            n = ss * ss
            out[i] = _tone(r / n)
            out[i + 1] = _tone(g / n)
            out[i + 2] = _tone(b / n)
            i += 3
    return W // ss, H // ss, out


def _bloom(img, W, H, threshold=0.85, strength=1.0, passes=3):
    w8, h8 = max(1, W // 8), max(1, H // 8)
    n = w8 * h8 * 3
    small = [0.0] * n
    for y in range(h8):
        for x in range(w8):
            r = g = b = 0.0
            for dy in range(8):
                row = img[min(H - 1, y * 8 + dy)]
                base = min(W - 1, x * 8) * 3
                for dx in range(8):
                    o = base + dx * 3
                    r += row[o]
                    g += row[o + 1]
                    b += row[o + 2]
            r /= 64.0
            g /= 64.0
            b /= 64.0
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            k = max(0.0, lum - threshold)
            o = (y * w8 + x) * 3
            small[o] = r * k
            small[o + 1] = g * k
            small[o + 2] = b * k

    tmp = [0.0] * n
    for _ in range(passes):
        for y in range(h8):
            for x in range(w8):
                o = (y * w8 + x) * 3
                for c in range(3):
                    s_ = small[o + c]
                    if x > 0:
                        s_ += small[o - 3 + c]
                    if x < w8 - 1:
                        s_ += small[o + 3 + c]
                    tmp[o + c] = s_ / 3.0
        for x in range(w8):
            for y in range(h8):
                o = (y * w8 + x) * 3
                for c in range(3):
                    s_ = tmp[o + c]
                    if y > 0:
                        s_ += tmp[o - w8 * 3 + c]
                    if y < h8 - 1:
                        s_ += tmp[o + w8 * 3 + c]
                    small[o + c] = s_ / 3.0

    k = strength * 2.2
    for y in range(H):
        row = img[y]
        sy = min(h8 - 1, y // 8) * w8
        for x in range(W):
            o8 = (sy + min(w8 - 1, x // 8)) * 3
            o = x * 3
            row[o] += small[o8] * k
            row[o + 1] += small[o8 + 1] * k
            row[o + 2] += small[o8 + 2] * k


def _tone(v):
    v = v / (1.0 + v * 0.30)          # 轻度压缩高光
    v = v ** (1 / 1.9)
    if v <= 0.0:
        return 0
    if v >= 1.0:
        return 255
    return int(v * 255.0 + 0.5)


def _raster(img, zbuf, W, H, A, B, C, albedo, emis, rough, shade, eye):
    ax, ay, aiz, an, ap = A
    bx, by, biz, bn, bp = B
    cx, cy, ciz, cn, cp = C

    minx = int(max(0, min(ax, bx, cx)))
    maxx = int(min(W - 1, max(ax, bx, cx) + 0.999))
    miny = int(max(0, min(ay, by, cy)))
    maxy = int(min(H - 1, max(ay, by, cy) + 0.999))
    if minx > maxx or miny > maxy:
        return

    d = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay)
    if abs(d) < 1e-12:
        return
    inv_d = 1.0 / d

    for y in range(miny, maxy + 1):
        py = y + 0.5
        row = y * W
        for x in range(minx, maxx + 1):
            px = x + 0.5
            w0 = ((bx - px) * (cy - py) - (cx - px) * (by - py)) * inv_d
            if w0 < 0.0:
                continue
            w1 = ((cx - px) * (ay - py) - (ax - px) * (cy - py)) * inv_d
            if w1 < 0.0:
                continue
            w2 = 1.0 - w0 - w1
            if w2 < 0.0:
                continue

            iz = w0 * aiz + w1 * biz + w2 * ciz
            if iz <= 0.0:
                continue
            z = 1.0 / iz
            slot = row + x
            if z >= zbuf[slot]:
                continue
            zbuf[slot] = z

            nx = w0 * an[0] + w1 * bn[0] + w2 * cn[0]
            ny = w0 * an[1] + w1 * bn[1] + w2 * cn[1]
            nz = w0 * an[2] + w1 * bn[2] + w2 * cn[2]
            nl = sqrt(nx * nx + ny * ny + nz * nz)
            if nl < 1e-9:
                nx, ny, nz = 0.0, 0.0, 1.0
                nl = 1.0
            nx /= nl
            ny /= nl
            nz /= nl

            vx = (w0 * ap[0] + w1 * bp[0] + w2 * cp[0]) - eye[0]
            vy = (w0 * ap[1] + w1 * bp[1] + w2 * cp[1]) - eye[1]
            vz = (w0 * ap[2] + w1 * bp[2] + w2 * cp[2]) - eye[2]
            vl = sqrt(vx * vx + vy * vy + vz * vz)
            if vl < 1e-9:
                vl = 1.0
            vx, vy, vz = -vx / vl, -vy / vl, -vz / vl

            r, g, b = shade((nx, ny, nz), (vx, vy, vz), albedo, emis, rough)
            o = x * 3
            im = img[y]
            im[o] = r
            im[o + 1] = g
            im[o + 2] = b


# --------------------------------------------------------------------------- #
#  PNG
# --------------------------------------------------------------------------- #

def write_png(path, w, h, rgb):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgb[y * w * 3:(y + 1) * w * 3]

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        c += struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        return c

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 6))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


# --------------------------------------------------------------------------- #
#  视角预设
# --------------------------------------------------------------------------- #

VIEWS = {
    # +X = 机器人正前方，+Y = 机器人左侧
    "front":         dict(eye=(4.60, 0.00, 1.02), target=(0.0, 0.0, 0.86), fov=24),
    "three_quarter": dict(eye=(3.55, -2.55, 1.45), target=(0.0, 0.0, 0.86), fov=24),
    "side":          dict(eye=(0.00, -4.60, 1.02), target=(0.0, 0.0, 0.86), fov=24),
    "back":          dict(eye=(-4.30, 0.55, 1.05), target=(0.0, 0.0, 0.86), fov=24),
    "hand":          dict(eye=(0.62, -0.62, 0.96), target=(0.02, -0.19, 0.78), fov=26),
    "head":          dict(eye=(0.95, -0.55, 1.66), target=(0.0, 0.0, 1.57), fov=26),
    "foot":          dict(eye=(0.75, -0.62, 0.36), target=(0.06, -0.10, 0.06), fov=32),
}


def main(argv):
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    rb = model.build()
    poses = model.presets()

    jobs = []
    sel = argv[1:] or ["front", "three_quarter", "hand", "head"]
    for name in sel:
        if name in poses:
            jobs.append((name, poses[name]["joints"], VIEWS["three_quarter"], 2))
        elif name in VIEWS:
            jobs.append((name, None, VIEWS[name], 2))
        else:
            print("unknown view:", name)
    # 姿态特写
    for pname in ("pen_grip", "cook_stir", "fold_blanket"):
        if pname in poses:
            jobs.append(("pose_" + pname, poses[pname]["joints"],
                         dict(eye=(2.95, -2.35, 1.42), target=(0.10, -0.05, 0.98), fov=26), 2))

    if "penhand" in sel:
        joints = poses["pen_grip"]["joints"]
        rb2 = rb
        ang = rb.pose_degrees(joints)
        world = rb.forward(ang)
        # 右手腕世界坐标 → 特写相机
        import geom
        P = world["r_hand"]
        tgt = (P[0][3], P[1][3], P[2][3] - 0.06)
        jobs.append(("pose_pen_hand", joints,
                     dict(eye=(tgt[0] + 0.34, tgt[1] - 0.30, tgt[2] + 0.20), target=tgt, fov=26), 2))

    for (name, joints, view, ss) in jobs:
        t0 = time.time()
        tris = collect(rb, joints)
        w, h, rgb = render(tris, 820, 1050, view["eye"], view["target"],
                           fov=view["fov"], ss=ss)
        path = os.path.join(OUT, name + ".png")
        write_png(path, w, h, rgb)
        print("%-18s %5.1fs  %d tris" % (name, time.time() - t0, len(tris)))


if __name__ == "__main__":
    main(sys.argv)
