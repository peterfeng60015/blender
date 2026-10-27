"""把 3D 物件批次渲染成 Unity 2D 用的多方向 sprite sheet（含法線圖）。

適用 Blender 5.2 LTS。只在背景模式執行，不會存回 .blend（--save-demo 除外）。

用法（參數放在 `--` 之後）：
    # 不需要任何 .blend，先用內建示範場景跑一次完整流程
    blender -b --python Tools/Blender/render_sprites.py -- --demo --out ./SpriteOut

    # 渲染自己的場景
    blender -b hero.blend --python Tools/Blender/render_sprites.py -- \
        --target Hero --anim-object Hero_Armature --actions Idle,Run --out //SpriteOut

輸出（{out} 底下）：
    {name}_{action}.png        顏色 sheet：一列一個方向（第一列是 S），一欄一幀
    {name}_{action}_n.png      法線 sheet（--no-normal 可關閉），排列與顏色 sheet 相同
    {name}_sprites.json        格子大小、方向順序、幀數、fps、建議 PPU 與 pivot
    _frames/...                單幀 PNG（除錯用）

方向約定：角色正面朝 Blender 的 -Y。S＝正面朝鏡頭，E＝面向畫面右邊，依此類推。
"""

import argparse
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

COMPASS = {
    4: ["S", "E", "N", "W"],
    8: ["S", "SE", "E", "NE", "N", "NW", "W", "SW"],
}
RIG_NAME = "SpriteRig"
OUTLINE_MOD = "SpriteOutline"
ALBEDO_PROP = "sprite_albedo"
ALBEDO_BSDFS = ("BSDF_PRINCIPLED", "BSDF_DIFFUSE", "BSDF_TOON")
MAX_SHEET_SIZE = 8192


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="render_sprites.py")
    p.add_argument("--demo", action="store_true", help="建立示範角色與兩個動作（Idle、Hop）")
    p.add_argument("--save-demo", default="", help="把示範場景另存成這個 .blend（渲染前存）")
    p.add_argument("--target", default="", help="要渲染的根物件名稱（旋轉中心＝它的原點）")
    p.add_argument("--anim-object", default="", help="掛動作的物件（通常是 Armature），預設＝target")
    p.add_argument("--actions", default="", help="逗號分隔的 Action 名稱；留空＝用場景目前的幀範圍")
    p.add_argument("--name", default="", help="輸出檔名前綴，預設＝target 名稱")
    p.add_argument("--out", default="//SpriteOut", help="輸出資料夾；// 開頭表示相對 .blend 所在處")
    p.add_argument("--dirs", type=int, default=8, help="方向數（4、8 用羅盤命名，其他用角度）")
    p.add_argument("--size", type=int, default=128, help="每格像素（正方形）")
    p.add_argument("--elevation", type=float, default=30.0, help="相機俯角（度），0＝正側面")
    p.add_argument("--ortho-scale", type=float, default=0.0, help="正交相機寬度（公尺），0＝自動")
    p.add_argument("--margin", type=float, default=1.1, help="自動取景時的留白倍率")
    p.add_argument("--step", type=int, default=1, help="每隔幾幀取一幀")
    p.add_argument("--engine", choices=["eevee", "workbench", "cycles"], default="eevee")
    p.add_argument("--samples", type=int, default=16, help="EEVEE／Cycles 取樣數")
    p.add_argument("--pixel", action="store_true", help="像素風：關抗鋸齒（取樣 1、濾鏡寬 0）")
    p.add_argument("--albedo", action="store_true",
                   help="顏色 pass 只輸出固有色、不烘焙明暗，光影交給 Unity 2D 光照")
    p.add_argument("--no-normal", action="store_true", help="不輸出法線 sheet")
    p.add_argument("--keep-scene-lights", action="store_true",
                   help="保留場景原有燈光（預設只用腳本建立、跟著相機轉的主光）")
    args = p.parse_args(argv)
    if args.dirs < 1:
        p.error("--dirs 至少 1")
    if args.size < 1:
        p.error("--size 至少 1")
    if args.step < 1:
        p.error("--step 至少 1")
    if not -89.0 <= args.elevation <= 89.0:
        p.error("--elevation 要在 -89～89 之間")
    if not args.demo and not args.target:
        p.error("沒有 --demo 時必須給 --target")
    return args


