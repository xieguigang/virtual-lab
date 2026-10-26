# -*- coding: utf-8 -*-
"""
机器人装配：连杆表 + 关节表 + 正运动学。

坐标：Z 向上、+X 向前、+Y 向左。地面 z = 0，骨盆中心 z = 0.90。
"""

from math import pi, radians

from geom import m_ident, m_mul, m_trans, m_rot_rpy, m_rot_axis, m_point
import parts as P

PELVIS_Z = 0.90                  # base_link → pelvis 的高度

MATERIALS = [
    # name,            基础色 (rgba)                   rough  metal  自发光 rgb        强度
    ("robot_white",    (0.930, 0.932, 0.938, 1.0),   0.26,  0.06, (0.00, 0.00, 0.00), 0.0),
    ("robot_visor",    (0.026, 0.030, 0.040, 1.0),   0.10,  0.55, (0.02, 0.10, 0.16), 0.35),
    ("robot_glow",     (0.180, 0.880, 1.000, 1.0),   0.20,  0.00, (0.20, 0.90, 1.00), 3.0),
    ("robot_graphite", (0.070, 0.077, 0.090, 1.0),   0.42,  0.65, (0.00, 0.00, 0.00), 0.0),
]


class LinkSpec(object):
    __slots__ = ("name", "mesh", "mass", "collision", "material")

    def __init__(self, name, mesh, mass, collision=None, material=0):
        self.name = name
        self.mesh = mesh
        self.mass = mass
        self.collision = collision or []
        self.material = material


class JointSpec(object):
    __slots__ = ("name", "kind", "parent", "child", "xyz", "rpy", "axis",
                 "lower", "upper", "effort", "velocity", "damping", "friction")

    def __init__(self, name, parent, child, xyz=(0, 0, 0), rpy=(0, 0, 0),
                 axis=(0, 0, 1), lower=-pi, upper=pi, effort=50.0, velocity=3.0,
                 kind="revolute", damping=0.08, friction=0.02):
        self.name = name
        self.kind = kind
        self.parent = parent
        self.child = child
        self.xyz = xyz
        self.rpy = rpy
        self.axis = axis
        self.lower = lower
        self.upper = upper
        self.effort = effort
        self.velocity = velocity
        self.damping = damping
        self.friction = friction


class Robot(object):
    def __init__(self, name="humanoid_robot"):
        self.name = name
        self.links = []
        self.joints = []
        self._link_index = {}

    def link(self, spec):
        self._link_index[spec.name] = len(self.links)
        self.links.append(spec)

    def joint(self, spec):
        self.joints.append(spec)

    def find_link(self, name):
        i = self._link_index.get(name)
        return None if i is None else self.links[i]

    # ------------------------------------------------------------------ #
    def origin_matrix(self, j):
        return m_mul(m_trans(j.xyz), m_rot_rpy(*j.rpy))

    def forward(self, angles=None):
        """返回 {link: 4x4 世界矩阵}。angles: {joint: 弧度}。"""
        angles = angles or {}
        world = {}
        for j in self.joints:
            if j.kind == "fixed":
                local = self.origin_matrix(j)
            else:
                a = angles.get(j.name, 0.0)
                local = m_mul(self.origin_matrix(j), m_rot_axis(j.axis, a))
            p = world.get(j.parent)
            if p is None:
                p = m_ident()
            world[j.child] = m_mul(p, local)
        if self.links and self.links[0].name not in world:
            world[self.links[0].name] = m_ident()
        return world

    def pose_degrees(self, deg):
        return dict((k, radians(v)) for k, v in deg.items())

    def dof(self):
        return sum(1 for j in self.joints if j.kind != "fixed")


# --------------------------------------------------------------------------- #
#  装配
# --------------------------------------------------------------------------- #

