"""配角與敵人：SsRat（業務鼠）、SsPigeon（監工鴿）、SsRhino（保全犀牛）、SsBoss（河董）、SsMentor（龜婆）。

全部面朝 +X，main 骨頭放在腳底（原點），整體壓扁／拉長時腳底不會離地。
"""

import math

from ss_lib import HIDE, Rig, box, cylinder, ellipsoid, empty, flat, frame_box, poly, rot_y, rot_z, shade, toon

DARK = "#1A1414"
WHITE = "#FFFFFF"


def setup(name, cell, bones, scale=1.0, shift_x=0.0):
    """shift_x：整個模型相對 pivot 往前後挪（頭重腳輕的角色，讓前傾的動作不出格）。"""
    root = empty(name)
    frame_box(root, cell / 64)
    model = empty(f"{name}_Model", root, scale=scale, location=(shift_x, 0, 0))
    return Rig(f"{name}_Rig", model, bones, world_scale=scale)


def eye(bm, c, r, mi_eye, mi_glint, toward=-1):
    """側臉的一隻眼：黑眼珠＋白色反光點（toward＝-1 朝鏡頭）。"""
    ellipsoid(bm, c, r, mi=mi_eye, seg=12)
    ellipsoid(bm, (c[0] + r[0] * 0.3, c[1] + toward * r[1] * 0.9, c[2] + r[2] * 0.3),
              (r[0] * 0.35, r[1] * 0.3, r[2] * 0.3), mi=mi_glint, seg=8)


def star(bm, c, r, mi):
    """五角星（從 -Y 看），量眩用。"""
    pts = []
    for k in range(10):
        a = math.pi / 2 + k * math.pi / 5
        rr = r if k % 2 == 0 else r * 0.45
        pts.append((c[0] + rr * math.cos(a), c[2] + rr * math.sin(a)))
    poly(bm, pts, y=c[1], mi=mi, depth=0.02)


# ---------- 業務鼠 ----------

