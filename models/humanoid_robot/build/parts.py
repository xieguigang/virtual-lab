# -*- coding: utf-8 -*-
"""
humanoid_robot 零件库：每个零件都在"自己 link 的局部坐标系"里建模。

统一约定
--------
* 每根骨骼沿 **-Z** 方向延伸（肩→肘、肘→腕、髋→膝…），关节球心在原点。
* +X = 机器人正前方（掌心朝前时的手背侧 / 脚背方向）
* +Y = 机器人左侧
* +Z = 上
单位：米。

科幻化处理（相对"纯白漆人偶"）
------------------------------
* 白色壳体保持不变，另加两类材质：**石墨色骨架件**（关节护套、腰带、散热口）
  与**青色发光件**（能量核心、关节光环、体侧导光条、面罩眼线）。
* 发光件一律做成"贴在曲面外 1~3 mm 的细条 / 细环"，既不破坏光滑曲面，
  又能在物理引擎里作为可识别的视觉标记。
"""

from math import pi, sin, cos
from geom import (Mesh, m_trans, m_rot_rpy, m_chain,
                  prim_ellipsoid, prim_capsule, prim_superbox, prim_tube)

MAT_WHITE = 0
MAT_VISOR = 1
MAT_GLOW = 2
MAT_DARK = 3


def _lerp(a, b, t):
    return a + (b - a) * t


def _smooth(t):
    """t∈[0,1] 的 smoothstep。"""
    if t < 0.0:
        t = 0.0
    elif t > 1.0:
        t = 1.0
    return t * t * (3.0 - 2.0 * t)


# --------------------------------------------------------------------------- #
#  科幻配件小工具
# --------------------------------------------------------------------------- #

def _strip(p0, p1, r=0.0030):
    """导光条：沿骨骼表面放的一根细胶囊。"""
    return prim_capsule(p0, p1, r, r * 0.88, nu=14, ncap=3)


def _glow_ring(rx, ry, z=0.0, thick=0.0042):
    """关节光环：一个很扁的椭球，套在关节球赤道上。"""
    return prim_ellipsoid(rx, ry, thick, nu=34, nv=10)


def _sleeve(z0, z1, r, nu=30):
    """关节护套 / 分模线：一段实心短筒（半径略大于骨骼，肉眼只见环带）。"""
    return prim_tube([(0.0, z0), (r, z0), (r, z1), (0.0, z1)], nu=nu)


def _slab(sx, sy, sz, at, e=0.35):
    """薄板（散热口、腰带等）。"""
    return prim_superbox(sx, sy, sz, e, e + 0.05, nu=26, nv=12, shift=at)


# --------------------------------------------------------------------------- #
#  躯干
# --------------------------------------------------------------------------- #

def build_pelvis():
    m = Mesh()
    # 骨盆主体：横向宽、前后薄
    m.add(prim_superbox(0.096, 0.148, 0.076, 0.52, 0.56,
                        taper=lambda t: 0.90 + 0.10 * _smooth(0.5 + 0.5 * t),
                        nu=44, nv=26,
                        shift=(0.0, 0.0, 0.032)))
    # 髋轭：一整块柔性壳体盖住两侧髋关节，避免关节球外露
    m.add(prim_ellipsoid(0.104, 0.172, 0.100), M=m_trans((0.0, 0.0, -0.016)))
    # 腰腹过渡
    m.add(prim_capsule((0, 0, 0.080), (0, 0, 0.100), 0.100, 0.096, nu=36, ncap=5))

    # —— 科幻：腰带 + 前腹能量槽 ——
    m.add(_slab(0.098, 0.152, 0.010, (0.0, 0.0, 0.074)), mat=MAT_DARK)
    m.add(prim_capsule((0.082, 0.0, -0.010), (0.094, 0.0, -0.010), 0.014, 0.012,
                       nu=24, ncap=4), mat=MAT_GLOW)
    return m


def build_spine_lower():
    m = Mesh()
    m.add(prim_superbox(0.082, 0.104, 0.040, 0.50, 0.54,
                        taper=lambda t: 0.94 + 0.06 * _smooth(0.5 + 0.5 * t)))
    # 腰部柔性段：三条环，视觉上像漆面分层
    for z in (-0.016, 0.020):
        m.add(prim_ellipsoid(0.087, 0.109, 0.012), M=m_trans((0.0, 0.0, z)),
              mat=MAT_DARK)
    # —— 科幻：腰部环形导光带 ——
    m.add(_glow_ring(0.0885, 0.1105, 0.0, 0.0048), mat=MAT_GLOW)
    return m


