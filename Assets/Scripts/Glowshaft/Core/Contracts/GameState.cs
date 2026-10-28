using System;

namespace Glowshaft
{
    /// <summary>
    /// 一局遊戲的數值狀態（血量、分數、波數、是否 Game Over）。由 Core 的 GameManager 擁有並修改，
    /// UI 只訂閱事件、讀屬性，不呼叫會改值的方法。
    ///
    /// 整個遊戲只有一個實例，重來時不換新物件，而是呼叫 <see cref="Reset"/>：
    /// 依序觸發 <see cref="Restarted"/>，再以新值觸發 HpChanged／ScoreChanged／WaveChanged，
    /// 所以 UI 不必在 Restarted 裡自己刷新數字。
    /// </summary>
    public sealed class GameState
    {
        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public int Score { get; private set; }
        /// <summary>目前波數，從 1 開始；0 代表第一波還沒開始。</summary>
        public int Wave { get; private set; }
        public bool IsGameOver { get; private set; }
        /// <summary>
        /// Game Over 後是否已開放重來（按 R 或左鍵）。這是「可以重來」的**唯一來源**：
        /// Core 依 <see cref="GameConfig.GameOverRestartDelay"/> 計時後呼叫 <see cref="AllowRestart"/> 設為 true，
        /// 同時觸發 <see cref="RestartAvailable"/>；UI 依它（或該事件）顯示「按 R 或左鍵重新開始」，不要自己計時。
        /// </summary>
        public bool CanRestart { get; private set; }

        /// <summary>(hp, maxHp)</summary>
        public event Action<int, int> HpChanged;
        /// <summary>(score)</summary>
        public event Action<int> ScoreChanged;
        /// <summary>(wave)：新的一波開始時觸發，UI 可以在這裡顯示「WAVE n」橫幅。</summary>
        public event Action<int> WaveChanged;
        /// <summary>玩家血量歸零。之後 Core 等待玩家按 R 或左鍵重來。</summary>
        public event Action GameOver;
        /// <summary>重來（或第一次開局）時觸發，UI 收到後要收起 Game Over 畫面。</summary>
        public event Action Restarted;
        /// <summary>Game Over 後開放重來的那一刻觸發一次（<see cref="CanRestart"/> 變成 true）。</summary>
        public event Action RestartAvailable;

        public void Reset(int maxHp)
        {
            MaxHp = Math.Max(1, maxHp);
            Hp = MaxHp;
            Score = 0;
            Wave = 0;
            IsGameOver = false;
            CanRestart = false;
            Restarted?.Invoke();
            HpChanged?.Invoke(Hp, MaxHp);
            ScoreChanged?.Invoke(Score);
            WaveChanged?.Invoke(Wave);
        }

        public void SetHp(int hp)
        {
            hp = Math.Max(0, Math.Min(MaxHp, hp));
            if (hp == Hp)
                return;
            Hp = hp;
            HpChanged?.Invoke(Hp, MaxHp);
        }

        public void AddScore(int amount)
        {
            if (amount == 0)
                return;
            Score += amount;
            ScoreChanged?.Invoke(Score);
        }

        public void SetWave(int wave)
        {
            Wave = wave;
            WaveChanged?.Invoke(Wave);
        }

        public void TriggerGameOver()
        {
            if (IsGameOver)
                return;
            IsGameOver = true;
            CanRestart = false;
            GameOver?.Invoke();
        }

        /// <summary>Core 專用：Game Over 後經過延遲，開放重來。</summary>
        public void AllowRestart()
        {
            if (!IsGameOver || CanRestart)
                return;
            CanRestart = true;
            RestartAvailable?.Invoke();
        }
    }
}
