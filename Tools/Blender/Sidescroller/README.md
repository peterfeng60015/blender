# 《熱浪急件》Sidescroller 美術素材

全部素材都由 bpy 程式化建模（基本形體＋倒殼描邊），再用上一層的 `render_sprites.py` 以**純側視、單方向**渲染。
和 Glowshaft 不同，這款**不用 `--albedo`**：3 階卡通色階直接烘進顏色 sheet（design.md §8.2），法線 sheet 照常輸出給局部光源點綴。

## 一鍵重做全部素材

```bash
python3 Tools/Blender/Sidescroller/render_all.py
```

- 在專案根目錄執行，用系統的 `python3`。每種素材各開一個 Blender 背景行程，預設同時 4 個，全部約 35 秒。
- `--only SsHeroDay,SsRat` 只重做指定素材；`--jobs N` 同時跑幾個 Blender；`--blender <路徑>` 或環境變數 `BLENDER` 指定 Blender。
- 單幀 PNG 與每種素材的 Blender log 放在 `--work`（預設系統暫存資料夾的 `sidescroller_render/`），不會進專案。同時跑兩個 `render_all.py` 時請各給一個 `--work`。
- 輸出到 `Assets/Art/Sidescroller/Sprites/<Name>/`。動作改名或刪掉時，舊的 sheet 會連同 `.meta` 一起清掉。

接著在 Unity 匯入（和 Glowshaft 共用匯入工具，兩個根目錄一起處理）：選單 **Tools → Glowshaft → 匯入 Sprite Sheet**，或

```bash
"/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -nographics -projectPath "$PWD" -executeMethod SpriteSheetImporter.ImportAndValidate -logFile <log>
```

## 檔案

| 檔案 | 用途 |
|---|---|
| `render_all.py` | 素材清單（名稱、動作與幀數、格子、pivot、tile、接縫與不透明檢查）＝唯一的規格來源，依序叫 Blender 跑下面三支 |
| `ss_models.py` | Blender 內的建模入口，依 `--target` 呼叫對應的建模函式 |
| `ss_lib.py` | 共用：NPR 色階材質、描邊、基本形體、骨架與動作、取景框、多張變化 |
| `ss_hero.py` | 阿甲（`SsHeroDay`、`SsHeroExpress`，同一副骨架與動作，只換服裝） |
| `ss_cast.py` | 業務鼠、監工鴿、保全犀牛、河董、龜婆 |
| `ss_items.py` | 子彈與特效：`SsParcel`、`SsCoin`、`SsBomb`、`SsShock`、`SsFx` |
| `ss_scenery.py` | `SsTiles`、`SsProps`、`SsLandmark`、`SsBgFar`、`SsBgMid`、`SsUi` |
| `ss_portraits.py` | 對話框立繪 `SsPortrait*`（4 種表情） |
| `ss_finalize.py` | 渲染後的檢查，在 json 補 `pixelsPerUnit`、`tile`，原子落地到 `Assets` |
| `npr_test/` | Gemini 的 NPR 預研（不屬於這條管線） |

## 規格

PPU 統一是 **64**：1 Unity 單位＝1 Blender 公尺＝64 px。每種素材的相機寬度固定為 `size / 64` 公尺。fps 一律 12。
名稱、動作、幀數、格子照 `Docs/sidescroller/interfaces.md` §6.4（`render_all.py` 的 `ASSETS` 表）。

| 種類 | pivot（json 的 `pivot`） | 說明 |
|---|---|---|
| 角色 128px | (0.5, 0.1)：腳底在格子下緣往上 0.2 單位 | 兩套主角的幀數與 pivot 完全相同 |
| `SsRhino` 160px／`SsBoss` 320px | (0.5, 0.08)／(0.5, 0.06) | 同樣是腳底 |
| `SsProps`／`SsLandmark` | (0.5, 0.04)／(0.5, 0.03) | 腳底 |
| 子彈、特效、tile、背景、UI、立繪 | (0.5, 0.5) | 中央 |

