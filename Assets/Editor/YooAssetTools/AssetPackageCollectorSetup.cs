using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

/// <summary>
/// 以程式建立/更新 YooAsset 的 AssetPackage（美術素材與資料）收集器設定。
/// 寫法與理由同 <see cref="CodePackageCollectorSetup"/>：走 API 不手寫 YAML、可重複執行、不寫 CollectorGUID。
///
/// 同一個 package（<see cref="PackageName"/>，GameAssets 只認這個）底下三個 group，規則都相同：
///   - glowshaft_sprites：Assets/Art/Glowshaft/Sprites（螢火礦坑）。
///   - sidescroller_sprites：Assets/Art/Sidescroller/Sprites（熱浪急件的 sprite sheet）。
///   - sidescroller_data：Assets/Art/Sidescroller/Data（演出腳本、關卡資料；Cutscenes、Levels 各一包）。
///   - PackTopDirectory：底下每個子資料夾一包（Miner、SsHeroDay、Cutscenes…），改一個只會重下那一包。
///   - AddressByFileName：定位地址＝檔名不含副檔名（Miner_Run、SsHeroDay_Run_n、SsLevel_Stage1）。
///   - CollectAll：png（主圖與法線圖 _n，各自是獨立的 sprite 貼圖）、json、txt 都收。
///
/// Sidescroller 的兩個 group 只在收集路徑存在時才加入（不存在就警告並略過）：
/// 資料還沒落地時 Glowshaft 照樣能打包與 Editor 模擬。資料夾第一次出現後要重跑一次選單
/// （Editor 模擬模式讀的是已存檔的設定，不會自動重跑；打包則每次自動跑）。
/// ⚠️ 反過來，已存檔的設定含 Sidescroller group、資料夾卻被移走時，YooAsset 收集會丟 "Collect path is invalid"，
/// Editor 模擬連 Glowshaft 都進不去，要重跑選單；所以這份設定檔要和 Sidescroller 資料夾一起 commit。
///
/// 素材規範（違反時模擬建置與打包都會失敗，<see cref="AssetPackageBuildTool"/> 打包前會先檢查並列出）：
///   - 收集根目錄只能放子資料夾，不能直接放檔案（PackTopDirectory 丟 "Root directory not found"）。
///   - 子資料夾名稱不能含 '.'（Path.HasExtension 會把它當成檔案，同樣丟例外）。
///   - 整個 package 內檔名（不含副檔名）必須唯一（AddressByFileName 撞名丟 "Address already exists"）。
///   - Sidescroller 的檔名與 Sprites 子資料夾名一律 Ss 開頭（Glowshaft 沒有 Ss 開頭的檔，加前綴就不會撞名）。
///
/// 只動 AssetPackage 這一筆，CodePackage 的設定由 CodePackageCollectorSetup 負責，兩者互不覆蓋。
/// ⚠️ 這一筆每次都整筆重建：在 YooAsset 收集器視窗手動加的 group／collector 會被覆蓋，要改請改這份程式。
/// </summary>
public static class AssetPackageCollectorSetup
{
    public const string PackageName = "AssetPackage";

    public const string SpritesCollectPath = "Assets/Art/Glowshaft/Sprites";
    public const string SidescrollerSpritesCollectPath = "Assets/Art/Sidescroller/Sprites";
    public const string SidescrollerDataCollectPath = "Assets/Art/Sidescroller/Data";

    // 與 CodePackageCollectorSetup 同一份設定檔（專案內只能有一份）。
    private const string SettingAssetPath = "Assets/BundleCollectorSetting.asset";

    #region Group 定義

    /// <summary>一個 group（一個 collector）的定義。</summary>
    internal sealed class GroupSpec
    {
        public string GroupName;
        public string GroupDesc;
        public string CollectPath;

        /// <summary>true：收集路徑不存在時略過這個 group；false：照樣寫入（打包時再報錯）。</summary>
        public bool Optional;

        /// <summary>檔名必須以這個前綴開頭；null 表示不檢查。</summary>
        public string RequiredFilePrefix;

        /// <summary>根目錄底下的子資料夾名也必須以 <see cref="RequiredFilePrefix"/> 開頭（Sprites 的 &lt;Name&gt;）。</summary>
        public bool FolderNeedsPrefix;
    }

    internal static readonly GroupSpec[] DefaultGroups =
    {
        new GroupSpec
        {
            GroupName = "glowshaft_sprites",
            GroupDesc = "每個 <Name> 子資料夾一包",
            CollectPath = SpritesCollectPath,
            Optional = false,
        },
        new GroupSpec
        {
            GroupName = "sidescroller_sprites",
            GroupDesc = "每個 <Name> 子資料夾一包（Ss 開頭）",
            CollectPath = SidescrollerSpritesCollectPath,
            Optional = true,
            RequiredFilePrefix = "Ss",
            FolderNeedsPrefix = true,
        },
        new GroupSpec
        {
            GroupName = "sidescroller_data",
            GroupDesc = "Cutscenes、Levels 各一包（檔名 Ss 開頭）",
            CollectPath = SidescrollerDataCollectPath,
            Optional = true,
            RequiredFilePrefix = "Ss",
        },
    };

