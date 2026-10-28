using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 能量子彈。沒有 Rigidbody／Collider，每幀用 CircleCast（<see cref="PhysicsQuery"/>）掃過這一幀的位移：
    /// 打到史萊姆就造成傷害並消失，打到掛 <see cref="Obstacle"/> 的東西就消失，其餘（玩家、補血）穿過。
    /// </summary>
    public sealed class Bullet : MonoBehaviour
    {
        private Vector2 _direction;
        private float _life;

        public static Bullet Spawn(Transform parent, Vector2 position, Vector2 direction)
        {
            var go = new GameObject("Bullet");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var bullet = go.AddComponent<Bullet>();
            bullet._direction = direction.normalized;
            bullet._life = GameConfig.BulletLifetime;

            var anim = SpriteAnimator.Attach(go, ArtLibrary.Get("Bullet_Idle"), GameConfig.MuzzleHeight);
            anim.SortByY = false;
            anim.FixedOrder = Sorting.Projectile;
            var light = GlowLights.CreatePointLight(go.transform, new Color(0.5f, 0.9f, 1f), 1.6f, 1.1f);
            light.transform.localPosition = new Vector3(0f, GameConfig.MuzzleHeight, 0f);
            return bullet;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            var from = (Vector2)transform.position;
            var distance = GameConfig.BulletSpeed * dt;
            var count = PhysicsQuery.CircleCast(from, GameConfig.BulletRadius, _direction, distance);
            for (var i = 0; i < count; i++)
            {
                var hit = PhysicsQuery.Hits[i];
                var col = hit.collider;
                if (col == null || col.isTrigger)
                    continue;
                var enemy = col.attachedRigidbody != null ? col.attachedRigidbody.GetComponent<Enemy>() : null;
                if (enemy != null)
                {
                    if (!enemy.IsAlive)
                        continue;
                    enemy.Hit(GameConfig.BulletDamage, _direction * GameConfig.BulletKnockback);
                    Impact(hit.point);
                    return;
                }
                if (col.GetComponent<Obstacle>() != null)
                {
                    Impact(hit.point);
                    return;
                }
            }
            transform.position = from + _direction * distance;
        }

        private void Impact(Vector2 point)
        {
            Flash.Spawn(transform.parent, point, new Color(0.5f, 0.9f, 1f), 1.4f, 1.4f, 0.15f);
            Destroy(gameObject);
        }
    }
}
