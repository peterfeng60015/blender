using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// GameFlow 建敵人與 Boss 時交給 enemy 的資料（player 建立，enemy 只讀）。
    /// 每條命（每次建關卡）一份；重生後舊的作廢，敵人跟著 worldRoot 一起被刪。
    /// </summary>
    public sealed class EnemyContext
    {
        public GameSession Session;
        public CameraRig Rig;
        public LevelInfo Level;
        /// <summary>敵人、子彈、特效都掛在這底下（worldRoot），重生時一起刪。</summary>
        public Transform Parent;
        /// <summary>主角根物件（腳底）。主角一定比敵人先建；用前仍檢查 null。</summary>
        public Transform Hero;

        /// <summary>主角的實體碰撞體（站立膠囊、滾殼圓），由 player 填。</summary>
        public readonly List<Collider2D> HeroBodies = new List<Collider2D>();

        /// <summary>
        /// 讓 collider 和主角的實體碰撞體互不推擠（Layer 碰撞矩陣不能改，改用 Physics2D.IgnoreCollision）。
        /// GameFlow 建好每隻敵人後會對它底下所有非 trigger 的 collider 呼叫一次；之後才新增的實心 collider（例如盾）由 enemy 自己呼叫。
        /// </summary>
        public void IgnoreHero(Collider2D collider)
        {
            if (collider == null)
                return;
            foreach (var body in HeroBodies)
            {
                if (body != null)
                    Physics2D.IgnoreCollision(collider, body, true);
            }
        }
    }

    /// <summary>
    /// Boss 的流程控制（enemy 實作，掛在 Boss 根物件上；GameFlow 用 GetComponent&lt;IBossController&gt;() 取得）。
    /// 建好時 AI 是停的；只有 GameFlow 的 BossArena 處理會呼叫 <see cref="StartFight"/>。
    /// </summary>
    public interface IBossController
    {
        int Hp { get; }
        int MaxHp { get; }
        /// <summary>啟動 AI（每條命最多呼叫一次）。</summary>
        void StartFight();
        /// <summary>停止一切行動（擊敗後 GameFlow 呼叫）；之後不再動、不再造成傷害。</summary>
        void StopAll();
        /// <summary>(hp, maxHp)：受傷時觸發，GameFlow 轉給 session.SetBoss。</summary>
        event Action<int, int> HpChanged;
        /// <summary>血量歸零時觸發一次。</summary>
        event Action Defeated;
    }
}
