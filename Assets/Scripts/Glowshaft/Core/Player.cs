using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Glowshaft
{
    /// <summary>
    /// 玩家（採礦機器人）：WASD 移動、滑鼠瞄準、左鍵射擊。8 方向 sprite 依瞄準方向切換，
    /// 移動中播 Run、靜止播 Idle。頭燈（Spot）隨瞄準方向轉動。
    /// </summary>
    public sealed class Player : MonoBehaviour
    {
        public Vector2 Position => _body.position;
        public Vector2 AimDirection { get; private set; } = Vector2.down;
        public bool IsAlive => _game != null && _game.State != null && _game.State.Hp > 0;

        private GameManager _game;
        private Rigidbody2D _body;
        private SpriteAnimator _anim;
        private Light2D _headlight;
        private SpriteSet _idle;
        private SpriteSet _run;
        private Vector2 _move;
        private Vector2 _knockback;
        private float _fireCooldown;
        private float _invulnerable;

        public static Player Create(GameManager game, Transform parent, Vector2 position)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.mass = 1f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<CircleCollider2D>().radius = GameConfig.PlayerRadius;

            var player = go.AddComponent<Player>();
            player._game = game;
            player._body = body;
            // 在 Game Over 畫面用左鍵重來時，那次點擊通常還按著；開局短暫不射擊，免得一出生就開一槍
            player._fireCooldown = GameConfig.SpawnFireDelay;
            player._idle = ArtLibrary.Get("Miner_Idle");
            player._run = ArtLibrary.Get("Miner_Run");
            player._anim = SpriteAnimator.Attach(go, player._idle);

            // 頭燈在頭頂高度；身邊再加一盞微弱暖光，讓機器人本身不會完全隱沒在黑暗裡
            player._headlight = GlowLights.CreateHeadlight(go.transform);
            player._headlight.transform.localPosition = new Vector3(0f, GameConfig.MuzzleHeight + 0.15f, 0f);
            var aura = GlowLights.CreatePointLight(go.transform, new Color(1f, 0.85f, 0.6f), 2.2f, 0.55f);
            aura.transform.localPosition = new Vector3(0f, GameConfig.MuzzleHeight, 0f);
            return player;
        }

        private void Update()
        {
            if (!GameManager.CanDrive(_game, this))
                return;
            var dt = Time.deltaTime;
            _fireCooldown -= dt;
            _invulnerable -= dt;

            if (!IsAlive || _game.State.IsGameOver)
            {
                _move = Vector2.zero;
                _anim.Play(_idle);
                return;
            }

            _move = GameInput.Move;
            if (GameInput.TryGetAimWorld(_game.Camera, out var aimWorld))
            {
                var aim = aimWorld - (Vector2)transform.position;
                if (aim.sqrMagnitude > 0.01f)
                    AimDirection = aim.normalized;
            }
            else if (_move.sqrMagnitude > 0.01f)
            {
                AimDirection = _move.normalized;
            }

            _anim.Play(_move.sqrMagnitude > 0.01f ? _run : _idle);
            _anim.Direction = SpriteSet.DirectionIndex(AimDirection, _idle.Directions);
            // 不用 transform.up = AimDirection：正下方時 FromToRotation 可能繞 X 軸翻轉，改成明確繞 Z 轉
            var aimAngle = Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg;
            _headlight.transform.localRotation = Quaternion.Euler(0f, 0f, aimAngle - 90f);

            if (GameInput.Fire && _fireCooldown <= 0f)
            {
                _fireCooldown = GameConfig.FireInterval;
                Bullet.Spawn(_game.WorldRoot, Position + AimDirection * (GameConfig.PlayerRadius + 0.15f), AimDirection);
            }

            // 無敵時間內閃爍
            var visible = _invulnerable <= 0f || Mathf.Repeat(_invulnerable, 0.16f) > 0.08f;
            _anim.Renderer.color = visible ? Color.white : new Color(1f, 0.4f, 0.4f, 0.5f);
        }

        private void FixedUpdate()
        {
            if (!GameManager.CanDrive(_game, this))
                return;
            _knockback = Vector2.MoveTowards(_knockback, Vector2.zero, 30f * Time.fixedDeltaTime);
            _body.linearVelocity = _move * GameConfig.PlayerSpeed + _knockback;
        }

        /// <summary>受傷；無敵時間內或已死亡時忽略。from＝傷害來源位置（決定擊退方向）。</summary>
        public void TakeDamage(int amount, Vector2 from)
        {
            if (_invulnerable > 0f || !IsAlive || _game.State.IsGameOver)
                return;
            _invulnerable = GameConfig.PlayerInvulnerableSeconds;
            var away = Position - from;
            _knockback = (away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.up) * GameConfig.PlayerKnockback;
            _game.State.SetHp(_game.State.Hp - amount);
            if (_game.State.Hp <= 0)
                _game.OnPlayerDied();
        }

        public void Heal(int amount)
        {
            if (!IsAlive)
                return;
            _game.State.SetHp(_game.State.Hp + amount);
        }

        /// <summary>Game Over 時呼叫：停下、變暗、關頭燈。</summary>
        public void ShowDead()
        {
            _move = Vector2.zero;
            _knockback = Vector2.zero;
            _body.linearVelocity = Vector2.zero;
            _anim.Renderer.color = new Color(0.35f, 0.35f, 0.4f);
            _headlight.enabled = false;
        }
    }
}
