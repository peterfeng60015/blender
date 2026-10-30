"""主角阿甲（犰狳快遞員）的即時 3D 模型試作：程式化建模、卡通著色預覽。

用法（在專案根目錄）：
  blender -b --python Tools/Blender/Hero3D/hero3d.py -- --out <輸出資料夾>
  blender -b --python Tools/Blender/Hero3D/hero3d.py -- --source meshy [--glb <glb>] --out <輸出資料夾>
    改用 Meshy AI 生成的模型（見「Meshy 來源」）：輸出檔名相同，沒有表情（沒有 faces 圖、FBX 沒有 blend shape），
    另外多 AhJia3D_weights(_raw).png（權重）、AhJia3D_actions_front／back.png（3/4 前後的動作總覽）、
    AhJia3D_game_scale.png（160px 遊戲尺寸）。--glb 預設是 Tools/Blender/Hero3D/meshy/AhJia_Meshy.glb；
    貼圖交給同資料夾的 meshy_texture.py，沒有時用暫代版。
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


def align_bone_roll(eb):
    """roll：讓每根骨頭的局部 X 都是世界的左右方向，擺姿勢時「繞 X 轉」一律是前後擺。"""
    d = (eb.tail - eb.head).normalized()
    eb.align_roll(Vector((0, 0, 1)) if abs(d.y) > 0.7 else Vector((0, -1, 0)))


def build_armature(bones=BONES):
    arm_data = bpy.data.armatures.new("AhJia_Rig")
    arm = bpy.data.objects.new("AhJia_Rig", arm_data)
    COLL.objects.link(arm)
    arm.parent = ROOT
    arm_data.display_type = "STICK"
    select_only([arm], arm)
    bpy.ops.object.mode_set(mode="EDIT")
    ebs = {}
    for name, head, tail, parent in bones:
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail = head, tail
        align_bone_roll(eb)
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

    # 描邊粗細用頂點群組控制：身體、殼、帽子 1.0，小配件 0.6，嘴是貼片不描邊 0（合併後還能分粗細，Unity 讀頂點色 A）
    widths = {"Body": 1.0, "Shell": 1.0, "Cap": 1.0, "Mouth": 0.0}
    for o in parts:
        g = o.vertex_groups.new(name="OutlineWidth")
        g.add(list(range(len(o.data.vertices))), widths.get(o.name, 0.6), "REPLACE")

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


def build_actions(arm, face=True):
    """依 ANIMS 建 Action（掛 fake user，FBX 匯出時全部帶出去）。回傳 {名稱: (action, 最後一格)}。
    表情（EXPRESSIONS）key 在骨架物件的自訂屬性上（face=False 時不 key，給沒有表情的模型），二次動畫（OVERLAP）最後疊上去。"""
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
        for frame, vals in EXPRESSIONS.get(name, [(keys[0][0], {})]) if face else []:
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
    if face:
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
    """Unity 用：每個面的材質色寫進頂點色 Col 的 RGB（sRGB，貼圖缺時的後備），A 寫描邊粗細（OutlineWidth 群組），
    材質合成一個 M_AhJia。Unity 端的 AhJiaToon shader 讀 _BaseMap／_MaskMap 與頂點色，整隻角色一次 draw call。
    會改動網格，存完 .blend 才呼叫。"""
    me = mesh.data
    colors = [m.diffuse_color[:] for m in me.materials]  # srgb() 轉過的線性值
    width = [1.0] * len(me.vertices)
    group = mesh.vertex_groups.get("OutlineWidth")
    if group:
        for v in me.vertices:
            for ge in v.groups:
                if ge.group == group.index:
                    width[v.index] = ge.weight
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for poly in me.polygons:
        c = colors[poly.material_index]
        for li in poly.loop_indices:
            attr.data[li].color = (c[0], c[1], c[2], width[me.loops[li].vertex_index])
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
    """Unity 用：只帶骨架與網格、全部 Action 各成一段 clip；描邊不在網格上（遊戲裡用 shader 描邊）。
    UV 層全部帶上（第二層 OutlineNormal＝描邊法線），切線一起匯出。"""
    bpy.ops.export_scene.fbx(
        colors_type="SRGB", use_tspace=True,
        filepath=path, object_types={"ARMATURE", "MESH"}, use_mesh_modifiers=False,
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    )

#endregion


#region UV 與貼圖

# UV0（UVMap）atlas＋兩張 1024² 貼圖。做法：Cycles 把每個 texel 的位置、法線、材質編號、凸度、AO 烘到浮點圖，
# 再用 numpy 依「材質種類＋3D 位置」畫圖案。圖案定在 3D 空間裡，UV 接縫不會斷紋；
# 各零件的位置從該材質的面算 bbox，不寫死頂點數，造型改網格後照樣能跑。
TEX_SIZE = 1024
UV_FACE_BOOST = 1.4           # 頭臉的 UV 島放大倍數（臉、嘴的 texel density 稍高）
TEX_DILATE = 12               # UV 島外擴的像素數（mipmap 不吃到背景色）
UV_ANGLE = 66                 # Smart UV Project 分島角度（之後每島再用 ABF 重展，角度大一點島比較少）
UV_MARGIN = 0.004             # 打包間距（atlas 寬的比例）
# 圖樣對照表：COLORS 鍵 → 圖樣。沒列到的鍵畫「固有色＋通用筆觸」，_MaskMap 用 STYLE_MASK 的預設列。
# 造型改配色、新增零件時只要在這裡補一行（例如新的布料零件指到 "fabric"）。
PAINT_STYLE = {
    "skin": "skin", "belly": "belly", "ear_inner": "gradient", "nose": "nose",
    "shell": "scutes_small", "band": "scutes", "cap": "cap", "cap_logo": "logo",
    "bag": "box_fabric", "bag_flap": "box_fabric", "strap": "fabric", "scarf": "fabric", "lace": "fabric",
    "shorts": "shorts", "shoe": "shoe", "sole": "sole", "buckle": "metal",
    "eye_white": "eye_white", "pupil": "pupil",
}
# 圖樣要參照的其他材質（找位置或借顏色）：角色 → COLORS 鍵；鍵不存在時該細節略過或改用推算的顏色
PAINT_REF = {"nose": "nose", "eye": "eye_white", "cap": "cap", "pink": "ear_inner", "thread": "cap_logo",
             "ink": "band", "sole": "sole"}
# 圖樣 → (_MaskMap R 高光遮罩, B 材質類別：0 皮膚、0.33 殼、0.66 布、1 金屬／眼睛)
STYLE_MASK = {
    "skin": (0.0, 0.0), "belly": (0.0, 0.0), "gradient": (0.0, 0.0), "nose": (0.9, 0.0),
    "scutes": (0.0, 0.33), "scutes_small": (0.0, 0.33), "cap": (0.7, 0.66), "logo": (0.5, 0.66),
    "box_fabric": (0.0, 0.66), "fabric": (0.0, 0.66), "shorts": (0.0, 0.66), "shoe": (0.0, 0.66),
    "sole": (0.0, 0.66), "metal": (1.0, 1.0), "eye_white": (0.6, 1.0), "pupil": (1.0, 1.0),
    None: (0.0, 0.66),
}


def _hash(ix, iy, iz, seed=0):
    """整數格點 → 0～1 的亂數（numpy 向量化）。"""
    h = (ix * 73856093 ^ iy * 19349663 ^ iz * 83492791 ^ seed * 2654435761) & 0xFFFFFFFF
    h = ((h ^ (h >> 16)) * 0x45D9F3B) & 0xFFFFFFFF
    h = ((h ^ (h >> 16)) * 0x45D9F3B) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def _noise(p, scale, seed=0):
    """3D value noise；scale 可以是三軸不同（拉長成筆刷方向）。"""
    import numpy as np
    q = p * np.asarray(scale, dtype=np.float64)
    i = np.floor(q).astype(np.int64)
    f = q - i
    u = f * f * (3 - 2 * f)
    out = np.zeros(len(p))
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = ((u[:, 0] if dx else 1 - u[:, 0]) * (u[:, 1] if dy else 1 - u[:, 1])
                     * (u[:, 2] if dz else 1 - u[:, 2]))
                out += w * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out


def _fbm(p, scale, octaves=2, seed=0):
    import numpy as np
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * _noise(p, np.asarray(scale, dtype=np.float64) * 2 ** o, seed + o)
        norm += amp
        amp *= 0.5
    return total / norm


def _step(e0, e1, x):
    import numpy as np
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _quant(x, levels):
    """0～1 量化成 levels 階（硬色階的筆觸）。"""
    import numpy as np
    return np.floor(np.clip(x, 0.0, 0.999) * levels) / (levels - 1)


def _rgb(key):
    import numpy as np
    h = COLORS[key].lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])


def _lerp(a, b, t):
    import numpy as np
    t = np.asarray(t, dtype=np.float64)
    return a + (b - a) * (t[:, None] if t.ndim == 1 else t)


def _dashes(s, period):
    """沿 s（公尺）每 period 一段的虛線（縫線）。"""
    import numpy as np
    return np.mod(s / period, 1.0) < 0.55


def _slot_keys(me):
    rev = {m.name: k for k, m in MATS.items()}
    return [rev.get(m.name, m.name) if m else "" for m in me.materials]


def _frames(me, keys):
    """每種材質（以及左右各一份）的面 bbox 與最薄的主軸 n（圓片狀零件的法線方向，正負未定）：
    畫腮紅、瞳孔亮點、縫線、帽標時找位置用。"""
    import numpy as np
    n = len(me.polygons)
    cen, area, mi = np.empty(n * 3), np.empty(n), np.empty(n, dtype=np.int32)
    me.polygons.foreach_get("center", cen)
    me.polygons.foreach_get("area", area)
    me.polygons.foreach_get("material_index", mi)
    cen = cen.reshape(-1, 3)

    def frame(s):
        lo, hi = cen[s].min(0), cen[s].max(0)
        w = area[s] / max(1e-12, area[s].sum())
        d = cen[s] - (cen[s] * w[:, None]).sum(0)
        n = np.linalg.eigh((d * w[:, None]).T @ d)[1][:, 0]     # 面中心分佈最薄的方向
        return dict(lo=lo, hi=hi, c=(lo + hi) / 2, half=(hi - lo) / 2, n=n)

    out = {}
    for i, key in enumerate(keys):
        s = mi == i
        if s.any():
            out[key] = frame(s)
        for sx in (1, -1):
            ss = s & (cen[:, 0] * sx > 0)
            if ss.any():
                out[(key, sx)] = frame(ss)
    return out


def _openness(bm):
    """每個面往外（法線半球 5 個方向）射線、0.12 m 內沒打到東西的比例：藏在殼裡、眼白裡的面接近 0。"""
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromBMesh(bm)
    out = {}
    for f in bm.faces:
        n = f.normal
        t = n.orthogonal().normalized()
        b = n.cross(t)
        o = f.calc_center_median() + n * 1e-4
        dirs = [n] + [(n + t * a + b * c).normalized() for a, c in ((1, 0), (-1, 0), (0, 1), (0, -1))]
        out[f] = sum(tree.ray_cast(o, d, 0.12)[0] is None for d in dirs) / len(dirs)
    return out


def _uv_stretch(me):
    """每個三角形 3D→UV 映射的兩個奇異值比（1＝無拉伸）；回傳面積加權的 p50／p95。"""
    import numpy as np
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    li, vi = np.empty(n * 3, dtype=np.int64), np.empty(n * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("loops", li)
    me.loop_triangles.foreach_get("vertices", vi)
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    p, t = co.reshape(-1, 3)[vi].reshape(n, 3, 3), uv.reshape(-1, 2)[li].reshape(n, 3, 2)
    e1, e2 = p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]
    x = e1 / np.maximum(1e-12, np.linalg.norm(e1, axis=1))[:, None]
    y = np.cross(np.cross(e1, e2), e1)
    y /= np.maximum(1e-12, np.linalg.norm(y, axis=1))[:, None]
    a = np.stack([np.stack([(e1 * x).sum(1), (e2 * x).sum(1)], 1), np.stack([(e1 * y).sum(1), (e2 * y).sum(1)], 1)], 1)
    b = np.stack([t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]], 2)
    ok = np.abs(np.linalg.det(a)) > 1e-12
    s = np.linalg.svd(b[ok] @ np.linalg.inv(a[ok]), compute_uv=False)
    ratio = s[:, 0] / np.maximum(1e-12, s[:, 1])
    w = np.abs(np.linalg.det(a[ok]))
    order = np.argsort(ratio)
    cw = np.cumsum(w[order]) / w.sum()
    return ratio[order][np.searchsorted(cw, 0.5)], ratio[order][np.searchsorted(cw, 0.95)]


def unwrap_atlas(mesh):
    """Smart UV Project 分島 → 依島重新 ABF 展開（降低拉伸）→ 島統一密度 → 頭臉的島放大、
    藏起來的島（殼內、眼白內）縮小 → 打包進一張 UV0（UVMap）。"""
    from bpy_extras.bmesh_utils import bmesh_linked_uv_islands
    me = mesh.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    if me.uv_layers[0] != uv:
        print("警告：UVMap 不是第一個 UV 層，Unity 的 UV0 會讀到別層")
    me.uv_layers.active = uv
    uv.active_render = True
    keys = _slot_keys(me)
    head = next((b[1] for b in BONES if b[0] == "Head"), None)    # 頭骨根部往下一點以上算頭臉（沒有 Head 骨就不放大）
    head_z = head[2] - 0.06 if head else math.inf

    bpy.context.scene.tool_settings.use_uv_select_sync = True
    select_only([mesh], mesh)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(UV_ANGLE), island_margin=0.0, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.seams_from_islands(mark_seams=True)
    bpy.ops.uv.unwrap(method="ANGLE_BASED", fill_holes=True, correct_aspect=True, margin=0.0)
    bpy.ops.uv.average_islands_scale()
    bm = bmesh.from_edit_mesh(me)
    layer = bm.loops.layers.uv.active
    vis = _openness(bm)
    islands = bmesh_linked_uv_islands(bm, layer)
    for island in islands:
        areas = [(f.calc_area(), f.calc_center_median().z > head_z
                  and PAINT_STYLE.get(keys[f.material_index]) not in ("cap", "logo"), vis[f]) for f in island]
        total = max(1e-12, sum(a for a, _, _ in areas))
        head = sum(a for a, h, _ in areas if h) / total
        seen = sum(a * o for a, _, o in areas) / total
        k = (UV_FACE_BOOST if head > 0.5 else 1.0) * (0.3 + 0.7 * min(1.0, seen / 0.3))
        if abs(k - 1.0) < 1e-3:
            continue
        loops = [l for f in island for l in f.loops]
        c = sum((l[layer].uv for l in loops), Vector((0, 0))) / len(loops)
        for l in loops:
            l[layer].uv = c + (l[layer].uv - c) * k
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.pack_islands(rotate=True, scale=True, margin_method="FRACTION", margin=UV_MARGIN,
                            shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    p50, p95 = _uv_stretch(me)
    print(f"UV：{len(islands)} 個島，拉伸比（面積加權）p50 {p50:.2f}、p95 {p95:.2f}")


def _bake_buffers(mesh):
    """Cycles 烘三張浮點圖：位置、物件空間法線、(材質編號＋1, 凸度, AO)。回傳 {種類: (S*S, 4) 陣列}。"""
    import numpy as np
    scene = bpy.context.scene
    me = mesh.data
    old_engine, old_mats = scene.render.engine, list(me.materials)
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.use_adaptive_sampling = False
    img = bpy.data.images.new("_TexBake", TEX_SIZE, TEX_SIZE, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    temp = []
    for i in range(len(old_mats)):
        m = bpy.data.materials.new(f"_TexBake{i}")
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
        ao.samples = 16
        ao.inputs["Distance"].default_value = 0.06
        comb = nt.nodes.new("ShaderNodeCombineXYZ")
        comb.inputs[0].default_value = i + 1
        emit = nt.nodes.new("ShaderNodeEmission")
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(geo.outputs["Pointiness"], comb.inputs[1])
        nt.links.new(ao.outputs["AO"], comb.inputs[2])
        nt.links.new(comb.outputs[0], emit.inputs["Color"])
        nt.links.new(emit.outputs[0], out.inputs["Surface"])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        nt.nodes.active = tex
        me.materials[i] = m
        temp.append(m)
    select_only([mesh], mesh)
    bufs = {}
    for kind, samples in (("POSITION", 1), ("NORMAL", 1), ("EMIT", 32)):
        scene.cycles.samples = samples
        bpy.ops.object.bake(type=kind, normal_space="OBJECT", margin=0, use_clear=True)
        a = np.empty(TEX_SIZE * TEX_SIZE * 4, dtype=np.float32)
        img.pixels.foreach_get(a)
        bufs[kind] = a.reshape(-1, 4).astype(np.float64)
    for i, m in enumerate(old_mats):
        me.materials[i] = m
    for m in temp:
        bpy.data.materials.remove(m)
    bpy.data.images.remove(img)
    scene.render.engine = old_engine
    return bufs


# 各圖樣的畫法：(位置 P, 法線 N, 凸度 pt, 材質 bbox F, 固有色 base, 材質鍵 key, 圖樣 style) → (顏色, 墨線 0～1)
# 墨線會讓 _MaskMap G 變低（甲片縫、帽縫在 shader 裡比較早進陰影，像手繪的陰影線）


def _ref_rgb(role, fallback):
    import numpy as np
    key = PAINT_REF.get(role)
    return _rgb(key) if key in COLORS else np.asarray(fallback, dtype=np.float64)


def _ref_frame(F, role, sx=None):
    key = PAINT_REF.get(role)
    return F.get(key if sx is None else (key, sx))


def _paint_skin(P, N, pt, F, base, key, style):
    import numpy as np
    col = np.tile(base, (len(P), 1))
    pink = _ref_rgb("pink", (0.89, 0.60, 0.59))
    nose = _ref_frame(F, "nose")
    if nose is None:
        return col, np.zeros(len(P))
    # 嘴尖越往前越粉、越深（量化成 3 階）
    d = np.linalg.norm(P - nose["c"], axis=1) + (_fbm(P, 70, 2, 3) - 0.5) * 0.03
    col = _lerp(col, pink * 0.92, 0.55 * _quant(1 - _step(0.03, 0.17, d), 3))
    for sx in (1, -1):                         # 腮紅：眼睛和鼻子之間、偏下偏外的一塊硬邊色塊
        eye = _ref_frame(F, "eye", sx)
        if eye is None:
            continue
        cheek = eye["c"] + (nose["c"] - eye["c"]) * 0.32 + np.array([0.022 * sx, 0.0, -0.05])
        d = np.linalg.norm(P - cheek, axis=1) + (_fbm(P, 90, 2, 5) - 0.5) * 0.012
        col = _lerp(col, pink, 0.6 * (d < 0.032) + 0.25 * ((d >= 0.032) & (d < 0.042)))
    return col, np.zeros(len(P))


def _paint_belly(P, N, pt, F, base, key, style):
    import numpy as np
    col = np.tile(base, (len(P), 1))
    col[(np.abs(P[:, 0]) < 0.05 + (_fbm(P, 30, 2, 13) - 0.5) * 0.04) & (N[:, 1] < -0.5)] *= 1.03   # 肚子中線亮一階
    w = P[:, 2] / 0.05 + (_fbm(P, 40, 2, 11) - 0.5) * 0.8    # 肚皮的橫向皺褶線（淡）
    fold = (np.mod(w, 1.0) < 0.07) & (N[:, 1] < -0.5)
    col[fold] *= 0.96
    return col, fold * 0.4


def _paint_gradient(P, N, pt, F, base, key, style):
    """由下往上變亮的 3 階漸層（內耳）。"""
    import numpy as np
    f = F[key]
    t = _quant(_step(f["lo"][2], f["hi"][2], P[:, 2]), 3)
    return _lerp(np.tile(base * 0.86, (len(P), 1)), base * 1.06, t), np.zeros(len(P))


def _shell_frame(F):
    """背殼橢球的中心與半徑：優先用參數區的 SHELL_C／SHELL_R，沒有就用殼紋材質的 bbox 推（殼的下緣當中心高度）。"""
    import numpy as np
    c, r = globals().get("SHELL_C"), globals().get("SHELL_R")
    if c is not None and r is not None:
        return np.array(c, dtype=np.float64), np.array(r, dtype=np.float64)
    fs = [F[k] for k, s in PAINT_STYLE.items() if s in ("scutes", "scutes_small") and k in F]
    lo, hi = np.min([f["lo"] for f in fs], axis=0), np.max([f["hi"] for f in fs], axis=0)
    return np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]]), np.maximum(
        np.array([(hi[0] - lo[0]) / 2, (hi[1] - lo[1]) / 2, hi[2] - lo[2]]), 1e-3)


def _paint_scutes(P, N, pt, F, base, key, style):
    """甲片：繞背殼中軸的角度 × 前後位置排成交錯的磚格；前緣磨亮、後緣壓暗、甲片縫畫墨線。
    scutes（活動殼帶）一條一排長甲片，列沿 y 排（y＝-0.05 起每 0.055 一列，對齊現行殼帶）；
    scutes_small（肩甲／臀甲／尾巴）是小鱗片，列沿橢球的極角弧長排（前後兩片的表面幾乎垂直 y 軸，直接用 y 會擠成很密的環）。"""
    import numpy as np
    sc, sr = _shell_frame(F)
    warp = (_fbm(P, 55, 2, 21) - 0.5) * 0.007
    q = (P - sc) / sr
    az = np.abs(np.arctan2(q[:, 0], q[:, 2]))
    if style == "scutes_small":
        theta = np.arccos(np.clip(-q[:, 1] / np.maximum(1e-6, np.linalg.norm(q, axis=1)), -1, 1))
        u = az * sr[0] * np.maximum(0.15, np.sin(theta)) + warp
        rowh, bw = 0.038, 0.038
        v = (theta * sr[1] + warp) / rowh
    else:
        u = az * sr[0] + warp
        rowh, bw = 0.055, 0.036
        v = (P[:, 1] + 0.05 + warp) / rowh
    row = np.floor(v)
    fy = v - row
    uu = u / bw + 0.5 * np.mod(row, 2)
    colm = np.floor(uu)
    fx = uu - colm
    edge = np.minimum(np.minimum(fx, 1 - fx) * bw, np.minimum(fy, 1 - fy) * rowh)
    tone = 0.93 + 0.07 * _quant(_hash(row.astype(np.int64), colm.astype(np.int64), 0, 7), 3)
    col = np.tile(base, (len(P), 1)) * tone[:, None]
    light = base + (_ref_rgb("thread", (0.96, 0.92, 0.87)) - base) * 0.42
    col = _lerp(col, light, 0.5 * (fy < 0.14))                # 前緣磨損亮線
    col[fy > 0.84] *= 0.88                                     # 後緣壓暗（下一片蓋過來的陰影）
    wear = ((fy < 0.32) & (_noise(P, (150, 150, 60), 23) > 0.64)) | (pt > 0.6)
    col = _lerp(col, light, 0.5 * wear)                        # 殼帶邊緣、下緣的磨損亮點
    inner = (N * q).sum(1) < -0.2                              # 殼的內側：UV 被縮小，只畫素色暗面
    ink = (edge < 0.0024) & ~inner
    col[ink] = _ref_rgb("ink", base * 0.75) * 0.62
    col[inner] = base * 0.72
    return col, ink * 1.0


def _paint_cap(P, N, pt, F, base, key, style):
    """帽頂 6 片縫線＋兩側明線、頂扣；帽簷同心車縫；頂部一塊亮色。"""
    import numpy as np
    f = F[key]
    top = f["hi"][2]
    dome = P[:, 2] > f["lo"][2] + 0.4 * (top - f["lo"][2])
    crown = np.array([f["c"][0], f["c"][1] + 0.35 * f["half"][1], top])     # 帽頂中心（帽簷往前伸，中心偏後）
    rel = P - crown
    r = np.hypot(rel[:, 0], rel[:, 1])
    th = np.arctan2(rel[:, 0], rel[:, 1])
    seam_d = np.abs(np.arcsin(np.clip(np.sin(3 * th), -1, 1))) / 3 * r      # 到最近一條縫的弧長
    brim = (np.abs(N[:, 2]) > 0.9) & (rel[:, 1] < -0.07)
    col = np.tile(base, (len(P), 1))
    hi = _fbm(P, 50, 2, 31) * 0.1 + (N @ np.array([0.0, -0.45, 0.9]))
    col = _lerp(col, np.minimum(base * 1.25, 1), 0.6 * ((hi > 0.95) & dome))
    seam = dome & ~brim & (seam_d < 0.002)
    stitch = dome & ~brim & (np.abs(seam_d - 0.0055) < 0.0013) & _dashes(r, 0.007)
    button = r < 0.011
    rows = brim & (np.mod(r / 0.011, 1.0) < 0.22) & _dashes(th * r, 0.007)
    col[brim & (N[:, 2] < 0)] *= 0.78
    col[seam | button] = base * 0.62
    col[stitch | rows] = base + (_ref_rgb("thread", (0.96, 0.92, 0.87)) - base) * 0.65
    return col, (seam | button) * 1.0


def _paint_logo(P, N, pt, F, base, key, style):
    """圓標：外圈＋信封 V 字（墨色用帽子的顏色）。"""
    import numpy as np
    f, cap = F[key], _ref_frame(F, "cap")
    col = np.tile(base, (len(P), 1))
    if cap is None:
        return col, np.zeros(len(P))
    rel = P - f["c"]
    n = f["n"] if f["n"] @ (f["c"] - cap["c"]) > 0 else -f["n"]    # 帽標朝外
    up = np.array([0.0, 0.0, 1.0]) - n * n[2]
    up /= max(1e-9, np.linalg.norm(up))
    side = np.cross(up, n)
    u, w = rel @ side, rel @ up
    rad = max(f["half"][0], 1e-4)
    d = np.hypot(u, w) / rad
    vee = (np.abs(w - (0.25 * rad - 0.75 * np.abs(u))) < 0.12 * rad) & (np.abs(u) < 0.5 * rad)
    box = (np.maximum(np.abs(u) / 0.55, np.abs(w + 0.05 * rad) / 0.38) / rad)
    ink = ((d > 0.72) & (d < 0.9)) | vee | (np.abs(box - 1) < 0.12)
    col[ink] = _ref_rgb("cap", base * 0.5) * 0.9
    return col, np.zeros(len(P))


def _box_seam(P, N, f, inset):
    """方盒狀零件（包身、包蓋）：每個面離邊 inset 的一圈車縫虛線。"""
    import numpy as np
    rel = np.abs(P - f["c"])
    half = np.maximum(f["half"], 1e-4)
    axis = np.argmax(np.abs(N), axis=1)
    d = np.full(len(P), 1.0)
    s = np.zeros(len(P))
    for a in range(3):
        d = np.where(axis != a, np.minimum(d, half[a] - rel[:, a]), d)
        s = s + np.where(axis != a, P[:, a], 0)
    return (np.abs(d - inset) < 0.0016) & _dashes(s, 0.006)


def _paint_fabric(P, N, pt, F, base, key, style):
    """布料：拉長的細紋（量化 3 階）；box_fabric 加一圈車縫，shorts 加腰頭、褲口車縫與前襠縫。"""
    import numpy as np
    col = np.tile(base, (len(P), 1))
    grain = _quant(_noise(P, (260, 260, 30), 41), 3)
    col *= (0.96 + 0.05 * grain)[:, None]
    ink = np.zeros(len(P), dtype=bool)
    thread = base + (_ref_rgb("thread", (0.96, 0.92, 0.87)) - base) * 0.55
    f = F[key]
    if style == "box_fabric":
        col[_box_seam(P, N, f, 0.007)] = thread
    elif style == "shorts":
        waist = P[:, 2] > f["hi"][2] - 0.022
        col[waist] *= 0.85
        ink = np.abs(P[:, 2] - (f["hi"][2] - 0.022)) < 0.0015
        hem = np.abs(P[:, 2] - (f["lo"][2] + 0.016)) < 0.0013
        front = (P[:, 1] < f["c"][1]) & (P[:, 2] > f["c"][2])
        fly = front & (np.abs(np.abs(P[:, 0] - f["c"][0]) - 0.008) < 0.0013)
        col[(hem | fly) & _dashes(P[:, 0] + P[:, 2], 0.007)] = thread
        ink |= front & (np.abs(P[:, 0] - f["c"][0]) < 0.0018)
        col[ink] = base * 0.7
    else:
        col[_noise(P, (500, 500, 500), 43) > 0.62] *= 0.92
    return col, ink * 1.0


def _paint_shoe(P, N, pt, F, base, key, style):
    """皮鞋：鞋頭包片＋車縫、鞋帶、底部一圈鞋底色（分色）。"""
    import numpy as np
    col = np.tile(base, (len(P), 1))
    lace = _ref_rgb("thread", (0.96, 0.92, 0.87)) * 0.92
    sole = _ref_rgb("sole", base * 0.6) * 1.25
    ink = np.zeros(len(P), dtype=bool)
    for sx in (1, -1):
        f = F.get((key, sx))
        if not f:
            continue
        s = P[:, 0] * sx > 0
        rel = P - f["c"]
        toe_y = -0.45 * f["half"][1]
        col[s & (rel[:, 1] < toe_y)] *= 1.12
        ink |= s & (np.abs(rel[:, 1] - toe_y) < 0.0016)
        col[s & (np.abs(rel[:, 1] - toe_y - 0.006) < 0.0012) & _dashes(P[:, 0], 0.006)] = lace * 0.8
        tongue = s & (np.abs(rel[:, 0]) < 0.022) & (rel[:, 2] > 0.35 * f["half"][2]) \
            & (rel[:, 1] > toe_y) & (rel[:, 1] < 0.25 * f["half"][1])
        col[tongue & (np.mod((rel[:, 1] - toe_y) / 0.016, 1.0) < 0.38)] = lace
        col[s & (P[:, 2] < f["lo"][2] + 0.016)] = sole
    col[ink] = base * 0.6
    return col, ink * 1.0


def _paint_small(P, N, pt, F, base, key, style):
    """小件：鞋底紋、鼻孔、眼白下緣、瞳孔亮點、金屬色階。"""
    import numpy as np
    col = np.tile(base, (len(P), 1))
    ink = np.zeros(len(P), dtype=bool)
    if style == "sole":
        col[np.mod(P[:, 1] / 0.012, 1.0) < 0.3] *= 0.8
    elif style == "nose":
        f = F[key]
        col[N[:, 2] > 0.55] *= 1.18
        for sx in (1, -1):
            hole = f["c"] + np.array([0.011 * sx, -0.8 * f["half"][1], -0.004])
            ink |= np.linalg.norm(P - hole, axis=1) < 0.0055
        col[ink] = base * 0.45
    elif style == "eye_white":
        for sx in (1, -1):
            f = F.get((key, sx))
            if f:
                low = (P[:, 0] * sx > 0) & (P[:, 2] < f["c"][2] - 0.35 * f["half"][2])
                col[low] = base * np.array([0.88, 0.9, 0.95])
    elif style == "pupil":
        for sx in (1, -1):                     # 亮點：眼白中心 → 瞳孔中心的方向就是瞳孔朝外的方向
            f, e = F.get((key, sx)), _ref_frame(F, "eye", sx)
            if f and e:
                r = max(f["half"])
                out = (f["c"] - e["c"]) / max(1e-9, np.linalg.norm(f["c"] - e["c"]))
                for off, size in ((0.45, 0.36), (-0.35, 0.16)):
                    p0 = f["c"] + out * r * 0.9 + np.array([0.0, 0.0, off * r])
                    col[(P[:, 0] * sx > 0) & (np.linalg.norm(P - p0, axis=1) < size * r)] = 1.0
    elif style == "metal":
        lit = N @ np.array([-0.3, -0.45, 0.84])
        col = _lerp(np.tile(base * 0.72, (len(P), 1)), np.minimum(base * 1.3, 1), _quant(lit * 0.5 + 0.5, 3))
    return col, ink * 1.0


PAINTERS = {"skin": _paint_skin, "belly": _paint_belly, "gradient": _paint_gradient, "scutes": _paint_scutes,
            "scutes_small": _paint_scutes, "cap": _paint_cap, "logo": _paint_logo, "box_fabric": _paint_fabric,
            "fabric": _paint_fabric, "shorts": _paint_fabric, "shoe": _paint_shoe}


def _paint_textures(keys, F, bufs):
    """回傳 (base (S,S,3) sRGB, mask (S,S,3) linear)。"""
    import numpy as np
    S = TEX_SIZE
    emit = bufs["EMIT"]
    mid = np.rint(emit[:, 0]).astype(np.int64)
    cov = mid > 0
    P_all = bufs["POSITION"][:, :3]
    N_all = bufs["NORMAL"][:, :3] * 2 - 1
    N_all /= np.maximum(1e-6, np.linalg.norm(N_all, axis=1))[:, None]
    base = np.zeros((S * S, 3))
    mask = np.zeros((S * S, 3))
    for i, key in enumerate(keys):
        sel = mid == i + 1
        if not sel.any():
            continue
        P, N, pt, ao = P_all[sel], N_all[sel], emit[sel, 1], emit[sel, 2]
        style = PAINT_STYLE.get(key)
        if key in COLORS:
            c0 = _rgb(key)
        else:
            print(f"警告：材質 {key} 不在 COLORS，貼圖先塗灰色")
            c0, style = np.array([0.5, 0.5, 0.5]), None
        if style not in PAINTERS and style not in ("sole", "nose", "eye_white", "pupil", "metal"):
            style = None                       # 沒對應的圖樣：固有色＋通用筆觸
        try:
            col, ink = (PAINTERS.get(style, _paint_small)(P, N, pt, F, c0, key, style) if style
                        else (np.tile(c0, (len(P), 1)), np.zeros(len(P))))
        except Exception as e:                 # 造型改了零件、找不到該有的位置：退回固有色，不讓整支腳本掛掉
            print(f"警告：材質 {key} 的圖樣 {style} 畫不出來（{e!r}），改用固有色")
            col, ink = np.tile(c0, (len(P), 1)), np.zeros(len(P))
        if style not in ("pupil", "eye_white", "metal"):
            # 共通筆觸：拉長噪聲量化成 3 階的明度斑塊、朝光的面提亮一階、凹處（AO 低）壓暗
            stroke = _quant((_fbm(P, (30, 30, 10), 2, 51) - 0.5) * 3 + 0.5, 3)
            lit = N @ np.array([-0.35, -0.45, 0.82])
            col = col * (0.97 + 0.06 * stroke)[:, None]
            col[lit > 0.8] *= 1.06
            col *= (1 - 0.12 * _step(0.6, 0.4, ao))[:, None]
        spec, cls = STYLE_MASK.get(style, STYLE_MASK[None])
        base[sel] = np.clip(col, 0, 1)
        mask[sel, 0] = spec
        mask[sel, 1] = np.clip(0.5 - 0.35 * _step(0.85, 0.35, ao) - 0.15 * ink, 0, 1)
        mask[sel, 2] = cls
    print(f"貼圖：UV 島覆蓋率 {100 * cov.mean():.1f}%")

    # UV 島往外擴，避免 mipmap／雙線性取樣吃到島外的黑色
    img = np.concatenate([base, mask], axis=1).reshape(S, S, 6)
    filled = cov.reshape(S, S).copy()
    for _ in range(TEX_DILATE):
        acc = np.zeros_like(img)
        cnt = np.zeros((S, S))
        for sh in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(img * filled[..., None], sh, (0, 1))
            cnt += np.roll(filled, sh, (0, 1))
        new = ~filled & (cnt > 0)
        img[new] = acc[new] / cnt[new, None]
        filled |= new
    return img[..., :3], img[..., 3:]


def _save_png(name, rgb, path, colorspace):
    import numpy as np
    S = rgb.shape[0]
    img = bpy.data.images.new(name, S, S, alpha=False)
    img.colorspace_settings.name = colorspace
    px = np.ones((S, S, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px.ravel())     # byte 圖的 pixels 直接對應 0～255，不做色彩轉換
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def use_basemap(mesh, img):
    """預覽用：toon／flat 材質的固有色改成取樣 _BaseMap（toon 的硬色階不變）。"""
    for m in mesh.data.materials:
        if m is None or m is MATS.get("outline"):
            continue
        nt = m.node_tree
        uvn = nt.nodes.new("ShaderNodeUVMap")
        uvn.uv_map = "UVMap"
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        nt.links.new(uvn.outputs[0], tex.inputs["Vector"])
        mul = next((n for n in nt.nodes if n.bl_idname == "ShaderNodeVectorMath" and n.operation == "MULTIPLY"), None)
        if mul:
            nt.links.new(tex.outputs["Color"], mul.inputs[1])
        else:
            emit = next(n for n in nt.nodes if n.bl_idname == "ShaderNodeEmission")
            nt.links.new(tex.outputs["Color"], emit.inputs["Color"])


def build_textures(mesh, out):
    """UV0 展開打包、烘出 _BaseMap／_MaskMap 存到 out，預覽材質改吃 _BaseMap。合併網格後、加描邊前呼叫（靜止姿勢）。"""
    import time
    t0 = time.time()
    unwrap_atlas(mesh)
    t1 = time.time()
    keys = _slot_keys(mesh.data)
    bufs = _bake_buffers(mesh)
    t2 = time.time()
    base, mask = _paint_textures(keys, _frames(mesh.data, keys), bufs)
    img = _save_png("AhJia3D_BaseMap", base, os.path.join(out, "AhJia3D_BaseMap.png"), "sRGB")
    mask_img = _save_png("AhJia3D_MaskMap", mask, os.path.join(out, "AhJia3D_MaskMap.png"), "Non-Color")
    bpy.data.images.remove(mask_img)
    use_basemap(mesh, img)
    print(f"貼圖：UV {t1 - t0:.1f}s、烘焙 {t2 - t1:.1f}s、繪製 {time.time() - t2:.1f}s")

#endregion

#region 描邊法線


def bake_outline_normals(mesh):
    """描邊用的平滑法線：位置相同的頂點（硬邊兩側、零件接縫）算成一組，把周圍面法線依角度加權平均，
    轉成每個角的切線空間 xy 寫進第二個 UV 層 OutlineNormal（Unity 的 TEXCOORD1，z 由 shader 重建）。
    倒殼沿這個方向擠出，尖角與硬邊不會裂開。切線照第一個 UV 層算（MikkTSpace，和 Unity 匯入時算的一致），
    網格還沒有 UV 就先依面朝向做方盒投影補上。會改動網格，存完 .blend 才呼叫。"""
    from mathutils.kdtree import KDTree

    me = mesh.data
    ngons = [p.index for p in me.polygons if p.loop_total > 4]
    if ngons:  # MikkTSpace 只吃三角形與四邊形
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        bmesh.ops.triangulate(bm, faces=[bm.faces[i] for i in ngons])
        bm.to_mesh(me)
        bm.free()
    # 零件建模時順手產生的 UVMap 大多是 0（沒有面積，切線會亂掉），這種當成沒有 UV
    if not me.uv_layers or sum(d.uv.length_squared > 0 for d in me.uv_layers[0].data) < len(me.loops) // 2:
        uv = me.uv_layers[0] if me.uv_layers else me.uv_layers.new(name="UVMap")
        for p in me.polygons:
            ax = max(range(3), key=lambda k: abs(p.normal[k]))
            u, v = [k for k in range(3) if k != ax]
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                uv.data[li].uv = (co[u], co[v])
    if len(me.uv_layers) != 1:
        raise RuntimeError("描邊法線要放第二個 UV 層，但網格已有 %d 層 UV" % len(me.uv_layers))

    # 位置相同（1e-5 公尺內）的頂點併成一組（union-find，鏈狀相鄰也會併在一起）
    verts = me.vertices
    kd = KDTree(len(verts))
    for v in verts:
        kd.insert(v.co, v.index)
    kd.balance()
    parent = list(range(len(verts)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for v in verts:
        for _, i, _ in kd.find_range(v.co, 1e-5):
            parent[root(i)] = root(v.index)
    group = [root(i) for i in range(len(verts))]

    acc = {}
    for p in me.polygons:
        cos = [verts[i].co for i in p.vertices]
        n = len(cos)
        for j, vi in enumerate(p.vertices):
            a, b = cos[j - 1] - cos[j], cos[(j + 1) % n] - cos[j]
            if a.length_squared > 1e-14 and b.length_squared > 1e-14:
                g = group[vi]
                acc[g] = acc.get(g, Vector()) + p.normal * a.angle(b)

    me.calc_tangents(uvmap=me.uv_layers[0].name)
    out = me.uv_layers.new(name="OutlineNormal")
    for lp in me.loops:
        s = acc.get(group[lp.vertex_index])
        s = s.normalized() if s is not None and s.length > 1e-6 else lp.normal
        out.data[lp.index].uv = (s.dot(lp.tangent), s.dot(lp.bitangent))
    me.free_tangents()

#endregion

#region Meshy 來源

# --source meshy：改用 Meshy AI 生成的模型（glb：單一網格＋Base Color、金屬／粗糙、法線三張貼圖），
# 接上同一套骨架（BONES 的名稱與階層）與 6 個動作。流程：匯入 → 焊接 → 正規化 → 減面（保留 Meshy UV）
# → 依 Meshy 原姿勢擺骨頭 → 熱擴散權重 → 拆開郵差包 → 部位規則 → 四肢擺回左右對稱、套成新的靜止姿勢 → 貼圖。
# 臉畫在貼圖上，沒有表情 shape key；動作也不 key 表情。

MESHY_GLB = os.path.join(os.path.dirname(os.path.abspath(__file__)), "meshy", "AhJia_Meshy.glb")   # --glb 的預設值（Git LFS）
MESHY_CAP_TOP = 1.041   # 現版程式化模型的帽頂高度（公尺，不含耳朵）；Meshy 模型的最高點是帽頂，等比縮放到這個高度
MESHY_TRIS = 20000      # 減面上限（三角形）
MESHY_FILL_TRIS = 300   # 其中留給拆開郵差包後補開口的三角形
MESHY_FILL_UV = 0.001   # 補片 UV 的寬度（貼圖寬的比例，2048 貼圖約 2 texel）

# 骨頭在 Meshy 原姿勢上的位置，單位是帽頂高（×MESHY_CAP_TOP 換成公尺）；名稱、父骨照 BONES。
# Meshy 原姿勢左手往後、右手往前擺，右腳在前，耳朵一前一後；長嘴往下斜、嘴尖略偏右，尾巴往右後方甩。
MESHY_BONES = {
    "Hips": ((0, 0.02, 0.33), (0, 0.02, 0.45)),
    "Spine": ((0, 0.02, 0.45), (0, 0.0, 0.58)),
    "Chest": ((0, 0.0, 0.58), (0, -0.02, 0.70)),
    "Neck": ((0, -0.02, 0.70), (0, -0.03, 0.78)),
    "Head": ((0, -0.03, 0.78), (0, -0.03, 0.97)),
    "Snout": ((-0.015, -0.07, 0.83), (-0.042, -0.19, 0.75)),
    "Tail1": ((0, 0.06, 0.32), (-0.04, 0.13, 0.27)),
    "Tail2": ((-0.04, 0.13, 0.27), (-0.09, 0.175, 0.24)),
    "Tail3": ((-0.09, 0.175, 0.24), (-0.135, 0.228, 0.218)),
    "Bag": ((0.095, -0.15, 0.54), (0.095, -0.16, 0.38)),
    "LeftUpperLeg": ((0.065, 0.02, 0.33), (0.06, 0.05, 0.20)),
    "LeftLowerLeg": ((0.06, 0.05, 0.20), (0.075, 0.065, 0.09)),
    "LeftFoot": ((0.075, 0.065, 0.09), (0.10, -0.04, 0.03)),
    "LeftShoulder": ((0.04, -0.01, 0.65), (0.13, 0.0, 0.65)),
    "LeftUpperArm": ((0.13, 0.0, 0.65), (0.195, 0.07, 0.50)),
    "LeftLowerArm": ((0.195, 0.07, 0.50), (0.245, 0.13, 0.415)),
    "LeftHand": ((0.245, 0.13, 0.415), (0.275, 0.19, 0.355)),
    "LeftEar": ((0.06, -0.02, 0.90), (0.125, -0.098, 0.946)),
    "LeftHandProp": ((0.26, 0.16, 0.385), (0.26, 0.06, 0.385)),
    "RightUpperLeg": ((-0.065, 0.0, 0.33), (-0.08, -0.05, 0.20)),
    "RightLowerLeg": ((-0.08, -0.05, 0.20), (-0.09, -0.08, 0.09)),
    "RightFoot": ((-0.09, -0.08, 0.09), (-0.085, -0.19, 0.03)),
    "RightShoulder": ((-0.04, -0.01, 0.65), (-0.13, -0.01, 0.65)),
    "RightUpperArm": ((-0.13, -0.01, 0.65), (-0.20, -0.08, 0.51)),
    "RightLowerArm": ((-0.20, -0.08, 0.51), (-0.25, -0.16, 0.43)),
    "RightHand": ((-0.25, -0.16, 0.43), (-0.285, -0.215, 0.36)),
    "RightEar": ((-0.06, 0.01, 0.90), (-0.144, 0.066, 0.958)),
    "RightHandProp": ((-0.27, -0.19, 0.395), (-0.27, -0.29, 0.395)),
}

# 擺回對稱站姿：這些骨頭左右成對，兩邊改成同一個方向（x 取絕對值平均、y／z 平均，再左右鏡射）
MESHY_SYMMETRIC = ["UpperLeg", "LowerLeg", "Foot", "UpperArm", "LowerArm", "Hand", "HandProp", "Ear"]
# 這些中線骨轉回 x＝0 的平面（長嘴偏右、尾巴往右甩）：roll 規則才會給出和程式化版一樣的局部 X（左右方向）
MESHY_MIDLINE = ["Snout", "Tail1", "Tail2", "Tail3"]

# 部位規則（座標單位同 MESHY_BONES；顏色取 Base Color 貼圖在頂點 UV 的 HSV，色相單位度）
MESHY_BAG_BOX = ((0.005, 0.20), (-0.26, -0.06), (0.36, 0.60))   # 郵差包的範圍 (x, y, z)，掛在左前腹；上緣切在背帶環上方的平直段（切在環上會切出夾點與非流形邊）
MESHY_HEAD_Z = (0.85, 0.89)                                      # 這以上（帽子、頭頂）全給 Head，中間漸變
MESHY_EAR = ((0.065, 0.09), (0.875, 0.895))                      # 耳朵：|x| 與 z 各自從前值漸變到後值
MESHY_SHOE_Z = (0.09, 0.13)                                      # 鞋：這以下全給 Foot，往上漸變到 LowerLeg
MESHY_LOOSE = 0.05                                               # 拆包後包圍盒對角線比這短（公尺）的鬆散碎塊刪掉
MESHY_SOLE_Z = 0.12                                             # 量鞋子長軸、鞋底高度用的頂點（這以下）
MESHY_ARM_R = (0.065, 0.085)                                     # 上臂骨的影響範圍：離骨頭這麼遠之間漸變成軀幹


def smoothstep_np(e0, e1, x):
    import numpy as np
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def meshy_bones(mesh):
    """BONES 換成 Meshy 的位置（名稱、父骨、順序不變）。Foot 的方向改成量網格上那隻鞋的長軸（左鞋本身往外撇，
    目測擺的骨頭和鞋不重合，擺回對稱後兩隻鞋會一正一歪）；水平長度、鞋尖高度照 MESHY_BONES。"""
    import numpy as np
    co = np.empty(len(mesh.data.vertices) * 3, dtype=np.float32)
    mesh.data.vertices.foreach_get("co", co)
    p = co.reshape(-1, 3) / MESHY_CAP_TOP
    ends = dict(MESHY_BONES)
    for sd, sx in (("Left", 1), ("Right", -1)):
        xy = p[(p[:, 2] < MESHY_SOLE_Z) & (p[:, 0] * sx > 0), :2]
        axis = np.linalg.eigh(np.cov((xy - xy.mean(axis=0)).T))[1][:, -1]
        axis = -axis if axis[1] > 0 else axis          # 鞋尖朝前（-Y）
        head, tail = ends[sd + "Foot"]
        reach = math.hypot(tail[0] - head[0], tail[1] - head[1])
        ends[sd + "Foot"] = (head, (head[0] + axis[0] * reach, head[1] + axis[1] * reach, tail[2]))
    s = MESHY_CAP_TOP
    return [(name, tuple(c * s for c in ends[name][0]), tuple(c * s for c in ends[name][1]), parent)
            for name, _, _, parent in BONES]


def _meshy_images(material):
    """glb 材質上的三張貼圖：Base Color、金屬／粗糙（經 Separate Color 接 Metallic／Roughness）、法線（經 Normal Map）。"""
    bsdf = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")

    def source(*sockets):
        for sock in sockets:
            for link in bsdf.inputs[sock].links:
                node = link.from_node
                if node.type == "TEX_IMAGE":
                    return node.image
                for inp in node.inputs:
                    for l2 in inp.links:
                        if l2.from_node.type == "TEX_IMAGE":
                            return l2.from_node.image
        return None
    return {"base": source("Base Color"), "metal_rough": source("Roughness", "Metallic"), "normal": source("Normal")}


def _meshy_vertex_hsv(me, img):
    """每個頂點在 Base Color 貼圖上的顏色（各角 UV 取樣平均）轉 HSV；回傳 (V, 3)，色相單位度。"""
    import numpy as np
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    uv = np.empty(len(me.loops) * 2, dtype=np.float32)
    me.uv_layers[0].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    vi = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", vi)
    rgb = px[np.clip((uv[:, 1] % 1 * h).astype(int), 0, h - 1), np.clip((uv[:, 0] % 1 * w).astype(int), 0, w - 1), :3]
    acc = np.zeros((len(me.vertices), 3))
    np.add.at(acc, vi, rgb)
    rgb = acc / np.maximum(np.bincount(vi, minlength=len(me.vertices)), 1)[:, None]
    mx, mn = rgb.max(axis=1), rgb.min(axis=1)
    d = np.where(mx > mn, mx - mn, 1.0)
    r, g, b = rgb.T
    hue = np.where(mx == r, (g - b) / d % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return np.stack([np.where(mx > mn, hue, 0), np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0), mx], axis=1)


def meshy_masks(me, base_img):
    """部位遮罩（每個頂點一個 bool）：帽子（紅）、郵差包（包的範圍內、不是皮膚也不是短褲）、背帶（包外的藍綠色）、
    皮膚、藍綠色。回傳 (頂點座標（帽頂高＝1）, 遮罩 dict)。"""
    import numpy as np
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    p = co.reshape(-1, 3) / MESHY_CAP_TOP
    hue, sat, val = _meshy_vertex_hsv(me, base_img).T
    red = ((hue < 15) | (hue > 340)) & (sat > 0.5)
    teal = (hue > 160) & (hue < 210) & (sat > 0.3)
    skin = (hue > 5) & (hue < 30) & (sat > 0.12) & (sat < 0.55) & (val > 0.45)
    navy = (hue > 205) & (hue < 250) & (sat > 0.25)
    (x0, x1), (y0, y1), (z0, z1) = MESHY_BAG_BOX
    box = (p[:, 0] > x0) & (p[:, 0] < x1) & (p[:, 1] > y0) & (p[:, 1] < y1) & (p[:, 2] > z0) & (p[:, 2] < z1)
    bag = box & ~skin & ~navy
    return p, {"cap": red & (p[:, 2] > 0.8), "bag": bag, "strap": teal & ~bag, "skin": skin & ~box, "teal": teal}


def meshy_detach_bag(me, base_img):
    """Meshy 把郵差包的背面和肚子長在一起，甩包時中間會拉出長條三角形。沿包的邊界把包整塊拆下來（頂點群組跟著複製），
    兩邊的開口各補一片（補片的 UV 縮在最近一個包／皮膚頂點旁邊，看起來是單色；不加頂點，權重不用另外給）。"""
    p, m = meshy_masks(me, base_img)
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.active
    inside = {f for f in bm.faces if all(m["bag"][v.index] for v in f.verts)}
    if not inside:
        raise RuntimeError("找不到郵差包的面，檢查 MESHY_BAG_BOX 與顏色門檻")
    for _ in range(2):                        # 顏色判斷有雜點：先把包裡的小缺口補起來，再只留最大的一塊
        inside |= {g for f in inside for e in f.edges for g in e.link_faces
                   if sum(h in inside for x in g.edges for h in x.link_faces if h is not g) >= 2}
    blocks, seen = [], set()
    for f in inside:
        if f in seen:
            continue
        block, todo = set(), [f]
        seen.add(f)
        while todo:
            g = todo.pop()
            block.add(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h in inside and h not in seen:
                        seen.add(h)
                        todo.append(h)
        blocks.append(block)
    inside = max(blocks, key=len)
    cut = {e for f in inside for e in f.edges if not all(g in inside for g in e.link_faces)}
    rim_verts = {v for e in cut for v in e.verts}
    center = sum((v.co for v in rim_verts), Vector()) / max(len(rim_verts), 1)

    def nearest_uv(faces, ok):
        best = min((lp for f in faces for lp in f.loops if ok[lp.vert.index]), key=lambda lp: (lp.vert.co - center).length,
                   default=None)
        if best is None:
            raise RuntimeError("拆包找不到補片取色用的包／皮膚頂點，檢查顏色門檻")
        return best[uv].uv.copy()
    fill_uv = {True: nearest_uv(inside, m["teal"]), False: nearest_uv([f for f in bm.faces if f not in inside], m["skin"])}

    before = {e for e in bm.edges if e.is_boundary}
    bmesh.ops.split_edges(bm, edges=list(cut))
    rim = {e for e in bm.edges if e.is_boundary and e not in before}
    holes = 0
    while rim:                                # 拆開後的開口一圈一圈補（包那側、肚子那側分開）
        holes += 1
        first = rim.pop()
        on_bag = first.link_faces[0] in inside
        loop, todo = set(), [first]
        while todo:
            e = todo.pop()
            loop.add(e)
            for v in e.verts:
                for x in v.link_edges:
                    if x in rim and (x.link_faces[0] in inside) == on_bag:
                        rim.discard(x)
                        todo.append(x)
        faces = bmesh.ops.holes_fill(bm, edges=list(loop), sides=0)["faces"]
        if not faces:
            print(f"警告：拆包的一個開口（{len(loop)} 條邊）沒有圍成一圈，沒補到")
            continue
        faces = bmesh.ops.triangulate(bm, faces=faces, quad_method="BEAUTY", ngon_method="BEAUTY")["faces"]
        # UV：以參考點為中心、平面投影縮到約 2 個 texel 寬（顏色等於單色，但 UV 有面積，切線與描邊法線才算得出來）
        n = sum((f.normal for f in faces), Vector()).normalized()
        a = n.orthogonal().normalized()
        b = n.cross(a)
        pts = [lp.vert.co for f in faces for lp in f.loops]
        span = max(max(p.dot(a) for p in pts) - min(p.dot(a) for p in pts),
                   max(p.dot(b) for p in pts) - min(p.dot(b) for p in pts), 1e-6)
        mid = sum(pts, Vector()) / len(pts)
        for f in faces:
            f.smooth = True
            for lp in f.loops:
                d = lp.vert.co - mid
                lp[uv].uv = fill_uv[on_bag] + Vector((d.dot(a), d.dot(b))) * (MESHY_FILL_UV / span)
    # 背帶接口附近會多切出幾小片（各約 1 公分、兩邊都不連，遊戲裡帶描邊會變成小黑點）：整塊刪掉；補洞產生的零面積面也清掉
    loose = []
    seen = set()
    for f in bm.faces:
        if f in seen:
            continue
        block, todo = [], [f]
        seen.add(f)
        while todo:
            g = todo.pop()
            block.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen:
                        seen.add(h)
                        todo.append(h)
        vs = {v for g in block for v in g.verts}
        lo = Vector((min(v.co.x for v in vs), min(v.co.y for v in vs), min(v.co.z for v in vs)))
        hi = Vector((max(v.co.x for v in vs), max(v.co.y for v in vs), max(v.co.z for v in vs)))
        if (hi - lo).length < MESHY_LOOSE:
            loose.extend(vs)
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges[:])
    # 開口在非流形點相接時，兩邊的補片會疊出背對背、頂點相同的兩片（非流形邊）：成對刪掉
    twins = {}
    for f in bm.faces:
        twins.setdefault(frozenset(v.index for v in f.verts), []).append(f)
    bmesh.ops.delete(bm, geom=[f for fs in twins.values() if len(fs) > 1 for f in fs], context="FACES")
    nonmanifold = sum(1 for e in bm.edges if not e.is_manifold)
    flat = sum(1 for f in bm.faces if f.calc_area() < 1e-10)
    bm.to_mesh(me)
    bm.free()
    print(f"Meshy：郵差包拆開 {len(inside)} 面，切開 {len(cut)} 條邊，補 {holes} 個開口，刪掉 {len(loose)} 個碎塊頂點，"
          f"非流形邊 {nonmanifold} 條，零面積面 {flat} 個")
    if nonmanifold or flat:                   # 非流形處的描邊法線會互相抵消（往側邊推），FBX 往返也會拆成開口
        raise RuntimeError("拆包後還有非流形邊或零面積面，檢查 MESHY_BAG_BOX 的上緣是不是切在背帶環上")


def import_meshy(glb):
    """匯入 glb：焊接 UV 接縫拆開的頂點，正規化（面朝 -Y、Z 朝上、腳底在原點、帽頂＝MESHY_CAP_TOP），
    減面到 MESHY_TRIS − MESHY_FILL_TRIS 以下（collapse 會保留 UV 接縫，Meshy 原 UV 不重展）。回傳 (網格物件, 原貼圖 dict)。"""
    import numpy as np
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=glb)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    if len(meshes) != 1 or len(meshes[0].data.materials) != 1:
        raise RuntimeError(f"glb 應該是一個網格、一個材質，實際 {len(meshes)} 個網格"
                           f"、{len(meshes[0].data.materials) if meshes else 0} 個材質")
    obj = meshes[0]
    images = _meshy_images(obj.data.materials[0])
    if images["base"] is None:
        raise RuntimeError("glb 的材質找不到 Base Color 貼圖")
    obj.data.transform(obj.matrix_world)
    for o in new:
        if o is not obj:
            bpy.data.objects.remove(o)
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    COLL.objects.link(obj)
    obj.parent = ROOT
    obj.matrix_world = Matrix()

    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(me)
    bm.free()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lo, hi = co.min(axis=0), co.max(axis=0)
    s = MESHY_CAP_TOP / (hi[2] - lo[2])
    me.transform(Matrix.Scale(s, 4) @ Matrix.Translation((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2])))

    src = tri_count(obj)
    ratio = MESHY_TRIS / src
    while tri_count(obj) > MESHY_TRIS - MESHY_FILL_TRIS:      # collapse 的結果不一定剛好，超過就把比例再調低一點重減
        ratio *= 0.99
        prev, obj.data = obj.data, me
        if prev is not me:
            bpy.data.meshes.remove(prev)
        dec = obj.modifiers.new("Decimate", "DECIMATE")
        dec.ratio = ratio
        dec.use_collapse_triangulate = True
        dg = bpy.context.evaluated_depsgraph_get()
        obj.data = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
        obj.modifiers.clear()
    if obj.data is not me:
        bpy.data.meshes.remove(me)
        me = obj.data
    for a in [a.name for a in me.attributes if a.name == "custom_normal"]:   # glTF 帶的自訂法線焊接後已失效
        me.attributes.remove(me.attributes[a])
    me.shade_smooth()
    if len(me.uv_layers) != 1:
        raise RuntimeError(f"Meshy 網格應該只有一層 UV，實際 {len(me.uv_layers)} 層")
    me.uv_layers[0].name = "UVMap"
    me.materials.clear()
    me.materials.append(toon_material("M_meshy", "#FFFFFF"))   # 固有色全由貼圖給，頂點色 RGB＝白
    obj.name = me.name = "AhJia_Mesh"
    print(f"Meshy：{src} → {tri_count(obj)} 三角形，{len(me.vertices)} 頂點")
    return obj, images


def _weights_get(mesh, names):
    import numpy as np
    w = np.zeros((len(mesh.data.vertices), len(names)), dtype=np.float32)
    col = {mesh.vertex_groups[n].index: i for i, n in enumerate(names) if n in mesh.vertex_groups}
    for v in mesh.data.vertices:
        for ge in v.groups:
            if ge.group in col:
                w[v.index, col[ge.group]] = ge.weight
    return w


def _weights_set(mesh, names, w):
    """整張權重表寫回頂點群組（每列已正規化），太小的權重不寫。"""
    import numpy as np
    for i, n in enumerate(names):
        g = mesh.vertex_groups.get(n) or mesh.vertex_groups.new(name=n)
        g.remove(list(range(len(w))))
        for vi in np.nonzero(w[:, i] > 1e-3)[0]:
            g.add([int(vi)], float(w[vi, i]), "REPLACE")


def _islands(me):
    """每個頂點所在的連通塊編號（union-find）。"""
    import numpy as np
    parent = np.arange(len(me.vertices))
    ev = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", ev)

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for a, b in ev.reshape(-1, 2):
        ra, rb = root(a), root(b)
        if ra != rb:
            parent[ra] = rb
    return np.array([root(i) for i in range(len(parent))])


def _meshy_torso(z):
    """依高度分給 Hips／Spine／Chest（同 torso_weights 的意思，門檻換成帽頂高＝1 的單位）。"""
    import numpy as np
    chest = smoothstep_np(0.55, 0.68, z)
    hips = 1 - smoothstep_np(0.38, 0.50, z)
    return {"Chest": chest, "Hips": hips, "Spine": np.clip(1 - chest - hips, 0, 1)}


def skin_meshy(arm, mesh, base_img):
    """熱擴散自動權重，拆開郵差包，再用部位規則修：手臂骨只管離骨頭近的頂點（肩甲、殼的側邊歸軀幹）、帽子與頭頂→Head、
    耳朵→Ear、鞋→Foot、郵差包（拆開的那一塊）→Bag、背帶→軀幹。熱擴散沒算到的頂點（身體裡的小碎片）抄最近的
    有權重頂點。描邊粗細寫進 OutlineWidth（手 0.6、其餘 1.0）。"""
    import numpy as np
    from mathutils.kdtree import KDTree
    for b in arm.data.bones:
        b.use_deform = b.name not in ACCESSORY_BONES
    select_only([mesh, arm], arm)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for b in arm.data.bones:
        b.use_deform = not b.name.endswith("HandProp")
    meshy_detach_bag(mesh.data, base_img)     # 熱擴散要在完整的網格上算，拆包放在後面
    if tri_count(mesh) > MESHY_TRIS:
        raise RuntimeError(f"拆包補開口後超過 {MESHY_TRIS} 三角形：{tri_count(mesh)}")
    names = [b.name for b in arm.data.bones if b.use_deform]
    bi = {n: i for i, n in enumerate(names)}
    w = _weights_get(mesh, names)
    p, m = meshy_masks(mesh.data, base_img)
    x, _, z = p.T

    empty = w.sum(axis=1) < 1e-4
    if empty.all():
        raise RuntimeError("自動權重失敗：Meshy 網格沒有頂點拿到權重")
    kd = KDTree(int((~empty).sum()))
    for i in np.nonzero(~empty)[0]:
        kd.insert(p[i], int(i))
    kd.balance()
    for i in np.nonzero(empty)[0]:
        w[i] = w[kd.find(p[i])[1]]
    w /= w.sum(axis=1, keepdims=True)

    def assign(mask, parts):
        """mask 內的頂點整列換掉：parts 是 {骨名: 純量或每個頂點一個值}。"""
        w[mask] = 0
        for n, v in parts.items():
            w[mask, bi[n]] = np.broadcast_to(v, mask.shape)[mask]

    # 肩甲蓋在上臂外面（離上臂骨 8～11 公分，手臂表面在 6 公分內），熱擴散卻分到上臂，抬手時殼會被掀開。
    # 前臂、手旁邊只有殼的側邊，不套這條（骨頭位置是目測擺的，前臂粗細一偏就會把手臂表面切給軀幹而撕開）
    torso = _meshy_torso(z)
    for sd in ("Left", "Right"):
        b = arm.data.bones[sd + "UpperArm"]
        h, d = np.array(b.head_local) / MESHY_CAP_TOP, np.array(b.tail_local - b.head_local) / MESHY_CAP_TOP
        # 手肘以外也照套：那裡拿到上臂權重、離手肘點又遠的是殼的側邊（前臂表面主要是 LowerArm 的權重）
        t = np.clip((p - h) @ d / (d @ d), 0, 1)
        moved = w[:, bi[sd + "UpperArm"]] * smoothstep_np(*MESHY_ARM_R, np.linalg.norm(p - h - t[:, None] * d, axis=1))
        w[:, bi[sd + "UpperArm"]] -= moved
        for n, f in torso.items():
            w[:, bi[n]] += moved * f

    head = smoothstep_np(*MESHY_HEAD_Z, z)    # 帽子、頭頂全給 Head（和下面的熱擴散權重漸變，低頭時眼睛附近才不會折一條線）
    w[head > 0] *= (1 - head[head > 0])[:, None]
    w[head > 0, bi["Head"]] += head[head > 0]
    ear = smoothstep_np(*MESHY_EAR[0], abs(x)) * smoothstep_np(*MESHY_EAR[1], z) * ~m["cap"]
    shoe = 1 - smoothstep_np(*MESHY_SHOE_Z, z)
    for sd, on in (("Left", x > 0), ("Right", x <= 0)):
        e = on & (ear > 0)                 # 耳朵根部和頭漸變
        w[e] *= (1 - ear[e])[:, None]
        w[e, bi[sd + "Ear"]] += ear[e]
        assign(on & (z < MESHY_SHOE_Z[1]), {sd + "Foot": shoe, sd + "LowerLeg": 1 - shoe})
    isl = _islands(mesh.data)
    if not m["bag"].any():
        raise RuntimeError("找不到郵差包的頂點，檢查 MESHY_BAG_BOX 與顏色門檻")
    bag_island = np.bincount(isl[m["bag"]]).argmax()
    if (isl == bag_island).mean() > 0.2:      # 拆包沒拆開時整隻會被當成包
        raise RuntimeError("郵差包沒有拆成獨立的一塊，檢查 MESHY_BAG_BOX 與顏色門檻")
    bag = isl == bag_island
    assign(bag, {"Bag": 1.0})
    print(f"Meshy：連通塊 {len(np.unique(isl))} 個")
    assign(m["strap"] & ~bag, torso)
    np.put_along_axis(w, np.argsort(-w, axis=1)[:, 4:], 0, axis=1)   # Unity 每個頂點最多吃 4 根骨頭：先截好，預覽才和遊戲一樣
    w /= np.maximum(w.sum(axis=1, keepdims=True), 1e-6)
    _weights_set(mesh, names, w)

    width = np.where(w[:, bi["LeftHand"]] + w[:, bi["RightHand"]] > 0.5, 0.6, 1.0)
    g = mesh.vertex_groups.new(name="OutlineWidth")
    for v in (0.6, 1.0):
        g.add([int(i) for i in np.nonzero(width == v)[0]], v, "REPLACE")


def meshy_straighten(arm, mesh):
    """MESHY_SYMMETRIC 的骨頭左右改成同一個方向（父骨先轉，子骨跟著走），網格跟著變形後把這個姿勢套成
    新的靜止姿勢，再照 align_bone_roll 重設 roll。動作的關鍵格是相對靜止姿勢的旋轉，這樣左右才一致。
    MESHY_MIDLINE 的骨頭轉回中線。最後兩隻鞋底對齊、整隻重新落地。"""
    import numpy as np
    targets = {}
    for base in MESHY_SYMMETRIC:
        l, r = arm.data.bones["Left" + base], arm.data.bones["Right" + base]
        dl = (l.tail_local - l.head_local).normalized()
        dr = (r.tail_local - r.head_local).normalized()
        d = Vector(((dl.x - dr.x) / 2, (dl.y + dr.y) / 2, (dl.z + dr.z) / 2)).normalized()
        targets["Left" + base], targets["Right" + base] = d, Vector((-d.x, d.y, d.z))
    for name in MESHY_MIDLINE:
        b = arm.data.bones[name]
        targets[name] = Vector((0, b.tail_local.y - b.head_local.y, b.tail_local.z - b.head_local.z)).normalized()
    select_only([arm], arm)
    bpy.ops.object.mode_set(mode="POSE")
    for name, _, _, _ in BONES:              # BONES 的順序父骨在前
        if name in targets:
            bpy.context.view_layer.update()
            pb = arm.pose.bones[name]
            q = (pb.tail - pb.head).rotation_difference(targets[name]).to_matrix().to_4x4()
            pb.matrix = Matrix.Translation(pb.head) @ q @ Matrix.Translation(-pb.head) @ pb.matrix
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    select_only([mesh], mesh)
    bpy.ops.object.modifier_apply(modifier="Armature")
    select_only([arm], arm)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="EDIT")
    ends = {eb.name: [eb.head.copy(), eb.tail.copy()] for eb in arm.data.edit_bones}   # 先記下：相連骨頭的頭尾是連動的

    # 左右腿骨長不同（原姿勢右腳在前、左膝微彎），擺直後鞋底差幾公釐：較高那隻把小腿往下拉長補平（膝蓋不動、腳踝以下整段下移）
    me = mesh.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    shoe = co[:, 2] < MESHY_SOLE_Z * MESHY_CAP_TOP
    soles = {sd: co[shoe & (co[:, 0] * sx > 0), 2].min() for sd, sx in (("Left", 1), ("Right", -1))}
    for sd, z0 in soles.items():
        dz = z0 - min(soles.values())
        if dz < 1e-5:
            continue
        knee, ankle = (np.array(v) for v in ends[sd + "LowerLeg"])
        t = np.clip((co - knee) @ (ankle - knee) / ((ankle - knee) @ (ankle - knee)), 0, 1)
        w = _weights_get(mesh, [sd + "Foot", sd + "LowerLeg"])
        co[:, 2] -= dz * (w[:, 0] + w[:, 1] * t)
        for n, k in ((sd + "LowerLeg", 1), (sd + "Foot", 0), (sd + "Foot", 1)):
            ends[n][k].z -= dz
        print(f"Meshy：{sd} 鞋底高 {dz * 1000:.1f} mm，小腿拉長補平")
    me.vertices.foreach_set("co", co.ravel())
    me.update()

    # 腿擺直後鞋底會稍微穿到地面下：網格與骨頭一起重新落地、等比縮回帽頂＝MESHY_CAP_TOP
    lo, hi = co[:, 2].min(), co[:, 2].max()
    fit = Matrix.Scale(MESHY_CAP_TOP / (hi - lo), 4) @ Matrix.Translation((0, 0, -lo))
    me.transform(fit)
    for eb in arm.data.edit_bones:
        eb.head, eb.tail = fit @ ends[eb.name][0], fit @ ends[eb.name][1]
        align_bone_roll(eb)
        dy = abs((eb.tail - eb.head).normalized().y)
        if abs(dy - 0.7) < 0.1:           # align_bone_roll 在 |d.y|＝0.7 換規則，太靠近時局部 X 可能不是左右方向
            print(f"警告：{eb.name} 的方向 |y|＝{dy:.2f} 靠近 roll 規則的分界 0.7")
    bpy.ops.object.mode_set(mode="OBJECT")
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm


def meshy_textures(images, out):
    """原貼圖解到暫存資料夾，交給 meshy_texture.build_meshy_textures 寫 _BaseMap／_MaskMap；
    貼圖模組還沒放進來時用暫代版（Base Color 縮到 TEX_SIZE、_MaskMap 全 (0, 0.5, 0)）。回傳 _BaseMap 影像。"""
    import shutil
    import tempfile
    tmp = tempfile.mkdtemp(prefix="ahjia_meshy_")
    try:
        base_path = _meshy_textures_in(images, out, tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for img in {img.name: img for img in images.values() if img is not None}.values():   # 同一張可能接兩個插槽
        bpy.data.images.remove(img)
    return bpy.data.images.load(base_path)


def _meshy_textures_in(images, out, tmp):
    """meshy_textures 的本體：原貼圖存成 png 到 tmp，寫出 _BaseMap／_MaskMap，回傳 _BaseMap 路徑。"""
    import numpy as np
    paths = {}
    for key, img in images.items():
        paths[key] = None
        if img is not None:
            paths[key] = os.path.join(tmp, key + ".png")
            img.filepath_raw = paths[key]
            img.file_format = "PNG"
            img.save()
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)
    try:
        import meshy_texture
    except ModuleNotFoundError as e:
        if e.name != "meshy_texture":
            raise
        meshy_texture = None
    if meshy_texture:
        base_path, _ = meshy_texture.build_meshy_textures(paths, out, size=TEX_SIZE)
    else:
        print("警告：找不到 meshy_texture.py，貼圖用暫代版")
        base_path = os.path.join(out, "AhJia3D_BaseMap.png")
        img = bpy.data.images.load(paths["base"])
        img.scale(TEX_SIZE, TEX_SIZE)
        img.filepath_raw = base_path
        img.file_format = "PNG"
        img.save()
        bpy.data.images.remove(img)
        mask = np.zeros((TEX_SIZE, TEX_SIZE, 3), dtype=np.float32)
        mask[..., 1] = 0.5
        bpy.data.images.remove(_save_png("AhJia3D_MaskMap", mask, os.path.join(out, "AhJia3D_MaskMap.png"), "Non-Color"))
    return base_path


def render_weight_sheet(scene, arm, mesh, path, cell=600):
    """權重檢查圖：每根骨頭一個顏色，頂點色＝各骨頭顏色依權重混合（交界越糊代表漸變越寬）。
    正面、3/4 前、側面、背面四格，靜止姿勢。"""
    import numpy as np
    names = [b.name for b in arm.data.bones if b.use_deform]
    w = _weights_get(mesh, names)
    hues = np.arange(len(names)) * 0.618034 % 1.0
    pal = np.stack([0.5 + 0.5 * np.cos(2 * np.pi * (hues + k / 3)) for k in range(3)], axis=1)
    rgb = (w @ pal) / np.maximum(w.sum(axis=1, keepdims=True), 1e-6)
    me = mesh.data
    attr = me.color_attributes.new("WeightViz", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.concatenate([rgb, np.ones((len(rgb), 1))], axis=1).astype(np.float32).ravel())
    viz = bpy.data.materials.new("M_weightviz")
    viz.use_nodes = True
    nt = viz.node_tree
    nt.nodes.clear()
    a = nt.nodes.new("ShaderNodeAttribute")
    a.attribute_name = "WeightViz"
    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(a.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    old = list(me.materials)
    me.materials.clear()
    me.materials.append(viz)
    cam = camera(scene, "CamWeights", 1.2, (0, 0, 0.53))
    tmp = os.path.join(os.path.dirname(path), "_weights.png")
    cells = []
    for rz in (0, -35, -90, 180):
        ROOT.rotation_euler = (0, 0, math.radians(rz))
        render(scene, cam, tmp, cell, cell)
        img = bpy.data.images.load(tmp)
        px = np.empty(cell * cell * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        bpy.data.images.remove(img)
        cells.append(px.reshape(cell, cell, 4))
    ROOT.rotation_euler = (0, 0, 0)
    sheet = np.concatenate(cells, axis=1)
    res = bpy.data.images.new("weight_sheet", sheet.shape[1], sheet.shape[0])
    res.pixels.foreach_set(sheet.ravel())
    res.filepath_raw = path
    res.file_format = "PNG"
    res.save()
    bpy.data.images.remove(res)
    os.remove(tmp)
    bpy.data.objects.remove(cam)
    me.materials.clear()
    for m in old:
        me.materials.append(m)
    bpy.data.materials.remove(viz)
    me.color_attributes.remove(attr)


def build_meshy(glb, out):
    """Meshy 來源的模型、骨架、權重與貼圖（對應程式化版從建模到 build_textures 那一段）。回傳 (骨架, 網格)。"""
    mesh, images = import_meshy(glb)
    arm = build_armature(meshy_bones(mesh))
    skin_meshy(arm, mesh, images["base"])
    with open(os.path.join(out, "stats.txt"), "w", encoding="utf-8") as f:
        f.write(f"{'AhJia_Mesh':10s} {tri_count(mesh):6d}\n{'TOTAL':10s} {tri_count(mesh):6d} 三角形\n")
    render_weight_sheet(bpy.context.scene, arm, mesh, os.path.join(out, "AhJia3D_weights_raw.png"))
    meshy_straighten(arm, mesh)
    render_weight_sheet(bpy.context.scene, arm, mesh, os.path.join(out, "AhJia3D_weights.png"))
    print("骨頭：" + ", ".join(b.name for b in arm.data.bones))
    print("頂點群組：" + ", ".join(g.name for g in mesh.vertex_groups))
    use_basemap(mesh, meshy_textures(images, out))
    return arm, mesh


def meshy_extra_renders(scene, cam, inst, arm, actions, out):
    """Meshy 版多出的檢查圖：動作總覽的 3/4 前與 3/4 後（看肩甲、殼、包、帽子穿插），以及遊戲尺寸側面圖。"""
    for name, rz in (("actions_front", -40), ("actions_back", -140)):
        inst.rotation_euler = (0, 0, math.radians(rz))
        render_action_sheet(scene, cam, arm, actions, os.path.join(out, f"AhJia3D_{name}.png"))
    use_action(arm, actions["Idle"][0])
    scene.frame_set(0)
    inst.rotation_euler = (0, 0, math.radians(90))      # 面向右，和橫向卷軸裡往前跑的方向一樣
    gcam = camera(scene, "CamGame", MESHY_CAP_TOP * 1.08, (0, 0, MESHY_CAP_TOP / 2), elev=0)
    render(scene, gcam, os.path.join(out, "AhJia3D_game_scale.png"), 160, 160)
    bpy.data.objects.remove(gcam)

#endregion

#region Meshy 動作修正

# Meshy 模型比例和程式化版不同（長嘴、肩甲與殼較厚、郵差包掛在左前腹、爪子細長），共用 ANIMS 的關鍵格
# 會穿插或滑步，這裡在 build_actions 之後逐格疊上增量。格式：{動作名: {骨名: {格數: (x, y, z)}}}，
# 角度是度、繞骨頭局部軸（正負號同 ANIMS）；"@Hips" 是 Hips 位移的增量（公尺，骨頭局部座標，Y＝往上）。
# 增量在 ANIMS 的關鍵格之間平滑過渡（沒寫到的關鍵格是 0），格數寫 "*" 表示整個動作都加同一個增量。
# 循環動作的第一格與最後一格是同一個姿勢，只寫其中一格，另一格會跟著改，接縫不跳。
MESHY_ANIM_FIXES = {
    # 跑步：經過格（5、15）改成「後腳收腿往前、前腳撐地」，支撐腳從前往後走，不再往前拖地；
    # 收腿往前擺的那隻腳在 8／18 格多抬一點，不貼地掃過
    "Run": {
        "LeftUpperLeg": {5: (-10, 0, 0), 8: (12, 0, 0), 15: (10, 0, 0)},
        "LeftLowerLeg": {5: (-87, 0, 0), 8: (-30, 0, 0), 15: (87, 0, 0)},
        "LeftFoot": {5: (-20, 0, 0), 15: (20, 0, 0)},
        "RightUpperLeg": {5: (10, 0, 0), 15: (-10, 0, 0), 18: (12, 0, 0)},
        "RightLowerLeg": {5: (87, 0, 0), 15: (-87, 0, 0), 18: (-30, 0, 0)},
        "RightFoot": {5: (20, 0, 0), 15: (-20, 0, 0)},
        "LeftUpperArm": {"*": (0, 0, 8)},
    },
    # 縮成球：Meshy 兩隻鞋靠得近，大腿抱到胸前時兩腳會互穿；大腿稍微外展
    "Jump": {"LeftUpperLeg": {"*": (0, 0, 5)}, "RightUpperLeg": {"*": (0, 0, -5)}},
    # 甩包：包掛在左前腹，往後拉會插進肚子、往前上甩會擋到臉；改成往外側拉、往前平甩
    "Melee": {"Bag": {3: (35, 0, 25), 6: (-50, 0, 0), 9: (-60, 0, 0)}},
    # 彈弓：拉皮筋的右手原本拉到嘴的高度，會插進長嘴；壓低到胸前
    "Shoot": {"RightUpperArm": {3: (-35, 0, 0)}, "RightLowerArm": {3: (10, 0, 0)}},
    # 受傷：頭往後甩時帽子後緣會壓進殼頂；脖子少仰一點，改由上身多仰
    "Hurt": {"Neck": {3: (12, 0, 0), 9: (7, 0, 0)}, "Spine": {3: (-5, 0, 0), 9: (-3, 0, 0)}},
}

# 這些動作逐格補 Hips 位移，讓縮成的球繞自己的中心轉、最低點貼地（Meshy 的身體捲起來後球心離 Hips 約 0.18 m，
# 直接繞 Hips 轉一圈會上下晃約 0.37 m）；球心前後放在腳底原點（y＝0），左右沿用第一格。修正表先套，球心最後算
MESHY_BALL_ACTIONS = ["Jump"]


def _meshy_bounds(scene, mesh, frame):
    """某一格變形後網格（只算 Armature，描邊等其他修改器先關掉）的世界座標 bbox：回傳 (最小角, 最大角)。"""
    import numpy as np
    scene.frame_set(frame)
    ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    mw = np.array(mesh.matrix_world)
    co = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    return Vector(co.min(axis=0)), Vector(co.max(axis=0))


def _meshy_center_ball(arm, act, last):
    scene = bpy.context.scene
    mesh = next(o for o in arm.children if o.type == "MESH")
    hidden = [m for m in mesh.modifiers if m.type != "ARMATURE" and m.show_viewport]
    for m in hidden:
        m.show_viewport = False
    use_action(arm, act)
    try:
        bounds = {f: _meshy_bounds(scene, mesh, f) for f in range(int(act.frame_range[0]), last + 1)}
    finally:
        for m in hidden:
            m.show_viewport = True
    centers = {f: (lo + hi) / 2 for f, (lo, hi) in bounds.items()}
    first = min(centers)
    target = Vector((centers[first].x, 0.0, max(c.z - lo.z for c, (lo, _) in zip(centers.values(), bounds.values()))))
    # Hips 位移在骨頭的靜止座標裡、先於旋轉套用：世界的平移量換回骨頭局部座標
    to_local = (arm.matrix_world.to_3x3() @ arm.data.bones["Hips"].matrix_local.to_3x3()).inverted()
    pb = arm.pose.bones["Hips"]
    base = {}
    for f in centers:                      # 先取完每格原本的位移再補 key，補的 key 不影響其他格的底
        scene.frame_set(f)
        base[f] = pb.location.copy()
    for f, c in centers.items():
        pb.location = base[f] + to_local @ (target - c)
        pb.keyframe_insert("location", frame=f, group="Hips")
    scene.frame_set(0)


def apply_meshy_anim_fixes(arm, actions):
    """依 MESHY_ANIM_FIXES 改 build_actions 建好的 Action：在指定格把 rotation_euler（或 Hips 的 location）
    加上增量後 key 回去，該格原本沒有 key 就以當下的曲線值為底補一個 key。只動骨頭，不碰 shape key。
    最後對 MESHY_BALL_ACTIONS 做球心置中（修正表改到的姿勢也算進球心）。"""
    current = arm.animation_data.action
    for name, bones in MESHY_ANIM_FIXES.items():
        if name not in actions:
            raise KeyError(f"MESHY_ANIM_FIXES：沒有動作 {name}")
        act, last = actions[name]
        loop = ANIMS[name][0]
        slot = act.slots[0] if hasattr(act, "slots") and len(act.slots) else None
        curves = {(fc.data_path, fc.array_index): fc for fc in _fcurves(act, slot)}
        for bone, frames in bones.items():
            prop, scale = ("location", 1.0) if bone == "@Hips" else ("rotation_euler", math.radians(1))
            target = "Hips" if bone == "@Hips" else bone
            if target not in arm.pose.bones:
                raise KeyError(f"MESHY_ANIM_FIXES：{name} 沒有骨頭 {target}")
            path = f'pose.bones["{target}"].{prop}'
            fcs = [curves.get((path, i)) for i in range(3)]
            if None in fcs:
                raise KeyError(f"MESHY_ANIM_FIXES：{name} 的 {target} 沒有 {prop} 曲線")
            # 控制點＝ANIMS 的關鍵格＋表上寫到的格，沒寫到的控制點增量 0；控制點之間平滑過渡，
            # 二次動畫逐格 key 的骨頭（尾巴、耳朵、包包）中間格也跟著改，不會只有寫到的那格跳開
            ctrl = {k: Vector() for k, _ in ANIMS[name][1]}
            for frame, delta in frames.items():   # 先放具體格數，"*" 最後加到全部控制點，跟表的寫法順序無關
                if frame != "*":
                    for f in [frame] + ([last] if loop and frame == 0 else [0] if loop and frame == last else []):
                        ctrl[f] = ctrl.get(f, Vector()) + Vector(delta)
            for f in ctrl:
                ctrl[f] += Vector(frames.get("*", (0, 0, 0)))
            pts = sorted(ctrl.items())

            def offset(f):
                if f <= pts[0][0] or f >= pts[-1][0]:
                    return pts[0][1] if f <= pts[0][0] else pts[-1][1]
                (f0, d0), (f1, d1) = next((a, b) for a, b in zip(pts, pts[1:]) if a[0] <= f <= b[0])
                return d0.lerp(d1, smoothstep(f0, f1, f))
            keyed = sorted({round(kp.co[0]) for fc in fcs for kp in fc.keyframe_points} | set(ctrl))
            base = {f: [fc.evaluate(f) for fc in fcs] for f in keyed}   # 先全部取值再寫，補的 key 不影響其他格的底
            for f in keyed:
                for fc, v, d in zip(fcs, base[f], offset(f)):
                    if d:
                        fc.keyframe_points.insert(f, v + d * scale, options={"FAST"})
            for fc in fcs:
                fc.update()
        _finish_curves(act, slot, name, loop)
    for name in MESHY_BALL_ACTIONS:
        act, last = actions[name]
        _meshy_center_ball(arm, act, last)
        _finish_curves(act, act.slots[0] if hasattr(act, "slots") and len(act.slots) else None, name, ANIMS[name][0])
    if current:
        use_action(arm, current)

#endregion


def main():
    global ROOT, COLL
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.abspath(argv[argv.index("--out") + 1]) if "--out" in argv else os.getcwd()
    source = argv[argv.index("--source") + 1] if "--source" in argv else "procedural"
    if source not in ("procedural", "meshy"):
        raise SystemExit("用法：--source meshy [--glb <glb 路徑>]；不帶 --source 是程式化版")
    os.makedirs(out, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = setup_scene()
    COLL = bpy.data.collections.new("AhJia")
    scene.collection.children.link(COLL)
    ROOT = bpy.data.objects.new("AhJia_Root", None)
    COLL.objects.link(ROOT)

    if source == "meshy":
        glb = os.path.abspath(argv[argv.index("--glb") + 1]) if "--glb" in argv else MESHY_GLB
        arm, mesh = build_meshy(glb, out)
    else:
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
        build_textures(mesh, out)

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
    actions = build_actions(arm, face=source != "meshy")
    if source == "meshy":
        apply_meshy_anim_fixes(arm, actions)
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
    if source != "meshy":
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
    if source == "meshy":
        meshy_extra_renders(scene, cam2, insts[1], arm, actions, out)
    use_action(arm, actions["Idle"][0])

    # 存檔：把模型放回場景、拿掉預覽用的 instance
    for e in insts:
        bpy.data.objects.remove(e)
    scene.collection.children.link(COLL)
    scene.camera = cam2
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "AhJia3D.blend"))
    bake_vertex_colors(mesh)
    bake_outline_normals(mesh)
    export_fbx(os.path.join(out, "AhJia3D.fbx"))
    print("輸出完成：" + out)


main()
