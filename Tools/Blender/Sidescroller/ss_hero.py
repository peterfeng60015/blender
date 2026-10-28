"""主角阿甲（犰狳快遞員）：SsHeroDay（日班制服）、SsHeroExpress（特急制服）。

兩套共用同一副骨架與同一組動作，只換顏色與配件，所以幀數、pivot 完全一致（design.md §8.3）。
"""

import math

import bmesh

from ss_lib import (HIDE, Rig, box, cylinder, ellipsoid, empty, flat, frame_box, rot_y, shade, toon)

SCALE = 1.2          # 建模用 1.25 公尺高的比例，整體放大到約 1.5 單位
CELL = 128
ORTHO = CELL / 64    # 2 公尺

OUTFITS = {
    "Day": dict(
        shell="#C9A26B", band="#8E6B3E", skin="#D8B6A4", cap="#D8342C", cap_stripe=None,
        bag="#1F9E9A", bag_flap="#17807C", strap="#14615E", shorts="#B59A6A", torso=None,
        torso_stripe=None, sleeve=None, shoe="#5A3A26", goggles=None, bag_box=False,
    ),
    "Express": dict(
        shell="#6E7F95", band="#FF7A1A", skin="#D8B6A4", cap="#1B1B22", cap_stripe="#FF7A1A",
        bag="#1B1B22", bag_flap="#FF7A1A", strap="#2A2A33", shorts="#33333A", torso="#1B1B22",
        torso_stripe="#FF7A1A", sleeve="#1B1B22", shoe="#22222A", goggles="#F2C230", bag_box=True,
    ),
}

BONES = {
    "main": ((0, 0, 0.40), None),
    "body": ((0, 0, 0.40), "main"),
    "head": ((0.24, 0, 0.80), "body"),
    "ear_n": ((0.24, -0.08, 0.98), "head"),
    "ear_f": ((0.24, 0.08, 0.98), "head"),
    "leg_n": ((0.05, -0.12, 0.36), "main"),
    "leg_f": ((0.05, 0.12, 0.36), "main"),
    "arm_n": ((0.16, -0.22, 0.66), "body"),
    "arm_f": ((0.16, 0.22, 0.66), "body"),
    "bag": ((0.08, -0.28, 0.70), "body"),
    "tail": ((-0.28, 0, 0.40), "body"),
    "sling": ((0.25, -0.26, 0.44), "arm_n"),
    "ball": ((0, 0, 0.40), None),
}

SHELL_C = (-0.06, 0.0, 0.62)
SHELL_R = (0.33, 0.27, 0.30)


def shell_band(bm, x, mi, center=SHELL_C, radii=SHELL_R, width=0.035, grow=0.014, cut=None):
    """殼上一圈橫向的殼紋：繞 X 軸的扁圓環，大小貼著橢球在 x 處的截面；cut＝只留高於這個 z 的部分。"""
    k = math.sqrt(max(0.0, 1.0 - ((x - center[0]) / radii[0]) ** 2))
    verts = cylinder(bm, (x - width / 2, center[1], center[2]), (x + width / 2, center[1], center[2]), 1.0,
                     mi=mi, seg=28)
    for v in verts:
        dy, dz = v.co.y - center[1], v.co.z - center[2]
        v.co.y = center[1] + dy * (radii[1] * k + grow)
        v.co.z = center[2] + dz * (radii[2] * k + grow)
    if cut is not None:
        kill = [v for v in verts if v.co.z < cut]
        bmesh.ops.delete(bm, geom=kill, context="VERTS")