def build():
    rb = Robot()
    L, J = rb.link, rb.joint

    # ---------------- 根 ----------------
    L(LinkSpec("base_link", None, 0.001))
    J(JointSpec("base_link_fixed", "base_link", "pelvis",
                xyz=(0, 0, PELVIS_Z), kind="fixed"))

    # ---------------- 躯干 ----------------
    L(LinkSpec("pelvis", P.build_pelvis(), 6.5,
               [("box", (0.31, 0.20, 0.19), (0, 0, 0))]))
    J(JointSpec("spine_yaw", "pelvis", "spine_lower", xyz=(0, 0, 0.075),
                axis=(0, 0, 1), lower=radians(-55), upper=radians(55),
                effort=160, velocity=2.0))

    L(LinkSpec("spine_lower", P.build_spine_lower(), 2.4,
               [("box", (0.22, 0.17, 0.09), (0, 0, 0))]))
    J(JointSpec("spine_pitch", "spine_lower", "spine_upper", xyz=(0, 0, 0.075),
                axis=(0, 1, 0), lower=radians(-25), upper=radians(45),
                effort=220, velocity=2.0))

    L(LinkSpec("spine_upper", P.build_spine_upper(), 2.6,
               [("box", (0.24, 0.19, 0.09), (0, 0, 0))]))
    J(JointSpec("spine_roll", "spine_upper", "chest", xyz=(0, 0, 0.075),
                axis=(1, 0, 0), lower=radians(-28), upper=radians(28),
                effort=200, velocity=2.0))

    L(LinkSpec("chest", P.build_chest(), 7.5,
               [("box", (0.35, 0.24, 0.31), (0, 0, 0.15))]))

    # ---------------- 颈 / 头 ----------------
    J(JointSpec("neck_yaw", "chest", "neck", xyz=(0, 0, 0.295),
                axis=(0, 0, 1), lower=radians(-75), upper=radians(75),
                effort=25, velocity=3.0))
    L(LinkSpec("neck", P.build_neck(), 0.6,
               [("cyl", 0.042, 0.075, (0, 0, 0.024))]))

    J(JointSpec("neck_pitch", "neck", "head_mount", xyz=(0, 0, 0.060),
                axis=(0, 1, 0), lower=radians(-45), upper=radians(45),
                effort=25, velocity=3.0))
    L(LinkSpec("head_mount", P.build_head_mount(), 0.2,
               [("sphere", 0.034, (0, 0, 0))]))

    J(JointSpec("head_roll", "head_mount", "head", xyz=(0, 0, 0.008),
                axis=(1, 0, 0), lower=radians(-30), upper=radians(30),
                effort=12, velocity=3.0))
    L(LinkSpec("head", P.build_head(), 2.2,
               [("sphere", 0.098, (0, 0, 0.104))]))

    # ---------------- 左右对称部分 ----------------
    for s, side in ((1, "l"), (-1, "r")):
        _arm(rb, s, side)
        _leg(rb, s, side)

    return rb


def _mirror(mesh, s):
    if s < 0:
        mesh.mirror_y()
    return mesh


def _arm(rb, s, side):
    L, J = rb.link, rb.joint
    n = lambda x: "%s_%s" % (side, x)

    # 肩：pitch → roll → yaw → 上臂
    J(JointSpec(n("shoulder_pitch"), "chest", n("shoulder"),
                xyz=(0, s * 0.185, 0.245), axis=(0, -1, 0),
                lower=radians(-40), upper=radians(170), effort=120, velocity=3.0))
    L(LinkSpec(n("shoulder"), _mirror(P.build_shoulder(), s), 0.9,
               [("sphere", 0.058, (0, 0, 0))]))

    J(JointSpec(n("shoulder_roll"), n("shoulder"), n("shoulder_yoke"),
                axis=(1, 0, 0), lower=radians(-15), upper=radians(180),
                effort=120, velocity=3.0))
    L(LinkSpec(n("shoulder_yoke"), None, 0.03))

    J(JointSpec(n("shoulder_yaw"), n("shoulder_yoke"), n("upper_arm"),
                axis=(0, 0, 1), lower=radians(-90), upper=radians(90),
                effort=60, velocity=3.5))
    L(LinkSpec(n("upper_arm"), _mirror(P.build_upper_arm(), s), 1.8,
               [("cyl", 0.050, 0.30, (0, 0, -0.150))]))

    J(JointSpec(n("elbow"), n("upper_arm"), n("forearm"),
                xyz=(0, 0, -0.300), axis=(0, -1, 0),
                lower=radians(0), upper=radians(150), effort=90, velocity=4.0))
    L(LinkSpec(n("forearm"), _mirror(P.build_forearm(), s), 1.2,
               [("cyl", 0.042, 0.26, (0, 0, -0.130))]))

    J(JointSpec(n("wrist_pitch"), n("forearm"), n("wrist"),
                xyz=(0, 0, -0.258), axis=(0, -1, 0),
                lower=radians(-75), upper=radians(75), effort=22, velocity=5.0))
    L(LinkSpec(n("wrist"), _mirror(P.build_wrist(), s), 0.25,
               [("cyl", 0.031, 0.026, (0, 0, -0.009))]))

    J(JointSpec(n("wrist_dev"), n("wrist"), n("hand_base"),
                xyz=(0, 0, -0.024), axis=(1, 0, 0),
                lower=radians(-35), upper=radians(35), effort=16, velocity=5.0))
    L(LinkSpec(n("hand_base"), _mirror(P.build_hand_base(), s), 0.08,
               [("cyl", 0.028, 0.014, (0, 0, -0.005))]))

    J(JointSpec(n("wrist_roll"), n("hand_base"), n("hand"),
                xyz=(0, 0, -0.012), axis=(0, 0, 1),
                lower=radians(-110), upper=radians(110), effort=12, velocity=6.0))
    L(LinkSpec(n("hand"), _mirror(P.build_hand(), s), 0.42,
               [("box", (0.046, 0.094, 0.100), (0.001, 0, -0.050))]))

    # ---- 四指 ----
    for (finger, fy, fz, lens, radii) in P.FINGERS:
        _finger(rb, s, side, finger, fy, fz, lens, radii)

    # ---- 拇指 ----
    _thumb(rb, s, side)


