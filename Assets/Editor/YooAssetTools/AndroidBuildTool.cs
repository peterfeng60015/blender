using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 原生 Android 一鍵出包：Android Player Settings → 代碼包（重產 AOT dll）→ 資源包 → APK。
///
/// 產物：
///   Builds/Android/{productName}-{bundleVersion}.apk  安裝檔，不進版控
///   ServerData/Android/{bundleVersion}/CodePackage/    代碼包（與其他平台同一套配號與攤平規則）
///   ServerData/Android/{bundleVersion}/AssetPackage/   資源包
/// Player 走 HostPlayMode，執行時從 {EntrySceneController.serverRoot}/Android/{Application.version}/… 下載，
/// 所以 ServerData/Android 要放到 serverRoot 底下手機才拿得到。
///
/// ⚠️ 一定要在 active build target 已經是 Android 的 Editor 裡跑（batchmode 帶 <c>-buildTarget Android</c>）。
/// 不在同一個行程裡切完平台就出包：HybridCLR 9.0.3 修補 Gradle 專案組件清單的 hook
/// （PatchScriptingAssemblyList）只在 <c>UNITY_ANDROID</c> 編譯，切平台後要等 Editor 重編譯才會生效。
/// 從選單執行而平台不是 Android 時，會詢問是否切換，切完請等編譯結束再按一次。
///
/// 每次都重產 AOT dll 的理由與 <see cref="WebGLBuildTool"/> 相同（補充元數據要與這次的 player 同源，
/// GenerateAll 會以 Android 完整跑一次 BuildPlayer，整個流程 build 兩次，很慢）。
/// 只更新代碼、APK 不變時，改用 <c>Tools/YooAsset/一鍵打包代碼包</c>（切在 Android 平台下）即可。
/// </summary>
public static class AndroidBuildTool
{
    private const string BuildOutputRelative = "Builds/Android";

    #region 選單與一鍵流程

    [MenuItem("Tools/Android/一鍵出 Android 包（代碼包＋資源包＋APK）", false, 50)]
    public static void OneClickBuildAndroid()
    {
        // batchmode（-executeMethod）不攔例外：讓 Unity 以非 0 結束碼退出，呼叫端才知道失敗。
        if (Application.isBatchMode)
        {
            BuildAll();
            return;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            if (EditorUtility.DisplayDialog("Android",
                    $"目前平台是 {EditorUserBuildSettings.activeBuildTarget}，要先切到 Android。\n" +
                    "切換後 Editor 會重新編譯，結束後請再執行一次本選單。", "切到 Android", "取消"))
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }
            return;
        }

