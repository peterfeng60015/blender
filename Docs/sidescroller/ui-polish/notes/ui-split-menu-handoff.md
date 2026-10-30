# UI 優化拆分：menu（遊戲選單主視覺改版）交接

- 日期：2026-10-30（使用者下班，暫停）
- 角色：menu。協調 session 是 local_710b2a16-2546-412f-a392-4b90470056e1，分工約定見 `side-session-notes/local_553e400a-…/ui-split-contract.md`
- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/ui-menu-eaad50`，分支 `claude/ui-menu-eaad50`
- 基底：ui 分支 `claude/upbeat-golick-2db7a2` 的 `81fab87`。用 `--ff-only` 快轉過去，沒有產生 merge commit

## 完成到哪（約 92%）
- 授權小字：照搬整合分支 `feat/hero-meshy` 的寫法，包括 `_safe` 安全區、右下角 `StoryTexts.MenuModelCredit`、每幀 `SsUiKit.FitSafeArea(_safe)`，以及 `StoryTexts` 裡的常數和 `All`。改版後小字仍在右下安全區、地面帶上，而且建在主角圖之後，不會被蓋住。
- 海報式版面（`GameSelectMenuView.cs`）：
  - 背景：夕陽城市視差慢速捲動（SsBgFar 不透明一層、SsBgMid 壓暗一層），加上左側漸層壓暗與底部地面帶（霓虹線）。
  - 左欄：Logo「熱浪急件」用 `UiSkin.Title` 做外框、陰影與微斜，後面是霓虹底線和慢轉光芒。Logo 下方是選項（`MenuList`＋`UiSkin`）。
  - 左下：操作提示膠囊。
  - 右邊：主角主視覺的位置。
- 進場動畫（unscaled time）：
  - 背景 0.5 秒淡入。
  - Logo 彈入（0.25 秒起）。
  - 主角從右滑入（0.45 秒起，有素材才會出現）。
  - 選項彈入（0.75 秒起，0.35 秒）。
  - 提示淡入後呼吸閃爍（1.0 秒起）。
  - 選項彈出完成（1.1 秒）才開放輸入。
- 讀取流程：
  - 選單建立時在背景跑 `SsArt.LoadAllAsync`，由 `Update` 判斷讀完、或逾時 4 秒（用真實時間計），才建畫面。
  - 建畫面丟例外時，拆掉重建不含素材的簡易版面，不會停在 Entry 已隱藏的黑畫面。
  - 遊戲開機的 `ResetStatics` 會清快取重讀；AssetPackage 的素材常駐，第二次很快。
- 選單專用素材已接好，交了就自動換上，不用改程式：
  - `SsMenuBg`：取代城市視差，等比蓋滿。
  - `SsMenuHero`：放右邊，腳底貼地面帶。
  - `SsMenuLogoBg`：取代光芒。
  - 素材是佔位圖或還沒交時，自動退回現有城市圖與光芒。ui 已在 `81fab87` 把三者加進 `SsArt` 清單。
- 文字：
  - 只有一款遊戲時，選項寫「開始遊戲」（`MenuStart`）。
  - 觸控提示改成「點一下「開始遊戲」」。
  - 拿掉已不用的 `MenuTitle`。

## 使用者的決定（原話選項）
- 版面：「A 海報式（建議）」，左 Logo＋按鈕、右主角主視覺、夕陽城市背景；另一案是 B 置中式。
- 按鈕字：「改「開始遊戲」（建議）」，不跟 Logo 重複。
- 下班指示（經協調轉達）：「我準備要下班了／通知所有session／暫停目前工作／並寫下交接文件／commit + push上repo」。

## 改了哪些檔
- `Assets/Scripts/Sidescroller/Menu/GameSelectMenuView.cs`：重寫版面、讀取、動畫，含 `ParallaxLayer` 內部類別。
- `Assets/Scripts/Sidescroller/Story/StoryTexts.cs`：新增 `MenuStart`、`MenuModelCredit`，改 `MenuHintTouch`，拿掉 `MenuTitle`，同步 `All`。
- `Docs/sidescroller/ui-polish/menu-handoff.md`：本檔。
- 沒動任何別人的檔。測試期間暫時改過 `UiShotCapture.cs` 的截圖延遲，已還原；暫時的測試腳本 `Assets/Editor/_MenuTmp/` 已刪除。

## 已驗證
| 項目 | 結果 |
|---|---|
| Editor／Android／WebGL 腳本編譯 | 0 錯誤（merge `81fab87` 後重跑過；暫時腳本呼叫 `PlayerBuildInterface.CompilePlayerScripts`） |
| 缺字 | 新字串都在思源黑體字集內（開始遊戲、「」、授權小字等） |
| 自審 diff | 修了兩個問題：`UniTask.WhenAny` 會產生值型別泛型 `UniTask<int>`（HybridCLR 下要避開）→ 改成旗標＋`Update`；建畫面例外會卡黑畫面 → 退回簡易版面 |
| subagent review | 確定問題 2 個都已修：建畫面失敗卡黑畫面、選項淡入一開始就收輸入。另外修了：逾時改用真實時間、錯誤訊息自動縮字、半透明中景不重疊（避免接縫細線）、畫面建好時記 log |
| 反向驗證：建畫面失敗 | 讓第一次 `Build` 丟例外 → 退回純色簡易版面，Logo、按鈕、提示、授權小字都在（截圖 `fallback/`） |
| 從 Entry 實測（暫時的 Play 測試，用 Input System 測試裝置送輸入） | 鍵盤 Enter：PASS；觸控點擊：PASS。兩者都是「彈出前 0.3 秒送的輸入不被接受、彈出後能選到並開始遊戲」，授權小字在 |
| 反向驗證：輸入保護 | 拿掉「彈出前不收輸入」的判斷 → 鍵盤與觸控在 0.3 秒就選定開始（log 有「選擇 熱浪急件」），測試失敗 → 判斷有效 |
| 直向提示 | 直向時蓋住畫面、不收輸入（截圖 `menu_rotate`） |

前後對照截圖在 `side-session-notes/local_553e400a-…/ui-split-menu-shots/{before,after,fallback}/`。**注意**：這些是 batchmode 拍的；ui 說 batchmode 下 `Screen` 固定 640×480，安全區與觸控排版可能和實機不同，需要用新工具重拍（見下）。

## 已知問題與判斷
- **鍵盤測試在 batchmode 一度失敗**：前兩輪鍵盤 Enter 收不到。判斷是 batchmode 下原生鍵盤在沒有焦點時被 Input System 停用，Input System 預設也不把輸入送進沒有焦點的 Game 視窗。改用不存檔的 `InputSettings`（`IgnoreFocus`、`AllDeviceInputAlwaysGoesToGameView`）加上自己新增的測試鍵盤後就 PASS。是測試環境問題，不是選單程式的問題。
- 舊版截圖工具固定在第 4 秒拍選單。素材讀取慢時（有一次讀了 2.3 秒）會拍到按鈕還沒彈出；新版工具（`81fab87`）改了做法，要重拍確認。建議 ui 之後改成「等選單畫面建好再拍」。
- 選單出現前有一段黑畫面，時間等於素材讀取時間（Editor 實測 0.4～2.3 秒，實機未量）。太長的話，可以考慮先建不靠素材的 Logo 與純色背景，素材到了再淡入。
- 按鈕目前是純色：ui 的 `SsUiSkin_Idle.png` 還是舊 8 格，`UiSkin.Loaded=false`。ui 交新素材後會自動換成外框。
- 只有一款遊戲時，鍵盤提示仍寫「↑↓：選擇」（沿用原字串）。之後加第二款遊戲時，`MenuHintTouch` 要一起改（`StoryTexts` 有註解）。
- 選單會先跑一次完整的 `SsArt.LoadAllAsync`（含 3D 主角），進遊戲時 `ResetStatics` 會再讀一輪。素材常駐，不會洩漏，只多一輪讀取。

## 還沒做的步驟與預估時間
1. 跟上 ui 分支（`81fab87` 之後的版本，含 `SsUiSkin` 新 10 格與選單三張素材）：`git -C <worktree> merge claude/upbeat-golick-2db7a2`。約 5 分。
2. 素材到了之後調整版面：看 `SsMenuBg` 的構圖，調左側壓暗的強度；確認 `SsMenuHero` 的大小與位置（目前框是 700×820、錨點 (0.75, 0.115)），以及 `SsMenuLogoBg`（1100×400）和字的對位。約 20～40 分。
3. 用新截圖工具重拍：不要加 `-batchmode`，執行 `Unity -projectPath <worktree> -executeMethod UiShotCapture.RunBatch -uiShotMode menu -captureDir <資料夾> -logFile <log>`。S22 Ultra（2316×1080）與 16:9 都要拍，確認安全區、授權小字、提示膠囊沒有重疊。約 15 分。
4. 素材換上後再做一輪驗證：自審 diff、Editor／Android／WebGL 編譯、從 Entry 實測鍵盤與觸控。實測的暫時腳本已刪，要重寫的話，參考上面「已知問題」的焦點設定與測試鍵盤做法。約 30～45 分。
5. 交使用者 review（前後對照截圖），再交差給協調。約 10 分。

合計約 1.5～2 小時（不含等 ui 生圖的時間；ui 估計在暫停／通關之後約 1～2.5 小時內交）。

## 未完成的 review／驗證項目
- 素材換上後的版面與截圖（上面第 2～3 步）。
- 新截圖工具下的安全區排版確認。前面的截圖都是 batchmode 的 640×480 排版縮放出來的。
- 實機（Android／WebGL）的選單讀取時間與黑畫面長短。
- 最終一輪自審＋反向驗證（素材換上後）。