def _finger(rb, s, side, finger, fy, fz, lens, radii):
    L, J = rb.link, rb.joint
    n = lambda x: "%s_%s_%s" % (side, finger, x)
    y = s * fy

    J(JointSpec(n("mcp_abd"), "%s_hand" % side, n("knuckle"),
                xyz=(0.0, y, fz), axis=(1, 0, 0),
                lower=radians(-18), upper=radians(18), effort=2.0, velocity=8.0,
                damping=0.02))
    L(LinkSpec(n("knuckle"), None, 0.006))

    J(JointSpec(n("mcp"), n("knuckle"), n("prox"), axis=(0, 1, 0),
                lower=radians(-20), upper=radians(95), effort=3.0, velocity=10.0,
                damping=0.02))
    L(LinkSpec(n("prox"), P.build_phalanx(lens[0], radii[0], radii[1]), 0.028,
               [("cyl", radii[0], lens[0], (0, 0, -lens[0] * 0.5))]))

    J(JointSpec(n("pip"), n("prox"), n("mid"), xyz=(0, 0, -lens[0]),
                axis=(0, 1, 0), lower=radians(0), upper=radians(110),
                effort=2.5, velocity=10.0, damping=0.02))
    L(LinkSpec(n("mid"), P.build_phalanx(lens[1], radii[1], radii[2]), 0.018,
               [("cyl", radii[1], lens[1], (0, 0, -lens[1] * 0.5))]))

    J(JointSpec(n("dip"), n("mid"), n("dist"), xyz=(0, 0, -lens[1]),
                axis=(0, 1, 0), lower=radians(0), upper=radians(80),
                effort=1.5, velocity=10.0, damping=0.02))
    L(LinkSpec(n("dist"), P.build_phalanx(lens[2], radii[2], radii[2] * 0.82), 0.014,
               [("cyl", radii[2] * 0.9, lens[2], (0, 0, -lens[2] * 0.5))]))


