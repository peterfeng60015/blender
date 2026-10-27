# Blender → Unity 2D sprite 實驗

用 `render_sprites.py` 把 3D 物件渲染成多方向的 sprite sheet，同時產出法線 sheet，再手動匯入 Unity 6（URP 2D Renderer），用 2D 光源打光。

- Blender：**5.2 LTS**（https://www.blender.org/download/ ）。4.2～4.5 大致可用，但沒有驗證過。
- Unity：6000.3，專案已經在用 `Assets/Settings/Renderer2D.asset`（URP 2D Renderer）。
- 已在 Blender 5.2.3 LTS（Apple Silicon）實跑過示範場景：8 方向 × 2 動作、含法線，約 40 秒。實測結果如下：
  - 方向命名正確
  - pivot 對準腳底
  - 法線值正確：正面約 (128,128,255)、右側偏紅、上方偏綠
  - `--pixel` 的 alpha 只有 0 與 255
  - Unity 端的匯入與打光**還沒驗證**

## 1. 在 Blender 端產出 sprite sheet

先用內建的示範角色跑一次完整流程，不需要任何 .blend。示範角色是「身體＋頭＋藍色鼻子」，鼻子指向正面，方便看出朝向；附兩個動作：Idle 16 幀、Hop 12 幀。

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/Blender/render_sprites.py -- --demo --save-demo ./SpriteOut/demo.blend --out ./SpriteOut
```

在專案根目錄執行，輸出到 `./SpriteOut/`。這個資料夾在 `Assets` 外，Unity 不會自動匯入。

渲染自己的模型時：

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b hero.blend --python Tools/Blender/render_sprites.py -- --target Hero --anim-object Hero_Armature --actions Idle,Run --out //SpriteOut
```

### 參數

| 參數 | 預設 | 說明 |
|---|---|---|
| `--target` | （必填，`--demo` 除外） | 根物件。它的原點就是旋轉中心和腳底 pivot。角色正面要朝 Blender 的 **-Y**；根物件本身**不要**有位移動畫（root motion），動作請放在子物件或 Armature 上 |
| `--anim-object` | 同 `--target` | 掛 Action 的物件，通常是 Armature |
| `--actions` | 目前掛著的動作 | 逗號分隔的 Action 名稱。每個 Action 輸出一張 sheet，幀範圍取 Action 的 Frame Range |
| `--dirs` | 8 | 4、8 方向用 S/SE/E/…命名，其他數字用角度（`a045`）命名 |
| `--size` | 128 | 每格像素（正方形） |
| `--elevation` | 30 | 相機俯角（度）。0＝正側面（橫向卷軸），30～35＝常見的斜俯視 |
| `--ortho-scale` | 自動 | 自動取景會掃過所有動作的所有幀，保證不會裁切，而且所有方向、動作的縮放一致 |
| `--step` | 1 | 每隔幾幀取一幀 |
| `--engine` | eevee | `eevee`／`workbench`／`cycles`。卡通著色（Shader to RGB）和背面剔除描邊只有 EEVEE 能用，所以 `--demo` 一律用 EEVEE；法線 pass 不論選哪個都用 EEVEE |
| `--pixel` | 關 | 像素風：關掉抗鋸齒。建議同時把 `--size` 調小，例如 32、48 |
| `--albedo` | 關 | 顏色 sheet 只輸出固有色、不烘焙明暗，光影完全交給 Unity 2D 光照（見下方「作法重點」） |
| `--name` | 同 `--target` | 輸出檔名前綴。和一般版本比較時可以換個名字，例如 `--name HeroFlat` |
| `--no-normal` | 關 | 不輸出法線 sheet |
| `--keep-scene-lights` | 關 | 預設會關掉場景原本的燈，只用跟著相機轉的主光，讓每個方向的光影一致 |

### 輸出

```
SpriteOut/
  Hero_Idle.png        顏色 sheet：一列一個方向，第一列是 S；一欄一幀
  Hero_Idle_n.png      法線 sheet，格子排列與顏色 sheet 完全相同
  Hero_Hop.png / Hero_Hop_n.png
  Hero_sprites.json    格子大小、方向順序、fps、建議的 PPU 與 pivot
  _frames/…            單幀 PNG，除錯用
```

