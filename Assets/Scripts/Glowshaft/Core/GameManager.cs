using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Glowshaft
{
    /// <summary>
    /// 遊戲總控：讀素材 → 建 HUD → 開局（建地圖、玩家）→ 一波波生成史萊姆 → Game Over → 重來。
    /// 相機跟隨玩家並限制在地圖範圍內。呼叫順序見 gameplay-split.md 第 6 節。
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        private enum Phase
        {
            Loading,
            Playing,
            GameOver,
        }

        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; }
        public Camera Camera { get; private set; }
        public Player Player { get; private set; }
        public Transform WorldRoot { get; private set; }
        public WorldInfo World { get; private set; }
        /// <summary>目前存活的史萊姆數（給測試與除錯用）。</summary>
        public int AliveEnemies => _enemies.Count;
        /// <summary>這一波還沒生出來的數量。</summary>
        public int PendingSpawns => _toSpawn;
        public bool IsPlaying => _phase == Phase.Playing;

        private readonly List<Enemy> _enemies = new List<Enemy>();
        private Phase _phase = Phase.Loading;
        private int _toSpawn;
        private float _spawnTimer;
        private float _breakTimer;
        private float _gameOverTime;
        private int _round;

        /// <summary>
        /// 場上實體（玩家、史萊姆、補血）每幀開頭的防護：回傳 true 才繼續。
        /// GameManager 已銷毀（或實體不是經由本類別生成）時，把實體自己銷毀並回傳 false，避免孤兒物件每幀拋例外洗 log；
        /// 狀態還沒建立（<see cref="State"/> 為 null）時只回傳 false。
        /// </summary>
        public static bool CanDrive(GameManager game, Component entity)
        {
            if (game == null)
            {
                WarnOnce($"{(entity != null ? entity.name : "?")} 的 GameManager 不存在（已銷毀或未經 GameManager 生成），自行銷毀");
                if (entity != null)
                    Destroy(entity.gameObject);
                return false;
            }
            if (game.State == null)
            {
                WarnOnce($"{(entity != null ? entity.name : "?")} 在 GameState 建立前就開始執行，略過");
                return false;
            }
            return true;
        }

        private static bool _warnedCanDrive;

        /// <summary>只記第一次，避免每幀洗 log（每次開機在 <see cref="Create"/> 重設）。</summary>
        private static void WarnOnce(string message)
        {
            if (_warnedCanDrive)
                return;
            _warnedCanDrive = true;
            Debug.LogWarning("[Glowshaft] " + message + "（之後同類情況不再記錄）");
        }

        public static GameManager Create(Camera camera)
        {
            _warnedCanDrive = false;
            var go = new GameObject("GameManager");
            var game = go.AddComponent<GameManager>();
            game.Camera = camera;
            return game;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            InitializeAsync().Forget();
        }

        private async UniTaskVoid InitializeAsync()
        {
            try
            {
                await ArtLibrary.LoadAllAsync();
            }
            catch (Exception ex)
            {
                // ArtLibrary 內部已逐張容錯；走到這裡是意料外的錯誤，記下後照樣用已讀到的與佔位圖開局
                Debug.LogError($"[Glowshaft] 素材讀取發生例外：{ex}");
            }
            if (this == null)
                return;
            try
            {
                State = new GameState();
                Hud.Create(State);
                StartRound();
            }
            catch (Exception ex)
            {
                Fail("遊戲初始化失敗", ex);
            }
        }

        /// <summary>
        /// 致命錯誤：Entry 畫面這時已隱藏，只記 log 的話玩家只會看到黑畫面；改在畫面上顯示訊息並停止遊戲流程。
        /// </summary>
        private void Fail(string what, Exception ex)
        {
            Debug.LogError($"[Glowshaft] {what}：{ex}");
            _fatalError = $"{what}：{ex.Message}";
            _phase = Phase.Loading;
        }

        private string _fatalError;

        private void OnGUI()
        {
            if (_fatalError == null)
                return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 36), wordWrap = true };
            style.normal.textColor = new Color(1f, 0.4f, 0.35f);
            GUI.Label(new Rect(24f, 24f, Screen.width - 48f, Screen.height - 48f), _fatalError + "\n請重啟 App。", style);
        }

        /// <summary>開局（第一次與每次重來）。</summary>
        public void StartRound()
        {
            if (WorldRoot != null)
            {
                // Destroy 要到這一幀結束才生效；先停用，舊的史萊姆、子彈、補血這一幀就不會再跑 Update、
                // 也不會留在物理世界裡擋住新地圖的生成檢查
                WorldRoot.gameObject.SetActive(false);
                Destroy(WorldRoot.gameObject);
            }
            _enemies.Clear();
            Player = null;

            _round++;
            WorldRoot = new GameObject($"World#{_round}").transform;
            var seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            World = WorldBuilder.Build(WorldRoot, seed);
            Player = Player.Create(this, WorldRoot, World.PlayerSpawn);
            SnapCamera();

            State.Reset(GameConfig.PlayerMaxHp);
            _phase = Phase.Playing;
            BeginWave(1);
            Debug.Log($"[Glowshaft] 開局 #{_round}（seed {seed}）");
        }

        private void BeginWave(int wave)
        {
            State.SetWave(wave);
            _toSpawn = GameConfig.WaveEnemyCount(wave);
            _spawnTimer = 0.5f;
            _breakTimer = 0f;
            Debug.Log($"[Glowshaft] 第 {wave} 波，{_toSpawn} 隻");
        }

        private void Update()
        {
            switch (_phase)
            {
                case Phase.Playing:
                    UpdateWaves(Time.deltaTime);
                    break;
                case Phase.GameOver:
                    // 「可以重來」只由這裡決定並寫進 State.CanRestart，UI 依它顯示提示（兩邊不各自計時）
                    if (!State.CanRestart && Time.unscaledTime - _gameOverTime >= GameConfig.GameOverRestartDelay)
                        State.AllowRestart();
                    if (State.CanRestart && GameInput.RestartPressed)
                    {
                        try
                        {
                            StartRound();
                        }
                        catch (Exception ex)
                        {
                            Fail("重新開局失敗", ex);
                        }
                    }
                    break;
            }
        }

        private void UpdateWaves(float dt)
        {
            _enemies.RemoveAll(e => e == null);
            var wave = State.Wave;

            if (_toSpawn > 0)
            {
                _spawnTimer -= dt;
                if (_spawnTimer <= 0f && _enemies.Count < GameConfig.WaveMaxAlive(wave))
                {
                    if (TrySpawnEnemy(wave))
                        _toSpawn--;
                    _spawnTimer = GameConfig.WaveSpawnInterval(wave);
                }
                return;
            }

            if (_enemies.Count > 0)
                return;

            // 這一波清光：給獎勵分數，休息一下再開下一波
            if (_breakTimer <= 0f)
            {
                _breakTimer = GameConfig.WaveBreakSeconds;
                State.AddScore(wave * GameConfig.WaveClearBonusPerWave);
                Debug.Log($"[Glowshaft] 第 {wave} 波清除");
                return;
            }
            _breakTimer -= dt;
            if (_breakTimer <= 0f)
                BeginWave(wave + 1);
        }

        /// <summary>在離玩家夠遠、沒有障礙物的地方生一隻；找不到位置就回傳 false（下次再試）。</summary>
        private bool TrySpawnEnemy(int wave)
        {
            var kind = GameConfig.PickKind(wave);
            var stats = GameConfig.Enemy(kind);
            var area = World.PlayArea;
            var margin = stats.Radius + 0.3f;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                // 偏好從地圖外圈出現
                var p = attempt < 12 ? RandomEdgePoint(area, margin) : RandomInside(area, margin);
                if (Player != null && (p - Player.Position).sqrMagnitude <
                    GameConfig.SpawnMinDistanceFromPlayer * GameConfig.SpawnMinDistanceFromPlayer)
                    continue;
                if (Physics2D.OverlapCircle(p, stats.Radius + 0.1f) != null)
                    continue;
                _enemies.Add(Enemy.Spawn(this, WorldRoot, kind, p, wave));
                return true;
            }
            return false;
        }

        private static Vector2 RandomInside(Rect area, float margin)
        {
            return new Vector2(UnityEngine.Random.Range(area.xMin + margin, area.xMax - margin),
                UnityEngine.Random.Range(area.yMin + margin, area.yMax - margin));
        }

        private static Vector2 RandomEdgePoint(Rect area, float margin)
        {
            var band = 2.5f;
            var p = RandomInside(area, margin);
            switch (UnityEngine.Random.Range(0, 4))
            {
                case 0: p.x = UnityEngine.Random.Range(area.xMin + margin, area.xMin + margin + band); break;
                case 1: p.x = UnityEngine.Random.Range(area.xMax - margin - band, area.xMax - margin); break;
                case 2: p.y = UnityEngine.Random.Range(area.yMin + margin, area.yMin + margin + band); break;
                default: p.y = UnityEngine.Random.Range(area.yMax - margin - band, area.yMax - margin); break;
            }
            return p;
        }

        public void OnEnemyKilled(Enemy enemy, EnemyStats stats)
        {
            _enemies.Remove(enemy);
            if (_phase != Phase.Playing)
                return;
            State.AddScore(stats.Score);
            if (UnityEngine.Random.value < stats.DropChance)
                Pickup.Spawn(this, WorldRoot, enemy.transform.position);
        }

        public void OnPlayerDied()
        {
            if (_phase != Phase.Playing)
                return;
            _phase = Phase.GameOver;
            _gameOverTime = Time.unscaledTime;
            Player.ShowDead();
            State.TriggerGameOver();
            Debug.Log($"[Glowshaft] Game Over：分數 {State.Score}，第 {State.Wave} 波");
        }

        private void LateUpdate()
        {
            if (Player == null || Camera == null)
                return;
            var target = ClampToArea(Player.transform.position);
            var pos = Vector2.Lerp(Camera.transform.position, target, 1f - Mathf.Exp(-8f * Time.deltaTime));
            Camera.transform.position = new Vector3(pos.x, pos.y, Camera.transform.position.z);
        }

        private void SnapCamera()
        {
            if (Camera == null || Player == null)
                return;
            var target = ClampToArea(Player.transform.position);
            Camera.transform.position = new Vector3(target.x, target.y, Camera.transform.position.z);
        }

        /// <summary>相機中心限制在「地圖範圍外擴 1 單位」內，地圖比畫面小的方向就置中。</summary>
        private Vector2 ClampToArea(Vector2 p)
        {
            if (World == null)
                return p;
            var area = World.PlayArea;
            var halfH = Camera.orthographicSize;
            var halfW = halfH * Camera.aspect;
            const float pad = 1f;
            float Clamp(float v, float min, float max, float half) =>
                max - min <= half * 2f ? (min + max) * 0.5f : Mathf.Clamp(v, min + half, max - half);
            return new Vector2(Clamp(p.x, area.xMin - pad, area.xMax + pad, halfW),
                Clamp(p.y, area.yMin - pad, area.yMax + pad, halfH));
        }
    }
}
