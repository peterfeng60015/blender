# 表情＋二次動畫（hero3d-expression）交接筆記

worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/musing-edison-9fdf4c`（未 commit，`git diff` 取改動）

## 改了什麼（hero3d.py）

- 參數區：`EYE_C`、`EYE_R = 0.034`（純量，眼白半徑）、`EYE_DIR = (0.62, -0.78, 0.05)`（+X 眼睛正前方）；`COLORS` 加 `brow`、`mouth`、`tongue`。
- `#region 頭部配件`：眼皮從 Eyes 拆走，Eyes 只剩 `eye_white`、`pupil` 兩個材質；眼睛位置改讀 `EYE_C`／`EYE_DIR`。`D["eye"]` 的第 5、6 個值（舊眼皮密度）已沒用到，造型 session 可刪。
- 新 `#region 臉部表情`：`build_face(body)` 建 `Eyelids`（上下眼皮、雙層封閉）、`Brows`、`Mouth`（貼在長嘴兩側的薄片＋舌頭帶），各帶 shape key；**Body 也加了 `Basis`＋`Mouth_Open`（下顎繞嘴角往下轉）**。嘴的位置用 ray_cast 打在 Body 表面上，會跟著造型改長嘴。
- `skin_character()` rules：Eyelids／Brows→Head；Mouth→Snout↔Head 漸變。
- 表情值放在骨架物件的自訂屬性（9 個，名稱＝shape key 名），shape key 用 driver 讀；Action 裡 key 這些屬性（`EXPRESSIONS` 表）。FBX `bake_anim_use_all_actions` 會逐格烘焙 shape key，每個 take 帶自己的表情曲線（已用 Blender 讀回驗證）。
- `#region 動畫`：`EXPRESSIONS`、`OVERLAP`（彈簧參數）、`KICKS`（Idle 抖耳、Hurt 包包／尾巴被震開）、`apply_overlap()`、`_finish_curves()`；Idle 第 36 格的 LeftEar -25 改 0（改由 KICKS 抖耳）。
- `main()`：`parts += build_face(parts[0])`、`add_expression_drivers(arm, mesh)`、`render_face_sheet(...)` 各一行。

## 整合注意

1. **Body 在 build_face 之後帶 shape key**：之後若有人對 Body 呼叫 `bake_modifiers()`（`new_from_object`）會把 shape key 丟掉；必須在 `build_face` 之前做完。bmesh `from_mesh/to_mesh` 會保留 shape key，可以。
2. 平滑描邊法線（UV1）是用基本形算的，表情變形時不會跟著更新，差異很小（只有眼皮、嘴），可接受。
3. Mouth 與 Brows 是貼片／小零件：頂點色 A（描邊粗細）建議 Mouth 給 0（不描邊，否則嘴縫外會多一圈粗線），Brows 給 0.6。
4. **帽簷和眉毛衝突**：目前帽簷（z≈0.89）蓋住眼睛上半，眉毛被帽頂吃掉，`AhJia3D_faces.png` 的 Brow_* 格看不出差別。眉毛位置由 `BROW_R`、`BROW_TH`（相對 EYE_C／EYE_R）決定，帽子抬高或往後推後就會露出來。
5. shape key 共 9 個（≤12）；三角形 Eyelids 704、Brows 104、Mouth 120。

## Unity 端怎麼確認

- 選 `AhJia3D.fbx` → Model 分頁確認 Blend Shapes 有勾（預設有）；展開 FBX，SkinnedMeshRenderer 的 BlendShapes 列表應有 9 個名稱。
- Animation 分頁選任一 clip（例如 `AhJia_Rig|AhJia_Hurt`）→ 預覽視窗播放看臉；或把 clip 拖進 Animation 視窗，曲線列表會有 `AhJia_Mesh : Blend Shape.Mouth_Open` 等項目。
