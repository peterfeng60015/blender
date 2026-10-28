using UnityEngine;

namespace Sidescroller
{
    /// <summary>演出可操控的角色（player 的主角、雜兵、Boss、NPC 實作）。</summary>
    public interface ICutsceneActor
    {
        string Id { get; }
        /// <summary>位置＝腳底。</summary>
        Transform Root { get; }
        /// <summary>+1 右、-1 左。</summary>
        int Facing { get; }
        /// <summary>停玩家輸入或 AI、水平速度歸零；重力照常（在空中會自己落地）。</summary>
        void BeginScripted();
        /// <summary>回到 BeginScripted 之前的狀態；本來就停著的（未啟動或已擊敗的 Boss）維持不動。</summary>
        void EndScripted();
        void SetFacing(int dir);
        /// <summary>自己用物理水平走到 x，自己播 Run／Walk，到了停下播 Idle。</summary>
        void ScriptedMoveTo(float x, float speed);
        bool IsScriptedMoving { get; }
        /// <summary>跳過演出用：瞬移到 x（y 不變）並停下。</summary>
        void Teleport(float x);
        /// <summary>沒有這個動作回 false；loop=false 播完回「待機動作」。</summary>
        bool PlayAction(string action, bool loop);
        /// <summary>一輪的秒數；沒有這個動作回 0。</summary>
        float GetActionDuration(string action);
    }
}
