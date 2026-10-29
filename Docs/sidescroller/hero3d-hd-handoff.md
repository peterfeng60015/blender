# 阿甲 3D 精緻度升級：交接（2026-10-29 下班暫停）

作者：協調 session（local_b8eaa63d-3326-4f44-8c4a-088f0c07e17e，上層 local_553e400a-bc1b-493f-a5df-47c8fabd86ec）。
狀態：**四路實作都已暫停**。Shader 已完成；造型、貼圖、表情都已實作完，各自卡在「subagent 審查」中途（審查已中止，沒有結果）。還沒整合。

## 0. 換電腦接手（使用者原話：「所有改動中的內容通通上傳／我要開其他電腦接著做」）

四路改動各自 commit 在自己的分支並 push 到 GitHub（`peterfeng60015/blender`），都基於 main 8d8c3db、**都還沒合進 main**：

| 角色 | 分支 | commit | 改動 | 審查 |
|---|---|---|---|---|
| 造型 | `claude/happy-montalcini-986b44` | e826ac2 | `hero3d.py` | subagent 審查未完成 |
| 貼圖 | `claude/epic-easley-7a5be3` | 6e52fd4 | `hero3d.py` | subagent 審查未完成 |
| Shader | `claude/sweet-goldberg-f4bbf6` | d4fd1b1 | `AhJiaToon.shader`、`M_AhJiaToon.mat`、`Hero3DPreviewSetup.cs`、`hero3d.py` | 已完成 |
| 表情 | `claude/musing-edison-9fdf4c` | 74a2d49 | `hero3d.py` | subagent 審查未完成 |
| 整合／交接 | `hero3d-hd` | 本分支 | 本文件與 `hero3d-hd-notes/` | — |

新電腦上：
```bash
git fetch origin
git worktree add ../blender-hero3d-hd hero3d-hd
git worktree add ../hero3d-shape claude/happy-montalcini-986b44
git worktree add ../hero3d-texture claude/epic-easley-7a5be3
git worktree add ../hero3d-shader claude/sweet-goldberg-f4bbf6
git worktree add ../hero3d-face claude/musing-edison-9fdf4c
```
各路的進度檔、分工約定、Shader 的截圖與驗證腳本、關鍵渲染圖都放進了 `Docs/sidescroller/hero3d-hd-notes/`（下面 §2 的本機路徑在新電腦上不存在，改看這個資料夾）：
- `contract.md`：分工與介面約定（含追加決定、造型提案、看板回報）
- `pause-shape.md`、`pause-texture.md`、`pause-face.md`：暫停時的進度與「恢復後第一步」
- `face-handover.md`：表情細節；`shader/HANDOVER-shader.md`：shader 參數表與相容性建議，同資料夾有截圖與驗證腳本
- `renders/`：新造型的 hero／turnaround、表情總覽、貼圖（舊模型上）效果；其餘圖跑 `hero3d.py` 約 40 秒可重產

**沒有跟著上來的**（只在原電腦）：
- 4 個工作 session 本身（對話紀錄、context）：新電腦要重開 session，交辦時給它這份文件與對應的 `pause-*.md`。
- 監控看板 `session-dashboard`（本機 repo，沒有遠端）與這次對它文件的修改。
- 全域 `~/.claude/CLAUDE.md` 這次新增的規則（變成協調就自動開看板、看板要在開工作 session 之前開好），新電腦要自己補。

## 1. 目標與使用者的決定

- 起點：把《熱浪急件》主角阿甲的 3D 模型精緻度提升到接近 Blender Studio《Spring》截圖的等級，提高貼圖與 shader 品質。
- 2026-10-29 使用者的決定（原話或選項）：
  - 範圍「A＋B＋C：含表情」：貼圖＋shader、造型重做、臉部表情＋二次動畫。
  - 畫風「Cowboi 色階＋提升精緻度」：保留粗描邊、硬色階，不改走 Spring 的柔和電影感。
  - 「直接改 hero3d.py」：不另開新腳本，仍是 `Tools/Blender/Hero3D/hero3d.py` 一支。
  - 「你用blender這個軟體實作模型吧」「把工作盡可能拆分給多個session同步進行」「你這個session變成協調用」。
  - 「整體模型可以重新設計／不需要按班原本的模型」，追問後選「連角色設定也可改」「骨架也可重做」。造型 session 的提案保留了 31 根骨頭的名稱與階層（Unity 端免改），其餘重新設計（見 §3）。
  - 從看板把四個工作 session 的 effort 改成 xhigh。
  - 「我準備要下班了／讓還在工作的session暫停／並寫下交接文件推上repo」。

## 2. 各路現況

