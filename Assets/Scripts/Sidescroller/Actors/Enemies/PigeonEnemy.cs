using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 監工鴿（design.md §7.5）：停在出生高度上下浮動 ±0.5，水平以 2.5 追主角；
    /// 主角在正下方（水平差 &lt; 1）時每 2.0 秒丟一顆公文炸彈。只有 trigger 接觸框（不站地形、不受重力）。
    /// </summary>
    public sealed class PigeonEnemy : EnemyActor
    {
        private const float FlySpeed = 2.5f;
        private const float BobAmplitude = 0.5f;
        private const float BobSpeed = 2.2f;
        private const float HeightGain = 6f;
        private const float WakeRange = 11f;
        private const float WakeHeight = 10f;
        private const float StopRange = 0.3f;
        private const float DropRange = 1f;
        private const float DropInterval = 2f;
        private const float FirstDropDelay = 0.6f;
        private static readonly string[] PigeonActions = { "Fly", "Attack", "Hurt" };

        private float _baseY;
        private float _bobTime;
        private float _dropTimer;
        private bool _attacking;

        protected override string ArtName => "SsPigeon";
        protected override string[] Actions => PigeonActions;
        protected override string DefaultAction => "Fly";
        protected override string MoveAction => "Fly";
        protected override Vector2 BodySize => new Vector2(0.9f, 0.9f);
        protected override int StartHp => 1;
        protected override float GravityScale => 0f;
        protected override bool HasSolidBody => false;
        protected override Vector2 ContactSize => BodySize;

        protected override void OnInit(SpawnPoint spawn)
        {
            _baseY = spawn.Position.y;
            _dropTimer = FirstDropDelay;
        }

        protected override void OnAiInterrupted()
        {
            _attacking = false;
            if (_dropTimer < FirstDropDelay)
                _dropTimer = FirstDropDelay;
        }

        protected override void HoldStill()
        {
            if (Body != null && Body.simulated)
                Body.linearVelocity = Vector2.zero;
        }

        protected override void OnScriptedMoveStep() => Body.linearVelocity = new Vector2(Body.linearVelocity.x, 0f);

        protected override void TickAi(float dt)
        {
            _bobTime += dt;
            var pos = Feet;
            var targetY = _baseY + Mathf.Sin(_bobTime * BobSpeed) * BobAmplitude;
            var vy = (targetY - pos.y) * HeightGain;
            var vx = 0f;

            if (TryGetHero(out var hero) && Mathf.Abs(hero.x - pos.x) <= WakeRange && Mathf.Abs(hero.y - pos.y) <= WakeHeight)
            {
                var dx = hero.x - pos.x;
                if (Mathf.Abs(dx) > StopRange)
                {
                    vx = Mathf.Sign(dx) * FlySpeed;
                    Face(dx > 0f ? 1 : -1);
                }
                _dropTimer -= dt;
                if (_dropTimer <= 0f && Mathf.Abs(dx) < DropRange && hero.y < pos.y - 0.5f)
                {
                    _dropTimer = DropInterval;
                    OfficeBomb.Spawn(this, Ctx, FxParent, pos + new Vector2(0f, 0.1f));
                    PlayAnim("Attack", false, true);
                    _attacking = true;
                }
            }

            if (_attacking && Anim.IsDone)
                _attacking = false;
            if (!_attacking)
                PlayAnim("Fly", true);
            Body.linearVelocity = new Vector2(vx, vy);
        }
    }
}