def build(name, outfit):
    o = OUTFITS[outfit]
    root = empty(name)
    frame_box(root, ORTHO)
    model = empty(f"{name}_Model", root, scale=SCALE)
    rig = Rig(f"{name}_Rig", model, BONES, world_scale=SCALE)

    skin, skin_far = toon(o["skin"]), toon(shade(o["skin"], 0.85))
    shell = toon(o["shell"], hard=True)
    band = toon(o["band"], hard=outfit == "Express")
    dark = flat("#1A1414")
    white = flat("#FFFFFF")
    cap = toon(o["cap"])
    outline_main = shade(o["band"] if outfit == "Day" else "#2A2A33", 0.45)

    # 身體：肚子（或夾克）、短褲、背殼與殼紋
    def body(bm):
        ellipsoid(bm, (0.08, 0, 0.56), (0.22, 0.2, 0.22), mi=1)          # 肚子／夾克
        ellipsoid(bm, (0.0, 0, 0.42), (0.25, 0.21, 0.13), mi=2)          # 短褲
        if o["torso_stripe"]:
            shell_band(bm, 0.16, 4, center=(0.08, 0, 0.56), radii=(0.22, 0.2, 0.22), width=0.05, grow=0.006)
    torso = toon(o["torso"]) if o["torso"] else skin
    rig.part(f"{name}_Body", "body", body,
             [skin, torso, toon(o["shorts"]), shell, toon(o["torso_stripe"] or "#000000")],
             outline_color=outline_main)

    def shell_part(bm):
        ellipsoid(bm, SHELL_C, SHELL_R, mi=0)
        for x in (-0.24, -0.11, 0.02, 0.15):
            shell_band(bm, x, 1, width=0.045, cut=0.42)
    rig.part(f"{name}_Shell", "body", shell_part, [shell, band], outline_color=outline_main)

    def tail(bm):
        cylinder(bm, (-0.27, 0, 0.42), (-0.46, 0, 0.3), 0.055, 0.012, mi=0)
    rig.part(f"{name}_Tail", "tail", tail, [shell], outline_color=outline_main)

    # 頭：尖嘴、鼻頭、半睜的眼（冷面）、帽子
    def head(bm):
        ellipsoid(bm, (0.3, 0, 0.86), (0.16, 0.14, 0.14), mi=0)
        cylinder(bm, (0.38, 0, 0.84), (0.6, 0, 0.78), 0.075, 0.03, mi=0)
        ellipsoid(bm, (0.6, 0, 0.785), (0.035, 0.035, 0.032), mi=1)
    rig.part(f"{name}_Head", "head", head, [skin, dark], outline_color=outline_main)

    def eye(bm):
        ellipsoid(bm, (0.37, -0.118, 0.9), (0.026, 0.02, 0.036), mi=0, seg=12)
        ellipsoid(bm, (0.376, -0.136, 0.892), (0.009, 0.006, 0.009), mi=1, seg=8)
    rig.part(f"{name}_Eye", "head", eye, [dark, white], outline_px=0)

    def lid(bm):
        ellipsoid(bm, (0.368, -0.116, 0.918), (0.034, 0.026, 0.02), mi=0, seg=12, cut_below=-0.2)
    rig.part(f"{name}_Lid", "head", lid, [skin], outline_px=1.5, outline_color=outline_main)

    def cap_part(bm):
        ellipsoid(bm, (0.29, 0, 0.955), (0.155, 0.15, 0.105), mi=0, cut_below=0.0)
        box(bm, (0.45, 0, 0.962), (0.15, 0.2, 0.026), mi=1, bevel=0.008)
        if o["cap_stripe"]:
            ellipsoid(bm, (0.29, 0, 0.955), (0.158, 0.153, 0.108), mi=2, cut_below=0.15)
            box(bm, (0.29, 0, 1.03), (0.33, 0.09, 0.05), mi=2)
        if o["goggles"]:
            cylinder(bm, (0.36, -0.07, 1.02), (0.42, -0.08, 1.04), 0.045, mi=3, seg=12)
            cylinder(bm, (0.36, 0.07, 1.02), (0.42, 0.08, 1.04), 0.045, mi=3, seg=12)
    rig.part(f"{name}_Cap", "head", cap_part,
             [cap, toon(shade(o["cap"], 0.85)), toon(o["cap_stripe"] or "#000000"),
              toon(o["goggles"] or "#000000", hard=True)], outline_color=shade(o["cap"], 0.3))

    for side, bone, mat in ((-1, "ear_n", skin), (1, "ear_f", skin_far)):
        def ear(bm, s=side):
            cylinder(bm, (0.24, 0.08 * s, 0.96), (0.13, 0.1 * s, 1.2), 0.058, 0.004, mi=0, seg=12)
        rig.part(f"{name}_{bone}", bone, ear, [mat], outline_color=outline_main)

    # 手腳：遠側的顏色暗一點，看得出前後
    sleeve = toon(o["sleeve"]) if o["sleeve"] else None
    for side, suffix, k in ((-1, "n", 1.0), (1, "f", 0.85)):
        s_mat = skin if k == 1.0 else skin_far
        shoe = toon(shade(o["shoe"], k))

        def leg(bm, s=side):
            cylinder(bm, (0.05, 0.12 * s, 0.36), (0.07, 0.12 * s, 0.08), 0.072, mi=0, seg=12)
            ellipsoid(bm, (0.12, 0.12 * s, 0.045), (0.095, 0.065, 0.048), mi=1, seg=16)
        rig.part(f"{name}_leg_{suffix}", f"leg_{suffix}", leg, [s_mat, shoe], outline_color=outline_main)

        arm_mats = [toon(shade(o["sleeve"], k)) if sleeve else s_mat, s_mat]

        def arm(bm, s=side):
            cylinder(bm, (0.16, 0.22 * s, 0.66), (0.24, 0.24 * s, 0.47), 0.046, mi=0, seg=12)
            ellipsoid(bm, (0.25, 0.24 * s, 0.44), (0.05, 0.05, 0.05), mi=1, seg=12)
        rig.part(f"{name}_arm_{suffix}", f"arm_{suffix}", arm, arm_mats, outline_color=outline_main)

    # 郵差包（特急是黑色快遞箱）：掛在肩上的骨頭，甩包時整個繞肩膀轉
    def bag(bm):
        cylinder(bm, (0.08, -0.29, 0.7), (0.02, -0.31, 0.53), 0.018, mi=2, seg=8)
        if o["bag_box"]:
            box(bm, (0.0, -0.33, 0.42), (0.27, 0.14, 0.24), mi=0, bevel=0.02)
            box(bm, (0.0, -0.403, 0.42), (0.27, 0.01, 0.045), mi=1)
        else:
            box(bm, (0.0, -0.33, 0.42), (0.25, 0.1, 0.2), mi=0, bevel=0.03)
            box(bm, (0.0, -0.385, 0.47), (0.25, 0.012, 0.11), mi=1, bevel=0.005)
            box(bm, (0.0, -0.393, 0.44), (0.05, 0.008, 0.04), mi=3)
    rig.part(f"{name}_Bag", "bag", bag,
             [toon(o["bag"], hard=o["bag_box"]), toon(o["bag_flap"]), toon(o["strap"]), toon("#E3B53B", hard=True)],
             outline_color=shade(o["bag"], 0.3) if outfit == "Day" else "#0A0A0E")

    # 彈弓：只在 Shoot 顯示，擺成直立的 Y 形
    def sling(bm):
        wood = 0
        cylinder(bm, (0.25, -0.3, 0.38), (0.25, -0.3, 0.5), 0.016, mi=wood, seg=8)
        cylinder(bm, (0.25, -0.3, 0.5), (0.21, -0.3, 0.6), 0.013, mi=wood, seg=8)
        cylinder(bm, (0.25, -0.3, 0.5), (0.29, -0.3, 0.6), 0.013, mi=wood, seg=8)
        cylinder(bm, (0.21, -0.31, 0.6), (0.29, -0.31, 0.6), 0.006, mi=1, seg=6)
    rig.part(f"{name}_Sling", "sling", sling, [toon("#7A4B2A"), flat("#3A2A2A")], outline_px=2,
             outline_color="#2A1A10")

    # 縮成球：跳、落、滾殼共用；殼紋與帽子露出一角，看得出在轉
    def ball(bm):
        c, r = (0, 0, 0.4), (0.38, 0.38, 0.38)
        ellipsoid(bm, c, r, mi=0)
        for x in (-0.2, 0.0, 0.2):
            shell_band(bm, x, 1, center=c, radii=r, width=0.04, grow=0.012)
        ellipsoid(bm, (0.2, 0, 0.72), (0.14, 0.14, 0.08), mi=2, rot=rot_y(30), cut_below=0.0)
        cylinder(bm, (0.27, -0.02, 0.62), (0.37, -0.04, 0.69), 0.04, 0.003, mi=3, seg=10)
    rig.part(f"{name}_Ball", "ball", ball, [shell, band, cap, skin], outline_color=outline_main)

    def base(pose, ball=False, sling=False):
        if ball:
            pose("main", s=HIDE)
        else:
            pose("ball", s=HIDE)
        if not sling:
            pose("sling", s=HIDE)

    def idle(i, t, pose):
        base(pose)
        w = 2 * math.pi * t
        pose("body", z=0.012 * math.sin(w), r=1.5 * math.sin(w))
        pose("head", r=2 * math.sin(w + 0.5))
        pose("ear_n", r=6 * math.sin(w + 1.2))
        pose("ear_f", r=4 * math.sin(w + 1.6))
        pose("tail", r=6 * math.sin(w))
        pose("bag", r=2 * math.sin(w))
        pose("arm_n", r=4 * math.sin(w))
        pose("arm_f", r=-3 * math.sin(w))

    def run(i, t, pose):
        base(pose)
        w = 2 * math.pi * t
        s = math.sin(w)
        pose("main", z=0.045 * abs(math.cos(w)) - 0.02)
        pose("body", r=-10)
        pose("head", r=4)
        pose("leg_n", r=42 * s)
        pose("leg_f", r=-42 * s)
        pose("arm_n", r=-38 * s + 10)
        pose("arm_f", r=38 * s + 10)
        pose("ear_n", r=18 + 8 * math.sin(2 * w))
        pose("ear_f", r=14 + 8 * math.sin(2 * w + 0.6))
        pose("bag", r=-18 + 10 * math.sin(w + 1.0))
        pose("tail", r=8 * math.sin(2 * w))

    def jump(i, t, pose):
        base(pose, ball=True)
        pose("ball", r=-60 * i)   # 往前滾＝畫面上順時針

    melee_keys = [  # 包、身體、近側手、耳朵
        (-70, 6, -40, 10),
        (10, -2, 10, 4),
        (98, -14, 75, -6),
        (122, -12, 90, -10),
        (35, -4, 25, 2),
    ]

    def melee(i, t, pose):
        base(pose)
        bag_r, body_r, arm_r, ear_r = melee_keys[i]
        pose("bag", r=bag_r)
        pose("body", r=body_r)
        pose("arm_n", r=arm_r)
        pose("arm_f", r=-body_r)
        pose("head", r=-body_r * 0.3)
        pose("ear_n", r=ear_r + 8)
        pose("ear_f", r=ear_r + 4)
        pose("leg_n", r=[8, 0, -12, -12, -4][i])
        pose("leg_f", r=[-6, 0, 14, 14, 6][i])

    shoot_keys = [  # 近側手、遠側手（拉皮筋）、身體
        (78, 40, 2),
        (84, 70, 5),
        (86, 18, 8),
        (70, 30, 3),
    ]

    def shoot(i, t, pose):
        base(pose, sling=True)
        arm_n, arm_f, body_r = shoot_keys[i]
        pose("arm_n", r=arm_n)
        pose("sling", r=-arm_n)
        pose("arm_f", r=arm_f)
        pose("body", r=body_r)
        pose("head", r=-body_r * 0.5)
        pose("bag", r=-6)
        pose("ear_n", r=10 + (6 if i == 2 else 0))
        pose("ear_f", r=8)
        pose("leg_n", r=10)
        pose("leg_f", r=-8)

    def hurt(i, t, pose):
        base(pose)
        k = [0.7, 1.0, 0.6][i]
        pose("main", x=-0.05 * k)
        pose("body", r=20 * k)
        pose("head", r=12 * k)
        pose("ear_n", r=35 * k)
        pose("ear_f", r=30 * k)
        pose("arm_n", r=70 * k)
        pose("arm_f", r=55 * k)
        pose("bag", r=-30 * k)
        pose("leg_n", r=25 * k)
        pose("leg_f", r=10 * k)
        pose("tail", r=-20 * k)

    for act, length, fn in (("Idle", 6, idle), ("Run", 8, run), ("Jump", 6, jump),
                            ("Melee", 5, melee), ("Shoot", 4, shoot), ("Hurt", 3, hurt)):
        rig.action(act, length, fn)
    rig.done()
    return rig.obj.name


def build_day():
    return build("SsHeroDay", "Day")


def build_express():
    return build("SsHeroExpress", "Express")