def build_rat():
    name = "SsRat"
    gray, suit, tie, case = "#8C8F99", "#33507A", "#C8312B", "#6B4A2B"
    rig = setup(name, 128, {
        "main": ((0, 0, 0), None),
        "body": ((0, 0, 0.5), "main"),
        "head": ((0.06, 0, 1.0), "body"),
        "ear_n": ((-0.01, -0.07, 1.2), "head"),
        "ear_f": ((-0.01, 0.07, 1.2), "head"),
        "leg_n": ((0, -0.08, 0.5), "main"),
        "leg_f": ((0, 0.08, 0.5), "main"),
        "arm_n": ((0.03, -0.15, 0.92), "body"),
        "arm_f": ((0.03, 0.15, 0.92), "body"),
        "tail": ((-0.13, 0, 0.52), "body"),
    })
    edge = shade(suit, 0.35)
    skin, skin_far = toon(gray), toon(shade(gray, 0.85))
    for s, sfx, k in ((-1, "n", 1.0), (1, "f", 0.85)):
        def leg(bm, s=s):
            cylinder(bm, (0, 0.08 * s, 0.52), (0.02, 0.08 * s, 0.09), 0.052, mi=0, seg=12)
            ellipsoid(bm, (0.07, 0.08 * s, 0.045), (0.1, 0.05, 0.045), mi=1, seg=12)
        rig.part(f"{name}_leg_{sfx}", f"leg_{sfx}", leg, [toon(shade(suit, k)), toon(shade("#22222A", k))],
                 outline_color=edge)

        def arm(bm, s=s):
            cylinder(bm, (0.03, 0.15 * s, 0.92), (0.07, 0.16 * s, 0.68), 0.042, mi=0, seg=12)
            ellipsoid(bm, (0.075, 0.16 * s, 0.64), (0.038, 0.038, 0.04), mi=1, seg=10)
            if s < 0:   # 近側的手提公事包
                cylinder(bm, (0.04, -0.19, 0.62), (0.11, -0.19, 0.62), 0.012, mi=3, seg=6)
                box(bm, (0.075, -0.2, 0.5), (0.3, 0.07, 0.2), mi=2, bevel=0.015)
                box(bm, (0.075, -0.236, 0.56), (0.05, 0.006, 0.03), mi=3)
        rig.part(f"{name}_arm_{sfx}", f"arm_{sfx}", arm,
                 [toon(shade(suit, k)), skin if k == 1 else skin_far, toon(case, hard=True), toon("#E3B53B", hard=True)],
                 outline_color=edge)

    def body(bm):
        ellipsoid(bm, (0, 0, 0.74), (0.16, 0.14, 0.3), mi=0)
        ellipsoid(bm, (0.09, 0, 0.88), (0.08, 0.09, 0.11), mi=1)
        box(bm, (0.165, 0, 0.8), (0.03, 0.045, 0.2), mi=2, rot=rot_y(-8))
        box(bm, (0.08, -0.14, 0.86), (0.07, 0.012, 0.045), mi=1)
    rig.part(f"{name}_Body", "body", body, [toon(suit, hard=True), toon(WHITE), toon(tie)], outline_color=edge)

    def tail(bm):
        cylinder(bm, (-0.13, 0, 0.52), (-0.36, 0, 0.42), 0.018, 0.014, mi=0, seg=8)
        cylinder(bm, (-0.36, 0, 0.42), (-0.5, 0, 0.52), 0.014, 0.006, mi=0, seg=8)
    rig.part(f"{name}_Tail", "tail", tail, [toon("#D99AA6")], outline_px=2, outline_color=edge)

    def head(bm):
        ellipsoid(bm, (0.09, 0, 1.1), (0.13, 0.11, 0.12), mi=0)
        cylinder(bm, (0.16, 0, 1.08), (0.36, 0, 1.03), 0.075, 0.02, mi=0)
        ellipsoid(bm, (0.36, 0, 1.035), (0.026, 0.026, 0.024), mi=1, seg=10)
        ellipsoid(bm, (0.08, 0, 1.2), (0.11, 0.1, 0.035), mi=2, rot=rot_y(10))   # 油頭
    rig.part(f"{name}_Head", "head", head, [skin, toon("#E39AA8"), toon("#55586A", hard=True)], outline_color=edge)

    def face(bm):
        eye(bm, (0.2, -0.085, 1.13), (0.02, 0.014, 0.028), 0, 1)
        box(bm, (0.2, -0.095, 1.17), (0.06, 0.01, 0.012), mi=0, rot=rot_y(12))   # 斜眉，奸笑
        for dz, ang in ((0.0, 5), (-0.02, -6)):
            cylinder(bm, (0.3, -0.04, 1.04 + dz), (0.42, -0.06, 1.04 + dz + ang * 0.004), 0.004, mi=0, seg=4)
    rig.part(f"{name}_Face", "head", face, [flat(DARK), flat(WHITE)], outline_px=0)

    for s, bone, k in ((-1, "ear_n", 1.0), (1, "ear_f", 0.85)):
        def ear(bm, s=s):
            ellipsoid(bm, (-0.02, 0.07 * s, 1.27), (0.09, 0.022, 0.09), mi=0, rot=rot_y(-15))
            ellipsoid(bm, (-0.01, 0.07 * s - 0.012, 1.27), (0.06, 0.012, 0.06), mi=1, rot=rot_y(-15))
        rig.part(f"{name}_{bone}", bone, ear, [toon(shade(gray, k)), toon(shade("#E39AA8", k))], outline_color=edge)

    def idle(i, t, pose):
        w = 2 * math.pi * t
        pose("body", z=0.01 * math.sin(w), r=2 * math.sin(w))
        pose("head", r=-3 * math.sin(w))
        pose("tail", r=8 * math.sin(w))
        pose("arm_n", r=4 * math.sin(w))
        pose("ear_n", r=5 * math.sin(w + 1))

    def walk(i, t, pose):
        w = 2 * math.pi * t
        s = math.sin(w)
        pose("main", z=0.03 * abs(math.cos(w)))
        pose("body", r=-6)
        pose("leg_n", r=30 * s)
        pose("leg_f", r=-30 * s)
        pose("arm_n", r=-12 * s)
        pose("arm_f", r=20 * s)
        pose("tail", r=10 * math.sin(2 * w))
        pose("head", r=3 * math.sin(2 * w))

    attack = [  # 身體、近側手（公事包）、頭
        (6, -40, 0), (16, -115, 6), (18, -120, 8), (-16, 95, -6), (-20, 122, -8), (-6, 30, -2)]

    def atk(i, t, pose):
        b, a, h = attack[i]
        pose("body", r=b)
        pose("arm_n", r=a)
        pose("arm_f", r=-b)
        pose("head", r=h)
        pose("leg_n", r=[0, 10, 10, -20, -20, -6][i])
        pose("leg_f", r=[0, -8, -8, 16, 16, 4][i])
        pose("tail", r=[0, 15, 18, -15, -18, -5][i])

    def hurt(i, t, pose):
        k = [0.7, 1.0, 0.6][i]
        pose("main", x=-0.05 * k)
        pose("body", r=18 * k)
        pose("head", r=14 * k)
        pose("ear_n", r=30 * k)
        pose("ear_f", r=25 * k)
        pose("arm_n", r=60 * k)
        pose("arm_f", r=70 * k)
        pose("leg_n", r=20 * k)
        pose("tail", r=-30 * k)

    for act, n, fn in (("Idle", 4, idle), ("Walk", 8, walk), ("Attack", 6, atk), ("Hurt", 3, hurt)):
        rig.action(act, n, fn)
    rig.done()


# ---------- 監工鴿 ----------

