using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 直式選項清單（遊戲選擇選單、暫停選單、通關畫面共用）。鍵盤讀 <see cref="SsInput"/> 的 MenuUp／MenuDown／Confirm，
    /// 滑鼠用 <see cref="RectTransformUtility.RectangleContainsScreenPoint(RectTransform, Vector2, Camera)"/> 自己判斷（不建 EventSystem）。
    /// <see cref="Open"/> 的那一幀不處理輸入（interfaces.md §7.3），從下一幀開始。
    /// </summary>
    public sealed class MenuList
    {
        private const float ItemHeight = 84f;
        private const float ItemWidth = 560f;

        private readonly RectTransform _root;
        private readonly TMP_FontAsset _font;
        private readonly List<RectTransform> _items = new List<RectTransform>();
        private readonly List<Image> _bars = new List<Image>();
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private int _openedFrame = -1;

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
            Refresh();
        }

        /// <summary>每幀呼叫一次；選定時回傳選項序號，否則 -1。</summary>
        public int Tick()
        {
            if (Time.frameCount == _openedFrame || Count == 0)
                return -1;
            var count = ActiveCount();
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
            if (SsUiKit.MouseClicked(out var pos))
            {
                for (var i = 0; i < count; i++)
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(_items[i], pos, null))
                    {
                        Selected = i;
                        Refresh();
                        return i;
                    }
                }
            }
            return -1;
        }

        #region 內部
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
            item.anchoredPosition = new Vector2(0f, -i * (ItemHeight + 12f));
            item.sizeDelta = new Vector2(ItemWidth, ItemHeight);
            var bar = SsUiKit.AddImage(item, Color.clear);
            var labelRt = SsUiKit.Fill("Label", item);
            var label = SsUiKit.AddText(labelRt, _font, 46f, SsUiKit.Paper);
            _items.Add(item);
            _bars.Add(bar);
            _labels.Add(label);
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
