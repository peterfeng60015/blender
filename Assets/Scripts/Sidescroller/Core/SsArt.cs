using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 素材與資料讀取的唯一集中點（interfaces.md §4.1、§6.4）。
    /// <see cref="LoadAllAsync"/> 依素材清單從 AssetPackage 讀 sheet 與 json；讀不到時 Editor 改用 AssetDatabase 直接讀
    /// <see cref="ArtRoot"/> 底下的檔（備援），再不行就用程式產生的佔位圖（大小、顏色、pivot、幀數同正式素材），
    /// 所以素材到了不用改程式就會自動換上。Player 下沒有備援。
    /// </summary>
    public static class SsArt
    {
        public const string ArtRoot = "Assets/Art/Sidescroller";
        public const string DataRoot = ArtRoot + "/Data";

        #region 素材清單（§6.4、design.md §8.3～8.5）
        private enum PivotKind { Feet, Center }

        private sealed class Entry
        {
            public readonly string Name;
            public readonly string[] Actions;
            /// <summary>每個動作的幀數（佔位圖用；多變化素材＝變化數）。</summary>
            public readonly int[] Frames;
            public readonly Vector2 Size;
            public readonly Color Color;
            public readonly PivotKind Pivot;
            /// <summary>多變化素材：每幀的佔位色深淺不同。</summary>
            public readonly bool Variants;

            public Entry(string name, Vector2 size, Color color, PivotKind pivot, bool variants, string[] actions, int[] frames)
            {
                Name = name;
                Size = size;
                Color = color;
                Pivot = pivot;
                Variants = variants;
                Actions = actions;
                Frames = frames;
            }
        }

        private static Entry Anim(string name, float w, float h, Color color, PivotKind pivot, string[] actions, int[] frames) =>
            new Entry(name, new Vector2(w, h), color, pivot, false, actions, frames);

        private static Entry Var(string name, float w, float h, Color color, PivotKind pivot, string action, int count) =>
            new Entry(name, new Vector2(w, h), color, pivot, true, new[] { action }, new[] { count });

        private static readonly string[] HeroActions = { "Idle", "Run", "Jump", "Melee", "Shoot", "Hurt" };
        private static readonly int[] HeroFrames = { 6, 8, 6, 5, 4, 3 };

        private static readonly Entry[] Catalog =
        {
            Anim("SsHeroDay", 0.9f, 1.5f, new Color(0.85f, 0.2f, 0.17f), PivotKind.Feet, HeroActions, HeroFrames),
            Anim("SsHeroExpress", 0.9f, 1.5f, new Color(1f, 0.48f, 0.1f), PivotKind.Feet, HeroActions, HeroFrames),
            Anim("SsMentor", 1.0f, 1.2f, new Color(0.25f, 0.65f, 0.3f), PivotKind.Feet, new[] { "Idle", "Talk" }, new[] { 6, 6 }),
            Anim("SsRat", 0.7f, 1.4f, new Color(0.55f, 0.56f, 0.6f), PivotKind.Feet, new[] { "Idle", "Walk", "Attack", "Hurt" }, new[] { 4, 8, 6, 3 }),
            Anim("SsPigeon", 0.9f, 0.9f, new Color(0.55f, 0.75f, 0.95f), PivotKind.Feet, new[] { "Fly", "Attack", "Hurt" }, new[] { 6, 4, 3 }),
            Anim("SsRhino", 1.4f, 1.6f, new Color(0.15f, 0.2f, 0.45f), PivotKind.Feet, new[] { "Idle", "Walk", "Attack", "Hurt" }, new[] { 4, 8, 6, 3 }),
            Anim("SsBoss", 3.2f, 4.0f, new Color(0.54f, 0.3f, 0.7f), PivotKind.Feet, new[] { "Idle", "Charge", "Spray", "Stomp", "Hurt" }, new[] { 6, 8, 6, 8, 3 }),
            Anim("SsParcel", 0.35f, 0.35f, new Color(0.55f, 0.36f, 0.2f), PivotKind.Center, new[] { "Spin" }, new[] { 4 }),
            Anim("SsCoin", 0.35f, 0.35f, new Color(0.95f, 0.78f, 0.2f), PivotKind.Center, new[] { "Spin" }, new[] { 4 }),
            Anim("SsBomb", 0.4f, 0.4f, Color.white, PivotKind.Center, new[] { "Fall" }, new[] { 2 }),
            Anim("SsShock", 0.8f, 0.8f, new Color(1f, 0.9f, 0.2f), PivotKind.Center, new[] { "Move" }, new[] { 4 }),
            Anim("SsFx", 1.0f, 1.0f, Color.white, PivotKind.Center, new[] { "Hit", "Poof", "Dust", "Clank" }, new[] { 4, 6, 4, 3 }),
            Var("SsTiles", 1f, 1f, new Color(0.6f, 0.6f, 0.6f), PivotKind.Center, "Idle", 7),
            Var("SsProps", 1f, 3f, new Color(0.15f, 0.4f, 0.2f), PivotKind.Feet, "Idle", 6),
            Var("SsLandmark", 6f, 6f, new Color(0.5f, 0.12f, 0.15f), PivotKind.Feet, "Idle", 3),
            Var("SsBgFar", 16f, 16f, new Color(0.36f, 0.23f, 0.48f), PivotKind.Center, "Idle", 1),
            Var("SsBgMid", 16f, 16f, new Color(0.15f, 0.2f, 0.42f), PivotKind.Center, "Idle", 2),
            Var("SsUi", 1f, 1f, new Color(1f, 0.55f, 0.75f), PivotKind.Center, "Idle", 8),
            Var("SsPortraitHero", 4f, 4f, new Color(0.85f, 0.2f, 0.17f), PivotKind.Center, "Face", 4),
            Var("SsPortraitBoss", 4f, 4f, new Color(0.54f, 0.3f, 0.7f), PivotKind.Center, "Face", 4),
            Var("SsPortraitMentor", 4f, 4f, new Color(0.25f, 0.65f, 0.3f), PivotKind.Center, "Face", 4),
            Var("SsPortraitRat", 4f, 4f, new Color(0.55f, 0.56f, 0.6f), PivotKind.Center, "Face", 4),
        };
        #endregion

        private static readonly Dictionary<string, SpriteSet> Cache = new Dictionary<string, SpriteSet>();
        /// <summary>程式產生的佔位貼圖與 sprite，ResetCache 時銷毀。</summary>
        private static readonly List<UnityEngine.Object> PlaceholderObjects = new List<UnityEngine.Object>();

        /// <summary>
        /// 每次 <see cref="ResetCache"/> 加一。讀取中途 Shutdown（或離開 Play）時，舊一輪的 await 接續可能在下一輪才跑完；
        /// 它發現世代號變了就放棄，不會把舊結果寫進新一輪的快取。
        /// </summary>
        private static int _generation;
        private static bool _warnedFallback;

        public static bool IsLoaded { get; private set; }
        public static int PlaceholderCount { get; private set; }

        #region 公開 API
        /// <summary>依 §6.4 的清單讀全部 sprite；讀不到的用佔位圖。重複呼叫只讀一次。</summary>
        public static async UniTask LoadAllAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (IsLoaded)
                return;
            var generation = _generation;
            var loaded = new Dictionary<string, SpriteSet>();
            var placeholders = 0;
            var ready = IsGameAssetsReady();
            if (!ready)
                Debug.LogWarning("[Sidescroller.SsArt] EntryScene.GameAssets 尚未就緒（沒從 Entry 場景按 Play？）");

            foreach (var entry in Catalog)
            {
                var fps = 0f;
                var json = await TryLoadTextAsync(entry.Name + "_sprites", ready, generation, ct);
                if (generation != _generation)
                    throw new OperationCanceledException("[Sidescroller.SsArt] 讀取中途被 ResetCache");
                if (json != null)
                    fps = ParseFps(json);

                for (var i = 0; i < entry.Actions.Length; i++)
                {
                    var location = $"{entry.Name}_{entry.Actions[i]}";
                    var sprites = await TryLoadSpritesAsync(location, ready, generation, ct);
                    if (generation != _generation)
                        throw new OperationCanceledException("[Sidescroller.SsArt] 讀取中途被 ResetCache");
                    SpriteSet set;
                    if (sprites != null && sprites.Length > 0)
                    {
                        if (sprites.Length != entry.Frames[i])
                            Debug.LogWarning($"[Sidescroller.SsArt] {location} 有 {sprites.Length} 幀，清單是 {entry.Frames[i]} 幀（以實際為準）");
                        set = new SpriteSet(location, sprites, fps, false);
                    }
                    else
                    {
                        set = CreatePlaceholder(entry, i, location);
                        placeholders++;
                    }
                    loaded[location] = set;
                }
            }

            if (generation != _generation)
                throw new OperationCanceledException("[Sidescroller.SsArt] 讀取中途被 ResetCache");
            foreach (var pair in loaded)
                Cache[pair.Key] = pair.Value;
            PlaceholderCount = placeholders;
            IsLoaded = true;
            Debug.Log($"[Sidescroller.SsArt] 素材讀取完成，{loaded.Count} 張 sheet，其中佔位圖 {placeholders} 張");
        }

        /// <summary>"{Name}_{Action}"；清單外或未讀完：回傳佔位圖並記警告。</summary>
        public static SpriteSet Get(string location)
        {
            location = location ?? string.Empty;
            if (Cache.TryGetValue(location, out var set))
                return set;
            var entry = FindEntry(location, out var actionIndex);
            if (!IsLoaded)
            {
                // 讀完前要求：佔位圖也放進快取（同一個 location 只建一次、只警告一次，每幀要也不會洩漏），
                // LoadAllAsync 讀完時會用真素材覆蓋；已經拿到佔位圖的物件要重新 Get 才會換上
                Debug.LogWarning($"[Sidescroller.SsArt] 素材尚未讀取完成就要求 {location}，暫用佔位圖");
                set = entry != null ? CreatePlaceholder(entry, actionIndex, location) : CreateUnknown(location);
                Cache[location] = set;
                return set;
            }
            Debug.LogWarning($"[Sidescroller.SsArt] 清單外的素材 {location}，改用佔位圖");
            set = CreateUnknown(location);
            Cache[location] = set;
            return set;
        }

        /// <summary>讀不到丟例外。Editor 下 AssetPackage 讀不到時改讀 <see cref="ArtRoot"/> 底下的同名檔。</summary>
        public static async UniTask<string> LoadTextAsync(string location, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Exception packageError = null;
            if (IsGameAssetsReady())
            {
                try
                {
                    var text = await EntryScene.GameAssets.LoadTextAsync(location);
                    ct.ThrowIfCancellationRequested();
                    return text;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    packageError = ex;
                }
            }
#if UNITY_EDITOR
            var path = FindArtFile(location, ".json", ".txt");
            if (path != null)
            {
                Debug.LogWarning($"[Sidescroller.SsArt] AssetPackage 讀不到 {location}，Editor 改讀檔案 {path}" +
                                 (packageError != null ? $"（{packageError.Message}）" : "（AssetPackage 未就緒）"));
                return System.IO.File.ReadAllText(path);
            }
#endif
            throw new InvalidOperationException($"[Sidescroller.SsArt] 讀不到文字資料 {location}", packageError);
        }

        public static void ResetCache()
        {
            _generation++;
            Cache.Clear();
            IsLoaded = false;
            PlaceholderCount = 0;
            _warnedFallback = false;
            foreach (var obj in PlaceholderObjects)
            {
                if (obj != null)
                    UnityEngine.Object.Destroy(obj);
            }
            PlaceholderObjects.Clear();
        }

        /// <summary>是否在 §6.4 清單內。</summary>
        public static bool IsKnownLocation(string location) => FindEntry(location, out _) != null;
        #endregion

        #region 讀取
        private static bool IsGameAssetsReady()
        {
            try
            {
                return EntryScene.GameAssets.IsReady;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Sidescroller.SsArt] 查詢 GameAssets.IsReady 失敗：{ex.Message}");
                return false;
            }
        }

        /// <summary>sheet 說明 json；讀不到回 null（不是錯誤：fps 用預設）。</summary>
        private static async UniTask<string> TryLoadTextAsync(string location, bool ready, int generation, CancellationToken ct)
        {
#if UNITY_EDITOR
            // 美術還沒交的檔不去問 AssetPackage（YooAsset 會對不存在的 location 記 Error 洗版）
            if (FindArtFile(location, ".json") == null)
                return null;
#endif
            if (ready)
            {
                try
                {
                    var text = await EntryScene.GameAssets.LoadTextAsync(location);
                    ct.ThrowIfCancellationRequested();
                    return text;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    if (generation != _generation)
                        return null;
                }
            }
#if UNITY_EDITOR
            var path = FindArtFile(location, ".json");
            if (path != null)
            {
                WarnFallback();
                return System.IO.File.ReadAllText(path);
            }
#endif
            return null;
        }

        private static async UniTask<Sprite[]> TryLoadSpritesAsync(string location, bool ready, int generation, CancellationToken ct)
        {
            Exception packageError = null;
#if UNITY_EDITOR
            if (FindArtFile(location, ".png") == null)
                return null;
#endif
            if (ready)
            {
                try
                {
                    var sprites = await EntryScene.GameAssets.LoadSpritesAsync(location);
                    ct.ThrowIfCancellationRequested();
                    if (sprites != null && sprites.Length > 0)
                        return sprites;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (generation != _generation)
                        return null;
                    packageError = ex;
                }
            }
#if UNITY_EDITOR
            var editorSprites = LoadEditorSprites(location);
            if (editorSprites != null)
            {
                WarnFallback();
                return editorSprites;
            }
#endif
            if (packageError != null)
                Debug.LogWarning($"[Sidescroller.SsArt] 讀不到 {location}，改用佔位圖：{packageError.Message}");
            return null;
        }

        private static readonly Regex FpsRegex = new Regex("\"fps\"\\s*:\\s*([0-9.]+)");

        /// <summary>只用 regex 取 fps（不用 JsonUtility）；讀不到回 0（SpriteSet 會改用 12）。</summary>
        private static float ParseFps(string json)
        {
            var m = FpsRegex.Match(json ?? string.Empty);
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f;
            return 0f;
        }

        private static void WarnFallback()
        {
            if (_warnedFallback)
                return;
            _warnedFallback = true;
            Debug.LogWarning("[Sidescroller.SsArt] AssetPackage 讀不到部分素材，Editor 改用 AssetDatabase 直接讀 " + ArtRoot +
                             "（Player 下沒有這個備援；請確認 AssetPackage 收集器設定已更新）");
        }

