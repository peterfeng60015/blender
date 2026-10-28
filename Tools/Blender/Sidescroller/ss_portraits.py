"""對話框立繪：SsPortraitHero、SsPortraitBoss、SsPortraitMentor、SsPortraitRat（4×4 單位，pivot 中央）。

半身像正面朝 -Y（鏡頭）建模，再整體繞 Z 轉 25°，變成略朝畫面右邊的 3/4 側臉。
Face 動作 4 格＝4 種表情（design.md §8.5）：0 平常、1 得意、2 生氣、3 驚訝；
頭、身體每格都顯示，眉、眼、嘴每種表情一組，只在對應的那一格顯示。
"""

import math

from ss_lib import (box, cylinder, ellipsoid, empty, flat, frame_box, frames_holder, mesh, poly, rot_x, rot_y,
                    shade, show_only_on, toon)

FACES = ("normal", "happy", "angry", "surprised")
DARK, WHITE = "#1A1414", "#FFFFFF"
TURN = 25.0
OUTLINE = 4.0


def portrait(name, body_parts, face):
    """body_parts：[(label, build, mats, outline_color)]；face：表情參數（見 expression）。"""
    root = empty(name)
    frame_box(root, 4.0)
    frames_holder(root, len(FACES), "Face")
    bust = empty(f"{name}_Bust", root)
    bust.rotation_euler = (0, 0, math.radians(TURN))
    for label, build, mats, edge in body_parts:
        mesh(f"{name}_{label}", build, mats, parent=bust, outline_px=OUTLINE, outline_color=edge)
    for k, kind in enumerate(FACES):
        def build(bm, kind=kind):
            expression(bm, kind, face)
        obj = mesh(f"{name}_Face{k}", build,
                   [flat(DARK), flat(WHITE), flat("#5A1E26"), flat("#E07A8A"), toon(face["lid"])], parent=bust)
        show_only_on([obj], k + 1, len(FACES))


def expression(bm, kind, f):
    """f：eyes＝[(x, y, z)]、eye＝(rx, rz)、brow_dz、mouth＝(x, y, z)、mouth_w、lid＝眼皮色、cool＝平常半睜眼。"""
    rx, rz = f["eye"]
    for side, (x, y, z) in zip((-1, 1), f["eyes"]):
        if kind == "happy":                                     # 笑瞇眼：倒 V
            for s in (-1, 1):
                box(bm, (x + s * rx * 0.5, y, z + rz * 0.1), (rx * 1.2, 0.03, rz * 0.28), mi=0,
                    rot=rot_y(s * 30))
        elif kind == "surprised":                               # 眼白放大、瞳孔變小
            ellipsoid(bm, (x, y + 0.01, z), (rx * 1.45, 0.03, rz * 1.35), mi=1, seg=16)
            ellipsoid(bm, (x, y - 0.03, z), (rx * 0.45, 0.02, rz * 0.45), mi=0, seg=12)
        else:
            ellipsoid(bm, (x, y, z), (rx, 0.04, rz), mi=0, seg=16)
            ellipsoid(bm, (x + rx * 0.3, y - 0.04, z + rz * 0.35), (rx * 0.32, 0.01, rz * 0.28), mi=1, seg=8)
            if kind == "normal" and f.get("cool"):              # 冷面：上半蓋眼皮
                ellipsoid(bm, (x, y - 0.01, z + rz * 0.55), (rx * 1.25, 0.05, rz * 0.6), mi=4, seg=12)
        # 眉毛：生氣＝內側往下，驚訝＝抬高，得意＝外側略抬
        tilt = {"normal": 0, "happy": -10, "angry": 28, "surprised": -6}[kind]
        lift = {"normal": 0.0, "happy": 0.04, "angry": -0.06, "surprised": 0.14}[kind]
        box(bm, (x, y - 0.02, z + rz + f["brow_dz"] + lift), (rx * 2.4, 0.04, 0.06), mi=0,
            rot=rot_y(tilt * side))
    mx, my, mz = f["mouth"]
    w = f["mouth_w"]
    if kind == "normal":
        box(bm, (mx, my, mz), (w * 0.6, 0.03, 0.035), mi=0)
    elif kind == "happy":                                       # 斜嘴笑
        pts = [(mx - w * 0.5 + w * k / 10, mz - 0.12 * math.sin(math.pi * k / 10) + 0.04 * k / 10) for k in range(11)]
        pts += [(mx + w * 0.5 - w * k / 10, mz + 0.02 + 0.04 * (10 - k) / 10 - 0.03 * math.sin(math.pi * k / 10))
                for k in range(11)]
        poly(bm, pts, y=my, mi=2)
        ellipsoid(bm, (mx + w * 0.1, my - 0.005, mz - 0.07), (w * 0.18, 0.01, 0.035), mi=3, seg=10)
    elif kind == "angry":                                       # 咬牙
        box(bm, (mx, my + 0.01, mz), (w * 0.85, 0.03, 0.17), mi=0)
        box(bm, (mx, my, mz), (w * 0.75, 0.02, 0.11), mi=1)
        for k in (-1, 0, 1):
            box(bm, (mx + k * w * 0.2, my - 0.012, mz), (0.02, 0.01, 0.11), mi=0)
    else:                                                       # 張嘴 O
        ellipsoid(bm, (mx, my, mz - 0.03), (w * 0.22, 0.03, 0.15), mi=2, seg=16)


