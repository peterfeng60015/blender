# shape（造型）暫停進度 — 2026-10-29

## 位置
- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/happy-montalcini-986b44`
- 分支：`claude/happy-montalcini-986b44`（基於 main 8d8c3db）
- session：local_d9ad93e1-a64c-4b4c-93eb-5a933b1dfdae
- **沒有 commit**；改動只在 `Tools/Blender/Hero3D/hero3d.py`（+491／−106 行）

## 未 commit 的改動摘要（重新設計版）
- 參數區：開頭加角色設定說明；`COLORS` 短褲改深藍 `#34406B`、新增 `lace`、`scarf`（芥末黃）；新增 `EYE_C`、`EYE_R`、`CAP_BRIM_Z`；`D` 改新密度（拿掉 `body_ratio`）。
- 新 `#region 造型工具`：`tube()`（沿折線長管子，可壓扁、半球收口、閉環）、`gauss()`、`mirror_x()`。
- `#region 身體`：拆成 `body_skeleton()`（頸、肩、肘、膝前後加節點＝關節多圈）、`sculpt_head()`（顴骨、嘴側凹、收細嘴尖、下顎線、眉骨、眼窩、鼻樑）、`build_body()`（拿掉 Decimate；頭與嘴先局部細分一次再 Subsurf 2，頭部密度 4 倍、全四邊形）。頭放大（半徑 0.118→0.132）、嘴加長。
- `#region 背殼`：`shell_zcut()`（後甲下垂、前後甲波浪下緣）、`plate_height()`（殼帶長方甲片、前後甲蜂巢甲片）、`shell_strip()` 改寫（甲片凸起＋看得到的邊才做圓角厚邊，拿掉整片 Solidify）。
- `#region 頭部配件`：眼睛改讀 `EYE_C`／`EYE_R`（眼皮那幾行原樣未動）；鼻子加大；耳朵改葉形有厚度（tube）；新 `build_cap()`：往後推、前緣上翹，六片帽縫凹溝、帽頂鈕扣、厚帽簷（往前伸、兩端收進帽口）、圓標貼在正面布片；用 BVHTree 射線在眼睛正上方量帽子下緣，整頂移到 `CAP_BRIM_Z`。
- 新 `#region 頸巾`：`build_scarf()`（繞脖子量 Body 實際半徑的布圈＋胸前三角巾＋結），新零件 `Scarf`。
- `#region 手、鞋、郵差包`：手改手掌＋3 指＋分開的拇指；新 `build_shoes()`（鞋底厚度圓角、鞋面、鞋口軟墊、鞋舌、3 條鞋帶＋蝴蝶結，右腳鏡射）；`rounded_box()` 回傳面並正確套材質；`build_bag()` 包身往後移避開手、前後側面滾邊、頂面＋外側厚蓋口、蓋口下緣滾邊、扣環方框＋皮帶舌。
- BONES：只改位置——`Snout` 尾端 (0,-0.43,0.778)、`Left/RightEar` (±0.062,-0.072,0.915)→(±0.11,-0.035,1.12)。31 根骨頭名稱與階層不變。
- `skin_character()`：新增 `Scarf` 綁骨規則（Neck／Chest 依高度混合）。

## 介面常數最終值
- `EYE_C = (0.081, -0.219, 0.886)`（+X 那顆，-X 鏡射）
- `EYE_R = 0.036`（眼白 z 半徑 = EYE_R × 1.09；現有眼皮半徑 0.039／0.041 仍比眼白大，不會穿出）
- `CAP_BRIM_Z = 0.958`——**定義改為「眼睛正上方（眉毛區）帽子最低處的 z」**（不是帽簷前緣；帽子往後推、前緣上翹後，前緣比眉毛區高，用前緣當上限會讓眉毛穿進帽子）。眼睛上緣到帽子 0.033 m。眉毛最高不超過 `CAP_BRIM_Z − 0.005`。

## 數字
- 三角形 17,930（Body 6696、Shell 3920、ShellFill 288、Eyes 536、Nose 168、Ears 544、Cap 1258、Scarf 380、Hands 1008、Shoes 1752、Bag 1056、Strap 324）；改前 7,480。

## 步驟進度
- 讀約定與現有程式（100%）
- 第一版造型（100%）
- 重新設計：比例、帽子、頸巾、配色（100%）
- 骨架位置微調與動作檢查（100%）：6 個動作與 TEST_POSE 目視正常
- 自審 diff＋subagent 審查（約 50%）：自審已完成並修 2 處（CAP_BRIM_Z 註解、rounded_box 材質）；**subagent 審查（變形破面、權重、穿插、法線）已中途停止，沒有結果**
- 回報協調（0%）

## 恢復後第一步
1. 重新派一個 subagent 審查（範圍同上：關節塌陷、頭部細分交界彎頸、Scarf 在 Hurt 仰頭時與下巴／脖子穿插、手與郵差包穿插、tube／鞋底／帽簷／殼厚邊法線）。
2. 依結果修正、重跑 `Blender -b --python Tools/Blender/Hero3D/hero3d.py -- --out <scratchpad>` 驗收。
3. 回報協調：worktree 路徑、改了哪些函式、三角形數、對照圖、常數值、已知限制。

## 還在等的背景工作
- 無（subagent 已停止）。

## 已知限制（目前觀察到）
- 背殼甲片凸起在 3 階色階下不太明顯，主要靠剪影與厚邊；細紋要靠貼圖 session。
- 手垂下時手掌後緣仍和郵差包前緣有約 1 cm 重疊（包已往後移，視覺上像手扶著包）。
- 眼皮仍是舊程式（固定半徑），由表情 session 重建。

## 最新對照圖（絕對路徑）
- 改前：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-happy-montalcini-986b44/7979700f-ee62-4c02-82e9-70ce60f3f565/scratchpad/before/AhJia3D_turnaround.png`（同資料夾有 hero／wire／pose_hero／actions）
- 改後：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-happy-montalcini-986b44/7979700f-ee62-4c02-82e9-70ce60f3f565/scratchpad/v2/AhJia3D_turnaround.png`（同資料夾有 AhJia3D_hero.png、AhJia3D_wire.png、AhJia3D_pose_hero.png、AhJia3D_actions.png、stats.txt、AhJia3D.blend、AhJia3D.fbx）
