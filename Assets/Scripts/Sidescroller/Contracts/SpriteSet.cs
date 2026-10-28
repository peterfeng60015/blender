using UnityEngine;

namespace Sidescroller
{
    /// <summary>一張 sheet 切好的全部幀（側視、單方向）。</summary>
    public sealed class SpriteSet
    {
        /// <summary>例如 "SsHeroDay_Run"。</summary>
        public readonly string Location;
        /// <summary>依編號排序。</summary>
        public readonly Sprite[] Frames;
        /// <summary>json 的 fps；讀不到時 12。</summary>
        public readonly float Fps;
        /// <summary>true＝美術還沒到，這是程式產生的佔位圖。</summary>
        public readonly bool IsPlaceholder;

        private bool _warnedVariant;

        public SpriteSet(string location, Sprite[] frames, float fps, bool isPlaceholder)
        {
            Location = location;
            Frames = frames ?? new Sprite[0];
            Fps = fps > 0f ? fps : 12f;
            IsPlaceholder = isPlaceholder;
        }

        public int FrameCount => Frames.Length;

        /// <summary>FrameCount / Fps。</summary>
        public float Duration => FrameCount / Fps;

        /// <summary>loop=false 時超出範圍停在最後一格。</summary>
        public Sprite Get(int frame, bool loop)
        {
            if (Frames.Length == 0)
                return null;
            if (loop)
            {
                frame %= Frames.Length;
                if (frame < 0)
                    frame += Frames.Length;
                return Frames[frame];
            }
            return Frames[Mathf.Clamp(frame, 0, Frames.Length - 1)];
        }

        /// <summary>單張多變化素材（tile、props、立繪表情）；超出範圍取 0 並記一次警告。</summary>
        public Sprite Variant(int variant)
        {
            if (Frames.Length == 0)
                return null;
            if (variant < 0 || variant >= Frames.Length)
            {
                if (!_warnedVariant)
                {
                    _warnedVariant = true;
                    Debug.LogWarning($"[Sidescroller.SpriteSet] {Location} 沒有第 {variant} 個變化（共 {Frames.Length} 個），改用 0");
                }
                variant = 0;
            }
            return Frames[variant];
        }
    }
}