# ---------- 示範場景 ----------

def enable_nodes(idblock):
    # 5.0 起材質／World 一律用節點，use_nodes 已棄用
    if bpy.app.version < (5, 0, 0):
        idblock.use_nodes = True


def toon_material(name, color):
    """Diffuse → Shader to RGB → 兩階 Color Ramp：只有 EEVEE 支援 Shader to RGB。"""
    mat = bpy.data.materials.new(name)
    enable_nodes(mat)
    nt = mat.node_tree
    nt.nodes.clear()
    diffuse = nt.nodes.new("ShaderNodeBsdfDiffuse")
    to_rgb = nt.nodes.new("ShaderNodeShaderToRGB")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    ramp.color_ramp.interpolation = "CONSTANT"
    dark, lit = ramp.color_ramp.elements
    dark.position = 0.0
    dark.color = (*[c * 0.45 for c in color], 1.0)
    lit.position = 0.5
    lit.color = (*color, 1.0)
    nt.links.new(diffuse.outputs["BSDF"], to_rgb.inputs["Shader"])
    nt.links.new(to_rgb.outputs["Color"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], out.inputs["Surface"])
    mat[ALBEDO_PROP] = (*color, 1.0)  # --albedo 用：色階的亮面色就是固有色
    mat.diffuse_color = (*color, 1.0)
    return mat


def outline_material():
    mat = bpy.data.materials.new("SpriteOutlineMat")
    enable_nodes(mat)
    nt = mat.node_tree
    nt.nodes.clear()
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (0.02, 0.02, 0.03, 1.0)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    mat.use_backface_culling = True
    return mat


def mesh_object(name, build, materials, parent=None):
    bm = bmesh.new()
    build(bm)
    for f in bm.faces:
        # 圓柱／圓錐的蓋面是 n-gon，保持平面，避免側面法線被蓋面平均成上下漸變
        f.smooth = len(f.verts) <= 4
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in materials:
        me.materials.append(m)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    return obj


def add_outline(obj, thickness=0.025):
    """反轉法線外殼描邊：Solidify 往外長、翻法線、用第 2 個材質槽（背面剔除）。"""
    mod = obj.modifiers.new(OUTLINE_MOD, "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = 1.0
    mod.use_flip_normals = True
    mod.material_offset = 1


def build_demo(scene):
    """清空場景，建一個「身體＋頭＋藍色鼻子」的角色：鼻子指向 -Y，方便看出朝向。"""
    for obj in list(scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    scene.world = scene.world or bpy.data.worlds.new("World")
    enable_nodes(scene.world)
    bg = scene.world.node_tree.nodes.get("Background") if scene.world.node_tree else None
    if bg:
        bg.inputs["Color"].default_value = (0.05, 0.05, 0.05, 1.0)

    root = bpy.data.objects.new("Hero", None)
    scene.collection.objects.link(root)
    edge = outline_material()
    skin = toon_material("HeroSkin", (0.9, 0.45, 0.2))
    nose_mat = toon_material("HeroNose", (0.2, 0.4, 0.95))

    def body_shape(bm):
        bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.35, radius2=0.3,
                              depth=0.9, matrix=Matrix.Translation((0, 0, 0.45)))
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=0.3,
                                  matrix=Matrix.Translation((0, 0, 1.15)))

    def nose_shape(bm):
        # 圓錐預設沿 +Z，繞 X 轉 90° 讓尖端指向 -Y
        bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.1, radius2=0.0, depth=0.3,
                              matrix=Matrix.Translation((0, -0.38, 1.15)) @ Matrix.Rotation(math.radians(90), 4, "X"))

    body = mesh_object("Hero_Body", body_shape, [skin, edge], parent=root)
    nose = mesh_object("Hero_Nose", nose_shape, [nose_mat, edge], parent=body)
    add_outline(body)
    add_outline(nose)

    # 動作掛在 Hero_Body 上，根物件 Hero 保持不動，旋轉中心與 pivot 才固定
    def make_action(name, keys, length):
        body.animation_data_clear()
        for frame, z in keys:
            body.location.z = z
            body.keyframe_insert("location", index=2, frame=frame)
        act = body.animation_data.action
        act.name = name
        act.use_fake_user = True
        act.use_frame_range = True
        act.frame_start = 1
        act.frame_end = length  # 最後一幀等於第一幀時不重複輸出，才能無縫循環
        act.use_cyclic = True
        return act

    make_action("Idle", [(1, 0.0), (9, 0.06), (17, 0.0)], 16)
    make_action("Hop", [(1, 0.0), (4, 0.35), (7, 0.45), (10, 0.35), (13, 0.0)], 12)
    body.location.z = 0.0
    return "Hero", "Hero_Body", ["Idle", "Hop"]


