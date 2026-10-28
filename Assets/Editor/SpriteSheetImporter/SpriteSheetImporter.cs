using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 讀 Blender（Tools/Blender/render_sprites.py）輸出的 *_sprites.json，自動完成 sprite sheet 的匯入設定。
///
/// 每份 json 的每張 sheet：
///   法線 sheet（*_n.png）→ Normal map、不切圖。
///   顏色 sheet           → Sprite／Multiple、json 的 pixelsPerUnit、Grid 切出每一格（空格也保留，
///                          等同 Sprite Editor 的 Keep Empty Rects，編號才不會錯位）、Custom pivot、
///                          Secondary Texture「_NormalMap」掛法線 sheet。
/// sprite 名稱是 &lt;sheet&gt;_&lt;編號&gt;，第 r 列第 c 格＝r × columns + c（第 0 列在最上面）。
/// 重跑時沿用同名 sprite 的 spriteID，既有引用不會斷。
///
/// 只用 Unity 內建型別，沒有任何 MonoBehaviour／ScriptableObject；放在獨立的 Editor 組件，不依賴
/// Assembly-CSharp（注意：專案裡任何組件有編譯錯誤時，batchmode 仍會在執行 -executeMethod 前中止）。
///
/// 這個資料夾歸素材管線所有：不合規的 json、PPU 不一致都算失敗。
///
/// batchmode（素材更新後重跑即可）：
///   Unity -batchmode -quit -nographics -projectPath &lt;專案&gt; \
///     -executeMethod SpriteSheetImporter.ImportAndValidate -logFile &lt;log&gt;
/// 失敗時以 exit code 1 結束。
/// </summary>
public static class SpriteSheetImporter
{
    public const string SpritesRoot = "Assets/Art/Glowshaft/Sprites";
    public const string NormalMapName = "_NormalMap";
    private const string LogPrefix = "[SpriteSheetImporter] ";

    // JsonUtility 依欄位名稱對應 json；只列用得到的欄位
    [Serializable]
    private class Manifest
    {
        public string name;
        public int cellSize;
        public string[] directions;
        public float fps;
        public float suggestedPixelsPerUnit;
        public float[] pivot;
        public Sheet[] sheets;
        public int pixelsPerUnit;  // art 新增的欄位：全素材統一的整數 PPU（必填）
        public bool tile;          // art 新增的欄位：可平鋪的 tile（FullRect mesh）
    }

    [Serializable]
    private class Sheet
    {
        public string action;
        public int columns;
        public int rows;
        public string color;
        public string normal;
    }

    [MenuItem("Tools/Glowshaft/匯入 Sprite Sheet", false, 100)]
    public static void ImportAllMenu()
    {
        int failures = ImportAllInternal();
        failures += ValidateAllInternal();
        EditorUtility.DisplayDialog("Sprite Sheet 匯入", failures == 0 ? "全部匯入並驗證通過。" : $"有 {failures} 個問題，請看 Console。", "OK");
    }

    [MenuItem("Tools/Glowshaft/檢查 Sprite Sheet 匯入結果", false, 101)]
    public static void ValidateAllMenu()
    {
        int failures = ValidateAllInternal();
        EditorUtility.DisplayDialog("Sprite Sheet 檢查", failures == 0 ? "全部正確。" : $"有 {failures} 個問題，請看 Console。", "OK");
    }

    /// <summary>batchmode 入口：匯入全部，失敗時 exit 1。</summary>
    public static void ImportAll() => ExitOnFailure(ImportAllInternal());

    /// <summary>batchmode 入口：只檢查，不改設定，失敗時 exit 1。</summary>
    public static void ValidateAll() => ExitOnFailure(ValidateAllInternal());

    /// <summary>batchmode 入口：匯入後再檢查一次，任一步失敗都 exit 1。</summary>
    public static void ImportAndValidate()
    {
        int failures = ImportAllInternal();
        failures += ValidateAllInternal();
        ExitOnFailure(failures);
    }

    private static void ExitOnFailure(int failures)
    {
        if (failures > 0 && Application.isBatchMode)
            EditorApplication.Exit(1);
    }

    #region 匯入

