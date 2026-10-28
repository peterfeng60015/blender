using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 共用的 sprite 動畫播放（主角、敵人、Boss、NPC、子彈、特效都用）。
    /// 掛在角色根物件上，自己在底下建子物件 <c>Visual</c> 放 SpriteRenderer；左右翻轉改 Visual 的 localScale.x
    /// （design.md §8.1：不用 flipX，法線光照才正確）。時間用 scaled time，暫停時動畫也停。
    /// </summary>
    public sealed class SpriteAnimator : MonoBehaviour
    {
        private SpriteSet _set;
        private bool _loop;
        private float _time;
        private float _flashLeft;
        private Color _flashColor = Color.white;
        private Color _baseColor = Color.white;

        public Transform Visual { get; private set; }
        public SpriteRenderer Renderer { get; private set; }
        public SpriteSet Current => _set;
        public bool Loop => _loop;
        /// <summary>從頭播放到現在的秒數。</summary>
        public float Time => _time;
        /// <summary>不循環的動作已播完（停在最後一格）。</summary>
        public bool IsDone => _set != null && !_loop && _time >= _set.Duration;
        /// <summary>+1 右、-1 左。</summary>
        public int Facing { get; private set; } = 1;

        /// <summary>在 root 上掛動畫元件並建 Visual 子物件；sortingOrder 用 <see cref="SsSorting"/>。</summary>
        public static SpriteAnimator Create(GameObject root, int sortingOrder)
        {
            var anim = root.AddComponent<SpriteAnimator>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            anim.Visual = visual.transform;
            anim.Renderer = visual.AddComponent<SpriteRenderer>();
            anim.Renderer.sortingOrder = sortingOrder;
            return anim;
        }

        /// <summary>播放；同一個 set 而且 restart=false 時不重來（循環動作重複呼叫不會卡在第一格）。</summary>
        public void Play(SpriteSet set, bool loop, bool restart = false)
        {
            if (set == null)
                return;
            if (!restart && ReferenceEquals(set, _set) && loop == _loop)
                return;
            _set = set;
            _loop = loop;
            _time = 0f;
            Apply();
        }

        /// <summary>換成另一套 sheet 但保留播放時間（換裝：兩套主角的幀數一致，動畫不中斷）。</summary>
        public void Swap(SpriteSet set)
        {
            if (set == null)
                return;
            _set = set;
            Apply();
        }

        public void SetFacing(int dir)
        {
            if (dir == 0)
                return;
            Facing = dir > 0 ? 1 : -1;
            if (Visual != null)
            {
                var s = Visual.localScale;
                s.x = Mathf.Abs(s.x) * Facing;
                Visual.localScale = s;
            }
        }

        /// <summary>平常的顏色（例如無敵閃爍時改 alpha）。</summary>
        public void SetBaseColor(Color color)
        {
            _baseColor = color;
            ApplyColor();
        }

        /// <summary>受擊閃色 time 秒（顏色是乘上去的，白色沒效果，用淡紅、淡黃等）。</summary>
        public void Flash(Color color, float time)
        {
            _flashColor = color;
            _flashLeft = time;
            ApplyColor();
        }

        private void Update()
        {
            if (_set == null)
                return;
            var dt = UnityEngine.Time.deltaTime;
            _time += dt;
            if (_flashLeft > 0f)
            {
                _flashLeft -= dt;
                ApplyColor();
            }
            Apply();
        }

        private void Apply()
        {
            if (Renderer == null || _set == null)
                return;
            var frame = Mathf.FloorToInt(_time * _set.Fps);
            Renderer.sprite = _set.Get(frame, _loop);
        }

        private void ApplyColor()
        {
            if (Renderer == null)
                return;
            Renderer.color = _flashLeft > 0f ? _flashColor * new Color(1f, 1f, 1f, _baseColor.a) : _baseColor;
        }
    }
}
