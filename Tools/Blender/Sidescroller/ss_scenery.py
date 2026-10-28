"""場景與介面：SsTiles、SsProps、SsLandmark、SsBgFar、SsBgMid、SsUi（多張變化：每一幀是一種變化）。

做法同 Glowshaft：<Name>_Frames 的 Action 決定幀數，每種變化的物件用 hide_render 動畫只在自己那一幀顯示。
可平鋪的素材（tile、背景）把會跨過格子邊的東西在左右（或上下）各複製一份，接縫才接得起來。
"""

import math
import random

import bmesh

from ss_lib import (box, cylinder, ellipsoid, empty, flat, frame_box, frames_holder, mesh, poly, quad, rot_y, shade,
                    show_only_on, toon)

GOLD, CORP = "#E3B53B", "#5E4E78"      # 大口集團：金＋紫灰
RED, TEAL = "#D8342C", "#1F9E9A"        # 龜速快遞：紅＋藍綠


def variants(name, cell, makers, action="Idle", scale=1.0):
    """makers[k](parent) 回傳第 k 種變化的物件清單；scale≠1 時模型掛在縮放過的空物件下（描邊另外扣回）。"""
    root = empty(name)
    frame_box(root, cell / 64)
    frames_holder(root, len(makers), action)
    parent = root if scale == 1.0 else empty(f"{name}_Model", root, scale=scale)
    for k, make in enumerate(makers):
        show_only_on(make(parent), k + 1, len(makers))
    return root


def part(parent, label, build, mats, outline_px=0.0, outline_color=None):
    name = parent.name[:-6] if parent.name.endswith("_Model") else parent.name
    return mesh(f"{name}_{label}", build, mats, parent=parent, outline_px=outline_px, outline_color=outline_color,
                world_scale=parent.scale.x)


def mouth_logo(bm, cx, cz, r, y, mi_gold, mi_dark):
    """大口集團標誌：金色圓牌上一張張開的大嘴（從 -Y 看）。"""
    pts = [(cx + r * math.cos(a), cz + r * math.sin(a)) for a in (k * math.pi / 12 for k in range(24))]
    poly(bm, pts, y=y, mi=mi_gold, depth=0.02)
    mouth = [(cx + 0.7 * r * math.cos(a), cz - 0.05 * r + 0.42 * r * math.sin(a))
             for a in (math.pi + k * math.pi / 10 for k in range(11))]
    mouth += [(cx + 0.7 * r * math.cos(a), cz - 0.05 * r + 0.12 * r * math.sin(a))
              for a in (k * math.pi / 10 for k in range(11))]
    poly(bm, mouth, y=y - 0.01, mi=mi_dark)


def turtle_logo(bm, cx, cz, r, y, mi_shell, mi_head, mi_ring):
    """龜速快遞標誌：藍綠圓環裡一隻小烏龜。"""
    ring = [(cx + r * math.cos(a), cz + r * math.sin(a)) for a in (k * math.pi / 12 for k in range(24))]
    poly(bm, ring, y=y, mi=mi_ring, depth=0.02)
    shell = [(cx + 0.55 * r * math.cos(a), cz - 0.1 * r + 0.5 * r * math.sin(a)) for a in (k * math.pi / 10 for k in range(11))]
    poly(bm, shell, y=y - 0.01, mi=mi_shell)
    head = [(cx + 0.55 * r + 0.18 * r * math.cos(a), cz + 0.05 * r + 0.15 * r * math.sin(a))
            for a in (k * math.pi / 6 for k in range(12))]
    poly(bm, head, y=y - 0.01, mi=mi_head)


# ---------- SsTiles：1×1 單位，側視，pivot 中央 ----------

