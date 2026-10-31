using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

/// <summary>
/// AssetPackage（美術素材）的 YooAsset 打包工具。
///
/// 產物路徑：ServerData/{BuildTarget}/{bundleVersion}/AssetPackage/（與 CodePackage 同層並列）
///   內部版號 = {bundleVersion}.{n}，n = 該目錄裡指針檔（AssetPackage.version）的 n + 1，沒有指針從 1 開始
///   ——規則與 CodePackage 相同，但兩個 package 各自配號、互不影響。
///   bundleVersion 的合法性檢查與平台資料夾沿用 <see cref="CodePackageBuildTool.ApplyCodePackageBuildPath"/>。
///
/// 管線：ScriptableBuildPipeline、一般 AssetBundle、LZ4、不加密。
///
/// 與 CodePackage 刻意不同的一點：檔名用 <c>BundleName_HashName</c>（帶內容 hash），不是固定檔名。
///   - 新舊版本的 bundle 可以並存在同一個目錄，攤平時不覆蓋任何舊檔，指針一換即原子切換；
///     不會有 CodePackage 那種「歸檔已換、指針未換」的時間窗。
///   - 內容變了網址就變，CDN／HTTP 快取不會回舊檔。
///   代價是 ServerData 與伺服器上的舊 bundle 會累積，需要時手動清（只留目前 manifest 引用的檔案）。
///
/// ⚠️ 版號只看本地指針，換機器或清掉 ServerData 的風險同 CodePackageBuildTool 的類別註解。
/// </summary>
public static class AssetPackageBuildTool
{
    public const string PackageName = AssetPackageCollectorSetup.PackageName;

    /// <summary>與 CodePackage 共用的 YooAsset 建置中繼目錄（Library 底下，以 package 名稱分開）。</summary>
    private const string BuildOutputRootRelative = "Library/YooAssetBuild";

    private const string AssetPackageFolderName = "AssetPackage";

    /// <summary>SBP 會放進版本輸出夾、但不該上傳的檔案（見 <see cref="FlattenCopyToServerData"/>）。</summary>
    private static readonly HashSet<string> SkippedPipelineFiles = new HashSet<string>(StringComparer.Ordinal)
    {
        "link.xml",
        "buildlogtep.json",
    };

    private static string ProjectRoot => HybridCLRTools.ProjectRoot;

    #region 選單與一鍵流程

    /// <summary>一鍵打包資源包：收集器設定 → 配號 → 打包 → 攤平。只改美術時用這個，不必重打代碼包。</summary>
    [MenuItem("Tools/YooAsset/一鍵打包資源包", false, 30)]
    public static void OneClickBuildAssetPackage()
    {
        RunFromMenu("資源包", () => BuildAndDeployAssetPackage());
    }

    /// <summary>
    /// 一鍵打包全部：先代碼包（<see cref="CodePackageBuildTool.BuildAll"/>，沿用既有 AOT dll 的判斷）、再資源包。
    /// 出新 player 或代碼與美術一起更新時用這個。代碼包失敗就不打資源包。
    /// </summary>
    [MenuItem("Tools/YooAsset/一鍵打包全部（代碼包＋資源包）", false, 31)]
    public static void OneClickBuildAll()
    {
        RunFromMenu("全部", () =>
        {
            var code = CodePackageBuildTool.BuildAll(forceAotDllWork: false);
            var asset = BuildAndDeployAssetPackage();
            return $"CodePackage {code} / AssetPackage {asset}";
        });
    }

    private static void RunFromMenu(string what, Func<string> build)
    {
        // batchmode（-executeMethod）不攔例外：讓 Unity 以非 0 結束碼退出，呼叫端才知道失敗。
        if (Application.isBatchMode)
        {
            build();
            return;
        }

        try
        {
            var result = build();
            EditorUtility.DisplayDialog("YooAsset", $"{what}打包完成：{result}", "確定");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("YooAsset", $"{what}打包失敗：\n{ex.Message}", "確定");
        }
    }

    #endregion

    #region 版號與路徑

