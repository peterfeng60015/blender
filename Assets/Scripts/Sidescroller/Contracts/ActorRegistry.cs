using System.Collections.Generic;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>演出用 id 找角色。player 在 Boot、重建關卡、Shutdown 時 Clear。</summary>
    public static class ActorRegistry
    {
        private static readonly List<ICutsceneActor> Actors = new List<ICutsceneActor>();

        /// <summary>同 id 已存在：記警告並以新的取代。</summary>
        public static void Register(ICutsceneActor actor)
        {
            if (actor == null)
                return;
            var index = Actors.FindIndex(a => a.Id == actor.Id);
            if (index >= 0)
            {
                if (!ReferenceEquals(Actors[index], actor))
                    Debug.LogWarning($"[Sidescroller.ActorRegistry] id「{actor.Id}」已存在，以新的取代");
                Actors[index] = actor;
                return;
            }
            Actors.Add(actor);
        }

        /// <summary>只移除同一個實例。</summary>
        public static void Unregister(ICutsceneActor actor)
        {
            var index = Actors.FindIndex(a => ReferenceEquals(a, actor));
            if (index >= 0)
                Actors.RemoveAt(index);
        }

        /// <summary>找不到回 null。</summary>
        public static ICutsceneActor Find(string id) => Actors.Find(a => a.Id == id);

        public static void Clear() => Actors.Clear();
    }
}
