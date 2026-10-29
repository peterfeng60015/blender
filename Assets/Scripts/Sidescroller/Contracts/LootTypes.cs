using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>道具種類（loot-design.md §1）。</summary>
    public enum ItemKind { Part = 0, Melee = 1, Ranged = 2, Shell = 3 }

    /// <summary>稀有度：白、藍、黃（loot-design.md §2）。</summary>
    public enum Rarity { Common = 0, Magic = 1, Rare = 2 }

    /// <summary>身上的三個裝備欄，對應 J 甩包／K 彈弓／L 滾殼。數值＝(int)AttackId。</summary>
    public enum EquipSlot { Melee = 0, Ranged = 1, Shell = 2 }

    /// <summary>詞條（loot-design.md §2 的表，順序照表）。</summary>
    public enum AffixId
    {
        Damage = 0, AttackSpeed = 1, AttackRange = 2, Knockback = 3, Range = 4, MultiShot = 5, Pierce = 6,
        RollDamage = 7, RollCooldown = 8, MaxHp = 9, HurtInvincible = 10, MoveSpeed = 11, PartFind = 12
    }

    /// <summary>一條詞條。Value 的單位依種類：+N 是整數、百分比存 N（18＝18%）、秒數存秒（0.3）；固定詞條為 1。</summary>
    public sealed class Affix
    {
        public AffixId Id;
        public float Value;

        /// <summary>顯示文字，例如「傷害 +2」「攻擊速度 +18%」「滾殼冷卻 -15%」「受傷後無敵 +0.3 秒」。</summary>
        public string Describe()
        {
            var n = Mathf.RoundToInt(Value);
            switch (Id)
            {
                case AffixId.Damage: return $"傷害 +{n}";
                case AffixId.AttackSpeed: return $"攻擊速度 +{n}%";
                case AffixId.AttackRange: return $"攻擊範圍 +{n}%";
                case AffixId.Knockback: return "擊退強化";
                case AffixId.Range: return $"射程 +{n}%";
                case AffixId.MultiShot: return $"多重射擊 +{n}";
                case AffixId.Pierce: return "穿透";
                case AffixId.RollDamage: return $"滾殼傷害 +{n}";
                // 字型沒有 U+2212（−），減號用半形
                case AffixId.RollCooldown: return $"滾殼冷卻 -{n}%";
                case AffixId.MaxHp: return $"最大血量 +{n}";
                case AffixId.HurtInvincible: return "受傷後無敵 +" + Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " 秒";
                case AffixId.MoveSpeed: return $"移動速度 +{n}%";
                case AffixId.PartFind: return $"零件掉落 +{n}%";
                default: return Id.ToString();
            }
        }
    }

    /// <summary>
    /// 一件道具（零件堆疊或一件裝備）。參考型別、以實例比對身分；背包與裝備欄裡放的就是這個實例。
    /// 由 data 建立（掉落、開場、劇情發放）；pickup、ui 只讀，改變一律透過 <see cref="LootState"/>／<see cref="Enhance"/>。
    /// </summary>
    public sealed class ItemData
    {
        public const int PartStack = 20;

        public ItemData(ItemKind kind, string name, Rarity rarity, int count = 1)
        {
            Kind = kind;
            Name = name;
            Rarity = rarity;
            Count = kind == ItemKind.Part ? Mathf.Clamp(count, 1, PartStack) : 1;
        }

        public ItemKind Kind { get; }
        /// <summary>基本名稱，例如「破舊郵差包」「零件」。</summary>
        public string Name { get; }
        public Rarity Rarity { get; }
        /// <summary>零件的堆疊數（1～20）；裝備恆為 1。只由 LootState 改。</summary>
        public int Count { get; internal set; }
        /// <summary>強化等級 0～<see cref="Enhance.MaxLevel"/>；零件恆為 0。只由 Enhance 改。</summary>
        public int Level { get; internal set; }
        /// <summary>隨機詞條（零件與白色裝備是空的）。</summary>
        public readonly List<Affix> Affixes = new List<Affix>();

        public bool IsEquipment => Kind != ItemKind.Part;
        /// <summary>裝備對應的欄位；零件呼叫無意義（回 Melee）。</summary>
        public EquipSlot Slot => Kind == ItemKind.Ranged ? EquipSlot.Ranged : Kind == ItemKind.Shell ? EquipSlot.Shell : EquipSlot.Melee;
        /// <summary>佔格寬：零件 1、武器 2、殼甲 2。</summary>
        public int Width => Kind == ItemKind.Part ? 1 : 2;
        /// <summary>佔格高：零件 1、武器 3、殼甲 2。</summary>
        public int Height => Kind == ItemKind.Part ? 1 : Kind == ItemKind.Shell ? 2 : 3;

        /// <summary>介面上的名稱：強化過的加「 +N」，零件加「 ×N」（data 可調整格式）。</summary>
        public string DisplayName => Kind == ItemKind.Part ? $"{Name} ×{Count}" : Level > 0 ? $"{Name} +{Level}" : Name;

        /// <summary>種類名稱：零件／近戰武器／遠程武器／殼甲。</summary>
        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case ItemKind.Melee: return "近戰武器";
                    case ItemKind.Ranged: return "遠程武器";
                    case ItemKind.Shell: return "殼甲";
                    default: return "零件";
                }
            }
        }

        /// <summary>
        /// 基礎數值的說明行（含強化加成，不含詞條與服裝），例如「傷害 2」「攻擊速度 +5%」；零件回空陣列。
        /// 殼甲：「滾殼傷害 2」，強化過再加「滾殼冷卻 -5%」。
        /// </summary>
        public string[] DescribeBase()
        {
            if (!IsEquipment)
                return new string[0];
            var damage = GearStats.EnhanceDamage(Level);
            var percent = GearStats.EnhancePercent(Level);
            var lines = new List<string>(2);
            if (Kind == ItemKind.Shell)
            {
                lines.Add($"滾殼傷害 {GearStats.ShellBaseRollDamage + damage}");
                if (percent > 0)
                    lines.Add($"滾殼冷卻 -{percent}%");
            }
            else
            {
                lines.Add($"傷害 {(Kind == ItemKind.Melee ? GearStats.MeleeBaseDamage : GearStats.RangedBaseDamage) + damage}");
                if (percent > 0)
                    lines.Add($"攻擊速度 +{percent}%");
            }
            return lines.ToArray();
        }

        /// <summary>詞條的說明行，順序同 <see cref="Affixes"/>。</summary>
        public string[] DescribeAffixes()
        {
            var lines = new string[Affixes.Count];
            for (var i = 0; i < lines.Length; i++)
                lines[i] = Affixes[i].Describe();
            return lines;
        }

        /// <summary>稀有度顏色：白、藍、黃（光柱、名稱、說明框共用）。</summary>
        public static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Magic: return new Color32(0x5A, 0x9B, 0xFF, 0xFF);
                case Rarity.Rare: return new Color32(0xFF, 0xD2, 0x3C, 0xFF);
                default: return new Color32(0xF2, 0xF2, 0xF2, 0xFF);
            }
        }
    }
}