# ---------- 取景 ----------

def descendants(obj):
    out = [obj]
    for child in obj.children:
        out.extend(descendants(child))
    return out


def assign_action(anim_obj, action):
    ad = anim_obj.animation_data or anim_obj.animation_data_create()
    ad.action = action
    # Blender 4.4 起 Action 有 slot；沒自動選到時用第一個
    if getattr(ad, "action_slot", None) is None and getattr(action, "slots", None):
        ad.action_slot = action.slots[0]


def action_frames(action, scene, step):
    if action is None:
        start, end = scene.frame_start, scene.frame_end
    else:
        start, end = (int(round(v)) for v in action.frame_range)
    return list(range(start, end + 1, step))


def measure(scene, target, anim_obj, actions, step):
    """掃過所有動作的所有幀，回傳相對原點的 (水平最大半徑, zmin, zmax)。"""
    origin = target.matrix_world.translation.copy()
    meshes = [o for o in descendants(target) if o.type in {"MESH", "CURVE", "SURFACE", "META", "FONT"}]
    if not meshes:
        raise SystemExit(f"{target.name} 底下沒有可渲染的物件")
    radius, zmin, zmax = 0.0, math.inf, -math.inf
    for action in actions:
        if action is not None:
            assign_action(anim_obj, action)
        for frame in action_frames(action, scene, step):
            scene.frame_set(frame)
            depsgraph = bpy.context.evaluated_depsgraph_get()
            for obj in meshes:
                ev = obj.evaluated_get(depsgraph)
                for corner in ev.bound_box:
                    p = ev.matrix_world @ Vector(corner) - origin
                    radius = max(radius, math.hypot(p.x, p.y))
                    zmin = min(zmin, p.z)
                    zmax = max(zmax, p.z)
    return origin, radius, zmin, zmax


