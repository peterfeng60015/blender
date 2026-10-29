using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// HUD 右上的背包圖示與拾取回饋（pickup 實作）：抵達時圖示跳一下並顯示「+3 零件」或裝備名稱（稀有度顏色）、
    /// 背包滿時閃紅並顯示「背包已滿」。拾取飛行的道具也畫在這一層。由 <see cref="StoryUi"/> 建立並每幀呼叫 <see cref="Tick"/>。
    /// 飛行用 unscaled time；暫停（開背包、暫停選單、直向提示）、重生（地上那條命的 worldRoot 被刪）、StoryUi 被刪時，
    /// 飛行中的預留一律立刻 Commit（loot-design.md §4-4；RULES 介面 3）。
    /// 位置：鍵盤模式在右上角；觸控模式在暫停鈕正下方（換裝鈕在暫停鈕左邊、左上是血量、上方中央是提示文字）。
    /// </summary>
    public sealed class LootHudView
    {
        #region 常數
        private const string Log = "[Sidescroller.LootHudView]";
        public const string TextFull = "背包已滿";
        public const string TextPartsFormat = "+{0} {1}";

        private const float BagSize = 104f;
        private const float KeyboardEdgeX = 40f, KeyboardEdgeY = 32f;
        // 與 TouchControlsView 的暫停鈕版面相同（右上角的小鈕）；改那邊時要一起改
        private const float TouchSmallSize = 120f, TouchMaxSmallSize = 190f, TouchMinButtonMm = 9f;
        private const float TouchSmallEdgeMm = 7f, TouchSmallEdgeFallback = 80f, TouchSmallEdgeMax = 130f, TouchMinEdge = 40f;
        private const float TouchGap = 28f;

        private const float FlyTime = 0.6f;
        /// <summary>飛到終點時的大小（相對起飛時）。</summary>
        private const float FlyEndScale = 0.35f;
        /// <summary>起飛時的圖示大小限制（畫布單位，取長邊）。</summary>
        private const float FlyMinSize = 56f, FlyMaxSize = 160f;
        private const float PunchTime = 0.3f, PunchScale = 0.3f;
        private const float FlashTime = 0.9f;
        private const int FlashCount = 3;
        private const float LabelTime = 1.6f, LabelFade = 0.4f;
        /// <summary>這段時間內重複的「背包已滿」不重新閃（例如好幾件同時落地都放不下）。</summary>
        private const float FullCooldown = 0.6f;
        private static readonly Color LabelBgColor = new Color(0.05f, 0.03f, 0.1f, 0.9f);
        #endregion

        private readonly GameSession _session;
        private readonly RectTransform _root;
        private readonly Image _bagImage;
        private readonly RectTransform _label;
        private readonly Image _labelBg;
        private readonly TextMeshProUGUI _labelText;
        private readonly List<Flight> _flights = new List<Flight>();
        private bool _disposed;

        private int _layoutMode = -1;
        private int _layoutWidth, _layoutHeight;
        private float _layoutDpi;
        private float _punch;
        private float _flash;
        private float _labelLeft;
        /// <summary>目前標籤顯示的零件累計數（連續撿零件時加總）；0＝標籤不是零件。</summary>
        private int _labelParts;
        private string _labelPartsName;
        private float _lastFullAt = -10f;

        public LootHudView(Transform parent, TMP_FontAsset font, GameSession session)
        {
            _session = session;
            _root = SsUiKit.Fill("LootHud", parent);
            SsUiKit.FitSafeArea(_root);
            _root.gameObject.AddComponent<LootFlyHook>().View = this;

            var bag = SsUiKit.Rect("BagIcon", _root, Vector2.one, Vector2.one, new Vector2(0.5f, 0.5f));
            bag.sizeDelta = new Vector2(BagSize, BagSize);
            _bagImage = SsUiKit.AddImage(bag, Color.white, LootVisuals.BagSprite());
            BagIcon = bag;

            _label = SsUiKit.Rect("Label", _root, Vector2.one, Vector2.one, new Vector2(1f, 0.5f));
            _label.sizeDelta = new Vector2(200f, 56f);
            _labelBg = SsUiKit.AddImage(_label, LabelBgColor);
            var textRt = SsUiKit.Fill("Text", _label);
            _labelText = SsUiKit.AddText(textRt, font, 32f, SsUiKit.Paper);
            _labelText.textWrappingMode = TextWrappingModes.NoWrap;
            _label.gameObject.SetActive(false);

            if (session != null)
                session.PausedChanged += OnPausedChanged;
            Relayout();
        }

        /// <summary>背包圖示（ui 用它判斷點擊開背包；pickup 用它當飛行終點）。建好之前是 null。</summary>
        public RectTransform BagIcon { get; private set; }

        /// <summary>目前場上的 LootHudView（StoryUi 不在時回 null）。只在拾取時呼叫，不每幀找。</summary>
        internal static LootHudView Find()
        {
            var ui = Object.FindFirstObjectByType<StoryUi>();
            var view = ui != null ? ui.LootHud : null;
            return view != null && !view._disposed ? view : null;
        }

        public void Tick()
        {
            if (_disposed)
                return;
            SsUiKit.FitSafeArea(_root);
            Relayout();
            var udt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            TickFlights(udt);
            TickFeedback(udt);
        }

        #region 拾取飛行
        private sealed class Flight
        {
            public LootReservation Reservation;
            public RectTransform Rect;
            public Transform World;
            public Vector2 From;
            public Vector2 Size;
            public float T;
        }

        /// <summary>
        /// 起飛：reservation 已經預留好位置。from＝道具在世界中的中心、worldSize＝它在世界中的大小、world＝它所屬的那條命（worldRoot）。
        /// 暫停中、找不到相機或已 Dispose 就直接 Commit（飛行中的道具不受暫停影響：直接放進背包）。
        /// </summary>
        internal void Fly(LootReservation reservation, Vector2 from, Vector2 worldSize, Transform world)
        {
            if (reservation == null)
                return;
            var cam = FindCamera();
            if (_disposed || cam == null || _session == null || _session.Paused)
            {
                Arrive(reservation, !_disposed);
                return;
            }
            var screen = (Vector2)cam.WorldToScreenPoint(new Vector3(from.x, from.y, 0f));
            var scale = Mathf.Max(0.0001f, _root.lossyScale.x);
            var pxPerUnit = Screen.height / Mathf.Max(0.01f, cam.orthographicSize * 2f);
            var size = worldSize * pxPerUnit / scale;
            var longSide = Mathf.Max(size.x, size.y);
            if (longSide > 0.01f)
                size *= Mathf.Clamp(longSide, FlyMinSize, FlyMaxSize) / longSide;

            var rt = SsUiKit.Rect("Fly", _root, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            rt.sizeDelta = size;
            rt.position = screen;
            SsUiKit.AddImage(rt, Color.white, LootVisuals.Icon(reservation.Item));
            _flights.Add(new Flight { Reservation = reservation, Rect = rt, World = world, From = screen, Size = size });
        }

        private void TickFlights(float udt)
        {
            if (_flights.Count == 0)
                return;
            var to = (Vector2)BagIcon.position;
            for (var i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                // 重生：那條命的 worldRoot 被刪（或先被關掉）＝立刻放進背包
                var worldGone = f.World == null || !f.World.gameObject.activeInHierarchy;
                f.T += udt / FlyTime;
                if (worldGone || f.T >= 1f)
                {
                    _flights.RemoveAt(i);
                    Finish(f, !worldGone);
                    continue;
                }
                // 越飛越快（t²）、越飛越小；控制點在起點正上方、終點的高度：先往上竄再橫向飛進背包
                var e = f.T * f.T;
                var control = new Vector2(Mathf.Lerp(f.From.x, to.x, 0.15f), Mathf.Max(to.y, f.From.y));
                var a = Vector2.Lerp(f.From, control, e);
                var b = Vector2.Lerp(control, to, e);
                f.Rect.position = Vector2.Lerp(a, b, e);
                var s = Mathf.Lerp(1f, FlyEndScale, e);
                f.Rect.localScale = new Vector3(s, s, 1f);
            }
        }

        private void Finish(Flight f, bool feedback)
        {
            if (f.Rect != null)
                Object.Destroy(f.Rect.gameObject);
            Arrive(f.Reservation, feedback && !_disposed);
        }

        /// <summary>真的放進背包；feedback＝true 時圖示跳一下、顯示「+N 零件」或裝備名稱。</summary>
        private void Arrive(LootReservation reservation, bool feedback)
        {
            // Commit 時零件可能被併進背包裡既有的堆，數量與名稱先記下來
            var item = reservation.Item;
            var count = item != null ? item.Count : 0;
            var label = item != null ? (item.Kind == ItemKind.Part ? item.Name : item.DisplayName) : null;
            try
            {
                reservation.Commit();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{Log} Commit 失敗（{item?.Name}）：{ex}");
                return;
            }
            if (!feedback || item == null)
                return;
            _punch = PunchTime;
            if (item.Kind == ItemKind.Part)
            {
                _labelParts = _labelLeft > LabelFade && _labelParts > 0 && _labelPartsName == label ? _labelParts + count : count;
                _labelPartsName = label;
                ShowLabel(string.Format(TextPartsFormat, _labelParts, label), ItemData.RarityColor(item.Rarity));
            }
            else
            {
                _labelParts = 0;
                ShowLabel(label, ItemData.RarityColor(item.Rarity));
            }
        }

        private void OnPausedChanged(bool paused)
        {
            if (paused)
                CommitAll(true);
        }

        /// <summary>飛行中的全部立刻放進背包（暫停、Dispose）。</summary>
        private void CommitAll(bool feedback)
        {
            while (_flights.Count > 0)
            {
                var f = _flights[_flights.Count - 1];
                _flights.RemoveAt(_flights.Count - 1);
                Finish(f, feedback);
            }
        }

        /// <summary>StoryUi 被刪時（<see cref="LootFlyHook"/>）：預留全部 Commit、取消訂閱。</summary>
        internal void Dispose()
        {
            if (_disposed)
                return;
            CommitAll(false);
            _disposed = true;
            if (_session != null)
                _session.PausedChanged -= OnPausedChanged;
        }

        private static Camera FindCamera()
        {
            var rig = Object.FindFirstObjectByType<CameraRig>();
            if (rig != null && rig.Camera != null)
                return rig.Camera;
            return Camera.main;
        }
        #endregion

        #region 回饋
        /// <summary>背包放不下：圖示閃紅並顯示「背包已滿」。短時間內重複呼叫只算一次。</summary>
        internal void ShowFull()
        {
            if (_disposed)
                return;
            var now = Time.unscaledTime;
            if (now - _lastFullAt < FullCooldown)
                return;
            _lastFullAt = now;
            _flash = FlashTime;
            _labelParts = 0;
            ShowLabel(TextFull, SsUiKit.Danger);
        }

        private void ShowLabel(string text, Color color)
        {
            _labelText.text = text;
            _labelText.color = color;
            var width = _labelText.GetPreferredValues(text).x;
            _label.sizeDelta = new Vector2(width + 40f, _label.sizeDelta.y);
            _labelLeft = LabelTime;
            _label.gameObject.SetActive(true);
            SetLabelAlpha(1f);
        }

        private void TickFeedback(float udt)
        {
            var scale = 1f;
            if (_punch > 0f)
            {
                _punch = Mathf.Max(0f, _punch - udt);
                scale += PunchScale * Mathf.Sin((1f - _punch / PunchTime) * Mathf.PI);
            }
            BagIcon.localScale = new Vector3(scale, scale, 1f);

            if (_flash > 0f)
            {
                _flash = Mathf.Max(0f, _flash - udt);
                var p = 1f - _flash / FlashTime;
                var w = 0.5f - 0.5f * Mathf.Cos(p * FlashCount * 2f * Mathf.PI);
                _bagImage.color = Color.Lerp(Color.white, SsUiKit.Danger, w);
            }
            else if (_bagImage.color != Color.white)
            {
                _bagImage.color = Color.white;
            }

            if (_labelLeft > 0f)
            {
                _labelLeft = Mathf.Max(0f, _labelLeft - udt);
                if (_labelLeft <= 0f)
                {
                    _label.gameObject.SetActive(false);
                    _labelParts = 0;
                }
                else
                {
                    SetLabelAlpha(Mathf.Clamp01(_labelLeft / LabelFade));
                }
            }
        }

        private void SetLabelAlpha(float a)
        {
            var c = _labelText.color;
            _labelText.color = new Color(c.r, c.g, c.b, a);
            _labelBg.color = new Color(LabelBgColor.r, LabelBgColor.g, LabelBgColor.b, LabelBgColor.a * a);
        }
        #endregion

        #region 版面
        /// <summary>輸入模式、解析度或 dpi 變了才重排。</summary>
        private void Relayout()
        {
            var touch = SsInput.TouchActive ? 1 : 0;
            if (touch == _layoutMode && Screen.width == _layoutWidth && Screen.height == _layoutHeight && Mathf.Approximately(Screen.dpi, _layoutDpi))
                return;
            _layoutMode = touch;
            _layoutWidth = Screen.width;
            _layoutHeight = Screen.height;
            _layoutDpi = Screen.dpi;

            Vector2 center;
            var size = BagSize;
            if (touch == 1)
            {
                var mm = SsUiKit.MmToUnits(TouchMinButtonMm);
                var small = Mathf.Clamp(mm, TouchSmallSize, TouchMaxSmallSize);
                var edgeUnits = SsUiKit.MmToUnits(TouchSmallEdgeMm);
                var edge = Mathf.Clamp(edgeUnits > 0f ? edgeUnits : TouchSmallEdgeFallback, TouchMinEdge, TouchSmallEdgeMax);
                size = Mathf.Clamp(mm, BagSize, TouchMaxSmallSize);
                center = new Vector2(-(edge + small / 2f), -(edge + small + TouchGap + size / 2f));
            }
            else
            {
                center = new Vector2(-(KeyboardEdgeX + size / 2f), -(KeyboardEdgeY + size / 2f));
            }
            BagIcon.anchoredPosition = center;
            BagIcon.sizeDelta = new Vector2(size, size);
            // 觸控：圖示在暫停鈕下方，標籤放左邊；鍵盤：圖示和上方中央的提示框同高，標籤改放圖示正下方（右緣對齊）
            _label.anchoredPosition = touch == 1
                ? new Vector2(center.x - size / 2f - 12f, center.y)
                : new Vector2(center.x + size / 2f, center.y - size / 2f - 12f - _label.sizeDelta.y / 2f);
        }
        #endregion
    }
}