def build_tiles():
    concrete, curb, joint = "#7A6E64", "#B8B2A8", "#4A403A"

    def ground_top(root):
        def b(bm):
            box(bm, (0, 0.1, -0.11), (1.6, 0.05, 0.78), mi=0)                        # 底下的混凝土
            box(bm, (0, 0.12, 0.39), (1.6, 0.05, 0.24), mi=2)                        # 路緣磚縫（底色）
            for x in (-1.0, -0.5, 0.0, 0.5, 1.0):
                box(bm, (x - 0.25 + 0.0, 0.0, 0.39), (0.47, 0.1, 0.22), mi=1, bevel=0.025)
            box(bm, (0, -0.06, 0.27), (1.6, 0.02, 0.025), mi=2)                     # 路緣下的陰影線
        return [part(root, "Tile0", b, [toon(concrete), toon(curb, hard=True), toon(joint)])]

    def stones(bm, mi, rng):
        for _ in range(5):
            x, z = rng.uniform(-0.34, 0.34), rng.uniform(-0.34, 0.34)
            r = rng.uniform(0.035, 0.07)
            ellipsoid(bm, (x, -0.03, z), (r * 1.3, 0.02, r), mi=mi, seg=10)

    def ground_inner(root):
        rng = random.Random(7)

        def b(bm):
            box(bm, (0, 0.1, 0), (1.6, 0.05, 1.6), mi=0)
            stones(bm, 1, rng)
        return [part(root, "Tile1", b, [toon(concrete), toon(shade(concrete, 0.82))])]

    def platform(root):
        wood = "#B07A45"

        def b(bm):
            for x in (-1.0, 0.0, 1.0):
                box(bm, (x, 0, 0.4), (0.97, 0.12, 0.16), mi=0, bevel=0.02)
                box(bm, (x, -0.065, 0.36), (0.9, 0.01, 0.015), mi=1)
                box(bm, (x, -0.065, 0.43), (0.7, 0.01, 0.012), mi=1)
            for x in (-0.3, 0.3):
                ellipsoid(bm, (x, -0.07, 0.4), (0.025, 0.01, 0.025), mi=2, seg=8)
            box(bm, (0, 0.06, 0.24), (1.6, 0.04, 0.05), mi=2)                       # 下方的鋼管
        return [part(root, "Tile2", b, [toon(wood), toon(shade(wood, 0.7)), toon("#8A8F99", hard=True)],
                     outline_px=2, outline_color=shade(wood, 0.3))]

    def crate(root):
        wood = "#C98F4E"

        def b(bm):
            box(bm, (0, 0, 0), (0.92, 0.5, 0.92), mi=0, bevel=0.03)
            for z in (-0.4, 0.4):
                box(bm, (0, -0.26, z), (0.92, 0.03, 0.12), mi=1)
            for x in (-0.4, 0.4):
                box(bm, (x, -0.27, 0), (0.12, 0.03, 0.92), mi=1)
            box(bm, (0, -0.28, 0), (0.1, 0.02, 1.0), mi=1, rot=rot_y(45))
            box(bm, (0.12, -0.3, -0.18), (0.22, 0.01, 0.12), mi=2)                  # 紅色易碎章
        return [part(root, "Tile3", b, [toon(wood), toon(shade(wood, 0.78)), toon(RED)], outline_px=2.5,
                     outline_color=shade(wood, 0.3))]

    def beam(root):
        steel = "#6A6078"

        def b(bm):
            box(bm, (0, 0.05, 0), (1.6, 0.05, 0.62), mi=0)                          # 腹板
            for z in (-0.38, 0.38):
                box(bm, (0, 0, z), (1.6, 0.16, 0.14), mi=1, bevel=0.02)             # 上下翼板
            for x in (-0.25, 0.25):
                ellipsoid(bm, (x, 0.0, 0), (0.15, 0.02, 0.15), mi=2, seg=16)       # 減重孔
            for x in (-0.375, -0.125, 0.125, 0.375):
                for z in (-0.38, 0.38):
                    ellipsoid(bm, (x, -0.085, z), (0.022, 0.01, 0.022), mi=3, seg=8)
        return [part(root, "Tile4", b, [toon(steel), toon(steel, hard=True), flat("#2A2433"), toon(GOLD, hard=True)],
                     outline_px=2, outline_color="#231C2C")]

    def pipes(root):
        def b(bm):
            box(bm, (0, 0.2, 0), (1.6, 0.05, 1.6), mi=0)
            cylinder(bm, (-0.8, 0, 0.18), (0.8, 0, 0.18), 0.24, mi=1, seg=24)
            cylinder(bm, (-0.8, -0.05, -0.28), (0.8, -0.05, -0.28), 0.14, mi=2, seg=20)
            cylinder(bm, (-0.05, 0, 0.18), (0.05, 0, 0.18), 0.27, mi=3, seg=24)    # 管箍
            cylinder(bm, (-0.04, -0.05, -0.28), (0.04, -0.05, -0.28), 0.165, mi=3, seg=20)
        return [part(root, "Tile5", b, [toon("#3E3A44"), toon("#5E8C8A", hard=True), toon("#A66A3A", hard=True),
                                        toon("#8A8F99", hard=True)], outline_px=2, outline_color="#1E2228")]

    def gate(root):
        def b(bm):
            box(bm, (0, 0.12, 0), (1.6, 0.05, 1.6), mi=0)
            for x in (-0.375, -0.125, 0.125, 0.375):
                cylinder(bm, (x, 0, -0.8), (x, 0, 0.8), 0.055, mi=1, seg=12)
            box(bm, (0, 0.02, 0), (1.6, 0.04, 0.06), mi=2)
        return [part(root, "Tile6", b, [toon("#2A2238"), toon(GOLD, hard=True), toon(CORP)], outline_px=2,
                     outline_color="#1A1424")]

    variants("SsTiles", 64, [ground_top, ground_inner, platform, crate, beam, pipes, gate])


