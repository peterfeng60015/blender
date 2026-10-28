using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

/// <summary>
/// CodePackage（HybridCLR 模組 dll）的 YooAsset 打包工具。
///
/// 產物路徑：ServerData/{BuildTarget}/{bundleVersion}/CodePackage/
///   bundleVersion = PlayerSettings.bundleVersion（執行期對應 Application.version）
///   內部版號     = {bundleVersion}.{n}，n = 上述目錄裡 YooAsset 指針檔（CodePackage.version）的 n + 1，
///                  沒有指針檔就從 1 開始。
///
/// 與 HPS 的差異：版號不再來自 AddressableBuildTool / CI 傳入的 CDN 指針，改看本地 ServerData 的指針檔；
/// 不寫建置報告、不算 BundleHashTool 的雜湊、沒有 CI 哨兵參數。
///
/// ⚠️ 前提：本地 ServerData 的指針必須與遠端伺服器上的指針一致（ServerData 已 gitignore，只存在打包的這台機器）。
/// 換機器打包，或清掉 ServerData 後，配號會從 .1 重新開始，與已發佈的版號撞號。客戶端本地已有同版號的
/// manifest 時不會重新下載（SandboxFileSystem 的 DownloadPackageManifestOperation 在 CheckExists 步驟直接沿用），
/// 結果是拿舊 manifest 驗新歸檔，下載失敗。換機器前要先把遠端的 CodePackage 目錄（至少是指針檔）同步回本地。
/// </summary>
public static class CodePackageBuildTool
{
    public const string PackageName = CodePackageCollectorSetup.PackageName;

    /// <summary>
    /// YooAsset 的建置輸出根目錄；最終產物會再攤平搬進 ServerData，這裡只是中繼站。
    /// 放 Library 底下（已 gitignore、可隨時清）。
    /// </summary>
    private const string BuildOutputRootRelative = "Library/YooAssetBuild";

    private const string CodePackageFolderName = "CodePackage";
    private const string ServerDataFolderName = "ServerData";

    private static string ProjectRoot => HybridCLRTools.ProjectRoot;

    /// <summary>bundleVersion 允許的字元：英數、. - _（見 <see cref="ApplyCodePackageBuildPath"/>）。</summary>
    private static readonly Regex BundleVersionPattern = new Regex(@"\A[A-Za-z0-9._-]+\z");

    #region 選單與一鍵流程

    /// <summary>
    /// 一鍵打包代碼包：（AotDllBytes 不齊、有清單外的檔案，或不是目前平台產生時）AotDllWork → ProjectDllWork → 收集器設定 → 配號 → 打包 → 攤平。
    /// AotDllBytes 齊全且是目前平台產生時沿用既有那份，因為補充元數據要與已發佈的 player 同源，不該每次重產。
    /// </summary>
    [MenuItem("Tools/YooAsset/一鍵打包代碼包", false, 20)]
    public static void OneClickBuildCodePackage()
    {
        RunFromMenu(forceAotDllWork: false);
    }

    /// <summary>
    /// 同上，但一定重跑 AotDllWork（GenerateAll 會以目前平台完整跑一次 BuildPlayer，很慢）。
    /// 升 Unity、改了 AOT 程式或 AOT 清單、要出新 player 時用這個。
    /// </summary>
    [MenuItem("Tools/YooAsset/一鍵打包代碼包（重產 AOT dll）", false, 21)]
    public static void OneClickBuildCodePackageWithAot()
    {
        RunFromMenu(forceAotDllWork: true);
    }

    private static void RunFromMenu(bool forceAotDllWork)
    {
        // batchmode（-executeMethod）不攔例外：讓 Unity 以非 0 結束碼退出，呼叫端才知道失敗。
        if (Application.isBatchMode)
        {
            BuildAll(forceAotDllWork);
            return;
        }

        try
        {
            var internalVersion = BuildAll(forceAotDllWork);
            EditorUtility.DisplayDialog("CodePackage", $"代碼包打包完成：{internalVersion}", "確定");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("CodePackage", $"代碼包打包失敗：\n{ex.Message}", "確定");
        }
    }

