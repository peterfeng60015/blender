using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace EntryScene
{
    /// <summary>
    /// 遊戲模組代碼載入美術素材的唯一入口（AssetPackage）。遊戲模組代碼**禁止** <c>using YooAsset</c>，一律透過這裡。
    ///
    /// EntrySceneController 會在呼叫 <c>AppBootstrap.Run()</c> 之前完成 AssetPackage 的初始化與下載，
    /// 所以 Run() 裡 <see cref="IsReady"/> 已是 true，可以直接載入。
    ///
    /// location 是收集器的定位地址＝檔名不含副檔名，例如 <c>Miner_Run</c>、<c>Miner_sprites</c>。
    ///
    /// 錯誤一律以 faulted UniTask 回報（await 時丟出），包括「尚未就緒」，不會在呼叫點同步丟出。
    /// 失敗（例如按需下載時斷網）之後同一個 location 可以再呼叫重試。
    ///
    /// [Preserve]：AOT 端沒有任何呼叫者，只有遊戲模組代碼會用；調高 IL2CPP 裁剪等級時不能被裁掉。
    /// </summary>
    [Preserve]
    public static class GameAssets
    {
        private static AssetPackageLoader _loader;

        /// <summary>AssetPackage 初始化（含下載）完成才為 true。</summary>
        public static bool IsReady
        {
            [Preserve]
            get => _loader != null;
        }

        /// <summary>
        /// 載入一張 sprite sheet 切好的全部 sprite，已依名稱尾碼編號排序
        /// （<c>Miner_Run_0, Miner_Run_1, … Miner_Run_10</c>，數字比較而非字串比較）。
        /// 回傳的陣列是呼叫端自己的副本；sprite 本身常駐（AssetPackage 不卸載）。
        /// </summary>
        [Preserve]
        public static async UniTask<Sprite[]> LoadSpritesAsync(string location)
        {
            return await GetLoader().LoadSpritesAsync(location);
        }

        /// <summary>載入 TextAsset 的文字內容（例如 <c>Miner_sprites</c> 的 json）。</summary>
        [Preserve]
        public static async UniTask<string> LoadTextAsync(string location)
        {
            return await GetLoader().LoadTextAsync(location);
        }

        /// <summary>由 EntrySceneController 在 AssetPackage 初始化與下載完成後掛上；重試前以 null 卸下。</summary>
        internal static void Attach(AssetPackageLoader loader)
        {
            _loader = loader;
        }

        /// <summary>
        /// Editor 關掉 Domain Reload（Enter Play Mode Options）時靜態欄位會留到下一次 Play，
        /// 指著上一輪已銷毀的 package，讓 EntrySceneController 誤判為已就緒而跳過初始化。每次進 Play 先清掉。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _loader = null;
        }

        private static AssetPackageLoader GetLoader()
        {
            if (_loader == null)
                throw new InvalidOperationException(
                    "[GameAssets] AssetPackage 尚未就緒。Editor 下請從 Entry 場景按 Play；" +
                    "Player 下 Entry 會在 AppBootstrap.Run() 之前完成初始化。");
            return _loader;
        }
    }
}
