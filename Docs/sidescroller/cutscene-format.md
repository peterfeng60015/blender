# 演出腳本 json 格式（`SsCutscene_*.json`）

作者：`design`。實作與資料歸 `story`（`Assets/Scripts/Sidescroller/Story/**`、`Assets/Art/Sidescroller/Data/Cutscenes/**`）。
型別與呼叫順序見 `interfaces.md`；劇本內容見 `design.md` §5；四份範例在本資料夾的 `cutscenes/`，story 落地時原樣複製到 `Assets/Art/Sidescroller/Data/Cutscenes/`。

---

## 1. 檔案與載入

- 位置：`Assets/Art/Sidescroller/Data/Cutscenes/SsCutscene_<Id>.json`，UTF-8（無 BOM 或有 BOM 都要能讀）。
- 進 AssetPackage 的 `sidescroller_data` group，location＝檔名不含副檔名（例如 `SsCutscene_Intro`）。
- 讀取：`SsArt.LoadTextAsync(location, ct)`（Editor 下讀不到 AssetPackage 時會直接讀檔）。
- **以 `Assets/Art/Sidescroller/Data/Cutscenes/` 那份為準**（歸 story）。`Docs/sidescroller/cutscenes/` 是設計定稿時的範本，story 落地時原樣複製過去；之後劇本有改動，story 在交接檔列出，由協調通知 design 同步 `design.md` 的劇本全文。
- 每次 `PlayCutsceneAsync` 都重新讀與解析（檔案很小；之後透過資源包更新劇本時不必重開遊戲流程）。

## 2. 頂層欄位

```json
{
  "id": "SsCutscene_Intro",
  "version": 1,
  "skippable": true,
  "keepFade": false,
  "speakers": { "hero": { "name": "阿甲", "portrait": "SsPortraitHero_Face", "side": "left" } },
  "commands": [ { "op": "say", "who": "hero", "text": "……" } ]
}
```

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `id` | string | 否 | — | 應等於檔名；不一致時記警告（不影響執行） |
| `version` | int | 否 | 1 | 格式版本；大於執行器支援的版本時記警告，照樣嘗試執行 |
| `skippable` | bool | 否 | true | false 時暫停選單不出現「跳過演出」 |
| `keepFade` | bool | 否 | false | 演出結束時畫面若不是透明：false＝自動淡回 0（0.3 秒，記警告提醒腳本漏寫）；true＝保持（例如結局淡到全黑後接通關畫面） |
| `speakers` | object | 否 | 空 | 說話者表，key＝`say` 的 `who` |
| `commands` | array | **是** | — | 依序執行的指令 |

### 2.1 說話者（`speakers` 的值）

| 欄位 | 型別 | 預設 | 說明 |
|---|---|---|---|
| `name` | string | key 本身 | 對話框上顯示的名字；空字串＝不顯示名牌 |
| `portrait` | string | 無 | 立繪 location（`<Name>_Face`），表情用 `Variant(n)` 取；沒有就不顯示立繪 |
| `side` | `"left"`／`"right"` | `"left"` | 立繪在對話框哪一側 |
| `style` | string | `"normal"` | 這個說話者的預設樣式（見 `say`） |

## 3. 指令

每個指令是一個物件，`op` 決定種類。所有秒數都是 **scaled time**（暫停時演出也停）。
「阻塞」＝執行器等這個指令做完才往下；有 `wait` 欄位的指令可以改成不等。

### `say`：對話（阻塞）

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `who` | string | **是** | — | 說話者 key；不在 `speakers` 裡時名字顯示 key 本身、沒有立繪（記一次警告） |
| `text` | string | **是** | — | 台詞；`\n` 換行 |
| `face` | string | 否 | `"normal"` | `normal`／`happy`／`angry`／`surprised` → 立繪第 0／1／2／3 格；不認得的值用 0 並記警告 |
| `style` | string | 否 | 說話者的 `style` | `normal`：底部對話框＋立繪＋名牌。`screen`：立繪放進電視框（其餘裝飾由 UI 決定），不播 `Talk`。`offscreen`：沒有立繪、名牌照常（畫外音）。`narration`：沒有名牌與立繪，文字置中偏斜體 |
| `side` | string | 否 | 說話者的 `side` | 覆寫立繪位置 |
| `actor` | string | 否 | 同 `who`（只在 `style` 是 `normal` 時） | 說話時要播 `Talk` 的場上角色 id。`style` 不是 `normal` 時，沒寫 `actor` 就**完全不碰角色**（不算「用到」，不呼叫 `BeginScripted`）；找不到角色或角色沒有 `Talk` 就不播 |
| `speed` | number | 否 | 30 | 逐字顯示速度（字／秒）；0＝一次顯示全部 |
| `auto` | number | 否 | 無 | 有值時：全文顯示後再等這麼多秒自動進下一句（按確定仍可提早） |

