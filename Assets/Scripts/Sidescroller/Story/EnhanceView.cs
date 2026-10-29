using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 強化介面（loot-design.md §6，補給站開啟）：左邊選裝備（身上 3 件＋背包裡的裝備），右邊顯示目前等級 → 下一級、下一級效果、
    /// 需要零件（目前持有）、大字成功率、失敗後果與「強化」鈕。規則全部呼叫 <see cref="Enhance"/>；按下後 0.6 秒結果演出
    /// （成功金光／失敗灰煙，unscaled time），演出完才顯示結果。由 <see cref="LootScreens"/> 開關與每幀 <see cref="Tick"/>。
    /// 操作：↑↓ 選裝備、J（Space、Enter、手把 South）強化、Esc（手把 Start）關閉；滑鼠／手指點清單、點「強化」「關閉」，清單超出時點 ▲▼ 翻頁。
    /// </summary>
    internal sealed class EnhanceView
    {
        #region 版面常數（1920×1080 畫布單位）
        private const float PanelW = 1500f;
        private const float Pad = 30f;
        private const float ListW = 600f;
        private const float KeyRowH = 84f;
        private const float TouchRowMm = 9f, TouchRowMin = 110f, TouchRowMax = 140f;
        private const float KeyButtonH = 72f;
        private const float RowGap = 8f;
        private const float EffectTime = 0.6f;
        private const int Smokes = 7;
        #endregion

        private static readonly Color PanelColor = new Color(0.06f, 0.04f, 0.1f, 0.95f);
        private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color SuccessColor = new Color32(0xFF, 0xD2, 0x3C, 0xFF);
        private static readonly Color FailColor = new Color(0.72f, 0.72f, 0.75f);

        private readonly LootState _loot;
        private readonly TMP_FontAsset _font;
        private readonly LootPointer _pointer;
        private readonly LootUiArt _art;

        #region 畫面物件
        private readonly RectTransform _root;
        private readonly RectTransform _panel;
        private readonly RectTransform _list;
        private readonly List<LootButton> _rows = new List<LootButton>();
        private readonly List<TextMeshProUGUI> _rowTags = new List<TextMeshProUGUI>();
        private readonly LootButton _pageUp;
        private readonly LootButton _pageDown;
        private readonly TextMeshProUGUI _empty;
        private readonly RectTransform _detail;
        private readonly TextMeshProUGUI _name;
        private readonly TextMeshProUGUI _info;
        private readonly TextMeshProUGUI _rateLabel;
        private readonly TextMeshProUGUI _rate;
        private readonly TextMeshProUGUI _fail;
        private readonly TextMeshProUGUI _result;
        private readonly LootButton _enhance;
        private readonly LootButton _close;
        private readonly TextMeshProUGUI _help;
        private readonly RectTransform _effect;
        private readonly Image _flash;
        private readonly Image _burst;
        private readonly Image[] _smoke = new Image[Smokes];
        #endregion

        #region 狀態
        private readonly List<ItemData> _entries = new List<ItemData>();
        private readonly List<ItemData> _bag = new List<ItemData>();
        private int _selected;
        private int _scroll;
        private int _visibleRows;
        private float _rowH = KeyRowH;
        private Vector2 _layoutSize;
        private bool _layoutTouch;
        private bool _laidOut;
        private bool _dirty = true;
        private int _openedFrame = -1;
        private bool _effectPlaying;
        private float _effectStart;
        private EnhanceResult _pending;
        private ItemData _pendingItem;
        private string _resultText;
        private Color _resultColor;
        #endregion

        public EnhanceView(Transform parent, TMP_FontAsset font, GameSession session, LootPointer pointer, LootUiArt art)
        {
            _loot = session.Loot;
            _font = font;
            _pointer = pointer;
            _art = art;

            _root = SsUiKit.Fill("Enhance", parent);
            SsUiKit.AddImage(_root, new Color(0.02f, 0.01f, 0.05f, 0.55f));
            _panel = SsUiKit.Rect("Panel", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            SsUiKit.AddImage(_panel, PanelColor);

            var title = TopLeftText("Title", _panel, 52f, SsUiKit.Gold);
            title.rectTransform.anchoredPosition = new Vector2(Pad, -Pad + 6f);
            title.rectTransform.sizeDelta = new Vector2(700f, 70f);
            title.text = StoryTexts.EnhanceTitle;

            _list = SsUiKit.Rect("List", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _pageUp = new LootButton("PageUp", _panel, font, "▲", 36f);
            _pageDown = new LootButton("PageDown", _panel, font, "▼", 36f);
            _empty = TopLeftText("Empty", _list, 34f, LabelColor);
            _empty.text = StoryTexts.EnhanceEmpty;

            #region 右邊：詳細
            _detail = SsUiKit.Rect("Detail", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            SsUiKit.AddImage(_detail, new Color(1f, 1f, 1f, 0.04f));
            _name = TopLeftText("Name", _detail, 44f, SsUiKit.Paper);
            _info = TopLeftText("Info", _detail, 32f, SsUiKit.Paper);
            _info.richText = true;
            _info.lineSpacing = 14f;
            _rateLabel = TopLeftText("RateLabel", _detail, 32f, LabelColor);
            _rateLabel.text = StoryTexts.EnhanceRate;
            _rate = TopLeftText("Rate", _detail, 132f, SsUiKit.Gold);
            _fail = TopLeftText("Fail", _detail, 30f, new Color(1f, 0.6f, 0.55f));
            _result = TopLeftText("Result", _detail, 40f, SsUiKit.Gold);
            _enhance = new LootButton("Enhance", _detail, font, StoryTexts.EnhanceButton, 46f);
            #endregion

            _close = new LootButton("Close", _panel, font, StoryTexts.Close, 38f);
            _help = TopLeftText("Help", _panel, 28f, new Color(1f, 1f, 1f, 0.6f));
            _help.textWrappingMode = TextWrappingModes.NoWrap;

            #region 結果演出（金光／灰煙）
            _effect = SsUiKit.Fill("Effect", _detail);
            _flash = SsUiKit.AddImage(_effect, Color.clear);
            var burstRt = SsUiKit.Rect("Burst", _effect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _burst = SsUiKit.AddImage(burstRt, Color.clear, art.Disc);
            _burst.preserveAspect = false;
            for (var i = 0; i < Smokes; i++)
            {
                var rt = SsUiKit.Rect($"Smoke{i}", _effect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                _smoke[i] = SsUiKit.AddImage(rt, Color.clear, art.Disc);
                _smoke[i].preserveAspect = false;
            }
            _effect.gameObject.SetActive(false);
            #endregion

            _loot.Changed += OnLootChanged;
            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public void Dispose() => _loot.Changed -= OnLootChanged;

        private void OnLootChanged() => _dirty = true;

        #region 開關
        public void Open()
        {
            _root.gameObject.SetActive(true);
            _openedFrame = Time.frameCount;
            _selected = 0;
            _scroll = 0;
            _effectPlaying = false;
            _effect.gameObject.SetActive(false);
            _resultText = null;
            _dirty = true;
            Layout(true);
            Rebuild(null);
            Draw();
        }

        /// <summary>收起（演出中被關也沒關係：強化結果在按下時就已經決定並寫進 LootState）。</summary>
        public void Close()
        {
            _effectPlaying = false;
            _effect.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }
        #endregion

        #region 每幀
        /// <summary>每幀呼叫（開著時）；回 false＝要求關閉。</summary>
        public bool Tick()
        {
            Layout(false);
            if (_dirty)
                Rebuild(Current);
            if (_effectPlaying)
            {
                // 演出中不收輸入（0.6 秒）
                TickEffect();
                Draw();
                return true;
            }
            if (Time.frameCount == _openedFrame)
            {
                Draw();
                return true;
            }
            if (SsInput.PausePressed || SsInput.InteractPressed && !SsInput.TouchActive)
                return false;

            if (_pointer.Down)
            {
                var pos = _pointer.DownPos;
                if (_close.Contains(pos))
                    return false;
                if (_enhance.Contains(pos))
                {
                    TryEnhance();
                }
                else if (_pageUp.Contains(pos))
                {
                    Scroll(-_visibleRows);
                }
                else if (_pageDown.Contains(pos))
                {
                    Scroll(_visibleRows);
                }
                else
                {
                    for (var i = 0; i < _rows.Count; i++)
                    {
                        if (_rows[i].Visible && _rows[i].Contains(pos, RowGap / 2f))
                        {
                            Choose(_scroll + i);
                            break;
                        }
                    }
                }
            }
            if (SsInput.MenuUpPressed)
                Choose(_selected - 1);
            else if (SsInput.MenuDownPressed)
                Choose(_selected + 1);
            if (SsInput.ConfirmPressed)
                TryEnhance();
            Draw();
            return true;
        }

        private ItemData Current => _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null;

        private void Select(int index)
        {
            if (_entries.Count == 0)
                return;
            _selected = Mathf.Clamp(index, 0, _entries.Count - 1);
            if (_selected < _scroll)
                _scroll = _selected;
            else if (_selected >= _scroll + _visibleRows)
                _scroll = _selected - _visibleRows + 1;
        }

        /// <summary>玩家換選（清掉上一次的結果文字）。</summary>
        private void Choose(int index)
        {
            var before = _selected;
            Select(index);
            if (_selected != before)
                _resultText = null;
        }

        private void Scroll(int delta)
        {
            var max = Mathf.Max(0, _entries.Count - _visibleRows);
            _scroll = Mathf.Clamp(_scroll + delta, 0, max);
            _selected = Mathf.Clamp(_selected, _scroll, Mathf.Min(_entries.Count - 1, _scroll + _visibleRows - 1));
        }
        #endregion

        #region 強化
        private void TryEnhance()
        {
            var item = Current;
            if (item == null || item.Level >= Enhance.MaxLevel)
                return;
            var level = item.Level;
            if (_loot.PartsTotal < Enhance.Cost(level))
            {
                _resultText = StoryTexts.EnhanceNoParts;
                _resultColor = SsUiKit.Danger;
                return;
            }
            var result = Enhance.TryEnhance(_loot, item);
            if (result == EnhanceResult.NotEnoughParts || result == EnhanceResult.NotAllowed)
            {
                _resultText = result == EnhanceResult.NotEnoughParts ? StoryTexts.EnhanceNoParts : StoryTexts.EnhanceMax;
                _resultColor = SsUiKit.Danger;
                return;
            }
            Debug.Log($"[Sidescroller.EnhanceView] 強化 {item.Name} +{level} → {result}（現在 +{item.Level}）");
            _pending = result;
            _pendingItem = item;
            _resultText = null;
            _effectPlaying = true;
            _effectStart = Time.unscaledTime;
            _effect.gameObject.SetActive(true);
            TickEffect();
        }

        /// <summary>0.6 秒演出（unscaled）：成功＝金色閃光＋擴散的光圈；失敗＝灰煙往上飄散。結束後顯示結果文字。</summary>
        private void TickEffect()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _effectStart) / EffectTime);
            var size = _detail.rect.size;
            var success = _pending == EnhanceResult.Success;
            if (success)
            {
                _flash.color = new Color(SuccessColor.r, SuccessColor.g, SuccessColor.b, 0.45f * (1f - t) * Mathf.Clamp01(t * 6f));
                var d = Mathf.Lerp(0.15f, 1.3f, 1f - (1f - t) * (1f - t)) * Mathf.Min(size.x, size.y);
                _burst.rectTransform.sizeDelta = new Vector2(d, d);
                _burst.color = new Color(1f, 0.92f, 0.55f, 0.95f * (1f - t));
                for (var i = 0; i < Smokes; i++)
                    _smoke[i].color = Color.clear;
            }
            else
            {
                _flash.color = new Color(0.15f, 0.15f, 0.17f, 0.4f * Mathf.Sin(t * Mathf.PI));
                _burst.color = Color.clear;
                for (var i = 0; i < Smokes; i++)
                {
                    // 每團煙從中下方不同位置冒出、往上飄並變大變淡
                    var u = (i + 0.5f) / Smokes;
                    var x = (u - 0.5f) * size.x * 0.55f + Mathf.Sin(i * 2.3f + t * 3f) * 24f;
                    var y = -size.y * 0.1f + t * size.y * (0.25f + 0.1f * (i % 3));
                    var d = (90f + 40f * (i % 3)) * (0.6f + t);
                    var rt = _smoke[i].rectTransform;
                    rt.anchoredPosition = new Vector2(x, y);
                    rt.sizeDelta = new Vector2(d, d);
                    var a = Mathf.Clamp01(t * 5f) * (1f - t) * 0.85f;
                    _smoke[i].color = new Color(FailColor.r, FailColor.g, FailColor.b, a);
                }
            }
            if (t < 1f)
                return;

            _effectPlaying = false;
            _effect.gameObject.SetActive(false);
            switch (_pending)
            {
                case EnhanceResult.Success:
                    _resultText = StoryTexts.EnhanceSuccess + "　" + _pendingItem.DisplayName;
                    _resultColor = SuccessColor;
                    break;
                case EnhanceResult.FailDowngrade:
                    _resultText = StoryTexts.EnhanceFailDown + _pendingItem.Level;
                    _resultColor = FailColor;
                    break;
                default:
                    _resultText = StoryTexts.EnhanceFail;
                    _resultColor = FailColor;
                    break;
            }
            _dirty = true;
        }
        #endregion

        #region 清單
        /// <summary>重建清單：身上 3 件（近戰、遠程、殼甲順序）＋格子盤上的裝備（由上到下、由左到右）。keep：盡量保持選到同一件。</summary>
        private void Rebuild(ItemData keep)
        {
            _dirty = false;
            _entries.Clear();
            for (var i = 0; i < 3; i++)
            {
                var e = _loot.GetEquipped((EquipSlot)i);
                if (e != null)
                    _entries.Add(e);
            }
            _bag.Clear();
            _loot.GetItems(_bag);
            _bag.RemoveAll(IsNotEquipment);
            _bag.Sort(CompareOrigin);
            _entries.AddRange(_bag);
            var index = keep != null ? _entries.IndexOf(keep) : -1;
            Select(index >= 0 ? index : Mathf.Min(_selected, _entries.Count - 1));
            if (_entries.Count == 0)
                _selected = _scroll = 0;
            Scroll(0);
        }

        private static bool IsNotEquipment(ItemData item) => item == null || !item.IsEquipment;

        private int CompareOrigin(ItemData a, ItemData b)
        {
            _loot.TryGetOrigin(a, out var ax, out var ay);
            _loot.TryGetOrigin(b, out var bx, out var by);
            return ay != by ? ay.CompareTo(by) : ax.CompareTo(bx);
        }
        #endregion

        #region 版面與畫面
        private void Layout(bool force)
        {
            SsUiKit.FitSafeArea(_root);
            var size = _root.rect.size;
            var touch = SsInput.TouchActive;
            if (!force && _laidOut && size == _layoutSize && touch == _layoutTouch)
                return;
            _laidOut = true;
            _layoutSize = size;
            _layoutTouch = touch;

            _rowH = touch ? Mathf.Clamp(Mathf.Max(SsUiKit.MmToUnits(TouchRowMm), TouchRowMin), TouchRowMin, TouchRowMax) : KeyRowH;
            var buttonH = touch ? _rowH : KeyButtonH;
            var panelW = Mathf.Min(PanelW, size.x - 24f);
            var panelH = Mathf.Min(980f, size.y - 16f);
            _panel.sizeDelta = new Vector2(panelW, panelH);

            var titleH = Mathf.Max(76f, buttonH + 8f);
            var top = -(Pad + titleH);
            var helpH = 48f;
            var bodyH = panelH - Pad * 2f - titleH - helpH - 8f;
            var listW = Mathf.Min(ListW, panelW * 0.42f);
            var pageW = touch ? _rowH : 72f;

            // 清單：右側留一欄放 ▲▼
            _list.anchoredPosition = new Vector2(Pad, top);
            _list.sizeDelta = new Vector2(listW - pageW - 10f, bodyH);
            _visibleRows = Mathf.Max(1, Mathf.FloorToInt((bodyH + RowGap) / (_rowH + RowGap)));
            for (var i = _rows.Count; i < _visibleRows; i++)
            {
                var row = new LootButton($"Row{i}", _list, _font, string.Empty, 34f);
                row.Label.alignment = TextAlignmentOptions.Left;
                row.Label.rectTransform.offsetMin = new Vector2(20f, 0f);
                row.Label.rectTransform.offsetMax = new Vector2(-140f, 0f);
                row.Label.overflowMode = TextOverflowModes.Ellipsis;
                var tagRt = SsUiKit.Fill("Tag", row.Rt);
                tagRt.offsetMax = new Vector2(-16f, 0f);
                var tag = SsUiKit.AddText(tagRt, _font, 26f, LabelColor, TextAlignmentOptions.Right);
                tag.text = StoryTexts.EnhanceEquipped;
                _rows.Add(row);
                _rowTags.Add(tag);
            }
            for (var i = 0; i < _rows.Count; i++)
                _rows[i].Place(new Vector2(0f, -i * (_rowH + RowGap)), new Vector2(_list.sizeDelta.x, _rowH));
            _empty.rectTransform.anchoredPosition = Vector2.zero;
            _empty.rectTransform.sizeDelta = new Vector2(_list.sizeDelta.x, 120f);
            _pageUp.Place(new Vector2(Pad + listW - pageW, top), new Vector2(pageW, pageW));
            _pageDown.Place(new Vector2(Pad + listW - pageW, top - bodyH + pageW), new Vector2(pageW, pageW));

            // 右邊詳細
            var dx = Pad + listW + 24f;
            var dw = panelW - dx - Pad;
            _detail.anchoredPosition = new Vector2(dx, top);
            _detail.sizeDelta = new Vector2(dw, bodyH);
            var inner = dw - 64f;
            _name.rectTransform.anchoredPosition = new Vector2(32f, -24f);
            _name.rectTransform.sizeDelta = new Vector2(inner, 60f);
            _info.rectTransform.anchoredPosition = new Vector2(32f, -96f);
            _info.rectTransform.sizeDelta = new Vector2(inner, 170f);
            // 由上往下：名稱、資訊、成功率標籤、成功率大字、失敗後果；最下面一列是結果文字（左）與「強化」鈕（右）。
            // 高度不夠（觸控模式的手機）時只縮成功率大字，其他不重疊
            var enhanceW = Mathf.Min(420f, inner * 0.5f);
            var enhanceH = Mathf.Max(buttonH, 96f);
            var buttonTop = -bodyH + 24f + enhanceH;
            var rateTop = -96f - 170f - 6f;
            const float failH = 44f;
            var rateH = Mathf.Clamp(rateTop - 40f - (buttonTop + 12f + failH), 60f, 150f);
            _rateLabel.rectTransform.anchoredPosition = new Vector2(32f, rateTop);
            _rateLabel.rectTransform.sizeDelta = new Vector2(300f, 44f);
            _rate.rectTransform.anchoredPosition = new Vector2(32f, rateTop - 40f);
            _rate.rectTransform.sizeDelta = new Vector2(inner * 0.5f, rateH);
            _rate.fontSize = Mathf.Min(132f, rateH * 0.88f);
            _fail.rectTransform.anchoredPosition = new Vector2(32f, rateTop - 40f - rateH);
            _fail.rectTransform.sizeDelta = new Vector2(inner, failH);
            _enhance.Place(new Vector2(dw - 32f - enhanceW, buttonTop), new Vector2(enhanceW, enhanceH));
            _result.rectTransform.anchoredPosition = new Vector2(32f, buttonTop);
            _result.rectTransform.sizeDelta = new Vector2(inner - enhanceW - 24f, enhanceH);
            _result.alignment = TextAlignmentOptions.Left;
            _result.enableAutoSizing = true;
            _result.fontSizeMax = 40f;
            _result.fontSizeMin = 24f;

            _close.Place(new Vector2(panelW - Pad - 200f, -Pad + 4f), new Vector2(200f, buttonH));
            _help.rectTransform.anchoredPosition = new Vector2(Pad, -panelH + Pad + helpH);
            _help.rectTransform.sizeDelta = new Vector2(panelW - Pad * 2f, helpH);
            _help.text = touch ? StoryTexts.EnhanceHelpTouch : StoryTexts.EnhanceHelp;
            Scroll(0);
        }

        private void Draw()
        {
            #region 清單
            _empty.gameObject.SetActive(_entries.Count == 0);
            for (var i = 0; i < _rows.Count; i++)
            {
                var index = _scroll + i;
                var row = _rows[i];
                var visible = i < _visibleRows && index < _entries.Count;
                row.Visible = visible;
                if (!visible)
                    continue;
                var item = _entries[index];
                var selected = index == _selected;
                var bg = selected ? SsUiKit.Magenta : LootButton.IdleColor;
                if (row.Bg.color != bg)
                    row.Bg.color = bg;
                var fg = selected ? Color.white : ItemData.RarityColor(item.Rarity);
                if (row.Label.color != fg)
                    row.Label.color = fg;
                if (row.Label.text != item.DisplayName)
                    row.Label.text = item.DisplayName;
                _rowTags[i].gameObject.SetActive(_loot.GetEquipped(item.Slot) == item);
            }
            var overflow = _entries.Count > _visibleRows;
            _pageUp.Visible = overflow;
            _pageDown.Visible = overflow;
            if (overflow)
            {
                _pageUp.SetState(false, _scroll > 0);
                _pageDown.SetState(false, _scroll + _visibleRows < _entries.Count);
            }
            #endregion

            #region 詳細
            var cur = Current;
            _detail.gameObject.SetActive(cur != null);
            _close.SetState(false);
            if (cur == null)
                return;
            _name.text = cur.DisplayName;
            _name.color = ItemData.RarityColor(cur.Rarity);
            var level = cur.Level;
            var max = level >= Enhance.MaxLevel;
            var cost = max ? 0 : Enhance.Cost(level);
            var owned = _loot.PartsTotal;
            var enough = !max && owned >= cost;
            if (max)
            {
                _info.text = $"<color=#A9A3B8>{StoryTexts.EnhanceLevel}</color>　+{level}\n{StoryTexts.EnhanceMax}";
                _rate.text = "—";
                _fail.text = string.Empty;
            }
            else
            {
                var effect = Enhance.NextLevelEffect(cur) ?? string.Empty;
                var costColor = enough ? "FFF7E6" : "F2564F";
                _info.text =
                    $"<color=#A9A3B8>{StoryTexts.EnhanceLevel}</color>　+{level} → +{level + 1}\n" +
                    $"<color=#A9A3B8>{StoryTexts.EnhanceNext}</color>　{effect}\n" +
                    $"<color=#A9A3B8>{StoryTexts.EnhanceCost}</color>　<color=#{costColor}>{cost}</color>{StoryTexts.EnhanceOwnedPrefix}{owned}{StoryTexts.EnhanceOwnedSuffix}";
                _rate.text = Enhance.SuccessPercent(level) + "%";
                _fail.text = Enhance.FailureText(level);
            }
            _enhance.SetState(true, enough);
            _enhance.Label.text = max ? StoryTexts.EnhanceMax : enough ? StoryTexts.EnhanceButton : StoryTexts.EnhanceNoParts;
            _enhance.Label.enableAutoSizing = true;
            _enhance.Label.fontSizeMax = 46f;
            _enhance.Label.fontSizeMin = 28f;
            _result.gameObject.SetActive(_resultText != null && !_effectPlaying);
            if (_resultText != null)
            {
                _result.text = _resultText;
                _result.color = _resultColor;
            }
            #endregion
        }

        private TextMeshProUGUI TopLeftText(string name, Transform parent, float size, Color color)
        {
            var rt = SsUiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            return SsUiKit.AddText(rt, _font, size, color, TextAlignmentOptions.TopLeft);
        }
        #endregion
    }
}
