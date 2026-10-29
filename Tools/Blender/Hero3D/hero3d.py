"""主角阿甲（犰狳快遞員）的即時 3D 模型試作：程式化建模、卡通著色預覽。

用法（在專案根目錄）：
  blender -b --python Tools/Blender/Hero3D/hero3d.py -- --out <輸出資料夾>
輸出：
  AhJia3D.blend            模型＋骨架（面朝 -Y、Z 朝上、腳底在原點，單位公尺），全部配件合併成一個蒙皮網格
  AhJia3D_turnaround.png   正面／3/4／側面／背面（靜止姿勢）
  AhJia3D_hero.png         3/4 特寫
  AhJia3D_wire.png         線框
  AhJia3D_pose.png         測試姿勢（檢查骨架與權重）
  AhJia3D_pose_hero.png    測試姿勢的 3/4 特寫
  AhJia3D_actions.png      動作總覽（Idle、Run、Jump、Melee、Shoot、Hurt，各取 6 格）
  AhJia3D_faces.png        表情總覽（無表情＋每個 shape key 一格；上排 3/4、下排側面）
  AhJia3D.fbx              給 Unity：骨架＋網格（含表情 blend shape）＋全部動作（表情跟著各 clip）
  stats.txt                各部位三角形數
骨架照 Unity Humanoid 的命名（Hips、Spine、Chest、Neck、Head、LeftUpperLeg…），
另有 Snout、LeftEar/RightEar、Tail1～3、Bag 這些非人形的骨頭。
描邊用倒殼（Solidify 翻面＋背面剔除），只是預覽用的修改器，不算進面數；進遊戲時改由 shader 處理。
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

#region 參數

# 角色設定（2026-10 重新設計）：犰狳快遞員阿甲，約 2.9 頭身，大頭長嘴＋圓背殼＋大鞋，側面剪影一眼認得。
# - 頭比舊版大約 12%，臉有顴骨、嘴側凹、收細的嘴尖與下顎線；眼睛加大，眼睛上緣到帽簷留約 3 公分額頭放眉毛。
# - 快遞帽往後推、前緣上翹（六片帽縫、帽頂鈕扣、厚帽簷）；芥末黃頸巾把大頭和身體分開；深藍制服短褲和土黃背殼拉開對比。
# - 背殼：肩甲＋5 條活動帶＋臀甲，帶厚邊倒角與甲片凸起；手 3 指＋拇指；鞋有鞋底厚度、鞋舌、鞋帶；郵差包有滾邊與厚蓋口。
# - 骨架：沿用 31 根骨頭的名稱與階層（見「骨架與蒙皮」的 BONES），只有 Snout、LeftEar／RightEar 的位置配合新頭形調整。

COLORS = {
    "shell": "#C9A26B", "band": "#8E6B3E", "skin": "#D8B6A4", "belly": "#EBD0C2", "cap": "#D8342C",
    "cap_logo": "#F4EBDD", "bag": "#1F9E9A", "bag_flap": "#17807C", "strap": "#14615E", "buckle": "#E8B830",
    "shorts": "#34406B", "shoe": "#5A3A26", "sole": "#3A2418", "nose": "#6B3E3E", "eye_white": "#FAF6EE",
    "pupil": "#1A1414", "ear_inner": "#E39A96", "outline": "#3B2414", "lace": "#F4EBDD", "scarf": "#EDB437",
    "brow": "#4A2E1C", "mouth": "#3A1418", "tongue": "#C2606A",
}

SHELL_C = (0.0, 0.11, 0.50)   # 背殼橢球中心（x 左右、y 前後、z 高）
SHELL_R = (0.25, 0.31, 0.33)
SHELL_ZCUT = 0.37             # 殼的下緣（側面看是圓頂＋下緣，後甲往後微微下垂）

# 眼睛：+X 那顆的眼白中心與半徑（-X 鏡射）；表情的眼皮、眉毛也讀這兩個值
EYE_C = (0.081, -0.219, 0.886)
EYE_R = 0.036
EYE_DIR = (0.62, -0.78, 0.05)    # +X 眼睛的正前方（瞳孔方向）
# 帽子在眼睛正上方（眉毛區）最低處的 z；build_cap 依它擺帽子高度。眉毛最高不超過 CAP_BRIM_Z − 0.005
CAP_BRIM_Z = 0.958

# 網格密度（合計約 18k 三角形）
D = dict(shell_nv=44, shell_rows=(4, 3, 9), fill=(16, 10), eye=(12, 7, 8, 5), nose=(12, 8),
         ear=(8, 7), cap=(40, 10, 14), hand=(12, 8, 8), shoe=(16, 9, 20), bag_seg=3, pipe=4, strap=(4, 1))

#endregion

#region 共用


def srgb(hex_str):
    h = hex_str.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)


def toon_material(name, hex_str):
    """明暗只分 3 階（Shader to RGB → Constant Color Ramp），乘上固有色後用 Emission 輸出。"""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    diffuse = nt.nodes.new("ShaderNodeBsdfDiffuse")
    diffuse.inputs["Color"].default_value = (1, 1, 1, 1)
    to_rgb = nt.nodes.new("ShaderNodeShaderToRGB")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    els = ramp.color_ramp.elements
    els[0].position, els[0].color = 0.0, (0.50, 0.47, 0.55, 1)
    els[1].position, els[1].color = 0.12, (0.78, 0.76, 0.80, 1)
    top = els.new(0.42)
    top.color = (1, 1, 1, 1)
    mul = nt.nodes.new("ShaderNodeVectorMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = srgb(hex_str)[:3]
    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(diffuse.outputs[0], to_rgb.inputs[0])
    nt.links.new(to_rgb.outputs["Color"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], mul.inputs[0])
    nt.links.new(mul.outputs[0], emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    mat.diffuse_color = srgb(hex_str)
    return mat


def flat_material(name, hex_str, cull=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = srgb(hex_str)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    mat.use_backface_culling = cull
    mat.diffuse_color = srgb(hex_str)
    return mat


MATS = {}


def mat(key):
    if key not in MATS:
        if key == "outline":
            MATS[key] = flat_material("M_outline", COLORS[key], cull=True)
        elif key in ("pupil", "eye_white"):
            MATS[key] = flat_material("M_" + key, COLORS[key])
        else:
            MATS[key] = toon_material("M_" + key, COLORS[key])
    return MATS[key]


ROOT = None
COLL = None


def make_object(name, bm, mats, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(mat(m))
    if smooth:
        me.shade_smooth()
    obj = bpy.data.objects.new(name, me)
    COLL.objects.link(obj)
    obj.parent = ROOT
    return obj


def add_ellipsoid(bm, center, radii, seg=16, rings=10, mi=0, rot=None):
    """在 bm 裡加一顆橢球（UV 球）；rot 是 3x3 Matrix（以中心旋轉）。回傳新頂點。"""
    res = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    verts = res["verts"]
    m = Matrix.Diagonal(Vector(radii)).to_4x4()
    if rot is not None:
        m = rot.to_4x4() @ m
    m = Matrix.Translation(Vector(center)) @ m
    bmesh.ops.transform(bm, matrix=m, verts=verts)
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mi
    return verts


def rot_xyz(rx=0, ry=0, rz=0):
    return (Matrix.Rotation(math.radians(rz), 3, "Z") @ Matrix.Rotation(math.radians(ry), 3, "Y")
            @ Matrix.Rotation(math.radians(rx), 3, "X"))


def bake_modifiers(obj):
    """把修改器結果寫回網格（不用 bpy.ops，背景模式也穩定）。"""
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    old = obj.data
    obj.modifiers.clear()
    obj.data = me
    bpy.data.meshes.remove(old)


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)

#endregion

#region 造型工具


def tube(bm, pts, radii, seg=8, caps=(True, True), flat=1.0, up=(0, 0, 1), closed=False, mi=0):
    """沿折線長一條管子（手指、鞋帶、滾邊、耳朵）。截面在 up 方向是 radii，另一軸乘 flat（壓扁）；
    caps 兩端各加半球收口；closed 時首尾相接成環（不加收口）。回傳新頂點。"""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    tans = []
    for i in range(n):
        if closed:
            t = pts[(i + 1) % n] - pts[i - 1]
        else:
            t = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        tans.append(t.normalized())
    ref = Vector(up)
    if abs(ref.normalized().dot(tans[0])) > 0.95:
        ref = Vector((1, 0, 0)) if abs(tans[0].x) < 0.9 else Vector((0, 1, 0))
    nrm = (ref - tans[0] * ref.dot(tans[0])).normalized()
    angles = [2 * math.pi * j / seg for j in range(seg)]

    def ring(c, t, nr, r):
        b = t.cross(nr)
        return [bm.verts.new(c + nr * (r * math.cos(a)) + b * (r * flat * math.sin(a))) for a in angles]

    frames = []
    for i in range(n):
        nrm = (nrm - tans[i] * nrm.dot(tans[i])).normalized()   # 平行移動：截面不扭轉
        frames.append(nrm.copy())
    rings = [ring(pts[i], tans[i], frames[i], radii[i]) for i in range(n)]
    poles = [None, None]
    if not closed:
        for end, (i, sgn) in enumerate(((0, -1), (n - 1, 1))):
            if not caps[end]:
                continue
            r = radii[i]
            extra = [ring(pts[i] + tans[i] * (sgn * r * math.sin(math.radians(a))), tans[i], frames[i],
                          r * math.cos(math.radians(a))) for a in (40, 72)]
            poles[end] = bm.verts.new(pts[i] + tans[i] * (sgn * r))
            rings = (extra[::-1] + rings) if end == 0 else (rings + extra)
    faces = []
    pairs = list(zip(rings[:-1], rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for r0, r1 in pairs:
        for j in range(seg):
            faces.append(bm.faces.new((r0[j], r0[(j + 1) % seg], r1[(j + 1) % seg], r1[j])))
    for end, pole in enumerate(poles):
        if pole is None:
            continue
        rg = rings[0] if end == 0 else rings[-1]
        for j in range(seg):
            faces.append(bm.faces.new((rg[(j + 1) % seg], rg[j], pole) if end == 0 else (rg[j], rg[(j + 1) % seg], pole)))
    for f in faces:
        f.material_index = mi
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return [v for r in rings for v in r] + [p for p in poles if p is not None]


def gauss(co, c, s):
    return math.exp(-(co - Vector(c)).length_squared / (2 * s * s))


def mirror_x(c, sx):
    return Vector((c[0] * sx, c[1], c[2]))

#endregion

#region 身體（Skin 修改器＋細分：四肢、尾巴、頭和長嘴是一整塊連續的網格）


def body_skeleton():
    """Skin 修改器的節點與連線。關節（頸、肩、肘、膝）前後各多一個節點，細分後關節處多幾圈，彎曲時不會塌。"""
    nodes = {
        "pelvis": ((0, 0.02, 0.36), 0.155),
        "belly": ((0, -0.01, 0.50), 0.175),
        "chest": ((0, -0.04, 0.63), 0.145),
        "neck": ((0, -0.09, 0.735), 0.085),
        "head": ((0, -0.135, 0.85), 0.132),
        "snout": ((0, -0.285, 0.812), 0.062),
        "snout_tip": ((0, -0.395, 0.778), 0.036),
        "tail1": ((0, 0.17, 0.31), 0.065),
        "tail2": ((0, 0.31, 0.21), 0.042),
        "tail3": ((0, 0.41, 0.15), 0.018),
    }
    for side, sx in (("L", 1), ("R", -1)):
        nodes.update({
            "hip" + side: ((0.085 * sx, 0.02, 0.29), 0.072),
            "knee" + side: ((0.10 * sx, -0.01, 0.17), 0.056),
            "ankle" + side: ((0.10 * sx, 0.0, 0.075), 0.046),
            "shoulder" + side: ((0.135 * sx, -0.04, 0.635), 0.052),
            "elbow" + side: ((0.215 * sx, -0.06, 0.505), 0.041),
            "wrist" + side: ((0.25 * sx, -0.09, 0.40), 0.036),
        })
    edges = [("pelvis", "belly"), ("belly", "chest"), ("chest", "neck"), ("neck", "head"), ("head", "snout"),
             ("snout", "snout_tip"), ("pelvis", "tail1"), ("tail1", "tail2"), ("tail2", "tail3")]
    for s in "LR":
        edges += [("pelvis", "hip" + s), ("hip" + s, "knee" + s), ("knee" + s, "ankle" + s),
                  ("chest", "shoulder" + s), ("shoulder" + s, "elbow" + s), ("elbow" + s, "wrist" + s)]

    def split(a, b, name, dist, r_mul=1.0):
        """在 a→b 連線上、離 a 距離 dist 處插一個節點（半徑內插）。"""
        pa, ra = Vector(nodes[a][0]), nodes[a][1]
        pb, rb = Vector(nodes[b][0]), nodes[b][1]
        t = dist / (pb - pa).length
        nodes[name] = (tuple(pa.lerp(pb, t)), (ra + (rb - ra) * t) * r_mul)
        i = edges.index((a, b))
        edges[i:i + 1] = [(a, name), (name, b)]

    split("chest", "neck", "neck_lo", 0.055)
    split("neck", "head", "neck_hi", 0.04)
    for s in "LR":
        split("hip" + s, "knee" + s, "knee_u" + s, 0.085)
        split("knee" + s, "ankle" + s, "knee_d" + s, 0.035, 1.03)    # 小腿肚
        split("shoulder" + s, "elbow" + s, "shoulder_o" + s, 0.045)
        split("shoulder_o" + s, "elbow" + s, "elbow_u" + s, 0.075)
        split("elbow" + s, "wrist" + s, "elbow_d" + s, 0.032, 1.04)  # 前臂
    return nodes, edges


def sculpt_head(bm):
    """細分後在頭與長嘴上雕出顴骨、嘴側凹、收細的嘴尖、下顎線與眉骨（沿法線推拉）。"""
    bm.normal_update()
    moves = []
    for v in bm.verts:
        co = v.co
        if co.z < 0.70 or co.y > 0.02:
            continue
        d = 0.0
        for sx in (1, -1):
            d += 0.011 * gauss(co, mirror_x((0.094, -0.174, 0.801), sx), 0.027)    # 顴骨
            d -= 0.011 * gauss(co, mirror_x((0.058, -0.283, 0.806), sx), 0.032)    # 嘴側凹
            d += 0.007 * gauss(co, mirror_x((0.076, -0.157, 0.774), sx), 0.024)    # 下顎角
            d += 0.004 * gauss(co, mirror_x((0.065, -0.191, 0.951), sx), 0.020)    # 眉骨
            d -= 0.009 * gauss(co, mirror_x(EYE_C, sx), 0.029)                     # 眼窩（眼睛嵌進去）
        d += 0.005 * gauss(co, (0.0, -0.258, 0.868), 0.038)                         # 鼻樑
        p = co + v.normal * d
        # 嘴尖收細：左右變窄、截面變成直立的橢圓
        tip = smoothstep(-0.30, -0.40, co.y)
        p.x *= 1.0 - 0.20 * tip
        # 下顎線：頭到嘴的下緣壓成斜平面，側面看下巴有一條清楚的線
        if co.y < -0.105 and abs(co.x) < 0.11:
            zj = 0.735 + (co.y + 0.135) * -0.05
            if p.z < zj:
                w = smoothstep(-0.105, -0.155, co.y) * (1.0 - smoothstep(0.065, 0.11, abs(co.x)))
                p.z += (zj - p.z) * 0.65 * w
        moves.append((v, p))
    for v, p in moves:
        v.co = p


def build_body():
    nodes, edges = body_skeleton()
    names = list(nodes)
    index = {n: i for i, n in enumerate(names)}
    me = bpy.data.meshes.new("Body")
    me.from_pydata([nodes[n][0] for n in names], [(index[a], index[b]) for a, b in edges], [])
    obj = bpy.data.objects.new("Body", me)
    COLL.objects.link(obj)
    obj.parent = ROOT
    skin = obj.modifiers.new("Skin", "SKIN")
    skin.branch_smoothing = 0.6
    for i, n in enumerate(names):
        sv = me.skin_vertices[0].data[i]
        sv.radius = (nodes[n][1], nodes[n][1])
        sv.use_root = n == "pelvis"
    bake_modifiers(obj)

    # 頭與長嘴先多切一次，細分後密度是身體的 4 倍（表情的嘴角、眼周 shape key 才拉得動）；
    # 交界落在頸部上段，細分後全是四邊形
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    head = [f for f in bm.faces if f.calc_center_median().z > 0.765 and f.calc_center_median().y < 0.0]
    bmesh.ops.subdivide_edges(bm, edges=list({e for f in head for e in f.edges}), cuts=1, use_grid_fill=True)
    bm.to_mesh(obj.data)
    bm.free()
    sub = obj.modifiers.new("Subsurf", "SUBSURF")
    sub.levels = sub.render_levels = 2
    bake_modifiers(obj)

    # 雕頭形；短褲上下緣切出整齊的邊
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    sculpt_head(bm)
    for z in (0.215, 0.425):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z), plane_no=(0, 0, 1))
    bm.to_mesh(obj.data)
    bm.free()

    for m in ("skin", "belly", "shorts", "shell"):
        obj.data.materials.append(mat(m))
    for p in obj.data.polygons:
        c = p.center
        if c.y > 0.15 and c.z < 0.36:
            p.material_index = 3                       # 尾巴：殼色
        elif 0.215 < c.z < 0.425 and c.y < 0.16 and abs(c.x) < 0.175:
            p.material_index = 2                       # 短褲
        elif c.y < -0.07 and 0.42 < c.z < 0.72 and abs(c.x) < 0.13:
            p.material_index = 1                       # 肚子
    obj.data.shade_smooth()
    return obj

#endregion

#region 背殼


SHELL_T = 0.016          # 殼的厚度（只在看得到的邊緣做出倒角厚邊，殼的內面由 ShellFill 擋住）
PLATE_H = 0.007          # 甲片凸起高度


def shell_zcut(y, kind):
    """殼的下緣高度：後甲往後下垂蓋住屁股，前後甲下緣每片甲一個小圓弧（波浪邊）。"""
    z = SHELL_ZCUT
    if kind == "back":
        z -= 0.035 * smoothstep(0.24, 0.36, y)
    if kind != "band":
        z += 0.007 * abs(math.sin(math.pi * (y - 0.01) / 0.05))
    return z


def plate_height(kind, s, y, v):
    """甲片的凸起量：殼帶是一排長方形甲片（左右之間有溝）；前後甲是蜂巢狀的多邊形甲片。"""
    if kind == "band":
        q = v / 0.085
        dist = abs(q - round(q))
        return PLATE_H * smoothstep(0.0, 0.22, dist) * smoothstep(0.0, 0.3, s)
    cell = 0.056
    row = y / (cell * 0.866)
    ds = []
    for r in (math.floor(row), math.floor(row) + 1):
        off = 0.5 * cell if r % 2 else 0.0
        for c in (math.floor((v - off) / cell), math.floor((v - off) / cell) + 1):
            ds.append(math.hypot(y - r * cell * 0.866, v - (c * cell + off)))
    ds.sort()
    return PLATE_H * smoothstep(0.0, 0.016, ds[1] - ds[0])


def shell_strip(bm, y0, y1, scale, bulge, nu, nv, mi, kind, rims):
    """背殼橢球在 y0～y1 之間的一條帶子；中間微微鼓起，邊緣貼回橢球，相鄰帶子交疊處才看得出分節。
    表面加甲片凸起；rims 列出要做厚邊的邊（"front" "back" "low"），厚邊往殼內折、外緣倒成圓角。"""
    cx, cy, cz = SHELL_C
    rx, ry, rz = SHELL_R
    grid, norms = [], []
    for i in range(nu + 1):
        s = i / nu
        y = y0 + (y1 - y0) * s
        k = math.sqrt(max(0.0004, 1.0 - ((y - cy) / ry) ** 2))
        m = scale * (1.0 + bulge * math.sin(math.pi * s))
        c = (shell_zcut(y, kind) - cz) / (rz * k * m)
        phimax = math.acos(max(-0.985, min(0.95, c)))
        row, nrow = [], []
        for j in range(nv + 1):
            phi = -phimax + 2 * phimax * j / nv
            n = Vector((math.sin(phi) / rx, 0, math.cos(phi) / rz)).normalized()
            h = plate_height(kind, s, y, phi * 0.29 * k)
            row.append(bm.verts.new(Vector((cx + rx * k * m * math.sin(phi), y, cz + rz * k * m * math.cos(phi)))
                                    + n * h))
            nrow.append(n)
        grid.append(row)
        norms.append(nrow)
    faces = []
    for i in range(nu):
        for j in range(nv):
            faces.append(bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j])))

    # 厚邊：沿邊界每個頂點往外推一點再往殼內折，兩排面做出圓角
    paths = []
    if "low" in rims:
        paths.append([(i, 0, (i, 1)) for i in range(nu + 1)][::-1])
        paths.append([(i, nv, (i, nv - 1)) for i in range(nu + 1)])
    if "back" in rims:
        paths.append([(nu, j, (nu - 1, j)) for j in range(nv + 1)])
    if "front" in rims:
        paths.append([(0, j, (1, j)) for j in range(nv + 1)][::-1])
    for path in paths:
        lip = []
        for i, j, (ii, jj) in path:
            v, n = grid[i][j], norms[i][j]
            e = v.co - grid[ii][jj].co
            e = (e - n * e.dot(n)).normalized()
            lip.append((v, bm.verts.new(v.co + e * 0.0035 - n * 0.006), bm.verts.new(v.co + e * 0.002 - n * SHELL_T), e))
        for a, b in zip(lip[:-1], lip[1:]):
            for f in (bm.faces.new((a[0], b[0], b[1], a[1])), bm.faces.new((a[1], b[1], b[2], a[2]))):
                f.normal_update()
                if f.normal.dot(a[3]) < 0:
                    f.normal_flip()
                faces.append(f)
    for f in faces:
        f.material_index = mi


def build_shell():
    bm = bmesh.new()
    cy, ry = SHELL_C[1], SHELL_R[1]
    front, back = cy - ry * 0.80, cy + ry * 0.995
    # 肩甲（前）、5 條活動殼帶、臀甲（後）；殼帶往後一條比一條小一點點，交疊時前一條蓋住後一條
    cuts = [front, -0.05, 0.005, 0.06, 0.115, 0.17, 0.225, back]
    rows_front, rows_band, rows_back = D["shell_rows"]
    for k in range(len(cuts) - 1):
        y0 = cuts[k] - (0.012 if k else 0.0)
        y1 = cuts[k + 1] + 0.012
        is_band = 1 <= k <= 5
        scale = 1.035 - 0.004 * k if is_band else 1.0
        kind = "band" if is_band else ("front" if k == 0 else "back")
        nu = rows_band if is_band else (rows_front if k == 0 else rows_back)
        # 看得到的邊才做厚邊：下緣全部、最前一條的前緣、殼帶的後緣（蓋在下一條上）
        rims = {"low"} | ({"front"} if k <= 1 else set()) | ({"back"} if is_band else set())
        shell_strip(bm, y0, y1, scale, 0.035 if is_band else 0.02, nu, D["shell_nv"], 1 if is_band else 0,
                    kind, rims)
    obj = make_object("Shell", bm, ["shell", "band"])

    # 殼底下的填充（從殼的開口往裡看不會是空的）
    bm = bmesh.new()
    add_ellipsoid(bm, (0, 0.12, 0.47), (0.215, 0.28, 0.25), seg=D["fill"][0], rings=D["fill"][1])
    make_object("ShellFill", bm, ["belly"])
    return obj

#endregion

#region 頭部配件


def build_head_parts():
    objs = []
    # 眼睛：眼白＋瞳孔（眼皮在「臉部表情」的 Eyelids）
    bm = bmesh.new()
    for sx in (1, -1):
        c = mirror_x(EYE_C, sx)
        out = Vector((EYE_DIR[0] * sx, EYE_DIR[1], EYE_DIR[2])).normalized()
        e = D["eye"]
        add_ellipsoid(bm, c, (EYE_R, EYE_R, EYE_R * 1.09), seg=e[0], rings=e[1], mi=0)
        add_ellipsoid(bm, c + out * (EYE_R * 0.73), (EYE_R * 0.48, EYE_R * 0.48, EYE_R * 0.58), seg=e[2], rings=e[3], mi=1)
    objs.append(make_object("Eyes", bm, ["eye_white", "pupil"]))

    # 鼻子
    bm = bmesh.new()
    add_ellipsoid(bm, (0, -0.428, 0.779), (0.034, 0.025, 0.027), seg=D["nose"][0], rings=D["nose"][1])
    objs.append(make_object("Nose", bm, ["nose"]))

    # 大尖耳：葉形、有厚度，外皮＋往前凸一點的內耳
    bm = bmesh.new()
    seg, n = D["ear"]
    for sx in (1, -1):
        m = Matrix.Translation((0.074 * sx, -0.068, 1.012)) @ rot_xyz(rx=-18, ry=22 * sx, rz=-14 * sx).to_4x4()
        for inner in (False, True):
            ts = [i / (n - 1) for i in range(n)]
            w = 0.037 if inner else 0.056
            pts = [(0, -0.017 if inner else 0.0, -0.12 + 0.24 * t * (0.74 if inner else 1.0) + (0.01 if inner else 0))
                   for t in ts]
            radii = [max(0.003, w * (1 - t) ** 0.8 * (1 + 0.7 * t)) for t in ts]
            vs = tube(bm, pts, radii, seg=seg, caps=(False, True), flat=0.2 if inner else 0.38, up=(1, 0, 0),
                      mi=1 if inner else 0)
            bmesh.ops.transform(bm, matrix=m, verts=vs)
    objs.append(make_object("Ears", bm, ["skin", "ear_inner"]))
    objs.append(build_cap())
    objs.append(build_scarf())
    return objs


def build_cap():
    """快遞帽：往後推、前緣上翹；六片布的帽頂（帽縫是凹溝）＋帽頂鈕扣＋有厚度的帽簷＋前面的圓標。
    擺放高度由 CAP_BRIM_Z 決定：眼睛正上方（眉毛區）帽子最低處剛好在 CAP_BRIM_Z。"""
    from mathutils.bvhtree import BVHTree
    bm = bmesh.new()
    nseg, nring, nvis = D["cap"]
    rx, ry, rz = 0.138, 0.152, 0.095

    def dome_pt(th, ph, d=0.0):
        """th＝繞一圈的角度（0＝正前方），ph＝從頂往下的角度；d 沿徑向外推。"""
        return Vector(((rx + d) * math.sin(ph) * math.sin(th), -(ry + d) * math.sin(ph) * math.cos(th),
                       (rz + d) * math.cos(ph)))

    rings = []
    for i in range(1, nring + 1):
        row = []
        for j in range(nseg):
            th = 2 * math.pi * j / nseg
            # 後面往下多包一段，包住後腦勺
            ph = math.radians(90 + 12 * max(0.0, -math.cos(th)) ** 1.5) * i / nring
            q = (th - math.radians(30)) / math.radians(60)
            seam = abs(q - round(q)) * math.radians(60)
            d = -0.0028 * (1 - smoothstep(0.0, 0.11, seam)) * smoothstep(0.1, 0.4, ph) * smoothstep(1.0, 0.8, i / nring)
            if i >= nring - 1:
                d += 0.0035                  # 帽口一圈稍微外翻（帽圍的厚度）
            row.append(bm.verts.new(dome_pt(th, ph, d)))
        rings.append(row)
    top = bm.verts.new(dome_pt(0, 0))
    bottom = bm.verts.new((0, 0, 0))
    for j in range(nseg):
        k = (j + 1) % nseg
        bm.faces.new((top, rings[0][j], rings[0][k]))
        for r0, r1 in zip(rings[:-1], rings[1:]):
            bm.faces.new((r0[j], r1[j], r1[k], r0[k]))
        bm.faces.new((rings[-1][j], bottom, rings[-1][k]))
    add_ellipsoid(bm, (0, 0, rz + 0.002), (0.015, 0.015, 0.007), seg=10, rings=6)     # 帽頂鈕扣

    # 帽簷：每一欄是一個封閉截面（上面→圓角外緣→下面），沿帽口前面排開
    za = 0.004
    fa = math.sqrt(1 - (za / rz) ** 2) * 0.97
    secs = []
    for j in range(nvis + 1):
        th = math.radians(-82 + 164 * j / nvis)
        taper = smoothstep(1.0, 0.7, abs(th) / math.radians(82))     # 兩端收進帽口，背面看不到突起
        reach = 0.076 * math.cos(th) ** 2 + 0.016 * taper
        base = Vector((rx * fa * math.sin(th), -ry * fa * math.cos(th), za))
        # 帽簷主要往前伸（不是沿帽口往外），側面才不會像翅膀一樣伸出去
        out = (Vector((math.sin(th) * rx, -math.cos(th) * ry, 0)).normalized() * 0.35 + Vector((0, -0.65, 0))).normalized()
        sec = []
        for t in (0.0, 0.35, 0.7, 1.0):
            p = base + out * reach * t
            droop = (-0.010 * t * t - 0.006 * math.sin(th) ** 2 * t) * taper
            sec.append((p, droop))
        h = 0.0045 * (0.4 + 0.6 * taper)
        loop = [bm.verts.new(p + Vector((0, 0, dz + h))) for p, dz in sec]
        tip_p, tip_dz = sec[-1]
        loop.append(bm.verts.new(tip_p + out * h + Vector((0, 0, tip_dz))))
        loop += [bm.verts.new(p + Vector((0, 0, dz - h))) for p, dz in sec[::-1]]
        secs.append(loop)
    for a, b in zip(secs[:-1], secs[1:]):
        for k in range(len(a)):
            bm.faces.new((a[k], b[k], b[(k + 1) % len(a)], a[(k + 1) % len(a)]))
    bm.faces.new(secs[0])
    bm.faces.new(secs[-1][::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

    # 圓標：貼在正面那片布的中間
    lp = dome_pt(0, math.radians(52))
    ln = Vector((lp.x / rx ** 2, lp.y / ry ** 2, lp.z / rz ** 2)).normalized()
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.026, radius2=0.026, depth=0.006)
    logo = set(res["verts"])
    bmesh.ops.transform(bm, matrix=Matrix.Translation(lp + ln * 0.001) @ ln.to_track_quat("Z", "Y").to_matrix().to_4x4(),
                        verts=list(logo))
    for f in bm.faces:
        f.material_index = 1 if all(v in logo for v in f.verts) else 0

    # 往後推、前緣上翹，再整頂上下移到眉毛區的帽子下緣＝CAP_BRIM_Z
    bmesh.ops.transform(bm, matrix=Matrix.Translation((0, -0.125, 0.93)) @ rot_xyz(rx=-14).to_4x4(), verts=bm.verts[:])
    tree = BVHTree.FromBMesh(bm)
    hits = []
    for dx in (-0.8, -0.4, 0.0, 0.4, 0.8):
        for dy in (-0.5, 0.0, 0.5):
            hit = tree.ray_cast(Vector((EYE_C[0] + dx * EYE_R, EYE_C[1] + dy * EYE_R, EYE_C[2])), Vector((0, 0, 1)))
            if hit[0] is not None:
                hits.append(hit[0].z)
    if not hits:
        raise RuntimeError("帽子沒有蓋到眼睛上方，無法用 CAP_BRIM_Z 定高度")
    bmesh.ops.translate(bm, vec=(0, 0, CAP_BRIM_Z - min(hits)), verts=bm.verts[:])
    print(f"帽子：眉毛區下緣 z={CAP_BRIM_Z:.3f}，眼睛上緣到帽子 {CAP_BRIM_Z - EYE_C[2] - EYE_R * 1.09:.3f} m")
    return make_object("Cap", bm, ["cap", "cap_logo"])

#endregion

#region 頸巾


def build_scarf():
    """快遞員頸巾：繞脖子一圈的布圈（貼著 Body 實際的脖子量半徑）＋垂在胸前的三角巾。"""
    body = bpy.data.objects["Body"].data
    n, ax = 16, Vector((0, -0.075))
    pts = []
    for i in range(n):
        th = 2 * math.pi * i / n                 # 0＝正前方
        d = Vector((math.sin(th), -math.cos(th)))
        z = 0.705 - 0.022 * math.cos(th)         # 前低後高，順著脖子往前傾
        r = 0.09
        for v in body.vertices:
            if abs(v.co.z - z) < 0.012:
                h = v.co.xy - ax
                if h.length > 1e-4 and h.normalized().dot(d) > 0.97:
                    r = max(r, h.length)
        pts.append((ax.x + d.x * (r + 0.008), ax.y + d.y * (r + 0.008), z))
    bm = bmesh.new()
    tube(bm, pts, [0.017] * n, seg=6, flat=0.65, closed=True)
    front = Vector(pts[0])
    tube(bm, [front + Vector(o) for o in ((0, 0.004, 0.002), (0.004, -0.010, -0.030), (0.006, -0.016, -0.062),
                                          (0.006, -0.018, -0.085))],
         (0.055, 0.042, 0.022, 0.004), seg=8, caps=(False, True), flat=0.16, up=(1, 0, 0))
    add_ellipsoid(bm, front + Vector((0, -0.006, -0.004)), (0.022, 0.016, 0.018), seg=10, rings=6)   # 結
    return make_object("Scarf", bm, ["scarf"])

#endregion

#region 手、鞋、郵差包


def build_hands_shoes():
    # 卡通手：手掌＋三根粗手指（往掌心微彎）＋往前分開的拇指；手垂在身側、掌心朝內
    bm = bmesh.new()
    pseg, prings, fseg = D["hand"]
    for sx in (1, -1):
        add_ellipsoid(bm, (0.257 * sx, -0.106, 0.362), (0.025, 0.040, 0.042), seg=pseg, rings=prings,
                      rot=rot_xyz(rx=-8))
        for fy, ln in ((-0.132, 0.92), (-0.107, 1.0), (-0.083, 0.88)):
            pts = [(0.258, fy, 0.345), (0.257, fy, 0.322), (0.250, fy - 0.003, 0.302), (0.240, fy - 0.005, 0.292)]
            pts = [mirror_x((x, y, 0.345 + (z - 0.345) * ln), sx) for x, y, z in pts]
            tube(bm, pts, (0.0135, 0.0135, 0.0125, 0.0115), seg=fseg, caps=(False, True))
        tube(bm, [mirror_x(p, sx) for p in ((0.250, -0.124, 0.372), (0.242, -0.148, 0.362), (0.236, -0.162, 0.345))],
             (0.0135, 0.0125, 0.0115), seg=fseg, caps=(False, True))
    hands = make_object("Hands", bm, ["skin"])
    return [hands, build_shoes()]


def build_shoes():
    """鞋：有厚度的鞋底（外緣圓角）＋鞋面＋腳踝的鞋口軟墊＋鞋舌＋三條鞋帶與蝴蝶結。"""
    bm = bmesh.new()
    seg, rings, nsole = D["shoe"]
    cx, cy, a, b, c, zc = 0.10, -0.050, 0.072, 0.118, 0.056, 0.052    # 鞋面橢球

    def top_z(x, y):
        q = 1 - ((x - cx) / a) ** 2 - ((y - cy) / b) ** 2
        return zc + c * math.sqrt(max(0.0, q))

    # 左腳（+X）
    # 鞋面：橢球下半壓進鞋底
    vs = add_ellipsoid(bm, (cx, cy, zc), (a, b, c), seg=seg, rings=rings)
    for v in vs:
        v.co.z = max(v.co.z, 0.014)
    # 鞋底：腳印輪廓（前寬後窄）往上長 4 層，上下外緣都是圓角
    outline = []
    for j in range(nsole):
        t = 2 * math.pi * j / nsole
        w = (a + 0.006) * (1.0 + 0.10 * -math.cos(t))
        outline.append((cx + w * math.sin(t), cy - 0.002 - (b + 0.008) * math.cos(t)))
    mid = (cx, cy - 0.002)
    layers = [(0.0, 0.88), (0.004, 1.0), (0.016, 1.0), (0.020, 0.93)]
    loops = [[bm.verts.new((mid[0] + (x - mid[0]) * k, mid[1] + (y - mid[1]) * k, z)) for x, y in outline]
             for z, k in layers]
    poles = [bm.verts.new((*mid, 0.0)), bm.verts.new((*mid, 0.020))]
    sole = []
    for j in range(nsole):
        k = (j + 1) % nsole
        sole.append(bm.faces.new((poles[0], loops[0][k], loops[0][j])))
        for l0, l1 in zip(loops[:-1], loops[1:]):
            sole.append(bm.faces.new((l0[j], l0[k], l1[k], l1[j])))
        sole.append(bm.faces.new((poles[1], loops[-1][j], loops[-1][k])))
    for f in sole:
        f.material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=sole)
    # 鞋口軟墊：繞腳踝一圈
    ring = [(cx + 0.050 * math.sin(t), 0.004 - 0.052 * math.cos(t), 0.097 + 0.010 * math.cos(t))
            for t in (2 * math.pi * i / 14 for i in range(14))]
    tube(bm, ring, [0.009] * 14, seg=5, closed=True)
    # 鞋舌：從鞋面往上翹出鞋口
    tube(bm, [(cx, -0.052, 0.090), (cx, -0.058, 0.116), (cx, -0.068, 0.130)], (0.022, 0.021, 0.018),
         seg=8, flat=0.22, up=(1, 0, 0))
    # 鞋帶：三條橫跨鞋面，最上面打一個蝴蝶結
    laces = []
    for ly in (-0.072, -0.094, -0.116):
        pts = [(cx + dx, ly, top_z(cx + dx, ly) + 0.004) for dx in (-0.026, -0.013, 0.0, 0.013, 0.026)]
        laces += tube(bm, pts, [0.0035] * 5, seg=4, caps=(False, False), flat=0.6)
    for dx in (-1, 1):
        laces += tube(bm, [(cx, -0.068, 0.115), (cx + 0.016 * dx, -0.072, 0.121), (cx + 0.022 * dx, -0.066, 0.113)],
                      (0.004, 0.006, 0.004), seg=4, flat=0.5)
    for f in {f for v in laces for f in v.link_faces}:
        f.material_index = 2
    # 右腳：整隻鏡射左腳（鞋帶、鞋舌的寫法只算一次）
    left = bm.verts[:]
    dup = bmesh.ops.duplicate(bm, geom=left + bm.edges[:] + bm.faces[:])
    bmesh.ops.scale(bm, vec=(-1, 1, 1), verts=[g for g in dup["geom"] if isinstance(g, bmesh.types.BMVert)])
    bmesh.ops.reverse_faces(bm, faces=[g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)])
    return make_object("Shoes", bm, ["shoe", "sole", "lace"])


def rounded_box(bm, center, half, bevel, mi):
    before = set(bm.faces)
    res = bmesh.ops.create_cube(bm, size=1.0)
    vs = res["verts"]
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ Matrix.Diagonal(Vector(half) * 2).to_4x4(), verts=vs)
    edges = list({e for v in vs for e in v.link_edges})
    bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=D["bag_seg"], profile=0.5, affect="EDGES")
    faces = [f for f in bm.faces if f not in before]
    for f in faces:
        f.material_index = mi
    return faces


def build_bag():
    """郵差包：圓角包身＋前後側面的滾邊（凸起的縫邊）＋有厚度、從頂面折到外側的蓋口＋扣環。"""
    objs = []
    bm = bmesh.new()
    cx, cy, cz = 0.220, 0.012, 0.385     # 比 Bag 骨頭往後一點，垂下來的手不會插進包裡
    hx, hy, hz = 0.042, 0.086, 0.072
    rounded_box(bm, (cx, cy, cz), (hx, hy, hz), 0.018, 0)
    # 滾邊：包身前後兩個側面的外框
    for fy in (cy - hy, cy + hy):
        pts = []
        r = 0.016
        for qx, qz, a0 in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
            for k in range(3):
                a = math.radians(a0 + 30 * k)
                pts.append((cx + qx * (hx - r) + r * math.cos(a), fy, cz + qz * (hz - r) + r * math.sin(a)))
        tube(bm, pts, [0.0035] * len(pts), seg=D["pipe"], closed=True, mi=1)
    # 蓋口：頂面一片＋外側一片（兩片交疊在折角，倒圓角看起來像一片厚布折下來）
    rounded_box(bm, (cx + 0.004, cy, cz + hz + 0.002), (hx + 0.006, hy + 0.004, 0.005), 0.0045, 1)
    rounded_box(bm, (cx + hx + 0.003, cy, cz + 0.030), (0.005, hy + 0.004, 0.047), 0.0045, 1)
    tube(bm, [(cx + hx + 0.009, cy - hy, cz - 0.016), (cx + hx + 0.009, cy + hy, cz - 0.016)], (0.0035, 0.0035),
         seg=D["pipe"], mi=1)                                                      # 蓋口下緣的滾邊
    # 扣環：方框＋穿過去的皮帶舌
    rounded_box(bm, (cx + hx + 0.010, cy, cz - 0.004), (0.003, 0.010, 0.026), 0.002, 1)
    frame = [(cx + hx + 0.013, cy + dy * 0.017, cz - 0.012 + dz * 0.013) for dy, dz in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
    pts = []
    for p0, p1 in zip(frame, frame[1:] + frame[:1]):
        pts += [Vector(p0).lerp(Vector(p1), t) for t in (0.0, 0.5)]
    tube(bm, pts, [0.003] * len(pts), seg=4, closed=True, mi=2)
    objs.append(make_object("Bag", bm, ["bag", "bag_flap", "buckle"]))

    # 背帶：從包的上緣斜過胸前、繞過右肩、從殼底下回到包
    cu = bpy.data.curves.new("StrapCurve", "CURVE")
    cu.dimensions = "3D"
    sp = cu.splines.new("NURBS")
    pts = [(0.205, -0.075, 0.45), (0.13, -0.175, 0.53), (0.0, -0.20, 0.60), (-0.11, -0.13, 0.67),
           (-0.16, -0.03, 0.70), (-0.12, 0.08, 0.68), (0.05, 0.15, 0.58), (0.20, 0.05, 0.45)]
    sp.points.add(len(pts) - 1)
    for p, co in zip(sp.points, pts):
        p.co = (*co, 1.0)
    sp.use_endpoint_u = True
    sp.order_u = 3
    sp.resolution_u = D["strap"][0]
    cu.bevel_depth = 0.011
    cu.bevel_resolution = D["strap"][1]
    cobj = bpy.data.objects.new("StrapCurve", cu)
    COLL.objects.link(cobj)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(cobj.evaluated_get(dg))
    bpy.data.objects.remove(cobj)
    bpy.data.curves.remove(cu)
    me.name = "Strap"
    me.materials.append(mat("strap"))
    strap = bpy.data.objects.new("Strap", me)
    COLL.objects.link(strap)
    strap.parent = ROOT
    objs.append(strap)
    return objs

#endregion

#region 臉部表情（眉毛、眼皮、嘴＋shape key）

# shape key 名稱（Unity 的 blend shape），順序也是 AhJia3D_faces.png 的格子順序（第一格是無表情）
FACE_KEYS = ["Blink_L", "Blink_R", "Brow_Angry", "Brow_Sad", "Brow_Up", "Mouth_Open", "Mouth_Smirk", "Mouth_Frown",
             "Squint"]

# 眼皮下緣／上緣的角度（度，0＝眼睛正上方、90＝正前方、180＝正下方）；預設是冷面的半垂眼
LID_REST = (92.0, 148.0)          # (上眼皮下緣, 下眼皮上緣)
LID_KEYS = {                      # 各 shape key 對 (左眼上, 左眼下, 右眼上, 右眼下) 的位移
    "Blink_L": (50, -10, 0, 0), "Blink_R": (0, 0, 50, -10), "Squint": (16, -22, 16, -22),
}


def eye_frame(sx):
    """眼睛中心與局部座標：f 正前方、u 往上、s 往外側（遠離鼻樑）。"""
    c = Vector((EYE_C[0] * sx, EYE_C[1], EYE_C[2]))
    f = Vector((EYE_DIR[0] * sx, EYE_DIR[1], EYE_DIR[2])).normalized()
    u = (Vector((0, 0, 1)) - f * f.z).normalized()
    s = f.cross(u)
    if s.x * sx < 0:
        s = -s
    return c, f, u, s


def sph(frame, th, la, r):
    """眼睛周圍球面上的點：th 從正上方往前往下轉的角度、la 往外側的角度（度）、r 半徑。"""
    c, f, u, s = frame
    th, la = math.radians(th), math.radians(la)
    return c + r * (math.sin(la) * s + math.cos(la) * (math.sin(th) * f + math.cos(th) * u))


def shell_patch(rows, cols, point):
    """rows×cols 的雙層曲面（外層＋內層＋四邊封口），point(i, j, outer) → 座標。回傳 (頂點, 面)。"""
    nr, nc = len(rows), len(cols)
    verts = [point(i, j, outer) for outer in (True, False) for i in range(nr) for j in range(nc)]
    idx = lambda outer, i, j: (0 if outer else nr * nc) + i * nc + j
    faces = []
    for i in range(nr - 1):
        for j in range(nc - 1):
            faces.append((idx(True, i, j), idx(True, i + 1, j), idx(True, i + 1, j + 1), idx(True, i, j + 1)))
            faces.append((idx(False, i, j), idx(False, i, j + 1), idx(False, i + 1, j + 1), idx(False, i + 1, j)))
    for i in range(nr - 1):                                       # 左右兩邊
        for j in (0, nc - 1):
            q = (idx(True, i, j), idx(False, i, j), idx(False, i + 1, j), idx(True, i + 1, j))
            faces.append(q if j == 0 else q[::-1])
    for j in range(nc - 1):                                       # 上下兩緣
        for i in (0, nr - 1):
            q = (idx(True, i, j), idx(True, i, j + 1), idx(False, i, j + 1), idx(False, i, j))
            faces.append(q if i == 0 else q[::-1])
    return verts, faces


def lid_mesh(edges):
    """兩眼的上下眼皮；edges＝(左上, 左下, 右上, 右下) 的緣角度。上眼皮從頭頂後方蓋到緣，下眼皮從緣蓋到下巴後方。"""
    verts, faces = [], []
    cols = [-85 + 170 * j / 8 for j in range(9)]
    for k, sx in enumerate((1, -1)):
        fr = eye_frame(sx)
        up, lo = edges[2 * k], edges[2 * k + 1]
        for th0, th1, r_out, r_in, n in ((-40, up, 1.20, 1.08, 5), (lo, 235, 1.13, 1.04, 3)):
            rows = list(range(n + 1))

            def point(i, j, outer, th0=th0, th1=th1, r_out=r_out, r_in=r_in, n=n):
                la = cols[j]
                # 外眼角稍微下垂（冷面、沒睡飽的眼神）
                th = th0 + (th1 - th0) * i / n + (6 * la / 85 if (i == n and th0 < 0) else 0)
                return sph(fr, th, la, EYE_R * (r_out if outer else r_in))
            v, fc = shell_patch(rows, cols, point)
            base = len(verts)
            verts += v
            # 兩眼的局部座標一左手一右手（s 都朝外），右眼的面要反過來法線才會朝外
            faces += [tuple(base + x for x in (f if sx > 0 else f[::-1])) for f in fc]
    return verts, faces


# 眉毛：沿眼睛外側角度 la 的一條粗短眉，角度 th 決定高低
BROW_R = 1.5                      # 眉毛離眼睛中心（×EYE_R）
BROW_TH = 42.0
BROW_KEYS = {                     # n：-1 眉頭（靠鼻樑）～ 1 眉尾，回傳 (th 的位移（正＝往下）, 往上平移 公尺)
    "Brow_Angry": lambda n: (8 - 14 * n, 0.0), "Brow_Sad": lambda n: (-6 + 14 * n, 0.0),
    "Brow_Up": lambda n: (-4, 0.008),
}


def brow_mesh(delta):
    verts, faces = [], []
    cols = [-45 + 95 * j / 6 for j in range(7)]
    for sx in (1, -1):
        fr = eye_frame(sx)
        r = EYE_R * BROW_R
        base = len(verts)
        for la in cols:
            n = (la + 45) / 47.5 - 1
            dth, lift = delta(n)
            th = BROW_TH - 4 * (1 - n * n) + dth          # 中間微拱
            h = math.degrees((0.016 - 0.006 * (n + 1) / 2) / r) / 2     # 眉頭粗、眉尾細
            for a, b in ((-h, 0), (h, 0), (h, 0.008), (-h, 0.008)):
                verts.append(sph(fr, th + a, la, r + b) + Vector((0, 0, lift)))
        # 不超過帽簷：整條眉往下移（基本形與每個 shape key 各自夾）
        over = max(v.z for v in verts[base:]) - (CAP_BRIM_Z - 0.005)
        if over > 0:
            verts[base:] = [v - Vector((0, 0, over)) for v in verts[base:]]
        for j in range(len(cols) - 1):
            for q in range(4):
                a0, a1 = base + j * 4 + q, base + j * 4 + (q + 1) % 4
                faces.append((a0, a0 + 4, a1 + 4, a1) if sx > 0 else (a0, a1, a1 + 4, a0 + 4))
        last = base + (len(cols) - 1) * 4
        cap0, cap1 = (base + 3, base + 2, base + 1, base), (last, last + 1, last + 2, last + 3)
        faces += [cap0[::-1], cap1[::-1]] if sx > 0 else [cap0, cap1]
    return verts, faces


# 嘴：長嘴兩側的嘴縫（貼在身體表面的薄片），u＝0 嘴角～1 前端；a＝長嘴剖面上的角度（0 正下方、90 正側面）
# 長嘴的中軸取 BONES 裡 Snout 骨的頭尾（射線從中軸往外打）；嘴角、前端在骨頭上的比例是 MOUTH_T
FACE_BONES = {"head": "Head", "snout": "Snout"}
MOUTH_T = (0.175, 0.84)
MOUTH_KEYS = {                     # 嘴角上下（度），u 與左右（sx）的函式；撇嘴主要在左邊（+X）
    "Mouth_Smirk": lambda u, sx: 30 * (1 - u) ** 2 * (1.0 if sx > 0 else 0.35),
    "Mouth_Frown": lambda u, sx: -16 * (1 - u) ** 2,
}
JAW_OPEN = 17                      # Mouth_Open 下顎繞嘴角往下轉的角度
JAW_BAND = 22                      # 嘴縫下方這麼寬（度）的皮膚張嘴時被拉開，由口腔薄片蓋住


def snout_line():
    """長嘴中軸 ((y0, z0), (y1, z1))；找不到 Snout 骨時用舊造型的值並警告。"""
    for name, head, tail, _ in BONES:
        if name == FACE_BONES["snout"]:
            return (head[1], head[2]), (tail[1], tail[2])
    print(f"警告：BONES 裡沒有 {FACE_BONES['snout']}，嘴的位置改用預設長嘴")
    return (-0.20, 0.83), (-0.40, 0.775)


def mouth_y():
    (y0, _), (y1, _) = snout_line()
    return tuple(y0 + (y1 - y0) * t for t in MOUTH_T)


def mouth_angle(u):
    return 78 - 40 * max(0.0, u) ** 0.8


def snout_axis_z(y):
    (y0, z0), (y1, z1) = snout_line()
    return z0 + (z1 - z0) * (y - y0) / (y1 - y0)


def on_body(body, y, a, sx):
    """長嘴剖面角度 a 處的身體表面（往外推一點，避免和皮膚重疊閃爍）。"""
    axis = Vector((0, y, snout_axis_z(y)))
    d = Vector((math.sin(math.radians(a)) * sx, 0, -math.cos(math.radians(a))))
    hit, co, nrm, _ = body.ray_cast(axis + d * 0.2, -d, distance=0.2)
    if not hit:
        return axis + d * 0.05
    return co + nrm * 0.0018


def jaw(p, amount=1.0):
    """Mouth_Open 的下顎：嘴縫以下、嘴角往前的長嘴繞嘴角往下轉；嘴縫附近一條皮膚漸變（被拉開的部分）。"""
    my = mouth_y()
    u = (p.y - my[0]) / (my[1] - my[0])
    az = snout_axis_z(p.y)
    if u <= 0 or Vector((p.x, p.z - az)).length > 0.09:
        return p.copy()
    a = math.degrees(math.atan2(abs(p.x), az - p.z))
    w = smoothstep(0.0, 0.3, u) * (1.0 - smoothstep(mouth_angle(u) - JAW_BAND, mouth_angle(u) - 2, a))
    pivot = Vector((0, my[0], snout_axis_z(my[0])))
    return pivot + Matrix.Rotation(math.radians(JAW_OPEN * amount * w), 3, "X") @ (p - pivot)


def mouth_mesh(body, key):
    """每一段由上到下 4 排頂點：嘴縫上緣、嘴縫下緣、舌頭上緣、下顎；平常後 3 排重疊（嘴縫只是一條線）。"""
    verts, faces = [], []
    n = 10
    shift = MOUTH_KEYS.get(key, lambda u, sx: 0)
    my = mouth_y()
    for sx in (1, -1):
        base = len(verts)
        for i in range(n + 1):
            u = i / n
            y = my[0] + (my[1] - my[0]) * u
            a = mouth_angle(u) + shift(u, sx)
            w = 1.5 + 4.5 * math.sin(math.pi * min(1.0, 0.15 + u))        # 嘴縫寬度（度），嘴角收尖
            if key == "Mouth_Open":
                rows = [on_body(body, y, a + w / 2, sx), on_body(body, y, a - w / 2, sx),
                        jaw(on_body(body, y, a - JAW_BAND * 0.55, sx)), jaw(on_body(body, y, a - JAW_BAND - 1, sx))]
            else:
                rows = [on_body(body, y, a + w / 2, sx)] + [on_body(body, y, a - w / 2, sx)] * 3
            verts += rows
        for i in range(n):
            for r in range(3):
                a0, a1 = base + i * 4 + r, base + (i + 1) * 4 + r
                faces.append((a0, a1, a1 + 1, a0 + 1) if sx > 0 else (a0, a0 + 1, a1 + 1, a1))
    return verts, faces


def face_part(name, mesh_fn, keys, mats, mat_of_face=None):
    """mesh_fn(key 名或 None) → (頂點, 面)；None 是基本形，其他是各 shape key 的形狀（拓樸相同）。"""
    verts, faces = mesh_fn(None)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    for m in mats:
        me.materials.append(mat(m))
    if mat_of_face:
        for p in me.polygons:
            p.material_index = mat_of_face(p.index)
    me.shade_smooth()
    obj = bpy.data.objects.new(name, me)
    COLL.objects.link(obj)
    obj.parent = ROOT
    obj.shape_key_add(name="Basis")
    for k in keys:
        sk = obj.shape_key_add(name=k)
        for p, co in zip(sk.data, mesh_fn(k)[0]):
            p.co = co
    return obj


def build_face(body):
    """眉毛、眼皮、嘴：依 EYE_C／EYE_R 與身體表面擺放，各自帶上對應的 shape key（合併網格時依名稱併在一起）。"""
    def lids(k):
        d = LID_KEYS.get(k, (0, 0, 0, 0))
        return lid_mesh(tuple(LID_REST[i % 2] + d[i] for i in range(4)))

    def brows(k):
        return brow_mesh(BROW_KEYS.get(k, lambda n: (0.0, 0.0)))

    def mouth(k):
        return mouth_mesh(body, k)

    eyelids = face_part("Eyelids", lids, list(LID_KEYS), ["skin"])
    brow = face_part("Brows", brows, list(BROW_KEYS), ["brow"])
    # 嘴的每一段三條帶子：上兩條是口腔（暗）、最下面是舌頭（平常寬度 0，張嘴才看得到）
    mouth_obj = face_part("Mouth", mouth, ["Mouth_Open"] + list(MOUTH_KEYS), ["mouth", "tongue"],
                          mat_of_face=lambda i: 1 if i % 3 == 2 else 0)
    # 身體也帶 Mouth_Open（下顎），合併網格時和嘴的同名 key 併成一個
    body.shape_key_add(name="Basis")
    sk = body.shape_key_add(name="Mouth_Open")
    for p, v in zip(sk.data, body.data.vertices):
        p.co = jaw(v.co)
    return [eyelids, brow, mouth_obj]


def face_bind_rules(arm):
    """Eyelids／Brows 綁頭；Mouth 前段跟長嘴、嘴角往頭漸變。骨頭不存在時退回頭骨（並警告）。"""
    names = {b.name for b in arm.data.bones}
    head = FACE_BONES["head"] if FACE_BONES["head"] in names else arm.data.bones[0].name
    snout = FACE_BONES["snout"] if FACE_BONES["snout"] in names else head
    if head != FACE_BONES["head"] or snout != FACE_BONES["snout"]:
        print(f"警告：臉部零件改綁 {head}／{snout}（骨架裡沒有 {FACE_BONES['head']} 或 {FACE_BONES['snout']}）")
    (y0, _), (y1, _) = snout_line()
    ramp = (y0, y0 + (y1 - y0) * 0.35)

    def mouth(co):
        w = smoothstep(ramp[0], ramp[1], co.y)
        return {snout: w, head: 1.0 - w} if snout != head else {head: 1.0}
    return {"Eyelids": lambda co: {head: 1.0}, "Brows": lambda co: {head: 1.0}, "Mouth": mouth}


def add_expression_drivers(arm, mesh):
    """表情的值放在骨架物件的自訂屬性（和骨頭一起 key 在同一個 Action），shape key 用 driver 讀它。
    FBX 匯出時逐格烘焙 shape key 的值，每個 Action 的表情各自跟著自己的 clip 進 Unity。"""
    keys = mesh.data.shape_keys.key_blocks
    for k in FACE_KEYS:
        arm[k] = 0.0
        arm.id_properties_ui(k).update(min=0.0, max=1.0)
        fc = keys[k].driver_add("value")
        drv = fc.driver
        drv.type = "AVERAGE"
        var = drv.variables.new()
        var.type = "SINGLE_PROP"
        var.targets[0].id = arm
        var.targets[0].data_path = f'["{k}"]'


def set_expression(arm, vals):
    for k in FACE_KEYS:
        arm[k] = float(vals.get(k, 0.0))


def render_face_sheet(scene, inst, arm, path, cell=360):
    """表情總覽：第一格無表情，之後每個 shape key 一格；上排 3/4、下排側面，頭部特寫。"""
    import numpy as np
    tmp = os.path.join(os.path.dirname(path), "_face.png")
    cam = camera(scene, "CamFace", 0.50, (0, 0, 0))
    rot0 = inst.rotation_euler.copy()
    label = bpy.data.objects.new("FaceLabel", bpy.data.curves.new("FaceLabel", "FONT"))
    label.data.size = 0.026
    label.data.align_x = "CENTER"
    label.data.materials.append(mat("outline"))
    scene.collection.objects.link(label)
    bm = bmesh.new()                       # 標籤底板
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5)
    bmesh.ops.scale(bm, vec=(1.0, 0.09, 1.0), verts=bm.verts)
    plate = make_object("FacePlate", bm, ["eye_white"], smooth=False)
    COLL.objects.unlink(plate)
    scene.collection.objects.link(plate)
    plate.parent = None
    rows = []
    for rz in (-30, -90):
        inst.rotation_euler = (0, 0, math.radians(rz))
        head = Matrix.Rotation(math.radians(rz), 3, "Z") @ Vector((0, EYE_C[1] - 0.015, EYE_C[2] + 0.008))
        cm = cam.rotation_euler.to_matrix()
        cam.location = head + cm @ Vector((0, 0, 6.0))
        label.location = head + cm @ Vector((0, -0.222, 1.0))
        label.rotation_euler = plate.rotation_euler = cam.rotation_euler
        plate.location = head + cm @ Vector((0, -0.228, 0.9))
        row = []
        for k in [None] + FACE_KEYS:
            set_expression(arm, {k: 1.0} if k else {})
            label.data.body = k or "Neutral"
            render(scene, cam, tmp, cell, cell)
            img = bpy.data.images.load(tmp)
            a = np.empty(cell * cell * 4, dtype=np.float32)
            img.pixels.foreach_get(a)
            bpy.data.images.remove(img)
            row.append(a.reshape(cell, cell, 4))
        rows.append(np.concatenate(row, axis=1))
    sheet = np.concatenate(rows[::-1], axis=0)
    h, w = sheet.shape[:2]
    res = bpy.data.images.new("face_sheet", w, h)
    res.pixels.foreach_set(sheet.ravel())
    res.filepath_raw = path
    res.file_format = "PNG"
    res.save()
    os.remove(tmp)
    bpy.data.objects.remove(label)
    bpy.data.objects.remove(plate)
    bpy.data.objects.remove(cam)
    inst.rotation_euler = rot0
    set_expression(arm, {})

#endregion

#region 預覽


def add_outline(obj, width):
    obj.data.materials.append(mat("outline"))
    sol = obj.modifiers.new("Outline", "SOLIDIFY")
    sol.thickness = width
    sol.offset = 1
    sol.use_flip_normals = True
    sol.use_rim = False
    sol.material_offset = 99


def setup_scene():
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.view_settings.view_transform = "Standard"
    scene.render.fps = ANIM_FPS          # FBX 的動畫片段用場景 fps 計時
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.80, 0.82, 0.86, 1)
    bg.inputs["Strength"].default_value = 0.6
    sun = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "SUN"))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    return scene


def camera(scene, name, ortho, target, dist=6.0, elev=8.0):
    cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = ortho
    e = math.radians(elev)
    cam.location = Vector(target) + Vector((0, -dist * math.cos(e), dist * math.sin(e)))
    cam.rotation_euler = (math.radians(90) - e, 0, 0)
    scene.collection.objects.link(cam)
    return cam


def render(scene, cam, path, w, h):
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.resolution_percentage = 100
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

#endregion


#region 骨架與蒙皮

# (名稱, 頭, 尾, 父骨)；Left＝+X（角色面朝 -Y，自己的左手在 +X）
BONES = [
    ("Hips", (0, 0.02, 0.33), (0, 0.0, 0.45), None),
    ("Spine", (0, 0.0, 0.45), (0, -0.03, 0.58), "Hips"),
    ("Chest", (0, -0.03, 0.58), (0, -0.07, 0.70), "Spine"),
    ("Neck", (0, -0.07, 0.70), (0, -0.11, 0.79), "Chest"),
    ("Head", (0, -0.11, 0.79), (0, -0.12, 0.99), "Neck"),
    ("Snout", (0, -0.20, 0.83), (0, -0.43, 0.778), "Head"),
    ("Tail1", (0, 0.10, 0.33), (0, 0.22, 0.27), "Hips"),
    ("Tail2", (0, 0.22, 0.27), (0, 0.31, 0.21), "Tail1"),
    ("Tail3", (0, 0.31, 0.21), (0, 0.42, 0.145), "Tail2"),
    ("Bag", (0.218, -0.02, 0.47), (0.218, -0.02, 0.31), "Hips"),
]
for _side, _sx in (("Left", 1), ("Right", -1)):
    BONES += [
        (_side + "UpperLeg", (0.085 * _sx, 0.02, 0.29), (0.10 * _sx, -0.01, 0.17), "Hips"),
        (_side + "LowerLeg", (0.10 * _sx, -0.01, 0.17), (0.10 * _sx, 0.0, 0.075), _side + "UpperLeg"),
        (_side + "Foot", (0.10 * _sx, 0.0, 0.075), (0.10 * _sx, -0.11, 0.03), _side + "LowerLeg"),
        (_side + "Shoulder", (0.04 * _sx, -0.04, 0.64), (0.135 * _sx, -0.04, 0.635), "Chest"),
        (_side + "UpperArm", (0.135 * _sx, -0.04, 0.635), (0.215 * _sx, -0.06, 0.505), _side + "Shoulder"),
        (_side + "LowerArm", (0.215 * _sx, -0.06, 0.505), (0.25 * _sx, -0.09, 0.40), _side + "UpperArm"),
        (_side + "Hand", (0.25 * _sx, -0.09, 0.40), (0.262 * _sx, -0.105, 0.31), _side + "LowerArm"),
        (_side + "Ear", (0.062 * _sx, -0.072, 0.915), (0.11 * _sx, -0.035, 1.12), "Head"),
        # 武器掛點：手心往前，之後換刀、弓等道具直接掛在這根骨頭底下（不變形網格）
        (_side + "HandProp", (0.262 * _sx, -0.10, 0.345), (0.262 * _sx, -0.20, 0.345), _side + "Hand"),
    ]

# 只給配件用、不參與身體自動權重的骨頭
ACCESSORY_BONES = {"Bag", "LeftEar", "RightEar", "LeftHandProp", "RightHandProp"}


def select_only(objs, active):
    vl = bpy.context.view_layer
    for o in vl.objects:
        o.select_set(o in objs)
    vl.objects.active = active


def build_armature():
    arm_data = bpy.data.armatures.new("AhJia_Rig")
    arm = bpy.data.objects.new("AhJia_Rig", arm_data)
    COLL.objects.link(arm)
    arm.parent = ROOT
    arm_data.display_type = "STICK"
    select_only([arm], arm)
    bpy.ops.object.mode_set(mode="EDIT")
    ebs = {}
    for name, head, tail, parent in BONES:
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail = head, tail
        # roll：讓每根骨頭的局部 X 都是世界的左右方向，擺姿勢時「繞 X 轉」一律是前後擺
        d = (Vector(tail) - Vector(head)).normalized()
        eb.align_roll(Vector((0, 0, 1)) if abs(d.y) > 0.7 else Vector((0, -1, 0)))
        if parent:
            eb.parent = ebs[parent]
            eb.use_connect = (ebs[parent].tail - Vector(head)).length < 1e-4
        ebs[name] = eb
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def torso_weights(co):
    """背殼、殼內填充、背帶：依高度在 Hips／Spine／Chest 之間漸變，彎腰時殼跟著身體走。"""
    chest = smoothstep(0.52, 0.70, co.z)
    hips = 1.0 - smoothstep(0.36, 0.50, co.z)
    spine = max(0.0, 1.0 - chest - hips)
    return {"Hips": hips, "Spine": spine, "Chest": chest}


def side(co, base):
    return {("Left" if co.x > 0 else "Right") + base: 1.0}


def rigid_bind(obj, arm, rule):
    """rule(頂點座標) → {骨名: 權重}，寫進頂點群組後掛 Armature 修改器。"""
    groups = {}
    for v in obj.data.vertices:
        for bone, w in rule(v.co).items():
            if w <= 0:
                continue
            if bone not in groups:
                groups[bone] = obj.vertex_groups.new(name=bone)
            groups[bone].add([v.index], w, "REPLACE")
    obj.parent = arm
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm


def skin_character(arm, parts):
    """身體用 Blender 的自動權重（熱擴散）；配件剛性綁到對應的骨頭；最後全部合併成一個網格。"""
    by_name = {o.name: o for o in parts}
    body = by_name["Body"]
    for b in arm.data.bones:
        b.use_deform = b.name not in ACCESSORY_BONES
    select_only([body, arm], arm)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for b in arm.data.bones:
        b.use_deform = not b.name.endswith("HandProp")
    if len(body.vertex_groups) < 10:
        raise RuntimeError("自動權重失敗：Body 的頂點群組太少")

    rules = {
        "Shell": torso_weights, "ShellFill": torso_weights, "Strap": torso_weights,
        "Eyes": lambda co: {"Head": 1.0}, "Cap": lambda co: {"Head": 1.0}, "Nose": lambda co: {"Snout": 1.0},
        "Ears": lambda co: side(co, "Ear"), "Hands": lambda co: side(co, "Hand"),
        "Shoes": lambda co: side(co, "Foot"), "Bag": lambda co: {"Bag": 1.0},
        "Scarf": lambda co: {"Neck": 0.5 * smoothstep(0.66, 0.73, co.z), "Chest": 1.0 - 0.5 * smoothstep(0.66, 0.73, co.z)},
    }
    rules.update(face_bind_rules(arm))
    for name, rule in rules.items():
        rigid_bind(by_name[name], arm, rule)

    # 描邊粗細用頂點群組控制：身體、殼、帽子 1.0，小配件 0.6（合併後還能分粗細）
    for o in parts:
        g = o.vertex_groups.new(name="OutlineWidth")
        g.add(list(range(len(o.data.vertices))), 1.0 if o.name in ("Body", "Shell", "Cap") else 0.6, "REPLACE")

    select_only(parts, body)
    bpy.ops.object.join()
    body.name = body.data.name = "AhJia_Mesh"
    return body


# 跑步中段＋轉頭、甩尾、耳朵後倒，用來檢查變形；(x, y, z) 度，繞骨頭局部軸（X＝前後擺）
TEST_POSE = {
    "Hips": (0, 0, -8), "Spine": (10, 0, 6), "Chest": (6, 0, 6), "Neck": (-10, 0, 0), "Head": (-6, 0, 18),
    "Snout": (-8, 0, 0), "Tail1": (25, 0, 0), "Tail2": (15, 0, 10), "Tail3": (10, 0, 10),
    "LeftUpperLeg": (-45, 0, 0), "LeftLowerLeg": (-60, 0, 0), "LeftFoot": (-20, 0, 0),
    "RightUpperLeg": (35, 0, 0), "RightLowerLeg": (-25, 0, 0), "RightFoot": (15, 0, 0),
    "LeftUpperArm": (40, 0, 0), "LeftLowerArm": (45, 0, 0),
    "RightUpperArm": (-45, 0, 0), "RightLowerArm": (60, 0, 0),
    "LeftEar": (-35, 0, 0), "RightEar": (-35, 0, 0),
}


def set_pose(arm, pose):
    for pb in arm.pose.bones:
        x, y, z = pose.get(pb.name, (0, 0, 0))
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)

#endregion


#region 動畫

# 30 fps。每個關鍵格是 {骨名: (x, y, z) 度}；"@Hips" 是 Hips 的位移（骨頭局部座標，Y＝往上）。
# 正負號（骨頭局部 X，實測）：直立或下垂的骨頭（脊椎、頸、頭、耳、手臂、腿、包包）正＝往前擺／往前彎／低頭；
# 膝蓋彎曲是 LowerLeg 負、手肘彎曲是 LowerArm 正；水平的骨頭（尾巴、嘴、腳掌）正＝往上翹。
# 每一格都會 key 全部骨頭（沒寫的＝0），動作之間不會殘留姿勢。
ANIM_FPS = 30


def _mirror(k):
    """左右對調（跑步後半步用）：Left↔Right，y、z 反號。"""
    out = {}
    for name, v in k.items():
        if name.startswith("@"):
            out[name] = v
            continue
        other = name.replace("Left", "#").replace("Right", "Left").replace("#", "Right")
        out[other] = (v[0], -v[1], -v[2])
    return out


_RUN_CONTACT = {
    "Spine": (12, 6, 0), "Chest": (4, 4, 0), "Neck": (-8, -4, 0), "Head": (-4, -3, 0),
    "LeftUpperLeg": (-42, 0, 0), "LeftLowerLeg": (-18, 0, 0), "LeftFoot": (-8, 0, 0),
    "RightUpperLeg": (34, 0, 0), "RightLowerLeg": (-20, 0, 0), "RightFoot": (15, 0, 0),
    "LeftUpperArm": (35, 0, 6), "LeftLowerArm": (40, 0, 0),
    "RightUpperArm": (-40, 0, -6), "RightLowerArm": (25, 0, 0),
    "Tail1": (12, 0, -10), "Tail2": (6, 0, -8), "LeftEar": (-28, 0, 0), "RightEar": (-28, 0, 0),
    "Bag": (12, 0, 0), "@Hips": (0, 0, 0),
}
_RUN_PASS = {
    "Spine": (12, 0, 0), "Chest": (4, 0, 0), "Neck": (-8, 0, 0), "Head": (-4, 0, 0),
    "LeftUpperLeg": (5, 0, 0), "LeftLowerLeg": (-8, 0, 0), "LeftFoot": (0, 0, 0),
    "RightUpperLeg": (-5, 0, 0), "RightLowerLeg": (-95, 0, 0), "RightFoot": (-20, 0, 0),
    "LeftUpperArm": (0, 0, 6), "LeftLowerArm": (35, 0, 0),
    "RightUpperArm": (0, 0, -6), "RightLowerArm": (35, 0, 0),
    "Tail1": (18, 0, 0), "Tail2": (10, 0, 0), "LeftEar": (-34, 0, 0), "RightEar": (-34, 0, 0),
    "Bag": (-8, 0, 0), "@Hips": (0, 0.035, 0),
}

# 縮成球：身體往前捲、大腿抬到胸前、手抱膝、尾巴往下捲進殼
_TUCK = {
    "Spine": (50, 0, 0), "Chest": (35, 0, 0), "Neck": (40, 0, 0), "Head": (30, 0, 0), "Snout": (-20, 0, 0),
    "LeftUpperLeg": (95, 0, 0), "LeftLowerLeg": (-120, 0, 0), "RightUpperLeg": (95, 0, 0),
    "RightLowerLeg": (-120, 0, 0), "LeftUpperArm": (60, 0, -15), "LeftLowerArm": (100, 0, 0),
    "RightUpperArm": (60, 0, 15), "RightLowerArm": (100, 0, 0), "Tail1": (-60, 0, 0), "Tail2": (-50, 0, 0),
    "Tail3": (-40, 0, 0), "LeftEar": (-60, 0, 0), "RightEar": (-60, 0, 0), "@Hips": (0, 0.12, 0),
}

ANIMS = {
    # 名稱: (是否循環, [(格, 關鍵姿勢), ...])
    "Idle": (True, [
        (0, {"Spine": (0, 0, 0), "Chest": (0, 0, 0), "Head": (0, 0, 0), "Tail2": (0, 0, -6),
             "LeftUpperArm": (0, 0, 0), "RightUpperArm": (0, 0, 0), "LeftEar": (0, 0, 0), "@Hips": (0, 0, 0)}),
        (24, {"Spine": (3, 0, 0), "Chest": (2, 0, 0), "Head": (-3, 0, 2), "Tail2": (0, 0, 6),
              "LeftUpperArm": (0, 0, 4), "RightUpperArm": (0, 0, -4), "LeftEar": (0, 0, 0), "@Hips": (0, -0.006, 0)}),
        (36, {"Spine": (2, 0, 0), "Chest": (1, 0, 0), "Head": (-2, 0, 1), "Tail2": (0, 0, 2),
              "LeftUpperArm": (0, 0, 2), "RightUpperArm": (0, 0, -2), "LeftEar": (-25, 0, 0), "@Hips": (0, -0.003, 0)}),
        (48, {"Spine": (0, 0, 0), "Chest": (0, 0, 0), "Head": (0, 0, 0), "Tail2": (0, 0, -6),
              "LeftUpperArm": (0, 0, 0), "RightUpperArm": (0, 0, 0), "LeftEar": (0, 0, 0), "@Hips": (0, 0, 0)}),
    ]),
    "Run": (True, [
        (0, _RUN_CONTACT), (5, _RUN_PASS), (10, _mirror(_RUN_CONTACT)), (15, _mirror(_RUN_PASS)),
        (20, _RUN_CONTACT),
    ]),
    # 跳躍＝縮成球旋轉（跳、落、滾殼共用，design.md §8.3）；Hips 繞 X 轉一圈（線性，正＝往前滾）
    "Jump": (True, [
        (0, dict(_TUCK, Hips=(0, 0, 0))), (16, dict(_TUCK, Hips=(360, 0, 0))),
    ]),
    # 甩包：靠鏡頭的左手抓背帶往後拉（預備）→ 轉身把包往前上方甩出 → 收招
    "Melee": (False, [
        (0, {"Spine": (0, 0, 0), "Chest": (0, 0, 0), "Bag": (0, 0, 0), "LeftUpperArm": (0, 0, 0),
             "LeftLowerArm": (0, 0, 0), "RightUpperArm": (0, 0, 0), "Head": (0, 0, 0)}),
        (3, {"Spine": (-4, -22, 0), "Chest": (-2, -12, 0), "Bag": (-45, 0, 10), "LeftUpperArm": (-35, 0, 15),
             "LeftLowerArm": (30, 0, 0), "RightUpperArm": (20, 0, 0), "Head": (0, 15, 0)}),
        (6, {"Spine": (12, 25, 0), "Chest": (6, 15, 0), "Bag": (150, 0, -5), "LeftUpperArm": (85, 0, 10),
             "LeftLowerArm": (10, 0, 0), "RightUpperArm": (-25, 0, 0), "Head": (0, -10, 0)}),
        (9, {"Spine": (10, 30, 0), "Chest": (5, 18, 0), "Bag": (175, 0, -10), "LeftUpperArm": (95, 0, 5),
             "LeftLowerArm": (5, 0, 0), "RightUpperArm": (-30, 0, 0), "Head": (0, -12, 0)}),
        (16, {"Spine": (0, 0, 0), "Chest": (0, 0, 0), "Bag": (0, 0, 0), "LeftUpperArm": (0, 0, 0),
              "LeftLowerArm": (0, 0, 0), "RightUpperArm": (0, 0, 0), "Head": (0, 0, 0)}),
    ]),
    # 彈弓：靠鏡頭的左手往前伸直（握彈弓），右手把皮筋拉到胸前，放開時往後彈
    "Shoot": (False, [
        (0, {"LeftUpperArm": (0, 0, 0), "LeftLowerArm": (0, 0, 0), "RightUpperArm": (0, 0, 0),
             "RightLowerArm": (0, 0, 0), "Spine": (0, 0, 0), "Head": (0, 0, 0)}),
        (3, {"LeftUpperArm": (85, 0, -10), "LeftLowerArm": (5, 0, 0), "RightUpperArm": (70, 0, 25),
             "RightLowerArm": (110, 0, 0), "Spine": (-4, 12, 0), "Head": (4, -8, 0)}),
        (5, {"LeftUpperArm": (90, 0, -10), "LeftLowerArm": (0, 0, 0), "RightUpperArm": (30, 0, 10),
             "RightLowerArm": (20, 0, 0), "Spine": (-6, 6, 0), "Head": (4, -6, 0)}),
        (12, {"LeftUpperArm": (0, 0, 0), "LeftLowerArm": (0, 0, 0), "RightUpperArm": (0, 0, 0),
              "RightLowerArm": (0, 0, 0), "Spine": (0, 0, 0), "Head": (0, 0, 0)}),
    ]),
    # 受傷：上身往後仰、頭往後甩、雙手張開、耳朵往後貼、尾巴翹起
    "Hurt": (False, [
        (0, {"Spine": (0, 0, 0), "Neck": (0, 0, 0), "Head": (0, 0, 0), "LeftUpperArm": (0, 0, 0),
             "RightUpperArm": (0, 0, 0), "LeftEar": (0, 0, 0), "RightEar": (0, 0, 0), "Tail1": (0, 0, 0)}),
        (3, {"Spine": (-18, 0, 0), "Neck": (-25, 0, 0), "Head": (-10, 0, 0), "LeftUpperArm": (0, 0, 45),
             "RightUpperArm": (0, 0, -45), "LeftEar": (-30, 0, 0), "RightEar": (-30, 0, 0), "Tail1": (35, 0, 0)}),
        (9, {"Spine": (-12, 0, 0), "Neck": (-15, 0, 0), "Head": (-6, 0, 0), "LeftUpperArm": (0, 0, 35),
             "RightUpperArm": (0, 0, -35), "LeftEar": (-20, 0, 0), "RightEar": (-20, 0, 0), "Tail1": (25, 0, 0)}),
    ]),
}

# 表情：(格, {shape key: 值})；沒寫到的 key 沿用上一格的值（第一格之前是 0），每一格都 key 全部表情
EXPRESSIONS = {
    # 冷面撇嘴，偶爾眨一下眼
    "Idle": [(0, {"Mouth_Smirk": 0.5}), (17, {}), (19, {"Blink_L": 1.0, "Blink_R": 1.0}),
             (22, {"Blink_L": 0.0, "Blink_R": 0.0}), (34, {}), (36, {"Brow_Up": 0.35}), (42, {"Brow_Up": 0.0}),
             (48, {})],
    "Run": [(0, {"Brow_Angry": 0.3, "Squint": 0.25, "Mouth_Frown": 0.3}), (20, {})],
    "Jump": [(0, {"Blink_L": 1.0, "Blink_R": 1.0, "Brow_Angry": 0.4, "Mouth_Frown": 0.5}), (16, {})],
    "Melee": [(0, {"Brow_Angry": 0.5}), (3, {"Brow_Angry": 1.0, "Squint": 0.5, "Mouth_Frown": 0.6}),
              (6, {"Squint": 0.2, "Mouth_Open": 0.9, "Mouth_Frown": 0.2}), (9, {}),
              (16, {"Brow_Angry": 0.3, "Squint": 0.0, "Mouth_Open": 0.0, "Mouth_Frown": 0.0})],
    "Shoot": [(0, {"Squint": 0.3}), (3, {"Squint": 0.9, "Brow_Angry": 0.3, "Mouth_Smirk": 0.4}),
              (5, {"Squint": 0.5, "Brow_Angry": 0.0, "Brow_Up": 0.3, "Mouth_Smirk": 0.7}),
              (12, {"Squint": 0.1, "Brow_Up": 0.0, "Mouth_Smirk": 0.5})],
    "Hurt": [(0, {}), (3, {"Brow_Sad": 1.0, "Mouth_Open": 0.9, "Squint": 1.0, "Mouth_Frown": 0.5}),
             (9, {"Brow_Sad": 0.8, "Mouth_Open": 0.45, "Squint": 0.8})],
}

# 二次動畫（overlapping action）：耳朵、尾巴、包包的末端像有慣性的質點，追著原本動作的位置跑，
# 落後與回彈的量換算成骨頭的額外旋轉。骨名: (跟隨強度, 速度保留, 最大偏角 度)；越往末端越軟。
OVERLAP = {
    "Tail1": (0.30, 0.55, 18), "Tail2": (0.24, 0.60, 18), "Tail3": (0.18, 0.65, 20),
    "LeftEar": (0.26, 0.62, 28), "RightEar": (0.26, 0.62, 28), "Bag": (0.22, 0.62, 30),
}
# 甩一下：(格, 骨名, 把末端甩開的角度 (x, y, z) 度)，之後由上面的彈簧自己回彈。Idle 抖耳朵；Hurt 被打到時包包與尾巴被震開
KICKS = {
    "Idle": [(34, "LeftEar", (-35, 0, -12)), (37, "LeftEar", (20, 0, 8))],
    "Hurt": [(1, "Bag", (-40, 0, 15)), (1, "Tail1", (25, 0, 0)), (1, "Tail3", (30, 0, 0))],
}


def _fcurves(action, slot):
    try:
        from bpy_extras.anim_utils import action_get_channelbag_for_slot
        bag = action_get_channelbag_for_slot(action, slot)
        return bag.fcurves if bag else []
    except ImportError:
        return action.fcurves


def apply_overlap(arm, name, first, last, loop):
    """在目前的 Action 上疊二次動畫：逐格取原本動作的姿勢，模擬 OVERLAP 各骨頭末端的彈簧，結果逐格 key 回去。
    循環動作連跑 3 圈取最後一圈（進入穩定的週期），最後一格＝第一格，接縫不跳。"""
    scene = bpy.context.scene
    bones = [b for b in OVERLAP if b in arm.pose.bones]
    missing = [b for b in OVERLAP if b not in arm.pose.bones]
    if missing:
        print(f"警告：{name} 的二次動畫略過不存在的骨頭 {missing}")
    frames = list(range(first, last if loop else last + 1))
    posed = {}
    for f in frames:
        scene.frame_set(f)
        posed[f] = {b: (arm.pose.bones[b].head.copy(), arm.pose.bones[b].tail.copy(),
                        arm.pose.bones[b].matrix.to_3x3().normalized(), arm.pose.bones[b].rotation_euler.copy())
                    for b in bones}
    kicks = KICKS.get(name, [])
    for b in bones:
        k, keep, limit = OVERLAP[b]
        p, v, out = None, Vector(), {}
        for f in frames * (3 if loop else 1):
            head, tail, m, eul = posed[f][b]
            if p is None:
                p = tail.copy()
            for tf, tb, kick in kicks:
                if tf == f and tb == b:
                    rel = m.transposed() @ (p - head)
                    p = head + m @ (rot_xyz(*kick) @ rel)
            v = v * keep + (tail - p) * k
            p = p + v
            q = (m.transposed() @ (tail - head)).rotation_difference(m.transposed() @ (p - head))
            if q.angle > 1e-6:                     # 軟上限：接近最大偏角時越來越難再轉
                q = Quaternion(q.axis, math.radians(limit) * math.tanh(q.angle / math.radians(limit)))
            out[f] = (eul.to_quaternion() @ q).to_euler("XYZ", eul)
        pb = arm.pose.bones[b]
        for f in frames + ([last] if loop else []):
            pb.rotation_euler = out[first if f == last and loop else f]
            pb.keyframe_insert("rotation_euler", frame=f, group=b)
    scene.frame_set(0)


def _finish_curves(act, slot, name, loop):
    for fc in _fcurves(act, slot):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR" if name == "Jump" else "BEZIER"
        if loop and not fc.modifiers:
            fc.modifiers.new("CYCLES")


def build_actions(arm):
    """依 ANIMS 建 Action（掛 fake user，FBX 匯出時全部帶出去）。回傳 {名稱: (action, 最後一格)}。
    表情（EXPRESSIONS）key 在骨架物件的自訂屬性上，二次動畫（OVERLAP）最後疊上去。"""
    arm.animation_data_create()
    result = {}
    for name, (loop, keys) in ANIMS.items():
        act = bpy.data.actions.new("AhJia_" + name)
        act.use_fake_user = True
        arm.animation_data.action = act
        # 每個動作都 key 全部骨頭與 Hips 位移：切換動作時（Blender 預覽或 Unity 的 Animator）不會殘留上一個動作的姿勢
        for frame, k in keys:
            for pb in arm.pose.bones:
                pb.rotation_mode = "XYZ"
                x, y, z = k.get(pb.name, (0, 0, 0))
                pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
                pb.keyframe_insert("rotation_euler", frame=frame, group=pb.name)
            arm.pose.bones["Hips"].location = k.get("@Hips", (0, 0, 0))
            arm.pose.bones["Hips"].keyframe_insert("location", frame=frame, group="Hips")
        # 表情同理：每個動作都 key 全部表情
        state = {}
        for frame, vals in EXPRESSIONS.get(name, [(keys[0][0], {})]):
            state.update(vals)
            set_expression(arm, state)
            for kname in FACE_KEYS:
                arm.keyframe_insert(f'["{kname}"]', frame=frame, group="Face")
        slot = arm.animation_data.action_slot if hasattr(arm.animation_data, "action_slot") else None
        _finish_curves(act, slot, name, False)
        apply_overlap(arm, name, keys[0][0], keys[-1][0], loop)
        _finish_curves(act, slot, name, loop)
        act.frame_range = (keys[0][0], keys[-1][0])
        result[name] = (act, keys[-1][0])
        set_pose(arm, {})
    arm.animation_data.action = result["Idle"][0]
    set_expression(arm, {})
    return result


def use_action(arm, act):
    arm.animation_data.action = act
    ad = arm.animation_data
    if hasattr(ad, "action_slot") and ad.action_slot is None and len(act.slots):
        ad.action_slot = act.slots[0]


def render_action_sheet(scene, cam, arm, actions, path, cols=6, cell=320):
    """每個動作一列、平均取 cols 格，拼成一張總覽（側面，面向右）。"""
    import numpy as np
    rows = []
    tmp = os.path.join(os.path.dirname(path), "_frame.png")
    for name, (act, last) in actions.items():
        use_action(arm, act)
        row = []
        for i in range(cols):
            scene.frame_set(round(i * last / cols) if ANIMS[name][0] else round(i * last / (cols - 1)))
            render(scene, cam, tmp, cell, cell)
            img = bpy.data.images.load(tmp)
            a = np.empty(cell * cell * 4, dtype=np.float32)
            img.pixels.foreach_get(a)
            bpy.data.images.remove(img)
            row.append(a.reshape(cell, cell, 4))
        rows.append(np.concatenate(row, axis=1))
    sheet = np.concatenate(rows[::-1], axis=0)    # Blender 影像原點在左下，第一個動作放最上面
    h, w = sheet.shape[:2]
    res = bpy.data.images.new("action_sheet", w, h)
    res.pixels.foreach_set(sheet.ravel())
    res.filepath_raw = path
    res.file_format = "PNG"
    res.save()
    os.remove(tmp)
    scene.frame_set(0)


def bake_vertex_colors(mesh):
    """Unity 用：每個面的材質色寫進頂點色 Col（sRGB），材質合成一個 M_AhJia。
    Unity 端的 AhJiaToon shader 讀頂點色做 3 階明暗與描邊，整隻角色一次 draw call。會改動網格，存完 .blend 才呼叫。"""
    me = mesh.data
    colors = [m.diffuse_color[:] for m in me.materials]  # srgb() 轉過的線性值
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for poly in me.polygons:
        c = colors[poly.material_index]
        for li in poly.loop_indices:
            attr.data[li].color = c
    me.color_attributes.active_color = attr
    me.materials.clear()
    me.materials.append(bpy.data.materials.new("M_AhJia"))
    for poly in me.polygons:
        poly.material_index = 0
    for mod in list(mesh.modifiers):
        if mod.type != "ARMATURE":
            mesh.modifiers.remove(mod)
    if "OutlineWidth" in mesh.vertex_groups:
        mesh.vertex_groups.remove(mesh.vertex_groups["OutlineWidth"])


def export_fbx(path):
    """Unity 用：只帶骨架與網格、全部 Action 各成一段 clip；描邊不在網格上（遊戲裡用 shader 描邊）。"""
    bpy.ops.export_scene.fbx(
        colors_type="SRGB",
        filepath=path, object_types={"ARMATURE", "MESH"}, use_mesh_modifiers=False,
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    )

#endregion


def main():
    global ROOT, COLL
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.abspath(argv[argv.index("--out") + 1]) if "--out" in argv else os.getcwd()
    os.makedirs(out, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = setup_scene()
    COLL = bpy.data.collections.new("AhJia")
    scene.collection.children.link(COLL)
    ROOT = bpy.data.objects.new("AhJia_Root", None)
    COLL.objects.link(ROOT)

    parts = [build_body(), build_shell()]
    parts += [o for o in COLL.objects if o.name == "ShellFill"]
    parts += build_head_parts() + build_hands_shoes() + build_bag()
    parts += build_face(parts[0])

    lines, total = [], 0
    for o in parts:
        n = tri_count(o)
        total += n
        lines.append(f"{o.name:10s} {n:6d}")
    lines.append(f"{'TOTAL':10s} {total:6d} 三角形")
    stats = "\n".join(lines)
    print(stats)
    with open(os.path.join(out, "stats.txt"), "w", encoding="utf-8") as f:
        f.write(stats + "\n")

    arm = build_armature()
    mesh = skin_character(arm, parts)
    add_expression_drivers(arm, mesh)
    print("骨頭：" + ", ".join(b.name for b in arm.data.bones))
    print("頂點群組：" + ", ".join(g.name for g in mesh.vertex_groups))

    mesh.data.materials.append(mat("outline"))
    sol = mesh.modifiers.new("Outline", "SOLIDIFY")   # 排在 Armature 之後，描邊跟著變形
    sol.thickness = 0.012
    sol.offset = 1
    sol.use_flip_normals = True
    sol.use_rim = False
    sol.material_offset = 99
    sol.vertex_group = "OutlineWidth"
    sol.thickness_vertex_group = 0.6
    arm.hide_render = True
    actions = build_actions(arm)
    set_pose(arm, {})
    arm.animation_data.action = None

    # 轉身圖：同一個 collection 用 4 個 instance 排開（原物件先移出場景）
    scene.collection.children.unlink(COLL)
    views = [("front", 0), ("three_quarter", -35), ("side", -90), ("back", 180)]
    insts = []
    for i, (name, rz) in enumerate(views):
        e = bpy.data.objects.new("View_" + name, None)
        e.instance_type = "COLLECTION"
        e.instance_collection = COLL
        e.location = ((i - 1.5) * 1.05, 0, 0)
        e.rotation_euler = (0, 0, math.radians(rz))
        scene.collection.objects.link(e)
        insts.append(e)
    cam = camera(scene, "CamTurn", 4.4, (0, 0, 0.58), elev=6)
    render(scene, cam, os.path.join(out, "AhJia3D_turnaround.png"), 2200, 700)

    set_pose(arm, TEST_POSE)
    render(scene, cam, os.path.join(out, "AhJia3D_pose.png"), 2200, 700)
    set_pose(arm, {})

    for e in [insts[0]] + insts[2:]:
        e.hide_render = True
    insts[1].location = (0, 0, 0)
    insts[1].rotation_euler = (0, 0, math.radians(-30))
    cam2 = camera(scene, "CamHero", 1.45, (0, 0, 0.56), elev=12)
    render(scene, cam2, os.path.join(out, "AhJia3D_hero.png"), 1200, 1200)
    set_pose(arm, TEST_POSE)
    render(scene, cam2, os.path.join(out, "AhJia3D_pose_hero.png"), 1200, 1200)
    set_pose(arm, {})
    render_face_sheet(scene, insts[1], arm, os.path.join(out, "AhJia3D_faces.png"))

    # 線框：加 Wireframe（保留原網格），描邊先關
    sol.show_render = False
    wf = mesh.modifiers.new("Wire", "WIREFRAME")
    wf.thickness = 0.0022
    wf.use_replace = False
    wf.use_even_offset = False
    wf.material_offset = 99
    render(scene, cam2, os.path.join(out, "AhJia3D_wire.png"), 1200, 1200)
    mesh.modifiers.remove(wf)
    sol.show_render = True

    # 動作總覽：側面（面向右）
    insts[1].rotation_euler = (0, 0, math.radians(-90))
    render_action_sheet(scene, cam2, arm, actions, os.path.join(out, "AhJia3D_actions.png"))
    use_action(arm, actions["Idle"][0])

    # 存檔：把模型放回場景、拿掉預覽用的 instance
    for e in insts:
        bpy.data.objects.remove(e)
    scene.collection.children.link(COLL)
    scene.camera = cam2
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "AhJia3D.blend"))
    bake_vertex_colors(mesh)
    export_fbx(os.path.join(out, "AhJia3D.fbx"))
    print("輸出完成：" + out)


main()
