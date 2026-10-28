using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// Boss 河董（design.md §7.6）。建好時 AI 停著，GameFlow 的 BossArena 處理呼叫 <see cref="StartFight"/> 才開打。
    /// 第 1 階段（40～21）：撒金幣 3 枚 → 衝撞 9 → 休息 1.0。
    /// 第 2 階段（20～0）：怒吼（Stomp＋鏡頭震動）後循環 重踩（震波）→ 撒金幣 5 枚 → 衝撞 11 → 休息 0.6。
    /// 不會被擊退；衝撞中照樣受傷；開打前不受傷。血量歸零：停止一切、播 Hurt、觸發 <see cref="Defeated"/>，物件留著給演出用。
    /// </summary>
    public sealed class BossActor : EnemyActor, IBossController
    {
        private enum Act { Wait, Spray, ChargeWindup, Charge, Dazed, StompAir, StompLand, Rest, Roar }

        private const int BossMaxHp = 40;
        private const int Phase2Hp = 20;
        private const float FirstActDelay = 0.8f;
        private const float SprayFireAt = 0.25f;
        private const float SprayAfter = 0.3f;
        private const float CoinFlightTime = 1.0f;
        private const float CoinSpacing = 1.5f;
        private const float ChargeWindupTime = 0.6f;
        private const float ChargeSpeed1 = 9f;
        private const float ChargeSpeed2 = 11f;
        private const float ChargeMaxTime = 4f;
        private const float DazedTime = 1.2f;
        private const float StompAirTime = 0.9f;
        private const float StompMaxAir = 3f;
        private const float StompRecover = 0.5f;
        private const float Rest1 = 1.0f;
        private const float Rest2 = 0.6f;
        private const float RoarMinTime = 0.8f;
        private static readonly Vector2 MouthOffset = new Vector2(1.4f, 2.6f);
        private static readonly string[] BossActions = { "Idle", "Charge", "Spray", "Stomp", "Hurt" };
        // 招式循環（Wait＝下一輪從頭）
        private static readonly Act[] Phase1Loop = { Act.Spray, Act.ChargeWindup, Act.Rest };
        private static readonly Act[] Phase2Loop = { Act.StompAir, Act.Spray, Act.ChargeWindup, Act.Rest };

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _fighting;
        private bool _stopped;
        private int _phase = 1;
        private bool _roarPending;
        private Act _act = Act.Wait;
        private int _step;
        private float _timer;
        private float _actTime;
        private bool _sprayed;
        private bool _leftGround;

        protected override string ArtName => "SsBoss";
        protected override string[] Actions => BossActions;
        protected override string DefaultAction => "Idle";
        protected override string MoveAction => "Charge";
        protected override Vector2 BodySize => new Vector2(3.2f, 4.0f);
        protected override Vector2 ContactSize => new Vector2(2.8f, 3.6f);
        protected override int StartHp => BossMaxHp;
        protected override ActorKind Kind => ActorKind.Boss;
        protected override int SortingOrder => SsSorting.Boss;
        protected override float GravityScale => 4.5f;
        protected override float HitKnockbackSpeed => 0f;

        #region IBossController
        public int Hp => HpLeft;
        public int MaxHp => BossMaxHp;
        public event Action<int, int> HpChanged;
        public event Action Defeated;

        /// <summary>目前階段（1 或 2）。</summary>
        public int Phase => _phase;
        public bool IsFighting => _fighting && !_stopped && !Dead;

        public void StartFight()
        {
            if (_fighting || _stopped || Dead)
                return;
            _fighting = true;
            BeginAct(Act.Wait, FirstActDelay);
        }

        public void StopAll()
        {
            _stopped = true;
            ClearSpawned();
            if (Body != null && Body.simulated)
                Body.linearVelocity = new Vector2(0f, Mathf.Min(0f, Body.linearVelocity.y));
            if (Anim != null && Anim.Visual != null)
                Anim.Visual.localPosition = Vector3.zero;
        }
        #endregion

        protected override void OnInit(SpawnPoint spawn)
        {
            // 河董很大，不站單向平台（鷹架木板）：重踩一律落回地面
            var root = Ctx != null ? Ctx.Parent : null;
            if (root == null || BodyCollider == null)
                return;
            foreach (var tag in root.GetComponentsInChildren<TerrainTag>(true))
            {
                if (tag.Kind != TerrainKind.OneWay)
                    continue;
                foreach (var col in tag.GetComponents<Collider2D>())
                    Physics2D.IgnoreCollision(BodyCollider, col, true);
            }
        }

        protected override bool CanThink => base.CanThink && _fighting && !_stopped;

        internal override bool CanDealContact => base.CanDealContact && _fighting && !_stopped;

        protected override bool AcceptHit(HitInfo hit) => _fighting && !_stopped;

        protected override void OnDamaged(HitInfo hit)
        {
            HpChanged?.Invoke(HpLeft, BossMaxHp);
            if (_phase == 1 && HpLeft <= Phase2Hp && HpLeft > 0)
            {
                _phase = 2;
                _roarPending = true;
            }
        }

        protected override void OnDefeated()
        {
            StopAll();
            PlayAnim("Hurt", true, true);
            Defeated?.Invoke();
        }

        protected override void OnAiInterrupted()
        {
            // 演出或暫停打斷：取消進行中的招式，恢復後從休息開始
            if (Anim != null && Anim.Visual != null)
                Anim.Visual.localPosition = Vector3.zero;
            if (_fighting && !_stopped && !Dead && _act != Act.Wait)
            {
                _act = Act.Rest;
                _timer = Rest2;
            }
        }

        protected override void OnDestroy()
        {
            ClearSpawned();
            base.OnDestroy();
        }

        #region AI
        protected override void TickAi(float dt)
        {
            _actTime += dt;
            if (_roarPending && _act != Act.StompAir && IsGrounded(true))
            {
                _roarPending = false;
                StartRoar();
                return;
            }

            switch (_act)
            {
                case Act.Wait:
                case Act.Rest:
                case Act.StompLand:
                case Act.Dazed:
                    SetVx(0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                        NextAct();
                    break;

                case Act.Roar:
                    SetVx(0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _step = 0;
                        StartAct(Phase2Loop[0]);
                    }
                    break;

                case Act.Spray:
                    SetVx(0f);
                    if (!_sprayed && _actTime >= SprayFireAt)
                    {
                        _sprayed = true;
                        SprayCoins(_phase == 1 ? 3 : 5);
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                        NextAct();
                    break;

                case Act.ChargeWindup:
                    SetVx(0f);
                    // 跺腳預備：Visual 左右抖
                    if (Anim.Visual != null)
                        Anim.Visual.localPosition = new Vector3(Mathf.Sin(_actTime * 60f) * 0.06f, 0f, 0f);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        if (Anim.Visual != null)
                            Anim.Visual.localPosition = Vector3.zero;
                        BeginAct(Act.Charge, ChargeMaxTime);
                        PlayAnim("Charge", true, true);
                    }
                    break;

                case Act.Charge:
                    if (WallAhead(0.2f))
                    {
                        SetVx(0f);
                        Shake(0.25f, 0.3f);
                        BeginAct(Act.Dazed, DazedTime);
                        PlayAnim("Hurt", true, true);
                        break;
                    }
                    SetVx(Facing * (_phase == 1 ? ChargeSpeed1 : ChargeSpeed2));
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        SetVx(0f);
                        BeginAct(Act.Dazed, DazedTime);
                        PlayAnim("Hurt", true, true);
                    }
                    break;

                case Act.StompAir:
                    _timer -= dt;
                    var grounded = IsGrounded(true);
                    if (!grounded)
                        _leftGround = true;
                    if ((_leftGround && grounded && Body.linearVelocity.y <= 0.01f) || _timer <= 0f)
                    {
                        SetVx(0f);
                        LandStomp();
                    }
                    break;
            }
        }

        private void NextAct()
        {
            var loop = _phase == 1 ? Phase1Loop : Phase2Loop;
            if (_act != Act.Wait)
                _step = (_step + 1) % loop.Length;
            StartAct(loop[_step]);
        }

        private void BeginAct(Act act, float time)
        {
            _act = act;
            _timer = time;
            _actTime = 0f;
        }

        private void StartAct(Act act)
        {
            var hasHero = TryGetHero(out var hero);
            if (hasHero && Mathf.Abs(hero.x - Feet.x) > 0.1f && act != Act.Rest)
                Face(hero.x > Feet.x ? 1 : -1);
            switch (act)
            {
                case Act.Spray:
                    _sprayed = false;
                    PlayAnim("Spray", false, true);
                    BeginAct(Act.Spray, Mathf.Max(Anim.Current != null ? Anim.Current.Duration : 0f, SprayFireAt) + SprayAfter);
                    break;
                case Act.ChargeWindup:
                    PlayAnim("Idle", true);
                    BeginAct(Act.ChargeWindup, ChargeWindupTime);
                    break;
                case Act.StompAir:
                    StartStomp(hasHero ? hero.x : Feet.x);
                    break;
                case Act.Rest:
                    PlayAnim("Idle", true);
                    BeginAct(Act.Rest, _phase == 1 ? Rest1 : Rest2);
                    break;
                default:
                    PlayAnim("Idle", true);
                    BeginAct(act, 0.5f);
                    break;
            }
        }

        private void StartRoar()
        {
            SetVx(0f);
            if (Anim.Visual != null)
                Anim.Visual.localPosition = Vector3.zero;
            PlayAnim("Stomp", false, true);
            var time = Mathf.Max(GetActionDuration("Stomp"), RoarMinTime);
            BeginAct(Act.Roar, time);
            Shake(0.5f, time);
        }

        private void StartStomp(float targetX)
        {
            var g = Mathf.Abs(Physics2D.gravity.y) * GravityScale;
            var vx = (targetX - Feet.x) / StompAirTime;
            var vy = 0.5f * g * StompAirTime;
            _leftGround = false;
            PlayAnim("Stomp", false, true);
            BeginAct(Act.StompAir, StompMaxAir);
            Body.linearVelocity = new Vector2(vx, vy);
        }

        private void LandStomp()
        {
            var feet = Feet;
            var half = BodySize.x * 0.5f;
            Track(BossShockwave.Spawn(this, Ctx, FxParent, feet + new Vector2(-half, 0f), -1).gameObject);
            Track(BossShockwave.Spawn(this, Ctx, FxParent, feet + new Vector2(half, 0f), 1).gameObject);
            FxPlayer.Spawn(FxParent, "SsFx_Dust", feet, Facing);
            Shake(0.35f, 0.3f);
            PlayAnim("Idle", true);
            BeginAct(Act.StompLand, StompRecover);
        }

        private void SprayCoins(int count)
        {
            var feet = Feet;
            var mouth = feet + new Vector2(Facing * MouthOffset.x, MouthOffset.y);
            var targetCenter = TryGetHero(out var hero) ? hero.x : feet.x + Facing * 5f;
            var g = Mathf.Abs(Physics2D.gravity.y) * BossCoin.GravityScale;
            var batch = new List<Collider2D>();
            for (var i = 0; i < count; i++)
            {
                var offset = (i - (count - 1) * 0.5f) * CoinSpacing;
                var tx = targetCenter + offset;
                var vx = (tx - mouth.x) / CoinFlightTime;
                var vy = (feet.y - mouth.y) / CoinFlightTime + 0.5f * g * CoinFlightTime;
                var coin = BossCoin.Spawn(this, Ctx, FxParent, mouth, new Vector2(vx, vy));
                var solid = coin.Solid;
                if (BodyCollider != null)
                    Physics2D.IgnoreCollision(solid, BodyCollider, true);
                foreach (var other in batch)
                    Physics2D.IgnoreCollision(solid, other, true);
                batch.Add(solid);
                Track(coin.gameObject);
            }
        }

        private void Shake(float strength, float time)
        {
            if (Ctx != null && Ctx.Rig != null)
                Ctx.Rig.Shake(strength, time);
        }

        private void Track(GameObject go)
        {
            _spawned.RemoveAll(o => o == null);
            _spawned.Add(go);
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                    Destroy(go);
            }
            _spawned.Clear();
        }
        #endregion
    }
}
