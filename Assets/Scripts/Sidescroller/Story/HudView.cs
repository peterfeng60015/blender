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

        private readonly GameSession _session;
        private readonly SpriteSet _icons;
        private readonly RectTransform _root;
        private readonly RectTransform _hearts;
        private readonly List<Image> _heartImages = new List<Image>();
        private readonly Image[] _attackIcons = new Image[3];
        private readonly Image _costumeIcon;
        private readonly RectTransform _plate;
        private readonly TextMeshProUGUI _switchHint;
        private readonly RectTransform _hint;
        private readonly TextMeshProUGUI _hintText;
        private readonly RectTransform _boss;
        private readonly TextMeshProUGUI _bossName;
        private readonly RectTransform _bossFill;
        private readonly Image _bossFillImage;

        public HudView(Transform parent, TMP_FontAsset font, GameSession session)
        {
            _session = session;
            _icons = SsArt.Get(UiLocation);
            _root = SsUiKit.Fill("Hud", parent);
            SsUiKit.FitSafeArea(_root);

            #region 左上：心、攻擊、服裝
            _hearts = SsUiKit.Rect("Hearts", _root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _hearts.anchoredPosition = new Vector2(40f, -32f);

            var loadout = SsUiKit.Rect("Loadout", _root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            loadout.anchoredPosition = new Vector2(40f, -32f - HeartSize - 14f);
            _plate = SsUiKit.Rect("Plate", loadout, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _plate.anchoredPosition = new Vector2(-8f, 8f);
            SsUiKit.AddImage(_plate, new Color(0f, 0f, 0f, 0.35f));
            for (var i = 0; i < 3; i++)
                _attackIcons[i] = Icon(loadout, i * 74f, IconMelee + i);
            _costumeIcon = Icon(loadout, 3 * 74f, IconCostumeDay);

            var switchRt = SsUiKit.Rect("SwitchHint", loadout, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            switchRt.anchoredPosition = new Vector2(0f, -84f);
            switchRt.sizeDelta = new Vector2(320f, 36f);
            _switchHint = SsUiKit.AddText(switchRt, font, 26f, SsUiKit.Paper, TextAlignmentOptions.Left);
            _switchHint.text = StoryTexts.SwitchCostume;
            #endregion

            #region 上方中央：提示
            _hint = SsUiKit.Rect("Hint", _root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _hint.anchoredPosition = new Vector2(0f, -36f);
            _hint.sizeDelta = new Vector2(1000f, 72f);
            SsUiKit.AddImage(_hint, new Color(0.05f, 0.03f, 0.1f, 0.7f));
            var hintTextRt = SsUiKit.Fill("Text", _hint);
            hintTextRt.offsetMin = new Vector2(24f, 0f);
            hintTextRt.offsetMax = new Vector2(-24f, 0f);
            _hintText = SsUiKit.AddText(hintTextRt, font, 34f, SsUiKit.Paper);
            _hint.gameObject.SetActive(false);
            #endregion

            #region 下方中央：Boss 血條
            _boss = SsUiKit.Rect("BossBar", _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _boss.anchoredPosition = new Vector2(0f, 48f);
            _boss.sizeDelta = new Vector2(BossBarWidth + 120f, 96f);
            var bossIcon = SsUiKit.Rect("Icon", _boss, Vector2.zero, Vector2.zero, Vector2.zero);
            bossIcon.sizeDelta = new Vector2(96f, 96f);
            SsUiKit.AddImage(bossIcon, Color.white, _icons.Variant(IconBoss));
            var label = SsUiKit.Rect("Label", _boss, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            label.anchoredPosition = new Vector2(112f, 0f);
            label.sizeDelta = new Vector2(BossBarWidth, 44f);
            _bossName = SsUiKit.AddText(label, font, 32f, SsUiKit.Gold, TextAlignmentOptions.BottomLeft);
            var frame = SsUiKit.Rect("Frame", _boss, Vector2.zero, Vector2.zero, Vector2.zero);
            frame.anchoredPosition = new Vector2(112f, 8f);
            frame.sizeDelta = new Vector2(BossBarWidth, 36f);
            SsUiKit.AddImage(frame, new Color(0.05f, 0.03f, 0.1f, 0.9f));
            _bossFill = SsUiKit.Rect("Fill", frame, Vector2.zero, new Vector2(0f, 1f), Vector2.zero);
            _bossFill.offsetMin = new Vector2(4f, 4f);
            _bossFill.offsetMax = new Vector2(4f, -4f);
            _bossFillImage = SsUiKit.AddImage(_bossFill, SsUiKit.Gold);
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
                _switchHint.text = switchText;
            RefreshHint();
        }

        public void SetVisible(bool visible)
        {
            if (_root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);
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
            _switchHint.gameObject.SetActive(loadout.CostumeCount > 1);
        }

        /// <summary>提示文字；演出中隱藏（StoryUi 每幀經 <see cref="Tick"/> 呼叫）。觸控模式時換成觸控版。</summary>
        public void RefreshHint()
        {
            var text = StoryTexts.ForInput(_session.Hint, SsInput.TouchActive);
            var show = !string.IsNullOrEmpty(text) && !_session.CutsceneActive;
            if (show && _hintText.text != text)
                _hintText.text = text;
            if (_hint.gameObject.activeSelf != show)
                _hint.gameObject.SetActive(show);
        }

        public void RefreshBoss()
        {
            var show = _session.BossBarVisible && _session.BossMaxHp > 0;
            _boss.gameObject.SetActive(show);
            if (!show)
                return;
            var name = string.IsNullOrEmpty(_session.BossName) ? SsIds.BossDisplayName : _session.BossName;
            _bossName.text = $"{StoryTexts.BossLabel}  {name}";
            var ratio = Mathf.Clamp01((float)_session.BossHp / _session.BossMaxHp);
            _bossFill.offsetMax = new Vector2(4f + (BossBarWidth - 8f) * ratio, -4f);
            _bossFillImage.color = _session.BossHp * 2 <= _session.BossMaxHp ? SsUiKit.Danger : SsUiKit.Gold;
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
