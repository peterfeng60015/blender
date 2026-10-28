using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>主角的三種攻擊（甩包、急件彈弓、滾殼衝撞）與換裝（design.md §7.3、§7.4）。</summary>
    public sealed partial class HeroController
    {
        private CostumeId _costume;
        private float _switchReadyAt;
        private float _shootReadyAt;
        private float _shootPoseUntil;
        private float _rollReadyAt;
        private int _rollDir = 1;
        private readonly List<IDamageable> _hitThisAttack = new List<IDamageable>();
        private readonly List<HeroProjectile> _projectiles = new List<HeroProjectile>();

        #region 甩包
        private void TryMelee()
        {
            if (_state != State.Normal || !_session.Loadout.HasAttack(AttackId.Melee))
                return;
            _hitThisAttack.Clear();
            SetState(State.Melee);
            PlayAnim("Melee", false, true);
        }

        /// <summary>FixedUpdate 呼叫：有效時間內判定，整個動作結束回 Normal。</summary>
        private void UpdateMelee()
        {
            if (_stateTime >= HeroStats.MeleeActiveStart && _stateTime <= HeroStats.MeleeActiveEnd)
            {
                var center = _rb.position + new Vector2(HeroStats.MeleeOffsetX * Facing, HeroStats.MeleeOffsetY);
                var size = new Vector2(HeroStats.MeleeWidth, HeroStats.MeleeHeight);
                var n = Physics2D.OverlapBox(center, size, 0f, AttackFilter(), _overlaps);
                HitColliders(n, DamageSource.Melee, HeroStats.MeleeDamage(_costume), false);
            }
            if (_stateTime >= HeroStats.MeleeTotal)
                SetState(State.Normal);
        }
        #endregion

        #region 彈弓
        private void TryShoot()
        {
            if (_state != State.Normal || !_session.Loadout.HasAttack(AttackId.Sling) || Time.time < _shootReadyAt)
                return;
            _projectiles.RemoveAll(p => p == null);
            if (_projectiles.Count >= HeroStats.SlingMaxOnScreen)
                return;
            _shootReadyAt = Time.time + HeroStats.SlingCooldown;
            _shootPoseUntil = Time.time + HeroStats.ShootPoseTime;
            var dir = _upHeld ? new Vector2(Facing, 1f).normalized : new Vector2(Facing, 0f);
            var origin = _rb.position + new Vector2(0.5f * Facing, 0.8f);
            _projectiles.Add(HeroProjectile.Spawn(_worldRoot, origin, dir, gameObject));
            if (_grounded)
                PlayAnim("Shoot", false, true);
        }
        #endregion

        #region 滾殼
        private void TryRoll()
        {
            if (_state != State.Normal || !_session.Loadout.HasAttack(AttackId.Roll) || Time.time < _rollReadyAt)
                return;
            _rollReadyAt = Time.time + HeroStats.RollCooldown(_costume);
            _rollDir = _moveX != 0f ? (_moveX > 0f ? 1 : -1) : Facing;
            _anim.SetFacing(_rollDir);
            _hitThisAttack.Clear();
            SetBallShape(true);
            SetState(State.Roll);
            PlayAnim("Jump", true, true);
        }

        /// <summary>衝刺 0.35 秒（無敵、撞到的敵人各算一次）；結束時頭上有天花板就維持球形慢慢滾，直到能站起來。</summary>
        private float UpdateRoll(float dt)
        {
            if (_stateTime < HeroStats.RollTime)
            {
                var center = _rb.position + new Vector2(0f, HeroStats.RollRadius);
                var n = Physics2D.OverlapCircle(center, HeroStats.RollRadius + 0.1f, AttackFilter(), _overlaps);
                HitColliders(n, DamageSource.Roll, HeroStats.RollDamage, true);
                return _rollDir * HeroStats.RollDistance / HeroStats.RollTime;
            }
            if (CanStand())
            {
                // 衝刺結束：直接回到跑速（沒按方向就停），不拖著衝刺速度滑行
                EndRollShape();
                SetState(State.Normal);
                return _scripted ? 0f : _moveX * HeroStats.RunSpeed(_costume);
            }
            if (_scripted)
                return ScriptedVelocityX(_rb.linearVelocity.x, dt);   // 演出中：維持球形，照演出走位（沒走位就停）
            if (_moveX != 0f)
                _rollDir = _moveX > 0f ? 1 : -1;
            _anim.SetFacing(_rollDir);
            return _rollDir * HeroStats.RollCrawlSpeed;
        }

        private void EndRollShape()
        {
            if (IsBall)
                SetBallShape(false);
        }
        #endregion

        #region 命中
        private static ContactFilter2D AttackFilter() => new ContactFilter2D { useTriggers = true };

        /// <summary>對 _overlaps 前 n 個 collider 所屬的敵人造成傷害；同一次攻擊對同一隻只算一次。</summary>
        private void HitColliders(int n, DamageSource source, int amount, bool breaksShield)
        {
            for (var i = 0; i < n; i++)
            {
                var col = _overlaps[i];
                var tag = col.GetComponentInParent<ActorTag>();
                if (tag == null || tag.Kind == ActorKind.Hero || tag.Kind == ActorKind.Npc)
                    continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || _hitThisAttack.Contains(target))
                    continue;
                _hitThisAttack.Add(target);
                var hit = new HitInfo
                {
                    Amount = amount,
                    Source = source,
                    Direction = Facing,
                    BreaksShield = breaksShield,
                    Attacker = gameObject
                };
                target.TakeHit(hit);   // 命中／擋下的特效由被打的一方播
            }
        }
        #endregion

        #region 換裝
        private void TrySwitchCostume()
        {
            var loadout = _session.Loadout;
            if (loadout.CostumeCount < 2 || Time.time < _switchReadyAt || _state != State.Normal)
                return;
            var next = loadout.Costume == CostumeId.Day ? CostumeId.Express : CostumeId.Day;
            if (loadout.SetCostume(next))
                _switchReadyAt = Time.time + HeroStats.CostumeSwitchCooldown;
        }

        /// <summary>按 C 或演出 grant 換上新服裝：換 sheet 但保留播放時間（兩套幀數一致，動畫不中斷）。</summary>
        private void OnLoadoutChanged()
        {
            var costume = _session.Loadout.Costume;
            if (costume == _costume)
                return;
            _costume = costume;
            if (_animAction != null)
                _anim.Swap(SsArt.Get(HeroStats.SpritePrefix(costume) + "_" + (_actionOverride ?? _animAction)));
            _anim.Flash(new Color(1f, 1f, 0.6f), 0.12f);
        }
        #endregion
    }
}
