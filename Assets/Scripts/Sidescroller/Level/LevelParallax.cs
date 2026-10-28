using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 視差背景（interfaces.md §4.2、design.md §8.4）：遠景 SsBgFar（視差 0.1）、中景 SsBgMid（視差 0.4），水平無限平鋪。
    /// 「視差 s」＝在畫面上移動的速度是前景的 s 倍。每次 <see cref="CameraRig"/> 移動完就重算位置（不自己跑 LateUpdate，
    /// 免得和鏡頭的 LateUpdate 順序不定而抖動）；建在關卡底下，重建關卡時跟著被刪掉。
    /// </summary>
    public sealed class LevelParallax : MonoBehaviour
    {
        #region 調校
        private const float FarFactor = 0.1f;
        private const float MidFactor = 0.4f;
        /// <summary>中景底邊在鏡頭最低時的世界 y（藏在地面 tile 後面）。</summary>
        private const float MidBaseY = 1f;
        /// <summary>遠景底邊比畫面底邊再低多少，避免露出相機背景色。</summary>
        private const float FarMargin = 0.5f;
        #endregion

        private sealed class Layer
        {
            public string Name;
            public SpriteSet Set;
            public float Factor;
            public int Order;
            public Transform Root;
            public readonly List<SpriteRenderer> Copies = new List<SpriteRenderer>();
        }

        private CameraRig _rig;
        private Rect _bounds;
        private float _midSwitchX;
        private Layer _far;
        private Layer _mid;

        internal static LevelParallax Create(Transform parent, CameraRig rig, Rect cameraBounds, float midSwitchX)
        {
            var go = new GameObject("Parallax");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<LevelParallax>();
            p._rig = rig;
            p._bounds = cameraBounds;
            p._midSwitchX = midSwitchX;
            p._far = p.NewLayer("BgFar", SsArt.Get("SsBgFar_Idle"), FarFactor, SsSorting.BgFar);
            p._mid = p.NewLayer("BgMid", SsArt.Get("SsBgMid_Idle"), MidFactor, SsSorting.BgMid);
            rig.Moved += p.Refresh;
            p.Refresh();
            return p;
        }

        private Layer NewLayer(string name, SpriteSet set, float factor, int order)
        {
            return new Layer
            {
                Name = name,
                Set = set,
                Factor = factor,
                Order = order,
                Root = LevelBuilder.NewChild(transform, name)
            };
        }

        private void OnDestroy()
        {
            if (_rig != null)
                _rig.Moved -= Refresh;
        }

        private void Refresh()
        {
            if (this == null || !isActiveAndEnabled || _rig == null || _rig.Camera == null)
                return;
            var cam = _rig.Camera;
            var camPos = (Vector2)cam.transform.position;
            var halfH = cam.orthographicSize;
            var halfW = halfH * cam.aspect;
            // 鏡頭在關卡最低處時的中心 y：以這裡為基準算垂直視差
            var lowY = _bounds.yMin + halfH;
            var rise = camPos.y - lowY;

            // 遠景：底邊貼著畫面底邊，鏡頭往上時只跟著上去 (1 - s)
            var farBottom = camPos.y - halfH - FarMargin - rise * FarFactor;
            Layout(_far, camPos.x, halfW, farBottom, false);
            // 中景：底邊從地面附近開始，鏡頭往上時在畫面上往下移 s 倍
            var midBottom = MidBaseY + rise * (1f - MidFactor);
            Layout(_mid, camPos.x, halfW, midBottom, true);
        }

        /// <summary>
        /// 圖層在「圖層空間」已捲動 u＝camX × s；第 k 張的左緣在圖層空間 k×W，世界 x＝camX + k×W − u。
        /// 只擺畫面看得到的那幾張，數量依鏡頭寬度自動增加。
        /// </summary>
        private void Layout(Layer layer, float camX, float halfW, float bottomY, bool useSwitch)
        {
            var sample = layer.Set.Variant(0);
            if (sample == null)
                return;
            var w = sample.bounds.size.x;
            if (w <= 0.01f)
                return;
            var u = camX * layer.Factor;
            var first = Mathf.FloorToInt((u - halfW) / w);
            var last = Mathf.FloorToInt((u + halfW) / w);
            var count = last - first + 1;
            while (layer.Copies.Count < count)
            {
                var sr = LevelBuilder.PlaceSprite(layer.Root, $"{layer.Name}_{layer.Copies.Count}", null, Vector2.zero, false, layer.Order);
                layer.Copies.Add(sr);
            }
            for (var i = 0; i < layer.Copies.Count; i++)
            {
                var sr = layer.Copies[i];
                if (i >= count)
                {
                    if (sr.enabled)
                        sr.enabled = false;
                    continue;
                }
                var k = first + i;
                var variant = 0;
                // 中景：第 k 張的左緣（接縫）在畫面中央時鏡頭的 x＝k×W / s；這個 x ≥ 切換點的張改用變化 1（工地）。切換一律發生在接縫上，不會跳圖
                if (useSwitch && k * w / layer.Factor >= _midSwitchX)
                    variant = 1;
                var sprite = layer.Set.Variant(variant);
                if (sprite == null)
                    sprite = sample;
                if (sr.sprite != sprite)
                    sr.sprite = sprite;
                if (!sr.enabled)
                    sr.enabled = true;
                var b = sprite.bounds;
                var left = camX + k * w - u;
                sr.transform.localPosition = new Vector3(left - b.min.x, bottomY - b.min.y, 0f);
            }
        }
    }
}
