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
    /// Entry 場景的開機流程（HPS EntrySceneController 的精簡版，只保留 CodePackage 熱更 dll 載入）：
    ///   初始化 CodePackage → 算下載量 → 下載 → 載 AOT metadata → 依序 Assembly.Load（Assembly-CSharp 最後）
    ///   → 卸載 CodePackage 歸檔 → 反射呼叫 Assembly-CSharp 的 <c>HotUpdateMain.Run()</c>。
    /// 任何一步失敗都停在錯誤畫面，按「重試」重跑整條流程。
    ///
    /// HPS 裡的 Addressables、gate、VersionDataModel、hacker、catalog、hash json、Boot 場景都不搬
    /// （本專案沒有 Addressables 可載 Boot 場景，所以改用反射呼叫熱更入口）。
    /// 進度只用 log 加上 OnGUI 的簡單文字顯示，不依賴場景裡的 UI 物件。
    /// </summary>
    public class EntrySceneController : MonoBehaviour
    {
        private const string AssemblyCSharpName = "Assembly-CSharp";
        private const string AssemblyCSharpDllName = AssemblyCSharpName + ".dll";
        private const string CodePackageFolderName = CodePackageLoader.PackageName;
        private const string HotUpdateEntryTypeName = "HotUpdateMain";
        private const string HotUpdateEntryMethodName = "Run";

#if UNITY_EDITOR
        // 與 editor-build 的 CodePackageCollectorSetup 收集路徑一致（見共同規則的介面約定）。
        private static readonly string[] EditorDllBytesFolders =
        {
            "Assets/DllBytes/AotDllBytes",
            "Assets/DllBytes/ProjectDllBytes",
        };
#endif

        [SerializeField]
        [Tooltip("CodePackage 產物的伺服器根目錄。實際遠端根目錄 = {serverRoot}/{平台名}/{Application.version}/CodePackage")]
        private string serverRoot = "https://storage.googleapis.com/peter_hybridclr_test/blender";

        private CodePackageLoader codePackageLoader = null;
        private bool _startupFlowRunning = false;
        private bool _hotUpdateEntered = false;

        /// <summary>Player 下 Assembly.Load 進來的 Assembly-CSharp；Editor 下恆為 null。</summary>
        private Assembly _hotUpdateAssembly = null;

        /// <summary>
        /// AOT／project dll 全部載入成功時的 CodePackage 內部版號；還沒全部載完為 null。
        /// 不為 null 時 Retry 直接跳過整段 CodePackage（不再連網），只重試熱更入口之前的步驟。
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

        private string _status = string.Empty;
        private float _progress = 0f;
        private bool _failed = false;

        #region Life Cycle
        private void Start()
        {
            StartEntryProcess();
        }

        private void OnGUI()
        {
            var scale = Mathf.Max(1f, Screen.height / 720f);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                GUILayout.BeginArea(new Rect(16, 16, Screen.width / scale - 32, Screen.height / scale - 32));
                GUILayout.Label(_status);
                if (_progress > 0f && _progress < 1f)
                    GUILayout.Label($"{_progress:P1}");
                if (_failed && GUILayout.Button("重試", GUILayout.Width(160), GUILayout.Height(40)))
                    Retry();
                GUILayout.EndArea();
            }
            finally
            {
                GUI.matrix = matrix;
            }
        }
        #endregion

        #region Private Function
        private void StartEntryProcess()
        {
            _failed = false;
            _progress = 0f;
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
#else
                Debug.LogWarning($"[Startup] 未定義平台資料夾對應，改用 Application.platform: {Application.platform}");
                return Application.platform.ToString();
#endif
            }
        }

        /// <summary>{serverRoot}/{平台名}/{Application.version}/CodePackage</summary>
        private string CodePackageRootUrl =>
            $"{serverRoot.TrimEnd('/')}/{PlatformBuildTargetDir}/{Application.version}/{CodePackageFolderName}";

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
                    await UpdateContentAsync();
                }

                SetStatus("進入熱更代碼");
                EnterHotUpdate();
                SetStatus("熱更代碼已啟動");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Startup] 發生未預期例外，中止開機流程: {ex}");
                // 反射呼叫（Editor 模擬建置、熱更入口）丟出的是 TargetInvocationException，
                // 外層訊息沒有資訊量，顯示內層的。
                var shown = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                if (_hotUpdateEntered)
                {
                    // 熱更入口已呼叫過（Run() 本身拋例外）：重跑不會再進一次，提供重試沒有意義。
                    SetStatus($"熱更代碼執行失敗，請重啟 App：{shown.Message}");
                }
                else
                {
                    _failed = true;
                    SetStatus($"載入失敗：{shown.Message}");
                }
            }
            finally
            {
                _startupFlowRunning = false;
            }
        }

        /// <summary>
        /// Editor 模擬模式下，DllBytes 的收集資料夾還沒準備好時，略過整段 CodePackage，
        /// 直接進熱更入口（Editor 裡 Assembly-CSharp 本來就已經載入了）。「沒準備好」指：
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

        private async UniTask UpdateContentAsync()
        {
            var codePackageSize = await PrepareCodePackageAsync();
            await DownloadCodePackageAsync(codePackageSize);
            await LoadHotUpdateDllsAsync();

            // dll 都載完了，讓 YooAsset 放掉歸檔與逐檔副本（細節見 CodePackageLoader.UnloadAsync）。
            // 必須顯式呼叫——handle.Release() 只減 RefCount，不會卸載資源包。
            await codePackageLoader.UnloadAsync();
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

            SetStatus("初始化 CodePackage");
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
        /// 下載 CodePackage（HybridCLR 熱更 dll）。
        /// 斷點續傳、失敗重試、CRC 校驗都由 YooAsset 內建，這裡只負責顯示進度。
        /// </summary>
        private async UniTask DownloadCodePackageAsync(long codePackageSize)
        {
            // ⚠️ 「有沒有檔案要下載」交給 CodePackageLoader.DownloadAsync() 依 downloader 的
            // TotalDownloadCount 判斷，這裡不自己先判一次就提前返回，免得兩層的拆除行為分歧。
            Debug.Log(codePackageSize > 0
                ? $"開始下載 [CodePackage]: {FormatBytes(codePackageSize)}"
                : "[DownloadCodePackage] 全部命中本地快取，不需下載");
            if (codePackageSize > 0)
                SetStatus($"下載 CodePackage：0 B / {FormatBytes(codePackageSize)}");

            var lastLoggedPercent = -1;
            await codePackageLoader.DownloadAsync(
                onProgress: null,
                onBytesChanged: (downloaded, total) =>
                {
                    _progress = total > 0 ? downloaded * 1f / total : 0f;
                    _status = $"下載 CodePackage：{FormatBytes(downloaded)} / {FormatBytes(total)}";
                    // log 只在每跨 10% 時記一次，免得洗版。
                    var percent = (int)(_progress * 10) * 10;
                    if (percent != lastLoggedPercent)
                    {
                        lastLoggedPercent = percent;
                        Debug.Log($"[DownloadCodePackage] {percent}%（{FormatBytes(downloaded)} / {FormatBytes(total)}）");
                    }
                });

            _progress = 0f;
            if (codePackageSize > 0)
                Debug.Log("結束下載 [CodePackage]");
        }

        /// <summary>
        /// 把 AOT 與熱更 dll 載進執行期。已載入的個別 dll 會跳過——
        /// 理由見 <see cref="_loadedAotDlls"/> 的欄位註解。
        /// </summary>
        private async UniTask LoadHotUpdateDllsAsync()
        {
            var version = codePackageLoader.InternalVersion;
            SetStatus("載入 AOT metadata");
            await LoadAotDlls();
            Debug.Log("Load AotDlls finish.");
            SetStatus("載入熱更 dll");
            await LoadProjectDlls();
            Debug.Log("Load ProjectDlls finish.");
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
            foreach (var asset in assets)
            {
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
        /// 載入 project Dll：其他熱更 dll 先、Assembly-CSharp 最後。
        /// Editor 下不呼叫 Assembly.Load（Assembly-CSharp 等熱更組件本來就已載入，重複載入會出現兩份同名組件），
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
                _hotUpdateAssembly = loaded;
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
        /// 以反射呼叫 Assembly-CSharp 裡的 <c>HotUpdateMain.Run()</c>（public static、無命名空間）。
        /// Player 下的 Assembly-CSharp 是上面 Assembly.Load 進來的那一份；Editor 下是原本就載入的那一份。
        /// 熱更入口只會進一次：Run() 丟例外時不提供重試，畫面改提示重啟 App（見 RunStartupFlowAsync 的 catch）。
        /// </summary>
        private void EnterHotUpdate()
        {
            if (_hotUpdateEntered)
            {
                Debug.LogWarning("[Startup] 熱更入口已呼叫過，不重複呼叫");
                return;
            }

            // Player 優先用剛 Assembly.Load 進來的那份；Editor（或 dll 已在前一輪載入、這輪跳過時）
            // 才退回依名稱在 AppDomain 裡找。
            var assembly = _hotUpdateAssembly ?? AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == AssemblyCSharpName);
            if (assembly == null)
                throw new Exception($"找不到已載入的 {AssemblyCSharpName}，無法進入熱更入口");

            var type = assembly.GetType(HotUpdateEntryTypeName);
            if (type == null)
                throw new Exception($"{AssemblyCSharpName} 裡找不到型別 {HotUpdateEntryTypeName}");

            var method = type.GetMethod(HotUpdateEntryMethodName, BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            if (method == null)
                throw new Exception($"找不到 public static {HotUpdateEntryTypeName}.{HotUpdateEntryMethodName}()");

            Debug.Log($"[Startup] 呼叫 {HotUpdateEntryTypeName}.{HotUpdateEntryMethodName}()");
            _hotUpdateEntered = true;
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // 拆掉反射的外殼、保留原本的堆疊，讓錯誤訊息直接指向熱更代碼。
                Debug.LogError($"[Startup] {HotUpdateEntryTypeName}.{HotUpdateEntryMethodName}() 拋出例外");
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        private void SetStatus(string status)
        {
            _status = status;
            Debug.Log($"[Startup] {status}");
        }

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
