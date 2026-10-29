using TMPro;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 背包介面（I 鍵／點右上背包圖示）與強化介面（補給站 E 鍵／觸控按鈕），ui 實作（loot-design.md §5、§6）。
    /// 開啟時 session.SetPaused(true)；關閉時延後一幀才 SetPaused(false)（同暫停選單）。由 <see cref="StoryUi"/> 建立並每幀呼叫 <see cref="Tick"/>。
    /// 只在沒有演出、沒暫停、沒有重生鎖（<see cref="GameSession.InputLocked"/>）、沒有通關畫面與直向提示時能開。
    /// 介面裡的畫面與操作在 <see cref="InventoryView"/>、<see cref="EnhanceView"/>；這裡管開關、暫停、補給站提示。
    /// </summary>
    public sealed class LootScreens
    {
        private readonly GameSession _session;
        private readonly StoryUi _ui;
        private readonly LootPointer _pointer = new LootPointer();
        private readonly LootUiArt _art = new LootUiArt();
        private readonly InventoryView _inventory;
        private readonly EnhanceView _enhance;
        private readonly RectTransform _promptRoot;
        private readonly RectTransform _prompt;
        private bool _stationInRange;
        private Vector3 _stationTop;
        /// <summary>≥0：等這一幀過了才 SetPaused(false)。</summary>
        private int _unpauseAfterFrame = -1;

        public LootScreens(Transform parent, TMP_FontAsset font, GameSession session, StoryUi ui)
        {
            _session = session;
            _ui = ui;

            #region 補給站提示「E：強化」（畫在補給站上方；觸控模式改顯示「強化」按鈕）
            _promptRoot = SsUiKit.Fill("StationPrompt", parent);
            _prompt = SsUiKit.Rect("Prompt", _promptRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            _prompt.sizeDelta = new Vector2(260f, 64f);
            SsUiKit.AddImage(_prompt, new Color(0.05f, 0.03f, 0.1f, 0.78f));
            var text = SsUiKit.AddText(SsUiKit.Fill("Text", _prompt), font, 36f, SsUiKit.Gold);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = StoryTexts.StationPrompt;
            _promptRoot.gameObject.SetActive(false);
            #endregion

            _inventory = new InventoryView(parent, font, session, _pointer);
            _enhance = new EnhanceView(parent, font, session, _pointer, _art);
        }

        /// <summary>背包或強化介面開著（含關閉後等一幀解除暫停的期間）。</summary>
        public bool IsOpen => _inventory.IsOpen || _enhance.IsOpen || _unpauseAfterFrame >= 0;

        /// <summary>主角在補給站範圍內（觸控的「強化」按鈕依此顯示）。</summary>
        internal bool StationInRange => _stationInRange;

        /// <summary>補給站呼叫：主角在補給站範圍內與否（顯示「E：強化」與觸控按鈕）。</summary>
        public void SetStationInRange(bool inRange) => _stationInRange = inRange;

        /// <summary>補給站呼叫：提示要畫在哪裡（世界座標，補給站頂端）。</summary>
        internal void SetStationAnchor(Vector3 worldTop) => _stationTop = worldTop;

        /// <summary>釋放程式產生的貼圖、取消訂閱（StoryUi.OnDestroy 呼叫）。</summary>
        internal void Dispose()
        {
            _inventory.Dispose();
            _enhance.Dispose();
            _art.Dispose();
        }

        /// <summary>每幀呼叫（暫停選單沒開時）；這一幀處理了輸入（介面開著、或剛開關）回 true，StoryUi 就不再處理暫停鍵。</summary>
        public bool Tick()
        {
            _pointer.Update();
            if (_unpauseAfterFrame >= 0)
            {
                if (Time.frameCount > _unpauseAfterFrame)
                {
                    _unpauseAfterFrame = -1;
                    _session.SetPaused(false);
                }
                SetPromptVisible(false);
                return true;
            }
            if (_inventory.IsOpen)
            {
                if (!_inventory.Tick())
                    Close();
                return true;
            }
            if (_enhance.IsOpen)
            {
                if (!_enhance.Tick())
                    Close();
                return true;
            }

            var canOpen = CanOpen;
            UpdatePrompt(canOpen);
            if (!canOpen)
                return false;
            if (SsInput.BagPressed || BagIconClicked())
            {
                Open(true);
                return true;
            }
            if (_stationInRange && SsInput.InteractPressed)
            {
                Open(false);
                return true;
            }
            return false;
        }

        #region 開關
        private bool CanOpen => !_session.InputLocked && _ui != null && !_ui.TouchBlocked && !_ui.IsCutscenePlaying;

        private void Open(bool bag)
        {
            SetPromptVisible(false);
            _session.SetPaused(true);
            if (bag)
                _inventory.Open();
            else
                _enhance.Open();
            Debug.Log($"[Sidescroller.LootScreens] 開啟{(bag ? "背包" : "強化")}介面");
        }

        private void Close()
        {
            _inventory.Close();
            _enhance.Close();
            // 延後一幀才解除暫停：關閉的那次按鍵（Esc、J、E）不會被主角吃到
            _unpauseAfterFrame = Time.frameCount;
        }

        /// <summary>點右上的背包圖示（滑鼠或手指；遊戲中觸控時，按在按鈕與搖桿以外的手指才算點擊）。</summary>
        private bool BagIconClicked()
        {
            var icon = _ui.LootHud != null ? _ui.LootHud.BagIcon : null;
            if (icon == null || !icon.gameObject.activeInHierarchy || !_pointer.Down)
                return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(icon, _pointer.DownPos, null, out var local))
                return false;
            // 圖示四周多算一點，手指比較好點
            var r = icon.rect;
            const float pad = 16f;
            r.xMin -= pad;
            r.yMin -= pad;
            r.xMax += pad;
            r.yMax += pad;
            return r.Contains(local);
        }
        #endregion

        #region 補給站提示
        private void UpdatePrompt(bool canOpen)
        {
            var show = canOpen && _stationInRange && !SsInput.TouchActive;
            SetPromptVisible(show);
            if (!show)
                return;
            var cam = _ui.WorldCamera;
            if (cam == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_promptRoot, cam.WorldToScreenPoint(_stationTop), null, out var local))
                return;
            _prompt.anchoredPosition = local + new Vector2(0f, 16f);
        }

        private void SetPromptVisible(bool visible)
        {
            if (_promptRoot.gameObject.activeSelf != visible)
                _promptRoot.gameObject.SetActive(visible);
        }
        #endregion
    }
}
