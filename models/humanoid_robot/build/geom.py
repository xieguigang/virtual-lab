# -*- coding: utf-8 -*-
"""
几何内核：矩阵 / 网格容器 / 参数化曲面（球、胶囊、超椭球圆角盒）。

约定
----
* 全部使用**列向量**约定： p' = M @ p，M 为 4x4 嵌套 list。
* 三角面绕序：逆时针为正面（法线朝外），右手系。
* 所有长度单位：米。
* 曲面一律用"环"（ring）堆叠生成，环按 z 递增排列，
  这样 build_surface() 生成的绕序一定朝外。
"""

from math import sin, cos, pi, sqrt, copysign, atan2, acos

# --------------------------------------------------------------------------- #
#  基础向量 / 矩阵
# --------------------------------------------------------------------------- #


def vadd(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def vsub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def vmul(a, k):
    return (a[0] * k, a[1] * k, a[2] * k)


def vdot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def vcross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def vlen(a):
    return sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def vnorm(a):
    n = vlen(a)
    if n < 1e-12:
        return (0.0, 0.0, 1.0)
    return (a[0] / n, a[1] / n, a[2] / n)


def sp(x, e):
    """带符号幂：sign(x)*|x|^e，超椭球用。"""
    if x == 0.0:
        return 0.0
    return copysign(abs(x) ** e, x)


def m_ident():
    return [[1.0, 0.0, 0.0, 0.0],
            [0.0, 1.0, 0.0, 0.0],
            [0.0, 0.0, 1.0, 0.0],
            [0.0, 0.0, 0.0, 1.0]]


def m_mul(a, b):
    out = [[0.0] * 4 for _ in range(4)]
    for i in range(4):
        ai = a[i]
        oi = out[i]
        for j in range(4):
            oi[j] = ai[0] * b[0][j] + ai[1] * b[1][j] + ai[2] * b[2][j] + ai[3] * b[3][j]
    return out


def m_chain(*ms):
    out = m_ident()
    for m in ms:
        out = m_mul(out, m)
    return out


def m_trans(t):
    m = m_ident()
    m[0][3], m[1][3], m[2][3] = t[0], t[1], t[2]
    return m


def m_rot_axis(axis, ang):
    x, y, z = vnorm(axis)
    c, s = cos(ang), sin(ang)
    C = 1.0 - c
    return [[c + x * x * C,     x * y * C - z * s, x * z * C + y * s, 0.0],
            [y * x * C + z * s, c + y * y * C,     y * z * C - x * s, 0.0],
            [z * x * C - y * s, z * y * C + x * s, c + z * z * C,     0.0],
            [0.0, 0.0, 0.0, 1.0]]


def m_rot_rpy(r, p, y):
    """URDF 的 rpy：固定轴 X-Y-Z，等价 R = Rz(y) @ Ry(p) @ Rx(r)。"""
    return m_chain(m_rot_axis((0, 0, 1), y),
                   m_rot_axis((0, 1, 0), p),
                   m_rot_axis((1, 0, 0), r))


def m_point(m, p):
    x = m[0][0] * p[0] + m[0][1] * p[1] + m[0][2] * p[2] + m[0][3]
    y = m[1][0] * p[0] + m[1][1] * p[1] + m[1][2] * p[2] + m[1][3]
    z = m[2][0] * p[0] + m[2][1] * p[1] + m[2][2] * p[2] + m[2][3]
    return (x, y, z)


def m_dir(m, p):
    x = m[0][0] * p[0] + m[0][1] * p[1] + m[0][2] * p[2]
    y = m[1][0] * p[0] + m[1][1] * p[1] + m[1][2] * p[2]
    z = m[2][0] * p[0] + m[2][1] * p[1] + m[2][2] * p[2]
    return (x, y, z)


def m_inv_rigid(m):
    """刚体矩阵求逆： R^T , -R^T t 。"""
    r = [[m[j][i] for j in range(3)] for i in range(3)]
    t = (m[0][3], m[1][3], m[2][3])
    inv = m_ident()
    for i in range(3):
        for j in range(3):
            inv[i][j] = r[i][j]
    inv[0][3] = -(r[0][0] * t[0] + r[0][1] * t[1] + r[0][2] * t[2])
    inv[1][3] = -(r[1][0] * t[0] + r[1][1] * t[1] + r[1][2] * t[2])
    inv[2][3] = -(r[2][0] * t[0] + r[2][1] * t[1] + r[2][2] * t[2])
    return inv


def m_to_quat(m):
    """旋转矩阵 → 四元数 (x,y,z,w)。"""
    tr = m[0][0] + m[1][1] + m[2][2]
    if tr > 0.0:
        s = sqrt(tr + 1.0) * 2.0
        w = 0.25 * s
        x = (m[2][1] - m[1][2]) / s
        y = (m[0][2] - m[2][0]) / s
        z = (m[1][0] - m[0][1]) / s
    elif m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2.0
        w = (m[2][1] - m[1][2]) / s
        x = 0.25 * s
        y = (m[0][1] + m[1][0]) / s
        z = (m[0][2] + m[2][0]) / s
    elif m[1][1] > m[2][2]:
        s = sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2.0
        w = (m[0][2] - m[2][0]) / s
        x = (m[0][1] + m[1][0]) / s
        y = 0.25 * s
        z = (m[1][2] + m[2][1]) / s
    else:
        s = sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2.0
        w = (m[1][0] - m[0][1]) / s
        x = (m[0][2] + m[2][0]) / s
        y = (m[1][2] + m[2][1]) / s
        z = 0.25 * s
    n = sqrt(x * x + y * y + z * z + w * w)
    if n < 1e-12:
        return (0.0, 0.0, 0.0, 1.0)
    return (x / n, y / n, z / n, w / n)


def frame_from_axis(d):
    """给一条轴向量构造右手正交基 (ex, ey, ez=d)。"""
    ez = vnorm(d)
    if abs(ez[2]) < 0.9:
        h = (0.0, 0.0, 1.0)
    else:
        h = (1.0, 0.0, 0.0)
    ex = vnorm(vcross(h, ez))
    ey = vcross(ez, ex)
    return ex, ey, ez


# --------------------------------------------------------------------------- #
#  网格容器
# --------------------------------------------------------------------------- #


class Mesh(object):
    """收集三角面；finalize() 做顶点焊接并计算平滑法线。"""

    __slots__ = ("pos", "faces", "fmat", "_key")

    def __init__(self):
        self.pos = []
        self.faces = []
        self.fmat = []
        self._key = []

    def add(self, prim, mat=0, M=None, flat=False):
        ps, fs = prim
        base = len(self.pos)
        if M is None:
            for p in ps:
                self.pos.append(p)
        else:
            for p in ps:
                self.pos.append(m_point(M, p))
        for i in range(len(ps)):
            self._key.append(None if flat else 0)
        for f in fs:
            self.faces.append((f[0] + base, f[1] + base, f[2] + base))
            self.fmat.append(mat)

    # ----------------------------------------------------------------- #
    def mirror_y(self):
        """沿 Y 镜像（用于右手）。同时翻转绕序保持法线朝外。"""
        self.pos = [(p[0], -p[1], p[2]) for p in self.pos]
        self.faces = [(a, c, b) for (a, b, c) in self.faces]

    def bbox(self):
        if not self.pos:
            return (0, 0, 0), (0, 0, 0)
        xs = [p[0] for p in self.pos]
        ys = [p[1] for p in self.pos]
        zs = [p[2] for p in self.pos]
        return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))

    # ----------------------------------------------------------------- #
    def finalize(self):
        """返回 (positions, normals, faces, fmat)。"""
        idx = {}
        remap = []
        npos = []
        for i, p in enumerate(self.pos):
            if self._key[i] is None:           # 平面着色：不焊接
                remap.append(len(npos))
                npos.append(p)
            else:
                k = (round(p[0], 6), round(p[1], 6), round(p[2], 6))
                j = idx.get(k)
                if j is None:
                    j = len(npos)
                    idx[k] = j
                    npos.append(p)
                remap.append(j)

        faces = []
        fmat = []
        for f, m in zip(self.faces, self.fmat):
            a, b, c = remap[f[0]], remap[f[1]], remap[f[2]]
            if a == b or b == c or a == c:
                continue
            faces.append((a, b, c))
            fmat.append(m)

        nrm = [[0.0, 0.0, 0.0] for _ in npos]
        for (a, b, c) in faces:
            pa, pb, pc = npos[a], npos[b], npos[c]
            n = vcross(vsub(pb, pa), vsub(pc, pa))
            for i in (a, b, c):
                ni = nrm[i]
                ni[0] += n[0]
                ni[1] += n[1]
                ni[2] += n[2]
        for i, n in enumerate(nrm):
            l = sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2])
            if l < 1e-15:
                nrm[i] = (0.0, 0.0, 1.0)
            else:
                nrm[i] = (n[0] / l, n[1] / l, n[2] / l)

        return npos, nrm, faces, fmat


