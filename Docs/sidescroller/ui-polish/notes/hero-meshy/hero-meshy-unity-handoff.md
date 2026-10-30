# hero-meshy：unity 交接

- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/heuristic-sammet-3892c5`（分支 `claude/heuristic-sammet-3892c5`，基於 main ac19ffb），**未 commit**。
- 交付 diff：`git -C <worktree> diff`，共 5 檔（+97／−16）。worktree 裡的模型、貼圖、shader、材質都已還原成 main 版，不在 diff 內。

## 改了什麼

| 檔案 | 內容 |
|---|---|
| `Assets/Editor/Hero3D/Hero3DPreviewSetup.cs` | 檔頭說明 Meshy 版也適用；FBX 材質名稱不是 `M_AhJia` 時一律對應到 `M_AhJiaToon`（第一次對應時印警告，重跑不再印）；clip 短名改成先取 `|` 之後、再認 6 個動作名稱的結尾（`AhJia_Rig|AhJia_Idle`、`Idle`、`Rig|Idle` 都對）；缺某個動作時印警告、該狀態改播 Idle，控制器永遠 6 個狀態，沒有 Idle 才中止；匯入後在 Console 印「三角形、骨頭、blend shape 數、材質」摘要；batchmode 的 Setup＋截圖共用同一份 clips（缺動作警告只印一次） |
| `Assets/Scripts/Sidescroller/Menu/GameSelectMenuView.cs` | 右下角加授權小字：放在每幀 `SsUiKit.FitSafeArea` 的 `SafeArea` 子物件裡，24pt、白 45%、靠右下、不換行，距安全區右 24、下 16；建在直向提示之前（直向時被蓋住） |
| `Assets/Scripts/Sidescroller/Story/StoryTexts.cs` | 新增 `MenuModelCredit = "主角模型：Meshy（CC BY 4.0）"`，列進 `All`（`CutsceneChecker` 會檢查缺字） |
| `Assets/Scripts/Sidescroller/Actors/HeroVisual.cs` | 只加一行註解：不碰 blend shape，有沒有表情都照常運作 |
| `Docs/sidescroller/interfaces.md` | §7.7 加「Meshy 版」：沒有表情、Setup 容錯、授權標註 |

盤點結論：Unity 端本來就沒有任何 `SetBlendShapeWeight`／依名稱找 blend shape 的程式，表情只存在舊 FBX 的 clip 曲線裡（新 FBX 沒有就不存在）；演出的 `face` 指令是轉向，與表情無關。`ModelScale`（1.3）、物件名、`M_AhJia` remap、UV1、頂點色用法都不用改。

## 怎麼重跑 Setup

1. rig 的 `AhJia3D.fbx` → `Assets/Art/Sidescroller/Models/SsHero3D/SsHero3D.fbx`；tex 的 `AhJia3D_BaseMap.png`／`AhJia3D_MaskMap.png` → 同資料夾 `SsHero3D_BaseMap.png`／`SsHero3D_MaskMap.png`（貼圖要用 tex 正式版，Meshy UV）。
2. Unity 選單 **Tools → Hero3D → 設定模型與預覽場景**；或 batchmode：
   `Unity -batchmode -quit -projectPath <專案> -executeMethod Hero3DPreviewSetup.SetupAndCaptureBatch -capturePath <png> -logFile <log>`
3. Console 應看到 `AhJia_Mesh：19800 三角形、28 根骨頭、0 個 blend shape、材質 M_AhJiaToon` 與 6 個動作。
4. 會重產 `AhJiaPreview.controller`、`Hero3DPreview.unity`、`SsHero3D_Anim.controller` 與貼圖 meta（多出 iOS 平台區段是 Unity 重新序列化），整合時一起進版。
5. 實測：`-executeMethod Hero3DPlayCapture.RunBatch -captureDir <資料夾>`（不加 -quit）。

## 實測

截圖都在 `…/local_553e400a-bc1b-493f-a5df-47c8fabd86ec/hero-meshy-unity-shots/`。

| 項目 | 結果 | 截圖 |
|---|---|---|
| rig 第一版 FBX（rig-20261030-1459）＋tex 正式貼圖＋tex 的 shader／`M_AhJiaToon.mat`（`_RampStrength` 0.6，測試用、未進 diff）：Setup | 19,800 三角形、28 骨、0 blend shape、6 clip 各 290 條曲線，無警告 | `meshy/preview.png` |
| 同上，從 Entry 進 Play（AssetPackage 模擬模式載入，沒有退回 AssetDatabase）：Idle、跑、跳、近戰、射擊、受傷、轉身 | 主角是 3D，動作正確，log 無「找不到狀態」或 blend shape 相關警告 | `meshy/play_0～6.png`（遊戲相機 1280×720） |
| 現版模型（改動不壞舊版）：Setup＋Entry 實測 | 18,738 三角形、9 blend shape，全部正常 | `current/` |
| 反向驗證：拿掉 Melee clip＋`importBlendShapes=false`（0 blend shape） | Setup 印一次警告、控制器 6 狀態（Melee=Idle）；Entry 實測零警告，Melee 那格播 Idle | `reverse/play_3_melee_missing.png` |
| 反向驗證：拿掉 `M_AhJia` remap | 材質留在 FBX 子資產、偵測得到；加回後 0 個（stray 偵測的前提成立） | — |
| 選單標註排版：1920×1080、1280×720、2400×1080、3088×1440（S22U）、2340×1080、2048×1536、2732×2048（模擬瀏海／圓角／Home 條安全區）＋1080×2400 直向 | 標註都在安全區內、和標題／按鈕／提示文字無重疊；直向被直向提示蓋住 | `menu/` |
| `CutsceneChecker` 缺字檢查 | 通過（含新字串） | — |
| 腳本編譯 | Editor、Android、WebGL Player 腳本都 0 錯誤（`PlayerBuildInterface.CompilePlayerScripts`） | — |
| AssetPackage | `sidescroller_models` 收整個 `SsHero3D/`（fbx、png、controller），材質與 shader 走依賴；檔名不變所以不用改收集設定 | — |
| HybridCLR | 資源只引用 Unity 型別；改動檔無 舊的模組命名字樣（改用 AppBootstrap／模組） | — |

審查：自審 diff＋1 輪 subagent review（無高／中嚴重度，修 3 個低項：缺動作警告重複、三角形計數改 `GetIndexCount`、文件措辭），修後重編譯與 Setup 重跑通過。

## 已知問題／提醒

- `interfaces.md` 與 Setup 檔頭寫了 `hero3d.py --source meshy --glb <路徑>`（照約定檔）；這個 worktree 的 `hero3d.py` 還沒有這兩個參數，要等 rig 的改動合進來才相符。
- 新模型在遊戲裡看起來比舊版稍瘦小（高度相近，殼比較貼身）；權重問題（肩甲、殼、包）協調已知，未另列。
- clip 名稱比對分大小寫：若 rig 匯出 `idle`／`Running` 之類名稱，Idle 對不上會中止、其他動作退回 Idle 並印警告（不會靜靜出錯）。
- 材質 remap 會累積舊名稱的對應（無害）。
- batchmode log 裡的 `ArgumentOutOfRangeException`（`UnityEditor.Search.SearchDatabase`）是 Unity Search 啟動索引的內部錯誤，與改動無關（堆疊全在 Unity 內部，不經過專案程式）。
- worktree 的 `Library` 是從主專案 APFS clone 來的（已 gitignore）。
