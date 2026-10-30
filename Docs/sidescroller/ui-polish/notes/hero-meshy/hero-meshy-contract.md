# 阿甲改用 Meshy 模型（hero-meshy）：分工與介面約定

協調 session：local_710b2a16-2546-412f-a392-4b90470056e1（本檔作者）。整合 worktree：`/Users/iulee/UnityProject/blender-hero-meshy`（分支 `feat/hero-meshy`，基於 main ac19ffb）。
看板：`http://127.0.0.1:8897/`，`python3 /Users/iulee/UnityProject/session-dashboard/boards/hero-meshy/report.py <角色> …`；角色 `rig`、`tex`、`unity`。
每個里程碑、開始審查前、回報完成前重看本檔；介面要改，先傳訊息給協調，由協調改本檔再通知各方。

## 使用者已拍板（2026-10-30）

- 主角改用 Meshy AI 模型：`/Users/iulee/Downloads/Meshy_AI_Skater_Armadillo_1030063357_texture.glb`（Meshy 6 Lite，141,520 三角形、一個材質、Base Color＋另外兩張 PBR 貼圖，2048²；面朝 -Y、Z 朝上，匯入後要正規化）。
- 方向：保留 Meshy 原貼圖（評估的 A 版），不要縮成色塊（B 版變糊，不採用）。
- 表情第一版不做（臉畫在貼圖上，沒有眼皮／嘴網格），不要有 shape key；遊戲裡任何表情相關呼叫要安全忽略、不報錯。
- 手先不修（細長爪子保留）。
- 授權 CC BY 4.0：遊戲選單（GameSelectMenu）角落加一行小字「主角模型：Meshy（CC BY 4.0）」，字要在思源黑體 TMP 字集內。
- 素材進 repo（2026-10-30 使用者原話：「放進 repo 用 LFS」）：協調已放進整合 worktree `Tools/Blender/Hero3D/meshy/`（`AhJia_Meshy.glb`、`concept.png`、`concept_refined.png`、`concept_back_shell.png`），`.gitattributes` 加 `Tools/Blender/Hero3D/meshy/** filter=lfs`。各工作 session 的 worktree 沒有這些檔，開發時仍用 `--glb` 傳 Downloads 的絕對路徑；rig 可把 `--glb` 預設值設成 `Tools/Blender/Hero3D/meshy/AhJia_Meshy.glb`（相對於專案根目錄）。工作 session 不要自己複製素材或改 `.gitattributes`。

## 參考

- 評估腳本（已驗證：匯入、正規化、Decimate 到 2 萬三角形保留 UV、Base Color × 3 階 toon＋倒殼描邊、四角度＋160px 渲染）：
  `/private/tmp/claude-501/-Users-iulee-UnityProject-blender/61498e74-1abc-4b2f-8b05-a9c78dc2accc/scratchpad/meshy_eval/textured.py`，結果圖在同層 `tex/`（`A_*.png` 是採用方向）。唯讀參考，不要改那裡。
- 設計圖：`/Users/iulee/Downloads/Meshy_AI_Armadillo Courier Concept.png`、`…Refined.png`、`Meshy_AI_7fbc1bc5…png`（斜後方甲殼）。
- 現有管線：`Tools/Blender/Hero3D/hero3d.py`（BONES 31 根、skin_character、build_actions、bake_vertex_colors、bake_outline_normals、export_fbx、build_textures）；Unity：`Assets/Editor/Hero3D/Hero3DPreviewSetup.cs`、`Assets/Art/Hero3D/AhJiaToon.shader`、`Assets/Scripts/Sidescroller/Actors/HeroVisual.cs`、`Core/SsArt.cs`；文件 `Docs/sidescroller/interfaces.md` §7.7、`Docs/sidescroller/hero3d-hd-handoff.md`。

## 介面約定