    private static int ImportAllInternal()
    {
        AssetDatabase.Refresh();  // Blender 剛寫進來的檔案要先讓 Unity 看到
        int failures = 0, imported = 0;
        foreach (string jsonPath in FindManifests())
        {
            Manifest m = LoadManifest(jsonPath, out string problem);
            if (m == null)
            {
                failures++;
                Debug.LogError(LogPrefix + $"無法匯入 {jsonPath}：{problem}");
                continue;
            }
            try
            {
                foreach (Sheet sheet in m.sheets)
                    ImportSheet(Path.GetDirectoryName(jsonPath).Replace('\\', '/'), m, sheet);
                imported++;
            }
            catch (Exception e)
            {
                failures++;
                Debug.LogError(LogPrefix + $"匯入 {jsonPath} 失敗：{e}");
            }
        }
        Debug.Log(LogPrefix + $"匯入完成：{imported} 份 json，{failures} 份失敗");
        return failures;
    }

    private static void ImportSheet(string dir, Manifest m, Sheet sheet)
    {
        string colorPath = $"{dir}/{sheet.color}";
        string normalPath = $"{dir}/{sheet.normal}";
        int width = sheet.columns * m.cellSize;
        int height = sheet.rows * m.cellSize;
        int maxSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height)), 32, 16384);

        var factory = new SpriteDataProviderFactories();
        factory.Init();

        // 先匯入法線 sheet，顏色 sheet 的 secondary texture 才拿得到 Normal map 型別的貼圖
        var normalImporter = GetImporter(normalPath);
        // Unity 6 的 2D 專案預設把新 PNG 匯入成 Multiple 並自動切圖；只改型別的話，那些 rect 會留在 .meta。
        // 不能用 ISpriteEditorDataProvider 清：它只在 Sprite／Multiple 時才寫入 rect，NormalMap 型別會被略過。
        ClearSpriteSheet(normalImporter);
        normalImporter.spriteImportMode = SpriteImportMode.None;
        normalImporter.textureType = TextureImporterType.NormalMap;
        normalImporter.convertToNormalmap = false;
        ApplyCommon(normalImporter, maxSize);
        normalImporter.SaveAndReimport();
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (normalTex == null)
            throw new InvalidOperationException($"載入不到法線貼圖 {normalPath}");

        var importer = GetImporter(colorPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Ppu(m);
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        ApplyCommon(importer, maxSize);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = m.tile ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);

        ISpriteEditorDataProvider dp = factory.GetSpriteEditorDataProviderFromObject(importer);
        dp.InitSpriteEditorDataProvider();

        // 同名 sprite 沿用舊 ID，重跑不會讓已存在的引用失效
        var oldIds = new Dictionary<string, GUID>();
        foreach (SpriteRect r in dp.GetSpriteRects())
            oldIds[r.name] = r.spriteID;

        string baseName = Path.GetFileNameWithoutExtension(sheet.color);
        var pivot = new Vector2(m.pivot[0], m.pivot[1]);
        var rects = new List<SpriteRect>(sheet.rows * sheet.columns);
        for (int row = 0; row < sheet.rows; row++)
        {
            for (int col = 0; col < sheet.columns; col++)
            {
                string spriteName = $"{baseName}_{row * sheet.columns + col}";
                rects.Add(new SpriteRect
                {
                    name = spriteName,
                    // Unity 的 rect 原點在左下；sheet 第 0 列在最上面
                    rect = new Rect(col * m.cellSize, height - (row + 1) * m.cellSize, m.cellSize, m.cellSize),
                    alignment = SpriteAlignment.Custom,
                    pivot = pivot,
                    border = Vector4.zero,
                    spriteID = oldIds.TryGetValue(spriteName, out GUID id) ? id : GUID.Generate(),
                });
            }
        }
        dp.SetSpriteRects(rects.ToArray());

        var nameIds = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameIds?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));

        var secondary = dp.GetDataProvider<ISecondaryTextureDataProvider>();
        secondary.textures = new[] { new SecondarySpriteTexture { name = NormalMapName, texture = normalTex } };

        dp.Apply();
        importer.SaveAndReimport();
        Debug.Log(LogPrefix + $"{colorPath}：{rects.Count} 格，PPU {Ppu(m)}，pivot ({pivot.x}, {pivot.y})");
    }

    // .meta 裡存切圖資料的欄位；法線 sheet 這三個都必須是空的
    private static readonly string[] SpriteSheetArrays =
    {
        "m_SpriteSheet.m_Sprites", "m_SpriteSheet.m_NameFileIdTable", "m_InternalIDToNameTable",
    };

    /// <summary>直接清掉 .meta 的切圖資料（不管目前的 Texture Type），找不到欄位就丟例外，不默默略過。</summary>
    private static void ClearSpriteSheet(TextureImporter importer)
    {
        var so = new SerializedObject(importer);
        foreach (string name in SpriteSheetArrays)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null || !p.isArray)
                throw new InvalidOperationException($"TextureImporter 找不到陣列欄位 {name}（Unity 版本變了？）");
            p.ClearArray();
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>.meta 裡殘留的切圖資料筆數；欄位不存在回傳 -1。</summary>
    private static int SpriteSheetEntries(TextureImporter importer)
    {
        var so = new SerializedObject(importer);
        int total = 0;
        foreach (string name in SpriteSheetArrays)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null || !p.isArray)
                return -1;
            total += p.arraySize;
        }
        return total;
    }

    private static void ApplyCommon(TextureImporter importer, int maxSize)
    {
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = maxSize;
        // Normal map 型別預設會把非 2 次方尺寸（例如 1280、384）縮成 2 次方，格子就對不上顏色 sheet
        importer.npotScale = TextureImporterNPOTScale.None;
        // 不壓縮：法線值要原樣保留，sheet 也不大；之後要省記憶體再改 CompressedHQ
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;
    }
    #endregion

    #region 檢查

    private static int ValidateAllInternal()
    {
        int failures = 0, sheets = 0;
        var ppus = new HashSet<int>();
        foreach (string jsonPath in FindManifests())
        {
            Manifest m = LoadManifest(jsonPath, out string problem);
            if (m == null)
            {
                failures++;
                Debug.LogError(LogPrefix + $"{jsonPath}：{problem}");
                continue;
            }
            ppus.Add(m.pixelsPerUnit);
            string dir = Path.GetDirectoryName(jsonPath).Replace('\\', '/');
            foreach (Sheet sheet in m.sheets)
            {
                var errors = ValidateSheet(dir, m, sheet);
                sheets++;
                if (errors.Count == 0)
                {
                    Debug.Log(LogPrefix + $"OK {sheet.color}：{sheet.rows * sheet.columns} 格、pivot、PPU、{NormalMapName} 正確");
                    continue;
                }
                failures += errors.Count;
                foreach (string e in errors)
                    Debug.LogError(LogPrefix + $"{dir}/{sheet.color}：{e}");
            }
        }
        if (ppus.Count > 1)
        {
            failures++;
            Debug.LogError(LogPrefix + $"各素材的 pixelsPerUnit 不一致：{string.Join(", ", ppus)}（規格要求全部相同）");
        }
        Debug.Log(LogPrefix + $"檢查完成：{sheets} 張 sheet，{failures} 個問題");
        return failures;
    }

    private static List<string> ValidateSheet(string dir, Manifest m, Sheet sheet)
    {
        var errors = new List<string>();
        string colorPath = $"{dir}/{sheet.color}";
        string normalPath = $"{dir}/{sheet.normal}";
        var importer = AssetImporter.GetAtPath(colorPath) as TextureImporter;
        var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (importer == null || normalImporter == null)
        {
            errors.Add("找不到顏色或法線 sheet");
            return errors;
        }
        if (normalImporter.textureType != TextureImporterType.NormalMap)
            errors.Add($"法線 sheet 的 Texture Type 是 {normalImporter.textureType}，應為 NormalMap");
        int normalSprites = AssetDatabase.LoadAllAssetsAtPath(normalPath).OfType<Sprite>().Count();
        if (normalSprites > 0 || normalImporter.spriteImportMode != SpriteImportMode.None)
            errors.Add($"法線 sheet 不該切圖（目前 {normalSprites} 張 sprite，Sprite Mode {normalImporter.spriteImportMode}）");
        int leftover = SpriteSheetEntries(normalImporter);
        if (leftover != 0)
            errors.Add($"法線 sheet 的 .meta 還有 {leftover} 筆切圖資料（{string.Join("、", SpriteSheetArrays)}）");
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple)
            errors.Add("顏色 sheet 不是 Sprite／Multiple");
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        var meshType = m.tile ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
        if (settings.spriteMeshType != meshType)
            errors.Add($"Mesh Type 是 {settings.spriteMeshType}，應為 {meshType}");

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        int width = sheet.columns * m.cellSize, height = sheet.rows * m.cellSize;
        if (tex == null || tex.width != width || tex.height != height)
            errors.Add($"顏色 sheet 尺寸 {tex?.width}x{tex?.height}，應為 {width}x{height}（Max Size 太小會被縮圖）");
        if (normalTex == null || normalTex.width != width || normalTex.height != height)
            errors.Add($"法線 sheet 尺寸 {normalTex?.width}x{normalTex?.height}，應為 {width}x{height}");

        string baseName = Path.GetFileNameWithoutExtension(sheet.color);
        var sprites = AssetDatabase.LoadAllAssetsAtPath(colorPath).OfType<Sprite>().ToDictionary(s => s.name);
        int expected = sheet.rows * sheet.columns;
        if (sprites.Count != expected)
            errors.Add($"切出 {sprites.Count} 張 sprite，應為 {expected}");
        var pivot = new Vector2(m.pivot[0], m.pivot[1]);
        var buffer = new SecondarySpriteTexture[4];
        for (int i = 0; i < expected; i++)
        {
            string spriteName = $"{baseName}_{i}";
            if (!sprites.TryGetValue(spriteName, out Sprite s))
            {
                errors.Add($"缺少 {spriteName}");
                continue;
            }
            int row = i / sheet.columns, col = i % sheet.columns;
            var rect = new Rect(col * m.cellSize, height - (row + 1) * m.cellSize, m.cellSize, m.cellSize);
            if (s.rect != rect)
                errors.Add($"{spriteName} 的 rect {s.rect}，應為 {rect}");
            Vector2 p = s.pivot / s.rect.size;
            if ((p - pivot).sqrMagnitude > 1e-6f)
                errors.Add($"{spriteName} 的 pivot ({p.x:F4}, {p.y:F4})，應為 ({pivot.x}, {pivot.y})");
            if (Mathf.Abs(s.pixelsPerUnit - Ppu(m)) > 1e-3f)
                errors.Add($"{spriteName} 的 PPU {s.pixelsPerUnit}，應為 {Ppu(m)}");
            int count = s.GetSecondaryTextures(buffer);
            if (count != 1 || buffer[0].name != NormalMapName || buffer[0].texture != normalTex)
                errors.Add($"{spriteName} 的 secondary texture 不是 {NormalMapName}＝{sheet.normal}");
        }
        return errors;
    }
    #endregion

    #region 共用

    private static IEnumerable<string> FindManifests()
    {
        if (!AssetDatabase.IsValidFolder(SpritesRoot))
            return Enumerable.Empty<string>();
        return AssetDatabase.FindAssets("t:TextAsset", new[] { SpritesRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith("_sprites.json", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    private static Manifest LoadManifest(string jsonPath, out string problem)
    {
        problem = null;
        Manifest m;
        try
        {
            m = JsonUtility.FromJson<Manifest>(File.ReadAllText(jsonPath));
        }
        catch (Exception e)
        {
            problem = $"json 解析失敗（{e.Message}）";
            return null;
        }
        if (m == null || m.cellSize <= 0 || m.sheets == null || m.sheets.Length == 0 || m.pivot == null || m.pivot.Length < 2)
            problem = "不是 render_sprites.py 的輸出（缺 cellSize／sheets／pivot）";
        else if (m.sheets.Any(s => string.IsNullOrEmpty(s.color) || string.IsNullOrEmpty(s.normal) || s.rows <= 0 || s.columns <= 0))
            problem = "有 sheet 缺 color／normal／rows／columns";
        else if (m.pixelsPerUnit <= 0)
            problem = "沒有 pixelsPerUnit（請用 Tools/Blender/Glowshaft/render_all.py 產生，不要直接放 render_sprites.py 的原始輸出）";
        return problem == null ? m : null;
    }

    private static float Ppu(Manifest m) => m.pixelsPerUnit;

    private static TextureImporter GetImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new FileNotFoundException($"找不到貼圖 {path}（Blender 輸出的檔名與 json 不一致？）");
        return importer;
    }
    #endregion
}
