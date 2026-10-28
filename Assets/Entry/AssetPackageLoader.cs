using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace EntryScene
{
    /// <summary>
    /// AssetPackage（美術素材：sprite sheet 與其 json）的 YooAsset 載入封裝。
    ///
    /// 與 <see cref="CodePackageLoader"/> 並列，是執行期另一個允許出現 YooAsset API 的檔案；
    /// 遊戲模組代碼一律透過 <see cref="GameAssets"/>。刻意 internal：遊戲模組代碼不能自己 new 一個來繞過 GameAssets
    /// （例如誤呼叫 DestroyAsync 把正在用的 package 銷毀）。
    ///
    /// 流程（與 CodePackage 相同，差在 bundle 類型與沒有加密）：
    ///   Initialize（Player：HostPlayMode，遠端根 = {serverRoot}/{平台}/{Application.version}/AssetPackage；
    ///               WebGL Player：WebPlayMode，沒有本地沙盒快取，bundle 在第一次載入時才抓；
    ///               Editor：EditorSimulateMode + VirtualAssetBundle，不必先打包）
    ///   → RequestPackageVersionAsync → LoadPackageManifestAsync → 清快取
    ///   → CreateResourceDownloader（下載缺檔）
    ///   → LoadSubAssetsAsync&lt;Sprite&gt; / LoadAssetAsync&lt;TextAsset&gt;
    ///
    /// 產物是一般 AssetBundle（ScriptableBuildPipeline），收集器 PackTopDirectory：
    /// Assets/Art/Sidescroller/Sprites/&lt;Name&gt;/ 與 Data/&lt;Name&gt;/ 每個子資料夾一包。
    ///
    /// 與 CodePackage 不同，這個 package 載完**不卸載**：sprite 要一直被遊戲引用，
    /// 而且遊戲模組代碼會在遊戲進行中隨時要素材。
    /// </summary>
    internal sealed class AssetPackageLoader
    {
        public const string PackageName = "AssetPackage";

        private const int ManifestTimeoutSeconds = 30;
        private const int DownloadMaxConcurrency = 8;
        /// <summary>理由同 CodePackageLoader.DownloadRetryCount（線性退避，5 次共約 15 秒）。</summary>
        private const int DownloadRetryCount = 5;
        /// <summary>
        /// 啟用斷點續傳的檔案大小門檻。美術 bundle 一個角色一包，8 方向 sheet 加法線圖常在數 MB，
        /// 沿用 CodePackage 的 256 KB；前提同樣是遠端支援 HTTP Range。
        /// </summary>
        private const long ResumeDownloadMinimumSize = 256 * 1024;

        private readonly string _remoteRoot;
        private ResourcePackage _package;
        private ResourceDownloaderOperation _downloader;

        /// <summary>
        /// 已載入的 sprite 陣列，以 location 為鍵。handle 刻意不釋放（sprite 要常駐），
        /// 同一個 location 只建一次 handle，重複呼叫直接回傳快取的副本。
        /// </summary>
        private readonly Dictionary<string, Sprite[]> _spriteCache = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);

        /// <summary>常駐的主資產（模型、動畫控制器），以 location 為鍵；handle 同樣刻意不釋放。</summary>
        private readonly Dictionary<string, UnityEngine.Object> _assetCache = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

        /// <summary>本次實際生效的內部版號（{bundleVersion}.{n}；Editor 模擬模式為 "Simulate"），供記錄與除錯。</summary>
        public string InternalVersion { get; private set; } = string.Empty;

        /// <summary>
        /// 是否走 YooAsset 的編輯器模擬模式。Editor 預設是（直接讀 Assets/Art/Sidescroller，
        /// 不必先打包）；Player 走 HostPlayMode（WebGL Player 走 WebPlayMode）。
        ///
        /// 想在 Editor 裡測遠端流程時，加 scripting define <c>ASSETPACKAGE_EDITOR_HOST_PLAY</c>
        /// （與 CodePackage 的 <c>CODEPACKAGE_EDITOR_HOST_PLAY</c> 各自獨立），沙盒快取在 Library/yoo 底下。
        /// </summary>
        public static bool IsEditorSimulate
        {
            get
            {
#if UNITY_EDITOR && !ASSETPACKAGE_EDITOR_HOST_PLAY
                return true;
#else
                return false;
#endif
            }
        }

        /// <param name="remoteRoot">
        /// AssetPackage 產物所在的遠端根目錄，例如
        /// https://storage.googleapis.com/peter_hybridclr_test/blender/Android/1.0/AssetPackage
        /// （由 EntrySceneController 組出）。Editor 模擬模式不會用到，但仍要求非空。
        /// </param>
        public AssetPackageLoader(string remoteRoot)
        {
            if (string.IsNullOrEmpty(remoteRoot))
                throw new ArgumentException("remoteRoot 不可為空", nameof(remoteRoot));

            _remoteRoot = remoteRoot.TrimEnd('/');
        }

        /// <summary>
        /// 初始化資源包、讀指針取得內部版號、載入 manifest。完成後即可查詢下載量。
        /// </summary>
        public async UniTask InitializeAsync()
        {
            if (!YooAssets.IsInitialized)
                YooAssets.Initialize();

            if (YooAssets.ContainsPackage(PackageName))
                await DestroyLeftoverPackageAsync();

            _package = YooAssets.ContainsPackage(PackageName)
                ? YooAssets.GetPackage(PackageName)
                : YooAssets.CreatePackage(PackageName);

            var initOp = _package.InitializePackageAsync(CreateInitializeOptions());
            await WaitAndThrowOnFailure(initOp, "InitializePackageAsync");

            var versionOp = _package.RequestPackageVersionAsync();
            await WaitAndThrowOnFailure(versionOp, "RequestPackageVersionAsync");
            InternalVersion = versionOp.PackageVersion;
            Debug.Log($"[AssetPackageLoader] 指針指向的內部版號: {InternalVersion}");

            var manifestOp = _package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(InternalVersion, ManifestTimeoutSeconds));
            await WaitAndThrowOnFailure(manifestOp, "LoadPackageManifestAsync");

            // 模擬模式沒有沙盒快取，不必清。插入位置的理由同 CodePackageLoader.ClearStaleCacheAsync：
            // manifest 之後（以當前 manifest 判斷引用）、下載器與任何載入之前（清理不保護使用中的檔案）。
            if (!IsEditorSimulate)
                await ClearStaleCacheAsync();
        }

        /// <summary>理由同 CodePackageLoader.DestroyLeftoverPackageAsync。</summary>
        private static async UniTask DestroyLeftoverPackageAsync()
        {
            var leftover = YooAssets.GetPackage(PackageName);
            if (leftover.InitializeStatus == EOperationStatus.None)
                return;

            Debug.LogWarning($"[AssetPackageLoader] 發現殘留的 {PackageName}（{leftover.InitializeStatus}），先銷毀");
            var op = leftover.DestroyPackageAsync();
            await UniTask.WaitUntil(() => op.IsDone);
            if (op.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[AssetPackageLoader] 銷毀殘留 package 失敗: {op.Error}");
                return;
            }
            YooAssets.RemovePackage(PackageName);
        }

        private InitializePackageOptions CreateInitializeOptions()
        {
#if UNITY_EDITOR && !ASSETPACKAGE_EDITOR_HOST_PLAY
            // 編輯器模擬：VirtualAssetBundle 直接用 AssetDatabase 讀原始資產，sub-assets 走
            // AssetDatabase.LoadAllAssetRepresentationsAtPath（3.0.6：VABHLoadSubAssetsOperation）。
            // 需要收集器設定裡有 AssetPackage（由 AssetPackageCollectorSetup 產生）；
            // 收集路徑不存在或收集不到資源時建置失敗，EntrySceneController 會先檢查並略過（見 ShouldSkipAssetPackageInEditor）。
            Debug.Log($"[AssetPackageLoader] Editor 模擬模式，不使用遠端根目錄 {_remoteRoot}");
            var buildResult = EditorSimulateBuildInvoker.Build(PackageName, (int)EBundleType.VirtualAssetBundle);
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory),
            };
