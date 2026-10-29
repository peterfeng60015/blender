using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 主角的三種攻擊（甩包、急件彈弓、滾殼衝撞）與換裝（design.md §7.3、§7.4）。
    /// 數值＝服裝＋身上裝備的基礎值＋詞條＋強化（loot-design.md §2、§6），由 <see cref="GearStats"/> 算好，裝備或服裝一變就重算。
    /// </summary>
    public sealed partial class HeroController
    {
        /// <summary>多重射擊每多一發往上下各偏的角度。</summary>
        private const float MultiShotSpread = 8f;
        /// <summary>「擊退強化」給敵人的擊退倍率（HitInfo.Knockback）。</summary>
        private const float MeleeKnockbackBoost = 2.5f;

        private CostumeId _costume;
        private readonly GearStats _gear = new GearStats();
        /// <summary>這條命裡已經因為裝上而補過血的殼甲（同一件取下再裝回不再補，避免拆裝回血）。</summary>
        private readonly List<ItemData> _hpBonusClaimed = new List<ItemData>();
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
            var t = _gear.MeleeTimeScale;
            if (_stateTime >= HeroStats.MeleeActiveStart * t && _stateTime <= HeroStats.MeleeActiveEnd * t)
            {
                // 攻擊範圍只往前加長：近端維持在身前 0.1
                var width = HeroStats.MeleeWidth * _gear.MeleeReach;
                var offsetX = HeroStats.MeleeOffsetX + (width - HeroStats.MeleeWidth) * 0.5f;
                var center = _rb.position + new Vector2(offsetX * Facing, HeroStats.MeleeOffsetY);
                var size = new Vector2(width, HeroStats.MeleeHeight);
                var n = Physics2D.OverlapBox(center, size, 0f, AttackFilter(), _overlaps);
                HitColliders(n, DamageSource.Melee, Mathf.Max(1, _gear.MeleeDamage), false, _gear.MeleeKnockback ? MeleeKnockbackBoost : 0f);
            }
            if (_stateTime >= HeroStats.MeleeTotal * t)
                SetState(State.Normal);
        }
        #endregion

        #region 彈弓
        private void TryShoot()
        {
            if (_state != State.Normal || !_session.Loadout.HasAttack(AttackId.Sling) || Time.time < _shootReadyAt)
                return;
            _projectiles.RemoveAll(p => p == null);
            var shots = 1 + Mathf.Max(0, _gear.ExtraShots);
            if (_projectiles.Count >= HeroStats.SlingMaxOnScreen * shots)
                return;
            _shootReadyAt = Time.time + _gear.ShootCooldown;
            _shootPoseUntil = Time.time + HeroStats.ShootPoseTime;
            var dir = _upHeld ? new Vector2(Facing, 1f).normalized : new Vector2(Facing, 0f);
            var origin = _rb.position + new Vector2(0.5f * Facing, 0.8f);
            var damage = Mathf.Max(1, _gear.RangedDamage);
            var life = HeroStats.SlingLife * _gear.RangedReach;
            for (var i = 0; i < shots; i++)
            {
                // 第一發照原方向，多的往上、往下輪流張開
                var step = (i + 1) / 2 * (i % 2 == 1 ? 1 : -1);
                var shotDir = (Vector2)(Quaternion.Euler(0f, 0f, MultiShotSpread * step * Facing) * dir);
                _projectiles.Add(HeroProjectile.Spawn(_worldRoot, origin, shotDir, gameObject, damage, life, _gear.Pierce));
            }
            if (_grounded)
                PlayAnim("Shoot", false, true);
        }
        #endregion

        #region 滾殼
        private void TryRoll()
        {
            if (_state != State.Normal || !_session.Loadout.HasAttack(AttackId.Roll) || Time.time < _rollReadyAt)
                return;
            _rollReadyAt = Time.time + _gear.RollCooldown;
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
                HitColliders(n, DamageSource.Roll, Mathf.Max(1, _gear.RollDamage), true);
                return _rollDir * HeroStats.RollDistance / HeroStats.RollTime;
            }
            if (CanStand())
            {
                // 衝刺結束：直接回到跑速（沒按方向就停），不拖著衝刺速度滑行
                EndRollShape();
                SetState(State.Normal);
                return _scripted ? 0f : _moveX * RunSpeed;
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
        private void HitColliders(int n, DamageSource source, int amount, bool breaksShield, float knockback = 0f)
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
                    Attacker = gameObject,
                    Knockback = knockback
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

        /// <summary>按 C 或演出 grant 換上新服裝：換外觀但動畫不中斷（sprite 換 sheet 保留播放時間；3D 目前同一隻），並閃一下。</summary>
        private void OnLoadoutChanged()
        {
            var costume = _session.Loadout.Costume;
            if (costume == _costume)
                return;
            _costume = costume;
            RefreshGear();
            _anim.SetCostume(costume);
            _anim.Flash(new Color(1f, 1f, 0.6f), 0.12f);
        }
        #endregion

        #region 裝備數值
        private float RunSpeed => HeroStats.RunSpeed(_costume) * _gear.MoveScale;

        private void OnLootChanged() => RefreshGear();

        /// <summary>
        /// 重算裝備數值（裝上、取下、強化後立刻生效）。最大血量變少時目前血量壓在上限內（活著至少留 1）；
        /// 變多時，這條命第一次裝上的那件殼甲才把多出來的上限補成血（同一件取下再裝回只加上限，不能拆裝回血）。
        /// 有變就寫回 session（HUD 心數跟著變）。
        /// </summary>
        private void RefreshGear()
        {
            var oldMax = _gear.MaxHp;
            _gear.Compute(_session.Loot, _costume);
            var max = _gear.MaxHp;
            if (max == oldMax || _state == State.Dead)
                return;
            if (max > oldMax)
            {
                var shell = _session.Loot.GetEquipped(EquipSlot.Shell);
                if (shell != null && !_hpBonusClaimed.Contains(shell))
                {
                    _hpBonusClaimed.Add(shell);
                    Hp += max - oldMax;
                }
                Hp = Mathf.Min(Hp, max);
            }
            else
            {
                Hp = Mathf.Max(1, Mathf.Min(Hp, max));
            }
            _session.SetHp(Hp, max);
        }
        #endregion
    }
}
