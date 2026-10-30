namespace Sidescroller
{
    /// <summary>介面固定文字（design.md §5「遊戲內提示與介面文字」）。集中在這裡，Editor 檢查工具才能一起檢查缺字。</summary>
    public static class StoryTexts
    {
        public const string GameTitle = "熱浪急件";
        public const string MenuTitle = "選擇遊戲";
        public const string MenuHint = "↑↓：選擇　Enter：確定";
        public const string MenuHintTouch = "點一下遊戲名稱開始";
        public const string MenuErrorPrefix = "啟動失敗：";
        /// <summary>主角 3D 模型的授權標註（Meshy 免費版 CC BY 4.0 要求署名），選單右下角小字。</summary>
        public const string MenuModelCredit = "主角模型：Meshy（CC BY 4.0）";

        public const string DialogueNext = "▼";
        public const string DialogueHelp = "Space／Enter：繼續　Esc：暫停";
        public const string DialogueHelpTouch = "點畫面：繼續　右上 Ⅱ：暫停";

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
        public const string SwitchCostumeTouch = "點「換裝」換制服";
        public const string BossLabel = "BOSS";

        #region 觸控
        public const string TouchJump = "跳";
        public const string TouchMelee = "甩包";
        public const string TouchShoot = "彈弓";
        public const string TouchRoll = "滾殼";
        public const string TouchSwitch = "換裝";
        public const string TouchPause = "Ⅱ";
        public const string RotateHint = "請把手機轉成橫向";
        public const string RotateSub = "遊戲已暫停，轉回橫向後繼續";

        /// <summary>
        /// 關卡資料（SsLevel_*.txt 的 hint 觸發區）裡寫鍵盤按鍵的提示 → 觸控版；兩個陣列一一對應。
        /// 改關卡提示原文時要一起改這裡，對不上就照原文顯示。
        /// </summary>
        private static readonly string[] KeyboardHints =
        {
            "A／D 或 ←／→：移動　J：甩包攻擊",
            "Space：跳躍（按住跳更高）",
            "S＋Space：從木板往下跳",
            "C：換制服（日班：甩包更痛／特急：跑更快）",
            "K：彈弓射擊　W＋K：往斜上射",
            "L：滾殼衝撞，可以撞破盾牌",
        };

        private static readonly string[] TouchHints =
        {
            "左側搖桿：移動　「甩包」鈕：甩包攻擊",
            "「跳」鈕：跳躍（按住跳更高）",
            "搖桿往下＋「跳」：從木板往下跳",
            "「換裝」鈕：換制服（日班：甩包更痛／特急：跑更快）",
            "「彈弓」鈕：射擊　搖桿往上＋「彈弓」：往斜上射",
            "「滾殼」鈕：衝撞，可以撞破盾牌",
        };

        /// <summary>觸控模式時把關卡提示換成觸控版（沒有對應的照原文）。</summary>
        public static string ForInput(string hint, bool touch)
        {
            if (!touch || string.IsNullOrEmpty(hint))
                return hint;
            for (var i = 0; i < KeyboardHints.Length; i++)
            {
                if (KeyboardHints[i] == hint)
                    return TouchHints[i];
            }
            return hint;
        }
        #endregion

        #region 背包與強化（loot-design.md §5、§6）
        public const string BagTitle = "背包";
        public const string BagParts = "零件　";
        public const string SlotMelee = "近戰";
        public const string SlotRanged = "遠程";
        public const string SlotShell = "殼甲";
        public const string BagHelp = "方向鍵：移動　J：拿起／放下　E：裝上　Esc：關閉";
        public const string BagHelpTouch = "點道具拿起，再點格子放下　拖到裝備欄：裝上　拖到外面：丟掉";
        public const string Close = "關閉";
        public const string DropAskPrefix = "把「";
        public const string DropAskSuffix = "」丟到地上？";
        public const string DropYes = "丟掉";
        public const string DropNo = "取消";
        public const string TooltipLevel = "強化等級　+";
        public const string TooltipEquipped = "身上裝備";
        public const string TooltipHeld = "手上";

        public const string EnhanceTitle = "補給站：強化";
        public const string EnhanceEquipped = "裝備中";
        public const string EnhanceLevel = "等級";
        public const string EnhanceNext = "下一級效果";
        public const string EnhanceCost = "需要零件";
        public const string EnhanceOwnedPrefix = "（目前持有 ";
        public const string EnhanceOwnedSuffix = "）";
        public const string EnhanceRate = "成功率";
        public const string EnhanceButton = "強化";
        public const string EnhanceMax = "已達最高等級";
        public const string EnhanceNoParts = "零件不足";
        public const string EnhanceEmpty = "沒有可以強化的裝備";
        public const string EnhanceSuccess = "強化成功！";
        public const string EnhanceFail = "強化失敗：零件消耗，等級不變";
        public const string EnhanceFailDown = "強化失敗：零件消耗，等級降為 +";
        public const string EnhanceHelp = "↑↓：選擇裝備　J：強化　Esc：關閉";
        public const string EnhanceHelpTouch = "點左邊選裝備，再按「強化」";
        public const string StationPrompt = "E：強化";
        public const string TouchEnhance = "強化";
        #endregion

        /// <summary>動態文字可能用到的字元（通關時間、血量、強化等級、零件數等）。</summary>
        public const string Digits = "0123456789:+-－×%→▲▼—";

        /// <summary>全部固定文字（含觸控版關卡提示），給 Editor 檢查工具檢查缺字（KeyboardHints 是關卡資料原文，由關卡檢查涵蓋）。</summary>
        public static readonly string[] All = Concat(new[]
        {
            GameTitle, MenuTitle, MenuHint, MenuHintTouch, MenuErrorPrefix, MenuModelCredit,
            DialogueNext, DialogueHelp, DialogueHelpTouch,
            PauseTitle, PauseResume, PauseSkip, PauseRespawn, PauseQuit,
            ClearTitle, ClearTimePrefix, ClearRestart, ClearQuit,
            GrantPrefix, SwitchCostume, SwitchCostumeTouch, BossLabel, Digits,
            TouchJump, TouchMelee, TouchShoot, TouchRoll, TouchSwitch, TouchPause, RotateHint, RotateSub,
            BagTitle, BagParts, SlotMelee, SlotRanged, SlotShell, BagHelp, BagHelpTouch, Close,
            DropAskPrefix, DropAskSuffix, DropYes, DropNo, TooltipLevel, TooltipEquipped, TooltipHeld,
            EnhanceTitle, EnhanceEquipped, EnhanceLevel, EnhanceNext, EnhanceCost, EnhanceOwnedPrefix, EnhanceOwnedSuffix,
            EnhanceRate, EnhanceButton, EnhanceMax, EnhanceNoParts, EnhanceEmpty, EnhanceSuccess, EnhanceFail, EnhanceFailDown,
            EnhanceHelp, EnhanceHelpTouch, StationPrompt, TouchEnhance,
        }, TouchHints);

        private static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }
    }
}