def build_spine_upper():
    m = Mesh()
    m.add(prim_superbox(0.090, 0.122, 0.040, 0.46, 0.50,
                        taper=lambda t: 0.92 + 0.08 * _smooth(0.5 + 0.5 * t)))
    # —— 科幻：腹侧两条竖向导光条 ——
    for s in (1, -1):
        m.add(_strip((0.086, s * 0.052, -0.028), (0.078, s * 0.062, 0.028), 0.0040),
              mat=MAT_GLOW)
    return m


def build_chest():
    m = Mesh()
    # 胸腔：上宽下窄、前后略扁，曲面尽量柔和
    m.add(prim_superbox(0.112, 0.170, 0.150, 0.52, 0.58,
                        taper=lambda t: 0.78 + 0.22 * _smooth((0.5 + 0.5 * t) ** 0.75),
                        nu=48, nv=30,
                        shift=(0.0, 0.0, 0.150)))
    # 锁骨
    for s in (1, -1):
        m.add(prim_capsule((0.004, s * 0.022, 0.230), (0.004, s * 0.176, 0.242),
                           0.040, 0.035, nu=26, ncap=5))
    # 肩窝外壳
    for s in (1, -1):
        m.add(prim_ellipsoid(0.044, 0.050, 0.050),
              M=m_trans((0.004, s * 0.160, 0.242)))
    # 背脊（完全包在壳体内，只负责补强曲面）
    m.add(prim_capsule((0.0, 0.0, 0.020), (0.0, 0.0, 0.245), 0.046, 0.040, nu=24, ncap=5))

    # —— 科幻：胸口反应堆（石墨基座 + 发光核心）——
    m.add(prim_capsule((0.018, 0.0, 0.172), (0.062, 0.0, 0.172), 0.062, 0.055,
                       nu=36, ncap=7), mat=MAT_DARK)
    m.add(prim_capsule((0.064, 0.0, 0.172), (0.084, 0.0, 0.172), 0.040, 0.037,
                       nu=36, ncap=7), mat=MAT_GLOW)

    # —— 科幻：锁骨下方的两条导光条 ——
    for s in (1, -1):
        m.add(_strip((0.103, s * 0.030, 0.222), (0.094, s * 0.104, 0.208), 0.0034),
              mat=MAT_GLOW)

    # —— 科幻：颈圈（石墨）+ 胸下分模线 ——
    m.add(prim_capsule((0.0, 0.0, 0.282), (0.0, 0.0, 0.296), 0.054, 0.050,
                       nu=32, ncap=4), mat=MAT_DARK)
    m.add(_slab(0.104, 0.156, 0.0075, (0.0, 0.0, 0.012)), mat=MAT_DARK)

    # —— 科幻：体侧散热口（每侧三条石墨凹槽）——
    for s in (1, -1):
        for k, zz in enumerate((0.196, 0.174, 0.152)):
            w = 0.030 - k * 0.004
            m.add(_slab(0.012, w, 0.005, (0.098, s * 0.108, zz), 0.30), mat=MAT_DARK)

    # —— 科幻：背脊导光条 ——
    m.add(_strip((-0.100, 0.0, 0.040), (-0.094, 0.0, 0.238), 0.0045), mat=MAT_GLOW)
    for s in (1, -1):
        m.add(_strip((-0.098, s * 0.030, 0.060), (-0.092, s * 0.030, 0.226), 0.0035),
              mat=MAT_GLOW)
    return m


def build_neck():
    m = Mesh()
    m.add(prim_capsule((0, 0, -0.014), (0, 0, 0.056), 0.046, 0.040, nu=28, ncap=5))
    # —— 科幻：颈部发光环 ——
    m.add(_glow_ring(0.0445, 0.0425, 0.020, 0.0048), mat=MAT_GLOW)
    return m


def build_head_mount():
    m = Mesh()
    m.add(prim_capsule((0, 0, -0.012), (0, 0, 0.012), 0.036, 0.034, nu=26, ncap=4))
    m.add(_sleeve(-0.014, -0.004, 0.0372), mat=MAT_DARK)
    return m


