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

        #region 暫停選單與通關畫面
        private RectTransform _pauseRoot;
        private MenuList _pauseList;
        private readonly int[] _pauseActions = new int[4];
        private int _pauseOpenedFrame = -1;
        private int _unpauseAfterFrame = -1;    // ≥0：等這一幀過了才 SetPaused(false)
        private int _afterUnpause = ActionNone;

        private RectTransform _clearRoot;
        private MenuList _clearList;
        private TextMeshProUGUI _clearTime;
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
            _clearRoot.gameObject.SetActive(true);
            _clearList.Open(new[] { StoryTexts.ClearRestart, StoryTexts.ClearQuit });
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
            var title = SsUiKit.Rect("Title", _pauseRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            title.anchoredPosition = new Vector2(0f, 200f);
            title.sizeDelta = new Vector2(800f, 110f);
            var t = SsUiKit.AddText(title, _font, 84f, SsUiKit.Gold);
            t.text = StoryTexts.PauseTitle;
            _pauseList = new MenuList(_pauseRoot, _font, 0.5f, 170f);
            _pauseRoot.gameObject.SetActive(false);
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
            var title = SsUiKit.Rect("Title", _clearRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            title.anchoredPosition = new Vector2(0f, 190f);
            title.sizeDelta = new Vector2(1200f, 150f);
            var t = SsUiKit.AddText(title, _font, 120f, SsUiKit.Gold);
            t.characterSpacing = 8f;
            t.text = StoryTexts.ClearTitle;
            var time = SsUiKit.Rect("Time", _clearRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            time.anchoredPosition = new Vector2(0f, 100f);
            time.sizeDelta = new Vector2(1000f, 70f);
            _clearTime = SsUiKit.AddText(time, _font, 52f, SsUiKit.Paper);
            _clearList = new MenuList(_clearRoot, _font, 0.5f, 40f);
            _clearRoot.gameObject.SetActive(false);
        }
        #endregion
    }
}
