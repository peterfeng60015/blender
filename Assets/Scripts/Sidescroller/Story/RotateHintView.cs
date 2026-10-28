using TMPro;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 直向提示（所有平台通用，Screen.width &lt; Screen.height 就算直向）：蓋住整個畫面的「請把手機轉成橫向」。
    /// 本身只管顯示；暫停由呼叫端處理（<see cref="StoryUi"/> 走 GameSession.SetPaused，選遊戲選單只停止接受輸入）。
    /// 要建在畫布最後（最上層）。
    /// </summary>
    public sealed class RotateHintView
    {
        private readonly RectTransform _root;

        public bool Visible => _root != null && _root.gameObject.activeSelf;

        public static bool IsPortrait => Screen.width < Screen.height;

        /// <summary>sub：標題下的小字（null＝不顯示）。</summary>
        public RotateHintView(Transform parent, TMP_FontAsset font, string sub)
        {
            _root = SsUiKit.Fill("RotateHint", parent);
            SsUiKit.AddImage(_root, new Color(0.04f, 0.02f, 0.08f, 0.96f));
            var title = SsUiKit.Rect("Title", _root, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f));
            title.offsetMin = new Vector2(48f, 10f);
            title.offsetMax = new Vector2(-48f, 140f);
            var t = SsUiKit.AddText(title, font, 72f, SsUiKit.Gold);
            t.text = StoryTexts.RotateHint;
            if (sub != null)
            {
                var subRt = SsUiKit.Rect("Sub", _root, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 1f));
                subRt.offsetMin = new Vector2(48f, -110f);
                subRt.offsetMax = new Vector2(-48f, -10f);
                var s = SsUiKit.AddText(subRt, font, 40f, SsUiKit.Paper);
                s.text = sub;
            }
            _root.gameObject.SetActive(false);
        }

        /// <summary>依目前方向顯示或收起；回傳是否直向。</summary>
        public bool Refresh()
        {
            var portrait = IsPortrait;
            if (_root != null && _root.gameObject.activeSelf != portrait)
                _root.gameObject.SetActive(portrait);
            return portrait;
        }
    }
}