def build_head():
    m = Mesh()
    # 颅体：前额略窄、下颌收
    m.add(prim_superbox(0.088, 0.077, 0.104, 0.62, 0.60,
                        taper=lambda t: 0.88 + 0.12 * _smooth((0.5 + 0.5 * t) ** 0.8),
                        nu=44, nv=28,
                        shift=(0.0, 0.0, 0.112)))
    # 面罩（石墨色）
    m.add(prim_superbox(0.024, 0.058, 0.032, 0.42, 0.44, nu=32, nv=18,
                        shift=(0.068, 0.0, 0.118)), mat=MAT_VISOR)
    # 耳部
    for s in (1, -1):
        m.add(prim_ellipsoid(0.014, 0.021, 0.027), M=m_trans((0.0, s * 0.074, 0.106)))
    # 后脑
    m.add(prim_ellipsoid(0.058, 0.060, 0.058), M=m_trans((-0.032, 0.0, 0.126)))

    # —— 科幻：面罩里的发光眼线 ——
    m.add(prim_superbox(0.007, 0.048, 0.0105, 0.45, 0.45, nu=26, nv=12,
                        shift=(0.086, 0.0, 0.120)), mat=MAT_GLOW)
    # —— 科幻：头顶中脊（白色导流鳍）——
    m.add(prim_superbox(0.040, 0.0075, 0.0085, 0.32, 0.38, nu=26, nv=12,
                        shift=(-0.014, 0.0, 0.212)))
    m.add(prim_superbox(0.016, 0.0060, 0.010, 0.32, 0.38, nu=20, nv=10,
                        shift=(-0.050, 0.0, 0.198)))
    # —— 科幻：太阳穴发光点 ——
    for s in (1, -1):
        m.add(prim_capsule((0.0, s * 0.084, 0.106), (0.0, s * 0.091, 0.106),
                           0.0085, 0.0075, nu=18, ncap=3), mat=MAT_GLOW)
    # —— 科幻：后脑散热槽 ——
    for k in range(3):
        m.add(_slab(0.005, 0.030, 0.0045,
                    (-0.086 + k * 0.004, 0.0, 0.086 + k * 0.018), 0.3), mat=MAT_DARK)
    return m


# --------------------------------------------------------------------------- #
#  手臂
# --------------------------------------------------------------------------- #

def build_shoulder():
    """肩球（属于 shoulder_pitch 与 shoulder_roll 之间的那个 link）。"""
    m = Mesh()
    m.add(prim_ellipsoid(0.058, 0.058, 0.058))
    m.add(prim_ellipsoid(0.050, 0.044, 0.052), M=m_trans((0.0, 0.016, 0.008)))

    # —— 科幻：机甲肩甲（带棱角的白色护盖）——
    m.add(prim_ellipsoid(0.061, 0.065, 0.034), M=m_trans((0.000, 0.006, 0.026)))
    # —— 科幻：肩部发光环 ——
    m.add(_glow_ring(0.0598, 0.0598, -0.014, 0.0045), mat=MAT_GLOW)
    m.add(_sleeve(-0.030, -0.019, 0.0588), mat=MAT_DARK)
    return m


def build_upper_arm():
    m = Mesh()
    m.add(prim_ellipsoid(0.052, 0.050, 0.050))
    m.add(prim_capsule((0, 0, -0.005), (0, 0, -0.292), 0.050, 0.040, nu=30, ncap=7))
    m.add(prim_ellipsoid(0.042, 0.041, 0.041), M=m_trans((0, 0, -0.298)))   # 肘窝

    # —— 科幻：上臂前后两条导光条 + 石墨护套 ——
    m.add(_sleeve(-0.024, -0.013, 0.0518), mat=MAT_DARK)
    m.add(_sleeve(-0.286, -0.275, 0.0436), mat=MAT_DARK)
    m.add(_strip((0.004, 0.0485, -0.034), (0.004, 0.0405, -0.262), 0.0034), mat=MAT_GLOW)
    m.add(_strip((0.0480, 0.0, -0.034), (0.0400, 0.0, -0.262), 0.0034), mat=MAT_GLOW)
    return m


