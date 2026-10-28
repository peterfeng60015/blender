# 橫向卷軸新作：模組介面與分工（art／player／level／story／assetpkg）

作者：`design`。狀態：**待使用者確認設計後生效**（主角、反派、名稱不影響本文件：型別、檔名、流程都和選案無關；選案只會換劇本文字與美術內容）。
共同規則（HybridCLR、命名、`#region`、Input System、渲染、Unity 鎖、審查、看板回報）一律照看板 `RULES.md`，本文件只補這款遊戲內部的分工與介面。
設計內容（數值、關卡、劇本、美術清單）見 `design.md`；演出腳本格式見 `cutscene-format.md`。

**改介面的流程**：本文件 §3（Contracts）的型別歸 `player`，§4 各角色的公開 API 歸各自角色。要改簽名或加欄位：先 SendMessage 給擁有者與協調，擁有者改完通知所有受影響的角色。**不要直接改別人的檔案**。

---

## 1. 資料夾與檔案歸屬

全部在 worktree `/Users/iulee/UnityProject/blender-sidescroller`。程式都在 Assembly-CSharp（遊戲模組代碼），**不放 asmdef**。

| 角色 | 擁有（新增／修改） | 內容 |
|---|---|---|
| `player` | `Assets/Scripts/Sidescroller/Contracts/**`、`Assets/Scripts/Sidescroller/Core/**`、`Assets/Scripts/Sidescroller/Actors/**` | 共用型別；遊戲啟動與流程（`SidescrollerGame`、`GameFlow`）、素材讀取 `SsArt`、輸入 `SsInput`、動畫播放；主角、能力與服裝、敵人、Boss、NPC、子彈 |
| `level` | `Assets/Scripts/Sidescroller/Level/**`、`Assets/Art/Sidescroller/Data/Levels/**` | 關卡資料與解析、地形與碰撞、裝飾與地標、視差背景、Global Light 2D、觸發區、閘門、鏡頭 `CameraRig` |
| `story` | `Assets/Scripts/Sidescroller/Story/**`、`Assets/Scripts/Sidescroller/Menu/**`、`Assets/Art/Sidescroller/Data/Cutscenes/**` | 演出腳本解析與執行、對話框、字卡、淡入淡出、HUD、暫停選單、通關畫面、遊戲選擇選單、四份演出腳本 |
| `art` | `Assets/Art/Sidescroller/Sprites/**`、`Tools/Blender/Sidescroller/**` | bpy 程式化建模與動作、NPR 渲染、立繪、場景素材、一鍵重做腳本與 finalize 檢查 |
| `assetpkg` | （只改下表的共用檔，以及由 Setup 改寫的 `Assets/BundleCollectorSetting.asset`） | AssetPackage 收集器分組、打包前檢查、模擬模式與打包驗證 |

### 1.1 要改的既有共用檔（經協調同意後最小改動）

| 檔案 | 誰改 | 改什麼 | 不能動的部分 |
|---|---|---|---|
| `Assets/Scripts/Bootstrap/AppBootstrap.cs` | **協調**（第 0 波，§9） | `Run()` 裡的 `Glowshaft.GlowshaftGame.Boot();` 改成 `Sidescroller.GameSelectMenu.Boot();`（§7）。之後誰都不再改 | 型別名、無命名空間、`public static void Run()` 簽名（EntrySceneController 用反射呼叫） |
| `Assets/Editor/SpriteSheetImporter/SpriteSheetImporter.cs`、`SpriteSheetPreview.cs` | `art` | `SpritesRoot`（單一路徑）改成兩個根目錄：`Assets/Art/Glowshaft/Sprites`、`Assets/Art/Sidescroller/Sprites`；兩邊都要求 PPU 64 | Glowshaft 素材的匯入結果不能變（改完重跑匯入，Glowshaft 的 `.meta` 不應出現 diff） |
| `Assets/Editor/YooAssetTools/AssetPackageCollectorSetup.cs`、`AssetPackageBuildTool.cs` | `assetpkg` | 加兩個 group（§6.3）、把打包前檢查擴大到新的根目錄 | 既有 group `glowshaft_sprites` 的設定與行為；`PackageName` 仍是 `AssetPackage` |

### 1.2 不能改的檔（任何角色）

- `Assets/Entry/**`（含 `GameAssets.cs`，屬於 AOT 端，改了要重新出包）、`Assets/Scenes/Entry.unity`。
- `Assets/Scripts/Glowshaft/**`、`Assets/Art/Glowshaft/**`、`Tools/Blender/Glowshaft/**`。
- `Tools/Blender/render_sprites.py`（共用渲染器；真的需要新參數時，只能**加向後相容的選項**，先經協調同意）。
- `Assets/Editor/HybridCLRTools/**`（AOT 清單由協調統一補，各角色在交接檔列出新用到的泛型）。
- `CLAUDE.md`、`.gitignore`、`Packages/`、`ProjectSettings/`（不用自訂 Layer、Tag、Sorting Layer）。
- `Assets/Plugins/IngameDebugConsole/**`（中文字型在這裡，只讀取不修改，§7.4）。

---

## 2. 命名

- 命名空間一律 `namespace Sidescroller`（不分子命名空間；檔名＝主要型別名）。和 Glowshaft 同名的型別（例如 `SpriteSet`）因命名空間不同不衝突，但 **Sidescroller 的程式不要 `using Glowshaft`**（只有 `GameSelectMenu` 用完整名稱呼叫 `Glowshaft.GlowshaftGame.Boot()`）。
- 素材與資料檔名一律 `Ss` 開頭（§6）。
- 程式、註解、log 的禁用字照 `RULES.md`「命名」一節；log 前綴用 `[Sidescroller.<型別>]`，例如 `[Sidescroller.GameFlow]`。
- C# 分區塊一律 `#region`／`#endregion`。

---

## 3. 共用型別（`Assets/Scripts/Sidescroller/Contracts/`，`player` 擁有）

初版由協調照本節**原樣**落地（§9 第 0 波），之後歸 `player` 維護。只放型別與極簡實作，不放遊戲邏輯。
規則：**型別與事件只用 class、enum、陣列、`List<參考型別>`、`Action`／`Action<…>`**，不用 `HashSet<enum>`、`Dictionary<enum,…>`、ValueTuple、自訂 struct 泛型（§8）。

### 3.1 `Ids.cs`：列舉

```csharp
public enum AttackId { Melee = 0, Sling = 1, Roll = 2 }
public enum CostumeId { Day = 0, Express = 1 }
public enum ActorKind { Hero = 0, Enemy = 1, Boss = 2, Npc = 3 }
public enum TerrainKind { Solid = 0, OneWay = 1 }
public enum TriggerKind { Cutscene = 0, Hint = 1, Checkpoint = 2, BossArena = 3, KillZone = 4 }
```

### 3.2 `SsIds.cs`：共用字串與常數

```csharp
public static class SsIds
{
    public const string LevelStage1 = "SsLevel_Stage1";
    public const string CutsceneIntro = "SsCutscene_Intro";
    public const string CutsceneMentor = "SsCutscene_Mentor";
    public const string CutsceneBossIntro = "SsCutscene_BossIntro";
    public const string CutsceneBossDefeat = "SsCutscene_BossDefeat";

    public const string ActorHero = "hero";
    public const string ActorBoss = "boss";
    public const string ActorMentor = "mentor";
    public const string BossDisplayName = "河董";   // Boss 血條上的名字（換反派案時改這裡）

    public const string FlagGateClosed = "tmp_boss_gate_closed";   // 閘門關閉（tmp_：重生時清掉）
    public const string FlagMentorDone = "mentor_done";
    public const string FlagStageClear = "stage_clear";
    public static string SeenFlag(string cutsceneLocation) => "seen_" + cutsceneLocation;

    public const float CameraSize = 6f;        // 一般鏡頭的正交 size
    public const float ArenaCameraSize = 6.75f; // Boss 房
    public const int PixelsPerUnit = 64;
}
```

