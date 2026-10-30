# UI 優化拆分（ui-split）：分工與約定

協調 session：local_710b2a16-2546-412f-a392-4b90470056e1。使用者 2026-10-30 原話：「把#6 優化項目分給多個session同步進行」，並選「拆成 4 個（建議）」與「ui 先 commit 到自己的分支（建議）」。
看板：`http://127.0.0.1:8897/`（板名 hero-meshy），角色 `ui`、`touch`、`menu`、`hud`。

## 已定的方向（使用者先前在 ui session 的決定）
- 方向「B：A＋新增 UI 美術素材」，美術素材用 **ComfyUI** 產生；模型 FLUX.1 schnell（Apache 2.0）＋SDXL base 1.0（OpenRAIL++-M）＋IP-Adapter SDXL vit-h（Apache 2.0）＋BiRefNet（MIT）；FLUX.1 dev 不能用（不能商用）。
- 素材範圍「外框組、觸控按鈕組、選單主視覺」，HUD 圖示不重繪；`SsUiSkin` 全部改 ComfyUI；生圖腳本放 repo `Tools/ComfyUI`。
- 使用者要求「暫停和通關畫面重做一個版本給我看」：ui 先做這兩個畫面給使用者看風格，**風格確認前，其他 session 先做版面、互動、程式結構，視覺沿用 `UiSkin` 現有素材，素材換新只是替換貼圖**。
- 品質問題清單：`/Users/iulee/Library/Application Support/Claude/side-session-notes/local_710b2a16-2546-412f-a392-4b90470056e1/ui-polish-findings.md`。

## 共用底稿
- ui 的分支 `claude/upbeat-golick-2db7a2` commit `719cf90`（本機，不 push）：`UiSkin.cs`、`SsUiSkin` 外框素材與產生器、`MenuList` 可選外框、`StoryUi` 暫停／通關試做、`SsArt` 清單、`Assets/Editor/UiShots/UiShotCapture.cs`（截圖工具）、`Tools/ComfyUI/{generate,postprocess}.py`、`assets.json`。
- touch／menu／hud 的 worktree 都從這個分支開出。之後 ui 改了 `UiSkin`／`SsUiKit` 的 API，會 commit 到它的分支並傳訊息給大家；需要時用 `git -C <自己 worktree> merge claude/upbeat-golick-2db7a2` 跟上（只 merge ui 的分支，不要 merge 別人的）。

## 檔案歸屬（只改自己的檔；要動別人的先 SendMessage 給擁有者，副本給協調）
| 角色 | 檔案 |
|---|---|
| ui（原 session） | `UiSkin.cs`、`SsUiKit.cs`、`StoryUi.cs`（暫停、通關、Canvas 組裝）、`MenuList.cs`、`DialogueView.cs`、`RotateHintView.cs`、`SsArt.cs`、`Assets/Art/Sidescroller/Sprites/SsUiSkin/**`、`Tools/ComfyUI/**`、`Assets/Editor/UiShots/**` |
| touch | `TouchControlsView.cs`（左側搖桿、右側跳／甩包／彈弓／滾殼、右上暫停／換裝）；`SsTouch.cs` 是輸入邏輯，要改先問協調 |
| menu | `GameSelectMenu.cs`、`GameSelectMenuView.cs`（選單主視覺、遊戲 Logo、版面）。**必須保留主角線加的授權小字**：整合分支 `feat/hero-meshy`（`/Users/iulee/UnityProject/blender-hero-meshy`）的 `GameSelectMenuView.cs` 有 `_safe` 安全區容器＋右下角 `StoryTexts.MenuModelCredit`（「主角模型：Meshy（CC BY 4.0）」）＋`Update` 裡 `SsUiKit.FitSafeArea(_safe)`；你的分支沒有，請照那份寫法一起放進你的版本（`StoryTexts.cs` 那兩行也一起加），協調合併時以你的版本為準 |
| hud | `HudView.cs`（含 Boss 血條）、`LootHudView.cs`、`InventoryView.cs`（背包）、`EnhanceView.cs`（強化）、`LootScreens.cs`、`LootUiParts.cs`、`StoryOverlays.cs`（淡入淡出、字卡） |
- `StoryTexts.cs`：新增文字各自加在自己的區塊，並加進 `StoryTexts.All`（缺字檢查）；合併時協調處理。
- 共用元件需求（新的按鈕樣式、面板、動畫工具）：向 ui 提，由 ui 加進 `UiSkin`／`SsUiKit`，不要各自複製一份。