def build_forearm():
    m = Mesh()
    m.add(prim_ellipsoid(0.044, 0.043, 0.043))                              # 肘头
    m.add(prim_capsule((0, 0, -0.008), (0, 0, -0.252), 0.042, 0.031, nu=30, ncap=7))
    m.add(prim_ellipsoid(0.032, 0.030, 0.030), M=m_trans((0, 0, -0.258)))   # 腕窝

    # —— 科幻：肘部光环 + 腕部护套 + 前臂导光条 ——
    m.add(_glow_ring(0.0458, 0.0448, -0.006, 0.0038), mat=MAT_GLOW)
    m.add(_sleeve(-0.248, -0.237, 0.0336), mat=MAT_DARK)
    m.add(_strip((0.0415, 0.0, -0.022), (0.0315, 0.0, -0.234), 0.0032), mat=MAT_GLOW)
    for s in (1, -1):
        m.add(_strip((0.006, s * 0.0405, -0.022), (0.006, s * 0.0305, -0.234), 0.0028),
              mat=MAT_GLOW)
    return m


def build_wrist():
    m = Mesh()
    m.add(prim_capsule((0, 0, 0.004), (0, 0, -0.022), 0.031, 0.029, nu=26, ncap=5))
    m.add(_sleeve(-0.011, 0.0, 0.0328), mat=MAT_DARK)
    return m


def build_hand_base():
    m = Mesh()
    m.add(prim_capsule((0, 0, 0.002), (0, 0, -0.012), 0.028, 0.027, nu=24, ncap=4))
    m.add(_glow_ring(0.0300, 0.0300, -0.004, 0.0030), mat=MAT_GLOW)
    return m


# 手指配置：(名字, 掌指关节 y, 掌指关节 z, 三节长度, 三节半径)
FINGERS = [
    ("index",  0.0315, -0.096, (0.043, 0.027, 0.023), (0.0088, 0.0082, 0.0072)),
    ("middle", 0.0105, -0.099, (0.046, 0.029, 0.024), (0.0090, 0.0084, 0.0074)),
    ("ring",  -0.0105, -0.096, (0.043, 0.027, 0.022), (0.0086, 0.0080, 0.0070)),
    ("little", -0.0315, -0.090, (0.035, 0.023, 0.020), (0.0078, 0.0072, 0.0064)),
]

THUMB_BASE = (0.002, 0.042, -0.030)      # 腕掌关节位置（掌局部系）
THUMB_RPY = (0.55, 0.0, 0.0)             # 拇指外展倾斜
THUMB_SEGS = ((0.040, 0.0112, 0.0102), (0.032, 0.0102, 0.0092), (0.026, 0.0092, 0.0080))


def build_hand():
    """掌部：掌体 + 大鱼际 + 四个掌指关节球。"""
    m = Mesh()
    # 腕口
    m.add(prim_capsule((0, 0, 0.004), (0, 0, -0.018), 0.028, 0.028, nu=26, ncap=4))
    # 掌体
    m.add(prim_superbox(0.0225, 0.0465, 0.048, 0.40, 0.52,
                        nu=40, nv=24,
                        taper=lambda t: 0.86 + 0.14 * _smooth(0.5 + 0.5 * t),
                        shift=(0.001, 0.0, -0.050)))
    # 掌背加厚
    m.add(prim_ellipsoid(0.014, 0.040, 0.038), M=m_trans((0.014, 0.0, -0.052)))
    # 大鱼际
    m.add(prim_ellipsoid(0.019, 0.024, 0.032), M=m_trans((-0.002, 0.038, -0.040)))
    # 掌指关节球
    for (_, fy, fz, _, r) in FINGERS:
        m.add(prim_ellipsoid(r[0] * 1.15, r[0] * 1.15, r[0] * 1.05),
              M=m_trans((0.0, fy, fz)))
    # 拇指腕掌球
    m.add(prim_ellipsoid(0.0135, 0.0135, 0.0135),
          M=m_chain(m_trans(THUMB_BASE), m_rot_rpy(*THUMB_RPY)))

    # —— 科幻：掌背导光条 + 石墨掌根 ——
    m.add(prim_capsule((0.018, 0.0, -0.052), (0.027, 0.0, -0.052), 0.0105, 0.0095,
                       nu=22, ncap=4), mat=MAT_GLOW)
    return m


def build_phalanx(length, r0, r1, ball=True):
    m = Mesh()
    if ball:
        m.add(prim_ellipsoid(r0 * 1.06, r0 * 1.06, r0 * 0.94))
    m.add(prim_capsule((0, 0, 0), (0, 0, -length), r0, r1, nu=20, ncap=5))
    # 指尖石墨指甲盖（只给末节，由调用方通过 length 判断不了，故统一加在球心侧）
    return m


