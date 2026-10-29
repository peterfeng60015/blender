using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 掛在 <see cref="LootHudView"/> 的根物件上：StoryUi 被刪（回選單、再玩一次）時通知它把飛行中的預留全部 Commit、取消訂閱。
    /// LootHudView 不是 MonoBehaviour，靠這個拿到 OnDestroy。
    /// </summary>
    internal sealed class LootFlyHook : MonoBehaviour
    {
        internal LootHudView View;

        private void OnDestroy()
        {
            View?.Dispose();
            View = null;
        }
    }
}
