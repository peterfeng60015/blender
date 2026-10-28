using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 業務鼠（design.md §7.5）：地面巡邏（碰到邊緣或牆就回頭）；主角在前方 6 單位、高度差 1.5 內就追；
    /// 距離 1.2 內：預備 0.35 秒 → 揮公事包（判定 1.4 寬）→ 冷卻 1.2 秒。
    /// </summary>
    public sealed class RatEnemy : EnemyActor
    {
        private enum State { Patrol, Chase, Windup, Swing, Cooldown }

        private const float PatrolSpeed = 1.8f;
        private const float ChaseSpeed = 4.0f;
        private const float SightRange = 6f;
        private const float SightHeight = 1.5f;
        private const float LoseRange = 8f;
        private const float LoseHeight = 2.5f;
        private const float AttackRange = 1.2f;
        private const float WindupTime = 0.35f;
        private const float SwingTime = 0.15f;
        private const float CooldownTime = 1.2f;
        private const float TurnLockTime = 0.25f;
        private static readonly Vector2 SwingSize = new Vector2(1.4f, 1.0f);
        private static readonly string[] RatActions = { "Idle", "Walk", "Attack", "Hurt" };

        private State _state;
        private float _timer;
        private float _turnLock;
        private bool _swingDone;

        protected override string ArtName => "SsRat";
        protected override string[] Actions => RatActions;
        protected override string DefaultAction => "Idle";
        protected override string MoveAction => "Walk";
        protected override Vector2 BodySize => new Vector2(0.7f, 1.4f);
        protected override int StartHp => 3;

        protected override void OnAiInterrupted()
        {
            _state = State.Patrol;
            _timer = 0f;
            _swingDone = false;
        }

        protected override void TickAi(float dt)
        {
            if (_turnLock > 0f)
                _turnLock -= dt;
            var hasHero = TryGetHero(out var hero);
            var dx = hero.x - Feet.x;
            var dy = hero.y - Feet.y;
            switch (_state)
            {
                case State.Patrol:
                    if (hasHero && Mathf.Abs(dx) <= SightRange && Mathf.Abs(dy) <= SightHeight && (dx >= 0f ? 1 : -1) == Facing)
                    {
                        _state = State.Chase;
                        break;
                    }
                    if (_turnLock <= 0f && IsGrounded() && (WallAhead() || !GroundAhead()))
                    {
                        Turn();
                        _turnLock = TurnLockTime;
                    }
                    SetVx(Facing * PatrolSpeed);
                    PlayAnim("Walk", true);
                    break;

                case State.Chase:
                    if (!hasHero || Mathf.Abs(dx) > LoseRange || Mathf.Abs(dy) > LoseHeight)
                    {
                        _state = State.Patrol;
                        break;
                    }
                    if (Mathf.Abs(dx) > 0.1f)
                        Face(dx > 0f ? 1 : -1);
                    if (Mathf.Abs(dx) <= AttackRange && Mathf.Abs(dy) < SightHeight)
                    {
                        _state = State.Windup;
                        _timer = WindupTime;
                        _swingDone = false;
                        SetVx(0f);
                        PlayAnim("Attack", false, true);
                        break;
                    }
                    if (IsGrounded() && (WallAhead() || !GroundAhead()))
                    {
                        // 不追下平台、不撞牆：停在邊上等
                        SetVx(0f);
                        PlayAnim("Idle", true);
                    }
                    else
                    {
                        SetVx(Facing * ChaseSpeed);
                        PlayAnim("Walk", true);
                    }
                    break;

                case State.Windup:
                    SetVx(0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _state = State.Swing;
                        _timer = SwingTime;
                    }
                    break;

                case State.Swing:
                    SetVx(0f);
                    if (!_swingDone)
                    {
                        var center = Feet + new Vector2(Facing * (BodySize.x * 0.5f + SwingSize.x * 0.5f - 0.15f), 0.7f);
                        _swingDone = HitHeroInBox(center, SwingSize, 1);
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _state = State.Cooldown;
                        _timer = CooldownTime;
                        PlayAnim("Idle", true);
                    }
                    break;

                case State.Cooldown:
                    SetVx(0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                        _state = State.Chase;
                    break;
            }
        }
    }
}