# --------------------------------------------------------------------------- #
#  腿
# --------------------------------------------------------------------------- #

def build_hip():
    m = Mesh()
    m.add(prim_ellipsoid(0.070, 0.068, 0.068))
    m.add(_glow_ring(0.0722, 0.0702, -0.030, 0.0043), mat=MAT_GLOW)
    return m


def build_thigh():
    m = Mesh()
    m.add(prim_ellipsoid(0.062, 0.062, 0.066))
    m.add(prim_capsule((0, 0, -0.022), (0, 0, -0.392), 0.070, 0.057, nu=34, ncap=8))
    m.add(prim_ellipsoid(0.056, 0.055, 0.054), M=m_trans((0, 0, -0.402)))

    # —— 科幻：大腿外侧导光条 + 膝部护套 ——
    m.add(_sleeve(-0.386, -0.376, 0.0595), mat=MAT_DARK)
    m.add(_strip((0.008, 0.0675, -0.064), (0.008, 0.0565, -0.372), 0.0034), mat=MAT_GLOW)
    m.add(_strip((0.0735, 0.0, -0.060), (0.0570, 0.0, -0.372), 0.0038), mat=MAT_GLOW)
    return m


def build_shank():
    m = Mesh()
    m.add(prim_ellipsoid(0.058, 0.057, 0.056))
    m.add(prim_capsule((0, 0, -0.012), (0, 0, -0.360), 0.058, 0.038, nu=32, ncap=8))
    # 小腿肚
    m.add(prim_ellipsoid(0.048, 0.052, 0.075), M=m_trans((-0.012, 0.0, -0.090)))
    m.add(prim_ellipsoid(0.038, 0.037, 0.036), M=m_trans((0, 0, -0.368)))

    # —— 科幻：膝部光环 + 胫前导光条 + 踝部护套 ——
    m.add(_glow_ring(0.0599, 0.0589, -0.008, 0.0040), mat=MAT_GLOW)
    m.add(_sleeve(-0.352, -0.341, 0.0404), mat=MAT_DARK)
    m.add(_strip((0.0565, 0.0, -0.030), (0.0385, 0.0, -0.336), 0.0040), mat=MAT_GLOW)
    for s in (1, -1):
        m.add(_strip((0.006, s * 0.0560, -0.030), (0.006, s * 0.0380, -0.336), 0.0032),
              mat=MAT_GLOW)
    return m


def build_ankle():
    m = Mesh()
    m.add(prim_ellipsoid(0.038, 0.036, 0.034))
    m.add(prim_superbox(0.030, 0.040, 0.026, 0.40, 0.45, shift=(0.0, 0.0, -0.026)))
    m.add(_glow_ring(0.0396, 0.0376, 0.0, 0.0033), mat=MAT_GLOW)
    return m


def build_foot():
    m = Mesh()
    # 脚掌
    m.add(prim_superbox(0.072, 0.050, 0.030, 0.42, 0.50, nu=40, nv=22,
                        shift=(0.052, 0.0, -0.060)))
    # 脚跟
    m.add(prim_ellipsoid(0.030, 0.043, 0.028), M=m_trans((-0.046, 0.0, -0.062)))
    # 足弓
    m.add(prim_capsule((-0.010, 0.0, -0.030), (0.040, 0.0, -0.052), 0.030, 0.030,
                       nu=24, ncap=5))
    # —— 科幻：石墨鞋底板 + 外侧导光条 ——
    m.add(prim_superbox(0.070, 0.048, 0.008, 0.30, 0.36, nu=30, nv=12,
                        shift=(0.052, 0.0, -0.082)), mat=MAT_DARK)
    m.add(_strip((0.010, 0.0495, -0.062), (0.112, 0.0495, -0.062), 0.0038), mat=MAT_GLOW)
    return m


def build_toe():
    m = Mesh()
    m.add(prim_superbox(0.046, 0.046, 0.028, 0.42, 0.50, nu=32, nv=20,
                        shift=(0.044, 0.0, -0.002)))
    # 石墨趾板（延续鞋底）
    m.add(prim_superbox(0.046, 0.044, 0.008, 0.30, 0.36, nu=26, nv=12,
                        shift=(0.044, 0.0, -0.022)), mat=MAT_DARK)
    return m