    /// <summary>
    /// 一鍵流程本體。回傳本次的內部版號。
    /// </summary>
    public static string BuildAll(bool forceAotDllWork)
    {
        // 先產 AOT 再編模組：GenerateAll 也會編一次模組 dll，放前面讓 ProjectDllWork 複製的是最後一次編譯結果。
        var missingAot = GetMissingAotDllBytes();
        var unexpectedAot = GetUnexpectedAotDllBytes();
        var stampMatched = HybridCLRTools.IsAotDllBytesForCurrentTarget();
        if (forceAotDllWork || missingAot.Count > 0 || unexpectedAot.Count > 0 || !stampMatched)
        {
            if (missingAot.Count > 0)
                Debug.LogWarning("[CodePackageBuildTool] AotDllBytes 不齊，先跑 AotDllWork。缺少:\n  " + string.Join("\n  ", missingAot));
            if (unexpectedAot.Count > 0)
                Debug.LogWarning("[CodePackageBuildTool] AotDllBytes 有 AOT 清單以外的檔案（清單刪過項目），先跑 AotDllWork 重產。" +
                                 "不清掉的話會照樣打包，載入時 LoadMetadataForAOTAssembly 回 AOT_ASSEMBLY_NOT_FIND。多出:\n  " +
                                 string.Join("\n  ", unexpectedAot));
            if (missingAot.Count == 0 && unexpectedAot.Count == 0 && !stampMatched)
                Debug.LogWarning("[CodePackageBuildTool] AotDllBytes 不是目前平台／Unity 版本產生的（或沒有產生紀錄），先跑 AotDllWork。");

            // AotDllWork 開頭會確保 HybridCLR 已安裝（batchmode 下自動安裝被略過）
            HybridCLRTools.AotDllWork();
        }
        else
        {
            Debug.Log("[CodePackageBuildTool] AotDllBytes 已齊全，沿用既有 AOT dll（要重產請用「一鍵打包代碼包（重產 AOT dll）」）。");
        }

        HybridCLRTools.ProjectDllWork();

        return BuildAndDeployCodePackage();
    }

    /// <summary>目前 AOT 清單裡，Assets/DllBytes/AotDllBytes 還沒有對應 .dll.bytes 的項目。</summary>
    private static List<string> GetMissingAotDllBytes()
    {
        var dir = Path.Combine(ProjectRoot, CodePackageCollectorSetup.AotCollectPath);
        return HybridCLRTools.AotAssemblyNames
            .Where(name => !File.Exists(Path.Combine(dir, name + ".dll.bytes")))
            .ToList();
    }

