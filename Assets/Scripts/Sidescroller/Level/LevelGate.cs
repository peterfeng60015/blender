using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// Boss 房閘門（interfaces.md §4.2）：旗標 <see cref="SsIds.FlagGateClosed"/> 成立時是實心牆（TerrainTag.Solid），
    /// 不成立時沒有碰撞也看不到。建立時讀旗標決定初始狀態（不播動畫），之後訂閱 <c>Flags.Changed</c>，
    /// 關上時鐵門從上方落下、打開時升起。
    /// </summary>
    public sealed class LevelGate : MonoBehaviour
    {
        /// <summary>落下或升起要幾秒（scaled time）。</summary>
        private const float MoveTime = 0.25f;

        private GameSession _session;
        private BoxCollider2D _collider;
        private Transform _visual;
        private int _height;
        private bool _closed;
        /// <summary>0＝全開（藏在上方）、1＝全關。</summary>
        private float _amount;

        internal static LevelGate Create(Transform parent, LevelData.GateData data, Sprite tile, GameSession session)
        {
            var go = new GameObject($"Gate_{data.Id}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(data.X, data.Y, 0f);
            var box = go.AddComponent<BoxCollider2D>();
            box.offset = new Vector2(0.5f, data.Height * 0.5f);
            box.size = new Vector2(1f, data.Height);
            go.AddComponent<TerrainTag>().Kind = TerrainKind.Solid;

            var visual = LevelBuilder.NewChild(go.transform, "Visual");
            for (var i = 0; i < data.Height; i++)
                LevelBuilder.PlaceSprite(visual, $"GateTile_{i}", tile, new Vector2(0.5f, i + 0.5f), false, SsSorting.Terrain + 1);

            var gate = go.AddComponent<LevelGate>();
            gate._session = session;
            gate._collider = box;
            gate._visual = visual;
            gate._height = data.Height;
            gate._closed = session.Flags.Get(SsIds.FlagGateClosed);
            gate._amount = gate._closed ? 1f : 0f;
            gate.Apply();
            session.Flags.Changed += gate.OnFlagChanged;
            return gate;
        }

        public bool IsClosed => _closed;

        private void OnFlagChanged(string flag, bool value)
        {
            if (flag != SsIds.FlagGateClosed || this == null)
                return;
            _closed = value;
            // 碰撞立刻生效（主角一定已經在房間裡），外觀用 Update 慢慢落下
            _collider.enabled = value;
        }

        private void Update()
        {
            var target = _closed ? 1f : 0f;
            if (Mathf.Approximately(_amount, target))
                return;
            _amount = Mathf.MoveTowards(_amount, target, Time.deltaTime / MoveTime);
            Apply();
        }

        private void Apply()
        {
            _collider.enabled = _closed;
            _visual.gameObject.SetActive(_amount > 0f);
            _visual.localPosition = new Vector3(0f, (1f - _amount) * _height, 0f);
        }

        private void OnDestroy()
        {
            if (_session != null)
                _session.Flags.Changed -= OnFlagChanged;
        }
    }
}
