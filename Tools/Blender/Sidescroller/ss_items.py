"""子彈與特效：SsParcel、SsCoin、SsBomb、SsShock、SsFx（pivot 都在格子中央）。"""

import math

from ss_lib import HIDE, Rig, box, cylinder, ellipsoid, empty, flat, frame_box, poly, rot_y, shade, toon


def setup(name, cell, bones):
    root = empty(name)
    frame_box(root, cell / 64)
    return Rig(f"{name}_Rig", root, bones)


def burst(bm, c, r_out, r_in, points, mi, y=0.0, rot=0.0):
    pts = []
    for k in range(points * 2):
        a = math.radians(rot) + k * math.pi / points
        rr = r_out if k % 2 == 0 else r_in
        pts.append((c[0] + rr * math.cos(a), c[1] + rr * math.sin(a)))
    poly(bm, pts, y=y, mi=mi, depth=0.02)


def build_parcel():
    """彈弓射出的急件小包裹：朝 +X 飛，原地翻滾。"""
    rig = setup("SsParcel", 32, {"box": ((0, 0, 0), None)})
    kraft = "#C99A5E"

    def parcel(bm):
        box(bm, (0, 0, 0), (0.28, 0.2, 0.2), mi=0, bevel=0.02)
        box(bm, (0, 0, 0), (0.29, 0.21, 0.04), mi=1)
        box(bm, (0, 0, 0), (0.04, 0.21, 0.21), mi=1)
        box(bm, (0, -0.105, 0), (0.08, 0.01, 0.08), mi=2)
    rig.part("SsParcel_Box", "box", parcel, [toon(kraft), toon("#8E6B3E"), toon("#D8342C")], outline_px=2,
             outline_color=shade(kraft, 0.3))
    rig.action("Spin", 4, lambda i, t, pose: pose("box", r=-45 * i))
    rig.done()


def build_coin():
    """河董撒的金幣：繞垂直軸自轉（水平縮放）。"""
    rig = setup("SsCoin", 32, {"coin": ((0, 0, 0), None)})
    gold = "#E3B53B"

    def coin(bm):
        cylinder(bm, (0, 0.03, 0), (0, -0.03, 0), 0.15, mi=0, seg=24)
        cylinder(bm, (0, -0.031, 0), (0, -0.034, 0), 0.1, mi=1, seg=24, smooth=False)
        box(bm, (0, -0.036, 0), (0.1, 0.004, 0.03), mi=2)          # 大口集團的嘴形標誌
    rig.part("SsCoin_Coin", "coin", coin, [toon(gold, hard=True), toon(shade(gold, 1.12)), flat("#8A5A10")],
             outline_px=2, outline_color="#6A4A10")
    rig.action("Spin", 4, lambda i, t, pose: pose("coin", s=([1.0, 0.65, 0.14, 0.65][i], 1.0)))
    rig.done()


def build_bomb():
    """監工鴿的公文炸彈：一疊公文綁紅帶，引信火花閃兩格。"""
    rig = setup("SsBomb", 32, {"bundle": ((0, 0, 0), None), "spark": ((0.08, 0, 0.13), "bundle")})

    def bundle(bm):
        box(bm, (0, 0, -0.03), (0.26, 0.16, 0.17), mi=0, bevel=0.012)
        box(bm, (0, 0, -0.03), (0.27, 0.17, 0.04), mi=1)
        box(bm, (0, 0, -0.03), (0.04, 0.17, 0.18), mi=1)
        cylinder(bm, (0.06, 0, 0.055), (0.08, 0, 0.12), 0.01, mi=2, seg=6)
    rig.part("SsBomb_Bundle", "bundle", bundle, [toon("#F2EEDC"), toon("#C8312B"), flat("#3A2A1A")], outline_px=2,
             outline_color="#3A2A1A")

    def spark(bm):
        burst(bm, (0.08, 0.13), 0.06, 0.025, 5, 0, y=-0.1)
    rig.part("SsBomb_Spark", "spark", spark, [flat("#FFD34A")], outline_px=1.5, outline_color="#C8312B")
    rig.action("Fall", 2, lambda i, t, pose: (pose("bundle", r=[-6, 6][i]), pose("spark", s=[1.0, 0.6][i], r=20 * i)))
    rig.done()