        try
        {
            var summary = BuildAll();
            EditorUtility.DisplayDialog("Android", $"Android 出包完成：\n{summary}", "確定");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Android", $"Android 出包失敗：\n{ex.Message}", "確定");
        }
    }

    [MenuItem("Tools/Android/套用 Android Player Settings", false, 51)]
    public static void ApplyAndroidPlayerSettingsMenu()
    {
        ApplyAndroidPlayerSettings();
    }

    /// <summary>一鍵流程本體。回傳給人看的摘要。</summary>
    public static string BuildAll()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new Exception("[AndroidBuildTool] 目前平台是 " + EditorUserBuildSettings.activeBuildTarget +
                                "，請先切到 Android（File > Build Profiles，或 batchmode 帶 -buildTarget Android）再執行。");

        ApplyAndroidPlayerSettings();

        // 先代碼包再資源包：代碼包失敗就不往下做（與 Tools/YooAsset/一鍵打包全部 相同的順序）。
        var code = CodePackageBuildTool.BuildAll(forceAotDllWork: true);
        var asset = AssetPackageBuildTool.BuildAndDeployAssetPackage();
        var output = BuildPlayer();

        var summary = $"CodePackage {code} / AssetPackage {asset}\nAPK: {output}";
        Debug.Log($"[AndroidBuildTool] 完成：{summary}");
        return summary;
    }

    #endregion

    #region Player Settings

    /// <summary>
    /// 套用原生 Android 需要的設定（冪等）：
    /// - 方向：自動旋轉、只允許兩個橫向。allowedAutorotate* 與 defaultInterfaceOrientation 是各平台共用的鍵，
    ///   WebGL 不讀這幾個鍵（WebGL 的鎖橫向由網頁範本處理），iOS 這次不出包。
    /// - IL2CPP（HybridCLR 必要）、只出 ARM64（現行 Android 手機都是 64 位元，省一半 il2cpp 時間與 APK 大小）。
    /// - 強制 INTERNET 權限：代碼包／資源包都從遠端下載，不靠 Unity 的自動偵測
    ///   （偵測看的是 player 裡的組件，遊戲模組代碼不在 player 裡）。
    /// 出 APK 不出 AAB（側載實測用；上架 Google Play 才需要 AAB 與正式簽章）由 <see cref="BuildPlayer"/> 出包期間暫時切換。
    /// </summary>
    public static void ApplyAndroidPlayerSettings()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        var signed = ApplyReleaseKeystore();

        AssetDatabase.SaveAssets();
        Debug.Log("[AndroidBuildTool] 已套用 Android Player Settings：自動旋轉限橫向、IL2CPP、ARM64、強制 INTERNET 權限、" +
                  (signed ? "正式簽章" : "除錯簽章"));
    }

    /// <summary>
    /// 正式簽章設定檔：環境變數 BLENDER_KEYSTORE_PROPERTIES，沒設時用 %USERPROFILE%/.blender-keystore/release.properties。
    /// 內容是 storeFile、storePassword、keyAlias、keyPassword（key=value 一行一個）。keystore 與密碼都不進版控，
    /// 換電腦出包要把整個 .blender-keystore 資料夾帶過去——簽章不同的 APK 不能覆蓋安裝，主體自動更新會失敗。
    /// </summary>
    public static string ReleaseKeystorePropertiesPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("BLENDER_KEYSTORE_PROPERTIES");
            return !string.IsNullOrEmpty(env)
                ? env
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".blender-keystore", "release.properties");
        }
    }

    /// <summary>
    /// 有正式簽章設定檔就用它簽（回傳 true），沒有就用 Unity 的除錯簽章並警告（回傳 false）。
    /// 密碼只放在這次 Editor 的記憶體裡（Unity 不會把 keystore 密碼寫進 ProjectSettings）。
    /// </summary>
    public static bool ApplyReleaseKeystore()
    {
        var path = ReleaseKeystorePropertiesPath;
        if (!File.Exists(path))
        {
            PlayerSettings.Android.useCustomKeystore = false;
            Debug.LogWarning($"[AndroidBuildTool] 找不到正式簽章設定 {path}，改用除錯簽章（這個 APK 不能覆蓋正式簽章的版本）");
            return false;
        }
        var props = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#") && l.Contains("="))
            .ToDictionary(l => l.Substring(0, l.IndexOf('=')).Trim(), l => l.Substring(l.IndexOf('=') + 1).Trim());
        string Get(string key) => props.TryGetValue(key, out var v) && v.Length > 0
            ? v
            : throw new Exception($"[AndroidBuildTool] {path} 缺少 {key}");
        var storeFile = Get("storeFile");
        if (!File.Exists(storeFile))
            throw new Exception($"[AndroidBuildTool] 找不到 keystore {storeFile}");
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = storeFile;
        PlayerSettings.Android.keystorePass = Get("storePassword");
        PlayerSettings.Android.keyaliasName = Get("keyAlias");
        PlayerSettings.Android.keyaliasPass = Get("keyPassword");
        return true;
    }

    #endregion

    #region Player

    /// <summary>以 Build Settings 裡啟用的場景出 APK 到 Builds/Android。回傳 APK 的絕對路徑。</summary>
    private static string BuildPlayer()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
            throw new Exception("[AndroidBuildTool] Build Settings 裡沒有啟用的場景");

        // bundleVersion 已由 CodePackageBuildTool 檢查過只含英數與 . - _，可直接當檔名。
        var apkName = $"{PlayerSettings.productName}-{PlayerSettings.bundleVersion}.apk";
        var output = Path.Combine(HybridCLRTools.ProjectRoot, BuildOutputRelative, apkName);
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        // 同名路徑若是之前匯出 Gradle 專案留下的資料夾，先刪掉，否則 Gradle 無法寫出 APK 檔。
        if (Directory.Exists(output))
            Directory.Delete(output, true);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None,
        };

        // 「匯出 Gradle 專案」與「出 AAB」是存在 Library 的 Editor 個人設定，開著時 BuildPlayer 不會出 APK。
        // 出包期間暫時關掉，結束後還原使用者原本的選擇（與 HybridCLR StripAOTDllCommand 的做法相同）。
        var oldExportAndroidProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        var oldBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
        BuildReport report;
        try
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.buildAppBundle = false;
            Debug.Log($"[AndroidBuildTool] 開始出 APK：{string.Join(", ", scenes)} → {output}");
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = oldExportAndroidProject;
            EditorUserBuildSettings.buildAppBundle = oldBuildAppBundle;
        }

        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception($"[AndroidBuildTool] APK 出包失敗：{report.summary.result}，" +
                                $"錯誤 {report.summary.totalErrors} 個（詳見 Console／log）");

        Debug.Log($"[AndroidBuildTool] APK 完成：{output}（{new FileInfo(output).Length / (1024 * 1024)} MB）");
        return output;
    }

    #endregion
}
