using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 存檔點 cp1、cp2 旁的補給站（ui 實作，loot-design.md §6）：外觀是染色的販賣機（SsProps 變化 2）加上頂端一顆會呼吸的金色光點。
    /// 每幀看主角在不在範圍內，進出時通知 <see cref="LootScreens.SetStationInRange"/>（顯示「E：強化」、觸控「強化」鈕）。
    /// 位置避開龜婆（x 76）與 Boss 閘門（x 189）：cp1 往左、cp2 往左。重生時跟著 worldRoot 一起被刪、再由 GameFlow 重建。
    /// </summary>
    public sealed class SupplyStation : MonoBehaviour
    {
        private const string Log = "[Sidescroller.SupplyStation]";
        private const string PropLocation = "SsProps_Idle";
        private const int VendingVariant = 2;
        /// <summary>補給站相對存檔點的水平位置（cp1：龜婆在右邊；cp2：閘門在右邊）。</summary>
        private const float OffsetCp1 = -3.2f, OffsetCp2 = -2.6f;
        private const float RangeX = 1.8f, RangeY = 2.5f;
        private static readonly Color Tint = new Color(0.62f, 1f, 0.9f);

        private StoryUi _story;
        private Transform _hero;
        private bool _inRange;
        private Transform _glow;
        private SpriteRenderer _glowRenderer;
        private Texture2D _glowTexture;
        private Sprite _glowSprite;

        /// <summary>GameFlow 每次建關卡（含重生）在建好角色後呼叫；物件掛在 parent（worldRoot）底下。</summary>
        public static void CreateAll(Transform parent, LevelInfo info, GameSession session, StoryUi story)
        {
            if (parent == null || info == null)
                return;
            Create(parent, info, story, "cp1", OffsetCp1);
            Create(parent, info, story, "cp2", OffsetCp2);
        }

        private static void Create(Transform parent, LevelInfo info, StoryUi story, string checkpoint, float offset)
        {
            var cp = info.FindCheckpoint(checkpoint);
            if (cp == null)
            {
                Debug.LogWarning($"{Log} 找不到存檔點 {checkpoint}，不建補給站");
                return;
            }
            var go = new GameObject($"SupplyStation_{checkpoint}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(cp.Position.x + offset, cp.Position.y, 0f);
            var station = go.AddComponent<SupplyStation>();
            station._story = story;
            station.Build();
        }

        private void Build()
        {
            var sprite = SsArt.Get(PropLocation).Variant(VendingVariant);
            var body = LevelBuilder.PlaceSprite(transform, "Body", sprite, Vector2.zero, true, SsSorting.Props + 1);
            body.color = Tint;
            var top = sprite != null ? sprite.bounds.size.y : 2f;

            _glowTexture = CreateGlowTexture(32);
            _glowSprite = Sprite.Create(_glowTexture, new Rect(0f, 0f, _glowTexture.width, _glowTexture.height), new Vector2(0.5f, 0.5f), 32f);
            _glowSprite.name = "SsSupplyGlow";
            _glow = LevelBuilder.NewChild(transform, "Glow");
            _glow.localPosition = new Vector3(0f, top + 0.45f, 0f);
            _glowRenderer = _glow.gameObject.AddComponent<SpriteRenderer>();
            _glowRenderer.sprite = _glowSprite;
            _glowRenderer.sortingOrder = SsSorting.Props + 2;
            _glowRenderer.color = new Color(1f, 0.82f, 0.3f, 0.9f);
        }

        private void Update()
        {
            if (_hero == null)
            {
                var actor = ActorRegistry.Find(SsIds.ActorHero);
                _hero = actor != null ? actor.Root : null;
            }
            var t = Time.time;
            if (_glow != null)
            {
                var s = 0.55f + 0.1f * Mathf.Sin(t * 3f);
                _glow.localScale = new Vector3(s, s, 1f);
            }
            var inRange = false;
            if (_hero != null)
            {
                var d = (Vector2)(_hero.position - transform.position);
                inRange = Mathf.Abs(d.x) <= RangeX && d.y > -0.5f && d.y <= RangeY;
            }
            var screens = _story != null ? _story.LootScreens : null;
            if (screens == null)
                return;
            if (inRange)
                screens.SetStationAnchor(_glow != null ? _glow.position + new Vector3(0f, 0.35f, 0f) : transform.position);
            if (inRange != _inRange)
            {
                _inRange = inRange;
                screens.SetStationInRange(inRange);
            }
        }

        private void OnDestroy()
        {
            if (_inRange && _story != null && _story.LootScreens != null)
                _story.LootScreens.SetStationInRange(false);
            if (_glowSprite != null)
                Destroy(_glowSprite);
            if (_glowTexture != null)
                Destroy(_glowTexture);
        }

        /// <summary>柔邊光點（中心白、往外變透明）。</summary>
        private static Texture2D CreateGlowTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SsSupplyGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            var c = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (size / 2f);
                    var a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