8 方向的列順序（由上到下）：`S, SE, E, NE, N, NW, W, SW`。S＝正面朝鏡頭，E＝面向畫面右邊。

### 作法重點

- **相機**：正交相機和主光都掛在 `SpriteRig` 空物件下，轉 rig 等於讓角色轉向。角色本身不會被動到，所以動畫裡的旋轉不受影響。
- **色彩**：顏色 pass 用 View Transform **Standard**。Blender 4.0 起預設是 AgX，會讓顏色跟貼圖不一致。
- **法線**：法線 pass 用材質覆寫，把**相機空間法線**編碼成顏色，並用 **Raw** 輸出、關掉 dither，數值原樣寫進 PNG。朝鏡頭的面是 (128,128,255)，X 朝右、Y 朝上。
  - Blender 的 Vector Transform 轉 Camera 時沿用 Cycles 的慣例（+Z＝鏡頭看出去的方向），所以腳本把 Z 翻號。
  - 描邊外殼不算進法線，描邊那圈在法線圖裡是透明的。描邊是黑色，打光的影響很小。
- **albedo 模式（`--albedo`）**：渲染前把角色每個材質的每個 Material Output 都改成「Emission（固有色）」，畫面上只剩固有色，不會烘進明暗。這只改記憶體裡的資料，不會存回 .blend。
  - **固有色的來源**，依以下優先順序取得：
    1. 材質的自訂屬性 `sprite_albedo`（示範角色用這個）
    2. 實際接到輸出的 Principled／Diffuse／Toon BSDF 的 Base Color 或 Color。會穿過 Mix／Add Shader、Reroute 和 Group 去找；有接貼圖就沿用那張貼圖
    3. 材質的 Viewport Display 顏色
  - **透明**：Principled 的 Alpha 會保留下來，鏤空貼圖（頭髮片、葉子）照樣是透明的。
  - **不會被改的材質**：已經接 Emission 的輸出（例如描邊）保持原樣。linked library 的材質無法修改，會略過並印出警告。
  - **法線 sheet**：和一般模式逐位元組相同。
  - **限制**：
    - Principled 本身的發光（Emission）會被丟掉。
    - BSDF 包在 Group 裡時只能取它的預設色，Group 內部接的貼圖拿不到。
    - Geometry Nodes 指定的材質、collection instance 的材質都不會被處理。
    - 搭 `--engine workbench` 時改用平光＋貼圖色，但不支援透明。
- **示範角色的卡通著色**：Diffuse → Shader to RGB → Color Ramp（Constant，兩階）。
- **示範角色的描邊**：反轉法線外殼，也就是 Solidify 往外長、翻法線，再配一個背面剔除的深色材質。

## 2. 匯入 Unity（手動步驟）

以下以 `Hero_Idle` 為例，數值請看 `Hero_sprites.json`。

### 2-1. 放進專案

把 `Hero_Idle.png`、`Hero_Idle_n.png` 複製到例如 `Assets/Art/Sprites/Hero/`。

### 2-2. 顏色 sheet（`Hero_Idle.png`）匯入設定

| 欄位 | 值 |
|---|---|
| Texture Type | Sprite (2D and UI) |
| Sprite Mode | Multiple |
| Pixels Per Unit | json 的 `suggestedPixelsPerUnit`。這個值能讓 1 Unity 單位＝1 Blender 公尺；也可以改成整數，但**同一批角色、tile 要統一** |
| Mesh Type | Tight（預設即可） |
| Generate Mip Maps | 關 |
| Wrap Mode | Clamp |
| Filter Mode | Bilinear；像素風用 **Point (no filter)** |
| Max Size | ≥ sheet 寬高中較大的那個，例如 16 幀×128＝2048 |
| Compression | 先用 None 看效果，確認後再改成 High Quality |

按 **Apply**。

### 2-3. 切圖

