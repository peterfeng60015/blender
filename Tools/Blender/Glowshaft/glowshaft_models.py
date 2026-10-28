"""《螢火礦坑》Glowshaft 的程式化建模：清空場景，依 --target 建出一種素材與它的動作。

適用 Blender 5.2 LTS。由 render_all.py 呼叫，和 render_sprites.py 串在同一個 Blender 行程裡：
    blender -b --factory-startup --python glowshaft_models.py --python ../render_sprites.py \
        --python glowshaft_finalize.py -- --target Miner --anim-object Miner_Rig --actions Idle,Run ...

三支腳本共用 `--` 之後的參數；這支只讀 --target，其餘留給 render_sprites.py。

約定（與 render_sprites.py 一致）：
- 角色正面朝 -Y、上方 +Z，根物件（名稱＝素材名）在原點且不動，原點＝腳底／pivot。
- 動作掛在子物件（Armature 或 <Name>_Body）上；多張變化（Rock、Crystal、Floor）用
  <Name>_Frames 空物件的 Action 定義幀數，每一幀只顯示一組變化（各物件自己的 hide_render 動畫）。
- 固有色寫在材質的自訂屬性 sprite_albedo，或用 Color Attribute 接 Base Color（逐面上色）。
  程式裡的顏色一律寫 sRGB（跟調色盤看到的一樣），寫進 Blender 前用 linear() 轉成線性。
- 描邊用反轉法線外殼，修改器名稱必須是 SpriteOutline，法線 pass 才會把它關掉。
"""

import argparse
import math
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

FPS = 12
OUTLINE_MOD = "SpriteOutline"  # 與 render_sprites.py 的 OUTLINE_MOD 相同
ALBEDO_PROP = "sprite_albedo"
COLOR_ATTR = "Col"

_outline_mat = None


# ---------- 共用工具 ----------

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="glowshaft_models.py")
    p.add_argument("--target", required=True)
    args, _ = p.parse_known_args(argv)
    return args


def reset_scene():
    scene = bpy.context.scene
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.actions, bpy.data.armatures,
                 bpy.data.cameras, bpy.data.lights):
        for block in list(coll):
            coll.remove(block)
    scene.render.fps = FPS
    scene.render.fps_base = 1.0
    scene.frame_start = 1
    global _outline_mat
    _outline_mat = None
    return scene


def linear(c):
    """sRGB → Blender 內部的線性色；輸出用 Standard 轉回 sRGB，PNG 上就是原本寫的顏色。"""
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)


def new_tree(mat):
    if bpy.app.version < (5, 0, 0):
        mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    return nt


def flat_material(name, color, roughness=0.6):
    """Principled BSDF，固有色同時寫進 sprite_albedo（--albedo 優先讀它）。color 是 sRGB。"""
    color = linear(color)
    mat = bpy.data.materials.new(name)
    nt = new_tree(mat)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    mat[ALBEDO_PROP] = (*color, 1.0)
    mat.diffuse_color = (*color, 1.0)
    return mat


def vcol_material(name):
    """Base Color 接逐角落的 Color Attribute；--albedo 會沿用這條線當固有色。"""
    mat = bpy.data.materials.new(name)
    nt = new_tree(mat)
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = COLOR_ATTR
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.8
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def outline_material():
    global _outline_mat
    if _outline_mat is None:
        mat = bpy.data.materials.new("GlowshaftOutline")
        nt = new_tree(mat)
        emit = nt.nodes.new("ShaderNodeEmission")
        emit.inputs["Color"].default_value = (0.015, 0.012, 0.02, 1.0)
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
        mat.use_backface_culling = True
        _outline_mat = mat
    return _outline_mat


def tag(bm, verts, mat_index):
    """把這批頂點所屬的面設成指定材質槽。"""
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat_index


def box(bm, center, size, mi=0, rot=None):
    m = Matrix.Translation(center) @ (rot or Matrix()) @ Matrix.Diagonal((*size, 1.0))
    tag(bm, bmesh.ops.create_cube(bm, size=1.0, matrix=m)["verts"], mi)


def cylinder(bm, center, radius, depth, mi=0, segments=8, radius2=None, rot=None):
    """預設沿 +Z；rot 給旋轉矩陣。"""
    m = Matrix.Translation(center) @ (rot or Matrix())
    r2 = radius if radius2 is None else radius2
    verts = bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius,
                                  radius2=r2, depth=depth, matrix=m)["verts"]
    tag(bm, verts, mi)
    return verts