| 角色 | session | worktree（分支） | 進度 | 恢復後第一步 |
|---|---|---|---|---|
| 造型 shape | local_d9ad93e1-a64c-4b4c-93eb-5a933b1dfdae | `.claude/worktrees/happy-montalcini-986b44`（`claude/happy-montalcini-986b44`） | 重新設計版完成，17,930 三角形；自審已修 2 處；subagent 審查未完成 | 重派 subagent 審：關節塌陷、頭部細分交界、Scarf 在 Hurt 仰頭時穿插、手與包穿插、厚邊法線 |
| 貼圖 texture | local_bb133c17-d67b-4c7a-bb72-e891fac989cc | `.claude/worktrees/epic-easley-7a5be3`（`claude/epic-easley-7a5be3`） | UV atlas＋程式化 _BaseMap／_MaskMap 完成（舊模型上驗過）；自審已修；subagent 審查未完成 | 重派 subagent 審 diff，修完重跑 |
| Shader | local_ad062894-4d2b-456d-814e-cf3e86696f27 | `.claude/worktrees/sweet-goldberg-f4bbf6`（`claude/sweet-goldberg-f4bbf6`） | **完成**：shader 重寫、描邊平滑法線、預覽設定；全平台編譯通過；subagent 審查後已修 | 無（等整合） |
| 表情 face | local_8ffa71fa-cb06-4f8f-80e7-14286d6e5715 | `.claude/worktrees/musing-edison-9fdf4c`（`claude/musing-edison-9fdf4c`） | Brows／Eyelids／Mouth＋9 個 shape key、表情與二次動畫疊加層、FBX 驗證通過；subagent 審查未完成 | 重派 subagent 審：`apply_overlap` 旋轉合成與循環接縫、shape key 頂點順序、`jaw()` 範圍 |

worktree 都在 `/Users/iulee/UnityProject/blender/` 底下，基於 main 8d8c3db。整合用的 worktree 是 `/Users/iulee/UnityProject/blender-hero3d-hd`（分支 `hero3d-hd`，就是這份文件所在的分支）。

各路的詳細進度檔（本機，不在 repo）：
- `/Users/iulee/UnityProject/session-dashboard/boards/hero3d-hd/pause/shape.md`、`texture.md`、`face.md`
- Shader：`/Users/iulee/Library/Application Support/Claude/side-session-notes/local_b8eaa63d-3326-4f44-8c4a-088f0c07e17e/shader/HANDOVER-shader.md`（參數表、截圖、WebGL／Android 建議）
- 表情：同資料夾的 `hero3d-expression-handover.md`
- 分工與介面約定：`/Users/iulee/Library/Application Support/Claude/side-session-notes/local_553e400a-bc1b-493f-a5df-47c8fabd86ec/hero3d-hd-contract.md`

## 3. 新造型（造型 session 的重新設計）

- 約 2.9 頭身；頭放大約 12%（頭半徑 0.132）、眼睛加大、長嘴加長；臉有顴骨、嘴側凹、收細嘴尖、下顎線、眉骨、眼窩。
- 快遞帽往後推、前緣上翹，六片帽縫、帽頂鈕扣、厚帽簷。
- 短褲改深藍 `#34406B`；新增芥末黃頸巾 `Scarf`（綁 Neck／Chest）；鞋帶新鍵 `lace`。帽紅、包青綠、鞋棕保留。
- 背殼：肩甲＋5 條活動帶＋臀甲，甲片凸起、圓角厚邊、前後甲波浪下緣。手 3 指＋拇指；鞋有鞋底厚度、鞋舌、鞋帶與蝴蝶結；包有滾邊、厚蓋口、扣環。
- 骨架：31 根骨頭名稱與階層不變，只移動 Snout、Left/RightEar 的位置。
- 改前 7,480 → 改後 17,930 三角形（預算 ≤ 20k）。
- `Docs/sidescroller/design.md` 的主角設定（配色、外型）**還沒改**；改之前要先問使用者。

## 4. 介面約定（摘要）

| 項目 | 約定 |
|---|---|
| UV0 | 貼圖 atlas（貼圖 session） |
| UV1 `OutlineNormal` | 描邊用平滑法線，切線空間 xy（Shader session） |
| 頂點色 `Col` | RGB＝固有色（後備）；A＝描邊粗細，主體 1.0、小配件 0.6（貼圖 session 寫入） |
| `_BaseMap` | sRGB 1024² albedo |
| `_MaskMap` | linear 1024²：R 高光、G 陰影偏移（0.5＝不偏移）、B 材質類別（0 皮膚、0.33 殼、0.66 布、1 金屬／眼睛） |
| Blend shape | `Blink_L` `Blink_R` `Brow_Angry` `Brow_Sad` `Brow_Up` `Mouth_Open` `Mouth_Smirk` `Mouth_Frown` `Squint` |
| 表情進動作 | 表情值 key 在骨架自訂屬性，driver 驅動 shape key，FBX 匯出時逐格烘進 6 個 take |
| 臉部常數（以造型為準） | `EYE_C = (0.081, -0.219, 0.886)`、`EYE_R = 0.036`、`CAP_BRIM_Z = 0.958` |
| 預算 | ≤ 20k 三角形、骨頭 ≤ 40、1 個 SkinnedMeshRenderer、1 材質（2 pass）、2 張 1024² 貼圖、blend shape ≤ 12 |