### 3.3 `Loadout.cs`：已取得的攻擊與服裝

```csharp
public sealed class Loadout
{
    public CostumeId Costume { get; }               // 目前穿的
    public int CostumeCount { get; }                // 已取得幾套（>1 才能按 C 換）
    public event Action Changed;                    // 取得新東西或換裝時觸發（狀態沒變不觸發）

    public bool HasAttack(AttackId id);
    public bool HasCostume(CostumeId id);
    public void GrantAttack(AttackId id);
    public void GrantCostume(CostumeId id, bool equip);
    public bool SetCostume(CostumeId id);           // 未取得：回 false 並記警告，不改狀態
    public void Reset();                            // 回到開場：只有 Melee＋Day，觸發 Changed

    public static string DisplayName(AttackId id);  // 甩包攻擊／急件彈弓／滾殼衝撞
    public static string DisplayName(CostumeId id); // 日班制服／特急制服
}
```
- 內部用 `int` 位元旗標存，不用集合。
- **誰改**：`story`（演出的 `grant`）、`player`（按 C 換裝、§5.3 的保底發放、`Reset`）。`level` 不碰。
- 重生（死亡重來）**不** Reset；只有 `SidescrollerGame.Boot()` 新建 `GameSession` 時是初始狀態。

### 3.4 `StoryFlags.cs`：劇情旗標

```csharp
public sealed class StoryFlags
{
    public const string TempPrefix = "tmp_";
    public event Action<string, bool> Changed;      // (flag, value)，值真的變了才觸發
    public bool Get(string flag);                   // 沒設過＝false
    public void Set(string flag, bool value = true);
    public void ClearTemporary();                   // 把 tmp_ 開頭的旗標全設回 false（各觸發一次 Changed）
}
```
- 內部用 `List<string>` 存值為 true 的旗標。
- 正式旗標：`seen_<cutscene>`（`player` 的流程在演出播完後設）、`mentor_done`、`stage_clear`（腳本設）、`tmp_boss_gate_closed`（腳本或流程設、重生時清掉）。

### 3.5 `GameSession.cs`：一次遊玩的共用狀態（整局只有一個）

```csharp
public sealed class GameSession
{
    public GameSession(CancellationToken lifetime);

    public CancellationToken Lifetime { get; }      // Shutdown 時取消；所有 await 都要帶它
    public Loadout Loadout { get; }
    public StoryFlags Flags { get; }

    #region 主角血量（player 改，story 讀）
    public int Hp { get; }
    public int MaxHp { get; }
    public event Action<int, int> HpChanged;        // (hp, maxHp)
    public void SetHp(int hp, int maxHp);
    #endregion

    #region Boss 血條（player 改，story 讀）
    public bool BossBarVisible { get; }
    public int BossHp { get; }
    public int BossMaxHp { get; }
    public string BossName { get; }                 // 「河董」
    public event Action BossChanged;
    public void SetBoss(bool visible, int hp, int maxHp, string name);
    #endregion

    #region 提示文字（player 的流程依觸發區改，story 的 HUD 顯示）
    public string Hint { get; }                     // null＝不顯示
    public event Action<string> HintChanged;
    public void SetHint(string text);
    #endregion

    #region 演出、暫停與輸入鎖
    public bool CutsceneActive { get; }             // story 設（PlayCutsceneAsync 期間）
    public void SetCutsceneActive(bool active);
    public bool Paused { get; }                     // story 的暫停選單設；player 收到後改 Time.timeScale
    public event Action<bool> PausedChanged;
    public void SetPaused(bool paused);
    public void PushInputLock();                    // player 的流程用（重生淡出入時）；與 Pop 成對
    public void PopInputLock();
    public bool InputLocked { get; }                // CutsceneActive || Paused || 鎖計數 > 0
    #endregion

    #region 觸發區（level 通知，player 的流程處理）
    public event Action<TriggerInfo> TriggerEntered;
    public event Action<TriggerInfo> TriggerExited;
    public void NotifyTriggerEntered(TriggerInfo trigger);
    public void NotifyTriggerExited(TriggerInfo trigger);
    #endregion

    #region 流程（story 要求、player 執行）
    public event Action RespawnRequested;           // 暫停選單「從檢查點重來」
    public void RequestRespawn();
    public event Action<float> StageCleared;        // 通關（參數＝通關秒數），story 顯示通關畫面
    public void NotifyStageCleared(float seconds);  // player 呼叫
    #endregion
}
```
- `Set…`／`Notify…` 方法依上面註明的角色呼叫，其他角色只讀屬性、訂閱事件。
- **訂閱者必須在 `OnDestroy` 取消訂閱**：`GameSession` 活得比關卡久（重生會重建關卡物件，但 session 不變）。
- 事件在主執行緒同步觸發；處理器裡不要丟例外（丟了會中斷後面的訂閱者）。

### 3.6 `LevelInfo.cs`：關卡建好後交給流程的資料

```csharp
public sealed class LevelInfo
{
    public Rect CameraBounds;                       // 鏡頭可以看到的範圍（世界座標）
    public Rect ArenaRect;                          // Boss 房（鏡頭鎖定範圍）
    public float KillY;                             // 角色掉到這個 y 以下＝掉坑（保底，正常靠 KillZone 觸發區）
    public readonly List<SpawnPoint> Spawns = new List<SpawnPoint>();         // 只有角色（不含主角）
    public readonly List<CheckpointInfo> Checkpoints = new List<CheckpointInfo>();
    public readonly List<TriggerInfo> Triggers = new List<TriggerInfo>();     // 已由 level 建成觸發區，僅供查詢
    public CheckpointInfo FindCheckpoint(string id); // 找不到回 null
}

public sealed class SpawnPoint
{
    public string Kind;      // "rat"、"pigeon"、"rhino"、"boss"、"npc"
    public string Id;        // 唯一，例如 "rat_intro"、"boss"、"mentor"；演出用這個找角色
    public Vector2 Position; // 腳底
    public int Facing;       // +1 右、-1 左
}

public sealed class CheckpointInfo
{
    public string Id;        // "cp0"、"cp1"、"cp2"
    public Vector2 Position; // 主角重生的腳底位置
}

public sealed class TriggerInfo
{
    public string Id;
    public TriggerKind Kind;
    public Rect Area;
    public string Arg;       // Cutscene／BossArena：演出 location；Hint：提示文字；Checkpoint：存檔點 id；KillZone：空
}
```

### 3.7 `ActorTag.cs`、`TerrainTag.cs`：標記元件

```csharp
public sealed class ActorTag : MonoBehaviour   // player 掛在每個角色的根物件
{
    public ActorKind Kind;
    public string Id;
}

public sealed class TerrainTag : MonoBehaviour // level 掛在每個地形 collider 所在的物件
{
    public TerrainKind Kind;
}
```
- 觸發區（level）用 `other.GetComponentInParent<ActorTag>()` 判斷是不是主角（`Kind == Hero`）。
- 子彈（player）打到掛 `TerrainTag` 且 `Kind == Solid` 的 collider 就消失；`OneWay` 不擋子彈。
- 兩者都由程式 `AddComponent`，欄位在 `AddComponent` 後立刻設定。

### 3.8 `ICutsceneActor.cs`、`ActorRegistry.cs`：演出操控角色

