using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 執行一份 <see cref="CutsceneScript"/>（cutscene-format.md §3、§4）。一次演出建一個，由 <see cref="StoryUi.PlayCutsceneAsync"/> 擁有。
    /// 角色只透過 <see cref="ActorRegistry"/>／<see cref="ICutsceneActor"/> 操控；第一次用到時 BeginScripted，結束時 EndScripted。
    /// 跳過：取消目前這個指令的 token，然後這個指令與剩下的指令改用立即模式（最後狀態和正常播完一樣）。
    /// 所有等待都用 scaled time（暫停時演出也停）。
    /// </summary>
    public sealed class CutsceneRunner
    {
        private readonly StoryUi _ui;
        private readonly GameSession _session;
        private readonly CameraRig _rig;
        private readonly CutsceneScript _script;
        private readonly CancellationToken _ct;
        private readonly List<ICutsceneActor> _used = new List<ICutsceneActor>();
        private readonly List<string> _warnedMissing = new List<string>();
        private readonly List<PendingMove> _pending = new List<PendingMove>();
        private CancellationTokenSource _stepCts;
        // 第一次執行時算好的目標（move 的 x、pan 的 y）；跳過補完同一個指令時沿用，不用走到一半的位置重算
        private CutsceneCommand _resolvedCmd;
        private float _resolvedValue;

        public CutsceneRunner(StoryUi ui, GameSession session, CameraRig rig, CutsceneScript script, CancellationToken ct)
        {
            _ui = ui;
            _session = session;
            _rig = rig;
            _script = script;
            _ct = ct;
        }

        public CutsceneScript Script => _script;
        public bool Skipping { get; private set; }
        public bool CanSkip => _script.Skippable && !Skipping;

        /// <summary>暫停選單「跳過演出」。不可跳過的演出忽略。</summary>
        public void RequestSkip()
        {
            if (!CanSkip)
                return;
            Skipping = true;
            Log($"跳過演出 {_script.Location}");
            _ui.Dialogue.Hide();
            _ui.Banner.Hide();
            foreach (var p in _pending)
            {
                if (IsAlive(p.Actor) && p.Actor.IsScriptedMoving)
                    p.Actor.Teleport(p.X);
            }
            _pending.Clear();
            _stepCts?.Cancel();
        }

        #region 執行
        public async UniTask RunAsync()
        {
            foreach (var cmd in _script.Commands)
            {
                _ct.ThrowIfCancellationRequested();
                if (!(cmd is SayCommand))
                    _ui.Dialogue.Hide();
                if (Skipping)
                {
                    await ExecuteGuardedAsync(cmd, _ct);
                    continue;
                }
                _stepCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
                try
                {
                    await ExecuteGuardedAsync(cmd, _stepCts.Token);
                }
                catch (OperationCanceledException) when (!_ct.IsCancellationRequested && Skipping)
                {
                    // 跳過打斷了這個指令：用立即模式補完它的最後狀態
                    await ExecuteGuardedAsync(cmd, _ct);
                }
                finally
                {
                    _stepCts.Dispose();
                    _stepCts = null;
                }
            }
        }

        /// <summary>結束清理（正常、跳過、出錯、取消都呼叫）：EndScripted、收起對話框與字卡、鏡頭回到跟隨主角。不碰已刪除的物件。</summary>
        public void Cleanup()
        {
            foreach (var a in _used)
            {
                // 回選單時場景根物件先被關閉、之後才銷毀：關閉中的角色不再碰
                if (!IsAlive(a) || a is Component comp && !comp.gameObject.activeInHierarchy)
                    continue;
                try
                {
                    a.EndScripted();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            _used.Clear();
            _pending.Clear();
            try
            {
                if (_ui != null)
                {
                    _ui.Dialogue.Hide();
                    _ui.Banner.Hide();
                }
                var hero = ActorRegistry.Find(SsIds.ActorHero);
                if (_rig != null && IsAlive(hero))
                    _rig.Follow(hero.Root, false);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>單一指令出例外：記錄後跳過（取消照樣往外丟）。</summary>
        private async UniTask ExecuteGuardedAsync(CutsceneCommand cmd, CancellationToken token)
        {
            try
            {
                await ExecuteAsync(cmd, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sidescroller.StoryUi] {_script.Location} 指令 #{cmd.Index}（{cmd.Op}）執行失敗，跳過");
                Debug.LogException(ex);
            }
        }

        private UniTask ExecuteAsync(CutsceneCommand cmd, CancellationToken token)
        {
            switch (cmd)
            {
                case SayCommand c: return SayAsync(c, token);
                case MoveCommand c: return MoveAsync(c, token);
                case FaceCommand c: Face(c); return UniTask.CompletedTask;
                case AnimCommand c: return AnimAsync(c, token);
                case CameraCommand c: return CameraAsync(c, token);
                case WaitCommand c: return Skipping ? UniTask.CompletedTask : WaitScaled(c.Time, token);
                case FadeCommand c: return FadeAsync(c, token);
                case BannerCommand c: return BannerAsync(c, token);
                case GrantCommand c: Grant(c); return UniTask.CompletedTask;
                case SetFlagCommand c: _session.Flags.Set(c.Flag, c.Value); return UniTask.CompletedTask;
                default:
                    Warn(cmd, "不認得的指令型別，跳過");
                    return UniTask.CompletedTask;
            }
        }
        #endregion

        #region say
        private async UniTask SayAsync(SayCommand c, CancellationToken token)
        {
            if (Skipping)
                return;
            _script.Speakers.TryGetValue(c.Who, out var speaker);
            if (speaker == null)
                WarnOnce("speaker:" + c.Who, $"說話者 {c.Who} 不在 speakers 裡，名字顯示 key、沒有立繪");
            var style = c.Style ?? speaker?.Style ?? "normal";
            var side = c.Side ?? speaker?.Side ?? "left";
            var name = speaker != null ? speaker.Name : c.Who;
            Sprite portrait = null;
            if (speaker?.Portrait != null && (style == "normal" || style == "screen"))
                portrait = SsArt.Get(speaker.Portrait).Variant(c.Face);

            // 只有 normal 才預設用 who 當角色；其他樣式沒寫 actor 就完全不碰角色
            var actorId = c.Actor ?? (style == "normal" ? c.Who : null);
            ICutsceneActor talker = null;
            if (actorId != null && style != "screen")
            {
                var a = UseActor(actorId, cmd: c);
                if (a != null && a.PlayAction("Talk", true))
                    talker = a;
            }

            try
            {
                _ui.Dialogue.Show(name, c.Text, style, side == "right", portrait);
                var total = _ui.Dialogue.CharacterCount;
                var shown = c.Speed <= 0f ? total : 0f;
                _ui.Dialogue.SetVisibleCharacters((int)shown);
                if (shown >= total)
                    _ui.Dialogue.SetWaiting(true);
                var autoT = 0f;
                while (true)
                {
                    // 先等一幀：結束上一句的那次確定鍵不會直接把這句打完
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    // 觸控：點按鈕以外的地方。演出中觸控按鈕只留暫停鈕，點畫面的手指綁成「點擊」，按著不放也不會變成跳躍
                    var confirm = !_session.Paused && (SsInput.ConfirmPressed || SsInput.ScreenTapped);
                    if (shown < total)
                    {
                        shown = confirm ? total : Mathf.Min(total, shown + c.Speed * Time.deltaTime);
                        _ui.Dialogue.SetVisibleCharacters(Mathf.FloorToInt(shown));
                        if (shown >= total)
                            _ui.Dialogue.SetWaiting(true);
                        continue;
                    }
                    if (confirm)
                        break;
                    if (c.Auto >= 0f)
                    {
                        autoT += Time.deltaTime;
                        if (autoT >= c.Auto)
                            break;
                    }
                }
            }
            finally
            {
                if (talker != null && IsAlive(talker))
                    talker.PlayAction("Idle", true);
            }
        }
        #endregion

        #region move／face／anim
        private async UniTask MoveAsync(MoveCommand c, CancellationToken token)
        {
            var a = UseActor(c.Actor, c);
            if (a == null)
                return;
            var from = a.Root.position.x;
            float x;
            if (_resolvedCmd == c)
            {
                x = _resolvedValue;
            }
            else if (c.HasX)
            {
                x = c.X;
            }
            else if (c.HasDx)
            {
                x = from + c.Dx;
            }
            else
            {
                var target = FindActor(c.Target, c);
                if (target == null)
                    return;
                var tx = target.Root.position.x;
                var side = from > tx ? 1f : -1f;
                x = tx + side * c.Gap;
            }
            _resolvedCmd = c;
            _resolvedValue = x;

            if (Skipping)
            {
                a.Teleport(x);
                return;
            }
            a.ScriptedMoveTo(x, c.Speed);
            if (!c.Wait)
            {
                _pending.Add(new PendingMove { Actor = a, X = x });
                return;
            }
            var timeout = Mathf.Abs(x - from) / c.Speed + 2f;
            var t = 0f;
            while (true)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                if (!IsAlive(a) || !a.IsScriptedMoving)
                    return;
                t += Time.deltaTime;
                if (t > timeout)
                {
                    Warn(c, $"{c.Actor} 走到 x={x:0.##} 逾時（{timeout:0.#} 秒），直接瞬移");
                    a.Teleport(x);
                    return;
                }
            }
        }

        private void Face(FaceCommand c)
        {
            var a = UseActor(c.Actor, c);
            if (a == null)
                return;
            if (c.Dir != 0)
            {
                a.SetFacing(c.Dir);
                return;
            }
            var other = FindActor(c.Toward, c);
            if (other == null)
                return;
            var dx = other.Root.position.x - a.Root.position.x;
            if (Mathf.Abs(dx) > 0.001f)
                a.SetFacing(dx > 0f ? 1 : -1);
        }

        private async UniTask AnimAsync(AnimCommand c, CancellationToken token)
        {
            if (Skipping && !c.Loop)
                return;
            var a = UseActor(c.Actor, c);
            if (a == null)
                return;
            if (!a.PlayAction(c.Name, c.Loop))
            {
                Warn(c, $"{c.Actor} 沒有動作 {c.Name}，跳過");
                return;
            }
            if (c.Wait && !c.Loop && !Skipping)
                await WaitScaled(a.GetActionDuration(c.Name), token);
        }
        #endregion

        #region camera
        private async UniTask CameraAsync(CameraCommand c, CancellationToken token)
        {
            if (_rig == null)
            {
                WarnOnce("rig", "沒有 CameraRig，鏡頭指令全部跳過");
                return;
            }
            var time = Skipping ? 0f : c.Time;
            switch (c.Mode)
            {
                case "pan":
                {
                    float y;
                    if (c.HasY)
                        y = c.Y;
                    else if (_resolvedCmd == c)
                        y = _resolvedValue;
                    else
                        y = _rig.Camera != null ? _rig.Camera.transform.position.y : 0f;
                    _resolvedCmd = c;
                    _resolvedValue = y;
                    await Maybe(_rig.PanToAsync(new Vector2(c.X, y), time, WaitToken(c.Wait, token)), c.Wait);
                    break;
                }
                case "follow":
                {
                    var a = FindActor(c.Actor, c);
                    if (a == null)
                        return;
                    _rig.Follow(a.Root, time <= 0f);
                    if (c.Wait && time > 0f)
                        await WaitScaled(time, token);
                    break;
                }
                case "zoom":
                    await Maybe(_rig.ZoomAsync(c.Size, time, WaitToken(c.Wait, token)), c.Wait);
                    break;
                case "shake":
                    if (Skipping)
                        return;
                    _rig.Shake(c.Strength, c.Time);
                    if (c.Wait)
                        await WaitScaled(c.Time, token);
                    break;
                case "reset":
                {
                    var hero = ActorRegistry.Find(SsIds.ActorHero);
                    if (IsAlive(hero))
                        _rig.Follow(hero.Root, time <= 0f);
                    else
                        WarnOnce("hero", "找不到主角，camera reset 只還原 size");
                    await Maybe(_rig.ZoomAsync(SsIds.CameraSize, time, WaitToken(c.Wait, token)), c.Wait);
                    break;
                }
            }
        }

        /// <summary>阻塞的用這個指令的 token（跳過時會被打斷）；不阻塞的用整場演出的 token（跳過後仍繼續，取消時才停）。</summary>
        private CancellationToken WaitToken(bool wait, CancellationToken stepToken) => wait ? stepToken : _ct;

        private static UniTask Maybe(UniTask task, bool wait)
        {
            if (wait)
                return task;
            Detach(task).Forget();
            return UniTask.CompletedTask;
        }

        private static async UniTaskVoid Detach(UniTask task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
        #endregion

        #region fade／banner／grant
        private UniTask FadeAsync(FadeCommand c, CancellationToken token)
        {
            if (Skipping)
            {
                _ui.Fade.SetImmediate(c.To, c.White);
                return UniTask.CompletedTask;
            }
            var version = _ui.Fade.Start(c.To, c.Time, c.White);
            return c.Wait ? _ui.WaitFadeAsync(version, token) : UniTask.CompletedTask;
        }

        private UniTask BannerAsync(BannerCommand c, CancellationToken token)
        {
            if (Skipping)
                return UniTask.CompletedTask;
            _ui.ShowBanner(c.Text, c.Time);
            return c.Wait ? WaitScaled(c.Time, token) : UniTask.CompletedTask;
        }

        private void Grant(GrantCommand c)
        {
            var loadout = _session.Loadout;
            string name;
            if (c.IsGear || c.IsAttack)
            {
                // 攻擊改由裝備欄決定：舊的 attack 寫法（資源包還沒更新時）也改發對應的初始裝備，維持「攻擊旗標＝裝備欄」
                var slot = c.IsGear ? c.Gear : (EquipSlot)(int)c.Attack;
                name = LootTables.GiveMentorGear(_session, slot);
            }
            else
            {
                loadout.GrantCostume(c.Costume, c.Equip);
                name = Loadout.DisplayName(c.Costume);
            }
            if (c.Banner && !Skipping)
                _ui.ShowBanner(StoryTexts.GrantPrefix + name, 2f);
        }
        #endregion

        #region 角色
        /// <summary>找角色並在第一次用到時 BeginScripted；找不到記一次警告回 null。</summary>
        private ICutsceneActor UseActor(string id, CutsceneCommand cmd)
        {
            var a = FindActor(id, cmd);
            if (a == null || _used.Contains(a))
                return a;
            _used.Add(a);
            a.BeginScripted();
            return a;
        }

        private ICutsceneActor FindActor(string id, CutsceneCommand cmd)
        {
            var a = ActorRegistry.Find(id);
            if (IsAlive(a))
                return a;
            WarnOnce("actor:" + id, $"指令 #{cmd.Index}（{cmd.Op}）找不到角色 {id}，跳過角色相關部分");
            return null;
        }

        /// <summary>角色多半是 MonoBehaviour：被 Destroy 後 C# 參考仍不是 null，要用 Unity 的 null 判斷。</summary>
        public static bool IsAlive(ICutsceneActor actor)
        {
            if (actor is UnityEngine.Object o)
                return o != null;
            return actor != null;
        }
        #endregion

        #region 輔助
        /// <summary>wait:false 的 move（跳過時要瞬移到目標）。用類別而不是 struct，避免 List&lt;struct&gt; 的 AOT 泛型。</summary>
        private sealed class PendingMove
        {
            public ICutsceneActor Actor;
            public float X;
        }

        /// <summary>scaled time 等待（暫停時停住）。</summary>
        public static async UniTask WaitScaled(float seconds, CancellationToken token)
        {
            var t = 0f;
            while (t < seconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                t += Time.deltaTime;
            }
        }

        private void Warn(CutsceneCommand cmd, string message) =>
            Debug.LogWarning($"[Sidescroller.StoryUi] {_script.Location} 指令 #{cmd.Index}（{cmd.Op}）：{message}");

        private void WarnOnce(string key, string message)
        {
            if (_warnedMissing.Contains(key))
                return;
            _warnedMissing.Add(key);
            Debug.LogWarning($"[Sidescroller.StoryUi] {_script.Location}：{message}");
        }

        private static void Log(string message) => Debug.Log($"[Sidescroller.StoryUi] {message}");
        #endregion
    }
}
