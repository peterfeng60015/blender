using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

/// <summary>
/// 以程式建立/更新 YooAsset 的 AssetPackage（美術素材）收集器設定。
/// 寫法與理由同 <see cref="CodePackageCollectorSetup"/>：走 API 不手寫 YAML、可重複執行、不寫 CollectorGUID。
///
/// 收集 Assets/Art/Glowshaft/Sprites（共同規則的介面約定）：
///   - PackTopDirectory：底下每個 &lt;Name&gt; 子資料夾一包（Miner、SlimeGreen…），
///     改一個角色只會重下那一包。
///   - AddressByFileName：定位地址＝檔名不含副檔名（Miner_Run、Miner_Run_n、Miner_sprites）。
///   - CollectAll：png（主圖與法線圖 _n，各自是獨立的 sprite 貼圖）與 json 都收，同一個 &lt;Name&gt; 資料夾在同一包。
///
/// 素材規範（違反時模擬建置與打包都會失敗，<see cref="AssetPackageBuildTool"/> 打包前會先檢查並列出）：
///   - Sprites 根目錄只能放子資料夾，不能直接放檔案（PackTopDirectory 丟 "Root directory not found"）。
///   - 子資料夾名稱不能含 '.'（Path.HasExtension 會把它當成檔案，同樣丟例外）。
///   - 整個 package 內檔名（不含副檔名）必須唯一（AddressByFileName 撞名丟 "Address already exists"）。
///
/// 只動 AssetPackage 這一筆，CodePackage 的設定由 CodePackageCollectorSetup 負責，兩者互不覆蓋。
/// ⚠️ 這一筆每次都整筆重建：在 YooAsset 收集器視窗手動加的 group／collector 會被覆蓋，要改請改這份程式。
/// </summary>
public static class AssetPackageCollectorSetup
{
    public const string PackageName = "AssetPackage";

    private const string GroupName = "glowshaft_sprites";
    public const string SpritesCollectPath = "Assets/Art/Glowshaft/Sprites";

    // 與 CodePackageCollectorSetup 同一份設定檔（專案內只能有一份）。
    private const string SettingAssetPath = "Assets/BundleCollectorSetting.asset";

    [MenuItem("Tools/YooAsset/建立或更新 AssetPackage 收集器設定", false, 11)]
    public static void SetupMenuItem()
    {
        Setup();
        EditorUtility.DisplayDialog("AssetPackage", $"收集器設定已就緒：{SettingAssetPath}", "確定");
    }

    /// <summary>
    /// 建立或更新 AssetPackage 的收集器設定。可重複執行。
    ///
    /// 收集路徑還不存在（art 素材尚未落地）時照樣寫入設定，但略過 CheckConfigError 並警告：
    /// 這個資料夾歸 art 管，這裡不替它建空資料夾。此時打包會失敗、Editor 模擬模式會略過 AssetPackage。
    /// </summary>
    public static void Setup()
    {
        var setting = LoadOrCreateSetting();

        // 原位替換而不是 RemoveAll + Add：與 CodePackageCollectorSetup 輪流執行時，
        // package 的順序才不會來回變動、讓 BundleCollectorSetting.asset 產生無意義的 diff。
        var index = setting.Packages.FindIndex(p => p.PackageName == PackageName);
        setting.Packages.RemoveAll(p => p.PackageName == PackageName);

        var package = new BundleCollectorPackage
        {
            PackageName = PackageName,
            PackageDesc = "Glowshaft 美術素材（sprite sheet、法線圖、json），ScriptableBuildPipeline",
            EnableAddressable = true,
            SupportExtensionless = false,
            LocationToLower = false,
            IncludeAssetGUID = false,
            // 目前只有貼圖與 json；日後若收進材質，引用的 shader 會自動集中成一包
            AutoCollectShaders = true,
        };

        var group = new BundleCollectorGroup
        {
            GroupName = GroupName,
            GroupDesc = "每個 <Name> 子資料夾一包",
        };
        group.Collectors.Add(new BundleCollector
        {
            CollectPath = SpritesCollectPath,
            CollectorGUID = string.Empty,
            CollectorType = ECollectorType.MainAssetCollector,
            PackRuleName = nameof(PackTopDirectory),
            AddressRuleName = nameof(AddressByFileName),
            FilterRuleName = nameof(CollectAll),
            AssetTags = string.Empty,
        });
        package.Groups.Add(group);

        if (index >= 0 && index <= setting.Packages.Count)
            setting.Packages.Insert(index, package);
        else
            setting.Packages.Add(package);

        if (AssetDatabase.IsValidFolder(SpritesCollectPath))
            package.CheckConfigError();
        else
            Debug.LogWarning($"[AssetPackageCollectorSetup] 收集路徑不存在：{SpritesCollectPath}（art 素材尚未落地），" +
                             "設定已寫入，但在素材放進去之前無法打包 AssetPackage。");

        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AssetPackageCollectorSetup] 已寫入 {SettingAssetPath}：package={PackageName}, group={GroupName}, " +
                  $"collector={SpritesCollectPath}（PackTopDirectory / AddressByFileName）");
    }

    private static BundleCollectorSetting LoadOrCreateSetting()
    {
        var guids = AssetDatabase.FindAssets($"t:{nameof(BundleCollectorSetting)}");
        if (guids.Length > 1)
        {
            var paths = new List<string>();
            foreach (var guid in guids)
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            throw new Exception($"[AssetPackageCollectorSetup] 專案內有多份 {nameof(BundleCollectorSetting)}，" +
                                $"YooAsset 會直接拋例外，請先刪到只剩一份:\n{string.Join("\n", paths)}");
        }

        if (guids.Length == 1)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var existing = AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>(path);
            if (existing == null)
                throw new Exception($"[AssetPackageCollectorSetup] 讀取失敗: {path}");
            return existing;
        }

        var setting = ScriptableObject.CreateInstance<BundleCollectorSetting>();
        AssetDatabase.CreateAsset(setting, SettingAssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AssetPackageCollectorSetup] 已新建 {SettingAssetPath}");
        return setting;
    }
}