    /// <summary>Assets/DllBytes/AotDllBytes 裡不在目前 AOT 清單內的 .dll.bytes（例如清單刪掉一項後留下的舊檔）。</summary>
    private static List<string> GetUnexpectedAotDllBytes()
    {
        var dir = Path.Combine(ProjectRoot, CodePackageCollectorSetup.AotCollectPath);
        if (!Directory.Exists(dir))
            return new List<string>();

        var expected = new HashSet<string>(HybridCLRTools.AotAssemblyNames.Select(name => name + ".dll.bytes"), StringComparer.Ordinal);
        return Directory.GetFiles(dir, "*.dll.bytes", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(fileName => !expected.Contains(fileName))
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToList();
    }

    #endregion

    #region 版號與路徑

    /// <summary>
    /// 取得本次建置的平台與 bundleVersion，並回傳 CodePackage 子目錄的絕對路徑。
    /// </summary>
    public static void ApplyCodePackageBuildPath(out string buildTarget, out string bundleVersion, out string codePackageDir)
    {
        // 白名單檢查，不做 Trim 或跳脫：bundleVersion 同時是本地資料夾名稱，也是 runtime 以 Application.version
        // 直接拼進 URL 的路徑段（沒有跳脫）。空白、#、?、% 等字元會讓兩邊路徑對不上，所以只允許英數、. - _，
        // 且不能是 . 或 ..（會變成上層目錄）。
        bundleVersion = PlayerSettings.bundleVersion ?? string.Empty;
        if (!BundleVersionPattern.IsMatch(bundleVersion) || bundleVersion == "." || bundleVersion == "..")
            throw new Exception($"[CodePackageBuildTool] PlayerSettings.bundleVersion '{bundleVersion}' 不合法：" +
                                "只能用英數字與 . - _，不可為空、不可是 . 或 ..，請先修正。");

        // 與 runtime 的遠端路徑約定一致：{serverRoot}/{BuildTarget 列舉名}/{Application.version}/CodePackage
        buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        codePackageDir = Path.Combine(ProjectRoot, ServerDataFolderName, buildTarget, bundleVersion, CodePackageFolderName);

        Debug.Log($"[CodePackageBuildTool] bundleVersion={bundleVersion}, buildTarget={buildTarget}\n" +
                  $"  CodePackage 輸出目錄: {codePackageDir}");
    }

    /// <summary>
    /// 配內部版號：<c>{bundleVersion}.{n}</c>，n = 本地 CodePackage 目錄裡指針檔的 n + 1，沒有指針檔從 1 開始。
    ///
    /// 指針檔只在攤平搬運的最後一步才覆蓋（見 <see cref="FlattenCopyToServerData"/>），
    /// 所以打包失敗時指針不前進，下次會配到同一個 n，殘骸由 <see cref="BuildCodePackage"/> 清掉。
    ///
    /// ⚠️ 只看本地指針，程式無法保證「版號每次必變」：本地指針與遠端不一致時（換機器、清掉 ServerData）
    /// 會配到已發佈過的版號，風險見類別註解。
    /// </summary>
    public static string AllocateInternalVersion(string bundleVersion, string codePackageDir)
    {
        var pointerPath = Path.Combine(codePackageDir, YooAssetConfiguration.GetPackageVersionFileName(PackageName));
        var current = 0;
        if (File.Exists(pointerPath))
        {
            var pointer = File.ReadAllText(pointerPath).Trim();
            if (!TryParseSuffix(bundleVersion, pointer, out current))
            {
                throw new Exception("[CodePackageBuildTool] 指針檔內容與 bundleVersion 對不上：" +
                                    $"{pointerPath} 的內容是 '{pointer}'，預期是 '{bundleVersion}.<正整數>'。" +
                                    "請確認指針檔沒有被手動改過，中止以免配錯號。");
            }
        }

        var next = current + 1;
        Debug.Log($"[CodePackageBuildTool] 內部版號配號: 本地指針={(current == 0 ? "（無）" : $"{bundleVersion}.{current}")}" +
                  $" → 本次 {bundleVersion}.{next}");
        return $"{bundleVersion}.{next}";
    }

    /// <summary>
    /// 從內部版號字串（例如 1.0.3）取出尾碼 3，並確認前綴正是本次的 bundleVersion（1.0）。
    /// bundleVersion 本身是點分格式且段數不固定，所以只能用「前綴 + 最後一段」判斷。
    /// </summary>
    private static bool TryParseSuffix(string bundleVersion, string internalVersion, out int suffix)
    {
        suffix = 0;
        var prefix = bundleVersion + ".";
        if (!internalVersion.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var tail = internalVersion.Substring(prefix.Length);
        return tail.Length > 0 && tail.All(char.IsDigit) && int.TryParse(tail, out suffix) && suffix > 0;
    }

    /// <summary>YooAsset 原始建置輸出裡本套件的目錄；版號是它底下的子目錄名。</summary>
    private static string BuildOutputPackageDir(string buildTarget)
    {
        return Path.Combine(ProjectRoot, BuildOutputRootRelative, buildTarget, PackageName);
    }

    /// <summary>
    /// 執行 ArchiveFileBuildPipeline，回傳 YooAsset 的版本輸出資料夾。
    /// </summary>
    public static string BuildCodePackage(string internalVersion)
    {
        var buildParameters = new ArchiveFileBuildParameters
        {
            BuildOutputRoot = Path.Combine(ProjectRoot, BuildOutputRootRelative),
            BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = EBuildPipeline.ArchiveFileBuildPipeline.ToString(),
            BuildBundleType = (int)EBundleType.ArchiveBundle,
            BuildTarget = EditorUserBuildSettings.activeBuildTarget,
            PackageName = PackageName,
            PackageVersion = internalVersion,
            VerifyBuildingResult = true,

            // 固定檔名（不帶 hash），每次發版直接覆蓋伺服器上的同名檔案。
            // 客戶端本地快取的鍵是 manifest 宣告的 FileHash，遠端檔名不參與快取命中判斷：
            // 新 manifest 帶新 hash → 快取未命中 → 重新下載並用新 CRC 驗證。
            // ⚠️ 這只在「版號每次都是新的」與「拿到的是新檔」時成立，程式兩者都不保證：
            //   - 版號：只看本地指針，見類別註解。
            //   - CDN／HTTP 快取：歸檔下載網址沒有快取破壞參數（SFSDownloadBundleOperation.GetRequestUrl 只用檔名組網址），
            //     例如 GCS 公開物件預設 Cache-Control max-age=3600，覆蓋上傳後快取期間內可能拿到舊歸檔，CRC 不符而下載失敗。
            //   要避免可改用 EFileNameStyle.BundleName_HashName，或上傳時對歸檔設 no-cache；是否改由使用者決定。
            FileNameStyle = EFileNameStyle.BundleName,

            // 不做首包內建資源：dll 一律走遠端下載
            BundledCopyOption = EBundledCopyOption.None,
            BundledCopyParams = string.Empty,
            ClearBuildCacheFiles = false,
            UseAssetDependencyDB = true,
            BundleEncryptor = new CodePackageEncryption(),

            // 維持預設 false：manifest 的 FileHash 等於「磁碟檔案內容（加密後）的標準 MD5」，
            // 直接對伺服器上的檔案算 MD5 就能對上；true 會變成含建置機絕對路徑的複合 hash。
            IncludePathInHash = false,
        };

        // 清掉這個版號的既有輸出。YooAsset 3.0.6 在輸出目錄已存在時以 PackageOutputDirectoryExists 中止
        // （TaskPrepare.PrepareOutputDirectory），那是它防覆蓋既有輸出的保護。
        // 本次版號 = 本地指針 + 1，指針只在成功攤平後才前進，所以 ServerData 與 Library 同步時，
        // 同版號的既有輸出是某次失敗或中斷的殘骸。
        // ⚠️ 但不一定是殘骸：ServerData 被清掉而 Library/YooAssetBuild 還在時，配號從 .1 重來，
        // 這裡刪掉的會是曾成功發佈過的輸出（遠端也可能還在用那個版號，見類別註解）。
        // 改動配號或攤平規則時要重新評估。
        var outputVersionDir = Path.Combine(
            BuildOutputPackageDir(EditorUserBuildSettings.activeBuildTarget.ToString()), internalVersion);
        if (Directory.Exists(outputVersionDir))
        {
            Debug.LogWarning($"[CodePackageBuildTool] 清除同版號的既有建置輸出: {outputVersionDir}\n" +
                             "  （通常是未完成建置的殘骸；若 ServerData 被清過，可能是曾發佈過的版號，請確認指針與遠端一致）");
            Directory.Delete(outputVersionDir, true);
        }

        var pipeline = new ArchiveFileBuildPipeline();
        var buildResult = pipeline.Run(buildParameters, true);
        if (!buildResult.Success)
        {
            throw new Exception("[CodePackageBuildTool] CodePackage 打包失敗\n" +
                                $"  FailedTask: {buildResult.FailedTask}\n" +
                                $"  Error: {buildResult.ErrorInfo}\n" +
                                $"  Stack: {buildResult.ErrorStack}");
        }

        Debug.Log($"[CodePackageBuildTool] 打包成功: {buildResult.OutputPackageDirectory}");
        return buildResult.OutputPackageDirectory;
    }

    /// <summary>
    /// 把 YooAsset 版本資料夾的內容攤平搬進 ServerData 的 CodePackage 子目錄。
    ///
    /// 規則：除了 .report（診斷檔，runtime 不下載）之外一律覆蓋。歸檔是固定檔名，
    /// 第二次建置必然「同名不同內容」，覆蓋就是設計本身；manifest 檔名帶版號、每版一對。
    /// **指針檔（CodePackage.version）最後才覆蓋**：中途失敗時下次配號仍看到上一版，殘骸會被清掉重打。
    /// ⚠️ 但歸檔已先被覆蓋：若 ServerData 直接對外提供，在「歸檔已換、指針未換」的時間窗（含中途失敗）
    /// 內下載的客戶端會拿舊 manifest 驗新歸檔而失敗，重走初始化即恢復（與 HPS 相同的取捨；
    /// 要完全避免得改用帶 hash 的檔名 EFileNameStyle.BundleName_HashName）。
    /// ⚠️ 上傳到 CDN 後還有快取問題：歸檔下載網址沒有快取破壞參數，CDN／HTTP 快取（例如 GCS 公開物件預設
    /// max-age=3600）在快取期間內可能回舊歸檔，客戶端用新 manifest 的 CRC 驗證失敗。
    /// 對策同上（BundleName_HashName），或上傳時對歸檔設 no-cache；是否改由使用者決定，目前程式未改。
    /// </summary>
    public static void FlattenCopyToServerData(string yooVersionDir, string codePackageDir, string internalVersion)
    {
        if (!Directory.Exists(yooVersionDir))
            throw new Exception($"[CodePackageBuildTool] YooAsset 輸出資料夾不存在: {yooVersionDir}");

        Directory.CreateDirectory(codePackageDir);

        var reportFileName = YooAssetConfiguration.GetBuildReportFileName(PackageName, internalVersion);
        var pointerFileName = YooAssetConfiguration.GetPackageVersionFileName(PackageName);
        string pointerSrc = null;
        var archiveCount = 0;

        foreach (var src in Directory.GetFiles(yooVersionDir, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(src);
            if (fileName == reportFileName)
            {
                Debug.Log($"[CodePackageBuildTool] 略過診斷檔（不上傳）: {fileName}");
                continue;
            }
            if (fileName == pointerFileName)
            {
                pointerSrc = src;
                continue;
            }

            File.Copy(src, Path.Combine(codePackageDir, fileName), overwrite: true);
            if (fileName.EndsWith("." + PackCodePackageArchive.ArchiveFileExtension, StringComparison.OrdinalIgnoreCase))
                archiveCount++;
        }

        if (pointerSrc == null)
            throw new Exception($"[CodePackageBuildTool] YooAsset 輸出裡沒有指針檔 {pointerFileName}: {yooVersionDir}");
        File.Copy(pointerSrc, Path.Combine(codePackageDir, pointerFileName), overwrite: true);

        Debug.Log($"[CodePackageBuildTool] 已攤平搬入 {codePackageDir}（歸檔 {archiveCount} 顆，指針 → {internalVersion}）");
    }

    #endregion

    #region 流程編排

    /// <summary>
    /// 代碼包本身的流程：收集器設定 → 配號 → YooAsset 打包 → 攤平搬入 ServerData。
    /// ProjectDllWork / AotDllWork 由呼叫端（<see cref="BuildAll"/>）負責。
    /// </summary>
    /// <returns>本次的內部版號</returns>
    public static string BuildAndDeployCodePackage()
    {
        // 每次都同步收集器設定（冪等），不必靠人記得手動跑選單；
        // 也順便在 fresh checkout 時建出 Assets/DllBytes/* 資料夾。
        CodePackageCollectorSetup.Setup();

        ApplyCodePackageBuildPath(out _, out var bundleVersion, out var codePackageDir);

        var internalVersion = AllocateInternalVersion(bundleVersion, codePackageDir);

        var yooVersionDir = BuildCodePackage(internalVersion);
        FlattenCopyToServerData(yooVersionDir, codePackageDir, internalVersion);

        return internalVersion;
    }

    #endregion
}
