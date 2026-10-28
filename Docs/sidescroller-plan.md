# 橫向卷軸動作遊戲：製程規劃（草案，待使用者確認）

狀態：**使用者已確認（2026-10-28）**，原話：「三個問題都用你建議的」。第 6 節三題皆採建議：製程與分工照本文件、worktree `blender-sidescroller`／分支 `feat/sidescroller`、新作名稱由設計 session 提案。由新的協調 session 接手開工。

## 1. 已拍板的需求與決定

| 項目 | 決定 |
|---|---|
| 玩法 | 2D 橫向動作平台：左右移動、跳躍、近戰＋遠程攻擊，關卡往右推進，關尾有 Boss |
| 世界觀 | **全新原創動物英雄**。風格參考 Blender Studio《Cowboi》（動畫感 NPR、粗描邊、卡通色階、90 年代動作片的色彩與節奏、動作＋喜劇），**只參考風格，不照抄角色** |
| 規模 | 垂直切片：**1 關＋3 段劇情**；攻擊 2～3 種、服裝 2 套。先把系統做齊，內容之後再加 |
| 主角 | 全程同一隻；攻擊手段與服裝由**劇情節點給予** |
| 劇情演出 | 場景上的對話框＋過場演出（角色移動、鏡頭、停頓、特寫、發放能力）；**資料驅動的演出腳本（json）**，放進 YooAsset 資源包，可熱更 |
| 對話語言 | 繁體中文，字型用專案內的思源黑體（已在 Git LFS） |
| 舊遊戲 | 《螢火礦坑》**保留，兩個遊戲並存**；進入點加選單選擇要玩哪一個 |
| 執行方式 | 子 session＋協調看板；先由設計 session 定稿，使用者確認後才分工開發 |

## 2. 遊戲內容草案（設計 session 會細化）

- **主角**：原創擬人動物（候選：山羊遊俠／犰狳快遞員／臭鼬牛仔，由設計 session 提 2～3 案給使用者選）。
- **反派**：誇張的企業型反派與手下雜兵（呼應參考作品「對抗邪惡企業」的喜劇調性，但角色與設定全部原創）。
- **關卡**：一條往右推進的關卡，分三段：
  1. 開場段：教學（移動、跳躍、近戰）。
  2. 中段：取得第 2 種攻擊（遠程）與第 2 套服裝後，用新能力通過的區段。
  3. Boss 房。
- **三段劇情**：
  1. 開場過場：交代主角與反派、目標。
  2. 中段對話：遇到 NPC，得到新攻擊＋新服裝（劇情節點發放）。
  3. Boss 前後演出：開戰前的對峙、擊敗後的收尾。
- **服裝**：改變外觀，可附少量數值差異（例如防禦或移動速度），由設計 session 定。

## 3. 技術設計

### 3.1 沿用的既有管線
- **美術**：`Tools/Blender/render_sprites.py`。這次用**側視角**（`--dirs` 只取左右，或只渲右側由程式翻轉）、動作多段（Idle／Run／Jump／Fall／Attack…）。
  NPR 風格以卡通色階＋描邊外殼（必要時 Grease Pencil Line Art）在 Blender 端做出，`--albedo`＋法線保留給 URP 2D 光照。
- **服裝**：同一模型換材質或配件後重渲一套 sheet（例如 `Hero_Default_*`、`Hero_Ranger_*`），程式依目前服裝切換 SpriteSet。
- **匯入**：`Assets/Editor/SpriteSheetImporter`（讀 json 自動切圖、掛法線）。
- **載入**：YooAsset `AssetPackage`＋`EntryScene.GameAssets`；演出腳本 json 一併打包。
- **遊戲入口**：`AppBootstrap.Run()` 先顯示「選擇遊戲」選單（螢火礦坑／新作），再進入對應遊戲。

### 3.2 新增系統
| 系統 | 內容 |
|---|---|
| 角色控制 | 平台跳躍物理（Rigidbody2D＋地面偵測、土狼時間、跳躍緩衝）、近戰判定、遠程子彈、受傷擊退與無敵時間 |
| 能力與服裝 | `Loadout`：目前已解鎖的攻擊與服裝；劇情指令可發放；切換時更新 SpriteSet 與數值 |
| 關卡 | 以資料（json 或 Tilemap 產生器）描述地形、平台、敵人生成點、劇情觸發區、Boss 房 |
| 敵人與 Boss | 2～3 種雜兵＋1 個 Boss（簡單狀態機） |
| 演出腳本 | json 指令序列：`say`（說話者、文字、立繪）、`move`、`camera`、`wait`、`fade`、`grant`（攻擊／服裝）、`setFlag`；由 `CutscenePlayer` 逐條執行，演出時鎖定玩家輸入 |
| 對話 UI | 對話框、說話者名稱、逐字顯示、按鍵跳過／推進；可選用立繪（同管線渲染的半身圖） |
| 鏡頭 | 跟隨主角、關卡邊界限制、演出時可被腳本接管 |

