using UnityEngine;

namespace Sidescroller
{
    /// <summary>龜婆（NPC）：不能打、不擋路，平常播 Idle；對話時由演出播 Talk。</summary>
    public sealed class Mentor : SimpleActor
    {
        private static readonly string[] MentorActions = { "Idle", "Talk" };

        public static Mentor Create(SpawnPoint spawn, EnemyContext ctx)
        {
            var go = new GameObject("Npc " + spawn.Id);
            go.transform.SetParent(ctx.Parent, false);
            go.transform.position = spawn.Position;
            var mentor = go.AddComponent<Mentor>();
            mentor.Setup(ActorKind.Npc, spawn, new Vector2(0.9f, 1.1f), SsSorting.Npc, "SsMentor", MentorActions, "Idle", null, false);
            ctx.IgnoreHero(mentor.Box);
            return mentor;
        }
    }
}
