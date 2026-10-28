using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 觸發區（interfaces.md §4.2）：BoxCollider2D isTrigger。只對主角（<c>ActorTag.Kind == Hero</c>）的非 trigger 碰撞體反應，
    /// 自己記重疊中的主角碰撞體：0→1 才送 <c>NotifyTriggerEntered</c>、1→0 才送 <c>NotifyTriggerExited</c>
    /// （主角滾殼換碰撞體、攻擊判定框、地面偵測器都不會產生多餘的進出）。
    /// 計數歸 0 後要再等 <see cref="ExitConfirmSteps"/> 個物理步仍是 0 才送離開：同一步「關掉舊碰撞體、打開新碰撞體」時，
    /// Unity 會先送舊的離開、下一步才送新的進入，不等的話會閃一次離開再進入。
    /// </summary>
    public sealed class LevelTrigger : MonoBehaviour
    {
        private const int ExitConfirmSteps = 2;

        private TriggerInfo _info;
        private GameSession _session;
        private readonly List<Collider2D> _inside = new List<Collider2D>();
        /// <summary>&gt;0：離開待確認，倒數中的物理步數。</summary>
        private int _exitPending;

        public TriggerInfo Info => _info;

        internal static LevelTrigger Create(Transform parent, TriggerInfo info, GameSession session)
        {
            var go = new GameObject($"Trigger_{info.Id}");
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = info.Area.center;
            box.size = info.Area.size;
            var trigger = go.AddComponent<LevelTrigger>();
            trigger._info = info;
            trigger._session = session;
            return trigger;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_session == null || !IsHeroBody(other) || _inside.Contains(other))
                return;
            _inside.RemoveAll(c => c == null);
            _inside.Add(other);
            if (_inside.Count != 1)
                return;
            if (_exitPending > 0)
                _exitPending = 0; // 離開還沒確認就又進來：對流程來說一直都在裡面，不重送進入
            else
                Notify(true);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (_session == null || !_inside.Remove(other))
                return;
            _inside.RemoveAll(c => c == null);
            if (_inside.Count == 0)
                _exitPending = ExitConfirmSteps;
        }

        private void FixedUpdate()
        {
            if (_exitPending <= 0 || --_exitPending > 0)
                return;
            _inside.RemoveAll(c => c == null);
            if (_inside.Count == 0)
                Notify(false);
        }

        /// <summary>重建關卡時整個關卡先被關掉：清掉計數就好，不送離開（流程那邊無害，見 interfaces.md §4.2）。</summary>
        private void OnDisable()
        {
            _inside.Clear();
            _exitPending = 0;
        }

        private static bool IsHeroBody(Collider2D other)
        {
            if (other == null || other.isTrigger)
                return false;
            var tag = other.GetComponentInParent<ActorTag>();
            return tag != null && tag.Kind == ActorKind.Hero;
        }

        private void Notify(bool entered)
        {
            try
            {
                if (entered)
                    _session.NotifyTriggerEntered(_info);
                else
                    _session.NotifyTriggerExited(_info);
            }
            catch (Exception ex)
            {
                // 物理回呼裡不讓例外往外丟，記下來就好
                Debug.LogException(ex);
            }
        }
    }
}
