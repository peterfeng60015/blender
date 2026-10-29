# face（表情＋二次動畫）暫停進度

- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/musing-edison-9fdf4c`
- 分支：`claude/musing-edison-9fdf4c`（基於 main 8d8c3db），**未 commit**
- 交接細節：`/Users/iulee/Library/Application Support/Claude/side-session-notes/local_b8eaa63d-3326-4f44-8c4a-088f0c07e17e/hero3d-expression-handover.md`

## 未 commit 的改動摘要（只動 `Tools/Blender/Hero3D/hero3d.py`）

- 參數區：`EYE_C`、`EYE_R = 0.034`、`EYE_DIR`、`CAP_BRIM_Z = 0.936`（暫定）；`COLORS` 加 `brow`、`mouth`、`tongue`。整合時改用造型的最終值 EYE_C=(0.081, -0.219, 0.886)、EYE_R=0.036、CAP_BRIM_Z=0.958。
- `#region 頭部配件`：眼皮從 Eyes 拆出，Eyes 只剩眼白＋瞳孔，位置讀 `EYE_C`／`EYE_DIR`。
- 新 `#region 臉部表情`：`build_face(body)` 建 `Eyelids`／`Brows`／`Mouth` 並帶 shape key；Body 也加了 `Mouth_Open`（下顎）；長嘴幾何從 `BONES` 的 Snout 骨推算；眉毛最高夾在 `CAP_BRIM_Z − 0.005`；`face_bind_rules(arm)`（骨頭不存在時退回並警告）；`add_expression_drivers()`（骨架自訂屬性 → driver → shape key）；`render_face_sheet()` 輸出 `AhJia3D_faces.png`。
- `#region 動畫`：疊加層 `EXPRESSIONS`（表情 key）、`OVERLAP`＋`KICKS`（耳／尾／包彈簧式二次動畫，`apply_overlap()`，缺骨略過並警告）、`_finish_curves()`；主動作 `ANIMS` 未改動。
- `skin_character()` 多一行 `rules.update(face_bind_rules(arm))`；`main()` 多三行（`build_face`、`add_expression_drivers`、`render_face_sheet`）；檔頭說明補 faces 圖。

## 步驟進度

| 步驟 | 進度 |
|---|---|
| 讀約定與 hero3d.py | 100% |
| 臉部零件與 shape key（9 個） | 100% |
| 表情與二次動畫進動作 | 100% |
| 跑 Blender、渲總覽、驗 FBX | 100% |
| 依追加決定改疊加層、骨名容錯、CAP_BRIM_Z 夾限 | 100% |
| 自審＋subagent 審並修 | 約 50%（自審已做完並修好法線方向；subagent 審查已中止，未取得結果） |
| 回報協調 | 0% |

## 恢復後第一步

重新開一個 subagent 審 `git diff`（重點：`apply_overlap` 的旋轉合成與循環接縫、shape key 頂點順序、表情值在各渲染步驟之間是否殘留、`jaw()` 影響範圍），修好後重跑 Blender、重新驗 FBX，再回報協調。

## 還在等的背景工作

無（subagent 已中止，沒有結果）。

## 輸出圖（scratchpad）

- 表情總覽：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-musing-edison-9fdf4c/21fb7038-68ea-4cfc-a496-80238bb05f5f/scratchpad/out/AhJia3D_faces.png`
- 動作總覽：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-musing-edison-9fdf4c/21fb7038-68ea-4cfc-a496-80238bb05f5f/scratchpad/out/AhJia3D_actions.png`
- 拿掉帽子的眉毛除錯圖：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-musing-edison-9fdf4c/21fb7038-68ea-4cfc-a496-80238bb05f5f/scratchpad/t/brows_sheet.png`

## FBX shape key 動畫驗證結果

用 Blender 讀回 `AhJia3D.fbx`：網格 `AhJia_Mesh` 帶 Basis＋9 個 shape key；6 個 take 各有自己的 shape key 曲線，數值與 `EXPRESSIONS` 一致（例：Hurt 的 Mouth_Open 0→0.9、Squint 0→1、Brow_Sad 0→1；Idle 的 Blink 0→1→0、Mouth_Smirk 0.5；Run 的 Brow_Angry 0.3、Squint 0.25）。作法：表情值 key 在骨架物件的自訂屬性上，shape key 用 driver 讀，`bake_anim_use_all_actions` 匯出時逐格烘焙進每個 take。

## 已知限制

- 舊帽子仍會擋住部分眉毛；換新帽子（CAP_BRIM_Z 0.958）後要再看一次。
- Body 帶 shape key 之後，不能再對 Body 呼叫 `bake_modifiers()`（會把 shape key 丟掉）。
- Mouth 是貼片：頂點色 A（描邊粗細）建議設 0。
- 表情變形時，描邊用的平滑法線（UV1）不會跟著更新，差異很小。
- 二次動畫：非循環 clip（Melee、Hurt）結尾還留著一點跟隨的餘量，切換到下一個動作時交給 Unity 的過渡處理。
