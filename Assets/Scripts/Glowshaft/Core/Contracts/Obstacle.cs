using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 障礙物標記（岩石、邊界牆）。World 建立的每個會擋路的物件都要掛這個元件，並在同一個 GameObject
    /// 上放靜態 <see cref="Collider2D"/>（不加 Rigidbody2D、不勾 isTrigger）。
    /// Core 的子彈打到掛了 Obstacle 的 collider 會消失；玩家與史萊姆靠物理碰撞被擋住。
    /// 不使用自訂 layer，全部在 Default layer。
    /// </summary>
    public sealed class Obstacle : MonoBehaviour
    {
    }
}