def build_pigeon():
    name = "SsPigeon"
    body_c, hat, beak = "#7D8FA6", "#F2C230", "#E8A33A"
    rig = setup(name, 128, {
        "main": ((0, 0, 0), None),
        "body": ((0, 0, 0.45), "main"),
        "head": ((0.18, 0, 0.62), "body"),
        "wing_n": ((0.02, -0.2, 0.6), "body"),
        "wing_f": ((0.02, 0.2, 0.6), "body"),
        "feet": ((0.02, 0, 0.26), "body"),
        "bomb": ((0.02, 0, 0.12), "feet"),
        "stars": ((0.2, 0, 0.92), "main"),
    })
    edge = shade(body_c, 0.3)

    def body(bm):
        ellipsoid(bm, (0, 0, 0.45), (0.28, 0.21, 0.24), mi=0)
        ellipsoid(bm, (0.1, 0, 0.42), (0.18, 0.17, 0.18), mi=1)               # 胸前淺色
        ellipsoid(bm, (-0.3, 0, 0.5), (0.16, 0.12, 0.05), mi=2, rot=rot_y(20))  # 尾羽
        ellipsoid(bm, (0.1, -0.02, 0.58), (0.12, 0.17, 0.08), mi=3)            # 頸部虹彩
    rig.part(f"{name}_Body", "body", body,
             [toon(body_c), toon("#A9B6C8"), toon(shade(body_c, 0.8)), toon("#5FA58E")], outline_color=edge)

    def head(bm):
        ellipsoid(bm, (0.22, 0, 0.68), (0.13, 0.11, 0.12), mi=0)
        cylinder(bm, (0.32, 0, 0.67), (0.43, 0, 0.64), 0.035, 0.008, mi=1, seg=10)
        ellipsoid(bm, (0.21, 0, 0.78), (0.14, 0.13, 0.08), mi=2, cut_below=0.0)   # 工地帽
        box(bm, (0.32, 0, 0.785), (0.12, 0.2, 0.02), mi=2)
        box(bm, (0.21, 0, 0.84), (0.2, 0.02, 0.03), mi=3)
    rig.part(f"{name}_Head", "head", head, [toon(body_c), toon(beak), toon(hat, hard=True), toon(shade(hat, 0.8))],
             outline_color=edge)

    def face(bm):
        ellipsoid(bm, (0.29, -0.085, 0.7), (0.032, 0.015, 0.032), mi=1, seg=12)
        eye(bm, (0.3, -0.096, 0.695), (0.014, 0.01, 0.018), 0, 1)
        box(bm, (0.29, -0.1, 0.735), (0.07, 0.01, 0.015), mi=0, rot=rot_y(-15))  # 皺眉
    rig.part(f"{name}_Face", "head", face, [flat(DARK), flat(WHITE)], outline_px=0)

    for s, bone, k in ((-1, "wing_n", 1.0), (1, "wing_f", 0.8)):
        def wing(bm, s=s):
            ellipsoid(bm, (-0.14, 0.2 * s, 0.56), (0.24, 0.03, 0.11), mi=0, rot=rot_y(10))
            ellipsoid(bm, (-0.28, 0.2 * s, 0.53), (0.12, 0.025, 0.07), mi=1, rot=rot_y(15))
        rig.part(f"{name}_{bone}", bone, wing, [toon(shade(body_c, k)), toon(shade("#4E5D72", k))],
                 outline_color=edge)

    def feet(bm):
        for y in (-0.06, 0.06):
            cylinder(bm, (0.02, y, 0.26), (0.02, y, 0.17), 0.018, mi=0, seg=8)
    rig.part(f"{name}_Feet", "feet", feet, [toon(beak)], outline_px=2, outline_color=edge)

    def bomb(bm):   # 抓著的公文炸彈（丟出去後由 SsBomb 接手）
        box(bm, (0.02, 0, 0.1), (0.2, 0.12, 0.14), mi=0, bevel=0.01)
        box(bm, (0.02, 0, 0.1), (0.21, 0.13, 0.03), mi=1)
        cylinder(bm, (0.1, 0, 0.17), (0.13, 0, 0.22), 0.008, mi=2, seg=6)
    rig.part(f"{name}_Bomb", "bomb", bomb, [toon("#F2EEDC"), toon("#C8312B"), flat("#FFB000")], outline_px=2,
             outline_color="#3A2A1A")

    def stars(bm):
        for k, (x, z) in enumerate(((-0.14, 0.0), (0.0, 0.06), (0.14, 0.0))):
            star(bm, (0.2 + x, -0.15, 0.92 + z), 0.05, 0)
    rig.part(f"{name}_Stars", "stars", stars, [flat("#FFE45C")], outline_px=1.5, outline_color="#6A4A00")

    def fly(i, t, pose, hold=True):
        w = 2 * math.pi * t
        pose("main", z=0.04 * math.sin(w))
        pose("wing_n", r=-55 * math.cos(w) + 10)
        pose("wing_f", r=-55 * math.cos(w + 0.3) + 10)
        pose("body", r=-4 + 3 * math.sin(w))
        pose("head", r=2 * math.sin(w + 1))
        pose("stars", s=HIDE)
        if not hold:
            pose("bomb", s=HIDE)

    def attack(i, t, pose):
        fly(i, t * 1.5, pose, hold=i < 2)
        pose("body", r=[-4, -14, -18, -8][i])
        pose("head", r=[0, -10, -14, -4][i])
        pose("feet", r=[0, 10, 35, 10][i])

    def hurt(i, t, pose):
        k = [0.7, 1.0, 0.6][i]
        pose("body", r=20 * k)
        pose("head", r=15 * k)
        pose("wing_n", r=-80 * k)
        pose("wing_f", r=-70 * k)
        pose("feet", r=-30 * k)
        pose("bomb", s=HIDE)
        pose("stars", r=40 * i)

    for act, n, fn in (("Fly", 6, fly), ("Attack", 4, attack), ("Hurt", 3, hurt)):
        rig.action(act, n, fn)
    rig.done()


