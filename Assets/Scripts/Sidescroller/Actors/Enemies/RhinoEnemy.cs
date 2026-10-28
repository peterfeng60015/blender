using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 保全犀牛（design.md §7.5）：每 1.5 秒轉向主角（轉身前停 0.4 秒）；主角在前方時慢慢逼近；
    /// 距離 1.6 內：預備 0.5 秒 → 盾牌突進 1.5 單位。
    /// 盾：正面來的甩包、彈弓擋下（0 傷害、Clank）；背後正常受傷；滾殼正面撞＝破盾，暈 2.0 秒，暈的時候任何方向都能打。
    /// </summary>
    public sealed class RhinoEnemy : EnemyActor
    {
        private enum State { Walk, Turning, Windup, Lunge, Recover, Stunned }

        private const float WalkSpeed = 1.4f;
        private const float TurnInterval = 1.5f;
        private const float TurnPause = 0.4f;
        private const float WakeRange = 10f;
        private const float WakeHeight = 3f;
        private const float AttackRange = 1.6f;
        private const float AttackHeight = 1.5f;
        private const float WindupTime = 0.5f;
        private const float LungeDistance = 1.5f;
        private const float LungeTime = 0.25f;
        private const float RecoverTime = 0.6f;
        private const float StunTime = 2f;
        private const float LungeKnockback = 1.6f; // 突進擊退加大（HitInfo.Knockback 倍率）
        private static readonly Vector2 LungeBox = new Vector2(0.8f, 1.4f);
        private static readonly string[] RhinoActions = { "Idle", "Walk", "Attack", "Hurt" };

        private State _state;
        private float _timer;
        private float _turnTimer = TurnInterval;
        private bool _lungeHit;

        protected override string ArtName => "SsRhino";
        protected override string[] Actions => RhinoActions;
        protected override string DefaultAction => "Idle";
        protected override string MoveAction => "Walk";
        protected override Vector2 BodySize => new Vector2(1.4f, 1.6f);
        protected override int StartHp => 5;

        /// <summary>暈眩中（測試與除錯用）。</summary>
        public bool IsStunned => _state == State.Stunned;

        protected override void OnAiInterrupted()
        {
            if (_state == State.Stunned && !Dead)
                return; // 暈眩照算完，演出停住只是不動
            _state = State.Walk;
            _timer = 0f;
            _turnTimer = TurnInterval;
        }

        protected override bool AcceptHit(HitInfo hit)
        {
            if (_state == State.Stunned)
                return true;
            var fromFront = hit.Direction == 0 || (hit.Direction > 0 ? 1 : -1) == -Facing;
            if (!fromFront)
                return true;
            if (hit.BreaksShield)
            {
                _state = State.Stunned;
                _timer = StunTime;
                PlayAnim("Hurt", true, true);
                return true;
            }
            FxPlayer.Spawn(FxParent, "SsFx_Clank", Feet + new Vector2(Facing * (BodySize.x * 0.5f + 0.1f), 0.9f), Facing);
            return false;
        }

        protected override void TickAi(float dt)
        {
            var hasHero = TryGetHero(out var hero);
            var dx = hero.x - Feet.x;
            var dy = hero.y - Feet.y;
            var awake = hasHero && Mathf.Abs(dx) <= WakeRange && Mathf.Abs(dy) <= WakeHeight;
            var inFront = (dx >= 0f ? 1 : -1) == Facing;

            switch (_state)
            {
                case State.Walk:
                    _turnTimer -= dt;
                    if (_turnTimer <= 0f)
                    {
                        _turnTimer = TurnInterval;
                        if (awake && !inFront && Mathf.Abs(dx) > 0.2f)
                        {
                            _state = State.Turning;
                            _timer = TurnPause;
                            SetVx(0f);
                            PlayAnim("Idle", true);
                            break;
                        }
                    }
                    if (awake && inFront && Mathf.Abs(dx) <= AttackRange + BodySize.x * 0.5f && Mathf.Abs(dy) < AttackHeight)
                    {
                        _state = State.Windup;
                        _timer = WindupTime;
                        SetVx(0f);
                        PlayAnim("Attack", false, true);
                        break;
                    }
                    if (awake && inFront && IsGrounded() && !WallAhead() && GroundAhead())
                    {
                        SetVx(Facing * WalkSpeed);
                        PlayAnim("Walk", true);
                    }
                    else
                    {
                        SetVx(0f);
                        PlayAnim("Idle", true);
                    }
                    break;

                case State.Turning:
                    SetVx(0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        Turn();
                        _state = State.Walk;
                    }
                    break;

                case State.Windup:
                    SetVx(0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _state = State.Lunge;
                        _timer = LungeTime;
                        _lungeHit = false;
                    }
                    break;

                case State.Lunge:
                    if (WallAhead() || !GroundAhead())
                        SetVx(0f);
                    else
                        SetVx(Facing * LungeDistance / LungeTime);
                    if (!_lungeHit)
                    {
                        var center = Feet + new Vector2(Facing * (BodySize.x * 0.5f + LungeBox.x * 0.5f - 0.1f), 0.8f);
                        _lungeHit = HitHeroInBox(center, LungeBox, 1, LungeKnockback);
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        SetVx(0f);
                        _state = State.Recover;
                        _timer = RecoverTime;
                        PlayAnim("Idle", true);
                    }
                    break;

                case State.Recover:
                    SetVx(0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                        _state = State.Walk;
                    break;

                case State.Stunned:
                    SetVx(0f);
                    PlayAnim("Hurt", true);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _state = State.Walk;
                        _turnTimer = TurnPause; // 醒來很快就轉向主角
                        PlayAnim("Idle", true);
                    }
                    break;
            }
        }
    }
}
