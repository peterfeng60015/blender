using System;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// enemy 還沒實作某種敵人時的替身：站著不動、不攻擊，只會被打、扣血、冒煙消失。
    /// kind＝"boss" 時另外實作 <see cref="IBossController"/>（40 血），讓 Boss 房與擊敗流程在 enemy 完成前就能測。
    /// </summary>
    public sealed class PlaceholderEnemy : SimpleActor, IDamageable, IBossController
    {
        private EnemyContext _ctx;
        private bool _isBoss;
        private bool _fighting;
        private bool _stopped;
        private bool _dead;

        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public event Action<int, int> HpChanged;
        public event Action Defeated;

        public static GameObject Create(SpawnPoint spawn, EnemyContext ctx)
        {
            var go = new GameObject("PlaceholderEnemy " + spawn.Id);
            go.transform.SetParent(ctx.Parent, false);
            go.transform.position = spawn.Position;
            var e = go.AddComponent<PlaceholderEnemy>();
            e._ctx = ctx;
            switch (spawn.Kind)
            {
                case "pigeon":
                    e.Setup(ActorKind.Enemy, spawn, new Vector2(0.8f, 0.8f), SsSorting.Enemy, "SsPigeon",
                        new[] { "Fly", "Attack", "Hurt" }, "Fly", null, true);
                    e.MaxHp = 1;
                    break;
                case "rhino":
                    e.Setup(ActorKind.Enemy, spawn, new Vector2(1.3f, 1.5f), SsSorting.Enemy, "SsRhino",
                        new[] { "Idle", "Walk", "Attack", "Hurt" }, "Idle", "Walk", false);
                    e.MaxHp = 5;
                    break;
                case "boss":
                    e.Setup(ActorKind.Boss, spawn, new Vector2(3f, 3.8f), SsSorting.Boss, "SsBoss",
                        new[] { "Idle", "Charge", "Spray", "Stomp", "Hurt" }, "Idle", "Charge", false);
                    e.MaxHp = 40;
                    e._isBoss = true;
                    break;
                default:
                    if (spawn.Kind != "rat")
                        Debug.LogWarning($"[Sidescroller.PlaceholderEnemy] 不認得的角色種類「{spawn.Kind}」（{spawn.Id}），當業務鼠");
                    e.Setup(ActorKind.Enemy, spawn, new Vector2(0.6f, 1.3f), SsSorting.Enemy, "SsRat",
                        new[] { "Idle", "Walk", "Attack", "Hurt" }, "Idle", "Walk", false);
                    e.MaxHp = 3;
                    break;
            }
            e.Hp = e.MaxHp;
            Debug.Log($"[Sidescroller.PlaceholderEnemy] {spawn.Id}（{spawn.Kind}）用佔位敵人");
            return go;
        }

        public bool TakeHit(HitInfo hit)
        {
            if (_dead || hit == null || hit.Amount <= 0 || (_isBoss && (_stopped || !_fighting)))
                return false;
            Hp = Mathf.Max(0, Hp - hit.Amount);
            Anim.Flash(new Color(1f, 0.55f, 0.55f), 0.1f);
            FxPlayer.Spawn(_ctx.Parent, "SsFx_Hit", (Vector2)transform.position + new Vector2(0f, Box.size.y * 0.5f), hit.Direction >= 0 ? 1 : -1);
            if (_isBoss)
                HpChanged?.Invoke(Hp, MaxHp);
            if (Hp > 0)
                return true;
            _dead = true;
            if (_isBoss)
            {
                PlayAction("Hurt", true);
                Defeated?.Invoke();
            }
            else
            {
                FxPlayer.Spawn(_ctx.Parent, "SsFx_Poof", (Vector2)transform.position + new Vector2(0f, 0.6f));
                Destroy(gameObject);
            }
            return true;
        }

        public void StartFight()
        {
            if (_fighting || _stopped)
                return;
            _fighting = true;
            Debug.Log("[Sidescroller.PlaceholderEnemy] 佔位 Boss：StartFight（沒有 AI）");
        }

        public void StopAll()
        {
            _stopped = true;
            _fighting = false;
        }
    }
}
