using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sidescroller
{
    /// <summary>
    /// 新作的啟動與關閉（interfaces.md §4.1、§5）。全部用程式建立，不載入場景檔。
    /// Boot 建場景、相機與 <see cref="GameFlow"/>（同步）；GameFlow 下一幀開始非同步初始化（讀素材、建關卡）。
    /// </summary>
    public static class SidescrollerGame
    {
        public const string SceneName = "Sidescroller";

        private static Scene _scene;
        /// <summary>Boot 時的 active 場景（選單已先切回 Entry），Shutdown 時切回它。</summary>
        private static Scene _entryScene;
        private static CancellationTokenSource _lifetime;

        public static bool IsRunning { get; private set; }

        /// <summary>目前這一局的流程（沒在跑時是 null）；給自動化測試讀狀態。</summary>
        internal static GameFlow Flow { get; private set; }

        /// <summary>由 GameSelectMenu 呼叫；同步部分失敗時丟例外（不留半套物件）。</summary>
        public static void Boot()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Sidescroller.SidescrollerGame] 遊戲已啟動，忽略重複呼叫");
                return;
            }

            ResetStatics();
            Time.timeScale = 1f;
            // 原生 Android 預設鎖 30fps，動作手感差；WebGL 交給瀏覽器 rAF，桌機與 Editor 不動。重複設定無害
            if (Application.platform == RuntimePlatform.Android)
                Application.targetFrameRate = 60;

            var previous = SceneManager.GetActiveScene();
            _entryScene = previous;
            try
            {
                _scene = SceneManager.CreateScene(UniqueSceneName());
                SceneManager.SetActiveScene(_scene);
                _lifetime = new CancellationTokenSource();
                var camera = CreateCamera();
                Flow = GameFlow.Create(camera, _lifetime.Token);
                IsRunning = true;
                Debug.Log("[Sidescroller.SidescrollerGame] 已啟動");
            }
            catch (Exception)
            {
                _lifetime?.Cancel();
                _lifetime?.Dispose();
                _lifetime = null;
                Flow = null;
                if (_scene.IsValid())
                {
                    if (previous.IsValid())
                        SceneManager.SetActiveScene(previous);
                    SceneManager.UnloadSceneAsync(_scene);
                }
                _scene = default;
                throw;
            }
        }

        /// <summary>通關畫面「再玩一次」：Shutdown → Boot。Boot 失敗時改回選單。</summary>
        public static void Restart()
        {
            Shutdown();
            try
            {
                Boot();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sidescroller.SidescrollerGame] 重新開始失敗：{ex}");
                ReturnToMenu(ex.Message);
            }
        }

        /// <summary>Shutdown → GameSelectMenu.Show(error)。</summary>
        public static void ReturnToMenu(string error = null)
        {
            Shutdown();
            try
            {
                GameSelectMenu.Show(error);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sidescroller.SidescrollerGame] 回到選擇遊戲失敗：{ex}");
            }
        }

        /// <summary>清本遊戲全部 static（GameSelectMenu.Boot 會呼叫）。</summary>
        public static void ResetStatics()
        {
            // 不重載 domain 時，上一次 Play 中途停止（沒走 Shutdown）留下的狀態也要清掉
            if (_lifetime != null)
            {
                try
                {
                    _lifetime.Cancel();
                    _lifetime.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
                _lifetime = null;
            }
            IsRunning = false;
            Flow = null;
            _scene = default;
            _entryScene = default;
            SsArt.ResetCache();
            SsInput.Reset();
            ActorRegistry.Clear();
            LootVisuals.ResetCache();
        }

        #region 內部
        private static void Shutdown()
        {
            if (_lifetime != null)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
                _lifetime = null;
            }
            Time.timeScale = 1f;
            ActorRegistry.Clear();
            if (_scene.IsValid() && _scene.isLoaded)
            {
                foreach (var root in _scene.GetRootGameObjects())
                    root.SetActive(false);
                if (_entryScene.IsValid() && _entryScene.isLoaded && _entryScene != _scene)
                    SceneManager.SetActiveScene(_entryScene);
                SceneManager.UnloadSceneAsync(_scene);
            }
            _scene = default;
            Flow = null;
            IsRunning = false;
            ResetStatics();
        }

        private static string UniqueSceneName()
        {
            var name = SceneName;
            for (var i = 2; SceneManager.GetSceneByName(name).IsValid(); i++)
                name = $"{SceneName}#{i}";
            return name;
        }

        private static Camera CreateCamera()
        {
            var go = new GameObject("GameCamera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, SsIds.CameraSize, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = SsIds.CameraSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.29f, 0.18f, 0.36f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            go.AddComponent<AudioListener>();
            return cam;
        }
        #endregion
    }
}