```csharp
public interface ICutsceneActor
{
    string Id { get; }
    Transform Root { get; }                       // 位置＝腳底
    int Facing { get; }                           // +1 右、-1 左
    void BeginScripted();                         // 停玩家輸入或 AI、水平速度歸零；重力照常（在空中會自己落地）
    void EndScripted();                           // 回到 BeginScripted 之前的狀態；本來就停著的（未啟動或已擊敗的 Boss）維持不動
    void SetFacing(int dir);
    void ScriptedMoveTo(float x, float speed);    // 自己用物理水平走到 x，自己播 Run／Walk，到了停下播 Idle
    bool IsScriptedMoving { get; }
    void Teleport(float x);                       // 跳過演出用：瞬移到 x（y 不變）並停下
    bool PlayAction(string action, bool loop);    // 沒有這個動作回 false；loop=false 播完回「待機動作」（Idle；沒有 Idle 的角色用自己的預設，例如監工鴿 Fly）
    float GetActionDuration(string action);       // 一輪的秒數；沒有這個動作回 0
}

public static class ActorRegistry
{
    public static void Register(ICutsceneActor actor);    // 同 id 已存在：記警告並以新的取代
    public static void Unregister(ICutsceneActor actor);  // 只移除同一個實例
    public static ICutsceneActor Find(string id);         // 找不到回 null
    public static void Clear();                           // player 在 Boot、重建關卡、Shutdown 時呼叫
}
```
- `player` 的每個角色（主角、雜兵、Boss、NPC）都實作並在建立時 `Register`、`OnDestroy` 時 `Unregister`。
- 動作名稱就是素材的 Action（`design.md` §8.3）。
- 呼叫者（`story` 的演出）：用到某角色的第一個指令前呼叫一次 `BeginScripted`，演出結束時（含失敗、跳過、取消）對所有用過的角色呼叫 `EndScripted`。

### 3.9 `Combat.cs`：傷害（讓 player 內部、或日後拆出的 enemy 角色共用）

```csharp
public enum DamageSource { Melee = 0, Sling = 1, Roll = 2, Enemy = 3 }

public sealed class HitInfo
{
    public int Amount;
    public DamageSource Source;
    public int Direction;        // 攻擊方向：+1 從左打向右、-1 從右打向左（判斷正面／背後與擊退方向）
    public bool BreaksShield;    // 滾殼＝true
    public GameObject Attacker;
}

public interface IDamageable
{
    bool TakeHit(HitInfo hit);   // 有效命中回 true；被盾擋、無敵中回 false
}
```

- **實作後補充**：`HitInfo` 另有 `public float Knockback;`（擊退倍率，0＝預設；犀牛突進用）。

### 3.9.1 `EnemyContracts.cs`：player 與 enemy 的對接（實作時由 player 新增）

```csharp
public sealed class EnemyContext            // GameFlow 每條命建一份交給 enemy，enemy 只讀
{
    public GameSession Session; public CameraRig Rig; public LevelInfo Level;
    public Transform Parent;                 // worldRoot，重生時一起刪
    public Transform Hero;                   // 主角根物件（腳底）
    public readonly List<Collider2D> HeroBodies;  // 主角實體碰撞體（站立膠囊、滾殼圓）
    public void IgnoreHero(Collider2D c);    // Physics2D.IgnoreCollision，主角與敵人互不推擠
}
public interface IBossController            // Boss 根物件上，GameFlow 用 GetComponent 取得
{
    int Hp { get; } int MaxHp { get; }
    void StartFight();                       // BossArena 處理時呼叫，每條命最多一次
    void StopAll();                          // 擊敗後呼叫
    event Action<int, int> HpChanged;        // GameFlow 轉給 session.SetBoss（Boss 血條只由 GameFlow 寫）
    event Action Defeated;
}
```
- 建立入口：`Actors/EnemySpawner.cs`（player）的 `public static GameObject Create(SpawnPoint, EnemyContext)`；真正的建立由 enemy 在 `Actors/Enemies/` 實作 `static partial void CreateEnemy(SpawnPoint, EnemyContext, ref GameObject)`，沒實作時流程改建佔位敵人。
- 共用元件：`Core/SpriteAnimator.cs`（幀動畫）、`Core/FxPlayer.cs`（特效；命中特效由被打的一方播）。

### 3.10 `SpriteSet.cs`：一張 sheet 切好的全部幀（側視、單方向）

```csharp
public sealed class SpriteSet
{
    public readonly string Location;    // 例如 "SsHeroDay_Run"
    public readonly Sprite[] Frames;    // 依編號排序
    public readonly float Fps;          // json 的 fps；讀不到時 12
    public readonly bool IsPlaceholder; // true＝美術還沒到，這是程式產生的佔位圖
    public SpriteSet(string location, Sprite[] frames, float fps, bool isPlaceholder);
    public int FrameCount { get; }
    public float Duration { get; }      // FrameCount / Fps
    public Sprite Get(int frame, bool loop);   // loop=false 時超出範圍停在最後一格
    public Sprite Variant(int variant);        // 單張多變化素材（tile、props、立繪表情）；超出範圍取 0 並記一次警告
}
```

### 3.11 `SsSorting.cs`：排序（只用 Default sorting layer）

```csharp
public static class SsSorting
{
    public const int BgFar = -1000, BgMid = -900, Landmark = -600, Props = -500, Terrain = -100;
    public const int Npc = 0, Enemy = 10, Boss = 15, Hero = 20, Projectile = 30, Fx = 40;
}
```
材質一律用 SpriteRenderer 預設（URP 2D 下是 `Sprite-Lit-Default`），不要換成 Unlit。UI 用 Screen Space Overlay Canvas，不受這張表影響。

---

## 4. 各角色的公開 API（簽名固定；第 0 波先落地佔位實作）

### 4.1 `player`：`Core/`

```csharp
public static class SidescrollerGame
{
    public const string SceneName = "Sidescroller";
    public static bool IsRunning { get; }
    public static void Boot();                          // 由 GameSelectMenu 呼叫；同步部分失敗時丟例外（不留半套物件）
    public static void Restart();                       // 通關畫面「再玩一次」：Shutdown → Boot
    public static void ReturnToMenu(string error = null); // Shutdown → GameSelectMenu.Show(error)
    public static void ResetStatics();                  // 清本遊戲全部 static（GameSelectMenu.Boot 會呼叫）
}

public static class SsArt
{
    public static bool IsLoaded { get; }
    public static int PlaceholderCount { get; }
    public static UniTask LoadAllAsync(CancellationToken ct);  // 依 §6.4 的清單讀全部 sprite；讀不到的用佔位圖
    public static SpriteSet Get(string location);              // "{Name}_{Action}"；清單外或未讀完：回傳佔位圖並記警告
    public static UniTask<string> LoadTextAsync(string location, CancellationToken ct); // 讀不到丟例外
    public static void ResetCache();
    public static bool IsKnownLocation(string location);         // 是否在 §6.4 清單內（story 的檢查工具用）
}

public static class SsInput
{
    public static float MoveX { get; }                  // -1、0、+1
    public static bool UpHeld { get; }
    public static bool DownHeld { get; }
    public static bool JumpPressed { get; }             // 這一幀按下
    public static bool JumpHeld { get; }
    public static bool MeleePressed { get; }
    public static bool ShootPressed { get; }
    public static bool RollPressed { get; }
    public static bool SwitchCostumePressed { get; }
    public static bool ConfirmPressed { get; }          // Space、Enter、J、手把 South
    public static bool PausePressed { get; }            // Esc、手把 Start
    public static bool MenuUpPressed { get; }           // W、↑、手把上
    public static bool MenuDownPressed { get; }         // S、↓、手把下

    public static bool Simulated;                       // 自動化測試用：true 時改讀下面的 Sim 欄位
    public static float SimMoveX;
    public static bool SimUp, SimDown, SimJump, SimMelee, SimShoot, SimRoll, SimSwitch, SimConfirm, SimPause { get; set; } // 實作為屬性（記下設定的那一幀），呼叫端照樣用賦值
    public static void Reset();                         // 清模擬輸入
}
```
- 鍵位見 `design.md` §7.1。只用 `UnityEngine.InputSystem`（`Keyboard.current`、`Gamepad.current`）；手把只讀按鈕的 `isPressed`／`wasPressedThisFrame`（搖桿用 `leftStick.left/right/up/down` 這些按鈕子控制項），**不呼叫 `ReadValue<T>()`**（§8）。
- `…Pressed` 是 `wasPressedThisFrame`。模擬輸入的按下類欄位（`SimJump`、`SimConfirm`…）只在**設定的那一幀**有效：`SsInput` 記下設定時的 `Time.frameCount`，同一幀內所有讀取者都讀得到，下一幀自動視為 false（不是「誰先讀誰拿到」）。
- `SsArt` 的 **Editor 備援**：`GameAssets` 未就緒、或某個 location 在 AssetPackage 裡讀不到時，`#if UNITY_EDITOR` 下改用 `AssetDatabase` 直接從 `Assets/Art/Sidescroller/` 找同名檔案（只比對 `.png`／`.json`／`.txt`，不要比到 `.meta`；sprite 用 `LoadAllAssetsAtPath` 並依編號排序；文字直接讀檔），並記一次警告。這讓 art／level／story 不必等 assetpkg 就能在 Editor 看到真素材與資料。Player 下沒有備援（sprite 用佔位圖、文字丟例外）。

