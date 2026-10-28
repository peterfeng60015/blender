using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 鏡頭（interfaces.md §4.2）。掛在相機物件上，整局一個，重生不重建。
    /// 跟隨：目標腳底帶死區（水平 ±0.5、往上 1.5／往下 1），鏡頭中心在腳底上方 2.5，面向那側多看 2 單位；平滑跟上，邊緣不超出邊界。
    /// 時間一律用 scaled time（暫停時鏡頭也停）。在 LateUpdate 移動，移動完送 <see cref="Moved"/>（視差背景用）。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        #region 調校
        private const float DeadZoneX = 0.5f;
        private const float DeadZoneUp = 1.5f;
        private const float DeadZoneDown = 1f;
        /// <summary>鏡頭中心在目標腳底上方多高。</summary>
        private const float FocusHeight = 2.5f;
        private const float LookAhead = 2f;
        private const float LookAheadSmooth = 0.5f;
        private const float FollowSmooth = 0.12f;
        /// <summary>目標沒有 ICutsceneActor 時，用位移判斷面向：每幀移動超過這個量才換向。</summary>
        private const float FacingMoveEpsilon = 0.005f;
        #endregion

        private Transform _target;
        private ICutsceneActor _targetActor;
        private bool _following;
        private Vector2 _focus;
        private float _lookAhead;
        private float _lookAheadVel;
        private int _movedFacing = 1;
        private float _lastTargetX;

        private Rect _bounds;
        private bool _hasBounds;
        private Rect _lockArea;
        private bool _locked;

        /// <summary>鏡頭中心（不含震動）；<see cref="_goal"/> 是不跟隨時要停的位置。</summary>
        private Vector2 _pos;
        private Vector2 _vel;
        private Vector2 _goal;
        private bool _panning;
        private Vector2 _panPos;
        private int _panVersion;
        private int _zoomVersion;

        private float _shakeStrength;
        private float _shakeTime;
        private float _shakeLeft;
        private Vector2 _shakeOffset;
        private readonly System.Random _random = new System.Random();

        /// <summary>每次鏡頭位置更新完觸發（level 的視差背景訂閱）。</summary>
        internal event Action Moved;

        public static CameraRig Create(Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
            var rig = camera.gameObject.AddComponent<CameraRig>();
            rig.Camera = camera;
            rig._pos = rig._goal = camera.transform.position;
            return rig;
        }

        public Camera Camera { get; private set; }

        public void SetBounds(Rect bounds)
        {
            _bounds = bounds;
            _hasBounds = true;
        }

        /// <summary>開始跟隨 target（會停掉進行中的平移）。snap=true 立刻到位，否則平滑跟上。</summary>
        public void Follow(Transform target, bool snap)
        {
            StopPan();
            _target = target;
            _following = target != null;
            if (target == null)
            {
                _targetActor = null;
                return;
            }
            _targetActor = target.GetComponentInParent<ICutsceneActor>();
            if (!IsAlive(_targetActor))
                _targetActor = null;
            _focus = target.position;
            _lastTargetX = target.position.x;
            if (!snap)
                return;
            _lookAhead = TargetFacing() * LookAhead;
            _lookAheadVel = 0f;
            _pos = _goal = Clamp(FollowGoal());
            _vel = Vector2.zero;
            Apply();
        }

        /// <summary>Boss 房：改用 area 當邊界（鏡頭平滑移進去）。</summary>
        public void LockTo(Rect area)
        {
            _lockArea = area;
            _locked = true;
        }

        /// <summary>回到 SetBounds 的邊界。</summary>
        public void Unlock() => _locked = false;

        /// <summary>
        /// 停止跟隨，平移到 center（會被邊界限制）。之後要 <see cref="Follow"/> 才會再跟隨。
        /// 被 ct 取消時丟 OperationCanceledException，鏡頭停在當下位置；被新的平移或 Follow 取代時安靜結束。
        /// </summary>
        public async UniTask PanToAsync(Vector2 center, float time, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _following = false;
            var version = ++_panVersion;
            var from = _pos;
            _panPos = from;
            _panning = true;
            try
            {
                var t = 0f;
                while (time > 0f && t < time)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    if (this == null || version != _panVersion)
                        return;
                    t += Time.deltaTime;
                    _panPos = Vector2.LerpUnclamped(from, center, Ease(t / time));
                    _goal = _panPos;
                }
                _panPos = _goal = center;
                if (time <= 0f && this != null)
                {
                    _pos = Clamp(center);
                    _vel = Vector2.zero;
                    Apply();
                }
            }
            finally
            {
                if (this != null && version == _panVersion)
                    _panning = false;
            }
        }

        /// <summary>改變正交 size。被 ct 取消時丟 OperationCanceledException、size 停在當下；被新的縮放或 ResetView 取代時安靜結束。</summary>
        public async UniTask ZoomAsync(float size, float time, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Camera == null)
                return;
            size = Mathf.Max(0.5f, size);
            var version = ++_zoomVersion;
            var from = Camera.orthographicSize;
            var t = 0f;
            while (time > 0f && t < time)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (this == null || Camera == null || version != _zoomVersion)
                    return;
                t += Time.deltaTime;
                Camera.orthographicSize = Mathf.Lerp(from, size, Ease(t / time));
            }
            if (this != null && Camera != null && version == _zoomVersion)
                Camera.orthographicSize = size;
        }

        /// <summary>震動 strength（單位）、time 秒，強度隨時間線性衰減。比進行中的震動弱時不取代。</summary>
        public void Shake(float strength, float time)
        {
            if (strength <= 0f || time <= 0f)
                return;
            var current = _shakeLeft > 0f ? _shakeStrength * (_shakeLeft / _shakeTime) : 0f;
            if (strength < current)
                return;
            _shakeStrength = strength;
            _shakeTime = _shakeLeft = time;
        }

        /// <summary>
        /// Unlock、size 回 <see cref="SsIds.CameraSize"/>、停止震動、停掉進行中的平移與縮放；跟隨對象不變。
        /// 注意：不會恢復跟隨。平移（PanToAsync）之後要再呼叫 <see cref="Follow"/> 鏡頭才會跟著目標走。
        /// </summary>
        public void ResetView()
        {
            Unlock();
            StopPan();
            _zoomVersion++;
            _shakeLeft = 0f;
            _shakeOffset = Vector2.zero;
            if (Camera != null)
                Camera.orthographicSize = SsIds.CameraSize;
        }

        #region 每幀
        private void LateUpdate()
        {
            if (Camera == null)
                return;
            var dt = Time.deltaTime;

            if (_following && _target == null)
                _following = false;
            if (_following)
                UpdateFollow(dt);

            if (_panning)
            {
                _pos = Clamp(_panPos);
                _vel = Vector2.zero;
            }
            else if (dt > 0f)
            {
                // 邊界改變（LockTo、縮放）時也是平滑移進去，不會瞬間跳
                _pos = Vector2.SmoothDamp(_pos, Clamp(_goal), ref _vel, FollowSmooth, Mathf.Infinity, dt);
            }

            UpdateShake(dt);
            Apply();
        }

        private void UpdateFollow(float dt)
        {
            Vector2 t = _target.position;
            if (t.x > _focus.x + DeadZoneX)
                _focus.x = t.x - DeadZoneX;
            else if (t.x < _focus.x - DeadZoneX)
                _focus.x = t.x + DeadZoneX;
            if (t.y > _focus.y + DeadZoneUp)
                _focus.y = t.y - DeadZoneUp;
            else if (t.y < _focus.y - DeadZoneDown)
                _focus.y = t.y + DeadZoneDown;

            if (dt > 0f)
                _lookAhead = Mathf.SmoothDamp(_lookAhead, TargetFacing() * LookAhead, ref _lookAheadVel, LookAheadSmooth, Mathf.Infinity, dt);
            _goal = FollowGoal();
        }

        private void UpdateShake(float dt)
        {
            if (_shakeLeft <= 0f)
            {
                _shakeOffset = Vector2.zero;
                return;
            }
            // 暫停（dt＝0）時保持當下的偏移，不在原地亂抖
            if (dt <= 0f)
                return;
            _shakeLeft -= dt;
            if (_shakeLeft <= 0f)
            {
                _shakeOffset = Vector2.zero;
                return;
            }
            var amp = _shakeStrength * (_shakeLeft / _shakeTime);
            var angle = (float)(_random.NextDouble() * Math.PI * 2.0);
            _shakeOffset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * amp;
        }

        private void Apply()
        {
            var p = _pos + _shakeOffset;
            transform.position = new Vector3(p.x, p.y, transform.position.z);
            Moved?.Invoke();
        }
        #endregion

        #region 小工具
        private Vector2 FollowGoal() => new Vector2(_focus.x + _lookAhead, _focus.y + FocusHeight);

        private int TargetFacing()
        {
            if (IsAlive(_targetActor))
                return _targetActor.Facing < 0 ? -1 : 1;
            if (_target == null)
                return _movedFacing;
            var x = _target.position.x;
            if (x > _lastTargetX + FacingMoveEpsilon)
                _movedFacing = 1;
            else if (x < _lastTargetX - FacingMoveEpsilon)
                _movedFacing = -1;
            _lastTargetX = x;
            return _movedFacing;
        }

        /// <summary>讓鏡頭邊緣不超出目前的邊界（鎖定中用 Boss 房）；邊界比畫面小時置中。</summary>
        private Vector2 Clamp(Vector2 center)
        {
            if (Camera == null || (!_locked && !_hasBounds))
                return center;
            var b = _locked ? _lockArea : _bounds;
            var halfH = Camera.orthographicSize;
            var halfW = halfH * Camera.aspect;
            center.x = b.width > halfW * 2f ? Mathf.Clamp(center.x, b.xMin + halfW, b.xMax - halfW) : b.center.x;
            center.y = b.height > halfH * 2f ? Mathf.Clamp(center.y, b.yMin + halfH, b.yMax - halfH) : b.center.y;
            return center;
        }

        private void StopPan()
        {
            _panVersion++;
            _panning = false;
        }

        /// <summary>介面參考可能指向已刪除的元件（Unity 的假 null），要用 UnityEngine.Object 的比較。</summary>
        private static bool IsAlive(ICutsceneActor actor) =>
            actor != null && !(actor is UnityEngine.Object o && o == null);

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
        #endregion
    }
}
