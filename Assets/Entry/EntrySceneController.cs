using Cysharp.Threading.Tasks;
using HybridCLR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace EntryScene
{
    /// <summary>
    /// Entry 場景的開機流程（HPS EntrySceneController 的精簡版）：
    ///   初始化 CodePackage 與 AssetPackage → 合計下載量 → 依序下載兩包 → 載 AOT metadata
    ///   → 依序 Assembly.Load（Assembly-CSharp 最後）→ 卸載 CodePackage 歸檔
    ///   → 把 AssetPackage 掛上 <see cref="GameAssets"/> → 反射呼叫 Assembly-CSharp 的 <c>AppBootstrap.Run()</c>。
    /// 任何一步失敗都停在錯誤畫面，按「重試」重跑整條流程（已完成的 dll 載入與 AssetPackage 會跳過）。
    ///
    /// HPS 裡的 Addressables、gate、VersionDataModel、hacker、catalog、hash json、Boot 場景都不搬
    /// （本專案沒有 Addressables 可載 Boot 場景，所以改用反射呼叫遊戲入口）。
    /// 畫面是 <see cref="EntryLoadingUi"/>（uGUI：狀態文字、分段加權的進度條、失敗時的重試、提示窗）。
    /// Android 會先檢查主體（APK）版本，有新版就強制更新（<see cref="AppUpdateChecker"/>）。
    /// </summary>
    public class EntrySceneController : MonoBehaviour
    {
        private const string AssemblyCSharpName = "Assembly-CSharp";
        private const string AssemblyCSharpDllName = AssemblyCSharpName + ".dll";
        private const string CodePackageFolderName = CodePackageLoader.PackageName;
        private const string GameEntryTypeName = "AppBootstrap";
        private const string GameEntryMethodName = "Run";

#if UNITY_EDITOR
        // 與 editor-build 的 CodePackageCollectorSetup 收集路徑一致（見共同規則的介面約定）。
        private static readonly string[] EditorDllBytesFolders =
        {
            "Assets/DllBytes/AotDllBytes",
            "Assets/DllBytes/ProjectDllBytes",
        };

        // 與 AssetPackageCollectorSetup.SidescrollerSpritesCollectPath 一致（見共同規則的介面約定）。
        private const string EditorSpritesFolder = "Assets/Art/Sidescroller/Sprites";
#endif

        [SerializeField]
        [Tooltip("CodePackage／AssetPackage 產物的伺服器根目錄。實際遠端根目錄 = {serverRoot}/{平台名}/{Application.version}/{CodePackage|AssetPackage}。WebGL 另可用相對路徑與同源的 ?serverRoot= 網址參數（見 ResolveWebServerRoot）")]
        private string serverRoot = "https://storage.googleapis.com/peter_hybridclr_test/blender";

        private CodePackageLoader codePackageLoader = null;
        private AssetPackageLoader assetPackageLoader = null;
        private bool _startupFlowRunning = false;
        private bool _gameEntered = false;

        /// <summary>Player 下 Assembly.Load 進來的 Assembly-CSharp；Editor 下恆為 null。</summary>
        private Assembly _moduleAssembly = null;

        /// <summary>
        /// AOT／project dll 全部載入成功時的 CodePackage 內部版號；還沒全部載完為 null。
        /// 不為 null 時 Retry 直接跳過整段 CodePackage（不再連網），只重試遊戲入口之前的步驟。
        /// </summary>
        private string _dllsLoadedForVersion = null;

        /// <summary>
        /// 已成功載入的 dll 名稱（AOT metadata 與 Assembly.Load 各記一份）。
        ///
        /// 兩種載入都不能重做，而且都無法卸載：
        /// - LoadMetadataForAOTAssembly 對已載入的 assembly 回傳 HOMOLOGOUS_ASSEMBLY_HAS_LOADED，這裡視為錯誤。
        /// - Assembly.Load 對同名、非 placeholder 的 assembly 不會報錯，而是默默再多載一份
        ///   （只有 placeholder 才丟例外；依整合審查引用的 HybridCLR 9.0.3
        ///   libil2cpp/hybridclr/metadata/Assembly.cpp 第 170-199 行）。多一份同名組件會讓型別解析
        ///   與靜態狀態分裂成兩套，比報錯更難查。
        /// 所以要逐顆記：只載入一部分就失敗時（例如 AOT 全部完成、project 歸檔讀取失敗），
        /// Retry 才能跳過已載入的、只補還沒載的，不會卡在 AOT 的錯誤碼上，也不會重複載入 project dll。
        /// </summary>
        private readonly HashSet<string> _loadedAotDlls = new HashSet<string>();
        private readonly HashSet<string> _loadedProjectDlls = new HashSet<string>();

        [SerializeField]
        [Tooltip("載入畫面的素材與中文字型（Assets/Entry/UI），由 Tools/Entry/設定載入畫面 指派")]
        private EntryLoadingUi.Assets loadingAssets = new EntryLoadingUi.Assets();

        private EntryLoadingUi _ui;

        /// <summary>主體版本已檢查過且不用更新（Retry 時不再檢查）。</summary>
        private bool _appUpdateChecked = false;

        #region 進度分段（加權）
        // 整條進度條依階段分配：沒有下載量時下載段直接走完，進度一樣會前進
        private const float InitEnd = 0.10f;       // 檢查主體版本、連線伺服器（初始化兩包）
        private const float DownloadEnd = 0.70f;   // 下載更新
        private const float AotEnd = 0.85f;        // 載入 AOT 補充元數據
        private const float ModuleEnd = 0.95f;     // 載入遊戲模組 dll
        // 0.95～1：進入遊戲
        #endregion

        #region Life Cycle
        private void Start()
        {
            _ui = new EntryLoadingUi(loadingAssets, Retry);
            StartEntryProcess();
        }

        private void OnDestroy()
        {
            _ui?.Dispose();
            _ui = null;
        }
        #endregion

        #region Private Function
        /// <summary>顯示提示窗，等玩家按下其中一個按鈕，回傳按鈕的索引。</summary>
        private UniTask<int> AskAsync(string text, params string[] buttons) => _ui.AskAsync(text, buttons);

        private void SetProgress(float progress) => _ui?.SetProgress(progress);

        private void StartEntryProcess()
        {
            _ui.ShowRetry(false);
            _ui.SetProgress(0f, true);
            RunStartupFlowAsync().Forget();
        }

        /// <summary>
        /// ServerData 底下對應目前平台的資料夾名稱，必須與 Editor 端打包用的
        /// EditorUserBuildSettings.activeBuildTarget 列舉名一致。
        /// </summary>
        private static string PlatformBuildTargetDir
        {
            get
            {
#if UNITY_ANDROID
                return "Android";
#elif UNITY_IOS
                return "iOS";
#elif UNITY_STANDALONE_OSX
                return "StandaloneOSX";
#elif UNITY_STANDALONE_WIN
                // BuildTarget 分 StandaloneWindows（32 位元）與 StandaloneWindows64，
                // 執行期的 RuntimePlatform 只有 WindowsPlayer，所以用指標大小區分。
                // ⚠️ Editor 加 CODEPACKAGE_EDITOR_HOST_PLAY 測遠端流程、目標平台又是 32 位元
                // StandaloneWindows 時，Editor 本身是 64 位元行程，這裡會回傳 StandaloneWindows64，
                // 與打包產物的資料夾對不上。Player 不受影響。
                return IntPtr.Size == 8 ? "StandaloneWindows64" : "StandaloneWindows";
#elif UNITY_STANDALONE_LINUX
                return "StandaloneLinux64";
#elif UNITY_WEBGL
                return "WebGL";
#else
                Debug.LogWarning($"[Startup] 未定義平台資料夾對應，改用 Application.platform: {Application.platform}");
                return Application.platform.ToString();
#endif
            }
        }

        /// <summary>{serverRoot}/{平台名}/{Application.version}/CodePackage</summary>
        private string CodePackageRootUrl =>
            $"{EffectiveServerRoot.TrimEnd('/')}/{PlatformBuildTargetDir}/{Application.version}/{CodePackageFolderName}";

        /// <summary>{serverRoot}/{平台名}/{Application.version}/AssetPackage</summary>
        private string AssetPackageRootUrl =>
            $"{EffectiveServerRoot.TrimEnd('/')}/{PlatformBuildTargetDir}/{Application.version}/{AssetPackageLoader.PackageName}";

        /// <summary>
        /// 實際使用的伺服器根目錄。WebGL Player 以外的平台恆為 <see cref="serverRoot"/>，行為不變。
        /// WebGL Player 見 <see cref="ResolveWebServerRoot"/>。
        /// </summary>
        private string EffectiveServerRoot
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // 頁面網址與序列化值在執行期間都不會變，解析一次就好（也免得 log 重複印）。
                if (_webServerRoot == null)
                    _webServerRoot = ResolveWebServerRoot(serverRoot, Application.absoluteURL);
                return _webServerRoot;
#else
                return serverRoot;
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private const string ServerRootQueryKey = "serverRoot";

        /// <summary>WebGL Player 解析後的伺服器根目錄快取（見 <see cref="EffectiveServerRoot"/>）。</summary>
        private string _webServerRoot = null;

        /// <summary>
        /// WebGL：決定伺服器根目錄。
        /// 1. 頁面網址帶 <c>?serverRoot=…</c>，而且解析後與頁面**同源**（同 scheme、host、port）、
        ///    路徑又落在**頁面所在目錄底下**時用它，給本機測試與測試站用
        ///    （例如 <c>?serverRoot=ServerData</c>，與遊戲放在同一個伺服器）。
        ///    ⚠️ 這個值決定要載入哪份遊戲模組代碼，若接受任意網址，別人只要給一條連結就能讓玩家
        ///    在我們的網域上執行他的 dll。只比同源還不夠：GCS 這類路徑式網址（storage.googleapis.com/&lt;bucket&gt;/…）
        ///    所有 bucket 都同源，所以再要求路徑在頁面目錄之下。不符的值一律忽略並警告。
        /// 2. 否則用序列化的 <see cref="serverRoot"/>；它是相對路徑（不含 ://）時以頁面網址為基準解析，
        ///    方便把 ServerData 與遊戲部署在同一個網站。絕對網址照用（跨源時伺服器要開 CORS）。
        ///    序列化值不受第 1 點的限制：它是我們自己出包時決定的。
        /// 解析失敗時退回序列化值原樣，交給後面的初始化報錯。
        /// </summary>
        private static string ResolveWebServerRoot(string serialized, string pageUrl)
        {
            Uri page;
            if (string.IsNullOrEmpty(pageUrl) || !Uri.TryCreate(pageUrl, UriKind.Absolute, out page))
            {
                Debug.LogWarning($"[Startup] 無法解析頁面網址 '{pageUrl}'，serverRoot 照原樣使用");
                return serialized;
            }

            var queryValue = GetQueryParameter(page, ServerRootQueryKey);
            if (!string.IsNullOrEmpty(queryValue))
            {
                var overridden = ResolveAgainstPage(page, queryValue);
                if (overridden != null && IsUnderPageDirectory(overridden, page))
                {
                    var root = overridden.GetLeftPart(UriPartial.Path).TrimEnd('/');
                    Debug.Log($"[Startup] 使用網址參數 {ServerRootQueryKey}：{root}");
                    return root;
                }
                Debug.LogWarning($"[Startup] 網址參數 {ServerRootQueryKey}='{queryValue}' 不在頁面所在的同源目錄底下，已忽略");
            }

            if (!string.IsNullOrEmpty(serialized) && !serialized.Contains("://"))
            {
                var relative = ResolveAgainstPage(page, serialized);
                if (relative != null)
                    return relative.GetLeftPart(UriPartial.Path).TrimEnd('/');
            }
            return serialized;
        }

        /// <summary>
        /// 以頁面網址為基準解析 value。先當 http(s) 絕對網址，否則當相對路徑。
        /// 不直接用 <c>Uri.TryCreate(baseUri, string, …)</c>：那個多載內部以 RelativeOrAbsolute 判斷，
        /// Mono 系的 BCL 在非 Windows 上可能把「/ServerData」當成 file:///ServerData。
        /// </summary>
        private static Uri ResolveAgainstPage(Uri page, string value)
        {
            Uri absolute;
            if (Uri.TryCreate(value, UriKind.Absolute, out absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
                return absolute;

            Uri relative, combined;
            if (Uri.TryCreate(value, UriKind.Relative, out relative) && Uri.TryCreate(page, relative, out combined))
                return combined;
            return null;
        }

        /// <summary>candidate 與頁面同源，而且路徑在頁面所在目錄（最後一個 / 之前）底下。</summary>
        private static bool IsUnderPageDirectory(Uri candidate, Uri page)
        {
            if (Uri.Compare(candidate, page, UriComponents.SchemeAndServer, UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            var pagePath = page.AbsolutePath;
            var pageDir = pagePath.Substring(0, pagePath.LastIndexOf('/') + 1);
            // AbsolutePath 已把 ../ 正規化掉，比的是跳脫後的形式，兩邊一致。
            return candidate.AbsolutePath.StartsWith(pageDir, StringComparison.Ordinal);
        }

        /// <summary>取查詢字串裡第一個同名參數的值（已 URL 解碼）；沒有就回 null。</summary>
        private static string GetQueryParameter(Uri uri, string key)
        {
            var query = uri.Query;
            if (string.IsNullOrEmpty(query))
                return null;
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                var index = pair.IndexOf('=');
                var name = Uri.UnescapeDataString(index < 0 ? pair : pair.Substring(0, index));
                if (name != key)
                    continue;
                return index < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(index + 1).Replace('+', ' '));
            }
            return null;
        }
#endif

        private async UniTask RunStartupFlowAsync()
        {
            if (_startupFlowRunning)
            {
                Debug.LogWarning("[Startup] 開機流程已在執行中，忽略重複呼叫");
                return;
            }
            _startupFlowRunning = true;
            try
            {
                // 主體要先更新：新的代碼包是配合新主體打的，舊主體不能繼續往下載
                if (!_appUpdateChecked)
                {
                    if (await CheckAppUpdateAsync())
                        return;
                    _appUpdateChecked = true;
                }

                var updateCode = false;
                if (_dllsLoadedForVersion != null)
                {
                    // 前一輪 dll 已全部載入、是後面的步驟失敗；dll 無法換版，不必再連網。
                    Debug.Log($"[Startup] dll 已於前次嘗試載入完成（{_dllsLoadedForVersion}），Retry 跳過 CodePackage");
                }
                else if (ShouldSkipCodePackageInEditor())
                {
                    SetStatus("Editor：DllBytes 收集資料夾未就緒，略過 CodePackage 載入（原因見 Console 警告）");
                }
                else
                {
                    updateCode = true;
                }

                var updateAsset = false;
                if (GameAssets.IsReady)
                {
                    // 前一輪 AssetPackage 已就緒、是後面的步驟失敗；保留同一份，不再連網。
                    Debug.Log("[Startup] AssetPackage 已於前次嘗試就緒，Retry 跳過 AssetPackage");
                }
                else if (ShouldSkipAssetPackageInEditor())
                {
                    SetStatus("Editor：美術素材資料夾未就緒，略過 AssetPackage（GameAssets 不可用，原因見 Console 警告）");
                }
                else
                {
                    updateAsset = true;
                }

                if (updateCode || updateAsset)
                    await UpdateContentAsync(updateCode, updateAsset);

                SetStatus("進入遊戲");
                SetProgress(ModuleEnd);
                await UniTask.Yield(); // 讓這一幀先畫出「進入遊戲」
                EnterGame();
                SetProgress(1f);
                Debug.Log("[Startup] 遊戲模組代碼已啟動");
                // 遊戲的第一個畫面（選單）建好、蓋上來之後再拆載入畫面，中間不會閃黑
                await UniTask.Delay(TimeSpan.FromSeconds(0.5), ignoreTimeScale: true);
                _ui?.Dispose();
                _ui = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Startup] 發生未預期例外，中止開機流程: {ex}");
                // 反射呼叫（Editor 模擬建置、遊戲入口）丟出的是 TargetInvocationException，
                // 外層訊息沒有資訊量，顯示內層的。
                var shown = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                if (_gameEntered)
                {
                    // 遊戲入口已呼叫過（Run() 本身拋例外）：重跑不會再進一次，提供重試沒有意義。
                    SetStatus($"遊戲模組代碼執行失敗，請重啟 App：{shown.Message}");
                }
                else
                {
                    SetStatus($"載入失敗：{shown.Message}");
                    _ui?.ShowRetry(true);
                }
            }
            finally
            {
                _startupFlowRunning = false;
            }
        }

        /// <summary>
        /// Editor 模擬模式下，DllBytes 的收集資料夾還沒準備好時，略過整段 CodePackage，
        /// 直接進遊戲入口（Editor 裡 Assembly-CSharp 本來就已經載入了）。「沒準備好」指：
        /// - 任一收集資料夾不存在：YooAsset 收集器要求每個收集路徑都是 AssetDatabase 認得的資料夾，
        ///   否則丟「Collect path is invalid」（3.0.6：BundleCollector.CheckConfigError，BundleCollector.cs 第 87-89 行）。
        ///   例如 fresh checkout 只產生過 ProjectDllBytes 時，模擬建置必失敗、重試也無效。
        /// - 兩個資料夾都沒有任何 .bytes：模擬建置會因收集不到資源而失敗（TaskGetBuildMap 的 PackAssetListIsEmpty）。
        /// 兩個資料夾都存在、且至少一個有 .bytes 才走模擬建置。Player 恆回傳 false。
        /// </summary>
        private static bool ShouldSkipCodePackageInEditor()
        {
#if UNITY_EDITOR
            // 只有模擬模式會走模擬建置；Editor 改走遠端流程（CODEPACKAGE_EDITOR_HOST_PLAY）時不看本地資料夾。
            if (!CodePackageLoader.IsEditorSimulate)
                return false;
            var missing = EditorDllBytesFolders.Where(f => !System.IO.Directory.Exists(f)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogWarning("[Startup] Editor：收集資料夾不存在（" + string.Join("、", missing) +
                                 "），模擬建置會失敗，略過 CodePackage 載入。請先執行 CodePackage 打包。");
                return true;
            }
            var hasAny = EditorDllBytesFolders.Any(f =>
                System.IO.Directory.GetFiles(f, "*.bytes", System.IO.SearchOption.AllDirectories).Length > 0);
            if (!hasAny)
            {
                Debug.LogWarning("[Startup] Editor：" + string.Join("、", EditorDllBytesFolders) +
                                 " 底下沒有任何 .bytes（尚未執行 CodePackage 打包），略過 CodePackage 載入。");
                return true;
            }
            return false;
#else
            return false;
#endif
        }

        /// <summary>
        /// Editor 模擬模式下，美術素材收集資料夾不存在或沒有任何檔案時略過 AssetPackage
        /// （模擬建置會因 Collect path is invalid／PackAssetListIsEmpty 失敗，重試也無效；理由同
        /// <see cref="ShouldSkipCodePackageInEditor"/>）。略過時 <see cref="GameAssets.IsReady"/> 維持 false。
        /// Player 恆回傳 false：素材缺了就是產物不完整，照常失敗、讓玩家重試。
        /// </summary>
        private static bool ShouldSkipAssetPackageInEditor()
        {
#if UNITY_EDITOR
            if (!AssetPackageLoader.IsEditorSimulate)
                return false;
            if (!System.IO.Directory.Exists(EditorSpritesFolder))
            {
                Debug.LogWarning($"[Startup] Editor：{EditorSpritesFolder} 不存在（art 素材尚未落地），略過 AssetPackage。");
                return true;
            }
            var hasAny = System.IO.Directory.EnumerateFiles(EditorSpritesFolder, "*", System.IO.SearchOption.AllDirectories)
                // .meta 與 .DS_Store 之類的隱藏檔不是素材，AssetDatabase 也不會收集
                .Any(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                          !System.IO.Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal));
            if (!hasAny)
            {
                Debug.LogWarning($"[Startup] Editor：{EditorSpritesFolder} 底下沒有任何素材，略過 AssetPackage。");
                return true;
            }
            return false;
#else
            return false;
#endif
        }

        /// <summary>
        /// Android 主體版本檢查（強制更新）。回傳 true＝更新流程接手，開機流程不往下走。
        /// 讀不到版本資訊（網路錯誤、伺服器上還沒有）時記警告、照常開機：不因為檢查失敗讓玩家完全進不了遊戲。
        /// 其他平台與 Editor 不檢查。
        /// </summary>
        private async UniTask<bool> CheckAppUpdateAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            SetStatus("檢查主體版本");
            var folderUrl = AppUpdateChecker.AppFolderUrl(EffectiveServerRoot);
            AppUpdateChecker.AppVersionInfo remote;
            long local;
            try
            {
                remote = await AppUpdateChecker.FetchAsync(folderUrl);
                local = AppUpdateChecker.LocalVersionCode();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppUpdate] 主體版本檢查失敗，略過：{ex.Message}");
                return false;
            }
            if (remote == null)
            {
                Debug.Log("[AppUpdate] 伺服器上沒有主體版本資訊，略過");
                return false;
            }
            Debug.Log($"[AppUpdate] 已安裝 versionCode {local}，伺服器 {remote.versionCode}（{remote.apk}）");
            if (remote.versionCode <= local)
                return false;
            await RunAppUpdateAsync(folderUrl, remote, local);
            return true;
#else
            await UniTask.CompletedTask;
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// 強制更新：提示窗（確認＝下載並安裝、取消＝退出遊戲）→ 下載與驗證 → 需要時請玩家允許「安裝未知應用程式」
        /// → 叫出系統安裝畫面。系統安裝畫面被取消時可以再叫一次；不提供跳過。
        /// </summary>
        private async UniTask RunAppUpdateAsync(string folderUrl, AppUpdateChecker.AppVersionInfo remote, long local)
        {
            SetStatus("主體有新版本");
            var size = remote.size > 0 ? $"，下載約 {FormatBytes(remote.size)}" : string.Empty;
            if (await AskAsync($"發現新版本（版號 {remote.versionCode}，目前 {local}）。\n需要更新才能繼續遊戲{size}。\n\n確認：自動下載並安裝\n取消：退出遊戲",
                    "確認", "取消") != 0)
            {
                QuitGame();
                return;
            }

            string apkPath = null;
            while (apkPath == null)
            {
                try
                {
                    // 更新主體時整條進度條都給 APK 下載用
                    SetStatus("下載新版本");
                    _ui.SetProgress(0f, true);
                    apkPath = await AppUpdateChecker.DownloadAsync(folderUrl, remote, p =>
                    {
                        SetProgress(p);
                        if (remote.size > 0)
                            _ui.SetStatus($"下載新版本：{FormatBytes((long)(remote.size * p))} / {FormatBytes(remote.size)}");
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AppUpdate] 下載失敗：{ex.Message}");
                    if (await AskAsync($"下載失敗：{ex.Message}", "重試", "退出遊戲") != 0)
                    {
                        QuitGame();
                        return;
                    }
                }
            }

            while (true)
            {
                if (!AppUpdateChecker.CanInstall())
                {
                    SetStatus("等待允許安裝");
                    if (await AskAsync("安裝前要允許本遊戲「安裝未知應用程式」。\n按「前往設定」打開設定頁，允許後回到遊戲按「繼續」。",
                            "前往設定", "退出遊戲") != 0)
                    {
                        QuitGame();
                        return;
                    }
                    AppUpdateChecker.OpenInstallSettings();
                    if (await AskAsync("允許後回到這裡按「繼續」。", "繼續", "退出遊戲") != 0)
                    {
                        QuitGame();
                        return;
                    }
                    continue;
                }

                SetStatus("安裝新版本");
                AppUpdateChecker.Install(apkPath);
                if (await AskAsync("請在系統畫面按「安裝」。\n安裝完成後重新開啟遊戲。", "重新安裝", "退出遊戲") != 0)
                {
                    QuitGame();
                    return;
                }
            }
        }
#endif

        private void QuitGame()
        {
            SetStatus("退出遊戲");
            Application.Quit();
        }

        /// <summary>
        /// 兩包都先初始化、問出下載量，合計後一次顯示，再依序下載；全部下載完才載 dll、掛 GameAssets。
        /// 先下載完再載 dll：任一包下載失敗時還沒有任何不可逆的 Assembly.Load。
        /// </summary>
        private async UniTask UpdateContentAsync(bool updateCode, bool updateAsset)
        {
            var codePackageSize = updateCode ? await PrepareCodePackageAsync() : 0L;
            var assetPackageSize = updateAsset ? await PrepareAssetPackageAsync() : 0L;
            await DownloadContentAsync(updateCode, codePackageSize, updateAsset, assetPackageSize);

            if (updateCode)
            {
                await LoadModuleDllsAsync();

                // dll 都載完了，讓 YooAsset 放掉歸檔與逐檔副本（細節見 CodePackageLoader.UnloadAsync）。
                // 必須顯式呼叫——handle.Release() 只減 RefCount，不會卸載資源包。
                await codePackageLoader.UnloadAsync();
            }

            if (updateAsset)
            {
                // AssetPackage 不卸載：遊戲模組代碼整個遊戲期間都會透過 GameAssets 載素材。
                GameAssets.Attach(assetPackageLoader);
                Debug.Log($"[Startup] AssetPackage 就緒（內部版號 {assetPackageLoader.InternalVersion}），GameAssets 可用");
            }
        }

        /// <summary>
        /// 建立這次要用的 CodePackage loader，並問出它要下載多少。
        /// </summary>
        /// <returns>CodePackage 這次要下載的位元組數（已在本地快取的不計入）。</returns>
        private async UniTask<long> PrepareCodePackageAsync()
        {
            if (!CodePackageLoader.IsEditorSimulate && string.IsNullOrWhiteSpace(serverRoot))
                throw new InvalidOperationException("EntrySceneController.serverRoot 未設定");

            // 若這是 Retry() 重跑（上一輪已把 CodePackage 初始化成功、但後段才失敗），
            // 下面即將重新 new 出來的 loader 會沿用同名的 YooAsset package——該 package
            // 只要成功初始化過一次，再次呼叫 InitializePackageAsync 就會直接丟
            // 「already initialized」，不論網路是否已經恢復都一樣失敗。
            // 重新初始化前必須先銷毀舊的。
            if (codePackageLoader != null)
            {
                await codePackageLoader.DestroyAsync();
                codePackageLoader = null;
            }

            SetStatus("連線伺服器");
            SetProgress(InitEnd * 0.3f);
            Debug.Log($"[開始載入] CodePackageRootUrl: {CodePackageRootUrl}" +
                      (CodePackageLoader.IsEditorSimulate ? "（Editor 模擬模式不使用）" : string.Empty));
            codePackageLoader = new CodePackageLoader(CodePackageRootUrl);
            await codePackageLoader.InitializeAsync();

            var codePackageSize = codePackageLoader.CreateDownloaderAndGetSize();
            Debug.Log($"codePackageSize: {FormatBytes(codePackageSize)}" +
                      $"（內部版號 {codePackageLoader.InternalVersion}）");
            return codePackageSize;
        }

        /// <summary>
        /// 建立這次要用的 AssetPackage loader，並問出它要下載多少。重建前先銷毀舊的，理由同 <see cref="PrepareCodePackageAsync"/>。
        /// </summary>
        /// <returns>AssetPackage 這次要下載的位元組數（已在本地快取的不計入）。</returns>
        private async UniTask<long> PrepareAssetPackageAsync()
        {
            if (!AssetPackageLoader.IsEditorSimulate && string.IsNullOrWhiteSpace(serverRoot))
                throw new InvalidOperationException("EntrySceneController.serverRoot 未設定");

            if (assetPackageLoader != null)
            {
                // 走到這裡代表上一輪沒有成功掛上 GameAssets（成功的話 IsReady 為 true，不會重跑），
                // 保險起見仍先卸下，避免遊戲模組代碼拿到正在銷毀的 loader。
                GameAssets.Attach(null);
                await assetPackageLoader.DestroyAsync();
                assetPackageLoader = null;
            }

            SetStatus("連線伺服器");
            SetProgress(InitEnd * 0.65f);
            Debug.Log($"[開始載入] AssetPackageRootUrl: {AssetPackageRootUrl}" +
                      (AssetPackageLoader.IsEditorSimulate ? "（Editor 模擬模式不使用）" : string.Empty));
            assetPackageLoader = new AssetPackageLoader(AssetPackageRootUrl);
            await assetPackageLoader.InitializeAsync();

            var assetPackageSize = assetPackageLoader.CreateDownloaderAndGetSize();
            Debug.Log($"assetPackageSize: {FormatBytes(assetPackageSize)}" +
                      $"（內部版號 {assetPackageLoader.InternalVersion}）");
            return assetPackageSize;
        }

        /// <summary>
        /// 依序下載 CodePackage 與 AssetPackage，進度以兩包合計顯示。
        /// 斷點續傳、失敗重試、CRC 校驗都由 YooAsset 內建，這裡只負責顯示進度。
        /// </summary>
        private async UniTask DownloadContentAsync(bool updateCode, long codePackageSize, bool updateAsset, long assetPackageSize)
        {
            // ⚠️ 「有沒有檔案要下載」交給各 loader 的 DownloadAsync() 依 downloader 的
            // TotalDownloadCount 判斷，這裡不自己先判一次就提前返回，免得兩層的拆除行為分歧。
            var total = codePackageSize + assetPackageSize;
            Debug.Log(total > 0
                ? $"開始下載：CodePackage {FormatBytes(codePackageSize)} + AssetPackage {FormatBytes(assetPackageSize)}" +
                  $" = {FormatBytes(total)}"
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL 沒有預下載（見 CodePackageLoader 的 WebGL 分支），下載量恆為 0，檔案在載入時才抓。
                : "[DownloadContent] WebGL 不預先下載，載入時才向伺服器取檔");
#else
                : "[DownloadContent] 全部命中本地快取，不需下載");
#endif
            SetProgress(InitEnd);
            if (total > 0)
                SetStatus($"下載更新：0 MB / {FormatMegabytes(total)}");

            var lastLoggedPercent = -1;
            void Report(long downloaded)
            {
                var ratio = total > 0 ? Mathf.Clamp01(downloaded * 1f / total) : 0f;
                SetProgress(Mathf.Lerp(InitEnd, DownloadEnd, ratio));
                _ui?.SetStatus($"下載更新：{FormatMegabytes(downloaded)} / {FormatMegabytes(total)}");
                // log 只在每跨 10% 時記一次，免得洗版。
                var percent = (int)(ratio * 10) * 10;
                if (percent != lastLoggedPercent)
                {
                    lastLoggedPercent = percent;
                    Debug.Log($"[DownloadContent] {percent}%（{FormatBytes(downloaded)} / {FormatBytes(total)}）");
                }
            }

            if (updateCode)
                await codePackageLoader.DownloadAsync(
                    onProgress: null,
                    onBytesChanged: (downloaded, _) => Report(downloaded));
            if (updateAsset)
                await assetPackageLoader.DownloadAsync((downloaded, _) => Report(codePackageSize + downloaded));

            // 沒有下載量（全部命中快取、WebGL）時這一段直接走完
            SetProgress(DownloadEnd);
            if (total > 0)
                Debug.Log("結束下載");
        }

        /// <summary>
        /// 把 AOT 與模組 dll 載進執行期。已載入的個別 dll 會跳過——
        /// 理由見 <see cref="_loadedAotDlls"/> 的欄位註解。
        /// </summary>
        private async UniTask LoadModuleDllsAsync()
        {
            var version = codePackageLoader.InternalVersion;
            SetStatus("載入 AOT 補充元數據");
            await LoadAotDlls();
            Debug.Log("Load AotDlls finish.");
            SetStatus("載入遊戲模組");
            SetProgress(AotEnd);
            await UniTask.Yield();
            await LoadProjectDlls();
            Debug.Log("Load ProjectDlls finish.");
            SetProgress(ModuleEnd);
            _dllsLoadedForVersion = version;
        }

        /// <summary>
        /// 載入 AOT Dll（補充 metadata）。Editor 下 RuntimeApi.LoadMetadataForAOTAssembly 直接回傳 OK。
        /// </summary>
        private async UniTask LoadAotDlls()
        {
            Debug.Log("LoadAotDlls");
            var assets = await codePackageLoader.LoadDllsAsync(
                CodePackageLoader.AotTag, allowEmpty: CodePackageLoader.IsEditorSimulate);
            assets = assets.OrderBy(a => a.Name).ToList(); // 確保載入順序一致，方便除錯
            for (var i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                // 每顆一段進度＋讓畫面更新一幀（LoadMetadataForAOTAssembly 是同步的）
                _ui?.SetStatus($"載入 AOT 補充元數據（第 {i + 1}/{assets.Count} 顆）");
                SetProgress(Mathf.Lerp(DownloadEnd, AotEnd, (float)i / assets.Count));
                await UniTask.Yield();
                if (_loadedAotDlls.Contains(asset.Name))
                {
                    Debug.Log($"[Load aot dll]: {asset.Name} 已於前次嘗試載入，略過");
                    continue;
                }
                Debug.Log($"[Load aot dll]: {asset.Name}");
                var errorCode = RuntimeApi.LoadMetadataForAOTAssembly(asset.Bytes, HomologousImageMode.SuperSet);
                if (errorCode == LoadImageErrorCode.OK)
                {
                    _loadedAotDlls.Add(asset.Name);
                    continue;
                }

                Debug.LogError($"[Load aot dll failed]: {asset.Name}, error code: {errorCode}");
                throw new Exception($"LoadMetadataForAOTAssembly failed: {asset.Name}, error code: {errorCode}");
            }
        }

        /// <summary>
        /// 載入 project Dll：其他模組 dll 先、Assembly-CSharp 最後。
        /// Editor 下不呼叫 Assembly.Load（Assembly-CSharp 等模組組件本來就已載入，重複載入會出現兩份同名組件），
        /// 只走一遍 YooAsset 的取值流程並記 log。
        /// </summary>
        private async UniTask LoadProjectDlls()
        {
            Debug.Log("LoadProjectDlls");
            var assets = await codePackageLoader.LoadDllsAsync(
                CodePackageLoader.ProjectTag, allowEmpty: CodePackageLoader.IsEditorSimulate);
            assets = assets.OrderBy(a => a.Name).ToList(); // 確保載入順序一致，方便除錯
            foreach (var asset in assets)
            {
                Debug.Log($"[Project Dll]: dll name = {asset.Name}");
            }

            // load other dlls except Assembly-CSharp
            foreach (var asset in assets)
            {
                if (asset.Name == AssemblyCSharpDllName)
                    continue;
                LoadProjectDllOnce(asset);
            }

            // load Assembly-CSharp
            var asset_assemblyCSharp = assets.FirstOrDefault(a => a.Name == AssemblyCSharpDllName);
            if (asset_assemblyCSharp == null)
            {
                if (CodePackageLoader.IsEditorSimulate)
                {
                    Debug.LogWarning($"[Editor] CodePackage 裡沒有 {AssemblyCSharpDllName}，略過（Editor 已載入 Assembly-CSharp）。");
                    return;
                }
                Debug.LogError($"{AssemblyCSharpDllName} is missing!");
                throw new Exception($"{AssemblyCSharpDllName} is missing!");
            }
            var loaded = LoadProjectDllOnce(asset_assemblyCSharp);
            if (loaded != null)
                _moduleAssembly = loaded;
        }

        /// <summary>載入一顆 project dll；已於前次嘗試載入過就略過並回傳 null。</summary>
        private Assembly LoadProjectDllOnce(DllAssetData asset)
        {
            if (_loadedProjectDlls.Contains(asset.Name))
            {
                Debug.Log($"[Load Project dll]: {asset.Name} 已於前次嘗試載入，略過");
                return null;
            }
            Debug.Log($"[Load Project dll]: {asset.Name}");
            var assembly = LoadAssembly(asset);
            _loadedProjectDlls.Add(asset.Name);
            return assembly;
        }

        /// <summary>
        /// 載入單一 dll 的 Assembly，失敗時記錄錯誤並中止流程。Editor 下只記 log，不實際載入（回傳 null）。
        /// </summary>
        private static Assembly LoadAssembly(DllAssetData asset)
        {
#if UNITY_EDITOR
            Debug.Log($"[Editor] 略過 Assembly.Load: {asset.Name}（{asset.Bytes.Length} bytes）");
            return null;
#else
            try
            {
                return Assembly.Load(asset.Bytes);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Load Project dll failed]: {asset.Name}, {ex}");
                throw;
            }
#endif
        }

        /// <summary>
        /// 以反射呼叫 Assembly-CSharp 裡的 <c>AppBootstrap.Run()</c>（public static、無命名空間）。
        /// Player 下的 Assembly-CSharp 是上面 Assembly.Load 進來的那一份；Editor 下是原本就載入的那一份。
        /// 遊戲入口只會進一次：Run() 丟例外時不提供重試，畫面改提示重啟 App（見 RunStartupFlowAsync 的 catch）。
        /// </summary>
        private void EnterGame()
        {
            if (_gameEntered)
            {
                Debug.LogWarning("[Startup] 遊戲入口已呼叫過，不重複呼叫");
                return;
            }

            // Player 優先用剛 Assembly.Load 進來的那份；Editor（或 dll 已在前一輪載入、這輪跳過時）
            // 才退回依名稱在 AppDomain 裡找。
            var assembly = _moduleAssembly ?? AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == AssemblyCSharpName);
            if (assembly == null)
                throw new Exception($"找不到已載入的 {AssemblyCSharpName}，無法進入遊戲入口");

            var type = assembly.GetType(GameEntryTypeName);
            if (type == null)
                throw new Exception($"{AssemblyCSharpName} 裡找不到型別 {GameEntryTypeName}");

            var method = type.GetMethod(GameEntryMethodName, BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            if (method == null)
                throw new Exception($"找不到 public static {GameEntryTypeName}.{GameEntryMethodName}()");

            Debug.Log($"[Startup] 呼叫 {GameEntryTypeName}.{GameEntryMethodName}()");
            _gameEntered = true;
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // 拆掉反射的外殼、保留原本的堆疊，讓錯誤訊息直接指向遊戲模組代碼。
                Debug.LogError($"[Startup] {GameEntryTypeName}.{GameEntryMethodName}() 拋出例外");
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        private void SetStatus(string status)
        {
            _ui?.SetStatus(status);
            Debug.Log($"[Startup] {status}");
        }

        /// <summary>下載量一律用 MB 顯示（小檔也不換單位，數字才不會跳來跳去）。</summary>
        private static string FormatMegabytes(long byteSize) => $"{byteSize / 1048576.0:0.0} MB";

        private static string FormatBytes(long byteSize)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = byteSize;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0 ? $"{byteSize} B" : $"{size:0.##} {units[unit]}";
        }
        #endregion

        #region Public Function
        /// <summary>
        /// 開機流程因例外結束後重跑整條流程。流程仍在執行中時忽略。
        /// </summary>
        public void Retry()
        {
            if (_startupFlowRunning)
                return;
            StartEntryProcess();
        }
        #endregion
    }
}
