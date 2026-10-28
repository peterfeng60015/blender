using System;
using System.Collections.Generic;

namespace Sidescroller
{
    /// <summary>一份解析好的演出腳本（cutscene-format.md §2）。</summary>
    public sealed class CutsceneScript
    {
        public string Location;
        public string Id;
        public int Version = 1;
        public bool Skippable = true;
        public bool KeepFade;
        public readonly Dictionary<string, CutsceneSpeaker> Speakers = new Dictionary<string, CutsceneSpeaker>();
        /// <summary>依序執行；解析失敗被跳過的指令不在這裡（序號保留在 <see cref="CutsceneCommand.Index"/>）。</summary>
        public readonly List<CutsceneCommand> Commands = new List<CutsceneCommand>();
    }

    /// <summary>說話者（cutscene-format.md §2.1）。</summary>
    public sealed class CutsceneSpeaker
    {
        public string Key;
        public string Name;
        public string Portrait;     // null＝沒有立繪
        public string Side;         // "left"／"right"
        public string Style;        // "normal"／"screen"／"offscreen"／"narration"
    }

    /// <summary>解析過程收集到的問題；執行時寫進 log，Editor 檢查工具拿來列表。</summary>
    public sealed class CutsceneIssues
    {
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) => Errors.Add(message);
    }

    #region 指令
    public abstract class CutsceneCommand
    {
        public int Index;   // 在 commands 陣列裡的序號（0 起算）
        public string Op;
    }

    public sealed class SayCommand : CutsceneCommand
    {
        public string Who;
        public string Text;
        public int Face;            // 0 平常、1 得意、2 生氣、3 驚訝
        public string Style;        // null＝用說話者的
        public string Side;         // null＝用說話者的
        public string Actor;        // null＝style 是 normal 時用 who
        public float Speed = 30f;
        public float Auto = -1f;    // <0＝不自動前進
    }

    public sealed class MoveCommand : CutsceneCommand
    {
        public string Actor;
        public bool HasX;
        public float X;
        public bool HasDx;
        public float Dx;
        public string Target;
        public float Gap = 1.5f;
        public float Speed = 3f;
        public bool Wait = true;
    }

    public sealed class FaceCommand : CutsceneCommand
    {
        public string Actor;
        public int Dir;             // +1／-1；0＝用 Toward
        public string Toward;
    }

    public sealed class AnimCommand : CutsceneCommand
    {
        public string Actor;
        public string Name;
        public bool Loop;
        public bool Wait;
    }

    public sealed class CameraCommand : CutsceneCommand
    {
        public string Mode;         // pan／follow／zoom／shake／reset
        public float X;
        public bool HasY;
        public float Y;
        public string Actor;
        public float Size;
        public float Strength;
        public float Time;
        public bool Wait;
    }

    public sealed class WaitCommand : CutsceneCommand
    {
        public float Time;
    }

    public sealed class FadeCommand : CutsceneCommand
    {
        public float To;
        public float Time = 0.5f;
        public bool White;
        public bool Wait = true;
    }

    public sealed class BannerCommand : CutsceneCommand
    {
        public string Text;
        public float Time = 2f;
        public bool Wait = true;
    }

    public sealed class GrantCommand : CutsceneCommand
    {
        public bool IsAttack;
        public AttackId Attack;
        public CostumeId Costume;
        public bool Equip = true;
        public bool Banner = true;
    }

    public sealed class SetFlagCommand : CutsceneCommand
    {
        public string Flag;
        public bool Value = true;
    }
    #endregion

    /// <summary>
    /// 把 <see cref="MiniJson"/> 的樹轉成 <see cref="CutsceneScript"/>（cutscene-format.md §2～§4 的錯誤處理）。
    /// json 語法錯、頂層不是物件、缺 <c>commands</c>：丟 <see cref="FormatException"/>。
    /// 單一指令的問題記進 <see cref="CutsceneIssues"/>，錯誤的指令跳過，其餘照常。
    /// </summary>
    public static class CutsceneParser
    {
        public const int SupportedVersion = 1;

        public static readonly string[] Ops = { "say", "move", "face", "anim", "camera", "wait", "fade", "banner", "grant", "setFlag" };
        public static readonly string[] Styles = { "normal", "screen", "offscreen", "narration" };
        public static readonly string[] Faces = { "normal", "happy", "angry", "surprised" };
        public static readonly string[] CameraModes = { "pan", "follow", "zoom", "shake", "reset" };

        private static readonly string[] TopKeys = { "id", "version", "skippable", "keepFade", "speakers", "commands" };
        private static readonly string[] SpeakerKeys = { "name", "portrait", "side", "style" };

        public static CutsceneScript Parse(string location, string text, CutsceneIssues issues)
        {
            var jsonWarnings = new List<string>();
            var root = MiniJson.Parse(text, jsonWarnings);
            foreach (var w in jsonWarnings)
                issues.Warn($"{location}：{w}");
            if (!(root is Dictionary<string, object> top))
                throw new FormatException($"{location}：頂層必須是物件");

            var script = new CutsceneScript { Location = location };
            var f = new Fields(top, location, "頂層", issues);
            f.CheckUnknown(TopKeys);
            script.Id = f.OptString("id", null);
            if (script.Id != null && script.Id != location)
                issues.Warn($"{location}：id \"{script.Id}\" 和檔名不一致");
            script.Version = (int)f.OptNumber("version", 1);
            if (script.Version > SupportedVersion)
                issues.Warn($"{location}：version {script.Version} 大於執行器支援的 {SupportedVersion}，照樣嘗試執行");
            script.Skippable = f.OptBool("skippable", true);
            script.KeepFade = f.OptBool("keepFade", false);

            if (top.TryGetValue("speakers", out var speakersObj) && speakersObj != null)
            {
                if (speakersObj is Dictionary<string, object> speakers)
                {
                    foreach (var pair in speakers)
                        ParseSpeaker(script, location, pair.Key, pair.Value, issues);
                }
                else
                {
                    issues.Warn($"{location}：speakers 必須是物件，忽略");
                }
            }

            if (!top.TryGetValue("commands", out var commandsObj) || !(commandsObj is List<object> commands))
                throw new FormatException($"{location}：缺少 commands 陣列");
            for (var i = 0; i < commands.Count; i++)
            {
                var cmd = ParseCommand(location, i, commands[i], issues);
                if (cmd != null)
                    script.Commands.Add(cmd);
            }
            return script;
        }

        #region 說話者
        private static void ParseSpeaker(CutsceneScript script, string location, string key, object value, CutsceneIssues issues)
        {
            if (!(value is Dictionary<string, object> obj))
            {
                issues.Warn($"{location}：說話者 {key} 必須是物件，忽略");
                return;
            }
            var f = new Fields(obj, location, $"說話者 {key}", issues);
            f.CheckUnknown(SpeakerKeys);
            var speaker = new CutsceneSpeaker
            {
                Key = key,
                Name = f.OptString("name", key),
                Portrait = f.OptString("portrait", null),
                Side = f.OptEnum("side", new[] { "left", "right" }, "left"),
                Style = f.OptEnum("style", Styles, "normal"),
            };
            if (speaker.Portrait == string.Empty)
                speaker.Portrait = null;
            script.Speakers[key] = speaker;
        }
        #endregion

        #region 指令
        private static CutsceneCommand ParseCommand(string location, int index, object value, CutsceneIssues issues)
        {
            if (!(value is Dictionary<string, object> obj))
            {
                issues.Warn($"{location} 指令 #{index}：不是物件，跳過");
                return null;
            }
            obj.TryGetValue("op", out var opObj);
            var op = opObj as string;
            if (string.IsNullOrEmpty(op))
            {
                issues.Warn($"{location} 指令 #{index}：缺少 op，跳過");
                return null;
            }
            var f = new Fields(obj, location, $"指令 #{index}（{op}）", issues);
            CutsceneCommand cmd;
            switch (op)
            {
                case "say": cmd = ParseSay(f); break;
                case "move": cmd = ParseMove(f); break;
                case "face": cmd = ParseFace(f); break;
                case "anim": cmd = ParseAnim(f); break;
                case "camera": cmd = ParseCamera(f); break;
                case "wait": cmd = ParseWait(f); break;
                case "fade": cmd = ParseFade(f); break;
                case "banner": cmd = ParseBanner(f); break;
                case "grant": cmd = ParseGrant(f); break;
                case "setFlag": cmd = ParseSetFlag(f); break;
                default:
                    issues.Warn($"{location} 指令 #{index}：不認得的 op \"{op}\"，跳過");
                    return null;
            }
            if (cmd == null || f.Failed)
                return null;
            cmd.Index = index;
            cmd.Op = op;
            return cmd;
        }

        private static CutsceneCommand ParseSay(Fields f)
        {
            f.CheckUnknown("op", "who", "text", "face", "style", "side", "actor", "speed", "auto");
            var cmd = new SayCommand
            {
                Who = f.ReqString("who"),
                Text = f.ReqString("text"),
                Style = f.OptEnum("style", Styles, null),
                Side = f.OptEnum("side", new[] { "left", "right" }, null),
                Actor = f.OptString("actor", null),
                Speed = f.OptNumber("speed", 30f),
                Auto = f.OptNumber("auto", -1f),
            };
            var face = f.OptString("face", "normal");
            cmd.Face = Array.IndexOf(Faces, face);
            if (cmd.Face < 0)
            {
                f.Warn($"face \"{face}\" 不認得，用 normal");
                cmd.Face = 0;
            }
            if (cmd.Speed < 0f)
            {
                f.Warn("speed 不能是負數，用 30");
                cmd.Speed = 30f;
            }
            return cmd;
        }

        private static CutsceneCommand ParseMove(Fields f)
        {
            f.CheckUnknown("op", "actor", "x", "dx", "target", "gap", "speed", "wait");
            var cmd = new MoveCommand
            {
                Actor = f.ReqString("actor"),
                HasX = f.Has("x"),
                HasDx = f.Has("dx"),
                Target = f.OptString("target", null),
                Gap = f.OptNumber("gap", 1.5f),
                Speed = f.OptNumber("speed", 3f),
                Wait = f.OptBool("wait", true),
            };
            if (cmd.HasX)
                cmd.X = f.ReqNumber("x");
            if (cmd.HasDx)
                cmd.Dx = f.ReqNumber("dx");
            var count = (cmd.HasX ? 1 : 0) + (cmd.HasDx ? 1 : 0) + (cmd.Target != null ? 1 : 0);
            if (count != 1)
                f.Error("x、dx、target 必須剛好三選一");
            if (cmd.Speed <= 0f)
            {
                f.Warn("speed 必須大於 0，用 3");
                cmd.Speed = 3f;
            }
            return cmd;
        }

        private static CutsceneCommand ParseFace(Fields f)
        {
            f.CheckUnknown("op", "actor", "dir", "toward");
            var cmd = new FaceCommand
            {
                Actor = f.ReqString("actor"),
                Toward = f.OptString("toward", null),
            };
            var hasDir = f.Has("dir");
            if (hasDir == (cmd.Toward != null))
            {
                f.Error("dir 與 toward 必須剛好二選一");
                return cmd;
            }
            if (hasDir)
            {
                var dir = f.ReqString("dir");
                if (dir == "left") cmd.Dir = -1;
                else if (dir == "right") cmd.Dir = 1;
                else f.Error($"dir \"{dir}\" 只能是 left 或 right");
            }
            return cmd;
        }

        private static CutsceneCommand ParseAnim(Fields f)
        {
            f.CheckUnknown("op", "actor", "name", "loop", "wait");
            return new AnimCommand
            {
                Actor = f.ReqString("actor"),
                Name = f.ReqString("name"),
                Loop = f.OptBool("loop", false),
                Wait = f.OptBool("wait", false),
            };
        }

        private static CutsceneCommand ParseCamera(Fields f)
        {
            var mode = f.ReqString("mode");
            if (mode == null)
                return null;
            var cmd = new CameraCommand { Mode = mode };
            switch (mode)
            {
                case "pan":
                    f.CheckUnknown("op", "mode", "x", "y", "time", "wait");
                    cmd.X = f.ReqNumber("x");
                    cmd.HasY = f.Has("y");
                    if (cmd.HasY)
                        cmd.Y = f.ReqNumber("y");
                    cmd.Time = f.OptNumber("time", 1f);
                    cmd.Wait = f.OptBool("wait", true);
                    break;
                case "follow":
                    f.CheckUnknown("op", "mode", "actor", "time", "wait");
                    cmd.Actor = f.ReqString("actor");
                    cmd.Time = f.OptNumber("time", 0.8f);
                    cmd.Wait = f.OptBool("wait", true);
                    break;
                case "zoom":
                    f.CheckUnknown("op", "mode", "size", "time", "wait");
                    cmd.Size = f.ReqNumber("size");
                    cmd.Time = f.OptNumber("time", 0.8f);
                    cmd.Wait = f.OptBool("wait", true);
                    break;
                case "shake":
                    f.CheckUnknown("op", "mode", "strength", "time", "wait");
                    cmd.Strength = f.OptNumber("strength", 0.2f);
                    cmd.Time = f.OptNumber("time", 0.3f);
                    cmd.Wait = f.OptBool("wait", false);
                    break;
                case "reset":
                    f.CheckUnknown("op", "mode", "time", "wait");
                    cmd.Time = f.OptNumber("time", 0.5f);
                    cmd.Wait = f.OptBool("wait", true);
                    break;
                default:
                    f.Warn($"不認得的 mode \"{mode}\"，跳過");
                    return null;
            }
            if (cmd.Time < 0f)
            {
                f.Warn("time 不能是負數，用 0");
                cmd.Time = 0f;
            }
            return cmd;
        }

        private static CutsceneCommand ParseWait(Fields f)
        {
            f.CheckUnknown("op", "time");
            return new WaitCommand { Time = Math.Max(0f, f.ReqNumber("time")) };
        }

        private static CutsceneCommand ParseFade(Fields f)
        {
            f.CheckUnknown("op", "to", "time", "color", "wait");
            var cmd = new FadeCommand
            {
                To = Clamp01(f.ReqNumber("to")),
                Time = Math.Max(0f, f.OptNumber("time", 0.5f)),
                White = f.OptEnum("color", new[] { "black", "white" }, "black") == "white",
                Wait = f.OptBool("wait", true),
            };
            return cmd;
        }

        private static CutsceneCommand ParseBanner(Fields f)
        {
            f.CheckUnknown("op", "text", "time", "wait");
            return new BannerCommand
            {
                Text = f.ReqString("text"),
                Time = Math.Max(0f, f.OptNumber("time", 2f)),
                Wait = f.OptBool("wait", true),
            };
        }

        private static CutsceneCommand ParseGrant(Fields f)
        {
            f.CheckUnknown("op", "attack", "costume", "equip", "banner");
            var hasAttack = f.Has("attack");
            var hasCostume = f.Has("costume");
            if (hasAttack == hasCostume)
            {
                f.Error("attack 與 costume 必須剛好二選一");
                return null;
            }
            var cmd = new GrantCommand
            {
                IsAttack = hasAttack,
                Equip = f.OptBool("equip", true),
                Banner = f.OptBool("banner", true),
            };
            if (hasAttack)
            {
                var name = f.ReqString("attack");
                switch (name)
                {
                    case "Melee": cmd.Attack = AttackId.Melee; break;
                    case "Sling": cmd.Attack = AttackId.Sling; break;
                    case "Roll": cmd.Attack = AttackId.Roll; break;
                    default: f.Error($"attack \"{name}\" 只能是 Melee／Sling／Roll"); break;
                }
            }
            else
            {
                var name = f.ReqString("costume");
                switch (name)
                {
                    case "Day": cmd.Costume = CostumeId.Day; break;
                    case "Express": cmd.Costume = CostumeId.Express; break;
                    default: f.Error($"costume \"{name}\" 只能是 Day／Express"); break;
                }
            }
            return cmd;
        }

        private static CutsceneCommand ParseSetFlag(Fields f)
        {
            f.CheckUnknown("op", "flag", "value");
            var cmd = new SetFlagCommand
            {
                Flag = f.ReqString("flag"),
                Value = f.OptBool("value", true),
            };
            if (cmd.Flag == string.Empty)
                f.Error("flag 不能是空字串");
            return cmd;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        #endregion

        #region 欄位讀取
        /// <summary>讀一個 json 物件的欄位；型別不符時依 cutscene-format.md §4 記錯誤或警告。</summary>
        private sealed class Fields
        {
            private readonly Dictionary<string, object> _obj;
            private readonly string _where;
            private readonly CutsceneIssues _issues;

            public bool Failed { get; private set; }

            public Fields(Dictionary<string, object> obj, string location, string what, CutsceneIssues issues)
            {
                _obj = obj;
                _where = $"{location} {what}";
                _issues = issues;
            }

            public bool Has(string key) => _obj.ContainsKey(key);

            public void Warn(string message) => _issues.Warn($"{_where}：{message}");

            public void Error(string message)
            {
                _issues.Error($"{_where}：{message}，跳過");
                Failed = true;
            }

            public void CheckUnknown(params string[] known)
            {
                foreach (var key in _obj.Keys)
                {
                    if (Array.IndexOf(known, key) < 0)
                        Warn($"不認得的欄位 \"{key}\"（忽略）");
                }
            }

            public string ReqString(string key)
            {
                if (!_obj.TryGetValue(key, out var v))
                {
                    Error($"缺少必填欄位 {key}");
                    return null;
                }
                if (v is string s)
                    return s;
                Error($"{key} 必須是字串");
                return null;
            }

            public float ReqNumber(string key)
            {
                if (!_obj.TryGetValue(key, out var v))
                {
                    Error($"缺少必填欄位 {key}");
                    return 0f;
                }
                if (v is double d)
                    return (float)d;
                Error($"{key} 必須是數字");
                return 0f;
            }

            public string OptString(string key, string fallback)
            {
                if (!_obj.TryGetValue(key, out var v) || v == null)
                    return fallback;
                if (v is string s)
                    return s;
                Warn($"{key} 必須是字串，用預設值");
                return fallback;
            }

            public float OptNumber(string key, float fallback)
            {
                if (!_obj.TryGetValue(key, out var v) || v == null)
                    return fallback;
                if (v is double d)
                    return (float)d;
                Warn($"{key} 必須是數字，用預設值 {fallback}");
                return fallback;
            }

            public bool OptBool(string key, bool fallback)
            {
                if (!_obj.TryGetValue(key, out var v) || v == null)
                    return fallback;
                if (v is bool b)
                    return b;
                Warn($"{key} 必須是 true／false，用預設值 {(fallback ? "true" : "false")}");
                return fallback;
            }

            public string OptEnum(string key, string[] allowed, string fallback)
            {
                var s = OptString(key, null);
                if (s == null)
                    return fallback;
                if (Array.IndexOf(allowed, s) >= 0)
                    return s;
                Warn($"{key} \"{s}\" 不認得（可用：{string.Join("／", allowed)}），用預設值");
                return fallback;
            }
        }
        #endregion
    }
}
