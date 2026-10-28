using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 沒有自己 AI 的簡單角色（NPC 龜婆、佔位敵人）共用的身體與演出操控：
    /// 動態 Rigidbody2D＋方框碰撞體、自己算重力、<see cref="ICutsceneActor"/> 的走位與動作播放。
    /// </summary>
    public abstract class SimpleActor : MonoBehaviour, ICutsceneActor
    {
        private const float Gravity = 44f;
        private const float MaxFall = 20f;

        private string _id;
        private string _spritePrefix;
        private string[] _actions;
        private string _idleAction;
        private string _moveAction;
        private bool _flying;

        protected Rigidbody2D Body { get; private set; }
        protected BoxCollider2D Box { get; private set; }
        protected SpriteAnimator Anim { get; private set; }

        private bool _scripted;
        private bool _moving;
        private float _targetX;
        private float _speed;
        private float _stuck;
        private string _override;
        private bool _overrideLoop;

        public string Id => _id;
        public Transform Root => transform;
        public int Facing => Anim != null ? Anim.Facing : 1;
        public bool IsScriptedMoving => _moving;
        protected bool Scripted => _scripted;

        /// <summary>子類別在 AddComponent 之後立刻呼叫。moveAction 為 null 時走位用 idle 動作。</summary>
        protected void Setup(ActorKind kind, SpawnPoint spawn, Vector2 size, int sorting, string spritePrefix,
            string[] actions, string idleAction, string moveAction, bool flying)
        {
            _id = spawn.Id;
            _spritePrefix = spritePrefix;
            _actions = actions;
            _idleAction = idleAction;
            _moveAction = moveAction ?? idleAction;
            _flying = flying;

            var tag = gameObject.AddComponent<ActorTag>();
            tag.Kind = kind;
            tag.Id = spawn.Id;

            Body = gameObject.AddComponent<Rigidbody2D>();
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.gravityScale = 0f;
            Body.freezeRotation = true;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;

            Box = gameObject.AddComponent<BoxCollider2D>();
            Box.size = size;
            Box.offset = new Vector2(0f, size.y * 0.5f);

            Anim = SpriteAnimator.Create(gameObject, sorting);
            Anim.SetFacing(spawn.Facing);
            Anim.Play(SsArt.Get(spritePrefix + "_" + idleAction), true);
            ActorRegistry.Register(this);
        }

        protected virtual void OnDestroy() => ActorRegistry.Unregister(this);

        protected virtual void FixedUpdate()
        {
            if (Body == null)
                return;
            var dt = Time.fixedDeltaTime;
            var v = Body.linearVelocity;
            v.x = MoveVelocity(dt);
            v.y = _flying ? 0f : Mathf.Max(v.y - Gravity * dt, -MaxFall);
            Body.linearVelocity = v;
        }

        protected virtual void Update()
        {
            if (Anim == null)
                return;
            if (_override != null)
            {
                if (_overrideLoop || !Anim.IsDone)
                    return;
                _override = null;
            }
            Anim.Play(SsArt.Get(_spritePrefix + "_" + (_moving ? _moveAction : _idleAction)), true);
        }

        private float MoveVelocity(float dt)
        {
            if (!_moving)
                return 0f;
            var dx = _targetX - Body.position.x;
            if (Mathf.Abs(dx) <= _speed * dt)
            {
                _moving = false;
                Body.position = new Vector2(_targetX, Body.position.y);
                return 0f;
            }
            var dir = dx > 0f ? 1 : -1;
            Anim.SetFacing(dir);
            if (Mathf.Abs(Body.linearVelocity.x) < 0.05f)
            {
                _stuck += dt;
                if (_stuck > 1f)
                {
                    Debug.LogWarning($"[Sidescroller.{GetType().Name}] {_id} 演出移動到 x={_targetX} 被擋住");
                    _moving = false;
                    return 0f;
                }
            }
            else
            {
                _stuck = 0f;
            }
            return dir * _speed;
        }

        #region ICutsceneActor
        public virtual void BeginScripted()
        {
            _scripted = true;
            _moving = false;
            _override = null;
        }

        public virtual void EndScripted()
        {
            _scripted = false;
            _moving = false;
            _override = null;
        }

        public void SetFacing(int dir) => Anim.SetFacing(dir);

        public void ScriptedMoveTo(float x, float speed)
        {
            _targetX = x;
            _speed = speed > 0f ? speed : 2f;
            _moving = true;
            _stuck = 0f;
            _override = null;
        }

        public void Teleport(float x)
        {
            _moving = false;
            var p = new Vector2(x, Body.position.y);
            Body.position = p;
            transform.position = p;
        }

        public bool PlayAction(string action, bool loop)
        {
            if (System.Array.IndexOf(_actions, action) < 0)
                return false;
            _override = action;
            _overrideLoop = loop;
            Anim.Play(SsArt.Get(_spritePrefix + "_" + action), loop, true);
            return true;
        }

        public float GetActionDuration(string action) =>
            System.Array.IndexOf(_actions, action) >= 0 ? SsArt.Get(_spritePrefix + "_" + action).Duration : 0f;
        #endregion
    }
}
