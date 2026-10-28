using HybridCLR;
using HybridCLR.Commands;
using HybridCLR.Installer;
using HybridCLR.Settings;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// HybridCLR 打包工具（移植自 HPS，改寫成 HybridCLR 9.0.3 的 API）。
///
/// 9.0.3 與 HPS 使用的 8.13.0 主要差異：
///   - 命名空間由 HybridCLR.Editor.* 改成 HybridCLR.*（HybridCLR、HybridCLR.Commands、
///     HybridCLR.Installer、HybridCLR.Settings）。
///   - InstallerController 沒有 GetCompatibleType()；Unity 版本不相容時，建構子內的
///     FindMatchedVersion 直接拋 NotSupportedException。
///   - 編譯輸出與裁剪後 AOT dll 固定在 Library/HybridCLR 底下，
///     SettingsUtil 的編譯輸出目錄與裁剪後 AOT 目錄 API 回傳的已是絕對路徑。
/// </summary>
public static class HybridCLRTools
{
    private static readonly string aotBytesPath = "DllBytes/AotDllBytes";
    private static readonly string projectBytesPath = "DllBytes/ProjectDllBytes";

    /// <summary>模組組件：只有 Assembly-CSharp（入口 AppBootstrap.Run），AOT 入口是 EntryScene asmdef。</summary>
    private static readonly string[] ModuleAssemblies = { "Assembly-CSharp" };

    // 產生檔放 Assets/HybridCLRGenerate/（.gitignore 已排除），與 HPS 相同；
    // 9.0.3 的預設值是 HybridCLR/Generate/，不在 .gitignore 內，每次 GenerateAll 都會冒出未追蹤檔。
    private const string OutputLinkFile = "HybridCLRGenerate/link.xml";
    private const string OutputAOTGenericReferenceFile = "HybridCLRGenerate/AOTGenericReferences.cs";

