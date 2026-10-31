using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sidescroller
{
    /// <summary>
    /// 把關卡的地形 tile 合成網格畫：每 <see cref="ChunkColumns"/> 欄一個 MeshRenderer，同一張貼圖、同一個材質，一塊一個 draw call。
    /// 每格各一個 SpriteRenderer 時，Stage1 畫面上約 60 格就是 60 個 draw call（URP 2D 的 sprite 不會自動合批）。
    ///
    /// 網格照 sprite 自己的頂點／UV／三角形組（和 SpriteRenderer 畫出來的一樣，pivot、裁邊都對得上），
    /// 材質用管線的預設 2D 材質（Sprite-Lit-Default）複製一份換上貼圖；網格與材質在關卡拆掉時一起銷毀。
    /// 只用在不會個別變動的 tile（地形、箱子、單向平台）；閘門要開關，仍用 SpriteRenderer。
    /// </summary>
    public sealed class TileMeshBatcher : MonoBehaviour
    {
        /// <summary>一塊網格的寬度（欄）。畫面寬約 30 格，同時只會看到 1～2 塊，看不到的整塊被剔除。</summary>
        private const int ChunkColumns = 32;

        private readonly List<Object> _owned = new List<Object>();

        private sealed class Chunk
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        private readonly Dictionary<(int, Texture2D), Chunk> _chunks = new Dictionary<(int, Texture2D), Chunk>();

        /// <summary>加一格：中心放在 anchor（區域座標），和 LevelBuilder.PlaceSprite(bottom: false) 擺的位置一樣。</summary>
        public void Add(Sprite sprite, Vector2 anchor, int column)
        {
            if (sprite == null)
                return;
            var key = (column / ChunkColumns, sprite.texture);
            if (!_chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk();
                _chunks[key] = chunk;
            }
            var offset = anchor - (Vector2)sprite.bounds.center;
            var start = chunk.Vertices.Count;
            foreach (var v in sprite.vertices)
                chunk.Vertices.Add(new Vector3(v.x + offset.x, v.y + offset.y, 0f));
            chunk.Uvs.AddRange(sprite.uv);
            foreach (var t in sprite.triangles)
                chunk.Triangles.Add(start + t);
        }

        /// <summary>依加進來的格子建網格；sortingOrder 同原本的 SpriteRenderer。回傳建了幾塊。</summary>
        public int Build(int sortingOrder)
        {
            var baseMaterial = DefaultSpriteMaterial();
            var materials = new Dictionary<Texture2D, Material>();
            foreach (var pair in _chunks)
            {
                var (index, texture) = pair.Key;
                var chunk = pair.Value;
                if (!materials.TryGetValue(texture, out var material))
                {
                    material = new Material(baseMaterial) { name = $"Tiles_{texture.name}", mainTexture = texture };
                    materials[texture] = material;
                    _owned.Add(material);
                }

                var mesh = new Mesh { name = $"Tiles_{index}" };
                mesh.indexFormat = chunk.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(chunk.Vertices);
                mesh.SetUVs(0, chunk.Uvs);
                var colors = new Color32[chunk.Vertices.Count];
                for (var i = 0; i < colors.Length; i++)
                    colors[i] = new Color32(255, 255, 255, 255);
                mesh.SetColors(colors);
                mesh.SetTriangles(chunk.Triangles, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                _owned.Add(mesh);

                var go = new GameObject(mesh.name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = sortingOrder;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var count = _chunks.Count;
            _chunks.Clear();
            return count;
        }

        /// <summary>管線的預設 2D 材質（SpriteRenderer 預設用的那個）；拿不到時借一個暫時的 SpriteRenderer 讀。</summary>
        private static Material DefaultSpriteMaterial()
        {
            var material = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.default2DMaterial : null;
            if (material != null)
                return material;
            var temp = new GameObject("TempSprite");
            material = temp.AddComponent<SpriteRenderer>().sharedMaterial;
            Destroy(temp);
            return material;
        }

        private void OnDestroy()
        {
            foreach (var o in _owned)
            {
                if (o != null)
                    Destroy(o);
            }
            _owned.Clear();
        }
    }
}
