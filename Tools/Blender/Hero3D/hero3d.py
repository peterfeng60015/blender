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
  AhJia3D.fbx              給 Unity：骨架＋網格＋全部動作
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
from mathutils import Matrix, Vector

#region 參數

COLORS = {
    "shell": "#C9A26B", "band": "#8E6B3E", "skin": "#D8B6A4", "belly": "#EBD0C2", "cap": "#D8342C",
    "cap_logo": "#F4EBDD", "bag": "#1F9E9A", "bag_flap": "#17807C", "strap": "#14615E", "buckle": "#E8B830",
    "shorts": "#B59A6A", "shoe": "#5A3A26", "sole": "#3A2418", "nose": "#6B3E3E", "eye_white": "#FAF6EE",
    "pupil": "#1A1414", "ear_inner": "#E39A96", "outline": "#3B2414",
}

SHELL_C = (0.0, 0.11, 0.50)   # 背殼橢球中心（x 左右、y 前後、z 高）
SHELL_R = (0.25, 0.31, 0.33)
SHELL_ZCUT = 0.37             # 殼的下緣（側面看是圓頂＋水平下緣）

# 網格密度（合計約 7.5k 三角形）
D = dict(body_ratio=0.6, shell_nv=20, fill=(16, 10), eye=(12, 7, 8, 5, 12, 8), nose=(12, 8), ear=10,
         cap=(24, 12, 24, 12), hand=(12, 8, 8, 6), shoe=(16, 8), bag_seg=2, strap=(4, 0))

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

#region 身體（Skin 修改器＋細分：四肢、尾巴、頭和長嘴是一整塊連續的網格）


def build_body():
    # (名稱, 位置, 半徑)；面朝 -Y
    nodes = [
        ("pelvis", (0, 0.02, 0.36), 0.155),
        ("belly", (0, -0.01, 0.50), 0.175),
        ("chest", (0, -0.04, 0.63), 0.145),
        ("neck", (0, -0.09, 0.735), 0.085),
        ("head", (0, -0.13, 0.84), 0.118),
        ("snout", (0, -0.27, 0.805), 0.06),
        ("snout_tip", (0, -0.375, 0.775), 0.034),
        ("tail1", (0, 0.17, 0.31), 0.065),
        ("tail2", (0, 0.31, 0.21), 0.042),
        ("tail3", (0, 0.41, 0.15), 0.018),
    ]
    for side, sx in (("L", 1), ("R", -1)):
        nodes += [
            ("hip" + side, (0.085 * sx, 0.02, 0.29), 0.072),
            ("knee" + side, (0.10 * sx, -0.01, 0.17), 0.058),
            ("ankle" + side, (0.10 * sx, 0.0, 0.075), 0.046),
            ("shoulder" + side, (0.135 * sx, -0.04, 0.635), 0.052),
            ("elbow" + side, (0.215 * sx, -0.06, 0.505), 0.043),
            ("wrist" + side, (0.25 * sx, -0.09, 0.40), 0.037),
        ]
    edges = [("pelvis", "belly"), ("belly", "chest"), ("chest", "neck"), ("neck", "head"), ("head", "snout"),
             ("snout", "snout_tip"), ("pelvis", "tail1"), ("tail1", "tail2"), ("tail2", "tail3")]
    for s in "LR":
        edges += [("pelvis", "hip" + s), ("hip" + s, "knee" + s), ("knee" + s, "ankle" + s),
                  ("chest", "shoulder" + s), ("shoulder" + s, "elbow" + s), ("elbow" + s, "wrist" + s)]
    index = {n[0]: i for i, n in enumerate(nodes)}
    me = bpy.data.meshes.new("Body")
    me.from_pydata([n[1] for n in nodes], [(index[a], index[b]) for a, b in edges], [])
    obj = bpy.data.objects.new("Body", me)
    COLL.objects.link(obj)
    obj.parent = ROOT
    skin = obj.modifiers.new("Skin", "SKIN")
    skin.branch_smoothing = 0.6
    for i, n in enumerate(nodes):
        sv = me.skin_vertices[0].data[i]
        sv.radius = (n[2], n[2])
        sv.use_root = n[0] == "pelvis"
    sub = obj.modifiers.new("Subsurf", "SUBSURF")
    sub.levels = sub.render_levels = 2
    dec = obj.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = D["body_ratio"]
    bake_modifiers(obj)

    # 短褲上下緣切出整齊的邊（減面後才切，切線不會被減面弄歪）
    bm = bmesh.new()
    bm.from_mesh(obj.data)
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


