using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 一局的組裝與流程（interfaces.md §5）：初始化、建關卡（第一次與每次重生）、觸發佇列、死亡重生、
    /// Boss 房與 Boss 擊敗、暫停。由 <see cref="SidescrollerGame.Boot"/> 建立，Shutdown 時跟著場景一起消失。
    /// <para>
    /// 兩種 token：<c>_lifetime</c>＝整局（Shutdown 取消）；<c>_lifeCts</c>＝「這條命」，每次建關卡新開、重生前取消。
    /// 屬於這條命的 await（演出、鏡頭、Boss 房、擊敗流程）一律帶 <c>_lifeCts.Token</c>，被取消時安靜結束。
    /// </para>
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        private const string Log = "[Sidescroller.GameFlow]";
        private const float FadeTime = 0.5f;
        private const float ArenaZoomTime = 0.8f;
        private const float BossLandWait = 1.5f;

        private Camera _camera;
        private CancellationToken _lifetime;
        private string _levelText;
        private GameSession _session;
        private CameraRig _rig;
        private StoryUi _story;

        #region 這條命
        private GameObject _worldRoot;
        private LevelInfo _info;
        private CancellationTokenSource _lifeCts;
        /// <summary>每次建關卡加一；舊一條命的 async 收尾時比對，不去動新一條命的狀態。</summary>
        private int _lifeGen;
        private HeroController _hero;
        private IBossController _boss;
        private bool _bossArenaHandled;
        private bool _bossDefeated;
        #endregion

        #region 觸發佇列
        private readonly List<TriggerInfo> _queue = new List<TriggerInfo>();
        /// <summary>主角目前重疊中的提示區（依進入順序）；相鄰提示區交界來回走時，離開一個後改顯示還在裡面的那一個。</summary>
        private readonly List<TriggerInfo> _activeHints = new List<TriggerInfo>();
        private bool _triggersOpen;
        private bool _processing;
        #endregion

        private string _checkpointId = "cp0";
        private bool _respawning;
        private bool _cleared;
        private float _elapsed;

        /// <summary>整局的共用狀態（初始化完成前是 null）。</summary>
        public GameSession Session => _session;
        public HeroController Hero => _hero;
        public bool Ready { get; private set; }

        #region 建立與初始化（§5.1）
        public static GameFlow Create(Camera camera, CancellationToken lifetime)
        {
            var go = new GameObject("GameFlow");
            var flow = go.AddComponent<GameFlow>();
            flow._camera = camera;
            flow._lifetime = lifetime;
            return flow;
        }

        /// <summary>下一幀才開始初始化：Boot 的同步部分先完整結束，初始化失敗回選單時才不會和 Boot 交錯。</summary>
        private void Start() => InitAsync().Forget();

        private async UniTaskVoid InitAsync()
        {
            try
            {
                await SsArt.LoadAllAsync(_lifetime);
                _levelText = await SsArt.LoadTextAsync(SsIds.LevelStage1, _lifetime);
                _lifetime.ThrowIfCancellationRequested();
                _session = new GameSession(_lifetime);
                _session.TriggerEntered += OnTriggerEntered;
                _session.TriggerExited += OnTriggerExited;
                _session.RespawnRequested += OnRespawnRequested;
                _session.PausedChanged += OnPausedChanged;
                _rig = CameraRig.Create(_camera);
                _story = StoryUi.Create(_session, _rig);
                Ready = true;
                Debug.Log($"{Log} 初始化完成，建第一條命");
                await BuildWorldAsync();
            }
            catch (OperationCanceledException)
            {
                // Shutdown：安靜結束
            }
            catch (Exception ex)
            {
                if (_lifetime.IsCancellationRequested)
                    return;
                Debug.LogError($"{Log} 初始化失敗：{ex}");
                SidescrollerGame.ReturnToMenu("熱浪急件初始化失敗：" + ex.Message);
            }
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.TriggerEntered -= OnTriggerEntered;
                _session.TriggerExited -= OnTriggerExited;
                _session.RespawnRequested -= OnRespawnRequested;
                _session.PausedChanged -= OnPausedChanged;
            }
            DisposeLife();
        }
        #endregion

        #region 建關卡（§5.2）
        private async UniTask BuildWorldAsync()
        {
            _lifetime.ThrowIfCancellationRequested();
            _session.PushInputLock();
            try
            {
                DisposeLife();
                _lifeGen++;
                _queue.Clear();
                _activeHints.Clear();
                _triggersOpen = false;
                _processing = false;
                _bossArenaHandled = false;
                _bossDefeated = false;
                _boss = null;
                _hero = null;
                ActorRegistry.Clear();
                if (_worldRoot != null)
                {
                    _worldRoot.SetActive(false);
                    Destroy(_worldRoot);
                    _worldRoot = null;
                }
                _session.Flags.ClearTemporary();
                _lifeCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
                var token = _lifeCts.Token;

                _worldRoot = new GameObject("World");
                _info = LevelBuilder.Build(_worldRoot.transform, _levelText, _session, _rig);
                SpawnActors();

                _session.SetHp(_hero.MaxHp, _hero.MaxHp);
                _session.SetBoss(false, 0, 0, SsIds.BossDisplayName);
                _session.SetHint(null);
                _rig.ResetView();
                _rig.SetBounds(_info.CameraBounds);
                _rig.Follow(_hero.transform, true);

                // 淡入期間主角無敵：這段時間 _respawning 仍為 true，被打死的話重生會被忽略
                _hero.PermanentInvincible = true;
                await _story.FadeAsync(0f, FadeTime, token);
                token.ThrowIfCancellationRequested();
                _hero.PermanentInvincible = false;
                _triggersOpen = true;
            }
            finally
            {
                _session.PopInputLock();
            }
        }

        private void SpawnActors()
        {
            var cp = _info.FindCheckpoint(_checkpointId);
            if (cp == null)
            {
                Debug.LogWarning($"{Log} 找不到存檔點 {_checkpointId}，改用第一個");
                cp = _info.Checkpoints.Count > 0 ? _info.Checkpoints[0] : new CheckpointInfo { Id = "cp0", Position = new Vector2(3f, 2f) };
                _checkpointId = cp.Id;
            }
            _hero = HeroController.Create(_worldRoot.transform, cp.Position, 1, _session);
            _hero.Died += OnHeroDied;

            var ctx = new EnemyContext
            {
                Session = _session,
                Rig = _rig,
                Level = _info,
                Parent = _worldRoot.transform,
                Hero = _hero.transform
            };
            ctx.HeroBodies.Add(_hero.Body);

            foreach (var spawn in _info.Spawns)
            {
                try
                {
                    if (spawn.Kind == "npc")
                    {
                        Mentor.Create(spawn, ctx);
                        continue;
                    }
                    var go = EnemySpawner.Create(spawn, ctx);
                    var boss = go.GetComponent<IBossController>();
                    if (boss != null && spawn.Kind == "boss")
                    {
                        if (_boss != null)
                            Debug.LogWarning($"{Log} 關卡有兩隻 Boss，只用第一隻");
                        else
                            BindBoss(boss);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{Log} 建立角色 {spawn.Id}（{spawn.Kind}）失敗：{ex}");
                }
            }

            try
            {
                SupplyStation.CreateAll(_worldRoot.transform, _info, _session, _story);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{Log} 建立補給站失敗：{ex}");
            }
        }

        private void DisposeLife()
        {
            if (_lifeCts == null)
                return;
            _lifeCts.Cancel();
            _lifeCts.Dispose();
            _lifeCts = null;
        }
        #endregion

        #region 每幀
        private void Update()
        {
            if (!Ready)
                return;
            if (!_cleared)
                _elapsed += Time.deltaTime;

            // 掉出關卡的保底（正常靠 KillZone 觸發區）
            if (_hero != null && _info != null && !_respawning && _hero.transform.position.y < _info.KillY)
                _hero.FallIntoPit();

            if (_triggersOpen && !_processing && !_respawning && !_session.Paused && _queue.Count > 0)
            {
                var trigger = _queue[0];
                _queue.RemoveAt(0);
                ProcessTriggerAsync(trigger).Forget();
            }
        }
        #endregion

        #region 觸發區（§5.3）
        private void OnTriggerEntered(TriggerInfo trigger)
        {
            if (trigger == null || _hero == null)
                return;
            if (trigger.Kind == TriggerKind.Hint)
            {
                _activeHints.Remove(trigger);
                _activeHints.Add(trigger);
                _session.SetHint(trigger.Arg);
                return;
            }
            if (_bossDefeated)
                return;   // 擊敗流程接手後不再處理觸發
            _queue.Add(trigger);
        }

        /// <summary>
        /// 只處理提示區的離開，而且只認目前重疊清單裡的那一個（重建關卡時舊觸發區送來的離開不在清單內，直接忽略）。
        /// 顯示中的是這句才換：改顯示最後進入、還在裡面的提示，沒有就清掉。
        /// </summary>
        private void OnTriggerExited(TriggerInfo trigger)
        {
            if (trigger == null || trigger.Kind != TriggerKind.Hint || !_activeHints.Remove(trigger))
                return;
            if (_session.Hint == trigger.Arg)
                _session.SetHint(_activeHints.Count > 0 ? _activeHints[_activeHints.Count - 1].Arg : null);
        }

        private async UniTaskVoid ProcessTriggerAsync(TriggerInfo trigger)
        {
            var gen = _lifeGen;
            var token = _lifeCts.Token;
            _processing = true;
            try
            {
                switch (trigger.Kind)
                {
                    case TriggerKind.Cutscene:
                        await PlayCutsceneOnceAsync(trigger.Arg, token);
                        break;
                    case TriggerKind.Checkpoint:
                        AdvanceCheckpoint(trigger.Arg);
                        break;
                    case TriggerKind.BossArena:
                        await EnterBossArenaAsync(trigger.Arg, token);
                        break;
                    case TriggerKind.KillZone:
                        // 排隊期間可能已被 KillY 保底拉回：主角不在坑裡就不再扣血
                        if (_hero != null && _hero.transform.position.y < trigger.Area.yMax)
                            _hero.FallIntoPit();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // 重生或 Shutdown：安靜結束
            }
            catch (Exception ex)
            {
                Debug.LogError($"{Log} 處理觸發區 {trigger.Id}（{trigger.Kind}）失敗：{ex}");
            }
            finally
            {
                if (gen == _lifeGen)
                    _processing = false;
            }
        }

        /// <summary>沒看過才播；播完（沒被取消）設 seen_。龜婆的演出另外做保底發放。</summary>
        private async UniTask PlayCutsceneOnceAsync(string location, CancellationToken token)
        {
            if (string.IsNullOrEmpty(location) || _session.Flags.Get(SsIds.SeenFlag(location)))
                return;
            await PlayCutsceneSafeAsync(location, token);
            _session.Flags.Set(SsIds.SeenFlag(location));
            if (location == SsIds.CutsceneMentor)
                EnsureMentorRewards();
        }

        /// <summary>播演出；失敗（回 false 或丟非取消的例外）記錯誤後照樣算播完，被取消則往外丟取消。</summary>
        private async UniTask PlayCutsceneSafeAsync(string location, CancellationToken token)
        {
            try
            {
                await _story.PlayCutsceneAsync(location, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{Log} 演出 {location} 執行失敗，當成播完繼續：{ex}");
            }
            token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 龜婆演出播完的保底（演出腳本壞掉時不卡關）：還沒拿到「急件彈弓」「殼帶護片」、特急制服就補發。
        /// 只看「這局有沒有發過」（mentor 獎勵旗標），不看裝備欄：玩家之後自己取下裝備不會被補。
        /// </summary>
        private void EnsureMentorRewards()
        {
            var loadout = _session.Loadout;
            var ranged = _session.Flags.Get(SsIds.FlagMentorRanged);
            var shell = _session.Flags.Get(SsIds.FlagMentorShell);
            if (ranged && shell && loadout.HasCostume(CostumeId.Express))
                return;
            Debug.LogWarning($"{Log} 龜婆演出播完但獎勵沒發齊，保底補發急件彈弓、殼帶護片、特急制服");
            if (!ranged)
                LootTables.GiveMentorGear(_session, EquipSlot.Ranged);
            if (!shell)
                LootTables.GiveMentorGear(_session, EquipSlot.Shell);
            if (!loadout.HasCostume(CostumeId.Express))
                loadout.GrantCostume(CostumeId.Express, true);
        }

        /// <summary>存檔點只往前（依關卡資料裡的順序），不會回到較早的存檔點。</summary>
        private void AdvanceCheckpoint(string id)
        {
            var next = IndexOfCheckpoint(id);
            if (next < 0)
            {
                Debug.LogWarning($"{Log} 觸發區指到不存在的存檔點 {id}");
                return;
            }
            if (next > IndexOfCheckpoint(_checkpointId))
            {
                _checkpointId = id;
                Debug.Log($"{Log} 存檔點更新為 {id}");
            }
        }

        private int IndexOfCheckpoint(string id)
        {
            for (var i = 0; i < _info.Checkpoints.Count; i++)
            {
                if (_info.Checkpoints[i].Id == id)
                    return i;
            }
            return -1;
        }
        #endregion

        #region Boss（§5.3 BossArena、§5.4 擊敗）
        private void BindBoss(IBossController boss)
        {
            _boss = boss;
            var gen = _lifeGen;
            boss.HpChanged += (hp, max) =>
            {
                if (gen == _lifeGen && _session.BossBarVisible)
                    _session.SetBoss(true, hp, max, SsIds.BossDisplayName);
            };
            boss.Defeated += () =>
            {
                if (gen == _lifeGen && _lifeCts != null && !_respawning)
                    BossDefeatedAsync(_lifeCts.Token).Forget();
            };
        }

        private async UniTask EnterBossArenaAsync(string location, CancellationToken token)
        {
            if (_bossArenaHandled)
                return;
            _bossArenaHandled = true;
            if (!string.IsNullOrEmpty(location) && !_session.Flags.Get(SsIds.SeenFlag(location)))
            {
                await PlayCutsceneSafeAsync(location, token);
                _session.Flags.Set(SsIds.SeenFlag(location));
            }
            _session.Flags.Set(SsIds.FlagGateClosed);
            await _rig.ZoomAsync(SsIds.ArenaCameraSize, ArenaZoomTime, token);
            token.ThrowIfCancellationRequested();
            _rig.LockTo(_info.ArenaRect);
            if (_boss == null)
            {
                Debug.LogWarning($"{Log} 關卡沒有 Boss，Boss 房只關門");
                return;
            }
            _boss.StartFight();
            _session.SetBoss(true, _boss.Hp, _boss.MaxHp, SsIds.BossDisplayName);
        }

        /// <summary>停 AI、主角無敵、上輸入鎖（之後不再放開，通關畫面接手）→ 等落地 → 收血條 → 擊敗演出 → 通關。</summary>
        private async UniTaskVoid BossDefeatedAsync(CancellationToken token)
        {
            if (_bossDefeated)
                return;
            _bossDefeated = true;
            _triggersOpen = false;
            _queue.Clear();
            _boss?.StopAll();
            if (_hero != null)
                _hero.PermanentInvincible = true;
            _session.PushInputLock();
            var keepLock = false;
            try
            {
                var waited = 0f;
                while (_hero != null && !_hero.IsGrounded && waited < BossLandWait)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    waited += Time.deltaTime;
                }
                token.ThrowIfCancellationRequested();
                _session.SetBoss(false, 0, _boss != null ? _boss.MaxHp : 0, SsIds.BossDisplayName);
                await PlayCutsceneSafeAsync(SsIds.CutsceneBossDefeat, token);
                _session.Flags.Set(SsIds.SeenFlag(SsIds.CutsceneBossDefeat));
                _cleared = true;
                keepLock = true;
                Debug.Log($"{Log} 通關，時間 {_elapsed:0.0} 秒");
                _session.NotifyStageCleared(_elapsed);
            }
            catch (OperationCanceledException)
            {
                // 擊敗流程中途重生或 Shutdown
            }
            finally
            {
                if (!keepLock)
                    _session.PopInputLock();
            }
        }
        #endregion

        #region 死亡、重生、暫停（§5.4）
        private void OnHeroDied()
        {
            if (_bossDefeated)
                return;   // 擊敗流程中主角無敵；保險起見也不讓死亡打斷擊敗流程
            Debug.Log($"{Log} 主角倒下，從 {_checkpointId} 重來");
            RespawnAsync().Forget();
        }

        private void OnRespawnRequested()
        {
            if (_cleared || _bossDefeated)
                return;   // Boss 擊敗後由擊敗流程與通關畫面接手
            RespawnAsync().Forget();
        }

        private async UniTaskVoid RespawnAsync()
        {
            if (_respawning || _cleared || !Ready)
                return;
            _respawning = true;
            _session.PushInputLock();
            try
            {
                DisposeLife();
                _triggersOpen = false;
                _queue.Clear();
                await _story.FadeAsync(1f, FadeTime, _lifetime);
                _lifetime.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                _respawning = false;
                return;
            }
            finally
            {
                _session.PopInputLock();
            }
            try
            {
                await BuildWorldAsync();
            }
            catch (OperationCanceledException)
            {
                // Shutdown，或建好前又被要求重生
            }
            catch (Exception ex)
            {
                Debug.LogError($"{Log} 重建關卡失敗：{ex}");
                if (!_lifetime.IsCancellationRequested)
                    SidescrollerGame.ReturnToMenu("重建關卡失敗：" + ex.Message);
            }
            finally
            {
                _respawning = false;
            }
        }

        private void OnPausedChanged(bool paused) => Time.timeScale = paused ? 0f : 1f;
        #endregion
    }
}
