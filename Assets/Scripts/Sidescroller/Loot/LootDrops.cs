using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 地上的道具（pickup 實作）：往上噴、落地彈一下、稀有度光柱、30 秒消失（最後 5 秒閃）、落地後自動拾取並飛進右上背包圖示；
    /// 背包放不下就留在地上，主角走過去再判斷一次（loot-design.md §3、§4）。物件掛在 parent（worldRoot）底下，重生時跟著被刪。
    /// 每件道具的行為見 <see cref="LootDrop"/>，飛行與回饋見 <see cref="LootHudView"/>。
    /// </summary>
    public static class LootDrops
    {
        private const string Log = "[Sidescroller.LootDrops]";
        /// <summary>玩家丟的道具從主角身體中央附近噴出。</summary>
        private const float HeroDropHeight = 0.8f;

        /// <summary>
        /// 在 position（世界座標，通常是敵人身體中央）噴出一件道具。data 在敵人死亡時對每件掉落各呼叫一次（零件整堆算一件）。
        /// droppedByPlayer＝true：玩家從背包丟的，主角離開它的範圍之前不會自動撿回去。
        /// </summary>
        public static void Spawn(Transform parent, GameSession session, ItemData item, Vector2 position, bool droppedByPlayer = false)
        {
            if (parent == null || session == null || item == null)
            {
                Debug.LogWarning($"{Log} Spawn 參數不完整（parent／session／item 有 null），忽略");
                return;
            }
            if (item.Count <= 0)
            {
                Debug.LogWarning($"{Log} {item.Name} 數量是 0，忽略");
                return;
            }
            LootDrop.Create(parent, session, item, position, droppedByPlayer);
        }

        /// <summary>
        /// 在主角腳邊噴出。背包介面「丟到地上」用預設的 droppedByPlayer＝true（主角離開前不撿）；
        /// 劇情發放但背包已滿時傳 false（落地後照常自動拾取）。找不到主角回 false（呼叫端把道具收回背包）。
        /// </summary>
        public static bool DropNearHero(GameSession session, ItemData item, bool droppedByPlayer = true)
        {
            var hero = SidescrollerGame.Flow != null ? SidescrollerGame.Flow.Hero : null;
            if (hero == null || hero.transform.parent == null || session == null || item == null || item.Count <= 0)
                return false;
            var feet = (Vector2)hero.transform.position;
            LootDrop.Create(hero.transform.parent, session, item, feet + new Vector2(0f, HeroDropHeight), droppedByPlayer);
            return true;
        }
    }
}
