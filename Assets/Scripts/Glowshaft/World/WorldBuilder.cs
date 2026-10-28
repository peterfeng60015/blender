using System.Collections.Generic;
using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 地圖建立入口（world 負責）。公開簽名不要改（改之前找 gameplay）。
    ///
    /// 地圖組成（全部掛在 parent 底下）：
    /// <list type="bullet">
    /// <item>Floor：<c>Floor_Idle</c> 依 sprite 實際世界尺寸平鋪，蓋滿 PlayArea 外加一圈邊框。</item>
    /// <item>Rocks：場內的岩石群（1～3 顆一群），每顆一個靜態 CapsuleCollider2D＋Obstacle；群內岩石互相重疊或相隔一條通道，群與群、群與牆之間保留可通行的間隙。</item>
    /// <item>Walls：PlayArea 外側四面 BoxCollider2D＋Obstacle；視覺上沿邊界排一圈岩石（純裝飾，不另加 collider）。</item>
    /// <item>Crystals：發光水晶裝飾（無 collider），一半貼著岩石群、一半貼牆，各帶一盞會緩慢呼吸的點光。</item>
    /// <item>GlobalLight：唯一一盞 Global Light 2D。</item>
    /// </list>
    /// 隨機一律用 <see cref="System.Random"/>(seed)，不動 UnityEngine.Random 的共用狀態，同 seed 地圖相同。
    /// </summary>
    public static class WorldBuilder
    {
        /// <summary>玩家出生點周圍保證沒有障礙物的半徑（世界單位）。</summary>
        public const float SpawnClearRadius = 4f;

        private static readonly Rect PlayArea = new Rect(-18f, -12f, 36f, 24f);
        private const float WallThickness = 1f;
        /// <summary>地板在 PlayArea 外多鋪的寬度，讓邊界岩石底下也有地板。</summary>
        private const float FloorMargin = 2f;
        /// <summary>岩石群之間、岩石群與牆之間至少留的通道寬度（玩家直徑 0.7、大史萊姆約 1.5）。</summary>
        private const float PassageWidth = 1.8f;
        private const int RockGroupCount = 14;
        private const int CrystalCount = 14;

        /// <summary>水晶光色，依變化編號循環（對應正式素材 Crystal_Idle 的青、洋紅、琥珀、綠四種變化）。</summary>
        private static readonly Color[] CrystalColors =
        {
            new Color(0.35f, 0.85f, 1f),
            new Color(0.95f, 0.4f, 1f),
            new Color(1f, 0.75f, 0.3f),
            new Color(0.4f, 1f, 0.55f),
        };

        /// <summary>一個岩石群（世界座標）：Radius＝碰撞的外接圓，用來保持通道；VisualRadius＝外觀的外接圓，用來擺水晶。</summary>
        private struct RockGroup
        {
            public Vector2 Center;
            public float Radius;
            public float VisualRadius;
        }

        /// <summary>
        /// 一顆岩石的形狀（sprite 本地座標，腳底 pivot 為原點）。用 struct 不用 ValueTuple，避免 HybridCLR 的 AOT 泛型實例化。
        /// </summary>
        private struct RockShape
        {
            /// <summary>膠囊碰撞的中心與大小。</summary>
            public Vector2 ColliderCenter;
            public Vector2 ColliderSize;
            /// <summary>不透明區的寬度。</summary>
            public float Width;
            /// <summary>不透明區離 pivot 最遠的水平距離。</summary>
            public float VisualHalfWidth;

            public float InnerRadius => Mathf.Min(ColliderSize.x, ColliderSize.y) * 0.5f;
            public float OuterRadius => Mathf.Max(ColliderSize.x, ColliderSize.y) * 0.5f;
            public float BoundRadius => ColliderCenter.magnitude + OuterRadius;
        }

        /// <summary>岩石群裡的一顆：腳底相對群原點的位移、sprite、縮放。</summary>
        private struct RockMember
        {
            public Vector2 Offset;
            public Sprite Sprite;
            public float Scale;
            public RockShape Shape;

            /// <summary>碰撞膠囊中心（相對群原點）。</summary>
            public Vector2 ColliderCenter => Offset + Shape.ColliderCenter * Scale;
            /// <summary>膠囊的內切圓半徑（兩顆的內切圓重疊＝膠囊一定重疊）。</summary>
            public float InnerRadius => Shape.InnerRadius * Scale;
            /// <summary>膠囊的外接圓半徑（以膠囊中心為圓心）。</summary>
            public float OuterRadius => Shape.OuterRadius * Scale;
        }

        /// <summary>
        /// 在 parent 底下建立整張地圖（地板、岩石、水晶、邊界、Global Light 2D）。
        /// 每次開局（含重來）Core 都會先 Destroy 舊的 parent 再建新的，所以這裡建的東西都要掛在 parent 底下，
        /// 不可以用 DontDestroyOnLoad 或掛到別處。seed 相同時地圖要相同。
        /// </summary>
        public static WorldInfo Build(Transform parent, int seed)
        {
            var info = new WorldInfo
            {
                PlayArea = PlayArea,
                PlayerSpawn = Vector2.zero,
            };
            var rng = new System.Random(seed);

            GlowLights.CreateGlobalLight(parent);
            BuildFloor(NewGroup(parent, "Floor"), rng);
            BuildWalls(NewGroup(parent, "Walls"), rng);
            var groups = BuildRocks(NewGroup(parent, "Rocks"), rng, info.PlayerSpawn);
            BuildCrystals(NewGroup(parent, "Crystals"), rng, groups, info.PlayerSpawn);
            return info;
        }

        #region 地板

        private static void BuildFloor(Transform root, System.Random rng)
        {
            var set = ArtLibrary.Get("Floor_Idle");
            var size = set.Frames[0].bounds.size;
            var w = Mathf.Max(0.25f, size.x);
            var h = Mathf.Max(0.25f, size.y);
            var area = Expand(PlayArea, FloorMargin);
            // 從整數倍的格點開始鋪，避免浮點累加造成接縫
            var cols = Mathf.CeilToInt(area.width / w);
            var rows = Mathf.CeilToInt(area.height / h);
            for (var j = 0; j < rows; j++)
            for (var i = 0; i < cols; i++)
            {
                var sprite = set.Variant(rng.Next(set.FramesPerDirection));
                var center = new Vector2(area.xMin + (i + 0.5f) * w, area.yMin + (j + 0.5f) * h);
                // sprite 的 pivot 不一定在中心：用 bounds.center 修正到格子中心
                var pos = center - (Vector2)sprite.bounds.center;
                var sr = NewSprite(root, "Tile", pos, sprite);
                sr.sortingOrder = Sorting.Floor;
            }
        }
        #endregion

        #region 邊界

        private static void BuildWalls(Transform root, System.Random rng)
        {
            var a = PlayArea;
            var t = WallThickness;
            CreateWall(root, "WallBottom", new Vector2(a.center.x, a.yMin - t * 0.5f), new Vector2(a.width + 2f * t, t));
            CreateWall(root, "WallTop", new Vector2(a.center.x, a.yMax + t * 0.5f), new Vector2(a.width + 2f * t, t));
            CreateWall(root, "WallLeft", new Vector2(a.xMin - t * 0.5f, a.center.y), new Vector2(t, a.height + 2f * t));
            CreateWall(root, "WallRight", new Vector2(a.xMax + t * 0.5f, a.center.y), new Vector2(t, a.height + 2f * t));

            // 視覺：沿四邊排一圈岩石，純裝飾，碰撞由上面的 BoxCollider2D 負責；上下兩排腳底落在牆的中線
            var rocks = ArtLibrary.Get("Rock_Idle");
            var step = Mathf.Max(0.5f, Footprint(rocks.Frames[0]).Width * 0.6f);
            var xMin = a.xMin - t * 0.5f;
            var xMax = a.xMax + t * 0.5f;
            var yMin = a.yMin - t * 0.5f;
            var yMax = a.yMax + t * 0.5f;
            var nx = Mathf.CeilToInt((xMax - xMin) / step);
            var ny = Mathf.CeilToInt((yMax - yMin) / step);
            for (var i = 0; i <= nx; i++)
            {
                var x = Mathf.Lerp(xMin, xMax, (float)i / nx);
                BorderRock(root, rocks, rng, new Vector2(x, yMin));
                BorderRock(root, rocks, rng, new Vector2(x, yMax));
            }
            // 左右兩排往外推，岩石外觀才不會伸進 PlayArea、和貼牆的玩家重疊（上下兩排是俯視角的前後遮擋，保留在牆中線）
            var side = Mathf.Max(t * 0.5f, Footprint(rocks.Frames[0]).VisualHalfWidth * 1.2f);
            for (var j = 1; j < ny; j++)
            {
                var y = Mathf.Lerp(yMin, yMax, (float)j / ny);
                BorderRock(root, rocks, rng, new Vector2(a.xMin - side, y));
                BorderRock(root, rocks, rng, new Vector2(a.xMax + side, y));
            }
        }

        private static void CreateWall(Transform root, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size;
            go.AddComponent<Obstacle>();
        }

        private static void BorderRock(Transform root, SpriteSet rocks, System.Random rng, Vector2 foot)
        {
            var jitter = new Vector2(Range(rng, -0.15f, 0.15f), Range(rng, -0.15f, 0.15f));
            var sprite = rocks.Variant(rng.Next(rocks.FramesPerDirection));
            var sr = NewSprite(root, "BorderRock", foot + jitter, sprite);
            sr.transform.localScale = Vector3.one * Range(rng, 1.0f, 1.35f);
            sr.sortingOrder = Sorting.ByY(sr.transform.position.y);
        }
        #endregion

        #region 場內岩石

        private static List<RockGroup> BuildRocks(Transform root, System.Random rng, Vector2 spawn)
        {
            var rocks = ArtLibrary.Get("Rock_Idle");
            var groups = new List<RockGroup>(RockGroupCount);
            for (var g = 0; g < RockGroupCount; g++)
            {
                // 先決定這群的組成（相對位置、變化、縮放），算出佔地圓，再找一個不擋路的位置
                var count = 1 + rng.Next(3);
                var members = new RockMember[count];
                var placed = 0;
                var groupRadius = 0f;
                var visualRadius = 0f;
                for (var k = 0; k < count; k++)
                {
                    var sprite = rocks.Variant(rng.Next(rocks.FramesPerDirection));
                    var scale = Range(rng, 0.9f, 1.5f);
                    var fp = Footprint(sprite);
                    var offset = Vector2.zero;
                    if (k > 0 && !TryAttach(rng, members, placed, fp, scale, out offset))
                        break;
                    members[placed++] = new RockMember { Offset = offset, Sprite = sprite, Scale = scale, Shape = fp };
                    groupRadius = Mathf.Max(groupRadius, offset.magnitude + fp.BoundRadius * scale);
                    visualRadius = Mathf.Max(visualRadius, offset.magnitude + fp.VisualHalfWidth * scale);
                }

                if (!TryPlace(rng, groupRadius, groups, spawn, out var center))
                    continue;
                groups.Add(new RockGroup { Center = center, Radius = groupRadius, VisualRadius = Mathf.Max(groupRadius, visualRadius) });

                for (var k = 0; k < placed; k++)
                    CreateRock(root, members[k].Sprite, center + members[k].Offset, members[k].Scale);
            }
            return groups;
        }

        /// <summary>
        /// 新的一顆岩石貼在前一顆旁邊（兩者內切圓重疊，所以碰撞一定相連），並檢查與群內其他岩石：
        /// 要嘛也重疊、要嘛間隙至少 <see cref="PassageWidth"/>，不留卡得住角色的窄縫。試 8 個角度都不行就放棄這顆。
        /// </summary>
        private static bool TryAttach(System.Random rng, RockMember[] members, int placed, RockShape fp, float scale, out Vector2 offset)
        {
            var prev = members[placed - 1];
            var candidate = new RockMember { Shape = fp, Scale = scale };
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var angle = Range(rng, 0f, Mathf.PI * 2f);
                var dist = (prev.InnerRadius + candidate.InnerRadius) * 0.8f;
                // 讓新岩石的碰撞中心落在 prev 碰撞中心的 angle 方向 dist 處
                candidate.Offset = prev.ColliderCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist - fp.ColliderCenter * scale;
                var ok = true;
                for (var j = 0; j < placed - 1 && ok; j++)
                {
                    var d = Vector2.Distance(members[j].ColliderCenter, candidate.ColliderCenter);
                    var overlaps = d < members[j].InnerRadius + candidate.InnerRadius;
                    var farEnough = d > members[j].OuterRadius + candidate.OuterRadius + PassageWidth;
                    ok = overlaps || farEnough;
                }
                if (ok)
                {
                    offset = candidate.Offset;
                    return true;
                }
            }
            offset = default;
            return false;
        }

        /// <summary>最多試 60 次找一個與出生點、牆、其他群都保持距離的群中心（佔地圓中心）。</summary>
        private static bool TryPlace(System.Random rng, float radius, List<RockGroup> groups, Vector2 spawn, out Vector2 center)
        {
            var inner = Expand(PlayArea, -(radius + PassageWidth));
            for (var attempt = 0; attempt < 60; attempt++)
            {
                center = new Vector2(Range(rng, inner.xMin, inner.xMax), Range(rng, inner.yMin, inner.yMax));
                if (Vector2.Distance(center, spawn) < SpawnClearRadius + radius + 0.5f)
                    continue;
                var ok = true;
                foreach (var g in groups)
                {
                    if (Vector2.Distance(center, g.Center) < g.Radius + radius + PassageWidth)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return true;
            }
            center = default;
            return false;
        }

        private static void CreateRock(Transform root, Sprite sprite, Vector2 foot, float scale)
        {
            var sr = NewSprite(root, "Rock", foot, sprite);
            sr.transform.localScale = Vector3.one * scale;
            sr.sortingOrder = Sorting.ByY(foot.y);
            var fp = Footprint(sprite);
            var col = sr.gameObject.AddComponent<CapsuleCollider2D>();
            col.direction = fp.ColliderSize.x >= fp.ColliderSize.y ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
            col.offset = fp.ColliderCenter;   // 本地座標，會跟著 localScale 一起縮放
            col.size = fp.ColliderSize;
            sr.gameObject.AddComponent<Obstacle>();
        }

        /// <summary>
        /// 岩石的形狀：用 sprite.vertices 的緊貼外框（Tight mesh 會貼著不透明區；FullRect 則是整張）。
        /// 碰撞用膠囊，寬取外框的 85%、高取 60%，放在外框偏下（俯視角下碰撞貼近底部）；
        /// 真素材的岩石偏寬（約 1.3×0.75），用圓形會窄到子彈看起來穿過岩石邊緣。
        /// </summary>
        private static RockShape Footprint(Sprite sprite)
        {
            var verts = sprite.vertices;
            Vector2 min, max;
            if (verts == null || verts.Length == 0)
            {
                min = sprite.bounds.min;
                max = sprite.bounds.max;
            }
            else
            {
                min = max = verts[0];
                foreach (var v in verts)
                {
                    min = Vector2.Min(min, v);
                    max = Vector2.Max(max, v);
                }
            }
            var size = Vector2.Max(max - min, new Vector2(0.2f, 0.2f));
            return new RockShape
            {
                ColliderCenter = new Vector2((min.x + max.x) * 0.5f, min.y + size.y * 0.35f),
                ColliderSize = new Vector2(size.x * 0.85f, size.y * 0.6f),
                Width = size.x,
                VisualHalfWidth = Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)),
            };
        }
        #endregion

        #region 水晶

        private static void BuildCrystals(Transform root, System.Random rng, List<RockGroup> groups, Vector2 spawn)
        {
            var crystals = ArtLibrary.Get("Crystal_Idle");
            var placed = new List<Vector2>(CrystalCount);
            for (var n = 0; n < CrystalCount; n++)
            {
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    Vector2 pos;
                    if (n % 2 == 0 && groups.Count > 0)
                    {
                        // 貼著岩石群的外觀邊緣（用外觀半徑，不會插進岩石圖裡）
                        var g = groups[rng.Next(groups.Count)];
                        var angle = Range(rng, 0f, Mathf.PI * 2f);
                        pos = g.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (g.VisualRadius + Range(rng, 0.05f, 0.3f));
                    }
                    else
                    {
                        // 貼著牆
                        pos = AlongWall(rng, Range(rng, 0.3f, 0.7f));
                    }
                    if (!PlayArea.Contains(pos) || Vector2.Distance(pos, spawn) < 2.5f)
                        continue;
                    if (InsideAnyGroup(pos, groups) || TooClose(pos, placed, 2.5f))
                        continue;
                    placed.Add(pos);
                    var variant = rng.Next(crystals.FramesPerDirection);
                    CreateCrystal(root, crystals.Variant(variant), pos, Range(rng, 0.7f, 1.1f),
                        CrystalColors[variant % CrystalColors.Length], (float)rng.NextDouble());
                    break;
                }
            }
        }

        private static void CreateCrystal(Transform root, Sprite sprite, Vector2 foot, float scale, Color color, float phase)
        {
            var sr = NewSprite(root, "Crystal", foot, sprite);
            sr.transform.localScale = Vector3.one * scale;
            sr.sortingOrder = Sorting.ByY(foot.y);
            // 光源放在水晶的視覺中心；掛在水晶底下會被 localScale 影響位置，所以用 sprite 本地座標
            var light = GlowLights.CreatePointLight(sr.transform, color, 3f, 0.9f);
            light.transform.localPosition = sprite.bounds.center;
            var glow = sr.gameObject.AddComponent<CrystalGlow>();
            glow.Init(light, phase);
        }

        private static Vector2 AlongWall(System.Random rng, float inset)
        {
            var a = PlayArea;
            switch (rng.Next(4))
            {
                case 0: return new Vector2(Range(rng, a.xMin + 1f, a.xMax - 1f), a.yMin + inset);
                case 1: return new Vector2(Range(rng, a.xMin + 1f, a.xMax - 1f), a.yMax - inset);
                case 2: return new Vector2(a.xMin + inset, Range(rng, a.yMin + 1f, a.yMax - 1f));
                default: return new Vector2(a.xMax - inset, Range(rng, a.yMin + 1f, a.yMax - 1f));
            }
        }

        private static bool InsideAnyGroup(Vector2 pos, List<RockGroup> groups)
        {
            foreach (var g in groups)
            {
                if (Vector2.Distance(pos, g.Center) < g.VisualRadius)
                    return true;
            }
            return false;
        }

        private static bool TooClose(Vector2 pos, List<Vector2> others, float minDistance)
        {
            foreach (var o in others)
            {
                if (Vector2.Distance(pos, o) < minDistance)
                    return true;
            }
            return false;
        }
        #endregion

        #region 共用

        private static Transform NewGroup(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>建立一個 SpriteRenderer（材質用預設，URP 2D 下是 Sprite-Lit-Default，才吃得到光與法線）。</summary>
        private static SpriteRenderer NewSprite(Transform root, string name, Vector2 position, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            return sr;
        }

        private static Rect Expand(Rect r, float amount)
        {
            return new Rect(r.xMin - amount, r.yMin - amount, r.width + amount * 2f, r.height + amount * 2f);
        }

        private static float Range(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }
        #endregion
    }
}