def shell_ring(bm, center, radii, x, mi, width=0.06):
    k = math.sqrt(max(0.0, 1.0 - ((x - center[0]) / radii[0]) ** 2))
    verts = cylinder(bm, (x - width / 2, center[1], center[2]), (x + width / 2, center[1], center[2]), 1.0, mi=mi,
                     seg=28)
    for v in verts:
        v.co.y = center[1] + (v.co.y - center[1]) * (radii[1] * k + 0.02)
        v.co.z = center[2] + (v.co.z - center[2]) * (radii[2] * k + 0.02)


def build_hero():
    skin, shell, band, cap, bag = "#D8B6A4", "#C9A26B", "#8E6B3E", "#D8342C", "#1F9E9A"
    edge = shade(band, 0.45)
    sc, sr = (0, 0.5, -0.55), (1.1, 0.55, 0.8)

    def back(bm):
        ellipsoid(bm, sc, sr, mi=0)
        for x in (-0.6, -0.2, 0.2, 0.6):
            shell_ring(bm, sc, sr, x, 1)
    def body(bm):
        ellipsoid(bm, (0, 0.05, -0.95), (0.85, 0.6, 0.6), mi=0)
        cylinder(bm, (0, 0, -0.30), (0, 0, 0.40), 0.38, mi=0, seg=16)
        cylinder(bm, (-0.75, -0.42, -0.20), (0.55, -0.55, -1.45), 0.07, mi=1, seg=10)   # 郵差包背帶
    def head(bm):
        ellipsoid(bm, (0, 0, 0.5), (0.62, 0.56, 0.55), mi=0)
        cylinder(bm, (0, -0.4, 0.32), (0, -1.15, 0.22), 0.27, 0.1, mi=0, seg=20)
        ellipsoid(bm, (0, -1.17, 0.22), (0.12, 0.08, 0.1), mi=1, seg=12)
        for s in (-1, 1):
            cylinder(bm, (0.32 * s, 0.05, 0.9), (0.6 * s, 0.18, 1.62), 0.17, 0.01, mi=0, seg=14)
    def cap_part(bm):
        ellipsoid(bm, (0, 0.04, 0.82), (0.62, 0.6, 0.4), mi=0, cut_below=0.0)
        box(bm, (0, -0.62, 0.84), (0.72, 0.6, 0.07), mi=1, rot=rot_x(-6), bevel=0.03)
        ellipsoid(bm, (0, -0.5, 1.0), (0.14, 0.05, 0.1), mi=2, seg=12)
    portrait("SsPortraitHero", [
        ("Back", back, [toon(shell, hard=True), toon(band)], edge),
        ("Body", body, [toon(skin), toon(shade(bag, 0.8))], edge),
        ("Head", head, [toon(skin), flat(DARK)], edge),
        ("Cap", cap_part, [toon(cap), toon(shade(cap, 0.82)), toon("#F2EEDC")], shade(cap, 0.3)),
    ], dict(eyes=[(-0.26, -0.5, 0.62), (0.26, -0.5, 0.62)], eye=(0.07, 0.1), brow_dz=0.07,
            mouth=(0.05, -0.74, 0.08), mouth_w=0.36, lid=skin, cool=True))