    /// <summary>
    /// 取得本次的平台、bundleVersion 與 AssetPackage 子目錄的絕對路徑。
    /// bundleVersion 的白名單檢查交給 CodePackageBuildTool，兩個 package 的規則才不會分岔。
    /// </summary>
    public static void ApplyAssetPackageBuildPath(out string buildTarget, out string bundleVersion, out string assetPackageDir)
    {
        CodePackageBuildTool.ApplyCodePackageBuildPath(out buildTarget, out bundleVersion, out var codePackageDir);
        assetPackageDir = Path.Combine(Path.GetDirectoryName(codePackageDir), AssetPackageFolderName);
        Debug.Log($"[AssetPackageBuildTool] AssetPackage 輸出目錄: {assetPackageDir}");
    }

    /// <summary>
    /// 配內部版號：<c>{bundleVersion}.{n}</c>，n = 本地指針的 n + 1。
    /// 指針只在攤平的最後一步才覆蓋，打包失敗時指針不前進。
    /// </summary>
    public static string AllocateInternalVersion(string bundleVersion, string assetPackageDir)
    {
        var pointerPath = Path.Combine(assetPackageDir, YooAssetConfiguration.GetPackageVersionFileName(PackageName));
        var current = 0;
        if (File.Exists(pointerPath))
        {
            var pointer = File.ReadAllText(pointerPath).Trim();
            if (!TryParseSuffix(bundleVersion, pointer, out current))
            {
                throw new Exception("[AssetPackageBuildTool] 指針檔內容與 bundleVersion 對不上：" +
                                    $"{pointerPath} 的內容是 '{pointer}'，預期是 '{bundleVersion}.<正整數>'。" +
                                    "請確認指針檔沒有被手動改過，中止以免配錯號。");
            }
        }

        var next = current + 1;
        Debug.Log($"[AssetPackageBuildTool] 內部版號配號: 本地指針={(current == 0 ? "（無）" : $"{bundleVersion}.{current}")}" +
                  $" → 本次 {bundleVersion}.{next}");
        return $"{bundleVersion}.{next}";
    }

