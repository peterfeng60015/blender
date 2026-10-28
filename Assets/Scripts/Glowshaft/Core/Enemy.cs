using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Glowshaft
{
    /// <summary>
    /// 史萊姆：朝玩家直線追擊（被岩石擋住時靠圓形碰撞滑開），碰到玩家造成接觸傷害。
    /// 自帶點光源；被打到時發亮閃一下並被擊退。
    /// </summary>
    public sealed class Enemy : MonoBehaviour
    {
        public EnemyKind Kind { get; private set; }
        public bool IsAlive => _hp > 0;

        private GameManager _game;
        private EnemyStats _stats;
        private Rigidbody2D _body;
        private SpriteAnimator _anim;
        private Light2D _light;
        private int _hp;
        private int _directions;
        private float _avoidSide = 1f;
        private bool _avoiding;
        private float _stuckTimer;
        private Vector2 _stuckCheckPos;
        private float _speed;
        private float _hitFlash;
        private Vector2 _facing = Vector2.down;

        public static Enemy Spawn(GameManager game, Transform parent, EnemyKind kind, Vector2 position, int wave)
        {
            var stats = GameConfig.Enemy(kind);
            var go = new GameObject($"Slime{kind}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.mass = stats.Mass;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            go.AddComponent<CircleCollider2D>().radius = stats.Radius;

            var enemy = go.AddComponent<Enemy>();
            enemy._game = game;
            enemy._stats = stats;
            enemy._body = body;
            enemy.Kind = kind;
            enemy._hp = Mathf.Max(1, Mathf.RoundToInt(stats.Hp * GameConfig.WaveHpMultiplier(wave)));
            // 同種之間略有快慢，群體才不會疊成一團
            enemy._speed = stats.Speed * GameConfig.WaveSpeedMultiplier(wave) * Random.Range(0.9f, 1.1f);
            enemy._avoidSide = Random.value < 0.5f ? 1f : -1f;
            enemy._stuckCheckPos = position;
            var sheet = ArtLibrary.Get(stats.Sheet);
            enemy._directions = sheet.Directions;
            enemy._anim = SpriteAnimator.Attach(go, sheet);
            enemy._light = GlowLights.CreatePointLight(go.transform, stats.LightColor, stats.LightRadius, 0.9f);
            enemy._light.transform.localPosition = new Vector3(0f, stats.Radius * 0.8f, 0f);
            return enemy;
        }

        private void Update()
        {
            if (!GameManager.CanDrive(_game, this))
                return;
            _hitFlash -= Time.deltaTime;
            var flash = Mathf.Clamp01(_hitFlash / 0.12f);
            _light.intensity = 0.9f + 1.6f * flash;
            _anim.Renderer.color = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.55f), flash);
            _anim.Direction = SpriteSet.DirectionIndex(_facing, _directions);

            var player = _game.Player;
            if (player == null || _game.State.IsGameOver || !player.IsAlive)
                return;
            // 接觸傷害：以中心距離判斷，不靠碰撞回呼（物理推擠時 OnCollisionStay 次數不穩）
            var reach = _stats.Radius + GameConfig.PlayerRadius + 0.08f;
            if ((player.Position - _body.position).sqrMagnitude <= reach * reach)
                player.TakeDamage(_stats.ContactDamage, _body.position);
        }

        private void FixedUpdate()
        {
            if (!GameManager.CanDrive(_game, this))
                return;
            var player = _game.Player;
            var target = Vector2.zero;
            if (player != null && !_game.State.IsGameOver)
            {
                var to = player.Position - _body.position;
                if (to.sqrMagnitude > 1e-4f)
                {
                    var dir = to.normalized;
                    var move = Steer(dir, to.magnitude);
                    _facing = move;
                    target = move * _speed;
                }
            }
            // 用加速度逼近目標速度，擊退與推擠才會自然衰減
            _body.linearVelocity = Vector2.MoveTowards(_body.linearVelocity, target, _speed * 6f * Time.fixedDeltaTime);
        }

        /// <summary>
        /// 繞障礙：往玩家方向探一小段，被掛 <see cref="Obstacle"/> 的東西擋住時改沿障礙表面的切線走
        /// （固定繞同一側，避免左右抖動）；每秒檢查一次，幾乎沒前進就換邊繞。
        /// 不做尋路：地圖是散落的岩石群，沿邊滑行就繞得過去。
        /// </summary>
        private Vector2 Steer(Vector2 dir, float distanceToPlayer)
        {
            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer >= 1f)
            {
                if ((_body.position - _stuckCheckPos).sqrMagnitude < 0.3f * 0.3f && _avoiding)
                    _avoidSide = -_avoidSide;
                _stuckCheckPos = _body.position;
                _stuckTimer = 0f;
            }

            var probe = Mathf.Min(distanceToPlayer, _stats.Radius + 0.8f);
            var count = PhysicsQuery.CircleCast(_body.position, _stats.Radius * 0.9f, dir, probe);
            for (var i = 0; i < count; i++)
            {
                var hit = PhysicsQuery.Hits[i];
                if (hit.collider == null || hit.collider.isTrigger || hit.collider.GetComponent<Obstacle>() == null)
                    continue;
                _avoiding = true;
                var tangent = new Vector2(-hit.normal.y, hit.normal.x) * _avoidSide;
                // 切線為主，加一點往玩家的分量，繞過角之後自然轉回去
                return (tangent * 0.85f + dir * 0.15f).normalized;
            }
            _avoiding = false;
            return dir;
        }

        public void Hit(int damage, Vector2 impulse)
        {
            if (!IsAlive)
                return;
            _hp -= damage;
            _hitFlash = 0.12f;
            _body.linearVelocity += impulse / Mathf.Max(0.5f, _stats.Mass);
            if (_hp <= 0)
                Die();
        }

        private void Die()
        {
            if (_game != null)
                _game.OnEnemyKilled(this, _stats);
            Flash.Spawn(transform.parent, _body.position, _stats.LightColor, _stats.LightRadius * 1.3f, 2.2f, 0.35f);
            Destroy(gameObject);
        }

    }
}