## 5. 整合時要處理的跨邊界問題（協調已知）

1. **`CAP_BRIM_Z` 的定義改了**：造型改成「眼睛正上方帽子最低處的 z」（不是帽簷前緣），因為新帽子前緣上翹。表情 session 用的是暫定值 0.936，整合時改成 0.958，換新帽子後要重看眉毛。
2. **常數衝突**：表情 session 自己在參數區加了 `EYE_C`、`EYE_R = 0.034`、`EYE_DIR`、`CAP_BRIM_Z`，造型也加了；合併時保留造型的值，`EYE_DIR` 保留。
3. **`main()` 呼叫順序**：`skin_character()` →（表情）`build_face`／`add_expression_drivers` →（貼圖）`build_textures(mesh, out)`（要在靜止姿勢、描邊 Solidify 之前，且 UVMap 必須是第一個 UV 層）→（Shader）寫 `OutlineNormal` UV1 → 匯出。四路都改了 `main()` 與 `skin_character()` 的 rules，會有合併衝突，手動排序。
4. **Body 帶 shape key 之後不能再呼叫 `bake_modifiers()`**（會丟掉 shape key）。
5. **Mouth 是貼片**：頂點色 A 建議設 0（不描邊）；貼圖 session 寫 A 時要讀得到這個例外。
6. **貼圖對新造型的假設**（貼圖 session 自己列的）：殼紋列座標寫死對齊舊殼帶切法；帽簷車縫用「法線近乎垂直」判斷，上翹帽簷可能抓不到；鞋帶目前畫在 shoe 材質上，造型已有獨立鞋帶零件，可拿掉那段；`scarf`、`lace` 已先對到布紋，還沒在新造型上實跑。
7. **Shader 的暫時處理**：沒有 atlas UV 時用方盒投影補 UV 算切線，頂點數暫時變多（Unity 4025 → 6345）；貼圖的 atlas 進來後就不用。陰影色、rim、高光的預設值是用頂點色模型調的，貼圖進來後要再調一次。
8. **Unity 端**：`Assets/Art/Sidescroller/Models/SsHero3D/SsHero3D.fbx` 目前是 main 版；整合後要用新 hero3d.py 重新匯出，連同 `SsHero3D_BaseMap.png`／`SsHero3D_MaskMap.png` 放在模型旁，跑「設定模型與預覽場景」（`Assets/Editor/Hero3D/Hero3DPreviewSetup.cs`）。骨頭名稱沒變，`SsHero3D_Anim.controller` 與 `Actors/HeroVisual.cs` 預期不用改，要實測。走 YooAsset 群組 `sidescroller_models`，要重新出資源包。

## 6. 恢復步驟

1. 協調 session 讀這份文件與 `hero3d-hd-notes/` 的進度檔，通知三個暫停的 session 恢復（各自重派 subagent 審查、修正、重跑、回報）。
2. 三路回報完成後，協調在 `/Users/iulee/UnityProject/blender-hero3d-hd` 依序合併四個分支（使用者同意後；或 `git diff main..<分支> | git apply -3`），依 §5 解衝突，跑一次 `Blender -b --python Tools/Blender/Hero3D/hero3d.py -- --out <scratchpad>`，看新造型＋貼圖＋表情的渲染。
3. 跨邊界審查（只看 §5 那些介面，各模組內部已由各自 session 審過），再做 Unity 端匯入與預覽截圖、改前改後對照。
4. 問使用者是否改 `Docs/sidescroller/design.md` 的主角設定；交使用者 review；commit／push 都要先問。
5. 全部完成後，收尾選項附「刪除所有子session」。

## 7. 監控看板

- `session-dashboard` 的看板 `hero3d-hd`：`/Users/iulee/UnityProject/session-dashboard/scripts/start.sh hero3d-hd 8897`，用內建瀏覽器開 `http://127.0.0.1:8897/`。
- 協調 session 的每 10 分鐘排程與變更請求的 Monitor 只活在那個 session 裡；換 session 接手要照 `session-dashboard/docs/coordinator.md` 重新掛上。
