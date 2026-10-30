using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 觸控操作層的畫面與版面（由 <see cref="StoryUi"/> 建立、每幀 <see cref="Tick"/>）：左側浮動虛擬搖桿，
    /// 右下跳躍／甩包／彈弓／滾殼四顆按鈕（在補給站旁時多一顆「強化」），右上換裝、暫停兩顆小鈕。整個版面放在 <see cref="Screen.safeArea"/> 內。
    /// 手指按到哪裡由 <see cref="Classify"/> 判斷（<see cref="SsTouch"/> 呼叫），本身不寫任何輸入狀態。
    /// 顯示規則：不是觸控模式時全部不顯示；暫停、通關、直向提示時全部不顯示（手指都當點擊）；演出中只留暫停鈕；其餘全部顯示。
    /// 按鈕至少約 9mm（<see cref="Screen.dpi"/> 可信時換算，否則照設計尺寸）。
    /// 外觀：圓形底盤＋圖示＋小字，按住時外觀縮小並換成亮底（判定範圍不跟著縮）。底盤與圖示用 <c>SsTouchUi_Idle</c>，
    /// 素材是佔位圖時改用程式畫的底盤，圖示借 HUD 的 <c>SsUi_Idle</c>（跳、暫停、強化程式畫）。
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
        /// <summary>背包圖示（LootHudView.Relayout 觸控版面）的最小邊長：排右下按鈕時要避開它。</summary>
        private const float BagSize = 104f;
        #endregion

        #region 外觀常數
        private const int ModeOff = 0, ModeHidden = 1, ModePauseOnly = 2, ModeFull = 3;
        private const string TouchUiLocation = "SsTouchUi_Idle";
        private const string HudIconLocation = "SsUi_Idle";
        /// <summary>SsTouchUi 的變化（ui 的 Tools/ComfyUI 產生）。</summary>
        private const int SkinBase = 0, SkinBasePressed = 1, SkinStickBase = 2, SkinStickKnob = 3, SkinIconFirst = 4, SkinCount = 11;
        /// <summary>SsUi 的 HUD 圖示（和 HudView 相同序號）。</summary>
        private const int HudMelee = 2, HudSling = 3, HudRoll = 4, HudCostumeDay = 5, HudCostumeExpress = 6;
        /// <summary>程式畫的替代素材邊長（px）：底盤 256、圖示 128。</summary>
        private const int TexSize = 256, IconTexSize = 128;
        /// <summary>按住時外觀縮到這個倍率；ScaleSpeed＝1 秒可以走完幾次 1↔PressedScale（30＝約 33ms 縮到底）。</summary>
        private const float PressedScale = 0.9f, ScaleSpeed = 30f;
        /// <summary>按鈕平常的不透明度（底盤本身已半透明）；搖桿沒在用時更淡：開局主角站在左邊，會被待機的搖桿蓋住。</summary>
        private const float IdleAlpha = 0.9f, StickIdleAlpha = 0.3f;
        /// <summary>字的大小（按鈕直徑的比例）。</summary>
        private const float LabelScale = 0.16f;

        private static readonly Color Outline = new Color32(0x2A, 0x0F, 0x2E, 0xFF);
        private static readonly Color RimGold = new Color32(0xF4, 0xC5, 0x5B, 0xFF);
        private static readonly Color RimCream = new Color32(0xFF, 0xF3, 0xC4, 0xFF);
        private static readonly Color FaceCenter = new Color(0.29f, 0.17f, 0.40f, 0.74f);
        private static readonly Color FaceEdge = new Color(0.12f, 0.06f, 0.18f, 0.64f);
        private static readonly Color PressedTop = new Color32(0xFD, 0xB3, 0x6A, 0xF5);
        private static readonly Color PressedBottom = new Color32(0xE2, 0x49, 0x7C, 0xF5);
        private static readonly Color LabelColor = new Color(1f, 0.97f, 0.9f, 1f);
        #endregion

        private readonly GameSession _session;
        private readonly StoryUi _ui;
        private readonly RectTransform _root;
        private readonly RectTransform _gameplay;
        private readonly RectTransform[] _buttons = new RectTransform[SsTouch.ButtonCount];
        /// <summary>按鈕的外觀（按住時縮放的是這層；判定用的 _buttons 不縮）。</summary>
        private readonly RectTransform[] _visuals = new RectTransform[SsTouch.ButtonCount];
        private readonly Image[] _bases = new Image[SsTouch.ButtonCount];
        private readonly Image[] _icons = new Image[SsTouch.ButtonCount];
        private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[SsTouch.ButtonCount];
        private readonly TextMeshProUGUI[] _labelShadows = new TextMeshProUGUI[SsTouch.ButtonCount];
        private readonly float[] _scales = new float[SsTouch.ButtonCount];
        private readonly bool[] _shownHeld = new bool[SsTouch.ButtonCount];
        private readonly RectTransform _stickBase;
        private readonly RectTransform _stickKnob;
        private readonly Image _stickBaseImage;
        private readonly Image _stickKnobImage;
        private float _stickEdge = StickEdgeFallback;
        private readonly Sprite _baseSprite;
        private readonly Sprite _pressedSprite;
        private readonly Sprite _costumeDay;
        private readonly Sprite _costumeExpress;
        private CostumeId _shownCostume = (CostumeId)(-1);
        /// <summary>程式產生的貼圖與 sprite（Dispose 時釋放）。</summary>
        private readonly List<Object> _owned = new List<Object>();

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

            #region 素材（SsTouchUi 沒讀到時：底盤程式畫、圖示借 HUD）
            var skin = SsArt.Get(TouchUiLocation);
            var hud = SsArt.Get(HudIconLocation);
            var useSkin = !skin.IsPlaceholder && skin.FrameCount >= SkinCount;
            var icons = new Sprite[SsTouch.ButtonCount];
            Sprite stickBase, stickKnob;
            if (useSkin)
            {
                _baseSprite = skin.Variant(SkinBase);
                _pressedSprite = skin.Variant(SkinBasePressed);
                stickBase = skin.Variant(SkinStickBase);
                stickKnob = skin.Variant(SkinStickKnob);
                // SsTouchUi 圖示順序：跳、甩包、彈弓、滾殼、強化、換裝、暫停
                icons[SsTouch.BJump] = skin.Variant(SkinIconFirst);
                icons[SsTouch.BMelee] = skin.Variant(SkinIconFirst + 1);
                icons[SsTouch.BShoot] = skin.Variant(SkinIconFirst + 2);
                icons[SsTouch.BRoll] = skin.Variant(SkinIconFirst + 3);
                icons[SsTouch.BInteract] = skin.Variant(SkinIconFirst + 4);
                icons[SsTouch.BSwitch] = skin.Variant(SkinIconFirst + 5);
                icons[SsTouch.BPause] = skin.Variant(SkinIconFirst + 6);
            }
            else
            {
                _baseSprite = Paint("SsTouchBase", PaintBase);
                _pressedSprite = Paint("SsTouchBasePressed", PaintPressed);
                stickBase = Paint("SsTouchStickBase", PaintStickBase);
                stickKnob = Paint("SsTouchStickKnob", PaintStickKnob);
                icons[SsTouch.BJump] = Paint("SsTouchIconJump", PaintJump, IconTexSize);
                icons[SsTouch.BPause] = Paint("SsTouchIconPause", PaintPause, IconTexSize);
                icons[SsTouch.BInteract] = Paint("SsTouchIconEnhance", PaintPlus, IconTexSize);
                if (!hud.IsPlaceholder)
                {
                    icons[SsTouch.BMelee] = hud.Variant(HudMelee);
                    icons[SsTouch.BShoot] = hud.Variant(HudSling);
                    icons[SsTouch.BRoll] = hud.Variant(HudRoll);
                    // 換裝鈕顯示「換過去」的那套（Tick 依目前服裝切換）
                    _costumeDay = hud.Variant(HudCostumeDay);
                    _costumeExpress = hud.Variant(HudCostumeExpress);
                    icons[SsTouch.BSwitch] = _costumeExpress;
                }
            }
            #endregion

            _root = SsUiKit.Fill("Touch", parent);
            _gameplay = SsUiKit.Fill("Gameplay", _root);

            #region 搖桿（閒置時淡淡待在左下，手指按下後移到按下的位置）
            _stickBase = SsUiKit.Rect("StickBase", _gameplay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _stickBase.sizeDelta = new Vector2(StickBaseSize, StickBaseSize);
            _stickBaseImage = SsUiKit.AddImage(_stickBase, Color.white, stickBase);
            _stickKnob = SsUiKit.Rect("Knob", _stickBase, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _stickKnob.sizeDelta = new Vector2(StickKnobSize, StickKnobSize);
            _stickKnobImage = SsUiKit.AddImage(_stickKnob, Color.white, stickKnob);
            #endregion

            #region 按鈕（右下在 Gameplay 下；暫停在 root 下，演出中也顯示）
            var bottomRight = new Vector2(1f, 0f);
            var topRight = Vector2.one;
            AddButton(SsTouch.BJump, _gameplay, bottomRight, font, StoryTexts.TouchJump, icons);
            AddButton(SsTouch.BMelee, _gameplay, bottomRight, font, StoryTexts.TouchMelee, icons);
            AddButton(SsTouch.BShoot, _gameplay, bottomRight, font, StoryTexts.TouchShoot, icons);
            AddButton(SsTouch.BRoll, _gameplay, bottomRight, font, StoryTexts.TouchRoll, icons);
            AddButton(SsTouch.BInteract, _gameplay, bottomRight, font, StoryTexts.TouchEnhance, icons);
            AddButton(SsTouch.BSwitch, _gameplay, topRight, font, StoryTexts.TouchSwitch, icons);
            // 暫停只有圖示（兩條直槓），沒有字
            AddButton(SsTouch.BPause, _root, topRight, font, null, icons);
            #endregion

            _root.gameObject.SetActive(false);
        }

        /// <summary>釋放程式產生的貼圖（StoryUi.OnDestroy 呼叫）。</summary>
        public void Dispose()
        {
            foreach (var o in _owned)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            _owned.Clear();
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
            ShowCostume(loadout.Costume);

            var mode = CurrentMode(SsInput.TouchActive);
            SetActive(_root, mode >= ModePauseOnly);
            SetActive(_gameplay, mode == ModeFull);
            if (mode < ModePauseOnly)
                return;
            var dt = Time.unscaledDeltaTime;
            for (var b = 0; b < SsTouch.ButtonCount; b++)
                DrawButton(b, SsTouch.Held(b), dt);
            if (mode == ModeFull)
                DrawStick();
        }

        /// <summary>按住：換亮底、外觀往 <see cref="PressedScale"/> 縮；放開：換回、放大回 1。</summary>
        private void DrawButton(int b, bool held, float dt)
        {
            if (_shownHeld[b] != held)
            {
                _shownHeld[b] = held;
                _bases[b].sprite = held ? _pressedSprite : _baseSprite;
                _bases[b].color = new Color(1f, 1f, 1f, held ? 1f : IdleAlpha);
            }
            var target = held ? PressedScale : 1f;
            if (Mathf.Approximately(_scales[b], target))
                return;
            _scales[b] = Mathf.MoveTowards(_scales[b], target, ScaleSpeed * (1f - PressedScale) * dt);
            _visuals[b].localScale = new Vector3(_scales[b], _scales[b], 1f);
        }

        private void ShowCostume(CostumeId costume)
        {
            if (costume == _shownCostume || _costumeDay == null)
                return;
            _shownCostume = costume;
            _icons[SsTouch.BSwitch].sprite = costume == CostumeId.Express ? _costumeDay : _costumeExpress;
        }

        private void DrawStick()
        {
            if (SsTouch.StickActive(out var origin, out var current) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_gameplay, origin, null, out var localOrigin) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_gameplay, current, null, out var localCurrent))
            {
                _stickBase.anchoredPosition = localOrigin;
                _stickKnob.anchoredPosition = Vector2.ClampMagnitude(localCurrent - localOrigin, StickRange);
                SetAlpha(_stickBaseImage, 1f);
                SetAlpha(_stickKnobImage, 1f);
                return;
            }
            var size = _gameplay.rect.size;
            _stickBase.anchoredPosition = new Vector2(-size.x / 2f + _stickEdge + StickBaseSize / 2f, -size.y / 2f + _stickEdge + StickBaseSize / 2f);
            _stickKnob.anchoredPosition = Vector2.zero;
            SetAlpha(_stickBaseImage, StickIdleAlpha);
            SetAlpha(_stickKnobImage, StickIdleAlpha);
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
        /// 這樣模式剛切換、畫面還沒更新的那一幀也判得對。按住時縮小的是外觀子物件，判定範圍不變。
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
        /// <summary>版面計算結果（畫布單位）。暫停、換裝的中心以右上角為原點，其他以右下角為原點。</summary>
        internal sealed class Layout
        {
            public float UnitsToPx;
            public float StickEdge;
            public float SafeWidth;
            public float SafeHeight;
            public readonly Vector2[] Centers = new Vector2[SsTouch.ButtonCount];
            public readonly float[] Sizes = new float[SsTouch.ButtonCount];
            /// <summary>背包圖示（LootHudView 觸控版面）的範圍，右下角為原點。</summary>
            public Rect Bag;
        }

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
            Apply(Compute(w, h, safe, dpi));
        }

        private void Apply(Layout layout)
        {
            _unitsToPx = layout.UnitsToPx;
            _stickEdge = layout.StickEdge;
            for (var b = 0; b < SsTouch.ButtonCount; b++)
                Place(b, layout.Centers[b], layout.Sizes[b]);
        }

        /// <summary>
        /// 依螢幕大小、安全區域與 dpi 排版（不讀 <see cref="Screen"/>，換算和 <see cref="SsUiKit.UnitsToPx"/>、<see cref="SsUiKit.MmToUnits"/> 相同），
        /// 實測工具用不同裝置的數值呼叫檢查。
        /// </summary>
        internal static Layout Compute(int width, int height, Rect safe, float dpi)
        {
            var layout = new Layout();
            var u = Mathf.Sqrt(Mathf.Max(1f, width) / 1920f * (Mathf.Max(1f, height) / 1080f));
            layout.UnitsToPx = u;
            var minUnits = MmToUnits(MinButtonMm, dpi, u);
            var action = Mathf.Clamp(minUnits, ActionSize, MaxActionSize);
            var small = Mathf.Clamp(minUnits, SmallSize, MaxSmallSize);
            var jump = action * JumpScale;
            // 安全區域無效時用整個螢幕（和 SsUiKit.FitSafeArea 相同）
            if (safe.width <= 0f || safe.height <= 0f)
                safe = new Rect(0f, 0f, width, height);
            var safeWidth = safe.width / u;
            var safeHeight = safe.height / u;
            layout.SafeWidth = safeWidth;
            layout.SafeHeight = safeHeight;

            // 右上：暫停在角落，換裝在它左邊（上方中央是提示文字，左上是血量）
            // 背包圖示（LootHudView.Relayout）照抄暫停鈕的大小與邊距放在它正下方：改這裡的版面要一起改那邊
            var smallEdge = Edge(SmallEdgeMm, SmallEdgeFallback, SmallEdgeMax, dpi, u);
            var pause = new Vector2(-(smallEdge + small / 2f), -(smallEdge + small / 2f));
            var costume = new Vector2(-(smallEdge + small * 1.5f + Gap), -(smallEdge + small / 2f));
            Set(layout, SsTouch.BPause, pause, small);
            Set(layout, SsTouch.BSwitch, costume, small);
            // 右上這群（含背包圖示）換算成右下角座標，排右下按鈕時避開
            var bag = Mathf.Clamp(minUnits, BagSize, MaxSmallSize);
            var top = new Vector2(0f, safeHeight);
            layout.Bag = new Rect(top + new Vector2(pause.x - bag / 2f, -(smallEdge + small + Gap + bag)), Vector2.one * bag);
            var avoid = new[]
            {
                new Rect(top + pause - Vector2.one * (small / 2f), Vector2.one * small),
                new Rect(top + costume - Vector2.one * (small / 2f), Vector2.one * small),
                layout.Bag,
            };

            // 跳躍在右下角，三個攻擊沿著它的左上排成弧（180°、135°、90°），相鄰兩顆不重疊
            var edge = Edge(ActionEdgeMm, ActionEdgeFallback, ActionEdgeMax, dpi, u);
            layout.StickEdge = Edge(StickEdgeMm, StickEdgeFallback, StickEdgeMax, dpi, u);
            var jumpCenter = new Vector2(-(edge + jump / 2f), edge + jump / 2f);
            var arc = Mathf.Max(jump / 2f + Gap + action / 2f, (action + Gap) / (2f * Mathf.Sin(22.5f * Mathf.Deg2Rad)));
            Set(layout, SsTouch.BJump, jumpCenter, jump);
            Set(layout, SsTouch.BMelee, jumpCenter + Polar(arc, 180f), action);
            Set(layout, SsTouch.BShoot, jumpCenter + Polar(arc, 135f), action);
            // 滾殼原本在跳躍正上方；螢幕矮或按鈕大時會壓到右上那群，改放到外圈（甩包左邊、彈弓左上）。
            // 補給站的「強化」放在外圈剩下的第一個空位。不會伸進左側搖桿區（安全區域寬的 45%）、不超出上緣
            var slots = new[]
            {
                jumpCenter + Polar(arc, 90f),
                jumpCenter + Polar(arc * 2f, 180f),
                jumpCenter + Polar(arc * 2f, 135f),
                jumpCenter + Polar(arc * 2f, 90f),
            };
            var stickRight = -safeWidth * (1f - StickZoneWidth);
            var roll = FreeSlot(slots, 0, 3, -1, action, stickRight, safeHeight, avoid);
            var interact = FreeSlot(slots, 1, 4, roll, action, stickRight, safeHeight, avoid);
            Set(layout, SsTouch.BRoll, slots[roll], action);
            Set(layout, SsTouch.BInteract, slots[interact], action);
            return layout;
        }

        private static void Set(Layout layout, int button, Vector2 center, float size)
        {
            layout.Centers[button] = center;
            layout.Sizes[button] = size;
        }

        /// <summary>slots[from..to) 裡第一個空位（跳過 taken）；都放不下時用第一個沒被佔的（記警告）。</summary>
        private static int FreeSlot(Vector2[] slots, int from, int to, int taken, float size, float stickRight, float safeHeight, Rect[] avoid)
        {
            var fallback = -1;
            for (var i = from; i < to; i++)
            {
                if (i == taken)
                    continue;
                if (fallback < 0)
                    fallback = i;
                if (Fits(slots[i], size, stickRight, safeHeight, avoid))
                    return i;
            }
            Debug.LogWarning($"[Sidescroller.TouchControlsView] 螢幕太小，按鈕放不下（安全區域高 {safeHeight:0} 單位），可能和其他按鈕或搖桿區重疊");
            return fallback;
        }

        /// <summary>圓（判定範圍）不伸進搖桿區、不超出上緣，畫出來的圓和避開的方塊至少隔半個 Gap。</summary>
        private static bool Fits(Vector2 center, float size, float stickRight, float safeHeight, Rect[] avoid)
        {
            if (center.x - size / 2f * HitScale < stickRight || center.y + size / 2f > safeHeight)
                return false;
            var r = size / 2f + Gap / 2f;
            foreach (var rect in avoid)
            {
                var nearest = new Vector2(Mathf.Clamp(center.x, rect.xMin, rect.xMax), Mathf.Clamp(center.y, rect.yMin, rect.yMax));
                if ((center - nearest).sqrMagnitude < r * r)
                    return false;
            }
            return true;
        }

        /// <summary>和 <see cref="SsUiKit.MmToUnits"/> 相同，dpi 與換算比例由參數給：dpi 不可信（不在 50～1000）時回傳 0。</summary>
        private static float MmToUnits(float mm, float dpi, float unitsToPx)
        {
            if (dpi < 50f || dpi > 1000f)
                return 0f;
            return mm / 25.4f * dpi / unitsToPx;
        }

        /// <summary>離邊緣的距離：mm 換算成畫布單位，dpi 不可信時用 fallback；限制在 MinEdge～max。</summary>
        private static float Edge(float mm, float fallback, float max, float dpi, float unitsToPx)
        {
            var units = MmToUnits(mm, dpi, unitsToPx);
            return Mathf.Clamp(units > 0f ? units : fallback, MinEdge, max);
        }

        private void Place(int button, Vector2 center, float size)
        {
            var rt = _buttons[button];
            rt.anchoredPosition = center;
            rt.sizeDelta = new Vector2(size, size);
            var label = _labels[button];
            if (label == null)
                return;
            label.fontSize = size * LabelScale;
            _labelShadows[button].fontSize = size * LabelScale;
        }

        private static Vector2 Polar(float radius, float degrees)
        {
            var rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
        }
        #endregion

        #region 建立
        /// <summary>按鈕：判定用的 rt（不縮放）→ 外觀 Visual（底盤、圖示、字；按住時縮放）。label 為 null 時圖示置中、沒有字。</summary>
        private void AddButton(int button, Transform parent, Vector2 anchor, TMP_FontAsset font, string label, Sprite[] icons)
        {
            var rt = SsUiKit.Rect($"Button{button}", parent, anchor, anchor, new Vector2(0.5f, 0.5f));
            var visual = SsUiKit.Fill("Visual", rt);
            var baseImg = SsUiKit.AddImage(visual, new Color(1f, 1f, 1f, IdleAlpha), _baseSprite);
            var iconRt = SsUiKit.Fill("Icon", visual);
            // 有字：圖示在上半、字在下方；沒字：圖示置中
            iconRt.anchorMin = label != null ? new Vector2(0.25f, 0.36f) : new Vector2(0.27f, 0.27f);
            iconRt.anchorMax = label != null ? new Vector2(0.75f, 0.86f) : new Vector2(0.73f, 0.73f);
            var icon = SsUiKit.AddImage(iconRt, Color.white, icons[button]);
            icon.enabled = icons[button] != null;
            if (label != null)
            {
                var labelRt = SsUiKit.Fill("Label", visual);
                labelRt.anchorMin = new Vector2(0.05f, icon.enabled ? 0.1f : 0.25f);
                labelRt.anchorMax = new Vector2(0.95f, icon.enabled ? 0.4f : 0.75f);
                var (body, shadow) = UiSkin.Label(labelRt, font, ActionSize * LabelScale, LabelColor);
                body.text = label;
                shadow.text = label;
                _labels[button] = body;
                _labelShadows[button] = shadow;
            }
            _buttons[button] = rt;
            _visuals[button] = visual;
            _bases[button] = baseImg;
            _icons[button] = icon;
            _scales[button] = 1f;
        }

        private static void SetActive(Component c, bool active)
        {
            if (c.gameObject.activeSelf != active)
                c.gameObject.SetActive(active);
        }

        private static void SetAlpha(Image img, float a)
        {
            if (!Mathf.Approximately(img.color.a, a))
                img.color = new Color(1f, 1f, 1f, a);
        }
        #endregion

        #region 程式畫的替代素材（SsTouchUi 是佔位圖時用；座標以貼圖中心為原點、單位 px）
        /// <summary>畫一個像素。圖形都左右對稱：只算左半邊，右半邊鏡射（解譯執行時省一半時間）。</summary>
        private delegate Color Painter(Vector2 p, float half);
        /// <summary>形狀：回傳到邊緣帶正負號的距離（px，內部為負）。不用 Func&lt;Vector2, float&gt;（避開 AOT 泛型）。</summary>
        private delegate float Shape(Vector2 p);

        private Sprite Paint(string name, Painter painter, int size = TexSize)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            var half = size / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size / 2; x++)
                {
                    var c = (Color32)painter(new Vector2(x + 0.5f - half, y + 0.5f - half), half);
                    pixels[y * size + x] = c;
                    pixels[y * size + size - 1 - x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            _owned.Add(sprite);
            _owned.Add(tex);
            return sprite;
        }

        // 底盤由內往外換色（Band），不一層層疊實心圓：中心只有底色的不透明度，按鈕才是半透明

        /// <summary>平常的底盤：深紫半透明底（中心略亮）、洋紅內圈、金色外圈、深紫描邊。</summary>
        private static Color PaintBase(Vector2 p, float half)
        {
            var r = p.magnitude;
            var R = half - 4f;
            var c = Color.Lerp(FaceCenter, FaceEdge, r / (R - 24f));
            c = Band(c, Fade(SsUiKit.Magenta, 0.85f), r - (R - 24f));
            c = Band(c, Fade(RimGold, 0.95f), r - (R - 19f));
            c = Band(c, Fade(Outline, 0.55f), r - (R - 7f));
            return Band(c, Color.clear, r - R);
        }

        /// <summary>按住的底盤：橘到粉的亮底（和選單選中的按鈕同色）、奶油色外圈。</summary>
        private static Color PaintPressed(Vector2 p, float half)
        {
            var r = p.magnitude;
            var R = half - 4f;
            var c = Color.Lerp(PressedBottom, PressedTop, (p.y / (R - 19f) + 1f) / 2f);
            c = Band(c, RimCream, r - (R - 19f));
            c = Band(c, Fade(Outline, 0.6f), r - (R - 7f));
            return Band(c, Color.clear, r - R);
        }

        /// <summary>搖桿底座：比按鈕淡的深紫底、洋紅與金色細圈，上下左右四個小三角。</summary>
        private static Color PaintStickBase(Vector2 p, float half)
        {
            var r = p.magnitude;
            var R = half - 4f;
            var c = Fade(FaceEdge, 0.55f);
            c = Band(c, Fade(SsUiKit.Magenta, 0.5f), r - (R - 15f));
            c = Band(c, Fade(RimGold, 0.75f), r - (R - 11f));
            c = Band(c, Fade(Outline, 0.4f), r - (R - 5f));
            c = Band(c, Color.clear, r - R);
            // 三角形：朝外的尖端在 0.86R，底邊在 0.66R
            var a = Mathf.Abs(p.x) > Mathf.Abs(p.y) ? new Vector2(Mathf.Abs(p.x), p.y) : new Vector2(Mathf.Abs(p.y), p.x);
            var tip = R * 0.86f;
            var baseX = R * 0.66f;
            var halfWidth = (tip - baseX) * 0.9f;
            var d = Mathf.Max(baseX - a.x, (Mathf.Abs(a.y) * (tip - baseX) / halfWidth + a.x - tip) * 0.7f);
            return Layer(c, RimCream, 0.85f, d);
        }

        /// <summary>搖桿頭：上奶油下洋紅的漸層、奶油外圈。</summary>
        private static Color PaintStickKnob(Vector2 p, float half)
        {
            var r = p.magnitude;
            var R = half - 4f;
            var c = Color.Lerp(PressedBottom, new Color32(0xFF, 0xE2, 0xAA, 0xFF), (p.y / R + 1f) / 2f);
            c.a = 0.9f;
            c = Band(c, RimCream, r - (R - 18f));
            c = Band(c, Fade(Outline, 0.5f), r - (R - 8f));
            return Band(c, Color.clear, r - R);
        }

        /// <summary>跳：兩個向上的箭頭。</summary>
        private static Color PaintJump(Vector2 p, float half)
        {
            return Icon(p, q =>
            {
                var w = half * 0.62f;
                var h = half * 0.5f;
                var upper = Mathf.Min(Segment(q, new Vector2(-w, h * 0.3f), new Vector2(0f, h * 0.3f + w)),
                    Segment(q, new Vector2(0f, h * 0.3f + w), new Vector2(w, h * 0.3f)));
                var lower = Mathf.Min(Segment(q, new Vector2(-w, -h * 0.9f), new Vector2(0f, -h * 0.9f + w)),
                    Segment(q, new Vector2(0f, -h * 0.9f + w), new Vector2(w, -h * 0.9f)));
                return Mathf.Min(upper, lower) - half * 0.11f;
            }, half);
        }

        /// <summary>暫停：兩條直槓。</summary>
        private static Color PaintPause(Vector2 p, float half)
        {
            return Icon(p, q =>
            {
                var x = half * 0.3f;
                var y = half * 0.5f;
                return Mathf.Min(Segment(q, new Vector2(-x, -y), new Vector2(-x, y)), Segment(q, new Vector2(x, -y), new Vector2(x, y))) - half * 0.15f;
            }, half);
        }

        /// <summary>強化：加號。</summary>
        private static Color PaintPlus(Vector2 p, float half)
        {
            return Icon(p, q =>
            {
                var l = half * 0.55f;
                return Mathf.Min(Segment(q, new Vector2(-l, 0f), new Vector2(l, 0f)), Segment(q, new Vector2(0f, -l), new Vector2(0f, l))) - half * 0.15f;
            }, half);
        }

        /// <summary>圖示：奶油色本體、深紫描邊，往下一點的深色硬陰影（亮底上也讀得到）。</summary>
        private static Color Icon(Vector2 p, Shape shape, float half)
        {
            var stroke = half * 0.09f;
            var shadow = new Vector2(0f, half * 0.07f);
            var c = Layer(Color.clear, Outline, 0.6f, shape(p + shadow) - stroke);
            c = Layer(c, Outline, 1f, shape(p) - stroke);
            return Layer(c, RimCream, 1f, shape(p));
        }

        /// <summary>點到線段的距離。</summary>
        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - a - ab * t).magnitude;
        }

        /// <summary>d＝到分界的距離（px，外側為正）：分界內是 inside，外是 outside，分界 1px 反鋸齒。</summary>
        private static Color Band(Color inside, Color outside, float d) => Color.Lerp(inside, outside, Mathf.Clamp01(0.5f + d));

        private static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * alpha);

        /// <summary>把 color（不透明度再乘 alpha）疊在 dst 上；d＝到形狀邊緣帶正負號的距離（px，內部為負），邊緣 1px 反鋸齒。</summary>
        private static Color Layer(Color dst, Color color, float alpha, float d)
        {
            var a = color.a * alpha * Mathf.Clamp01(0.5f - d);
            if (a <= 0f)
                return dst;
            var outA = a + dst.a * (1f - a);
            if (outA <= 0f)
                return Color.clear;
            var rgb = ((Vector4)color * a + (Vector4)dst * (dst.a * (1f - a))) / outA;
            return new Color(rgb.x, rgb.y, rgb.z, outA);
        }
        #endregion
    }
}