| 項目 | 約定 | 負責 |
|---|---|---|
| 跑法 | `/Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/Blender/Hero3D/hero3d.py -- --source meshy --glb <glb 絕對路徑> --out <資料夾>`；不帶 `--source` 時行為和現在完全一樣（程式化版不能壞） | rig |
| 輸出檔名 | 和現版相同：`AhJia3D.fbx`、`AhJia3D_BaseMap.png`、`AhJia3D_MaskMap.png`、`AhJia3D.blend`、預覽圖（turnaround／pose／actions／hero），外加 `AhJia3D_game_scale.png`（160px 高、遊戲尺寸） | rig（貼圖檔由 tex 模組寫） |
| 座標與尺寸 | 面朝 -Y、Z 朝上、腳底在原點、單位公尺；**總高度與現版程式化模型一致**（rig 先量現版頭頂／帽頂高度，寫進本檔），這樣 `HeroVisual.ModelScale`、碰撞與相機都不用改 | rig |
| 骨架 | `BONES` 28 根（交辦寫的 31 根有誤，現版實際 28 根）名稱與階層不變（位置依新模型重擺）；`ACCESSORY_BONES` 不變；骨頭 roll 規則不變 | rig |
| 網格 | 單一 SkinnedMesh，物件名 `AhJia_Mesh`、材質 `M_AhJia`（一個）；約 2 萬三角形（≤ 20k）；**沒有 shape key** | rig |
| UV0 | Meshy 原 UV（Decimate 時保留 UV 接縫、不重新展開），名稱 `UVMap` | rig |
| UV1 | `OutlineNormal`，沿用 `bake_outline_normals()` | rig |
| 頂點色 `Col` | RGB＝(1,1,1)（貼圖就是固有色）；A＝描邊粗細，預設 1.0，爪子／帽繩等細小部位可降到 0.6（rig 決定） | rig |
| 動作 | Idle／Run／Jump／Melee／Shoot／Hurt 6 個 Action 名稱不變，沿用現有關鍵格（必要時依新比例微調）；動作裡不能 key 任何 shape key | rig |
| 貼圖模組 | `Tools/Blender/Hero3D/meshy_texture.py`，不 import bpy 以外的第三方套件（Blender 內建 numpy 可用）。進入點：`build_meshy_textures(images, out, size=1024) -> (base_path, mask_path)`；`images` 是 dict：`{"base": <Base Color png 路徑>, "metal_rough": <路徑或 None>, "normal": <路徑或 None>}`（rig 從 glb 解出到暫存資料夾後傳入）；寫出 `out/AhJia3D_BaseMap.png`（sRGB）與 `out/AhJia3D_MaskMap.png`（linear），皆 `size`²。純貼圖空間運算，不依賴網格 | tex |
| `_MaskMap` | 沿用現有 shader 語意：R＝高光遮罩、G＝陰影偏移／AO（0.5＝不偏移，越低越容易進陰影）、B＝材質類別（0 皮膚、0.33 殼、0.66 布、1 金屬／眼睛）。依 Base Color 顏色分類產生 | tex |
| Shader／材質預設 | `Assets/Art/Hero3D/AhJiaToon.shader` 由 tex 負責（Meshy 貼圖自帶光影與 toon 色階疊加的處理、必要的參數預設值）；改了要全平台（Android／WebGL／Editor Metal）編譯 0 錯誤。`Hero3DPreviewSetup.cs` 由 unity 負責；tex 需要材質預設值改變時，寫進 shader 的 Properties 預設值，或把要的數值傳給 unity／協調 | tex／unity |
| Unity 程式 | `Hero3DPreviewSetup.cs`、`HeroVisual.cs`、`SsArt.cs`、`GameSelectMenuView.cs`（授權小字）由 unity 負責；不序列化引用遊戲模組型別（HybridCLR 限制），資源走既有 AssetPackage 群組 `sidescroller_models` | unity |

## 交接中途檔

rig 有第一版可用的 FBX＋貼圖（權重粗也可以）就放到 `/Users/iulee/Library/Application Support/Claude/side-session-notes/local_553e400a-bc1b-493f-a5df-47c8fabd86ec/hero-meshy-drop/<日期時間>/`，並傳一句給協調；協調轉知 unity。之後每有大改版再放一份。tex 同理，可把試算的貼圖放 `hero-meshy-drop/tex-<日期時間>/`。

## 共通規則

- 每個工作 session 在自己的 worktree（從 `feat/hero-meshy` 開出）改，**不 commit、不 push**；完成後回報 worktree 路徑與交接檔，協調用 `git diff` 合進 `feat/hero-meshy`。
- 檔案所有權：rig＝`hero3d.py`；tex＝`meshy_texture.py`、`AhJiaToon.shader`（與 `M_AhJiaToon.mat` 若有）；unity＝`Assets/Editor/Hero3D/*`、`Assets/Scripts/**`、`Docs/sidescroller/interfaces.md`。其他檔要改先問協調。`design.md` 等文件、commit／push、上傳 GCS、出包，一律先問使用者。
- 輸出圖、FBX 放自己的 scratchpad，不要放進 repo（模型進 `Assets/Art/Sidescroller/Models/SsHero3D/` 由協調整合時做）。
- 專案規範：給使用者的文字繁中；C# 與 Python 分區塊用 `#region`；程式與註解不得出現 舊的模組命名字樣（改用 AppBootstrap／模組）（用 AppBootstrap／模組）；註解密度與既有程式一致；commit 前綴規則（雖然你們不 commit）。
- Unity：同一專案路徑一次只能開一個 Unity（`pgrep -f '^/Applications/Unity/Hub/Editor/[^ ]*/Unity.app/Contents/MacOS/Unity .*<專案路徑>'`）；batchmode 後被改寫的 `Assets/Settings/*.asset` 要還原。Unity 6000.3.27f1。
- 效能預算（S22 Ultra 等級＋手機 WebGL）：≤ 20k 三角形、骨頭 31、1 SkinnedMeshRenderer、1 材質（主體＋描邊 2 pass）、貼圖 2 張 1024²。
- 交差前：自審 diff、subagent review＋反向驗證、修好、重跑通過；交接檔寫在本資料夾 `hero-meshy-<角色>-handoff.md`。
- 使用者在你的 session 下的決定（同意、否決、選方案）要立即傳給協調，附使用者原話。

