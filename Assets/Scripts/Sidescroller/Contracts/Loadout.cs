using System;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 已取得的攻擊與服裝。內部用 int 位元旗標存（避開值型別集合的 AOT 泛型）。
    /// 誰改：story（演出的 grant）、player（換裝、保底發放、Reset）。重生不 Reset。
    /// </summary>
    public sealed class Loadout
    {
        private int _attacks;
        private int _costumes;

        public Loadout()
        {
            _attacks = Bit((int)AttackId.Melee);
            _costumes = Bit((int)CostumeId.Day);
            Costume = CostumeId.Day;
        }

        /// <summary>目前穿的服裝。</summary>
        public CostumeId Costume { get; private set; }

        /// <summary>已取得幾套（&gt;1 才能按 C 換）。</summary>
        public int CostumeCount
        {
            get
            {
                var n = 0;
                for (var bits = _costumes; bits != 0; bits &= bits - 1)
                    n++;
                return n;
            }
        }

        /// <summary>取得新東西或換裝時觸發（狀態沒變不觸發）。</summary>
        public event Action Changed;

        public bool HasAttack(AttackId id) => (_attacks & Bit((int)id)) != 0;
        public bool HasCostume(CostumeId id) => (_costumes & Bit((int)id)) != 0;

        public void GrantAttack(AttackId id)
        {
            if (HasAttack(id))
                return;
            _attacks |= Bit((int)id);
            Changed?.Invoke();
        }

        public void GrantCostume(CostumeId id, bool equip)
        {
            var changed = false;
            if (!HasCostume(id))
            {
                _costumes |= Bit((int)id);
                changed = true;
            }
            if (equip && Costume != id)
            {
                Costume = id;
                changed = true;
            }
            if (changed)
                Changed?.Invoke();
        }

        /// <summary>未取得：回 false 並記警告，不改狀態。</summary>
        public bool SetCostume(CostumeId id)
        {
            if (!HasCostume(id))
            {
                Debug.LogWarning($"[Sidescroller.Loadout] 還沒取得服裝 {id}，不能換上");
                return false;
            }
            if (Costume == id)
                return true;
            Costume = id;
            Changed?.Invoke();
            return true;
        }

        /// <summary>回到開場：只有 Melee＋Day，觸發 Changed。</summary>
        public void Reset()
        {
            _attacks = Bit((int)AttackId.Melee);
            _costumes = Bit((int)CostumeId.Day);
            Costume = CostumeId.Day;
            Changed?.Invoke();
        }

        public static string DisplayName(AttackId id)
        {
            switch (id)
            {
                case AttackId.Melee: return "甩包攻擊";
                case AttackId.Sling: return "急件彈弓";
                case AttackId.Roll: return "滾殼衝撞";
                default: return id.ToString();
            }
        }

        public static string DisplayName(CostumeId id)
        {
            switch (id)
            {
                case CostumeId.Day: return "日班制服";
                case CostumeId.Express: return "特急制服";
                default: return id.ToString();
            }
        }

        private static int Bit(int index) => 1 << index;
    }
}
