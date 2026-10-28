using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// batchmode 驗證：從 Entry 場景進 Play（開發期開關 Sidescroller.DevBoot 直接進《熱浪急件》），
/// 讓遊戲跑一段時間，在幾個時間點叫主角播指定動作（ICutsceneActor.PlayAction／SetFacing，和演出腳本同一條路），
/// 稍後把遊戲相機拍成 PNG，並記錄主角是 3D 還是 sprite、Animator 目前的狀態。
///
///   Unity -batchmode -projectPath &lt;專案&gt; -executeMethod Hero3DPlayCapture.RunBatch \
///     -captureDir &lt;輸出資料夾&gt; -logFile &lt;log&gt;
/// 不能加 -quit（要讓編輯器繼續跑 Play）、不能加 -nographics（要算圖）。拍完自己結束；失敗或逾時 exit 1。
/// 進 Play 會 domain reload，進度存在 SessionState；結束時把 DevBoot 開關設回 false。
/// </summary>
[InitializeOnLoad]
public static class Hero3DPlayCapture
{
    private const string ActiveKey = "Hero3DPlayCapture.Active";
    private const string DirKey = "Hero3DPlayCapture.Dir";
    private const string StartKey = "Hero3DPlayCapture.Start";
    private const string ShotKey = "Hero3DPlayCapture.Shot";
    private const string DevBootPref = "Sidescroller.DevBoot";
    private const string EntryScenePath = "Assets/Scenes/Entry.unity";
    private const string LogPrefix = "[Hero3DPlayCapture] ";

    /// <summary>進 Play 後第幾秒拍；拍之前 <see cref="Lead"/> 秒叫主角播對應的動作（null＝不動、照遊戲本身）。</summary>
    private static readonly float[] ShotTimes = { 5f, 8f, 10f, 12f, 14f, 16f, 18f };
    private static readonly string[] ShotActions = { null, "Run", "Jump", "Melee", "Shoot", "Hurt", "Run" };
    /// <summary>每一張要不要先轉向左邊（最後一張檢查轉身）。</summary>
    private static readonly bool[] ShotFaceLeft = { false, false, false, false, false, false, true };
    private const float Lead = 0.2f;
    private const string TriggeredKey = "Hero3DPlayCapture.Triggered";
    private const float Timeout = 90f;

    static Hero3DPlayCapture()
    {
        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.update += Tick;
    }

    public static void RunBatch()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-captureDir");
            var dir = Path.GetFullPath(i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/Hero3DPlayCapture");
            Directory.CreateDirectory(dir);

            AssetPackageCollectorSetup.Setup();
            EditorSceneManager.OpenScene(EntryScenePath);
            EditorPrefs.SetBool(DevBootPref, true);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(DirKey, dir);
            SessionState.SetFloat(StartKey, -1f);
            SessionState.SetInt(ShotKey, 0);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Finish(1);
        }
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
            return;
        }
        var elapsed = now - start;
        var shot = SessionState.GetInt(ShotKey, 0);
        if (shot < ShotTimes.Length && SessionState.GetInt(TriggeredKey, -1) < shot && elapsed >= ShotTimes[shot] - Lead)
        {
            SessionState.SetInt(TriggeredKey, shot);
            var hero = UnityEngine.Object.FindAnyObjectByType<Sidescroller.HeroController>();
            if (hero != null)
            {
                hero.SetFacing(ShotFaceLeft[shot] ? -1 : 1);
                if (ShotActions[shot] != null)
                    hero.PlayAction(ShotActions[shot], ShotActions[shot] == "Run" || ShotActions[shot] == "Jump");
            }
        }
        if (shot < ShotTimes.Length && elapsed >= ShotTimes[shot])
        {
            try
            {
                Capture(Path.Combine(SessionState.GetString(DirKey, "Temp"), $"play_{shot}.png"), elapsed);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(1);
                return;
            }
            SessionState.SetInt(ShotKey, ++shot);
        }
        if (shot >= ShotTimes.Length)
            Finish(0);
        else if (elapsed > Timeout)
            Finish(1);
    }

    private static void Capture(string path, float elapsed)
    {
        var cam = FindGameCamera();
        if (cam == null)
            throw new Exception(LogPrefix + "找不到啟用中的相機");

        var hero = GameObject.Find("Hero");
        var info = hero == null ? "找不到 Hero" : DescribeHero(hero);
        Debug.Log(LogPrefix + $"{elapsed:0.0} 秒：相機 {cam.name}、{info}");

        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previous;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
    }

    private static string DescribeHero(GameObject hero)
    {
        var model = hero.transform.Find("Model");
        if (model == null)
            return $"主角是 sprite，位置 {hero.transform.position}";
        var animator = model.GetComponent<Animator>();
        var state = animator.GetCurrentAnimatorStateInfo(0);
        var name = "?";
        foreach (var clip in animator.GetCurrentAnimatorClipInfo(0))
            name = clip.clip.name;
        return $"主角是 3D，位置 {hero.transform.position}、yaw {model.localEulerAngles.y:0}、動作 {name}（{state.normalizedTime:0.00}）";
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

    private static void Finish(int code)
    {
        EditorPrefs.SetBool(DevBootPref, false);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseString(DirKey);
        SessionState.EraseFloat(StartKey);
        SessionState.EraseInt(ShotKey);
        SessionState.EraseInt(TriggeredKey);
        EditorApplication.update -= Tick;
        Debug.Log(LogPrefix + (code == 0 ? "完成" : "失敗") + $"（exit {code}）");
        EditorApplication.Exit(code);
    }
}