- 按確定（`SsInput.ConfirmPressed`）：逐字中 → 立刻顯示全文；全文已顯示 → 下一個指令。**暫停中（`session.Paused`）不處理確定鍵**（暫停選單也用同一顆鍵）。
- 全文顯示後在對話框右下角顯示 `▼`。
- 開始時若有 `actor`：`PlayAction("Talk", true)`；**只有 Talk 真的播成功（回 true）**，這句結束時才 `PlayAction("Idle", true)`。這樣沒有 Talk 的角色（例如正在循環 Hurt 的河董）不會被蓋回 Idle。

### `move`：角色走到某處（預設阻塞）

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `actor` | string | **是** | — | 角色 id |
| `x` | number | 三選一 | — | 目標的世界 x |
| `dx` | number | 三選一 | — | 相對目前位置 |
| `target`＋`gap` | string＋number | 三選一 | `gap` 1.5 | 走到 `target` 角色旁邊、停在自己這一側距離 `gap` 的位置 |
| `speed` | number | 否 | 3 | 單位／秒 |
| `wait` | bool | 否 | true | false＝下令後立刻往下執行 |

- 執行：`ScriptedMoveTo(x, speed)`，每幀檢查 `IsScriptedMoving`。超過「距離 ÷ 速度＋2 秒」還沒到：`Teleport(x)` 並記警告（不讓演出卡死）。
- 走完後面向維持移動方向；需要轉身請接 `face`。

### `face`：轉向（立即）

| 欄位 | 型別 | 必填 | 說明 |
|---|---|---|---|
| `actor` | string | **是** | 角色 id |
| `dir` | `"left"`／`"right"` | 二選一 | 指定方向 |
| `toward` | string | 二選一 | 面向另一個角色 |

### `anim`：播動作（預設不阻塞）

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `actor` | string | **是** | — | 角色 id |
| `name` | string | **是** | — | 動作名稱（`design.md` §8.3），例如 `Attack`、`Stomp`、`Hurt` |
| `loop` | bool | 否 | false | false＝播一輪後回 `Idle` |
| `wait` | bool | 否 | false | true＝等一輪（`GetActionDuration`）；`loop` 為 true 時忽略 |

- 角色沒有這個動作（`PlayAction` 回 false）：記警告、跳過。

### `camera`：鏡頭（依 `mode`）

| mode | 欄位（預設） | 預設阻塞 | 呼叫 |
|---|---|---|---|
| `pan` | `x`（必填）、`y`（目前鏡頭 y）、`time`（1.0） | 是 | `rig.PanToAsync` |
| `follow` | `actor`（必填）、`time`（0.8，平滑回到跟隨） | 是 | `rig.Follow(actor.Root, false)`，再等 `time` |
| `zoom` | `size`（必填）、`time`（0.8） | 是 | `rig.ZoomAsync` |
| `shake` | `strength`（0.2）、`time`（0.3） | 否 | `rig.Shake` |
| `reset` | `time`（0.5） | 是 | 回到跟隨主角、size 回 `SsIds.CameraSize` |

所有 mode 都可用 `wait` 覆寫是否阻塞。不認得的 mode：記警告、跳過。

### `wait`：停頓（阻塞）

| 欄位 | 型別 | 必填 | 說明 |
|---|---|---|---|
| `time` | number | **是** | 秒 |

### `fade`：淡入淡出（預設阻塞）

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `to` | number | **是** | — | 0＝透明、1＝全蓋住 |
| `time` | number | 否 | 0.5 | 秒；0＝立刻 |
| `color` | `"black"`／`"white"` | 否 | `"black"` | 換顏色時從目前不透明度接著淡 |
| `wait` | bool | 否 | true | |

和流程的淡入淡出共用同一個遮罩（`StoryUi.FadeAsync`）。

### `banner`：字卡（預設阻塞）