def setup_rig(scene, origin, aim_height, elevation, ortho_scale, distance):
    """空物件放在原點，相機與主光都是它的子物件；轉 rig＝讓角色相對鏡頭轉向。"""
    rig = bpy.data.objects.new(RIG_NAME, None)
    scene.collection.objects.link(rig)
    rig.location = origin

    cam_data = bpy.data.cameras.new("SpriteCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam_data.clip_start = 0.01
    cam_data.clip_end = distance * 2.0
    cam = bpy.data.objects.new("SpriteCam", cam_data)
    scene.collection.objects.link(cam)
    cam.parent = rig
    e = math.radians(elevation)
    # 方向 0 時相機在 -Y 側往 +Y 看（看到角色正面），再往下俯 elevation 度
    cam.location = (0.0, -distance * math.cos(e), aim_height + distance * math.sin(e))
    cam.rotation_euler = (math.radians(90.0) - e, 0.0, 0.0)
    scene.camera = cam

    sun_data = bpy.data.lights.new("SpriteKey", "SUN")
    sun_data.energy = 3.0
    sun_data.use_shadow = False
    sun = bpy.data.objects.new("SpriteKey", sun_data)
    scene.collection.objects.link(sun)
    sun.parent = rig
    sun.rotation_euler = (math.radians(50.0), 0.0, math.radians(-35.0))  # 左前上方打光
    return rig


# ---------- 渲染設定 ----------

def set_engine(scene, engine):
    if engine == "eevee":
        # 5.x 叫 BLENDER_EEVEE；4.2～4.5 叫 BLENDER_EEVEE_NEXT
        ids = {i.identifier for i in scene.render.bl_rna.properties["engine"].enum_items}
        scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in ids else "BLENDER_EEVEE_NEXT"
    elif engine == "cycles":
        scene.render.engine = "CYCLES"
    else:
        scene.render.engine = "BLENDER_WORKBENCH"


def setup_render(scene, args):
    r = scene.render
    r.resolution_x = r.resolution_y = args.size
    r.resolution_percentage = 100
    r.pixel_aspect_x = r.pixel_aspect_y = 1.0
    r.film_transparent = True
    r.use_freestyle = False
    r.dither_intensity = 0.0  # 預設 1.0 會在 8-bit 輸出加雜訊，干擾法線值與像素風
    if hasattr(r.image_settings, "media_type"):  # 5.0 起要先設 media_type
        r.image_settings.media_type = "IMAGE"
    r.image_settings.file_format = "PNG"
    r.image_settings.color_mode = "RGBA"
    r.image_settings.color_depth = "8"
    samples = 1 if args.pixel else args.samples
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = samples
    if hasattr(scene, "cycles"):
        scene.cycles.samples = samples
        if args.pixel:
            scene.cycles.filter_width = 0.01
    if args.pixel:
        r.filter_size = 0.0
        scene.display.render_aa = "OFF"  # Workbench 的抗鋸齒


def color_pass(scene, args):
    set_engine(scene, args.engine)
    vs = scene.view_settings
    vs.view_transform = "Standard"  # 預設 AgX 會改變顏色
    vs.look = "None"
    vs.exposure = 0.0
    vs.gamma = 1.0
    if args.albedo and args.engine == "workbench":
        # Workbench 不讀節點：平光＋貼圖色，沒貼圖的材質用 apply_albedo 寫入的 diffuse_color
        sh = scene.display.shading
        sh.light = "FLAT"
        sh.color_type = "TEXTURE"
        sh.show_cavity = False
        sh.show_shadows = False
        sh.show_specular_highlight = False
    bpy.context.view_layer.material_override = None
    set_outline_render(scene, True)


def upstream_node(socket):
    """socket 實際接到的節點，會穿過 Reroute；沒接線回傳 None。"""
    while socket.is_linked:
        node = socket.links[0].from_node
        if node.type != "REROUTE":
            return node
        socket = node.inputs[0]
    return None


def find_bsdf(socket, seen=None):
    """從 Surface 往上游找實際接到輸出的 BSDF，穿過 Mix／Add Shader、Reroute、Group。"""
    seen = set() if seen is None else seen
    if not socket.is_linked:
        return None
    link = socket.links[0]
    node = link.from_node
    if node.as_pointer() in seen or node.mute:
        return None
    seen.add(node.as_pointer())
    if node.type in ALBEDO_BSDFS:
        return node
    if node.type == "GROUP" and node.node_tree:
        gout = next((n for n in node.node_tree.nodes
                     if n.type == "GROUP_OUTPUT" and n.is_active_output), None)
        if gout is None:
            return None
        return find_bsdf(gout.inputs.get(link.from_socket.identifier) or gout.inputs[0], seen)
    for inp in node.inputs:
        if inp.type == "SHADER" or node.type == "REROUTE":
            hit = find_bsdf(inp, seen)
            if hit:
                return hit
    return None


def albedo_color_input(nt, emit, mat, bsdf):
    """把固有色接到 emit 的 Color。

    優先序：自訂屬性 sprite_albedo → 接到輸出的 Principled／Diffuse／Toon BSDF 的顏色
    （有接貼圖就沿用那條線；BSDF 在 Group 裡時拿不到內部連線，只取預設色）→ Viewport Display 顏色。
    """
    color = emit.inputs["Color"]
    if ALBEDO_PROP in mat:
        v = tuple(mat[ALBEDO_PROP])
        color.default_value = (*v[:3], v[3] if len(v) > 3 else 1.0)
    elif bsdf is not None:
        src = bsdf.inputs.get("Base Color") or bsdf.inputs["Color"]
        if src.is_linked and bsdf.id_data == nt:
            nt.links.new(src.links[0].from_socket, color)
        else:
            color.default_value = src.default_value
    else:
        color.default_value = mat.diffuse_color
    if not color.is_linked:
        mat.diffuse_color = color.default_value  # Workbench 只看這個顏色


def apply_albedo(target):
    """把 target 底下所有材質的每個輸出都改成 Emission(固有色)，透明度照舊。

    只改記憶體裡的資料，腳本不存檔，所以不會動到 .blend。已經直接接 Emission 的輸出
    （例如描邊）保持原樣；linked library 的材質無法修改，會略過並警告。
    """
    mats = {slot.material for obj in descendants(target) for slot in obj.material_slots if slot.material}
    for mat in mats:
        nt = mat.node_tree
        if nt is None:
            continue
        if mat.library:
            print(f"[render_sprites] 警告：{mat.name} 來自 linked library，--albedo 略過它")
            continue
        for out in [n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"]:
            surface = out.inputs["Surface"]
            src = upstream_node(surface)
            if src is not None and src.type == "EMISSION":
                continue
            bsdf = find_bsdf(surface)
            emit = nt.nodes.new("ShaderNodeEmission")
            emit.inputs["Strength"].default_value = 1.0
            albedo_color_input(nt, emit, mat, bsdf)
            shader = emit.outputs["Emission"]
            alpha = bsdf.inputs.get("Alpha") if bsdf is not None and bsdf.id_data == nt else None
            if alpha is not None and (alpha.is_linked or alpha.default_value < 1.0):
                # 鏤空／半透明材質：用 Transparent 混回原本的 Alpha
                transp = nt.nodes.new("ShaderNodeBsdfTransparent")
                mix = nt.nodes.new("ShaderNodeMixShader")
                if alpha.is_linked:
                    nt.links.new(alpha.links[0].from_socket, mix.inputs["Fac"])
                else:
                    mix.inputs["Fac"].default_value = alpha.default_value
                nt.links.new(transp.outputs["BSDF"], mix.inputs[1])
                nt.links.new(shader, mix.inputs[2])
                shader = mix.outputs["Shader"]
            nt.links.new(shader, surface)


def normal_material():
    """相機空間法線編碼成顏色：朝鏡頭＝(0.5, 0.5, 1)，X 右、Y 上。

    Vector Transform 的 Camera 空間沿用 Cycles 慣例（+Z＝鏡頭看出去的方向），
    所以 Z 要翻號，朝鏡頭的面才會是藍色 1。
    """
    mat = bpy.data.materials.get("SpriteNormalOverride")
    if mat:
        return mat
    mat = bpy.data.materials.new("SpriteNormalOverride")
    enable_nodes(mat)
    nt = mat.node_tree
    nt.nodes.clear()
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    xform = nt.nodes.new("ShaderNodeVectorTransform")
    xform.vector_type = "NORMAL"
    xform.convert_from = "WORLD"
    xform.convert_to = "CAMERA"
    remap = nt.nodes.new("ShaderNodeVectorMath")
    remap.operation = "MULTIPLY_ADD"
    remap.inputs[1].default_value = (0.5, 0.5, -0.5)
    remap.inputs[2].default_value = (0.5, 0.5, 0.5)
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Strength"].default_value = 1.0
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(geo.outputs["Normal"], xform.inputs["Vector"])
    nt.links.new(xform.outputs["Vector"], remap.inputs[0])
    nt.links.new(remap.outputs["Vector"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    return mat


def normal_pass(scene):
    # Workbench 不吃節點材質，法線一律用 EEVEE 渲染
    set_engine(scene, "eevee")
    scene.view_settings.view_transform = "Raw"  # 數值原樣寫進 PNG，不做 sRGB 轉換
    scene.view_settings.look = "None"
    bpy.context.view_layer.material_override = normal_material()
    set_outline_render(scene, False)  # 描邊外殼不算進法線


def set_outline_render(scene, enabled):
    for obj in scene.objects:
        mod = obj.modifiers.get(OUTLINE_MOD) if hasattr(obj, "modifiers") else None
        if mod:
            mod.show_render = enabled


# ---------- 輸出 ----------

def direction_names(count):
    if count in COMPASS:
        return COMPASS[count]
    return [f"a{round(i * 360 / count):03d}" for i in range(count)]


def resolve_out(path):
    if path.startswith("//") and not bpy.data.filepath:
        return os.path.abspath(path[2:])  # 沒有 .blend 時相對目前工作目錄
    return os.path.abspath(bpy.path.abspath(path))


def build_sheet(frame_paths, rows, cols, size, out_path):
    """frame_paths[row][col] 拼成一張；第 0 列在最上面（Unity Grid 切圖從左上開始編號）。"""
    width, height = cols * size, rows * size
    sheet = np.zeros((height, width, 4), dtype=np.float32)
    buf = np.empty(size * size * 4, dtype=np.float32)
    for r, row in enumerate(frame_paths):
        y0 = (rows - 1 - r) * size  # Blender 影像資料是由下往上
        for c, path in enumerate(row):
            img = bpy.data.images.load(path)
            if tuple(img.size) != (size, size):
                raise SystemExit(f"{path} 尺寸 {tuple(img.size)} 與 --size {size} 不符")
            img.pixels.foreach_get(buf)  # 8-bit PNG 讀出來是原始值 /255，沒有色彩轉換
            sheet[y0:y0 + size, c * size:(c + 1) * size] = buf.reshape(size, size, 4)
            bpy.data.images.remove(img)
    out = bpy.data.images.new(os.path.basename(out_path), width, height, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = out_path
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)


def render_still(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    scene = bpy.context.scene

    if args.demo:
        if args.engine != "eevee":
            # 示範角色的卡通材質（Shader to RGB）與背面剔除描邊只有 EEVEE 支援
            print("[render_sprites] --demo 只支援 EEVEE，改用 eevee")
            args.engine = "eevee"
        target_name, anim_name, action_names = build_demo(scene)
        if args.save_demo:
            demo_path = os.path.abspath(args.save_demo)
            os.makedirs(os.path.dirname(demo_path), exist_ok=True)
            bpy.ops.wm.save_as_mainfile(filepath=demo_path)
    else:
        target_name = args.target
        anim_name = args.anim_object or args.target
        action_names = [a.strip() for a in args.actions.split(",") if a.strip()]

    target = bpy.data.objects.get(target_name)
    anim_obj = bpy.data.objects.get(anim_name)
    if target is None or anim_obj is None:
        raise SystemExit(f"找不到物件：{target_name if target is None else anim_name}")
    missing = [n for n in action_names if n not in bpy.data.actions]
    if missing:
        raise SystemExit(f"找不到 Action：{', '.join(missing)}")
    actions = [bpy.data.actions[n] for n in action_names]
    if not actions:
        # 沒指定就用目前掛著的動作；連動作都沒有就渲染場景幀範圍
        cur = anim_obj.animation_data.action if anim_obj.animation_data else None
        actions = [cur]
        action_names = [cur.name if cur else "Anim"]

    name = args.name or target_name
    out_dir = resolve_out(args.out)
    frames_dir = os.path.join(out_dir, "_frames")
    os.makedirs(frames_dir, exist_ok=True)

    if not args.keep_scene_lights:
        for obj in scene.objects:
            if obj.type == "LIGHT":
                obj.hide_render = True

    origin, radius, zmin, zmax = measure(scene, target, anim_obj, actions, args.step)
    e = math.radians(args.elevation)
    aim_height = (zmin + zmax) / 2.0  # 讓畫面垂直置中
    if args.ortho_scale > 0:
        ortho = args.ortho_scale
    else:
        screen_h = (zmax - zmin) * math.cos(e) + 2.0 * radius * abs(math.sin(e))
        ortho = max(2.0 * radius, screen_h) * args.margin
    distance = max(ortho, radius, zmax - zmin) * 4.0 + 1.0
    rig = setup_rig(scene, origin, aim_height, args.elevation, ortho, distance)
    setup_render(scene, args)
    if args.albedo:
        apply_albedo(target)

    dirs = direction_names(args.dirs)
    passes = [("color", "")] + ([] if args.no_normal else [("normal", "_n")])
    fps = scene.render.fps / scene.render.fps_base
    manifest = {
        "name": name,
        "cellSize": args.size,
        "directions": dirs,
        "fps": round(fps, 3),
        "orthoScale": round(ortho, 4),
        # Unity 1 單位＝Blender 1 公尺時的 Pixels Per Unit
        "suggestedPixelsPerUnit": round(args.size / ortho, 3),
        # 原點（腳底）在格子裡的位置，給 Sprite Editor 的 Custom Pivot（0～1，左下為 0,0）
        "pivot": [0.5, round(0.5 - aim_height * math.cos(e) / ortho, 4)],
        "elevation": args.elevation,
        "albedo": args.albedo,
        "sheets": [],
    }

    for action, action_name in zip(actions, action_names):
        if action is not None:
            assign_action(anim_obj, action)
        frames = action_frames(action, scene, args.step)
        if len(frames) * args.size > MAX_SHEET_SIZE or len(dirs) * args.size > MAX_SHEET_SIZE:
            print(f"[render_sprites] 警告：{action_name} 的 sheet 超過 {MAX_SHEET_SIZE}px，"
                  "Unity 可能會縮圖；請調大 --step 或調小 --size")
        sheet_info = {"action": action_name, "columns": len(frames), "rows": len(dirs)}
        for pass_name, suffix in passes:
            if pass_name == "color":
                color_pass(scene, args)
            else:
                normal_pass(scene)
            grid = []
            for i, d in enumerate(dirs):
                rig.rotation_euler = (0.0, 0.0, -math.radians(i * 360.0 / len(dirs)))
                row = []
                for f in frames:
                    scene.frame_set(f)
                    path = os.path.join(frames_dir, action_name, pass_name,
                                        f"{name}_{action_name}_{d}_{f:04d}{suffix}.png")
                    render_still(scene, path)
                    row.append(path)
                grid.append(row)
            sheet_file = f"{name}_{action_name}{suffix}.png"
            build_sheet(grid, len(dirs), len(frames), args.size, os.path.join(out_dir, sheet_file))
            sheet_info["normal" if pass_name == "normal" else "color"] = sheet_file
            print(f"[render_sprites] 完成 {sheet_file}")
        manifest["sheets"].append(sheet_info)

    with open(os.path.join(out_dir, f"{name}_sprites.json"), "w", encoding="utf-8") as fp:
        json.dump(manifest, fp, ensure_ascii=False, indent=2)
    print(f"[render_sprites] 全部完成：{out_dir}")


if __name__ == "__main__":
    main()
