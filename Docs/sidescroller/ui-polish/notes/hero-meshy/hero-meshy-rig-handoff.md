# hero-meshy rig 交接

- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/confident-engelbart-f3a2fb`（分支 `claude/confident-engelbart-f3a2fb`，從 main ac19ffb 開出），**未 commit、未 push**。
- 交付物只有 `Tools/Blender/Hero3D/hero3d.py`。worktree 裡另有從 tex 拷來測試的 `Tools/Blender/Hero3D/meshy_texture.py`（未追蹤，不是 rig 的交付物，整合時用 tex 的版本）。
- 最新 drop：`hero-meshy-drop/rig-20261030-1550/`（第六版定稿；1545 是同一個模型、少了歸零檢查）（FBX、貼圖、預覽、check/、tools/、`hero3d.py` 快照）。

## 跑法

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/Blender/Hero3D/hero3d.py -- --source meshy [--glb <glb>] --out <資料夾>
```

`--glb` 不給時預設 `Tools/Blender/Hero3D/meshy/AhJia_Meshy.glb`（相對腳本所在資料夾）。約 1.5 分鐘。不帶 `--source` 是原本的程式化版。

## 改了什麼（hero3d.py）

- 既有程式（行為不變）：roll 規則抽成 `align_bone_roll(eb)`；`build_armature(bones=BONES)`；`build_actions(arm, face=True)`（face=False 不 key 表情）；`main()` 加 `--source`／`--glb`，meshy 分支只有 4 處（建模換成 `build_meshy`、`build_actions(face=False)` 後呼叫 `apply_meshy_anim_fixes`、不出表情圖、多出 `meshy_extra_renders`）；檔頭用法說明。
- `#region Meshy 來源`（新）：
  1. `import_meshy`：匯入 glb → 焊接（UV 接縫拆開的頂點）→ 正規化（面朝 -Y、Z 上、腳底 0、**帽頂 1.041 m**）→ Decimate（collapse，保留 Meshy UV）到 19,600。
  2. `meshy_bones(mesh)`：28 根骨頭照 `MESHY_BONES`（在 Meshy 原姿勢上量的），Foot 方向改量鞋子長軸。
  3. `skin_meshy`：熱擴散 → `meshy_detach_bag`（郵差包從肚子整塊拆開、兩邊開口補片；再刪掉 < 5 cm 的鬆散碎塊、零面積面、背對背重疊的補片）→ 規則：上臂骨只管離骨頭 6.5～8.5 公分內（肩甲歸軀幹）、z>0.85～0.89 漸變給 Head（帽子、頭頂）、耳朵、鞋→Foot、包（拆開的那塊）→Bag、背帶→軀幹；每頂點最多 4 骨；`OutlineWidth`（手 0.6、其餘 1.0）。
  4. `meshy_straighten`：四肢、耳朵左右取平均方向，Snout／Tail1～3 轉回中線 → 套成靜止姿勢 → 左小腿拉長 7.9 mm 讓兩鞋底同高 → 重新落地、縮回帽頂 1.041 → 照原規則重設 roll。
  5. `meshy_textures`：glb 三張貼圖解到暫存資料夾，交給 `meshy_texture.build_meshy_textures(images, out, size=1024)`；沒有這個模組時用暫代版。
  6. 檢查圖：`AhJia3D_weights_raw.png`／`AhJia3D_weights.png`（擺正前／後的權重）、`AhJia3D_actions_front.png`／`_back.png`、`AhJia3D_game_scale.png`（160px，面向右）。
- `#region Meshy 動作修正`（新，空殼，**歸 anim**）：`MESHY_ANIM_FIXES = {}`、`MESHY_BALL_ACTIONS = []`、`apply_meshy_anim_fixes(arm, actions)`。

## 輸出（第六版實測，FBX 重新匯入量的）

- 19,664 三角形；連通塊 3 個（身體、包、原模型就有的體內小塊）、非流形邊 0、邊界邊 0、UV 零面積面 0；`AhJia_Mesh`、材質 `M_AhJia`、UV `UVMap`＋`OutlineNormal`、`Col` RGB 全白、A 只有 0.6／1.0；沒有 shape key；28 根骨頭；6 個 clip（不 key 表情）。
- 高度 0～1.041 m；兩鞋底 z＝0；每頂點最多 4 骨。
- Snout、Tail1～3、Foot 的局部 X 軸是左右方向（同程式化版；Foot 因鞋子外撇約 18° 跟著斜）。