#elif UNITY_WEBGL
            // WebGL：WebPlayMode + WebNetworkFileSystem，理由與限制同 CodePackageLoader 的 WebGL 分支
            // （沒有預下載，下載量恆為 0；斷點續傳參數不能加）。
            // 不加密的 AssetBundle 走 UnityWebRequestAssetBundle，DisableUnityWebCache 維持預設 false：
            // 以 manifest 的 FileHash 當版本鍵存進 Unity 的 WebGL 快取（IndexedDB），第二次開頁不必重抓；
            // 檔名帶 hash，換版自然換鍵。bundle 在遊戲第一次要素材時才抓（GameAssets 都是非同步 API）。
            var webParams = FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(
                new AssetPackageRemoteService(_remoteRoot));
            // 重試次數加上限，理由見 WebDownloadRetryPolicy。
            webParams.AddParameter(EFileSystemParameter.DownloadRetryPolicy, new WebDownloadRetryPolicy());

            return new WebPlayModeOptions
            {
                WebServerFileSystemParameters = null,
                WebNetworkFileSystemParameters = webParams,
            };
#else
            // 聯機模式：沒有內建包，只掛沙盒快取檔案系統。一般 AssetBundle、不加密，不需要解密器。
            var cacheParams = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(
                new AssetPackageRemoteService(_remoteRoot));
            // 斷點續傳必須明確開啟，3.0.6 預設門檻是 long.MaxValue（細節見 CodePackageLoader 同段註解）。
            cacheParams.AddParameter(
                EFileSystemParameter.DownloadResumeMinimumSize, ResumeDownloadMinimumSize);

            return new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = null,
                CacheFileSystemParameters = cacheParams,
            };
