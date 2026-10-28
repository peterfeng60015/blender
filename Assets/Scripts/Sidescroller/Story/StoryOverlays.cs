using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 全螢幕淡入淡出遮罩（流程與演出共用）。動畫在 <see cref="Tick"/> 用 scaled time 推進（暫停時停住）；
    /// 每次 <see cref="Start"/>／<see cref="SetImmediate"/> 都換一個版本號，舊的等待者看到版本變了就結束。
    /// </summary>
    public sealed class FadeView
    {
        private readonly Image _image;
        private float _from;
        private float _to;
        private float _time;
        private float _elapsed;
        private Color _rgb = Color.black;

        public float Alpha { get; private set; }
        public int Version { get; private set; }
        public bool Animating { get; private set; }
        public bool White => _rgb == Color.white;
        /// <summary>動畫結束後的不透明度（沒在動畫時＝目前值）。</summary>
        public float Target => Animating ? _to : Alpha;

        public FadeView(Transform parent)
        {
            var rt = SsUiKit.Fill("Fade", parent);
            _image = SsUiKit.AddImage(rt, Color.black);
        }

        /// <summary>從目前不透明度淡到 to；換顏色時保留目前不透明度。回傳這次的版本號。</summary>
        public int Start(float to, float time, bool white)
        {
            _rgb = white ? Color.white : Color.black;
            if (time <= 0f)
                return SetImmediate(to, white);
            Version++;
            _from = Alpha;
            _to = Mathf.Clamp01(to);
            _time = time;
            _elapsed = 0f;
            Animating = true;
            Apply();
            return Version;
        }

        public int SetImmediate(float to, bool white)
        {
            Version++;
            _rgb = white ? Color.white : Color.black;
            Alpha = Mathf.Clamp01(to);
            Animating = false;
            Apply();
            return Version;
        }

        /// <summary>版本 version 的淡入淡出已經做完（或被新的取代）。</summary>
        public bool IsDone(int version) => version != Version || !Animating;

        public void Tick(float dt)
        {
            if (!Animating)
                return;
            _elapsed += dt;
            var t = Mathf.Clamp01(_elapsed / _time);
            Alpha = Mathf.Lerp(_from, _to, t);
            if (t >= 1f)
                Animating = false;
            Apply();
        }

        private void Apply()
        {
            _image.color = new Color(_rgb.r, _rgb.g, _rgb.b, Alpha);
            var on = Alpha > 0.001f;
            if (_image.enabled != on)
                _image.enabled = on;
        }
    }

    /// <summary>字卡：畫面中央的橫條大字，淡入、停留、淡出，總長＝time。畫在遮罩之上。新字卡直接取代舊的。</summary>
    public sealed class BannerView
    {
        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly TextMeshProUGUI _text;
        private float _time;
        private float _elapsed;

        public bool Visible => _root.gameObject.activeSelf;

        public BannerView(Transform parent, TMP_FontAsset font)
        {
            _root = SsUiKit.Rect("Banner", parent, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
            _root.sizeDelta = new Vector2(0f, 170f);
            _root.anchoredPosition = new Vector2(0f, 120f);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            SsUiKit.AddImage(_root, new Color(0.05f, 0.03f, 0.1f, 0.78f));
            var top = SsUiKit.Rect("LineTop", _root, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f));
            top.sizeDelta = new Vector2(0f, 5f);
            SsUiKit.AddImage(top, SsUiKit.Orange);
            var bottom = SsUiKit.Rect("LineBottom", _root, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            bottom.sizeDelta = new Vector2(0f, 5f);
            SsUiKit.AddImage(bottom, SsUiKit.Magenta);
            var textRt = SsUiKit.Fill("Text", _root);
            _text = SsUiKit.AddText(textRt, font, 72f, SsUiKit.Gold);
            _text.characterSpacing = 6f;
            _root.gameObject.SetActive(false);
        }

        public void Show(string text, float time)
        {
            _text.text = text ?? string.Empty;
            _time = Mathf.Max(0.01f, time);
            _elapsed = 0f;
            _root.gameObject.SetActive(true);
            Apply();
        }

        public void Hide()
        {
            if (_root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
        }

        /// <summary>scaled time（字卡是演出的一部分，暫停時停住）。</summary>
        public void Tick(float dt)
        {
            if (!_root.gameObject.activeSelf)
                return;
            _elapsed += dt;
            if (_elapsed >= _time)
            {
                Hide();
                return;
            }
            Apply();
        }

        private void Apply()
        {
            var edge = Mathf.Min(0.25f, _time / 4f);
            var a = Mathf.Min(Mathf.Clamp01(_elapsed / edge), Mathf.Clamp01((_time - _elapsed) / edge));
            _group.alpha = a;
            var s = 0.92f + 0.08f * Mathf.Clamp01(_elapsed / edge);
            _root.localScale = new Vector3(1f, s, 1f);
        }
    }
}