## ComfyUI（只有一張 GPU）
- ComfyUI 伺服器由 ui 開著：`http://127.0.0.1:8188`，FLUX 一張方圖約 8 分鐘。**不要自己再開一個 ComfyUI、不要下載任何模型**。
- 需要素材：把需求（用途、尺寸、9-slice 邊界、透明背景、數量、風格參考）傳給 ui，由 ui 排進佇列、後製後放進 `SsUiSkin` 並通知你。不要直接呼叫 8188（避免和 ui 的佇列與後製流程衝突）。

## 共通規則
- 不 commit、不 push（ui 的底稿 commit 是使用者同意的特例）；交差時回報 worktree 路徑與交接檔 `ui-split-<角色>-handoff.md`（本資料夾）。
- Unity：同一專案路徑一次只開一個 Unity；batchmode 後被改寫的 `Assets/Settings/*.asset`、`.vscode/settings.json` 要還原。這台 Mac 同時跑 FLUX（約 17GB）和多個 Unity，記憶體吃緊：batchmode 跑完就結束，不要長時間掛著 Editor。
- 新字一定在思源黑體 TMP 字集內（`StoryTexts.All`＋`CutsceneChecker` 檢查）；不重建字集。
- HybridCLR 限制：資源不序列化引用遊戲模組型別；程式與註解不得出現 舊的模組命名字樣（改用 AppBootstrap／模組）；C# 分區塊用 `#region`；註解密度與既有程式一致。
- 手機：安全區、觸控熱區 ≥ 9mm、S22 Ultra（2316×1080）與 16:9 都要截圖；截圖工具用 `Assets/Editor/UiShots/UiShotCapture.cs`。
- 交使用者 review 前：自審 diff＋subagent review＋反向驗證、Editor／Android／WebGL 腳本編譯 0 錯誤、從 Entry 實測、前後對照截圖。
- 使用者在你的 session 下的決定（同意、否決、選方案）立即 SendMessage 回報協調，附原話。

## ui 補充（2026-10-30 16:30）
1. 程式產生器 `UiSkinGenerator` 已刪，`SsUiSkin` 之後全由 `Tools/ComfyUI/postprocess.py` 輸出。
2. `UiSkin` 已改成 10 格排版：0 面板、1 按鈕選中、2 按鈕未選中、3 陰影、4 光芒、5 數值標籤、6 緞帶、7 星光、8 對話框、9 名牌（橫向外框取格子中間 256×128）。repo 的 `SsUiSkin_Idle.png` 還是舊 8 格 → 目前 `UiSkin.Loaded=false`、全部退回純色，**這是正常的**；ui 後製好新素材會 commit 到它的分支並通知，之後各自 `git merge claude/upbeat-golick-2db7a2`。
3. 觸控素材是另一張 sheet `SsTouchUi`（ui 擁有，`Sprites/SsTouchUi`）：0 按鈕底盤、1 按下底盤、2 搖桿底座、3 搖桿頭、4 跳、5 甩包、6 彈弓、7 滾殼、8 強化、9 換裝、10 暫停，每格 256，`SsArt` 清單已加；用 `SsArt.Get("SsTouchUi_Idle").Variant(i)`，素材到之前是佔位圖。
4. 選單主視覺 ui 會輸出 `SsMenuBg`；選單階段 `SsArt` 尚未載入時的讀取方式由 menu 處理，需要 ui 配合再找 ui。
5. 生圖排程：先跑暫停／通關的 6 張，touch 與選單主視覺排後面，約 2～2.5 小時內輪完。
- 2026-10-30 16:40：menu 使用者決定（原話選項）：版面「A 海報式（建議）」（左 Logo＋按鈕、右主角主視覺、夕陽城市背景）；按鈕字「改「開始遊戲」（建議）」。素材需求已給 ui：SsMenuBg 2400×1080、SsMenuHero 約 1000×1200（IP-Adapter 參考 concept_refined.png）、可選 SsMenuLogoBg；選單用 GameAssets.LoadSpritesAsync 讀（整合時要確認 AssetPackage 收集到新素材）。
- 2026-10-30 16:50：touch 使用者決定（原話選項）：造型「圓形金框（建議）」、文字「保留小字（建議）」、重疊「一起修（建議）」（滾殼壓到右上鈕的排法在 TouchControlsView 內修，暫停鈕位置大小不變）。SsTouchUi 規格已給 ui。
- 2026-10-30 16:45：hud 使用者決定（原話）：「照方向全部套用」（背包／強化 UiSkin 外框、緞帶標題、100% 字級 132→96 並修紅色殘影、HUD 提示條金邊、Boss 血條外框；接著套字卡動畫、確認框、拾取標籤、補給站提示、通關收 HUD）。
