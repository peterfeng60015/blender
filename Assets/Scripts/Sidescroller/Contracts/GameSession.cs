using System;
using System.Threading;

namespace Sidescroller
{
    /// <summary>
    /// 一次遊玩的共用狀態（整局只有一個，活得比關卡久）。Set…／Notify… 依註明的角色呼叫，其他角色只讀屬性、訂閱事件。
    /// 訂閱者必須在 OnDestroy 取消訂閱。事件在主執行緒同步觸發，處理器不要丟例外。
    /// </summary>
    public sealed class GameSession
    {
        private int _inputLocks;

        public GameSession(CancellationToken lifetime)
        {
            Lifetime = lifetime;
            Loadout = new Loadout();
            Flags = new StoryFlags();
            Loot = new LootState(Loadout);
        }

        /// <summary>Shutdown 時取消；所有 await 都要帶它。</summary>
        public CancellationToken Lifetime { get; }
        public Loadout Loadout { get; }
        public StoryFlags Flags { get; }
        /// <summary>背包、裝備欄、零件（data 實作；整局有效，死亡不扣）。</summary>
        public LootState Loot { get; }

        #region 主角血量（player 改，story 讀）
        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        /// <summary>(hp, maxHp)</summary>
        public event Action<int, int> HpChanged;

        public void SetHp(int hp, int maxHp)
        {
            if (Hp == hp && MaxHp == maxHp)
                return;
            Hp = hp;
            MaxHp = maxHp;
            HpChanged?.Invoke(hp, maxHp);
        }
        #endregion

        #region Boss 血條（player 改，story 讀）
        public bool BossBarVisible { get; private set; }
        public int BossHp { get; private set; }
        public int BossMaxHp { get; private set; }
        public string BossName { get; private set; }
        public event Action BossChanged;

        public void SetBoss(bool visible, int hp, int maxHp, string name)
        {
            if (BossBarVisible == visible && BossHp == hp && BossMaxHp == maxHp && BossName == name)
                return;
            BossBarVisible = visible;
            BossHp = hp;
            BossMaxHp = maxHp;
            BossName = name;
            BossChanged?.Invoke();
        }
        #endregion

        #region 提示文字（player 的流程依觸發區改，story 的 HUD 顯示）
        /// <summary>null＝不顯示。</summary>
        public string Hint { get; private set; }
        public event Action<string> HintChanged;

        public void SetHint(string text)
        {
            if (Hint == text)
                return;
            Hint = text;
            HintChanged?.Invoke(text);
        }
        #endregion

        #region 演出、暫停與輸入鎖
        /// <summary>story 設（PlayCutsceneAsync 期間）。</summary>
        public bool CutsceneActive { get; private set; }
        public void SetCutsceneActive(bool active) => CutsceneActive = active;

        /// <summary>story 的暫停選單設；player 收到後改 Time.timeScale。</summary>
        public bool Paused { get; private set; }
        public event Action<bool> PausedChanged;

        public void SetPaused(bool paused)
        {
            if (Paused == paused)
                return;
            Paused = paused;
            PausedChanged?.Invoke(paused);
        }

        /// <summary>player 的流程用（重生淡出入時）；與 <see cref="PopInputLock"/> 成對。</summary>
        public void PushInputLock() => _inputLocks++;

        public void PopInputLock()
        {
            if (_inputLocks > 0)
                _inputLocks--;
        }

        public bool InputLocked => CutsceneActive || Paused || _inputLocks > 0;
        #endregion

        #region 觸發區（level 通知，player 的流程處理）
        public event Action<TriggerInfo> TriggerEntered;
        public event Action<TriggerInfo> TriggerExited;
        public void NotifyTriggerEntered(TriggerInfo trigger) => TriggerEntered?.Invoke(trigger);
        public void NotifyTriggerExited(TriggerInfo trigger) => TriggerExited?.Invoke(trigger);
        #endregion

        #region 流程（story 要求、player 執行）
        /// <summary>暫停選單「從檢查點重來」。</summary>
        public event Action RespawnRequested;
        public void RequestRespawn() => RespawnRequested?.Invoke();

        /// <summary>通關（參數＝通關秒數），story 顯示通關畫面。</summary>
        public event Action<float> StageCleared;
        public void NotifyStageCleared(float seconds) => StageCleared?.Invoke(seconds);
        #endregion
    }
}