| 欄位 | 型別 | 必填 | 預設 |
|---|---|---|---|
| `text` | string | **是** | — |
| `time` | number | 否 | 2.0（含淡入淡出） |
| `wait` | bool | 否 | true |

字卡畫在淡入淡出遮罩**之上**（全黑時也看得到）。

### `grant`：發放能力或服裝（立即）

| 欄位 | 型別 | 必填 | 預設 | 說明 |
|---|---|---|---|---|
| `attack` | `"Melee"`／`"Sling"`／`"Roll"` | 二選一 | — | `session.Loadout.GrantAttack` |
| `costume` | `"Day"`／`"Express"` | 二選一 | — | `session.Loadout.GrantCostume(id, equip)` |
| `equip` | bool | 否 | true | 只對 `costume` 有效：發放後立刻換上 |
| `banner` | bool | 否 | true | 顯示字卡「獲得：<名稱>」（`Loadout.DisplayName`），不阻塞 |

`attack` 與 `costume` 同時有或都沒有：記錯誤、跳過。名稱大小寫要完全相同。

### `setFlag`：設旗標（立即）

| 欄位 | 型別 | 必填 | 預設 |
|---|---|---|---|
| `flag` | string | **是** | — |
| `value` | bool | 否 | true |

`tmp_` 開頭的旗標在重生時會被清掉（`StoryFlags.ClearTemporary`）。`seen_<location>` 由流程在演出後自己設，腳本不用寫。

## 4. 執行規則（`StoryUi.PlayCutsceneAsync`）

1. 讀檔與解析。失敗：`Debug.LogError`（含 location，解析錯誤含行與欄）→ 回傳 **false**，不改任何狀態。
2. `session.SetCutsceneActive(true)`（輸入鎖住、敵人停 AI、HUD 提示隱藏）。
3. 依序執行指令。某角色第一次被指令用到時呼叫它的 `BeginScripted()`。
4. 結束（正常、跳過、出錯、取消都走這裡，用 `try/finally`）：
   1. 對所有用過的角色呼叫 `EndScripted()`；
   2. 對話框、字卡收起；
   3. 鏡頭：`rig.Follow(主角, snap: false)`（zoom 不動，Boss 房由流程接手）；
   4. 遮罩不是 0 且 `keepFade` 為 false：淡回 0 並記警告；
   5. **等下一幀**才 `session.SetCutsceneActive(false)`：讓結束最後一句的那次 Space／J 不會被主角當成跳躍或攻擊。
5. 回傳 true。

### 跳過（暫停選單「跳過演出」）

剩下的指令改用**立即模式**執行，最後狀態和正常播完一樣：

| 指令 | 立即模式 |
|---|---|
| `say`、`banner`、`wait`、`camera shake` | 略過 |
| `move` | `Teleport` 到目標 x |
| `face`、`setFlag`、`grant` | 照常執行（`grant` 不顯示字卡） |
| `anim` | `loop: true` 的照常（保留最後姿勢），其他略過 |
| `camera pan／zoom／follow／reset` | `time` 當 0 |
| `fade` | 直接設成 `to` |

### 取消

`ct`（流程傳入的「這條命」token，見 `interfaces.md` §5.2）被取消（死亡重生、從檢查點重來、回選單、再玩一次）時：立刻停止，執行第 4 步的清理但**不再碰已被刪除的物件**（檢查 null），`OperationCanceledException` 往外丟，由流程安靜結束。

### 錯誤處理（單一指令）

原則：**記錄後跳過，絕不卡住演出**。

| 狀況 | 處理 |
|---|---|
| 不認得的 `op`、缺 `op` | `LogWarning`（含指令序號與 location），跳過 |
| 必填欄位缺少或型別錯誤 | `LogError`，跳過該指令 |
| 選填欄位型別錯誤 | `LogWarning`，用預設值 |
| 找不到角色（`ActorRegistry.Find` 回 null） | `LogWarning`，跳過該指令（`say` 只是不播 Talk，台詞照講） |
| 角色沒有該動作 | `LogWarning`，跳過 |
| 立繪 location 讀不到 | `SsArt.Get` 會給佔位圖並警告，照常顯示 |
| `move` 逾時 | `Teleport` 並 `LogWarning` |
| 指令丟出例外 | `LogException`，跳過該指令繼續 |

