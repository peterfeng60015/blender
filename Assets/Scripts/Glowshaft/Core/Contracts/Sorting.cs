using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 繪製順序約定。全部用 Default sorting layer，只靠 sortingOrder 區分：
    /// 地板在最底，站在地上的東西（玩家、史萊姆、岩石、水晶、補血）依 y 排序（y 越小越前面）。
    /// </summary>
    public static class Sorting
    {
        public const int Floor = -32000;
        /// <summary>地板上的平貼裝飾（若有），在地板之上、所有站立物之下。</summary>
        public const int FloorDecal = -31000;
        /// <summary>子彈等飛行物，永遠畫在站立物之上。</summary>
        public const int Projectile = 31000;

        /// <summary>站立物依世界座標 y 算出的 sortingOrder（腳底位置）。</summary>
        public static int ByY(float y)
        {
            return Mathf.Clamp(Mathf.RoundToInt(-y * 100f), -30000, 30000);
        }
    }
}