def build_shock():
    """河董重踩的震波：朝 +X 前進的弧形能量＋揚塵，高約 0.8。"""
    rig = setup("SsShock", 64, {"wave": ((0, 0, 0), None), "dust": ((-0.2, 0, -0.3), None)})

    def wave(bm):
        pts = []
        for k in range(13):     # 外弧
            a = math.radians(-90 + k * 15)
            pts.append((0.05 + 0.28 * math.cos(a), -0.02 + 0.34 * math.sin(a)))
        for k in range(12, -1, -1):  # 內弧
            a = math.radians(-90 + k * 15)
            pts.append((-0.06 + 0.22 * math.cos(a), -0.02 + 0.27 * math.sin(a)))
        poly(bm, pts, y=0, mi=0, depth=0.04)
        pts = []
        for k in range(13):
            a = math.radians(-80 + k * 13.3)
            pts.append((0.0 + 0.21 * math.cos(a), -0.02 + 0.25 * math.sin(a)))
        for k in range(12, -1, -1):
            a = math.radians(-80 + k * 13.3)
            pts.append((-0.08 + 0.17 * math.cos(a), -0.02 + 0.2 * math.sin(a)))
        poly(bm, pts, y=-0.03, mi=1, depth=0.01)
    rig.part("SsShock_Wave", "wave", wave, [flat("#FFD34A"), flat("#FFF6C8")], outline_px=2.5,
             outline_color="#C8541E")

    def dust(bm):
        for x, z, r in ((-0.22, -0.3, 0.08), (-0.1, -0.28, 0.1), (-0.3, -0.26, 0.055), (0.05, -0.31, 0.06)):
            ellipsoid(bm, (x, 0.05, z), (r, r, r * 0.85), mi=0, seg=12)
    rig.part("SsShock_Dust", "dust", dust, [toon("#D9C7A8")], outline_px=2, outline_color="#6A5A44")

    def move(i, t, pose):
        pose("wave", s=(1.0 + 0.05 * math.sin(i * math.pi / 2), [1.0, 1.05, 0.96, 1.02][i]))
        pose("dust", x=-0.02 * i, s=[0.85, 0.95, 1.0, 0.9][i])
    rig.action("Move", 4, move)
    rig.done()


def build_fx():
    """命中、冒煙、落地揚塵、盾牌擋下：四組特效各掛一根骨頭，動作裡只顯示自己那組。"""
    bones = {k: ((0, 0, 0), None) for k in ("hit", "poof", "dust", "clank")}
    rig = setup("SsFx", 128, bones)

    def hit(bm):
        burst(bm, (0, 0), 0.62, 0.24, 8, 0, y=0.0)
        burst(bm, (0, 0), 0.36, 0.14, 8, 1, y=-0.05, rot=22.5)
    rig.part("SsFx_Hit", "hit", hit, [flat("#FFD34A"), flat("#FFFFFF")], outline_px=3, outline_color="#C8541E")

    def poof(bm):
        for x, z, r in ((0, 0.05, 0.3), (-0.3, -0.05, 0.22), (0.3, -0.04, 0.24), (-0.12, 0.3, 0.2), (0.16, 0.28, 0.18),
                        (0.0, -0.25, 0.2)):
            ellipsoid(bm, (x, 0, z), (r, r, r), mi=0, seg=16)
    rig.part("SsFx_Poof", "poof", poof, [toon("#E8E2EE")], outline_px=3, outline_color="#5A5070")

    def dust(bm):
        for s in (-1, 1):
            for x, z, r in ((0.2, -0.32, 0.11), (0.38, -0.36, 0.08), (0.08, -0.36, 0.08)):
                ellipsoid(bm, (x * s, 0, z), (r, r, r * 0.8), mi=0, seg=12)
    rig.part("SsFx_Dust", "dust", dust, [toon("#D9C7A8")], outline_px=2, outline_color="#6A5A44")

    def clank(bm):
        for k in range(7):
            a = math.radians(-60 + k * 20)
            c, s = math.cos(a), math.sin(a)
            box(bm, (0.32 * c, 0, 0.32 * s), (0.3, 0.02, 0.04), mi=0, rot=rot_y(-math.degrees(a)))
        burst(bm, (0.05, 0), 0.22, 0.08, 4, 1, y=-0.05, rot=45)
    rig.part("SsFx_Clank", "clank", clank, [flat("#FFE45C"), flat("#FFFFFF")], outline_px=2, outline_color="#7A6A20")

    def only(pose, keep, **kw):
        for k in bones:
            if k != keep:
                pose(k, s=HIDE)
        pose(keep, **kw)

    rig.action("Hit", 4, lambda i, t, pose: only(pose, "hit", s=[0.5, 1.0, 0.85, 0.5][i], r=12 * i))
    rig.action("Poof", 6, lambda i, t, pose: only(pose, "poof", s=[0.35, 0.7, 1.0, 1.1, 0.8, 0.45][i],
                                                  z=0.04 * i, r=-6 * i))
    rig.action("Dust", 4, lambda i, t, pose: only(pose, "dust", s=([0.5, 0.8, 1.0, 1.15][i], [0.7, 0.9, 0.8, 0.5][i])))
    rig.action("Clank", 3, lambda i, t, pose: only(pose, "clank", s=[0.6, 1.0, 0.75][i], r=-8 * i))
    rig.done()


BUILDERS = {
    "SsParcel": build_parcel,
    "SsCoin": build_coin,
    "SsBomb": build_bomb,
    "SsShock": build_shock,
    "SsFx": build_fx,
}