def build_boss():
    skin, suit, gold = "#8A7C9C", "#F1EEE6", "#E3B53B"
    edge = shade(skin, 0.32)

    def body(bm):
        ellipsoid(bm, (0, 0.25, -1.0), (1.55, 0.9, 0.85), mi=0)
        ellipsoid(bm, (0, -0.35, -1.15), (0.5, 0.3, 0.32), mi=1)
        cylinder(bm, (0, -0.62, -1.05), (0, -0.7, -1.85), 0.13, 0.06, mi=2, seg=12)
        ellipsoid(bm, (0, -0.62, -1.05), (0.12, 0.1, 0.1), mi=2, seg=12)
    def head(bm):
        ellipsoid(bm, (0, 0.05, 0.70), (0.95, 0.8, 0.72), mi=0)
        ellipsoid(bm, (0, -0.6, 0.20), (0.92, 0.58, 0.55), mi=0)
        for s in (-1, 1):
            ellipsoid(bm, (0.45 * s, -0.35, 1.33), (0.24, 0.2, 0.2), mi=0)
            ellipsoid(bm, (0.72 * s, 0.15, 1.45), (0.16, 0.1, 0.18), mi=0)
            ellipsoid(bm, (0.32 * s, -1.06, 0.55), (0.09, 0.05, 0.06), mi=1, seg=10)
    portrait("SsPortraitBoss", [
        ("Body", body, [toon(suit, hard=True), toon("#E9A6B8"), toon(gold, hard=True)], edge),
        ("Head", head, [toon(skin), flat("#3A2A40")], edge),
    ], dict(eyes=[(-0.45, -0.55, 1.35), (0.45, -0.55, 1.35)], eye=(0.07, 0.09), brow_dz=0.07,
            mouth=(0.0, -1.14, -0.05), mouth_w=0.9, lid=skin))