def shell_strip(bm, y0, y1, scale, bulge, nu, nv, mi):
    """背殼橢球在 y0～y1 之間的一條帶子；中間微微鼓起，邊緣貼回橢球，相鄰帶子交疊處才看得出分節。"""
    cx, cy, cz = SHELL_C
    rx, ry, rz = SHELL_R
    grid = []
    for i in range(nu + 1):
        s = i / nu
        y = y0 + (y1 - y0) * s
        k = math.sqrt(max(0.0004, 1.0 - ((y - cy) / ry) ** 2))
        m = scale * (1.0 + bulge * math.sin(math.pi * s))
        c = (SHELL_ZCUT - cz) / (rz * k * m)
        phimax = math.acos(max(-0.985, min(0.95, c)))
        row = []
        for j in range(nv + 1):
            phi = -phimax + 2 * phimax * j / nv
            row.append(bm.verts.new((cx + rx * k * m * math.sin(phi), y, cz + rz * k * m * math.cos(phi))))
        grid.append(row)
    for i in range(nu):
        for j in range(nv):
            f = bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
            f.material_index = mi


def build_shell():
    bm = bmesh.new()
    cy, ry = SHELL_C[1], SHELL_R[1]
    front, back = cy - ry * 0.80, cy + ry * 0.995
    # 肩甲（前）、5 條活動殼帶、臀甲（後）；殼帶往後一條比一條小一點點，交疊時前一條蓋住後一條
    cuts = [front, -0.05, 0.005, 0.06, 0.115, 0.17, 0.225, back]
    for k in range(len(cuts) - 1):
        y0 = cuts[k] - (0.012 if k else 0.0)
        y1 = cuts[k + 1] + 0.012
        is_band = 1 <= k <= 5
        scale = 1.035 - 0.004 * k if is_band else 1.0
        nu = 2 if is_band else (3 if k == 0 else 6)
        shell_strip(bm, y0, y1, scale, 0.035 if is_band else 0.02, nu, D["shell_nv"], 1 if is_band else 0)
    obj = make_object("Shell", bm, ["shell", "band"])
    sol = obj.modifiers.new("Thickness", "SOLIDIFY")
    sol.thickness = 0.018
    sol.offset = -1
    bake_modifiers(obj)
    obj.data.shade_smooth()

    # 殼底下的填充（從殼的開口往裡看不會是空的）
    bm = bmesh.new()
    add_ellipsoid(bm, (0, 0.12, 0.47), (0.215, 0.28, 0.25), seg=D["fill"][0], rings=D["fill"][1])
    make_object("ShellFill", bm, ["belly"])
    return obj

#endregion

#region 頭部配件


def build_head_parts():
    objs = []
    # 眼睛：眼白＋瞳孔＋半垂的眼皮（冷面笑匠）
    bm = bmesh.new()
    for sx in (1, -1):
        c = Vector((0.072 * sx, -0.205, 0.872))
        out = Vector((0.62 * sx, -0.78, 0.05)).normalized()
        e = D["eye"]
        add_ellipsoid(bm, c, (0.033, 0.033, 0.036), seg=e[0], rings=e[1], mi=0)
        add_ellipsoid(bm, c + out * 0.024, (0.016, 0.016, 0.019), seg=e[2], rings=e[3], mi=1)
        lid = add_ellipsoid(bm, c, (0.039, 0.039, 0.041), seg=e[4], rings=e[5], mi=2,
                            rot=Matrix.Rotation(math.atan2(out.x, -out.y), 3, "Z") @ Matrix.Rotation(math.radians(-20), 3, "X"))
        # 只留上半（眼皮）：刪掉 z 低於眼睛中心＋一點的頂點
        bmesh.ops.delete(bm, geom=[v for v in lid if v.co.z < c.z + 0.006], context="VERTS")
    objs.append(make_object("Eyes", bm, ["eye_white", "pupil", "skin"]))

    # 鼻子
    bm = bmesh.new()
    add_ellipsoid(bm, (0, -0.405, 0.775), (0.03, 0.022, 0.024), seg=D["nose"][0], rings=D["nose"][1])
    objs.append(make_object("Nose", bm, ["nose"]))

    # 大尖耳：扁圓錐，外皮＋內耳
    bm = bmesh.new()
    for sx in (1, -1):
        for inner in (False, True):
            res = bmesh.ops.create_cone(bm, cap_ends=True, segments=D["ear"], radius1=1.0, radius2=0.0, depth=1.0)
            vs = res["verts"]
            sc = (0.052, 0.02, 0.23) if not inner else (0.034, 0.01, 0.17)
            m = (Matrix.Translation((0.065 * sx, -0.065 - (0.012 if inner else 0), 0.995 if not inner else 0.985))
                 @ rot_xyz(rx=-18, ry=22 * sx, rz=-14 * sx).to_4x4()
                 @ Matrix.Diagonal(Vector(sc)).to_4x4())
            bmesh.ops.transform(bm, matrix=m, verts=vs)
            for f in {f for v in vs for f in v.link_faces}:
                f.material_index = 1 if inner else 0
    objs.append(make_object("Ears", bm, ["skin", "ear_inner"]))

    # 快遞帽：帽頂＋帽簷＋前面的圓標
    bm = bmesh.new()
    tilt = rot_xyz(rx=12)
    dome = add_ellipsoid(bm, (0, 0, 0), (0.125, 0.138, 0.088), seg=D["cap"][0], rings=D["cap"][1])
    bmesh.ops.delete(bm, geom=[v for v in dome if v.co.z < -0.002], context="VERTS")
    edge = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.edgeloop_fill(bm, edges=edge)
    bmesh.ops.transform(bm, matrix=Matrix.Translation((0, -0.115, 0.905)) @ tilt.to_4x4(), verts=bm.verts[:])
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=D["cap"][2], radius1=0.13, radius2=0.125, depth=0.016)
    brim = res["verts"]
    bmesh.ops.delete(bm, geom=[v for v in brim if v.co.y > 0.02], context="VERTS")
    brim = [v for v in brim if v.is_valid]
    bmesh.ops.transform(bm, matrix=Matrix.Translation((0, -0.19, 0.893)) @ rot_xyz(rx=6).to_4x4()
                        @ Matrix.Diagonal(Vector((0.95, 1.12, 1.0))).to_4x4(), verts=brim)
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=D["cap"][3], radius1=0.024, radius2=0.024, depth=0.006)
    logo = set(res["verts"])
    bmesh.ops.transform(bm, matrix=Matrix.Translation((0, -0.226, 0.94)) @ rot_xyz(rx=62).to_4x4(),
                        verts=list(logo))
    for f in bm.faces:
        f.material_index = 1 if all(v in logo for v in f.verts) else 0
    objs.append(make_object("Cap", bm, ["cap", "cap_logo"]))
    return objs

