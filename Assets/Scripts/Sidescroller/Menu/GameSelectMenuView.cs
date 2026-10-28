using TMPro;
using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 選擇遊戲選單的畫面與輸入（<see cref="GameSelectMenu"/> 建立）。建在目前的 active 場景（<c>GameSelect</c>）：
    /// 黑底相機＋Overlay Canvas。出現的那一幀不處理輸入（<see cref="MenuList"/>），用 unscaled time。
    /// 選項可以用滑鼠或手指直接點；操作提示依輸入裝置切換；直向時蓋直向提示。
    /// </summary>
    public sealed class GameSelectMenuView : MonoBehaviour
    {
        /// <summary>
        /// 選單列出的遊戲（依序）。使用者決定目前只列《熱浪急件》；螢火礦坑的程式與素材仍在專案裡，
        /// 要放回選單時在這裡加上 <see cref="GameSelectMenu.ChoiceGlowshaft"/>。
        /// </summary>
        private static readonly int[] Choices = { GameSelectMenu.ChoiceSidescroller };

        private MenuList _list;
        private TextMeshProUGUI _error;
        private TextMeshProUGUI _hint;
        private RotateHintView _rotate;
        private bool _chosen;

        public static GameSelectMenuView Create(TMP_FontAsset font)
        {
            #region 相機（黑底、不畫任何物件）
            var camGo = new GameObject("MenuCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            camGo.AddComponent<AudioListener>();
            #endregion

            var canvas = SsUiKit.CreateCanvas("MenuCanvas", 100);
            var view = canvas.gameObject.AddComponent<GameSelectMenuView>();
            view.Build(canvas.transform, font);
            return view;
        }

        public void SetError(string error)
        {
            var show = !string.IsNullOrEmpty(error);
            _error.gameObject.SetActive(show);
            if (show)
                _error.text = StoryTexts.MenuErrorPrefix + error.Replace('!', '！'); // 思源黑體字集沒有半形 !（例外訊息常見）
        }

        private void Build(Transform root, TMP_FontAsset font)
        {
            var bg = SsUiKit.Fill("Background", root);
            SsUiKit.AddImage(bg, new Color(0.09f, 0.06f, 0.14f));
            var stripe = SsUiKit.Rect("Stripe", root, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
            stripe.anchoredPosition = new Vector2(0f, 250f);
            stripe.sizeDelta = new Vector2(0f, 6f);
            SsUiKit.AddImage(stripe, SsUiKit.Magenta);

            var title = SsUiKit.Rect("Title", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            title.anchoredPosition = new Vector2(0f, 270f);
            title.sizeDelta = new Vector2(1000f, 120f);
            var t = SsUiKit.AddText(title, font, 88f, SsUiKit.Gold);
            t.characterSpacing = 10f;
            t.text = StoryTexts.MenuTitle;

            _list = new MenuList(root, font, 0.5f, 140f);
            var labels = new string[Choices.Length];
            for (var i = 0; i < Choices.Length; i++)
                labels[i] = Choices[i] == GameSelectMenu.ChoiceGlowshaft ? StoryTexts.GlowshaftTitle : StoryTexts.GameTitle;
            _list.Open(labels);

            var hint = SsUiKit.Rect("Hint", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            hint.anchoredPosition = new Vector2(0f, 90f);
            hint.sizeDelta = new Vector2(1200f, 50f);
            _hint = SsUiKit.AddText(hint, font, 34f, new Color(1f, 1f, 1f, 0.6f));
            _hint.text = SsInput.TouchActive ? StoryTexts.MenuHintTouch : StoryTexts.MenuHint;

            var error = SsUiKit.Rect("Error", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            error.anchoredPosition = new Vector2(0f, 170f);
            error.sizeDelta = new Vector2(1600f, 90f);
            _error = SsUiKit.AddText(error, font, 32f, SsUiKit.Danger);
            _error.gameObject.SetActive(false);

            _rotate = new RotateHintView(root, font, null);
        }

        private void Update()
        {
            if (_chosen || _list == null)
                return;
            var hint = SsInput.TouchActive ? StoryTexts.MenuHintTouch : StoryTexts.MenuHint;
            if (_hint.text != hint)
                _hint.text = hint;
            // 直向時蓋提示、不接受輸入（選單沒有遊戲進行，不必暫停）
            if (_rotate.Refresh())
                return;
            var i = _list.Tick();
            if (i < 0)
                return;
            _chosen = true;
            GameSelectMenu.OnChosen(Choices[i]);
        }
    }
}
