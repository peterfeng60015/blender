# ui-split：hud 交接（HUD、背包、強化、字卡改版）

> 狀態（2026-10-30 下班暫停）：程式改版、subagent 審查與修正、三平台編譯、從 Entry 實跑截圖都已完成；**還沒交使用者做最後 review**，也還沒換上 ui 的 ComfyUI 新外框素材。使用者指示（協調轉達原話）：「我準備要下班了／通知所有session／暫停目前工作／並寫下交接文件／commit + push上repo」。

- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/ui-hud-352f1a`，分支 `claude/ui-hud-352f1a`
- 基底：已 fast-forward 到 ui 分支 `claude/upbeat-golick-2db7a2` 的 `81fab87`（沒有自己的 commit，改動全在工作區，未 commit、未 push）
- 使用者決定（原話）：看完背包／強化／HUD 樣張後選「照方向全部套用」。HUD 圖示不重繪（沿用像素圖示）。

## 改了哪些檔
| 檔案 | 內容 |
|---|---|
| `HudView.cs` | 裝備底板、「換制服」提示（字加陰影）、上方提示條改 `UiSkin.Pill` 金邊圓角底框，寬度跟字長（360～1100，最寬還放不下時縮字 34→24）；Boss 血條改 Pill 外框＋深色軌道＋上半截高光＋白色扣血殘影（停 0.35 秒再縮）、名稱描邊；`SetVisible(false)` 改成 0.3 秒往上滑出淡出（通關時） |
| `LootHudView.cs` | 拾取標籤改 Pill 底＋字陰影、淡出改用 CanvasGroup；訂閱 `GameSession.StageCleared`：通關時飛行中的道具立刻放進背包、背包圖示 0.3 秒淡出後關掉，之後撿到的直接進背包不顯示回饋 |
| `LootUiParts.cs` | `LootButton` 改用 `UiSkin` ButtonOn／ButtonOff（選中／未選／停用）＋字陰影，新增 `SetText/SetFont/SetPadding/SetEllipsis/SetState(selected, enabled, color)`，外部不再直接改 `Bg`／`Label`；新增 `LootFrame`（暗底＋陰影＋Panel 外框＋`UiPop` 進場、面板上緣緞帶標題）；說明框改 Panel 外框 |
| `InventoryView.cs` | 面板改 `LootFrame`（不透明）、緞帶標題「背包」、格子不透明深底＋淡紫細框、裝備欄 ButtonOff 外框、「關閉」寬 200→168（高度仍 ≥ 9mm）、零件數金色、說明文字對比提高、丟掉確認框也套外框與進場動畫；版面扣掉緞帶露出的高度 |
| `EnhanceView.cs` | 同上套 `LootFrame`＋緞帶標題「補給站：強化」、詳細區改 Pill 卡片、成功率大字上限 132→96 並加陰影；**紅色殘影**修正：成功率 100% 時 `Enhance.FailureText` 回「—」被紅字畫出來，改成必定成功時不顯示（只動顯示，數值邏輯未動） |
| `LootScreens.cs` | 建構子多收 `UiSkin`；補給站「E：強化」提示改 Pill＋字陰影 |
| `StoryOverlays.cs` | `BannerView`（字卡）：大字改描邊＋陰影（`UiSkin.Title`），進場橫條由中間撐開、字從右滑入，退場字往左滑出並淡出；上線金、下線洋紅 |
| `StoryUi.cs`（ui 的檔） | **只改 4 行**：`HudView`／`LootHudView`／`BannerView`／`LootScreens` 建構子多傳 `_skin`。ui 已同意，合併時以 hud 這 4 行為準（ui 不會動 Init 這幾行） |

## 驗證
- 自審 diff＋subagent review 1 輪（1 個必修：只有一套服裝時「換制服」陰影關不掉；2 個低風險：背包緞帶在 21:9 頂出安全區、Boss 非滿血出場多播一次殘影）→ 都已修。反向驗證：通關收起、按鈕外觀快取、換制服提示三項逐一以程式路徑確認。
- 腳本編譯：Editor／Android（49 組件）／WebGL（48 組件）0 錯誤（`PlayerBuildInterface.CompilePlayerScripts`）。
- 從 Entry 實跑（`UiShotCapture` game 模式）：HUD、HUD＋提示＋Boss 血條、暫停、背包、強化、字卡、通關、直向提示，S22 Ultra 2316×1080 與 1920×1080。
- 前後對照圖：見下方「截圖」。
- `StoryUi.cs` 的 4 行是**保留的正式改動**（不是暫時的）。

## 截圖
- 前後對照（上＝改前、下＝改後，新版截圖工具 81fab87，從 Entry 實跑）：`/Users/iulee/Library/Application Support/Claude/side-session-notes/local_553e400a-bc1b-493f-a5df-47c8fabd86ec/ui-split-hud-shots/對照_*.png`（HUD、鍵盤 HUD、提示＋Boss、背包、強化、字卡、通關；2316×1080 與 1920×1080）。
- 外框是暫時素材（見已知 2），只能看版面與層級；真正質感要等 ui 新素材。截圖不進 repo。

## 已知、未處理（不在 hud 檔案範圍或刻意保留）
1. 畫面右側 IngameDebugConsole 的錯誤計數浮窗（finding 7）不是 hud 的檔，正式版要不要關需要決定。
2. 外框素材：repo 的 `SsUiSkin_Idle.png` 仍是舊 8 格，`UiSkin.Loaded=false` 時全部退回純色（版面不變）。截圖時 hud 在本機暫時把舊圖補成 10 格（橫向外框壓成橫條）才看得到效果，**截完已還原，不在交付內容**。ui 新素材到了直接替換即可。
3. `UiSkin.Set` 退回純色時會忽略 tint：沒素材時，停用按鈕外框與一般相同（只有字變淡）、HUD 裝備底板比舊版暗。屬 `UiSkin`（ui）行為，素材到位後不影響。
4. 面板進場動畫 0.2 秒內 Window 從 0.9 倍放大到 1，按鈕熱區跟畫面一起縮（判定與畫面一致），動畫結束即恢復 ≥ 9mm。
5. 右上暫停鈕：touch 確認大小與位置不變，背包圖示維持在暫停鈕正下方、大小規則不變（touch 的 `TouchControlsView` 依同一公式避開背包圖示；兩邊改規則要互相通知）。

6. 改後截圖的 IngameDebugConsole 警告計數比改前多 1（錯誤數相同）。該次 log 被後一輪覆寫，沒查到來源，待確認（可能是 Editor 端的警告，也可能是新程式）。
7. `UiShotCapture` 偶爾「等不到截圖（Game 視窗沒有重畫？）」中途失敗，重跑即可（ui 的工具）。

## 還沒做的步驟與預估時間
1. merge ui 分支新的 commit（ui 換上 10 格 `SsUiSkin` 新素材之後）：`git merge claude/upbeat-golick-2db7a2`，再用新截圖工具重拍，檢查九宮格邊界、字與外框對比，必要時微調 scale／內距（約 30～45 分）。
2. 查第 6 項的警告來源（約 10 分）。
3. 前後對照圖給使用者做最後 review，依意見修改（每輪約 15～30 分）。
4. 交差：腳本編譯（Editor／Android／WebGL）、從 Entry 實測、更新本檔、通知協調（約 20 分）。

## 明天怎麼接
1. `cd /Users/iulee/UnityProject/blender/.claude/worktrees/ui-hud-352f1a`（或任一 clone `git checkout claude/ui-hud-352f1a`）。
2. `git merge claude/upbeat-golick-2db7a2`（拿 ui 81fab87 之後的版本：新截圖工具、新素材）。
3. 截圖：`Unity -projectPath <worktree> -executeMethod UiShotCapture.RunBatch -uiShotMode game -captureDir <資料夾> -logFile <log>`（不加 -batchmode、不加 -quit，會開 Editor 視窗跑完自己關）。素材若仍是 8 格，`UiSkin` 會退回純色。
4. 平台編譯檢查：今天用的暫時腳本已刪，需要時在 `Assets/Editor` 暫放一個呼叫 `PlayerBuildInterface.CompilePlayerScripts`（Android、WebGL）的 batchmode 方法，跑完刪掉。

## 未完成的 review／驗證
- 使用者最後 review（還沒給看完整改後版）。
- 換上新素材後的視覺驗證與重拍。
- 第 6 項警告來源。
- 實機（S22 Ultra）觸控手感沒測，只做了 Editor 觸控模式截圖。

## 交付前清理（已做）
- 刪除暫時的 `Assets/Editor/_HudTmp/`（編譯檢查腳本）、還原暫時改的 `UiShotCapture.cs`（ui 已在 81fab87 用同樣改法）。
- 還原 `SsUiSkin_Idle(.png/_n.png/.meta)`、`SsUiSkin_sprites.json`、`.vscode/settings.json`。
