namespace Sidescroller
{
    /// <summary>主角的數值（design.md §7.2～7.4）。服裝不同的數值用 <see cref="CostumeId"/> 當索引。</summary>
    public static class HeroStats
    {
        #region 基本
        public const int MaxHp = 5;
        /// <summary>跳高 3.2、到頂 0.38 秒：g＝2h/t²、v0＝2h/t。</summary>
        public const float Gravity = 2f * 3.2f / (0.38f * 0.38f);
        public const float JumpSpeed = 2f * 3.2f / 0.38f;
        public const float JumpCutMultiplier = 0.5f;
        public const float FallGravityMultiplier = 1.6f;
        public const float MaxFallSpeed = 20f;
        public const float CoyoteTime = 0.1f;
        public const float JumpBufferTime = 0.12f;
        public const float GroundAccel = 60f;
        public const float AirAccel = 40f;
        /// <summary>從單向平台落下時忽略那片平台的秒數。</summary>
        public const float DropThroughTime = 0.25f;
        #endregion

        #region 受傷
        public const float KnockbackX = 5f;
        public const float KnockbackY = 6f;
        public const float HurtStun = 0.25f;
        public const float HurtInvincible = 1.0f;
        #endregion

        #region 攻擊
        // 使用者回饋初始攻擊距離過短：判定框加長到前方 0.1～2.1、高 0.1～1.5，有效時間放寬（原本寬 1.2、高 1.0、0.06～0.16 秒）
        public const float MeleeWidth = 2.0f, MeleeHeight = 1.4f, MeleeOffsetX = 1.1f, MeleeOffsetY = 0.8f;
        public const float MeleeActiveStart = 0.04f, MeleeActiveEnd = 0.20f, MeleeTotal = 0.32f;

        public const int SlingDamage = 1;
        public const float SlingSpeed = 14f, SlingLife = 0.8f, SlingCooldown = 0.35f;
        public const int SlingMaxOnScreen = 3;
        /// <summary>射擊動作顯示多久（只是動畫，不鎖移動）。</summary>
        public const float ShootPoseTime = 0.25f;

        public const int RollDamage = 2;
        public const float RollTime = 0.35f, RollDistance = 4.5f, RollRadius = 0.45f, RollCrawlSpeed = 3f;

        public const float CostumeSwitchCooldown = 0.4f;
        #endregion

        #region 碰撞體
        public const float BodyWidth = 0.7f, BodyHeight = 1.4f;
        #endregion

        #region 服裝（索引＝(int)CostumeId：Day、Express）
        // 服裝的差異和裝備數值相加（loot-design.md §2）：日班甩包 2、滾殼冷卻 1.2 秒；特急甩包 1、冷卻 0.8 秒（裝備基礎值見 GearStats）
        private static readonly float[] RunSpeeds = { 6.0f, 7.0f };
        private static readonly int[] MeleeDamageBonuses = { 0, -1 };
        private static readonly float[] RollCooldownDeltas = { 0f, -0.4f };

        public static float RunSpeed(CostumeId c) => RunSpeeds[(int)c];
        /// <summary>服裝對近戰傷害的加成（加在 <see cref="GearStats.MeleeBaseDamage"/> 上）。</summary>
        public static int MeleeDamageBonus(CostumeId c) => MeleeDamageBonuses[(int)c];
        /// <summary>服裝對滾殼冷卻的增減（秒，加在 <see cref="GearStats.ShellBaseRollCooldown"/> 上）。</summary>
        public static float RollCooldownDelta(CostumeId c) => RollCooldownDeltas[(int)c];
        public static string SpritePrefix(CostumeId c) => c == CostumeId.Express ? "SsHeroExpress" : "SsHeroDay";
        #endregion
    }
}