- **方向**：只渲右側。模型面朝 **+X**，`--dirs 1 --elevation 0`（相機在 -Y 看 +Y，畫面右方＝+X），左側由程式翻轉 `Visual` 的 `localScale.x`。
- **子彈**：`SsParcel`、`SsShock` 朝 +X。
- **多張變化**：`SsTiles`（7）、`SsProps`（6）、`SsLandmark`（3）、`SsBgMid`（2）、`SsUi`（8）、立繪（4 表情）都是單一動作，第 n 幀＝第 n 種變化，意義見 design.md §8.4、§8.5。
- **可平鋪**（json `tile: true`，匯入用 FullRect）：`SsTiles`、`SsBgFar`、`SsBgMid`。
  - `SsTiles`：0、2、4、5 左右可接；1 上下左右都可接；6（閘門）上下可接。0、1、6 完全不透明。
  - `SsBgFar` 完全不透明、左右可接；`SsBgMid` 上方透明、左右可接。

## NPR 做法（design.md §8.2）

- **色階**：材質是「世界法線 · 固定光向 → Constant Color Ramp → Emission」。光向是「正上方偏前」`(0, -0.5, 0.866)`，沒有 X 分量，所以左右翻轉後明暗不變；跟 `render_sprites.py` 的場景燈無關。
  門檻：內積 < -0.05 暗（45％）、< 0.42 中（75％）、其餘亮（100％）；殼、西裝、盾牌等硬材質另有內積 ≥ 0.93 的窄高光帶（往白色混 45％）。
- **描邊**：倒殼（Solidify 往外長、翻法線、背面剔除的描邊材質），修改器名稱 `SpriteOutline`，渲法線時渲染器會自動關掉。
  粗細以像素給（角色 3px、Boss 4px、立繪 4px），顏色是各角色主色的深色版。
- **顏色**：程式裡一律寫 sRGB 十六進位，轉線性後交給 Blender；渲染用 Standard，PNG 上就是寫的值。
- 色階門檻與光向都在 `ss_lib.py` 開頭，要調整整體明暗改那裡即可。

## 建模約定

- 根物件名稱＝素材名，原點＝pivot。角色的骨架叫 `<Name>_Rig`，多張變化的幀數由 `<Name>_Frames` 的 Action 決定。
- **固定 pivot**：`render_sprites.py` 會依包圍盒自動置中，所以每個素材都有一個不渲染的取景框 `<Name>_FrameBox`（兩倍格高），把置中高度鎖在 `render_all.py` 給的 pivot。
  pivot 的規格值只寫在 `render_all.py` 的 `ASSETS` 表；finalize 核對的是「輸出的 pivot 等於表上的值」，也就是取景框有包住全部部件，表上的值本身要人對照 design.md §8.1。
- **骨架**：骨頭一律朝 +Z、roll 0。pose 的旋轉＝畫面上逆時針為正：往下垂的手、腳、包往前甩是正值，直立的身體、頭往前傾是負值。
- **切換部件**：同一個 Action 裡要藏起來的部件，把它的骨頭縮到 `HIDE`（1e-4），例如阿甲縮成球、彈弓只在 Shoot 出現。
  `pose()` 每次都會把縮放設回 1，所以同一根骨頭的位移與隱藏要在同一次呼叫裡給。
- 描邊在尖角（愛心尖端、很薄又有倒角的方塊）會被 Solidify 撐出尖刺：`box()` 已把倒角限制在最薄邊的 30％；尖角形狀改用「後面墊一個放大的深色形狀」當描邊。
- 可平鋪的背景用 `tiled()` 把跨格子邊的東西在左右各複製一份，複製品要用同一組亂數（每棟樓自己的 seed），接縫才一樣。

## 渲染後的檢查（finalize）

任一項不通過就不落地，`render_all.py` 回報失敗：

- 名稱：`Ss` 開頭、PascalCase、不含底線；動作名不含底線。
- 單方向純側視（`a000`、俯角 0）、`cellSize / orthoScale` 等於 PPU、pivot 等於素材表、動作與幀數等於素材表。
- 每格都不能是空的；法線 sheet 的尺寸要和 json 一致。
- 非 tile：每格最外圈的 alpha ≤ 2/255（沒被裁切）。
- tile：指定的格子要不透明；指定的接縫（左右／上下）用預乘 alpha 逐列比較，兩側差異 > 0.1 的列數比例不能超過 1％，或不能超過格子內部同樣跳動比例的 2 倍。
  比較的是最外兩欄（列），左右對稱但內容接不起來的圖（例如木箱兩側都是描邊）抓不到；`SS_SEAMS`／`SS_OPAQUE` 的格子編號超出範圍會報錯。
- 落地：先寫到 `<Name>/.staging/`（Unity 與 AssetPackage 都略過點開頭的資料夾），全部成功才一次搬進去；法線 sheet 的透明像素填成 (128,128,255)。