def _thumb(rb, s, side):
    L, J = rb.link, rb.joint
    n = lambda x: "%s_thumb_%s" % (side, x)
    bx, by, bz = P.THUMB_BASE
    seg = P.THUMB_SEGS

    J(JointSpec(n("cmc_abd"), "%s_hand" % side, n("knuckle"),
                xyz=(bx, s * by, bz), rpy=(P.THUMB_RPY[0] * s, 0, 0),
                axis=(1, 0, 0), lower=radians(-15), upper=radians(15),
                effort=2.5, velocity=8.0, damping=0.02))
    L(LinkSpec(n("knuckle"), None, 0.008))

    J(JointSpec(n("cmc_flex"), n("knuckle"), n("metacarpal"), axis=(0, 1, 0),
                lower=radians(-10), upper=radians(60), effort=3.5, velocity=8.0,
                damping=0.02))
    L(LinkSpec(n("metacarpal"), P.build_phalanx(seg[0][0], seg[0][1], seg[0][2]), 0.035,
               [("cyl", seg[0][1], seg[0][0], (0, 0, -seg[0][0] * 0.5))]))

    J(JointSpec(n("mcp"), n("metacarpal"), n("prox"), xyz=(0, 0, -seg[0][0]),
                axis=(0, 1, 0), lower=radians(-10), upper=radians(70),
                effort=3.0, velocity=10.0, damping=0.02))
    L(LinkSpec(n("prox"), P.build_phalanx(seg[1][0], seg[1][1], seg[1][2]), 0.022,
               [("cyl", seg[1][1], seg[1][0], (0, 0, -seg[1][0] * 0.5))]))

    J(JointSpec(n("ip"), n("prox"), n("dist"), xyz=(0, 0, -seg[1][0]),
                axis=(0, 1, 0), lower=radians(-10), upper=radians(80),
                effort=2.0, velocity=10.0, damping=0.02))
    L(LinkSpec(n("dist"), P.build_phalanx(seg[2][0], seg[2][1], seg[2][2]), 0.016,
               [("cyl", seg[2][1], seg[2][0], (0, 0, -seg[2][0] * 0.5))]))


def _leg(rb, s, side):
    L, J = rb.link, rb.joint
    n = lambda x: "%s_%s" % (side, x)

    J(JointSpec(n("hip_yaw"), "pelvis", n("hip"),
                xyz=(0, s * 0.095, -0.020), axis=(0, 0, 1),
                lower=radians(-40), upper=radians(40), effort=180, velocity=3.0))
    L(LinkSpec(n("hip"), _mirror(P.build_hip(), s), 0.5,
               [("sphere", 0.070, (0, 0, 0))]))

    J(JointSpec(n("hip_roll"), n("hip"), n("hip_yoke"),
                axis=(1, 0, 0), lower=radians(-45), upper=radians(45),
                effort=200, velocity=3.0))
    L(LinkSpec(n("hip_yoke"), None, 0.03))

    J(JointSpec(n("hip_pitch"), n("hip_yoke"), n("thigh"),
                axis=(0, -1, 0), lower=radians(-30), upper=radians(120),
                effort=220, velocity=3.0))
    L(LinkSpec(n("thigh"), _mirror(P.build_thigh(), s), 3.6,
               [("cyl", 0.082, 0.41, (0, 0, -0.205))]))

    J(JointSpec(n("knee"), n("thigh"), n("shank"),
                xyz=(0, 0, -0.410), axis=(0, 1, 0),
                lower=radians(0), upper=radians(145), effort=240, velocity=3.5))
    L(LinkSpec(n("shank"), _mirror(P.build_shank(), s), 2.4,
               [("cyl", 0.058, 0.38, (0, 0, -0.190))]))

    J(JointSpec(n("ankle_pitch"), n("shank"), n("ankle"),
                xyz=(0, 0, -0.380), axis=(0, -1, 0),
                lower=radians(-45), upper=radians(40), effort=110, velocity=4.0))
    L(LinkSpec(n("ankle"), _mirror(P.build_ankle(), s), 0.25,
               [("sphere", 0.038, (0, 0, 0))]))

    J(JointSpec(n("ankle_roll"), n("ankle"), n("foot"),
                axis=(1, 0, 0),
                lower=radians(-22), upper=radians(22), effort=60, velocity=4.0))
    L(LinkSpec(n("foot"), _mirror(P.build_foot(), s), 0.85,
               [("box", (0.16, 0.10, 0.06), (0.045, 0, -0.060))]))

    J(JointSpec(n("toe"), n("foot"), n("toe"),
                xyz=(0.124, 0, -0.060), axis=(0, -1, 0),
                lower=radians(-20), upper=radians(60), effort=45, velocity=4.0))
    L(LinkSpec(n("toe"), _mirror(P.build_toe(), s), 0.18,
               [("box", (0.09, 0.09, 0.056), (0.044, 0, -0.002))]))


# --------------------------------------------------------------------------- #
#  预设姿态（度）
# --------------------------------------------------------------------------- #

def _fingers(side, mcp, pip, dip, abd=0.0):
    out = {}
    for (f, _, _, _, _) in P.FINGERS:
        base = "%s_%s_" % (side, f)
        out[base + "mcp"] = mcp
        out[base + "pip"] = pip
        out[base + "dip"] = dip
        out[base + "mcp_abd"] = abd
    return out