# ---------- 保全犀牛 ----------

def build_rhino():
    name = "SsRhino"
    hide, navy, shield, horn = "#9A9590", "#26324A", "#C9CDD3", "#E8E0CC"
    rig = setup(name, 160, {
        "main": ((0, 0, 0), None),
        "body": ((0, 0, 0.55), "main"),
        "head": ((0.42, 0, 1.15), "body"),
        "leg_n": ((0.0, -0.18, 0.55), "main"),
        "leg_f": ((0.0, 0.18, 0.55), "main"),
        "arm_n": ((0.25, -0.34, 1.05), "body"),
        "arm_f": ((0.2, 0.34, 1.05), "body"),
        "stars": ((0.45, 0, 1.55), "main"),
    }, shift_x=-0.25)
    edge = shade(navy, 0.4)
    for s, sfx, k in ((-1, "n", 1.0), (1, "f", 0.82)):
        def leg(bm, s=s):
            cylinder(bm, (0, 0.18 * s, 0.6), (0.02, 0.18 * s, 0.1), 0.12, 0.11, mi=0, seg=14)
            ellipsoid(bm, (0.06, 0.18 * s, 0.07), (0.16, 0.12, 0.08), mi=1, seg=14)
        rig.part(f"{name}_leg_{sfx}", f"leg_{sfx}", leg, [toon(shade(navy, k)), toon(shade("#1A1A20", k))],
                 outline_color=edge)

    def body(bm):
        ellipsoid(bm, (0, 0, 0.82), (0.44, 0.34, 0.42), mi=0)
        box(bm, (0, 0, 0.58), (0.84, 0.66, 0.08), mi=1)                       # 腰帶
        box(bm, (0.36, -0.2, 0.58), (0.08, 0.04, 0.08), mi=2)
        box(bm, (0.32, -0.27, 1.0), (0.1, 0.02, 0.12), mi=2, bevel=0.01)      # 胸前徽章
    rig.part(f"{name}_Body", "body", body, [toon(navy), toon("#15151A"), toon("#E3B53B", hard=True)],
             outline_color=edge)

    def arm_f(bm):
        cylinder(bm, (0.2, 0.34, 1.05), (0.34, 0.36, 0.72), 0.09, mi=0, seg=12)
        ellipsoid(bm, (0.36, 0.36, 0.68), (0.08, 0.08, 0.08), mi=1)
    rig.part(f"{name}_arm_f", "arm_f", arm_f, [toon(shade(navy, 0.82)), toon(shade(hide, 0.82))], outline_color=edge)

    def head(bm):
        ellipsoid(bm, (0.52, 0, 1.22), (0.26, 0.22, 0.22), mi=0)
        ellipsoid(bm, (0.74, 0, 1.15), (0.16, 0.17, 0.15), mi=0)
        cylinder(bm, (0.80, 0, 1.25), (0.86, 0, 1.4), 0.075, 0.0, mi=1, seg=14)
        cylinder(bm, (0.66, 0, 1.38), (0.66, 0, 1.38), 0.04, 0.0, mi=1, seg=10)
        cylinder(bm, (0.40, -0.13, 1.38), (0.3, -0.16, 1.42), 0.045, 0.01, mi=0, seg=10)   # 耳朵
        ellipsoid(bm, (0.46, 0, 1.40), (0.2, 0.2, 0.09), mi=2, cut_below=0.0)               # 保全帽
        box(bm, (0.64, 0, 1.41), (0.14, 0.26, 0.025), mi=2)
        box(bm, (0.54, -0.19, 1.45), (0.06, 0.01, 0.05), mi=3)
    rig.part(f"{name}_Head", "head", head, [toon(hide), toon(horn, hard=True), toon(navy), toon("#E3B53B", hard=True)],
             outline_color=shade(hide, 0.3))

    def face(bm):
        eye(bm, (0.64, -0.14, 1.27), (0.02, 0.015, 0.024), 0, 1)
        box(bm, (0.64, -0.155, 1.31), (0.08, 0.01, 0.022), mi=0, rot=rot_y(-18))
        ellipsoid(bm, (0.86, -0.08, 1.12), (0.02, 0.012, 0.012), mi=0, seg=8)
    rig.part(f"{name}_Face", "head", face, [flat(DARK), flat(WHITE)], outline_px=0)

    def arm_n(bm):   # 近側手拿防暴盾：盾面轉向鏡頭一點，側視也看得出是盾
        cylinder(bm, (0.25, -0.34, 1.05), (0.5, -0.4, 0.72), 0.09, mi=0, seg=12)
        m = rot_z(-50)
        box(bm, (0.74, -0.42, 0.6), (0.07, 0.5, 0.74), mi=1, rot=m, bevel=0.025)
        box(bm, (0.72, -0.45, 0.72), (0.075, 0.36, 0.08), mi=2, rot=m)
        box(bm, (0.72, -0.45, 0.48), (0.075, 0.36, 0.08), mi=2, rot=m)
    rig.part(f"{name}_arm_n", "arm_n", arm_n, [toon(navy), toon(shield, hard=True), toon("#E3B53B", hard=True)],
             outline_color="#2A3040")

    def stars(bm):
        for x, z in ((-0.18, 0.0), (0.0, 0.07), (0.18, 0.0)):
            star(bm, (0.45 + x, -0.4, 1.55 + z), 0.065, 0)
    rig.part(f"{name}_Stars", "stars", stars, [flat("#FFE45C")], outline_px=1.5, outline_color="#6A4A00")

    def base(pose):
        pose("stars", s=HIDE)

    def idle(i, t, pose):
        base(pose)
        w = 2 * math.pi * t
        pose("body", z=0.015 * math.sin(w), s=(1.0 + 0.01 * math.sin(w), 1.0))
        pose("head", r=2 * math.sin(w))
        pose("arm_n", r=2 * math.sin(w))

    def walk(i, t, pose):
        base(pose)
        w = 2 * math.pi * t
        s = math.sin(w)
        pose("main", z=0.03 * abs(math.cos(w)))
        pose("body", r=-3 + 2 * math.sin(2 * w))
        pose("leg_n", r=24 * s)
        pose("leg_f", r=-24 * s)
        pose("arm_f", r=15 * s)
        pose("head", r=2 * math.sin(2 * w))

    attack = [(0, 0, 0), (8, -0.06, -10), (10, -0.08, -14), (-10, 0.16, 18), (-10, 0.18, 20), (-4, 0.06, 6)]

    def atk(i, t, pose):
        base(pose)
        b, dx, a = attack[i]
        pose("main", x=dx, z=-0.04 if i in (1, 2) else 0.0)
        pose("body", r=b)
        pose("arm_n", r=a)
        pose("head", r=-b * 0.5)
        pose("leg_n", r=[0, -10, -12, 22, 22, 6][i])
        pose("leg_f", r=[0, 12, 14, -18, -18, -4][i])

    def hurt(i, t, pose):   # 暈眩時循環
        k = [1.0, 0.7, 1.0][i]
        pose("body", r=6 * k * (1 if i != 1 else -1))
        pose("head", r=10 * (1 if i != 1 else -1))
        pose("arm_n", r=-35)
        pose("arm_f", r=25)
        pose("stars", r=-40 * i)

    for act, n, fn in (("Idle", 4, idle), ("Walk", 8, walk), ("Attack", 6, atk), ("Hurt", 3, hurt)):
        rig.action(act, n, fn)
    rig.done()