# --------------------------------------------------------------------------- #
#  参数化曲面
# --------------------------------------------------------------------------- #


def build_surface(rings):
    """rings: 由"环"组成的列表，每环是等长的 3D 点列表，或单点（极点）。"""
    pos = []
    groups = []
    for r in rings:
        if len(r) == 1:
            groups.append((len(pos), 1))
            pos.append(r[0])
        else:
            groups.append((len(pos), len(r)))
            pos.extend(r)

    faces = []
    for k in range(len(groups) - 1):
        (s0, c0), (s1, c1) = groups[k], groups[k + 1]
        if c0 == 1 and c1 == 1:
            continue
        n = c0 if c0 > 1 else c1
        if c0 == 1:                                   # 底极点 → 环
            for u in range(n):
                faces.append((s0, s1 + (u + 1) % n, s1 + u))
        elif c1 == 1:                                 # 环 → 顶极点
            for u in range(n):
                faces.append((s0 + u, s0 + (u + 1) % n, s1))
        else:
            for u in range(n):
                v = (u + 1) % n
                faces.append((s0 + u, s0 + v, s1 + v))
                faces.append((s0 + u, s1 + v, s1 + u))
    return pos, faces


def prim_ellipsoid(rx, ry, rz, nu=32, nv=18):
    """UV 椭球，极点收敛为单点。"""
    rings = []
    for j in range(nv + 1):
        phi = -pi / 2 + pi * j / nv
        cr, sz = cos(phi), sin(phi)
        if abs(cr) < 1e-9:
            rings.append([(0.0, 0.0, rz * sz)])
        else:
            rings.append([(rx * cr * cos(2 * pi * u / nu),
                           ry * cr * sin(2 * pi * u / nu),
                           rz * sz) for u in range(nu)])
    return build_surface(rings)


