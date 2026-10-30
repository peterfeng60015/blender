using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// UI 截圖：從 Entry 場景進 Play，在幾個時間點把 Game 視窗拍成 PNG（多種解析度）。
/// 選單模式（-uiShotMode menu）走正常開機進選擇遊戲選單；遊戲模式（game）開 DevBoot 直接進《熱浪急件》，
/// 依序拍開場演出、HUD、暫停、背包、強化、字卡、通關畫面與直向提示（用反射打開，和玩家操作走同一組方法）。
///
///   Unity -projectPath &lt;專案&gt; -executeMethod UiShotCapture.RunBatch \
///     -uiShotMode game -captureDir &lt;輸出資料夾&gt; -logFile &lt;log&gt;
/// **不要加 -batchmode**：batchmode 沒有 Game 視窗，Screen 固定 640×480，讀 Screen 排版的 UI（安全區、觸控層）會照錯的尺寸排。
/// 這裡開一般 Editor 視窗，每張先把 Game 視窗切成固定解析度（Screen、safeArea、直向判斷都跟著變），等版面跟上再用
/// ScreenCapture 拍（含 Overlay Canvas）。拍完移除加進 Game 視窗的尺寸並結束；失敗或逾時 exit 1。觸控模式用反射打開（手機是主要平台）。
/// </summary>
[InitializeOnLoad]
public static class UiShotCapture
{
    private const string ActiveKey = "UiShotCapture.Active";
    private const string DirKey = "UiShotCapture.Dir";
    private const string ModeKey = "UiShotCapture.Mode";
    private const string StartKey = "UiShotCapture.Start";
    private const string StepKey = "UiShotCapture.Step";
    private const string StepAtKey = "UiShotCapture.StepAt";
    private const string DevBootPref = "Sidescroller.DevBoot";
    private const string EntryScenePath = "Assets/Scenes/Entry.unity";
    private const string LogPrefix = "[UiShotCapture] ";
    private const float Timeout = 240f;
    /// <summary>切換 Game 視窗尺寸後等版面跟上（各 UI 在 Update 裡依 Screen 重排）。</summary>
    private const float SettleTime = 0.4f;
    private const string SizePrefix = "UiShot ";

    /// <summary>橫向解析度：S22 Ultra（3088×1440，19.3:9）縮到高 1080、一般 16:9。</summary>
    private static readonly Vector2Int[] Landscape = { new Vector2Int(2316, 1080), new Vector2Int(1920, 1080) };
    private static readonly Vector2Int Portrait = new Vector2Int(1080, 2316);

    private sealed class Step
    {
        public float Delay;
        public string Name;
        public Action Before;
        public Action After;
        /// <summary>直向（Game 視窗切成直向尺寸，遊戲自己蓋直向提示）。</summary>
        public bool PortraitShot;
        /// <summary>觸控模式（每幀重設：開機時 SsTouch.ForgetDevice 會清掉）。</summary>
        public bool Touch = true;
    }

    /// <summary>目前這一步拍到第幾個尺寸（Play 中不會 domain reload，放 static 即可）。</summary>
    private static int _sizeIndex = -1;
    private static float _readyAt;
    private static string _pendingPath;
    private static float _pendingSince;

    static UiShotCapture()
    {
        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.update += Tick;
    }

    public static void RunBatch()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var dir = Path.GetFullPath(Arg(args, "-captureDir") ?? "Temp/UiShots");
            var mode = Arg(args, "-uiShotMode") ?? "game";
            Directory.CreateDirectory(dir);