def presets():
    poses = {}

    poses["rest"] = {
        "note": "站立中立位：双臂自然下垂，五指伸直微屈",
        "joints": dict(_fingers("l", 8, 6, 4), **_fingers("r", 8, 6, 4)),
    }

    # ---- 握笔：右手拇指+食指捏合，中指托住笔杆；站在工作台前 ----
    pen = {}
    pen.update(dict(_fingers("r", 40, 45, 20)))
    pen.update({
        "r_index_mcp": 30, "r_index_pip": 38, "r_index_dip": 16,
        "r_middle_mcp": 58, "r_middle_pip": 66, "r_middle_dip": 26,
        "r_ring_mcp": 70, "r_ring_pip": 74, "r_ring_dip": 30,
        "r_little_mcp": 76, "r_little_pip": 78, "r_little_dip": 32,
        "r_index_mcp_abd": 5,
        "r_thumb_cmc_abd": 6, "r_thumb_cmc_flex": 30,
        "r_thumb_mcp": 38, "r_thumb_ip": 24,
        "r_shoulder_pitch": 12, "r_shoulder_roll": -12, "r_shoulder_yaw": 4,
        "r_elbow": 45, "r_wrist_pitch": 24, "r_wrist_dev": 4, "r_wrist_roll": 20,
        "spine_pitch": 6, "spine_yaw": -3, "neck_pitch": 14,
    })
    pen.update(dict(_fingers("l", 10, 8, 6)))
    pen.update({"l_shoulder_pitch": 5, "l_shoulder_roll": -6, "l_elbow": 12})
    poses["pen_grip"] = {
        "note": "右手三指握笔。手臂链：肩 12° + 肘 45° → 手腕落在 z≈1.00 m、身前 0.27 m，"
                "适合站在 0.90~0.95 m 的工作台前书写；台面更低时把 shoulder_pitch 调小即可",
        "joints": pen}

    # ---- 书写笔画关键帧：手腕下压 + 小幅度偏转，可循环成笔画 ----
    wr = dict(pen)
    wr.update({"r_wrist_pitch": 34, "r_wrist_roll": 12, "r_wrist_dev": -6,
               "r_elbow": 48, "spine_pitch": 8, "neck_pitch": 17})
    poses["pen_write"] = {"note": "落笔关键帧（配合 pen_grip 循环做笔画轨迹）", "joints": wr}

    # ---- 炒菜：左手扶锅、右手持铲 ----
    cook = {}
    cook.update(dict(_fingers("l", 45, 58, 30)))
    cook.update(dict(_fingers("r", 34, 46, 22)))
    cook.update({
        "l_shoulder_pitch": 34, "l_shoulder_roll": -26, "l_elbow": 58,
        "l_wrist_pitch": 10, "l_wrist_roll": 34,
        "r_shoulder_pitch": 46, "r_shoulder_roll": -10, "r_elbow": 44,
        "r_wrist_pitch": 20, "r_wrist_roll": 10,
        "r_thumb_cmc_flex": 32, "r_thumb_mcp": 34,
        "spine_pitch": 5, "neck_pitch": 12,
    })
    poses["cook_stir"] = {"note": "炒菜：左扶锅、右持铲；循环改 r_elbow/r_wrist_roll 即为翻炒", "joints": cook}

    # ---- 叠被子 / 桌面折叠：双臂前伸下压，掌心朝下 ----
    fold = {}
    fold.update(dict(_fingers("l", 14, 22, 12)))
    fold.update(dict(_fingers("r", 14, 22, 12)))
    fold.update({
        "l_shoulder_pitch": 52, "l_shoulder_roll": -18, "l_elbow": 28,
        "l_wrist_pitch": 34, "l_wrist_roll": 0,
        "r_shoulder_pitch": 52, "r_shoulder_roll": -18, "r_elbow": 28,
        "r_wrist_pitch": 34, "r_wrist_roll": 0,
        "spine_pitch": 16, "neck_pitch": 18,
        "l_hip_pitch": 2, "r_hip_pitch": 2,
    })
    poses["fold_blanket"] = {"note": "叠被子/叠衣物：双臂前伸下压、掌心朝下", "joints": fold}

    return poses
