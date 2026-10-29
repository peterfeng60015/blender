# shader session 交接：AhJiaToon 升級＋平滑描邊法線

- worktree：`/Users/iulee/UnityProject/blender/.claude/worktrees/sweet-goldberg-f4bbf6`（分支 `claude/sweet-goldberg-f4bbf6`，未 commit）
- 改動 4 檔：
  - `Tools/Blender/Hero3D/hero3d.py`：新 `#region 描邊法線`（`bake_outline_normals`）；`export_fbx()` 加 `use_tspace=True`（docstring 補一句）；`main()` 在 `bake_vertex_colors(mesh)` 後加一行 `bake_outline_normals(mesh)`。
  - `Assets/Art/Hero3D/AhJiaToon.shader`：重寫（見下）。
  - `Assets/Art/Hero3D/M_AhJiaToon.mat`：新屬性預設值；移除舊的 `_Shade1`／`_Shade2`。
  - `Assets/Editor/Hero3D/Hero3DPreviewSetup.cs`：匯入設定明訂 normals=Import、tangents=CalculateMikk、不交換 UV、不產生光照貼圖 UV；模型旁有 `SsHero3D_BaseMap.png`／`SsHero3D_MaskMap.png` 就設好匯入格式並接上材質（`_VertexColor`=0、`_UseMask`=1），沒有就維持頂點色後備。
- `SsHero3D.fbx` 驗證時暫時換過，已還原成 main 版；整合後要用新的 hero3d.py 重新匯出 FBX 再跑 `Tools/Hero3D/設定模型與預覽場景`。

## 整合注意

- `bake_outline_normals` 必須在**最後的網格**上跑（所有零件合併、shape key 加完、UV0 展好之後），放在 `export_fbx` 之前。它會把 n 邊形切成三角形（MikkTSpace 只吃三角／四邊形；已驗證 shape key 與頂點群組保留）。
- 它要求網格剛好 1 層 UV（UV0），自己加第 2 層 `OutlineNormal`；若貼圖 session 留了多層 UV 會直接 raise。
- UV0 若大多是 0（現況：零件建模順手產生的 UVMap，只有 431／16006 個角非 0），會整層換成方盒投影以便算切線。貼圖 session 的 atlas UV 進來後就不會觸發。方盒投影的接縫讓 Unity 頂點數 4025→6345（暫時的，atlas UV 進來後由 atlas 接縫決定）。
- 不依賴任何零件名、骨頭名，造型重新設計不影響。
- **頂點色 A 目前全是 1**（`srgb()` alpha 固定 1，`OutlineWidth` 群組在 `bake_vertex_colors` 被刪）。約定由貼圖 session 寫入 A（主體 1.0、小配件 0.6）；沒寫的話小零件描邊會比 Blender 預覽粗 1.67 倍。

## shader 參數（Sidescroller/AhJiaToon）

| 參數 | 預設 | 說明 |
|---|---|---|
| `_BaseMap` | white | albedo（sRGB） |
| `_MaskMap` | linearGrey | R 高光遮罩、G 陰影偏移（0.5 不偏移）、B 材質類別（0 皮膚、0.33 殼、0.66 布、1 金屬／眼睛）；有自己的取樣器 |
| `_UseMask` | 0 | 0＝不讀 mask（R=0、G=0.5、陰影用 `_ShadowColor`） |
| `_VertexColor` | 1 | 頂點色乘到 albedo 的比重；有貼圖後設 0 |
| `_Tint` | 白 | 只影響主體（受擊閃色，HeroVisual 用 MPB 改），描邊不吃 |
| `_LightDir` | (-0.35, 0.8, -0.5) | 固定世界方向光，不吃場景燈 |
| `_Step1`／`_Step2` | 0.38／0.62 | half-lambert 的陰影／亮部門檻（硬邊，fwidth 約 1px 抗鋸齒） |
| `_MidMix` | 0.55 | 中間色階＝陰影色往白混多少 |
| `_ShadowOffset` | 0.3 | mask G 對門檻的推移量 |
| `_ShadowColor` | (0.58, 0.52, 0.6) | 沒有 mask 時的通用陰影色（乘在 albedo） |
| `_ShadowSkin`／`Shell`／`Cloth`／`Metal` | 暖粉／棕紫／冷藍／灰紫 | 依 mask B 選的陰影色 |
| `_RimColor`／`_RimStrength`／`_RimWidth` | 暖白／0.45／0.28 | 輪廓亮邊顏色、強度、寬度（1−N·V 的門檻） |
| `_RimDir` | (0.8, 0.25, 0.45) | 亮邊出現在法線朝這個世界方向的那側（預設畫面右側，與主光相反） |
| `_SpecColor`／`_SpecSize` | 白／0.06 | 硬邊高光（Blinn N·H 門檻）乘 mask R |
| `_OutlineColor` | 深棕 | 描邊色 |
| `_OutlineWidth` | 2.5 | 描邊寬度，單位「1080 高畫面的像素」，再乘頂點色 A；換解析度、鏡頭遠近都一樣粗 |
| `_OutlineDepth` | 0.02 | 描邊沿視線往後推（公尺），減少殼條、帽簷間零碎的內線 |