`Actors/` 沒有給其他角色呼叫的公開 API；對外只透過 §3 的介面（`ICutsceneActor`、`IDamageable`、`ActorTag`、`ActorRegistry`）。

### 4.2 `level`：`Level/`

```csharp
public static class LevelBuilder
{
    // 在 parent 底下建整個關卡：地形 tile＋collider（掛 TerrainTag）、單向平台（PlatformEffector2D）、裝飾、地標、
    // 視差背景、Global Light 2D（整個場景只有這一盞）、觸發區（呼叫 session.NotifyTrigger…）、閘門（依旗標開關）。
    // 不建角色、不建相機、不建 UI。levelText 格式錯誤時丟 FormatException（訊息含行號）。
    public static LevelInfo Build(Transform parent, string levelText, GameSession session, CameraRig rig);
}

public sealed class CameraRig : MonoBehaviour
{
    public static CameraRig Create(Camera camera);          // 掛在 camera 物件上；整局一個，重生不重建
    public Camera Camera { get; }
    public void SetBounds(Rect bounds);                     // 鏡頭邊緣不超出 bounds
    public void Follow(Transform target, bool snap);        // snap=true 立刻到位
    public void LockTo(Rect area);                          // Boss 房：改用 area 當邊界
    public void Unlock();                                   // 回到 SetBounds 的邊界
    public UniTask PanToAsync(Vector2 center, float time, CancellationToken ct); // 停止跟隨，平移到 center
    public UniTask ZoomAsync(float size, float time, CancellationToken ct);
    public void Shake(float strength, float time);
    public void ResetView();                                // Unlock、size 回 SsIds.CameraSize、停止震動、停掉平移；平移停掉後不會自動恢復跟隨，要再呼叫 Follow（GameFlow 建關卡時會呼叫）
}
```
- `CameraRig` 在 `LateUpdate` 移動（主角的 Rigidbody2D 要開 Interpolate，避免抖動）；跟隨帶小死區與往前看（面向那側多看 2 單位）。
- 時間一律用 scaled time（暫停時鏡頭也停）。
- 視差背景由 `Build` 建在 parent 底下，訂閱 `CameraRig` 的 `internal event Action Moved`（鏡頭每次更新位置後觸發）算偏移，`OnDestroy` 取消訂閱；重建關卡時跟著被刪掉。
- 閘門：建立時讀 `session.Flags.Get(SsIds.FlagGateClosed)` 決定初始狀態，並訂閱 `Flags.Changed`（`OnDestroy` 取消）。關上時是實心 collider（`TerrainTag.Solid`）。
- 觸發區：`BoxCollider2D isTrigger`＋level 自己的 MonoBehaviour；只對主角（`ActorTag.Kind == Hero`）**而且 `other.isTrigger == false` 的碰撞體**反應（主角的攻擊判定框、地面偵測器是 trigger，不算）；每個觸發區自己**計數**重疊中的主角碰撞體，0→1 才算進入、1→0 才算離開（滾殼時換碰撞體不會產生多餘的進出）。進入送 `NotifyTriggerEntered`、離開送 `NotifyTriggerExited`。重建關卡時 Physics2D 可能對被關閉的舊觸發區送出離開回呼，level 不必特別擋；流程只對 `Hint` 處理離開（而且只在目前顯示的是同一句時才清），其他種類忽略離開，所以無害。

### 4.3 `story`：`Story/`、`Menu/`

```csharp
public sealed class StoryUi : MonoBehaviour
{
    // 每次 Boot 建一次（重生不重建）。自己建 Screen Space Overlay Canvas：HUD、對話框、字卡、淡入淡出、
    // 暫停選單、通關畫面。建立時畫面是全黑（等流程淡入）。不建 EventSystem，按鍵一律讀 SsInput。
    public static StoryUi Create(GameSession session, CameraRig rig);
    public bool IsCutscenePlaying { get; }
    public UniTask<bool> PlayCutsceneAsync(string location, CancellationToken ct); // false＝讀取或解析失敗（已記錯誤）
    public UniTask FadeAsync(float to, float time, CancellationToken ct);           // to：0 透明～1 全黑
    public void ShowBanner(string text, float time);
}

public static class GameSelectMenu
{
    public static void Boot();                      // 只由 AppBootstrap.Run() 呼叫（§7.1）
    public static void Show(string error = null);   // 顯示選單；error 不為 null 時在選單上顯示「啟動失敗：…」
}
```
- `PlayCutsceneAsync` 的行為（`SetCutsceneActive`、輸入鎖延後一幀解除、鏡頭還原、跳過）見 `cutscene-format.md` §4。
- HUD 顯示：心（`HpChanged`）、已取得的攻擊圖示與目前服裝（`Loadout.Changed`）、兩套以上時顯示「C 換制服」、提示文字（`HintChanged`；演出中隱藏）、Boss 血條（`BossChanged`，第 2 階段變紅＝血量 ≤ 一半）。
- 暫停選單（Esc）：繼續／跳過演出（只在可跳過的演出中出現）／從檢查點重來（`session.RequestRespawn()`；**演出中不出現**）／回到選擇遊戲（`SidescrollerGame.ReturnToMenu()`）。開啟時 `session.SetPaused(true)`，關閉時 false。暫停選單自己用 unscaled time。通關畫面出現後不能暫停。
- 通關畫面（`StageCleared`）：畫在淡入淡出遮罩**之上**（結局演出把遮罩留在全黑）；「急件送達！」、通關時間 mm:ss、再玩一次（`SidescrollerGame.Restart()`）、回到選擇遊戲（`SidescrollerGame.ReturnToMenu()`）。

---

## 5. 呼叫順序與誰建立誰（`player` 的 `GameFlow` 負責組裝）

### 5.1 開機

1. Entry 載完 dll → `AppBootstrap.Run()` → `GameSelectMenu.Boot()`（story）：
   清自己的 static、呼叫 `SidescrollerGame.ResetStatics()`、`Time.timeScale = 1`、隱藏 Entry（§7.2）、`Show()`。
2. 選「新作」→ 選單關閉自己 → `SidescrollerGame.Boot()`（player）：
   1. `ResetStatics()`（`SsArt.ResetCache()`、`SsInput.Reset()`、`ActorRegistry.Clear()`）、`Time.timeScale = 1`。
   2. 建執行期場景 `Sidescroller`（同名還在時加編號）並設為 active；之後所有 `new GameObject` 都進這個場景。
   3. 建相機（正交、size `SsIds.CameraSize`、背景色＝夕陽上緣的暮紫）、`CancellationTokenSource`、`GameFlow` 物件。
   4. 任一步丟例外：刪掉已建的東西、卸載場景、往外丟（選單接住並顯示錯誤）。
