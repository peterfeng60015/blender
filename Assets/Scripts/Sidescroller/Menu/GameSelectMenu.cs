using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Sidescroller
{
    /// <summary>
    /// 遊戲選擇選單（interfaces.md §4.3、§7）。只由 <c>AppBootstrap.Run()</c> 呼叫 <see cref="Boot"/>。
    /// 選單建自己的執行期場景 <c>GameSelect</c>、相機（黑底）與 Overlay Canvas；選定後卸載選單場景、切回 Entry，再呼叫遊戲的 Boot。
    /// 開發期開關：Editor 下 EditorPrefs <see cref="DevBootPref"/> 為 true 時略過選單直接進新作（Player 一律顯示選單）。
    /// 這是 story 唯一保留 static 狀態的型別（Entry 隱藏紀錄、選單實例、字型快取），每次 Boot 先清掉。
    /// </summary>
    public static class GameSelectMenu
    {
        public const string DevBootPref = "Sidescroller.DevBoot";
        public const string SceneName = "GameSelect";
        private const string FontName = "思源黑體-Medium";

        /// <summary>被隱藏的 Entry 根物件與開機控制器，啟動失敗時用來還原。</summary>
        private static readonly List<GameObject> HiddenEntryRoots = new List<GameObject>();
        private static Behaviour _hiddenEntryController;
        private static Scene _entryScene;
        private static Scene _menuScene;
        private static GameSelectMenuView _view;
        private static TMP_FontAsset _font;
        private static bool _fontSearched;

        public static void Boot()
        {
            HiddenEntryRoots.Clear();
            _hiddenEntryController = null;
            _menuScene = default;
            _view = null;
            _font = null;
            _fontSearched = false;
            SidescrollerGame.ResetStatics();
            Time.timeScale = 1f;
            _entryScene = SceneManager.GetActiveScene();

            try
            {
                HideEntryScene(_entryScene);
                if (DevBootEnabled())
                {
                    Debug.Log($"[Sidescroller.GameSelectMenu] EditorPrefs {DevBootPref}=true，略過選單直接進新作");
                    SidescrollerGame.Boot();
                }
                else
                {
                    Show();
                }
            }
            catch (Exception)
            {
                DestroyMenu();
                RestoreEntryScene();
                throw;
            }
        }

        /// <summary>顯示選單；error 不為 null 時在選單上顯示「啟動失敗：…」。選單已經在時只更新錯誤訊息。</summary>
        public static void Show(string error = null)
        {
            if (error != null)
                Debug.LogError($"[Sidescroller.GameSelectMenu] {StoryTexts.MenuErrorPrefix}{error}");
            if (_view != null)
            {
                _view.SetError(error);
                return;
            }

            var previous = SceneManager.GetActiveScene();
            try
            {
                _menuScene = SceneManager.CreateScene(UniqueSceneName());
                SceneManager.SetActiveScene(_menuScene);
                _view = GameSelectMenuView.Create(Font());
                _view.SetError(error);
                Debug.Log("[Sidescroller.GameSelectMenu] 顯示選擇遊戲選單");
            }
            catch (Exception)
            {
                if (previous.IsValid() && previous.isLoaded && previous != _menuScene)
                    SceneManager.SetActiveScene(previous);
                DestroyMenu();
                throw;
            }
        }

        /// <summary>思源黑體（Entry 的除錯主控台 prefab 已載入，interfaces.md §7.4）。找不到時記錯誤並改用 TMP 預設字型。</summary>
        public static TMP_FontAsset Font()
        {
            if (_fontSearched)
                return _font;
            _fontSearched = true;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f != null && f.name == FontName)
                {
                    _font = f;
                    return _font;
                }
            }
            Debug.LogError($"[Sidescroller.GameSelectMenu] 找不到 TMP 字型 {FontName}（Entry 場景的 IngameDebugConsole 被拿掉了？），改用 TMP 預設字型，中文會變方塊");
            _font = TMP_Settings.defaultFontAsset;
            return _font;
        }

        #region 選定
        internal const int ChoiceGlowshaft = 0;
        internal const int ChoiceSidescroller = 1;

        /// <summary>由 <see cref="GameSelectMenuView"/> 在選定時呼叫（不用 Action&lt;int&gt;，少一個值型別泛型）。</summary>
        internal static void OnChosen(int choice)
        {
            Debug.Log($"[Sidescroller.GameSelectMenu] 選擇 {(choice == ChoiceGlowshaft ? StoryTexts.GlowshaftTitle : StoryTexts.GameTitle)}");
            DestroyMenu();
            try
            {
                if (choice == ChoiceGlowshaft)
                    Glowshaft.GlowshaftGame.Boot();
                else
                    SidescrollerGame.Boot();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                try
                {
                    Show(ex.Message);
                }
                catch (Exception showEx)
                {
                    // 呼叫端是選單的 Update，沒有人能接住；只記錯誤（畫面停在 Entry 已隱藏的黑畫面）
                    Debug.LogError($"[Sidescroller.GameSelectMenu] 顯示選單失敗：{showEx}");
                }
            }
        }

        /// <summary>選單場景根物件全部關掉 → 切回 Entry（GlowshaftGame.Boot 用 GetActiveScene 當成 Entry）→ 非同步卸載。</summary>
        private static void DestroyMenu()
        {
            _view = null;
            if (!_menuScene.IsValid() || !_menuScene.isLoaded)
            {
                _menuScene = default;
                return;
            }
            foreach (var root in _menuScene.GetRootGameObjects())
                root.SetActive(false);
            if (_entryScene.IsValid() && _entryScene.isLoaded)
                SceneManager.SetActiveScene(_entryScene);
            SceneManager.UnloadSceneAsync(_menuScene);
            _menuScene = default;
        }

        private static string UniqueSceneName()
        {
            var name = SceneName;
            for (var i = 2; SceneManager.GetSceneByName(name).IsValid(); i++)
                name = $"{SceneName}#{i}";
            return name;
        }
        #endregion

        #region Entry 隱藏與還原
        private static bool DevBootEnabled()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(DevBootPref, false);
#else
            return false;
#endif
        }

        /// <summary>照 GlowshaftGame.HideEntryScene：關掉有 Camera／Canvas／Light／Light2D 的根物件與 EntrySceneController 的 OnGUI。</summary>
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
        #endregion
    }
}
