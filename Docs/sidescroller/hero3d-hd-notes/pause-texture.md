# texture（貼圖 session）暫停進度

- 暫停時間：2026-10-29（使用者下班）
- session：local_bb133c17-d67b-4c7a-bb72-e891fac989cc（上層 local_b8eaa63d-3326-4f44-8c4a-088f0c07e17e）
- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/epic-easley-7a5be3`
- 分支：`claude/epic-easley-7a5be3`（基於 main 8d8c3db），**未 commit**

## 未 commit 的改動（只有 `Tools/Blender/Hero3D/hero3d.py`，+653／-3）

- 新增 `#region UV 與貼圖`（放在 `#region 動畫` 之後、`main()` 之前）：
  - 常數：`TEX_SIZE`、`UV_FACE_BOOST`、`TEX_DILATE`、`UV_ANGLE`、`UV_MARGIN`；對照表 `PAINT_STYLE`（COLORS 鍵 → 圖樣）、`PAINT_REF`（圖樣借用的位置／顏色角色 → COLORS 鍵）、`STYLE_MASK`（圖樣 → _MaskMap R 高光、B 類別；沒列到的鍵用 `None` 列：R 0、B 0.66）。
  - `unwrap_atlas()`：Smart UV Project 分島 → seams_from_islands＋ABF 重展 → average_islands_scale → 頭臉島放大 1.4、藏起來的島（`_openness()` BVH 射線判斷）縮小到 0.3 → pack_islands（CONCAVE，間距 0.004）。`_uv_stretch()` 印出拉伸比。
  - `_bake_buffers()`：Cycles 把位置、物件空間法線、(材質編號＋1, pointiness, AO) 烘到浮點圖（暫時換材質，烘完還原）。
  - `_frames()`：每種材質（含左右）的面 bbox＋最薄主軸。
  - 各圖樣畫法 `_paint_skin／_paint_belly／_paint_gradient／_paint_scutes／_paint_cap／_paint_logo／_paint_fabric／_paint_shoe／_paint_small`，查表 `PAINTERS`；`_paint_textures()` 加通用筆觸、AO 壓暗、寫 mask、UV 島外擴 12px。畫不出來（缺零件）時 try/except 退回固有色並印警告。
  - `_save_png()`、`use_basemap()`（預覽 toon／flat 材質改取樣 _BaseMap）、`build_textures(mesh, out)`。
- `bake_vertex_colors()`：頂點色 Col 的 RGB 保留固有色，**A 改寫描邊粗細**（OutlineWidth 群組權重）。
- `main()`：`skin_character()` 之後多一行 `build_textures(mesh, out)`（要在加描邊 Solidify 之前、靜止姿勢）。
- 沒改模組開頭 docstring 的輸出清單（不在我的 region；整合時可補 `AhJia3D_BaseMap.png`、`AhJia3D_MaskMap.png` 兩行）。

## 步驟進度

| 步驟 | 進度 |
|---|---|
| 讀約定與 hero3d.py | 100% |
| 實作 UV 與貼圖 region | 100% |
| 改 bake_vertex_colors 與 main | 100% |
| 跑 Blender、檢視貼圖與渲染（舊模型） | 100% |
| 圖樣改對照表＋缺鍵退回（因應重新設計） | 100% |
| 自審＋subagent 審查並修正 | 約 65%：自審已做完並修好（封閉形體平均法線、BMFace 索引、缺鍵退回等）；subagent 審查啟動後因暫停被停止，**沒有結果** |
| 回報協調（worktree、函式、路徑、時間、限制） | 0% |

## 恢復後第一步

1. 重開一個 subagent 審 `git diff`（審查重點：座標空間、像素順序、色彩空間、bake 設定、numpy 形狀、缺零件邊界、對造型改網格的脆弱假設），修好後重跑：
   `/Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/Blender/Hero3D/hero3d.py -- --out <scratchpad>`
2. 看渲染與兩張貼圖，然後回報協調。