3. `GameFlow` 非同步初始化（帶 `session.Lifetime`）：
   `await SsArt.LoadAllAsync()` → `levelText = await SsArt.LoadTextAsync(SsIds.LevelStage1)` → `session = new GameSession(token)` →
   `rig = CameraRig.Create(camera)` → `story = StoryUi.Create(session, rig)` → `BuildWorld("cp0")`。
   失敗（例如關卡資料讀不到或格式錯誤）：記錯誤 → `SidescrollerGame.ReturnToMenu("…")`。

### 5.2 建關卡（第一次與每次重生）

GameFlow 除了 `session.Lifetime`，還有一個**「這條命」的 token**：每次建關卡時新開一個與 `Lifetime` 連結的 `CancellationTokenSource`（`lifeCts`），重生前取消它。演出（`PlayCutsceneAsync`）、鏡頭（`PanToAsync`／`ZoomAsync`）、Boss 房處理等**屬於這條命的 await 一律帶 `lifeCts.Token`**；被取消時安靜結束，不再碰舊關卡的物件。

1. `session.PushInputLock()`；取消舊的 `lifeCts`、**清空觸發佇列**、關閉「處理觸發」開關；`ActorRegistry.Clear()`；舊 worldRoot 先 `SetActive(false)` 再 `Destroy`（Destroy 要到這一幀結束才生效，先關掉才不會和新關卡的碰撞與觸發區同時存在）；`session.Flags.ClearTemporary()`；開新的 `lifeCts`。
2. `worldRoot = new GameObject("World")`；`info = LevelBuilder.Build(worldRoot.transform, levelText, session, rig)`。
3. 在 `info.FindCheckpoint(目前存檔點)` 建主角；依 `info.Spawns` 建雜兵、Boss、NPC（全部掛在 worldRoot 底下，各自 `Register`）。**Boss 建好時 AI 是停的**，只有 5.3 的 BossArena 處理會啟動它。
4. `session.SetHp(max, max)`、`session.SetBoss(false, …)`、`session.SetHint(null)`；`rig.ResetView()`、`rig.SetBounds(info.CameraBounds)`、`rig.Follow(主角, snap: true)`。
5. `await story.FadeAsync(0, 0.5, lifeCts.Token)` → `session.PopInputLock()` → **打開「處理觸發」開關**。這之前收到的觸發（例如主角一出生就站在 `trg_intro` 裡）先排在佇列，開關打開後才依序處理。

**誰建立**：相機、`GameSession`、主角、雜兵、Boss、NPC、子彈與特效＝player；地形、裝飾、背景、Global Light 2D、觸發區、閘門＝level；Canvas 與所有 UI＝story；選單自己的場景、相機、Canvas＝story。

### 5.3 觸發區處理（GameFlow 依序處理，一次一件；演出中進來的觸發排隊）

`Hint` 的進入與離開立即生效（不排隊）；其他種類進佇列，「處理觸發」開關打開時一件一件處理。

| Kind | 處理 |
|---|---|
| `Cutscene` | 若 `seen_<Arg>` 已成立就忽略。否則 `await story.PlayCutsceneAsync(Arg, lifeCts.Token)` → 設 `seen_<Arg>`（被取消則不設）。`Arg == SsIds.CutsceneMentor` 時，播完（成功或失敗都算，被取消不算）**保底**檢查：沒有 Sling／Roll／Express 就補發並換上 Express（記警告），避免演出腳本壞掉讓遊戲卡關 |
| `Hint` | 進入：`session.SetHint(Arg)`；離開：目前顯示的是這句才 `SetHint(null)` |
| `Checkpoint` | 目前存檔點＝`Arg`（只往前，不會回到較早的存檔點） |
| `BossArena` | 每條命只處理一次。沒看過 `Arg` 演出：播演出（腳本會設閘門旗標）→ 設 `seen_`；看過：直接 `Flags.Set(FlagGateClosed)`。然後確保閘門旗標成立 → `rig.ZoomAsync(ArenaCameraSize, …, lifeCts.Token)`、`rig.LockTo(info.ArenaRect)` → 啟動 Boss AI → `session.SetBoss(true, 40, 40, SsIds.BossDisplayName)`。每個 await 之後都檢查 `lifeCts` 沒被取消才往下 |
| `KillZone` | 主角扣 1 心並回到最後站穩的位置（血量歸零則照死亡處理）。主角在「掉坑回位」處理中時忽略重複的 KillZone |

### 5.4 死亡、重生、通關、暫停、離開

- **死亡**（血量 0）／**從檢查點重來**（`RespawnRequested`）：`PushInputLock` → 取消 `lifeCts`（進行中的演出會收尾並放開鎖） → `await story.FadeAsync(1, 0.5, session.Lifetime)` → `PopInputLock` → 回到 5.2（從目前存檔點）。已取得的能力、非 `tmp_` 旗標保留。
- **Boss 被擊敗**：停 Boss AI、主角無敵、`session.PushInputLock()`（**之後不再 Pop**，通關畫面接手）→ 等主角落地（最多 1.5 秒）→ `session.SetBoss(false, …)` → `await story.PlayCutsceneAsync(SsIds.CutsceneBossDefeat, lifeCts.Token)` → `session.NotifyStageCleared(通關秒數)`。通關秒數＝Boot 後累計的 scaled 時間（暫停不計）。演出設了 `keepFade`，遮罩停在全黑；**通關畫面畫在遮罩之上**（story 負責排序）。
- **暫停**：`PausedChanged(true)` → `Time.timeScale = 0`；false → 1。所有角色在 `session.InputLocked` 時不讀輸入；`CutsceneActive` 時敵人與 Boss 停止 AI。story 關閉暫停選單時**延後一幀**才 `SetPaused(false)`，讓選「繼續」的那次 Space／J 不會被主角或對話框吃到。
- **回到選擇遊戲／再玩一次**（`SidescrollerGame.Shutdown`，私有）：取消 `Lifetime` 的 `CancellationTokenSource`（連帶取消 `lifeCts`；所有 await 收到 `OperationCanceledException` 要安靜結束，不記錯誤）→ `Time.timeScale = 1` → `ActorRegistry.Clear()` → `Sidescroller` 場景的所有根物件 `SetActive(false)` → `SceneManager.SetActiveScene(Entry 場景)` → `UnloadSceneAsync`（非同步，先關掉根物件才不會和下一個場景的相機、Canvas、Global Light 2D 並存）→ `ResetStatics()`。然後 `ReturnToMenu` 呼叫 `GameSelectMenu.Show(error)`，`Restart` 呼叫 `Boot()`。
- **`Restart()` 自己 catch**：`Boot()` 同步丟例外時記錯誤並改呼叫 `ReturnToMenu(訊息)`（呼叫端是通關畫面的 Update，沒有人能接住例外）。`ReturnToMenu` 裡 `GameSelectMenu.Show` 再失敗時只記錯誤（畫面停在 Entry 已隱藏的黑畫面，log 看得到原因）。

---

## 6. 素材與資料：命名、收集路徑、AssetPackage 分組

### 6.1 資料夾

```
Assets/Art/Sidescroller/
  Sprites/<Name>/<Name>_<Action>.png       顏色 sheet（art）
  Sprites/<Name>/<Name>_<Action>_n.png     法線 sheet（art）
  Sprites/<Name>/<Name>_sprites.json       sheet 說明（art）
  Data/Cutscenes/SsCutscene_<Id>.json      演出腳本（story）
  Data/Levels/SsLevel_<Id>.txt             關卡資料（level）
```
- `Sprites/` 與 `Data/` 根目錄**只能放子資料夾**，子資料夾名稱不能有 `.`（YooAsset `PackTopDirectory` 的限制）。
- `<Name>` 一律 `Ss` 開頭、PascalCase、**不含底線**；Action 也不含底線。location＝`<Name>_<Action>`。
- 整個 AssetPackage 的檔名（不含副檔名）必須唯一：Glowshaft 沒有 `Ss` 開頭的檔，所以加前綴就不會撞名。
- 收集路徑裡不要放 README 或其他檔案（會被收進包）；說明寫在 `Tools/Blender/Sidescroller/README.md` 或 `Docs/sidescroller/`。