def build_mentor():
    skin, shell, scarf, apron = "#8DBA74", "#5E7A3A", "#D8342C", "#1F9E9A"
    edge = shade(shell, 0.35)

    def back(bm):
        ellipsoid(bm, (0, 0.55, -1.0), (1.15, 0.5, 0.75), mi=0)
        for x, z in ((-0.6, -0.55), (0.0, -0.4), (0.6, -0.55)):
            ellipsoid(bm, (x, 0.12, z), (0.25, 0.08, 0.2), mi=1, seg=12)
    def body(bm):
        ellipsoid(bm, (0, 0.05, -1.35), (0.85, 0.55, 0.5), mi=0)
        ellipsoid(bm, (0, -0.3, -1.35), (0.55, 0.3, 0.45), mi=1)
        box(bm, (0.2, -0.6, -1.3), (0.16, 0.02, 0.12), mi=2)
        cylinder(bm, (0, 0, -0.7), (0, 0, 0.0), 0.3, mi=0, seg=16)
    def head(bm):
        ellipsoid(bm, (0, 0, 0.35), (0.55, 0.52, 0.58), mi=0)
        ellipsoid(bm, (0, 0.05, 0.62), (0.6, 0.57, 0.45), mi=1, cut_below=-0.1)        # 頭巾
        ellipsoid(bm, (0.5, 0.25, 0.75), (0.18, 0.12, 0.12), mi=1)
        ellipsoid(bm, (0, 0.2, 1.08), (0.22, 0.2, 0.15), mi=2)                       # 白髮髻
        for s in (-1, 1):                                                             # 圓框眼鏡
            cylinder(bm, (0.22 * s, -0.5, 0.36), (0.22 * s, -0.56, 0.36), 0.18, mi=3, seg=24, smooth=False)
            cylinder(bm, (0.22 * s, -0.52, 0.36), (0.22 * s, -0.575, 0.36), 0.15, mi=4, seg=24, smooth=False)
        box(bm, (0, -0.57, 0.38), (0.12, 0.02, 0.03), mi=3)
    portrait("SsPortraitMentor", [
        ("Back", back, [toon(shell, hard=True), toon(shade(shell, 1.25))], edge),
        ("Body", body, [toon(skin), toon(apron), toon(scarf)], edge),
        ("Head", head, [toon(skin), toon(scarf), toon("#EFEAE0"), flat("#3A2A2A"), flat("#C7E8F0")], edge),
    ], dict(eyes=[(-0.22, -0.62, 0.36), (0.22, -0.62, 0.36)], eye=(0.05, 0.07), brow_dz=0.14,
            mouth=(0.0, -0.5, 0.06), mouth_w=0.34, lid=skin))


def build_rat():
    gray, suit, tie = "#8C8F99", "#33507A", "#C8312B"
    edge = shade(suit, 0.35)

    def body(bm):
        ellipsoid(bm, (0, 0.1, -1.00), (1.0, 0.6, 0.45), mi=0)
        ellipsoid(bm, (0, -0.32, -0.80), (0.32, 0.25, 0.3), mi=1)
        cylinder(bm, (0, -0.55, -0.60), (0, -0.6, -1.45), 0.1, 0.05, mi=2, seg=12)
        box(bm, (-0.55, -0.5, -0.85), (0.26, 0.03, 0.14), mi=1)
        cylinder(bm, (0, 0, -0.45), (0, 0, 0.30), 0.25, mi=3, seg=16)
    def head(bm):
        ellipsoid(bm, (0, 0, 0.35), (0.48, 0.45, 0.48), mi=0)
        cylinder(bm, (0, -0.35, 0.22), (0, -1.05, 0.12), 0.25, 0.07, mi=0, seg=20)
        ellipsoid(bm, (0, -1.08, 0.12), (0.09, 0.07, 0.08), mi=1, seg=12)
        ellipsoid(bm, (0, 0.0, 0.75), (0.45, 0.42, 0.14), mi=2, rot=rot_x(-8))    # 油頭
        for s in (-1, 1):
            ellipsoid(bm, (0.6 * s, 0.1, 0.9), (0.38, 0.07, 0.38), mi=0)
            ellipsoid(bm, (0.6 * s, 0.03, 0.9), (0.26, 0.04, 0.26), mi=1)
            for dz in (0.0, -0.07):
                cylinder(bm, (0.12 * s, -0.85, 0.1 + dz), (0.55 * s, -0.95, 0.12 + dz * 2), 0.01, mi=3, seg=4)
    portrait("SsPortraitRat", [
        ("Body", body, [toon(suit, hard=True), toon(WHITE), toon(tie), toon(gray)], edge),
        ("Head", head, [toon(gray), toon("#E39AA8"), toon("#55586A", hard=True), flat(DARK)], edge),
    ], dict(eyes=[(-0.2, -0.43, 0.45), (0.2, -0.43, 0.45)], eye=(0.05, 0.08), brow_dz=0.06,
            mouth=(0.05, -0.64, 0.03), mouth_w=0.3, lid=gray))


BUILDERS = {
    "SsPortraitHero": build_hero,
    "SsPortraitBoss": build_boss,
    "SsPortraitMentor": build_mentor,
    "SsPortraitRat": build_rat,
}
