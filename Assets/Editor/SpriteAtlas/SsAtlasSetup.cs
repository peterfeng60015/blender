using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// 建立／更新《熱浪急件》的 UI 圖集（Sprite Atlas V2）：UI 類 sheet 合成一張貼圖，畫面上的 UI 可以合批。
///
/// 圖集放在 <see cref="AtlasRoot"/>/&lt;Name&gt;/&lt;Name&gt;.spriteatlasv2，由 AssetPackageCollectorSetup 的
/// sidescroller_atlases group 收進 AssetPackage（每個圖集一包）。sprite 照舊從各自的 bundle 讀（SsArt 不用改），
/// 打包時 TrackSpriteAtlasDependencies 會讓這些 bundle 依賴圖集那一包，載入 sprite 時圖集貼圖跟著載入。
///
/// 只加主圖（不加 _n 法線圖）：法線圖是主圖的 Secondary Texture「_NormalMap」，圖集會自動一起打包。
/// 選單主視覺（SsMenuBg、SsMenuHero）是整張大圖，放進圖集沒有好處，不收。
/// 打包設定：UI 的 Image 不支援旋轉與 tight packing（四邊形會蓋到鄰圖），都關掉。
///
/// 可重複執行（內容與設定以這份程式為準）。素材換圖不用重跑：圖集收的是貼圖，打包時自動重新打包。
/// 新增 UI 類 sheet 時加進 <see cref="UiSheets"/> 再跑一次。
/// batchmode：Unity -batchmode -quit -projectPath &lt;專案&gt; -executeMethod SsAtlasSetup.SetupAll -logFile &lt;log&gt;
/// </summary>
public static class SsAtlasSetup
{
    public const string AtlasRoot = "Assets/Art/Sidescroller/Atlases";
    private const string SpritesRoot = "Assets/Art/Sidescroller/Sprites";
    private const string LogPrefix = "[SsAtlasSetup] ";

    public const string UiAtlasName = "SsUiAtlas";

    /// <summary>UI 圖集收的 sheet（Sprites/&lt;資料夾&gt;/&lt;檔名&gt;.png）。</summary>
    private static readonly string[] UiSheets =
    {
        "SsUi/SsUi_Idle",
        "SsUiSkin/SsUiSkin_Idle",
        "SsTouchUi/SsTouchUi_Idle",
        "SsMenuLogoBg/SsMenuLogoBg_Idle",
        "SsPortraitHero/SsPortraitHero_Face",
        "SsPortraitBoss/SsPortraitBoss_Face",
        "SsPortraitMentor/SsPortraitMentor_Face",
        "SsPortraitRat/SsPortraitRat_Face",
    };

    [MenuItem("Tools/SpriteAtlas/建立或更新 UI 圖集", false, 1)]
    public static void SetupMenuItem()
    {
        var path = SetupUiAtlas();
        EditorUtility.DisplayDialog("SpriteAtlas", $"UI 圖集已更新：{path}", "確定");
    }

    /// <summary>batchmode 入口：失敗時丟例外（Unity 以非 0 結束碼退出）。</summary>
    public static void SetupAll()
    {
        SetupUiAtlas();
    }

    public static string SetupUiAtlas()
    {
        var textures = new List<Texture2D>();
        foreach (var sheet in UiSheets)
        {
            var texPath = $"{SpritesRoot}/{sheet}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
                throw new InvalidOperationException(LogPrefix + $"找不到 {texPath}");
            textures.Add(tex);
        }

        var folder = $"{AtlasRoot}/{UiAtlasName}";
        EnsureFolder(folder);
        var path = $"{folder}/{UiAtlasName}.spriteatlasv2";

        // 每次新建內容覆寫：清單以這份程式為準（.meta 與 GUID 保留；V2 的打包設定在 importer 上，下面重設）
        var atlas = new SpriteAtlasAsset();
        atlas.Add(textures.Cast<UnityEngine.Object>().ToArray());
        SpriteAtlasAsset.Save(atlas, path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(path);
        importer.includeInBuild = true;
        importer.packingSettings = new SpriteAtlasPackingSettings
        {
            blockOffset = 1,
            padding = 4,
            enableRotation = false,
            enableTightPacking = false,
            enableAlphaDilation = true,
        };
        importer.textureSettings = new SpriteAtlasTextureSettings
        {
            readable = false,
            generateMipMaps = false,
            sRGB = true,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 0,
        };
        // 與各 sheet 的匯入設定一致：上限 2048、一般壓縮（各平台自動選格式）
        var platform = importer.GetPlatformSettings("DefaultTexturePlatform");
        platform.maxTextureSize = 2048;
        platform.textureCompression = TextureImporterCompression.Compressed;
        importer.SetPlatformSettings(platform);
        importer.SaveAndReimport();

        // 打包預覽：確認每張 sheet 都進了圖集、貼圖頁數
        SpriteAtlasUtility.PackAtlases(new[] { AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path) }, EditorUserBuildSettings.activeBuildTarget);
        var spriteAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
        if (spriteAtlas == null)
            throw new InvalidOperationException(LogPrefix + $"{path} 匯入後讀不到 SpriteAtlas");
        var expected = 0;
        foreach (var tex in textures)
            expected += AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(tex)).OfType<Sprite>().Count();
        if (spriteAtlas.spriteCount != expected)
            throw new InvalidOperationException(LogPrefix + $"{UiAtlasName} 收了 {spriteAtlas.spriteCount} 個 sprite，預期 {expected} 個");

        Debug.Log(LogPrefix + $"{path}：{textures.Count} 張 sheet、{spriteAtlas.spriteCount} 個 sprite");
        return path;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