## 協調補記

（rig 量到的現版總高度、介面異動，記在這裡）
- 2026-10-30 14:50：工作 session 由使用者按提案卡片啟動，worktree 由 app 從 main（ac19ffb，與 `feat/hero-meshy` 同一點）開出，不是直接從 `feat/hero-meshy`；兩者內容相同，不影響。
- 2026-10-30 15:05（rig）：現版程式化模型（main ac19ffb，靜止姿勢、不含描邊）實測：**帽頂 1.041 m**（不含耳朵的最高點）、**總高 1.122 m**（耳尖）、寬 ±0.282、前後 -0.453（嘴尖）～0.430（尾尖）。Meshy 模型的最高點就是帽頂（耳尖約低 3%），所以 meshy 模式把**帽頂對齊 1.041 m**（等比縮放），整體高度與現版一致；耳朵本來就比現版短，總高（含耳）會是約 1.041 m 而不是 1.122 m。另外 Meshy 原姿勢不對稱（左手往後擺約 30°、右手往前約 38°、右腳在前約 15 cm），rig 會在綁好權重後把四肢擺回對稱的站姿再套成靜止姿勢，動作才不會偏。
- 2026-10-30 15:05：tex 已交差，改動（`meshy_texture.py`、`AhJiaToon.shader` 的 `_RampStrength`、`M_AhJiaToon.mat` 設 0.6）已套進整合 worktree。
- 2026-10-30 15:10：**rig 拆分**（使用者原話：「#1 的工作好像太繁重／有辦法在拆分細一點給其他session分著做嗎？」→ 選「照方案拆」）：
  - **rig**：專心匯入、骨架、權重、匯出與修正；**不再調動作關鍵格**。請在 `hero3d.py` 先加一個空的 `#region Meshy 動作修正`：`MESHY_ANIM_FIXES = {}`（`{動作名: {骨名: {格數: (x, y, z) 度的增量}}}`，格式由 anim 定、可改）與 `apply_meshy_anim_fixes(arm, actions)`（目前什麼都不做），並在 meshy 模式 `build_actions` 之後呼叫一次。這個 region 歸 anim，rig 之後不要改。第一版 FBX 放 drop 時，**同一個資料夾附上當時的 `hero3d.py` 快照**。
  - **anim**（新）：只改 `#region Meshy 動作修正`。等 rig 的 drop 出現（有 `hero3d.py` 快照）後，在自己的 worktree 用那份快照跑 meshy 模式，逐一檢查 6 個動作（肩甲、殼、包、帽子穿插、長嘴、爪子、腳底滑步、Jump 落地高度），用修正表調；交差時給協調那個 region 的完整內容，協調貼進 rig 的最終版。
  - **review**（新，只讀）：獨立審查 rig 的 Meshy 匯入、正規化、Decimate 保留 UV、骨架擺位、權重規則、頂點色／描邊法線、匯出設定、不帶 `--source` 時程式化版不變；發現問題直接 SendMessage 給 rig（副本給協調），不改檔。
