using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 底部對話框（cutscene-format.md §3 say）：立繪、名牌、逐字台詞、▼。
    /// 樣式：normal＝立繪＋名牌；screen＝立繪放進電視框；offscreen＝只有名牌；narration＝沒有名牌與立繪、文字置中斜體。
    /// 由 <see cref="StoryUi"/> 建立與驅動，本身不讀輸入。對話框與名牌用 <see cref="UiSkin"/> 的外框（沒讀到時是純色）。
    /// </summary>
    public sealed class DialogueView
    {
        private const float PanelSide = 160f;
        private const float PanelBottom = 40f;
        private const float PanelHeight = 250f;
        private const float PortraitSize = 380f;

        private readonly RectTransform _root;
        private readonly RectTransform _portraitRt;
        private readonly Image _portrait;
        private readonly RectTransform _tv;
        private readonly Image _tvPortrait;
        private readonly Image _panel;
        private readonly RectTransform _nameplate;
        private readonly TextMeshProUGUI _name;
        private readonly TextMeshProUGUI _text;
        private readonly TextMeshProUGUI _next;
        private readonly TextMeshProUGUI _help;
        private float _blink;

        public bool Visible => _root.gameObject.activeSelf;
        /// <summary>目前台詞的字數（TMP 的 characterCount，含空白與換行）。</summary>
        public int CharacterCount { get; private set; }

        public DialogueView(Transform parent, TMP_FontAsset font, UiSkin skin)
        {
            _root = SsUiKit.Fill("Dialogue", parent);

            #region 立繪（在對話框後面，下緣被對話框蓋住一點）
            _portraitRt = SsUiKit.Rect("Portrait", _root, Vector2.zero, Vector2.zero, Vector2.zero);
            _portraitRt.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _portrait = SsUiKit.AddImage(_portraitRt, Color.white);
            #endregion

            #region 電視框（style: screen）
            _tv = SsUiKit.Rect("Tv", _root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _tv.anchoredPosition = new Vector2(0f, -90f);
            _tv.sizeDelta = new Vector2(520f, 440f);
            SsUiKit.AddImage(_tv, SsUiKit.Neon);
            var bezel = SsUiKit.Fill("Bezel", _tv);
            bezel.offsetMin = new Vector2(8f, 8f);
            bezel.offsetMax = new Vector2(-8f, -8f);
            SsUiKit.AddImage(bezel, new Color(0.11f, 0.1f, 0.16f));
            var screen = SsUiKit.Fill("Screen", _tv);
            screen.offsetMin = new Vector2(30f, 30f);
            screen.offsetMax = new Vector2(-30f, -30f);
            SsUiKit.AddImage(screen, new Color(0.16f, 0.26f, 0.38f));
            var tvPortraitRt = SsUiKit.Fill("TvPortrait", screen);
            tvPortraitRt.offsetMin = new Vector2(8f, 0f);
            tvPortraitRt.offsetMax = new Vector2(-8f, -8f);
            _tvPortrait = SsUiKit.AddImage(tvPortraitRt, Color.white);
            var onAir = SsUiKit.Rect("OnAir", _tv, Vector2.one, Vector2.one, Vector2.one);
            onAir.anchoredPosition = new Vector2(-14f, -12f);
            onAir.sizeDelta = new Vector2(14f, 14f);
            SsUiKit.AddImage(onAir, SsUiKit.Danger);
            #endregion

            #region 對話框
            var panel = SsUiKit.Rect("Panel", _root, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            panel.offsetMin = new Vector2(PanelSide, PanelBottom);
            panel.offsetMax = new Vector2(-PanelSide, PanelBottom + PanelHeight);
            _panel = skin.Add(panel, UiSkin.Dialogue, 0.6f);

            var textRt = SsUiKit.Fill("Text", panel);
            textRt.offsetMin = new Vector2(52f, 44f);
            textRt.offsetMax = new Vector2(-52f, -46f);
            _text = SsUiKit.AddText(textRt, font, 42f, SsUiKit.Paper, TextAlignmentOptions.TopLeft);
            _text.lineSpacing = 12f;

            var nextRt = SsUiKit.Rect("Next", panel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            nextRt.anchoredPosition = new Vector2(-28f, 14f);
            nextRt.sizeDelta = new Vector2(44f, 44f);
            _next = SsUiKit.AddText(nextRt, font, 34f, SsUiKit.Gold);
            _next.text = StoryTexts.DialogueNext;

            var helpRt = SsUiKit.Rect("Help", panel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            helpRt.anchoredPosition = new Vector2(-82f, 12f);
            helpRt.sizeDelta = new Vector2(640f, 36f);
            _help = SsUiKit.AddText(helpRt, font, 22f, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.BottomRight);
            _help.text = StoryTexts.DialogueHelp;

            _nameplate = SsUiKit.Rect("Nameplate", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            _nameplate.sizeDelta = new Vector2(240f, 62f);
            skin.Add(_nameplate, UiSkin.Nameplate, 0.45f);
            var nameRt = SsUiKit.Fill("Name", _nameplate);
            _name = SsUiKit.AddText(nameRt, font, 36f, SsUiKit.Ink);
            #endregion

            _root.gameObject.SetActive(false);
        }

        /// <summary>顯示一句台詞（全部字先隱藏，用 <see cref="SetVisibleCharacters"/> 逐字打出）。</summary>
        public void Show(string name, string text, string style, bool rightSide, Sprite portrait)
        {
            _root.gameObject.SetActive(true);
            var narration = style == "narration";
            var screen = style == "screen";
            var showName = !narration && !string.IsNullOrEmpty(name);

            #region 立繪與電視框
            var showPortrait = style == "normal" && portrait != null;
            _portraitRt.gameObject.SetActive(showPortrait);
            if (showPortrait)
            {
                _portrait.sprite = portrait;
                _portrait.preserveAspect = true;
                _portraitRt.anchorMin = _portraitRt.anchorMax = new Vector2(rightSide ? 1f : 0f, 0f);
                // pivot 放中央：右側鏡像（scale.x = -1）時才不會往畫面外翻
                _portraitRt.pivot = new Vector2(0.5f, 0f);
                var cx = PanelSide + 30f + PortraitSize / 2f;
                _portraitRt.anchoredPosition = new Vector2(rightSide ? -cx : cx, PanelBottom + PanelHeight - 50f);
                // 立繪預設面向右；放右側時鏡像，讓角色面向畫面中央
                _portraitRt.localScale = new Vector3(rightSide ? -1f : 1f, 1f, 1f);
            }
            _tv.gameObject.SetActive(screen);
            if (screen)
            {
                _tvPortrait.sprite = portrait;
                _tvPortrait.preserveAspect = true;
                _tvPortrait.enabled = portrait != null;
            }
            #endregion

            #region 名牌（放在立繪旁邊，不蓋到臉）
            _nameplate.gameObject.SetActive(showName);
            if (showName)
            {
                _name.text = name;
                var inset = showPortrait ? PortraitSize + 60f : 40f;
                _nameplate.anchorMin = _nameplate.anchorMax = new Vector2(rightSide ? 1f : 0f, 1f);
                _nameplate.pivot = new Vector2(rightSide ? 1f : 0f, 0.5f);
                _nameplate.anchoredPosition = new Vector2(rightSide ? -inset : inset, 0f);
            }
            #endregion

            // 旁白：外框壓暗、半透明，和角色台詞區分，也讓開場時看得到後面的場景與角色
            _panel.color = narration ? new Color(0.55f, 0.55f, 0.6f, 0.9f) : Color.white;
            _text.alignment = narration ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            _text.fontStyle = narration ? FontStyles.Italic : FontStyles.Normal;
            _text.text = text ?? string.Empty;
            _text.maxVisibleCharacters = 0;
            _text.ForceMeshUpdate();
            CharacterCount = _text.textInfo.characterCount;
            SetWaiting(false);
        }

        public void SetVisibleCharacters(int count) => _text.maxVisibleCharacters = Mathf.Clamp(count, 0, CharacterCount);

        /// <summary>全文已顯示、等按鍵：顯示會閃的 ▼。</summary>
        public void SetWaiting(bool waiting)
        {
            _next.gameObject.SetActive(waiting);
            _blink = 0f;
        }

        public void Hide()
        {
            if (_root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
        }

        /// <summary>操作說明依輸入裝置切換；▼ 上下跳動（unscaled，暫停中也動）。</summary>
        public void Tick(float unscaledDt)
        {
            if (_root.gameObject.activeSelf)
            {
                var help = SsInput.TouchActive ? StoryTexts.DialogueHelpTouch : StoryTexts.DialogueHelp;
                if (_help.text != help)
                    _help.text = help;
            }
            if (!_next.gameObject.activeSelf)
                return;
            _blink += unscaledDt;
            var rt = _next.rectTransform;
            rt.anchoredPosition = new Vector2(-28f, 14f + Mathf.Abs(Mathf.Sin(_blink * 5f)) * 6f);
        }
    }
}
