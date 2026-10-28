using UnityEngine;

namespace Sidescroller
{
    /// <summary>player 掛在每個角色的根物件；觸發區用 GetComponentInParent 判斷是不是主角。</summary>
    public sealed class ActorTag : MonoBehaviour
    {
        public ActorKind Kind;
        public string Id;
    }
}