1. 按 **Sprite Editor** → 左上 **Slice**。
2. Type＝**Grid By Cell Size**，Pixel Size＝`cellSize`×`cellSize`（例如 128×128），Offset／Padding＝0。
3. Pivot＝**Custom**，Custom Pivot 填 json 的 `pivot`（例如 0.5, 0.21），讓腳底對齊物件位置。
4. 勾 **Keep Empty Rects**，避免全透明的格子被跳過，導致編號錯位。
5. 按 **Slice** → 右上 **Apply**。

切出來的 sprite 叫 `Hero_Idle_0`、`Hero_Idle_1`…，從左上開始逐列編號。第 r 列（0＝S）第 c 幀的編號＝`r × columns + c`，`columns` 可在 json 查到。

### 2-4. 法線 sheet（`Hero_Idle_n.png`）匯入設定

| 欄位 | 值 |
|---|---|
| Texture Type | **Normal map** |
| Create From Grayscale | 關 |
| Filter Mode／Max Size／Compression | 與顏色 sheet 相同 |

按 **Apply**。法線 sheet **不用**切圖。

### 2-5. 把法線 sheet 掛成 Secondary Texture

1. 回到 `Hero_Idle.png` 的 **Sprite Editor**，左上下拉選單切到 **Secondary Textures**。
2. 按 **+**，Name 填 **`_NormalMap`**，Texture 指定 `Hero_Idle_n`。
3. 按 **Apply**。

### 2-6. 做動畫

1. 在 Project 視窗展開 `Hero_Idle.png`，選取第一列（S）的全部幀：`Hero_Idle_0` ～ `Hero_Idle_{columns-1}`。
2. 拖進 Scene，Unity 會要求存 `.anim`，並自動建立帶 Animator 的物件。
3. 在 Animation 視窗把 Sample Rate 設成 json 的 `fps`。Sample Rate 若沒顯示，從視窗右上「⋮」→ **Show Sample Rate** 打開。
4. 其他方向重複以上步驟，或在 Animator 裡用 Blend Tree 依移動方向切換。

### 2-7. 2D 光照驗證法線

1. 確認 Sprite Renderer 的材質是 **Sprite-Lit-Default**（URP 2D 預設就是）。
2. 把場景的 **Global Light 2D** Intensity 調低（例如 0.3），才看得出打光效果。
3. 新增 **GameObject → Light → Spot Light 2D**，在 Light 2D 的 **Normal Maps** 區把 Quality 設 **Fast** 或 **Accurate**，Distance 視需要調整。
4. Play 或在 Scene 拖動光源：光從右邊來時，角色右側應該變亮。

### 2-8. 像素完美（`--pixel` 產出的素材）

1. 匯入設定改用 Filter Mode＝Point、Compression＝None，所有 sprite 的 PPU 要一致。
2. Main Camera 加 **Pixel Perfect Camera**：Assets Pixels Per Unit＝sprite 的 PPU，Reference Resolution 例如 320×180，Grid Snapping 選 Pixel Snapping 或 Upscale Render Texture。
3. URP 2D 已經內建這個元件，**不要**另外裝舊的 2D Pixel Perfect 套件（那是給 Built-in 管線用的）。

## 3. 驗收清單

- [ ] 8 個方向的動畫都能播放，鼻子朝向與方向名稱一致（E＝鼻子朝畫面右邊）。
- [ ] 腳底不會隨方向或動作跳動（pivot 正確）。
- [ ] 2D 光從右邊照時，角色右側變亮。如果亮的是左側或上下顛倒，表示法線 X 或 Y 方向要翻轉，請回報。
- [ ] 顏色與 Blender 視窗一致，沒有偏灰或偏暗。
- [ ] `--pixel` 版本放大看邊緣是硬邊，沒有半透明鋸齒。

## 參考資料

- Blender 命令列渲染：https://docs.blender.org/manual/en/latest/advanced/command_line/render.html
- Shader to RGB：https://docs.blender.org/manual/en/latest/render/shader_nodes/color/shader_to_rgb.html
- Unity 法線與遮罩貼圖（Secondary Textures）：https://docs.unity3d.com/6000.0/Documentation/Manual/urp/SecondaryTextures.html
- Unity Pixel Perfect Camera：https://docs.unity3d.com/Manual/urp/2d-pixelperfect-configure.html