- 2026-10-30 15:15：rig 第一版 drop `rig-20261030-1459/`（19,800 三角形、28 根骨頭、權重粗、貼圖暫代、無 `hero3d.py` 快照）。已轉 unity（貼圖改用 tex 正式版、shader 從 tex worktree 拷來測）與 anim（先用 rig worktree 現檔當快照）。骨頭數更正：現版實際 28 根。
- 2026-10-30 15:15：unity 已交差，5 檔改動（Setup、GameSelectMenuView、StoryTexts、HeroVisual 註解、interfaces.md）已套進整合 worktree。觀察：新模型遊戲中看起來比舊版稍瘦小（高度相近），整合時給使用者並排比較再決定要不要加大 `ModelScale`。
- 2026-10-30 15:35（anim）：`#region Meshy 動作修正` 格式定案：`MESHY_ANIM_FIXES = {動作名: {骨名: {格數: (x, y, z)}}}`，度、繞骨頭局部軸、正負號同 ANIMS，在 build_actions 後的曲線上「該格取值＋增量再 key 回去」（沒 key 的格會補 key）。擴充：格數可寫 `"*"`＝該骨頭在此動作的每個 key 都加；骨名 `"@Hips"`＝Hips 位移增量（公尺、骨頭局部、Y＝上）；循環動作改第 0 格會連最後一格一起改。另有 `MESHY_BALL_ACTIONS = ["Jump"]`：逐格補 Hips 位移讓縮成的球繞球心轉、最低點貼地（Meshy 捲起來球心離 Hips 約 0.18 m，不補會上下晃約 0.37 m）。進入點仍是 `apply_meshy_anim_fixes(arm, actions)`，在 meshy 模式 build_actions 之後呼叫一次；需要 hero3d.py 既有的 `ANIMS`、`_fcurves`、`_finish_curves`、`use_action`。
- 2026-10-30 15:30：另開 ui session（local_b85be610…，UI 品質優化，獨立於主角線）。使用者決定（原話選項）：方向「B A＋新增 UI 美術素材」；試做「先試做暫停＋通關 (Recommended)」；截圖工具「留著 (Recommended)」（`Assets/Editor/UiShots/UiShotCapture.cs`）。不動 `GameSelectMenuView.cs`。
- 2026-10-30 15:27：使用者補充 ui 範圍，原話：「UI優化也包括左邊的移動按鈕和右邊的數個功能按鈕」，已轉 ui。
- 2026-10-30 15:40：使用者決定（經上層 session 轉達）ui 線的 UI 美術素材改用 ComfyUI 產生，原話：「我想用「ComfyUI」來實作我開的session「優化ＵＩ」」。安裝與模型下載需先列檔名／來源／大小取得使用者同意；模型授權須可商用（FLUX.1 dev 不可）。已轉 ui。
- 2026-10-30 15:50：ui 使用者決定（原話選項）：模型「FLUX.1 schnell」；選配「IP-Adapter（統一風格）,BiRefNet（去背）」；SsUiSkin「全部改 ComfyUI」。IP-Adapter 與 FLUX schnell 授權／相容性衝突，ui 正在請使用者釐清，尚未下載。
- 2026-10-30 15:55：ui 使用者決定（原話）：風格一致配置「幫我挑一個可以做得出比較高品質的配置」；下載「同意下載 (Recommended)」。ui 選 FLUX.1 schnell（Apache 2.0）＋SDXL base 1.0（OpenRAIL++-M）＋IP-Adapter SDXL vit-h 與 image encoder（Apache 2.0）＋BiRefNet（MIT），約 28GB，裝在 ~/ComfyUI。
- 2026-10-30 16:05：ui 使用者決定（原話選項）：素材「外框組（必要）,觸控按鈕組（必要）,選單主視覺」（HUD 圖示不重繪）；腳本「放進 repo Tools/ComfyUI (Recommended)」。選單主視覺會動 `GameSelectMenuView.cs`（主角線已加授權小字，合併時要保留），ui 動之前會通知協調。
- 2026-10-30 16:10：ui 使用者指示（原話）：「暫停和通關畫面重做一個版本給我看」→ ui 用 ComfyUI 外框素材重做暫停＋通關，先給使用者看再擴到其他畫面。
- 2026-10-30 16:25：使用者：「先試放大看看」→ 試 ModelScale 1.5／1.7 截圖（scale_compare.png），程式已還原 1.3 等使用者選。使用者：「把#6 優化項目分給多個session同步進行」→ 選「拆成 4 個」「ui 先 commit 到自己的分支」；ui 分支 commit 719cf90；開 touch（local_40b88b64…）、menu（local_a3fb98cc…）、hud（local_9c150b82…），約定見 ui-split-contract.md。
- 2026-10-30 16:25：使用者選 ModelScale，原話：「用 1.5」→ HeroVisual.ModelScale 1.3→1.5，interfaces.md 同步。
- 2026-10-30 16:35：使用者：「主角的修改就直接commit + push」→ feat/hero-meshy commit 58aefda，已 push 到 origin（LFS 4 檔）。尚未合進 main。
- 2026-10-30 16:45：使用者：「合進 main」→ 主 checkout main 快轉到 58aefda（本機），尚未 push main。
- 2026-10-30 16:50：使用者：「push main」→ origin/main 已到 58aefda。
- 2026-10-30 16:55：使用者：「剛剛移除的收工卡片 也幫我把session刪掉吧」→ 已永久刪除 rig、tex、unity、anim、review 五個 session（含 worktree；成果已在 main 58aefda）。
- 2026-10-30 17:00：使用者：「全部清掉」→ 移除 5 個已刪 session 的殘留 worktree 與整合 worktree blender-hero-meshy（分支 feat/hero-meshy 保留，已在 origin 與 main）。
