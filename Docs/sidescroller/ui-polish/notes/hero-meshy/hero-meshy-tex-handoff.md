# hero-meshy：tex 交接

- session：local_7db5f263-12ab-4580-b677-4a0dbf2386b7
- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/distracted-cannon-92d91b`（分支 `claude/distracted-cannon-92d91b`，基於 main ac19ffb；未 commit）
- 改動檔：`Tools/Blender/Hero3D/meshy_texture.py`（新檔）、`Assets/Art/Hero3D/AhJiaToon.shader`、`Assets/Art/Hero3D/M_AhJiaToon.mat`
- 合併：`git -C <worktree> diff` 拿 shader／mat 的差異，`meshy_texture.py` 是未追蹤檔要直接複製。

## meshy_texture.py

```python
import meshy_texture   # 同資料夾；hero3d.py 已在 sys.path 時直接 import
base_path, mask_path = meshy_texture.build_meshy_textures(
    {"base": "<Base Color>", "metal_rough": "<metallicRoughness 或 None>", "normal": None}, out_dir, size=1024)
```

- 只用 bpy＋numpy；輸入可以是 PNG 或 JPEG（glb 內嵌的是 JPEG，不用轉檔）；不依賴網格；import 時沒有副作用（只有 `__main__` 才跑 CLI）。
- 讀進來的暫存 image 會自己 `remove`，不會留在 .blend 裡。
- `_BaseMap`：可分離 Lanczos3 縮到 size²（在 sRGB 編碼值上縮，細黑線不變淡）。和 2048 原圖並排看，臉部五官、甲殼紋幾乎沒差（對照圖 `m_sharp.png`，見下）。Meshy 畫進去的 AO／筆觸**保留**，不在貼圖端去光影（理由見「烘焙光影」）。
- `_MaskMap`（linear）：
  - B 材質類別：0 皮膚（淺粉、低紋理）、0.33 殼（預設：土黃、褐、殼帶、**鞋**、爪）、0.66 布（紅帽、黃頸巾、青綠包與背帶、深藍短褲）、1 眼睛（含瞳孔）。最後做 7×7 多數決去雜點。
  - 眼睛：有 metal_rough 時用粗糙度 < 0.45 判定（Meshy 這份只有眼睛是光滑的）；**不採用金屬度**（金屬度只標在看不到的內側面，平均色是皮膚褐粉，採用會在那些地方亂開高光）。沒有 metal_rough 時退回「近白色＝眼白」（會誤抓包包白滾邊、瞳孔不算）。
  - G 陰影偏移：皮膚 0.6、眼睛 0.8、其他 0.5（臉比較不容易碎成陰影塊、眼白不會變灰）。
  - R 高光：只有眼睛 1（瞳孔上的硬邊高光＝眼神光）。
  - 存 8-bit 後 B＝0／84／168／255，shader `mask.b*3` 四捨五入正好落在 0／1／2／3（已讀回驗證）。
- 單獨跑（多寫一張分類可視化 `AhJia3D_MaskClasses.png`，和 BaseMap 同 UV，可直接貼回模型看）：
  `blender -b --python Tools/Blender/Hero3D/meshy_texture.py -- --base <Base Color> --metal-rough <路徑> --out <資料夾> [--size 1024]`
- 1024 跑一次約 2 秒。

## Shader 與材質

- `AhJiaToon.shader` 新增 `_RampStrength ("Ramp Strength (Lower for Painted Albedo)", Range(0,1)) = 1`：`ramp = lerp(1, ramp, _RampStrength)`，3 階色階只疊一部分；rim、高光、描邊不受影響。預設 1＝原本行為（程式化版不變）。CBUFFER 已加，兩個 pass 共用，SRP Batcher 相容。
- 全平台編譯：Android GLES3／Vulkan、WebGL（GLES3x）、Editor Metal，Toon 與 Outline 兩個 pass 的頂點／片段都 0 錯誤（用最小 URP 17.3.0 專案＋`ShaderData.Pass.CompileVariant` 檢查；故意改壞會報錯，檢查本身有效）。
- `M_AhJiaToon.mat` 加 `_RampStrength: 0.6`（Meshy 版建議值）。其他數值不動。`Hero3DPreviewSetup.cs` 不用改（它只設貼圖、`_VertexColor`、`_UseMask`，不覆寫浮點數）；如果 unity 端會重建材質，記得設 `_RampStrength = 0.6`。
- 注意：mat 改成 0.6 後，若退回程式化版模型，色階會比以前淡；要恢復就把 mat 的 `_RampStrength` 設回 1。

## 烘焙光影的處理

- Meshy 這份 Base Color 沒有明顯的方向光，主要是 AO（凹處壓暗）和筆觸，本身就像手繪貼圖。疊上完整色階（`_RampStrength`=1）時，減面後的法線起伏讓臉上碎成一塊塊陰影，眼白也會變灰。
- 處理：材質端 `_RampStrength` 0.6＋mask G 讓皮膚／眼睛偏亮。不在貼圖端去光影：atlas 碎成很多小島、沒有間隙，低頻濾波會跨島混色；而且 AO 是讓它看起來有手繪感的部分。

## 對照圖（Blender toon 模擬：固定光向 3 階＋依 B 選陰影色＋G 偏移＋倒殼描邊；**沒有模擬 rim 與高光**）

資料夾 `hero-meshy-drop/tex-20261030-1500/`（同本檔目錄）：
- `AhJia3D_BaseMap.png`、`AhJia3D_MaskMap.png`、`AhJia3D_MaskClasses.png`：試算貼圖（1024²），可直接給 unity 試。
- `m_cls.png`：分類可視化貼回模型（上排四角度＋臉；下排左二是沒有 metal_rough 的退回模式，右三是最終 toon）。粉＝皮膚、土黃＝殼、藍＝布、黃＝眼睛。
- `m_fin.png`：最終參數（`_RampStrength` 0.6）四角度＋臉；`fin_game_scale.png`：160px 遊戲尺寸。
- `m_game.png`：160px 三張並排：最終／評估 A 版／完整色階（舊參數）。
- 和評估 A 版比：清晰度相同（同一張貼圖、1024 Lanczos），臉部陰影比完整色階乾淨；A 版是 Blender 太陽光，光向不完全一樣。

渲染腳本（scratchpad，非 repo）：`/private/tmp/claude-501/-Users-iulee-UnityProject-blender--claude-worktrees-distracted-cannon-92d91b/de8c5e67-79c8-4874-ae3d-9aebacdaf4f0/scratchpad/scripts/render.py`（參數 `flatten`＝1−`_RampStrength`）。

## 已知問題

- 鞋子（深褐＋米色鞋底）和殼帶同色，分到「殼」，陰影色偏暖紫而不是布的藍灰；影響小。
- 皮膚上的深色皺褶線、頭頂甲片會分到殼（頭頂本來就是甲片）。
- 沒有 metal_rough 的退回模式：瞳孔不算眼睛、包包白滾邊會算成眼睛；皮膚紋理門檻是在 1024 調的，size 改小（如 512）臉會比較碎。rig 照約定傳 metal_rough 就不會走到。
- 分類門檻是針對這一份 Meshy 貼圖調的；換模型要重看 `AhJia3D_MaskClasses.png`。
- Unity 實機的最終觀感（含 rim、高光、ASTC 壓縮）要協調整合後在 Unity 確認；若臉還是太碎，可再降 `_RampStrength`（0.5）或提高皮膚 G（`CLASS_SHADOW`）。
