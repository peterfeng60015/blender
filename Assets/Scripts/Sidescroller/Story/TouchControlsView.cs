using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 觸控操作層的畫面與版面（由 <see cref="StoryUi"/> 建立、每幀 <see cref="Tick"/>）：左側浮動虛擬搖桿，
    /// 右下跳躍／甩包／彈弓／滾殼四顆按鈕（在補給站旁時甩包左邊多一顆「強化」），右上換裝、暫停兩顆小鈕。整個版面放在 <see cref="Screen.safeArea"/> 內。
    /// 手指按到哪裡由 <see cref="Classify"/> 判斷（<see cref="SsTouch"/> 呼叫），本身不寫任何輸入狀態。
    /// 顯示規則：不是觸控模式時全部不顯示；暫停、通關、直向提示時全部不顯示（手指都當點擊）；演出中只留暫停鈕；其餘全部顯示。
    /// 按鈕至少約 9mm（<see cref="Screen.dpi"/> 可信時換算，否則照設計尺寸）。
    /// </summary>
    public sealed class TouchControlsView
    {
        #region 版面常數（1920×1080 畫布單位）
        private const float ActionSize = 170f;
        private const float JumpScale = 1.3f;
        private const float SmallSize = 120f;
        private const float MaxActionSize = 240f;
        private const float MaxSmallSize = 190f;
        /// <summary>
        /// 離安全區域邊緣的距離（mm 換算；dpi 不可信時用後面的單位值）。右下按鈕群與搖桿待機位置要離手機邊緣一段距離，
        /// 手握著時拇指才按得到，也避開曲面螢幕的長邊；暫停、換裝只稍微內移。
        /// </summary>
        private const float ActionEdgeMm = 12f, ActionEdgeFallback = 150f, ActionEdgeMax = 220f;
        private const float StickEdgeMm = 15f, StickEdgeFallback = 170f, StickEdgeMax = 260f;
        private const float SmallEdgeMm = 7f, SmallEdgeFallback = 80f, SmallEdgeMax = 130f;
        private const float MinEdge = 40f;
        private const float Gap = 28f;
        private const float StickBaseSize = 260f;
        private const float StickKnobSize = 120f;
        private const float StickRange = 100f;
        private const float MinButtonMm = 9f;
        /// <summary>判定範圍比畫出來的圓大一點，手指比較好按。</summary>
        private const float HitScale = 1.15f;
        /// <summary>搖桿可以按下的區域：安全區域左側寬度與高度的比例（上方留給 HUD）。</summary>
        private const float StickZoneWidth = 0.45f;
        private const float StickZoneHeight = 0.72f;
        #endregion

        private const int ModeOff = 0, ModeHidden = 1, ModePauseOnly = 2, ModeFull = 3;
        private static readonly Color IdleColor = new Color(0.07f, 0.05f, 0.12f, 0.4f);
        /// <summary>搖桿沒在用時更淡：開局主角站在左邊，會被待機的搖桿蓋住。</summary>
        private static readonly Color StickIdleColor = new Color(0.07f, 0.05f, 0.12f, 0.2f);
        private static readonly Color KnobIdleColor = new Color(1f, 0.97f, 0.9f, 0.22f);
        private static readonly Color KnobHeldColor = new Color(1f, 0.97f, 0.9f, 0.45f);
        private static readonly Color HeldColor = new Color(0.88f, 0.27f, 0.48f, 0.7f);
        private static readonly Color LabelColor = new Color(1f, 0.97f, 0.9f, 0.85f);

        private readonly GameSession _session;
        private readonly StoryUi _ui;
        private readonly RectTransform _root;
        private readonly RectTransform _gameplay;
        private readonly RectTransform[] _buttons = new RectTransform[SsTouch.ButtonCount];
        private readonly Image[] _buttonImages = new Image[SsTouch.ButtonCount];
        private readonly RectTransform _stickBase;
        private readonly RectTransform _stickKnob;
        private readonly Image _stickBaseImage;
        private readonly Image _stickKnobImage;
        private float _stickEdge = StickEdgeFallback;
        private readonly Texture2D _discTexture;
        private readonly Sprite _disc;

        private int _layoutWidth = -1;
        private int _layoutHeight = -1;
        private Rect _layoutSafe;
        private float _layoutDpi = -1f;
        private float _unitsToPx = 1f;

        /// <summary>搖桿推到底的距離（螢幕像素）。</summary>
        public float StickRangePx => StickRange * _unitsToPx;

        public TouchControlsView(Transform parent, TMP_FontAsset font, GameSession session, StoryUi ui)
        {
            _session = session;
            _ui = ui;
            _discTexture = CreateDiscTexture(128);
            _disc = Sprite.Create(_discTexture, new Rect(0f, 0f, _discTexture.width, _discTexture.height), new Vector2(0.5f, 0.5f), 100f);
            _disc.name = "SsTouchDisc";

            _root = SsUiKit.Fill("Touch", parent);
            _gameplay = SsUiKit.Fill("Gameplay", _root);

            #region 搖桿（閒置時半透明待在左下，手指按下後移到按下的位置）
            _stickBase = SsUiKit.Rect("StickBase", _gameplay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _stickBase.sizeDelta = new Vector2(StickBaseSize, StickBaseSize);
            _stickBaseImage = SsUiKit.AddImage(_stickBase, StickIdleColor, _disc);
            _stickBaseImage.preserveAspect = false;
            _stickKnob = SsUiKit.Rect("Knob", _stickBase, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _stickKnob.sizeDelta = new Vector2(StickKnobSize, StickKnobSize);
            _stickKnobImage = SsUiKit.AddImage(_stickKnob, KnobIdleColor, _disc);
            _stickKnobImage.preserveAspect = false;
            #endregion

            #region 按鈕（右下四顆在 Gameplay 下；暫停在 root 下，演出中也顯示）
            var bottomRight = new Vector2(1f, 0f);
            var topRight = Vector2.one;
            AddButton(SsTouch.BJump, _gameplay, bottomRight, font, StoryTexts.TouchJump, 64f);
            AddButton(SsTouch.BMelee, _gameplay, bottomRight, font, StoryTexts.TouchMelee, 44f);
            AddButton(SsTouch.BShoot, _gameplay, bottomRight, font, StoryTexts.TouchShoot, 44f);
            AddButton(SsTouch.BRoll, _gameplay, bottomRight, font, StoryTexts.TouchRoll, 44f);
            AddButton(SsTouch.BInteract, _gameplay, bottomRight, font, StoryTexts.TouchEnhance, 44f);
            AddButton(SsTouch.BSwitch, _gameplay, topRight, font, StoryTexts.TouchSwitch, 36f);
            AddButton(SsTouch.BPause, _root, topRight, font, StoryTexts.TouchPause, 48f);
            #endregion

            _root.gameObject.SetActive(false);
        }

        /// <summary>釋放程式產生的貼圖（StoryUi.OnDestroy 呼叫）。</summary>
        public void Dispose()
        {
            if (_disc != null)
                Object.Destroy(_disc);
            if (_discTexture != null)
                Object.Destroy(_discTexture);
        }

        #region 每幀
        /// <summary>
        /// 依目前模式顯示／隱藏、畫搖桿與按住的按鈕。安全區域、版面與按鈕開放狀態不論模式每幀都跟上
        /// （收起期間轉向或取得新攻擊，下一次顯示或第一下觸控時判定位置都已經是對的）。
        /// </summary>
        public void Tick()
        {
            if (_root == null)
                return;
            SsUiKit.FitSafeArea(_root);
            Relayout();
            var loadout = _session.Loadout;
            SetActive(_buttons[SsTouch.BMelee], loadout.HasAttack(AttackId.Melee));
            SetActive(_buttons[SsTouch.BShoot], loadout.HasAttack(AttackId.Sling));
            SetActive(_buttons[SsTouch.BRoll], loadout.HasAttack(AttackId.Roll));
            SetActive(_buttons[SsTouch.BSwitch], loadout.CostumeCount > 1);
            SetActive(_buttons[SsTouch.BInteract], _ui != null && _ui.LootScreens != null && _ui.LootScreens.StationInRange);

            var mode = CurrentMode(SsInput.TouchActive);
            SetActive(_root, mode >= ModePauseOnly);
            SetActive(_gameplay, mode == ModeFull);
            if (mode < ModePauseOnly)
                return;
            for (var b = 0; b < SsTouch.ButtonCount; b++)
            {
                var color = SsTouch.Held(b) ? HeldColor : IdleColor;
                if (_buttonImages[b].color != color)
                    _buttonImages[b].color = color;
            }
            if (mode == ModeFull)
                DrawStick();
        }

        private void DrawStick()
        {
            if (SsTouch.StickActive(out var origin, out var current) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_gameplay, origin, null, out var localOrigin) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_gameplay, current, null, out var localCurrent))
            {
                _stickBase.anchoredPosition = localOrigin;
                _stickKnob.anchoredPosition = Vector2.ClampMagnitude(localCurrent - localOrigin, StickRange);
                _stickBaseImage.color = HeldColor;
                _stickKnobImage.color = KnobHeldColor;
                return;
            }
            var size = _gameplay.rect.size;
            _stickBase.anchoredPosition = new Vector2(-size.x / 2f + _stickEdge + StickBaseSize / 2f, -size.y / 2f + _stickEdge + StickBaseSize / 2f);
            _stickKnob.anchoredPosition = Vector2.zero;
            _stickBaseImage.color = StickIdleColor;
            _stickKnobImage.color = KnobIdleColor;
        }
        #endregion

        #region 判定（SsTouch 在手指按下那一幀呼叫）
        /// <summary>手指按在 screenPos：回傳按鈕序號、<see cref="SsTouch.Stick"/> 或 <see cref="SsTouch.Tap"/>。</summary>
        internal int Classify(Vector2 screenPos)
        {
            if (_root == null)
                return SsTouch.Tap;
            // 計算途中不能再讀 SsTouch.Active（會遞迴），讀原始值
            var mode = CurrentMode(SsTouch.ActiveNoEvaluate);
            if (mode < ModePauseOnly)
                return SsTouch.Tap;
            if (Hit(SsTouch.BPause, screenPos))
                return SsTouch.BPause;
            if (mode < ModeFull)
                return SsTouch.Tap;
            for (var b = 0; b < SsTouch.ButtonCount; b++)
            {
                if (b != SsTouch.BPause && Hit(b, screenPos))
                    return b;
            }
            var safe = Screen.safeArea;
            if (screenPos.x < safe.xMin + safe.width * StickZoneWidth && screenPos.y < safe.yMin + safe.height * StickZoneHeight)
                return SsTouch.Stick;
            return SsTouch.Tap;
        }

        /// <summary>
        /// 按鈕可不可以按依「有沒有開放」（activeSelf，例如還沒取得的攻擊）判斷，顯示模式另外由 <see cref="CurrentMode"/> 決定，
        /// 這樣模式剛切換、畫面還沒更新的那一幀也判得對。
        /// </summary>
        private bool Hit(int button, Vector2 screenPos)
        {
            var rt = _buttons[button];
            if (!rt.gameObject.activeSelf)
                return false;
            var center = (Vector2)rt.position;
            var radius = rt.rect.width * 0.5f * rt.lossyScale.x * HitScale;
            return (screenPos - center).sqrMagnitude <= radius * radius;
        }

        private int CurrentMode(bool touchActive)
        {
            if (!touchActive)
                return ModeOff;
            if (_ui == null || _ui.TouchBlocked)
                return ModeHidden;
            return _session.CutsceneActive ? ModePauseOnly : ModeFull;
        }
        #endregion

        #region 版面
        /// <summary>解析度、安全區域或 dpi 變了才重排。</summary>
        private void Relayout()
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;
            var dpi = Screen.dpi;
            if (w == _layoutWidth && h == _layoutHeight && safe == _layoutSafe && Mathf.Approximately(dpi, _layoutDpi))
                return;
            _layoutWidth = w;
            _layoutHeight = h;
            _layoutSafe = safe;
            _layoutDpi = dpi;
            _unitsToPx = SsUiKit.UnitsToPx();
            var minUnits = SsUiKit.MmToUnits(MinButtonMm);
            var action = Mathf.Clamp(minUnits, ActionSize, MaxActionSize);
            var small = Mathf.Clamp(minUnits, SmallSize, MaxSmallSize);
            var jump = action * JumpScale;

            // 跳躍在右下角，三個攻擊沿著它的左上排成弧（180°、135°、90°），相鄰兩顆不重疊
            var edge = Edge(ActionEdgeMm, ActionEdgeFallback, ActionEdgeMax);
            var smallEdge = Edge(SmallEdgeMm, SmallEdgeFallback, SmallEdgeMax);
            _stickEdge = Edge(StickEdgeMm, StickEdgeFallback, StickEdgeMax);
            var jumpCenter = new Vector2(-(edge + jump / 2f), edge + jump / 2f);
            var arc = Mathf.Max(jump / 2f + Gap + action / 2f, (action + Gap) / (2f * Mathf.Sin(22.5f * Mathf.Deg2Rad)));
            Place(SsTouch.BJump, jumpCenter, jump);
            Place(SsTouch.BMelee, jumpCenter + Polar(arc, 180f), action);
            Place(SsTouch.BShoot, jumpCenter + Polar(arc, 135f), action);
            Place(SsTouch.BRoll, jumpCenter + Polar(arc, 90f), action);
            // 補給站的「強化」：甩包再往左一格；會伸進左側搖桿區（安全區域寬的 45%）時改放到滾殼上方
            var interact = jumpCenter + Polar(arc * 2f, 180f);
            var stickRight = -safe.width / _unitsToPx * (1f - StickZoneWidth);
            if (interact.x - action / 2f * HitScale < stickRight)
                interact = jumpCenter + Polar(arc * 2f, 90f);
            Place(SsTouch.BInteract, interact, action);
            // 右上：暫停在角落，換裝在它左邊（上方中央是提示文字，左上是血量）
            // 背包圖示（LootHudView.Relayout）照抄暫停鈕的大小與邊距放在它正下方：改這裡的版面要一起改那邊
            Place(SsTouch.BPause, new Vector2(-(smallEdge + small / 2f), -(smallEdge + small / 2f)), small);
            Place(SsTouch.BSwitch, new Vector2(-(smallEdge + small * 1.5f + Gap), -(smallEdge + small / 2f)), small);
        }

        /// <summary>離邊緣的距離：mm 換算成畫布單位，dpi 不可信時用 fallback；限制在 MinEdge～max。</summary>
        private static float Edge(float mm, float fallback, float max)
        {
            var units = SsUiKit.MmToUnits(mm);
            return Mathf.Clamp(units > 0f ? units : fallback, MinEdge, max);
        }

        private void Place(int button, Vector2 center, float size)
        {
            var rt = _buttons[button];
            rt.anchoredPosition = center;
            rt.sizeDelta = new Vector2(size, size);
        }

        private static Vector2 Polar(float radius, float degrees)
        {
            var rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
        }
        #endregion

        #region 建立
        private void AddButton(int button, Transform parent, Vector2 anchor, TMP_FontAsset font, string label, float fontSize)
        {
            var rt = SsUiKit.Rect($"Button{button}", parent, anchor, anchor, new Vector2(0.5f, 0.5f));
            var img = SsUiKit.AddImage(rt, IdleColor, _disc);
            img.preserveAspect = false;
            var ring = SsUiKit.Fill("Ring", rt);
            ring.offsetMin = new Vector2(-4f, -4f);
            ring.offsetMax = new Vector2(4f, 4f);
            ring.SetAsFirstSibling();
            var ringImg = SsUiKit.AddImage(ring, new Color(1f, 1f, 1f, 0.28f), _disc);
            ringImg.preserveAspect = false;
            var labelRt = SsUiKit.Fill("Label", rt);
            var text = SsUiKit.AddText(labelRt, font, fontSize, LabelColor);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = label;
            _buttons[button] = rt;
            _buttonImages[button] = img;
        }

        /// <summary>白色實心圓（邊緣 1.5px 反鋸齒）；顏色由 Image.color 決定。</summary>
        private static Texture2D CreateDiscTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SsTouchDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            var c = (size - 1) / 2f;
            var r = size / 2f - 1f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    var a = Mathf.Clamp01((r - d) / 1.5f + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static void SetActive(Component c, bool active)
        {
            if (c.gameObject.activeSelf != active)
                c.gameObject.SetActive(active);
        }
        #endregion
    }
}
