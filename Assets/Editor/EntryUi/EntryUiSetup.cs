using System;
using EntryScene;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Entry 載入畫面的素材設定：Assets/Entry/UI 的圖（Tools/ComfyUI 的 entry_* 產生）設成 sprite（九宮格的設邊界），
/// 再把圖與中文字型指派到 Entry 場景的 EntrySceneController.loadingAssets（場景引用，跟主體一起打包）。
/// 字型用除錯主控台也在用的思源黑體（同一份資產，主體不會多一份）。
/// 素材重新產生後不用重跑（GUID 不變）；換檔名或新增素材時改這裡再跑一次。
/// batchmode：Unity -batchmode -quit -projectPath &lt;專案&gt; -executeMethod EntryUiSetup.Setup -logFile &lt;log&gt;
/// </summary>
public static class EntryUiSetup
{
    private const string UiRoot = "Assets/Entry/UI";
    private const string EntryScenePath = "Assets/Scenes/Entry.unity";
    private const string FontPath = "Assets/Plugins/IngameDebugConsole/Fonts & Materials/Chinese/思源黑體-Medium.asset";
    private const string LogPrefix = "[EntryUiSetup] ";

    /// <summary>(檔名, 九宮格邊界 左下右上 px, 貼圖上限)</summary>
    private static readonly (string Name, Vector4 Border, int MaxSize)[] Sprites =
    {
        ("EntryLoadingBg", Vector4.zero, 2048),
        ("EntryBarFrame", new Vector4(64, 0, 64, 0), 1024),
        ("EntryBarFill", new Vector4(48, 0, 48, 0), 1024),
        ("EntryPanel", new Vector4(120, 120, 120, 120), 1024),
        ("EntryLogo", Vector4.zero, 512),
    };

    [MenuItem("Tools/Entry/設定載入畫面", false, 1)]
    public static void SetupMenuItem()
    {
        Setup();
        EditorUtility.DisplayDialog("Entry", "載入畫面素材已設定並指派到 Entry 場景", "確定");
    }

    public static void Setup()
    {
        var sprites = new Sprite[Sprites.Length];
        for (var i = 0; i < Sprites.Length; i++)
            sprites[i] = ImportSprite(Sprites[i].Name, Sprites[i].Border, Sprites[i].MaxSize);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new InvalidOperationException(LogPrefix + $"找不到中文字型 {FontPath}");

        var scene = EditorSceneManager.OpenScene(EntryScenePath, OpenSceneMode.Single);
        var controller = UnityEngine.Object.FindFirstObjectByType<EntrySceneController>();
        if (controller == null)
            throw new InvalidOperationException(LogPrefix + $"{EntryScenePath} 裡找不到 EntrySceneController");
        var so = new SerializedObject(controller);
        var assets = so.FindProperty("loadingAssets");
        assets.FindPropertyRelative("font").objectReferenceValue = font;
        assets.FindPropertyRelative("background").objectReferenceValue = sprites[0];
        assets.FindPropertyRelative("barFrame").objectReferenceValue = sprites[1];
        assets.FindPropertyRelative("barFill").objectReferenceValue = sprites[2];
        assets.FindPropertyRelative("panel").objectReferenceValue = sprites[3];
        assets.FindPropertyRelative("logo").objectReferenceValue = sprites[4];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(LogPrefix + $"已指派 {Sprites.Length} 張圖與字型 {font.name} 到 {EntryScenePath}");
    }

    private static Sprite ImportSprite(string name, Vector4 border, int maxSize)
    {
        var path = $"{UiRoot}/{name}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException(LogPrefix + $"找不到 {path}（先跑 Tools/ComfyUI 的 generate.py 與 postprocess.py）");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = maxSize;
        importer.textureCompression = TextureImporterCompression.Compressed;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; // 九宮格要 FullRect
        settings.spriteBorder = border;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            throw new InvalidOperationException(LogPrefix + $"{path} 匯入後讀不到 sprite");
        return sprite;
    }
}