# ---------- SsProps：4×4 單位，腳底 pivot ----------

def build_props():
    def lamp(root):
        pole = "#3E5A5A"

        def b(bm):
            cylinder(bm, (0, 0, 0), (0, 0, 0.35), 0.12, 0.09, mi=0, seg=12)
            cylinder(bm, (0, 0, 0.35), (0, 0, 3.2), 0.06, 0.045, mi=0, seg=12)
            cylinder(bm, (0, 0, 3.2), (0.25, 0, 3.4), 0.04, mi=0, seg=10)
            cylinder(bm, (0.25, 0, 3.4), (0.7, 0, 3.4), 0.04, mi=0, seg=10)
            ellipsoid(bm, (0.72, 0, 3.32), (0.22, 0.16, 0.1), mi=0, cut_below=0.0)
            ellipsoid(bm, (0.72, 0, 3.3), (0.17, 0.12, 0.05), mi=1)
        return [part(root, "Lamp", b, [toon(pole, hard=True), flat("#FFE7A0")], outline_px=3,
                     outline_color="#16262A")]

    def palm(root):
        def b(bm):
            pts = [(0, 0), (0.05, 0.6), (0.15, 1.2), (0.28, 1.8), (0.42, 2.4), (0.55, 3.0)]
            for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
                cylinder(bm, (x0, 0, z0), (x1, 0, z1), 0.13, 0.11, mi=0, seg=12)
                cylinder(bm, (x1 - 0.01, 0, z1 - 0.06), (x1, 0, z1), 0.135, mi=1, seg=12)
            for k, ang in enumerate((-160, -120, -60, -20, 20, 70, 140)):
                a = math.radians(ang)
                length = 1.1 if k % 2 else 0.95
                c = (0.55 + 0.55 * length * math.cos(a), -0.05 + 0.02 * k, 3.05 + 0.35 * math.sin(a) - 0.15)
                ellipsoid(bm, c, (0.55 * length, 0.08, 0.1), mi=2, rot=rot_y(-ang * 0.35 if abs(ang) < 90 else
                                                                        (180 - ang) * 0.35))
            for dx in (-0.08, 0.08):
                ellipsoid(bm, (0.55 + dx, -0.1, 2.95), (0.08, 0.08, 0.08), mi=3)
        return [part(root, "Palm", b, [toon("#9A7048"), toon("#7A5634"), toon("#3E9A4A"), toon("#6B4A2B")],
                     outline_px=3, outline_color="#1E3A1E")]

    def vending(root):
        body = "#2E4A7D"

        def b(bm):
            box(bm, (0, 0, 0.95), (0.95, 0.7, 1.9), mi=0, bevel=0.05)
            box(bm, (-0.1, -0.36, 1.2), (0.6, 0.02, 1.0), mi=1)                     # 發光櫥窗
            for z in (0.85, 1.15, 1.45):
                box(bm, (-0.1, -0.375, z), (0.58, 0.01, 0.025), mi=2)
                for x in (-0.3, -0.1, 0.1):
                    box(bm, (x, -0.38, z + 0.08), (0.1, 0.01, 0.13), mi=3 if x != -0.1 else 4)
            box(bm, (0.32, -0.36, 1.2), (0.16, 0.02, 0.5), mi=2)
            box(bm, (-0.05, -0.36, 0.35), (0.6, 0.02, 0.18), mi=2)                  # 取物口
            box(bm, (0, -0.36, 1.82), (0.85, 0.02, 0.14), mi=4)
        return [part(root, "Vending", b, [toon(body, hard=True), flat("#BFF7F2"), flat("#1A2238"), flat(RED),
                                          flat("#2FE6D8")], outline_px=3, outline_color="#121C30")]

    def mailbox(root):
        def b(bm):
            box(bm, (0, 0, 0.18), (0.16, 0.16, 0.36), mi=1)
            box(bm, (0, 0, 0.7), (0.5, 0.42, 0.66), mi=0, bevel=0.04)
            ellipsoid(bm, (0, 0, 1.03), (0.25, 0.21, 0.14), mi=0, cut_below=0.0)
            box(bm, (0, -0.215, 0.85), (0.3, 0.02, 0.05), mi=2)
            box(bm, (0, -0.215, 0.6), (0.2, 0.02, 0.14), mi=3)
        return [part(root, "Mailbox", b, [toon(RED, hard=True), toon("#3A3A44"), flat("#2A0A0A"), toon("#F2EEDC")],
                     outline_px=3, outline_color="#4A0E0A")]

    def cone(root):
        def b(bm):
            box(bm, (0, 0, 0.03), (0.5, 0.5, 0.06), mi=0, bevel=0.015)
            cylinder(bm, (0, 0, 0.06), (0, 0, 0.66), 0.2, 0.035, mi=0, seg=20)
            cylinder(bm, (0, 0, 0.3), (0, 0, 0.4), 0.145, 0.12, mi=1, seg=20)
            cylinder(bm, (0, 0, 0.5), (0, 0, 0.56), 0.09, 0.075, mi=1, seg=20)
        return [part(root, "Cone", b, [toon("#FF7A1A"), toon("#F2F2F2")], outline_px=3, outline_color="#5A2408")]

    def fence(root):
        def b(bm):
            box(bm, (0, 0, 0.82), (1.9, 0.06, 1.3), mi=0)
            for x in (-0.9, 0.9):
                box(bm, (x, 0, 0.08), (0.18, 0.4, 0.16), mi=1, bevel=0.03)
                cylinder(bm, (x, 0, 0.16), (x, 0, 1.5), 0.04, mi=2, seg=10)
            for z in (0.24, 1.42):
                box(bm, (0, -0.04, z), (1.9, 0.03, 0.08), mi=3)
            mouth_logo(bm, 0, 0.82, 0.38, -0.05, 3, 4)
        return [part(root, "Fence", b, [toon(CORP), toon("#3A3A44"), toon("#8A8F99", hard=True),
                                        toon(GOLD, hard=True), flat("#2A1A30")], outline_px=3,
                     outline_color="#1E1828")]

    variants("SsProps", 256, [lamp, palm, vending, mailbox, cone, fence])


