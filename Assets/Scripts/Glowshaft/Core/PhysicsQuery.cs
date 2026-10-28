using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 不配置記憶體的 CircleCast（取代每幀 new 陣列的 <c>Physics2D.CircleCastAll</c>）。
    /// 結果放在共用緩衝 <see cref="Hits"/>，依距離由近到遠排序，只在下一次呼叫前有效；
    /// 呼叫端處理結果時不可再呼叫本類別（目前 Bullet、Enemy 都是當場處理完）。
    /// </summary>
    public static class PhysicsQuery
    {
        public static readonly RaycastHit2D[] Hits = new RaycastHit2D[16];

        private static ContactFilter2D _filter = CreateFilter();

        private static ContactFilter2D CreateFilter()
        {
            var filter = new ContactFilter2D();
            filter.NoFilter();
            filter.useTriggers = false;
            return filter;
        }

        /// <summary>回傳命中數量；超過緩衝大小的命中會被捨棄（16 個對子彈與繞障都足夠）。</summary>
        public static int CircleCast(Vector2 origin, float radius, Vector2 direction, float distance)
        {
            var count = Physics2D.CircleCast(origin, radius, direction, _filter, Hits, distance);
            // 文件沒保證排序，自己依距離插入排序（數量很少）
            for (var i = 1; i < count; i++)
            {
                var hit = Hits[i];
                var j = i - 1;
                while (j >= 0 && Hits[j].distance > hit.distance)
                {
                    Hits[j + 1] = Hits[j];
                    j--;
                }
                Hits[j + 1] = hit;
            }
            return count;
        }
    }
}
