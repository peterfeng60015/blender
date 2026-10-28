using UnityEngine;

namespace Sidescroller
{
    /// <summary>主角的演出操控（interfaces.md §3.8 <see cref="ICutsceneActor"/>）。</summary>
    public sealed partial class HeroController
    {
        private static readonly string[] Actions = { "Idle", "Run", "Jump", "Melee", "Shoot", "Hurt" };

        private bool _scripted;
        private bool _scriptMoving;
        private float _scriptTargetX;
        private float _scriptSpeed;
        private float _scriptStuckTime;
        private string _actionOverride;
        private bool _actionOverrideLoop;

        public bool IsScriptedMoving => _scriptMoving;

        public void BeginScripted()
        {
            _scripted = true;
            _scriptMoving = false;
            _actionOverride = null;
            _moveX = 0f;
            _jumpPressedAt = float.NegativeInfinity;
            _jumpCutPending = false;
            if (_state == State.Melee || _state == State.Hurt)
                SetState(State.Normal);
            if (_state == State.Roll && CanStand())
            {
                EndRollShape();
                SetState(State.Normal);
            }
            if (_rb != null)
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        public void EndScripted()
        {
            _scripted = false;
            _scriptMoving = false;
            _actionOverride = null;
        }

        public void SetFacing(int dir) => _anim.SetFacing(dir);

        public void ScriptedMoveTo(float x, float speed)
        {
            _scriptTargetX = x;
            _scriptSpeed = speed > 0f ? speed : HeroStats.RunSpeed(_costume);
            _scriptMoving = true;
            _scriptStuckTime = 0f;
            _actionOverride = null;
        }

        public void Teleport(float x)
        {
            _scriptMoving = false;
            var p = new Vector2(x, _rb.position.y);
            _rb.position = p;
            transform.position = p;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        public bool PlayAction(string action, bool loop)
        {
            if (!IsAction(action))
                return false;
            _actionOverride = action;
            _actionOverrideLoop = loop;
            PlayAnim(action, loop, true);
            return true;
        }

        public float GetActionDuration(string action) =>
            IsAction(action) ? SsArt.Get(HeroStats.SpritePrefix(_costume) + "_" + action).Duration : 0f;

        private static bool IsAction(string action) => System.Array.IndexOf(Actions, action) >= 0;

        /// <summary>演出移動：以 _scriptSpeed 走到目標 x；被擋住超過 1 秒就放棄（避免演出卡住）。</summary>
        private float ScriptedVelocityX(float vx, float dt)
        {
            if (!_scriptMoving)
                return 0f;
            var dx = _scriptTargetX - _rb.position.x;
            if (Mathf.Abs(dx) <= _scriptSpeed * dt)
            {
                _scriptMoving = false;
                var p = new Vector2(_scriptTargetX, _rb.position.y);
                _rb.position = p;
                return 0f;
            }
            var dir = dx > 0f ? 1 : -1;
            _anim.SetFacing(dir);
            if (Mathf.Abs(vx) < 0.05f && _stateTime > 0.1f)
            {
                _scriptStuckTime += dt;
                if (_scriptStuckTime > 1f)
                {
                    Debug.LogWarning($"[Sidescroller.HeroController] 演出移動到 x={_scriptTargetX} 被擋住，停在 {_rb.position.x:0.00}");
                    _scriptMoving = false;
                    return 0f;
                }
            }
            else
            {
                _scriptStuckTime = 0f;
            }
            return dir * _scriptSpeed;
        }
    }
}