def prim_capsule(p0, p1, r0, r1=None, nu=28, ncap=7, nring=1):
    """两端半球封头的锥形胶囊；端点 p0/p1 为球心。"""
    if r1 is None:
        r1 = r0
    L = vlen(vsub(p1, p0))
    prof = []
    if L < 1e-9:
        return prim_ellipsoid(r0, r0, r0, nu, ncap * 2)
    for j in range(ncap + 1):                       # 下半球
        phi = -pi / 2 + (pi / 2) * j / ncap
        prof.append((r0 * cos(phi), r0 * sin(phi)))
    for j in range(1, nring + 1):                   # 直段
        t = j / float(nring)
        prof.append((r0 + (r1 - r0) * t, L * t))
    for j in range(ncap + 1):                       # 上半球
        phi = (pi / 2) * j / ncap
        prof.append((r1 * cos(phi), L + r1 * sin(phi)))

    ex, ey, ez = frame_from_axis(vsub(p1, p0))
    rings = []
    for (r, z) in prof:
        if r < 1e-9:
            rings.append([m_point(m_trans(p0), vmul(ez, z))])
        else:
            ring = []
            for u in range(nu):
                a = 2 * pi * u / nu
                loc = vadd(vadd(vmul(ex, r * cos(a)), vmul(ey, r * sin(a))), vmul(ez, z))
                ring.append(vadd(p0, loc))
            rings.append(ring)
    return build_surface(rings)


def prim_superbox(a, b, c, e1=0.4, e2=0.4, nu=44, nv=26, taper=None, shift=None):
    """
    超椭球（圆角盒 / 蛋形）。 e1,e2 → 0.1 接近方盒，→ 1.0 接近椭球。
    taper(t)  t=z/c∈[-1,1] → 横截面缩放系数，用来做"上宽下窄"的躯干。
    """
    rings = []
    for j in range(nv + 1):
        v = -pi / 2 + pi * j / nv
        sv, cv = sp(sin(v), e1), sp(cos(v), e1)
        z = c * sv
        s = 1.0 if taper is None else taper(sv)
        if abs(cv) < 1e-9:
            zz = z + (shift[2] if shift else 0.0)
            xx = shift[0] if shift else 0.0
            yy = shift[1] if shift else 0.0
            rings.append([(xx, yy, zz)])
        else:
            ring = []
            for u in range(nu):
                t = 2 * pi * u / nu
                x = a * cv * sp(cos(t), e2) * s
                y = b * cv * sp(sin(t), e2) * s
                if shift:
                    x += shift[0]
                    y += shift[1]
                    zz = z + shift[2]
                else:
                    zz = z
                ring.append((x, y, zz))
            rings.append(ring)
    return build_surface(rings)


def prim_tube(profile, nu=32):
    """
    旋转体：profile = [(r, z), ...]，从下到上；r=0 表示极点。
    用来做脚踝、手腕、颈圈这类过渡件。
    """
    rings = []
    for (r, z) in profile:
        if r < 1e-9:
            rings.append([(0.0, 0.0, z)])
        else:
            rings.append([(r * cos(2 * pi * u / nu), r * sin(2 * pi * u / nu), z)
                          for u in range(nu)])
    return build_surface(rings)
