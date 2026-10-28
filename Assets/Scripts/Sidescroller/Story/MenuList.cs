using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 直式選項清單（遊戲選擇選單、暫停選單、通關畫面共用）。鍵盤讀 <see cref="SsInput"/> 的 MenuUp／MenuDown／Confirm，
    /// 滑鼠與觸控點擊用 <see cref="SsUiKit.PointerClicked"/> 讀位置、自己判斷點到哪一項（不建 EventSystem）。
    /// 觸控模式時項目加高到約 9mm（鍵盤／手把版面不變）。
    /// <see cref="Open"/> 的那一幀不處理輸入（interfaces.md §7.3），從下一幀開始。
    /// </summary>
    public sealed class MenuList
    {
        private const float ItemHeight = 84f;
        private const float ItemWidth = 560f;
        private const float ItemGap = 12f;
        /// <summary>觸控模式的項目高度：約 9mm，最少 120（dpi 不可信或偏低時）；上限 140，暫停選單 4 項在 19.5:9 手機上還放得下。</summary>
        private const float TouchItemMm = 9f;
        private const float TouchItemFallback = 120f;
        private const float TouchItemMax = 140f;

        private readonly RectTransform _root;
        private readonly TMP_FontAsset _font;
        private readonly List<RectTransform> _items = new List<RectTransform>();
        private readonly List<Image> _bars = new List<Image>();
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private int _openedFrame = -1;
        private float _itemHeight = ItemHeight;

        public int Selected { get; private set; }
        public int Count => _items.Count;
        public RectTransform Root => _root;

        /// <summary>清單的中心放在 parent 的 (0.5, anchorY)，再往下偏 offsetY。</summary>
        public MenuList(Transform parent, TMP_FontAsset font, float anchorY, float offsetY)
        {
            _font = font;
            _root = SsUiKit.Rect("Items", parent, new Vector2(0.5f, anchorY), new Vector2(0.5f, anchorY), new Vector2(0.5f, 1f));
            _root.anchoredPosition = new Vector2(0f, offsetY);
            _root.sizeDelta = new Vector2(ItemWidth, 0f);
        }

        /// <summary>重設選項並從下一幀開始接受輸入。</summary>
        public void Open(IList<string> labels, int selected = 0)
        {
            for (var i = 0; i < labels.Count; i++)
            {
                if (i >= _items.Count)
                    AddItem();
                _items[i].gameObject.SetActive(true);
                _labels[i].text = labels[i];
            }
            for (var i = labels.Count; i < _items.Count; i++)
                _items[i].gameObject.SetActive(false);
            Selected = Mathf.Clamp(selected, 0, Mathf.Max(0, labels.Count - 1));
            _openedFrame = Time.frameCount;
            UpdateItemHeight();
            Refresh();
        }

        /// <summary>每幀呼叫一次；選定時回傳選項序號，否則 -1。</summary>
        public int Tick()
        {
            if (Time.frameCount == _openedFrame || Count == 0)
            {
                UpdateItemHeight();
                return -1;
            }
            var count = ActiveCount();
            // 點擊用這一幀重排前的版面判定（玩家看到的位置）：這一下可能剛把觸控模式打開，版面接著要加高
            var clicked = SsUiKit.PointerClicked(out var pos) ? HitIndex(pos, count) : -1;
            UpdateItemHeight();
            if (count == 0)
                return -1;
            if (SsInput.MenuUpPressed)
            {
                Selected = (Selected + count - 1) % count;
                Refresh();
            }
            else if (SsInput.MenuDownPressed)
            {
                Selected = (Selected + 1) % count;
                Refresh();
            }
            if (SsInput.ConfirmPressed)
                return Selected;
            if (clicked >= 0)
            {
                Selected = clicked;
                Refresh();
                return clicked;
            }
            return -1;
        }

        #region 內部
        /// <summary>
        /// 點到前 count 項的哪一項（-1＝都沒點到）：上下各多算半個間隔，項目之間不留點不到的縫（手指點比較寬鬆）。
        /// </summary>
        private int HitIndex(Vector2 screenPos, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_items[i], screenPos, null, out var local))
                    continue;
                var r = _items[i].rect;
                r.yMin -= ItemGap / 2f;
                r.yMax += ItemGap / 2f;
                if (r.Contains(local))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 觸控模式時項目加高到約 9mm，最少 <see cref="TouchItemFallback"/>（WebGL 等 dpi 不準時的保底）；
        /// 鍵盤／手把版面不變。輸入裝置或解析度變了才重排。
        /// </summary>
        private void UpdateItemHeight()
        {
            var h = ItemHeight;
            if (SsInput.TouchActive)
                h = Mathf.Clamp(Mathf.Max(SsUiKit.MmToUnits(TouchItemMm), TouchItemFallback), ItemHeight, TouchItemMax);
            if (Mathf.Approximately(h, _itemHeight))
                return;
            _itemHeight = h;
            for (var i = 0; i < _items.Count; i++)
                PlaceItem(i);
        }

        private void PlaceItem(int i)
        {
            _items[i].anchoredPosition = new Vector2(0f, -i * (_itemHeight + ItemGap));
            _items[i].sizeDelta = new Vector2(ItemWidth, _itemHeight);
        }

        private int ActiveCount()
        {
            var n = 0;
            while (n < _items.Count && _items[n].gameObject.activeSelf)
                n++;
            return n;
        }

        private void AddItem()
        {
            var i = _items.Count;
            var item = SsUiKit.Rect($"Item{i}", _root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            var bar = SsUiKit.AddImage(item, Color.clear);
            var labelRt = SsUiKit.Fill("Label", item);
            var label = SsUiKit.AddText(labelRt, _font, 46f, SsUiKit.Paper);
            _items.Add(item);
            _bars.Add(bar);
            _labels.Add(label);
            PlaceItem(i);
        }

        private void Refresh()
        {
            for (var i = 0; i < _items.Count; i++)
            {
                var on = i == Selected;
                _bars[i].color = on ? SsUiKit.Magenta : new Color(1f, 1f, 1f, 0.06f);
                _labels[i].color = on ? Color.white : new Color(1f, 1f, 1f, 0.7f);
                            }
        }
        #endregion
    }
}