    /// <summary>同 CodePackageBuildTool.TryParseSuffix：前綴必須正是 bundleVersion，尾碼是正整數。</summary>
    private static bool TryParseSuffix(string bundleVersion, string internalVersion, out int suffix)
    {
        suffix = 0;
        var prefix = bundleVersion + ".";
        if (!internalVersion.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var tail = internalVersion.Substring(prefix.Length);
        return tail.Length > 0 && tail.All(char.IsDigit) && int.TryParse(tail, out suffix) && suffix > 0;
    }

    /// <summary>
    /// 執行 ScriptableBuildPipeline，回傳 YooAsset 的版本輸出資料夾。
    /// </summary>
    public static string BuildAssetPackage(string internalVersion)
    {
        var buildTarget = EditorUserBuildSettings.activeBuildTarget;
        var buildParameters = new ScriptableBuildParameters
        {
            BuildOutputRoot = Path.Combine(ProjectRoot, BuildOutputRootRelative),
            BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
            BuildBundleType = (int)EBundleType.AssetBundle,
            BuildTarget = buildTarget,
            PackageName = PackageName,
            PackageVersion = internalVersion,
            EnableSharePackRule = true,
            VerifyBuildingResult = true,
            // 理由見類別註解
            FileNameStyle = EFileNameStyle.BundleName_HashName,
            // 不做首包內建資源：美術一律走遠端下載
            BundledCopyOption = EBundledCopyOption.None,
            BundledCopyParams = string.Empty,
            CompressOption = ECompressOption.LZ4,
            ClearBuildCacheFiles = false,
            UseAssetDependencyDB = true,
            // sprite 收進 Sprite Atlas 後，貼圖在圖集那一包：讓引用圖集的 bundle 在清單裡依賴它（見 SsAtlasSetup）
            TrackSpriteAtlasDependencies = true,
            // 不加密
            BundleEncryptor = null,
            // 與自動收集的 shader 包同名（同 YooAsset 建置視窗 ScriptableBuildPipelineViewer.GetBuiltinShaderBundleName）
            BuiltinShadersBundleName = DefaultBundlePackRule.CreateShadersPackRuleResult()
                .GetBundleName(PackageName, BundleCollectorSettingData.Setting.UniqueBundleName),
        };

        // 同版號的既有輸出通常是失敗或中斷的殘骸（指針只在成功攤平後前進），YooAsset 遇到已存在的
        // 輸出目錄會中止，先清掉。風險同 CodePackageBuildTool.BuildCodePackage 的註解。
        var outputVersionDir = Path.Combine(ProjectRoot, BuildOutputRootRelative, buildTarget.ToString(), PackageName, internalVersion);
        if (Directory.Exists(outputVersionDir))
        {
            Debug.LogWarning($"[AssetPackageBuildTool] 清除同版號的既有建置輸出: {outputVersionDir}");
            Directory.Delete(outputVersionDir, true);
        }

        var pipeline = new ScriptableBuildPipeline();
        var buildResult = pipeline.Run(buildParameters, true);
        if (!buildResult.Success)
        {
            throw new Exception("[AssetPackageBuildTool] AssetPackage 打包失敗\n" +
                                $"  FailedTask: {buildResult.FailedTask}\n" +
                                $"  Error: {buildResult.ErrorInfo}\n" +
                                $"  Stack: {buildResult.ErrorStack}");
        }

        Debug.Log($"[AssetPackageBuildTool] 打包成功: {buildResult.OutputPackageDirectory}");
        return buildResult.OutputPackageDirectory;
    }

    /// <summary>
    /// 把 YooAsset 版本資料夾的內容攤平搬進 ServerData 的 AssetPackage 子目錄。
    /// 略過 .report（診斷檔）與 SBP 額外放進來的 link.xml／buildlogtep.json（3.0.6：TaskCreatePackage_SBP，
    /// 執行期不下載，link.xml 是給 Player 裁剪用的，放伺服器沒有作用），其餘都複製；
    /// bundle 檔名帶 hash，同名即同內容，所以覆蓋無害。
    /// **指針檔最後才覆蓋**：之前的任何失敗都不會讓客戶端看到新版本。
    /// </summary>
    public static void FlattenCopyToServerData(string yooVersionDir, string assetPackageDir, string internalVersion)
    {
        if (!Directory.Exists(yooVersionDir))
            throw new Exception($"[AssetPackageBuildTool] YooAsset 輸出資料夾不存在: {yooVersionDir}");

        Directory.CreateDirectory(assetPackageDir);

        var reportFileName = YooAssetConfiguration.GetBuildReportFileName(PackageName, internalVersion);
        var pointerFileName = YooAssetConfiguration.GetPackageVersionFileName(PackageName);
        string pointerSrc = null;
        var bundleCount = 0;

        foreach (var src in Directory.GetFiles(yooVersionDir, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(src);
            if (fileName == reportFileName || SkippedPipelineFiles.Contains(fileName))
                continue;
            if (fileName == pointerFileName)
            {
                pointerSrc = src;
                continue;
            }

            File.Copy(src, Path.Combine(assetPackageDir, fileName), overwrite: true);
            if (fileName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                bundleCount++;
        }

        if (pointerSrc == null)
            throw new Exception($"[AssetPackageBuildTool] YooAsset 輸出裡沒有指針檔 {pointerFileName}: {yooVersionDir}");
        File.Copy(pointerSrc, Path.Combine(assetPackageDir, pointerFileName), overwrite: true);

        Debug.Log($"[AssetPackageBuildTool] 已攤平搬入 {assetPackageDir}（bundle {bundleCount} 顆，指針 → {internalVersion}）");
    }

    /// <summary>
    /// 打包前先檢查素材規範（見 AssetPackageCollectorSetup 的類別註解），一次列出所有違規，
    /// 而不是等 YooAsset 在收集途中丟出只指向第一個違規的例外。
    /// 檢查範圍是這次實際收集的全部根目錄；檔名唯一性跨根目錄檢查（整個 package 共用一個定位地址空間）。
    /// 刻意比 YooAsset 嚴格：NormalIgnoreRule 會忽略的檔（無副檔名、.cs、Unity 不認得的類型、Editor／Gizmos 底下）
    /// 這裡照樣算違規，因為收集路徑本來就不該放素材以外的檔（interfaces.md §6.1）。
    /// </summary>
    internal static void ValidateCollectFolders(IReadOnlyList<AssetPackageCollectorSetup.GroupSpec> groups)
    {
        var problems = new List<string>();
        var addresses = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var spec in groups)
        {
            var root = spec.CollectPath;
            if (!Directory.Exists(root))
            {
                problems.Add($"收集路徑不存在：{root}（group {spec.GroupName}）");
                continue;
            }

            foreach (var file in Directory.GetFiles(root))
            {
                var name = Path.GetFileName(file);
                if (!name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !IsIgnoredByUnity(name))
                    problems.Add($"根目錄不能直接放檔案：{root}/{name}");
            }

            foreach (var dir in Directory.GetDirectories(root))
            {
                var dirName = Path.GetFileName(dir);
                if (IsIgnoredByUnity(dirName))
                    continue;
                if (dirName.Contains("."))
                    problems.Add($"子資料夾名稱不能含 '.'：{root}/{dirName}");
                if (spec.FolderNeedsPrefix && spec.RequiredFilePrefix != null &&
                    !dirName.StartsWith(spec.RequiredFilePrefix, StringComparison.Ordinal))
                    problems.Add($"子資料夾名稱必須以 '{spec.RequiredFilePrefix}' 開頭：{root}/{dirName}");

                var dirPath = dir.Replace('\\', '/');
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);
                    if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || IsIgnoredByUnity(name))
                        continue;
                    var path = file.Replace('\\', '/');
                    // 隱藏資料夾裡的檔案（例如 art 匯入中途當掉留下的 <Name>/.staging/）Unity 不匯入、YooAsset 也收不到，
                    // 不算進撞名與前綴檢查
                    var relative = path.Substring(dirPath.Length + 1);
                    if (relative.Split('/').Any(IsIgnoredByUnity))
                        continue;
                    var address = Path.GetFileNameWithoutExtension(name);
                    if (spec.RequiredFilePrefix != null && !address.StartsWith(spec.RequiredFilePrefix, StringComparison.Ordinal))
                        problems.Add($"檔名必須以 '{spec.RequiredFilePrefix}' 開頭：{path}");
                    if (addresses.TryGetValue(address, out var other))
                        problems.Add($"定位地址撞名 '{address}'：{other} 與 {path}");
                    else
                        addresses.Add(address, path);
                }
            }
        }

