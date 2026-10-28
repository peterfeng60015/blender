using UnityEngine;

namespace Sidescroller
{
    /// <summary>level 掛在每個地形 collider 所在的物件；子彈只被 Solid 擋。</summary>
    public sealed class TerrainTag : MonoBehaviour
    {
        public TerrainKind Kind;
    }
}