### 6.2 sprite sheet 的 json 欄位（沿用 Glowshaft，匯入工具共用）

| 欄位 | 型別 | 說明 |
|---|---|---|
| `name` | string | `<Name>` |
| `cellSize` | int | 每格 px（正方形） |
| `directions` | string[] | 側視只有一個方向，`render_sprites.py --dirs 1` 輸出 `["a000"]` |
| `fps` | number | 12 |
| `suggestedPixelsPerUnit`、`orthoScale`、`elevation`、`albedo` | — | 渲染器輸出，保留 |
| `pivot` | [x, y] | 0～1，左下為 (0,0)；角色腳底、子彈與 tile 中央（`design.md` §8.1） |
| `sheets` | [{`action`, `columns`, `rows`, `color`, `normal`}] | 單方向時 `rows = 1`、`columns`＝幀數 |
| `pixelsPerUnit` | int（必填） | 64 |
| `tile` | bool | 可平鋪（`SsTiles`、`SsBgFar`、`SsBgMid`），匯入用 FullRect mesh |

sprite 名稱＝`<Name>_<Action>_<編號>`，編號＝第幾幀（單列）。`SsArt` 只用 regex 讀 `fps`，幀數以實際 sprite 數為準。

### 6.3 AssetPackage 分組（`assetpkg`）

仍是同一個 package **`AssetPackage`**（`GameAssets` 只認這個），新增兩個 group：

| Group | CollectPath | PackRule | AddressRule | Filter | 結果 |
|---|---|---|---|---|---|
| `glowshaft_sprites`（既有，不動） | `Assets/Art/Glowshaft/Sprites` | PackTopDirectory | AddressByFileName | CollectAll | 每個 `<Name>` 一包 |
| `sidescroller_sprites` | `Assets/Art/Sidescroller/Sprites` | PackTopDirectory | AddressByFileName | CollectAll | 每個 `<Name>` 一包，改一個角色只重下那一包 |
| `sidescroller_data` | `Assets/Art/Sidescroller/Data` | PackTopDirectory | AddressByFileName | CollectAll | `Cutscenes`、`Levels` 各一包，改劇本只重下演出包 |

- **新 group 只在收集路徑存在時才加入**（不存在就記警告並略過）：art 或 story／level 的資料還沒落地時，Glowshaft 照樣能打包與在 Editor 模擬。
- 打包前檢查擴大到新的根目錄：根目錄不放檔案、子資料夾名不含 `.`、全 package 檔名唯一、Sidescroller 的檔名都以 `Ss` 開頭；略過以 `.` 開頭或 `~` 結尾的檔案與資料夾（art 的 `.staging/`）。
- `AssetPackageBuildTool` 打包前會自己跑 `AssetPackageCollectorSetup.Setup()`，但 **Editor 模擬模式讀的是已存檔的 `Assets/BundleCollectorSetting.asset`，不會自動重跑**：Sidescroller 的資料夾第一次出現後要重跑一次選單 `Tools/YooAsset/建立或更新 AssetPackage 收集器設定`（assetpkg 交差前跑、協調整合前再跑一次）。`BundleCollectorSetting.asset` 只由這個 Setup 改寫，歸 assetpkg。
- 驗收：Editor 模擬模式從 Entry 進新作，`SsArt` 不走 Editor 備援（log 沒有備援警告）；`OneClickBuildAssetPackage` 成功，清單裡有全部 `Ss*` location。

### 6.4 `SsArt` 的素材清單（player 寫進程式；art 照這張交貨）

佔位圖：素材還沒到時，`SsArt` 依下表的大小與顏色產生純色方塊（pivot 同正式素材），**換上真素材不用改程式**。

| Name | Actions | 格子 px | 佔位大小（單位） | 佔位色 |
|---|---|---|---|---|
| `SsHeroDay` | Idle, Run, Jump, Melee, Shoot, Hurt | 128 | 0.9×1.5 | 紅 |
| `SsHeroExpress` | Idle, Run, Jump, Melee, Shoot, Hurt | 128 | 0.9×1.5 | 橘 |
| `SsMentor` | Idle, Talk | 128 | 1.0×1.2 | 綠 |
| `SsRat` | Idle, Walk, Attack, Hurt | 128 | 0.7×1.4 | 灰 |
| `SsPigeon` | Fly, Attack, Hurt | 128 | 0.9×0.9 | 淺藍 |
| `SsRhino` | Idle, Walk, Attack, Hurt | 160 | 1.4×1.6 | 深藍 |
| `SsBoss` | Idle, Charge, Spray, Stomp, Hurt | 320 | 3.2×4.0 | 紫 |
| `SsParcel` | Spin | 32 | 0.35×0.35 | 棕 |
| `SsCoin` | Spin | 32 | 0.35×0.35 | 金 |
| `SsBomb` | Fall | 32 | 0.4×0.4 | 白 |
| `SsShock` | Move | 64 | 0.8×0.8 | 黃 |
| `SsFx` | Hit, Poof, Dust, Clank | 128 | 1.0×1.0 | 白 |
| `SsTiles` | Idle（7 變化） | 64 | 1×1 | 依變化不同灰階 |
| `SsProps` | Idle（6 變化） | 256 | 1×3 | 暗綠 |
| `SsLandmark` | Idle（3 變化） | 512 | 6×6 | 暗紅 |
| `SsBgFar` | Idle（1） | 1024 | 16×16 | 暮紫 |
| `SsBgMid` | Idle（2 變化） | 1024 | 16×16 | 深藍 |
| `SsUi` | Idle（8 變化） | 64 | 1×1 | 粉紅 |
| `SsPortraitHero`、`SsPortraitBoss`、`SsPortraitMentor`、`SsPortraitRat` | Face（4 表情） | 256 | 4×4 | 各自主色 |

- 變化（variant）的意義見 `design.md` §8.4、§8.5；立繪表情 0 平常、1 得意、2 生氣、3 驚訝。
- 多變化素材的佔位圖要產生對應數量的幀（例如 `SsTiles_Idle` 7 幀），`Variant(n)` 才拿得到。
- art 想增減動作或變化：先改這張表（找 design／協調），不要只改素材。

### 6.5 關卡資料格式（`SsLevel_Stage1.txt`，`level` 擁有）

只有 level 讀寫，這裡定大方向，細節由 level 補在 `Level/` 的程式註解與交接檔。用純文字＋區段，**不用 json**（地圖本身就是字元格，比 json 好讀好改）。

```
# 註解以 # 開頭
[meta]
cameraBounds=0,0,214,16        # x,y,w,h
arena=190,0,24,13.5
killY=-3
[map]
（16 行，第一行是 y=15、最後一行是 y=0；每字元＝1 單位；圖例同 design.md §6.3：# = x ^ 空白）
[entities]
# kind id x y facing
rat rat_intro 13 2 -1
npc mentor 76 2 -1
boss boss 209 2 -1
gate gate 189 2 7               # 閘門：level 自己處理，不進 LevelInfo.Spawns（最後一欄＝高度）
[checkpoints]
cp0 3 2
[triggers]
# kind id x y w h arg（arg 到行尾，可含空白）
cutscene trg_intro 0 0 7 16 SsCutscene_Intro
hint hint_1 0 0 13 16 A／D 或 ←／→：移動　J：甩包攻擊
checkpoint trg_cp1 74 0 2 16 cp1
bossArena trg_boss 192 0 2 16 SsCutscene_BossIntro
killzone pit_1 26 -6 3 6
[decor]
# name variant x y（SsProps／SsLandmark，純裝飾）
SsLandmark 1 12 2
```
- 內容照 `design.md` §6.3、§6.4。`[map]` 的 `|` 不用（閘門由 `[entities]` 的 gate 建）。
- 地形合併：連續的實心格合成一個 `BoxCollider2D`（或 `CompositeCollider2D`），避免角色卡在 tile 接縫。

