using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 程式建 uGUI 的小工具（無狀態）。全部 Screen Space Overlay、以 1920×1080 為基準縮放；
    /// 不加 GraphicRaycaster、不建 EventSystem，每個 Graphic 都 raycastTarget = false，滑鼠與觸控點擊由 <see cref="MenuList"/>、
    /// <see cref="TouchControlsView"/> 自己判斷。
    /// </summary>
    public static class SsUiKit
    {
        #region 配色（design.md §8.6）
        public static readonly Color Magenta = new Color32(0xE0, 0x45, 0x7B, 0xFF);
        public static readonly Color Orange = new Color32(0xFF, 0x8C, 0x42, 0xFF);
        public static readonly Color Dusk = new Color32(0x5B, 0x3A, 0x7A, 0xFF);
        public static readonly Color SkyBlue = new Color32(0x2E, 0x4A, 0x7D, 0xFF);
        public static readonly Color Neon = new Color32(0x2F, 0xE6, 0xD8, 0xFF);
        public static readonly Color Gold = new Color32(0xFF, 0xC8, 0x3A, 0xFF);
        public static readonly Color Ink = new Color(0.07f, 0.05f, 0.12f, 0.92f);
        public static readonly Color Paper = new Color(1f, 0.97f, 0.9f);
        public static readonly Color Danger = new Color(0.95f, 0.22f, 0.2f);
        #endregion

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>空的 RectTransform；anchorMin/Max 與 pivot 一起設，位置與大小另設。</summary>
        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        /// <summary>撐滿 parent 的 RectTransform。</summary>
        public static RectTransform Fill(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image AddImage(RectTransform rt, Color color, Sprite sprite = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = sprite != null;
            return img;
        }

        public static TextMeshProUGUI AddText(RectTransform rt, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
                t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = false;
            t.text = string.Empty;
            return t;
        }

        /// <summary>
        /// 這一幀點擊的螢幕座標：觸控按在觸控按鈕以外的地方，或滑鼠左鍵按下（剛觸控過的那段時間不算，避免觸控被模擬成滑鼠後點兩次）。
        /// 只用按鈕的 wasPressedThisFrame 與 AxisControl（float）讀位置，不呼叫 <c>ReadValue&lt;Vector2&gt;</c>（interfaces.md §8）。
        /// </summary>
        public static bool PointerClicked(out Vector2 screenPos)
        {
            if (SsTouch.Tapped(out screenPos))
                return true;
            screenPos = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || SsTouch.MouseSuppressed)
                return false;
            screenPos = new Vector2(mouse.position.x.ReadValue(), mouse.position.y.ReadValue());
            return true;
        }

        /// <summary>畫布單位換成螢幕像素的比例（與 <see cref="CreateCanvas"/> 的 CanvasScaler 相同：1920×1080、match 0.5＝寬高比例的幾何平均）。</summary>
        public static float UnitsToPx() => Mathf.Sqrt(Mathf.Max(1f, Screen.width) / 1920f * (Mathf.Max(1f, Screen.height) / 1080f));

        /// <summary>實體長度（mm）換成畫布單位；<see cref="Screen.dpi"/> 不可信（不在 50～1000）時回傳 0，由呼叫端改用設計尺寸。</summary>
        public static float MmToUnits(float mm)
        {
            var dpi = Screen.dpi;
            if (dpi < 50f || dpi > 1000f)
                return 0f;
            return mm / 25.4f * dpi / UnitsToPx();
        }

        /// <summary>
        /// 把撐滿畫布的 rt 縮到 <see cref="Screen.safeArea"/>（避開瀏海、圓角、Home 指示條）。每幀呼叫即可：
        /// 旋轉或改解析度後自動跟上，沒變時不動 RectTransform。
        /// </summary>
        public static void FitSafeArea(RectTransform rt)
        {
            var w = Screen.width;
            var h = Screen.height;
            if (rt == null || w <= 0 || h <= 0)
                return;
            var safe = Screen.safeArea;
            var min = new Vector2(Mathf.Clamp01(safe.xMin / w), Mathf.Clamp01(safe.yMin / h));
            var max = new Vector2(Mathf.Clamp01(safe.xMax / w), Mathf.Clamp01(safe.yMax / h));
            if (max.x <= min.x || max.y <= min.y)
            {
                min = Vector2.zero;
                max = Vector2.one;
            }
            if (rt.anchorMin == min && rt.anchorMax == max)
                return;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>mm:ss（超過 99 分鐘照樣累加分鐘）。</summary>
        public static string FormatTime(float seconds)
        {
            var total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