# ---------- SsLandmark：8×8 單位，腳底 pivot ----------

def build_landmark():
    def shop(root):
        wall = "#E9D8B4"

        def b(bm):
            box(bm, (0, 0.3, 2.4), (5.6, 0.6, 4.8), mi=0)                           # 牆
            box(bm, (0, 0.0, 4.95), (5.9, 0.7, 0.3), mi=1, bevel=0.04)             # 屋簷
            box(bm, (0, -0.02, 3.95), (4.2, 0.1, 0.9), mi=2, bevel=0.05)           # 招牌
            turtle_logo(bm, -1.4, 3.95, 0.36, -0.09, 4, 5, 3)
            for k in range(7):                                                      # 條紋遮陽棚
                x = -2.55 + k * 0.85
                poly(bm, [(x - 0.425, 3.35), (x + 0.425, 3.35), (x + 0.425, 2.95), (x, 2.8),
                          (x - 0.425, 2.95)], y=-0.45, mi=2 if k % 2 == 0 else 6, depth=0.05)
            box(bm, (-1.3, -0.02, 1.4), (2.2, 0.06, 1.9), mi=7)                     # 櫥窗
            box(bm, (-1.3, -0.06, 1.4), (0.08, 0.04, 1.9), mi=8)
            box(bm, (-1.3, -0.06, 0.48), (2.3, 0.08, 0.1), mi=8)
            box(bm, (1.4, -0.02, 1.15), (1.1, 0.06, 2.3), mi=9, bevel=0.03)        # 門
            ellipsoid(bm, (1.05, -0.08, 1.15), (0.05, 0.03, 0.05), mi=10, seg=8)
            for x in (-1.8, -0.8):                                                  # 櫥窗裡的包裹
                box(bm, (x, -0.08, 0.75), (0.5, 0.05, 0.4), mi=11, bevel=0.02)
            box(bm, (0, -0.1, 0.06), (5.8, 0.25, 0.12), mi=8)
        return [part(root, "Shop", b, [toon(wall), toon("#8E5A3A"), toon(RED), toon(TEAL), toon("#8DBA74"),
                                       toon("#8DBA74"), toon("#F2EEDC"), flat("#FFE2A0"), toon("#5A3A26"),
                                       toon(TEAL), toon(GOLD, hard=True), toon("#C99A5E")],
                     outline_px=4, outline_color="#3A2418")]

    def billboard(root):
        def b(bm):
            for x in (-1.2, 1.2):
                cylinder(bm, (x, 0.2, 0), (x, 0.2, 3.3), 0.14, mi=0, seg=12)
            box(bm, (0, 0.1, 4.7), (5.4, 0.4, 3.0), mi=1, bevel=0.08)              # 外框
            box(bm, (0, -0.12, 4.7), (5.0, 0.05, 2.6), mi=2)                        # 螢幕
            mouth_logo(bm, -1.2, 4.75, 0.95, -0.16, 3, 4)
            for k, z in enumerate((5.4, 4.8, 4.2)):                                # 螢幕上的字條（廣告）
                box(bm, (1.05, -0.16, z), ([2.2, 1.6, 1.9][k], 0.02, 0.28), mi=5 if k else 3)
            for x in (-2.2, -1.1, 0.0, 1.1, 2.2):                                   # 頂上的燈
                ellipsoid(bm, (x, -0.1, 6.28), (0.12, 0.1, 0.1), mi=6, seg=10)
            box(bm, (0, 0.1, 3.25), (3.0, 0.3, 0.12), mi=0)
        return [part(root, "Billboard", b, [toon("#3A3A44", hard=True), toon(CORP, hard=True), flat("#E0457B"),
                                            toon(GOLD, hard=True), flat("#2A1A30"), flat("#FFE45C"),
                                            flat("#FFF6C8")], outline_px=4, outline_color="#1E1828")]

    def gate(root):
        def b(bm):
            for x in (-2.7, 2.7):
                box(bm, (x, 0, 2.6), (0.7, 0.7, 5.2), mi=0, bevel=0.05)
                for z in (0.6, 1.4, 2.2, 3.0, 3.8):                                # 警示斜紋
                    box(bm, (x, -0.36, z), (0.7, 0.02, 0.18), mi=1, rot=rot_y(30))
            box(bm, (0, 0, 5.6), (6.4, 0.8, 1.0), mi=2, bevel=0.06)                # 橫樑招牌
            mouth_logo(bm, 0, 5.6, 0.42, -0.42, 1, 3)
            for x in (-1.9, -1.2, 1.2, 1.9):
                box(bm, (x, -0.42, 5.6), (0.5, 0.02, 0.22), mi=1)
            for x in (-3.0, 3.0):
                ellipsoid(bm, (x, -0.2, 6.25), (0.16, 0.14, 0.14), mi=4, seg=10)    # 警示燈
        return [part(root, "Gate", b, [toon("#4A3E5E", hard=True), toon(GOLD, hard=True), toon(CORP, hard=True),
                                       flat("#2A1A30"), flat("#FF5A3A")], outline_px=4, outline_color="#1A1424")]

    variants("SsLandmark", 512, [shop, billboard, gate])


