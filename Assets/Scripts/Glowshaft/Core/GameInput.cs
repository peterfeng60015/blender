using UnityEngine;
using UnityEngine.InputSystem;

namespace Glowshaft
{
    /// <summary>
    /// 輸入集中讀取（新版 Input System）：WASD 移動、滑鼠瞄準、左鍵射擊、R 或左鍵重來。
    /// <see cref="Simulated"/> 為 true 時改讀 Sim* 欄位，給自動化測試驅動用（正式遊戲不會設）。
    /// </summary>
    public static class GameInput
    {
        public static bool Simulated;
        public static Vector2 SimMove;
        public static Vector2 SimAimWorld;
        public static bool SimFire;
        /// <summary>設 true 代表「這一幀按下重來」，被讀取一次後自動清掉。</summary>
        public static bool SimRestart;

        /// <summary>清掉模擬輸入（每次開機呼叫；不重載 domain 時 static 會跨 Play 保留）。</summary>
        public static void Reset()
        {
            Simulated = false;
            SimMove = Vector2.zero;
            SimAimWorld = Vector2.zero;
            SimFire = false;
            SimRestart = false;
        }

        public static Vector2 Move
        {
            get
            {
                if (Simulated)
                    return Vector2.ClampMagnitude(SimMove, 1f);
                var kb = Keyboard.current;
                if (kb == null)
                    return Vector2.zero;
                var v = Vector2.zero;
                if (kb.wKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed) v.y -= 1f;
                if (kb.dKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed) v.x -= 1f;
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        /// <summary>滑鼠在世界座標的位置；沒有滑鼠時回傳 false。</summary>
        public static bool TryGetAimWorld(Camera cam, out Vector2 world)
        {
            if (Simulated)
            {
                world = SimAimWorld;
                return true;
            }
            var mouse = Mouse.current;
            if (mouse == null || cam == null)
            {
                world = default;
                return false;
            }
            var screen = mouse.position.ReadValue();
            world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            return true;
        }

        public static bool Fire
        {
            get
            {
                if (Simulated)
                    return SimFire;
                var mouse = Mouse.current;
                return mouse != null && mouse.leftButton.isPressed;
            }
        }

        public static bool RestartPressed
        {
            get
            {
                if (Simulated)
                {
                    var pressed = SimRestart;
                    SimRestart = false;
                    return pressed;
                }
                var kb = Keyboard.current;
                var mouse = Mouse.current;
                return (kb != null && kb.rKey.wasPressedThisFrame) ||
                       (mouse != null && mouse.leftButton.wasPressedThisFrame);
            }
        }
    }
}