story 另外交一個 Editor 檢查工具（選單 `Tools/Sidescroller/檢查演出腳本`）：用同一個解析器讀全部 `SsCutscene_*.json` 與 `SsLevel_*.txt`，檢查 json 語法、未知 `op` 與欄位、`who` 是否在 `speakers`、`portrait` 是否在 `SsArt` 清單（`SsArt.IsKnownLocation`）、角色 id 是否出現在關卡資料的 `[entities]`（或是 `hero`），以及**所有台詞、名字、字卡、提示文字的每個字是否在思源黑體的字元表裡**。有問題時列出並以失敗結束（batchmode 可用 `-executeMethod`）。

## 5. json 解析方式：自寫小型解析器（`Story/MiniJson.cs`）

**決定**：story 自己寫一個遞迴下降的 json 解析器（約 200 行），輸出：

| json | C# |
|---|---|
| object | `Dictionary<string, object>`（key 重複時後者覆蓋並警告） |
| array | `List<object>` |
| string | `string`（支援 `\" \\ \/ \b \f \n \r \t \uXXXX` 與代理對） |
| number | `double`（`CultureInfo.InvariantCulture`） |
| `true`／`false` | `bool`（boxed） |
| `null` | `null` |

錯誤時丟 `FormatException`，訊息含行號、欄號與附近的文字。允許前後空白與 UTF-8 BOM；不支援註解與尾端逗號（保持標準 json，方便用外部工具檢查）。
再由 `CutsceneParser` 把樹轉成指令物件（每種 `op` 一個小類別，欄位讀取用 `GetString／GetNumber／GetBool(key, 預設)` 這類輔助方法，型別不符時依 §4 的規則警告）。

**理由**：

1. **避開 HybridCLR 反序列化遊戲模組型別的風險**：`JsonUtility.FromJson<T>` 走 Unity 原生的序列化器，依賴型別的序列化中繼資料；`T` 是 Assembly-CSharp（由 HybridCLR 執行期載入）的型別時，這條路徑比較少被驗證，Editor 能跑不代表 Player 一樣，出問題時通常是欄位默默變成預設值，很難查。Glowshaft 因此改用 regex。
2. **不需要新的 AOT 泛型**：輸出只用 `Dictionary<string, object>` 與 `List<object>`，都是參考型別參數，HybridCLR 走共用的泛型實作，不用補充元數據；數值以 boxed `double`／`bool` 存在 `object` 裡，也不產生值型別泛型。
3. **指令是異質的**：每種 `op` 欄位不同，`JsonUtility` 不支援多型與 `Dictionary`（`speakers` 是 key 不固定的物件），硬用要把所有欄位攤平成一個大類別，預設值也無法區分「沒寫」與「寫了 0」。
4. **regex 不夠用**：Glowshaft 只讀兩個固定欄位所以用 regex；演出腳本有巢狀陣列與物件，regex 會很脆弱。
5. **Newtonsoft.Json 不在專案裡**，而且它大量用反射與泛型，HybridCLR 下風險更高。
6. **錯誤訊息好**：自寫解析器能報行號與欄號，配合 Editor 檢查工具，改劇本時馬上知道哪裡錯。

## 6. 範例

完整範例見 `cutscenes/`：

| 檔案 | 觸發 | 重點 |
|---|---|---|
| `SsCutscene_Intro.json` | 起點 `trg_intro` | 字卡、旁白、鏡頭平移看電視（`style: screen`）、雜兵走進畫面（`move`），結束後雜兵直接開打 |
| `SsCutscene_Mentor.json` | `trg_mentor` | `grant` 三連發、白色閃光換裝（`fade color white`） |
| `SsCutscene_BossIntro.json` | `trg_boss`（第一次） | `setFlag tmp_boss_gate_closed` 關門、`zoom` 到 Boss 房、`anim Stomp wait` |
| `SsCutscene_BossDefeat.json` | Boss 被擊敗 | `move target/gap` 走到 Boss 旁、畫外音（`offscreen`）、淡到全黑接字卡、`keepFade: true` |

片段：

```json
{ "op": "say", "who": "boss", "style": "screen", "face": "happy", "text": "熱浪市的各位市民，午安！" }
{ "op": "move", "actor": "hero", "target": "boss", "gap": 2.2, "speed": 3 }
{ "op": "camera", "mode": "zoom", "size": 6.75, "time": 0.8, "wait": false }
{ "op": "grant", "costume": "Express", "equip": true, "banner": false }
{ "op": "fade", "to": 1, "time": 0.2, "color": "white" }
```