def ico(bm, center, radius, mi=0, subdiv=1, scale=(1.0, 1.0, 1.0)):
    m = Matrix.Translation(center) @ Matrix.Diagonal((*scale, 1.0))
    verts = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius, matrix=m)["verts"]
    tag(bm, verts, mi)
    return verts


def rot_x(deg):
    return Matrix.Rotation(math.radians(deg), 4, "X")


def rot_y(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Y")


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Z")


def make_mesh(name, bm, mats, parent=None, outline=0.0, face_colors=None):
    """bmesh 轉物件，全部平面著色（低多邊形的面感）。

    face_colors：函式 f(BMFace) → sRGB (r, g, b)，轉線性後寫進 Color Attribute（搭 vcol_material）。
    outline：描邊厚度（公尺），0＝不描邊。
    """
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = False
    if face_colors is not None:
        layer = bm.loops.layers.float_color.new(COLOR_ATTR)
        for f in bm.faces:
            c = linear(face_colors(f))
            for loop in f.loops:
                loop[layer] = (*c, 1.0)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    if outline > 0:
        me.materials.append(outline_material())
        mod = obj.modifiers.new(OUTLINE_MOD, "SOLIDIFY")
        mod.thickness = outline
        mod.offset = 1.0
        mod.use_flip_normals = True
        mod.use_rim = False
        mod.material_offset = len(mats)  # 超出範圍的索引會夾到最後一槽＝描邊材質
    return obj


def empty(name, parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    return obj


def finish_action(obj, name, length, cyclic=True):
    """把 obj 目前的 Action 改名並設定幀範圍 1～length；第 length+1 幀等於第 1 幀，循環才無縫。"""
    act = obj.animation_data.action
    act.name = name
    act.use_fake_user = True
    act.use_frame_range = True
    act.frame_start = 1
    act.frame_end = length
    act.use_cyclic = cyclic
    return act


def set_key_interpolation(kind):
    bpy.context.preferences.edit.keyframe_new_interpolation_type = kind


def frames_holder(root, count):
    """<Name>_Frames：只有幀範圍的 Action「Idle」，給多張變化的素材當 --anim-object。"""
    holder = empty(f"{root.name}_Frames", root)
    holder["variant"] = 0
    for f in range(1, count + 1):
        holder["variant"] = f - 1
        holder.keyframe_insert('["variant"]', frame=f)
    finish_action(holder, "Idle", count, cyclic=False)
    return holder


def show_only_on_frame(objs, frame, count):
    """objs 只在第 frame 幀渲染；其他幀 hide_render。"""
    set_key_interpolation("CONSTANT")
    for obj in objs:
        for f in range(1, count + 1):
            obj.hide_render = f != frame
            obj.keyframe_insert("hide_render", frame=f)
        obj.hide_render = frame != 1
    set_key_interpolation("BEZIER")


def jitter(rng, c, amount):
    return tuple(max(0.0, min(1.0, v + rng.uniform(-amount, amount))) for v in c)


# ---------- Miner：採礦機器人 ----------

def build_miner():
    root = empty("Miner")
    arm_data = bpy.data.armatures.new("Miner_Rig")
    rig = bpy.data.objects.new("Miner_Rig", arm_data)
    bpy.context.scene.collection.objects.link(rig)
    rig.parent = root
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    # 骨頭一律朝 +Z、頭在旋轉軸心：骨頭本地 X＝世界 X，本地 Y＝世界 Z
    bones = {
        "hips": ((0, 0, 0.42), None),
        "torso": ((0, 0, 0.52), "hips"),
        "head": ((0, 0, 0.94), "torso"),
        "leg.L": ((0.15, 0, 0.42), "hips"),
        "leg.R": ((-0.15, 0, 0.42), "hips"),
        "arm.L": ((0.34, 0, 0.86), "torso"),
        "arm.R": ((-0.34, 0, 0.86), "torso"),
    }
    for name, (head, parent) in bones.items():
        eb = arm_data.edit_bones.new(name)
        eb.head = head
        eb.tail = (head[0], head[1], head[2] + 0.15)
        eb.roll = 0.0
        if parent:
            eb.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")

    yellow = flat_material("MinerYellow", (1.0, 0.74, 0.16))
    steel = flat_material("MinerSteel", (0.46, 0.49, 0.55))
    dark = flat_material("MinerDark", (0.26, 0.27, 0.31))
    glass = flat_material("MinerVisor", (0.12, 0.36, 0.46))
    cyan = flat_material("MinerEyes", (0.55, 0.95, 1.0))
    lens = flat_material("MinerLens", (1.0, 0.95, 0.7))
    green = flat_material("MinerStatus", (0.35, 1.0, 0.45))
    edge = 0.018

    def part(name, bone, build, mats):
        bm = bmesh.new()
        build(bm)
        obj = make_mesh(name, bm, mats, parent=rig, outline=edge)
        obj.parent_type = "BONE"
        obj.parent_bone = bone
        b = arm_data.bones[bone]
        # 骨頭父子關係以骨尾為基準；把靜止姿勢的骨頭矩陣反過來，頂點才留在建模時的世界座標
        obj.matrix_parent_inverse = (rig.matrix_world @ b.matrix_local
                                     @ Matrix.Translation((0, b.length, 0))).inverted()
        return obj

    for side, bone in ((1, "leg.L"), (-1, "leg.R")):
        def leg(bm, s=side):
            box(bm, (0.15 * s, 0, 0.27), (0.14, 0.16, 0.3), 0)
            box(bm, (0.15 * s, -0.04, 0.06), (0.2, 0.28, 0.12), 1)
        part(f"Miner_{bone}", bone, leg, [steel, dark])

    def hips(bm):
        box(bm, (0, 0, 0.47), (0.42, 0.3, 0.12), 0)
    part("Miner_Hips", "hips", hips, [dark])

    def torso(bm):
        box(bm, (0, 0, 0.74), (0.56, 0.42, 0.4), 0)               # 機身
        box(bm, (0, -0.215, 0.73), (0.34, 0.04, 0.24), 1)         # 胸甲
        box(bm, (0.1, -0.24, 0.8), (0.07, 0.03, 0.06), 2)         # 狀態燈
        box(bm, (-0.06, -0.24, 0.8), (0.1, 0.03, 0.04), 3)
        box(bm, (0, 0.29, 0.75), (0.42, 0.18, 0.36), 3)           # 背包電池
        box(bm, (0, 0.385, 0.75), (0.28, 0.02, 0.06), 2)          # 背包電量條
        cylinder(bm, (0.13, 0.3, 0.98), 0.05, 0.12, 1, segments=6)  # 排氣管
        cylinder(bm, (-0.13, 0.3, 0.98), 0.05, 0.12, 1, segments=6)
        cylinder(bm, (0, 0, 0.96), 0.11, 0.08, 1, segments=8)     # 脖子
    part("Miner_Torso", "torso", torso, [yellow, dark, green, steel])

    def head(bm):
        cylinder(bm, (0, 0, 1.06), 0.22, 0.2, 0, segments=8)      # 頭
        cylinder(bm, (0, 0, 1.18), 0.25, 0.06, 1, segments=8)     # 安全帽帽沿
        cylinder(bm, (0, 0, 1.25), 0.22, 0.08, 1, segments=8, radius2=0.13)  # 帽頂
        box(bm, (0, -0.2, 1.06), (0.32, 0.07, 0.1), 2)            # 護目鏡
        box(bm, (0.075, -0.24, 1.06), (0.07, 0.02, 0.04), 3)      # 眼睛
        box(bm, (-0.075, -0.24, 1.06), (0.07, 0.02, 0.04), 3)
        # 頭燈燈殼（燈光由遊戲程式加 Light2D）：沿 -Y 的圓筒＋鏡片
        cylinder(bm, (0, -0.2, 1.22), 0.075, 0.12, 0, segments=8, rot=rot_x(90))
        cylinder(bm, (0, -0.27, 1.22), 0.06, 0.02, 4, segments=8, rot=rot_x(90))
    part("Miner_Head", "head", head, [steel, yellow, glass, cyan, lens])

    def arm_left(bm):
        ico(bm, (0.34, 0, 0.86), 0.1, 0)
        box(bm, (0.37, 0, 0.7), (0.1, 0.1, 0.26), 1)
        box(bm, (0.37, -0.02, 0.54), (0.13, 0.15, 0.1), 2)
    part("Miner_arm.L", "arm.L", arm_left, [yellow, steel, dark])

    def arm_right(bm):
        ico(bm, (-0.34, 0, 0.86), 0.1, 0)
        box(bm, (-0.37, 0, 0.72), (0.1, 0.1, 0.22), 1)
        box(bm, (-0.37, -0.1, 0.6), (0.11, 0.26, 0.1), 1)         # 前臂往前端著槍
        cylinder(bm, (-0.37, -0.3, 0.6), 0.065, 0.3, 2, segments=8, rot=rot_x(90))  # 能量槍
        cylinder(bm, (-0.37, -0.46, 0.6), 0.075, 0.04, 3, segments=8, rot=rot_x(90))  # 槍口
    part("Miner_arm.R", "arm.R", arm_right, [yellow, steel, dark, cyan])

    def pose(f, length, fn):
        t = (f - 1) / length * 2 * math.pi
        for pb in rig.pose.bones:
            pb.rotation_mode = "XYZ"
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)
        fn(t)
        for pb in rig.pose.bones:
            pb.keyframe_insert("location", frame=f)
            pb.keyframe_insert("rotation_euler", frame=f)

    def idle(t):
        pb = rig.pose.bones
        pb["torso"].location.y = 0.015 * math.sin(t)          # 本地 Y＝世界 Z：呼吸起伏
        pb["head"].rotation_euler.x = math.radians(2.5 * math.sin(t + 0.6))
        pb["arm.L"].rotation_euler.x = math.radians(4 * math.sin(t))
        pb["arm.R"].rotation_euler.x = math.radians(-2 * math.sin(t))

    def run(t):
        pb = rig.pose.bones
        s = math.sin(t)
        pb["hips"].location.y = 0.04 * abs(s)
        pb["torso"].rotation_euler.x = math.radians(10)        # 往前（-Y）傾
        pb["leg.L"].rotation_euler.x = math.radians(35 * s)
        pb["leg.R"].rotation_euler.x = math.radians(-35 * s)
        pb["arm.L"].rotation_euler.x = math.radians(-30 * s)
        pb["arm.R"].rotation_euler.x = math.radians(6 * s)
        pb["head"].rotation_euler.x = math.radians(-6)

    for name, fn, length in (("Idle", idle, 8), ("Run", run, 8)):
        rig.animation_data_clear()
        for f in range(1, length + 2):
            pose(f, length, fn)
        finish_action(rig, name, length)
    rig.animation_data_clear()
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_euler = (0, 0, 0)


# ---------- 史萊姆 ----------

SLIMES = {
    # 顏色、半徑、跳躍高度、壓扁量、裝飾
    "SlimeGreen": dict(color=(0.38, 0.9, 0.22), radius=0.38, hop=0.2, squash=0.2, deco="none"),
    "SlimePurple": dict(color=(0.66, 0.3, 0.98), radius=0.28, hop=0.26, squash=0.16, deco="horns"),
    "SlimeOrange": dict(color=(1.0, 0.5, 0.08), radius=0.6, hop=0.12, squash=0.24, deco="rocks"),
}


def build_slime(name):
    cfg = SLIMES[name]
    r = cfg["radius"]
    root = empty(name)
    body_col = cfg["color"]
    belly_col = tuple(c * 0.72 for c in body_col)
    shine_col = tuple(min(1.0, c * 0.35 + 0.65) for c in body_col)
    mats = [flat_material(f"{name}Body", body_col), flat_material(f"{name}Belly", belly_col),
            flat_material(f"{name}Shine", shine_col)]

    bm = bmesh.new()
    verts = ico(bm, (0, 0, 0), 1.0, 0, subdiv=2)
    for v in verts:
        x, y, z = v.co
        z = max(z, -0.55) + 0.55                      # 底部壓平、底面在 z=0
        zn = z / 1.55
        widen = 1.0 + 0.18 * (1.0 - zn)               # 下寬上窄的軟糖形
        v.co = Vector((x * widen * r, y * widen * r, z * 0.78 * r))
    for f in bm.faces:
        if f.calc_center_median().z < 0.18 * r:
            f.material_index = 1
    height = 1.55 * 0.78 * r
    body = make_mesh(f"{name}_Body", bm, mats, parent=root, outline=0.018)

    # 臉：眼白＋瞳孔＋高光；全部掛在 Body 底下，跟著壓扁拉長
    white = flat_material(f"{name}EyeWhite", (0.97, 0.97, 0.95))
    black = flat_material(f"{name}Pupil", (0.03, 0.03, 0.05))
    bm = bmesh.new()
    def surface_radius(h):
        """高度比例 h（0＝底、1＝頂）處的身體水平半徑，和上面的變形公式一致。"""
        z = h * 1.55 - 0.55
        return math.sqrt(max(0.0, 1 - z * z)) * (1.0 + 0.18 * (1.0 - h)) * r

    eye_h = 0.62
    eye_z = height * eye_h
    eye_x = r * 0.34
    eye_r = r * 0.27
    front = -math.sqrt(max(0.0, surface_radius(eye_h) ** 2 - eye_x ** 2))  # 臉在 -Y 側的表面
    for s in (1, -1):
        ico(bm, (s * eye_x, front + eye_r * 0.2, eye_z), eye_r, 0, subdiv=2, scale=(1, 0.6, 1.2))
        ico(bm, (s * eye_x * 0.95, front - eye_r * 0.25, eye_z - eye_r * 0.05), eye_r * 0.55, 1,
            subdiv=2, scale=(1, 0.5, 1.25))
    if cfg["deco"] == "horns":
        # 生氣的眉毛：斜的黑色長條
        brow_h = eye_h + eye_r * 1.15 / height
        brow_y = -math.sqrt(max(0.0, surface_radius(brow_h) ** 2 - eye_x ** 2))  # 貼著眉毛高度的表面
        for s in (1, -1):
            box(bm, (s * eye_x, brow_y, height * brow_h), (eye_r * 1.6, 0.04, eye_r * 0.38), 1,
                rot=rot_y(-24 * s))
    if cfg["deco"] == "rocks":
        mouth_h = eye_h - eye_r * 1.9 / height
        box(bm, (0, -surface_radius(mouth_h) + 0.01, height * mouth_h), (r * 0.4, 0.06, r * 0.07), 1)  # 嘴
    make_mesh(f"{name}_Face", bm, [white, black], parent=body, outline=0.012)

    bm = bmesh.new()
    ico(bm, (-r * 0.35, -r * 0.35, height * 0.86), r * 0.16, 0, subdiv=1, scale=(1.2, 1, 0.5))
    make_mesh(f"{name}_Shine", bm, [mats[2]], parent=body)

    if cfg["deco"] == "horns":
        horn = flat_material(f"{name}Horn", (0.88, 0.78, 1.0))
        bm = bmesh.new()
        for s in (1, -1):
            cylinder(bm, (s * r * 0.42, 0, height * 0.98), r * 0.22, r * 0.8, 0, segments=5, radius2=0.0,
                     rot=rot_y(28 * s))
        make_mesh(f"{name}_Horns", bm, [horn], parent=body, outline=0.012)
    elif cfg["deco"] == "rocks":
        rock = flat_material(f"{name}Rock", (0.45, 0.4, 0.36))
        bm = bmesh.new()
        rng = random.Random(7)
        for i in range(5):
            a = i / 5 * 2 * math.pi + 0.4
            rr = r * 0.45
            c = (math.cos(a) * rr, math.sin(a) * rr * 0.9, height * (0.82 + 0.06 * (i % 2)))
            vs = ico(bm, c, r * rng.uniform(0.13, 0.19), 0, subdiv=0)
            for v in vs:
                v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * r * 0.03
        make_mesh(f"{name}_Rocks", bm, [rock], parent=body, outline=0.015)

    # Move：8 幀一跳。根物件不動，動 Body 的高度與縮放
    hop, sq = cfg["hop"], cfg["squash"]
    keys = [  # (幀, 高度比例, xy 縮放, z 縮放, 前傾角)
        (1, 0.0, 1 + sq * 0.8, 1 - sq, 0),
        (3, 0.45, 1 - sq * 0.45, 1 + sq * 0.9, -8),
        (5, 1.0, 1.0, 1.0, 0),
        (7, 0.25, 1 - sq * 0.25, 1 + sq * 0.5, 6),
        (8, 0.0, 1 + sq * 0.6, 1 - sq * 0.7, 0),
        (9, 0.0, 1 + sq * 0.8, 1 - sq, 0),
    ]
    for f, h, sxy, sz, lean in keys:
        body.location = (0, 0, h * hop)
        body.scale = (sxy, sxy, sz)
        body.rotation_euler = (math.radians(lean), 0, 0)
        body.keyframe_insert("location", frame=f)
        body.keyframe_insert("scale", frame=f)
        body.keyframe_insert("rotation_euler", frame=f)
    finish_action(body, "Move", 8)
    body.location = (0, 0, 0)
    body.scale = (1, 1, 1)
    body.rotation_euler = (0, 0, 0)


# ---------- Bullet：能量子彈（俯視，朝 +X＝畫面右邊） ----------

def build_bullet():
    root = empty("Bullet")
    body = empty("Bullet_Body", root)
    mat = vcol_material("BulletEnergy")
    bm = bmesh.new()
    ico(bm, (0.02, 0, 0), 0.07, 0, subdiv=2, scale=(2.2, 1, 0.8))          # 彈頭
    for i, (x, rr) in enumerate(((-0.13, 0.04), (-0.19, 0.028))):         # 拖尾光點
        ico(bm, (x, 0.012 * (1 if i % 2 else -1), 0), rr, 0, subdiv=1, scale=(1.6, 1, 0.8))

    def color(f):
        x = f.calc_center_median().x
        if x > 0.07:
            return (0.95, 1.0, 1.0)
        if x > -0.06:
            return (0.55, 0.95, 1.0)
        return (0.25, 0.75, 1.0)
    core = make_mesh("Bullet_Core", bm, [mat], parent=body, face_colors=color)
    core.scale = (0.78, 0.78, 0.78)  # 脈動放大時頭尾仍要離 0.5m 格子邊緣幾 px
    # Idle：4 幀脈動
    for f, s in ((1, 1.0), (2, 1.08), (3, 1.0), (4, 0.94), (5, 1.0)):
        body.scale = (s, 1.0 / math.sqrt(s), 1.0)
        body.keyframe_insert("scale", frame=f)
    finish_action(body, "Idle", 4)
    body.scale = (1, 1, 1)


# ---------- Battery：補血電池（旋轉＋上下浮動） ----------

def build_battery():
    root = empty("Battery")
    body = empty("Battery_Body", root)
    shell = flat_material("BatteryShell", (0.3, 0.33, 0.4))
    charge = flat_material("BatteryCharge", (0.3, 1.0, 0.4))
    metal = flat_material("BatteryMetal", (0.75, 0.77, 0.8))
    bolt = flat_material("BatteryBolt", (1.0, 0.88, 0.2))
    bm = bmesh.new()
    cylinder(bm, (0, 0, 0.27), 0.14, 0.36, 0, segments=8)
    cylinder(bm, (0, 0, 0.27), 0.148, 0.16, 1, segments=8)               # 電量環
    cylinder(bm, (0, 0, 0.47), 0.06, 0.05, 2, segments=8)                # 正極
    cylinder(bm, (0, 0, 0.1), 0.15, 0.03, 2, segments=8)                 # 底座金屬環
    cylinder(bm, (0, 0, 0.44), 0.15, 0.03, 2, segments=8)
    # 閃電標誌：在 XZ 平面畫多邊形再往 -Y 擠出
    pts = [(0.02, 0.36), (-0.06, 0.25), (-0.005, 0.25), (-0.035, 0.17), (0.06, 0.29), (0.005, 0.29), (0.035, 0.36)]
    for side in (-1, 1):
        y0 = 0.148 * side
        vs = [bm.verts.new((x * -side, y0, z)) for x, z in pts]
        f = bm.faces.new(vs if side < 0 else list(reversed(vs)))
        f.material_index = 3
        ext = bmesh.ops.extrude_face_region(bm, geom=[f])
        moved = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, verts=moved, vec=(0, 0.018 * side, 0))
        for e in ext["geom"]:
            if isinstance(e, bmesh.types.BMFace):
                e.material_index = 3
    make_mesh("Battery_Mesh", bm, [shell, charge, metal, bolt], parent=body, outline=0.014)
    set_key_interpolation("LINEAR")
    for f in range(1, 10):
        t = (f - 1) / 8
        body.rotation_euler = (0, 0, t * 2 * math.pi)
        body.location = (0, 0, 0.04 * math.sin(t * 2 * math.pi))
        body.keyframe_insert("rotation_euler", frame=f)
        body.keyframe_insert("location", frame=f)
    set_key_interpolation("BEZIER")
    finish_action(body, "Idle", 8)
    body.rotation_euler = (0, 0, 0)
    body.location = (0, 0, 0)


