using UnityEngine;

namespace Sidescroller
{
    /// <summary>一次性特效（SsFx_Hit／Poof／Dust／Clank 等）：播一輪就自己刪掉。</summary>
    public sealed class FxPlayer : MonoBehaviour
    {
        private SpriteAnimator _anim;

        /// <summary>在 parent 底下 position 播 location（例如 "SsFx_Poof"）一輪；parent 被刪時跟著消失。</summary>
        public static FxPlayer Spawn(Transform parent, string location, Vector2 position, int facing = 1)
        {
            var go = new GameObject("Fx " + location);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var fx = go.AddComponent<FxPlayer>();
            fx._anim = SpriteAnimator.Create(go, SsSorting.Fx);
            fx._anim.SetFacing(facing);
            fx._anim.Play(SsArt.Get(location), false, true);
            return fx;
        }

        private void LateUpdate()
        {
            if (_anim == null || _anim.IsDone)
                Destroy(gameObject);
        }
    }
}
