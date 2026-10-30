using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// batchmode UI 截圖：從 Entry 場景進 Play，在幾個時間點把遊戲相機＋所有 Overlay Canvas 拍成 PNG（多種解析度）。
/// 選單模式（-uiShotMode menu）走正常開機進選擇遊戲選單；遊戲模式（game）開 DevBoot 直接進《熱浪急件》，
/// 依序拍開場演出、HUD、暫停、背包、強化、字卡、通關畫面與直向提示（用反射打開，和玩家操作走同一組方法）。
///
///   Unity -batchmode -projectPath &lt;專案&gt; -executeMethod UiShotCapture.RunBatch \
///     -uiShotMode game -captureDir &lt;輸出資料夾&gt; -logFile &lt;log&gt;
/// 不能加 -quit、不能加 -nographics。拍完自己結束；失敗或逾時 exit 1。
///
/// Overlay Canvas 不會被 Camera.Render 畫到，所以拍照時暫時改成 Screen Space Camera、掛到一台只看截圖圖層的相機，
/// 底下墊一張遊戲畫面一起畫；拍完還原。觸控模式用反射打開（手機是主要平台）。
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
    private const int ShotLayer = 31;
    private const float Timeout = 150f;

    /// <summary>橫向解析度：S22 Ultra（3088×1440，19.3:9）縮到高 1080、一般 16:9。</summary>
    private static readonly Vector2Int[] Landscape = { new Vector2Int(2316, 1080), new Vector2Int(1920, 1080) };
    private static readonly Vector2Int Portrait = new Vector2Int(1080, 2316);

    private sealed class Step
    {
        public float Delay;
        public string Name;
        public Action Before;
        public Action After;
        public bool PortraitShot;
        /// <summary>觸控模式（每幀重設：開機時 SsTouch.ForgetDevice 會清掉）。</summary>
        public bool Touch = true;
        /// <summary>Before 在拍的同一幀才做（直向提示會被下一幀的 Refresh 收起）。</summary>
        public bool Instant;
    }

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
            s.Add(new Step { Delay = 0.5f, Name = "menu_rotate", Before = () => ShowRotate(FindView("Sidescroller.GameSelectMenuView")), PortraitShot = true, Instant = true });
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
            Call(Ui(), "OnStageCleared", 185.4f);
        } });
        s.Add(new Step { Delay = 0.5f, Name = "rotate", Before = () => ShowRotate(Ui()), PortraitShot = true, Instant = true });
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
            if (!step.Instant)
                Try(step.Before, step.Name + " 前置");
            return;
        }
        if (now - at < step.Delay)
            return;
        if (step.Instant)
            Try(step.Before, step.Name + " 前置");
        try
        {
            var dir = SessionState.GetString(DirKey, "Temp");
            if (step.PortraitShot)
                Capture(Path.Combine(dir, $"{step.Name}_{Portrait.x}x{Portrait.y}.png"), Portrait);
            else
                foreach (var size in Landscape)
                    Capture(Path.Combine(dir, $"{step.Name}_{size.x}x{size.y}.png"), size);
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

    private static void ShowRotate(object owner)
    {
        var rotate = Get(owner, "_rotate");
        ((RectTransform)Get(rotate, "_root")).gameObject.SetActive(true);
    }
    #endregion

    #region 截圖
    private static void Capture(string path, Vector2Int size)
    {
        var w = size.x;
        var h = size.y;
        var game = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var cam = FindGameCamera();
        if (cam != null)
        {
            var previous = cam.targetTexture;
            cam.targetTexture = game;
            cam.Render();
            cam.targetTexture = previous;
        }
        var pixels = RenderUi(rt, game);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        game.Release();
        UnityEngine.Object.DestroyImmediate(game);
    }

    private static Color[] Read(RenderTexture rt)
    {
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        var c = tex.GetPixels();
        UnityEngine.Object.DestroyImmediate(tex);
        return c;
    }

    /// <summary>
    /// 把所有啟用中的 Overlay 根 Canvas 暫時掛到截圖相機上，墊一張遊戲畫面（最底層的 RawImage）一起畫，
    /// 混色由引擎做（和實機一樣的色彩空間）；畫完還原。
    /// </summary>
    private static Color[] RenderUi(RenderTexture rt, RenderTexture game)
    {
        var camGo = new GameObject("UiShotCamera");
        camGo.transform.position = new Vector3(0f, -50000f, -100f);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 1 << ShotLayer;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        cam.targetTexture = rt;
        cam.enabled = false;

        var bgGo = new GameObject("UiShotBackground", typeof(RectTransform));
        bgGo.layer = ShotLayer;
        var bgCanvas = bgGo.AddComponent<Canvas>();
        bgCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        bgCanvas.worldCamera = cam;
        bgCanvas.planeDistance = 20f;
        bgCanvas.sortingOrder = -10000;
        var raw = new GameObject("Frame", typeof(RectTransform));
        raw.layer = ShotLayer;
        var rawRt = (RectTransform)raw.transform;
        rawRt.SetParent(bgGo.transform, false);
        rawRt.anchorMin = Vector2.zero;
        rawRt.anchorMax = Vector2.one;
        rawRt.offsetMin = Vector2.zero;
        rawRt.offsetMax = Vector2.zero;
        raw.AddComponent<RawImage>().texture = game;

        var canvases = new List<Canvas>();
        var layers = new Dictionary<Transform, int>();
        foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay || !c.isActiveAndEnabled)
                continue;
            canvases.Add(c);
            foreach (var t in c.GetComponentsInChildren<Transform>(true))
            {
                layers[t] = t.gameObject.layer;
                t.gameObject.layer = ShotLayer;
            }
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = 10f;
            RefreshScaler(c);
        }
        try
        {
            Canvas.ForceUpdateCanvases();
            cam.Render();
            return Read(rt);
        }
        finally
        {
            foreach (var c in canvases)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.worldCamera = null;
                RefreshScaler(c);
            }
            foreach (var kv in layers)
            {
                if (kv.Key != null)
                    kv.Key.gameObject.layer = kv.Value;
            }
            Canvas.ForceUpdateCanvases();
            UnityEngine.Object.DestroyImmediate(bgGo);
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }

    private static void RefreshScaler(Canvas c)
    {
        var scaler = c.GetComponent<CanvasScaler>();
        if (scaler == null)
            return;
        scaler.enabled = false;
        scaler.enabled = true;
    }

    private static Camera FindGameCamera()
    {
        Camera best = null;
        foreach (var cam in Camera.allCameras)
        {
            if (cam.enabled && cam.gameObject.activeInHierarchy && (best == null || cam.depth > best.depth))
                best = cam;
        }
        return best;
    }
    #endregion

    private static void Finish(int code)
    {
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
