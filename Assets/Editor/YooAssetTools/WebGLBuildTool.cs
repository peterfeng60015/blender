using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// WebGL 一鍵出包：WebGL Player Settings → 代碼包（重產 AOT dll）→ 資源包 → WebGL Player。
///
/// 產物：
///   Builds/WebGL/                                   網頁本體（index.html、Build/、TemplateData/），不進版控
///   ServerData/WebGL/{bundleVersion}/CodePackage/   代碼包（與其他平台同一套配號與攤平規則）
///   ServerData/WebGL/{bundleVersion}/AssetPackage/  資源包
///
/// ⚠️ 一定要在 active build target 已經是 WebGL 的 Editor 裡跑（batchmode 帶 <c>-buildTarget WebGL</c>）。
/// 不在程式裡自動切平台：HybridCLR 9.0.3 修補 scriptingassemblies.json 的 hook
/// （PatchScriptingAssembliesJsonHook）只在 <c>UNITY_WEBGL</c> 編譯，切平台後要等 Editor 重編譯才會生效，
/// 同一個行程裡切完立刻出包拿不到。
///
/// 為什麼每次都重產 AOT dll：這裡會出新 player，補充元數據要與這次的 player 同源
/// （GenerateAll 以目前平台完整跑一次 BuildPlayer 取得裁剪後的 AOT dll，所以整個流程會 build 兩次 WebGL，很慢）。
/// 只更新代碼、player 不變時，改用 <c>Tools/YooAsset/一鍵打包代碼包</c>（切在 WebGL 平台下）即可。
/// </summary>
public static class WebGLBuildTool
{
    private const string BuildOutputRelative = "Builds/WebGL";

    #region 選單與一鍵流程

    [MenuItem("Tools/WebGL/一鍵出 WebGL 包（代碼包＋資源包＋Player）", false, 40)]
    public static void OneClickBuildWebGL()
    {
        // batchmode（-executeMethod）不攔例外：讓 Unity 以非 0 結束碼退出，呼叫端才知道失敗。
        if (Application.isBatchMode)
        {
            BuildAll();
            return;
        }

        try
        {
            var summary = BuildAll();
            EditorUtility.DisplayDialog("WebGL", $"WebGL 出包完成：\n{summary}", "確定");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("WebGL", $"WebGL 出包失敗：\n{ex.Message}", "確定");
        }
    }

    [MenuItem("Tools/WebGL/套用 WebGL Player Settings", false, 41)]
    public static void ApplyWebGLPlayerSettingsMenu()
    {
        ApplyWebGLPlayerSettings();
    }

    /// <summary>一鍵流程本體。回傳給人看的摘要。</summary>
    public static string BuildAll()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            throw new Exception("[WebGLBuildTool] 目前平台是 " + EditorUserBuildSettings.activeBuildTarget +
                                "，請先切到 WebGL（File > Build Profiles，或 batchmode 帶 -buildTarget WebGL）再執行。");

        ApplyWebGLPlayerSettings();

        // 先代碼包再資源包：代碼包失敗就不往下做（與 Tools/YooAsset/一鍵打包全部 相同的順序）。
        var code = CodePackageBuildTool.BuildAll(forceAotDllWork: true);
        var asset = AssetPackageBuildTool.BuildAndDeployAssetPackage();
        var output = BuildPlayer();

        var summary = $"CodePackage {code} / AssetPackage {asset}\nPlayer: {output}";
        Debug.Log($"[WebGLBuildTool] 完成：{summary}");
        return summary;
    }

    #endregion

    #region Player Settings

    /// <summary>手機版範本，在 Assets/WebGLTemplates/MobileLandscape（PROJECT: 表示專案內的範本）。</summary>
    private const string WebGLTemplate = "PROJECT:MobileLandscape";

    /// <summary>
    /// 只改 WebGL 專屬的設定（ProjectSettings 的 WebGL 區段），其他平台不受影響。
    /// - 範本 MobileLandscape：手機滿版、禁止縮放與捲動、第一次點擊進全螢幕並鎖橫向；桌機維持 Default 範本的版面。
    /// - 壓縮 Gzip＋Decompression Fallback：檔案約小一半以上（手機網路差很多），而且任何靜態主機都能跑——
    ///   伺服器有回 Content-Encoding: gzip 時由瀏覽器原生解壓，沒有時由載入器用 JS 解壓（較慢，但不會壞）。
    ///   不用 Brotli：瀏覽器只在 HTTPS 才解 br，區網 http 實測手機會落到 JS 解壓，而 JS 解 Brotli 很慢。
    ///   Tools/WebGL/serve.py 會對 gzip 內容的 .unityweb 回 Content-Encoding。
    /// - 資料快取開：.data 存進 IndexedDB，第二次開頁不必整包重抓。
    /// - 記憶體：起始 InitialMemoryMB、上限 2048、幾何成長。理由見 InitialMemoryMB。
    /// - 不開多執行緒：需要 SharedArrayBuffer（COOP／COEP 標頭），一般主機與 GCS 預設都沒有；
    ///   HybridCLR 與 YooAsset 的 Web 流程也不需要。
    /// - Scripting backend：WebGL 只有 IL2CPP，HybridCLR 的 CheckSettings 也會檢查。
    /// </summary>
    public static void ApplyWebGLPlayerSettings()
    {
        PlayerSettings.WebGL.template = WebGLTemplate;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.initialMemorySize = InitialMemoryMB;
        PlayerSettings.WebGL.maximumMemorySize = 2048;
        PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
        PlayerSettings.WebGL.threadsSupport = false;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WebGLBuildTool] 已套用 WebGL Player Settings：範本 {WebGLTemplate}、壓縮 Gzip（含 fallback）、" +
                  $"資料快取、起始記憶體 {InitialMemoryMB} MB、不開多執行緒、IL2CPP");
    }

    /// <summary>
    /// WebGL 起始記憶體（MB）。實測（2026-10-28，起始 32 MB）：開到選單約 138 MB，進《熱浪急件》開場與關卡後
    /// 長到約 199 MB 並維持。起始直接給 256 MB，開場不必一路成長：每次成長都要重新配置 wasm 記憶體，
    /// 手機上既慢又可能因為瞬間要新舊兩份而配置失敗。上限 2048 只是位址空間上限，不會預先佔用。
    /// 之後內容變多、實測超過時再往上調。
    /// </summary>
    private const int InitialMemoryMB = 256;

    #endregion

    #region Player

    /// <summary>以 Build Settings 裡啟用的場景出 WebGL player 到 Builds/WebGL。回傳輸出資料夾的絕對路徑。</summary>
    private static string BuildPlayer()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
            throw new Exception("[WebGLBuildTool] Build Settings 裡沒有啟用的場景");

        var output = Path.Combine(HybridCLRTools.ProjectRoot, BuildOutputRelative);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None,
        };

        Debug.Log($"[WebGLBuildTool] 開始出 WebGL player：{string.Join(", ", scenes)} → {output}");
        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception($"[WebGLBuildTool] WebGL player 出包失敗：{report.summary.result}，" +
                                $"錯誤 {report.summary.totalErrors} 個（詳見 Console／log）");

        Debug.Log($"[WebGLBuildTool] WebGL player 完成：{output}（{report.summary.totalSize / (1024 * 1024)} MB）");
        return output;
    }

    #endregion
}