### 3.3 必須遵守的限制（沿用 Glowshaft 經驗）
- **HybridCLR**：遊戲程式在 Assembly-CSharp（遊戲模組），build 裡的場景與資源包**不可序列化引用**遊戲模組的型別，物件一律由程式建立；不用 Unity Timeline。
- **命名**：程式與註解**不出現 HotUpdate／熱更**字樣；C# 分區塊一律用 `#region`。
- **AOT 補充元數據**：新用到的泛型（例如 Physics2D 的回呼、Input System）出包前跑 `Generate/All`，對照 `AOTGenericReferences.cs` 補進 `HybridCLRTools.AotAssemblyListBase`。
- **Unity 鎖**：同一時間只能有一個 Unity 開 worktree；檢查用錨定開頭的 `pgrep`。
- **大檔**：push 前檢查 >10MB 的檔案（LFS 追蹤的除外）。
- **commit**：前綴 feat/fix/docs/refactor/chore，使用者 review 後才 commit／push。

## 4. 執行流程與分工

工作目錄：git worktree `/Users/iulee/UnityProject/blender-sidescroller`，分支 `feat/sidescroller`（從 `main` 開出）。

| 階段 | 內容 | 產出 | 誰 |
|---|---|---|---|
| 0 | 使用者確認本文件 | — | 使用者 |
| 1 | **設計定稿**：主角與反派提案（2～3 案）、三段劇本全文、關卡草圖與節奏、攻擊／服裝規格、演出腳本 json 格式、模組介面文件 | 設計文件＋介面文件 | 設計 session |
| 2 | 使用者確認設計（選主角案、看劇本） | — | 使用者 |
| 3 | 平行開發（各改各的資料夾，介面照設計文件） | 程式、素材、資料 | 見下表 |
| 4 | 協調整合審查（**只看跨邊界**）、整體編譯、從 Entry 跑整合實測 | — | 協調 |
| 5 | 交使用者 review → commit／push／tag | — | 使用者決定 |

階段 3 的子 session（effort 一律 medium，必要時再拆）：

| 角色 | 負責 |
|---|---|
| art | Blender 程式化建模（主角＋2 套服裝、雜兵、Boss、場景素材、立繪）、NPR 風格渲染、匯入 |
| player | 角色控制、戰鬥、能力與服裝（Loadout）、敵人與 Boss |
| level | 關卡資料與產生器、鏡頭、劇情觸發區、背景視差 |
| story | 演出腳本執行器、對話 UI、三段劇情的腳本資料、遊戲選擇選單 |
| assetpkg | 新素材與腳本進 AssetPackage、打包流程確認（若變動小，可併入 art） |

每個子 session 交差前：自審＋subagent review 與反向驗證、編譯 0 錯誤、Play 實測、寫交接檔。

## 5. 風險與待確認

- **NPR 風格的還原度**：預渲染 sprite 能做出卡通色階＋描邊，但「動畫感」很吃動作設計；垂直切片先求可讀與一致，風格細節之後迭代。
- **側視角動作數**：動作越多渲染越久、資源包越大；垂直切片先控制在每角色 5～6 個動作。
- **兩個遊戲並存**：共用 `AppBootstrap` 入口與 AssetPackage，素材資料夾要分開（`Assets/Art/Glowshaft/`、`Assets/Art/<新作>/`），收集器也要分組。
- **中文字型在 Player 的顯示**：思源黑體 TMP 字型是 Static 字集，要確認劇本用到的字都在字集內；設計 session 會列出需求。
- **工期**：設計約 1 小時（含使用者確認），平行開發約 2～3 小時，整合與 review 約 1 小時（實際依用量與實測而定）。

## 6. 需要使用者確認的事

1. 這份製程與分工是否 OK？
2. worktree 位置與分支名 `blender-sidescroller`／`feat/sidescroller` 是否 OK？
3. 新作暫定名稱（設計 session 也可以提案）。
