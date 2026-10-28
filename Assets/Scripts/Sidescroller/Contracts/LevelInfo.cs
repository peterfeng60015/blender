using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>關卡建好後由 <c>LevelBuilder.Build</c> 交給流程的資料。</summary>
    public sealed class LevelInfo
    {
        /// <summary>鏡頭可以看到的範圍（世界座標）。</summary>
        public Rect CameraBounds;
        /// <summary>Boss 房（鏡頭鎖定範圍）。</summary>
        public Rect ArenaRect;
        /// <summary>角色掉到這個 y 以下＝掉坑（保底，正常靠 KillZone 觸發區）。</summary>
        public float KillY;
        /// <summary>只有角色（不含主角）。</summary>
        public readonly List<SpawnPoint> Spawns = new List<SpawnPoint>();
        public readonly List<CheckpointInfo> Checkpoints = new List<CheckpointInfo>();
        /// <summary>已由 level 建成觸發區，僅供查詢。</summary>
        public readonly List<TriggerInfo> Triggers = new List<TriggerInfo>();

        /// <summary>找不到回 null。</summary>
        public CheckpointInfo FindCheckpoint(string id) => Checkpoints.Find(c => c.Id == id);
    }

    public sealed class SpawnPoint
    {
        /// <summary>"rat"、"pigeon"、"rhino"、"boss"、"npc"</summary>
        public string Kind;
        /// <summary>唯一，例如 "rat_intro"、"boss"、"mentor"；演出用這個找角色。</summary>
        public string Id;
        /// <summary>腳底。</summary>
        public Vector2 Position;
        /// <summary>+1 右、-1 左。</summary>
        public int Facing;
    }

    public sealed class CheckpointInfo
    {
        /// <summary>"cp0"、"cp1"、"cp2"</summary>
        public string Id;
        /// <summary>主角重生的腳底位置。</summary>
        public Vector2 Position;
    }

    public sealed class TriggerInfo
    {
        public string Id;
        public TriggerKind Kind;
        public Rect Area;
        /// <summary>Cutscene／BossArena：演出 location；Hint：提示文字；Checkpoint：存檔點 id；KillZone：空。</summary>
        public string Arg;
    }
}
