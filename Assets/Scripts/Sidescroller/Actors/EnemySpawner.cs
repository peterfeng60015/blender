using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// GameFlow 依 <see cref="SpawnPoint.Kind"/> 建雜兵與 Boss 的入口。
    /// 真正的建立由 enemy 在 <c>Actors/Enemies/</c> 以 partial 方法 <see cref="CreateEnemy"/> 實作；
    /// 還沒實作（或不認得這個 kind）時改建 <see cref="PlaceholderEnemy"/>，讓流程照常能跑。
    /// </summary>
    public static partial class EnemySpawner
    {
        /// <summary>
        /// enemy 實作：依 spawn.Kind（"rat"／"pigeon"／"rhino"／"boss"）在 ctx.Parent 底下建好角色並設 result；
        /// 不認得的 kind 不要設 result。建好的根物件要有 ActorTag、IDamageable、ICutsceneActor（建立時 Register），
        /// Boss 另外要有 IBossController，而且 AI 先停著。
        /// </summary>
        static partial void CreateEnemy(SpawnPoint spawn, EnemyContext ctx, ref GameObject result);

        /// <summary>"npc" 以外的 kind 都走這裡；回傳 null 不會發生（最差是佔位敵人）。</summary>
        public static GameObject Create(SpawnPoint spawn, EnemyContext ctx)
        {
            GameObject result = null;
            CreateEnemy(spawn, ctx, ref result);
            if (result == null)
                result = PlaceholderEnemy.Create(spawn, ctx);
            foreach (var col in result.GetComponentsInChildren<Collider2D>(true))
            {
                if (!col.isTrigger)
                    ctx.IgnoreHero(col);
            }
            return result;
        }
    }
}
