using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

/// <summary>
/// 以程式建立/更新 YooAsset 的 CodePackage 收集器設定。
///
/// 刻意不手寫 .asset 的 YAML：收集器設定是 ScriptableObject，手寫容易在欄位改名或
/// 新增欄位時默默失效；走 API 建立則由編譯器把關，且本方法可重複執行（idempotent），
/// fresh checkout 或設定被誤改時重跑一次就回到已知狀態。
///
/// 收集兩個資料夾（HybridCLRTools 的 ProjectDllWork / AotDllWork 輸出目的地）：
///   Assets/DllBytes/AotDllBytes     → tag "aot"
///   Assets/DllBytes/ProjectDllBytes → tag "project"
///
/// 與 HPS 不同：本專案的 .gitignore 連 Assets/DllBytes 的 .meta 都排除，資料夾 GUID 每次
/// fresh checkout 都不同，所以**不寫 CollectorGUID**（留空，YooAsset 以 CollectPath 為準）。
/// 寫了的話，BundleCollectorSetting.asset 會隨機器變動，換機後 YooAsset 還會警告
/// 「Collector GUID is invalid」並自動清掉。
/// </summary>
public static class CodePackageCollectorSetup
{
    public const string PackageName = "CodePackage";
    public const string AotTag = "aot";
    public const string ProjectTag = "project";

    private const string GroupName = "hybridclr_dll";
    public const string AotCollectPath = "Assets/DllBytes/AotDllBytes";
    public const string ProjectCollectPath = "Assets/DllBytes/ProjectDllBytes";

    // YooAsset 的 SettingLoader 找不到設定檔時會自建在 Assets/ 底下同名檔案，這裡沿用同一個位置，
    // 避免日後有人從 YooAsset 視窗又建出第二份（FindAssets 找到多份會直接 throw）。
    private const string SettingAssetPath = "Assets/BundleCollectorSetting.asset";

    [MenuItem("Tools/YooAsset/建立或更新 CodePackage 收集器設定", false, 10)]
    public static void SetupMenuItem()
    {
        Setup();
        EditorUtility.DisplayDialog("CodePackage", $"收集器設定已就緒：{SettingAssetPath}", "確定");
    }

    /// <summary>
    /// 建立或更新 CodePackage 的收集器設定。可重複執行。
    /// </summary>
    public static void Setup()
    {
        EnsureCollectFoldersExist();

        var setting = LoadOrCreateSetting();

        // 移除同名 package 後重建，確保結果與這份程式碼一致，
        // 不會殘留手動編輯過的舊 group / collector。
        // 重建後放回原本的位置（沒有才加在最後）：與 AssetPackageCollectorSetup 輪流執行時，
        // package 順序才不會來回變動、讓 BundleCollectorSetting.asset 產生無意義的 diff。
        var index = setting.Packages.FindIndex(p => p.PackageName == PackageName);
        setting.Packages.RemoveAll(p => p.PackageName == PackageName);

        var package = new BundleCollectorPackage
        {
            PackageName = PackageName,
            PackageDesc = "HybridCLR 模組 dll（aot / project），ArchiveFileBuildPipeline",
            // 以定位地址（檔名）載入：AddressByFileName 對
            // Assets/DllBytes/ProjectDllBytes/Assembly-CSharp.dll.bytes 取出的位址是 "Assembly-CSharp.dll"。
            EnableAddressable = true,
            SupportExtensionless = false,
            LocationToLower = false,
            IncludeAssetGUID = false,
            // 這個包裡只有 dll 裸檔，沒有任何 shader
            AutoCollectShaders = false,
        };

        var group = new BundleCollectorGroup
        {
            GroupName = GroupName,
            GroupDesc = "aot / project dll",
        };
        group.Collectors.Add(CreateCollector(AotCollectPath, AotTag));
        group.Collectors.Add(CreateCollector(ProjectCollectPath, ProjectTag));
        package.Groups.Add(group);

        if (index >= 0 && index <= setting.Packages.Count)
            setting.Packages.Insert(index, package);
        else
            setting.Packages.Add(package);

        // 設定合法性由 YooAsset 自己把關（收集路徑不存在、規則類別名稱打錯會在這裡就炸，
        // 而不是拖到打包當下才發現）
        package.CheckConfigError();

        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[CodePackageCollectorSetup] 已寫入 {SettingAssetPath}：" +
                  $" package={PackageName}, group={GroupName}, collectors=2" +
                  $"（{AotCollectPath} [{AotTag}] / {ProjectCollectPath} [{ProjectTag}]）");
    }

    private static BundleCollector CreateCollector(string collectPath, string tag)
    {
        return new BundleCollector
        {
            CollectPath = collectPath,
            // 刻意留空，理由見類別註解
            CollectorGUID = string.Empty,
            CollectorType = ECollectorType.MainAssetCollector,
            // 一個收集器一包（aot / project 各一個歸檔檔案），不是每顆 dll 一檔：
            // AOT 那組在只更新代碼時不會變、不會重傳；Assembly-CSharp 則每次都會變。
            // ⚠️ 不可再往上合併成一包——那會把不變的 AOT 綁進每次更新。
            PackRuleName = nameof(PackCodePackageArchive),
            AddressRuleName = nameof(AddressByFileName),
            FilterRuleName = nameof(CollectAll),
            AssetTags = tag,
        };
    }

    /// <summary>
    /// 收集路徑必須是 AssetDatabase 認得的資料夾，否則 CheckConfigError 會拋例外。
    /// Assets/DllBytes/* 不進版控（含 .meta），fresh checkout 時一定不存在，由這裡建起來，
    /// 讓設定步驟不必依賴打包順序。
    /// </summary>
    private static void EnsureCollectFoldersExist()
    {
        var created = false;
        foreach (var path in new[] { AotCollectPath, ProjectCollectPath })
        {
            if (AssetDatabase.IsValidFolder(path))
                continue;

            var full = Path.Combine(HybridCLRTools.ProjectRoot, path);
            Directory.CreateDirectory(full);
            created = true;
            Debug.LogWarning($"[CodePackageCollectorSetup] 收集路徑不存在，已建立: {path}");
        }

        if (created)
            AssetDatabase.Refresh();
    }

    private static BundleCollectorSetting LoadOrCreateSetting()
    {
        var guids = AssetDatabase.FindAssets($"t:{nameof(BundleCollectorSetting)}");
        if (guids.Length > 1)
        {
            var paths = new List<string>();
            foreach (var guid in guids)
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            throw new Exception($"[CodePackageCollectorSetup] 專案內有多份 {nameof(BundleCollectorSetting)}，" +
                                $"YooAsset 會直接拋例外，請先刪到只剩一份:\n{string.Join("\n", paths)}");
        }

        if (guids.Length == 1)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var existing = AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>(path);
            if (existing == null)
                throw new Exception($"[CodePackageCollectorSetup] 讀取失敗: {path}");
            return existing;
        }

        var setting = ScriptableObject.CreateInstance<BundleCollectorSetting>();
        AssetDatabase.CreateAsset(setting, SettingAssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CodePackageCollectorSetup] 已新建 {SettingAssetPath}");
        return setting;
    }
}