## 檢查圖

drop 的 `check/check_<動作>.png`（關鍵格；上排 3/4 前、中排側面、下排 3/4 後）、`AhJia3D_actions*.png`、`AhJia3D_pose*.png`、`AhJia3D_weights*.png`。

## 驗證

- **程式化版回歸**：改後跑一次和改前比，FBX 頂點、權重、頂點色、骨頭、1,788 條動作曲線、10 個 shape key、stats 全部相同；檔名清單相同。BaseMap／預覽圖像素不同，是原版本身的 UV 打包不確定（原版跑兩次也一樣不同）。
- **拉伸量測**（`tools/stretch.py`，每格邊長／靜止邊長）：第一版 Melee 最大 197 倍 → 現在 5.3 倍；Run 19.5 → 5.4。
- **反向驗證**（scratchpad 的突變版）：
  - 鞋綁到另一隻腳 → Run 超過 2.5 倍的邊 21 → 566，量測抓得到。
  - 不拆包 → 防呆直接丟錯（「郵差包沒有拆成獨立的一塊」）。
  - 關掉上臂規則 → 拉伸數字反而變小，量測抓不到；靠特寫圖看得出差別（抬手時背殼上緣跟著隆起）。所以這條規則要靠看圖驗。
- **review**：
  - review session 的 3 項（尾巴 roll、超過 4 骨、鞋子不對稱）已修，並用 FBX 量過。
  - 自己開的 subagent 9 項：尾巴 roll、補片 UV、空遮罩防呆、補不到的開口警告、暫存檔清理、影像重複刪除、錯誤訊息都已修。
  - 「上臂規則不要套到手肘以外」試過後變差（殼的側邊被前臂拉走，Shoot 拉伸邊 116 → 138），所以沒採用，程式註解有寫原因。
  - 「耳朵規則沒有前後範圍」實測眼睛高度沒有頂點拿到 Ear 權重，沒改。
  - review 第二輪：補片描邊法線（第四版已修，UV 零面積面 171 → 2）；背帶接口切出的 3 小片浮在原地 → 第五版改成跟著包走；協調裁示要整個修乾淨 → 第六版改成：刪 < 5 cm 的鬆散碎塊、`dissolve_degenerate` 清零面積面、刪背對背重疊的補片，包範圍上緣 0.57 → 0.60（切口移到背帶環上方的平直段，切在環上會切出夾點）。非流形邊 8 → 0、零面積面 2 → 0，並加歸零檢查（拆包後不是 0 就 RuntimeError；反向驗證：上緣改回 0.57 會丟錯）。試過但沒採用：塌縮非流形邊（變更多）、刪鄰近 1～3 圈面重補（剩 1 條或留 29 個夾點）。

## 已知問題

- 抬手超過約 70°（Shoot、Melee）時腋下有拉伸（靜止邊長 1～9 mm 的邊拉到 5～10 倍），被手臂擋住。程式化版這裡沒有。
- 郵差包甩起來時肚子上看得到補片（單色、邊緣不規則），背帶末端留在身體上（背帶歸軀幹）。背帶環的小碎塊刪掉後，環在靜止時看起來略細。
- Jump 尾巴在中線上捲進球裡，球內部有拉伸（看不到）；Jump 的球心、落地交給 anim。
- 骨頭位置是在 Meshy 原姿勢上目測量的（`MESHY_BONES`），換別的 glb 要重量。
- `MESHY_LOOSE` 是對整個網格刪 < 5 cm 的鬆散碎塊。目前這個 glb 只刪到背帶環的碎片；換模型時，若有小的獨立零件會被靜默刪掉，可改成只刪包附近的，或刪到時報錯（review 提的資訊項，不擋）。
- review 已複驗定稿 1550，通過：拆口描邊法線 95% 8°、沒有超過 90° 的；.blend 與 FBX 往返後非流形邊、零面積面、邊界邊都是 0；歸零檢查的反向驗證有效。審查結束。
- 新模型總高（含耳）是 1.041 m，現版含耳是 1.122 m；遊戲裡看起來會稍小。unity 也觀察到這點，要不要加大 `ModelScale` 由使用者決定。
