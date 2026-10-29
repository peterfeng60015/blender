using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 雜兵與 Boss 的共用基底：建立（ActorTag、Rigidbody2D、本體與接觸框、動畫）、ActorRegistry 登記、
    /// 演出操控（<see cref="ICutsceneActor"/>）、受擊（<see cref="IDamageable"/>）、死亡冒煙、演出中停 AI。
    /// 根物件位置＝腳底；本體 collider 往上偏移半個身高。子類別只寫各自的 AI（<see cref="TickAi"/>）。
    /// </summary>
    public abstract class EnemyActor : MonoBehaviour, ICutsceneActor, IDamageable
    {
        #region 子類別設定
        /// <summary>素材名稱，例如 "SsRat"；location＝ArtName_Action。</summary>
        protected abstract string ArtName { get; }
        /// <summary>這個角色有的動作（design.md §8.3）。</summary>
        protected abstract string[] Actions { get; }
        /// <summary>待機動作（Idle；監工鴿是 Fly）。</summary>
        protected abstract string DefaultAction { get; }
        /// <summary>演出 ScriptedMoveTo 時播的動作。</summary>
        protected abstract string MoveAction { get; }
        /// <summary>本體大小（單位），底邊在腳底。</summary>
        protected abstract Vector2 BodySize { get; }
        protected abstract int StartHp { get; }
        protected virtual ActorKind Kind => ActorKind.Enemy;
        protected virtual int SortingOrder => SsSorting.Enemy;
        protected virtual float GravityScale => 4f;
        /// <summary>false＝只有接觸框（trigger），不站地形（監工鴿）。</summary>
        protected virtual bool HasSolidBody => true;
        protected virtual Vector2 ContactSize => BodySize * 0.85f;
        /// <summary>被打中時輕微後退的速度（Boss 為 0：不會被擊退）。</summary>
        protected virtual float HitKnockbackSpeed => 3f;
        #endregion

        protected const float HitKnockbackTime = 0.1f;
        protected const float DeathDelay = 0.35f;
        private static readonly Color HitFlashColor = new Color(1f, 0.55f, 0.55f, 1f);

        protected EnemyContext Ctx;
        protected GameSession Session;
        protected Rigidbody2D Body;
        protected BoxCollider2D BodyCollider;
        protected BoxCollider2D ContactCollider;
        protected SpriteAnimator Anim;
        protected int HpLeft;
        /// <summary>血量歸零（不再受傷、不再行動）。</summary>
        protected bool Dead;

        private PhysicsMaterial2D _material;
        private ContactFilter2D _solidFilter;
        private readonly RaycastHit2D[] _rayHits = new RaycastHit2D[16];
        private readonly Collider2D[] _overlapHits = new Collider2D[8];
        private bool _scripted;
        private bool _scriptedMoving;
        private float _moveTargetX;
        private float _moveSpeed;
        private bool _returnToDefault;
        private float _knockbackLeft;
        private float _knockbackVx;
        private bool _frozen;
        private float _removeTimer = -1f;
        private float _fallDestroyY = -10f;
        /// <summary>關卡資料的種類（rat／pigeon／rhino／boss），掉落表用。</summary>
        private string _spawnKind;

        #region 建立
        /// <summary>在 ctx.Parent 底下建一個 T，登記到 ActorRegistry 並回傳根物件。失敗時不留半套物件。</summary>
        internal static GameObject Create<T>(SpawnPoint spawn, EnemyContext ctx) where T : EnemyActor
        {
            var go = new GameObject($"{typeof(T).Name} {spawn.Id}");
            try
            {
                go.transform.SetParent(ctx.Parent, false);
                go.transform.position = spawn.Position;
                go.AddComponent<T>().Init(spawn, ctx);
                return go;
            }
            catch
            {
                Destroy(go);
                throw;
            }
        }

        private void Init(SpawnPoint spawn, EnemyContext ctx)
        {
            Id = spawn.Id;
            _spawnKind = spawn.Kind;
            Ctx = ctx;
            Session = ctx.Session;
            if (ctx.Level != null)
                _fallDestroyY = ctx.Level.KillY - 6f;

            var tag = gameObject.AddComponent<ActorTag>();
            tag.Kind = Kind;
            tag.Id = Id;

            Body = gameObject.AddComponent<Rigidbody2D>();
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.gravityScale = GravityScale;
            Body.freezeRotation = true;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.sleepMode = RigidbodySleepMode2D.NeverSleep; // 接觸框靠 OnTriggerStay2D，睡著就不會再送

            var size = BodySize;
            if (HasSolidBody)
            {
                _material = new PhysicsMaterial2D(ArtName + "NoFriction") { friction = 0f, bounciness = 0f };
                BodyCollider = gameObject.AddComponent<BoxCollider2D>();
                BodyCollider.size = size;
                BodyCollider.offset = new Vector2(0f, size.y * 0.5f);
                BodyCollider.sharedMaterial = _material;
            }

            var contact = new GameObject("Contact");
            contact.transform.SetParent(transform, false);
            ContactCollider = contact.AddComponent<BoxCollider2D>();
            ContactCollider.isTrigger = true;
            ContactCollider.size = ContactSize;
            ContactCollider.offset = new Vector2(0f, size.y * 0.5f);
            contact.AddComponent<EnemyContact>().Owner = this;

            _solidFilter = new ContactFilter2D { useTriggers = false }; // 地形查詢不含 trigger（接觸框、金幣傷害框）
            Anim = SpriteAnimator.Create(gameObject, SortingOrder);
            Anim.SetFacing(spawn.Facing >= 0 ? 1 : -1);
            HpLeft = StartHp;

            IgnoreOtherEnemies();
            OnInit(spawn);
            PlayDefault();
            ActorRegistry.Register(this);
        }

        /// <summary>敵人之間互不推擠（巡邏撞到別隻不會卡住或把牠推下平台）；後建的對先建的各做一次就涵蓋全部。</summary>
        private void IgnoreOtherEnemies()
        {
            if (BodyCollider == null || Ctx.Parent == null)
                return;
            foreach (var other in Ctx.Parent.GetComponentsInChildren<EnemyActor>(true))
            {
                if (other != this && other.BodyCollider != null)
                    Physics2D.IgnoreCollision(BodyCollider, other.BodyCollider, true);
            }
        }

        /// <summary>子類別的額外建立（元件都已建好、尚未 Register）。</summary>
        protected virtual void OnInit(SpawnPoint spawn) { }

        protected virtual void OnDestroy()
        {
            ActorRegistry.Unregister(this);
            if (_material != null)
                Destroy(_material);
        }
        #endregion

        #region 每幀
        protected virtual void Update()
        {
            if (_removeTimer >= 0f)
            {
                _removeTimer -= Time.deltaTime;
                if (_removeTimer < 0f)
                {
                    FxPlayer.Spawn(FxParent, "SsFx_Poof", Center, Facing);
                    Destroy(gameObject);
                }
                return;
            }
            if (_returnToDefault && Anim.IsDone)
            {
                _returnToDefault = false;
                PlayDefault();
            }
        }

        private void FixedUpdate()
        {
            if (Body == null || !Body.simulated)
                return;
            if (transform.position.y < _fallDestroyY)
            {
                Destroy(gameObject);
                return;
            }
            var dt = Time.fixedDeltaTime;
            if (_knockbackLeft > 0f)
            {
                _knockbackLeft -= dt;
                SetVx(_knockbackVx);
                return;
            }
            if (_scripted || _scriptedMoving)
            {
                TickScripted(dt);
                return;
            }
            if (Dead || !CanThink)
            {
                if (!_frozen)
                {
                    _frozen = true;
                    OnAiInterrupted();
                    if (!Dead)
                        PlayDefault();
                }
                HoldStill();
                return;
            }
            if (_frozen)
            {
                _frozen = false;
                OnAiInterrupted();
            }
            TickAi(dt);
        }

        /// <summary>AI 可以動嗎（演出中不行；Boss 另外要開打後）。</summary>
        protected virtual bool CanThink => Session == null || !Session.CutsceneActive;

        /// <summary>接觸框可以傷人嗎。</summary>
        internal virtual bool CanDealContact => !Dead && !_scripted && (Session == null || !Session.CutsceneActive);

        /// <summary>AI 一步（FixedUpdate）。</summary>
        protected abstract void TickAi(float dt);

        /// <summary>AI 被打斷（演出開始或結束、停住後恢復）：子類別把狀態機重設成初始狀態。</summary>
        protected virtual void OnAiInterrupted() { }

        /// <summary>停住時每步呼叫：預設水平速度歸零、重力照常。</summary>
        protected virtual void HoldStill() => SetVx(0f);

        private void TickScripted(float dt)
        {
            if (!_scriptedMoving)
            {
                HoldStill();
                return;
            }
            var pos = Body.position;
            var dx = _moveTargetX - pos.x;
            var step = _moveSpeed * dt;
            if (Mathf.Abs(dx) <= step)
            {
                Body.position = new Vector2(_moveTargetX, pos.y);
                _scriptedMoving = false;
                HoldStill();
                PlayDefault();
                return;
            }
            var dir = dx > 0f ? 1 : -1;
            Anim.SetFacing(dir);
            SetVx(dir * _moveSpeed);
            OnScriptedMoveStep();
        }

        /// <summary>演出移動中的每一步（監工鴿用來維持高度）。</summary>
        protected virtual void OnScriptedMoveStep() { }
        #endregion

        #region ICutsceneActor
        public string Id { get; private set; }
        public Transform Root => transform;
        public int Facing => Anim != null ? Anim.Facing : 1;
        public bool IsScriptedMoving => _scriptedMoving;

        public void BeginScripted()
        {
            _scripted = true;
            _scriptedMoving = false;
            _knockbackLeft = 0f;
            OnAiInterrupted();
            if (Body != null && Body.simulated)
                HoldStill();
        }

        public void EndScripted()
        {
            _scripted = false;
            _scriptedMoving = false;
            if (Body != null && Body.simulated)
                HoldStill();
            OnAiInterrupted();
        }

        public void SetFacing(int dir) => Anim.SetFacing(dir);

        public void ScriptedMoveTo(float x, float speed)
        {
            if (_removeTimer >= 0f)
                return;
            _moveTargetX = x;
            _moveSpeed = Mathf.Max(0.1f, speed);
            _scriptedMoving = true;
            PlayAnim(MoveAction, true);
        }

        public void Teleport(float x)
        {
            _scriptedMoving = false;
            var pos = new Vector2(x, transform.position.y);
            transform.position = pos;
            if (Body != null)
            {
                Body.position = pos;
                Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);
            }
            PlayDefault();
        }

        public bool PlayAction(string action, bool loop)
        {
            if (!HasAction(action))
                return false;
            Anim.Play(SsArt.Get(ArtName + "_" + action), loop, true);
            _returnToDefault = !loop;
            return true;
        }

        public float GetActionDuration(string action) => HasAction(action) ? SsArt.Get(ArtName + "_" + action).Duration : 0f;

        private bool HasAction(string action)
        {
            if (string.IsNullOrEmpty(action))
                return false;
            foreach (var a in Actions)
            {
                if (a == action)
                    return true;
            }
            return false;
        }
        #endregion

        #region 受擊與死亡
        public bool TakeHit(HitInfo hit)
        {
            if (hit == null || hit.Amount <= 0 || Dead || _removeTimer >= 0f)
                return false;
            if (!AcceptHit(hit))
                return false;
            HpLeft = Mathf.Max(0, HpLeft - hit.Amount);
            Anim.Flash(HitFlashColor, 0.1f);
            FxPlayer.Spawn(FxParent, "SsFx_Hit", Center, hit.Direction >= 0 ? 1 : -1);
            OnDamaged(hit);
            if (HpLeft <= 0)
            {
                Dead = true;
                _knockbackLeft = 0f;
                LootTables.OnEnemyDefeated(Ctx, Id, _spawnKind, Center);
                OnDefeated();
            }
            else if (HitKnockbackSpeed > 0f && !_scripted)
            {
                // hit.Knockback：主角近戰的「擊退強化」詞條給的倍率（0＝預設）
                _knockbackLeft = HitKnockbackTime;
                _knockbackVx = (hit.Direction >= 0 ? 1f : -1f) * HitKnockbackSpeed * (hit.Knockback > 0f ? hit.Knockback : 1f);
            }
            return true;
        }

        /// <summary>false＝這一下被擋（犀牛的盾）或無效（Boss 開打前）。</summary>
        protected virtual bool AcceptHit(HitInfo hit) => true;

        /// <summary>扣完血之後（HpLeft 已更新）。</summary>
        protected virtual void OnDamaged(HitInfo hit) { }

        /// <summary>血量歸零：雜兵播 Hurt 後冒煙消失；Boss 覆寫成停在原地。</summary>
        protected virtual void OnDefeated()
        {
            SetVx(0f);
            Body.simulated = false;
            ContactCollider.enabled = false;
            Anim.Play(SsArt.Get(ArtName + "_Hurt"), false, true);
            _returnToDefault = false;
            _removeTimer = DeathDelay;
        }
        #endregion

        #region 共用工具
        protected Transform FxParent => Ctx != null && Ctx.Parent != null ? Ctx.Parent : transform.parent;

        /// <summary>身體中央（世界座標）。</summary>
        protected Vector2 Center => (Vector2)transform.position + new Vector2(0f, BodySize.y * 0.5f);

        protected Vector2 Feet => Body != null ? Body.position : (Vector2)transform.position;

        /// <summary>主角腳底；主角不在（還沒建或已刪）回 false。</summary>
        protected bool TryGetHero(out Vector2 hero)
        {
            var t = Ctx != null ? Ctx.Hero : null;
            if (t == null)
            {
                var actor = ActorRegistry.Find(SsIds.ActorHero);
                if (actor is Object obj && obj != null)
                    t = actor.Root;
            }
            hero = t != null ? (Vector2)t.position : Vector2.zero;
            return t != null;
        }

        protected void SetVx(float vx)
        {
            if (Body != null && Body.simulated)
                Body.linearVelocity = new Vector2(vx, Body.linearVelocity.y);
        }

        protected void Face(int dir) => Anim.SetFacing(dir);

        protected void Turn() => Anim.SetFacing(-Facing);

        protected void PlayDefault() => PlayAnim(DefaultAction, true);

        /// <summary>AI 用：同一個循環動作重複呼叫不會重來；restart=true 一定從頭播。</summary>
        protected void PlayAnim(string action, bool loop, bool restart = false)
        {
            _returnToDefault = false;
            Anim.Play(SsArt.Get(ArtName + "_" + action), loop, restart);
        }

        /// <summary>從 origin 往 dir 射線，碰到地形（solidOnly＝只算實心，單向平台不算）回 true。</summary>
        protected bool RayTerrain(Vector2 origin, Vector2 dir, float distance, bool solidOnly)
        {
            var n = Physics2D.Raycast(origin, dir, _solidFilter, _rayHits, distance);
            for (var i = 0; i < n; i++)
            {
                if (IsTerrain(_rayHits[i].collider, solidOnly))
                    return true;
            }
            return false;
        }

        /// <summary>腳下有地形。</summary>
        protected bool IsGrounded(bool solidOnly = false)
        {
            var feet = Feet;
            var half = BodySize.x * 0.4f;
            return RayTerrain(feet + new Vector2(0f, 0.1f), Vector2.down, 0.2f, solidOnly)
                   || RayTerrain(feet + new Vector2(-half, 0.1f), Vector2.down, 0.2f, solidOnly)
                   || RayTerrain(feet + new Vector2(half, 0.1f), Vector2.down, 0.2f, solidOnly);
        }

        /// <summary>面向的前方有實心牆（含關上的閘門）。</summary>
        protected bool WallAhead(float extra = 0.15f)
        {
            var feet = Feet;
            var dir = new Vector2(Facing, 0f);
            var dist = BodySize.x * 0.5f + extra;
            return RayTerrain(feet + new Vector2(0f, 0.3f), dir, dist, true)
                   || RayTerrain(feet + new Vector2(0f, BodySize.y * 0.7f), dir, dist, true);
        }

        /// <summary>面向的前方腳下還有地面（沒有就是平台邊緣或坑）。</summary>
        protected bool GroundAhead()
        {
            var origin = Feet + new Vector2(Facing * (BodySize.x * 0.5f + 0.15f), 0.1f);
            return RayTerrain(origin, Vector2.down, 0.6f, false);
        }

        /// <summary>在框內找主角並送一次傷害；打中回 true。knockback：擊退倍率（0＝主角預設）。</summary>
        protected bool HitHeroInBox(Vector2 center, Vector2 size, int amount, float knockback = 0f)
        {
            var n = Physics2D.OverlapBox(center, size, 0f, _solidFilter, _overlapHits);
            for (var i = 0; i < n; i++)
            {
                if (TryHitHero(_overlapHits[i], gameObject, Center, amount, knockback))
                    return true;
            }
            return false;
        }

        internal static bool IsTerrain(Collider2D col, bool solidOnly)
        {
            if (col == null || col.isTrigger)
                return false;
            var tag = col.GetComponent<TerrainTag>();
            return tag != null && (!solidOnly || tag.Kind == TerrainKind.Solid);
        }

        /// <summary>other 是主角的實體碰撞體（非 trigger）時，對主角送一次敵方傷害；有效命中回 true。</summary>
        internal static bool TryHitHero(Collider2D other, GameObject attacker, Vector2 from, int amount, float knockback = 0f)
        {
            if (other == null || other.isTrigger)
                return false;
            var tag = other.GetComponentInParent<ActorTag>();
            if (tag == null || tag.Kind != ActorKind.Hero)
                return false;
            var target = other.GetComponentInParent<IDamageable>();
            if (target == null)
                return false;
            return target.TakeHit(new HitInfo
            {
                Amount = amount,
                Source = DamageSource.Enemy,
                Direction = other.bounds.center.x >= from.x ? 1 : -1,
                BreaksShield = false,
                Attacker = attacker,
                Knockback = knockback
            });
        }

        /// <summary>保險：IgnoreCollision 若因主角切換 collider 而失效，第一次碰到時補上。</summary>
        private void OnCollisionEnter2D(Collision2D collision)
        {
            var tag = collision.collider.GetComponentInParent<ActorTag>();
            if (tag != null && tag.Kind == ActorKind.Hero)
                Physics2D.IgnoreCollision(collision.otherCollider, collision.collider, true);
        }
        #endregion
    }

    /// <summary>敵人的接觸傷害框（trigger 子物件）：碰到主角的實體碰撞體送 1 點傷害。</summary>
    internal sealed class EnemyContact : MonoBehaviour
    {
        public EnemyActor Owner;

        private void OnTriggerStay2D(Collider2D other)
        {
            if (Owner != null && Owner.CanDealContact)
                EnemyActor.TryHitHero(other, Owner.gameObject, transform.position, 1);
        }
    }
}
