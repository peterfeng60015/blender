using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 道具範本、詞條抽取與掉落表（loot-design.md §1～§3）。亂數用 UnityEngine.Random，不另外存 static 狀態。
    /// 敵人死亡時 <see cref="EnemyActor"/> 呼叫 <see cref="OnEnemyDefeated"/>：依關卡 id 防刷，產生道具交給 <see cref="LootDrops.Spawn"/>。
    /// </summary>
    public static class LootTables
    {
        private const string Log = "[Sidescroller.LootTables]";

        #region 名稱
        public const string PartName = "零件";
        public const string StarterMeleeName = "破舊郵差包";
        public const string StarterRangedName = "急件彈弓";
        public const string StarterShellName = "殼帶護片";

        private static readonly string[] MeleeNames = { "郵差包", "捲筒報紙", "長柄雨傘" };
        private static readonly string[] RangedNames = { "急件彈弓", "快遞短弓", "包裹十字弓" };
        private static readonly string[] ShellNames = { "殼帶護片", "鋼板殼甲" };

        /// <summary>所有道具名稱（Editor 缺字檢查用）。</summary>
        public static IEnumerable<string> AllNames()
        {
            yield return PartName;
            yield return StarterMeleeName;
            foreach (var n in MeleeNames) yield return n;
            foreach (var n in RangedNames) yield return n;
            foreach (var n in ShellNames) yield return n;
        }
        #endregion

        #region 開場與劇情發放
        /// <summary>開場的近戰：白色、沒有詞條。</summary>
        public static ItemData CreateStarterMelee() => new ItemData(ItemKind.Melee, StarterMeleeName, Rarity.Common);

        /// <summary>劇情 2 龜婆發的遠程（白色、沒有詞條）。</summary>
        public static ItemData CreateStarterRanged() => new ItemData(ItemKind.Ranged, StarterRangedName, Rarity.Common);

        /// <summary>劇情 2 龜婆發的殼甲（白色、沒有詞條）。</summary>
        public static ItemData CreateStarterShell() => new ItemData(ItemKind.Shell, StarterShellName, Rarity.Common);

        /// <summary>
        /// 龜婆發裝備（劇情 2 的演出與 GameFlow 的保底共用）：產生該欄的初始裝備，欄位空著就直接裝上，
        /// 已經有裝備就放進背包，背包也滿了就掉在主角腳邊。遠程、殼甲每局只發一次（演出中途被打斷重播不會重複發），
        /// 設「這局發過」的旗標。回傳道具名稱（字卡用）。
        /// </summary>
        public static string GiveMentorGear(GameSession session, EquipSlot slot)
        {
            var item = CreateStarter(slot);
            var flag = slot == EquipSlot.Ranged ? SsIds.FlagMentorRanged : slot == EquipSlot.Shell ? SsIds.FlagMentorShell : null;
            if (flag != null && session.Flags.Get(flag))
                return item.Name;
            var loot = session.Loot;
            if (loot.GetEquipped(slot) == null)
                loot.Equip(item);
            else if (!loot.AddAnywhere(item) && !LootDrops.DropNearHero(session, item, false))
                Debug.LogWarning($"{Log} {item.Name} 裝不上、背包已滿也找不到主角，這件沒有發出");
            if (flag != null)
                session.Flags.Set(flag);
            return item.Name;
        }

        public static ItemData CreateStarter(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Ranged: return CreateStarterRanged();
                case EquipSlot.Shell: return CreateStarterShell();
                default: return CreateStarterMelee();
            }
        }
        #endregion

        #region 裝備與詞條
        /// <summary>產生一件隨機裝備：名稱從該類別的清單平均抽，詞條數依稀有度（白 0、藍 1～2、黃 3～4）。</summary>
        public static ItemData CreateEquipment(ItemKind kind, Rarity rarity)
        {
            if (kind == ItemKind.Part)
                throw new ArgumentException("零件不是裝備", nameof(kind));
            var names = kind == ItemKind.Melee ? MeleeNames : kind == ItemKind.Ranged ? RangedNames : ShellNames;
            var item = new ItemData(kind, names[UnityEngine.Random.Range(0, names.Length)], rarity);
            RollAffixes(item);
            return item;
        }

        /// <summary>依稀有度抽詞條：同一件不重複；多重射擊、穿透只在稀有出現。</summary>
        public static void RollAffixes(ItemData item)
        {
            item.Affixes.Clear();
            int count;
            switch (item.Rarity)
            {
                case Rarity.Magic: count = UnityEngine.Random.Range(1, 3); break;
                case Rarity.Rare: count = UnityEngine.Random.Range(3, 5); break;
                default: return;
            }
            var pool = new List<Affix>();
            for (var i = 0; i <= (int)AffixId.PartFind; i++)
            {
                if (AllowedOn((AffixId)i, item.Kind, item.Rarity))
                    pool.Add(new Affix { Id = (AffixId)i });
            }
            for (var i = 0; i < count && pool.Count > 0; i++)
            {
                var pick = UnityEngine.Random.Range(0, pool.Count);
                var affix = pool[pick];
                pool.RemoveAt(pick);
                affix.Value = RollValue(affix.Id);
                item.Affixes.Add(affix);
            }
            // 顯示順序照設計文件的表
            item.Affixes.Sort((a, b) => ((int)a.Id).CompareTo((int)b.Id));
        }

        /// <summary>這條詞條能不能出現在這種裝備上（loot-design.md §2 的「可出現在」欄）。</summary>
        public static bool AllowedOn(AffixId id, ItemKind kind, Rarity rarity)
        {
            switch (id)
            {
                case AffixId.Damage:
                case AffixId.AttackSpeed:
                    return kind == ItemKind.Melee || kind == ItemKind.Ranged;
                case AffixId.AttackRange:
                case AffixId.Knockback:
                    return kind == ItemKind.Melee;
                case AffixId.Range:
                    return kind == ItemKind.Ranged;
                case AffixId.MultiShot:
                case AffixId.Pierce:
                    return kind == ItemKind.Ranged && rarity == Rarity.Rare;
                case AffixId.RollDamage:
                case AffixId.RollCooldown:
                case AffixId.MaxHp:
                case AffixId.HurtInvincible:
                    return kind == ItemKind.Shell;
                case AffixId.MoveSpeed:
                case AffixId.PartFind:
                    return kind != ItemKind.Part;
                default:
                    return false;
            }
        }

        /// <summary>詞條數值：整數範圍兩端都含；秒數取到 0.1；固定詞條為 1。</summary>
        public static float RollValue(AffixId id)
        {
            switch (id)
            {
                case AffixId.Damage: return UnityEngine.Random.Range(1, 3);
                case AffixId.AttackSpeed: return UnityEngine.Random.Range(10, 26);
                case AffixId.AttackRange: return UnityEngine.Random.Range(10, 31);
                case AffixId.Range: return UnityEngine.Random.Range(15, 41);
                case AffixId.RollDamage: return UnityEngine.Random.Range(1, 3);
                case AffixId.RollCooldown: return UnityEngine.Random.Range(10, 26);
                case AffixId.HurtInvincible: return UnityEngine.Random.Range(2, 6) / 10f;
                case AffixId.MoveSpeed: return UnityEngine.Random.Range(5, 13);
                case AffixId.PartFind: return UnityEngine.Random.Range(10, 31);
                default: return 1f;   // 擊退強化、多重射擊 +1、穿透、最大血量 +1
            }
        }

        /// <summary>稀有度：白 60、藍 30、黃 10；atLeastMagic（Boss）＝只在藍、黃之間依 30:10 抽。</summary>
        public static Rarity RollRarity(bool atLeastMagic)
        {
            if (atLeastMagic)
                return UnityEngine.Random.Range(0, 40) < 30 ? Rarity.Magic : Rarity.Rare;
            var r = UnityEngine.Random.Range(0, 100);
            return r < 60 ? Rarity.Common : r < 90 ? Rarity.Magic : Rarity.Rare;
        }

        /// <summary>裝備種類平均抽：近戰、遠程、殼甲各 1/3。</summary>
        public static ItemKind RollEquipKind()
        {
            switch (UnityEngine.Random.Range(0, 3))
            {
                case 0: return ItemKind.Melee;
                case 1: return ItemKind.Ranged;
                default: return ItemKind.Shell;
            }
        }
        #endregion

        #region 掉落表
        /// <summary>一種敵人的掉落：零件 PartsMin～PartsMax、裝備機率 EquipPercent。</summary>
        private sealed class DropRow
        {
            public string Kind;
            public int PartsMin, PartsMax, EquipPercent;
            public bool AtLeastMagic;
        }

        private static readonly DropRow[] Rows =
        {
            new DropRow { Kind = EnemySpawner.KindRat, PartsMin = 1, PartsMax = 3, EquipPercent = 15 },
            new DropRow { Kind = EnemySpawner.KindPigeon, PartsMin = 1, PartsMax = 2, EquipPercent = 10 },
            new DropRow { Kind = EnemySpawner.KindRhino, PartsMin = 4, PartsMax = 6, EquipPercent = 60 },
            new DropRow { Kind = EnemySpawner.KindBoss, PartsMin = 10, PartsMax = 10, EquipPercent = 100, AtLeastMagic = true },
        };

        /// <summary>
        /// 依敵人種類擲掉落（不檢查防刷）。零件套「零件掉落 +N%」（四捨五入，至少 1）後整堆算一件，超過 20 拆成多堆；
        /// 不認得的種類回空。
        /// </summary>
        public static void RollDrops(string enemyKind, int partFindPercent, List<ItemData> into)
        {
            DropRow row = null;
            foreach (var r in Rows)
            {
                if (r.Kind == enemyKind)
                    row = r;
            }
            if (row == null)
                return;
            var parts = UnityEngine.Random.Range(row.PartsMin, row.PartsMax + 1);
            if (partFindPercent > 0)
                parts = Mathf.Max(1, Mathf.RoundToInt(parts * (1f + partFindPercent / 100f)));
            while (parts > 0)
            {
                var n = Mathf.Min(parts, ItemData.PartStack);
                into.Add(new ItemData(ItemKind.Part, PartName, Rarity.Common, n));
                parts -= n;
            }
            if (UnityEngine.Random.Range(0, 100) < row.EquipPercent)
                into.Add(CreateEquipment(RollEquipKind(), RollRarity(row.AtLeastMagic)));
        }

        /// <summary>
        /// 敵人（含 Boss）血量歸零時呼叫：這個關卡 id 整局第一次被打倒才掉（死亡重生後再打不掉），
        /// 依掉落表在 position（身體中央）對每件呼叫 LootDrops.Spawn。不丟例外。
        /// </summary>
        public static void OnEnemyDefeated(EnemyContext ctx, string spawnId, string enemyKind, Vector2 position)
        {
            try
            {
                var session = ctx != null ? ctx.Session : null;
                if (session == null || ctx.Parent == null)
                    return;
                if (!session.Loot.MarkDropped(spawnId))
                    return;
                var gear = new GearStats();
                gear.Compute(session.Loot, session.Loadout.Costume);
                var drops = new List<ItemData>();
                RollDrops(enemyKind, gear.PartFindPercent, drops);
                foreach (var item in drops)
                    LootDrops.Spawn(ctx.Parent, session, item, position, false);
                if (drops.Count > 0)
                    Debug.Log($"{Log} {spawnId}（{enemyKind}）掉落 {Describe(drops)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{Log} {spawnId} 掉落失敗：{ex}");
            }
        }

        private static string Describe(List<ItemData> items)
        {
            var parts = new string[items.Count];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = items[i].IsEquipment ? $"{items[i].Name}（{items[i].Rarity}，{items[i].Affixes.Count} 條詞條）" : items[i].DisplayName;
            return string.Join("、", parts);
        }
        #endregion
    }
}
