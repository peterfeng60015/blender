using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 急件彈弓的小包裹（design.md §7.3）：等速直線、存活 0.8 秒；每個物理步用 CircleCast 掃過這一步的路徑，
    /// 打到實心地形（TerrainTag.Solid）就消失，單向平台不擋；打到敵人呼叫 IDamageable（被盾擋也消失；特效由被打的一方播）。
    /// 不放 collider，所以不會觸發關卡的觸發區。傷害、存活時間（射程）與穿透由主角依裝備給；
    /// 穿透：命中敵人後繼續飛（同一隻只打一次），被盾擋下或打到實心地形照樣消失。
    /// </summary>
    public sealed class HeroProjectile : MonoBehaviour
    {
        private const float Radius = 0.15f;

        private Vector2 _velocity;
        private float _life;
        private GameObject _owner;
        private int _damage;
        private bool _pierce;
        private readonly System.Collections.Generic.List<IDamageable> _hitTargets = new System.Collections.Generic.List<IDamageable>();
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[8];
        private ContactFilter2D _filter;

        public static HeroProjectile Spawn(Transform parent, Vector2 position, Vector2 direction, GameObject owner,
            int damage = HeroStats.SlingDamage, float life = HeroStats.SlingLife, bool pierce = false)
        {
            var go = new GameObject("HeroProjectile");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            // 左右用 Visual 的 localScale.x 翻轉（design.md §8.1），旋轉只表示往上的角度
            var facing = direction.x < 0f ? -1 : 1;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg * facing);
            var p = go.AddComponent<HeroProjectile>();
            p._velocity = direction.normalized * HeroStats.SlingSpeed;
            p._life = life;
            p._owner = owner;
            p._damage = damage;
            p._pierce = pierce;
            p._filter = new ContactFilter2D { useTriggers = true };
            var anim = SpriteAnimator.Create(go, SsSorting.Projectile);
            anim.SetFacing(facing);
            anim.Play(SsArt.Get("SsParcel_Spin"), true);
            return p;
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            var pos = (Vector2)transform.position;
            var step = _velocity * dt;
            var n = Physics2D.CircleCast(pos, Radius, step.normalized, _filter, _hits, step.magnitude);
            SortByDistance(n);
            for (var i = 0; i < n; i++)
            {
                if (HandleHit(_hits[i]))
                {
                    Destroy(gameObject);
                    return;
                }
            }
            transform.position = pos + step;
            _life -= dt;
            if (_life <= 0f)
                Destroy(gameObject);
        }

        /// <summary>回 true＝子彈到此為止。</summary>
        private bool HandleHit(RaycastHit2D hit)
        {
            var col = hit.collider;
            if (col == null)
                return false;
            var terrain = col.GetComponentInParent<TerrainTag>();
            if (terrain != null)
                return terrain.Kind == TerrainKind.Solid && !col.isTrigger;
            var tag = col.GetComponentInParent<ActorTag>();
            if (tag == null || tag.Kind == ActorKind.Hero || tag.Kind == ActorKind.Npc)
                return false;
            var target = col.GetComponentInParent<IDamageable>();
            if (target == null || _hitTargets.Contains(target))
                return false;
            _hitTargets.Add(target);
            var landed = target.TakeHit(new HitInfo
            {
                Amount = _damage,
                Source = DamageSource.Sling,
                Direction = _velocity.x >= 0f ? 1 : -1,
                BreaksShield = false,
                Attacker = _owner
            });
            return !(_pierce && landed);   // 命中／擋下的特效由被打的一方播
        }

        private void SortByDistance(int n)
        {
            for (var i = 1; i < n; i++)
            {
                var h = _hits[i];
                var j = i - 1;
                while (j >= 0 && _hits[j].distance > h.distance)
                {
                    _hits[j + 1] = _hits[j];
                    j--;
                }
                _hits[j + 1] = h;
            }
        }
    }
}