# ---------- 背景：16×16 單位，左右可平鋪 ----------

def tiled(xs, width=16.0):
    """跨格子邊的東西在左右各複製一份。"""
    for x in xs:
        yield x
        if x < -width / 2 + 3:
            yield x + width
        if x > width / 2 - 3:
            yield x - width


def build_bg_far():
    bands = [(8.5, "#3B2A63"), (5.0, "#5B3A7A"), (2.6, "#8A3F7E"), (0.8, "#E0457B"), (-0.6, "#FF6F5A"),
             (-1.8, "#FF8C42"), (-2.8, "#FFB45E")]

    def sky(root):
        def b(bm):
            for k, (top, _) in enumerate(bands):
                bottom = bands[k + 1][0] if k + 1 < len(bands) else -9.0
                quad(bm, [(-9, 0.5, bottom), (9, 0.5, bottom), (9, 0.5, top), (-9, 0.5, top)], mi=k)
            sun = [(3.0 + 1.7 * math.cos(a), -1.9 + 1.7 * math.sin(a)) for a in (k * math.pi / 24 for k in range(48))]
            poly(bm, sun, y=0.4, mi=len(bands))
            for k, z in enumerate((-1.2, -1.75, -2.25)):                             # 太陽上的橫條（90 年代夕陽）
                quad(bm, [(1.0, 0.35, z), (5.0, 0.35, z), (5.0, 0.35, z + 0.14 - 0.03 * k), (1.0, 0.35, z + 0.14 - 0.03 * k)],
                     mi=len(bands) - 1)
        mats = [flat(c) for _, c in bands] + [flat("#FFD27A")]
        objs = [part(root, "Sky", b, mats)]

        rng = random.Random(11)
        for layer, (color, ymin, ymax, y, seed_w) in enumerate((("#6A3A78", -3.2, -0.6, 0.2, (0.5, 1.4)),
                                                                ("#3E2758", -4.6, -1.8, 0.1, (0.6, 1.8)))):
            xs, x = [], -8.0
            while x < 8.0:
                w = rng.uniform(*seed_w)
                xs.append((x + w / 2, w, rng.uniform(ymin, ymax), rng.random() < 0.3))
                x += w + rng.uniform(0.0, 0.25)

            def city(bm, xs=xs, y=y, layer=layer):
                for idx, (cx, w, top, spire) in enumerate(xs):
                    for px in tiled([cx]):
                        rng = random.Random(1000 * layer + idx)   # 每份複製用同一組亂數，接縫兩側才一樣
                        quad(bm, [(px - w / 2, y, -9), (px + w / 2, y, -9), (px + w / 2, y, top), (px - w / 2, y, top)], mi=0)
                        if spire:
                            poly(bm, [(px - 0.06, top), (px + 0.06, top), (px, top + 0.7)], y=y, mi=0)
                        for wz in range(int((top + 7.8) / 0.55)):
                            for wx in range(max(1, int(w / 0.35))):
                                if rng.random() < 0.22:
                                    zz = -7.8 + wz * 0.55
                                    xx = px - w / 2 + 0.17 + wx * 0.35
                                    if xx + 0.08 < px + w / 2 and zz + 0.16 < top - 0.1:
                                        quad(bm, [(xx, y - 0.01, zz), (xx + 0.08, y - 0.01, zz),
                                                  (xx + 0.08, y - 0.01, zz + 0.16), (xx, y - 0.01, zz + 0.16)], mi=1)
            objs.append(part(root, f"City{layer}", city, [flat(color), flat("#FFB45E" if layer else "#FFC98A")]))
        return objs

    variants("SsBgFar", 1024, [sky])


