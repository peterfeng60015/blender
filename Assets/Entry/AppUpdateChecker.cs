using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace EntryScene
{
    /// <summary>
    /// Android 主體（APK）更新：讀 GCS 上的版本資訊、下載 APK、驗證、叫出系統安裝畫面。
    /// 流程與提示窗在 <see cref="EntrySceneController"/>；Java 端在 Assets/Plugins/Android/AppUpdate。
    ///
    /// 版本資訊：{serverRoot}/Android/app/version.json（Jenkins 完整出包時上傳，見 Tools/Jenkins/ci.ps1）
    ///   { "versionCode": 3, "versionName": "1.0", "apk": "blender-3.apk", "size": 62364182, "sha256": "…" }
    /// apk 是相對 version.json 的檔名。遠端 versionCode 大於已安裝的就要更新（強制）。
    /// </summary>
    public static class AppUpdateChecker
    {
        [Serializable]
        public sealed class AppVersionInfo
        {
            public long versionCode;
            public string versionName;
            public string apk;
            public long size;
            public string sha256;
        }

        public const string VersionFileName = "version.json";
        private const int FetchTimeoutSeconds = 15;

        /// <summary>{serverRoot}/Android/app</summary>
        public static string AppFolderUrl(string serverRoot) => $"{serverRoot.TrimEnd('/')}/Android/app";

        /// <summary>讀版本資訊；伺服器上還沒有（404／403）回 null，其他錯誤丟例外。</summary>
        public static async UniTask<AppVersionInfo> FetchAsync(string folderUrl)
        {
            // 加時間參數避開快取：CDN 與手機上的 HTTP 快取都可能還留著舊版本資訊
            var url = $"{folderUrl}/{VersionFileName}?t={DateTime.UtcNow.Ticks}";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = FetchTimeoutSeconds;
                // 自己等而不用 UniTask 的 ToUniTask：它遇到 HTTP 錯誤會直接丟例外，這裡要分辨 404
                var op = request.SendWebRequest();
                while (!op.isDone)
                    await UniTask.Yield();
                // GCS 上檔案不存在時，bucket 沒開放列出物件會回 403 而不是 404，兩者都當作「還沒有版本資訊」
                if (request.responseCode == 404 || request.responseCode == 403)
                    return null;
                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception($"讀不到 {url}：{request.error}");
                var info = JsonUtility.FromJson<AppVersionInfo>(request.downloadHandler.text);
                if (info == null || info.versionCode <= 0 || string.IsNullOrEmpty(info.apk))
                    throw new Exception($"{url} 內容不完整：{request.downloadHandler.text}");
                return info;
            }
        }

        /// <summary>
        /// 下載 APK 到 Application.temporaryCachePath（ApkProvider 只提供這個資料夾），驗證大小與 SHA-256，回傳檔案路徑。
        /// 已經下載過、驗證也通過的直接用；其他舊的 APK 順便刪掉。
        /// </summary>
        public static async UniTask<string> DownloadAsync(string folderUrl, AppVersionInfo info, Action<float> onProgress)
        {
            var dir = Application.temporaryCachePath;
            var path = Path.Combine(dir, Path.GetFileName(info.apk));
            foreach (var old in Directory.GetFiles(dir, "*.apk"))
            {
                if (old != path)
                    File.Delete(old);
            }
            if (File.Exists(path) && await VerifyAsync(path, info) == null)
                return path;

            var tmp = path + ".download";
            using (var request = new UnityWebRequest($"{folderUrl}/{info.apk}", UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(tmp) { removeFileOnAbort = true };
                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                    onProgress?.Invoke(info.size > 0 ? Mathf.Clamp01((float)request.downloadedBytes / info.size) : request.downloadProgress);
                    await UniTask.Yield();
                }
                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception($"下載失敗：{request.error}");
            }
            onProgress?.Invoke(1f);
            var error = await VerifyAsync(tmp, info);
            if (error != null)
            {
                File.Delete(tmp);
                throw new Exception(error);
            }
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
            return path;
        }

        /// <summary>通過回 null，否則回錯誤訊息。雜湊在背景執行緒算（60 MB 上下）。</summary>
        private static async UniTask<string> VerifyAsync(string path, AppVersionInfo info)
        {
            var length = new FileInfo(path).Length;
            if (info.size > 0 && length != info.size)
                return $"檔案大小不符（{length}，應為 {info.size}）";
            if (string.IsNullOrEmpty(info.sha256))
                return null;
            var hash = await UniTask.RunOnThreadPool(() =>
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            });
            return string.Equals(hash, info.sha256, StringComparison.OrdinalIgnoreCase) ? null : "檔案驗證失敗（SHA-256 不符）";
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string InstallerClass = "com.peterfeng.blender.ApkInstaller";

        private static T CallInstaller<T>(string method, params object[] args)
        {
            using (var installer = new AndroidJavaClass(InstallerClass))
                return installer.CallStatic<T>(method, Prepend(UnityEngine.Android.AndroidApplication.currentActivity, args));
        }

        private static void CallInstaller(string method, params object[] args)
        {
            using (var installer = new AndroidJavaClass(InstallerClass))
                installer.CallStatic(method, Prepend(UnityEngine.Android.AndroidApplication.currentActivity, args));
        }

        private static object[] Prepend(object first, object[] rest)
        {
            var all = new object[rest.Length + 1];
            all[0] = first;
            Array.Copy(rest, 0, all, 1, rest.Length);
            return all;
        }

        /// <summary>已安裝主體的 versionCode。</summary>
        public static long LocalVersionCode() => CallInstaller<long>("versionCode");

        /// <summary>是否已允許這個 App「安裝未知應用程式」。</summary>
        public static bool CanInstall() => CallInstaller<bool>("canInstall");

        public static void OpenInstallSettings() => CallInstaller("openInstallSettings");

        /// <summary>叫出系統安裝畫面（玩家按「安裝」才會裝）。</summary>
        public static void Install(string apkPath) => CallInstaller("install", apkPath);
#endif
    }
}
