using System;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 主角阿甲：移動、跳躍（土狼時間、跳躍緩衝、按住跳更高）、單向平台下跳、受傷與死亡、掉坑回位。
    /// 攻擊與換裝在 HeroController.Combat.cs，演出操控（ICutsceneActor）在 HeroController.Scripted.cs。
    /// 輸入在 Update 讀（session.InputLocked 時一律當沒按），物理在 FixedUpdate 算；重力自己算（gravityScale＝0），
    /// 不受專案 Physics2D.gravity 影響。
    /// </summary>
    public sealed partial class HeroController : MonoBehaviour, ICutsceneActor, IDamageable
    {
        private enum State { Normal, Melee, Roll, Hurt, Dead }

        private GameSession _session;
        private Transform _worldRoot;
        private Rigidbody2D _rb;
        private CapsuleCollider2D _body;
        private HeroVisual _anim;
        private PhysicsMaterial2D _material;

        private State _state = State.Normal;
        private float _stateTime;

        #region 輸入（Update 讀、FixedUpdate 用）
        private float _moveX;
        private bool _jumpHeld;
        private bool _downHeld;
        private bool _upHeld;
        private float _jumpPressedAt = float.NegativeInfinity;
        #endregion

        #region 地面與跳躍
        private bool _grounded;
        private float _lastGroundedAt = float.NegativeInfinity;
        private Collider2D _groundOneWay;
        private bool _jumpCutPending;
        private Collider2D _droppingThrough;
        private float _dropUntil;
        private Vector2 _lastSafe;
        private readonly ContactPoint2D[] _contacts = new ContactPoint2D[16];
        private readonly Collider2D[] _overlaps = new Collider2D[16];
        private readonly RaycastHit2D[] _rayHits = new RaycastHit2D[4];
        private ContactFilter2D _solidFilter;
        #endregion

        #region 受傷
        private float _invincibleUntil;
        private float _pitBusyUntil;
        private bool _diedRaised;
        #endregion

        public string Id => SsIds.ActorHero;
        public Transform Root => transform;
        public int Facing => _anim != null ? _anim.Facing : 1;
        public int Hp { get; private set; }
        public bool IsGrounded => _grounded;
        public bool IsDead => _state == State.Dead;
        /// <summary>主角的實體碰撞體（站立是膠囊，滾殼時同一個碰撞體縮成圓）。</summary>
        public Collider2D Body => _body;
        /// <summary>Boss 擊敗後由流程設成 true：之後不再受傷。</summary>
        public bool PermanentInvincible { get; set; }

        /// <summary>血量歸零時觸發一次（GameFlow 接手重生）。</summary>
        public event Action Died;

        #region 建立
        /// <summary>在 parent 底下 feet（腳底）建主角，血量補滿並寫進 session。</summary>
        public static HeroController Create(Transform parent, Vector2 feet, int facing, GameSession session)
        {
            var go = new GameObject("Hero");
            go.transform.SetParent(parent, false);
            go.transform.position = feet;

            var tag = go.AddComponent<ActorTag>();
            tag.Kind = ActorKind.Hero;
            tag.Id = SsIds.ActorHero;

            var hero = go.AddComponent<HeroController>();
            hero._session = session;
            hero._worldRoot = parent;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            hero._rb = rb;

            hero._material = new PhysicsMaterial2D("HeroNoFriction") { friction = 0f, bounciness = 0f };
            var body = go.AddComponent<CapsuleCollider2D>();
            body.sharedMaterial = hero._material;
            hero._body = body;
            hero.SetBallShape(false);

            hero._anim = HeroVisual.Create(go, SsSorting.Hero, session.Loadout.Costume);
            hero._anim.SetFacing(facing);

            hero._solidFilter = new ContactFilter2D { useTriggers = false };
            hero._lastSafe = feet;
            hero._costume = session.Loadout.Costume;
            hero.Hp = HeroStats.MaxHp;
            session.SetHp(HeroStats.MaxHp, HeroStats.MaxHp);
            session.Loadout.Changed += hero.OnLoadoutChanged;
            hero.PlayAnim("Idle", true);

            ActorRegistry.Register(hero);
            return hero;
        }

        private void OnDestroy()
        {
            if (_session != null)
                _session.Loadout.Changed -= OnLoadoutChanged;
            ActorRegistry.Unregister(this);
            if (_material != null)
                Destroy(_material);
        }
        #endregion

        #region 每幀
        private void Update()
        {
            if (_session == null)
                return;
            ReadInput();
            UpdateAnimation();
            UpdateBlink();
        }

        private void ReadInput()
        {
            var canControl = !_session.InputLocked && !_scripted && _state != State.Dead;
            if (!canControl)
            {
                _jumpPressedAt = float.NegativeInfinity;   // 鎖住前按的跳不留到解鎖後
                _moveX = 0f;
                _jumpHeld = false;
                _downHeld = false;
                _upHeld = false;
                return;
            }
            _moveX = SsInput.MoveX;
            _jumpHeld = SsInput.JumpHeld;
            _downHeld = SsInput.DownHeld;
            _upHeld = SsInput.UpHeld;
            if (SsInput.JumpPressed)
                _jumpPressedAt = Time.time;
            if (SsInput.MeleePressed)
                TryMelee();
            if (SsInput.ShootPressed)
                TryShoot();
            if (SsInput.RollPressed)
                TryRoll();
            if (SsInput.SwitchCostumePressed)
                TrySwitchCostume();
        }

        private void FixedUpdate()
        {
            if (_session == null)
                return;
            var dt = Time.fixedDeltaTime;
            _stateTime += dt;
            UpdateGround();
            RestoreDropThrough();

            var v = _rb.linearVelocity;
            switch (_state)
            {
                case State.Dead:
                    v.x = Mathf.MoveTowards(v.x, 0f, HeroStats.GroundAccel * dt);
                    break;
                case State.Hurt:
                    if (_stateTime >= HeroStats.HurtStun)
                        SetState(State.Normal);
                    break;
                case State.Roll:
                    v.x = UpdateRoll(dt);
                    break;
                default:
                    if (_state == State.Melee)
                        UpdateMelee();
                    v.x = _scripted ? ScriptedVelocityX(v.x, dt) : ControlVelocityX(v.x, dt);
                    break;
            }

            if (_state == State.Normal || _state == State.Melee)
                TryJump(ref v);

            if (_jumpCutPending && v.y > 0f && !_jumpHeld)
            {
                v.y *= HeroStats.JumpCutMultiplier;
                _jumpCutPending = false;
            }
            if (v.y <= 0f)
                _jumpCutPending = false;

            var g = HeroStats.Gravity * (v.y < 0f ? HeroStats.FallGravityMultiplier : 1f);
            v.y = Mathf.Max(v.y - g * dt, -HeroStats.MaxFallSpeed);
            _rb.linearVelocity = v;

            UpdateSafePosition();
        }
        #endregion

        #region 移動與跳躍
        private float ControlVelocityX(float vx, float dt)
        {
            var target = _moveX * HeroStats.RunSpeed(_costume);
            var accel = _grounded ? HeroStats.GroundAccel : HeroStats.AirAccel;
            if (_moveX != 0f && _state != State.Melee)
                _anim.SetFacing(_moveX > 0f ? 1 : -1);
            return Mathf.MoveTowards(vx, target, accel * dt);
        }

        private void TryJump(ref Vector2 v)
        {
            if (_scripted || Time.time - _jumpPressedAt > HeroStats.JumpBufferTime)
                return;
            var canJump = _grounded || Time.time - _lastGroundedAt <= HeroStats.CoyoteTime;
            if (!canJump)
                return;
            _jumpPressedAt = float.NegativeInfinity;
            _lastGroundedAt = float.NegativeInfinity;
            if (_downHeld && _grounded && _groundOneWay != null)
            {
                DropThrough(_groundOneWay);
                return;
            }
            v.y = HeroStats.JumpSpeed;
            _grounded = false;
            _jumpCutPending = true;
        }

        /// <summary>
        /// 是否站在地上：Rigidbody 的接觸點法線朝上，或腳底下一小塊範圍內有地形（膠囊底部是圓的，
        /// 走到平台邊緣時接觸法線會斜掉，只看接觸點會太早判定離地、吃掉土狼時間）。順便記下腳下的單向平台。
        /// </summary>
        private void UpdateGround()
        {
            _grounded = false;
            _groundOneWay = null;
            if (_rb.linearVelocity.y > 0.01f)
                return;
            var n = _rb.GetContacts(_contacts);
            for (var i = 0; i < n; i++)
            {
                var c = _contacts[i];
                if (c.normal.y < 0.7f || c.collider == null || c.collider.isTrigger)
                    continue;
                MarkGround(c.collider);
            }
            var feet = _rb.position;
            var probeWidth = (IsBall ? HeroStats.RollRadius * 2f : HeroStats.BodyWidth) * 0.8f;
            n = Physics2D.OverlapBox(feet + new Vector2(0f, -0.04f), new Vector2(probeWidth, 0.08f), 0f, _solidFilter, _overlaps);
            for (var i = 0; i < n; i++)
            {
                var col = _overlaps[i];
                if (col == _body || col == _droppingThrough)
                    continue;
                var terrain = col.GetComponentInParent<TerrainTag>();
                if (terrain == null)
                    continue;   // 只認地形（敵人的身體不能踩）
                if (terrain.Kind == TerrainKind.OneWay && col.bounds.max.y > feet.y + 0.1f)
                    continue;   // 單向平台：腳在平台頂端以下（正在穿過）不算
                MarkGround(col);
            }
            if (_grounded)
                _lastGroundedAt = Time.time;
        }

        private void MarkGround(Collider2D col)
        {
            _grounded = true;
            var terrain = col.GetComponentInParent<TerrainTag>();
            if (terrain != null && terrain.Kind == TerrainKind.OneWay)
                _groundOneWay = col;
        }

        private void DropThrough(Collider2D platform)
        {
            RestoreDropThrough(true);
            Physics2D.IgnoreCollision(_body, platform, true);
            _droppingThrough = platform;
            _dropUntil = Time.time + HeroStats.DropThroughTime;
            _grounded = false;
        }

        private void RestoreDropThrough(bool force = false)
        {
            if (_droppingThrough == null || (!force && Time.time < _dropUntil))
                return;
            if (_body != null)
                Physics2D.IgnoreCollision(_body, _droppingThrough, false);
            _droppingThrough = null;
        }

        /// <summary>站在實心地面、而且身體左右兩側底下都有實心地形時，記成掉坑後的回位點。</summary>
        private void UpdateSafePosition()
        {
            if (!_grounded || _groundOneWay != null || _state != State.Normal || _scripted)
                return;
            var p = _rb.position;
            var half = HeroStats.BodyWidth * 0.5f;
            if (SolidBelow(new Vector2(p.x - half, p.y + 0.1f)) && SolidBelow(new Vector2(p.x + half, p.y + 0.1f)))
                _lastSafe = p;
        }

        private bool SolidBelow(Vector2 origin)
        {
            var n = Physics2D.Raycast(origin, Vector2.down, _solidFilter, _rayHits, 0.3f);
            for (var i = 0; i < n; i++)
            {
                var terrain = _rayHits[i].collider.GetComponentInParent<TerrainTag>();
                if (terrain != null && terrain.Kind == TerrainKind.Solid)
                    return true;
            }
            return false;
        }

        /// <summary>站起來的空間有沒有被實心地形擋住（滾殼結束前檢查）。</summary>
        private bool CanStand()
        {
            var p = _rb.position;
            var size = new Vector2(HeroStats.BodyWidth * 0.9f, HeroStats.BodyHeight - 0.15f);
            var center = p + new Vector2(0f, 0.1f + size.y * 0.5f);
            var n = Physics2D.OverlapBox(center, size, 0f, _solidFilter, _overlaps);
            for (var i = 0; i < n; i++)
            {
                var col = _overlaps[i];
                if (col == _body)
                    continue;
                var terrain = col.GetComponentInParent<TerrainTag>();
                if (terrain != null && terrain.Kind == TerrainKind.Solid)
                    return false;
            }
            return true;
        }

        /// <summary>站立：0.7×1.4 膠囊；滾殼：同一個膠囊縮成直徑 0.9 的圓（不換碰撞體，IgnoreCollision 才不會失效）。</summary>
        private void SetBallShape(bool ball)
        {
            if (ball)
            {
                var d = HeroStats.RollRadius * 2f;
                _body.direction = CapsuleDirection2D.Vertical;
                _body.size = new Vector2(d, d);
                _body.offset = new Vector2(0f, HeroStats.RollRadius);
            }
            else
            {
                _body.direction = CapsuleDirection2D.Vertical;
                _body.size = new Vector2(HeroStats.BodyWidth, HeroStats.BodyHeight);
                _body.offset = new Vector2(0f, HeroStats.BodyHeight * 0.5f);
            }
        }

        private bool IsBall => _body != null && _body.size.y < HeroStats.BodyHeight - 0.01f;

        private void SetState(State state)
        {
            _state = state;
            _stateTime = 0f;
        }
        #endregion

        #region 受傷、死亡、掉坑
        public bool TakeHit(HitInfo hit)
        {
            if (hit == null || hit.Amount <= 0 || _state == State.Dead || PermanentInvincible)
                return false;
            if (Time.time < _invincibleUntil || _scripted || _session.CutsceneActive)
                return false;
            if (_state == State.Roll && _stateTime < HeroStats.RollTime)
                return false;

            Hp = Mathf.Max(0, Hp - hit.Amount);
            _session.SetHp(Hp, HeroStats.MaxHp);
            if (Hp == 0)
            {
                Die();
                return true;
            }
            _invincibleUntil = Time.time + HeroStats.HurtInvincible;
            if (_state == State.Roll && IsBall && !CanStand())
                return true;   // 在低矮通道裡：維持滾殼，不擊退
            EndRollShape();
            var dir = hit.Direction != 0 ? (hit.Direction > 0 ? 1 : -1) : -Facing;
            var scale = hit.Knockback > 0f ? hit.Knockback : 1f;
            _rb.linearVelocity = new Vector2(HeroStats.KnockbackX * scale * dir, HeroStats.KnockbackY);
            _jumpCutPending = false;
            SetState(State.Hurt);
            PlayAnim("Hurt", false, true);
            return true;
        }

        /// <summary>
        /// 掉坑：扣 1 心並回到最後站穩的位置（血量歸零則死亡）。回位處理中重複呼叫會被忽略。
        /// <see cref="PermanentInvincible"/>（重生淡入中、Boss 擊敗後）時只拉回、不扣血。
        /// </summary>
        public void FallIntoPit()
        {
            if (_state == State.Dead || Time.time < _pitBusyUntil)
                return;
            _pitBusyUntil = Time.time + 0.3f;
            if (!PermanentInvincible)
            {
                Hp = Mathf.Max(0, Hp - 1);
                _session.SetHp(Hp, HeroStats.MaxHp);
                if (Hp == 0)
                {
                    Die();
                    return;
                }
            }
            EndRollShape();
            SetState(State.Normal);
            _rb.position = _lastSafe;
            transform.position = _lastSafe;
            _rb.linearVelocity = Vector2.zero;
            _invincibleUntil = Time.time + HeroStats.HurtInvincible;
        }

        private void Die()
        {
            EndRollShape();
            SetState(State.Dead);
            PlayAnim("Hurt", false, true);
            if (_diedRaised)
                return;
            _diedRaised = true;
            Died?.Invoke();
        }

        private void UpdateBlink()
        {
            var blinking = _state != State.Dead && Time.time < _invincibleUntil;
            var alpha = blinking && Mathf.FloorToInt(Time.time / 0.08f) % 2 == 0 ? 0.35f : 1f;
            _anim.SetBaseColor(new Color(1f, 1f, 1f, alpha));
        }
        #endregion

        #region 動畫
        private string _animAction;
        private bool _animLoop;

        private void PlayAnim(string action, bool loop, bool restart = false)
        {
            if (!restart && action == _animAction && loop == _animLoop)
                return;
            _animAction = action;
            _animLoop = loop;
            _anim.Play(action, loop, true);
        }

        private void UpdateAnimation()
        {
            if (_actionOverride != null)
            {
                if (_actionOverrideLoop || !_anim.IsDone)
                    return;
                _actionOverride = null;
            }
            switch (_state)
            {
                case State.Dead:
                case State.Hurt:
                    PlayAnim("Hurt", false);
                    return;
                case State.Roll:
                    PlayAnim("Jump", true);
                    return;
                case State.Melee:
                    PlayAnim("Melee", false);
                    return;
            }
            if (!_grounded && Time.time - _lastGroundedAt > 0.05f)
                PlayAnim("Jump", true);
            else if (Time.time < _shootPoseUntil)
                PlayAnim("Shoot", false);
            else if (Mathf.Abs(_rb.linearVelocity.x) > 0.3f)
                PlayAnim("Run", true);
            else
                PlayAnim("Idle", true);
        }
        #endregion
    }
}