# ---------- Floor：俯視無縫礦坑地板（4 種變化，邊緣完全相同） ----------

TILE = 2.0       # 一格 tile 的寬度（公尺）＝ cellSize / PPU
GRID = 48        # 一格 tile 的網格數（越密石縫越不鋸齒）
STONES = 5       # 每邊幾顆石板（週期 Voronoi）


def build_floor():
    root = empty("Floor")
    rng = random.Random(1234)
    seeds = []
    for i in range(STONES):
        for j in range(STONES):
            seeds.append(((i + rng.uniform(0.15, 0.85)) * TILE / STONES,
                          (j + rng.uniform(0.15, 0.85)) * TILE / STONES))
    palette = [(0.42, 0.39, 0.37), (0.38, 0.36, 0.35), (0.45, 0.4, 0.35), (0.36, 0.36, 0.39),
               (0.41, 0.37, 0.33)]
    stone_col = [jitter(rng, rng.choice(palette), 0.02) for _ in seeds]
    stone_tilt = [(rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04)) for _ in seeds]
    vjit = {(i, j): rng.uniform(-0.006, 0.006) for i in range(GRID) for j in range(GRID)}

    def voronoi(x, y):
        """週期 Voronoi：回傳 (最近的石板編號, 到邊界的距離)。"""
        px, py = x % TILE, y % TILE
        best = []
        for k, (sx, sy) in enumerate(seeds):
            for ox in (-TILE, 0, TILE):
                for oy in (-TILE, 0, TILE):
                    best.append(((px - sx - ox) ** 2 + (py - sy - oy) ** 2, k))
        best.sort()
        return best[0][1], math.sqrt(best[1][0]) - math.sqrt(best[0][0])

    def height(i, j):
        x, y = i * TILE / GRID, j * TILE / GRID
        k, edge = voronoi(x, y)
        tx, ty = stone_tilt[k]
        sx, sy = seeds[k]
        dx = ((x - sx + TILE / 2) % TILE) - TILE / 2
        dy = ((y - sy + TILE / 2) % TILE) - TILE / 2
        top = 0.05 + tx * dx + ty * dy
        groove = min(1.0, edge / 0.09)               # 石縫附近往下塌
        return top * groove + vjit[(i % GRID, j % GRID)]

    # 鋪 3×3 格，中央那格就是相機看到的範圍；外圍兩格只是讓邊緣取樣與中央一致
    n = GRID * 3
    half = n // 2
    bm = bmesh.new()
    grid = {}
    for i in range(-half, half + 1):
        for j in range(-half, half + 1):
            grid[i, j] = bm.verts.new((i * TILE / GRID, j * TILE / GRID, height(i, j)))
    for i in range(-half, half):
        for j in range(-half, half):
            a, b, c, d = grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]
            if (i + j) % 2 == 0:                     # 對角線交錯，三角面感比較自然
                bm.faces.new((a, b, c))
                bm.faces.new((a, c, d))
            else:
                bm.faces.new((a, b, d))
                bm.faces.new((b, c, d))
    crack = (0.16, 0.14, 0.13)

    def color(f):
        cx, cy, _ = f.calc_center_median()
        k, edge = voronoi(cx, cy)
        if edge < 0.035:
            return crack
        shade = 1.0 + max(-0.08, min(0.06, (f.normal.x - f.normal.y) * 0.4))  # 斜面略深淺，增加石面質感
        return tuple(min(1.0, v * shade) for v in stone_col[k])
    make_mesh("Floor_Ground", bm, [vcol_material("FloorStone")], parent=root, face_colors=color)

    # 4 種變化：0＝素面；1～3 在中央（離邊 ≥0.3m）加碎石、礦點，邊緣不變所以彼此可混鋪
    variants = 4
    frames_holder(root, variants)
    pebble = vcol_material("FloorPebble")
    for v in range(1, variants):
        r2 = random.Random(100 + v)
        bm = bmesh.new()
        ore = [(1.0, 0.78, 0.2), (0.3, 0.85, 1.0), (0.9, 0.4, 1.0)][v - 1]
        cols = []
        for p in range(4 + v * 2):
            c = (r2.uniform(-0.7, 0.7), r2.uniform(-0.7, 0.7), 0.04)
            is_ore = p % 3 == 0
            rr = r2.uniform(0.03, 0.06) if is_ore else r2.uniform(0.04, 0.09)
            vs = ico(bm, c, rr, 0, subdiv=0, scale=(1, r2.uniform(0.7, 1.1), 0.6))
            col = ore if is_ore else jitter(r2, (0.5, 0.46, 0.42), 0.04)
            cols.append((set(vs), col))

        def pcolor(f, cols=cols):
            for vs, col in cols:
                if f.verts[0] in vs:
                    return col
            return (0.3, 0.3, 0.3)
        obj = make_mesh(f"Floor_Var{v}", bm, [pebble], parent=root, face_colors=pcolor)
        show_only_on_frame([obj], v + 1, variants)


