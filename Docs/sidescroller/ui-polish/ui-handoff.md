# UI 優化（ui 角色）交接（2026-10-30 暫停）

分支 `claude/upbeat-golick-2db7a2`（worktree `/Users/iulee/UnityProject/blender/.claude/worktrees/upbeat-golick-2db7a2`，從 main 開出）。
協調 session：local_710b2a16-2546-412f-a392-4b90470056e1。拆分約定：side-session-notes `local_553e400a…/ui-split-contract.md`。
品質問題清單：side-session-notes `local_710b2a16…/ui-polish-findings.md`。

## 完成到哪
1. **盤點與截圖（完成）**：全部 UI 畫面的改前截圖、品質問題清單 8 項（含追加的觸控搖桿與按鈕）。
2. **截圖工具 `Assets/Editor/UiShots/UiShotCapture.cs`（完成）**：從 Entry 進 Play，依序拍選單或遊戲內各畫面（演出、HUD、暫停、背包、強化、字卡、通關、直向）。
   不加 `-batchmode`，開一般 Editor 視窗、把 Game 視窗切成 2316×1080／1920×1080／1080×2316 再用 ScreenCapture 拍（batchmode 的 Screen 固定 640×480，版面會錯）。
   ```
   Unity -projectPath <worktree> -executeMethod UiShotCapture.RunBatch -uiShotMode game|menu -captureDir <資料夾> -logFile <log>
   ```
3. **程式繪製版試做（完成，但已決定換掉）**：`UiSkin.cs`（九宮格外框、陰影標題字、`UiPop` 進場動畫）、`MenuList` 可選外框與選中放大、`StoryUi` 暫停（斜角面板＋緞帶標題）與通關（光芒、星光、彈入標題、時間標籤、通關時隱藏 HUD）。
4. **ComfyUI 管線（裝好、腳本完成、生圖進行到一半）**：見下方。

## 使用者的決定（原話）
- 開 session：「開一個session／幫我優化目前遊戲的ＵＩ／現在的版本品質太差」
- 方向「B A＋新增 UI 美術素材」；試做「先試做暫停＋通關 (Recommended)」；截圖工具「留著 (Recommended)」
- 追加範圍（經協調）：「UI優化也包括左邊的移動按鈕和右邊的數個功能按鈕」
- 改用 ComfyUI（經協調）：「我想用「ComfyUI」來實作我開的session「優化ＵＩ」」
- 模型「FLUX.1 schnell」；選配「IP-Adapter（統一風格）,BiRefNet（去背）」；SsUiSkin「全部改 ComfyUI」
- 風格一致配置「幫我挑一個可以做得出比較高品質的配置」→ 我選 FLUX.1 schnell＋SDXL base 1.0＋IP-Adapter＋BiRefNet；下載「同意下載 (Recommended)」
- 素材範圍「外框組（必要）,觸控按鈕組（必要）,選單主視覺」（HUD 圖示不重繪）；腳本「放進 repo Tools/ComfyUI (Recommended)」
- 「暫停和通關畫面重做一個版本給我看」（用 ComfyUI 素材重做，**尚未完成**）
- 拆分（經協調）：「把#6 優化項目分給多個session同步進行」，「拆成 4 個（建議）」、「ui 先 commit 到自己的分支（建議）」
- 下班（經協調）：「我準備要下班了／通知所有session／暫停目前工作／並寫下交接文件／commit + push上repo」

## 改了哪些檔（本分支相對 main）
| 檔案 | 內容 |
|---|---|
| `Assets/Scripts/Sidescroller/Story/UiSkin.cs`（新） | 讀 `SsUiSkin_Idle` 建九宮格 sprite（10 格新排版；素材沒讀到時退回純色）、`Title`（8 方向錯位深紫字當外框＋洋紅陰影，不用 SDF 描邊：字集 padding 只有 5，描邊會吃掉筆畫）、`Label`、`UiPop` |
| `Assets/Scripts/Sidescroller/Story/MenuList.cs` | 建構子加可選 `UiSkin`；有外框時用按鈕外框＋陰影＋字陰影、選中項放大 1.06；`ContentHeight` |
| `Assets/Scripts/Sidescroller/Story/StoryUi.cs` | 建 `_skin`（OnDestroy 釋放）；暫停面板（高度隨選項數）、通關畫面改版、`TickPauseAndClear` |
| `Assets/Scripts/Sidescroller/Core/SsArt.cs` | 清單加 `SsUiSkin`（10）、`SsTouchUi`（11）；新類型 `Single`（單張圖、沒有 `_sprites.json`）：`SsMenuBg`、`SsMenuHero`、`SsMenuLogoBg` |
| `Assets/Art/Sidescroller/Sprites/SsUiSkin/**` | **舊的程式繪製 8 格版**（產生器已刪）。`UiSkin` 現在要 10 格 → 目前 `Loaded=false`，全部退回純色，等 ComfyUI 新版取代 |
| `Assets/Editor/UiShots/UiShotCapture.cs`（新） | 截圖工具（見上） |
| `Tools/ComfyUI/`（新） | `generate.py`（呼叫本機 ComfyUI API；FLUX／SDXL＋IP-Adapter；BiRefNet 遮罩）、`postprocess.py`（去背、裁切、對稱化、排 sheet、衍生變化、圖示描邊陰影、單張主視覺）、`assets.json`（全部提示詞與 seed）、`.gitignore`（`out/`） |

