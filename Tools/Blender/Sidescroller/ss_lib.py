"""《熱浪急件》建模共用函式：NPR 色階材質、倒殼描邊、基本形體、骨架與動作、取景框、多張變化。

在 Blender 裡由 ss_models.py 匯入（不是單獨執行）。慣例：
- 長度單位是公尺＝Unity 單位（PPU 64：1 公尺＝64 px）。
- 角色面朝 +X、上方 +Z，原點＝pivot；相機在 -Y 看 +Y（render_sprites.py --dirs 1 --elevation 0），
  畫面右方＝+X、上方＝+Z。-Y 是靠鏡頭的一側（近側），+Y 是遠側。
- 程式裡的顏色一律寫 sRGB（"#RRGGBB"），linear() 轉線性後才交給 Blender；輸出用 Standard，PNG 上就是寫的值。
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

PPU = 64
FPS = 12
OUTLINE_MOD = "SpriteOutline"   # render_sprites.py 渲染法線時會關掉這個名稱的修改器
HIDE = 1e-4                     # 骨頭縮到這麼小＝隱藏（同一個 Action 裡切換部件，例如縮成球）

# 色階用的固定光源方向：正上方偏前（朝鏡頭 -Y），沒有 X 分量，所以左右翻轉後明暗不變
LIGHT_DIR = Vector((0.0, -0.5, 0.866)).normalized()
# 3 階：暗 45％／中 75％／亮 100％；門檻是法線與 LIGHT_DIR 的內積
TONE_DARK, TONE_MID = 0.45, 0.75
TH_MID, TH_LIGHT, TH_HIGHLIGHT = -0.05, 0.42, 0.93
HIGHLIGHT_MIX = 0.45            # 硬材質高光帶：亮色往白色混多少


# ---------- 場景與顏色 ----------

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
    _materials.clear()
    return scene


def srgb(c):
    """"#RRGGBB" 或 (r, g, b)（0～1）→ sRGB tuple。"""
    if isinstance(c, str):
        c = c.lstrip("#")
        return tuple(int(c[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    return tuple(c)


def linear(c):
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in srgb(c))


def shade(c, k):
    """sRGB 顏色乘上 k（色階、深色描邊用）。"""
    return tuple(max(0.0, min(1.0, v * k)) for v in srgb(c))


def mix(a, b, t):
    a, b = srgb(a), srgb(b)
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# ---------- 材質 ----------

_materials = {}


def _tree(mat):
    if bpy.app.version < (5, 0, 0):
        mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    return nt


def toon(color, hard=False):
    """NPR 色階材質：世界法線·LIGHT_DIR → Constant Color Ramp → Emission。

    明暗只看法線，跟場景燈（render_sprites.py 的 SpriteKey）無關。hard＝殼、西裝、盾牌等硬材質，
    多一道窄的高光帶。同顏色同參數的材質共用。
    """
    key = ("toon", srgb(color), hard)
    if key in _materials:
        return _materials[key]
    base = srgb(color)
    mat = bpy.data.materials.new("Toon_" + "".join(f"{round(v * 255):02X}" for v in base) + ("_H" if hard else ""))
    nt = _tree(mat)
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    dot = nt.nodes.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    dot.inputs[1].default_value = LIGHT_DIR
    remap = nt.nodes.new("ShaderNodeMath")       # Color Ramp 只吃 0～1：內積 -1～1 → 0～1
    remap.operation = "MULTIPLY_ADD"
    remap.inputs[1].default_value = 0.5
    remap.inputs[2].default_value = 0.5
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    stops = [(None, shade(base, TONE_DARK)), (TH_MID, shade(base, TONE_MID)), (TH_LIGHT, base)]
    if hard:
        stops.append((TH_HIGHLIGHT, mix(base, (1, 1, 1), HIGHLIGHT_MIX)))
    els = ramp.color_ramp.elements
    while len(els) > 1:
        els.remove(els[-1])
    els[0].position = 0.0
    els[0].color = (*linear(stops[0][1]), 1.0)
    for th, col in stops[1:]:
        el = els.new(th * 0.5 + 0.5)   # 門檻是內積值，換算到 0～1
        el.color = (*linear(col), 1.0)
    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(geo.outputs["Normal"], dot.inputs[0])
    nt.links.new(dot.outputs["Value"], remap.inputs[0])
    nt.links.new(remap.outputs["Value"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    mat.diffuse_color = (*linear(base), 1.0)
    _materials[key] = mat
    return mat


def flat(color):
    """不分色階的純色（背景天空、剪影、發光的霓虹、眼睛反光）。"""
    key = ("flat", srgb(color))
    if key in _materials:
        return _materials[key]
    mat = bpy.data.materials.new("Flat_" + "".join(f"{round(v * 255):02X}" for v in srgb(color)))
    nt = _tree(mat)
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (*linear(color), 1.0)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    mat.diffuse_color = (*linear(color), 1.0)
    _materials[key] = mat
    return mat


def outline_material(color):
    key = ("outline", srgb(color))
    if key in _materials:
        return _materials[key]
    mat = flat(color).copy()
    mat.name = "Outline_" + mat.name[5:]
    mat.use_backface_culling = True
    _materials[key] = mat
    return mat


# ---------- 基本形體（bmesh） ----------

def _finish(bm, verts, mi, smooth):
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        f.material_index = mi
        f.smooth = smooth if smooth != "sides" else len(f.verts) <= 4
    return verts


def ellipsoid(bm, center, radii, mi=0, rot=None, seg=24, cut_below=None):
    """橢球；cut_below＝只留 z（局部，旋轉前）≥ 這個值的部分（做殼、帽頂）。"""
    m = Matrix.Translation(center) @ (rot or Matrix()) @ Matrix.Diagonal((*radii, 1.0))
    verts = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(8, seg // 2), radius=1.0)["verts"]
    if cut_below is not None:
        kill = [v for v in verts if v.co.z < cut_below - 1e-6]
        bmesh.ops.delete(bm, geom=kill, context="VERTS")
        verts = [v for v in verts if v.is_valid]
    bmesh.ops.transform(bm, matrix=m, verts=verts)
    return _finish(bm, verts, mi, True)


def cylinder(bm, a, b, r1, r2=None, mi=0, seg=16, smooth="sides"):
    """從點 a 到點 b 的圓柱／圓錐（r2＝b 端半徑，0＝尖）。"""
    a, b = Vector(a), Vector(b)
    d = b - a
    r2 = r1 if r2 is None else r2
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    m = Matrix.Translation((a + b) / 2) @ rot
    verts = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r1, radius2=r2,
                                  depth=d.length, matrix=m)["verts"]
    return _finish(bm, verts, mi, smooth)


def box(bm, center, size, mi=0, rot=None, bevel=0.0):
    m = Matrix.Translation(center) @ (rot or Matrix()) @ Matrix.Diagonal((*size, 1.0))
    verts = bmesh.ops.create_cube(bm, size=1.0, matrix=m)["verts"]
    # 倒角接近最薄邊的一半會壓出退化面，描邊（Solidify even offset）會因此炸開
    bevel = min(bevel, min(size) * 0.3)
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        res = bmesh.ops.bevel(bm, geom=verts + edges, offset=bevel, segments=2, affect="EDGES",
                              profile=0.5, clamp_overlap=True)
        verts = list({v for v in verts if v.is_valid} | {v for f in res["faces"] for v in f.verts})
    return _finish(bm, verts, mi, bevel > 0)


def quad(bm, corners, mi=0):
    """四個點的平面（背景剪影、旗子）。"""
    vs = [bm.verts.new(c) for c in corners]
    f = bm.faces.new(vs)
    f.material_index = mi
    return vs


def poly(bm, pts, y=0.0, mi=0, depth=0.0):
    """XZ 平面上的多邊形（從 -Y 看是逆時針）；depth>0 時擠出成厚板（往 +Y）。"""
    vs = [bm.verts.new((x, y, z)) for x, z in pts]
    f = bm.faces.new(vs)
    f.material_index = mi
    if f.normal.y > 0:
        f.normal_flip()
    out = list(vs)
    if depth > 0:
        ext = bmesh.ops.extrude_face_region(bm, geom=[f])
        nv = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=(0, depth, 0), verts=nv)
        out += nv
        for g in ext["geom"]:
            if isinstance(g, bmesh.types.BMFace):
                g.material_index = mi
    return out


def rot_x(deg):
    return Matrix.Rotation(math.radians(deg), 4, "X")


def rot_y(deg):
    """繞 Y 軸；從鏡頭看是順時針（往前傾）為正。"""
    return Matrix.Rotation(math.radians(deg), 4, "Y")


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Z")


# ---------- 物件 ----------

def empty(name, parent=None, scale=1.0, location=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    obj.scale = (scale, scale, scale)
    obj.location = location
    return obj


def mesh(name, build, mats, parent=None, outline_px=0.0, outline_color=None, world_scale=1.0):
    """bmesh → 物件。mats：材質清單（build 裡的 mi 對應索引）。

    outline_px：描邊粗細（像素，PPU 64），0＝不描邊；world_scale＝父物件累積的縮放（描邊要扣回來）。
    outline_color：描邊色，預設是第一個材質主色的深色版（不是純黑）。
    """
    bm = bmesh.new()
    build(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    if outline_px > 0:
        col = outline_color if outline_color is not None else shade(_base_color(mats[0]), 0.28)
        me.materials.append(outline_material(col))
        mod = obj.modifiers.new(OUTLINE_MOD, "SOLIDIFY")
        mod.thickness = outline_px / PPU / world_scale
        mod.offset = 1.0
        mod.use_flip_normals = True
        mod.use_rim = False
        mod.use_even_offset = True
        mod.material_offset = len(mats)
    return obj


def _base_color(mat):
    lin = mat.diffuse_color[:3]
    return tuple(v * 12.92 if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055 for v in lin)


def frame_box(root, ortho, pivot_y=None, half_width=0.01):
    """不渲染的取景框：讓 render_sprites.py 的自動置中固定在 aim 高度，pivot 就不受動作影響。

    pivot_y＝0.5 − aim／ortho。框的高度是兩倍格高：render_sprites.py 量的是各物件旋轉後的包圍盒角點，
    會比實際形狀大（例如轉動中的球），框要比這些都大，zmin／zmax 才一定由它決定。
    pivot_y 預設讀環境變數 SS_PIVOT（render_all.py 的素材表是唯一來源，ss_finalize.py 會再核對）。
    """
    if pivot_y is None:
        pivot_y = float(os.environ.get("SS_PIVOT", "0.5,0.5").split(",")[1])
    aim = (0.5 - pivot_y) * ortho

    def build(bm):
        box(bm, (0, 0, aim), (half_width * 2, half_width * 2, ortho * 2))
    obj = mesh(f"{root.name}_FrameBox", build, [flat("#FF00FF")], parent=root)
    obj.hide_render = True
    return obj


# ---------- 骨架與動作 ----------

class Rig:
    """骨頭一律朝 +Z、roll 0：骨頭本地 X＝世界 X、本地 Y＝世界 Z、本地 Z＝世界 -Y。

    所以 pose 的 rotation_euler.z＝畫面上逆時針（往後仰）為正；location (x, y)＝畫面上的 (右, 上)。
    """

    def __init__(self, name, parent, bones, world_scale=1.0):
        data = bpy.data.armatures.new(name)
        self.obj = bpy.data.objects.new(name, data)
        bpy.context.scene.collection.objects.link(self.obj)
        self.obj.parent = parent
        self.world_scale = world_scale
        bpy.context.view_layer.objects.active = self.obj
        self.obj.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        for bname, (head, bparent) in bones.items():
            eb = data.edit_bones.new(bname)
            eb.head = head
            eb.tail = (head[0], head[1], head[2] + 0.1)
            eb.roll = 0.0
            if bparent:
                eb.parent = data.edit_bones[bparent]
        bpy.ops.object.mode_set(mode="OBJECT")
        for pb in self.obj.pose.bones:
            pb.rotation_mode = "XYZ"

    def part(self, name, bone, build, mats, outline_px=3.0, outline_color=None):
        obj = mesh(name, build, mats, parent=self.obj, outline_px=outline_px, outline_color=outline_color,
                   world_scale=self.world_scale)
        obj.parent_type = "BONE"
        obj.parent_bone = bone
        b = self.obj.data.bones[bone]
        # 骨頭父子關係以骨尾為基準；乘上靜止姿勢的反矩陣，頂點才留在建模時的座標
        obj.matrix_parent_inverse = (b.matrix_local @ Matrix.Translation((0, b.length, 0))).inverted()
        return obj

    def _reset(self):
        for pb in self.obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_euler = (0, 0, 0)
            pb.scale = (1, 1, 1)

    def action(self, name, length, fn, cyclic=True):
        """fn(i, t, pose)：i＝第幾幀（0 起），t＝i/length；用 pose(bone, r=, x=, z=, s=) 設定這一幀。

        每一幀都打 key（渲染只取整數幀），第 length+1 幀重複第 0 幀讓循環無縫。
        """
        self.obj.animation_data_clear()
        pbs = self.obj.pose.bones

        def pose(bone, r=0.0, x=0.0, z=0.0, s=1.0):
            pb = pbs[bone]
            pb.rotation_euler = (0, 0, math.radians(r))
            pb.location = (x, z, 0)
            pb.scale = (s, s, s) if not isinstance(s, tuple) else (s[0], s[1], (s[0] + s[1]) / 2)

        for f in range(length + 1):
            self._reset()
            i = f % length
            fn(i, i / length, pose)
            for pb in pbs:
                for path in ("location", "rotation_euler", "scale"):
                    pb.keyframe_insert(path, frame=f + 1)
        act = self.obj.animation_data.action
        act.name = name
        act.use_fake_user = True
        act.use_frame_range = True
        act.frame_start = 1
        act.frame_end = length
        act.use_cyclic = cyclic
        return act

    def done(self):
        self.obj.animation_data_clear()
        self._reset()


# ---------- 多張變化（tile、props、立繪表情、UI） ----------

def frames_holder(root, count, name="Idle"):
    """<Name>_Frames：只有幀範圍的 Action，給多張變化的素材當 --anim-object。"""
    holder = empty(f"{root.name}_Frames", root)
    holder["variant"] = 0
    for f in range(1, count + 1):
        holder["variant"] = f - 1
        holder.keyframe_insert('["variant"]', frame=f)
    act = holder.animation_data.action
    act.name = name
    act.use_fake_user = True
    act.use_frame_range = True
    act.frame_start = 1
    act.frame_end = count
    act.use_cyclic = False
    return holder


def show_only_on(objs, frames, count):
    """objs 只在 frames（1 起算的幀號集合）渲染，其他幀 hide_render。"""
    frames = {frames} if isinstance(frames, int) else set(frames)
    for obj in objs:
        for f in range(1, count + 1):
            obj.hide_render = f not in frames
            obj.keyframe_insert("hide_render", frame=f)
        obj.hide_render = 1 not in frames
