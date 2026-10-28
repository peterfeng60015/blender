using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 演出腳本檢查工具（cutscene-format.md §4 末段、interfaces.md §7.4）。用執行期同一個解析器讀全部 <c>SsCutscene_*.json</c> 與 <c>SsLevel_*.txt</c>，檢查：
    /// json 語法、未知 op 與欄位、who 是否在 speakers、portrait 是否在 SsArt 清單、角色 id 是否在關卡 [entities]（或是 hero）、
    /// 關卡觸發區指向的演出是否存在，以及所有台詞、名字、字卡、提示與介面文字的每個字是否在思源黑體的字元表裡。
    /// 選單 <c>Tools/Sidescroller/檢查演出腳本</c>；batchmode 用 <c>-executeMethod Sidescroller.CutsceneChecker.RunBatch</c>（有問題時 exit code 1）。
    /// </summary>
    public static class CutsceneChecker
    {
        public const string CutsceneDir = "Assets/Art/Sidescroller/Data/Cutscenes";
        public const string LevelDir = "Assets/Art/Sidescroller/Data/Levels";
        public const string FontPath = "Assets/Plugins/IngameDebugConsole/Fonts & Materials/Chinese/思源黑體-Medium.asset";

        [MenuItem("Tools/Sidescroller/檢查演出腳本")]
        public static void RunMenu()
        {
            var problems = Check(out var summary);
            if (problems.Count == 0)
            {
                Debug.Log($"[Sidescroller.CutsceneChecker] 通過：{summary}");
                EditorUtility.DisplayDialog("檢查演出腳本", $"通過\n{summary}", "OK");
                return;
            }
            foreach (var p in problems)
                Debug.LogError($"[Sidescroller.CutsceneChecker] {p}");
            EditorUtility.DisplayDialog("檢查演出腳本", $"發現 {problems.Count} 個問題（詳見 Console）\n{summary}", "OK");
        }

        public static void RunBatch()
        {
            var code = 0;
            try
            {
                var problems = Check(out var summary);
                foreach (var p in problems)
                    Debug.LogError($"[Sidescroller.CutsceneChecker] {p}");
                Debug.Log($"[Sidescroller.CutsceneChecker] {(problems.Count == 0 ? "通過" : $"失敗：{problems.Count} 個問題")}；{summary}");
                code = problems.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                code = 2;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>回傳問題清單（空＝通過）；summary 是檢查了多少東西的一句話。</summary>
        public static List<string> Check(out string summary)
        {
            var problems = new List<string>();
            var texts = new List<string>();     // 要檢查字元的文字，格式「來源\u0001文字」
            var actorRefs = new List<string>(); // 「來源\u0001角色 id」

            #region 關卡資料：角色 id、提示文字、演出觸發
            var entityIds = new List<string> { SsIds.ActorHero };
            var cutsceneRefs = new List<string>();
            var levelFiles = Directory.Exists(LevelDir) ? Directory.GetFiles(LevelDir, "SsLevel_*.txt") : new string[0];
            if (levelFiles.Length == 0)
                problems.Add($"找不到關卡資料（{LevelDir}/SsLevel_*.txt），無法檢查角色 id 與提示文字");
            foreach (var path in levelFiles)
                ReadLevel(path.Replace('\\', '/'), entityIds, texts, cutsceneRefs, problems);
            #endregion

            #region 演出腳本
            var files = Directory.Exists(CutsceneDir) ? Directory.GetFiles(CutsceneDir, "SsCutscene_*.json") : new string[0];
            if (files.Length == 0)
                problems.Add($"找不到演出腳本（{CutsceneDir}/SsCutscene_*.json）");
            var locations = new List<string>();
            var commandCount = 0;
            foreach (var raw in files)
            {
                var path = raw.Replace('\\', '/');
                var location = Path.GetFileNameWithoutExtension(path);
                locations.Add(location);
                var issues = new CutsceneIssues();
                CutsceneScript script;
                try
                {
                    script = CutsceneParser.Parse(location, File.ReadAllText(path), issues);
                }
                catch (FormatException ex)
                {
                    problems.Add(ex.Message);
                    continue;
                }
                problems.AddRange(issues.Errors);
                problems.AddRange(issues.Warnings);
                commandCount += script.Commands.Count;
                CheckScript(script, texts, actorRefs, problems);
            }
            foreach (var r in cutsceneRefs)
            {
                var parts = r.Split('\u0001');
                if (!locations.Contains(parts[1]))
                    problems.Add($"{parts[0]}：觸發的演出 {parts[1]} 不存在於 {CutsceneDir}");
            }
            if (levelFiles.Length > 0)
            {
                foreach (var r in actorRefs)
                {
                    var parts = r.Split('\u0001');
                    if (!entityIds.Contains(parts[1]))
                        problems.Add($"{parts[0]}：角色 id {parts[1]} 不在關卡資料的 [entities]（也不是 hero）");
                }
            }
            #endregion

            #region 介面文字與缺字
            foreach (var s in StoryTexts.All)
                texts.Add("介面文字\u0001" + s);
            texts.Add("Boss 名稱\u0001" + SsIds.BossDisplayName);
            foreach (AttackId id in new[] { AttackId.Melee, AttackId.Sling, AttackId.Roll })
                texts.Add("獲得字卡\u0001" + StoryTexts.GrantPrefix + Loadout.DisplayName(id));
            foreach (CostumeId id in new[] { CostumeId.Day, CostumeId.Express })
                texts.Add("獲得字卡\u0001" + StoryTexts.GrantPrefix + Loadout.DisplayName(id));
            var charCount = CheckGlyphs(texts, problems);
            #endregion

            summary = $"演出腳本 {files.Length} 份（{commandCount} 個指令）、關卡資料 {levelFiles.Length} 份、文字 {texts.Count} 段（{charCount} 種字元）";
            return problems;
        }

        #region 演出腳本內容
        private static void CheckScript(CutsceneScript script, List<string> texts, List<string> actorRefs, List<string> problems)
        {
            var loc = script.Location;
            foreach (var sp in script.Speakers.Values)
            {
                if (!string.IsNullOrEmpty(sp.Name))
                    texts.Add($"{loc} 說話者 {sp.Key}\u0001{sp.Name}");
                if (sp.Portrait != null && !SsArt.IsKnownLocation(sp.Portrait))
                    problems.Add($"{loc} 說話者 {sp.Key}：portrait {sp.Portrait} 不在 SsArt 的素材清單");
            }
            foreach (var cmd in script.Commands)
            {
                var where = $"{loc} 指令 #{cmd.Index}（{cmd.Op}）";
                switch (cmd)
                {
                    case SayCommand c:
                    {
                        texts.Add(where + "\u0001" + c.Text);
                        script.Speakers.TryGetValue(c.Who, out var sp);
                        if (sp == null)
                            problems.Add($"{where}：who {c.Who} 不在 speakers");
                        var style = c.Style ?? sp?.Style ?? "normal";
                        var actor = c.Actor ?? (style == "normal" ? c.Who : null);
                        if (actor != null && style != "screen")
                            actorRefs.Add(where + "\u0001" + actor);
                        break;
                    }
                    case MoveCommand c:
                        actorRefs.Add(where + "\u0001" + c.Actor);
                        if (c.Target != null)
                            actorRefs.Add(where + "\u0001" + c.Target);
                        break;
                    case FaceCommand c:
                        actorRefs.Add(where + "\u0001" + c.Actor);
                        if (c.Toward != null)
                            actorRefs.Add(where + "\u0001" + c.Toward);
                        break;
                    case AnimCommand c:
                        actorRefs.Add(where + "\u0001" + c.Actor);
                        break;
                    case CameraCommand c:
                        if (c.Mode == "follow")
                            actorRefs.Add(where + "\u0001" + c.Actor);
                        break;
                    case BannerCommand c:
                        texts.Add(where + "\u0001" + c.Text);
                        break;
                }
            }
        }
        #endregion

        #region 關卡資料
        /// <summary>只讀 [entities] 的 id 與 [triggers] 的 hint 文字、cutscene／bossArena 的 location（格式見 interfaces.md §6.5）。</summary>
        private static void ReadLevel(string path, List<string> entityIds, List<string> texts, List<string> cutsceneRefs, List<string> problems)
        {
            var name = Path.GetFileName(path);
            var section = string.Empty;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line;
                    continue;
                }
                if (section == "[entities]")
                {
                    var tokens = Tokens(StripComment(line), 0);
                    if (tokens.Length >= 2)
                        entityIds.Add(tokens[1]);
                }
                else if (section == "[triggers]")
                {
                    // kind id x y w h arg（arg 到行尾，可含空白）
                    var tokens = Tokens(line, 7);
                    if (tokens.Length < 7)
                        continue;
                    var kind = tokens[0];
                    var arg = tokens[6].Trim();
                    var where = $"{name} 第 {i + 1} 行（{tokens[1]}）";
                    if (kind == "hint")
                        texts.Add(where + "\u0001" + arg);
                    else if (kind == "cutscene" || kind == "bossArena")
                        cutsceneRefs.Add(where + "\u0001" + StripComment(arg));
                }
            }
        }

        private static string StripComment(string line)
        {
            var i = line.IndexOf('#');
            return (i >= 0 ? line.Substring(0, i) : line).Trim();
        }

        /// <summary>依空白切開；max&gt;0 時最多切 max 段，最後一段保留原文（含空白）。</summary>
        private static string[] Tokens(string line, int max)
        {
            var result = new List<string>();
            var pos = 0;
            while (pos < line.Length)
            {
                while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                    pos++;
                if (pos >= line.Length)
                    break;
                if (max > 0 && result.Count == max - 1)
                {
                    result.Add(line.Substring(pos));
                    break;
                }
                var start = pos;
                while (pos < line.Length && !char.IsWhiteSpace(line[pos]))
                    pos++;
                result.Add(line.Substring(start, pos - start));
            }
            return result.ToArray();
        }
        #endregion

        #region 缺字
        private static int CheckGlyphs(List<string> texts, List<string> problems)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null)
            {
                problems.Add($"讀不到字型 {FontPath}");
                return 0;
            }
            var lookup = font.characterLookupTable;
            var seen = new HashSet<string>();
            var missing = new Dictionary<string, string>(); // 字元 → 第一個出現的位置
            foreach (var entry in texts)
            {
                var parts = entry.Split(new[] { '\u0001' }, 2);
                var s = parts[1];
                for (var i = 0; i < s.Length; i++)
                {
                    int cp;
                    string ch;
                    if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        cp = char.ConvertToUtf32(s[i], s[i + 1]);
                        ch = s.Substring(i, 2);
                        i++;
                    }
                    else
                    {
                        cp = s[i];
                        ch = s[i].ToString();
                    }
                    if (cp == '\n' || cp == '\r' || cp == '\t')
                        continue;
                    seen.Add(ch);
                    if (!lookup.ContainsKey((uint)cp) && !missing.ContainsKey(ch))
                        missing[ch] = parts[0];
                }
            }
            foreach (var pair in missing)
            {
                var code = char.IsSurrogate(pair.Key[0]) && pair.Key.Length < 2 ? pair.Key[0] : char.ConvertToUtf32(pair.Key, 0);
                problems.Add(pair.Key.Length == 1 && char.IsSurrogate(pair.Key[0])
                    ? $"不成對的代理字元（U+{code:X4}），出現在 {pair.Value}"
                    : $"缺字「{pair.Key}」（U+{code:X4}），第一次出現在 {pair.Value}");
            }
            return seen.Count;
        }
        #endregion
    }
}
