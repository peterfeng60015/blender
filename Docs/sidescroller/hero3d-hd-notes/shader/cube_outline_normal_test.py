import ast, bpy, bmesh, math
from mathutils import Vector
src = open("Tools/Blender/Hero3D/hero3d.py", encoding="utf-8").read()
fn = [n for n in ast.parse(src).body if isinstance(n, ast.FunctionDef) and n.name == "bake_outline_normals"][0]
ns = {"bmesh": bmesh, "bpy": bpy, "Vector": Vector}
exec(compile(ast.Module([fn], []), "x", "exec"), ns)
bpy.ops.wm.read_factory_settings(use_empty=True)
bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
# 頂面拆開成獨立的面（同位置不同頂點，模擬零件接縫）＋整顆平面著色（硬邊）
top = [f for f in bm.faces if f.normal.z > 0.9]
bmesh.ops.split(bm, geom=top)
me = bpy.data.meshes.new("c"); bm.to_mesh(me); bm.free()
ob = bpy.data.objects.new("c", me); bpy.context.scene.collection.objects.link(ob)
me.shade_flat()
ns["bake_outline_normals"](ob)
me.calc_tangents(uvmap=me.uv_layers[0].name)
uv = me.uv_layers["OutlineNormal"]
worst = 0
for lp in me.loops:
    x, y = uv.data[lp.index].uv
    z = math.sqrt(max(0, 1 - x*x - y*y))
    d = (x*lp.tangent + y*lp.bitangent + z*lp.normal).normalized()
    corner = me.vertices[lp.vertex_index].co.normalized()  # 立方體角落的平滑法線＝對角線方向
    worst = max(worst, math.degrees(d.angle(corner)))
print("verts", len(me.vertices), "loops", len(me.loops), "worst vs diagonal", round(worst, 4))