def cyclic_spans(rng, wmin, wmax, gmin, gmax, start=-7.0, width=16.0, keep_off=0.15):
    """在一整個循環（start～start+width）裡排一列房子，回傳 (中心, 寬)；邊緣不落在格子邊 ±8 附近，
    接縫檢查比的是格子兩側最外一欄，房子的邊剛好落在那裡會被當成接縫。"""
    def near_seam(v):
        return min(abs(v - width / 2), abs(v + width / 2)) < keep_off

    end = start + width
    spans, x = [], start
    while x < end - wmin - gmax:
        if near_seam(x):
            x += 2 * keep_off
        w = rng.uniform(wmin, wmax)
        if near_seam(x + w):
            w += 2 * keep_off
        spans.append((x + w / 2, w))
        x += w + rng.uniform(gmin, gmax)
    # 收尾：最後一棟拉長或縮短到剛好留一般的間距接上下一循環的第一棟（不重疊、不留大縫）
    c, w = spans[-1]
    left = c - w / 2
    w = max(wmin * 0.5, end - rng.uniform(gmin, gmax) - left)
    spans[-1] = (left + w / 2, w)
    return spans


def build_bg_mid():
    def street(root):
        rng = random.Random(23)
        blds = [(c, w, rng.uniform(-2.5, 2.5)) for c, w in cyclic_spans(rng, 1.8, 3.2, 0.1, 0.5)]

        def b(bm):
            for idx, (cx, w, top) in enumerate(blds):
                for px in tiled([cx]):
                    rng = random.Random(500 + idx)   # 每份複製用同一組亂數，接縫兩側才一樣
                    box(bm, (px, 0.2, (top - 8) / 2), (w, 0.4, top + 8), mi=0)
                    box(bm, (px, 0.0, top + 0.06), (w + 0.1, 0.1, 0.12), mi=1)
                    for wz in range(int((top + 7.5) / 0.8)):
                        for wx in range(max(1, int((w - 0.3) / 0.5))):
                            xx = px - w / 2 + 0.35 + wx * 0.5
                            zz = -7.6 + wz * 0.8
                            if zz + 0.4 < top - 0.2:
                                box(bm, (xx, -0.01, zz + 0.2), (0.24, 0.02, 0.34), mi=2 if rng.random() < 0.35 else 3)
                    if rng.random() < 0.7:                                          # 霓虹招牌
                        sz = top - rng.uniform(1.0, 2.5)
                        box(bm, (px + rng.uniform(-0.3, 0.3), -0.05, sz), (rng.uniform(0.8, 1.4), 0.04, 0.42),
                            mi=4 if rng.random() < 0.5 else 5)
                    if rng.random() < 0.5:                                          # 屋頂水塔
                        cylinder(bm, (px + 0.4, 0.2, top), (px + 0.4, 0.2, top + 0.6), 0.28, mi=1, seg=12)
        return [part(root, "Street", b, [flat("#2E4A7D"), flat("#22385F"), flat("#FFD27A"), flat("#1A2A48"),
                                         flat("#2FE6D8"), flat("#E0457B")])]

    def site(root):
        def b(bm):
            for cx, w, top in ((-5.0, 4.5, -0.5), (1.0, 5.0, 1.5)):                # 鷹架
                for px in tiled([cx]):
                    for k in range(int(w / 0.9) + 1):
                        xx = px - w / 2 + k * 0.9
                        box(bm, (xx, 0, (top - 8) / 2), (0.08, 0.08, top + 8), mi=0)
                    for zz in [-8 + k * 1.1 for k in range(int((top + 8) / 1.1) + 1)]:
                        box(bm, (px, 0, zz), (w, 0.08, 0.08), mi=0)
                        box(bm, (px, 0.1, zz - 0.35), (w, 0.04, 0.7), mi=1)
            for px in tiled([6.0]):                                                 # 吊車
                for k in range(14):
                    z = -8 + k * 0.75
                    box(bm, (px - 0.3, -0.05, z + 0.375), (0.08, 0.06, 0.75), mi=2)
                    box(bm, (px + 0.3, -0.05, z + 0.375), (0.08, 0.06, 0.75), mi=2)
                    box(bm, (px, -0.05, z + 0.375), (0.85, 0.05, 0.06), mi=2, rot=rot_y(40 if k % 2 else -40))
                box(bm, (px - 1.0, -0.05, 2.6), (6.5, 0.1, 0.22), mi=2)
                box(bm, (px + 1.9, -0.05, 2.3), (0.06, 0.04, 0.6), mi=3)
                box(bm, (px + 1.9, -0.05, 1.9), (0.4, 0.2, 0.3), mi=2)
                box(bm, (px - 3.6, -0.05, 2.35), (0.8, 0.3, 0.5), mi=2)
            for px in tiled([-2.0]):                                                # 集團布條
                box(bm, (px, -0.1, -1.6), (3.0, 0.03, 1.1), mi=4)
                mouth_logo(bm, px - 0.9, -1.6, 0.4, -0.13, 5, 6)
        return [part(root, "Site", b, [flat("#6A5A3A"), flat("#3A2E50"), flat("#C79A2E"), flat("#1A1424"),
                                       flat(CORP), flat(GOLD), flat("#2A1A30")])]

    variants("SsBgMid", 1024, [street, site])


