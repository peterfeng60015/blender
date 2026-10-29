using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 背包（10×4 格子盤）、身上三個裝備欄與零件（loot-design.md §1、§4、§5、§7）。整局一份，放在 <see cref="GameSession.Loot"/>：
    /// 死亡重生不扣；回選單、再玩一次時 GameSession 重建，自然清空。開場已裝上「破舊郵差包」。
    /// 只有 data 實作；pickup（拾取）、ui（背包、強化介面）照下面的契約呼叫。全部在主執行緒呼叫。
    /// 不變式（data 維護）：<c>Loadout.HasAttack((AttackId)slot) == (GetEquipped(slot) != null)</c>，HUD、觸控按鈕、主角照舊讀 Loadout。
    /// </summary>
    public sealed class LootState
    {
        public const int Columns = 10, Rows = 4;
        private const string Log = "[Sidescroller.LootState]";

        /// <summary>格子盤上的一件道具與它的左上角；Pending＝零件堆上已被預留、還沒 Commit 的數量。</summary>
        internal sealed class Placed
        {
            public ItemData Item;
            public int X, Y;
            public int Pending;
        }

        private readonly ItemData[] _cells = new ItemData[Columns * Rows];
        private readonly LootReservation[] _reserved = new LootReservation[Columns * Rows];
        private readonly List<Placed> _placed = new List<Placed>();
        private readonly ItemData[] _equipped = new ItemData[3];
        private readonly Loadout _loadout;
        private readonly HashSet<string> _droppedIds = new HashSet<string>();

        /// <summary>不綁 Loadout（測試用）；開場同樣裝上破舊郵差包。</summary>
        public LootState() : this(null) { }

        /// <summary>GameSession 用：裝備欄的變化同步到 loadout 的攻擊旗標。</summary>
        internal LootState(Loadout loadout)
        {
            _loadout = loadout;
            _equipped[(int)EquipSlot.Melee] = LootTables.CreateStarterMelee();
            SyncLoadout();
        }

        /// <summary>格子盤、裝備欄、零件數或強化等級有任何改變時觸發（預留不觸發，Commit 才觸發）。</summary>
        public event Action Changed;

        #region 格子盤
        /// <summary>佔住 (x, y) 的道具（多格道具的任何一格都回同一件）；空格或預留中的格子回 null。x 往右、y 往下，(0,0)＝左上。</summary>
        public ItemData GetAt(int x, int y) => InBounds(x, y) ? _cells[Index(x, y)] : null;

        /// <summary>道具在格子盤上的左上角；不在格子盤上回 false。</summary>
        public bool TryGetOrigin(ItemData item, out int x, out int y)
        {
            var p = FindPlaced(item);
            x = p != null ? p.X : -1;
            y = p != null ? p.Y : -1;
            return p != null;
        }

        /// <summary>把格子盤上的全部道具加進 into（不含裝備欄、不含預留中的）。</summary>
        public void GetItems(List<ItemData> into)
        {
            if (into == null)
                return;
            foreach (var p in _placed)
                into.Add(p.Item);
        }

        /// <summary>
        /// 手上的 item 能不能以 (x, y) 為左上角放下：超出邊界或壓到預留格回 false；壓到 0 件＝直接放；
        /// 壓到剛好 1 件＝交換（swapWith＝那件；零件放到未滿的零件堆上＝合併，swapWith 也是那堆）；壓到 2 件以上回 false。
        /// item 自己若還在格子盤上，它佔的格子當成空的（等於搬位置）；裝在身上的道具回 false（先 Unequip）。
        /// </summary>
        public bool CanPlace(ItemData item, int x, int y, out ItemData swapWith)
        {
            swapWith = null;
            if (item == null || item.Count <= 0 || IsEquipped(item))
                return false;
            if (x < 0 || y < 0 || x + item.Width > Columns || y + item.Height > Rows)
                return false;
            for (var cy = y; cy < y + item.Height; cy++)
            {
                for (var cx = x; cx < x + item.Width; cx++)
                {
                    var i = Index(cx, cy);
                    if (_reserved[i] != null)
                    {
                        swapWith = null;
                        return false;
                    }
                    var other = _cells[i];
                    if (other == null || other == item || other == swapWith)
                        continue;
                    if (swapWith != null)
                    {
                        swapWith = null;
                        return false;
                    }
                    swapWith = other;
                }
            }
            return true;
        }

        /// <summary>
        /// 放下手上的 item（呼叫前先 <see cref="CanPlace"/>；不合法丟 InvalidOperationException）。
        /// 回傳放下後手上剩下的道具：交換出來的那件、零件合併後的餘數，或 null（手上空了）。
        /// 零件放到未滿的零件堆上是合併：能疊多少疊多少，item.Count 扣掉疊進去的數量。
        /// </summary>
        public ItemData Place(ItemData item, int x, int y)
        {
            if (!CanPlace(item, x, y, out var other))
                throw new InvalidOperationException($"{Log} 不能把 {item?.Name} 放在 ({x},{y})");
            RemoveInternal(item);   // 還在格子盤上（搬位置）：先拿起來
            ItemData hand = null;
            if (other != null && IsMergeTarget(item, other))
            {
                var moved = Mathf.Min(item.Count, SpaceLeft(FindPlaced(other)));
                other.Count += moved;
                item.Count -= moved;
                hand = item.Count > 0 ? item : null;
            }
            else
            {
                if (other != null)
                {
                    RemoveInternal(other);
                    hand = other;
                }
                PutInternal(item, x, y);
            }
            RaiseChanged();
            return hand;
        }

        /// <summary>從格子盤拿起（拿到手上）；不在格子盤上回 false。</summary>
        public bool Remove(ItemData item)
        {
            if (!RemoveInternal(item))
                return false;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 自動找位置放進格子盤（零件先疊到既有未滿的堆，剩下的佔新格）；放不下回 false 且不改狀態。用於劇情發放、關背包時收回手上的道具。
        /// 零件全部疊進既有堆時 item 本身不會進格子盤（Count 變 0）。已經在格子盤或裝備欄上的道具回 false。
        /// </summary>
        public bool AddAnywhere(ItemData item)
        {
            if (!AddAnywhereInternal(item))
                return false;
            RaiseChanged();
            return true;
        }

        /// <summary>格子盤上所有零件的總數。</summary>
        public int PartsTotal
        {
            get
            {
                var total = 0;
                foreach (var p in _placed)
                {
                    if (p.Item.Kind == ItemKind.Part)
                        total += p.Item.Count;
                }
                return total;
            }
        }

        /// <summary>扣零件（從數量少的堆先扣，扣光的堆疊移除）；不夠回 false 且不改狀態。</summary>
        public bool SpendParts(int amount)
        {
            if (amount <= 0)
                return true;
            if (PartsTotal < amount)
                return false;
            SpendPartsInternal(amount);
            RaiseChanged();
            return true;
        }
        #endregion

        #region 裝備欄
        public ItemData GetEquipped(EquipSlot slot) => _equipped[(int)slot];

        /// <summary>
        /// 裝上手上的 item（不能在格子盤上；種類必須對應某一欄，零件丟 ArgumentException）。
        /// 回傳換下的那件（原本空著回 null），由呼叫端拿在手上。item 已經裝在身上時不做事、回 null。
        /// </summary>
        public ItemData Equip(ItemData item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (!item.IsEquipment)
                throw new ArgumentException($"{Log} 零件不能裝備", nameof(item));
            if (FindPlaced(item) != null)
                throw new InvalidOperationException($"{Log} {item.Name} 還在格子盤上，先 Remove 再 Equip（或用 TryEquipFromBag）");
            var slot = (int)item.Slot;
            var old = _equipped[slot];
            if (old == item)
                return null;
            _equipped[slot] = item;
            SyncLoadout();
            RaiseChanged();
            return old;
        }

        /// <summary>取下該欄的裝備交給呼叫端（拿在手上）；空著回 null。該欄空著時對應的攻擊不能用。</summary>
        public ItemData Unequip(EquipSlot slot)
        {
            var old = _equipped[(int)slot];
            if (old == null)
                return null;
            _equipped[(int)slot] = null;
            SyncLoadout();
            RaiseChanged();
            return old;
        }

        /// <summary>背包介面的 E 鍵：格子盤上的裝備直接裝上，換下的那件放回同一個位置（同種類同尺寸，一定放得下）。不是格子盤上的裝備回 false。</summary>
        public bool TryEquipFromBag(ItemData item)
        {
            var p = FindPlaced(item);
            if (p == null || !item.IsEquipment)
                return false;
            int x = p.X, y = p.Y;
            RemoveInternal(item);
            var slot = (int)item.Slot;
            var old = _equipped[slot];
            _equipped[slot] = item;
            if (old != null)
                PutInternal(old, x, y);
            SyncLoadout();
            RaiseChanged();
            return true;
        }
        #endregion

        #region 拾取（pickup 用）
        /// <summary>
        /// 落地後判斷放不放得下並先預留位置（同時掉落的多件不會搶同一格）。
        /// 裝備：有位置就預留整件，回傳的 <see cref="LootReservation.Item"/> 就是 item；沒位置回 null。
        /// 零件：預留 min(item.Count, 可放的數量)；回傳的 Item 是新的一堆（Count＝預留數），item.Count 同時扣掉預留數（扣到 0＝整堆都進背包）；一個都放不下回 null。
        /// 零件的 Cancel 只釋放格子，不會把數量加回 item（回傳的那堆由呼叫端處理，例如留在地上）。
        /// </summary>
        public LootReservation Reserve(ItemData item)
        {
            if (item == null || item.Count <= 0 || FindPlaced(item) != null || IsEquipped(item))
                return null;
            var r = new LootReservation { Owner = this };
            if (item.IsEquipment)
            {
                if (!FindFree(item.Width, item.Height, out var x, out var y))
                    return null;
                r.Item = item;
                r.Shares.Add(new LootReservation.Share { X = x, Y = y, Amount = 1 });
                MarkReserved(r, x, y, item.Width, item.Height);
                return r;
            }

            var left = item.Count;
            foreach (var p in _placed)
            {
                if (left == 0)
                    break;
                if (p.Item.Kind != ItemKind.Part)
                    continue;
                var take = Mathf.Min(left, SpaceLeft(p));
                if (take <= 0)
                    continue;
                p.Pending += take;
                r.Shares.Add(new LootReservation.Share { Target = p, Amount = take });
                left -= take;
            }
            while (left > 0 && FindFree(1, 1, out var x, out var y))
            {
                var take = Mathf.Min(left, ItemData.PartStack);
                r.Shares.Add(new LootReservation.Share { X = x, Y = y, Amount = take });
                MarkReserved(r, x, y, 1, 1);
                left -= take;
            }
            var reserved = item.Count - left;
            if (reserved == 0)
                return null;
            item.Count = left;
            r.Item = new ItemData(ItemKind.Part, item.Name, item.Rarity, reserved);
            return r;
        }

        /// <summary>LootReservation.Commit／Cancel 的實作（只會被呼叫一次）。</summary>
        internal void Settle(LootReservation r, bool commit)
        {
            var equipment = r.Item.IsEquipment;
            foreach (var s in r.Shares)
            {
                if (s.Target == null)
                    ReleaseReserved(r, s.X, s.Y, equipment ? r.Item.Width : 1, equipment ? r.Item.Height : 1);
                else
                    s.Target.Pending = Mathf.Max(0, s.Target.Pending - s.Amount);
            }
            if (!commit)
                return;
            foreach (var s in r.Shares)
            {
                if (equipment)
                {
                    CommitInto(r.Item, s.X, s.Y);
                    continue;
                }
                if (s.Target != null && _placed.Contains(s.Target))
                {
                    var moved = Mathf.Min(s.Amount, ItemData.PartStack - s.Target.Item.Count);
                    s.Target.Item.Count += moved;
                    if (moved < s.Amount)
                        CommitInto(new ItemData(ItemKind.Part, r.Item.Name, r.Item.Rarity, s.Amount - moved), -1, -1);
                }
                else
                {
                    var x = s.Target == null ? s.X : -1;
                    var y = s.Target == null ? s.Y : -1;
                    CommitInto(new ItemData(ItemKind.Part, r.Item.Name, r.Item.Rarity, s.Amount), x, y);
                }
            }
            RaiseChanged();
        }
        #endregion

        #region data 內部
        /// <summary>這個關卡 id 的敵人第一次被打倒時回 true（之後回 false：防刷）；id 空的一律回 true。</summary>
        internal bool MarkDropped(string spawnId)
        {
            if (string.IsNullOrEmpty(spawnId))
                return true;
            return _droppedIds.Add(spawnId);
        }

        /// <summary>道具在格子盤或裝備欄上（強化前檢查）。</summary>
        internal bool Contains(ItemData item) => item != null && (FindPlaced(item) != null || IsEquipped(item));

        internal void NotifyChanged() => RaiseChanged();

        /// <summary>給 Enhance 用：不檢查、不觸發 Changed（呼叫端已檢查 PartsTotal）。從數量少的堆先扣。</summary>
        internal void SpendPartsInternal(int amount)
        {
            while (amount > 0)
            {
                Placed smallest = null;
                foreach (var p in _placed)
                {
                    if (p.Item.Kind == ItemKind.Part && (smallest == null || p.Item.Count < smallest.Item.Count))
                        smallest = p;
                }
                if (smallest == null)
                    return;
                var take = Mathf.Min(amount, smallest.Item.Count);
                smallest.Item.Count -= take;
                amount -= take;
                if (smallest.Item.Count == 0)
                    RemoveInternal(smallest.Item);
            }
        }
        #endregion

        #region 內部
        private static bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Columns && y < Rows;
        private static int Index(int x, int y) => y * Columns + x;

        private static bool IsMergeTarget(ItemData hand, ItemData other) =>
            hand.Kind == ItemKind.Part && other.Kind == ItemKind.Part && other.Count < ItemData.PartStack;

        private static int SpaceLeft(Placed p) => p == null ? 0 : Mathf.Max(0, ItemData.PartStack - p.Item.Count - p.Pending);

        private bool IsEquipped(ItemData item)
        {
            if (item == null)
                return false;
            foreach (var e in _equipped)
            {
                if (e == item)
                    return true;
            }
            return false;
        }

        private Placed FindPlaced(ItemData item)
        {
            if (item == null)
                return null;
            foreach (var p in _placed)
            {
                if (p.Item == item)
                    return p;
            }
            return null;
        }

        private bool AddAnywhereInternal(ItemData item)
        {
            if (item == null || item.Count <= 0 || FindPlaced(item) != null || IsEquipped(item))
                return false;
            if (item.IsEquipment)
            {
                if (!FindFree(item.Width, item.Height, out var x, out var y))
                    return false;
                PutInternal(item, x, y);
                return true;
            }
            if (PartCapacity() < item.Count)
                return false;
            foreach (var p in _placed)
            {
                if (item.Count == 0)
                    break;
                if (p.Item.Kind != ItemKind.Part)
                    continue;
                var moved = Mathf.Min(item.Count, SpaceLeft(p));
                p.Item.Count += moved;
                item.Count -= moved;
            }
            if (item.Count > 0 && FindFree(1, 1, out var px, out var py))
                PutInternal(item, px, py);
            return true;
        }

        /// <summary>零件還能放多少（既有堆的空間＋空格 ×20）。</summary>
        private int PartCapacity()
        {
            var total = 0;
            foreach (var p in _placed)
            {
                if (p.Item.Kind == ItemKind.Part)
                    total += SpaceLeft(p);
            }
            for (var i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == null && _reserved[i] == null)
                    total += ItemData.PartStack;
            }
            return total;
        }

        /// <summary>找 w×h 的空位（沒有道具也沒有預留）：由左而右一欄一欄找，同一欄由上而下。</summary>
        private bool FindFree(int w, int h, out int x, out int y)
        {
            for (x = 0; x + w <= Columns; x++)
            {
                for (y = 0; y + h <= Rows; y++)
                {
                    if (IsFree(x, y, w, h))
                        return true;
                }
            }
            x = y = -1;
            return false;
        }

        private bool IsFree(int x, int y, int w, int h)
        {
            if (x < 0 || y < 0 || x + w > Columns || y + h > Rows)
                return false;
            for (var cy = y; cy < y + h; cy++)
            {
                for (var cx = x; cx < x + w; cx++)
                {
                    var i = Index(cx, cy);
                    if (_cells[i] != null || _reserved[i] != null)
                        return false;
                }
            }
            return true;
        }

        private void PutInternal(ItemData item, int x, int y)
        {
            _placed.Add(new Placed { Item = item, X = x, Y = y });
            for (var cy = y; cy < y + item.Height; cy++)
            {
                for (var cx = x; cx < x + item.Width; cx++)
                    _cells[Index(cx, cy)] = item;
            }
        }

        private bool RemoveInternal(ItemData item)
        {
            var p = FindPlaced(item);
            if (p == null)
                return false;
            _placed.Remove(p);
            for (var i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == item)
                    _cells[i] = null;
            }
            return true;
        }

        private void MarkReserved(LootReservation r, int x, int y, int w, int h)
        {
            for (var cy = y; cy < y + h; cy++)
            {
                for (var cx = x; cx < x + w; cx++)
                    _reserved[Index(cx, cy)] = r;
            }
        }

        private void ReleaseReserved(LootReservation r, int x, int y, int w, int h)
        {
            for (var cy = y; cy < y + h; cy++)
            {
                for (var cx = x; cx < x + w; cx++)
                {
                    var i = Index(cx, cy);
                    if (_reserved[i] == r)
                        _reserved[i] = null;
                }
            }
        }

        /// <summary>Commit 時放進格子盤：預留的位置空著就放那裡，否則找別的位置；都不行記錯誤（正常不會發生，預留格別人放不進去）。</summary>
        private void CommitInto(ItemData item, int x, int y)
        {
            if (x >= 0 && IsFree(x, y, item.Width, item.Height))
            {
                PutInternal(item, x, y);
                return;
            }
            var count = item.Count;
            if (!AddAnywhereInternal(item))
                Debug.LogError($"{Log} 預留的 {item.Name}（{count}）放不進背包，遺失");
        }

        /// <summary>維持不變式：Loadout 的攻擊旗標＝裝備欄有沒有東西。</summary>
        private void SyncLoadout()
        {
            if (_loadout == null)
                return;
            _loadout.SetAttackFromGear(AttackId.Melee, _equipped[(int)EquipSlot.Melee] != null);
            _loadout.SetAttackFromGear(AttackId.Sling, _equipped[(int)EquipSlot.Ranged] != null);
            _loadout.SetAttackFromGear(AttackId.Roll, _equipped[(int)EquipSlot.Shell] != null);
        }

        /// <summary>逐一呼叫處理器：前面的丟例外不影響後面的（主角的數值重算一定收得到）。</summary>
        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null)
                return;
            foreach (var d in handler.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{Log} Changed 處理器丟例外：{ex}");
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// 一次拾取的預留。<see cref="Commit"/>＝真的放進背包（觸發 Changed）；<see cref="Cancel"/>＝放棄、釋放格子。
    /// 兩者都只有第一次有效（之後呼叫無作用）。pickup 在飛行抵達、暫停（PausedChanged true）、物件被刪（重生）時 Commit，
    /// 所以背包介面打開時不會有未完成的預留。
    /// </summary>
    public sealed class LootReservation
    {
        /// <summary>預留的一份：Target＝疊到既有零件堆（null＝佔新格 (X,Y)）；Amount＝零件數（裝備為 1）。</summary>
        internal sealed class Share
        {
            public LootState.Placed Target;
            public int X, Y;
            public int Amount;
        }

        public ItemData Item { get; internal set; }
        public bool Settled { get; internal set; }

        internal LootState Owner;
        internal readonly List<Share> Shares = new List<Share>();

        public void Commit() => Settle(true);
        public void Cancel() => Settle(false);

        private void Settle(bool commit)
        {
            if (Settled)
                return;
            Settled = true;
            Owner?.Settle(this, commit);
        }
    }
}
