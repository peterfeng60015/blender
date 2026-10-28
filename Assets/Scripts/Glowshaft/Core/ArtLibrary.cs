using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 美術素材讀取的唯一集中點。所有遊戲程式（Core、World、UI）都只透過 <see cref="Get"/> 取 sprite，
    /// 不直接碰 <c>EntryScene.GameAssets</c>。
    ///
    /// <see cref="LoadAllAsync"/> 依素材清單逐一從 AssetPackage 讀 sheet 與 json；某張讀不到（美術還沒交、
    /// 或沒從 Entry 場景按 Play）時改用程式產生的純色佔位圖並記警告，所以素材到了不用改程式就會自動換上。
    /// </summary>
    public static class ArtLibrary
    {
        private sealed class CatalogEntry
        {
            public readonly string Name;
            public readonly int Dirs;
            public readonly string[] Actions;

            public CatalogEntry(string name, int dirs, params string[] actions)
            {
                Name = name;
                Dirs = dirs;
                Actions = actions;
            }
        }

        /// <summary>素材清單（名稱、方向數、動作），與共同規則的素材清單一致。</summary>
        private static readonly CatalogEntry[] Catalog =
        {
            new CatalogEntry("Miner", 8, "Idle", "Run"),
            new CatalogEntry("SlimeGreen", 8, "Move"),
            new CatalogEntry("SlimePurple", 8, "Move"),
            new CatalogEntry("SlimeOrange", 8, "Move"),
            new CatalogEntry("Bullet", 1, "Idle"),
            new CatalogEntry("Battery", 1, "Idle"),
            new CatalogEntry("Floor", 1, "Idle"),
            new CatalogEntry("Rock", 1, "Idle"),
            new CatalogEntry("Crystal", 1, "Idle"),
        };

        private static readonly Dictionary<string, SpriteSet> Sets = new Dictionary<string, SpriteSet>();

        /// <summary><see cref="LoadAllAsync"/> 已完成。</summary>
        public static bool IsLoaded { get; private set; }

        /// <summary>有幾張 sheet 是佔位圖（0＝全部是真素材）。</summary>
        public static int PlaceholderCount { get; private set; }

        /// <summary>
        /// 取一張 sheet，location＝<c>{Name}_{Action}</c>，例如 <c>Miner_Run</c>、<c>Floor_Idle</c>、<c>Rock_Idle</c>。
        /// 必須在 <see cref="LoadAllAsync"/> 完成後呼叫；清單外的 location 回傳一個洋紅色佔位圖。
        /// </summary>
        public static SpriteSet Get(string location)
        {
            if (Sets.TryGetValue(location, out var set))
                return set;
            if (!IsLoaded)
            {
                // 讀取完成前呼叫（例如 world／ui 單獨測試）：給佔位圖但不快取，之後讀到真素材才不會被蓋住
                Debug.LogWarning($"[ArtLibrary] 素材尚未讀取完成就要求 {location}，暫用佔位圖");
                return Placeholders.Create(location, DirectionsOf(location));
            }
            Debug.LogWarning($"[ArtLibrary] 未知的素材 {location}，改用佔位圖");
            set = Placeholders.Create(location, 1);
            Sets[location] = set;
            return set;
        }

        /// <summary>
        /// 清掉快取，下次 <see cref="LoadAllAsync"/> 重新讀。專案開了 Enter Play Mode Options（不重載 domain），
        /// static 會跨 Play 保留，所以每次開機（<see cref="GlowshaftGame.Boot"/>）都先清一次，
        /// 讓兩次 Play 之間新匯入的素材能換上、不會沿用上一次的佔位圖。
        /// </summary>
        public static void ResetCache()
        {
            _generation++;
            Sets.Clear();
            IsLoaded = false;
            PlaceholderCount = 0;
        }

        /// <summary>
        /// 每次 <see cref="ResetCache"/> 加一。讀取中途離開 Play（domain 不重載）時，舊一輪的 await 接續
        /// 可能在下一輪才跑完；它發現世代號變了就放棄，不會把舊結果寫進新一輪的 <see cref="Sets"/>。
        /// </summary>
        private static int _generation;

        /// <summary>讀入全部素材。重複呼叫只會讀一次。</summary>
        public static async UniTask LoadAllAsync()
        {
            if (IsLoaded)
                return;
            var generation = _generation;
            // 先收在區域變數，全部讀完且仍是同一世代才一次寫回 static
            var loaded = new Dictionary<string, SpriteSet>();
            var placeholders = 0;
            var ready = IsGameAssetsReady();
            if (!ready)
                Debug.LogWarning("[ArtLibrary] EntryScene.GameAssets 尚未就緒（沒從 Entry 場景按 Play？），全部改用佔位圖");

            foreach (var entry in Catalog)
            {
                var name = entry.Name;
                var dirs = entry.Dirs;
                var fps = 0f;
                var jsonDirs = dirs;
                if (ready)
                {
                    try
                    {
                        var json = await EntryScene.GameAssets.LoadTextAsync($"{name}_sprites");
                        if (generation != _generation)
                            return;
                        ParseManifest(json, ref fps, ref jsonDirs);
                    }
                    catch (Exception ex)
                    {
                        if (generation != _generation)
                            return;
                        Debug.LogWarning($"[ArtLibrary] 讀不到 {name}_sprites：{ex.Message}");
                    }
                }

                foreach (var action in entry.Actions)
                {
                    var location = $"{name}_{action}";
                    SpriteSet set = null;
                    if (ready)
                    {
                        try
                        {
                            var sprites = await EntryScene.GameAssets.LoadSpritesAsync(location);
                            if (generation != _generation)
                                return;
                            if (sprites != null && sprites.Length > 0)
                            {
                                if (sprites.Length % jsonDirs != 0)
                                    Debug.LogWarning($"[ArtLibrary] {location} 有 {sprites.Length} 張，無法被方向數 {jsonDirs} 整除");
                                set = new SpriteSet(location, sprites, jsonDirs, fps, false);
                            }
                            else
                            {
                                Debug.LogWarning($"[ArtLibrary] {location} 沒有任何 sprite，改用佔位圖");
                            }
                        }
                        catch (Exception ex)
                        {
                            if (generation != _generation)
                                return;
                            Debug.LogWarning($"[ArtLibrary] 讀不到 {location}，改用佔位圖：{ex.Message}");
                        }
                    }

                    if (set == null)
                    {
                        set = Placeholders.Create(location, dirs);
                        placeholders++;
                    }
                    loaded[location] = set;
                }
            }

            if (generation != _generation)
                return;
            foreach (var pair in loaded)
                Sets[pair.Key] = pair.Value;
            PlaceholderCount = placeholders;
            IsLoaded = true;
            Debug.Log($"[ArtLibrary] 素材讀取完成，{Sets.Count} 張 sheet，其中佔位圖 {PlaceholderCount} 張");
        }

        private static int DirectionsOf(string location)
        {
            foreach (var entry in Catalog)
            {
                if (location.StartsWith(entry.Name + "_", StringComparison.Ordinal))
                    return entry.Dirs;
            }
            return 1;
        }

        private static bool IsGameAssetsReady()
        {
            try
            {
                return EntryScene.GameAssets.IsReady;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ArtLibrary] 查詢 GameAssets.IsReady 失敗：{ex.Message}");
                return false;
            }
        }

        private static readonly Regex FpsRegex = new Regex("\"fps\"\\s*:\\s*([0-9.]+)");
        private static readonly Regex DirectionsRegex = new Regex("\"directions\"\\s*:\\s*\\[([^\\]]*)\\]");

        /// <summary>只取需要的兩個欄位，不依賴 JsonUtility（模組型別的反序列化在 HybridCLR 下較少驗證）。</summary>
        private static void ParseManifest(string json, ref float fps, ref int dirs)
        {
            if (string.IsNullOrEmpty(json))
                return;
            var m = FpsRegex.Match(json);
            if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f))
                fps = f;
            var d = DirectionsRegex.Match(json);
            if (d.Success)
            {
                var count = d.Groups[1].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (count > 0)
                    dirs = count;
            }
        }
    }
}