        if (problems.Count > 0)
            throw new Exception("[AssetPackageBuildTool] 素材不符合 AssetPackage 收集規範：\n  " + string.Join("\n  ", problems));
    }

    /// <summary>
    /// Unity 不匯入的檔案或資料夾：以 '.' 開頭或以 '~' 結尾（AssetDatabase 的隱藏規則），
    /// 所以 YooAsset 也收集不到，掃描時要跳過。
    /// </summary>
    private static bool IsIgnoredByUnity(string name)
    {
        return name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal);
    }

    #endregion

    #region 流程編排

    /// <summary>收集器設定 → 配號 → YooAsset 打包 → 攤平搬入 ServerData。回傳本次的內部版號。</summary>
    public static string BuildAndDeployAssetPackage()
    {
        return BuildAndDeployAssetPackage(AssetPackageCollectorSetup.DefaultGroups);
    }

    /// <summary>同上，但以指定的 group 定義打包（測試用；呼叫端測完要自己 Setup() 還原設定）。</summary>
    internal static string BuildAndDeployAssetPackage(IReadOnlyList<AssetPackageCollectorSetup.GroupSpec> specs)
    {
        // 外部工具（Blender 腳本）剛產出的資料夾在 AssetDatabase 匯入前會被當成不存在而整個 group 略過，先同步匯入
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetPackageCollectorSetup.Setup(specs);
        if (!AssetDatabase.IsValidFolder(AssetPackageCollectorSetup.SidescrollerSpritesCollectPath))
            throw new Exception($"[AssetPackageBuildTool] 收集路徑不存在：{AssetPackageCollectorSetup.SidescrollerSpritesCollectPath}，沒有素材可打包。");
        ValidateCollectFolders(AssetPackageCollectorSetup.ActiveGroups(specs));

        ApplyAssetPackageBuildPath(out _, out var bundleVersion, out var assetPackageDir);

        var internalVersion = AllocateInternalVersion(bundleVersion, assetPackageDir);

        var yooVersionDir = BuildAssetPackage(internalVersion);
        FlattenCopyToServerData(yooVersionDir, assetPackageDir, internalVersion);

        return internalVersion;
    }

    #endregion
}