# ---------- Rock：障礙岩石（4 種變化） ----------

def lumpy_rock(bm, rng, center, size, mi=0, subdiv=1):
    """不規則的低多邊形石頭，底部壓平在 z=center.z。"""
    vs = ico(bm, (0, 0, 0), 1.0, mi, subdiv=subdiv)
    for v in vs:
        v.co *= rng.uniform(0.8, 1.15)
        z = max(v.co.z, -0.35)
        v.co = Vector((center[0] + v.co.x * size[0], center[1] + v.co.y * size[1],
                       center[2] + (z + 0.35) * size[2]))
    return vs


def build_rock():
    root = empty("Rock")
    variants = 4
    frames_holder(root, variants)
    base = (0.53, 0.48, 0.43)
    gold = (1.0, 0.78, 0.25)
    specs = [
        [((0, 0, 0), (0.62, 0.52, 0.45))],                                        # 大圓石
        [((0.05, 0, 0), (0.4, 0.36, 0.75))],                                       # 高石柱
        [((-0.28, 0.05, 0), (0.42, 0.38, 0.38)), ((0.33, -0.08, 0), (0.3, 0.28, 0.28))],  # 兩顆
        [((0, 0, 0), (0.7, 0.5, 0.25))],                                           # 扁平含金礦
    ]
    for v, parts in enumerate(specs):
        rng = random.Random(50 + v)
        bm = bmesh.new()
        for c, s in parts:
            lumpy_rock(bm, rng, c, s)
        tints = {}

        def color(f, rng=rng, v=v):
            key = f.index
            if key not in tints:
                if v == 3 and rng.random() < 0.12 and f.normal.z > 0.2:
                    tints[key] = gold
                else:
                    lift = 0.06 * f.normal.z                     # 朝上的面略亮
                    tints[key] = jitter(rng, tuple(c + lift for c in base), 0.035)
            return tints[key]
        obj = make_mesh(f"Rock_Var{v}", bm, [vcol_material(f"RockMat{v}")], parent=root,
                        outline=0.02, face_colors=color)
        show_only_on_frame([obj], v + 1, variants)


