using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 素材總覽圖與法線方向檢查：在暫時的空場景裡鋪地板、擺上各素材，用 URP 2D 光源打光後拍一張 PNG。
///
/// 法線檢查：Rock 第 0 格分別只從右邊、左邊打點光，各拍「關法線」與「開法線」兩張。
/// 距離衰減本身就會讓靠近光的那半邊較亮，所以比的是開法線後「右-左」亮度差比關法線時多了多少：
/// 光在右要變大、光在左要變小，否則表示法線 X 方向反了或 _NormalMap 沒生效。
///
/// 需要 GPU，batchmode 不能加 -nographics：
///   Unity -batchmode -quit -projectPath &lt;專案&gt; -executeMethod SpriteSheetPreview.RenderOverview \
///     -overviewPath &lt;輸出.png&gt; -logFile &lt;log&gt;
/// 會開新的空場景且不存檔，Editor 裡執行前請先存好目前場景。
/// </summary>
public static class SpriteSheetPreview
{
    private const string LogPrefix = "[SpriteSheetPreview] ";
    private const int Width = 1280, Height = 800;   // 16×10 單位，每單位 80px
    private const float OrthoSize = 5f;
    private static readonly MethodInfo LateUpdateMethod =
        typeof(Light2D).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

    [MenuItem("Tools/Glowshaft/拍素材總覽圖", false, 120)]
    public static void RenderOverviewMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        string path = EditorUtility.SaveFilePanel("素材總覽圖", "", "GlowshaftOverview.png", "png");
        if (string.IsNullOrEmpty(path))
            return;
        string scenePath = EditorSceneManager.GetActiveScene().path;
        Run(path);
        if (!string.IsNullOrEmpty(scenePath))
            EditorSceneManager.OpenScene(scenePath);  // 拍完回到原本的場景
    }

    /// <summary>batchmode 入口；輸出路徑取 -overviewPath，預設 Temp/GlowshaftOverview.png。失敗時 exit 1。</summary>
    public static void RenderOverview()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-overviewPath");
        string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/GlowshaftOverview.png";
        bool ok = Run(path);
        if (!ok && Application.isBatchMode)
            EditorApplication.Exit(1);
    }

    private static bool Run(string outPath)
    {
        if (LateUpdateMethod == null)
        {
            // URP 改版拿掉了這個私有方法：點光不會更新 mesh，總覽圖與法線檢查都不可信
            Debug.LogError(LogPrefix + "找不到 Light2D.LateUpdate（URP 版本變了？），無法在 Editor 更新點光，請改用 Play Mode 檢查");
            return false;
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("PreviewCamera").AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = OrthoSize;
        cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        bool normalsOk = CheckNormalDirection(cam);

        // 地板：8×5 片 Floor（2×2 單位），四種變化交錯，看接縫
        Sprite[] floor = Sprites("Floor", "Floor_Idle");
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 5; y++)
                Place(floor[(x * 3 + y) % floor.Length], new Vector2(-7 + x * 2, -4 + y * 2), -100);

        Place(Sprites("Miner", "Miner_Idle")[0], new Vector2(-5.5f, 1.2f));
        Place(Sprites("Miner", "Miner_Run")[2 * 8 + 3], new Vector2(-3.5f, 1.2f));      // E 方向第 3 幀
        Place(Sprites("SlimeGreen", "SlimeGreen_Move")[0], new Vector2(-1.2f, 1.2f));
        Place(Sprites("SlimePurple", "SlimePurple_Move")[1 * 8], new Vector2(0.6f, 1.2f));  // SE
        Place(Sprites("SlimeOrange", "SlimeOrange_Move")[7 * 8], new Vector2(3f, 1.0f));    // SW
        Place(Sprites("Battery", "Battery_Idle")[0], new Vector2(5.5f, 1.2f));
        Sprite[] bullet = Sprites("Bullet", "Bullet_Idle");
        for (int k = 0; k < 3; k++)
            Place(bullet[k], new Vector2(-4.8f + k * 0.6f, 2.8f), 10, k * 20f);
        Sprite[] rock = Sprites("Rock", "Rock_Idle");
        Sprite[] crystal = Sprites("Crystal", "Crystal_Idle");
        for (int k = 0; k < 4; k++)
        {
            Place(rock[k], new Vector2(-6f + k * 1.7f, -2.6f));
            Place(crystal[k], new Vector2(1.2f + k * 1.6f, -2.6f));
        }

        AddLight("Global", Light2D.LightType.Global, Vector2.zero, Color.white, 0.18f, 0);
        AddLight("Headlamp", Light2D.LightType.Point, new Vector2(-4.5f, 0.2f), new Color(1f, 0.92f, 0.75f), 1.4f, 5.5f);
        AddLight("SlimeGlow", Light2D.LightType.Point, new Vector2(1.5f, 2.5f), new Color(0.5f, 1f, 0.5f), 1.1f, 3.5f);
        AddLight("CrystalGlow", Light2D.LightType.Point, new Vector2(4f, -1.8f), new Color(0.5f, 0.85f, 1f), 1.3f, 4.5f);
        AddLight("Warm", Light2D.LightType.Point, new Vector2(-4f, -3.5f), new Color(1f, 0.6f, 0.3f), 1.0f, 3.5f);

        Texture2D shot = Capture(cam);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllBytes(outPath, shot.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(shot);
        Debug.Log(LogPrefix + $"總覽圖已存到 {Path.GetFullPath(outPath)}");
        return normalsOk;
    }

    /// <summary>同一顆石頭分別從右、左打光，比較開／關法線時左右半邊的亮度差。</summary>
    private static bool CheckNormalDirection(Camera cam)
    {
        var sr = Place(Sprites("Rock", "Rock_Idle")[0], new Vector2(0, -0.6f));
        sr.transform.localScale = Vector3.one * 4f;
        float[] gain = new float[2];
        for (int side = 0; side < 2; side++)
        {
            float dir = side == 0 ? 1f : -1f;
            float off = Balance(cam, dir, Light2D.NormalMapQuality.Disabled);
            float on = Balance(cam, dir, Light2D.NormalMapQuality.Accurate);
            gain[side] = on - off;
        }
        UnityEngine.Object.DestroyImmediate(sr.gameObject);
        bool ok = gain[0] > 0.05f && gain[1] < -0.05f;
        string msg = $"法線方向檢查：開法線後 (右-左)/總和 的變化，光在右＝{gain[0]:+0.000;-0.000}，光在左＝{gain[1]:+0.000;-0.000}";
        if (ok) Debug.Log(LogPrefix + "PASS " + msg);
        else Debug.LogError(LogPrefix + "FAIL " + msg + "（應為正、負；可能法線 X 反了或 _NormalMap 沒生效）");
        return ok;
    }

    /// <summary>只有一盞點光（左或右）時畫面的 (右-左)/總和 亮度。</summary>
    private static float Balance(Camera cam, float dir, Light2D.NormalMapQuality quality)
    {
        Light2D l = AddLight("NormalCheck", Light2D.LightType.Point, new Vector2(dir * 3.5f, 0.5f), Color.white, 1.5f, 6f, quality);
        Texture2D tex = Capture(cam);
        float left = 0, right = 0;
        Color[] px = tex.GetPixels();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Color c = px[y * Width + x];
                float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                if (x < Width / 2) left += lum; else right += lum;
            }
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(l.gameObject);
        return (right - left) / Mathf.Max(1e-3f, right + left);
    }

    private static Sprite[] Sprites(string name, string sheet)
    {
        string path = $"{SpriteSheetImporter.SpritesRoot}/{name}/{sheet}.png";
        var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1))).ToArray();
        if (sprites.Length == 0)
            throw new InvalidOperationException($"{path} 沒有 sprite，請先執行 SpriteSheetImporter.ImportAll");
        return sprites;
    }

    private static SpriteRenderer Place(Sprite sprite, Vector2 pos, int order = 0, float angle = 0f)
    {
        var go = new GameObject(sprite.name);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0, 0, angle);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;  // 預設材質就是 URP 2D 的 Sprite-Lit-Default
        // 腳底 pivot 的物件依 y 排序：越下面越前面
        sr.sortingOrder = order != 0 ? order : Mathf.RoundToInt(-pos.y * 10);
        return sr;
    }

    private static Light2D AddLight(string name, Light2D.LightType type, Vector2 pos, Color color, float intensity, float radius,
        Light2D.NormalMapQuality quality = Light2D.NormalMapQuality.Accurate)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(pos.x, pos.y, 0);
        var light = go.AddComponent<Light2D>();
        light.lightType = type;
        light.color = color;
        light.intensity = intensity;
        light.targetSortingLayers = SortingLayer.layers.Select(l => l.id).ToArray();
        if (type == Light2D.LightType.Point)
        {
            light.pointLightInnerRadius = radius * 0.15f;
            light.pointLightOuterRadius = radius;
            // 法線設定沒有公開 setter，用序列化欄位設
            var so = new SerializedObject(light);
            so.FindProperty("m_NormalMapQuality").intValue = (int)quality;
            so.FindProperty("m_NormalMapDistance").floatValue = 1.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            // 光源 mesh 與包圍球在 LateUpdate 依半徑重建；batchmode 的 Editor 不跑 LateUpdate，要手動呼叫，
            // 否則半徑仍是預設值、包圍球是空的，點光會被剔除
            LateUpdateMethod.Invoke(light, null);
        }
        return light;
    }

    private static Texture2D Capture(Camera cam)
    {
        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }
}
