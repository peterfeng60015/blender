using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 阿甲 3D（Tools/Blender/Hero3D/hero3d.py 匯出的 AhJia3D.fbx）在 Unity 的預覽設定，可重複執行：
///   1. 材質 M_AhJiaToon（shader Sidescroller/AhJiaToon：頂點色＋3 階明暗＋描邊），並把 FBX 內的 M_AhJia 對應過去。
///   2. FBX 匯入設定：Generic 骨架、套用軸向轉換、動畫片段改成短名（Idle、Run…）、Idle／Run／Jump 循環。
///   3. 動畫控制器 AhJiaPreview：依序輪播 6 個動作。
///   4. 預覽場景 Hero3DPreview：3/4 視角與側面（遊戲視角）各一隻，按 Play 就會動。
/// 只是美術預覽，不進 Build Settings、不進資源包；場景裡沒有任何遊戲模組（Assembly-CSharp）的元件。
/// </summary>
public static class Hero3DPreviewSetup
{
    private const string Folder = "Assets/Art/Hero3D";
    private const string ModelPath = Folder + "/AhJia3D.fbx";
    private const string ShaderName = "Sidescroller/AhJiaToon";
    private const string MaterialPath = Folder + "/M_AhJiaToon.mat";
    private const string ControllerPath = Folder + "/AhJiaPreview.controller";
    private const string ScenePath = Folder + "/Hero3DPreview.unity";
    private const string LogPrefix = "[Hero3DPreviewSetup] ";

    /// <summary>輪播順序與每段播放的次數（非循環的動作 &gt; 1 表示停在最後一格一下）。</summary>
    private static readonly (string Name, float Repeats)[] Sequence =
    {
        ("Idle", 2f), ("Run", 4f), ("Jump", 3f), ("Melee", 1.6f), ("Shoot", 1.8f), ("Hurt", 2.2f),
    };

    private static readonly HashSet<string> LoopingClips = new HashSet<string> { "Idle", "Run", "Jump" };

