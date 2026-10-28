using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace EntryScene
{
    /// <summary>
    /// dll 名稱與內容的輕量副本，避免呼叫端持有載入 handle 造成 bytes 常駐記憶體。
    ///
    /// Name 的值是 YooAsset 的定位地址，由收集器的 AddressByFileName 規則產生
    /// （= Path.GetFileNameWithoutExtension），對 Assembly-CSharp.dll.bytes 得到
    /// "Assembly-CSharp.dll"。
    /// </summary>
    public sealed class DllAssetData
    {
        public readonly string Name;
        public readonly byte[] Bytes;

        public DllAssetData(string name, byte[] bytes)
        {
            Name = name;
            Bytes = bytes;
        }
    }

    /// <summary>
    /// CodePackage（HybridCLR 模組 dll）的 YooAsset 載入封裝。
    ///
    /// **執行期唯一允許出現 YooAsset API 的檔案**：其餘程式碼（含 EntrySceneController）
    /// 一律只看得到 <see cref="DllAssetData"/>。日後 YooAsset 升版或整個抽換，改動點只有這裡。
    ///
    /// 流程：
    ///   Initialize（Player：HostPlayMode，遠端根 = {serverRoot}/{平台}/{Application.version}/CodePackage；
    ///               Editor：EditorSimulateMode，不必先打包）
    ///   → RequestPackageVersionAsync（讀 CodePackage.version 指針，得內部版號）
    ///   → LoadPackageManifestAsync（載該版 manifest）
    ///   → CreateResourceDownloader（下載缺檔；斷點續傳與重試由 YooAsset 內建）
    ///   → LoadAssetAsync&lt;RawFileObject&gt; 逐檔取解密後 bytes
    ///
    /// 產物是 ArchiveFileBuildPipeline 打出的歸檔資源包（aot / project 各一顆 .archive，
    /// 內含各自的 dll）。歸檔對呼叫方是透明的——YooAsset 會按 AssetPath 從歸檔切出對應區段。
    ///
    /// ⚠️ 取 bytes 一定要走 LoadAssetAsync&lt;RawFileObject&gt; + RawFileObject.GetBytes()。
    /// 3.0.6 沒有「取檔案路徑再自己讀」這條路可走：EnsureBundleFileAsync 拿到的是**密文**路徑，
    /// 直接餵 Assembly.Load 會失敗。2.x 的 GetRawFileData() 在 3.0.6 預設根本不存在：
    /// 相容層 Runtime/Compatibility/CompatibleHandles.cs 整份包在 #if YOOASSET_LEGACY_API 裡，
    /// 沒定義就是編譯錯誤；定義了也只是直接 throw NotSupportedException 的空殼。
    /// </summary>
    public sealed class CodePackageLoader
    {
        public const string PackageName = "CodePackage";
        public const string AotTag = "aot";
        public const string ProjectTag = "project";

        private const int ManifestTimeoutSeconds = 30;
        private const int DownloadMaxConcurrency = 8;
        /// <summary>
        /// 下載失敗的重試次數。YooAsset 3.0.6 的退避是線性 +1 秒、上限 10 秒
        /// （DefaultDownloadRetryPolicy.CalculateRetryDelay：Clamp(previousDelay + 1, 1, 10)，
        /// 起始值 0），所以 N 次的總等待是 1+2+…+N —— 5 次 = 15 秒。
        ///
        /// 取 5 而非 3（= 6 秒）：行動網路的短暫中斷常常超過 6 秒。再往上加的邊際效益低
        /// ——真的沒網路時只是讓玩家等更久才看到提示，而呼叫端已有失敗後重試的處理
        /// （見 EntrySceneController.Retry）。
        /// </summary>
        private const int DownloadRetryCount = 5;

        /// <summary>
        /// 啟用斷點續傳的檔案大小門檻。比這更小的檔，重下比維護續傳狀態便宜。
        ///
        /// 取 256 KB 而不是 1 MB：目前 AOT 歸檔約 1.05 MB（1,100,760 bytes），只比 1 MB 多約 5%，
        /// 貼著門檻；AOT 清單或壓縮率稍有變動就可能掉到門檻下，續傳會悄悄失效。
        /// project 歸檔目前約 20 KB，遠小於門檻，不走續傳也無妨。
        /// </summary>
        private const long ResumeDownloadMinimumSize = 256 * 1024;

        private readonly string _remoteRoot;
        private ResourcePackage _package;
        private ResourceDownloaderOperation _downloader;

        /// <summary>本次實際生效的內部版號（{bundleVersion}.{n}；Editor 模擬模式為 "Simulate"），供記錄與除錯。</summary>
        public string InternalVersion { get; private set; } = string.Empty;

        /// <summary>
        /// 是否走 YooAsset 的編輯器模擬模式。Editor 預設是（直接讀 Assets/DllBytes 底下的原始
        /// .dll.bytes，不必先打包、不經解密器）；Player 一律走 HostPlayMode。
        ///
        /// 想在 Editor 裡測遠端流程（下載、斷點續傳、解密）時，加 scripting define
        /// <c>CODEPACKAGE_EDITOR_HOST_PLAY</c>，Editor 就改走與 Player 相同的 HostPlayMode
        /// （沙盒快取在 Library/yoo 底下）。此時 EntrySceneController 仍不會 Assembly.Load。
        /// </summary>
        public static bool IsEditorSimulate
        {
            get
            {
#if UNITY_EDITOR && !CODEPACKAGE_EDITOR_HOST_PLAY
                return true;
#else
                return false;
#endif
            }
        }

        /// <param name="remoteRoot">
        /// CodePackage 產物所在的遠端根目錄，例如
        /// https://storage.googleapis.com/peter_hybridclr_test/blender/Android/1.0/CodePackage
        /// （由 EntrySceneController 組出）。Editor 模擬模式不會用到，但仍要求非空，
        /// 讓兩種模式的呼叫端寫法一致。
        /// </param>
        public CodePackageLoader(string remoteRoot)
        {
            if (string.IsNullOrEmpty(remoteRoot))
                throw new ArgumentException("remoteRoot 不可為空", nameof(remoteRoot));

            _remoteRoot = remoteRoot.TrimEnd('/');
        }

        /// <summary>
        /// 初始化資源包、讀指針取得內部版號、載入 manifest。
        /// 完成後即可查詢下載量與載入 dll。
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
            Debug.Log($"[CodePackageLoader] 指針指向的內部版號: {InternalVersion}");

            var manifestOp = _package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(InternalVersion, ManifestTimeoutSeconds));
            await WaitAndThrowOnFailure(manifestOp, "LoadPackageManifestAsync");

            // 模擬模式沒有沙盒快取，不必清。
            if (!IsEditorSimulate)
                await ClearStaleCacheAsync();
        }

        /// <summary>
        /// 同名 package 還在（上一輪 <see cref="DestroyAsync"/> 失敗、或有人沒呼叫就重建 loader）而且
        /// 不是未初始化狀態時，先銷毀再移除；否則 InitializePackageAsync 會丟「is already initialized」，
        /// 重試永遠過不去。銷毀再失敗就讓後面的初始化照常丟例外，交給呼叫端的重試流程。
        /// </summary>
        private static async UniTask DestroyLeftoverPackageAsync()
        {
            var leftover = YooAssets.GetPackage(PackageName);
            if (leftover.InitializeStatus == EOperationStatus.None)
                return;

            Debug.LogWarning($"[CodePackageLoader] 發現殘留的 {PackageName}（{leftover.InitializeStatus}），先銷毀");
            var op = leftover.DestroyPackageAsync();
            await UniTask.WaitUntil(() => op.IsDone);
            if (op.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[CodePackageLoader] 銷毀殘留 package 失敗: {op.Error}");
                return;
            }
            YooAssets.RemovePackage(PackageName);
        }

        private InitializePackageOptions CreateInitializeOptions()
        {
#if UNITY_EDITOR && !CODEPACKAGE_EDITOR_HOST_PLAY
            // 編輯器模擬：用 VirtualArchiveBundle 模擬歸檔資源包，直接讀原始 .dll.bytes（明文），
            // 不經解密器，LoadAssetAsync<RawFileObject> 一樣可用（3.0.6：VirtualArchiveBundle
            // .CreateRawFileObject 以 File.ReadAllBytes(assetPath) 取值）。
            // Build 內部用反射呼叫 YooAsset.Editor 的 BundleSimulateBuilder.SimulateBuild，
            // 需要收集器設定裡有 CodePackage 這個 package（由 editor-build 的 CodePackageCollectorSetup 產生）。
            // 建置失敗（收集器設定缺、收集不到任何資源＝TaskGetBuildMap 的 PackAssetListIsEmpty 等）時，
            // 因為是反射呼叫，丟出的是 TargetInvocationException（內層 InvalidOperationException），
            // 實際原因在 Console 的 LogError；EntrySceneController 會把內層訊息顯示出來。
            Debug.Log($"[CodePackageLoader] Editor 模擬模式，不使用遠端根目錄 {_remoteRoot}");
            var buildResult = EditorSimulateBuildInvoker.Build(PackageName, (int)EBundleType.VirtualArchiveBundle);
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory),
            };
