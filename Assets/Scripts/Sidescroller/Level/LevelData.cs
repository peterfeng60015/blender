using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 關卡文字檔（<c>SsLevel_*.txt</c>）的解析結果（interfaces.md §6.5）。只有 <see cref="LevelBuilder"/> 使用。
    ///
    /// 格式（純文字＋區段，UTF-8）：
    /// <code>
    /// [meta]         key=value。cameraBounds=x,y,w,h、arena=x,y,w,h、killY=數字（三者必填）、bgMidSwitchX=數字（選填）
    /// [map]          每行一列，第一行是最上面那列；最後一行是 y=0。每字元＝1 單位，第 i 字元是 x=i。
    ///                # 實心  = 單向平台  x 木箱（實心）  ^ 低矮通道天花板（實心）  . 或空白＝空
    ///                較短的行在右邊補空；本區段內不能有註解（# 是地形字元）。空行代表 [map] 結束，
///                之後到下一個區段標題前只能有空行（所以全空的一列要寫成 .，不能留空白）。
    /// [entities]     kind id x y facing   kind：rat、pigeon、rhino、boss、npc；facing：1 或 -1
    ///                gate id x y height   閘門（level 自己建，不進 LevelInfo.Spawns），佔 x～x+1、y～y+height
    /// [checkpoints]  id x y               必須有 cp0
    /// [triggers]     kind id x y w h arg  kind：cutscene、hint、checkpoint、bossArena、killzone；
    ///                arg 從第 7 欄到行尾（可含空白，只以半形空白與 Tab 分欄）；killzone 不用 arg
    /// [decor]        name variant x y     name：SsProps、SsLandmark（讀 &lt;name&gt;_Idle 的第 variant 個變化）
    /// </code>
    /// 註解：<c>[map]</c> 以外，整行以 # 開頭的是註解；<c>[meta]</c>、<c>[entities]</c>、<c>[checkpoints]</c>、<c>[decor]</c>
    /// 另可在行尾用「空白＋#」加註解。<c>[triggers]</c> 的 arg 到行尾，不能接行尾註解。
    /// 位置一律是腳底（x 為中央）；觸發區是左下角＋寬高。格式錯誤丟 <see cref="FormatException"/>，訊息含行號。
    /// </summary>
    internal sealed class LevelData
    {
        #region 地圖字元
        public const char Solid = '#';
        public const char OneWay = '=';
        public const char Crate = 'x';
        public const char Ceiling = '^';
        public const char Empty = '.';
        #endregion

        public Rect CameraBounds;
        public Rect Arena;
        public float KillY;
        /// <summary>中景改用工地（SsBgMid 變化 1）的 x；沒寫＝不換。</summary>
        public float BgMidSwitchX = float.PositiveInfinity;

        /// <summary>Rows[y]＝第 y 列（y=0 在最下面），每列長度都是 <see cref="Width"/>。</summary>
        public string[] Rows;
        public int Width;
        public int Height => Rows.Length;

        public readonly List<SpawnPoint> Spawns = new List<SpawnPoint>();
        public readonly List<GateData> Gates = new List<GateData>();
        public readonly List<CheckpointInfo> Checkpoints = new List<CheckpointInfo>();
        public readonly List<TriggerInfo> Triggers = new List<TriggerInfo>();
        public readonly List<DecorData> Decor = new List<DecorData>();

        public sealed class GateData
        {
            public string Id;
            public int X, Y, Height;
        }

        public sealed class DecorData
        {
            public string Name;
            public int Variant;
            public Vector2 Position;
        }

        /// <summary>超出地圖範圍一律當空。</summary>
        public char Cell(int x, int y)
        {
            if (y < 0 || y >= Rows.Length || x < 0 || x >= Width)
                return Empty;
            return Rows[y][x];
        }

        public static bool IsSolid(char c) => c == Solid || c == Crate || c == Ceiling;

        #region 解析
        private static readonly char[] Blank = { ' ', '\t' };
        /// <summary>[map] 遇到空行後的狀態：之後到下一個區段前出現資料就是格式錯誤。</summary>
        private const string MapEnded = "map(結束)";

        public static LevelData Parse(string text)
        {
            if (text == null)
                throw Error(0, "關卡資料是 null");

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var data = new LevelData();
            var mapRows = new List<string>();
            var seenSections = new List<string>();
            var ids = new List<string>();
            string section = null;
            bool hasBounds = false, hasArena = false, hasKillY = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var lineNo = i + 1;
                var raw = lines[i];
                if (i == 0 && raw.Length > 0 && raw[0] == '\uFEFF')
                    raw = raw.Substring(1);
                var trimmed = raw.Trim(Blank);

                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2);
                    if (Array.IndexOf(new[] { "meta", "map", "entities", "checkpoints", "triggers", "decor" }, section) < 0)
                        throw Error(lineNo, $"未知的區段 [{section}]");
                    if (seenSections.Contains(section))
                        throw Error(lineNo, $"區段 [{section}] 重複");
                    seenSections.Add(section);
                    continue;
                }

                if (section == "map")
                {
                    if (trimmed.Length == 0)
                    {
                        if (mapRows.Count > 0)
                            section = MapEnded;
                        continue;
                    }
                    foreach (var c in raw)
                    {
                        if (c != Solid && c != OneWay && c != Crate && c != Ceiling && c != Empty && c != ' ')
                            throw Error(lineNo, c == '|' ? "閘門不畫在 [map]，請用 [entities] 的 gate" : $"[map] 有未知字元 '{c}'");
                    }
                    mapRows.Add(raw.TrimEnd(Blank).Replace(' ', Empty));
                    continue;
                }

                // [map] 遇到空行後到下一個區段前只能是空行：地圖列可能以 # 開頭，不能當註解略過
                if (section == MapEnded && trimmed.Length > 0)
                    throw Error(lineNo, "[map] 中間不能有空行（全空的一列請寫成 .；[map] 後面要先接區段標題才能寫註解）");
                if (trimmed.Length == 0 || trimmed[0] == '#')
                    continue;
                if (section == null)
                    throw Error(lineNo, "區段標題（例如 [meta]）之前不能有資料");

                switch (section)
                {
                    case "meta":
                        ParseMeta(data, StripComment(trimmed), lineNo, ref hasBounds, ref hasArena, ref hasKillY);
                        break;
                    case "entities":
                        ParseEntity(data, Split(StripComment(trimmed)), lineNo, ids);
                        break;
                    case "checkpoints":
                        ParseCheckpoint(data, Split(StripComment(trimmed)), lineNo);
                        break;
                    case "triggers":
                        ParseTrigger(data, trimmed, lineNo, ids);
                        break;
                    case "decor":
                        ParseDecor(data, Split(StripComment(trimmed)), lineNo);
                        break;
                }
            }

            if (!hasBounds || !hasArena || !hasKillY)
                throw Error(0, "[meta] 必須有 cameraBounds、arena、killY");
            if (mapRows.Count == 0)
                throw Error(0, "缺少 [map] 或 [map] 是空的");
            BuildRows(data, mapRows);
            if (data.Checkpoints.Find(c => c.Id == "cp0") == null)
                throw Error(0, "[checkpoints] 必須有 cp0");
            foreach (var t in data.Triggers)
            {
                if (t.Kind == TriggerKind.Checkpoint && data.Checkpoints.Find(c => c.Id == t.Arg) == null)
                    throw Error(0, $"觸發區 {t.Id} 指向不存在的存檔點 {t.Arg}");
            }
            return data;
        }

        private static void ParseMeta(LevelData data, string line, int lineNo, ref bool hasBounds, ref bool hasArena, ref bool hasKillY)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
                throw Error(lineNo, "[meta] 要寫成 key=value");
            var key = line.Substring(0, eq).Trim(Blank);
            var value = line.Substring(eq + 1).Trim(Blank);
            switch (key)
            {
                case "cameraBounds":
                    data.CameraBounds = ParseRect(value, lineNo);
                    hasBounds = true;
                    break;
                case "arena":
                    data.Arena = ParseRect(value, lineNo);
                    hasArena = true;
                    break;
                case "killY":
                    data.KillY = ParseFloat(value, lineNo);
                    hasKillY = true;
                    break;
                case "bgMidSwitchX":
                    data.BgMidSwitchX = ParseFloat(value, lineNo);
                    break;
                default:
                    throw Error(lineNo, $"[meta] 未知的 key：{key}");
            }
        }

        private static void ParseEntity(LevelData data, string[] t, int lineNo, List<string> ids)
        {
            if (t.Length != 5)
                throw Error(lineNo, "[entities] 要 5 欄：kind id x y facing（gate 的最後一欄是高度）");
            var kind = t[0];
            var id = t[1];
            AddId(ids, id, lineNo);
            if (kind == "gate")
            {
                var height = ParseInt(t[4], lineNo);
                if (height < 1)
                    throw Error(lineNo, "閘門高度至少 1");
                data.Gates.Add(new GateData { Id = id, X = ParseInt(t[2], lineNo), Y = ParseInt(t[3], lineNo), Height = height });
                return;
            }
            if (kind != "rat" && kind != "pigeon" && kind != "rhino" && kind != "boss" && kind != "npc")
                throw Error(lineNo, $"未知的角色種類：{kind}");
            var facing = ParseInt(t[4], lineNo);
            if (facing != 1 && facing != -1)
                throw Error(lineNo, "facing 只能是 1 或 -1");
            data.Spawns.Add(new SpawnPoint
            {
                Kind = kind,
                Id = id,
                Position = new Vector2(ParseFloat(t[2], lineNo), ParseFloat(t[3], lineNo)),
                Facing = facing
            });
        }

        private static void ParseCheckpoint(LevelData data, string[] t, int lineNo)
        {
            if (t.Length != 3)
                throw Error(lineNo, "[checkpoints] 要 3 欄：id x y");
            if (data.Checkpoints.Find(c => c.Id == t[0]) != null)
                throw Error(lineNo, $"存檔點 id 重複：{t[0]}");
            data.Checkpoints.Add(new CheckpointInfo
            {
                Id = t[0],
                Position = new Vector2(ParseFloat(t[1], lineNo), ParseFloat(t[2], lineNo))
            });
        }

        private static void ParseTrigger(LevelData data, string line, int lineNo, List<string> ids)
        {
            var t = SplitHead(line, 6, out var arg);
            if (t == null)
                throw Error(lineNo, "[triggers] 至少要 6 欄：kind id x y w h");
            TriggerKind kind;
            switch (t[0])
            {
                case "cutscene": kind = TriggerKind.Cutscene; break;
                case "hint": kind = TriggerKind.Hint; break;
                case "checkpoint": kind = TriggerKind.Checkpoint; break;
                case "bossArena": kind = TriggerKind.BossArena; break;
                case "killzone": kind = TriggerKind.KillZone; break;
                default: throw Error(lineNo, $"未知的觸發區種類：{t[0]}");
            }
            AddId(ids, t[1], lineNo);
            var area = new Rect(ParseFloat(t[2], lineNo), ParseFloat(t[3], lineNo), ParseFloat(t[4], lineNo), ParseFloat(t[5], lineNo));
            if (area.width <= 0f || area.height <= 0f)
                throw Error(lineNo, "觸發區寬高要大於 0");
            if (kind == TriggerKind.KillZone)
            {
                if (arg.Length > 0)
                    throw Error(lineNo, "killzone 不用 arg（行尾也不能接註解）");
            }
            else if (arg.Length == 0)
            {
                throw Error(lineNo, $"{t[0]} 要有 arg");
            }
            data.Triggers.Add(new TriggerInfo { Id = t[1], Kind = kind, Area = area, Arg = arg });
        }

        private static void ParseDecor(LevelData data, string[] t, int lineNo)
        {
            if (t.Length != 4)
                throw Error(lineNo, "[decor] 要 4 欄：name variant x y");
            if (t[0] != "SsProps" && t[0] != "SsLandmark")
                throw Error(lineNo, $"裝飾只能是 SsProps 或 SsLandmark：{t[0]}");
            var variant = ParseInt(t[1], lineNo);
            if (variant < 0)
                throw Error(lineNo, "variant 不能是負數");
            data.Decor.Add(new DecorData
            {
                Name = t[0],
                Variant = variant,
                Position = new Vector2(ParseFloat(t[2], lineNo), ParseFloat(t[3], lineNo))
            });
        }

        /// <summary>檔案裡第一行是最上面那列，翻成 y=0 在最下面；短的行補空。</summary>
        private static void BuildRows(LevelData data, List<string> mapRows)
        {
            var width = 0;
            foreach (var r in mapRows)
                width = Mathf.Max(width, r.Length);
            data.Width = width;
            data.Rows = new string[mapRows.Count];
            for (var y = 0; y < mapRows.Count; y++)
                data.Rows[y] = mapRows[mapRows.Count - 1 - y].PadRight(width, Empty);
        }
        #endregion

        #region 小工具
        private static string StripComment(string line)
        {
            for (var i = 1; i < line.Length; i++)
            {
                if (line[i] == '#' && (line[i - 1] == ' ' || line[i - 1] == '\t'))
                    return line.Substring(0, i).TrimEnd(Blank);
            }
            return line;
        }

        /// <summary>只以半形空白與 Tab 分欄（全形空白 U+3000 是提示文字的一部分）。</summary>
        private static string[] Split(string line) => line.Split(Blank, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>取前 count 欄，其餘（去掉前後半形空白）放進 rest；欄數不足回 null。</summary>
        private static string[] SplitHead(string line, int count, out string rest)
        {
            var head = new string[count];
            var pos = 0;
            for (var n = 0; n < count; n++)
            {
                while (pos < line.Length && (line[pos] == ' ' || line[pos] == '\t'))
                    pos++;
                var start = pos;
                while (pos < line.Length && line[pos] != ' ' && line[pos] != '\t')
                    pos++;
                if (pos == start)
                {
                    rest = null;
                    return null;
                }
                head[n] = line.Substring(start, pos - start);
            }
            rest = line.Substring(pos).Trim(Blank);
            return head;
        }

        private static void AddId(List<string> ids, string id, int lineNo)
        {
            if (ids.Contains(id))
                throw Error(lineNo, $"id 重複：{id}");
            ids.Add(id);
        }

        private static Rect ParseRect(string value, int lineNo)
        {
            var p = value.Split(',');
            if (p.Length != 4)
                throw Error(lineNo, "矩形要寫成 x,y,w,h");
            var r = new Rect(ParseFloat(p[0], lineNo), ParseFloat(p[1], lineNo), ParseFloat(p[2], lineNo), ParseFloat(p[3], lineNo));
            if (r.width <= 0f || r.height <= 0f)
                throw Error(lineNo, "矩形寬高要大於 0");
            return r;
        }

        private static float ParseFloat(string s, int lineNo)
        {
            if (!float.TryParse(s.Trim(Blank), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || float.IsNaN(v) || float.IsInfinity(v))
                throw Error(lineNo, $"不是數字：{s}");
            return v;
        }

        private static int ParseInt(string s, int lineNo)
        {
            if (!int.TryParse(s.Trim(Blank), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                throw Error(lineNo, $"不是整數：{s}");
            return v;
        }

        private static FormatException Error(int lineNo, string message) =>
            new FormatException(lineNo > 0
                ? $"[Sidescroller.LevelBuilder] 關卡資料第 {lineNo} 行：{message}"
                : $"[Sidescroller.LevelBuilder] 關卡資料：{message}");
        #endregion
    }
}
