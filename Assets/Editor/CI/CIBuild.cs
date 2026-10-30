using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Jenkins 用的 batchmode 打包入口（Tools/Jenkins/ 的腳本呼叫），平台照 active build target（命令列帶 -buildTarget）。
///
///   Unity -batchmode -quit -projectPath &lt;專案&gt; -buildTarget WebGL|Android \
///     -executeMethod CIBuild.Run -ciMode full|asset|code -logFile &lt;log&gt;
///
/// 模式：
///   full  ＝完整出包：代碼包（重產 AOT dll）＋資源包＋主體（WebGL 網頁或 APK），等同各平台的「一鍵出包」選單。
///   asset ＝只打資源包（AssetPackage）。
///   code  ＝只打代碼包（CodePackage），沿用上次完整出包產生的 AOT dll：只適用主體沒變（Entry、link.xml 沒改）的時候。
///   asset+code ＝兩個都打（先代碼包再資源包），不出主體。
///
/// 版號接在 ServerData/&lt;平台&gt;/&lt;bundleVersion&gt;/&lt;套件&gt;/&lt;套件&gt;.version 指針後面；Jenkins 會先把指針同步成 GCS 上的版號。
/// 結果寫進 Builds/ci_result.txt（key=value，一行一個），給上傳步驟讀。失敗時丟例外，Unity 以非 0 結束碼退出。
/// </summary>
public static class CIBuild
{
    private const string ResultRelative = "Builds/ci_result.txt";

    public static void Run()
    {
        var mode = GetArg("-ciMode") ?? "full";
        var target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.WebGL && target != BuildTarget.Android)
            throw new Exception($"[CIBuild] 不支援的平台 {target}（命令列要帶 -buildTarget WebGL 或 Android）");

        var resultPath = Path.Combine(HybridCLRTools.ProjectRoot, ResultRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        if (File.Exists(resultPath))
            File.Delete(resultPath);

        Debug.Log($"[CIBuild] 開始：平台 {target}、模式 {mode}、bundleVersion {PlayerSettings.bundleVersion}");
        string summary;
        if (mode == "full")
        {
            summary = target == BuildTarget.WebGL ? WebGLBuildTool.BuildAll() : AndroidBuildTool.BuildAll();
        }
        else
        {
            var parts = mode.Split('+');
            if (parts.Length == 0 || parts.Any(p => p != "asset" && p != "code"))
                throw new Exception($"[CIBuild] 不認得的 -ciMode '{mode}'（只接受 full、asset、code、asset+code）");
            var lines = new List<string>();
            // 與完整出包同順序：先代碼包再資源包
            if (parts.Contains("code"))
                lines.Add("CodePackage " + CodePackageBuildTool.BuildAll(forceAotDllWork: false));
            if (parts.Contains("asset"))
                lines.Add("AssetPackage " + AssetPackageBuildTool.BuildAndDeployAssetPackage());
            summary = string.Join("\n", lines);
        }

        File.WriteAllLines(resultPath, new[]
        {
            "platform=" + target,
            "mode=" + mode,
            "bundleVersion=" + PlayerSettings.bundleVersion,
            "productName=" + PlayerSettings.productName,
            "summary=" + summary.Replace("\n", " | "),
        });
        Debug.Log($"[CIBuild] 完成：{summary}");
    }

    private static string GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
