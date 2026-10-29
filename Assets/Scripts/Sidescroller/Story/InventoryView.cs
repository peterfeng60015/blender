using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 背包介面（loot-design.md §5）：左邊 3 個裝備欄、右邊 10×4 格子盤、下面零件總數。由 <see cref="LootScreens"/> 開關與每幀 <see cref="Tick"/>。
    /// 操作：
    /// - 滑鼠／手指：點起道具 → 點格子放下（壓到 1 件＝交換）；按住拖曳也可以。拖或點到裝備欄＝裝上（換下的那件回到手上）；
    ///   拖或點到面板外＝丟到地上（先確認，<see cref="LootDrops.DropNearHero"/>）。
    /// - 鍵盤／手把：方向鍵移動游標，J（Space、Enter、手把 South）拿起／放下，E（手把 East）直接裝上（裝備欄上按＝卸下放進背包），
    ///   Esc／I（手把 Start／Select）關閉。
    /// 「手上的道具」只存在這裡（不在 LootState）；關閉時先放回原位，不行就 AddAnywhere，再不行丟到地上。
    /// </summary>
    internal sealed class InventoryView
    {
        #region 版面常數（1920×1080 畫布單位）
        private const float Pad = 30f;
        private const float Gap = 28f;
        private const float SlotGap = 14f;
        private const float FooterH = 60f;
        private const float HelpH = 50f;
        /// <summary>裝備欄的寬度＝幾格。</summary>
        private const float EquipCols = 1.8f;
        private const float KeyCell = 100f;
        private const float TouchCellMm = 9f, TouchCellMin = 120f, TouchCellMax = 140f;
        private const float MinCell = 60f;
        private const float KeyButtonH = 64f;
        private const float TouchButtonMm = 9f, TouchButtonMin = 110f, TouchButtonMax = 140f;
        /// <summary>按住移動超過這個距離（mm；dpi 不可信時用畫布單位）才算拖曳。</summary>
        private const float DragMm = 2.5f, DragFallback = 28f;
        #endregion

        private const int Columns = LootState.Columns, Rows = LootState.Rows;
        private const float BadFlashTime = 0.25f;

        private static readonly Color PanelColor = new Color(0.06f, 0.04f, 0.1f, 0.95f);
        private static readonly Color CellColor = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color SlotColor = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Color CursorColor = new Color(0.18f, 0.9f, 0.85f, 0.35f);
        private static readonly Color OkColor = new Color(0.3f, 0.95f, 0.45f, 0.35f);
        private static readonly Color BadColor = new Color(0.95f, 0.25f, 0.2f, 0.45f);
        private static readonly string[] SlotNames = { StoryTexts.SlotMelee, StoryTexts.SlotRanged, StoryTexts.SlotShell };

        private readonly GameSession _session;
        private readonly LootState _loot;
        private readonly TMP_FontAsset _font;
        private readonly LootPointer _pointer;

        #region 畫面物件
        private readonly RectTransform _root;
        private readonly RectTransform _panel;
        private readonly TextMeshProUGUI _title;
        private readonly RectTransform _grid;
        private readonly RectTransform[] _cells = new RectTransform[Columns * Rows];
        private readonly RectTransform _itemLayer;
        private readonly List<ItemWidget> _widgets = new List<ItemWidget>();
        private readonly RectTransform[] _slots = new RectTransform[3];
        private readonly TextMeshProUGUI[] _slotLabels = new TextMeshProUGUI[3];
        private readonly ItemWidget[] _slotWidgets = new ItemWidget[3];
        private readonly RectTransform _cursor;
        private readonly Image _cursorImage;
        private readonly TextMeshProUGUI _parts;
        private readonly TextMeshProUGUI _help;
        private readonly LootButton _close;
        private readonly ItemWidget _heldWidget;
        private readonly LootTooltip _tooltip;

        private readonly RectTransform _confirm;
        private readonly TextMeshProUGUI _confirmText;
        private readonly LootButton _confirmYes;
        private readonly LootButton _confirmNo;
        #endregion

        #region 狀態
        private readonly List<ItemData> _items = new List<ItemData>();
        private bool _dirty = true;
        private float _cell = -1f;
        private Vector2 _layoutSize;
        private bool _layoutTouch;
        private int _openedFrame = -1;
        private bool _wantClose;

        /// <summary>游標：_curSlot ≥ 0＝在裝備欄，否則在格子 (_curX, _curY)。</summary>
        private int _curSlot = -1;
        private int _curX;
        private int _curY;
        /// <summary>最後一次操作是滑鼠／觸控（手上的道具跟著指標畫）還是鍵盤（畫在游標上）。</summary>
        private bool _pointerMode;
        private bool _pointerOverGrid;

        private ItemData _held;
        /// <summary>手上道具的原位（關閉時放回）：格子盤左上角，或裝備欄；都 -1＝沒有原位。</summary>
        private int _heldX = -1, _heldY = -1, _heldSlot = -1;
        /// <summary>用指標從格子盤拿起時，指標在道具裡的哪一格（之後放下時那一格對準指標）；-1＝道具中心對準指標。</summary>
        private int _grabX = -1, _grabY = -1;

        /// <summary>按下時拿起了道具：放開前移動夠遠就算拖曳，放開的位置就是放下的位置。</summary>
        private bool _dragArmed;
        private bool _dragging;
        private Vector2 _pressPos;

        private bool _confirmOpen;
        private int _confirmOpenedFrame = -1;
        private int _confirmSel;

        private float _badUntil = -1f;
        #endregion

        public InventoryView(Transform parent, TMP_FontAsset font, GameSession session, LootPointer pointer)
        {
            _session = session;
            _loot = session.Loot;
            _font = font;
            _pointer = pointer;

            _root = SsUiKit.Fill("Inventory", parent);
            SsUiKit.AddImage(_root, new Color(0.02f, 0.01f, 0.05f, 0.55f));
            _panel = SsUiKit.Rect("Panel", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            SsUiKit.AddImage(_panel, PanelColor);

            var titleRt = SsUiKit.Rect("Title", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            titleRt.anchoredPosition = new Vector2(Pad, -Pad + 6f);
            titleRt.sizeDelta = new Vector2(400f, 70f);
            _title = SsUiKit.AddText(titleRt, font, 52f, SsUiKit.Gold, TextAlignmentOptions.Left);
            _title.text = StoryTexts.BagTitle;

            for (var i = 0; i < 3; i++)
            {
                _slots[i] = SsUiKit.Rect($"Slot{i}", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                SsUiKit.AddImage(_slots[i], SlotColor);
                var labelRt = SsUiKit.Fill("Label", _slots[i]);
                labelRt.offsetMin = new Vector2(10f, 6f);
                labelRt.offsetMax = new Vector2(-8f, -6f);
                _slotLabels[i] = SsUiKit.AddText(labelRt, font, 26f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.TopLeft);
                _slotLabels[i].text = SlotNames[i];
                _slotWidgets[i] = new ItemWidget("Equipped", _slots[i], font);
                _slotWidgets[i].Visible = false;
            }

            _grid = SsUiKit.Rect("Grid", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            for (var y = 0; y < Rows; y++)
            {
                for (var x = 0; x < Columns; x++)
                {
                    var cell = SsUiKit.Rect($"Cell{x}_{y}", _grid, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                    SsUiKit.AddImage(cell, CellColor);
                    _cells[y * Columns + x] = cell;
                }
            }
            _itemLayer = SsUiKit.Fill("Items", _grid);

            _cursor = SsUiKit.Rect("Cursor", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _cursorImage = SsUiKit.AddImage(_cursor, CursorColor);

            var partsRt = SsUiKit.Rect("Parts", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _parts = SsUiKit.AddText(partsRt, font, 38f, SsUiKit.Paper, TextAlignmentOptions.Left);
            var helpRt = SsUiKit.Rect("Help", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _help = SsUiKit.AddText(helpRt, font, 28f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left);
            _help.textWrappingMode = TextWrappingModes.NoWrap;
            _close = new LootButton("Close", _panel, font, StoryTexts.Close, 38f);

            _heldWidget = new ItemWidget("Held", _root, font);
            _heldWidget.Visible = false;
            _tooltip = new LootTooltip(_root, font);

            #region 丟掉的確認框
            _confirm = SsUiKit.Fill("Confirm", _root);
            SsUiKit.AddImage(_confirm, new Color(0f, 0f, 0f, 0.55f));
            var box = SsUiKit.Rect("Box", _confirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            box.sizeDelta = new Vector2(900f, 340f);
            SsUiKit.AddImage(box, new Color(0.08f, 0.05f, 0.13f, 0.98f));
            var textRt = SsUiKit.Rect("Text", box, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            textRt.anchoredPosition = new Vector2(0f, -40f);
            textRt.sizeDelta = new Vector2(-80f, 120f);
            _confirmText = SsUiKit.AddText(textRt, font, 44f, SsUiKit.Paper);
            _confirmYes = new LootButton("Yes", box, font, StoryTexts.DropYes, 42f);
            _confirmNo = new LootButton("No", box, font, StoryTexts.DropNo, 42f);
            _confirm.gameObject.SetActive(false);
            #endregion

            _loot.Changed += OnLootChanged;
            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public void Dispose() => _loot.Changed -= OnLootChanged;

        private void OnLootChanged() => _dirty = true;

        #region 開關
        public void Open()
        {
            _root.gameObject.SetActive(true);
            _openedFrame = Time.frameCount;
            _wantClose = false;
            _dirty = true;
            _held = null;
            _heldX = _heldY = _heldSlot = -1;
            _dragArmed = _dragging = false;
            _confirmOpen = false;
            _confirm.gameObject.SetActive(false);
            _pointerMode = SsInput.TouchActive;
            _pointerOverGrid = false;
            _curSlot = -1;
            _curX = Mathf.Clamp(_curX, 0, Columns - 1);
            _curY = Mathf.Clamp(_curY, 0, Rows - 1);
            Layout(true);
            Refresh();
            Draw();
        }

        /// <summary>收起：手上的道具放回原位 → AddAnywhere → 丟到地上。</summary>
        public void Close()
        {
            if (!IsOpen)
                return;
            ReturnHeld();
            if (_held != null)
            {
                var item = _held;
                _held = null;
                if (!LootDrops.DropNearHero(_session, item))
                    Debug.LogError($"[Sidescroller.InventoryView] 關背包時「{item.DisplayName}」放不回背包也丟不到地上，道具遺失");
            }
            _confirmOpen = false;
            _confirm.gameObject.SetActive(false);
            _tooltip.Hide();
            _root.gameObject.SetActive(false);
        }
        #endregion

        #region 每幀
        /// <summary>每幀呼叫（開著時）；回 false＝要求關閉（呼叫端呼叫 <see cref="Close"/>）。</summary>
        public bool Tick()
        {
            Layout(false);
            if (_dirty)
                Refresh();
            if (Time.frameCount == _openedFrame)
            {
                Draw();
                return true;
            }
            if (_confirmOpen)
            {
                TickConfirm();
                Draw();
                return true;
            }
            if (SsInput.PausePressed || SsInput.BagPressed)
                return false;
            HandlePointer();
            if (_wantClose)
                return false;
            HandleKeys();
            if (_dirty)
                Refresh();
            Draw();
            return true;
        }

        private void HandlePointer()
        {
            var p = _pointer;
            if (p.Moved)
            {
                _pointerMode = true;
                Hover(p.Down ? p.DownPos : p.Pos);
            }
            if (p.Down)
            {
                var pos = p.DownPos;
                if (_close.Contains(pos))
                {
                    _wantClose = true;
                    return;
                }
                if (_held == null)
                {
                    if (PickAtScreen(pos))
                    {
                        _dragArmed = true;
                        _dragging = false;
                        _pressPos = pos;
                    }
                }
                else
                {
                    DropAtScreen(pos);
                    _dragArmed = false;
                }
            }
            if (!_dragArmed)
                return;
            if (p.Held && (p.Pos - _pressPos).magnitude > DragDistancePx())
                _dragging = true;
            if (p.Released || !p.Held)
            {
                if (_dragging && _held != null)
                    DropAtScreen(p.Pos);
                _dragArmed = _dragging = false;
            }
        }

        private void HandleKeys()
        {
            var dx = SsInput.MenuLeftPressed ? -1 : SsInput.MenuRightPressed ? 1 : 0;
            var dy = SsInput.MenuUpPressed ? -1 : SsInput.MenuDownPressed ? 1 : 0;
            if (dx != 0 || dy != 0)
            {
                _pointerMode = false;
                MoveCursor(dx, dy);
            }
            if (SsInput.ConfirmPressed)
            {
                _pointerMode = false;
                if (_held == null)
                    PickAtCursor();
                else if (_curSlot >= 0)
                    EquipHeld(_curSlot);
                else
                    PlaceHeld(_curX, _curY);
            }
            else if (SsInput.InteractPressed)
            {
                QuickEquip();
            }
        }
        #endregion

        #region 操作
        private bool PickAtScreen(Vector2 pos)
        {
            if (GridCell(pos, out var fx, out var fy))
            {
                _curSlot = -1;
                var cx = Mathf.FloorToInt(fx);
                var cy = Mathf.FloorToInt(fy);
                _curX = cx;
                _curY = cy;
                if (!PickAtCursor())
                    return false;
                // PickAtCursor 把游標移到道具左上角；記下抓的是哪一格，原地再點一下會放回原位
                _grabX = cx - _curX;
                _grabY = cy - _curY;
                return true;
            }
            var slot = SlotAt(pos);
            if (slot < 0)
                return false;
            _curSlot = slot;
            return PickAtCursor();
        }

        private bool PickAtCursor()
        {
            if (_curSlot >= 0)
            {
                var item = _loot.Unequip((EquipSlot)_curSlot);
                if (item == null)
                    return false;
                SetHeld(item, -1, -1, _curSlot);
                return true;
            }
            var at = _loot.GetAt(_curX, _curY);
            if (at == null || !_loot.TryGetOrigin(at, out var ox, out var oy) || !_loot.Remove(at))
                return false;
            SetHeld(at, ox, oy, -1);
            // 鍵盤拿起多格道具：游標移到它的左上角，放下時位置不變
            _curX = ox;
            _curY = oy;
            return true;
        }

        /// <summary>手上有道具時點在 pos：格子盤＝放下（交換）、裝備欄＝裝上、面板外＝丟到地上（確認）。</summary>
        private void DropAtScreen(Vector2 pos)
        {
            if (GridCell(pos, out var fx, out var fy))
            {
                HeldOrigin(fx, fy, out var ox, out var oy);
                _curSlot = -1;
                _curX = ox;
                _curY = oy;
                PlaceHeld(ox, oy);
                return;
            }
            var slot = SlotAt(pos);
            if (slot >= 0)
            {
                _curSlot = slot;
                EquipHeld(slot);
                return;
            }
            if (!RectTransformUtility.RectangleContainsScreenPoint(_panel, pos, null))
                OpenConfirm();
        }

        private void PlaceHeld(int x, int y)
        {
            if (_held == null)
                return;
            x = Mathf.Clamp(x, 0, Columns - _held.Width);
            y = Mathf.Clamp(y, 0, Rows - _held.Height);
            if (!_loot.CanPlace(_held, x, y, out _))
            {
                Bad();
                return;
            }
            var rest = _loot.Place(_held, x, y);
            // 換出來的那件（或零件合併後的餘數）沒有原位，關閉時找空位
            SetHeld(rest, -1, -1, -1);
        }

        private void EquipHeld(int slot)
        {
            if (_held == null || !_held.IsEquipment || (int)_held.Slot != slot)
            {
                Bad();
                return;
            }
            var previous = _loot.Equip(_held);
            SetHeld(previous, -1, -1, -1);
        }

        /// <summary>E：手上有裝備＝裝上；游標在背包的裝備上＝直接裝上；游標在裝備欄＝卸下放進背包。</summary>
        private void QuickEquip()
        {
            if (_held != null)
            {
                if (_held.IsEquipment)
                {
                    _curSlot = (int)_held.Slot;
                    EquipHeld(_curSlot);
                }
                else
                {
                    Bad();
                }
                return;
            }
            if (_curSlot >= 0)
            {
                var item = _loot.Unequip((EquipSlot)_curSlot);
                if (item == null)
                    return;
                if (!_loot.AddAnywhere(item))
                {
                    _loot.Equip(item);
                    Bad();
                }
                return;
            }
            var at = _loot.GetAt(_curX, _curY);
            if (at == null || !at.IsEquipment || !_loot.TryEquipFromBag(at))
                Bad();
        }

        /// <summary>手上的道具放回原位；不行就 AddAnywhere。都不行時 _held 不變。</summary>
        private void ReturnHeld()
        {
            if (_held == null)
                return;
            if (_heldSlot >= 0 && _held.IsEquipment && (int)_held.Slot == _heldSlot && _loot.GetEquipped((EquipSlot)_heldSlot) == null)
            {
                _loot.Equip(_held);
                SetHeld(null, -1, -1, -1);
                return;
            }
            if (_heldX >= 0 && _loot.CanPlace(_held, _heldX, _heldY, out var other) && other == null)
            {
                _loot.Place(_held, _heldX, _heldY);
                SetHeld(null, -1, -1, -1);
                return;
            }
            if (_loot.AddAnywhere(_held))
                SetHeld(null, -1, -1, -1);
        }

        private void SetHeld(ItemData item, int x, int y, int slot)
        {
            _held = item;
            _heldX = x;
            _heldY = y;
            _heldSlot = slot;
            _grabX = _grabY = -1;
            _dirty = true;
            // 換出來的道具尺寸可能不同：游標（手上道具的左上角）重新限制在格子盤內
            if (_curSlot < 0)
                ClampCursor();
        }

        private void Bad() => _badUntil = Time.unscaledTime + BadFlashTime;
        #endregion

        #region 丟掉確認
        private void OpenConfirm()
        {
            if (_held == null)
                return;
            _confirmOpen = true;
            _confirmOpenedFrame = Time.frameCount;
            _confirmSel = 1;    // 預設「取消」
            _confirmText.text = StoryTexts.DropAskPrefix + _held.DisplayName + StoryTexts.DropAskSuffix;
            _confirm.gameObject.SetActive(true);
            _dragArmed = _dragging = false;
        }

        private void TickConfirm()
        {
            LayoutConfirm();
            if (Time.frameCount == _confirmOpenedFrame)
                return;
            if (SsInput.PausePressed)
            {
                CloseConfirm(false);
                return;
            }
            if (SsInput.MenuLeftPressed || SsInput.MenuRightPressed)
                _confirmSel = 1 - _confirmSel;
            if (_pointer.Down)
            {
                if (_confirmYes.Contains(_pointer.DownPos))
                {
                    CloseConfirm(true);
                    return;
                }
                if (_confirmNo.Contains(_pointer.DownPos))
                {
                    CloseConfirm(false);
                    return;
                }
            }
            if (SsInput.ConfirmPressed)
            {
                CloseConfirm(_confirmSel == 0);
                return;
            }
            _confirmYes.SetState(_confirmSel == 0);
            _confirmNo.SetState(_confirmSel == 1);
        }

        private void CloseConfirm(bool drop)
        {
            _confirmOpen = false;
            _confirm.gameObject.SetActive(false);
            if (!drop || _held == null)
                return;
            var item = _held;
            if (LootDrops.DropNearHero(_session, item))
            {
                Debug.Log($"[Sidescroller.InventoryView] 丟到地上：{item.DisplayName}");
                SetHeld(null, -1, -1, -1);
                return;
            }
            // 找不到主角：收回背包（放不下就留在手上）
            ReturnHeld();
        }

        private void LayoutConfirm()
        {
            var h = ButtonHeight();
            _confirmYes.Place(new Vector2(110f, -340f + 40f + h), new Vector2(300f, h));
            _confirmNo.Place(new Vector2(490f, -340f + 40f + h), new Vector2(300f, h));
            _confirmYes.SetState(_confirmSel == 0);
            _confirmNo.SetState(_confirmSel == 1);
        }
        #endregion

        #region 游標
        /// <summary>方向鍵：在格子盤上一格一格走（停在道具上時直接跳過整件），格子盤最左再往左＝裝備欄。</summary>
        private void MoveCursor(int dx, int dy)
        {
            if (_curSlot >= 0)
            {
                if (dy != 0)
                    _curSlot = Mathf.Clamp(_curSlot + dy, 0, 2);
                if (dx > 0)
                {
                    _curY = _curSlot == 0 ? 0 : _curSlot == 1 ? 1 : Rows - 1;
                    _curX = 0;
                    _curSlot = -1;
                    ClampCursor();
                }
                return;
            }
            var x = _curX + dx;
            var y = _curY + dy;
            if (_held == null)
            {
                var at = _loot.GetAt(_curX, _curY);
                if (at != null && _loot.TryGetOrigin(at, out var ox, out var oy))
                {
                    if (dx > 0) x = ox + at.Width;
                    else if (dx < 0) x = ox - 1;
                    if (dy > 0) y = oy + at.Height;
                    else if (dy < 0) y = oy - 1;
                }
            }
            if (x < 0)
            {
                _curSlot = _curY <= 0 ? 0 : _curY <= 2 ? 1 : 2;
                return;
            }
            _curX = x;
            _curY = y;
            ClampCursor();
        }

        private void ClampCursor()
        {
            var w = _held != null ? _held.Width : 1;
            var h = _held != null ? _held.Height : 1;
            _curX = Mathf.Clamp(_curX, 0, Columns - w);
            _curY = Mathf.Clamp(_curY, 0, Rows - h);
        }

        /// <summary>滑鼠移動／手指按著：游標跟著指標（手上有道具時是它的左上角）。</summary>
        private void Hover(Vector2 pos)
        {
            _pointerOverGrid = false;
            if (GridCell(pos, out var fx, out var fy))
            {
                _pointerOverGrid = true;
                _curSlot = -1;
                if (_held != null)
                {
                    HeldOrigin(fx, fy, out _curX, out _curY);
                }
                else
                {
                    _curX = Mathf.FloorToInt(fx);
                    _curY = Mathf.FloorToInt(fy);
                }
                return;
            }
            var slot = SlotAt(pos);
            if (slot >= 0)
            {
                _curSlot = slot;
                _pointerOverGrid = true;
            }
        }

        /// <summary>指標在格子 (fx, fy)（小數）時，手上道具的左上角：道具中心對準指標，限制在格子盤內。</summary>
        private void HeldOrigin(float fx, float fy, out int x, out int y)
        {
            var w = _held != null ? _held.Width : 1;
            var h = _held != null ? _held.Height : 1;
            if (_grabX >= 0)
            {
                x = Mathf.FloorToInt(fx) - _grabX;
                y = Mathf.FloorToInt(fy) - _grabY;
            }
            else
            {
                x = Mathf.RoundToInt(fx - w / 2f);
                y = Mathf.RoundToInt(fy - h / 2f);
            }
            x = Mathf.Clamp(x, 0, Columns - w);
            y = Mathf.Clamp(y, 0, Rows - h);
        }

        /// <summary>螢幕座標在格子盤上的位置（格為單位，x 往右、y 往下）；不在格子盤上回 false。</summary>
        private bool GridCell(Vector2 screenPos, out float fx, out float fy)
        {
            fx = fy = -1f;
            if (_cell <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_grid, screenPos, null, out var local))
                return false;
            fx = local.x / _cell;
            fy = -local.y / _cell;
            return fx >= 0f && fx < Columns && fy >= 0f && fy < Rows;
        }

        private int SlotAt(Vector2 screenPos)
        {
            for (var i = 0; i < 3; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_slots[i], screenPos, null))
                    return i;
            }
            return -1;
        }

        private float DragDistancePx()
        {
            var units = SsUiKit.MmToUnits(DragMm);
            return (units > 0f ? units : DragFallback) * SsUiKit.UnitsToPx();
        }
        #endregion

        #region 版面與畫面
        private float ButtonHeight() =>
            SsInput.TouchActive ? Mathf.Clamp(Mathf.Max(SsUiKit.MmToUnits(TouchButtonMm), TouchButtonMin), TouchButtonMin, TouchButtonMax) : KeyButtonH;

        /// <summary>依安全區域大小與輸入模式決定格子大小（觸控約 9mm）；有變才重排。</summary>
        private void Layout(bool force)
        {
            SsUiKit.FitSafeArea(_root);
            var size = _root.rect.size;
            var touch = SsInput.TouchActive;
            if (!force && size == _layoutSize && touch == _layoutTouch && _cell > 0f)
                return;
            _layoutSize = size;
            _layoutTouch = touch;

            var buttonH = ButtonHeight();
            var titleH = Mathf.Max(70f, buttonH + 8f);
            var target = touch ? Mathf.Clamp(Mathf.Max(SsUiKit.MmToUnits(TouchCellMm), TouchCellMin), KeyCell, TouchCellMax) : KeyCell;
            var fixedW = Pad * 2f + Gap;
            var fixedH = Pad * 2f + titleH + FooterH + HelpH + 12f;
            var cell = Mathf.Min(target, (size.x - 24f - fixedW) / (Columns + EquipCols), (size.y - 16f - fixedH) / Rows);
            _cell = Mathf.Max(MinCell, Mathf.Floor(cell));

            var panelW = fixedW + (Columns + EquipCols) * _cell;
            var panelH = fixedH + Rows * _cell;
            _panel.sizeDelta = new Vector2(panelW, panelH);

            var top = -(Pad + titleH);
            var slotW = EquipCols * _cell;
            var slotH = (Rows * _cell - SlotGap * 2f) / 3f;
            for (var i = 0; i < 3; i++)
            {
                _slots[i].anchoredPosition = new Vector2(Pad, top - i * (slotH + SlotGap));
                _slots[i].sizeDelta = new Vector2(slotW, slotH);
            }
            var gridX = Pad + slotW + Gap;
            _grid.anchoredPosition = new Vector2(gridX, top);
            _grid.sizeDelta = new Vector2(Columns * _cell, Rows * _cell);
            for (var y = 0; y < Rows; y++)
            {
                for (var x = 0; x < Columns; x++)
                {
                    var c = _cells[y * Columns + x];
                    c.anchoredPosition = new Vector2(x * _cell + 2f, -(y * _cell + 2f));
                    c.sizeDelta = new Vector2(_cell - 4f, _cell - 4f);
                }
            }
            var footerTop = top - Rows * _cell - 12f;
            _parts.rectTransform.anchoredPosition = new Vector2(gridX, footerTop);
            _parts.rectTransform.sizeDelta = new Vector2(Columns * _cell, FooterH);
            _help.rectTransform.anchoredPosition = new Vector2(Pad, footerTop - FooterH);
            _help.rectTransform.sizeDelta = new Vector2(panelW - Pad * 2f, HelpH);
            _help.text = touch ? StoryTexts.BagHelpTouch : StoryTexts.BagHelp;
            _help.enableAutoSizing = true;
            _help.fontSizeMax = 28f;
            _help.fontSizeMin = 18f;
            _title.rectTransform.sizeDelta = new Vector2(400f, titleH - 8f);
            _close.Place(new Vector2(panelW - Pad - 200f, -Pad + 4f), new Vector2(200f, buttonH));
            _dirty = true;
        }

        /// <summary>依 LootState 重畫格子盤與裝備欄的道具、零件總數。</summary>
        private void Refresh()
        {
            _dirty = false;
            _items.Clear();
            _loot.GetItems(_items);
            var n = 0;
            foreach (var item in _items)
            {
                if (!_loot.TryGetOrigin(item, out var ox, out var oy))
                    continue;
                if (n >= _widgets.Count)
                    _widgets.Add(new ItemWidget($"Item{n}", _itemLayer, _font));
                var w = _widgets[n++];
                w.Set(item, _cell);
                w.Rt.anchoredPosition = new Vector2(ox * _cell + 2f, -(oy * _cell + 2f));
                w.Rt.sizeDelta = new Vector2(item.Width * _cell - 4f, item.Height * _cell - 4f);
            }
            for (var i = n; i < _widgets.Count; i++)
                _widgets[i].Set(null, _cell);

            for (var i = 0; i < 3; i++)
            {
                var item = _loot.GetEquipped((EquipSlot)i);
                var widget = _slotWidgets[i];
                widget.Set(item, _cell);
                _slotLabels[i].gameObject.SetActive(item == null);
                if (item == null)
                    continue;
                // 照佔格比例縮進裝備欄（四邊留 8）
                var area = _slots[i].rect.size - new Vector2(16f, 16f);
                var scale = Mathf.Min(area.x / item.Width, area.y / item.Height);
                widget.Rt.anchorMin = widget.Rt.anchorMax = new Vector2(0.5f, 0.5f);
                widget.Rt.pivot = new Vector2(0.5f, 0.5f);
                widget.Rt.anchoredPosition = Vector2.zero;
                widget.Rt.sizeDelta = new Vector2(item.Width * scale, item.Height * scale);
            }
            _parts.text = StoryTexts.BagParts + _loot.PartsTotal;
        }

        /// <summary>游標、手上的道具、說明框。</summary>
        private void Draw()
        {
            var bad = Time.unscaledTime < _badUntil;
            var showCursor = !_pointerMode || _pointerOverGrid || _held != null;
            RectTransform anchorRt = null;

            if (_curSlot >= 0)
            {
                CursorTo(LootTooltip.LocalRect(_panel, _slots[_curSlot]), _held != null && !CanEquip(_curSlot) || bad ? BadColor : CursorColor);
                anchorRt = _slots[_curSlot];
            }
            else if (_held != null)
            {
                var ok = _loot.CanPlace(_held, Mathf.Clamp(_curX, 0, Columns - _held.Width), Mathf.Clamp(_curY, 0, Rows - _held.Height), out _);
                CursorToCells(_curX, _curY, _held.Width, _held.Height, ok && !bad ? OkColor : BadColor);
            }
            else
            {
                var at = _loot.GetAt(_curX, _curY);
                if (at != null && _loot.TryGetOrigin(at, out var ox, out var oy))
                    CursorToCells(ox, oy, at.Width, at.Height, bad ? BadColor : CursorColor);
                else
                    CursorToCells(_curX, _curY, 1, 1, bad ? BadColor : CursorColor);
            }
            SetActive(_cursor, showCursor && (_held == null || _curSlot >= 0 || !_pointerMode || _pointerOverGrid));

            #region 手上的道具
            _heldWidget.Set(_held, _cell);
            if (_held != null)
            {
                var size = new Vector2(_held.Width * _cell - 4f, _held.Height * _cell - 4f);
                _heldWidget.Rt.anchorMin = _heldWidget.Rt.anchorMax = new Vector2(0.5f, 0.5f);
                _heldWidget.Rt.pivot = new Vector2(0.5f, 0.5f);
                _heldWidget.Rt.sizeDelta = size;
                Vector2 center;
                if (_pointerMode && RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, _pointer.Pos, null, out var local))
                {
                    // 抓的那一格對準指標
                    center = _grabX < 0
                        ? local
                        : local + new Vector2((_held.Width / 2f - _grabX - 0.5f) * _cell, -(_held.Height / 2f - _grabY - 0.5f) * _cell);
                }
                else
                {
                    var r = _curSlot >= 0 ? LootTooltip.LocalRect(_root, _slots[_curSlot]) : LootTooltip.LocalRect(_root, _cursor);
                    center = r.center;
                }
                _heldWidget.Rt.anchoredPosition = center;
                _heldWidget.Rt.SetAsLastSibling();
                anchorRt = _heldWidget.Rt;
            }
            #endregion

            #region 說明框
            var target = _held;
            string header = null;
            if (target != null)
            {
                header = StoryTexts.TooltipHeld;
            }
            else if (showCursor)
            {
                target = _curSlot >= 0 ? _loot.GetEquipped((EquipSlot)_curSlot) : _loot.GetAt(_curX, _curY);
                if (target != null && anchorRt == null)
                    anchorRt = _cursor;
            }
            if (target == null || anchorRt == null || _confirmOpen)
            {
                _tooltip.Hide();
            }
            else
            {
                ItemData compare = null;
                if (target.IsEquipment)
                {
                    var equipped = _loot.GetEquipped(target.Slot);
                    if (equipped != null && equipped != target)
                        compare = equipped;
                }
                _tooltip.Show(target, header, compare, LootTooltip.LocalRect(_root, anchorRt));
            }
            #endregion
            _confirm.SetAsLastSibling();
        }

        private bool CanEquip(int slot) => _held != null && _held.IsEquipment && (int)_held.Slot == slot;

        private void CursorToCells(int x, int y, int w, int h, Color color)
        {
            var gridPos = _grid.anchoredPosition;
            _cursor.anchoredPosition = gridPos + new Vector2(x * _cell, -y * _cell);
            _cursor.sizeDelta = new Vector2(w * _cell, h * _cell);
            if (_cursorImage.color != color)
                _cursorImage.color = color;
        }

        private void CursorTo(Rect panelLocal, Color color)
        {
            // panelLocal 以 panel 的 pivot（中央）為原點；cursor 的錨點在 panel 左上
            var size = _panel.rect.size;
            _cursor.anchoredPosition = new Vector2(panelLocal.xMin + size.x / 2f, panelLocal.yMax - size.y / 2f);
            _cursor.sizeDelta = panelLocal.size;
            if (_cursorImage.color != color)
                _cursorImage.color = color;
        }

        private static void SetActive(Component c, bool active)
        {
            if (c.gameObject.activeSelf != active)
                c.gameObject.SetActive(active);
        }
        #endregion
    }
}
