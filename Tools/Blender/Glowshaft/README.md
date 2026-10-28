# 《螢火礦坑》Glowshaft 美術素材

全部素材都由 bpy 程式化建模（低多邊形、平面著色、黑色描邊），再用上一層的 `render_sprites.py` 以 `--albedo`＋法線渲染。顏色 sheet 只有固有色，明暗全部交給 URP 2D Light 和法線圖。

## 一鍵重做全部素材

```bash
python3 Tools/Blender/Glowshaft/render_all.py
```

- 在專案根目錄執行，用系統的 `python3`，不是 Blender 內建的 Python。每種素材各開一個 Blender 背景行程，全部約 1 分鐘。
- `--only Miner,Floor` 只重做指定素材；`--jobs 4` 同時跑幾個 Blender；`--blender <路徑>` 或環境變數 `BLENDER` 指定 Blender。
- 單幀 PNG 與每種素材的 Blender log 放在 `--work`，預設是系統暫存資料夾的 `glowshaft_render/`，不會進專案。
- 輸出位置是 `Assets/Art/Glowshaft/Sprites/<Name>/`。動作改名或刪掉時，舊的 sheet 會連同 `.meta` 一起清掉。

接著在 Unity 匯入，選單是 **Tools → Glowshaft → 匯入 Sprite Sheet**。也可以用 batchmode：

```bash
"/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -nographics -projectPath "$PWD" -executeMethod SpriteSheetImporter.ImportAndValidate -logFile /tmp/import.log
```

## 檔案

| 檔案 | 用途 |
|---|---|
| `render_all.py` | 素材清單與渲染參數，依序叫 Blender 跑下面三支腳本 |
| `glowshaft_models.py` | 建模＋動作（Blender 內執行，只讀 `--target`） |
| `../render_sprites.py` | 共用的多方向渲染器 |
| `glowshaft_finalize.py` | 檢查 PPU、裁切或接縫，在 json 補 `pixelsPerUnit`、`tile`，再複製進 `Assets` |

## 規格

PPU 統一是 **64**：1 Unity 單位＝1 Blender 公尺＝64 px。每種素材的相機寬度固定為 `size / 64` 公尺，所以素材之間的比例一致。

| Name | 方向 | Actions（幀數） | 格子 px | 俯角 | 說明 |
|---|---|---|---|---|---|
| Miner | 8 | Idle（8）、Run（8） | 128 | 40° | 高約 1.3 單位，右手（-X）拿能量槍，頭燈燈殼朝前 |
| SlimeGreen | 8 | Move（8） | 128 | 40° | 寬約 0.9 單位 |
| SlimePurple | 8 | Move（8） | 128 | 40° | 較小（寬約 0.65），有角與怒眉 |
| SlimeOrange | 8 | Move（8） | 160 | 40° | 較大（寬約 1.4），背上有石塊 |
| Bullet | 1 | Idle（4，脈動） | 32 | 90° | 正俯視，**彈頭朝 +X（畫面右）**，旋轉 sprite 即可對準任意方向 |
| Battery | 1 | Idle（8，自轉＋浮動） | 64 | 40° | |
| Floor | 1 | Idle（4 種變化） | 128 | 90° | 2×2 單位無縫 tile。4 種的邊緣完全相同，可以混鋪；第 0 幀是素面 |
| Rock | 1 | Idle（4 種變化） | 128 | 40° | 大圓石／高石柱／兩顆／扁平含金礦 |
| Crystal | 1 | Idle（4 種變化） | 96 | 40° | 青／洋紅／金／綠 |

- **pivot**：角色與道具在腳底（原點）；Bullet、Floor 在中心。數值以各 json 的 `pivot` 為準。
- **fps**：一律 12。
- **sprite 編號**：第 r 列（方向，順序 `S, SE, E, NE, N, NW, W, SW`）第 c 幀＝`r × columns + c`。
- **json 新增欄位**（原有欄位不變）：
  - `pixelsPerUnit`：整數 PPU。
  - `tile`：可平鋪。匯入時會用 FullRect mesh。

## 建模約定

- 根物件名稱＝素材名，放在原點不動。正面朝 -Y、上方 +Z，原點就是腳底 pivot。
- 動作掛在子物件上：Miner 掛在 Armature `Miner_Rig`，史萊姆、子彈、電池掛在 `<Name>_Body`。
- 多張變化（Floor、Rock、Crystal）的做法：`<Name>_Frames` 空物件的 Action「Idle」決定幀數，每組變化的物件用自己的 `hide_render` 動畫，只在對應的那一幀顯示。
- 程式裡的顏色一律寫 **sRGB**，`linear()` 轉成線性後才交給 Blender。渲染用 Standard 輸出，PNG 上的顏色就是程式裡寫的值。
- 描邊修改器必須叫 `SpriteOutline`，`render_sprites.py` 渲染法線 pass 時才會把它關掉。
- 地板是週期 Voronoi 石板，鋪滿 3×3 格，相機只看中央那一格，所以四邊的取樣都會循環接上。

## 渲染後的檢查（finalize）

以下任一項不通過就不落地，而且 `render_all.py` 會回報失敗：

- 每格都不能是空的，法線 sheet 的尺寸要和 json 一致。
- `cellSize / orthoScale` 必須等於 PPU（相對誤差 ≤ 0.1%）。
- 非 tile 素材：每格最外圈的 alpha 必須 ≤ 2/255，否則判定為被裁切。
- tile：
  - 俯角必須是 90°，每格必須完全不透明。
  - 左右、上下接縫的平均色差不能超過內部相鄰像素平均色差的 1.5 倍。

落地方式：

- 檔案先寫到 `<Name>/.staging/`，全部成功後才一次搬進去，中途失敗不會留下半套素材。Unity 不會匯入以點開頭的資料夾。
- 法線 sheet 的透明像素（背景、描邊）會填成朝鏡頭的 (128,128,255)，避免 bilinear 取樣時輪廓混到錯誤的法線。
- 只會清掉這個素材自己的舊 sheet（`<Name>_*.png` 和 `<Name>_sprites.json`），資料夾裡的其他檔案不會被動到。
- 匯入工具把整個 `Assets/Art/Glowshaft/Sprites/` 視為管線所有：任何不合規的 `*_sprites.json`，或 PPU 不一致，都算失敗。
