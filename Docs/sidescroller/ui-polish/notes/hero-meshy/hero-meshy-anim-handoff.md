# anim 交接：Meshy 阿甲 6 個動作修正

- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/adoring-dubinsky-f441f3`（分支 `claude/adoring-dubinsky-f441f3`，未 commit）
- 底稿：rig 第六版定稿快照 `hero-meshy-drop/rig-20261030-1550/hero3d.py`（1529、1533、1545 也跑過，量測完全相同）。worktree 的 `Tools/Blender/Hero3D/hero3d.py` ＝ 這份快照，只把空的 `#region Meshy 動作修正` 換成下面的內容；region 以外逐字相同（已用 diff 確認）。整合時把 region 整段貼進 rig 的最終版即可，呼叫點（meshy 模式 `build_actions` 之後 `apply_meshy_anim_fixes(arm, actions)`）rig 已經有。
- 跑法驗證：`Blender -b --python Tools/Blender/Hero3D/hero3d.py -- --source meshy --glb /Users/iulee/Downloads/Meshy_AI_Skater_Armadillo_1030063357_texture.glb --out <資料夾>` 跑通（Blender 5.2.3）。動作裡全部 522 條曲線都是 `pose.bones[...]`，沒有 shape key 曲線，檔內 shape key 數 0。
- 不帶 `--source` 的程式化版不受影響（region 只在 meshy 模式被呼叫）；共用 `ANIMS` 沒動（協調裁示）。

## 每個動作修了什麼

| 動作 | 問題（修正表清空時） | 修正 |
|---|---|---|
| Run | 共用 ANIMS 第 5、15 格左右腳配反：落地的前腳馬上踢到身後、後腳貼地往前拖（倒退滑步），腳貼地往前拖總量 0.435 m | 第 5／15 格換腳（支撐腳從前往後走）；擺動腳在第 8／18 格多抬（UpperLeg +12°、LowerLeg -30°），不貼地掃過；左手臂整段外展 8°，避開左前腹的包。修正後貼地往前拖 0 |
| Jump | 捲起來球心離 Hips 約 0.18 m，繞 Hips 轉一圈球上下晃 0.365 m；兩腳在球裡互穿約 6 mm | `MESHY_BALL_ACTIONS` 逐格補 Hips 位移：球繞球心轉、最低點貼地（晃動 0.001 m，網格最低點 0～+5.8 cm（球不是正圓））；大腿整段外展 ±5°，兩腳間隙 +2 cm |
| Melee | 包在左前腹：往後拉 -45° 插進肚子，往前上甩 150～175° 擋到臉（包最高 z 0.733） | Bag 第 3 格改往外側拉（+35°, 0, +25°），第 6／9 格少甩 50／60°（往前平甩，最高 z 0.644）；Bag 是二次動畫逐格 key 的骨頭，增量在關鍵格之間平滑過渡，相鄰格最大跳動 0.165→0.090 m |
| Shoot | 第 3 格拉皮筋的右手拉到 z 0.81（長嘴高度），插進長嘴 | 第 3 格 RightUpperArm -35°、RightLowerArm +10°，右手壓到胸前 z 0.72 |
| Hurt | 第 3 格頭往後甩，帽子後緣壓進殼頂 | Neck 第 3／9 格少仰 12／7°，Spine 多仰 5／3°（整體後仰幅度差不多） |
| Idle | 無問題（兩腳間隙 5.3 cm，Hips 下沉 6 mm 時腳會跟著沉 6 mm，和程式化版一樣，遊戲尺寸看不出來） | 不修 |

兩腳穿插（review 提醒鞋內側幾乎相貼）：按 y-z 2 cm 格子比兩腳同位置的 x，Idle／Melee／Shoot／Hurt 最小間隙 5.3 cm、Run 4.1 cm、Jump 2 cm，都沒互穿；review 量到的 1 mm 是兩鞋 bbox 的 x（鞋外八，鞋跟與鞋尖不在同一個 y），不需要改靜止站姿。

## 修正前後對照圖

`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-adoring-dubinsky-f441f3/773a386f-fc15-45fb-80f9-799c6e70ff5e/scratchpad/compare/compare_<動作>.png`：上半＝修正表清空（第一排側面、第二排 3/4 前），紅線下＝修正後（同排序）。完整三視角（側面、3/4 前、3/4 後）：修正後 `/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-adoring-dubinsky-f441f3/773a386f-fc15-45fb-80f9-799c6e70ff5e/scratchpad/fix6/check/<動作>.png`、清空 `/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-adoring-dubinsky-f441f3/773a386f-fc15-45fb-80f9-799c6e70ff5e/scratchpad/neg3/check/<動作>.png`；逐格量測 `metrics.txt` 在同資料夾。rig 原生輸出（含 actions 總覽）在 `/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-adoring-dubinsky-f441f3/773a386f-fc15-45fb-80f9-799c6e70ff5e/scratchpad/fix6/`。

## 留給 rig 的權重問題

這輪檢查沒有看到要靠改權重才能修的變形（肩甲、殼、包、帽子在 6 個動作的關鍵格都跟著骨頭走，沒有撕裂或明顯凹陷），所以沒有傳訊息給 rig。

## 審查

- 自審抓到並修好 2 個 bug：只改指定格會讓逐格 key 的骨頭（包）中間格跳回原值，改成在關鍵格之間平滑過渡；`keyframe_points.insert` 帶 `"REPLACE"` 時，原本沒 key 的格不會新增，已拿掉。
- subagent 審查：無高嚴重度。已修：`"*"` 會受表的寫法順序影響（改成先放具體格數、最後加常數）；修改器關閉改用 try/finally 還原；球心置中改在修正表之後做；註解改準確。未修（輕微、不跳動）：改了 Neck／Spine 後子骨的二次動畫彈簧沒重算；新增的 key 會改變相鄰關鍵格的自動把手；smoothstep 在控制點斜率為 0。
- 反向驗證：同一份快照，把 `MESHY_ANIM_FIXES` 清空、`MESHY_BALL_ACTIONS` 設成 []，量測與對照圖都回到上表的「問題」數值，修正後回到「修正」數值。

## `#region Meshy 動作修正` 完整內容

```python
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
```
