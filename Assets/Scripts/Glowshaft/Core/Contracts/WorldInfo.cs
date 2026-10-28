using UnityEngine;

namespace Glowshaft
{
    /// <summary><see cref="WorldBuilder.Build"/> 的回傳值。</summary>
    public sealed class WorldInfo
    {
        /// <summary>
        /// 可走動的矩形範圍（世界座標，邊界牆在這個矩形外側）。Core 用它限制相機、挑史萊姆生成點。
        /// </summary>
        public Rect PlayArea;

        /// <summary>玩家出生點；World 保證以它為圓心半徑 <see cref="WorldBuilder.SpawnClearRadius"/> 內沒有障礙物。</summary>
        public Vector2 PlayerSpawn;
    }
}
