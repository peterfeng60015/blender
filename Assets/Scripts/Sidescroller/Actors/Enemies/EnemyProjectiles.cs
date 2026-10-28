using UnityEngine;

namespace Sidescroller
{
    /// <summary>敵方子彈的共用部分：演出開始就消失、超時消失、掉出關卡消失。</summary>
    internal abstract class EnemyProjectile : MonoBehaviour
    {
        protected GameSession Session;
        protected Rigidbody2D Body;
        protected SpriteAnimator Anim;
        protected GameObject Attacker;
        private float _life;

        protected abstract float MaxLife { get; }

        protected void Setup(EnemyActor owner, EnemyContext ctx, string location, float gravityScale)
        {
            Session = ctx != null ? ctx.Session : null;
            Attacker = owner != null ? owner.gameObject : null;
            Body = gameObject.AddComponent<Rigidbody2D>();
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.gravityScale = gravityScale;
            Body.freezeRotation = true;
            Body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Anim = SpriteAnimator.Create(gameObject, SsSorting.Projectile);
            Anim.Play(SsArt.Get(location), true);
        }

        protected virtual void Update()
        {
            _life += Time.deltaTime;
            if (_life >= MaxLife || (Session != null && Session.CutsceneActive) || transform.position.y < -20f)
                Destroy(gameObject);
        }
    }

    /// <summary>監工鴿的公文炸彈：受重力落下，碰到地形或主角就爆炸（半徑 1.0，傷害 1）。</summary>
    internal sealed class OfficeBomb : EnemyProjectile
    {
        private const float Radius = 1f;
        private readonly Collider2D[] _hits = new Collider2D[8];
        private bool _exploded;

        protected override float MaxLife => 6f;

        public static OfficeBomb Spawn(EnemyActor owner, EnemyContext ctx, Transform parent, Vector2 position)
        {
            var go = new GameObject("SsBomb");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var bomb = go.AddComponent<OfficeBomb>();
            bomb.Setup(owner, ctx, "SsBomb_Fall", 2.5f);
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.2f;
            return bomb;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_exploded)
                return;
            if (EnemyActor.TryHitHero(other, Attacker, transform.position, 1))
                Explode(false);
            else if (EnemyActor.IsTerrain(other, false))
                Explode(true);
        }

        private void Explode(bool damageArea)
        {
            _exploded = true;
            Vector2 pos = transform.position;
            if (damageArea)
            {
                var filter = new ContactFilter2D().NoFilter();
                var n = Physics2D.OverlapCircle(pos, Radius, filter, _hits);
                for (var i = 0; i < n; i++)
                {
                    if (EnemyActor.TryHitHero(_hits[i], Attacker, pos, 1))
                        break;
                }
            }
            FxPlayer.Spawn(transform.parent, "SsFx_Poof", pos);
            Destroy(gameObject);
        }
    }

    /// <summary>河董的金幣：拋物線落下，落地彈一下，第一次落地後 2 秒消失；碰到主角傷害 1 並消失。</summary>
    internal sealed class BossCoin : EnemyProjectile
    {
        public const float GravityScale = 3f;
        private const float LingerTime = 2f;
        private float _landedLeft = -1f;
        private PhysicsMaterial2D _material;

        protected override float MaxLife => 6f;

        public static BossCoin Spawn(EnemyActor owner, EnemyContext ctx, Transform parent, Vector2 position, Vector2 velocity)
        {
            var go = new GameObject("SsCoin");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var coin = go.AddComponent<BossCoin>();
            coin.Setup(owner, ctx, "SsCoin_Spin", GravityScale);
            coin._material = new PhysicsMaterial2D("SsCoinBounce") { bounciness = 0.45f, friction = 0.6f };
            var body = go.AddComponent<CircleCollider2D>();
            body.radius = 0.17f;
            body.sharedMaterial = coin._material;
            ctx?.IgnoreHero(body);

            var hurt = new GameObject("Hurt");
            hurt.transform.SetParent(go.transform, false);
            var trigger = hurt.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.22f;
            hurt.AddComponent<CoinHurt>().Coin = coin;

            coin.Body.linearVelocity = velocity;
            return coin;
        }

        /// <summary>這顆的實體碰撞體（Boss 用來讓同一批金幣、Boss 本體互不碰撞）。</summary>
        public Collider2D Solid => GetComponent<CircleCollider2D>();

        protected override void Update()
        {
            base.Update();
            if (_landedLeft >= 0f)
            {
                _landedLeft -= Time.deltaTime;
                if (_landedLeft < 0f)
                    Destroy(gameObject);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_landedLeft < 0f && EnemyActor.IsTerrain(collision.collider, false))
            {
                _landedLeft = LingerTime;
                // 第一次落地已彈起；之後不再彈（design.md：落地彈一次）
                if (_material != null)
                    _material.bounciness = 0f;
            }
        }

        internal void OnHurtTouch(Collider2D other)
        {
            if (EnemyActor.TryHitHero(other, Attacker, transform.position, 1))
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }

    internal sealed class CoinHurt : MonoBehaviour
    {
        public BossCoin Coin;

        private void OnTriggerStay2D(Collider2D other)
        {
            if (Coin != null)
                Coin.OnHurtTouch(other);
        }
    }

    /// <summary>河董重踩的震波：沿地面直線前進（速度 7、高 0.8），撞牆消失；每道只傷主角一次。</summary>
    internal sealed class BossShockwave : EnemyProjectile
    {
        public const float Speed = 7f;
        public const float Height = 0.8f;
        private const float Width = 0.7f;
        private const float MaxDistance = 30f;
        private readonly RaycastHit2D[] _rayHits = new RaycastHit2D[8];
        private ContactFilter2D _filter;
        private int _dir;
        private float _startX;
        private bool _hit;

        protected override float MaxLife => MaxDistance / Speed;

        public static BossShockwave Spawn(EnemyActor owner, EnemyContext ctx, Transform parent, Vector2 feet, int dir)
        {
            var go = new GameObject("SsShock");
            go.transform.SetParent(parent, false);
            go.transform.position = feet;
            var wave = go.AddComponent<BossShockwave>();
            wave.Setup(owner, ctx, "SsShock_Move", 0f);
            wave._dir = dir >= 0 ? 1 : -1;
            wave._startX = feet.x;
            wave._filter = new ContactFilter2D().NoFilter();
            wave.Anim.SetFacing(wave._dir);
            // 底邊離地 0.05，避免和地面的接觸被當成重疊
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(Width, Height - 0.05f);
            col.offset = new Vector2(0f, 0.05f + (Height - 0.05f) * 0.5f);
            wave.Body.linearVelocity = new Vector2(wave._dir * Speed, 0f);
            return wave;
        }

        private void FixedUpdate()
        {
            Vector2 pos = Body.position;
            Body.linearVelocity = new Vector2(_dir * Speed, 0f);
            var n = Physics2D.Raycast(pos + new Vector2(0f, Height * 0.5f), new Vector2(_dir, 0f), _filter, _rayHits, Width * 0.5f + 0.05f);
            for (var i = 0; i < n; i++)
            {
                if (EnemyActor.IsTerrain(_rayHits[i].collider, true))
                {
                    FxPlayer.Spawn(transform.parent, "SsFx_Dust", pos, _dir);
                    Destroy(gameObject);
                    return;
                }
            }
            if (Mathf.Abs(pos.x - _startX) > MaxDistance)
                Destroy(gameObject);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (!_hit && EnemyActor.TryHitHero(other, Attacker, transform.position, 1))
                _hit = true;
        }
    }
}