## ComfyUI 環境（本機，不在 repo）
- `~/ComfyUI`（git clone，commit b5a4321），venv `~/ComfyUI/.venv`（Python 3.12、PyTorch 2.15、MPS 可跑 FP8）。
- 自訂節點：`custom_nodes/ComfyUI_IPAdapter_plus`（GPL-3.0，只是工具）、`custom_nodes/ComfyUI_BiRefNet_ll`（MIT）。
- 模型與授權：
  | 檔案 | 來源 | 大小 | 授權 |
  |---|---|---|---|
  | `models/checkpoints/flux1-schnell-fp8.safetensors` | Comfy-Org/flux1-schnell | 17.2GB | Apache 2.0 |
  | `models/checkpoints/sd_xl_base_1.0.safetensors` | stabilityai/stable-diffusion-xl-base-1.0 | 6.94GB | CreativeML OpenRAIL++-M |
  | `models/ipadapter/ip-adapter_sdxl_vit-h.safetensors` | h94/IP-Adapter | 0.70GB | Apache 2.0 |
  | `models/clip_vision/CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors` | h94/IP-Adapter `models/image_encoder/model.safetensors` | 2.53GB | Apache 2.0 |
  | `models/birefnet/General.safetensors` | ZhengPeng7/BiRefNet `model.safetensors` | 0.44GB | MIT |
- 啟動：`cd ~/ComfyUI && .venv/bin/python main.py --listen 127.0.0.1 --port 8188`（目前已關閉）。
- 速度（M4、32GB）：FLUX 一步約 1.3～2 分，方圖約 8 分、2:1 橫幅約 4～5 分；SDXL 30 步約 5 分。

## 生圖進度
- **已生（原圖在 `Tools/ComfyUI/out/raw/`，未進 repo）**：`panel`、`button`、`ribbon`（各含 `_mask.png`）。panel 品質不錯（洋紅框＋金鉚釘＋紫漸層）。
- **還沒生（提示詞都在 `Tools/ComfyUI/assets.json`）**：
  - 外框組：`pill`、`sunburst`、`sparkle`、`dialogue`、`nameplate`（`SsUiSkin` 10 格要全到齊 postprocess 才會輸出）
  - 選單（menu 要的）：`menu_bg`（1600×720 → 2400×1080）、`menu_logo_bg`、`menu_hero`（SDXL＋IP-Adapter，參考圖放 `Tools/ComfyUI/out/ref/hero_concept.png`，來源 `/Users/iulee/UnityProject/blender-hero-meshy/Tools/Blender/Hero3D/meshy/concept_refined.png`）
  - 觸控（touch 要的，規格見 touch 的訊息／示意圖 `local_710b2a16…/touch_design_mockup.png`）：`touch_disc`、`stick_base`、`stick_knob`、`icon_jump`、`icon_melee`、`icon_shoot`、`icon_roll`、`icon_enhance`、`icon_switch`、`icon_pause`
- 試過 SDXL 生面板：風格詞裡有 sunset 時會畫成風景，已把風格前綴改成只描述配色（`assets.json` 的 `style`）。

## 明天怎麼接
1. 啟動 ComfyUI（上面指令），在 worktree 跑：
   ```
   ~/ComfyUI/.venv/bin/python Tools/ComfyUI/generate.py pill sunburst sparkle dialogue nameplate
   ~/ComfyUI/.venv/bin/python Tools/ComfyUI/postprocess.py
   ```
   看 `Tools/ComfyUI/out/preview/SsUiSkin.png`；不滿意的用 `--seed-offset N` 重抽，好的 seed 寫回 `assets.json`。
2. Unity 跑 `SpriteSheetImporter.ImportAndValidate`（batchmode 可），依實際圖量 `UiSkin.Borders`（九宮格邊界，目前是估值）。
3. 用截圖工具拍暫停＋通關，前後對照給使用者看 → 使用者確認風格。
4. 再生 menu 三張、touch 11 張，postprocess 後 commit 到本分支並通知 menu／touch（`git merge claude/upbeat-golick-2db7a2`）。
5. 對話框（`DialogueView.cs`）、直向提示（`RotateHintView.cs`）套用外框。
6. 交 review 前：自審 diff＋subagent review＋反向驗證、Editor／Android／WebGL 編譯 0 錯誤、Entry 實測、前後對照截圖。

預估剩餘：生外框 5 張＋後製＋看圖約 50 分；暫停＋通關重拍與調整約 40 分；menu／touch 素材約 2 小時（生圖背景跑）；對話框與直向提示約 1 小時；review＋實測約 1.5 小時。

## 已知問題
- repo 裡的 `SsUiSkin_Idle.png` 是舊 8 格版，`UiSkin` 要 10 格 → 目前暫停／通關（與用 UiSkin 的 hud／menu／touch）顯示純色退回版，直到新素材進來。
- `UiSkin.Borders` 是估值，要依 ComfyUI 後製出的圖重量。
- `postprocess.py` 的對稱化（quad／half 鏡射）、未選中按鈕的壓暗轉紫、按下底盤的配色都還沒用真圖驗證過。
- `Docs/sidescroller/interfaces.md` 的素材清單還沒加 `SsUiSkin`、`SsTouchUi`、`SsMenu*`（規範文件，要先問使用者）。
- 選單的 IngameDebugConsole 錯誤計數浮窗會疊在畫面上（Entry 場景既有，是否進正式版待確認）。
- hud 要求在 `StoryUi.Init` 把 `_skin` 傳給 HudView／LootHudView／BannerView／LootScreens：這 4 行只在 hud 的 worktree（本分支的建構子還是舊的），合併時以 hud 版本為準。

## 未完成的 review／驗證
- 本分支所有程式改動都還沒做 subagent review 與反向驗證，也還沒跑 Android／WebGL 腳本編譯（只有 Editor 編譯＋截圖通過）。
- ComfyUI 版暫停／通關還沒做出來、使用者還沒確認風格。