#if UNITY_EDITOR
        /// <summary>在 ArtRoot 底下找檔名（不含副檔名）＝location、副檔名在 exts 內的檔（不會比到 .meta）。</summary>
        private static string FindArtFile(string location, params string[] exts)
        {
            if (string.IsNullOrEmpty(location) || !System.IO.Directory.Exists(ArtRoot))
                return null;
            foreach (var ext in exts)
            {
                foreach (var hit in System.IO.Directory.GetFiles(ArtRoot, location + ext, System.IO.SearchOption.AllDirectories))
                {
                    var path = hit.Replace('\\', '/');
                    if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && !InHiddenFolder(path))
                        return path;
                }
            }
            return null;
        }

        /// <summary>Unity 不匯入的資料夾（. 開頭或 ~ 結尾，例如 art 的 .staging/）。</summary>
        private static bool InHiddenFolder(string path)
        {
            foreach (var part in path.Substring(ArtRoot.Length).Split('/'))
            {
                if (part.Length > 0 && (part[0] == '.' || part[part.Length - 1] == '~'))
                    return true;
            }
            return false;
        }

        private static Sprite[] LoadEditorSprites(string location)
        {
            var path = FindArtFile(location, ".png");
            if (path == null)
                return null;
            var list = new List<Sprite>();
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite)
                    list.Add(sprite);
            }
            if (list.Count == 0)
                return null;
            list.Sort((a, b) => FrameNumber(a.name).CompareTo(FrameNumber(b.name)));
            return list.ToArray();
        }

        /// <summary>sprite 名稱尾碼的編號（"SsHeroDay_Run_10" → 10）；沒有編號回 0。</summary>
        private static int FrameNumber(string name)
        {
            var i = name.LastIndexOf('_');
            return i >= 0 && int.TryParse(name.Substring(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
#endif
        #endregion

        #region 佔位圖
        private static Entry FindEntry(string location, out int actionIndex)
        {
            actionIndex = -1;
            if (string.IsNullOrEmpty(location))
                return null;
            foreach (var entry in Catalog)
            {
                if (!location.StartsWith(entry.Name + "_", StringComparison.Ordinal))
                    continue;
                var action = location.Substring(entry.Name.Length + 1);
                for (var i = 0; i < entry.Actions.Length; i++)
                {
                    if (entry.Actions[i] == action)
                    {
                        actionIndex = i;
                        return entry;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 純色方塊：深色外框、面向 +X 的那一側有一條亮帶（看得出左右翻轉），
        /// 動畫幀在底部有一個依幀號移動的小記號；多變化素材每格深淺不同。
        /// </summary>
        private static SpriteSet CreatePlaceholder(Entry entry, int actionIndex, string location)
        {
            var count = Mathf.Max(1, entry.Frames[actionIndex]);
            var ppu = SsIds.PixelsPerUnit;
            var w = Mathf.Max(2, Mathf.RoundToInt(entry.Size.x * ppu));
            var h = Mathf.Max(2, Mathf.RoundToInt(entry.Size.y * ppu));
            var pivot = entry.Pivot == PivotKind.Feet ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
            var frames = new Sprite[count];
            for (var f = 0; f < count; f++)
            {
                var color = entry.Color;
                if (entry.Variants && count > 1)
                    color = Color.Lerp(color * 0.55f, Color.Lerp(color, Color.white, 0.35f), f / (float)(count - 1));
                color.a = 1f;
                var tex = MakeTexture(w, h, color, entry.Variants ? -1 : f, count);
                tex.name = $"{location}_{f}";
                frames[f] = MakeSprite(tex, pivot);
            }
            return new SpriteSet(location, frames, 12f, true);
        }

        private static SpriteSet CreateUnknown(string location)
        {
            var tex = MakeTexture(SsIds.PixelsPerUnit, SsIds.PixelsPerUnit, new Color(1f, 0f, 1f), -1, 1);
            tex.name = "SsPlaceholder";
            return new SpriteSet(location, new[] { MakeSprite(tex, new Vector2(0.5f, 0f)) }, 12f, true);
        }

        private static Sprite MakeSprite(Texture2D tex, Vector2 pivot)
        {
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, SsIds.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = tex.name;
            PlaceholderObjects.Add(sprite);
            return sprite;
        }

        private static Texture2D MakeTexture(int w, int h, Color color, int frame, int frameCount)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var body = (Color32)color;
            var dark = color * 0.45f;
            dark.a = 1f;
            var edge = (Color32)dark;
            var front = (Color32)Color.Lerp(color, Color.white, 0.5f);
            var border = Mathf.Clamp(Mathf.Min(w, h) / 16, 1, 4);
            var stripe = Mathf.Max(1, w / 6);
            var markW = frame >= 0 ? Mathf.Max(1, w / Mathf.Max(1, frameCount)) : 0;
            var markX = frame >= 0 ? frame * w / Mathf.Max(1, frameCount) : 0;
            var markH = Mathf.Max(1, h / 10);
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    Color32 c;
                    if (x < border || y < border || x >= w - border || y >= h - border)
                        c = edge;
                    else if (frame >= 0 && y < border + markH && x >= markX && x < markX + markW)
                        c = edge;
                    else if (x >= w - border - stripe)
                        c = front;
                    else
                        c = body;
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            PlaceholderObjects.Add(tex);
            return tex;
        }
        #endregion
    }
}
