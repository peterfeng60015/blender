using UnityEngine;

namespace Glowshaft
{
    public enum EnemyKind
    {
        Green = 0,
        Purple = 1,
        Orange = 2,
    }

    /// <summary>一種史萊姆的基礎數值（波數加成在 <see cref="GameConfig.WaveHpMultiplier"/> 等處另外乘）。</summary>
    public sealed class EnemyStats
    {
        public string Sheet;
        public int Hp;
        public float Speed;
        public float Radius;
        public float Mass;
        public int ContactDamage;
        public int Score;
        public Color LightColor;
        public float LightRadius;
        public float DropChance;
    }

    /// <summary>所有可調數值集中在這裡。長度單位＝Unity 單位（1 單位約一個礦工的身高）。</summary>
    public static class GameConfig
    {
        #region 玩家
        public const int PlayerMaxHp = 10;
        public const float PlayerSpeed = 5f;
        public const float PlayerRadius = 0.35f;
        public const float PlayerInvulnerableSeconds = 1.0f;
        public const float PlayerKnockback = 6f;
        /// <summary>頭燈、子彈在畫面上的「離地高度」（sprite 的腳底 pivot 往上的位移）。</summary>
        public const float MuzzleHeight = 0.45f;
        #endregion

        #region 射擊
        public const float FireInterval = 0.12f;
        /// <summary>開局（含重來）後多久才能射擊。</summary>
        public const float SpawnFireDelay = 0.25f;
        public const float BulletSpeed = 16f;
        public const float BulletRadius = 0.12f;
        public const float BulletLifetime = 1.4f;
        public const int BulletDamage = 1;
        public const float BulletKnockback = 2.5f;
        #endregion

        #region 補血
        public const int BatteryHeal = 3;
        public const float BatteryLifetime = 12f;
        public const float BatteryPickupRadius = 0.75f;
        public const int BatteryScore = 5;
        #endregion

        #region 波數
        public const float WaveBreakSeconds = 2.5f;
        public const float SpawnMinDistanceFromPlayer = 8f;
        public const int WaveClearBonusPerWave = 50;
        public const float GameOverRestartDelay = 0.8f;
        #endregion

        public static int WaveEnemyCount(int wave) => 5 + 3 * (wave - 1);
        public static int WaveMaxAlive(int wave) => Mathf.Min(30, 6 + 2 * wave);
        public static float WaveSpawnInterval(int wave) => Mathf.Max(0.3f, 1.2f - 0.08f * (wave - 1));
        public static float WaveHpMultiplier(int wave) => 1f + 0.15f * (wave - 1);
        public static float WaveSpeedMultiplier(int wave) => Mathf.Min(1.5f, 1f + 0.04f * (wave - 1));

        /// <summary>第 wave 波抽一隻的種類：第 2 波起出現紫色、第 3 波起出現橘色，比例逐波上升。</summary>
        public static EnemyKind PickKind(int wave)
        {
            var purple = wave >= 2 ? Mathf.Min(0.35f, 0.15f + 0.04f * (wave - 2)) : 0f;
            var orange = wave >= 3 ? Mathf.Min(0.3f, 0.1f + 0.04f * (wave - 3)) : 0f;
            var r = Random.value;
            if (r < orange)
                return EnemyKind.Orange;
            if (r < orange + purple)
                return EnemyKind.Purple;
            return EnemyKind.Green;
        }

        private static readonly EnemyStats[] Enemies =
        {
            // 一般：慢、會追玩家
            new EnemyStats
            {
                Sheet = "SlimeGreen_Move", Hp = 3, Speed = 1.8f, Radius = 0.38f, Mass = 1f, ContactDamage = 1, Score = 10,
                LightColor = new Color(0.4f, 1f, 0.45f), LightRadius = 2.6f, DropChance = 0.1f,
            },
            // 快速、血少
            new EnemyStats
            {
                Sheet = "SlimePurple_Move", Hp = 1, Speed = 3.4f, Radius = 0.3f, Mass = 0.7f, ContactDamage = 1, Score = 15,
                LightColor = new Color(0.8f, 0.45f, 1f), LightRadius = 2.2f, DropChance = 0.08f,
            },
            // 大隻、血多、慢
            new EnemyStats
            {
                Sheet = "SlimeOrange_Move", Hp = 9, Speed = 1.2f, Radius = 0.65f, Mass = 3f, ContactDamage = 2, Score = 40,
                LightColor = new Color(1f, 0.6f, 0.2f), LightRadius = 3.5f, DropChance = 0.35f,
            },
        };

        public static EnemyStats Enemy(EnemyKind kind) => Enemies[(int)kind];
    }
}