# ---------- 河董（Boss） ----------

def build_boss():
    name = "SsBoss"
    skin, suit, gold = "#8A7C9C", "#F1EEE6", "#E3B53B"
    rig = setup(name, 320, {
        "main": ((0, 0, 0), None),
        "body": ((0, 0, 0.9), "main"),
        "head": ((0.7, 0, 2.7), "body"),
        "jaw": ((1.0, 0, 2.55), "head"),
        "leg_n": ((0.1, -0.5, 0.95), "main"),
        "leg_f": ((0.1, 0.5, 0.95), "main"),
        "arm_n": ((0.55, -1.0, 2.45), "body"),
        "arm_f": ((0.5, 1.0, 2.45), "body"),
        "coins": ((2.0, 0, 2.5), "head"),
        "sweat": ((0.6, 0, 3.6), "head"),
    }, shift_x=-0.25)
    edge = shade(skin, 0.32)
    s_skin, s_far = toon(skin), toon(shade(skin, 0.85))
    for s, sfx, k in ((-1, "n", 1.0), (1, "f", 0.85)):
        def leg(bm, s=s):
            cylinder(bm, (0.1, 0.5 * s, 1.0), (0.14, 0.5 * s, 0.18), 0.17, 0.15, mi=0, seg=14)
            ellipsoid(bm, (0.26, 0.5 * s, 0.11), (0.3, 0.2, 0.12), mi=1, seg=16)
        rig.part(f"{name}_leg_{sfx}", f"leg_{sfx}", leg, [toon(shade(suit, k)), toon(shade("#2A2230", k), hard=True)],
                 outline_px=4, outline_color=edge)

    def body(bm):
        ellipsoid(bm, (0, 0, 1.95), (1.3, 1.1, 1.35), mi=0)
        ellipsoid(bm, (0.95, 0, 2.15), (0.42, 0.55, 0.5), mi=1, rot=rot_y(-25))     # 粉色襯衫領口
        cylinder(bm, (1.3, 0, 2.3), (1.36, 0, 1.55), 0.12, 0.05, mi=2, seg=10)      # 金領帶
        ellipsoid(bm, (1.3, 0, 2.3), (0.1, 0.12, 0.08), mi=2, seg=12)
        for z in (1.35, 1.0):
            ellipsoid(bm, (1.25, -0.06, z), (0.05, 0.05, 0.05), mi=2, seg=10)
    rig.part(f"{name}_Body", "body", body, [toon(suit, hard=True), toon("#E9A6B8"), toon(gold, hard=True)],
             outline_px=4, outline_color=edge)

    def head(bm):
        ellipsoid(bm, (0.9, 0, 3.0), (0.75, 0.7, 0.6), mi=0)
        ellipsoid(bm, (1.5, 0, 2.75), (0.55, 0.62, 0.42), mi=0)                    # 大嘴的上顎
        ellipsoid(bm, (0.55, -0.42, 3.5), (0.13, 0.1, 0.14), mi=0)                 # 耳
        ellipsoid(bm, (0.55, 0.42, 3.5), (0.13, 0.1, 0.14), mi=0)
        ellipsoid(bm, (0.9, -0.3, 3.45), (0.2, 0.18, 0.18), mi=0)                  # 眼丘
        ellipsoid(bm, (0.9, 0.3, 3.45), (0.2, 0.18, 0.18), mi=0)
        ellipsoid(bm, (1.85, -0.2, 3.03), (0.06, 0.05, 0.03), mi=1, seg=10)        # 鼻孔
        ellipsoid(bm, (1.85, 0.2, 3.03), (0.06, 0.05, 0.03), mi=1, seg=10)
        ellipsoid(bm, (1.45, 0, 2.45), (0.48, 0.5, 0.3), mi=2)                     # 嘴裡
        box(bm, (1.85, -0.3, 2.43), (0.1, 0.08, 0.12), mi=3, bevel=0.02)           # 上排牙
        box(bm, (1.85, 0.3, 2.43), (0.1, 0.08, 0.12), mi=3, bevel=0.02)
    rig.part(f"{name}_Head", "head", head, [s_skin, flat("#3A2A40"), toon("#7A1F2E"), toon(WHITE)],
             outline_px=4, outline_color=edge)

    def face(bm):
        ellipsoid(bm, (1.0, -0.42, 3.5), (0.08, 0.04, 0.09), mi=1, seg=12)
        eye(bm, (1.04, -0.45, 3.49), (0.045, 0.02, 0.055), 0, 1)
        box(bm, (1.0, -0.47, 3.64), (0.24, 0.02, 0.05), mi=0, rot=rot_y(-14))     # 怒眉
    rig.part(f"{name}_Face", "head", face, [flat(DARK), flat(WHITE)], outline_px=0)

    def jaw(bm):
        ellipsoid(bm, (1.5, 0, 2.38), (0.55, 0.58, 0.24), mi=0)
        box(bm, (1.9, -0.32, 2.55), (0.1, 0.08, 0.14), mi=1, bevel=0.02)           # 下排獠牙
        box(bm, (1.9, 0.32, 2.55), (0.1, 0.08, 0.14), mi=1, bevel=0.02)
        ellipsoid(bm, (1.5, 0, 2.5), (0.42, 0.45, 0.12), mi=2)                     # 舌頭
    rig.part(f"{name}_Jaw", "jaw", jaw, [s_skin, toon(WHITE), toon("#C2556B")], outline_px=4, outline_color=edge)

    for s, sfx, k in ((-1, "n", 1.0), (1, "f", 0.85)):
        def arm(bm, s=s):
            cylinder(bm, (0.55, 1.0 * s, 2.45), (0.95, 1.08 * s, 1.75), 0.2, 0.17, mi=0, seg=14)
            ellipsoid(bm, (1.0, 1.08 * s, 1.62), (0.18, 0.18, 0.18), mi=1)
            if s < 0:
                cylinder(bm, (1.1, -1.12, 1.68), (1.13, -1.2, 1.7), 0.08, mi=2, seg=12)   # 金戒指
        rig.part(f"{name}_arm_{sfx}", f"arm_{sfx}", arm,
                 [toon(shade(suit, k), hard=True), s_skin if k == 1 else s_far, toon(gold, hard=True)],
                 outline_px=4, outline_color=edge)

    def coins(bm):   # Spray 時嘴裡噴出的金幣（真正的子彈是 SsCoin）
        for x, z, a in ((2.05, 2.55, 20), (2.25, 2.75, -30), (2.2, 2.35, 60)):
            cylinder(bm, (x, -0.55, z), (x, -0.5, z), 0.13, mi=0, seg=16, smooth=False)
            ellipsoid(bm, (x, -0.56, z), (0.06, 0.005, 0.06), mi=1, seg=8)
    rig.part(f"{name}_Coins", "coins", coins, [toon(gold, hard=True), flat("#B8862A")], outline_px=3,
             outline_color="#6A4A10")

    def sweat(bm):
        for x, z in ((0.45, 3.65), (1.3, 3.55)):
            ellipsoid(bm, (x, -0.65, z), (0.06, 0.02, 0.1), mi=0, seg=10)
    rig.part(f"{name}_Sweat", "sweat", sweat, [toon("#9FE0FF")], outline_px=2, outline_color="#2A6A8A")

    def base(pose, coins=False, sweat=False):
        if not coins:
            pose("coins", s=HIDE)
        if not sweat:
            pose("sweat", s=HIDE)

    def idle(i, t, pose):
        base(pose)
        w = 2 * math.pi * t
        pose("body", s=(1.0 + 0.015 * math.sin(w), 1.0 - 0.01 * math.sin(w)))
        pose("head", r=3 * math.sin(w))
        pose("jaw", r=-4 - 4 * math.sin(w))
        pose("arm_n", r=8 * math.sin(w))
        pose("arm_f", r=-6 * math.sin(w))

    def charge(i, t, pose):
        base(pose)
        if i < 2:   # 跺腳預備
            pose("leg_n", r=[25, 0][i])
            pose("main", z=[0.06, -0.04][i], s=(1.0, [1.02, 0.94][i]))
            pose("body", r=[6, -4][i])
            pose("arm_n", r=[-30, 20][i])
            pose("arm_f", r=[-25, 15][i])
            pose("jaw", r=-12)
            return
        w = 2 * math.pi * (i - 2) / 6
        s = math.sin(w)
        pose("main", z=0.06 * abs(math.cos(w)))
        pose("body", r=-8)
        pose("head", r=-4)
        pose("jaw", r=-18)
        pose("leg_n", r=35 * s)
        pose("leg_f", r=-35 * s)
        pose("arm_n", r=-40 + 20 * s)
        pose("arm_f", r=-40 - 20 * s)

    spray = [(8, 0, 1.03), (4, -8, 1.0), (14, -38, 1.0), (16, -42, 1.0), (8, -20, 1.0), (2, -4, 1.0)]

    def spr(i, t, pose):
        h, j, sc = spray[i]
        base(pose, coins=True)
        pose("head", r=h)
        pose("jaw", r=j)
        pose("body", s=(sc, sc), r=h * 0.3)
        pose("arm_n", r=[-20, 10, 40, 45, 20, 0][i])
        pose("arm_f", r=[-15, 5, 30, 35, 15, 0][i])
        # pose() 會把縮放設回 1，所以金幣的位移與隱藏要在同一次呼叫
        pose("coins", x=0.1 * (i - 2), z=0.08 * (i - 2), s=1.0 if i in (2, 3) else HIDE)

    stomp = [  # main 的 z、壓扁 (sx, sz)、手臂、腿收
        (0.0, (1.06, 0.9), 20, 0), (0.0, (1.1, 0.84), 30, 0), (0.25, (0.94, 1.08), -70, 30),
        (0.35, (0.96, 1.05), -80, 40), (0.25, (1.0, 1.0), -60, 20), (0.0, (1.14, 0.82), 40, -10),
        (0.0, (1.08, 0.9), 30, -5), (0.0, (1.0, 1.0), 5, 0)]

    def stp(i, t, pose):
        z, sc, arm, leg = stomp[i]
        base(pose, sweat=i in (5, 6))
        pose("main", z=z, s=sc)
        pose("arm_n", r=arm)
        pose("arm_f", r=arm * 0.8)
        pose("leg_n", r=leg)
        pose("leg_f", r=-leg)
        pose("jaw", r=-30 if i in (5, 6) else -8)
        pose("head", r=10 if i in (2, 3) else 0)

    def hurt(i, t, pose):
        k = [0.7, 1.0, 0.6][i]
        base(pose, sweat=True)
        pose("main", x=-0.1 * k)
        pose("body", r=10 * k)
        pose("head", r=12 * k)
        pose("jaw", r=-25 * k)
        pose("arm_n", r=70 * k)
        pose("arm_f", r=60 * k)
        pose("leg_n", r=15 * k)

    for act, n, fn in (("Idle", 6, idle), ("Charge", 8, charge), ("Spray", 6, spr), ("Stomp", 8, stp),
                       ("Hurt", 3, hurt)):
        rig.action(act, n, fn)
    rig.done()


