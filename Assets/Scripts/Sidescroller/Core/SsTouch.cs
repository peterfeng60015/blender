using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Sidescroller
{
    /// <summary>
    /// 觸控讀取（<see cref="SsInput"/> 內部用）。每幀第一次被讀時從 <c>Touchscreen.current</c> 算一次，不靠腳本執行順序。
    /// 手指按下的那一幀由 <see cref="Pad"/>（<see cref="TouchControlsView.Classify"/>）決定綁到搖桿、按鈕或「點擊」，放開前不換目標：
    /// 點擊推進對話、點選單的那根手指之後一直按著，也不會變成跳躍或攻擊。
    /// 只用 press 的 isPressed／wasPressedThisFrame、position.x/y 與 touchId 的非泛型 ReadValue（避開 AOT 泛型）。
    /// </summary>
    internal static class SsTouch
    {
        /// <summary>手指的綁定目標；≥0＝按鈕序號。</summary>
        public const int None = -1, Tap = -2, Stick = -3;
        public const int BJump = 0, BMelee = 1, BShoot = 2, BRoll = 3, BSwitch = 4, BPause = 5, ButtonCount = 6;

        private const int MaxSlots = 10;
        /// <summary>觸控後這段時間（秒，不受 timeScale 影響）內忽略滑鼠點擊：瀏覽器與 Device Simulator 會把觸控再模擬成滑鼠。</summary>
        private const float MouseGuard = 0.6f;
        /// <summary>搖桿推出（以推動範圍為 1）超過這個值才算左右。</summary>
        private const float DeadZoneX = 0.3f;
        /// <summary>搖桿推出超過這個值才算上／下（約 30° 以上）。</summary>
        private const float ThresholdY = 0.5f;

        private static readonly int[] SlotId = new int[MaxSlots];
        /// <summary>
        /// 每個 slot 最後看過的觸控（touchId＋開始時間，Reset 不清）：用來認出「同一幀按下又放開」留下的結束狀態。
        /// 一起比開始時間，是因為有些平台會重複使用 touchId（例如單指每次都是同一個 id）。
        /// </summary>
        private static readonly int[] LastSeenId = new int[MaxSlots];
        private static readonly double[] LastSeenStart = new double[MaxSlots];
        /// <summary>LastSeen 是對哪一個觸控裝置記的；換裝置、開機或上一幀沒有計算時，先記下目前狀態，不當成新的點擊。</summary>
        private static Touchscreen _seenDevice;
        /// <summary>上一次真的算過的幀（Reset 那一幀的跳過不算）。</summary>
        private static int _lastEvaluatedFrame = int.MinValue;
        private static readonly int[] SlotTarget = new int[MaxSlots];
        private static readonly bool[] ButtonHeld = new bool[ButtonCount];
        private static readonly bool[] ButtonDown = new bool[ButtonCount];

        private static int _frame;
        private static bool _detected;
        private static bool _active;
        private static float _lastTouchTime = float.NegativeInfinity;
        private static bool _tapped;
        private static Vector2 _tapPos;
        private static int _stickSlot = -1;
        private static Vector2 _stickOrigin;
        private static Vector2 _stickPos;
        private static float _moveX;
        private static bool _up;
        private static bool _down;

        /// <summary>目前場景的觸控按鈕版面（<see cref="StoryUi"/> 建立時登記）；null＝沒有按鈕，所有觸控都算點擊。</summary>
        internal static TouchControlsView Pad;

        static SsTouch()
        {
            Reset();
            ForgetDevice();
        }

        /// <summary>
        /// 開機時呼叫（GameSelectMenu.Boot；AppBootstrap 每次啟動只呼叫一次）：下次讀取時重新偵測觸控模式。
        /// 不重載 domain 時，上一次 Play 的觸控模式不能沿用。
        /// </summary>
        public static void ForgetDevice()
        {
            _detected = false;
            _active = false;
            _seenDevice = null;
        }

        /// <summary>
        /// 清觸控的按鍵與搖桿狀態（<see cref="SsInput.Reset"/> 呼叫：每次 Boot、Shutdown）：手指綁定、按鈕、搖桿、版面登記。
        /// 之後還按著的手指一律不算數，要放開重按。觸控模式（<see cref="Active"/>）不關：「再玩一次」或回選單後照樣顯示觸控 UI；
        /// 只有開機時由 <see cref="ForgetDevice"/> 重新偵測。
        /// </summary>
        public static void Reset()
        {
            for (var i = 0; i < MaxSlots; i++)
            {
                SlotId[i] = int.MinValue;
                SlotTarget[i] = None;
            }
            for (var i = 0; i < ButtonCount; i++)
            {
                ButtonHeld[i] = false;
                ButtonDown[i] = false;
            }
            // 這一幀不再計算：Reset 常發生在幀中（點選單→開新局），同一幀內再讀不能把剛按下的手指重新綁定
            _frame = Time.frameCount;
            _lastTouchTime = float.NegativeInfinity;
            _tapped = false;
            _stickSlot = -1;
            _moveX = 0f;
            _up = false;
            _down = false;
            Pad = null;
        }

        #region 讀值
        /// <summary>觸控模式：行動裝置開機時、或任何一次觸控後為 true；按了鍵盤（Esc＝Android 返回鍵除外）或手把後變回 false。</summary>
        public static bool Active
        {
            get
            {
                Evaluate();
                // Reset 那一幀不計算，但開機偵測照做：同一幀建立的選單提示也能拿到正確的模式
                DetectDevice();
                return _active;
            }
        }

        /// <summary>不觸發計算（給 <see cref="TouchControlsView.Classify"/> 在計算途中用）。</summary>
        internal static bool ActiveNoEvaluate => _active;

        public static float MoveX
        {
            get
            {
                Evaluate();
                return _moveX;
            }
        }

        public static bool UpHeld
        {
            get
            {
                Evaluate();
                return _up;
            }
        }

        public static bool DownHeld
        {
            get
            {
                Evaluate();
                return _down;
            }
        }

        public static bool Held(int button)
        {
            Evaluate();
            return ButtonHeld[button];
        }

        /// <summary>這一幀按下。</summary>
        public static bool Pressed(int button)
        {
            Evaluate();
            return ButtonDown[button];
        }

        /// <summary>這一幀有手指按在按鈕與搖桿以外的地方（同一幀多根只回報第一根）。</summary>
        public static bool Tapped(out Vector2 screenPos)
        {
            Evaluate();
            screenPos = _tapPos;
            return _tapped;
        }

        /// <summary>剛觸控過：這段期間的滑鼠點擊視為觸控模擬出來的，不處理。</summary>
        public static bool MouseSuppressed
        {
            get
            {
                Evaluate();
                return Time.realtimeSinceStartup - _lastTouchTime < MouseGuard;
            }
        }

        /// <summary>搖桿的手指（畫面用）：起點與目前位置，螢幕座標。</summary>
        internal static bool StickActive(out Vector2 origin, out Vector2 current)
        {
            Evaluate();
            origin = _stickOrigin;
            current = _stickPos;
            return _stickSlot >= 0;
        }
        #endregion

        #region 計算
        private static void Evaluate()
        {
            var frame = Time.frameCount;
            if (frame == _frame)
                return;
            _frame = frame;
            var continuous = _lastEvaluatedFrame == frame - 1;
            _lastEvaluatedFrame = frame;
            for (var i = 0; i < ButtonCount; i++)
            {
                ButtonHeld[i] = false;
                ButtonDown[i] = false;
            }
            _tapped = false;

            var ts = Touchscreen.current;
            DetectDevice();
            DetectOtherDevices();

            var n = 0;
            if (ts != null)
            {
                var touches = ts.touches;
                n = Mathf.Min(touches.Count, MaxSlots);
                if (ts != _seenDevice || !continuous)
                {
                    // 開機、換了觸控裝置、或有幾幀沒人讀（載入中、Reset 那一幀）：slot 裡現有的觸控先記成「看過」，
                    // 那段期間完成的快點不在新畫面補算
                    _seenDevice = ts;
                    for (var i = 0; i < n; i++)
                    {
                        LastSeenId[i] = touches[i].touchId.ReadValue();
                        LastSeenStart[i] = touches[i].startTime.ReadValue();
                    }
                }
                for (var i = 0; i < n; i++)
                    ReadSlot(i, touches[i]);
            }
            for (var i = n; i < MaxSlots; i++)
                Release(i);
            ComputeStick();
        }

        private static void ReadSlot(int slot, TouchControl touch)
        {
            var pressed = touch.press.isPressed;
            var began = touch.press.wasPressedThisFrame;
            var id = touch.touchId.ReadValue();
            var start = touch.startTime.ReadValue();
            if (!pressed && !began)
            {
                // 同一幀內按下又放開（adb input tap、Android 上極快的輕點）時 Input System 可能沒記到「這一幀按下」，
                // slot 只留下結束狀態；沒看過的觸控（touchId 或開始時間不同）就補當成一次按下＋放開
                if (id == 0 || (id == LastSeenId[slot] && start == LastSeenStart[slot]))
                {
                    Release(slot);
                    return;
                }
                began = true;
            }
            LastSeenId[slot] = id;
            LastSeenStart[slot] = start;
            var pos = new Vector2(touch.position.x.ReadValue(), touch.position.y.ReadValue());
            _lastTouchTime = Time.realtimeSinceStartup;
            if (began || SlotId[slot] != id)
            {
                Release(slot);
                SlotId[slot] = id;
                // 不是這一幀按下、卻沒見過的手指（Reset 前就按著，或沒被讀到按下那幀）：不算數
                SlotTarget[slot] = began ? Bind(slot, pos) : None;
            }
            if (!pressed)
            {
                // 同一幀按下又放開：按鈕的「按下」已記，不留按住
                Release(slot);
                return;
            }
            var target = SlotTarget[slot];
            if (target >= 0)
                ButtonHeld[target] = true;
            else if (target == Stick)
                _stickPos = pos;
        }

        private static int Bind(int slot, Vector2 pos)
        {
            var wasActive = _active;
            _active = true;
            var target = Pad != null ? Pad.Classify(pos) : Tap;
            // 觸控模式剛打開的這一下，按鈕還沒畫出來：不當按鈕（看不見的跳躍），搖桿可以
            if (!wasActive && target >= 0)
                target = Tap;
            if (target >= 0)
            {
                ButtonDown[target] = true;
            }
            else if (target == Stick)
            {
                if (_stickSlot >= 0)
                    return None;    // 已經有一根手指在控搖桿
                _stickSlot = slot;
                _stickOrigin = pos;
                _stickPos = pos;
            }
            else if (target == Tap && !_tapped)
            {
                _tapped = true;
                _tapPos = pos;
            }
            return target;
        }

        private static void Release(int slot)
        {
            if (_stickSlot == slot)
                _stickSlot = -1;
            SlotId[slot] = int.MinValue;
            SlotTarget[slot] = None;
        }

        /// <summary>浮動搖桿：拉超過推動範圍時起點跟著手指走，反方向一推就能轉向。</summary>
        private static void ComputeStick()
        {
            _moveX = 0f;
            _up = false;
            _down = false;
            if (_stickSlot < 0)
                return;
            var range = Pad != null ? Pad.StickRangePx : 100f;
            if (range < 1f)
                range = 1f;
            var d = _stickPos - _stickOrigin;
            var len = d.magnitude;
            if (len > range)
            {
                _stickOrigin = _stickPos - d * (range / len);
                d = _stickPos - _stickOrigin;
            }
            var v = d / range;
            if (v.x > DeadZoneX)
                _moveX = 1f;
            else if (v.x < -DeadZoneX)
                _moveX = -1f;
            _up = v.y > ThresholdY;
            _down = v.y < -ThresholdY;
        }

        /// <summary>開機後第一次讀取時偵測一次：行動裝置且有觸控螢幕就直接進觸控模式。</summary>
        private static void DetectDevice()
        {
            if (_detected)
                return;
            _detected = true;
            // UnityEngine.Device 版本在 Device Simulator 裡會回模擬裝置的值，Player 裡等於 UnityEngine.Application
            _active = Touchscreen.current != null && UnityEngine.Device.Application.isMobilePlatform;
        }

        /// <summary>用了鍵盤或手把就收起觸控 UI（Esc 不算：Android 的返回鍵會送 Esc）。</summary>
        private static void DetectOtherDevices()
        {
            if (!_active)
                return;
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame && !kb.escapeKey.wasPressedThisFrame)
            {
                _active = false;
                return;
            }
            var pad = Gamepad.current;
            if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame ||
                                pad.buttonWest.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame))
                _active = false;
        }
        #endregion
    }
}