---

## 7. 遊戲選擇選單與兩款遊戲並存

### 7.1 接進 `AppBootstrap.Run()`

```csharp
public static void Run()
{
    Debug.Log(…原本的 log…);
    Sidescroller.GameSelectMenu.Boot();
}
```
`GameSelectMenu.Boot()`（story）：
1. 清自己的 static（隱藏紀錄、選單實例、字型快取）、呼叫 `SidescrollerGame.ResetStatics()`、`Time.timeScale = 1`。
2. 隱藏 Entry（7.2），建選單（7.3）。
3. 這兩步中任何一步丟例外：還原 Entry、刪掉半套選單，**往外丟**（EntrySceneController 會在畫面顯示「遊戲模組代碼執行失敗」）。

### 7.2 Entry 場景的隱藏與還原（選單負責）

照 `GlowshaftGame.HideEntryScene` 的做法：關掉 Entry 根物件裡有 Camera／Canvas／Light／Light2D 的（`SetActive(false)`），並把 `EntrySceneController` 元件 `enabled = false`（停掉它的 OnGUI 狀態文字），都記下來。**Entry 場景本身不卸載**（中文字型在它的除錯主控台 prefab 裡，§7.4）。
之後兩款遊戲都在 Entry 已隱藏的前提下啟動：Glowshaft 的 `HideEntryScene` 會因為物件已關閉而什麼都不做，這是預期行為，不用改 Glowshaft。

### 7.3 選單內容與切換

- 選單建自己的執行期場景 `GameSelect`（設為 active）、自己的相機（黑底）與 Overlay Canvas；字型用思源黑體（7.4）。
- 項目：**目前只有新作《熱浪急件》**（2026-10-28 使用者決定）；列哪些遊戲由 `GameSelectMenuView.Choices` 決定，螢火礦坑要放回時加 `GameSelectMenu.ChoiceGlowshaft`。提示「↑↓：選擇　Enter：確定」。
- **選單、暫停選單、通關畫面出現的那一幀不處理輸入**（從下一幀開始）：例如在暫停選單按 Space 選「回到選擇遊戲」，同一幀的 `ConfirmPressed` 不能讓新出現的選單直接選中第一項。滑鼠點擊用 `RectTransformUtility.RectangleContainsScreenPoint` 自己判斷，**不建 EventSystem**；鍵盤讀 `SsInput.MenuUp/Down/ConfirmPressed`。
- 選定後：選單場景的根物件全部 `SetActive(false)` → `SceneManager.SetActiveScene(Entry 場景)`（**一定要**：`GlowshaftGame.Boot` 用 `GetActiveScene()` 當成 Entry）→ `UnloadSceneAsync(GameSelect)` → 再呼叫遊戲的 Boot，**用 try/catch 包住**：
  - 新作：`SidescrollerGame.Boot()`；
  - 螢火礦坑：`Glowshaft.GlowshaftGame.Boot()`。
  - 丟例外：記錯誤 → `Show("…例外訊息…")`（Entry 保持隱藏）。
- **返回選單**：只有新作支援（暫停選單、通關畫面、初始化失敗）。螢火礦坑保留不動，**進去後不能返回**（Glowshaft 的 `GameManager` 也不允許同一次 Play 重複啟動，所以選單在螢火礦坑啟動成功後就不再出現）。要支援返回得改 Glowshaft，列為使用者待決事項。

### 7.4 中文字型

- 思源黑體 TMP 字型 `Assets/Plugins/IngameDebugConsole/Fonts & Materials/Chinese/思源黑體-Medium.asset`（Static、16264 字）被 Entry 場景裡的 `IngameDebugConsoleV2` prefab 引用，所以**執行期已經載入**，不用改 `GameAssets`、不用放進 AssetPackage。
- story 用 `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()` 找名稱 `思源黑體-Medium` 的那一個，快取起來（Boot 時清快取）。找不到：`Debug.LogError` 說明原因（Entry 場景的除錯主控台被拿掉了？），改用 TMP 預設字型（中文會變方塊，問題看得出來）。
- 文字元件用 `TextMeshProUGUI`（`TMPro`，屬於 `com.unity.ugui` 2.0，AOT 端組件）。
- 劇本與介面用字已全部在字集內（`design.md` §5）；story 交一個 Editor 檢查工具（7.5）防止日後改劇本出現缺字。

### 7.5 清理責任總表

| 時機 | 誰 | 清什麼 |
|---|---|---|
| `GameSelectMenu.Boot()`（每次進 Play 的第一個呼叫） | story | 選單 static、字型快取；呼叫 `SidescrollerGame.ResetStatics()`；`Time.timeScale = 1` |
| 選單關閉（選了遊戲） | story | 選單物件、`GameSelect` 場景（Entry 保持隱藏） |
| `SidescrollerGame.Boot()` | player | `SsArt.ResetCache()`、`SsInput.Reset()`、`ActorRegistry.Clear()`、`Time.timeScale = 1` |
| 重生（重建關卡） | player | `ActorRegistry.Clear()`、`Destroy(worldRoot)`、`Flags.ClearTemporary()`；level／player 物件在 `OnDestroy` 取消對 session 的訂閱 |
| Shutdown（回選單、再玩一次） | player | 取消 token、`Time.timeScale = 1`、`ActorRegistry.Clear()`、卸載 `Sidescroller` 場景、`ResetStatics()` |
| 其他 | level、story | **不留 static 狀態**（`GameSelectMenu` 除外）；需要的狀態放在實例上，跟著物件消失 |

**Domain Reload 關閉**：不要依賴 `[RuntimeInitializeOnLoadMethod]` 清 Sidescroller 的 static——Player 下遊戲模組組件是開機後才由 HybridCLR 載入，這類回呼的時機已經過了，Editor 和 Player 行為會不一致。一律由上表的明確呼叫清理。

---

### 7.6 網頁版（WebGL，2026-10-28 追加）

- Entry（AOT 端）的 `CodePackageLoader`、`AssetPackageLoader`、`EntrySceneController` 有 `UNITY_WEBGL` 分支：YooAsset 用 WebPlayMode＋WebNetworkFileSystem，下載失敗有重試上限（`WebDownloadRetryPolicy`）；其他平台行為不變。
- 出包：`Tools/WebGL/一鍵出 WebGL 包`（`WebGLBuildTool.OneClickBuildWebGL`），必須在 active target＝WebGL 下出；本機跑 `Tools/WebGL/serve.py`。
- 遊戲模組代碼不需要為 WebGL 改動。詳細的部署注意事項見看板 `handoff-webgl.md`。

## 8. HybridCLR 與 AOT 泛型（各角色寫程式時遵守，新用到的列進交接檔）

