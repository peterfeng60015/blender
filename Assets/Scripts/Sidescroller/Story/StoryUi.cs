using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 演出與所有 UI（interfaces.md §4.3）。每次 Boot 建一次，重生不重建。
    /// 自己建 Screen Space Overlay Canvas，由下到上：HUD、觸控按鈕、對話框、淡入淡出遮罩、字卡、通關畫面、暫停選單、直向提示。
    /// 建立時畫面全黑（等流程淡入）。不建 EventSystem，按鍵一律讀 <see cref="SsInput"/>。
    /// 直向（Screen.width &lt; Screen.height）時蓋直向提示並 SetPaused(true)；轉回橫向時只解除自己設的暫停，玩家開的暫停選單照舊。
    /// 不留 static 狀態：全部狀態在實例上，跟著場景卸載消失。
    /// </summary>
    public sealed class StoryUi : MonoBehaviour
    {
        private const int CanvasOrder = 100;
        private const int ActionNone = 0, ActionResume = 1, ActionSkip = 2, ActionRespawn = 3, ActionQuit = 4;

        private GameSession _session;
        private CameraRig _rig;
        private TMP_FontAsset _font;
        private bool _busy;
        private CutsceneRunner _runner;
        private bool _subscribed;
        private UiSkin _skin;

        #region 暫停選單與通關畫面
        private const float PauseTop = 300f;
        private const float PauseHeader = 130f;
        private const float PauseFooter = 56f;
        private const float PanelWidth = 780f;

        private RectTransform _pauseRoot;
        private RectTransform _pausePanel;
        private UiPop _pausePop;
        private MenuList _pauseList;
        private readonly int[] _pauseActions = new int[4];
        private int _pauseOpenedFrame = -1;
        private int _unpauseAfterFrame = -1;    // ≥0：等這一幀過了才 SetPaused(false)
        private int _afterUnpause = ActionNone;

        private RectTransform _clearRoot;
        private MenuList _clearList;
        private TextMeshProUGUI _clearTime;
        private UiPop _clearPop;
        private UiPop _clearTitlePop;
        private RectTransform _clearBurst;
        private readonly List<RectTransform> _clearSparkles = new List<RectTransform>();
        private float _clearElapsed;
        private bool _cleared;
        #endregion

        #region 直向提示
        private RotateHintView _rotate;
        /// <summary>暫停是直向提示設的（轉回橫向時要解除）；玩家自己開的暫停選單不算。</summary>
        private bool _rotatePaused;
        #endregion

        internal HudView Hud { get; private set; }
        /// <summary>背包圖示與拾取回饋（pickup）。</summary>
        internal LootHudView LootHud { get; private set; }
        /// <summary>背包與強化介面（ui）。</summary>
        internal LootScreens LootScreens { get; private set; }
        internal DialogueView Dialogue { get; private set; }
        internal FadeView Fade { get; private set; }
        internal BannerView Banner { get; private set; }
        internal TouchControlsView Touch { get; private set; }

        /// <summary>遊戲鏡頭（補給站提示把世界座標換成畫面位置用）。</summary>
        internal Camera WorldCamera => _rig != null ? _rig.Camera : null;

        /// <summary>觸控按鈕要全部收起（手指都當點擊）：暫停中（含暫停選單、背包與強化介面、直向提示）、通關畫面。</summary>
        internal bool TouchBlocked => _cleared || _session.Paused || (_rotate != null && _rotate.Visible);

        public static StoryUi Create(GameSession session, CameraRig rig)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            var go = new GameObject("StoryUi");
            try
            {
                var ui = go.AddComponent<StoryUi>();
                ui.Init(session, rig);
                return ui;
            }
            catch (Exception)
            {
                Destroy(go);
                throw;
            }
        }

        public bool IsCutscenePlaying => _busy;

        #region 演出
        /// <summary>
        /// 讀取、解析並播放演出（cutscene-format.md §4）。false＝讀取或解析失敗（已記錯誤，不改任何狀態）。
        /// ct 被取消時清理後丟 <see cref="OperationCanceledException"/>。
        /// </summary>
        public async UniTask<bool> PlayCutsceneAsync(string location, CancellationToken ct)
        {
            if (_busy)
            {
                Debug.LogError($"[Sidescroller.StoryUi] 演出 {location} 開始時已有演出在播，忽略");
                return false;
            }
            _busy = true;
            CutsceneScript script;
            try
            {
                var text = await SsArt.LoadTextAsync(location, ct);
                var issues = new CutsceneIssues();
                script = CutsceneParser.Parse(location, text, issues);
                foreach (var w in issues.Warnings)
                    Debug.LogWarning($"[Sidescroller.StoryUi] {w}");
                foreach (var e in issues.Errors)
                    Debug.LogError($"[Sidescroller.StoryUi] {e}");
            }
            catch (OperationCanceledException)
            {
                _busy = false;
                throw;
            }
            catch (Exception ex)
            {
                _busy = false;
                Debug.LogError($"[Sidescroller.StoryUi] 演出 {location} 讀取或解析失敗：{ex.Message}");
                return false;
            }

            Debug.Log($"[Sidescroller.StoryUi] 開始演出 {location}（{script.Commands.Count} 個指令）");
            _session.SetCutsceneActive(true);
            var runner = new CutsceneRunner(this, _session, _rig, script, ct);
            _runner = runner;
            try
            {
                await runner.RunAsync();
            }
            finally
            {
                try
                {
                    runner.Cleanup();
                }
                catch (Exception ex)
                {
                    // 清理失敗也一定要解除輸入鎖，否則玩家永久卡住
                    Debug.LogException(ex);
                }
                _runner = null;
                if (!ct.IsCancellationRequested && this != null)
                {
                    try
                    {
                        if (!script.KeepFade)
                        {
                            if (Fade.Target > 0.001f)
                            {
                                Debug.LogWarning($"[Sidescroller.StoryUi] 演出 {location} 結束時畫面沒有淡回透明，自動淡回（腳本漏寫 fade to 0？）");
                                Fade.Start(0f, 0.3f, Fade.White);
                            }
                            await WaitFadeAsync(Fade.Version, ct);
                        }
                        // 等下一幀才解除：結束最後一句的那次 Space／J 不會被主角當成跳躍或攻擊
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                _session.SetCutsceneActive(false);
                _busy = false;
                Debug.Log($"[Sidescroller.StoryUi] 演出 {location} 結束" + (ct.IsCancellationRequested ? "（取消）" : ""));
            }
            ct.ThrowIfCancellationRequested();
            return true;
        }

        /// <summary>to：0 透明～1 全黑。流程用，一律黑色。</summary>
        public async UniTask FadeAsync(float to, float time, CancellationToken ct)
        {
            if (this == null)
                return;
            var version = Fade.Start(to, time, false);
            await WaitFadeAsync(version, ct);
        }

        public void ShowBanner(string text, float time) => Banner.Show(text, time);

        internal async UniTask WaitFadeAsync(int version, CancellationToken ct)
        {
            while (this != null && !Fade.IsDone(version))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        #endregion

        #region 建立與事件
        private void Init(GameSession session, CameraRig rig)
        {
            _session = session;
            _rig = rig;
            _font = GameSelectMenu.Font();

            _skin = new UiSkin();
            var canvas = SsUiKit.CreateCanvas("StoryCanvas", CanvasOrder, transform);
            var root = canvas.transform;
            Hud = new HudView(root, _font, session);
            LootHud = new LootHudView(root, _font, session);
            Touch = new TouchControlsView(root, _font, session, this);
            Dialogue = new DialogueView(root, _font);
            Fade = new FadeView(root);
            Banner = new BannerView(root, _font);
            BuildClearScreen(root);
            LootScreens = new LootScreens(root, _font, session, this);
            BuildPauseMenu(root);
            _rotate = new RotateHintView(root, _font, StoryTexts.RotateSub);
            Fade.SetImmediate(1f, false);
            SsTouch.Pad = Touch;

            session.HpChanged += OnHpChanged;
            session.Loadout.Changed += OnLoadoutChanged;
            session.HintChanged += OnHintChanged;
            session.BossChanged += OnBossChanged;
            session.StageCleared += OnStageCleared;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (Touch != null)
            {
                // 重新開始時新的 StoryUi 可能已經登記了，只清自己的
                if (SsTouch.Pad == Touch)
                    SsTouch.Pad = null;
                Touch.Dispose();
            }
            LootScreens?.Dispose();
            _skin?.Dispose();
            if (!_subscribed || _session == null)
                return;
            _subscribed = false;
            _session.HpChanged -= OnHpChanged;
            _session.Loadout.Changed -= OnLoadoutChanged;
            _session.HintChanged -= OnHintChanged;
            _session.BossChanged -= OnBossChanged;
            _session.StageCleared -= OnStageCleared;
        }

        private void OnHpChanged(int hp, int maxHp) => Hud.RefreshHp();
        private void OnLoadoutChanged() => Hud.RefreshLoadout();
        private void OnHintChanged(string text) => Hud.RefreshHint();
        private void OnBossChanged() => Hud.RefreshBoss();

        private void OnStageCleared(float seconds)
        {
            if (_cleared)
                return;
            _cleared = true;
            if (_pauseRoot.gameObject.activeSelf || _unpauseAfterFrame >= 0)
            {
                _pauseRoot.gameObject.SetActive(false);
                _unpauseAfterFrame = -1;
                _afterUnpause = ActionNone;
                _session.SetPaused(false);
            }
            _clearTime.text = StoryTexts.ClearTimePrefix + SsUiKit.FormatTime(seconds);
            Hud.SetVisible(false);
            _clearRoot.gameObject.SetActive(true);
            _clearList.Open(new[] { StoryTexts.ClearRestart, StoryTexts.ClearQuit });
            _clearElapsed = 0f;
            _clearPop.Play(0.25f);
            _clearTitlePop.Play();
            Debug.Log($"[Sidescroller.StoryUi] 通關畫面（{SsUiKit.FormatTime(seconds)}）");
        }
        #endregion

        #region 每幀
        private void Update()
        {
            var dt = Time.deltaTime;
            var udt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Fade.Tick(dt);
            Banner.Tick(dt);
            Dialogue.Tick(udt);
            Hud.Tick();
            LootHud.Tick();
            TickPauseAndClear(udt);
            var portrait = UpdateRotateHint();
            Touch.Tick();
            if (portrait)
                return;

            if (_unpauseAfterFrame >= 0)
            {
                if (Time.frameCount > _unpauseAfterFrame)
                    FinishUnpause();
                return;
            }
            if (_cleared)
            {
                switch (_clearList.Tick())
                {
                    case 0: SidescrollerGame.Restart(); break;
                    case 1: SidescrollerGame.ReturnToMenu(); break;
                }
                return;
            }
            if (_pauseRoot.gameObject.activeSelf)
            {
                if (SsInput.PausePressed && Time.frameCount != _pauseOpenedFrame)
                {
                    ClosePause(ActionNone);
                    return;
                }
                var i = _pauseList.Tick();
                if (i >= 0)
                    OnPauseChosen(_pauseActions[i]);
                return;
            }
            if (LootScreens.Tick())
                return;
            if (SsInput.PausePressed && !_session.Paused)
                OpenPause();
        }
        #endregion

        #region 直向提示
        /// <summary>
        /// 直向：顯示提示；還沒暫停就 SetPaused(true) 並記下是自己設的。直向期間 Update 不處理任何選單輸入
        /// （延後一幀的解除暫停也等轉回來才做）。轉回橫向：只有自己設的暫停才解除。
        /// </summary>
        private bool UpdateRotateHint()
        {
            if (_rotate.Refresh())
            {
                if (!_rotatePaused && !_session.Paused)
                {
                    _rotatePaused = true;
                    _session.SetPaused(true);
                    Debug.Log("[Sidescroller.StoryUi] 直向，暫停");
                }
                return true;
            }
            if (_rotatePaused)
            {
                _rotatePaused = false;
                _session.SetPaused(false);
                Debug.Log("[Sidescroller.StoryUi] 轉回橫向，解除暫停");
            }
            return false;
        }
        #endregion

        #region 暫停選單
        private void BuildPauseMenu(Transform parent)
        {
            _pauseRoot = SsUiKit.Fill("Pause", parent);
            SsUiKit.AddImage(_pauseRoot, new Color(0.04f, 0.02f, 0.08f, 0.72f));
            // 面板、標題與選項放在同一層，進場動畫一起縮放
            var window = SsUiKit.Fill("Window", _pauseRoot);
            _pausePop = new UiPop(window, 0.22f);

            _pausePanel = SsUiKit.Rect("Panel", window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f));
            _pausePanel.anchoredPosition = new Vector2(0f, PauseTop);
            var shadow = SsUiKit.Fill("Shadow", _pausePanel);
            shadow.offsetMin = new Vector2(-24f, -44f);
            shadow.offsetMax = new Vector2(40f, 4f);
            _skin.Add(shadow, UiSkin.Shadow, 0.8f);
            _skin.Add(SsUiKit.Fill("Frame", _pausePanel), UiSkin.Panel, 0.6f);

            var ribbon = SsUiKit.Rect("Ribbon", window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ribbon.anchoredPosition = new Vector2(0f, PauseTop);
            ribbon.sizeDelta = new Vector2(440f, 120f);
            _skin.Add(ribbon, UiSkin.Ribbon, 0.5f);
            var titleRt = SsUiKit.Fill("Title", ribbon);
            titleRt.anchoredPosition = new Vector2(0f, 4f);
            _skin.Title(titleRt, _font, 72f, SsUiKit.Paper, StoryTexts.PauseTitle, 5f);

            _pauseList = new MenuList(window, _font, 0.5f, PauseTop - PauseHeader, _skin);
            _pauseRoot.gameObject.SetActive(false);
        }

        /// <summary>面板高度跟著選項數與項目高度（觸控模式會加高）。</summary>
        private void LayoutPausePanel()
        {
            _pausePanel.sizeDelta = new Vector2(PanelWidth, PauseHeader + _pauseList.ContentHeight + PauseFooter);
        }

        private void OpenPause()
        {
            var labels = new List<string>();
            var n = 0;
            labels.Add(StoryTexts.PauseResume);
            _pauseActions[n++] = ActionResume;
            if (_runner != null && _runner.CanSkip)
            {
                labels.Add(StoryTexts.PauseSkip);
                _pauseActions[n++] = ActionSkip;
            }
            // 演出中、重生淡出入中、Boss 擊敗後（流程鎖住輸入不再放開）都不給重來
            if (!_busy && !_session.InputLocked)
            {
                labels.Add(StoryTexts.PauseRespawn);
                _pauseActions[n++] = ActionRespawn;
            }
            labels.Add(StoryTexts.PauseQuit);
            _pauseActions[n] = ActionQuit;

            _pauseRoot.gameObject.SetActive(true);
            _pauseList.Open(labels);
            LayoutPausePanel();
            _pausePop.Play();
            _pauseOpenedFrame = Time.frameCount;
            _session.SetPaused(true);
        }

        private void OnPauseChosen(int action)
        {
            switch (action)
            {
                case ActionSkip:
                    _runner?.RequestSkip();
                    ClosePause(ActionNone);
                    break;
                case ActionRespawn:
                    ClosePause(ActionRespawn);
                    break;
                case ActionQuit:
                    // Shutdown 會把 timeScale 設回 1、卸載整個場景（包括這個 UI）
                    SidescrollerGame.ReturnToMenu();
                    break;
                default:
                    ClosePause(ActionNone);
                    break;
            }
        }

        /// <summary>收起選單；延後一幀才 SetPaused(false)（選「繼續」的那次確定鍵不會被主角或對話框吃到），然後才做 after。</summary>
        private void ClosePause(int after)
        {
            _pauseRoot.gameObject.SetActive(false);
            _unpauseAfterFrame = Time.frameCount;
            _afterUnpause = after;
        }

        private void FinishUnpause()
        {
            _unpauseAfterFrame = -1;
            var after = _afterUnpause;
            _afterUnpause = ActionNone;
            _session.SetPaused(false);
            if (after == ActionRespawn)
                _session.RequestRespawn();
        }
        #endregion

        #region 通關畫面
        private void BuildClearScreen(Transform parent)
        {
            _clearRoot = SsUiKit.Fill("Clear", parent);
            SsUiKit.AddImage(_clearRoot, new Color(0.03f, 0.02f, 0.06f, 0.85f));

            #region 標題：旋轉光芒、星光、彈出的大字
            _clearBurst = SsUiKit.Rect("Burst", _clearRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _clearBurst.anchoredPosition = new Vector2(0f, 265f);
            _clearBurst.sizeDelta = new Vector2(1150f, 1150f);
            _skin.Add(_clearBurst, UiSkin.Sunburst, 1f, new Color(1f, 1f, 1f, 0.32f));

            var title = SsUiKit.Rect("Title", _clearRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            title.anchoredPosition = new Vector2(0f, 265f);
            title.sizeDelta = new Vector2(1200f, 150f);
            _skin.Title(title, _font, 124f, SsUiKit.Gold, StoryTexts.ClearTitle, 8f, 8f);
            _clearTitlePop = new UiPop(title, 0.45f) { From = 1.8f };

            var sparkles = new[] { new Vector3(-470f, 330f, 90f), new Vector3(455f, 345f, 70f), new Vector3(-395f, 195f, 54f), new Vector3(520f, 215f, 60f) };
            foreach (var sp in sparkles)
            {
                var rt = SsUiKit.Rect("Sparkle", _clearRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                rt.anchoredPosition = new Vector2(sp.x, sp.y);
                rt.sizeDelta = new Vector2(sp.z, sp.z) * 2f;
                _skin.Add(rt, UiSkin.Sparkle, 1f);
                _clearSparkles.Add(rt);
            }
            #endregion

            #region 時間與選項（一起淡入）
            var window = SsUiKit.Fill("Window", _clearRoot);
            _clearPop = new UiPop(window, 0.3f) { From = 0.92f };
            var pill = SsUiKit.Rect("Time", window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            pill.anchoredPosition = new Vector2(0f, 86f);
            pill.sizeDelta = new Vector2(600f, 84f);
            _skin.Add(pill, UiSkin.Pill, 0.5f);
            var timeRt = SsUiKit.Fill("Text", pill);
            _clearTime = SsUiKit.AddText(timeRt, _font, 46f, SsUiKit.Paper);
            _clearTime.textWrappingMode = TextWrappingModes.NoWrap;
            _clearList = new MenuList(window, _font, 0.5f, 40f, _skin);
            #endregion

            _clearRoot.gameObject.SetActive(false);
        }

        /// <summary>暫停與通關畫面的動畫（unscaled，暫停中也動）。</summary>
        private void TickPauseAndClear(float udt)
        {
            if (_pauseRoot.gameObject.activeSelf)
            {
                _pausePop.Tick(udt);
                LayoutPausePanel();
            }
            if (!_clearRoot.gameObject.activeSelf)
                return;
            _clearElapsed += udt;
            _clearPop.Tick(udt);
            _clearTitlePop.Tick(udt);
            _clearBurst.localEulerAngles = new Vector3(0f, 0f, -_clearElapsed * 12f);
            var burst = 1f + Mathf.Sin(_clearElapsed * 2f) * 0.04f;
            _clearBurst.localScale = new Vector3(burst, burst, 1f) * Mathf.Clamp01(_clearElapsed * 3f);
            for (var i = 0; i < _clearSparkles.Count; i++)
            {
                // 每顆錯開相位，閃一下、停一下
                var phase = Mathf.Repeat(_clearElapsed * 0.9f + i * 0.37f, 1f);
                var s = Mathf.Sin(Mathf.Clamp01(phase / 0.45f) * Mathf.PI);
                _clearSparkles[i].localScale = new Vector3(s, s, 1f);
                _clearSparkles[i].localEulerAngles = new Vector3(0f, 0f, phase * 90f);
            }
        }
        #endregion
    }
}
