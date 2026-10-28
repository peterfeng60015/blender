using UnityEngine;

namespace Sidescroller
{
    public enum DamageSource { Melee = 0, Sling = 1, Roll = 2, Enemy = 3 }

    public sealed class HitInfo
    {
        public int Amount;
        public DamageSource Source;
        /// <summary>攻擊方向：+1 從左打向右、-1 從右打向左（判斷正面／背後與擊退方向）。</summary>
        public int Direction;
        /// <summary>滾殼＝true。</summary>
        public bool BreaksShield;
        public GameObject Attacker;
        /// <summary>擊退倍率（打主角用）：0＝預設；例如犀牛突進給 1.6。</summary>
        public float Knockback;
    }

    public interface IDamageable
    {
        /// <summary>有效命中回 true；被盾擋、無敵中回 false。</summary>
        bool TakeHit(HitInfo hit);
    }
}