#else
            // 聯機模式：沒有內建包（dll 一律走遠端下載），只掛沙盒快取檔案系統。
            var cacheParams = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(
                new CodePackageRemoteService(_remoteRoot));
            // 歸檔資源包（ArchiveBundle）只接受記憶體式解密器（見 CodePackageDecryption 的註解）。
            // 注意參數鍵是 ArchiveBundleDecryptor，掛到 RawBundleDecryptor 上不會生效也不會報錯
            // ——載入時才會失敗（3.0.6：LoadLocalArchiveBundleOperation 第 60-65 行只讀
            // ArchiveBundleDecryptor，null 時 SetError "archive bundle decryptor is null"）。
            cacheParams.AddParameter(EFileSystemParameter.ArchiveBundleDecryptor, new CodePackageDecryption());

            // 斷點續傳。⚠️ 必須明確開啟——YooAsset 3.0.6 的預設是
            // SandboxFileSystem.ResumeDownloadMinimumSize = long.MaxValue（SandboxFileSystem.cs 第 109 行），
            // 於是 DownloadAndCacheFileOperation 第 48 行的 `Bundle.FileSize >= ResumeDownloadMinimumSize`
            // 恆為 false，一律走 CreateNormalRequest()（從 0 下載，且開頭就 DeleteTempFile，第 192 行）
            // ——中斷時已下載的位元組會全部丟棄。
            //
            // 開啟後失敗時暫存檔會保留（同檔第 101-110 行，續傳模式只有 HTTP 416 才刪），
            // 下次由 CreateResumeRequest() 讀 fileInfo.Length 當起點續傳（第 159-171 行）。
            //
            // ⚠️ 前提是遠端要支援 HTTP Range。換 CDN 供應商或改自架來源時要重新確認。
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
        /// 清掉「不被當前 manifest 引用」的 bundle 與「非當前版本」的 manifest。
        ///
        /// YooAsset 不會自動做這件事，不呼叫就會每次代碼更新都在裝置上多留一份舊歸檔。
        ///
        /// ⚠️ **插入位置是安全性的唯一保證，不要搬動**：
        /// - 必須在 <c>LoadPackageManifestAsync</c> 之後——判斷「是否被引用」拿的是當前 active manifest。
        /// - 必須在 <c>LoadDllsAsync</c> / 建立下載器之前——清理直接刪檔，**沒有「已載入 / 下載中」保護**。
        ///   此刻本 session 零載入、零下載器。
        ///
        /// ⚠️ 失敗一律吞掉不外拋：清快取是最佳化，擋開機是災難。
        /// </summary>
        private async UniTask ClearStaleCacheAsync()
        {
            await ClearCacheAsync(ClearCacheMethods.ClearUnusedBundleFiles);
            await ClearCacheAsync(ClearCacheMethods.ClearUnusedManifestFiles);
        }

        /// <summary>
        /// 執行一種清理方式。失敗只記 LogWarning。
        /// ⚠️ 刻意不用 <c>WaitAndThrowOnFailure</c>——那支會 throw。
        /// </summary>
        private async UniTask ClearCacheAsync(string clearMethod)
        {
            try
            {
                var op = _package.ClearCacheAsync(new ClearCacheOptions(clearMethod));
                await op.ToUniTask();
                if (op.Status != EOperationStatus.Succeeded)
                {
                    Debug.LogWarning($"[CodePackageLoader] {clearMethod} 失敗，略過清理: {op.Error}");
                    return;
                }
                Debug.Log($"[CodePackageLoader] {clearMethod} 完成");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CodePackageLoader] {clearMethod} 發生例外，略過清理: {ex}");
            }
        }

        /// <summary>
        /// 建立下載器並回傳這次需要下載的總位元組數（已在本地快取的不計入）。
        /// 回傳 0 代表全部命中快取，不需要下載（Editor 模擬模式恆為 0）。
        /// </summary>
        public long CreateDownloaderAndGetSize()
        {
            EnsureInitialized();

            _downloader = _package.CreateResourceDownloader(
                new ResourceDownloaderOptions(DownloadMaxConcurrency, DownloadRetryCount));

            Debug.Log($"[CodePackageLoader] 待下載: {_downloader.TotalDownloadCount} 檔 / " +
                      $"{_downloader.TotalDownloadBytes} bytes");
            return _downloader.TotalDownloadBytes;
        }

        /// <summary>
        /// 執行下載。斷點續傳與失敗重試由 YooAsset 內建，這裡只負責把進度轉給呼叫端。
        /// 必須先呼叫 <see cref="CreateDownloaderAndGetSize"/>。
        /// </summary>
        /// <param name="onProgress">0~1 的整體進度</param>
        /// <param name="onBytesChanged">(已下載位元組, 總位元組)</param>
        public async UniTask DownloadAsync(Action<float> onProgress, Action<long, long> onBytesChanged)
        {
            // ⚠️ 一定要先把 _downloader 存進區域變數再全程只用這個區域變數，不要在方法
            // 中途再讀寫 _downloader 欄位。這是共用欄位——若同一個實例上有兩個呼叫重疊
            // （例如一次呼叫還沒 await 完，CreateDownloaderAndGetSize() 又被呼叫一次），
            // 欄位會被換成另一個 downloader，讓這裡的訂閱/解除訂閱作用到錯的物件上。
            var downloader = _downloader;
            if (downloader == null)
                throw new InvalidOperationException("[CodePackageLoader] 請先呼叫 CreateDownloaderAndGetSize()");

            void OnProgressChanged(DownloadProgressChangedEventArgs args)
            {
                onProgress?.Invoke(args.Progress);
                onBytesChanged?.Invoke(args.CurrentDownloadBytes, args.TotalDownloadBytes);
            }

            downloader.DownloadProgressChanged += OnProgressChanged;
            try
            {
                // ⚠️ 「沒有檔案要下載」不能用提前 return 處理：那會跳過 finally，讓 _downloader
                // 欄位繼續指著一個已經用完的 downloader，掩蓋「忘了先呼叫 CreateDownloaderAndGetSize」
                // 的錯誤。訂閱／解除訂閱在零檔案時是無害的空操作。
                if (downloader.TotalDownloadCount > 0)
                {
                    downloader.StartDownload();
                    await WaitAndThrowOnFailure(downloader, "CodePackage 下載");
                }
            }
            finally
            {
                downloader.DownloadProgressChanged -= OnProgressChanged;
                // 只清掉欄位仍指向「這次自己取用的」downloader 時才清，避免清掉另一個
                // 並發呼叫剛建立、還沒開始下載的 downloader。
                if (ReferenceEquals(_downloader, downloader))
                    _downloader = null;
            }
        }

        /// <summary>
        /// 載入指定 tag（aot / project）底下的全部 dll，回傳名稱與解密後的 bytes。
        /// 每顆 dll 取完 bytes 後立即釋放 handle，不讓資料隨 handle 常駐記憶體。
        /// </summary>
        /// <param name="allowEmpty">
        /// tag 底下沒有任何資源時：false 丟例外（Player 的正式流程，代表產物不完整）；
        /// true 只記警告並回傳空清單（Editor 模擬模式下 DllBytes 子資料夾還沒產生 dll 的情況）。
        /// </param>
        public async UniTask<IList<DllAssetData>> LoadDllsAsync(string tag, bool allowEmpty = false)
        {
            EnsureInitialized();

            var assetInfos = _package.GetAssetInfos(tag);
            if (assetInfos == null || assetInfos.Length == 0)
            {
                if (allowEmpty)
                {
                    Debug.LogWarning($"[CodePackageLoader] tag '{tag}' 底下沒有任何資源，略過。");
                    return Array.Empty<DllAssetData>();
                }
                throw new Exception($"[CodePackageLoader] tag '{tag}' 底下沒有任何資源，CodePackage 產物可能不完整。");
            }

            var result = new List<DllAssetData>(assetInfos.Length);
            foreach (var assetInfo in assetInfos)
            {
                // 泛型多載只吃定位地址字串（沒有吃 AssetInfo 的泛型版本）；
                // Address 就是收集器 AddressByFileName 產生的 "Assembly-CSharp.dll" 這種值。
                var handle = _package.LoadAssetAsync<RawFileObject>(assetInfo.Address);
                try
                {
                    await WaitAndThrowOnFailure(handle, $"載入 {assetInfo.Address}");

                    var rawFile = handle.GetAssetObject<RawFileObject>();
                    if (rawFile == null)
                        throw new Exception($"[CodePackageLoader] {assetInfo.Address} 取不到 RawFileObject");

                    // GetBytes() 回傳的是內部陣列的副本（Buffer.BlockCopy），handle 釋放後仍可安全使用
                    var bytes = rawFile.GetBytes();
                    if (bytes == null || bytes.Length == 0)
                        throw new Exception($"[CodePackageLoader] {assetInfo.Address} 內容為空");

                    result.Add(new DllAssetData(assetInfo.Address, bytes));
                }
                finally
                {
                    handle.Release();
                }
            }

            Debug.Log($"[CodePackageLoader] tag '{tag}' 載入完成，共 {result.Count} 顆 dll");
            return result;
        }

        /// <summary>
        /// 卸載 CodePackage 佔用的記憶體。
        /// **dll 全部 <c>Assembly.Load</c> / <c>LoadMetadataForAOTAssembly</c> 完成之後呼叫一次即可。**
        ///
        /// 為什麼一定要顯式呼叫：YooAsset 的 <c>handle.Release()</c> 只把 provider 的 RefCount
        /// 減一，不做任何 destroy（前提是 InitializePackageOptions.AutoUnloadBundleWhenUnused 維持
        /// 預設 false；改成 true 的話 RefCount 歸 0 就會卸載，<see cref="LoadDllsAsync"/> 逐顆釋放
        /// handle 會讓同一個歸檔反覆解密解壓）。<c>ResourceManager._bundleLoaderDict</c> 要等到有人呼叫
        /// <c>UnloadUnusedAssetsAsync()</c> 或 <c>TryUnloadUnusedAsset()</c> 才會移除 loader，
        /// 在那之前歸檔會一直常駐，每包握著兩份資料（3.0.6：ArchiveBundle.cs 第 59、61 行）：
        ///   ArchiveBundle._memoryData     解密解壓後的整份歸檔明文
        ///   ArchiveBundle._cachedObjects  每顆 dll 各一份 RawFileObject 副本
        /// 而 dll 載完之後這兩份都再也用不到。
        ///
        /// 卸載時兩份的釋放方式不同（3.0.6：ArchiveBundle.Unload，第 120-130 行）：
        /// _cachedObjects 會立刻 Release + Destroy；_memoryData 是 readonly 欄位，Unload 不會清它，
        /// 要等 loader 從 _bundleLoaderDict 移除、ArchiveBundle 沒有任何引用之後由 GC 回收。
        ///
        /// 順帶一提，loader 以 BundleName 為鍵重用（ResourceManager.GetOrCreateBundleLoader），
        /// 所以 <see cref="LoadDllsAsync"/> 逐顆 dll 取值的寫法**不會**讓同一個歸檔反覆解密解壓。
        ///
        /// 不影響已載入的 dll：呼叫端持有的 <see cref="DllAssetData.Bytes"/> 是
        /// <c>RawFileObject.GetBytes()</c> 回傳的**獨立副本**，卸載只影響 YooAsset 自己那兩份。
        ///
        /// 失敗刻意不拋例外：此時 dll 已全部載入，沒有理由為了回收記憶體而中止啟動流程。
        /// </summary>
        public async UniTask UnloadAsync()
        {
            if (_package == null)
                return;

            _downloader = null;

            var op = _package.UnloadUnusedAssetsAsync();
            await UniTask.WaitUntil(() => op.IsDone);
            if (op.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[CodePackageLoader] 卸載失敗（不影響已載入的 dll）: {op.Error}");
                return;
            }

            Debug.Log("[CodePackageLoader] 已卸載 CodePackage 歸檔（逐檔副本已釋放，歸檔明文待 GC 回收）");
        }

        /// <summary>
        /// 銷毀整個 package。用於重新初始化前清空——同名 package 只要 InitializePackageAsync
        /// 成功過一次，再呼叫一次就會直接丟「is already initialized」（3.0.6：ResourcePackage
        /// .InitializePackageAsync 只有在上次初始化**失敗**時才會自動重置，見 TryResetAfterFailure；
        /// YooAssets.GetPackage 回傳的是同一個底層實例，不分是不是同一個 <see cref="CodePackageLoader"/>
        /// C# 物件）。任何會重跑初始化流程的地方（例如 EntrySceneController.Retry）在初始化成功後、
        /// 後段（版號指針/manifest/下載/dll 載入）才失敗時，都必須先呼叫這支再建立新的 loader，
        /// 否則重試會在 InitializePackageAsync 這一行直接拋例外，不論網路有沒有恢復都一樣失敗。
        ///
        /// ⚠️ RemovePackage 不能只放在 DestroyPackageAsync 成功之後：若 Destroy 失敗就整段跳進
        /// catch，YooAsset 內部仍留著同名 package。改成無論 DestroyPackageAsync 是否成功都嘗試
        /// RemovePackage（3.0.6 的 RemovePackage 在 InitializeStatus 不是 None 時會丟例外，
        /// 這裡吞掉只記警告），盡量避免 YooAsset 端留下沒被清掉的殘留註冊。
        /// </summary>
        public async UniTask DestroyAsync()
        {
            if (_package == null)
                return;
            try
            {
                var op = _package.DestroyPackageAsync();
                await op.ToUniTask();
                // ToUniTask（走 IEnumerator 擴充）不會因 operation 失敗而拋例外，要自己看狀態。
                // 失敗時下面的 RemovePackage 會因「has not been destroyed」被吞掉，同名 package 殘留；
                // 下一輪 InitializeAsync 會在初始化前再銷毀一次（見 DestroyLeftoverPackageAsync）。
                if (op.Status != EOperationStatus.Succeeded)
                    Debug.LogError($"[CodePackageLoader] DestroyPackageAsync 失敗: {op.Error}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CodePackageLoader] DestroyPackageAsync 失敗（已忽略）: {ex.Message}");
            }
            finally
            {
                try
                {
                    YooAssets.RemovePackage(PackageName);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CodePackageLoader] RemovePackage 失敗（已忽略）: {ex.Message}");
                }

                _package = null;
                _downloader = null;
                InternalVersion = string.Empty;
            }
        }

        private void EnsureInitialized()
        {
            if (_package == null)
                throw new InvalidOperationException("[CodePackageLoader] 尚未初始化，請先呼叫 InitializeAsync()");
        }

        /// <summary>
        /// YooAsset 的 operation 不會在失敗時自己拋例外，只把錯誤放在 Status/Error，
        /// 這裡統一轉成例外，讓 EntrySceneController 的 try/catch → 重試能接住，
        /// 不必在每個呼叫點各寫一次狀態檢查。
        /// </summary>
        private static async UniTask WaitAndThrowOnFailure(AsyncOperationBase operation, string what)
        {
            await UniTask.WaitUntil(() => operation.IsDone);
            if (operation.Status != EOperationStatus.Succeeded)
                throw new Exception($"[CodePackageLoader] {what} 失敗: {operation.Error}");
        }

        private static async UniTask WaitAndThrowOnFailure(HandleBase handle, string what)
        {
            await UniTask.WaitUntil(() => handle.IsDone);
            if (handle.Status != EOperationStatus.Succeeded)
                throw new Exception($"[CodePackageLoader] {what} 失敗: {handle.Error}");
        }

        /// <summary>
        /// 遠端位址查詢：CodePackage 的所有檔案都直接掛在同一個子目錄底下，
        /// 所以組法就是「根目錄 + 檔名」。回傳單一 URL（沒有備援站台）。
        /// </summary>
        private sealed class CodePackageRemoteService : IRemoteService
        {
            private readonly string _root;

            public CodePackageRemoteService(string root)
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
