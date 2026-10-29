using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 地上的一件道具（由 <see cref="LootDrops.Spawn"/> 建立）。物件的位置＝道具底部中央。
    /// 移動不用 Rigidbody：自己用 BoxCast 對地形（<see cref="TerrainTag"/>）做碰撞，不會被角色推動，也不會擋子彈。
    /// 地上的計時（彈跳、30 秒消失、閃爍）用 scaled time，暫停時停住；拾取飛行交給 <see cref="LootHudView"/>（unscaled）。
    /// </summary>
    internal sealed class LootDrop : MonoBehaviour
    {
        #region 常數
        internal const float Lifetime = 30f;
        internal const float BlinkTime = 5f;
        private const float Gravity = 45f;
        private const float MaxFall = 18f;
        /// <summary>第一次落地時往上彈的比例；之後落地就停住（整段約 0.5 秒）。</summary>
        private const float Bounce = 0.3f;
        private const float MinBounceSpeed = 2f;
        private const float Skin = 0.01f;
        /// <summary>空中太久（掉進坑、卡進地形）：拉回噴出點正下方的地面。</summary>
        private const float RescueAirTime = 1.6f;
        private const float RescueProbe = 30f;
        /// <summary>噴出點正下方找不到地面時，往左右每隔這麼遠再找，最多找到 RescueReach。</summary>
        private const float RescueStep = 0.5f, RescueReach = 4f;
        private const float ColliderWidth = 0.4f, ColliderHeight = 0.2f;
        /// <summary>主角身體碰到道具（外擴這麼多）＝走到道具上。</summary>
        private const float PickupMargin = 0.1f;
        /// <summary>玩家丟的道具：主角離開這個外擴範圍才恢復可撿（比 PickupMargin 大，避免邊界來回跳）。</summary>
        private const float LeaveMargin = 0.5f;
        /// <summary>主角站在道具上但背包放不下：每隔這麼久安靜地再判斷一次（只有剛走上來那次顯示「背包已滿」）。</summary>
        private const float RetryInterval = 0.4f;
        private const float BeamWidth = 0.42f, BeamHeight = 2.4f;
        private const float BobHeight = 0.05f, BobSpeed = 3f;
        #endregion

        private GameSession _session;
        private ItemData _item;
        private bool _droppedByPlayer;
        private Vector2 _velocity;
        private Vector2 _spawnPoint;
        private bool _grounded;
        private bool _bounced;
        private float _airTime;
        private float _age;
        private float _blinkPhase;
        private bool _pendingAuto;
        private bool _heroInside;
        private float _retry;
        private Vector2 _size;

        private Transform _visual;
        private SpriteRenderer _icon;
        private SpriteRenderer _beam;
        private SpriteRenderer _glow;
        private Color _beamColor;
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
        private ContactFilter2D _filter;

        internal ItemData Item => _item;

        internal static LootDrop Create(Transform parent, GameSession session, ItemData item, Vector2 position, bool droppedByPlayer)
        {
            var go = new GameObject($"Loot_{item.Name}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            var drop = go.AddComponent<LootDrop>();
            drop.Init(session, item, droppedByPlayer);
            return drop;
        }

        private void Init(GameSession session, ItemData item, bool droppedByPlayer)
        {
            _session = session;
            _item = item;
            _droppedByPlayer = droppedByPlayer;
            _spawnPoint = transform.position;
            _pendingAuto = !droppedByPlayer;
            _filter = new ContactFilter2D { useTriggers = false };
            _size = new Vector2(item.Width, item.Height) * LootVisuals.CellWorld;
            var side = Random.value < 0.5f ? -1f : 1f;
            _velocity = droppedByPlayer
                ? new Vector2(side * Random.Range(0.3f, 0.9f), Random.Range(4f, 5f))
                : new Vector2(side * Random.Range(0.4f, 2.2f), Random.Range(6.5f, 8f));

            var color = ItemData.RarityColor(item.Rarity);
            _beamColor = new Color(color.r, color.g, color.b, item.Rarity == Rarity.Common ? 0.35f : 0.55f);

            _glow = NewRenderer("Glow", transform, LootVisuals.GlowSprite(), SsSorting.Npc - 7);
            _glow.transform.localScale = new Vector3(Mathf.Max(0.6f, _size.x * 1.4f), 0.3f, 1f);
            _glow.color = _beamColor;
            _beam = NewRenderer("Beam", transform, LootVisuals.BeamSprite(), SsSorting.Npc - 6);
            _beam.transform.localScale = new Vector3(BeamWidth, BeamHeight / 4f, 1f);
            _beam.color = _beamColor;
            _beam.enabled = _glow.enabled = false;

            _visual = new GameObject("Icon").transform;
            _visual.SetParent(transform, false);
            _visual.localPosition = new Vector3(0f, _size.y * 0.5f, 0f);
            _icon = NewRenderer("Sprite", _visual, LootVisuals.Icon(item), SsSorting.Npc - 5);
        }

        private static SpriteRenderer NewRenderer(string name, Transform parent, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        #region 每幀
        private void Update()
        {
            if (_session == null || _item == null)
            {
                Destroy(gameObject);
                return;
            }
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            _age += dt;
            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }
            if (!_grounded)
                Move(dt);
            UpdateLook();
            if (_grounded && dt > 0f && !_session.Paused)
                UpdatePickup(dt);
        }

        private void UpdateLook()
        {
            var remaining = Lifetime - _age;
            var visible = true;
            if (remaining < BlinkTime)
            {
                // 越接近消失閃得越快（每秒 3 次 → 8 次）；相位自己累積，頻率改變時才不會亂跳
                _blinkPhase += Mathf.Lerp(8f, 3f, remaining / BlinkTime) * Mathf.Min(Time.deltaTime, 0.05f);
                visible = Mathf.Repeat(_blinkPhase, 1f) < 0.6f;
            }
            _icon.enabled = visible;
            _beam.enabled = _glow.enabled = _grounded && visible;
            if (_grounded)
            {
                var bob = Mathf.Sin(_age * BobSpeed) * BobHeight;
                _visual.localPosition = new Vector3(0f, _size.y * 0.5f + BobHeight + bob, 0f);
                var pulse = 0.8f + 0.2f * Mathf.Sin(_age * 4f);
                _beam.color = new Color(_beamColor.r, _beamColor.g, _beamColor.b, _beamColor.a * pulse);
            }
        }
        #endregion

        #region 移動與碰撞
        private void Move(float dt)
        {
            if (dt <= 0f)
                return;
            _airTime += dt;
            if (_airTime > RescueAirTime)
            {
                Rescue();
                return;
            }
            _velocity.y = Mathf.Max(_velocity.y - Gravity * dt, -MaxFall);
            var pos = (Vector2)transform.position;

            var dx = _velocity.x * dt;
            if (Mathf.Abs(dx) > 0.0001f)
            {
                var dir = new Vector2(Mathf.Sign(dx), 0f);
                if (Cast(pos, dir, Mathf.Abs(dx), false, out var dist))
                {
                    pos.x += dir.x * Mathf.Max(0f, dist - Skin);
                    _velocity.x = -_velocity.x * 0.3f;
                }
                else
                {
                    pos.x += dx;
                }
            }

            var dy = _velocity.y * dt;
            if (dy < 0f)
            {
                if (Cast(pos, Vector2.down, -dy, true, out var dist))
                {
                    pos.y -= Mathf.Max(0f, dist - Skin);
                    Land();
                }
                else
                {
                    pos.y += dy;
                }
            }
            else if (dy > 0f)
            {
                if (Cast(pos, Vector2.up, dy, false, out var dist))
                {
                    pos.y += Mathf.Max(0f, dist - Skin);
                    _velocity.y = 0f;
                }
                else
                {
                    pos.y += dy;
                }
            }
            transform.position = new Vector3(pos.x, pos.y, 0f);
        }

        private void Land()
        {
            if (!_bounced && -_velocity.y > MinBounceSpeed)
            {
                _bounced = true;
                _velocity = new Vector2(_velocity.x * 0.5f, -_velocity.y * Bounce);
                return;
            }
            _velocity = Vector2.zero;
            _grounded = true;
        }

        /// <summary>
        /// 空中太久（掉進坑、穿過地形）：從噴出點往下找地面放上去；正下方是坑就往左右交替找最近的地面；
        /// 都找不到就停在噴出點（不會掉出關卡）。
        /// </summary>
        private void Rescue()
        {
            var pos = _spawnPoint;
            for (var offset = 0f; offset <= RescueReach; offset += RescueStep)
            {
                if (TryGround(_spawnPoint + new Vector2(offset, 0f), ref pos) || offset > 0f && TryGround(_spawnPoint - new Vector2(offset, 0f), ref pos))
                    break;
            }
            transform.position = new Vector3(pos.x, pos.y, 0f);
            _velocity = Vector2.zero;
            _grounded = true;
            Debug.LogWarning($"[Sidescroller.LootDrop] {_item.Name} 在空中太久，拉回噴出點下方 ({pos.x:0.0}, {pos.y:0.0})");
        }

        private bool TryGround(Vector2 from, ref Vector2 pos)
        {
            if (!Cast(from, Vector2.down, RescueProbe, true, out var dist))
                return false;
            pos = new Vector2(from.x, from.y - Mathf.Max(0f, dist - Skin));
            return true;
        }

        /// <summary>
        /// 腳底的小方框往 dir 掃 distance：只認地形。實心一律擋；單向平台只在往下、而且起點在平台頂端以上時擋。
        /// 起點就重疊的（距離 0）忽略，避免卡在地形裡動不了（空中太久會被 <see cref="Rescue"/> 拉回）。
        /// </summary>
        private bool Cast(Vector2 feet, Vector2 dir, float distance, bool allowOneWay, out float hitDistance)
        {
            hitDistance = float.MaxValue;
            var center = feet + new Vector2(0f, ColliderHeight * 0.5f + Skin);
            var size = new Vector2(Mathf.Min(ColliderWidth, _size.x), ColliderHeight);
            var n = Physics2D.BoxCast(center, size, 0f, dir, _filter, _hits, distance + Skin);
            var found = false;
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.distance <= 0f)
                    continue;
                var terrain = h.collider.GetComponentInParent<TerrainTag>();
                if (terrain == null)
                    continue;
                if (terrain.Kind == TerrainKind.OneWay)
                {
                    if (!allowOneWay || feet.y < h.collider.bounds.max.y - 0.05f)
                        continue;
                }
                if (h.distance < hitDistance)
                {
                    hitDistance = h.distance;
                    found = true;
                }
            }
            return found;
        }
        #endregion

        #region 拾取
        private void UpdatePickup(float dt)
        {
            var hero = HeroBounds(out var heroRect);
            var inside = hero && Overlaps(heroRect, PickupMargin);

            if (_droppedByPlayer)
            {
                // 玩家丟的：主角離開範圍才恢復，之後要主角再走上來才撿
                if (!hero || !Overlaps(heroRect, LeaveMargin))
                    _droppedByPlayer = false;
                _heroInside = inside;
                return;
            }

            if (_pendingAuto)
            {
                _pendingAuto = false;
                _heroInside = inside;
                _retry = RetryInterval;
                TryPickup(true);
                return;
            }

            if (inside && !_heroInside)
            {
                _retry = RetryInterval;
                TryPickup(true);
            }
            else if (inside)
            {
                _retry -= dt;
                if (_retry <= 0f)
                {
                    _retry = RetryInterval;
                    TryPickup(false);
                }
            }
            _heroInside = inside;
        }

        /// <summary>預留背包位置並起飛；整件都進背包就刪掉地上的物件。放不下（或零件只放得下一部分）時 feedback＝true 會讓背包圖示閃紅。</summary>
        private void TryPickup(bool feedback)
        {
            if (this == null || _item == null)
                return;
            var reservation = _session.Loot.Reserve(_item);
            var hud = LootHudView.Find();
            if (reservation == null)
            {
                if (feedback)
                    hud?.ShowFull();
                return;
            }
            var from = (Vector2)_visual.position;
            if (hud != null)
                hud.Fly(reservation, from, _size, transform.parent);
            else
                reservation.Commit();

            if (_item.Kind == ItemKind.Part && _item.Count > 0)
            {
                // 零件只放得下一部分：剩下的留在地上
                if (feedback)
                    hud?.ShowFull();
                return;
            }
            _item = null;
            Destroy(gameObject);
        }

        private static bool HeroBounds(out Rect rect)
        {
            rect = default;
            var hero = SidescrollerGame.Flow != null ? SidescrollerGame.Flow.Hero : null;
            if (hero == null || hero.Body == null || !hero.Body.enabled)
                return false;
            var b = hero.Body.bounds;
            rect = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            return true;
        }

        private bool Overlaps(Rect hero, float margin)
        {
            var p = (Vector2)transform.position;
            var mine = new Rect(p.x - _size.x * 0.5f - margin, p.y - margin, _size.x + margin * 2f, _size.y + margin * 2f);
            return mine.Overlaps(hero);
        }
        #endregion
    }
}