# ---------- Crystal：發光水晶裝飾（4 種顏色、排列） ----------

def build_crystal():
    root = empty("Crystal")
    variants = 4
    frames_holder(root, variants)
    colors = [(0.35, 0.95, 1.0), (0.95, 0.38, 0.95), (1.0, 0.85, 0.3), (0.45, 1.0, 0.55)]
    for v in range(variants):
        rng = random.Random(300 + v)
        bm = bmesh.new()
        lumpy_rock(bm, rng, (0, 0, 0), (0.3, 0.26, 0.14), mi=0, subdiv=1)
        count = 3 + v % 3
        for k in range(count):
            a = k / count * 2 * math.pi + rng.uniform(-0.3, 0.3)
            tilt = 0 if k == 0 else rng.uniform(18, 35)
            length = rng.uniform(0.5, 0.75) if k == 0 else rng.uniform(0.25, 0.5)
            radius = rng.uniform(0.08, 0.11) if k == 0 else rng.uniform(0.05, 0.08)
            off = 0 if k == 0 else 0.1
            rot = rot_z(math.degrees(a)) @ rot_y(tilt)
            base = Vector((math.cos(a) * off, math.sin(a) * off, 0.04))
            # 六角柱＋尖頂
            col_v = cylinder(bm, base + rot @ Vector((0, 0, length / 2)), radius, length, 1,
                             segments=6, rot=rot)
            tip_v = cylinder(bm, base + rot @ Vector((0, 0, length + radius * 0.9)), radius, radius * 1.8, 2,
                             segments=6, radius2=0.0, rot=rot)
        c = colors[v]
        light = tuple(min(1.0, x * 0.45 + 0.55) for x in c)
        mats = [flat_material(f"CrystalRock{v}", (0.33, 0.3, 0.32)), flat_material(f"CrystalBody{v}", c),
                flat_material(f"CrystalTip{v}", light)]
        obj = make_mesh(f"Crystal_Var{v}", bm, mats, parent=root, outline=0.015)
        show_only_on_frame([obj], v + 1, variants)


BUILDERS = {
    "Miner": build_miner,
    "SlimeGreen": lambda: build_slime("SlimeGreen"),
    "SlimePurple": lambda: build_slime("SlimePurple"),
    "SlimeOrange": lambda: build_slime("SlimeOrange"),
    "Bullet": build_bullet,
    "Battery": build_battery,
    "Floor": build_floor,
    "Rock": build_rock,
    "Crystal": build_crystal,
}


def main():
    args = parse_args()
    if args.target not in BUILDERS:
        raise SystemExit(f"[glowshaft_models] 不認得的素材：{args.target}（可用：{', '.join(BUILDERS)}）")
    reset_scene()
    BUILDERS[args.target]()
    bpy.context.scene.frame_set(1)
    print(f"[glowshaft_models] 已建立 {args.target}")


if __name__ == "__main__":
    main()
