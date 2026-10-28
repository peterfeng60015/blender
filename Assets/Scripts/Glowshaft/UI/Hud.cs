using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Glowshaft
{
    /// <summary>
    /// HUD 與 Game Over 畫面（ui 負責）。全部由程式建立 uGUI Canvas（Screen Space Overlay），不依賴場景或 prefab。
    ///
    /// 為什麼選 uGUI 而不是 IMGUI：uGUI 只在數值變動時重建網格，IMGUI 每幀至少跑兩次 OnGUI 並產生 GC；
    /// 而且 uGUI 能用 Outline／Shadow 做發光字、用 CanvasScaler 依解析度縮放，比較好做出礦坑的發光風格。
    /// 字型用內建的 LegacyRuntime.ttf（不需要額外資產），中文靠作業系統字型 fallback 顯示。
    ///
    /// 只訂閱 <see cref="GameState"/> 的事件、讀屬性；不讀輸入、不建 EventSystem（重來的輸入由 Core 處理）。
    /// 動畫一律用 unscaledDeltaTime，Core 若在 Game Over 時把 timeScale 設成 0 也照樣播放。
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        #region 配色：漆黑礦坑裡的頭燈琥珀色＋史萊姆螢光
        private static readonly Color Amber = new Color(1f, 0.78f, 0.32f);
        private static readonly Color AmberGlow = new Color(1f, 0.55f, 0.1f, 0.45f);
        private static readonly Color Cyan = new Color(0.45f, 1f, 0.85f);
        private static readonly Color CyanGlow = new Color(0.1f, 0.9f, 0.7f, 0.4f);
        private static readonly Color Danger = new Color(1f, 0.3f, 0.22f);
        private static readonly Color DangerGlow = new Color(1f, 0.15f, 0.05f, 0.5f);
        private static readonly Color Dim = new Color(0.75f, 0.72f, 0.65f);
        private static readonly Color PanelColor = new Color(0.02f, 0.02f, 0.03f, 0.6f);
        private static readonly Color CellEmpty = new Color(0.14f, 0.13f, 0.12f, 0.9f);
        #endregion

        private const int MaxCells = 20;            // MaxHp 超過這個數就改用連續血條
        private const float HpBarX = 72f;
        private const float HpBarWidth = 300f;
        private const float HpBarHeight = 22f;
        private const float CellGap = 4f;
        private const float BannerSeconds = 2.4f;
        private const float GameOverFadeSeconds = 0.6f;

        private GameState _state;
        private Font _font;

        // 畫面元件
        private RectTransform _hpBar;
        private readonly List<Image> _hpCells = new List<Image>();
        private Image _hpFill;                      // 連續血條（MaxHp > MaxCells 時用）
        private Text _hpText;
        private Text _scoreText;
        private Text _waveText;
        private Image _damageFlash;
        private CanvasGroup _bannerGroup;
        private Text _bannerTitle;
        private Text _bannerSub;
        private CanvasGroup _gameOverGroup;
        private Text _gameOverScore;
        private Text _gameOverWave;
        private Text _gameOverHint;

        // 動畫狀態
        private int _shownMaxHp = -1;
        private int _lastHp = -1;                   // -1＝下一次 HpChanged 不算受傷或補血（開局、重來）
        private float _damageFlashT;
        private float _healFlashT;
        private float _hpAlpha = 1f;                // 目前套在已填滿血格上的透明度
        private float _displayScore;
        private float _scorePunchT;
        private int _shownScore = -1;
        private float _bannerT = -1f;               // -1＝橫幅沒在播
        private float _gameOverT = -1f;             // -1＝不在 Game Over
        private float _hintT = -1f;                 // -1＝重來提示沒顯示；否則是 RestartAvailable 之後經過的秒數（閃爍相位）

        /// <summary>
        /// 整個遊戲只呼叫一次（素材讀完、第一次開局前）。Hud 自己建 GameObject 與 Canvas、訂閱 state 的事件，
        /// 重來時不會重建；被 Destroy 時要取消訂閱。
        /// </summary>
        public static Hud Create(GameState state)
        {
            var go = new GameObject("Hud", typeof(RectTransform));
            var hud = go.AddComponent<Hud>();
            hud.Init(state);
            return hud;
        }

        private void Init(GameState state)
        {
            _state = state;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();

            if (_state == null)
                return;
            _state.HpChanged += OnHpChanged;
            _state.ScoreChanged += OnScoreChanged;
            _state.WaveChanged += OnWaveChanged;
            _state.GameOver += OnGameOver;
            _state.Restarted += OnRestarted;
            _state.RestartAvailable += OnRestartAvailable;

            // 以目前的值刷新一次（正常流程在第一次 Reset 前建立，這時都是 0）
            OnHpChanged(_state.Hp, _state.MaxHp);
            _displayScore = _state.Score;
            SetScoreText(_state.Score);
            SetWaveText(_state.Wave);
            if (_state.IsGameOver)
                OnGameOver();
        }

        private void OnDestroy()
        {
            if (_state == null)
                return;
            _state.HpChanged -= OnHpChanged;
            _state.ScoreChanged -= OnScoreChanged;
            _state.WaveChanged -= OnWaveChanged;
            _state.GameOver -= OnGameOver;
            _state.Restarted -= OnRestarted;
            _state.RestartAvailable -= OnRestartAvailable;
            _state = null;
        }

        #region 事件

        private void OnHpChanged(int hp, int maxHp)
        {
            if (maxHp != _shownMaxHp)
                RebuildHpBar(maxHp);

            if (_lastHp >= 0)
            {
                if (hp < _lastHp)
                    _damageFlashT = 1f;
                else if (hp > _lastHp)
                    _healFlashT = 1f;
            }
            _lastHp = hp;

            _hpText.text = $"{hp}/{maxHp}";
            RefreshHpColors(hp, maxHp);
        }

        private void OnScoreChanged(int score)
        {
            // 分數變小（重來）直接跳過去；變大則在 Update 裡滾動數字並放大一下
            if (score < _displayScore)
            {
                _displayScore = score;
                SetScoreText(score);
            }
            else if (score > _displayScore)
            {
                _scorePunchT = 1f;
            }
        }

        private void OnWaveChanged(int wave)
        {
            SetWaveText(wave);
            if (wave <= 0 || _gameOverT >= 0f)
            {
                HideBanner();
                return;
            }
            _bannerTitle.text = $"WAVE {wave}";
            _bannerSub.text = $"第 {wave} 波史萊姆來襲";
            _bannerT = 0f;
        }

        private void OnGameOver()
        {
            _gameOverScore.text = $"SCORE  {_state.Score:N0}";
            _gameOverWave.text = $"撐到第 {Mathf.Max(1, _state.Wave)} 波";
            _gameOverGroup.gameObject.SetActive(true);
            _gameOverGroup.alpha = 0f;
            _gameOverT = 0f;
            HideBanner();
            // 「可以重來」以 Core 的 GameState.CanRestart 為唯一來源，UI 不自己計時
            if (_state.CanRestart)
                OnRestartAvailable();
            else
                HideRestartHint();
        }

        private void OnRestartAvailable()
        {
            _gameOverHint.enabled = true;
            _hintT = 0f;
        }

        private void HideRestartHint()
        {
            _gameOverHint.enabled = false;
            _hintT = -1f;
        }

        private void OnRestarted()
        {
            _gameOverT = -1f;
            HideRestartHint();
            _gameOverGroup.gameObject.SetActive(false);
            HideBanner();
            _damageFlashT = 0f;
            _healFlashT = 0f;
            _scorePunchT = 0f;
            _scoreText.rectTransform.localScale = Vector3.one;
            _lastHp = -1;   // 接下來的 HpChanged（補滿）不閃補血光
        }

        private void HideBanner()
        {
            _bannerT = -1f;
            _bannerGroup.alpha = 0f;
        }
        #endregion

        #region 動畫

        private void Update()
        {
            // unscaledDeltaTime 不受 maximumDeltaTime 限制：開局那一幀建地圖的卡頓可能一次超過橫幅的 2.4 秒，
            // 讓第 1 波橫幅直接被跳過，所以自己夾住
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // 受傷：全螢幕紅閃＋血條抖動
            if (_damageFlashT > 0f)
                _damageFlashT = Mathf.Max(0f, _damageFlashT - dt * 2.5f);
            _damageFlash.enabled = _damageFlashT > 0f;
            if (_damageFlash.enabled)
                _damageFlash.color = new Color(Danger.r, 0f, 0f, 0.2f * _damageFlashT * _damageFlashT);

            // 血條：補血放大一下、低血量（≤30%）呼吸閃爍
            if (_healFlashT > 0f)
                _healFlashT = Mathf.Max(0f, _healFlashT - dt * 2f);
            if (_state != null)
            {
                bool low = !_state.IsGameOver && _state.Hp > 0 && HpColor(_state.Hp, _state.MaxHp) == Danger;  // 與紅色同門檻
                float alpha = low ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) : 1f;
                if (alpha != _hpAlpha)
                {
                    _hpAlpha = alpha;
                    SetHpAlpha(alpha);
                }
                // 抖動用時間的正弦，不用 UnityEngine.Random，避免動到 Core／World 共用的全域亂數序列
                float shake = _damageFlashT > 0.3f ? Mathf.Sin(Time.unscaledTime * 90f) * 3f * _damageFlashT : 0f;
                var pos = new Vector2(HpBarX + shake, 0f);
                if (_hpBar.anchoredPosition != pos)
                    _hpBar.anchoredPosition = pos;
                var scale = Vector3.one * (1f + 0.06f * _healFlashT);
                if (_hpBar.localScale != scale)
                    _hpBar.localScale = scale;
            }

            // 分數滾動
            if (_state != null && _displayScore < _state.Score)
            {
                float step = Mathf.Max(30f, (_state.Score - _displayScore) * 8f) * dt;
                _displayScore = Mathf.Min(_state.Score, _displayScore + step);
                int shown = Mathf.FloorToInt(_displayScore);
                if (shown != _shownScore)
                    SetScoreText(shown);
            }
            if (_scorePunchT > 0f)
            {
                _scorePunchT = Mathf.Max(0f, _scorePunchT - dt * 4f);
                _scoreText.rectTransform.localScale = Vector3.one * (1f + 0.18f * _scorePunchT);
            }

            // 波數橫幅：淡入 0.3 秒、停留、淡出 0.6 秒，標題由大縮回原尺寸
            if (_bannerT >= 0f)
            {
                _bannerT += dt;
                float a = _bannerT < 0.3f ? _bannerT / 0.3f
                        : _bannerT > BannerSeconds - 0.6f ? (BannerSeconds - _bannerT) / 0.6f
                        : 1f;
                _bannerGroup.alpha = Mathf.Clamp01(a);
                if (_bannerT - dt < 0.35f)
                {
                    float s = Mathf.Lerp(1.35f, 1f, Mathf.Clamp01(_bannerT / 0.35f));
                    _bannerTitle.rectTransform.localScale = new Vector3(s, s, 1f);
                }
                if (_bannerT >= BannerSeconds)
                    HideBanner();
            }

            // Game Over：面板淡入；重來提示只在 GameState.CanRestart 之後顯示並閃爍（相位從 RestartAvailable 起算）
            if (_gameOverT >= 0f)
            {
                _gameOverT += dt;
                if (_gameOverGroup.alpha < 1f)
                    _gameOverGroup.alpha = Mathf.Clamp01(_gameOverT / GameOverFadeSeconds);
            }
            if (_hintT >= 0f)
            {
                _hintT += dt;
                var c = _gameOverHint.color;
                c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(_hintT * 3f));
                _gameOverHint.color = c;
            }
        }
        #endregion

        #region 刷新

        private void SetScoreText(int score)
        {
            _shownScore = score;
            _scoreText.text = score.ToString("N0");
        }

        private void SetWaveText(int wave) => _waveText.text = wave > 0 ? $"WAVE {wave}" : "WAVE -";

        private void RebuildHpBar(int maxHp)
        {
            _shownMaxHp = maxHp;
            foreach (var cell in _hpCells)
                Destroy(cell.gameObject);
            _hpCells.Clear();

            bool useCells = maxHp > 0 && maxHp <= MaxCells;
            _hpFill.transform.parent.gameObject.SetActive(!useCells);
            if (!useCells)
                return;

            float w = (HpBarWidth - CellGap * (maxHp - 1)) / maxHp;
            for (int i = 0; i < maxHp; i++)
            {
                var img = NewImage($"Cell{i}", _hpBar, Color.white);
                Place(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(i * (w + CellGap), 0f), new Vector2(w, HpBarHeight));
                _hpCells.Add(img);
            }
        }

        private static Color HpColor(int hp, int maxHp)
        {
            float r = maxHp > 0 ? (float)hp / maxHp : 0f;
            if (r > 0.6f)
                return Cyan;
            if (r > 0.3f)
                return Amber;
            return Danger;
        }

        private void RefreshHpColors(int hp, int maxHp)
        {
            var full = HpColor(hp, maxHp);
            for (int i = 0; i < _hpCells.Count; i++)
                _hpCells[i].color = i < hp ? full : CellEmpty;
            _hpFill.color = full;
            _hpAlpha = 1f;  // 顏色剛重設成不透明，讓 Update 下一幀重新套用低血量閃爍
            _hpFill.rectTransform.anchorMax = new Vector2(maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f, 1f);
            _hpText.color = full;
            _hpText.GetComponent<Outline>().effectColor = HpGlow(full);
        }

        private static Color HpGlow(Color full) => full == Cyan ? CyanGlow : full == Amber ? AmberGlow : DangerGlow;

        private void SetHpAlpha(float a)
        {
            int filled = Mathf.Min(_state.Hp, _hpCells.Count);
            for (int i = 0; i < filled; i++)
            {
                var c = _hpCells[i].color;
                c.a = a;
                _hpCells[i].color = c;
            }
            var f = _hpFill.color;
            f.a = a;
            _hpFill.color = f;
        }
        #endregion

        #region 建立畫面

        private void BuildCanvas()
        {
            gameObject.layer = 5; // UI
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // 不加 GraphicRaycaster：UI 不接收輸入

            var root = (RectTransform)transform;

            // 受傷時的全螢幕紅閃（最底層，不擋其他 UI）
            _damageFlash = NewImage("DamageFlash", root, Color.clear);
            Stretch(_damageFlash.rectTransform);
            _damageFlash.enabled = false;

            BuildHpPanel(root);
            BuildWavePanel(root);
            BuildScorePanel(root);
            BuildBanner(root);
            BuildGameOver(root);
        }

        private void BuildHpPanel(RectTransform root)
        {
            var panel = NewPanel("HpPanel", root, new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(500f, 64f));

            var label = NewText("Label", panel, "HP", 26, Amber, AmberGlow, TextAnchor.MiddleLeft);
            Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(50f, 40f));

            _hpBar = NewRect("Bar", panel);
            Place(_hpBar, new Vector2(0f, 0.5f), new Vector2(HpBarX, 0f), new Vector2(HpBarWidth, HpBarHeight));

            // 連續血條（MaxHp 太大時才顯示）
            var bg = NewImage("FillBg", _hpBar, CellEmpty);
            Stretch(bg.rectTransform);
            _hpFill = NewImage("Fill", bg.rectTransform, Cyan);
            var fr = _hpFill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            bg.gameObject.SetActive(false);

            _hpText = NewText("Value", panel, "0/0", 24, Cyan, CyanGlow, TextAnchor.MiddleRight);
            Place(_hpText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(100f, 40f));
        }

        private void BuildWavePanel(RectTransform root)
        {
            var panel = NewPanel("WavePanel", root, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(220f, 64f));
            _waveText = NewText("Value", panel, "WAVE -", 32, Amber, AmberGlow, TextAnchor.MiddleCenter);
            Stretch(_waveText.rectTransform);
        }

        private void BuildScorePanel(RectTransform root)
        {
            var panel = NewPanel("ScorePanel", root, new Vector2(1f, 1f), new Vector2(-32f, -28f), new Vector2(320f, 64f));
            var label = NewText("Label", panel, "SCORE", 22, Dim, AmberGlow, TextAnchor.MiddleLeft);
            Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(100f, 40f));
            _scoreText = NewText("Value", panel, "0", 36, Cyan, CyanGlow, TextAnchor.MiddleRight);
            Place(_scoreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(200f, 50f));
        }

        private void BuildBanner(RectTransform root)
        {
            var rt = NewRect("WaveBanner", root);
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(900f, 220f));
            _bannerGroup = rt.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;
            _bannerGroup.interactable = false;

            var strip = NewImage("Strip", rt, new Color(0f, 0f, 0f, 0.45f));
            Place(strip.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 180f));

            _bannerTitle = NewText("Title", rt, "WAVE 1", 96, Amber, AmberGlow, TextAnchor.MiddleCenter);
            Place(_bannerTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(900f, 120f));
            _bannerSub = NewText("Sub", rt, "", 34, Cyan, CyanGlow, TextAnchor.MiddleCenter, cjk: true);
            Place(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -54f), new Vector2(900f, 44f));
        }

        private void BuildGameOver(RectTransform root)
        {
            var rt = NewRect("GameOver", root);
            Stretch(rt);
            _gameOverGroup = rt.gameObject.AddComponent<CanvasGroup>();
            _gameOverGroup.blocksRaycasts = false;
            _gameOverGroup.interactable = false;

            var dim = NewImage("Dim", rt, new Color(0f, 0f, 0f, 0.78f));
            Stretch(dim.rectTransform);

            var title = NewText("Title", rt, "GAME OVER", 128, Danger, DangerGlow, TextAnchor.MiddleCenter);
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1400f, 170f));
            var sub = NewText("Sub", rt, "頭燈熄滅了……", 34, Dim, AmberGlow, TextAnchor.MiddleCenter, cjk: true);
            Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1000f, 48f));

            _gameOverScore = NewText("Score", rt, "SCORE  0", 64, Cyan, CyanGlow, TextAnchor.MiddleCenter);
            Place(_gameOverScore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1000f, 90f));
            _gameOverWave = NewText("Wave", rt, "", 38, Amber, AmberGlow, TextAnchor.MiddleCenter, cjk: true);
            Place(_gameOverWave.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(1000f, 52f));

            _gameOverHint = NewText("Hint", rt, "按 R 或滑鼠左鍵重新開始", 40, Color.white, AmberGlow, TextAnchor.MiddleCenter, cjk: true);
            Place(_gameOverHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(1000f, 52f));

            rt.gameObject.SetActive(false);
        }
        #endregion

        #region 小工具

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>半透明暗底＋底部一條琥珀色細線，像礦工的儀表板。</summary>
        private static RectTransform NewPanel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = NewImage(name, parent, PanelColor).rectTransform;
            Place(rt, anchor, pos, size);
            var line = NewImage("Edge", rt, new Color(Amber.r, Amber.g, Amber.b, 0.55f)).rectTransform;
            line.anchorMin = Vector2.zero;
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.sizeDelta = new Vector2(0f, 2f);
            line.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <param name="cjk">含中文的字：LegacyRuntime.ttf 沒有中文字形，靠 OS 字型 fallback，
        /// 加粗＋彩色光暈會糊成一團，所以改成不加粗、用深色細描邊保持清楚。</param>
        private Text NewText(string name, Transform parent, string content, int size, Color color, Color glow, TextAnchor align, bool cjk = false)
        {
            var go = NewRect(name, parent).gameObject;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = cjk ? FontStyle.Normal : FontStyle.Bold;
            t.color = color;
            t.alignment = align;
            t.text = content;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            // 發光：半透明的同色系描邊當光暈，再加往下的深色陰影，讓字在全黑或被照亮的地面上都清楚
            var outline = go.AddComponent<Outline>();
            outline.effectColor = cjk ? new Color(0f, 0f, 0f, 0.9f) : glow;
            float d = cjk ? 1.5f : size <= 26 ? 1.25f : 2f;  // 小字描邊太粗會糊
            outline.effectDistance = new Vector2(d, d);
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -3f);
            return t;
        }

        /// <summary>錨點與 pivot 都設在 anchor，pos 是相對錨點的位移。</summary>
        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        #endregion
    }
}
