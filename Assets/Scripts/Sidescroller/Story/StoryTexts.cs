namespace Sidescroller
{
    /// <summary>介面固定文字（design.md §5「遊戲內提示與介面文字」）。集中在這裡，Editor 檢查工具才能一起檢查缺字。</summary>
    public static class StoryTexts
    {
        public const string GameTitle = "熱浪急件";
        public const string GlowshaftTitle = "螢火礦坑";
        public const string MenuTitle = "選擇遊戲";
        public const string MenuHint = "↑↓：選擇　Enter：確定";
        public const string MenuErrorPrefix = "啟動失敗：";

        public const string DialogueNext = "▼";
        public const string DialogueHelp = "Space／Enter：繼續　Esc：暫停";

        public const string PauseTitle = "暫停";
        public const string PauseResume = "繼續";
        public const string PauseSkip = "跳過演出";
        public const string PauseRespawn = "從檢查點重來";
        public const string PauseQuit = "回到選擇遊戲";

        public const string ClearTitle = "急件送達！";
        public const string ClearTimePrefix = "通關時間　";
        public const string ClearRestart = "再玩一次";
        public const string ClearQuit = "回到選擇遊戲";

        public const string GrantPrefix = "獲得：";
        public const string SwitchCostume = "C 換制服";
        public const string BossLabel = "BOSS";

        /// <summary>動態文字可能用到的字元（通關時間、血量等）。</summary>
        public const string Digits = "0123456789:";

        /// <summary>全部固定文字，給 Editor 檢查工具檢查缺字。</summary>
        public static readonly string[] All =
        {
            GameTitle, GlowshaftTitle, MenuTitle, MenuHint, MenuErrorPrefix,
            DialogueNext, DialogueHelp,
            PauseTitle, PauseResume, PauseSkip, PauseRespawn, PauseQuit,
            ClearTitle, ClearTimePrefix, ClearRestart, ClearQuit,
            GrantPrefix, SwitchCostume, BossLabel, Digits,
        };
    }
}