#endregion

#region 手、鞋、郵差包


def build_hands_shoes():
    bm = bmesh.new()
    for sx in (1, -1):
        add_ellipsoid(bm, (0.258 * sx, -0.10, 0.355), (0.043, 0.045, 0.05), seg=D["hand"][0], rings=D["hand"][1])
        add_ellipsoid(bm, (0.235 * sx, -0.135, 0.37), (0.018, 0.018, 0.026), seg=D["hand"][2], rings=D["hand"][3],
                      rot=rot_xyz(rx=-25))
    hands = make_object("Hands", bm, ["skin"])

    bm = bmesh.new()
    for sx in (1, -1):
        vs = add_ellipsoid(bm, (0.10 * sx, -0.045, 0.048), (0.068, 0.11, 0.052), seg=D["shoe"][0], rings=D["shoe"][1])
        for v in vs:                       # 鞋底壓平
            if v.co.z < 0.012:
                v.co.z = 0.0
        for f in {f for v in vs for f in v.link_faces}:
            f.material_index = 1 if f.calc_center_median().z < 0.008 else 0
    shoes = make_object("Shoes", bm, ["shoe", "sole"])
    return [hands, shoes]


def rounded_box(bm, center, half, bevel, mi):
    res = bmesh.ops.create_cube(bm, size=1.0)
    vs = res["verts"]
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ Matrix.Diagonal(Vector(half) * 2).to_4x4(), verts=vs)
    edges = list({e for v in vs for e in v.link_edges})
    bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=D["bag_seg"], profile=0.5, affect="EDGES")


def build_bag():
    objs = []
    bm = bmesh.new()
    rounded_box(bm, (0.218, -0.02, 0.385), (0.042, 0.095, 0.072), 0.018, 0)
    n0 = len(bm.faces)
    rounded_box(bm, (0.257, -0.02, 0.418), (0.006, 0.098, 0.045), 0.005, 0)
    bm.faces.ensure_lookup_table()
    for f in bm.faces[n0:]:
        f.material_index = 1
    n1 = len(bm.faces)
    rounded_box(bm, (0.265, -0.02, 0.385), (0.005, 0.018, 0.016), 0.004, 0)
    bm.faces.ensure_lookup_table()
    for f in bm.faces[n1:]:
        f.material_index = 2
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
    ("Snout", (0, -0.20, 0.83), (0, -0.40, 0.775), "Head"),
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
        (_side + "Ear", (0.055 * _sx, -0.07, 0.90), (0.095 * _sx, -0.035, 1.10), "Head"),
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
    }
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


def _fcurves(action, slot):
    try:
        from bpy_extras.anim_utils import action_get_channelbag_for_slot
        bag = action_get_channelbag_for_slot(action, slot)
        return bag.fcurves if bag else []
    except ImportError:
        return action.fcurves


def build_actions(arm):
    """依 ANIMS 建 Action（掛 fake user，FBX 匯出時全部帶出去）。回傳 {名稱: (action, 最後一格)}。"""
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
        slot = arm.animation_data.action_slot if hasattr(arm.animation_data, "action_slot") else None
        for fc in _fcurves(act, slot):
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR" if name == "Jump" else "BEZIER"
            if loop:
                fc.modifiers.new("CYCLES")
        act.frame_range = (keys[0][0], keys[-1][0])
        result[name] = (act, keys[-1][0])
        set_pose(arm, {})
    arm.animation_data.action = result["Idle"][0]
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
