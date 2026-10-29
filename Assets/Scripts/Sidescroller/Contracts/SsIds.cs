namespace Sidescroller
{
    /// <summary>各模組共用的字串與常數（素材 location、角色 id、旗標、鏡頭）。</summary>
    public static class SsIds
    {
        public const string LevelStage1 = "SsLevel_Stage1";
        public const string CutsceneIntro = "SsCutscene_Intro";
        public const string CutsceneMentor = "SsCutscene_Mentor";
        public const string CutsceneBossIntro = "SsCutscene_BossIntro";
        public const string CutsceneBossDefeat = "SsCutscene_BossDefeat";

        public const string ActorHero = "hero";
        public const string ActorBoss = "boss";
        public const string ActorMentor = "mentor";
        /// <summary>Boss 血條上的名字（換反派案時改這裡）。</summary>
        public const string BossDisplayName = "河董";

        /// <summary>閘門關閉（tmp_：重生時清掉）。</summary>
        public const string FlagGateClosed = "tmp_boss_gate_closed";
        public const string FlagMentorDone = "mentor_done";
        /// <summary>龜婆的「急件彈弓」「殼帶護片」這局已經發過（演出或保底）；之後玩家自己取下不會再補發。</summary>
        public const string FlagMentorRanged = "got_mentor_ranged";
        public const string FlagMentorShell = "got_mentor_shell";
        public const string FlagStageClear = "stage_clear";
        public static string SeenFlag(string cutsceneLocation) => "seen_" + cutsceneLocation;

        /// <summary>一般鏡頭的正交 size。</summary>
        public const float CameraSize = 6f;
        /// <summary>Boss 房的正交 size。</summary>
        public const float ArenaCameraSize = 6.75f;
        public const int PixelsPerUnit = 64;
    }
}
