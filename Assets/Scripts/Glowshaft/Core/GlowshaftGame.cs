using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Glowshaft
{
    /// <summary>
    /// 遊戲啟動：由 <c>AppBootstrap.Run()</c> 呼叫。全部用程式建立，不載入任何場景檔
    /// （HybridCLR：build 裡的場景不能序列化引用 Assembly-CSharp 的型別）。
    /// </summary>
    public static class GlowshaftGame
    {
        public const string SceneName = "Glowshaft";
        public const float CameraSize = 5f;

        /// <summary>被 <see cref="HideEntryScene"/> 關掉的 Entry 根物件與開機控制器，失敗時用來還原。</summary>
        private static readonly List<GameObject> HiddenEntryRoots = new List<GameObject>();
        private static Behaviour _hiddenEntryController;

        public static void Boot()
        {
            if (GameManager.Instance != null)
            {
                Debug.LogWarning("[Glowshaft] 遊戲已啟動，忽略重複呼叫");
                return;
            }

            // 專案關掉了進 Play 時的 domain 重載，static 會留著上一次 Play 的狀態
            ArtLibrary.ResetCache();
            GameInput.Reset();
            HiddenEntryRoots.Clear();
            _hiddenEntryController = null;

            var entry = SceneManager.GetActiveScene();
            var scene = default(Scene);
            try
            {
                // 先把遊戲場景、相機、GameManager 都建好，最後才隱藏 Entry：
                // 中途任何一步丟例外時，Entry 的畫面與開機狀態文字都還在，錯誤看得到
                scene = SceneManager.CreateScene(UniqueSceneName());
                SceneManager.SetActiveScene(scene);
                GameManager.Create(CreateCamera());
                HideEntryScene(entry);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Glowshaft] 遊戲啟動失敗，還原 Entry 畫面：{ex}");
                RestoreEntryScene();
                if (scene.IsValid())
                {
                    if (entry.IsValid())
                        SceneManager.SetActiveScene(entry);
                    SceneManager.UnloadSceneAsync(scene);
                }
                // 往外丟給 EntrySceneController：它會在畫面上顯示「遊戲模組代碼執行失敗」與訊息
                throw;
            }
        }

        /// <summary>同名的執行期場景還在時（例如上一次沒卸載乾淨）CreateScene 會丟例外，改用加編號的名稱。</summary>
        private static string UniqueSceneName()
        {
            var name = SceneName;
            for (var i = 2; SceneManager.GetSceneByName(name).IsValid(); i++)
                name = $"{SceneName}#{i}";
            return name;
        }

        private static Camera CreateCamera()
        {
            var camGo = new GameObject("GameCamera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CameraSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            camGo.AddComponent<AudioListener>();
            return cam;
        }

        /// <summary>
        /// 關掉 Entry 場景裡會跟遊戲衝突的東西：它的相機、Canvas、燈（3D Light 與 Light2D，避免與 World 的
        /// Global Light 2D 重複），以及 EntrySceneController 的 OnGUI 狀態文字。
        /// EntrySceneController 本身的物件保留（它的開機流程在 Run() 回傳後還會寫一次狀態）。
        /// </summary>
        private static void HideEntryScene(Scene entry)
        {
            if (!entry.IsValid())
                return;
            foreach (var root in entry.GetRootGameObjects())
            {
                var controller = root.GetComponentInChildren<EntryScene.EntrySceneController>(true);
                if (controller != null)
                {
                    if (controller.enabled)
                    {
                        controller.enabled = false;
                        _hiddenEntryController = controller;
                    }
                    continue;
                }
                if (!root.activeSelf)
                    continue;
                if (root.GetComponentInChildren<Camera>(true) != null ||
                    root.GetComponentInChildren<Canvas>(true) != null ||
                    root.GetComponentInChildren<Light>(true) != null ||
                    root.GetComponentInChildren<Light2D>(true) != null)
                {
                    root.SetActive(false);
                    HiddenEntryRoots.Add(root);
                }
            }
        }

        /// <summary>把 <see cref="HideEntryScene"/> 關掉的東西開回來（啟動失敗時用）。</summary>
        private static void RestoreEntryScene()
        {
            foreach (var root in HiddenEntryRoots)
            {
                if (root != null)
                    root.SetActive(true);
            }
            HiddenEntryRoots.Clear();
            if (_hiddenEntryController != null)
                _hiddenEntryController.enabled = true;
            _hiddenEntryController = null;
        }
    }
}
