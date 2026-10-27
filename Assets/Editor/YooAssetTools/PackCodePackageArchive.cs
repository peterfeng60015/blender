using YooAsset.Editor;

/// <summary>
/// CodePackage 專用的打包規則：**一個收集器 = 一個歸檔檔案**。
///
/// 行為與內建的 <see cref="PackCollector"/> 相同（以收集器路徑當包名，該收集器底下所有
/// 檔案打進同一包），差別只有**副檔名**：內建規則一律用 "bundle"，而我們的產物是
/// YooAsset 的歸檔檔案（ArchiveFileBuildPipeline）而不是 Unity AssetBundle。
/// 給它獨立副檔名，讓「用副檔名區分兩種產物」在任何工具裡都成立。
/// </summary>
public class PackCodePackageArchive : IBundlePackRule
{
    /// <summary>歸檔檔案的副檔名。CodePackageBuildTool 也引用這個常數。</summary>
    public const string ArchiveFileExtension = "archive";

    BundlePackRuleResult IBundlePackRule.GetPackRuleResult(BundlePackRuleData data)
    {
        // 用收集器路徑當包名：兩個收集器（AotDllBytes / ProjectDllBytes）各成一包。
        // YooAsset 之後會把它正規化成小寫底線串，例如 assets_dllbytes_aotdllbytes.archive
        return new BundlePackRuleResult(data.CollectPath, ArchiveFileExtension);
    }
}