# ---------- SsUi：1×1 單位，pivot 中央 ----------

def heart_pts(cx, cz, r):
    pts = []
    for k in range(40):
        t = k / 40 * 2 * math.pi
        x = 16 * math.sin(t) ** 3
        z = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((cx + x / 17 * r, cz + z / 17 * r))
    return pts


def build_ui():
    def heart(full):
        def make(root):
            # 尖角上 Solidify 描邊會撐出尖刺，改成後面墊一顆放大的深色愛心
            def b(bm):
                poly(bm, heart_pts(0, 0.02, 0.4), y=0, mi=0, depth=0.1)
                poly(bm, heart_pts(0, 0.0, 0.46), y=0.12, mi=2)
                if full:
                    ellipsoid(bm, (-0.15, -0.02, 0.14), (0.08, 0.01, 0.05), mi=1, rot=rot_y(30), seg=10)
            color = "#E8344A" if full else "#4A3E52"
            return [part(root, "Heart" + ("Full" if full else "Empty"), b,
                         [toon(color), flat("#FFD0D6"), flat("#2A0A12" if full else "#16101C")])]
        return make

    def bag(root):
        def b(bm):
            box(bm, (0, 0, -0.06), (0.62, 0.2, 0.5), mi=0, bevel=0.06)
            box(bm, (0, -0.105, 0.05), (0.62, 0.02, 0.28), mi=1, bevel=0.01)
            box(bm, (0, -0.12, -0.02), (0.1, 0.01, 0.1), mi=2)
            cylinder(bm, (-0.24, 0, 0.18), (-0.1, 0, 0.38), 0.03, mi=1, seg=8)
            cylinder(bm, (-0.1, 0, 0.38), (0.1, 0, 0.38), 0.03, mi=1, seg=8)
            cylinder(bm, (0.1, 0, 0.38), (0.24, 0, 0.18), 0.03, mi=1, seg=8)
        return [part(root, "Bag", b, [toon(TEAL), toon("#17807C"), toon(GOLD, hard=True)], outline_px=3,
                     outline_color="#0A3A38")]

    def sling(root):
        def b(bm):
            cylinder(bm, (0.0, 0, -0.42), (0.0, 0, -0.02), 0.06, mi=0, seg=10)
            cylinder(bm, (0.0, 0, -0.02), (-0.2, 0, 0.36), 0.05, mi=0, seg=10)
            cylinder(bm, (0.0, 0, -0.02), (0.2, 0, 0.36), 0.05, mi=0, seg=10)
            cylinder(bm, (-0.2, -0.04, 0.33), (0.0, -0.04, 0.12), 0.015, mi=1, seg=6)
            cylinder(bm, (0.2, -0.04, 0.33), (0.0, -0.04, 0.12), 0.015, mi=1, seg=6)
            box(bm, (0, -0.06, 0.12), (0.16, 0.06, 0.12), mi=2, bevel=0.01)
        return [part(root, "Sling", b, [toon("#9A6438"), flat("#3A2A2A"), toon("#C99A5E")], outline_px=3,
                     outline_color="#2A1A10")]

    def ball(root):
        def b(bm):
            ellipsoid(bm, (0, 0, 0), (0.38, 0.38, 0.38), mi=0)
            for x in (-0.18, 0.0, 0.18):
                cylinder(bm, (x - 0.02, 0, 0), (x + 0.02, 0, 0), 0.37 * math.sqrt(1 - (x / 0.38) ** 2) + 0.012,
                         mi=1, seg=28)
            box(bm, (0.3, -0.2, -0.28), (0.3, 0.02, 0.05), mi=2)                   # 速度線
            box(bm, (-0.42, -0.2, 0.1), (0.14, 0.02, 0.04), mi=2)
        return [part(root, "Ball", b, [toon("#C9A26B", hard=True), toon("#8E6B3E"), flat("#FFFFFF")], outline_px=3,
                     outline_color="#3A2A18")]

    def cap(color, stripe=None, goggles=None):
        def make(root):
            def b(bm):
                ellipsoid(bm, (-0.05, 0, -0.02), (0.36, 0.34, 0.3), mi=0, cut_below=0.0)
                box(bm, (0.3, 0, 0.0), (0.32, 0.5, 0.06), mi=1, bevel=0.02)
                ellipsoid(bm, (-0.05, 0, 0.29), (0.06, 0.06, 0.04), mi=1)
                if stripe:
                    ellipsoid(bm, (-0.05, 0, -0.02), (0.365, 0.345, 0.305), mi=2, cut_below=0.35)
                    box(bm, (-0.05, 0, 0.17), (0.66, 0.2, 0.08), mi=2)
                if goggles:
                    cylinder(bm, (0.08, -0.16, 0.17), (0.22, -0.18, 0.2), 0.11, mi=3, seg=14)
                    cylinder(bm, (0.08, 0.16, 0.17), (0.22, 0.18, 0.2), 0.11, mi=3, seg=14)
                bmesh.ops.translate(bm, vec=(0, 0, -0.14), verts=bm.verts)   # 帽子重心偏上，往下移到格子中央
            return [part(root, "Cap" + ("Express" if goggles else "Day"), b,
                         [toon(color), toon(shade(color, 0.82)), toon(stripe or "#000000"),
                          toon(goggles or "#000000", hard=True)], outline_px=3, outline_color=shade(color, 0.3))]
        return make

    def boss(root):
        skin = "#8A7C9C"

        def b(bm):
            ellipsoid(bm, (0, 0, 0.05), (0.36, 0.3, 0.3), mi=0)
            ellipsoid(bm, (0.0, -0.08, -0.14), (0.34, 0.28, 0.2), mi=0)
            for x in (-0.18, 0.18):
                ellipsoid(bm, (x, 0, 0.32), (0.08, 0.06, 0.08), mi=0)
                ellipsoid(bm, (x * 0.7, -0.24, 0.16), (0.07, 0.03, 0.08), mi=1, seg=10)
                ellipsoid(bm, (x * 0.7, -0.27, 0.15), (0.03, 0.02, 0.04), mi=2, seg=8)
                box(bm, (x * 0.7, -0.27, 0.26), (0.14, 0.01, 0.03), mi=2, rot=rot_y(-20 if x < 0 else 20))
                ellipsoid(bm, (x * 0.5, -0.33, -0.08), (0.04, 0.02, 0.025), mi=2, seg=8)
            box(bm, (0, -0.31, -0.24), (0.36, 0.02, 0.05), mi=3)                   # 嘴縫
        return [part(root, "BossHead", b, [toon(skin), flat("#FFFFFF"), flat("#1A1414"), flat("#3A2A40")],
                     outline_px=3, outline_color="#2A2238")]

    variants("SsUi", 64, [heart(True), heart(False), bag, sling, ball, cap(RED), cap("#1B1B22", "#FF7A1A", "#F2C230"),
                          boss], scale=0.8)


BUILDERS = {
    "SsTiles": build_tiles,
    "SsProps": build_props,
    "SsLandmark": build_landmark,
    "SsBgFar": build_bg_far,
    "SsBgMid": build_bg_mid,
    "SsUi": build_ui,
}