    /// <summary>可選且收集路徑不存在的 group 不寫進設定（Setup 與打包前檢查共用這個判斷，範圍才不會脫節）。</summary>
    private static bool IsSkipped(GroupSpec spec)
    {
        return spec.Optional && !AssetDatabase.IsValidFolder(spec.CollectPath);
    }

    /// <summary>實際會寫進設定的 group。</summary>
    internal static List<GroupSpec> ActiveGroups(IReadOnlyList<GroupSpec> specs)
    {
        var result = new List<GroupSpec>();
        foreach (var spec in specs)
        {
            if (!IsSkipped(spec))
                result.Add(spec);
        }
        return result;
    }

    #endregion

    [MenuItem("Tools/YooAsset/建立或更新 AssetPackage 收集器設定", false, 11)]
    public static void SetupMenuItem()
    {
        Setup();
        EditorUtility.DisplayDialog("AssetPackage", $"收集器設定已就緒：{SettingAssetPath}", "確定");
    }

    /// <summary>
    /// 建立或更新 AssetPackage 的收集器設定。可重複執行。
    ///
    /// Glowshaft 的收集路徑還不存在時照樣寫入設定，但略過 CheckConfigError 並警告：
    /// 這個資料夾歸 art 管，這裡不替它建空資料夾。此時打包會失敗、Editor 模擬模式會略過 AssetPackage。
    /// Sidescroller 的收集路徑不存在時整個 group 略過（見類別註解）。
    /// </summary>
    public static void Setup()
    {
        Setup(DefaultGroups);
    }

    /// <summary>以指定的 group 定義重建 AssetPackage（測試時可傳入暫時的定義，測完再呼叫 <see cref="Setup()"/> 還原）。</summary>
    internal static void Setup(IReadOnlyList<GroupSpec> specs)
    {
        var setting = LoadOrCreateSetting();

        // 原位替換而不是 RemoveAll + Add：與 CodePackageCollectorSetup 輪流執行時，
        // package 的順序才不會來回變動、讓 BundleCollectorSetting.asset 產生無意義的 diff。
        var index = setting.Packages.FindIndex(p => p.PackageName == PackageName);
        setting.Packages.RemoveAll(p => p.PackageName == PackageName);

        var package = new BundleCollectorPackage
        {
            PackageName = PackageName,
            PackageDesc = "Glowshaft／Sidescroller 美術素材與資料（sprite sheet、法線圖、json、txt），ScriptableBuildPipeline",
            EnableAddressable = true,
            SupportExtensionless = false,
            LocationToLower = false,
            IncludeAssetGUID = false,
            // 目前只有貼圖與文字；日後若收進材質，引用的 shader 會自動集中成一包
            AutoCollectShaders = true,
        };

        var summary = new List<string>();
        var allPathsExist = true;
        foreach (var spec in specs)
        {
            if (IsSkipped(spec))
            {
                Debug.LogWarning($"[AssetPackageCollectorSetup] 收集路徑不存在：{spec.CollectPath}，略過 group {spec.GroupName}" +
                                 "（資料落地後請重跑選單 Tools/YooAsset/建立或更新 AssetPackage 收集器設定）。");
                continue;
            }

            var group = new BundleCollectorGroup
            {
                GroupName = spec.GroupName,
                GroupDesc = spec.GroupDesc,
            };
            var collector = new BundleCollector
            {
                CollectPath = spec.CollectPath,
                CollectorGUID = string.Empty,
                CollectorType = ECollectorType.MainAssetCollector,
                PackRuleName = nameof(PackTopDirectory),
                AddressRuleName = nameof(AddressByFileName),
                FilterRuleName = nameof(CollectAll),
                AssetTags = string.Empty,
            };
            group.Collectors.Add(collector);
            package.Groups.Add(group);

            if (!AssetDatabase.IsValidFolder(spec.CollectPath))
            {
                allPathsExist = false;
                Debug.LogWarning($"[AssetPackageCollectorSetup] 收集路徑不存在：{spec.CollectPath}（素材尚未落地），" +
                                 "設定已寫入，但在素材放進去之前無法打包 AssetPackage。");
            }
            summary.Add($"{spec.GroupName}={spec.CollectPath}");
        }

        if (index >= 0 && index <= setting.Packages.Count)
            setting.Packages.Insert(index, package);
        else
            setting.Packages.Add(package);

        // 收集路徑不存在時 CheckConfigError 一定丟例外，所以只在全部存在時才檢查（同改版前的行為）
        if (allPathsExist)
            package.CheckConfigError();

        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AssetPackageCollectorSetup] 已寫入 {SettingAssetPath}：package={PackageName}（PackTopDirectory / AddressByFileName）\n  " +
                  string.Join("\n  ", summary));
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