## 背景工作

- 無（subagent 審查已停止，沒有產出）。

## 最新輸出（舊模型，約 38～42 秒跑完；UV 8～12s、烘焙約 7s、繪製約 1s）

- 貼圖：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-epic-easley-7a5be3/ca29da5f-578e-4787-9d53-48c004f7402c/scratchpad/final2/AhJia3D_BaseMap.png`、`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-epic-easley-7a5be3/ca29da5f-578e-4787-9d53-48c004f7402c/scratchpad/final2/AhJia3D_MaskMap.png`
- 渲染：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-epic-easley-7a5be3/ca29da5f-578e-4787-9d53-48c004f7402c/scratchpad/final2/AhJia3D_hero.png`、`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-epic-easley-7a5be3/ca29da5f-578e-4787-9d53-48c004f7402c/scratchpad/final2/AhJia3D_turnaround.png`、`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-epic-easley-7a5be3/ca29da5f-578e-4787-9d53-48c004f7402c/scratchpad/final2/AhJia3D_pose_hero.png`
- 韌性測試（拿掉 SHELL_C／SHELL_R、新鍵 mystery、PAINT_REF 指到不存在的鍵）：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-epic-easley-7a5be3/ca29da5f-578e-4787-9d53-48c004f7402c/scratchpad/robust/AhJia3D_hero.png`（正常跑完）
- 驗證過：FBX 只有一個 UV 層 `UVMap`（範圍 0.004～0.996）、頂點色 A 為 1.0／0.6 兩種值；_BaseMap 固有色數值與 COLORS 一致（sRGB 直接寫入）；UV 拉伸比面積加權 p50 1.02、p95 1.08；UV 島覆蓋率約 44%（外擴前）。

## 造型新零件（Scarf、`scarf`／`lace` 鍵）的處理現況

- `PAINT_STYLE` 已預先加 `"scarf": "fabric"`、`"lace": "fabric"`（布紋細紋＋通用筆觸，_MaskMap B＝0.66）。尚未在新造型上實跑。
- 其他沒列到的新鍵：自動退回固有色＋通用筆觸（B＝0.66、R＝0）；鍵若不在 COLORS 塗灰並印警告。
- 已知假設／限制（整合時要看）：
  - `build_textures()` 要在 `skin_character()` 合併網格之後、描邊 Solidify 與 shader session 的 `OutlineNormal` UV 層之前呼叫，否則 UVMap 可能不是第一層（會印警告）。
  - 殼紋列座標：活動殼帶寫死從 y＝-0.05 起每 0.055 一列（對齊現行殼帶），造型改殼帶切法後甲片列可能不對齊殼帶邊（不會壞，只是不齊）；肩甲／臀甲用 `SHELL_C／SHELL_R` 橢球，沒有這兩個常數時改用殼紋材質 bbox 推。
  - 帽子：帽簷用「法線近乎垂直＋在帽頂中心前方」判斷，造型改成上翹帽簷後帽簷車縫可能抓不到。帽頂中心用帽子 bbox 推（中心往後 0.35 半寬）。
  - 腮紅位置由 `PAINT_REF` 的 nose、eye（eye_white）兩種材質 bbox 推；鞋頭／鞋帶用 shoe 材質左右 bbox；包包縫線用 bag／bag_flap 各自 bbox 當方盒。
  - 鞋子目前在 shoe 材質上「畫」鞋帶；若造型改成獨立的 `lace` 零件，可把 `_paint_shoe` 的鞋帶段拿掉。
  - 頭臉 UV 放大用 `BONES` 的 Head 骨根部高度 −0.06 判斷（沒有 Head 骨就不放大）。
  - UV 打包每次結果略有不同（Blender CONCAVE 打包非決定性），貼圖逐像素不可比；用渲染圖比較。
  - _MaskMap PNG 沒有色彩空間標記，Unity 匯入要關 sRGB（shader session／Hero3DPreviewSetup 處理）。
