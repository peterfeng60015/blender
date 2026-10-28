using UnityEngine;

namespace Glowshaft
{
    /// <summary>補血道具（電池）：史萊姆死亡時機率掉落，玩家靠近自動撿起，逾時消失（最後 3 秒閃爍）。</summary>
    public sealed class Pickup : MonoBehaviour
    {
        private GameManager _game;
        private SpriteAnimator _anim;
        private Transform _visual;
        private float _life;

        public static Pickup Spawn(GameManager game, Transform parent, Vector2 position)
        {
            var go = new GameObject("Battery");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var pickup = go.AddComponent<Pickup>();
            pickup._game = game;
            pickup._life = GameConfig.BatteryLifetime;
            pickup._anim = SpriteAnimator.Attach(go, ArtLibrary.Get("Battery_Idle"));
            pickup._visual = pickup._anim.Renderer.transform;
            var light = GlowLights.CreatePointLight(go.transform, new Color(0.4f, 1f, 0.5f), 1.8f, 0.9f);
            light.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            return pickup;
        }

        private void Update()
        {
            if (!GameManager.CanDrive(_game, this))
                return;
            _life -= Time.deltaTime;
            if (_life <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            _visual.localPosition = new Vector3(0f, 0.08f + Mathf.Sin(Time.time * 4f) * 0.06f, 0f);
            _anim.Renderer.enabled = _life > 3f || Mathf.Repeat(_life, 0.3f) > 0.12f;

            var player = _game.Player;
            if (player == null || !player.IsAlive || _game.State.IsGameOver)
                return;
            var r = GameConfig.BatteryPickupRadius;
            if ((player.Position - (Vector2)transform.position).sqrMagnitude > r * r)
                return;
            player.Heal(GameConfig.BatteryHeal);
            _game.State.AddScore(GameConfig.BatteryScore);
            Flash.Spawn(transform.parent, transform.position, new Color(0.4f, 1f, 0.5f), 2.5f, 1.8f, 0.3f);
            Destroy(gameObject);
        }
    }
}