    [MenuItem("Tools/Hero3D/建立預覽場景", false, 200)]
    public static void SetupMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Setup();
        Debug.Log(LogPrefix + $"完成：已開啟 {ScenePath}，按 Play 播放動作");
    }

    public static void Setup()
    {
        var material = SetupMaterial();
        SetupModelImporter(material);
        var controller = SetupController(LoadClips());
        BuildScene(controller);
    }

    /// <summary>
    /// batchmode 入口：Setup 後把每個動作取幾格、從預覽場景的相機拍成一張總覽（需要 GPU，不能加 -nographics）。
    /// 輸出路徑取 -capturePath，預設 Temp/Hero3DPreview.png。失敗時 exit 1。
    /// </summary>
    public static void SetupAndCaptureBatch()
    {
        try
        {
            Setup();
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-capturePath");
            Capture(i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/Hero3DPreview.png");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// 每個動作一列、取 5 格（用 AnimationClip.SampleAnimation，不必進 Play）。
    /// batchmode 手動 Camera.Render 時蒙皮不會跟著取樣即時更新，所以每格先 BakeMesh 成靜態網格再拍。
    /// </summary>
    private static void Capture(string path)
    {
        const int Cell = 360, Cols = 5;
        var clips = LoadClips();
        var cam = Camera.main;
        var actors = new[] { GameObject.Find("AhJia_ThreeQuarter"), GameObject.Find("AhJia_Side") };
        var skins = actors.Select(a => a.GetComponentInChildren<SkinnedMeshRenderer>()).ToArray();
        var baked = new Mesh[skins.Length];
        for (var k = 0; k < skins.Length; k++)
        {
            var proxy = new GameObject(skins[k].name + "_Baked");
            proxy.transform.SetParent(skins[k].transform, false);
            proxy.AddComponent<MeshRenderer>().sharedMaterials = skins[k].sharedMaterials;
            baked[k] = new Mesh();
            proxy.AddComponent<MeshFilter>().sharedMesh = baked[k];
            skins[k].enabled = false;
        }
        var rt = new RenderTexture(Cell * 2, Cell, 24, RenderTextureFormat.ARGB32);
        var sheet = new Texture2D(Cell * 2 * Cols, Cell * Sequence.Length, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        cam.aspect = 2f;
        for (var r = 0; r < Sequence.Length; r++)
        {
            var clip = clips[Sequence[r].Name];
            Debug.Log(LogPrefix + $"{clip.name}：{clip.length:0.00} 秒、{clip.frameRate} fps、{AnimationUtility.GetCurveBindings(clip).Length} 條曲線、loop={clip.isLooping}");
            for (var c = 0; c < Cols; c++)
            {
                var t = clip.length * c / (LoopingClips.Contains(clip.name) ? Cols : Cols - 1);
                for (var k = 0; k < actors.Length; k++)
                {
                    clip.SampleAnimation(actors[k], t);
                    skins[k].BakeMesh(baked[k], true);
                }
                cam.Render();
                RenderTexture.active = rt;
                // 第一個動作放最上面（貼圖原點在左下）
                sheet.ReadPixels(new Rect(0, 0, Cell * 2, Cell), c * Cell * 2, (Sequence.Length - 1 - r) * Cell);
            }
        }
        sheet.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
        Debug.Log(LogPrefix + $"總覽已存到 {System.IO.Path.GetFullPath(path)}");
    }

    #region 材質與匯入設定

    private static Material SetupMaterial()
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null)
            throw new Exception(LogPrefix + $"找不到 shader {ShaderName}（AhJiaToon.shader 編譯失敗？）");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    private static void SetupModelImporter(Material material)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new Exception(LogPrefix + $"找不到模型 {ModelPath}");
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.bakeAxisConversion = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_AhJia"), material);

        // FBX 裡的片段名稱是「AhJia_Rig|AhJia_Idle」，取最後一個 '_' 之後當短名
        var clips = new List<ModelImporterClipAnimation>();
        foreach (var clip in importer.defaultClipAnimations)
        {
            var shortName = clip.name.Substring(clip.name.LastIndexOf('_') + 1);
            clip.name = shortName;
            clip.loopTime = LoopingClips.Contains(shortName);
            clips.Add(clip);
        }
        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();
    }

    private static Dictionary<string, AnimationClip> LoadClips()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
            .ToDictionary(c => c.name);
        foreach (var (name, _) in Sequence)
        {
            if (!clips.ContainsKey(name))
                throw new Exception(LogPrefix + $"FBX 裡沒有動畫片段 {name}（現有：{string.Join(", ", clips.Keys)}）");
        }
        return clips;
    }

    #endregion

    #region 動畫控制器與場景

    private static AnimatorController SetupController(Dictionary<string, AnimationClip> clips)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var states = new List<AnimatorState>();
        for (var i = 0; i < Sequence.Length; i++)
        {
            var state = machine.AddState(Sequence[i].Name, new Vector3(250f, 60f * i, 0f));
            state.motion = clips[Sequence[i].Name];
            states.Add(state);
        }
        machine.defaultState = states[0];
        for (var i = 0; i < states.Count; i++)
        {
            var transition = states[i].AddTransition(states[(i + 1) % states.Count]);
            transition.hasExitTime = true;
            transition.exitTime = Sequence[i].Repeats;
            transition.hasFixedDuration = true;
            transition.duration = 0.12f;
        }
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void BuildScene(AnimatorController controller)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.80f, 0.82f, 0.86f);
        cam.fieldOfView = 22f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        camGo.transform.position = new Vector3(0.7f, 0.8f, -4.2f);
        camGo.transform.LookAt(new Vector3(0.7f, 0.55f, 0f));

        // 模型匯入後面向 -Z（朝相機）；相機在 -Z 看 +Z，畫面右邊＝+X
        // 3/4 視角一隻（臉朝相機偏右）、側面一隻（遊戲裡的視角，面向畫面右邊）
        Spawn(model, controller, "AhJia_ThreeQuarter", new Vector3(0f, 0f, 0f), -45f);
        Spawn(model, controller, "AhJia_Side", new Vector3(1.4f, 0f, 0f), -90f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
    }

    private static void Spawn(GameObject model, AnimatorController controller, string name, Vector3 position, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = name;
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var animator = go.GetComponent<Animator>();
        if (animator == null)
            animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
    }

    #endregion
}
