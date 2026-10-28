#if UNITY_WEBGL
using System;
using System.Collections.Generic;
using YooAsset;

namespace EntryScene
{
    /// <summary>
    /// WebGL 專用的下載重試策略：同一個網址連續失敗超過 <see cref="MaxRetryCount"/> 次就放棄。
    ///
    /// 為什麼需要：YooAsset 3.0.6 在 Web 上「載入時才下載」的幾個操作（LoadWebArchiveBundleOperation、
    /// LoadWebPlatformAssetBundleOperation 等）建構時用 <c>new DownloadRetryController(int.MaxValue, policy)</c>，
    /// 次數上限等於沒有；預設的 DefaultDownloadRetryPolicy 又把 httpCode 0（離線、CORS 被擋）、5xx、408、429
    /// 都判成可重試。結果是正式站沒開 CORS、CDN 回 5xx、玩家斷線時，載入永遠不回來，
    /// Entry 停在「載入 AOT metadata」、沒有重試按鈕；遊戲中 GameAssets 的載入也會一直掛著。
    /// 次數只能在策略這一層擋：IsRetryableError 回 false，操作就以錯誤結束，交給呼叫端的重試流程。
    ///
    /// 放棄時把該網址的計數歸零：下一次由呼叫端重新發起的載入（Entry 的「重試」、遊戲再要一次素材）
    /// 重新有 N 次機會。介面沒有「成功」回呼，所以中途失敗幾次後又成功的網址，計數會留著到下次失敗時累加；
    /// 多個操作同時抓同一個網址時也會共用計數。兩者都只會讓放棄提早、不會更晚。
    ///
    /// ⚠️ 擋不到的情況：下載成功但大小校驗失敗時，YooAsset 只看 DownloadRetryController.HasRetriesRemaining
    /// （上限 int.MaxValue），不經過這裡，伺服器若持續回錯的內容（例如 CDN 快取到舊檔）仍會一直重試。
    ///
    /// 延遲沿用 YooAsset 預設（每次 +1 秒、上限 10 秒），5 次共約 15 秒，同 CodePackageLoader.DownloadRetryCount。
    /// </summary>
    internal sealed class WebDownloadRetryPolicy : IDownloadRetryPolicy
    {
        public const int MaxRetryCount = 5;

        private readonly DefaultDownloadRetryPolicy _default = new DefaultDownloadRetryPolicy();
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.Ordinal);

        public bool IsRetryableError(string url, long httpCode, string httpError)
        {
            var key = url ?? string.Empty;
            if (!_default.IsRetryableError(url, httpCode, httpError))
            {
                _failures.Remove(key);
                return false;
            }

            _failures.TryGetValue(key, out var count);
            if (count >= MaxRetryCount)
            {
                _failures.Remove(key);
                return false;
            }
            _failures[key] = count + 1;
            return true;
        }

        public float CalculateRetryDelay(int retryCount, float previousDelay)
        {
            return _default.CalculateRetryDelay(retryCount, previousDelay);
        }
    }
}
#endif