- 場景與資源包**不序列化引用** Assembly-CSharp 型別：所有物件 `new GameObject`＋`AddComponent`；不用 Timeline、不用 prefab、不用 ScriptableObject 資產。
- 素材與資料只用 `Sprite`、`Texture2D`、`TextAsset`，透過 `SsArt`（內部用 `EntryScene.GameAssets`）讀；**禁止 `using YooAsset`**。
- **避開值型別泛型的新實例化**：不用 `HashSet<enum>`、`Dictionary<enum, …>`、`Dictionary<…, struct>`、ValueTuple、`List<自訂 struct>`；改用陣列（以 `(int)enum` 當索引）、`List<參考型別>`、`Dictionary<string, 參考型別>`。
- Input System：只讀 `isPressed`／`wasPressedThisFrame`，不呼叫 `ReadValue<T>()`（Glowshaft 交接記錄過 `InputControl<Vector2>.ReadValue` 不在補充元數據清單）。
- 已知會出現、要協調確認補充元數據的：`Action<int, int>`（Glowshaft 已用過）、`UniTask<bool>`、`UniTask<string>` 與 Assembly-CSharp 裡的 `async UniTask` 狀態機（Glowshaft 已用過 `async UniTask`）、`Action<string, bool>`、`Action<bool>`、`Action<float>`、`Action<TriggerInfo>`、`Resources.FindObjectsOfTypeAll<TMP_FontAsset>()`、`GetComponentInParent<ActorTag>()`／`<IDamageable>()`（參考型別，風險低）。
- **IL2CPP 裁剪**：只有遊戲模組代碼用到的 TMP、uGUI、Physics2D 成員（例如 `TextMeshProUGUI`、`PlatformEffector2D`、`CompositeCollider2D`）在 AOT 端可能沒人引用而被裁掉，Player 下會找不到。各角色在交接檔列出用到的這類 Unity 型別，由協調決定是否補 `link.xml`。
- 演出腳本用 story 自寫的小型 json 解析器（理由見 `cutscene-format.md` §5）；sprite 的 json 用 regex 讀 `fps`（同 Glowshaft）；關卡用純文字。**整款遊戲不用 `JsonUtility` 反序列化遊戲模組型別。**

---

## 9. 落地順序與佔位策略（讓五個角色平行開工、互不阻塞）

### 第 0 波：骨架（協調在開子 session 前落地，約 20 分鐘）

協調照本文件建立下列檔案並編譯到 0 錯誤，之後各檔歸屬照 §1：

| 檔案 | 內容 |
|---|---|
| `Contracts/*.cs` | §3 全部，**完整實作**（都很小） |
| `Core/SidescrollerGame.cs` | 簽名＋最小實作：建場景與相機、印 log；`GameFlow` 由 player 補 |
| `Core/SsArt.cs` | 簽名＋`Get` 只回佔位圖；`LoadTextAsync` 先試 `GameAssets.LoadTextAsync`，失敗時 Editor 直接讀 `Assets/Art/Sidescroller/Data/**/<location>.*`（§4.1 備援的文字部分，十幾行），Player 下丟例外。story、level 才能在 player 完成前讀到自己的資料 |
| `Core/SsInput.cs` | 完整（鍵盤部分很短） |
| `Level/LevelBuilder.cs`、`Level/CameraRig.cs` | 簽名＋最小實作：Build 回傳一個空的 `LevelInfo`（含 `cp0`）；CameraRig 只做跟隨，其他方法立刻完成 |
| `Story/StoryUi.cs` | 簽名＋最小實作：`PlayCutsceneAsync` 立刻回 true、`FadeAsync` 立刻完成 |
| `Menu/GameSelectMenu.cs` | 簽名＋最小實作（見下方「開發期入口」） |
| `Assets/Scripts/Bootstrap/AppBootstrap.cs` | 改成呼叫 `Sidescroller.GameSelectMenu.Boot()`（§1.1、§7.1） |

**開發期入口**（讓 player、level、story 在真選單完成前就能「從 Entry 進 Play」測新作）：骨架的 `GameSelectMenu.Boot()` 先照 §7.2 隱藏 Entry，然後
`#if UNITY_EDITOR` 讀 `EditorPrefs.GetBool("Sidescroller.DevBoot")`：true＝直接 `SidescrollerGame.Boot()`；false（預設）或 Player＝`Glowshaft.GlowshaftGame.Boot()`（行為和現在一樣）。
測試用的 `-executeMethod` 腳本進 Play 前自己把這個 EditorPrefs 設成 true、測完設回 false。story 的真選單**保留這個開關**（為 true 時略過選單直接進新作），方便日後自動化測試；它只在 Editor 生效。

### 第 1 波：五個角色同時開工

| 角色 | 先做（讓別人早點拿到） | 再做 | 自己怎麼測（不等別人） |
|---|---|---|---|
| art | ① 匯入工具支援第二個根目錄 ② `SsHeroDay` 全套（最多人要看） | `SsTiles`、`SsRat`、`SsBoss` → 其餘角色 → 背景、地標、立繪、UI | `render_all.py` 重做、匯入驗證、總覽截圖 |
| assetpkg | 收集器兩個 group（路徑不存在就略過） | 打包前檢查、模擬模式驗證、打包 | **不要**在別人的收集路徑放測試檔；用暫時的獨立資料夾（例如 `Assets/Art/_SsPkgTest/`）加暫時的 collector 驗證規則，測完連設定一起刪掉；最後等真資料到齊再驗一次 |
| player | `SsArt`（含佔位與 Editor 備援）、`GameFlow` 組裝、主角移動與跳躍 | 三種攻擊、換裝、雜兵、Boss、NPC、`ICutsceneActor` | 第 0 波的平地＋佔位素材就能測手感；level 真關卡到了再接 |
| level | 關卡資料 `SsLevel_Stage1.txt`、`LevelBuilder` 地形與觸發區 | `CameraRig` 全功能、視差背景、閘門、裝飾 | 暫時的 Editor 腳本直接呼叫 `Build`，用一個假的跟隨目標截圖 |
| story | 演出解析器與 `PlayCutsceneAsync`、對話框、字型 | HUD、暫停、通關、遊戲選擇選單（保留開發期開關）、Editor 檢查工具 | 自己寫假的 `ICutsceneActor`（暫時檔，測完刪）跑四份腳本 |

規則：
- 只替換**自己的**骨架檔；公開簽名不變，可以自由加私有型別與新檔。
- 用到別人尚未完成的 API 時，照骨架的佔位行為寫，不要自己補別人的實作。
- 暫時的測試腳本放在自己資料夾的 `Editor/` 子資料夾（例如 `Assets/Scripts/Sidescroller/Level/Editor/`），測完刪掉。
- 所有角色共用 Assembly-CSharp：**存檔前先確認能編譯**，大改先在 scratchpad 寫好再一次放進來；Unity 鎖照 `RULES.md`。

### 第 2 波：整合（協調）

從 Entry 進選單 → 新作 → 全關卡走一遍（含死亡重生、跳過演出、暫停回選單、再玩一次、回選單後改玩螢火礦坑）。只審跨邊界。

### 依賴一覽（誰等誰，只在整合時才需要）

```
art ─素材→ SsArt（player） ─SpriteSet→ level、story、player
assetpkg ─AssetPackage 分組→ （Editor 備援讓大家不必等它）
level ─LevelInfo、觸發區、CameraRig→ player 的 GameFlow
story ─StoryUi、選單→ player 的 GameFlow；player ─SidescrollerGame→ story 的選單
player ─ICutsceneActor、GameSession→ story 的演出
```

---

## 10. 各角色交差時的驗收重點（自審與 subagent 反向驗證時對照）

- **player**：重生三次後 `ActorRegistry` 沒有殘留；演出播到一半、Boss 房鏡頭拉遠到一半時重生，舊演出被取消、新關卡不受影響；演出中按 Space 結束最後一句，主角**不會**跟著跳（輸入鎖延後一幀）；在空中被觸發演出時能正常落地；Boss 兩階段循環、震波可跳過；換裝時動畫不中斷；回選單後再進遊戲，static 都是新的。
- **level**：關卡與 `design.md` §6.3 草圖逐格一致；角色在地形接縫不會卡；單向平台可以下跳；低矮通道只有滾殼過得去；閘門旗標開關正確；觸發區只對主角反應。
- **story**：四份腳本全部跑完、跳過、中途回選單都不會留下黑幕或鎖住輸入；`grant` 後 HUD 更新；字型找得到；Editor 檢查工具能抓出缺字與未知指令。
- **art**：每個 location 的幀數、pivot、PPU 符合 §6.4；兩套主角 sheet 幀數一致；Glowshaft 素材匯入結果不變。
- **assetpkg**：Sidescroller 資料夾不存在時 Glowshaft 照樣能打包；全部 `Ss*` location 進包；`.staging` 等暫存不進包。
