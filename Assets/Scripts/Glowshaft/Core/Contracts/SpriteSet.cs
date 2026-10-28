using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 一張 sprite sheet（一個 Name 的一個 Action，例如 <c>Miner_Run</c>）切好的全部幀。
    /// 編號規則照共同規則：第 r 列（方向，0＝S，順序 S, SE, E, NE, N, NW, W, SW）第 c 幀 ＝ r × FramesPerDirection + c。
    /// 單方向素材（地板、岩石、水晶等）Directions＝1，「幀」常用來放不同變化（例如三種岩石）。
    /// </summary>
    public sealed class SpriteSet
    {
        /// <summary>定位地址，例如 <c>Rock_Idle</c>。</summary>
        public readonly string Location;
        public readonly Sprite[] Frames;
        public readonly int Directions;
        public readonly int FramesPerDirection;
        public readonly float Fps;
        /// <summary>true＝美術素材還沒到，這是程式產生的純色佔位圖。</summary>
        public readonly bool IsPlaceholder;

        public SpriteSet(string location, Sprite[] frames, int directions, float fps, bool isPlaceholder)
        {
            Location = location;
            Frames = frames;
            Directions = Mathf.Max(1, directions);
            FramesPerDirection = Mathf.Max(1, frames.Length / Directions);
            Fps = fps > 0f ? fps : 10f;
            IsPlaceholder = isPlaceholder;
        }

        /// <summary>取第 direction 方向的第 frame 幀；frame 超出範圍會循環，direction 超出範圍取 0。</summary>
        public Sprite Get(int direction, int frame)
        {
            if (direction < 0 || direction >= Directions)
                direction = 0;
            frame %= FramesPerDirection;
            if (frame < 0)
                frame += FramesPerDirection;
            var index = direction * FramesPerDirection + frame;
            return index < Frames.Length ? Frames[index] : Frames[Frames.Length - 1];
        }

        /// <summary>單方向素材的第 variant 個變化（等同 Get(0, variant)）。</summary>
        public Sprite Variant(int variant)
        {
            return Get(0, variant);
        }

        /// <summary>
        /// 把世界座標的方向向量換成 8 方向索引（0＝S，逆時針看是 S→SE→E→NE→N→NW→W→SW）。
        /// directions 不是 8 時一樣從 S 起算、每格 360/directions 度。
        /// </summary>
        public static int DirectionIndex(Vector2 v, int directions)
        {
            if (directions <= 1 || v.sqrMagnitude < 1e-6f)
                return 0;
            var angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; // E＝0、N＝90、S＝-90
            var step = 360f / directions;
            var index = Mathf.RoundToInt((angle + 90f) / step) % directions;
            return index < 0 ? index + directions : index;
        }
    }
}
