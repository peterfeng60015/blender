using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Sidescroller
{
    /// <summary>
    /// 建整個關卡（interfaces.md §4.2）：地形 tile＋碰撞（掛 TerrainTag）、單向平台、閘門、觸發區、裝飾與地標、視差背景、Global Light 2D。
    /// 不建角色、相機、UI。關卡資料格式見 <see cref="LevelData"/>。
    ///
    /// 座標約定：所有物件建在 parent 的區域座標，<see cref="LevelInfo"/> 的座標也直接當世界座標用，
    /// 所以 parent 要放在原點、不旋轉不縮放（GameFlow 的 worldRoot 就是）。parent 必須是啟用中的物件（Light2D.Awake 才會跑）。
    /// </summary>
    public static class LevelBuilder
    {
        #region 調校
        /// <summary>單向平台的碰撞厚度（貼著格子頂端），太薄落速快時可能穿過。</summary>
        private const float OneWayThickness = 0.5f;
        /// <summary>關卡左右兩側看不見的牆，讓角色不會走出鏡頭範圍。</summary>
        private const float BoundaryWallHeight = 40f;
        #endregion

        #region SsTiles 變化（design.md §8.4）
        private const int TileGroundTop = 0, TileGround = 1, TileOneWay = 2, TileCrate = 3, TileSteel = 4, TileCeiling = 5, TileGate = 6;
        private const string TilesLocation = "SsTiles_Idle";
        #endregion

        public static LevelInfo Build(Transform parent, string levelText, GameSession session, CameraRig rig)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            // 先解析：格式錯誤時不留下任何物件
            var data = LevelData.Parse(levelText);

            var root = new GameObject("Level");
            root.transform.SetParent(parent, false);
            try
            {
                var tiles = SsArt.Get(TilesLocation);
                BuildSolids(root.transform, data);
                BuildOneWays(root.transform, data);
                BuildTiles(root.transform, data, tiles);
                foreach (var gate in data.Gates)
                    LevelGate.Create(root.transform, gate, tiles.Variant(TileGate), session);
                var triggerRoot = NewChild(root.transform, "Triggers");
                foreach (var trigger in data.Triggers)
                    LevelTrigger.Create(triggerRoot, trigger, session);
                BuildDecor(root.transform, data);
                if (rig != null)
                    LevelParallax.Create(root.transform, rig, data.CameraBounds, data.BgMidSwitchX);
                else
                    Debug.LogWarning("[Sidescroller.LevelBuilder] 沒有 CameraRig，不建視差背景");
                BuildGlobalLight(root.transform);
            }
            catch
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                throw;
            }

            var info = new LevelInfo
            {
                CameraBounds = data.CameraBounds,
                ArenaRect = data.Arena,
                KillY = data.KillY
            };
            info.Spawns.AddRange(data.Spawns);
            info.Checkpoints.AddRange(data.Checkpoints);
            info.Triggers.AddRange(data.Triggers);
            return info;
        }

        #region 地形碰撞
        /// <summary>
        /// 實心格（# x ^）合成矩形，全部掛在同一個物件上、併進一個 CompositeCollider2D，
        /// 外框是一整片多邊形，角色走過 tile 接縫不會卡。左右再加看不見的邊界牆。
        /// </summary>
        private static void BuildSolids(Transform parent, LevelData data)
        {
            var go = NewChild(parent, "Solids").gameObject;
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var composite = go.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Manual;
            go.AddComponent<TerrainTag>().Kind = TerrainKind.Solid;

            foreach (var r in MergeRects(data, false))
                AddBox(go, new Rect(r.X, r.Y, r.W, r.H));

            var b = data.CameraBounds;
            AddBox(go, new Rect(b.xMin - 1f, b.yMin - 10f, 1f, BoundaryWallHeight));
            AddBox(go, new Rect(b.xMax, b.yMin - 10f, 1f, BoundaryWallHeight));
            composite.GenerateGeometry();
        }

        /// <summary>每段連續的 = 一個物件：薄的 BoxCollider2D（貼格子頂端）＋PlatformEffector2D＋TerrainTag.OneWay。</summary>
        private static void BuildOneWays(Transform parent, LevelData data)
        {
            var group = NewChild(parent, "OneWays");
            foreach (var r in MergeRects(data, true))
            {
                for (var row = 0; row < r.H; row++)
                {
                    var y = r.Y + row;
                    var go = NewChild(group, $"OneWay_{r.X}_{y}").gameObject;
                    var box = go.AddComponent<BoxCollider2D>();
                    box.offset = new Vector2(r.X + r.W * 0.5f, y + 1f - OneWayThickness * 0.5f);
                    box.size = new Vector2(r.W, OneWayThickness);
                    box.usedByEffector = true;
                    var effector = go.AddComponent<PlatformEffector2D>();
                    effector.useOneWay = true;
                    effector.useOneWayGrouping = true;
                    effector.surfaceArc = 180f;
                    go.AddComponent<TerrainTag>().Kind = TerrainKind.OneWay;
                }
            }
        }

        private sealed class CellRect
        {
            public int X, Y, W, H;
        }

        /// <summary>每列找連續段，再把上下相同的段合成一個矩形。oneWay=false 找實心格，true 找單向平台。</summary>
        private static List<CellRect> MergeRects(LevelData data, bool oneWay)
        {
            var done = new List<CellRect>();
            var open = new List<CellRect>();
            for (var y = 0; y < data.Height; y++)
            {
                var next = new List<CellRect>();
                var x = 0;
                while (x < data.Width)
                {
                    if (!Match(data.Cell(x, y), oneWay))
                    {
                        x++;
                        continue;
                    }
                    var start = x;
                    while (x < data.Width && Match(data.Cell(x, y), oneWay))
                        x++;
                    var w = x - start;
                    var above = open.Find(o => o.X == start && o.W == w);
                    if (above != null)
                    {
                        above.H++;
                        open.Remove(above);
                        next.Add(above);
                    }
                    else
                    {
                        next.Add(new CellRect { X = start, Y = y, W = w, H = 1 });
                    }
                }
                done.AddRange(open);
                open = next;
            }
            done.AddRange(open);
            return done;
        }

        private static bool Match(char c, bool oneWay) => oneWay ? c == LevelData.OneWay : LevelData.IsSolid(c);

        private static void AddBox(GameObject go, Rect rect)
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.offset = rect.center;
            box.size = rect.size;
            box.compositeOperation = Collider2D.CompositeOperation.Merge;
        }
        #endregion

        #region 外觀
        /// <summary>
        /// 地形 tile 合成網格（<see cref="TileMeshBatcher"/>，每 32 欄一個 draw call）。
        /// 不用 SpriteRenderer drawMode Tiled：它要求 FullRect mesh，正式素材不一定是。
        /// </summary>
        private static void BuildTiles(Transform parent, LevelData data, SpriteSet tiles)
        {
            var batcher = NewChild(parent, "Tiles").gameObject.AddComponent<TileMeshBatcher>();
            for (var y = 0; y < data.Height; y++)
            {
                for (var x = 0; x < data.Width; x++)
                {
                    var variant = TileVariant(data, x, y);
                    if (variant < 0)
                        continue;
                    batcher.Add(tiles.Variant(variant), new Vector2(x + 0.5f, y + 0.5f), x);
                }
            }
            batcher.Build(SsSorting.Terrain);
        }

        private static int TileVariant(LevelData data, int x, int y)
        {
            switch (data.Cell(x, y))
            {
                case LevelData.Solid:
                    // Boss 房裡（含緊貼右緣那一欄，Stage1 的右牆在 x 214）單獨一格寬的直立牆用鋼樑
                    if (x >= data.Arena.xMin && x <= data.Arena.xMax && y + 0.5f >= data.Arena.yMin && y + 0.5f < data.Arena.yMax &&
                        data.Cell(x - 1, y) != LevelData.Solid && data.Cell(x + 1, y) != LevelData.Solid)
                        return TileSteel;
                    return LevelData.IsSolid(data.Cell(x, y + 1)) ? TileGround : TileGroundTop;
                case LevelData.OneWay: return TileOneWay;
                case LevelData.Crate: return TileCrate;
                case LevelData.Ceiling: return TileCeiling;
                default: return -1;
            }
        }

        private static void BuildDecor(Transform parent, LevelData data)
        {
            var group = NewChild(parent, "Decor");
            foreach (var d in data.Decor)
            {
                var landmark = d.Name == "SsLandmark";
                var sprite = SsArt.Get(d.Name + "_Idle").Variant(d.Variant);
                PlaceSprite(group, $"{d.Name}_{d.Variant}", sprite, d.Position, true, landmark ? SsSorting.Landmark : SsSorting.Props);
            }
        }

        /// <summary>
        /// 依 sprite 的實際外框擺放，pivot 在哪裡都對得上：bottom=true 時 anchor 是底邊中央（道具、地標），否則是中心（tile）。
        /// </summary>
        internal static SpriteRenderer PlaceSprite(Transform parent, string name, Sprite sprite, Vector2 anchor, bool bottom, int order)
        {
            var go = NewChild(parent, name).gameObject;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            var offset = Vector2.zero;
            if (sprite != null)
            {
                var b = sprite.bounds;
                offset = bottom ? new Vector2(b.center.x, b.min.y) : (Vector2)b.center;
            }
            go.transform.localPosition = anchor - offset;
            return sr;
        }

        private static void BuildGlobalLight(Transform parent)
        {
            // 色階已烘進顏色 sheet（design.md §8.2），全域光用白光 1.0 讓顏色原樣顯示；整個場景只有這一盞
            var go = NewChild(parent, "GlobalLight2D").gameObject;
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1f;
        }
        #endregion

        internal static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
