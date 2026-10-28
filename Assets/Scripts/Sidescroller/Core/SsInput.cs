using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Sidescroller
{
    /// <summary>
    /// 輸入集中讀取（新版 Input System；鍵位見 design.md §7.1）。手把只讀按鈕的 isPressed／wasPressedThisFrame，
    /// 不呼叫 ReadValue&lt;T&gt;()（避開 AOT 泛型）。
    /// <see cref="Simulated"/> 為 true 時改讀 Sim 欄位，給自動化測試用：按鍵類的 Sim 欄位只在「設定成 true 的那一幀」
    /// 算按下（同一幀所有讀取者都讀得到），之後只算按住。
    /// </summary>
    public static class SsInput
    {
        public static bool Simulated;
        public static float SimMoveX;

        #region 模擬按鍵（設定時記下 frameCount）
        private static readonly bool[] SimValue = new bool[9];
        private static readonly int[] SimFrame = new int[9];
        private const int KUp = 0, KDown = 1, KJump = 2, KMelee = 3, KShoot = 4, KRoll = 5, KSwitch = 6, KConfirm = 7, KPause = 8;

        public static bool SimUp { get => SimValue[KUp]; set => SetSim(KUp, value); }
        public static bool SimDown { get => SimValue[KDown]; set => SetSim(KDown, value); }
        public static bool SimJump { get => SimValue[KJump]; set => SetSim(KJump, value); }
        public static bool SimMelee { get => SimValue[KMelee]; set => SetSim(KMelee, value); }
        public static bool SimShoot { get => SimValue[KShoot]; set => SetSim(KShoot, value); }
        public static bool SimRoll { get => SimValue[KRoll]; set => SetSim(KRoll, value); }
        public static bool SimSwitch { get => SimValue[KSwitch]; set => SetSim(KSwitch, value); }
        public static bool SimConfirm { get => SimValue[KConfirm]; set => SetSim(KConfirm, value); }
        public static bool SimPause { get => SimValue[KPause]; set => SetSim(KPause, value); }

        private static void SetSim(int key, bool value)
        {
            SimValue[key] = value;
            SimFrame[key] = value ? Time.frameCount : -1;
        }

        private static bool SimPressed(int key) => SimValue[key] && SimFrame[key] == Time.frameCount;
        #endregion

        /// <summary>清模擬輸入（SidescrollerGame.Boot 呼叫；不重載 domain 時 static 會跨 Play 保留）。</summary>
        public static void Reset()
        {
            Simulated = false;
            SimMoveX = 0f;
            for (var i = 0; i < SimValue.Length; i++)
            {
                SimValue[i] = false;
                SimFrame[i] = -1;
            }
        }

        #region 讀值
        /// <summary>-1、0、+1。</summary>
        public static float MoveX
        {
            get
            {
                if (Simulated)
                    return Mathf.Clamp(Mathf.Round(SimMoveX), -1f, 1f);
                var kb = Keyboard.current;
                var pad = Gamepad.current;
                var x = 0f;
                if (Held(kb?.aKey) || Held(kb?.leftArrowKey) || Held(pad?.dpad.left) || Held(pad?.leftStick.left)) x -= 1f;
                if (Held(kb?.dKey) || Held(kb?.rightArrowKey) || Held(pad?.dpad.right) || Held(pad?.leftStick.right)) x += 1f;
                return x;
            }
        }

        public static bool UpHeld => Simulated ? SimUp : UpRaw(false);
        public static bool DownHeld => Simulated ? SimDown : DownRaw(false);

        /// <summary>這一幀按下。</summary>
        public static bool JumpPressed => Simulated ? SimPressed(KJump) : Pressed(Keyboard.current?.spaceKey) || Pressed(Gamepad.current?.buttonSouth);
        public static bool JumpHeld => Simulated ? SimJump : Held(Keyboard.current?.spaceKey) || Held(Gamepad.current?.buttonSouth);
        public static bool MeleePressed => Simulated ? SimPressed(KMelee) : Pressed(Keyboard.current?.jKey) || Pressed(Gamepad.current?.buttonWest);
        public static bool ShootPressed => Simulated ? SimPressed(KShoot) : Pressed(Keyboard.current?.kKey) || Pressed(Gamepad.current?.buttonNorth);

        public static bool RollPressed => Simulated
            ? SimPressed(KRoll)
            : Pressed(Keyboard.current?.lKey) || Pressed(Keyboard.current?.leftShiftKey) || Pressed(Gamepad.current?.rightShoulder);

        public static bool SwitchCostumePressed => Simulated ? SimPressed(KSwitch) : Pressed(Keyboard.current?.cKey) || Pressed(Gamepad.current?.leftShoulder);

        /// <summary>Space、Enter、J、手把 South。</summary>
        public static bool ConfirmPressed
        {
            get
            {
                if (Simulated)
                    return SimPressed(KConfirm);
                var kb = Keyboard.current;
                return Pressed(kb?.spaceKey) || Pressed(kb?.enterKey) || Pressed(kb?.numpadEnterKey) || Pressed(kb?.jKey) ||
                       Pressed(Gamepad.current?.buttonSouth);
            }
        }

        /// <summary>Esc、手把 Start。</summary>
        public static bool PausePressed => Simulated ? SimPressed(KPause) : Pressed(Keyboard.current?.escapeKey) || Pressed(Gamepad.current?.startButton);

        /// <summary>W、↑、手把上。</summary>
        public static bool MenuUpPressed => Simulated ? SimPressed(KUp) : UpRaw(true);
        /// <summary>S、↓、手把下。</summary>
        public static bool MenuDownPressed => Simulated ? SimPressed(KDown) : DownRaw(true);
        #endregion

        #region 內部
        private static bool UpRaw(bool pressed)
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return Check(kb?.wKey, pressed) || Check(kb?.upArrowKey, pressed) || Check(pad?.dpad.up, pressed) || Check(pad?.leftStick.up, pressed);
        }

        private static bool DownRaw(bool pressed)
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return Check(kb?.sKey, pressed) || Check(kb?.downArrowKey, pressed) || Check(pad?.dpad.down, pressed) || Check(pad?.leftStick.down, pressed);
        }

        private static bool Check(ButtonControl button, bool pressed) => pressed ? Pressed(button) : Held(button);
        private static bool Held(ButtonControl button) => button != null && button.isPressed;
        private static bool Pressed(ButtonControl button) => button != null && button.wasPressedThisFrame;
        #endregion
    }
}