    /// <summary>專案根目錄（Assets/ 的上一層），給其他 Editor 工具共用。</summary>
    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    /// <summary>
    /// 檢查 HybridCLR 是否已安裝（本機 Library/HybridCLR/LocalIl2CppData-*/il2cpp/libil2cpp/hybridclr
    /// 是否存在，且安裝版本等於套件版本），沒安裝就用預設來源（git clone hybridclr + il2cpp_plus）安裝。
    /// 已安裝時直接略過。安裝需要網路連線，失敗會拋例外中止。
    /// </summary>
    [MenuItem("HybridCLR/Build/確保 HybridCLR 已安裝", priority = 90)]
    public static void EnsureHybridCLRInstalled()
    {
        // 9.0.3：Unity 版本不在 hybridclr_version.json 的相容清單內時，建構子本身就會拋
        // NotSupportedException（訊息內含相容版本清單），等同 8.x 的 CompatibleType.Incompatible。
        // 9.0.3 沒有「可能不相容」這一級，所以 HPS 的 MaybeIncompatible 警告分支整段拿掉。
        var controller = new InstallerController();
        var packageVersion = controller.PackageVersion;
        var installedVersion = controller.InstalledLibil2cppVersion ?? "Unknown";
        if (controller.HasInstalledHybridCLR() && packageVersion == installedVersion)
        {
            Debug.Log($"HybridCLR 已安裝（version: {installedVersion}），略過安裝。");
            return;
        }

        Debug.Log("偵測到 HybridCLR 尚未安裝或版本不符，開始執行安裝（clone hybridclr + il2cpp_plus，需要網路連線）...");
        try
        {
            // 安裝要 clone 遠端 repo 再複製整份 il2cpp，可能要一段時間；開 progress bar 避免看起來像卡住。
            EditorUtility.DisplayProgressBar("HybridCLR", "正在安裝 HybridCLR（clone 遠端 repo，需要網路連線）...", 0.5f);
            controller.InstallDefaultHybridCLR();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // InstallDefaultHybridCLR 在 git clone 失敗時會拋例外，但複製 il2cpp 階段失敗只會 LogError
        // （InstallerController.RunInitLocalIl2CppData），這裡再檢查一次實際結果與版本，
        // 避免後續打包流程在缺少 HybridCLR 的狀態下靜默繼續（BuildPlayer 的 CheckSettings 也是比對版本）。
        if (!controller.HasInstalledHybridCLR() || controller.InstalledLibil2cppVersion != controller.PackageVersion)
        {
            throw new System.Exception("HybridCLR 安裝失敗，請確認網路連線可以存取安裝所需的 git repo，或查看 Console 詳細錯誤訊息。");
        }

        Debug.Log("HybridCLR 安裝完成。");
    }

    /// <summary>
    /// 開啟 Unity（或每次 domain reload）時自動檢查/安裝 HybridCLR。
    ///
    /// 與 HPS 不同：
    ///   - batchmode 一律略過。batchmode 下沒有人看得到 progress bar，第一次開專案時
    ///     會在任何 -executeMethod 之前同步 git clone + 複製整份 il2cpp（數百 MB），
    ///     無網路時只留一行 LogError、建置照跑；需要 HybridCLR 的流程（AotDllWork，
    ///     一鍵打包代碼包經由它）開頭會自己呼叫 EnsureHybridCLRInstalled，失敗時拋例外中止。
    ///   - 用 delayCall 排到 domain reload 之後，不在 InitializeOnLoad 階段做長時間阻塞；
    ///     正在進出 Play 模式時略過，不在遊戲執行中觸發安裝。
    /// 例外照 HPS 只記錄不往外拋：這不是使用者主動觸發的操作。
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoEnsureHybridCLRInstalledOnLoad()
    {
        if (Application.isBatchMode)
            return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            try
            {
                EnsureHybridCLRInstalled();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HybridCLRTools] 開啟 Unity 時自動檢查/安裝 HybridCLR 失敗: {ex}");
            }
        };
    }

    [MenuItem("HybridCLR/Build/設定Settings的Dll清單", priority = 100)]
    public static void SetupSettingsDllList()
    {
        // 9.0.0 起 HybridCLRSettings 沒有 patchAOTAssemblies 欄位（RELEASELOG「remove some fields of
        // HybridCLRSettings」），AOT 清單改由本類別的 AotAssemblyNames 保存，這裡只剩模組組件設定要寫。
        SetupModuleAssemblyDefinition();
    }

    [MenuItem("HybridCLR/Build/ProjectDll: 編譯 + 複製檔案", priority = 100)]
    public static void ProjectDllWork()
    {
        // 先同步模組組件設定，確保複製清單反映這份程式碼，而非 .asset 內殘留的狀態。與 AotDllWork 對稱。
        SetupModuleAssemblyDefinition();

        // 編譯 dll。先清空輸出目錄：9.0.3 的 CompileDllCommand.CompileDll 不檢查 CompilePlayerScripts 的結果，
        // player 專屬的編譯錯誤（例如 #if !UNITY_EDITOR 區塊）不會中斷流程；不清空的話會複製到上一次的舊 dll，
        // 清空後缺檔會由 CopyAndRenameDll 擋下。（9.0.3 回傳的已是絕對路徑）
        var srcPath = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget);
        EnsureEmptyFolder(srcPath);
        CompileDllCommand.CompileDllActiveBuildTarget();

        // 複製 dll + 改副檔名
        var dllBytesPath = Path.Combine(Application.dataPath, projectBytesPath);
        EnsureEmptyFolder(dllBytesPath);
        CopyAndRenameDll(srcPath, dllBytesPath, SettingsUtil.HotUpdateAssemblyNamesExcludePreserved);
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 重新產生 AOT 清單並跑 GenerateAll，再把裁剪後的 AOT dll 複製到 Assets/DllBytes/AotDllBytes。
    ///
    /// ⚠️ GenerateAll 內的 StripAOTDllCommand 會以目前平台跑一次完整 BuildPlayer 來取得裁剪後的 AOT dll；
    /// 補充元數據要與玩家手上的 player 同源才有意義，所以 AOT 有變動（升 Unity、改 AOT 程式、
    /// 改 AOT 清單）時要與出包一起跑，之後只更新代碼的版本沿用同一份。
    /// </summary>
    [MenuItem("HybridCLR/Build/AotDll: 複製檔案", priority = 100)]
    public static void AotDllWork()
    {
        // GenerateAll 只檢查「有沒有裝」，版本不符要到 BuildPlayer 的 CheckSettings 才失敗；先確保裝好且版本相符。
        EnsureHybridCLRInstalled();

        // 每次都重跑 GenerateAll：AOT generic metadata 是否過時取決於程式碼內容，而非 AOT 名稱清單是否變動。
        SetupSettingsDllList();
        PrebuildCommand.GenerateAll();

        CopyAotDllFromAotAssemblyNames();
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 寫入本專案固定的模組組件設定：啟用 HybridCLR、模組組件只有 Assembly-CSharp、
    /// 不使用 asmdef 形式的模組組件、產生檔放 HybridCLRGenerate/。
    /// </summary>
    public static void SetupModuleAssemblyDefinition()
    {
        Debug.Log("設定 Settings 的模組組件清單");
        var settings = SettingsUtil.HybridCLRSettings;
        settings.enable = true;
        settings.hotUpdateAssemblies = (string[])ModuleAssemblies.Clone();
        // 明確寫入空清單，清掉 .asset 內可能殘留的舊項目。
        settings.hotUpdateAssemblyDefinitions = new AssemblyDefinitionAsset[0];
        settings.outputLinkFile = OutputLinkFile;
        settings.outputAOTGenericReferenceFile = OutputAOTGenericReferenceFile;
        HybridCLRSettings.Save();
    }

    /// <summary>
    /// 目前平台要做補充元數據的 AOT 組件名稱（不含 .dll，已排序）。
    /// HPS 是寫進 HybridCLRSettings.patchAOTAssemblies 再讀回來；9.0.0 起沒有那個欄位，改成直接由這裡提供。
    /// </summary>
    public static List<string> AotAssemblyNames
    {
        get
        {
            List<string> newList;
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android:
                    newList = GetAotAssemblyList_Android();
                    break;
                case BuildTarget.iOS:
                    newList = GetAotAssemblyList_IOS();
                    break;
                default:
                    // 非 Android/iOS（Editor 自測用的 StandaloneOSX / StandaloneWindows64 等）一律套用 PC 清單
                    newList = GetAotAssemblyList_PC();
                    break;
            }
            newList.Sort(System.StringComparer.Ordinal);
            return newList;
        }
    }

    // 各平台共用的 AOT assembly 基底清單（最終會被排序，此處順序不影響結果）。
    // ⚠️ 只能列「會進 player 的組件」：沒進 player 的組件不會出現在裁剪後 AOT 目錄，
    // CopyAndRenameDll 會因找不到 dll 而中斷。日後模組程式用到其他 AOT 組件的泛型
    // （例如 YooAsset、UnityEngine.UI），再依 GenerateAll 產生的 Assets/HybridCLRGenerate/AOTGenericReferences.cs 補進來。
    private static readonly List<string> AotAssemblyListBase = new List<string>
    {
        "EntryScene",
        "System.Core",
        "System",
        "UniTask",
        "Unity.InputSystem",
        "Unity.TextMeshPro",
        "UnityEngine.CoreModule",
        "mscorlib",
    };

    // Android 在基底清單之外額外需要的 AOT assembly（目前沒有，保留結構）
    private static readonly List<string> AotAssemblyListAndroidExtra = new List<string>();

    // iOS 在基底清單之外額外需要的 AOT assembly（目前沒有，保留結構）
    private static readonly List<string> AotAssemblyListIOSExtra = new List<string>();

    private static List<string> GetAotAssemblyList_PC()
    {
        return new List<string>(AotAssemblyListBase);
    }

    private static List<string> GetAotAssemblyList_Android()
    {
        var list = new List<string>(AotAssemblyListBase);
        list.AddRange(AotAssemblyListAndroidExtra);
        return list;
    }

    private static List<string> GetAotAssemblyList_IOS()
    {
        var list = new List<string>(AotAssemblyListBase);
        list.AddRange(AotAssemblyListIOSExtra);
        return list;
    }

    private static void CopyAotDllFromAotAssemblyNames()
    {
        var srcPath = SettingsUtil.GetAssembliesPostIl2CppStripDir(EditorUserBuildSettings.activeBuildTarget);
        var dllBytesPath = Path.Combine(Application.dataPath, aotBytesPath);
        // 先刪標記再清空：複製中途失敗時不會留下「標記說齊全、檔案卻不齊」的狀態
        if (File.Exists(AotDllBytesStampPath))
            File.Delete(AotDllBytesStampPath);
        EnsureEmptyFolder(dllBytesPath);
        CopyAndRenameDll(srcPath, dllBytesPath, AotAssemblyNames);
        Directory.CreateDirectory(Path.GetDirectoryName(AotDllBytesStampPath));
        File.WriteAllText(AotDllBytesStampPath, CurrentAotDllBytesStamp);
    }

    /// <summary>
    /// 記錄 Assets/DllBytes/AotDllBytes 是哪個平台、哪個 Unity 版本產生的。
    /// AotDllBytes 是所有平台共用的資料夾，只看檔案在不在的話，切平台後會把別的平台的 AOT dll 打進代碼包，
    /// 補充元數據與 player 不同源。放在 Library（不能放在收集路徑底下，否則會被打進代碼包）；
    /// Library 被清掉時標記消失，視為不齊、重產，是安全的方向。
    /// </summary>
    private static string AotDllBytesStampPath => Path.Combine(ProjectRoot, "Library", "HybridCLR", "AotDllBytes.stamp");

    private static string CurrentAotDllBytesStamp => $"{EditorUserBuildSettings.activeBuildTarget}|{Application.unityVersion}";

    /// <summary>AotDllBytes 是否由目前平台、目前 Unity 版本產生（見 <see cref="AotDllBytesStampPath"/>）。</summary>
    public static bool IsAotDllBytesForCurrentTarget()
    {
        return File.Exists(AotDllBytesStampPath) && File.ReadAllText(AotDllBytesStampPath) == CurrentAotDllBytesStamp;
    }

    private static void CopyAndRenameDll(string sourcePath, string destPath, List<string> listDllFile)
    {
        var missingDlls = new List<string>();
        foreach (var dllFileName in listDllFile)
        {
            string srcDllPath = $"{sourcePath}/{dllFileName}.dll";
            if (!File.Exists(srcDllPath))
            {
                missingDlls.Add(srcDllPath);
                continue;
            }

            var destFileNewName = $"{destPath}/{dllFileName}.dll.bytes";
            File.Copy(srcDllPath, destFileNewName, true);
            Debug.LogFormat("[CopyDllToAssets]: FileName = {0}, {1} -> {2}", dllFileName, srcDllPath, destPath);
        }

        // Refresh 交由呼叫端統一處理，避免重複刷新（見 ProjectDllWork / AotDllWork）
        if (missingDlls.Count > 0)
        {
            throw new System.Exception($"CopyAndRenameDll 缺少以下 dll，中斷打包流程:\n{string.Join("\n", missingDlls)}");
        }
    }

    /// <summary>
    /// 確保資料夾存在且為空（不存在則建立，存在則清空內容）。
    /// Assets/DllBytes/* 不進版控，fresh checkout 時由這裡建出來。
    /// </summary>
    public static void EnsureEmptyFolder(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                return;
            }

            foreach (var file in Directory.GetFiles(folderPath))
            {
                File.Delete(file);
            }
            foreach (var subdirectory in Directory.GetDirectories(folderPath))
            {
                Directory.Delete(subdirectory, true);
            }
        }
        catch (System.Exception ex)
        {
            throw new System.Exception($"清空/建立資料夾失敗: {folderPath}", ex);
        }
    }
}
