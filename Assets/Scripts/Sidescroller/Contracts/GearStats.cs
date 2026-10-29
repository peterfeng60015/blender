using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 裝備的基礎數值、強化加成與詞條數值範圍（loot-design.md §2、§6），以及身上裝備＋服裝合起來的主角戰鬥數值。
    /// 主角在裝備欄、強化等級或服裝改變時呼叫 <see cref="Compute"/> 重新計算。
    /// </summary>
    public sealed class GearStats
    {
        #region 基礎數值（依種類固定）
        /// <summary>近戰基礎傷害（日班制服下就是實際傷害）；再加服裝的 <see cref="HeroStats.MeleeDamageBonus"/>（特急 -1）。</summary>
        public const int MeleeBaseDamage = 2;
        public const int RangedBaseDamage = 1;
        public const int ShellBaseRollDamage = 2;
        /// <summary>殼甲的基礎滾殼冷卻；加上服裝的 <see cref="HeroStats.RollCooldownDelta"/>。</summary>
        public const float ShellBaseRollCooldown = 1.2f;
        /// <summary>冷卻縮短的下限（避免詞條＋強化疊到 0）。</summary>
        private const float MinRollCooldown = 0.3f;
        #endregion

        #region 強化加成（偶數級傷害 +1、奇數級攻擊速度 +5%／滾殼冷卻 -5%）
        public const int EnhanceSpeedStep = 5;

        /// <summary>強化到 level 累計的傷害加成（武器傷害或滾殼傷害）：+2、+4 各 +1。</summary>
        public static int EnhanceDamage(int level) => Mathf.Max(0, level) / 2;

        /// <summary>強化到 level 累計的百分比加成（武器攻擊速度或滾殼冷卻縮短）：+1、+3、+5 各 5%。</summary>
        public static int EnhancePercent(int level) => (Mathf.Max(0, level) + 1) / 2 * EnhanceSpeedStep;
        #endregion

        #region 合計結果（Compute 填）
        public int MeleeDamage;
        /// <summary>近戰動作時間倍率（&lt;1＝變快）：攻擊速度 +N% → 1/(1+N%)。</summary>
        public float MeleeTimeScale = 1f;
        /// <summary>近戰判定框寬度倍率（攻擊範圍 +N%）。</summary>
        public float MeleeReach = 1f;
        public bool MeleeKnockback;

        public int RangedDamage;
        public float ShootCooldown = HeroStats.SlingCooldown;
        /// <summary>射程倍率（子彈存活時間）。</summary>
        public float RangedReach = 1f;
        public int ExtraShots;
        public bool Pierce;

        public int RollDamage;
        public float RollCooldown;

        public int MaxHp = HeroStats.MaxHp;
        public float HurtInvincible = HeroStats.HurtInvincible;
        /// <summary>跑速倍率（移動速度 +N%）。</summary>
        public float MoveScale = 1f;
        /// <summary>零件掉落加成（%）。</summary>
        public int PartFindPercent;
        #endregion

        /// <summary>依身上三件裝備與服裝重算全部欄位（空著的欄位對應的攻擊本來就不能用，數值照基礎值填）。</summary>
        public void Compute(LootState loot, CostumeId costume)
        {
            var melee = loot != null ? loot.GetEquipped(EquipSlot.Melee) : null;
            var ranged = loot != null ? loot.GetEquipped(EquipSlot.Ranged) : null;
            var shell = loot != null ? loot.GetEquipped(EquipSlot.Shell) : null;

            var meleeSpeed = Level(melee, EnhancePercent) + Sum(melee, AffixId.AttackSpeed);
            MeleeDamage = MeleeBaseDamage + HeroStats.MeleeDamageBonus(costume) + Level(melee, EnhanceDamage) + Mathf.RoundToInt(Sum(melee, AffixId.Damage));
            MeleeTimeScale = 1f / (1f + meleeSpeed / 100f);
            MeleeReach = 1f + Sum(melee, AffixId.AttackRange) / 100f;
            MeleeKnockback = Sum(melee, AffixId.Knockback) > 0f;

            var rangedSpeed = Level(ranged, EnhancePercent) + Sum(ranged, AffixId.AttackSpeed);
            RangedDamage = RangedBaseDamage + Level(ranged, EnhanceDamage) + Mathf.RoundToInt(Sum(ranged, AffixId.Damage));
            ShootCooldown = HeroStats.SlingCooldown / (1f + rangedSpeed / 100f);
            RangedReach = 1f + Sum(ranged, AffixId.Range) / 100f;
            ExtraShots = Mathf.RoundToInt(Sum(ranged, AffixId.MultiShot));
            Pierce = Sum(ranged, AffixId.Pierce) > 0f;

            var rollCut = Level(shell, EnhancePercent) + Sum(shell, AffixId.RollCooldown);
            RollDamage = ShellBaseRollDamage + Level(shell, EnhanceDamage) + Mathf.RoundToInt(Sum(shell, AffixId.RollDamage));
            RollCooldown = Mathf.Max(MinRollCooldown, (ShellBaseRollCooldown + HeroStats.RollCooldownDelta(costume)) * (1f - rollCut / 100f));

            MaxHp = HeroStats.MaxHp + Mathf.RoundToInt(Sum(shell, AffixId.MaxHp));
            HurtInvincible = HeroStats.HurtInvincible + Sum(shell, AffixId.HurtInvincible);

            MoveScale = 1f + (Sum(melee, AffixId.MoveSpeed) + Sum(ranged, AffixId.MoveSpeed) + Sum(shell, AffixId.MoveSpeed)) / 100f;
            PartFindPercent = Mathf.RoundToInt(Sum(melee, AffixId.PartFind) + Sum(ranged, AffixId.PartFind) + Sum(shell, AffixId.PartFind));
        }

        #region 內部
        private delegate int LevelBonus(int level);

        private static int Level(ItemData item, LevelBonus bonus) => item != null ? bonus(item.Level) : 0;

        private static float Sum(ItemData item, AffixId id)
        {
            if (item == null)
                return 0f;
            var total = 0f;
            foreach (var a in item.Affixes)
            {
                if (a.Id == id)
                    total += a.Value;
            }
            return total;
        }
        #endregion
    }
}