無 keyword，每個 pass 一個變體；兩個 pass 共用同一個 `UnityPerMaterial`（SRP Batcher 相容）。

## 驗證

- 編譯：GLES3x（Android、WebGL）、Vulkan、Metal、D3D 的兩個 pass 都成功（`ShaderData.Pass.CompileVariant`）。
- 描邊法線：Unity 匯入後用 UV1＋Unity 的 Mikk 切線還原，和 Blender 算的一致（同位置頂點最大差 0.02°）；硬邊立方體測試（`cube_outline_normal_test.py`）每個角還原成對角線方向，誤差 0°。
- 截圖（本資料夾）：`Hero3DPreview.png`（正式 SetupAndCaptureBatch，每格 360 高，描邊只剩約 0.8px 是正常的）、`game_scale_crop.png`（遊戲鏡頭 size 6、模型 1.3 倍、左右翻面，3 倍放大）、`closeup.png`、`far.png`（鏡頭推遠一倍，描邊像素寬不變）、`mask_*.png`（單色 mask 測四種陰影色與高光）、`pose_Run.png`／`pose_Melee.png`。
- 驗證腳本：`TmpShaderVerify.cs.txt`（放進 `Assets/Editor` 改回 .cs 即可用：`-executeMethod TmpShaderVerify.Run -outDir <dir>`）。

## 相容性（WebGL／Android）

- 貼圖：Android 用 ASTC（albedo 6x6、mask 4x4，Setup 已設定 override）。WebGL 一個 build 只能選一種壓縮格式：手機 WebGL 建議在 Build Settings／Build Profile 把 Texture Compression 設 ASTC（iOS Safari、Android Chrome 都支援），桌機 WebGL 用 DXT/BC；要兩邊都顧就出兩個 build 或用 ETC2（相容性最廣、畫質較差）。mask 的 B 是分類資料，壓縮後的誤差會被 shader 四捨五入吸收（類別間隔 0.33）；UV 島之間要留 padding，避免 mipmap 把不同類別混在一起產生陰影色細邊。
- 精度：主體 fragment 用 half；描邊 vertex 全程 float。fwidth／向量三元運算在 WebGL2（GLES3）可用。
- 專案 `meshDeformation: 0`（CPU 蒙皮）：切線通道會增加每幀蒙皮成本，一隻角色可接受。
- 若日後開啟 Vulkan「Apply display rotation during rendering」（pre-transform），描邊的畫面比例修正會套錯軸，要改用投影矩陣推算比例。

## 已知限制

- 現在的網格全部平滑著色、沒有硬邊，平滑法線等於頂點法線，這版看不出差異；造型 session 加倒角／硬邊／分節後才會發揮作用。
- 平滑法線與某個角的面法線夾角超過 90°（例如極薄、對折的片）時，z 重建為 0，該處描邊方向會退化成切平面上的投影。
- 定寬描邊在鏡頭拉很遠時不會跟著變細（設計如此）；若之後有大幅拉遠的鏡頭，要加上依距離縮放的上限。
- 陰影色、rim、高光預設值是用頂點色模型調的，貼圖進來後要再看一次。
