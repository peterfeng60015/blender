# 阿甲 3D 精緻度升級（hero3d-hd）：分工與介面約定

協調 session：hero3d-hd 協調（本檔作者）。整合用 worktree：`/Users/iulee/UnityProject/blender-hero3d-hd`（分支 `hero3d-hd`，基於 main 8d8c3db）。
使用者已決定（2026-10-29）：範圍 A＋B＋C（貼圖＋shader、造型重做、表情＋二次動畫）；畫風「Cowboi 色階＋提升精緻度」（保留粗描邊、硬色階，不改走 Spring 柔和電影感）；**直接改 `Tools/Blender/Hero3D/hero3d.py`**（不另開新腳本）；要拆成多個 session 同步做。

## 2026-10-29 追加決定：整體可重新設計

使用者原話：「整體模型可以重新設計／不需要按班原本的模型」；並選了「連角色設定也可改」「骨架也可重做」。
- 只保留「犰狳快遞員阿甲」這個概念；比例、頭身、臉型、配件、配色、服裝都可重做。畫風仍是 Cowboi 式粗描邊＋硬色階、精緻度往《Spring》靠。
- **造型 session 是角色設計的主導者**：擁有參數區、`COLORS`、`BONES`／`ACCESSORY_BONES`，以及 6 個動作的**身體主動作**關鍵格（ANIMS 的骨頭旋轉）；骨頭可改名、增減（≤ 40 根），新設計要在 `#region 參數` 開頭的 docstring 寫清楚。
- 表情 session 只負責在主動作之上疊表情 shape key 與耳／尾／包等跟隨（二次動畫用獨立的關鍵格表，例如 `SECONDARY` 與 `FACE_KEYS`，在 build_actions 合併），骨頭名稱讀造型的定義。
- 貼圖 session 依 `COLORS` 鍵與零件名處理，名稱以造型 session 最終版為準；新零件要能自動套用（找不到對應時退回固有色）。
- Shader session 不受影響（介面不變）。
- 物件名、`EYE_C`／`EYE_R`／`CAP_BRIM_Z` 等常數：名稱保留當介面，值與是否有帽子由造型決定（若取消帽子，`CAP_BRIM_Z` 改為頭頂可放眉的上限高度）。
- Unity 端（`SsHero3D_Anim.controller`、`HeroVisual.cs`）與 `Docs/sidescroller/design.md` 的同步，整合時由協調處理（改 design.md 前先問使用者）。

## 造型 session 的重設計提案（2026-10-29，已開工）

約 2.9 頭身、頭放大約 12%（頭半徑 0.132）、`EYE_R` 0.038、長嘴略加長；快遞帽往後推、前緣上翹，`CAP_BRIM_Z` 約 0.96；短褲改深藍、新增芥末黃頸巾零件 `Scarf`（`COLORS` 新鍵 `scarf`，綁 Neck／Chest）、鞋帶新鍵 `lace`；**31 根骨頭名稱與階層不變**（只微調 Head／Snout／Ear 位置），6 個動作主關鍵格沿用；目標約 18k 三角形。貼圖 session：`scarf`、`lace` 是新鍵（頸巾可用布紋）。

## 看板回報（2026-10-29 補）

看板 `http://127.0.0.1:8897/`，用 `python3 /Users/iulee/UnityProject/session-dashboard/boards/hero3d-hd/report.py <角色> …` 回報；角色：`shape`、`texture`、`shader`、`face`。規則全文見 `/Users/iulee/UnityProject/session-dashboard/docs/brief-snippet.md`（`<名稱>`＝hero3d-hd）。每個里程碑、開始審查前、回報完成前重看本檔。

## 共通規則

- 每個工作 session 在自己的 worktree 改，**不 commit、不 push**；完成後回報 worktree 路徑，協調 session 用 `git diff` 合進 `hero3d-hd`。
- 都改同一支 `hero3d.py`：**只在自己負責的 `#region` 裡改**，新功能開新的 `#region`；`main()` 只加最少行（呼叫自己的函式），參數區只加自己的常數。這樣合併衝突最小。
- 跑法：`/Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/Blender/Hero3D/hero3d.py -- --out <自己的 scratchpad 資料夾>`（約 30 秒）。輸出圖用 Read 看，自己比對前後。不要把輸出放進 repo。
- 專案規範：給使用者的文字繁中；C# 與 Python 分區塊用 `#region`；程式與註解遵守專案的命名禁用字規定；註解密度與既有程式一致。
- Unity：同一時間一個專案路徑只能開一個 Unity（檢查：`pgrep -f '^/Applications/Unity/Hub/Editor/[^ ]*/Unity.app/Contents/MacOS/Unity .*<專案路徑>'`）；batchmode 後 Unity 常改寫 `Assets/Settings/*.asset`，要還原。只有 shader session 需要碰 Unity。
- 效能預算（Android S22 Ultra 等級＋手機 WebGL）：整隻 ≤ 20k 三角形、骨頭 ≤ 40（目前 31 根）、1 個 SkinnedMeshRenderer、1 材質（主體＋描邊 2 pass）、貼圖合計 ≤ 2 張 1024²（albedo＋mask），blend shape ≤ 12 個。

## 介面約定（跨 session 的資料格式）