# ---------- 龜婆（龜速快遞老闆娘） ----------

def build_mentor():
    name = "SsMentor"
    skin, shell, rim, scarf, apron = "#8DBA74", "#5E7A3A", "#C9B27A", "#D8342C", "#1F9E9A"
    rig = setup(name, 128, {
        "main": ((0, 0, 0), None),
        "body": ((0, 0, 0.3), "main"),
        "head": ((0.2, 0, 0.8), "body"),
        "mouth": ((0.42, 0, 0.84), "head"),
        "arm_n": ((0.15, -0.2, 0.68), "body"),
        "arm_f": ((0.12, 0.2, 0.68), "body"),
        "leg_n": ((0.0, -0.13, 0.3), "main"),
        "leg_f": ((0.0, 0.13, 0.3), "main"),
    })
    edge = shade(shell, 0.35)
    for s, sfx, k in ((-1, "n", 1.0), (1, "f", 0.85)):
        def leg(bm, s=s):
            cylinder(bm, (0, 0.13 * s, 0.32), (0.02, 0.13 * s, 0.06), 0.075, mi=0, seg=12)
            ellipsoid(bm, (0.06, 0.13 * s, 0.045), (0.1, 0.075, 0.045), mi=0, seg=12)
        rig.part(f"{name}_leg_{sfx}", f"leg_{sfx}", leg, [toon(shade(skin, k))], outline_color=edge)

    def body(bm):
        ellipsoid(bm, (-0.06, 0, 0.55), (0.34, 0.3, 0.32), mi=0)                     # 龜殼
        for x, z in ((-0.2, 0.75), (0.0, 0.8), (-0.32, 0.55), (-0.1, 0.55), (0.1, 0.6)):
            ellipsoid(bm, (x, -0.24, z), (0.08, 0.06, 0.07), mi=1, seg=12)            # 殼上的甲片
        ellipsoid(bm, (-0.06, 0, 0.4), (0.37, 0.32, 0.08), mi=2)                      # 殼緣
        ellipsoid(bm, (0.14, 0, 0.5), (0.18, 0.22, 0.24), mi=3)                       # 圍裙
        box(bm, (0.24, -0.15, 0.48), (0.08, 0.01, 0.06), mi=4)                        # 圍裙上的紅色標誌
    rig.part(f"{name}_Body", "body", body,
             [toon(shell, hard=True), toon(shade(shell, 1.25)), toon(rim), toon(apron), toon(scarf)],
             outline_color=edge)

    def head(bm):
        cylinder(bm, (0.12, 0, 0.62), (0.24, 0, 0.8), 0.08, mi=0, seg=12)             # 脖子
        ellipsoid(bm, (0.3, 0, 0.84), (0.15, 0.13, 0.12), mi=0)
        ellipsoid(bm, (0.24, 0, 0.9), (0.17, 0.15, 0.1), mi=1, cut_below=-0.2)       # 頭巾
        cylinder(bm, (0.1, 0, 0.92), (0.0, 0, 0.88), 0.04, 0.02, mi=1, seg=10)       # 頭巾結
        ellipsoid(bm, (0.3, 0, 1.0), (0.07, 0.07, 0.05), mi=2)                        # 白髮髻
    rig.part(f"{name}_Head", "head", head, [toon(skin), toon(scarf), toon("#EFEAE0")], outline_color=edge)

    def face(bm):
        eye(bm, (0.38, -0.11, 0.87), (0.018, 0.012, 0.022), 0, 1)
        for dx in (0.0,):   # 圓框眼鏡
            cylinder(bm, (0.38 + dx, -0.125, 0.87), (0.38 + dx, -0.135, 0.87), 0.04, mi=2, seg=16, smooth=False)
        cylinder(bm, (0.42, -0.13, 0.875), (0.45, -0.1, 0.875), 0.006, mi=0, seg=4)
        box(bm, (0.42, -0.1, 0.8), (0.06, 0.01, 0.01), mi=0)                          # 抿嘴
    rig.part(f"{name}_Face", "head", face, [flat(DARK), flat(WHITE), flat("#C7E8F0")], outline_px=0)

    def mouth(bm):
        ellipsoid(bm, (0.42, -0.105, 0.8), (0.03, 0.01, 0.022), mi=0, seg=10)
    rig.part(f"{name}_Mouth", "mouth", mouth, [flat("#5A1E26")], outline_px=0)

    for s, sfx, k in ((-1, "n", 1.0), (1, "f", 0.85)):
        def arm(bm, s=s):
            cylinder(bm, (0.15, 0.2 * s, 0.68), (0.25, 0.22 * s, 0.48), 0.045, mi=0, seg=12)
            ellipsoid(bm, (0.26, 0.22 * s, 0.45), (0.045, 0.045, 0.045), mi=0)
            if s < 0:   # 拐杖
                cylinder(bm, (0.27, -0.25, 0.5), (0.33, -0.25, -0.0), 0.02, mi=1, seg=8)
                cylinder(bm, (0.27, -0.25, 0.5), (0.2, -0.25, 0.55), 0.02, mi=1, seg=8)
        rig.part(f"{name}_arm_{sfx}", f"arm_{sfx}", arm, [toon(shade(skin, k)), toon("#7A4B2A")],
                 outline_color=edge)

    def idle(i, t, pose):
        w = 2 * math.pi * t
        pose("body", z=0.008 * math.sin(w))
        pose("head", r=-3 * math.sin(w), x=0.01 * math.sin(w))
        pose("arm_f", r=4 * math.sin(w))
        pose("mouth", s=HIDE)

    def talk(i, t, pose):
        w = 2 * math.pi * t
        pose("head", r=-5 * math.sin(2 * w) - 3)
        pose("arm_f", r=[20, 60, 75, 55, 70, 35][i])
        pose("body", r=-2 * math.sin(w))
        pose("mouth", s=1.0 if i % 2 == 0 else HIDE)

    rig.action("Idle", 6, idle)
    rig.action("Talk", 6, talk)
    rig.done()


BUILDERS = {
    "SsRat": build_rat,
    "SsPigeon": build_pigeon,
    "SsRhino": build_rhino,
    "SsBoss": build_boss,
    "SsMentor": build_mentor,
}
