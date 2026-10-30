# ui-split：touch 交接（觸控搖桿與功能按鈕改版）

2026-10-30 下班暫停時的狀態。使用者原話（經協調轉達）：「我準備要下班了／通知所有session／暫停目前工作／並寫下交接文件／commit + push上repo」。

- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/ui-touch-82e39b`，分支 `claude/ui-touch-82e39b`（已 `--ff-only` 跟上 ui 分支 `81fab87`）。
- 本次 commit：只含 `TouchControlsView.cs` 與這份交接檔（commit 雜湊見協調的紀錄／`git log`）。
- 協調 session：local_710b2a16-2546-412f-a392-4b90470056e1；分工約定：notes `local_553e400a-…/ui-split-contract.md`。

## 完成到哪
| 步驟 | 狀態 |
|---|---|
| 盤點、改版前截圖 | 完成 |
| 設計方案給使用者確認 | 完成（示意圖 notes `local_710b2a16-…/touch_design_mockup.png`） |
| SsTouchUi 素材規格傳給 ui | 完成（ui 已排 ComfyUI，素材**還沒到**） |
| 實作 | 完成（程式畫的替代素材＋HUD 圖示） |
| 自審、subagent review、反向驗證 | 完成 |
| Editor／Android／WebGL 腳本編譯 | 完成，0 錯誤（Android 49、WebGL 48 個組件） |
| 從 Entry 實測（模擬觸控） | 完成（batchmode，547 項 0 失敗） |
| 用 ui 新版截圖工具（81fab87）拍正式前後對照 | 改版前只拍到 HUD；改版後那輪工具在開場就逾時失敗（「等不到截圖」，ui 工具的問題）。**明天重拍** |
| 交使用者 review | **還沒做** |
| SsTouchUi 素材到了之後換圖、重新驗證 | **還沒做** |

## 使用者做過的決定（2026-10-30，在 touch session，選項原文）
- 造型：「圓形金框（建議）」
- 圖示下方文字：「保留小字（建議）」
- 三種攻擊都拿到時滾殼壓到右上暫停／換裝／背包：「一起修（建議）」

## 改了哪些檔
- `Assets/Scripts/Sidescroller/Story/TouchControlsView.cs`（唯一的程式改動）。
- 沒動：`SsTouch.cs`（輸入邏輯）、`StoryTexts.cs`、`LootHudView.cs`、ui 擁有的檔案。`StoryTexts.TouchPause`（「Ⅱ」）現在沒用到（暫停改成圖示），留著沒刪。

### 內容
1. **外觀**：按鈕＝圓形底盤（深紫半透明底＋洋紅內圈＋金外圈＋深紫描邊）＋圖示＋小字（`UiSkin.Label`）；暫停只有兩條直槓圖示。
   搖桿底座＝淡深紫底＋細圈＋四個方向小三角；把手＝奶油→洋紅漸層。待機時搖桿不透明度 0.3（不擋開局主角），按下變 1。
2. **按下回饋**：按住時外觀子物件 `Visual` 縮到 0.9（約 33ms）並換成橘→粉亮底；判定用的按鈕 rt 不縮，判定範圍不變。
3. **素材**：`SsTouchUi_Idle`（11 格：0 底盤、1 按下底盤、2 搖桿底座、3 搖桿頭、4～10 跳／甩包／彈弓／滾殼／強化／換裝／暫停）不是佔位圖就直接用，**素材到了不用改程式**；
   佔位圖時：底盤、搖桿、跳／暫停／強化圖示程式畫（左右對稱只算一半），甩包／彈弓／滾殼／換裝借 HUD 的 `SsUi_Idle`。換裝鈕（佔位圖模式）顯示「換過去那套」的圖示（HUD 顯示目前那套，刻意相反）。
4. **版面**：大小、邊距、暫停／換裝位置完全不變（hud 的背包圖示照舊在暫停鈕正下方，已告知 hud）。排版抽成不讀 `Screen` 的 `internal static Compute(width, height, safe, dpi)`。
   滾殼預設仍在跳躍正上方；會壓到右上那群（含背包圖示）時改到外圈（甩包左邊，不行再彈弓左上），強化取外圈剩下的空位；都放不下記警告。
   實測：S22 Ultra、16:9 手機、平板、iPad、桌機 WebGL、dpi 不明 → 滾殼留原位；Pixel 7、iPhone 14、720p 小手機 → 舊版確實壓到背包圖示，新版移到甩包左邊。
5. **輸入手感沒變**：`Classify`、HitScale 1.15、搖桿區、死區、`StickRangePx`、多指處理都沒改。

## 已做的驗證
- 自審 diff；subagent review（sonnet）：沒有高嚴重度，採納 5 項（程式畫底盤層層疊實心圓、中心幾乎不透明 → 改由內往外換色；鏡射省一半計算；safeArea 無效退回全螢幕；放不下記警告；sprite DontSave），2 項判定為刻意（強化在小螢幕優先放外圈 135°、換裝圖示語意）。
- 實測工具 `TouchControlsCheck`（暫時工具，**沒進 repo**，存在 notes `local_553e400a-…/touch-tools/TouchControlsCheck.cs`；要用時放回 `Assets/Editor/TouchShots/`）：
  - `RunBatch`（batchmode）：12 組裝置版面（不重疊、不壓背包、在安全區內、≥9mm、滾殼位置）＋從 Entry 進遊戲用模擬 Touchscreen 多指觸控（搖桿推 80% 移動、死區 20% 不動、搖桿＋跳＋彈弓三指同按、其他鈕沒誤觸、放開無殘留、按下外觀 0.9／rt 1.0、1.1 倍半徑仍判定、1.3 倍不判定）。最後一次 547 項 0 失敗。
  - `CompileBatch`：Android、WebGL player 腳本編譯。
- 反向驗證（三個突變一起跑，全部被抓到）：HitScale 1.15→1.0、按下時改縮判定 rt、不避背包圖示。

## 明天怎麼接（預估）
1. 有新的 ui commit 就 `git merge --ff-only claude/upbeat-golick-2db7a2`（或一般 merge，只 merge ui 的分支）。（5 分）
2. 用 ui 新版截圖工具重拍前後對照（**不要加 -batchmode**）：`Unity -projectPath <worktree> -executeMethod UiShotCapture.RunBatch -uiShotMode game -captureDir <資料夾> -logFile <log>`；改版前用 `git show 719cf90:Assets/Scripts/Sidescroller/Story/TouchControlsView.cs` 暫時換回舊版拍，拍完換回。另外用 `TouchControlsCheck` 拍全部按鈕與按下中的畫面（S22、16:9、Pixel 7）。（20～30 分）
3. 交使用者 review（截圖＋改動說明）。（等使用者）
4. SsTouchUi 素材到了（ui commit 後通知）：merge、重跑 `TouchControlsCheck.RunBatch`、截圖確認素材的圖示大小、透明度、亮暗背景可讀性，必要時微調 `IdleAlpha`／圖示錨點；再交使用者看。（30～45 分，不含等素材）
5. 收尾：決定 `TouchControlsCheck` 要不要正式進 repo（問使用者）。

## 截圖（notes 資料夾，不進 repo）
- `touch-shots/before/`：`old-tool_*` 舊截圖工具（觸控層照 640×480 排，位置不準）、`new-tool_*` ui 新版工具（Game 視窗真解析度）拍的改版前 HUD。
- `touch-shots/after/`：`TouchControlsCheck` 拍的改版後（給全部攻擊與第二套服裝、套 S22 FHD+／16:9 400dpi／Pixel 7 的版面）`all_*` 全部按鈕、`pressed_2316x1080` 搖桿＋跳＋彈弓按住中。
- 實測工具：`touch-tools/TouchControlsCheck.cs`。

## 已知問題／未處理
- 640×480 這種實體很小的螢幕，小鈕受既有畫布上限 190 限制只有 8.5mm（舊版就這樣）。
- 佔位圖模式在 HybridCLR 解譯下，開場程式畫圖的耗時沒在實機量（素材到了就不走這段）。
- 1920×1080／400dpi 的截圖裡，滾殼被 IngameDebugConsole 的錯誤計數浮窗蓋住（浮窗是否出現在正式版是 ui 問題清單第 7 項）。
- batchmode 的 Screen 固定 640×480：舊版截圖工具拍的觸控層與背包圖示是照 640×480 排的（ui 已在 81fab87 修好工具）。
