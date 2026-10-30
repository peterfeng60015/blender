# 協調交接（2026-10-30 下班暫停）

協調 session：local_710b2a16-2546-412f-a392-4b90470056e1。使用者原話：「我準備要下班了／通知所有session／暫停目前工作／並寫下交接文件／commit + push上repo」。

## 已完成：主角改用 Meshy 模型（已合進 main）

- commit `58aefda`，已 push 到 `origin/main`（同時保留 `origin/feat/hero-meshy`）。
- 內容：`hero3d.py --source meshy`（匯入 Meshy glb、正規化、減面約 2 萬三角形、28 根骨頭、權重、郵差包拆分與非流形歸零檢查、Meshy 專用動作修正）、`meshy_texture.py`（1024 BaseMap＋MaskMap）、`AhJiaToon._RampStrength`（材質 0.6）、`Hero3DPreviewSetup` 容錯、`HeroVisual.ModelScale` 1.3→1.5、選單右下「主角模型：Meshy（CC BY 4.0）」、Meshy 原始 glb 與設計圖放 `Tools/Blender/Hero3D/meshy/`（Git LFS）。
- 使用者決定：表情第一版不做、手先不修、CC BY 標註放選單、素材「放進 repo 用 LFS」、ModelScale「用 1.5」、「合進 main」「push main」。
- 已知小問題（不擋）：Setup 重建材質時不會把 `_RampStrength` 設成 0.6（只在材質檔被刪時發生）；`MESHY_LOOSE` 對整個網格刪 <5 cm 碎塊，換模型時留意。
- 主角線的 5 個工作 session（rig、tex、unity、anim、review）與它們的 worktree 已依使用者指示刪除。

## 進行中：UI 優化（拆成 4 個 session，已暫停）

使用者原話：「開一個session／幫我優化目前遊戲的ＵＩ／現在的版本品質太差」，之後「把#6 優化項目分給多個session同步進行」（選「拆成 4 個」「ui 先 commit 到自己的分支」）。

| 角色 | session | 分支 | 範圍 | 交接 |
|---|---|---|---|---|
| ui | local_b85be610-a5be-4421-89b2-1056ab0da58f | `claude/upbeat-golick-2db7a2` | ComfyUI 生圖管線、共用 `UiSkin`／`SsUiKit`、暫停＋通關、對話框、`MenuList`、直向提示、`SsArt`、截圖工具 | `ui-handoff.md` |
| touch | local_40b88b64-5c52-4d01-b6af-634a29024b7f | `claude/ui-touch-82e39b` | `TouchControlsView`（搖桿、功能按鈕） | `touch-handoff.md` |
| menu | local_a3fb98cc-9ac3-4f41-b104-acc6fffa5753 | `claude/ui-menu-eaad50` | `GameSelectMenu*`（海報式主視覺，保留授權小字） | `menu-handoff.md` |
| hud | local_9c150b82-f1ad-4047-948a-6a287a454b5d | `claude/ui-hud-352f1a` | HUD、Boss 血條、背包、強化、Loot 畫面、字卡／淡入淡出 | `hud-handoff.md` |

- 各角色交接文件在同資料夾（`Docs/sidescroller/ui-polish/<角色>-handoff.md`，在各自的分支上），各自 commit 並 push 到自己的分支；**都還沒合進 main**。
- 分工約定全文（檔案歸屬、ComfyUI 規則、使用者決定紀錄）：本機 `~/Library/Application Support/Claude/side-session-notes/local_553e400a-bc1b-493f-a5df-47c8fabd86ec/ui-split-contract.md`。
- 使用者在 UI 線的決定：方向「B：A＋新增 UI 美術素材」、素材用 ComfyUI（FLUX.1 schnell＋SDXL base＋IP-Adapter SDXL＋BiRefNet，皆可商用，裝在 `~/ComfyUI`，約 28GB）、素材範圍「外框組、觸控按鈕組、選單主視覺」（HUD 圖示不重繪）、`SsUiSkin`「全部改 ComfyUI」、腳本放 `Tools/ComfyUI`、「暫停和通關畫面重做一個版本給我看」、觸控「圓形金框／保留小字／一起修重疊」、選單「A 海報式」與按鈕字「開始遊戲」、hud「照方向全部套用」。

## 明天接手順序（建議）

1. ui 先把 ComfyUI 佇列剩下的素材生完、後製、commit 到 `claude/upbeat-golick-2db7a2`（交接列有佇列清單），並給使用者看暫停＋通關的 ComfyUI 版。
2. touch／menu／hud 各自 `git merge claude/upbeat-golick-2db7a2` 拿到新素材與修好的截圖工具（`UiShotCapture` 不加 `-batchmode`、開 Editor 視窗切固定解析度），重拍前後對照、跑完 review 與反向驗證，交使用者 review。
3. 協調整合四個分支（衝突點：`StoryUi.Init` 那 4 行以 hud 版為準、`StoryTexts.cs` 各自新增字串、`SsArt.cs` 以 ui 版為準、`GameSelectMenuView.cs` 以 menu 版為準且必須保留授權小字），跨邊界審查、從 Entry 實測，交使用者 review 後再問 commit／push／合進 main。

## 待使用者決定

- 遊戲畫面右側的 IngameDebugConsole 錯誤計數浮窗，正式版要不要關掉（hud 提出，不屬於任何 UI session 的檔案）。

## 看板與工具

- 看板 `http://127.0.0.1:8897/`（板名 hero-meshy，`/Users/iulee/UnityProject/session-dashboard/boards/hero-meshy`）。協調的每 10 分鐘排程與變更請求 Monitor 已停；明天重開協調時重新掛上（`session-dashboard/docs/coordinator.md` A、B 段）。
- 儀表板 repo 今天另推了 `4d1ef26`（巡檢提醒清掉已回答的「等你決定」）。
- 注意：menu session 多次把收尾回報寫成英文，提醒三次後仍偶發，明天請留意。
