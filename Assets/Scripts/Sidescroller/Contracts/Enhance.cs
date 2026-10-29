namespace Sidescroller
{
    /// <summary>強化的結果。</summary>
    public enum EnhanceResult
    {
        /// <summary>成功，等級 +1。</summary>
        Success = 0,
        /// <summary>失敗：零件消耗，等級不變。</summary>
        Fail = 1,
        /// <summary>失敗：零件消耗，等級 −1（+3 之後）。</summary>
        FailDowngrade = 2,
        /// <summary>零件不夠，沒有消耗。</summary>
        NotEnoughParts = 3,
        /// <summary>已到最高等級或不是裝備，沒有消耗。</summary>
        NotAllowed = 4
    }

    /// <summary>
    /// 強化規則（loot-design.md §6）：費用、成功率、失敗規則與每級效果。只有 data 實作；ui 的強化介面照這裡顯示與呼叫。
    /// 參數 level＝目前等級（要從 level 升到 level+1）。
    /// </summary>
    public static class Enhance
    {
        public const int MaxLevel = 5;

        private const string Log = "[Sidescroller.Enhance]";
        private static readonly int[] Costs = { 3, 5, 8, 12, 18 };
        private static readonly int[] Percents = { 100, 85, 65, 45, 30 };
        /// <summary>從這個等級起失敗會掉級（+3 → +4）。</summary>
        private const int DowngradeFrom = 3;

        /// <summary>需要零件：3、5、8、12、18。已滿級或負數回 0。</summary>
        public static int Cost(int level) => InRange(level) ? Costs[level] : 0;

        /// <summary>成功率（整數百分比）：100、85、65、45、30。已滿級或負數回 0。</summary>
        public static int SuccessPercent(int level) => InRange(level) ? Percents[level] : 0;

        /// <summary>失敗會不會掉級（+3 → +4 起）。</summary>
        public static bool DowngradeOnFail(int level) => InRange(level) && level >= DowngradeFrom;

        /// <summary>失敗後果的說明，例如「失敗：零件消耗，等級不變」；100% 成功時回「—」。</summary>
        public static string FailureText(int level)
        {
            if (!InRange(level) || SuccessPercent(level) >= 100)
                return "—";
            return DowngradeOnFail(level) ? "失敗：零件消耗，等級 -1" : "失敗：零件消耗，等級不變";
        }

        /// <summary>
        /// 下一級的效果說明：武器升到偶數級「傷害 +1」、奇數級「攻擊速度 +5%」；殼甲偶數級「滾殼傷害 +1」、奇數級「滾殼冷卻 -5%」。
        /// 已滿級、零件或 null 回 null。
        /// </summary>
        public static string NextLevelEffect(ItemData item)
        {
            if (item == null || !item.IsEquipment || item.Level >= MaxLevel)
                return null;
            var next = item.Level + 1;
            var damageUp = GearStats.EnhanceDamage(next) > GearStats.EnhanceDamage(item.Level);
            if (item.Kind == ItemKind.Shell)
                return damageUp ? "滾殼傷害 +1" : $"滾殼冷卻 -{GearStats.EnhanceSpeedStep}%";
            return damageUp ? "傷害 +1" : $"攻擊速度 +{GearStats.EnhanceSpeedStep}%";
        }

        /// <summary>
        /// 執行一次強化：檢查等級與零件 → 從 loot 扣零件 → 擲亂數（不做保底）→ 改 item.Level → 觸發 loot.Changed。
        /// item 可以在格子盤上或裝備欄上（裝備欄上的強化後主角數值立刻更新）；不在 loot 裡（例如拿在手上）回 NotAllowed。
        /// </summary>
        public static EnhanceResult TryEnhance(LootState loot, ItemData item)
        {
            if (loot == null || item == null || !item.IsEquipment || item.Level >= MaxLevel || !loot.Contains(item))
                return EnhanceResult.NotAllowed;
            var level = item.Level;
            var cost = Cost(level);
            if (loot.PartsTotal < cost)
                return EnhanceResult.NotEnoughParts;
            loot.SpendPartsInternal(cost);
            // Random.Range(0, 100) 是 0～99：成功率 100 一定成功、30 有 30 個值成功
            var success = UnityEngine.Random.Range(0, 100) < SuccessPercent(level);
            EnhanceResult result;
            if (success)
            {
                item.Level = level + 1;
                result = EnhanceResult.Success;
            }
            else if (DowngradeOnFail(level))
            {
                item.Level = level - 1;
                result = EnhanceResult.FailDowngrade;
            }
            else
            {
                result = EnhanceResult.Fail;
            }
            UnityEngine.Debug.Log($"{Log} {item.Name} +{level} → +{item.Level}（{result}，花 {cost} 零件）");
            loot.NotifyChanged();
            return result;
        }

        private static bool InRange(int level) => level >= 0 && level < MaxLevel;
    }
}
