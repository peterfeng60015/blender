using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 依 SpriteSet 播放幀動畫並依方向挑列；LateUpdate 時依擁有者（本元件所在物件）的 y 設定 sortingOrder。
    /// SpriteRenderer 放在子物件 "Visual" 上，方便做離地位移與閃爍而不影響碰撞。
    /// </summary>
    public sealed class SpriteAnimator : MonoBehaviour
    {
        public SpriteRenderer Renderer { get; private set; }
        public int Direction { get; set; }
        /// <summary>true＝依 y 排序；false＝固定 <see cref="FixedOrder"/>。</summary>
        public bool SortByY = true;
        public int FixedOrder;
        /// <summary>播放速度倍率（例如依移動速度調整）。</summary>
        public float SpeedScale = 1f;

        private SpriteSet _set;
        private float _time;

        public static SpriteAnimator Attach(GameObject owner, SpriteSet set, float visualHeight = 0f)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(owner.transform, false);
            visual.transform.localPosition = new Vector3(0f, visualHeight, 0f);
            var anim = owner.AddComponent<SpriteAnimator>();
            anim.Renderer = visual.AddComponent<SpriteRenderer>();
            anim.Play(set, true);
            return anim;
        }

        public void Play(SpriteSet set, bool restart = false)
        {
            if (set == _set && !restart)
                return;
            _set = set;
            _time = 0f;
            Apply();
        }

        private void LateUpdate()
        {
            _time += Time.deltaTime * SpeedScale;
            Apply();
        }

        private void Apply()
        {
            if (_set == null || Renderer == null)
                return;
            var frame = Mathf.FloorToInt(_time * _set.Fps);
            Renderer.sprite = _set.Get(Direction, frame);
            Renderer.sortingOrder = SortByY ? Sorting.ByY(transform.position.y) : FixedOrder;
        }
    }
}
