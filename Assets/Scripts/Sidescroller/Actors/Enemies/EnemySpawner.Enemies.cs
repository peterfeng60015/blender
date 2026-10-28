using UnityEngine;

namespace Sidescroller
{
    /// <summary>enemy 對 <see cref="EnemySpawner"/> 的實作：依 SpawnPoint.Kind 建雜兵與 Boss。</summary>
    public static partial class EnemySpawner
    {
        public const string KindRat = "rat";
        public const string KindPigeon = "pigeon";
        public const string KindRhino = "rhino";
        public const string KindBoss = "boss";

        static partial void CreateEnemy(SpawnPoint spawn, EnemyContext ctx, ref GameObject result)
        {
            if (spawn == null || ctx == null)
                return;
            switch (spawn.Kind)
            {
                case KindRat:
                    result = EnemyActor.Create<RatEnemy>(spawn, ctx);
                    break;
                case KindPigeon:
                    result = EnemyActor.Create<PigeonEnemy>(spawn, ctx);
                    break;
                case KindRhino:
                    result = EnemyActor.Create<RhinoEnemy>(spawn, ctx);
                    break;
                case KindBoss:
                    result = EnemyActor.Create<BossActor>(spawn, ctx);
                    break;
            }
        }
    }
}
