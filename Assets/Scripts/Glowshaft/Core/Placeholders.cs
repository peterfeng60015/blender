using System.Collections.Generic;
using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 美術素材還沒到時用的程式產生佔位圖（純色幾何形）。8 方向的角色會在朝向那一側畫一個深色小點，
    /// 方便確認方向切換正確；Run 的兩幀上下錯開一點，看得出在播動畫。
    /// 尺寸（世界單位）大致對應正式素材預期的大小，讓碰撞半徑看起來合理。
    /// </summary>
    internal static class Placeholders
    {
        private const int TexSize = 64;

        public static SpriteSet Create(string location, int dirs)
        {
            var name = location;
            var cut = location.IndexOf('_');
            if (cut > 0)
                name = location.Substring(0, cut);
            var action = cut > 0 ? location.Substring(cut + 1) : string.Empty;

            switch (name)
            {
                case "Miner":
                    return Character(location, dirs, new Color(0.95f, 0.75f, 0.25f), 1.0f, action == "Run" ? 4 : 1, false);
                case "SlimeGreen":
                    return Character(location, dirs, new Color(0.35f, 0.95f, 0.4f), 0.9f, 4, true);
                case "SlimePurple":
                    return Character(location, dirs, new Color(0.75f, 0.4f, 1f), 0.7f, 4, true);
                case "SlimeOrange":
                    return Character(location, dirs, new Color(1f, 0.55f, 0.2f), 1.5f, 4, true);
                case "Bullet":
                    return Single(location, 0.35f, 1, (x, y, v) => Disc(x, y, 0.9f, new Color(0.6f, 0.95f, 1f)));
                case "Battery":
                    return Single(location, 0.6f, 1, (x, y, v) => BatteryShape(x, y));
                case "Floor":
                    return Single(location, 2f, 1, FloorShape, pivotY: 0.5f);
                case "Rock":
                    return Single(location, 1.4f, 3, RockShape);
                case "Crystal":
                    return Single(location, 1.0f, 3, CrystalShape);
                default:
                    return Single(location, 1f, 1, (x, y, v) => new Color(1f, 0f, 1f));
            }
        }

        private delegate Color Shape(float x, float y, int variant);

        /// <summary>x、y 是 -1～1 的格子座標（0 在中心），回傳該像素顏色（alpha 0＝透明）。</summary>
        private static SpriteSet Single(string location, float worldSize, int variants, Shape shape, float pivotY = 0.3f)
        {
            var sprites = new Sprite[variants];
            for (var v = 0; v < variants; v++)
                sprites[v] = Make($"{location}_{v}", worldSize, (x, y) => shape(x, y, v), pivotY);
            return new SpriteSet(location, sprites, 1, 8f, true);
        }

        private static SpriteSet Character(string location, int dirs, Color body, float worldSize, int frames, bool slime)
        {
            var sprites = new List<Sprite>(dirs * frames);
            for (var d = 0; d < dirs; d++)
            {
                // 方向 d 的朝向（0＝S，逆時針）
                var angle = (-90f + d * 360f / dirs) * Mathf.Deg2Rad;
                var face = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                for (var f = 0; f < frames; f++)
                {
                    var bob = frames > 1 ? Mathf.Sin(f * Mathf.PI * 2f / frames) * 0.08f : 0f;
                    var squash = slime ? 1f + bob : 1f;
                    sprites.Add(Make($"{location}_{d * frames + f}", worldSize, (x, y) =>
                    {
                        var px = x / squash;
                        var py = (y - bob) * squash;
                        // 史萊姆是下半圓壓扁的水滴，礦工是圓身體
                        var r = slime ? (py < 0 ? Mathf.Sqrt(px * px + py * py * 2.2f) : Mathf.Sqrt(px * px + py * py * 0.9f))
                                      : Mathf.Sqrt(px * px + py * py);
                        if (r > 0.82f)
                            return Color.clear;
                        var eye = new Vector2(px, py) - face * 0.5f;
                        if (eye.sqrMagnitude < 0.04f)
                            return new Color(0.08f, 0.08f, 0.1f);
                        var shade = Mathf.Lerp(1f, 0.65f, r / 0.82f);
                        return new Color(body.r * shade, body.g * shade, body.b * shade, slime ? 0.9f : 1f);
                    }, 0.3f));
                }
            }
            return new SpriteSet(location, sprites.ToArray(), dirs, 8f, true);
        }

        private static Sprite Make(string name, float worldSize, System.Func<float, float, Color> pixel, float pivotY)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[TexSize * TexSize];
            for (var j = 0; j < TexSize; j++)
            for (var i = 0; i < TexSize; i++)
            {
                var x = (i + 0.5f) / TexSize * 2f - 1f;
                var y = (j + 0.5f) / TexSize * 2f - 1f;
                pixels[j * TexSize + i] = pixel(x, y);
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, pivotY),
                TexSize / worldSize, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        private static Color Disc(float x, float y, float radius, Color c)
        {
            var r = Mathf.Sqrt(x * x + y * y);
            return r > radius ? Color.clear : Color.Lerp(Color.white, c, r / radius);
        }

        private static Color BatteryShape(float x, float y)
        {
            if (Mathf.Abs(x) < 0.45f && y > -0.85f && y < 0.6f)
                return y > 0.2f ? new Color(0.3f, 1f, 0.45f) : new Color(0.15f, 0.6f, 0.25f);
            if (Mathf.Abs(x) < 0.18f && y >= 0.6f && y < 0.8f)
                return new Color(0.8f, 0.8f, 0.8f);
            return Color.clear;
        }

        private static Color FloorShape(float x, float y, int variant)
        {
            // 以格子座標的整數雜湊做斑點；邊緣不特別處理，平鋪時接縫不明顯即可
            var i = Mathf.FloorToInt((x + 1f) * 8f);
            var j = Mathf.FloorToInt((y + 1f) * 8f);
            var h = Mathf.Abs((i * 73856093) ^ (j * 19349663)) % 100;
            var g = 0.22f + h * 0.0012f;
            return new Color(g * 1.05f, g * 0.95f, g * 0.85f);
        }

        private static Color RockShape(float x, float y, int variant)
        {
            var a = Mathf.Atan2(y, x);
            var radius = 0.75f + 0.12f * Mathf.Sin(a * (3 + variant) + variant) + 0.05f * Mathf.Sin(a * 7f);
            var r = Mathf.Sqrt(x * x + y * y * 1.3f);
            if (r > radius)
                return Color.clear;
            var shade = Mathf.Lerp(0.6f, 0.35f, r / radius) + (y > 0 ? 0.08f : 0f);
            return new Color(shade, shade * 0.95f, shade * 0.9f);
        }

        private static Color CrystalShape(float x, float y, int variant)
        {
            var colors = new[] { new Color(0.4f, 0.9f, 1f), new Color(0.9f, 0.45f, 1f), new Color(0.45f, 1f, 0.75f) };
            var c = colors[variant % colors.Length];
            // 菱形
            if (Mathf.Abs(x) * 1.8f + Mathf.Abs(y + 0.1f) < 0.8f)
                return Color.Lerp(Color.white, c, 0.5f + 0.5f * Mathf.Abs(x) * 2f);
            return Color.clear;
        }
    }
}
