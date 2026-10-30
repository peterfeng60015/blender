using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// HUD（interfaces.md §4.3）：左上心與已取得的攻擊、目前服裝、「C 換制服」；上方中央提示文字（演出中隱藏）；
    /// 下方中央 Boss 血條（血量 ≤ 一半＝第 2 階段變紅）。圖示用 <c>SsUi_Idle</c> 的變化（design.md §8.4）。
    /// 由 <see cref="StoryUi"/> 訂閱 session 事件後呼叫各 Refresh，每幀呼叫 <see cref="Tick"/>。
    /// 整個 HUD 放在 <see cref="Screen.safeArea"/> 內（避開瀏海與圓角）；觸控模式時提示文字換成觸控版。
    /// </summary>
    public sealed class HudView
    {
        private const string UiLocation = "SsUi_Idle";
        private const int IconHeartFull = 0, IconHeartEmpty = 1, IconMelee = 2, IconSling = 3, IconRoll = 4;
        private const int IconCostumeDay = 5, IconCostumeExpress = 6, IconBoss = 7;
        private const float HeartSize = 60f;
        private const float BossBarWidth = 760f;
        private const float BossBarHeight = 44f;
        /// <summary>Boss 血條外框（Pill）到血量條的內距。</summary>
        private const float BossBarInset = 9f;
        /// <summary>扣血後白色殘影停多久才開始縮（秒）、每秒縮掉幾成血條。</summary>
        private const float BossTrailDelay = 0.35f, BossTrailSpeed = 0.6f;
        /// <summary>通關時收起：往上滑多少（畫布單位）、多久（秒，unscaled）。</summary>
        private const float HideTime = 0.3f, HideSlide = 90f;
        private const float HintHeight = 72f, HintMinWidth = 360f, HintMaxWidth = 1100f, HintPad = 56f;
        private static readonly Color BossTrailColor = new Color(1f, 0.93f, 0.85f, 0.9f);
        private static readonly Color BossTrackColor = new Color(0.05f, 0.03f, 0.1f, 1f);

        private readonly GameSession _session;
        private readonly SpriteSet _icons;
        private readonly RectTransform _root;
        private readonly RectTransform _hearts;
        private readonly List<Image> _heartImages = new List<Image>();
        private readonly Image[] _attackIcons = new Image[3];
        private readonly Image _costumeIcon;
        private readonly RectTransform _plate;
        /// <summary>「換制服」提示（本體＋陰影）的容器：只有一套服裝時整個關掉。</summary>
        private readonly RectTransform _switchRoot;
        private readonly TextMeshProUGUI _switchHint;
        private readonly TextMeshProUGUI _switchHintShadow;
        private readonly RectTransform _hint;
        private readonly TextMeshProUGUI _hintText;
        private readonly TextMeshProUGUI _hintShadow;
        private readonly RectTransform _boss;
        /// <summary>Boss 名稱的本體、外框、陰影（同一段字的多份）。</summary>
        private readonly TextMeshProUGUI[] _bossName;
        private readonly RectTransform _bossFill;
        private readonly Image _bossFillImage;
        private readonly RectTransform _bossTrail;
        private float _bossRatio = 1f;
        private float _bossTrailRatio = 1f;
        private float _bossTrailWait;
        private readonly CanvasGroup _group;
        /// <summary>收起動畫的進度（0＝顯示、1＝收完）；&lt;0＝沒在收。</summary>
        private float _hideT = -1f;

        public HudView(Transform parent, TMP_FontAsset font, GameSession session, UiSkin skin)
        {
            _session = session;
            _icons = SsArt.Get(UiLocation);
            _root = SsUiKit.Fill("Hud", parent);
            SsUiKit.FitSafeArea(_root);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            #region 左上：心、攻擊、服裝
            _hearts = SsUiKit.Rect("Hearts", _root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _hearts.anchoredPosition = new Vector2(40f, -32f);

            var loadout = SsUiKit.Rect("Loadout", _root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            loadout.anchoredPosition = new Vector2(40f, -32f - HeartSize - 14f);
            _plate = SsUiKit.Rect("Plate", loadout, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _plate.anchoredPosition = new Vector2(-8f, 8f);
            skin.Add(_plate, UiSkin.Pill, 0.3f, new Color(1f, 1f, 1f, 0.85f));
            for (var i = 0; i < 3; i++)
                _attackIcons[i] = Icon(loadout, i * 74f, IconMelee + i);
            _costumeIcon = Icon(loadout, 3 * 74f, IconCostumeDay);

            _switchRoot = SsUiKit.Rect("SwitchHint", loadout, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _switchRoot.anchoredPosition = new Vector2(0f, -84f);
            _switchRoot.sizeDelta = new Vector2(320f, 36f);
            (_switchHint, _switchHintShadow) = UiSkin.Label(_switchRoot, font, 26f, SsUiKit.Paper);
            _switchHint.alignment = _switchHintShadow.alignment = TextAlignmentOptions.Left;
            _switchHint.text = _switchHintShadow.text = StoryTexts.SwitchCostume;
            #endregion

            #region 上方中央：提示
            _hint = SsUiKit.Rect("Hint", _root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _hint.anchoredPosition = new Vector2(0f, -36f);
            _hint.sizeDelta = new Vector2(HintMaxWidth, HintHeight);
            skin.Add(_hint, UiSkin.Pill, 0.4f);
            var hintTextRt = SsUiKit.Fill("Text", _hint);
            hintTextRt.offsetMin = new Vector2(HintPad / 2f, 0f);
            hintTextRt.offsetMax = new Vector2(-HintPad / 2f, 0f);
            (_hintText, _hintShadow) = UiSkin.Label(hintTextRt, font, 34f, SsUiKit.Paper);
            // 不換行；底框到最寬還放不下時縮字（本體與陰影同一個框、同一段字，縮出來一樣大）
            foreach (var t in new[] { _hintText, _hintShadow })
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = 34f;
                t.fontSizeMin = 24f;
            }
            _hint.gameObject.SetActive(false);
            #endregion

            #region 下方中央：Boss 血條
            _boss = SsUiKit.Rect("BossBar", _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _boss.anchoredPosition = new Vector2(0f, 48f);
            _boss.sizeDelta = new Vector2(BossBarWidth + 120f, 104f);
            var bossIcon = SsUiKit.Rect("Icon", _boss, Vector2.zero, Vector2.zero, Vector2.zero);
            bossIcon.sizeDelta = new Vector2(96f, 96f);
            SsUiKit.AddImage(bossIcon, Color.white, _icons.Variant(IconBoss));
            var label = SsUiKit.Rect("Label", _boss, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            label.anchoredPosition = new Vector2(116f, 0f);
            label.sizeDelta = new Vector2(BossBarWidth, 44f);
            skin.Title(label, font, 32f, SsUiKit.Gold, string.Empty, 3f, 1f, 3f);
            _bossName = label.GetComponentsInChildren<TextMeshProUGUI>();
            foreach (var t in _bossName)
                t.alignment = TextAlignmentOptions.BottomLeft;
            // 外框（Pill）→ 深色軌道 → 白色殘影（扣血後慢慢縮）→ 血量 → 上半截高光
            var frame = SsUiKit.Rect("Frame", _boss, Vector2.zero, Vector2.zero, Vector2.zero);
            frame.anchoredPosition = new Vector2(112f, 6f);
            frame.sizeDelta = new Vector2(BossBarWidth, BossBarHeight);
            skin.Add(frame, UiSkin.Pill, 0.3f);
            var track = SsUiKit.Fill("Track", frame);
            track.offsetMin = new Vector2(BossBarInset, BossBarInset);
            track.offsetMax = new Vector2(-BossBarInset, -BossBarInset);
            SsUiKit.AddImage(track, BossTrackColor);
            _bossTrail = SsUiKit.Rect("Trail", track, Vector2.zero, new Vector2(0f, 1f), Vector2.zero);
            SsUiKit.AddImage(_bossTrail, BossTrailColor);
            _bossFill = SsUiKit.Rect("Fill", track, Vector2.zero, new Vector2(0f, 1f), Vector2.zero);
            _bossFillImage = SsUiKit.AddImage(_bossFill, SsUiKit.Gold);
            var gloss = SsUiKit.Rect("Gloss", _bossFill, new Vector2(0f, 0.55f), Vector2.one, new Vector2(0.5f, 1f));
            SsUiKit.AddImage(gloss, new Color(1f, 1f, 1f, 0.28f));
            _boss.gameObject.SetActive(false);
            #endregion

            RefreshHp();
            RefreshLoadout();
            RefreshHint();
            RefreshBoss();
        }

        /// <summary>每幀：跟上安全區域、依輸入裝置切換文字、更新提示。</summary>
        public void Tick()
        {
            SsUiKit.FitSafeArea(_root);
            var switchText = SsInput.TouchActive ? StoryTexts.SwitchCostumeTouch : StoryTexts.SwitchCostume;
            if (_switchHint.text != switchText)
                _switchHint.text = _switchHintShadow.text = switchText;
            RefreshHint();
            TickBossTrail(Time.deltaTime);
            TickHide(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        /// <summary>false：往上滑出並淡出（<see cref="HideTime"/> 秒，unscaled）後關掉；true：立刻顯示。</summary>
        public void SetVisible(bool visible)
        {
            if (visible)
            {
                _hideT = -1f;
                ApplyHide(0f);
                if (!_root.gameObject.activeSelf)
                    _root.gameObject.SetActive(true);
                return;
            }
            if (_root.gameObject.activeSelf && _hideT < 0f)
                _hideT = 0f;
        }

        private void TickHide(float udt)
        {
            if (_hideT < 0f)
                return;
            _hideT = Mathf.Min(1f, _hideT + udt / HideTime);
            ApplyHide(_hideT);
            if (_hideT >= 1f)
            {
                _hideT = -1f;
                _root.gameObject.SetActive(false);
            }
        }

        /// <summary>t＝0 原位、1 往上 HideSlide 並全透明（安全區域的錨點不動，只改上下 offset）。</summary>
        private void ApplyHide(float t)
        {
            var e = t * t;
            _group.alpha = 1f - e;
            var y = HideSlide * e;
            _root.offsetMin = new Vector2(_root.offsetMin.x, y);
            _root.offsetMax = new Vector2(_root.offsetMax.x, y);
        }

        #region 更新（session 事件）
        public void RefreshHp()
        {
            var max = Mathf.Max(0, _session.MaxHp);
            while (_heartImages.Count < max)
            {
                var rt = SsUiKit.Rect($"Heart{_heartImages.Count}", _hearts, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                rt.anchoredPosition = new Vector2(_heartImages.Count * (HeartSize + 6f), 0f);
                rt.sizeDelta = new Vector2(HeartSize, HeartSize);
                _heartImages.Add(SsUiKit.AddImage(rt, Color.white));
            }
            for (var i = 0; i < _heartImages.Count; i++)
            {
                var img = _heartImages[i];
                var on = i < max;
                img.gameObject.SetActive(on);
                if (on)
                    img.sprite = _icons.Variant(i < _session.Hp ? IconHeartFull : IconHeartEmpty);
            }
        }

        public void RefreshLoadout()
        {
            var loadout = _session.Loadout;
            var slot = 0;
            for (var i = 0; i < _attackIcons.Length; i++)
            {
                var has = loadout.HasAttack((AttackId)i);
                _attackIcons[i].gameObject.SetActive(has);
                if (has)
                    _attackIcons[i].rectTransform.anchoredPosition = new Vector2(slot++ * 74f, 0f);
            }
            _costumeIcon.rectTransform.anchoredPosition = new Vector2(slot * 74f + 18f, 0f);
            _plate.sizeDelta = new Vector2(slot * 74f + 18f + 64f + 16f, 64f + 16f);
            _costumeIcon.sprite = _icons.Variant(loadout.Costume == CostumeId.Express ? IconCostumeExpress : IconCostumeDay);
            _switchRoot.gameObject.SetActive(loadout.CostumeCount > 1);
        }

        /// <summary>提示文字；演出中隱藏（StoryUi 每幀經 <see cref="Tick"/> 呼叫）。觸控模式時換成觸控版。</summary>
        public void RefreshHint()
        {
            var text = StoryTexts.ForInput(_session.Hint, SsInput.TouchActive);
            var show = !string.IsNullOrEmpty(text) && !_session.CutsceneActive;
            if (show && _hintText.text != text)
            {
                _hintText.text = _hintShadow.text = text;
                // 底框寬度跟著字長（短提示不要拉一條長條擋畫面）
                var width = Mathf.Clamp(_hintText.GetPreferredValues(text).x + HintPad + 40f, HintMinWidth, HintMaxWidth);
                _hint.sizeDelta = new Vector2(width, HintHeight);
            }
            if (_hint.gameObject.activeSelf != show)
                _hint.gameObject.SetActive(show);
        }

        public void RefreshBoss()
        {
            var show = _session.BossBarVisible && _session.BossMaxHp > 0;
            var appeared = show && !_boss.gameObject.activeSelf;
            _boss.gameObject.SetActive(show);
            if (!show)
                return;
            var name = string.IsNullOrEmpty(_session.BossName) ? SsIds.BossDisplayName : _session.BossName;
            var title = $"{StoryTexts.BossLabel}  {name}";
            foreach (var t in _bossName)
            {
                if (t.text != title)
                    t.text = title;
            }
            var ratio = Mathf.Clamp01((float)_session.BossHp / _session.BossMaxHp);
            // 剛出現、回血或換一隻 Boss：殘影直接跟上；扣血：殘影停一下再縮
            if (appeared || ratio >= _bossTrailRatio)
                _bossTrailRatio = ratio;
            else if (ratio < _bossRatio)
                _bossTrailWait = BossTrailDelay;
            _bossRatio = ratio;
            SetBar(_bossFill, ratio);
            SetBar(_bossTrail, _bossTrailRatio);
            _bossFillImage.color = _session.BossHp * 2 <= _session.BossMaxHp ? SsUiKit.Danger : SsUiKit.Gold;
        }

        /// <summary>白色殘影往目前血量縮（scaled time：暫停時停住）。</summary>
        private void TickBossTrail(float dt)
        {
            if (!_boss.gameObject.activeSelf || _bossTrailRatio <= _bossRatio)
                return;
            if (_bossTrailWait > 0f)
            {
                _bossTrailWait -= dt;
                return;
            }
            _bossTrailRatio = Mathf.Max(_bossRatio, _bossTrailRatio - BossTrailSpeed * dt);
            SetBar(_bossTrail, _bossTrailRatio);
        }

        private static void SetBar(RectTransform bar, float ratio)
        {
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = new Vector2((BossBarWidth - BossBarInset * 2f) * ratio, 0f);
        }
        #endregion

        private Image Icon(Transform parent, float x, int variant)
        {
            var rt = SsUiKit.Rect($"Icon{variant}", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(64f, 64f);
            return SsUiKit.AddImage(rt, Color.white, _icons.Variant(variant));
        }
    }
}