| 項目 | 約定 | 負責 |
|---|---|---|
| 物件名 | 合併前的零件名維持 `Body Shell ShellFill Eyes Nose Ears Cap Hands Shoes Bag Strap`；可新增（例如 `Fingers`、`Teeth`），但 `skin_character()` 的 rules 要補上綁骨規則 | 造型 |
| 臉部表情零件 | 眉毛 `Brows`、眼皮 `Eyelids`（從 `Eyes` 拆出）、嘴 `Mouth`（嘴縫／口腔）：由**表情** session 建 | 表情 |
| 眼睛位置 | 參數區常數 `EYE_C = (0.072, -0.205, 0.872)`（+X 那顆，-X 鏡射）、`EYE_R`（眼白半徑）；造型 session 改頭形時只改這些常數，表情 session 讀它們 | 造型定值，表情讀 |
| 帽簷高度 | （2026-10-29 新增）參數區常數 `CAP_BRIM_Z`＝帽簷前緣下緣的 z；眼睛上緣到它留約 0.03 公尺額頭給眉毛。表情 session 的眉毛（含 `Brow_Up`）最高不超過 `CAP_BRIM_Z − 0.005` | 造型定值，表情讀 |
| 材質鍵 | `COLORS` 的鍵＝材質種類；新增顏色在 `COLORS` 加鍵 | 各自 |
| UV0 | 貼圖 atlas UV（各零件 UV 島擺進同一張 1024²） | 貼圖 |
| UV1（Blender 第二個 UV 層 `OutlineNormal`） | 描邊用的平滑法線，**切線空間** xy（z 由 shader 重建）；shader 在 Unity 讀 `TEXCOORD1` | shader |
| 頂點色 `Col` | RGB＝材質固有色（現況，保留當後備）；**A＝描邊粗細**（1.0 主體、0.6 小配件，取代現在只在 Blender 預覽用的 `OutlineWidth` 群組） | 貼圖（烘頂點色那段）、shader 讀 |
| `_BaseMap` | albedo，sRGB，1024²，手繪感（殼紋、漸層、布紋、邊緣亮化） | 貼圖 |
| `_MaskMap` | linear，1024²：R＝高光遮罩（帽子、扣環、眼睛、鼻子）、G＝陰影偏移／AO（0.5＝不偏移，越低越容易進陰影）、B＝材質類別（0 皮膚、0.33 殼、0.66 布、1 金屬／眼睛），給 ramp 選列用 | 貼圖產出、shader 讀 |
| 貼圖輸出 | 腳本輸出到 `--out` 的 `AhJia3D_BaseMap.png`、`AhJia3D_MaskMap.png`；FBX 不嵌貼圖 | 貼圖 |
| Blend shape 名稱 | `Blink_L` `Blink_R` `Brow_Angry` `Brow_Sad` `Brow_Up` `Mouth_Open` `Mouth_Smirk` `Mouth_Frown` `Squint`（≤ 12）；FBX 匯出要帶 shape key | 表情 |
| 表情進動作 | 表情的 shape key 值直接 key 在現有 6 個 Action 裡（例如 Idle 眨眼、Melee 生氣、Hurt 痛），Unity 不用新增程式 | 表情 |

## 各 session 範圍

1. **造型**：`#region 身體`、`背殼`、`頭部配件`（眼皮／眉／嘴除外）、`手、鞋、郵差包`、參數區 `D`。目標 15～20k 三角形：頭形與長嘴更有雕塑感（顴骨、嘴側凹）、背殼分節帶厚度與邊緣倒角、殼面鱗片凸起（可用位移）、手改 3～4 指、鞋子有鞋舌與鞋帶結構、包包有縫線凸邊、帽子有帽縫與厚帽簷；拓樸利於變形（關節處多一圈）。
2. **貼圖**：新 `#region UV 與貼圖`＋改 `bake_vertex_colors()`：每零件 UV 展開打包、程式化手繪感 albedo（殼的鱗片紋與邊緣磨損、皮膚的粉色漸層與腮紅、布料細紋、帽縫）、mask 圖、輸出 PNG；頂點色 A 寫描邊粗細。要能吃造型 session 改過的網格（依材質種類與零件名處理，不要寫死頂點數）。
3. **Shader＋描邊法線**：改 `Assets/Art/Hero3D/AhJiaToon.shader`（與 `M_AhJiaToon.mat`、必要時 `Assets/Editor/Hero3D/Hero3DPreviewSetup.cs`）＋ `hero3d.py` 新 `#region 描邊法線`（算平滑法線寫進 UV1、匯出 FBX 帶第二 UV 與切線）。shader：貼圖＋ramp（依 mask B 選列，保持硬色階）、mask G 陰影偏移、rim light、帽子／眼睛高光、倒殼描邊改用平滑法線＋頂點色 A 粗細＋螢幕空間定寬，URP 2D Renderer 的 `Universal2D`／`SRPDefaultUnlit` pass；不可引用遊戲模組型別。
4. **表情＋二次動畫**：新 `#region 臉部表情`（建 Brows／Eyelids／Mouth、shape keys）、改 `#region 動畫` 的 ANIMS（表情 key、耳朵／尾巴／包包的延遲與跟隨，overlapping action）、FBX 帶 shape key。