#endif
        }

        /// <summary>
        /// 清掉不被當前 manifest 引用的 bundle 與非當前版本的 manifest。
        /// 失敗一律吞掉不外拋：清快取是最佳化，擋開機是災難。
        /// </summary>
        private async UniTask ClearStaleCacheAsync()
        {
            await ClearCacheAsync(ClearCacheMethods.ClearUnusedBundleFiles);
            await ClearCacheAsync(ClearCacheMethods.ClearUnusedManifestFiles);
        }

        private async UniTask ClearCacheAsync(string clearMethod)
        {
            try
            {
                var op = _package.ClearCacheAsync(new ClearCacheOptions(clearMethod));
                await op.ToUniTask();
                if (op.Status != EOperationStatus.Succeeded)
                {
                    Debug.LogWarning($"[AssetPackageLoader] {clearMethod} 失敗，略過清理: {op.Error}");
                    return;
                }
                Debug.Log($"[AssetPackageLoader] {clearMethod} 完成");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetPackageLoader] {clearMethod} 發生例外，略過清理: {ex}");
            }
        }

        /// <summary>
        /// 建立下載器並回傳這次需要下載的總位元組數（已在本地快取的不計入）。
        /// 回傳 0 代表全部命中快取（Editor 模擬模式恆為 0）。
        /// 下載全部 bundle：素材量小、遊戲一開始就幾乎全部用到，不做分批或按需下載。
        /// </summary>
        public long CreateDownloaderAndGetSize()
        {
            EnsureInitialized();

            _downloader = _package.CreateResourceDownloader(
                new ResourceDownloaderOptions(DownloadMaxConcurrency, DownloadRetryCount));

            Debug.Log($"[AssetPackageLoader] 待下載: {_downloader.TotalDownloadCount} 檔 / " +
                      $"{_downloader.TotalDownloadBytes} bytes");
            return _downloader.TotalDownloadBytes;
        }

        /// <summary>
        /// 執行下載。必須先呼叫 <see cref="CreateDownloaderAndGetSize"/>。
        /// 區域變數與零檔案不提前 return 的理由同 CodePackageLoader.DownloadAsync。
        /// </summary>
        /// <param name="onBytesChanged">(已下載位元組, 總位元組)</param>
        public async UniTask DownloadAsync(Action<long, long> onBytesChanged)
        {
            var downloader = _downloader;
            if (downloader == null)
                throw new InvalidOperationException("[AssetPackageLoader] 請先呼叫 CreateDownloaderAndGetSize()");

            void OnProgressChanged(DownloadProgressChangedEventArgs args)
            {
                onBytesChanged?.Invoke(args.CurrentDownloadBytes, args.TotalDownloadBytes);
            }

            downloader.DownloadProgressChanged += OnProgressChanged;
            try
            {
                if (downloader.TotalDownloadCount > 0)
                {
                    downloader.StartDownload();
                    await WaitAndThrowOnFailure(downloader, "AssetPackage 下載");
                }
            }
            finally
            {
                downloader.DownloadProgressChanged -= OnProgressChanged;
                if (ReferenceEquals(_downloader, downloader))
                    _downloader = null;
            }
        }

        /// <summary>
        /// 載入一張 sprite sheet 的全部 sprite（sub-assets），依名稱尾碼編號排序後回傳副本。
        /// handle 不釋放，sprite 常駐；同一個 location 第二次起直接回傳快取。
        /// </summary>
        public async UniTask<Sprite[]> LoadSpritesAsync(string location)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(location))
                throw new ArgumentException("location 不可為空", nameof(location));

            if (_spriteCache.TryGetValue(location, out var cached))
                return (Sprite[])cached.Clone();

            var handle = _package.LoadSubAssetsAsync<Sprite>(location);
            try
            {
                await WaitAndThrowOnFailure(handle, $"載入 sprites {location}");
            }
            catch
            {
                handle.Release();
                ForgetFailedLoad(location);
                throw;
            }

            // 等待期間可能有另一個呼叫已經載完同一個 location：用先放進快取的那份，
            // 這個 handle 釋放掉（只減 RefCount，底層資產是同一份）。
            if (_spriteCache.TryGetValue(location, out cached))
            {
                handle.Release();
                return (Sprite[])cached.Clone();
            }

            var subAssets = handle.GetSubAssetObjects<Sprite>();
            if (subAssets == null || subAssets.Count == 0)
            {
                handle.Release();
                throw new Exception($"[AssetPackageLoader] {location} 沒有任何 sprite（貼圖的 Sprite Mode 是否已設定？）");
            }

            var sprites = new Sprite[subAssets.Count];
            for (var i = 0; i < sprites.Length; i++)
                sprites[i] = subAssets[i];
            Array.Sort(sprites, CompareBySuffixNumber);

            _spriteCache[location] = sprites;
            return (Sprite[])sprites.Clone();
        }

        /// <summary>
        /// 載入一個要常駐的主資產（3D 模型、動畫控制器）。和 sprite 一樣 handle 不釋放，
        /// 同一個 location 第二次起直接回傳快取；回傳的是資產本身，呼叫端用 Instantiate 產生實例。
        /// </summary>
        public async UniTask<T> LoadResidentAssetAsync<T>(string location) where T : UnityEngine.Object
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(location))
                throw new ArgumentException("location 不可為空", nameof(location));

            if (_assetCache.TryGetValue(location, out var cached))
                return cached as T ?? throw new Exception($"[AssetPackageLoader] {location} 不是 {typeof(T).Name}");

            var handle = _package.LoadAssetAsync<T>(location);
            try
            {
                await WaitAndThrowOnFailure(handle, $"載入 {typeof(T).Name} {location}");
            }
            catch
            {
                handle.Release();
                ForgetFailedLoad(location);
                throw;
            }

            if (_assetCache.TryGetValue(location, out cached))
            {
                handle.Release();
                return cached as T ?? throw new Exception($"[AssetPackageLoader] {location} 不是 {typeof(T).Name}");
            }

            var asset = handle.GetAssetObject<T>();
            if (asset == null)
            {
                handle.Release();
                throw new Exception($"[AssetPackageLoader] {location} 不是 {typeof(T).Name}");
            }
            _assetCache[location] = asset;
            return asset;
        }

        /// <summary>載入 TextAsset 並回傳文字內容。字串已複製出來，handle 立即釋放。</summary>
        public async UniTask<string> LoadTextAsync(string location)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(location))
                throw new ArgumentException("location 不可為空", nameof(location));

            var handle = _package.LoadAssetAsync<TextAsset>(location);
            try
            {
                try
                {
                    await WaitAndThrowOnFailure(handle, $"載入文字 {location}");
                }
                catch
                {
                    handle.Release();
                    handle = null;
                    ForgetFailedLoad(location);
                    throw;
                }
                var textAsset = handle.GetAssetObject<TextAsset>();
                if (textAsset == null)
                    throw new Exception($"[AssetPackageLoader] {location} 不是 TextAsset");
                return textAsset.text;
            }
            finally
            {
                handle?.Release();
            }
        }

        /// <summary>
        /// 載入失敗後把失敗的 provider／bundle loader 從 YooAsset 移除，讓同一個 location 下次能真的重載。
        ///
        /// 不做的話會永遠失敗：3.0.6 的 ResourceManager 以 providerKey 重用 _providerDict 裡的 provider，
        /// handle.Release() 只減 RefCount、不移除；失敗的 bundle loader 也留在 _bundleLoaderDict 不會自動重試
        /// ——例如遊戲中途某包不在快取、觸發按需下載時剛好斷網，之後同一包的所有資源都會立刻拿到同一個失敗結果，
        /// 而那時已進入遊戲模組，Entry 的重試按鈕救不到。
        /// 呼叫前 handle 必須已 Release（RefCount 歸 0 才會被卸載）。清理本身失敗只記警告，不蓋掉原本的例外。
        /// </summary>
        private void ForgetFailedLoad(string location)
        {
            try
            {
                if (_package != null && _package.IsLocationValid(location))
                    _package.TryUnloadUnusedAsset(location);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetPackageLoader] 清除失敗的載入狀態時發生例外（已忽略）: {location}, {ex.Message}");
            }
        }

        /// <summary>
        /// 依名稱最後一個 '_' 之後的整數排序（SsHeroDay_Run_2 在 SsHeroDay_Run_10 之前）。
        /// 沒有數字尾碼的排在有尾碼的後面，彼此再以字串排序，讓結果固定。
        /// </summary>
        private static int CompareBySuffixNumber(Sprite a, Sprite b)
        {
            var hasA = TryGetSuffixNumber(a.name, out var na);
            var hasB = TryGetSuffixNumber(b.name, out var nb);
            if (hasA && hasB && na != nb)
                return na.CompareTo(nb);
            if (hasA != hasB)
                return hasA ? -1 : 1;
            return string.CompareOrdinal(a.name, b.name);
        }

        private static bool TryGetSuffixNumber(string name, out int number)
        {
            number = 0;
            var index = name.LastIndexOf('_');
            if (index < 0 || index == name.Length - 1)
                return false;
            for (var i = index + 1; i < name.Length; i++)
            {
                if (name[i] < '0' || name[i] > '9')
                    return false;
            }
            return int.TryParse(name.Substring(index + 1), out number);
        }

        /// <summary>
        /// 銷毀整個 package，用於重新初始化前清空。理由與 RemovePackage 一律嘗試的原因同
        /// CodePackageLoader.DestroyAsync。已掛在 <see cref="GameAssets"/> 上時由呼叫端先卸下。
        /// </summary>
        public async UniTask DestroyAsync()
        {
            if (_package == null)
                return;
            try
            {
                var op = _package.DestroyPackageAsync();
                await op.ToUniTask();
                if (op.Status != EOperationStatus.Succeeded)
                    Debug.LogError($"[AssetPackageLoader] DestroyPackageAsync 失敗: {op.Error}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetPackageLoader] DestroyPackageAsync 失敗（已忽略）: {ex.Message}");
            }
            finally
            {
                try
                {
                    YooAssets.RemovePackage(PackageName);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AssetPackageLoader] RemovePackage 失敗（已忽略）: {ex.Message}");
                }

                _package = null;
                _downloader = null;
                _spriteCache.Clear();
                _assetCache.Clear();
                InternalVersion = string.Empty;
            }
        }

        private void EnsureInitialized()
        {
            if (_package == null)
                throw new InvalidOperationException("[AssetPackageLoader] 尚未初始化，請先呼叫 InitializeAsync()");
        }

        private static async UniTask WaitAndThrowOnFailure(AsyncOperationBase operation, string what)
        {
            await UniTask.WaitUntil(() => operation.IsDone);
            if (operation.Status != EOperationStatus.Succeeded)
                throw new Exception($"[AssetPackageLoader] {what} 失敗: {operation.Error}");
        }

        private static async UniTask WaitAndThrowOnFailure(HandleBase handle, string what)
        {
            await UniTask.WaitUntil(() => handle.IsDone);
            if (handle.Status != EOperationStatus.Succeeded)
                throw new Exception($"[AssetPackageLoader] {what} 失敗: {handle.Error}");
        }

        /// <summary>所有檔案都直接掛在 AssetPackage 子目錄底下：根目錄 + 檔名。</summary>
        private sealed class AssetPackageRemoteService : IRemoteService
        {
            private readonly string _root;

            public AssetPackageRemoteService(string root)
            {
                _root = root;
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                return new[] { $"{_root}/{fileName}" };
            }
        }
    }
}