            AssetPackageCollectorSetup.Setup();
            EditorSceneManager.OpenScene(EntryScenePath);
            EditorPrefs.SetBool(DevBootPref, mode == "game");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(DirKey, dir);
            SessionState.SetString(ModeKey, mode);
            SessionState.SetFloat(StartKey, -1f);
            SessionState.SetInt(StepKey, 0);
            SessionState.SetFloat(StepAtKey, 0f);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Finish(1);
        }
    }

    private static string Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    #region 流程
    private static List<Step> Steps(string mode)
    {
        var s = new List<Step>();
        if (mode == "menu")
        {
            s.Add(new Step { Delay = 4f, Name = "menu_touch" });
            s.Add(new Step { Delay = 1f, Name = "menu_kb", Touch = false });
            s.Add(new Step { Delay = 0.5f, Name = "menu_rotate", PortraitShot = true });
            return s;
        }
        for (var i = 0; i < 5; i++)
            s.Add(new Step { Delay = 2.5f, Name = $"game_{i:00}" });
        s.Add(new Step { Delay = 3f, Name = "hud", Before = SkipCutscene });
        s.Add(new Step { Delay = 0.5f, Name = "hud_kb", Touch = false });
        s.Add(new Step { Delay = 0.5f, Name = "hud_hint_boss", Before = () => SetHintBoss(true), After = () => SetHintBoss(false) });
        s.Add(new Step { Delay = 0.5f, Name = "pause", Before = () => Call(Ui(), "OpenPause"), After = () => Call(Ui(), "ClosePause", 0) });
        s.Add(new Step { Delay = 0.8f, Name = "bag", Before = () => Call(Get(Ui(), "LootScreens"), "Open", true), After = () => Call(Get(Ui(), "LootScreens"), "Close") });
        s.Add(new Step { Delay = 0.8f, Name = "enhance", Before = () => Call(Get(Ui(), "LootScreens"), "Open", false), After = () => Call(Get(Ui(), "LootScreens"), "Close") });
        s.Add(new Step { Delay = 0.8f, Name = "banner", Before = () => Call(Get(Ui(), "Banner"), "Show", "第一章　熱浪急件", 4f) });
        s.Add(new Step { Delay = 0.8f, Name = "clear", Before = () =>
        {
            Call(Get(Ui(), "Banner"), "Hide");
            // 走實際遊戲的路徑（session 事件），訂閱通關事件的介面（例如背包圖示淡出）才會跟著動
            Call(Get(Ui(), "_session"), "NotifyStageCleared", 185.4f);
        } });
        s.Add(new Step { Delay = 0.5f, Name = "rotate", PortraitShot = true });
        return s;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        var now = (float)EditorApplication.timeSinceStartup;
        if (!EditorApplication.isPlaying)
        {
            if (now > Timeout * 2f)
            {
                Debug.LogError(LogPrefix + "一直沒有進入 Play");
                Finish(1);
            }
            return;
        }
        var start = SessionState.GetFloat(StartKey, -1f);
        if (start < 0f)
        {
            SessionState.SetFloat(StartKey, now);
            SessionState.SetFloat(StepAtKey, now);
            return;
        }
        if (now - start > Timeout)
        {
            Debug.LogError(LogPrefix + "逾時");
            Finish(1);
            return;
        }

        var steps = Steps(SessionState.GetString(ModeKey, "game"));
        var index = SessionState.GetInt(StepKey, 0);
        if (index >= steps.Count)
        {
            Finish(0);
            return;
        }
        var step = steps[index];
        Try(() => SetTouch(step.Touch), "觸控模式");
        var at = SessionState.GetFloat(StepAtKey, now);
        // Before 在拍之前 Delay 秒做（做過的步驟記在 beforeKey）
        var beforeKey = StepKey + ".Before";
        if (SessionState.GetInt(beforeKey, -1) != index)
        {
            SessionState.SetInt(beforeKey, index);
            SessionState.SetFloat(StepAtKey, now);
            _sizeIndex = -1;
            Try(step.Before, step.Name + " 前置");
            return;
        }
        if (now - at < step.Delay)
            return;
        try
        {
            if (!Shoot(step, now))
                return;
            Debug.Log(LogPrefix + $"{now - start:0.0} 秒：拍 {step.Name}");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Finish(1);
            return;
        }
        Try(step.After, step.Name + " 收尾");
        SessionState.SetInt(StepKey, index + 1);
    }

    /// <summary>
    /// 這一步的各個尺寸依序：切 Game 視窗尺寸 → 等 <see cref="SettleTime"/> → ScreenCapture（幀尾才寫檔）→ 等檔案出現。
    /// 全部拍完回 true。
    /// </summary>
    private static bool Shoot(Step step, float now)
    {
        var sizes = step.PortraitShot ? new[] { Portrait } : Landscape;
        if (_pendingPath != null)
        {
            if (!File.Exists(_pendingPath) || new FileInfo(_pendingPath).Length == 0)
            {
                if (now - _pendingSince > 10f)
                    throw new Exception(LogPrefix + $"等不到截圖 {_pendingPath}（Game 視窗沒有重畫？）");
                return false;
            }
            _pendingPath = null;
        }
        else if (_sizeIndex >= 0 && now >= _readyAt)
        {
            // 這裡（Editor 的 update）讀到的 Screen 是 Editor 視窗，不是 Game 視窗；遊戲自己的 Update 讀到的才是切換後的尺寸
            var size = sizes[_sizeIndex];
            _pendingPath = Path.Combine(SessionState.GetString(DirKey, "Temp"), $"{step.Name}_{size.x}x{size.y}.png");
            if (File.Exists(_pendingPath))
                File.Delete(_pendingPath);
            _pendingSince = now;
            ScreenCapture.CaptureScreenshot(_pendingPath);
            return false;
        }
        if (_sizeIndex >= 0 && now < _readyAt)
            return false;
        _sizeIndex++;
        if (_sizeIndex >= sizes.Length)
        {
            _sizeIndex = -1;
            return true;
        }
        SetGameViewSize(sizes[_sizeIndex]);
        _readyAt = now + SettleTime;
        return false;
    }

    private static void Try(Action action, string what)
    {
        if (action == null)
            return;
        try
        {
            action();
        }
        catch (Exception e)
        {
            Debug.LogWarning(LogPrefix + $"{what}失敗：{e}");
        }
    }
    #endregion

    #region 遊戲操作（反射）
    private static Type GameType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name);
            if (t != null)
                return t;
        }
        throw new Exception(LogPrefix + $"找不到型別 {name}");
    }

    private static object FindView(string typeName)
    {
        var obj = UnityEngine.Object.FindAnyObjectByType(GameType(typeName));
        if (obj == null)
            throw new Exception(LogPrefix + $"找不到 {typeName}");
        return obj;
    }

    private static object Ui() => FindView("Sidescroller.StoryUi");

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static object Get(object target, string name)
    {
        var type = target as Type ?? target.GetType();
        var inst = target is Type ? null : target;
        var p = type.GetProperty(name, Any);
        if (p != null)
            return p.GetValue(inst);
        var f = type.GetField(name, Any);
        if (f != null)
            return f.GetValue(inst);
        throw new Exception(LogPrefix + $"{type.Name} 沒有 {name}");
    }

    private static void SetField(Type type, string name, object value) => type.GetField(name, Any).SetValue(null, value);

    private static object Call(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var inst = target is Type ? null : target;
        foreach (var m in type.GetMethods(Any))
        {
            if (m.Name == name && m.GetParameters().Length == args.Length)
                return m.Invoke(inst, args);
        }
        throw new Exception(LogPrefix + $"{type.Name} 沒有 {name}({args.Length})");
    }

    private static void SetTouch(bool on)
    {
        var t = GameType("Sidescroller.SsTouch");
        SetField(t, "_detected", true);
        SetField(t, "_active", on);
    }

    private static void SkipCutscene()
    {
        var runner = Get(Ui(), "_runner");
        if (runner != null && (bool)Get(runner, "CanSkip"))
            Call(runner, "RequestSkip");
    }

    private static void SetHintBoss(bool on)
    {
        var session = Get(Ui(), "_session");
        Call(session, "SetHint", on ? "A／D 或 ←／→：移動　J：甩包攻擊" : null);
        Call(session, "SetBoss", on, 37, 60, null);
    }
    #endregion

    #region Game 視窗尺寸（UnityEditor 內部 API，用反射）
    private static readonly Assembly EditorAsm = typeof(Editor).Assembly;

    private static object SizeGroup()
    {
        var sizesType = EditorAsm.GetType("UnityEditor.GameViewSizes");
        var instance = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
        var groupType = sizesType.GetProperty("currentGroupType").GetValue(instance);
        return sizesType.GetMethod("GetGroup").Invoke(instance, new[] { groupType });
    }

    /// <summary>把 Game 視窗切成固定解析度 size（沒有就加一個名稱 <see cref="SizePrefix"/> 開頭的自訂尺寸）。</summary>
    private static void SetGameViewSize(Vector2Int size)
    {
        var group = SizeGroup();
        var gt = group.GetType();
        var count = (int)gt.GetMethod("GetTotalCount").Invoke(group, null);
        var index = -1;
        for (var i = 0; i < count && index < 0; i++)
        {
            var s = gt.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
            var st = s.GetType();
            if ((int)st.GetProperty("width").GetValue(s) == size.x && (int)st.GetProperty("height").GetValue(s) == size.y &&
                Convert.ToInt32(st.GetProperty("sizeType").GetValue(s)) == 1)
                index = i;
        }
        if (index < 0)
        {
            var sizeType = EditorAsm.GetType("UnityEditor.GameViewSizeType");
            var gvs = Activator.CreateInstance(EditorAsm.GetType("UnityEditor.GameViewSize"),
                Enum.ToObject(sizeType, 1), size.x, size.y, $"{SizePrefix}{size.x}x{size.y}");
            gt.GetMethod("AddCustomSize").Invoke(group, new[] { gvs });
            index = count;
        }
        var gameView = EditorWindow.GetWindow(EditorAsm.GetType("UnityEditor.GameView"));
        // 和使用者在 Game 視窗下拉選單選尺寸走同一條路（會重算畫面大小）
        gameView.GetType().GetMethod("SizeSelectionCallback", Any).Invoke(gameView, new object[] { index, null });
        gameView.Repaint();
    }

    /// <summary>移除這個工具加進 Game 視窗的自訂尺寸（不留在使用者的 Editor 設定裡）。</summary>
    private static void RemoveGameViewSizes()
    {
        var group = SizeGroup();
        var gt = group.GetType();
        var builtin = (int)gt.GetMethod("GetBuiltinCount").Invoke(group, null);
        var custom = (int)gt.GetMethod("GetCustomCount").Invoke(group, null);
        for (var i = builtin + custom - 1; i >= builtin; i--)
        {
            var s = gt.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
            var name = (string)s.GetType().GetProperty("baseText").GetValue(s);
            if (name != null && name.StartsWith(SizePrefix, StringComparison.Ordinal))
                gt.GetMethod("RemoveCustomSize").Invoke(group, new object[] { i });
        }
    }
    #endregion

    private static void Finish(int code)
    {
        Try(RemoveGameViewSizes, "移除 Game 視窗尺寸");
        EditorPrefs.SetBool(DevBootPref, false);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseString(DirKey);
        SessionState.EraseString(ModeKey);
        SessionState.EraseFloat(StartKey);
        SessionState.EraseInt(StepKey);
        SessionState.EraseInt(StepKey + ".Before");
        SessionState.EraseFloat(StepAtKey);
        EditorApplication.update -= Tick;
        Debug.Log(LogPrefix + (code == 0 ? "完成" : "失敗") + $"（exit {code}）");
        EditorApplication.Exit(code);
    }
}
