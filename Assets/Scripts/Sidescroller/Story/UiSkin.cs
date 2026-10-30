using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// UI 外框素材 <c>SsUiSkin_Idle</c>（Tools/ComfyUI 產生與後製）的九宮格 sprite，與有外框、陰影的標題字。
    /// 由 <see cref="StoryUi"/> 在建 UI 時建立一份、OnDestroy 時 <see cref="Dispose"/>（不留 static 狀態）。
    /// 素材是佔位圖時（沒讀到）一律退回純色矩形，版面不變。
    /// </summary>
    public sealed class UiSkin
    {
        public const string Location = "SsUiSkin_Idle";
        public const int Panel = 0, ButtonOn = 1, ButtonOff = 2, Shadow = 3, Sunburst = 4, Pill = 5, Ribbon = 6, Sparkle = 7;
        public const int Dialogue = 8, Nameplate = 9;
        private const int Count = 10;
        private const float Cell = 256f;

        /// <summary>
        /// 各變化在格子裡的範圍（px，x、y、寬、高，y 從下往上）：橫向外框是格子中間 256×128，其他撐滿。
        /// 要和 Tools/ComfyUI/postprocess.py 的排版一致。
        /// </summary>
        private static readonly Rect Wide = new Rect(0f, 64f, Cell, 128f);
        private static readonly Rect Full = new Rect(0f, 0f, Cell, Cell);
        private static readonly Rect[] Areas = { Full, Wide, Wide, Full, Full, Wide, Wide, Full, Wide, Wide };

        /// <summary>各變化的九宮格邊界（px，左、下、右、上）；依後製後的圖量出來，換圖要重量。</summary>
        private static readonly Vector4[] Borders =
        {
            new Vector4(72f, 72f, 72f, 72f),
            new Vector4(64f, 40f, 64f, 40f),
            new Vector4(64f, 40f, 64f, 40f),
            new Vector4(72f, 72f, 72f, 72f),
            Vector4.zero,
            new Vector4(60f, 40f, 60f, 40f),
            new Vector4(64f, 36f, 64f, 36f),
            Vector4.zero,
            new Vector4(80f, 44f, 80f, 44f),
            new Vector4(56f, 36f, 56f, 36f),
        };

        /// <summary>素材沒讀到時的替代純色（和舊版介面相同色系）。</summary>
        private static readonly Color[] Fallback =
        {
            new Color(0.09f, 0.06f, 0.17f, 0.96f), SsUiKit.Magenta, new Color(1f, 1f, 1f, 0.06f), Color.clear,
            Color.clear, new Color(0.09f, 0.06f, 0.17f, 0.92f), SsUiKit.Magenta, Color.clear,
            SsUiKit.Ink, SsUiKit.Orange,
        };

        /// <summary>Canvas 的 referencePixelsPerUnit（uGUI 預設 100）。</summary>
        private const float CanvasPpu = 100f;

        private readonly Sprite[] _sprites;
        private readonly List<Object> _owned = new List<Object>();

        /// <summary>標題字外框的顏色（深紫）。</summary>
        public static readonly Color StrokeColor = new Color32(0x2A, 0x0F, 0x2E, 0xFF);

        public bool Loaded => _sprites != null;

        public UiSkin()
        {
            var set = SsArt.Get(Location);
            if (!set.IsPlaceholder && set.FrameCount >= Count)
            {
                _sprites = new Sprite[Count];
                for (var i = 0; i < Count; i++)
                {
                    var src = set.Variant(i);
                    var area = Areas[i];
                    var rect = new Rect(src.rect.x + area.x, src.rect.y + area.y, area.width, area.height);
                    var s = Sprite.Create(src.texture, rect, new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect, Borders[i]);
                    s.name = $"{Location}_{i}_Sliced";
                    _sprites[i] = s;
                    _owned.Add(s);
                }
            }
            else
            {
                Debug.LogWarning($"[Sidescroller.UiSkin] {Location} 沒讀到，介面改用純色");
            }
        }

        public void Dispose()
        {
            foreach (var o in _owned)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            _owned.Clear();
        }

        /// <summary>
        /// 在 rt 上加外框圖。scale＝素材 1 px 在畫布上的大小（九宮格邊角跟著縮放，中間拉伸）。
        /// 沒讀到素材時是純色（光芒、星光、陰影則是透明）。
        /// </summary>
        public Image Add(RectTransform rt, int variant, float scale = 0.5f, Color? tint = null)
        {
            var img = SsUiKit.AddImage(rt, Color.white);
            Set(img, variant, scale, tint);
            return img;
        }

        /// <summary>換成另一個變化（例如按鈕選中與未選中）。</summary>
        public void Set(Image img, int variant, float scale = 0.5f, Color? tint = null)
        {
            if (_sprites == null)
            {
                img.sprite = null;
                img.color = Fallback[variant];
                return;
            }
            img.sprite = _sprites[variant];
            img.color = tint ?? Color.white;
            img.preserveAspect = false;
            var sliced = Borders[variant] != Vector4.zero;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.fillCenter = true;
            // 九宮格邊角在畫布上的大小＝px × CanvasPpu ÷ (spritePpu × 倍率)
            img.pixelsPerUnitMultiplier = sliced ? CanvasPpu / (_sprites[variant].pixelsPerUnit * scale) : 1f;
        }

        /// <summary>
        /// 有外框與陰影的標題字：先建往右下偏的洋紅陰影，再建 8 個方向錯開 stroke 的深紫字當外框，最後建本體。回傳本體。
        /// 不用 SDF 描邊：這套字集的 padding 很小，描邊會往內吃掉筆畫。
        /// </summary>
        public TextMeshProUGUI Title(RectTransform rt, TMP_FontAsset font, float size, Color color, string text,
            float shadow = 7f, float spacing = 0f, float stroke = 4f)
        {
            Copy(rt, "Shadow", font, size, new Color(0.55f, 0.12f, 0.32f, 0.9f), text, spacing, new Vector2(shadow + stroke * 0.5f, -shadow - stroke * 0.5f));
            for (var i = 0; i < 8; i++)
            {
                var a = i * Mathf.PI / 4f;
                Copy(rt, "Stroke", font, size, StrokeColor, text, spacing, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * stroke);
            }
            return Copy(rt, "Text", font, size, color, text, spacing, Vector2.zero);
        }

        /// <summary>按鈕上的字：底下一份往下偏的深色字當陰影，亮底上也讀得到。回傳 (本體, 陰影)。</summary>
        public static (TextMeshProUGUI body, TextMeshProUGUI shadow) Label(RectTransform rt, TMP_FontAsset font, float size, Color color)
        {
            var s = Copy(rt, "Shadow", font, size, new Color(0.1f, 0.03f, 0.12f, 0.7f), string.Empty, 0f, new Vector2(0f, -3f));
            var t = Copy(rt, "Text", font, size, color, string.Empty, 0f, Vector2.zero);
            return (t, s);
        }

        private static TextMeshProUGUI Copy(RectTransform parent, string name, TMP_FontAsset font, float size, Color color, string text,
            float spacing, Vector2 offset)
        {
            var rt = SsUiKit.Fill(name, parent);
            rt.anchoredPosition = offset;
            var t = SsUiKit.AddText(rt, font, size, color);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.characterSpacing = spacing;
            t.text = text;
            return t;
        }
    }

    /// <summary>
    /// 面板進場動畫：從 <see cref="From"/> 倍放大回 1（easeOutBack）並淡入，用 unscaled time（暫停中也動）。
    /// 由擁有者每幀呼叫 <see cref="Tick"/>。
    /// </summary>
    internal sealed class UiPop
    {
        private readonly RectTransform _rt;
        private readonly CanvasGroup _group;
        private readonly float _time;
        private float _delay;
        private float _elapsed = -1f;

        public float From = 0.85f;

        public UiPop(RectTransform rt, float time)
        {
            _rt = rt;
            _time = Mathf.Max(0.01f, time);
            _group = rt.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        /// <summary>從頭播（delay 秒後才開始；等待期間不顯示）。</summary>
        public void Play(float delay = 0f)
        {
            _delay = delay;
            _elapsed = 0f;
            Apply(0f);
        }

        public void Tick(float unscaledDt)
        {
            if (_elapsed < 0f || !_rt.gameObject.activeInHierarchy)
                return;
            _elapsed += unscaledDt;
            var t = Mathf.Clamp01((_elapsed - _delay) / _time);
            Apply(t);
            if (t >= 1f)
                _elapsed = -1f;
        }

        private void Apply(float t)
        {
            _group.alpha = Mathf.Clamp01(t * 2.5f);
            var s = Mathf.LerpUnclamped(From, 1f, EaseOutBack(t));
            _rt.localScale = new Vector3(s, s, 1f);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            var u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
