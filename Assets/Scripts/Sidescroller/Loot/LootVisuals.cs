using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 道具圖示（pickup 實作）：地上、飛行、背包格子、說明框共用同一套。目前沒有美術素材，由程式產生（依種類的形狀＋稀有度顏色）。
    /// 快取的 Sprite／Texture 由 <see cref="SidescrollerGame.ResetStatics"/> 呼叫 <see cref="ResetCache"/> 清掉。
    /// </summary>
    public static class LootVisuals
    {
        /// <summary>每格的像素數（零件 32×32、武器 64×96、殼甲 64×64）。</summary>
        public const int CellPixels = 32;
        /// <summary>地上道具每格的世界尺寸（武器約 0.64×0.96）。</summary>
        public const float CellWorld = 0.32f;

        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color Outline = new Color(0.08f, 0.06f, 0.12f, 1f);
        private static readonly Color Metal = new Color(0.72f, 0.74f, 0.8f);
        private static readonly Color MetalDark = new Color(0.45f, 0.47f, 0.55f);
        private static readonly Color Wood = new Color(0.62f, 0.4f, 0.22f);
        private static readonly Color Leather = new Color(0.55f, 0.36f, 0.24f);
        private static readonly Color Paper = new Color(0.92f, 0.9f, 0.82f);
        private static readonly Color ShellGreen = new Color(0.42f, 0.6f, 0.34f);

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly List<Object> Owned = new List<Object>();

        /// <summary>道具圖示（長寬比照佔格：零件 1×1、武器 2×3、殼甲 2×2）。item 為 null 回 null。</summary>
        public static Sprite Icon(ItemData item)
        {
            if (item == null)
                return null;
            var shape = ShapeOf(item);
            var key = $"icon/{(int)item.Kind}/{shape}/{(int)item.Rarity}";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var canvas = new Raster(item.Width * CellPixels, item.Height * CellPixels);
            DrawItem(canvas, item.Kind, shape, item.Rarity);
            canvas.AddOutline(Outline);
            if (item.Rarity != Rarity.Common)
                canvas.AddOutline(ItemData.RarityColor(item.Rarity));
            return Make(key, canvas, new Vector2(0.5f, 0.5f), CellPixels / CellWorld, FilterMode.Point);
        }

        public static void ResetCache()
        {
            foreach (var o in Owned)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            Owned.Clear();
            Cache.Clear();
        }

        #region 其他共用圖（pickup 內部用）
        /// <summary>右上背包圖示（郵差背包，64×64）。</summary>
        internal static Sprite BagSprite()
        {
            const string key = "bag";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var c = new Raster(64, 64);
            c.Ring(32, 44, 13, 4, Shade(Leather, 0.7f), 0f);
            c.RoundRect(8, 6, 56, 46, 8, Leather);
            c.RoundRect(8, 28, 56, 48, 6, Shade(Leather, 0.8f));
            c.Rect(28, 24, 36, 34, SsUiKit.Gold);
            c.Rect(30, 26, 34, 32, Shade(SsUiKit.Gold, 0.7f));
            c.Rect(12, 12, 52, 14, Shade(Leather, 0.75f));
            c.AddOutline(Outline);
            c.AddOutline(SsUiKit.Paper);
            return Make(key, c, new Vector2(0.5f, 0.5f), 100f, FilterMode.Point);
        }

        /// <summary>光柱：白色、底部最亮往上淡出、左右邊緣柔和；顏色由 SpriteRenderer.color 決定。軸心在底部中央，世界尺寸 1×4。</summary>
        internal static Sprite BeamSprite()
        {
            const string key = "beam";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            const int w = 16, h = 64;
            var c = new Raster(w, h);
            for (var y = 0; y < h; y++)
            {
                var v = 1f - y / (h - 1f);
                for (var x = 0; x < w; x++)
                {
                    var u = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                    c.Set(x, y, new Color(1f, 1f, 1f, v * v * Mathf.Clamp01(1f - u * u)));
                }
            }
            return Make(key, c, new Vector2(0.5f, 0f), w, FilterMode.Bilinear);
        }

        /// <summary>地面光暈（白色圓、邊緣柔和）；軸心在中央，世界尺寸 1×1。</summary>
        internal static Sprite GlowSprite()
        {
            const string key = "glow";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            const int size = 32;
            var c = new Raster(size, size);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) / size * 2f - 1f;
                    var dy = (y + 0.5f) / size * 2f - 1f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    c.Set(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * 0.9f));
                }
            }
            return Make(key, c, new Vector2(0.5f, 0.5f), size, FilterMode.Bilinear);
        }
        #endregion

        #region 繪製
        private const int ShapeDefault = 0, ShapeNewspaper = 1, ShapeUmbrella = 2, ShapeBow = 3, ShapeCrossbow = 4, ShapeSteel = 5;

        /// <summary>同一種類依名稱換外形（郵差包／捲筒報紙／長柄雨傘、彈弓／短弓／十字弓、護片／鋼板）；認不得的名稱用該種類的預設外形。</summary>
        private static int ShapeOf(ItemData item)
        {
            var name = item.Name ?? string.Empty;
            switch (item.Kind)
            {
                case ItemKind.Melee:
                    if (name.Contains("報紙")) return ShapeNewspaper;
                    if (name.Contains("傘")) return ShapeUmbrella;
                    return ShapeDefault;
                case ItemKind.Ranged:
                    if (name.Contains("十字弓")) return ShapeCrossbow;
                    if (name.Contains("弓") && !name.Contains("彈弓")) return ShapeBow;
                    return ShapeDefault;
                case ItemKind.Shell:
                    return name.Contains("鋼") ? ShapeSteel : ShapeDefault;
                default:
                    return ShapeDefault;
            }
        }

        /// <summary>主材質往稀有度顏色靠：白色不變，藍、黃各混一半。</summary>
        private static Color Mix(Color material, Rarity rarity) =>
            rarity == Rarity.Common ? material : Color.Lerp(material, ItemData.RarityColor(rarity), 0.5f);

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static void DrawItem(Raster c, ItemKind kind, int shape, Rarity rarity)
        {
            switch (kind)
            {
                case ItemKind.Part:
                    DrawGear(c);
                    break;
                case ItemKind.Melee:
                    if (shape == ShapeNewspaper)
                        DrawNewspaper(c, Mix(Paper, rarity));
                    else if (shape == ShapeUmbrella)
                        DrawUmbrella(c, Mix(new Color(0.3f, 0.32f, 0.42f), rarity));
                    else
                        DrawSatchel(c, Mix(Leather, rarity));
                    break;
                case ItemKind.Ranged:
                    if (shape == ShapeBow)
                        DrawBow(c, Mix(Wood, rarity));
                    else if (shape == ShapeCrossbow)
                        DrawCrossbow(c, Mix(Wood, rarity));
                    else
                        DrawSlingshot(c, Mix(Wood, rarity));
                    break;
                case ItemKind.Shell:
                    DrawShell(c, shape == ShapeSteel, Mix(shape == ShapeSteel ? Metal : ShellGreen, rarity));
                    break;
            }
        }

        /// <summary>零件：8 齒齒輪、中間有孔（32×32）。</summary>
        private static void DrawGear(Raster c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = x + 0.5f - 16f;
                    var dy = y + 0.5f - 16f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var outer = Mathf.Cos(Mathf.Atan2(dy, dx) * 8f) > 0.1f ? 13f : 10f;
                    if (r <= outer && r >= 4.5f)
                        c.Set(x, y, dy > 2f ? Metal : MetalDark);
                }
            }
        }

        /// <summary>郵差包：背帶＋包身＋蓋子＋扣環（64×96）。</summary>
        private static void DrawSatchel(Raster c, Color bag)
        {
            c.Ring(32, 58, 22, 3f, Shade(bag, 0.7f), 0.2f);
            c.RoundRect(8, 10, 56, 62, 8, bag);
            c.RoundRect(8, 40, 56, 64, 6, Shade(bag, 0.8f));
            c.Rect(28, 36, 36, 46, SsUiKit.Gold);
            c.Rect(30, 38, 34, 44, Shade(SsUiKit.Gold, 0.7f));
            c.Rect(12, 16, 52, 18, Shade(bag, 0.75f));
        }

        /// <summary>捲筒報紙：斜放的圓筒＋字行（64×96）。</summary>
        private static void DrawNewspaper(Raster c, Color paper)
        {
            c.Line(18, 12, 46, 84, 9f, paper);
            for (var i = 1; i < 6; i++)
            {
                var t = i / 6f;
                var px = Mathf.Lerp(18, 46, t);
                var py = Mathf.Lerp(12, 84, t);
                c.Line(px - 7f, py + 3f, px + 7f, py - 3f, 1.2f, Shade(paper, 0.6f));
            }
            c.Disc(46, 84, 8.5f, Shade(paper, 0.85f));
            c.Disc(46, 84, 3f, Shade(paper, 0.55f));
        }

        /// <summary>長柄雨傘（收起）：傘布、傘骨、J 形把手（64×96）。</summary>
        private static void DrawUmbrella(Raster c, Color cloth)
        {
            c.Line(32, 22, 32, 90, 2f, MetalDark);
            for (var y = 34; y <= 84; y++)
            {
                var t = (y - 34) / 50f;
                var half = Mathf.Lerp(9f, 2f, t * t);
                c.Rect(Mathf.RoundToInt(32 - half), y, Mathf.RoundToInt(32 + half), y + 1, y % 8 < 2 ? Shade(cloth, 0.75f) : cloth);
            }
            c.Line(32, 16, 32, 34, 3.5f, Wood);
            c.Ring(24, 16, 8, 3.5f, Wood, -1f);
            c.Rect(14, 16, 34, 30, new Color(0f, 0f, 0f, 0f));
            c.Line(32, 16, 32, 34, 3.5f, Wood);
        }

        /// <summary>彈弓：Y 形木叉＋橡皮筋＋皮兜（64×96）。</summary>
        private static void DrawSlingshot(Raster c, Color wood)
        {
            c.Line(32, 8, 32, 46, 5f, wood);
            c.Line(32, 46, 14, 84, 4.5f, wood);
            c.Line(32, 46, 50, 84, 4.5f, wood);
            c.Line(14, 82, 32, 66, 1.2f, SsUiKit.Danger);
            c.Line(50, 82, 32, 66, 1.2f, SsUiKit.Danger);
            c.Disc(32, 66, 4f, Leather);
            c.Rect(27, 14, 37, 22, Leather);
        }

        /// <summary>短弓：弓臂弧線＋弦＋握把（64×96）。</summary>
        private static void DrawBow(Raster c, Color wood)
        {
            for (var y = 6; y < 90; y++)
            {
                var t = (y - 48f) / 42f;
                c.Disc(20f + 22f * (1f - t * t), y, 3.2f, wood);
            }
            c.Line(20, 6, 20, 90, 0.8f, Paper);
            c.Rect(37, 42, 46, 54, Leather);
        }

        /// <summary>十字弓：直立弩身、橫向弓臂、弦、箭（64×96）。</summary>
        private static void DrawCrossbow(Raster c, Color wood)
        {
            c.RoundRect(28, 6, 37, 78, 3, wood);
            for (var x = 6; x < 58; x++)
            {
                var t = (x - 32f) / 26f;
                c.Disc(x, 72f - 10f * t * t, 2.6f, MetalDark);
            }
            c.Line(7, 62, 32, 50, 0.8f, Paper);
            c.Line(57, 62, 32, 50, 0.8f, Paper);
            c.Line(32, 50, 32, 90, 1.5f, Metal);
            c.Rect(26, 22, 39, 28, MetalDark);
        }

        /// <summary>殼甲：橢圓殼＋六角紋（護片）或十字鋼條與鉚釘（鋼板），兩側皮帶（64×64）。</summary>
        private static void DrawShell(Raster c, bool steel, Color body)
        {
            c.Rect(3, 27, 61, 37, Leather);
            c.Ellipse(32, 30, 27, 23, Shade(body, 0.75f));
            c.Ellipse(32, 32, 24, 20, body);
            var line = Shade(body, 0.7f);
            if (steel)
            {
                c.Rect(8, 30, 56, 33, line);
                c.Rect(30, 12, 34, 52, line);
                for (var i = 0; i < 4; i++)
                    c.Disc(i % 2 == 0 ? 19 : 45, i < 2 ? 22 : 42, 2.5f, Color.Lerp(body, Color.white, 0.5f));
            }
            else
            {
                c.Hex(32, 32, 7f, line);
                c.Hex(19, 39, 5.5f, line);
                c.Hex(45, 39, 5.5f, line);
                c.Hex(19, 25, 5.5f, line);
                c.Hex(45, 25, 5.5f, line);
            }
        }
        #endregion

        #region 產生 Sprite
        private static Sprite Make(string key, Raster canvas, Vector2 pivot, float ppu, FilterMode filter)
        {
            var tex = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false)
            {
                name = "SsLoot_" + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = filter,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(canvas.Pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, canvas.Width, canvas.Height), pivot, ppu, 0, SpriteMeshType.FullRect);
            sprite.name = tex.name;
            sprite.hideFlags = HideFlags.DontSave;
            Owned.Add(tex);
            Owned.Add(sprite);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>簡單的像素畫布：(0,0)＝左下，形狀用距離判斷、不做反鋸齒（像素風）。後畫的蓋掉先畫的；alpha 0 的顏色＝擦掉。</summary>
        private sealed class Raster
        {
            public readonly int Width, Height;
            public readonly Color32[] Pixels;

            public Raster(int w, int h)
            {
                Width = w;
                Height = h;
                Pixels = new Color32[w * h];
                for (var i = 0; i < Pixels.Length; i++)
                    Pixels[i] = Clear;
            }

            public void Set(int x, int y, Color c)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    return;
                Pixels[y * Width + x] = c;
            }

            private bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Pixels[y * Width + x].a > 0;

            /// <summary>[x0, x1) × [y0, y1)。</summary>
            public void Rect(int x0, int y0, int x1, int y1, Color c)
            {
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                        Set(x, y, c);
            }

            public void RoundRect(int x0, int y0, int x1, int y1, float radius, Color c)
            {
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var px = x + 0.5f;
                        var py = y + 0.5f;
                        var cx = Mathf.Clamp(px, x0 + radius, x1 - radius);
                        var cy = Mathf.Clamp(py, y0 + radius, y1 - radius);
                        if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= radius * radius)
                            Set(x, y, c);
                    }
                }
            }

            public void Disc(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

            public void Ellipse(float cx, float cy, float rx, float ry, Color c)
            {
                for (var y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
                {
                    for (var x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                    {
                        var dx = (x + 0.5f - cx) / rx;
                        var dy = (y + 0.5f - cy) / ry;
                        if (dx * dx + dy * dy <= 1f)
                            Set(x, y, c);
                    }
                }
            }

            /// <summary>圓環；只畫 dy ≥ minYRatio×r 的部分（0＝上半圈、-1＝整圈）。</summary>
            public void Ring(float cx, float cy, float r, float thickness, Color c, float minYRatio)
            {
                for (var y = Mathf.FloorToInt(cy - r - thickness); y <= Mathf.CeilToInt(cy + r + thickness); y++)
                {
                    for (var x = Mathf.FloorToInt(cx - r - thickness); x <= Mathf.CeilToInt(cx + r + thickness); x++)
                    {
                        var dx = x + 0.5f - cx;
                        var dy = y + 0.5f - cy;
                        if (dy < minYRatio * r)
                            continue;
                        if (Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) <= thickness * 0.5f)
                            Set(x, y, c);
                    }
                }
            }

            /// <summary>粗線（兩端圓頭）。</summary>
            public void Line(float x0, float y0, float x1, float y1, float halfWidth, Color c)
            {
                var a = new Vector2(x0, y0);
                var ab = new Vector2(x1, y1) - a;
                var len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
                var minX = Mathf.FloorToInt(Mathf.Min(x0, x1) - halfWidth);
                var maxX = Mathf.CeilToInt(Mathf.Max(x0, x1) + halfWidth);
                var minY = Mathf.FloorToInt(Mathf.Min(y0, y1) - halfWidth);
                var maxY = Mathf.CeilToInt(Mathf.Max(y0, y1) + halfWidth);
                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        if ((p - (a + ab * t)).sqrMagnitude <= halfWidth * halfWidth)
                            Set(x, y, c);
                    }
                }
            }

            /// <summary>六角形（尖角朝左右）。</summary>
            public void Hex(float cx, float cy, float r, Color c)
            {
                for (var y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                {
                    for (var x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                    {
                        var dx = Mathf.Abs(x + 0.5f - cx);
                        var dy = Mathf.Abs(y + 0.5f - cy);
                        if (dy <= r * 0.866f && dx + dy * 0.577f <= r)
                            Set(x, y, c);
                    }
                }
            }

            /// <summary>在不透明區域外圍加一圈 1px 的邊（只往透明格子長）。</summary>
            public void AddOutline(Color c)
            {
                var edge = new bool[Pixels.Length];
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        if (!Opaque(x, y))
                            edge[y * Width + x] = Opaque(x - 1, y) || Opaque(x + 1, y) || Opaque(x, y - 1) || Opaque(x, y + 1);
                    }
                }
                Color32 c32 = c;
                for (var i = 0; i < edge.Length; i++)
                {
                    if (edge[i])
                        Pixels[i] = c32;
                }
            }
        }
        #endregion
    }
}
