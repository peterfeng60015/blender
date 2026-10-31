using Cysharp.Threading.Tasks;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EntryScene
{
    /// <summary>
    /// Entry 的載入畫面（uGUI）：背景、Logo、目前狀態文字、進度條、失敗時的「重試」、以及提示窗（主體更新等）。
    /// 素材在 Assets/Entry/UI（ComfyUI 產生，Tools/ComfyUI/assets.json 的 entry_*），中文字型是除錯主控台也在用的思源黑體；
    /// 都由 Entry 場景引用（<see cref="EntrySceneController"/> 的 loadingAssets），跟主體一起打包——
    /// 這時候遊戲模組與 AssetPackage 都還沒載入，不能用那邊的素材與字型。指派由 Assets/Editor/EntryUi/EntryUiSetup.cs 做。
    ///
    /// 遊戲模組不建 EventSystem（自己讀觸控），所以這裡要按鈕時自己建一個，<see cref="Dispose"/> 時一起拆掉。
    /// </summary>
    public sealed class EntryLoadingUi
    {
        [Serializable]
        public sealed class Assets
        {
            public TMP_FontAsset font;
            public Sprite background;
            public Sprite barFrame;
            public Sprite barFill;
            public Sprite panel;
            public Sprite logo;
        }

        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float BarWidth = 1200f;
        private const float BarHeight = 110f;
        /// <summary>填充條在外框裡的內縮（外框的凹槽）。</summary>
        private const float FillInsetX = 28f, FillInsetY = 22f;
        /// <summary>顯示的進度追上目標的速度（每秒比例）：分段跳動時看起來是滑過去。</summary>
        private const float ProgressSpeed = 1.5f;

        private readonly Assets _assets;
        private readonly GameObject _root;
        private readonly GameObject _eventSystem;
        private readonly TextMeshProUGUI _status;
        private readonly TextMeshProUGUI _percent;
        private readonly RectTransform _fill;
        private readonly RectTransform _logo;
        private readonly GameObject _retry;
        private readonly GameObject _dialog;
        private readonly TextMeshProUGUI _dialogText;
        private readonly RectTransform _dialogButtons;
        private float _target;
        private float _shown;
        private UniTaskCompletionSource<int> _dialogResult;

        public EntryLoadingUi(Assets assets, Action onRetry)
        {
            _assets = assets ?? new Assets();
            if (_assets.font == null)
                Debug.LogError("[EntryLoadingUi] 沒有指派中文字型（執行 Tools/Entry/設定載入畫面），文字會顯示成方框");

            _root = new GameObject("EntryLoadingCanvas");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // 手機多半比 16:9 寬，以高度為準
            _root.AddComponent<GraphicRaycaster>();
            _root.AddComponent<Ticker>().OnTick = Tick;
            var root = (RectTransform)_root.transform;

            if (EventSystem.current == null)
            {
                _eventSystem = new GameObject("EntryEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                UnityEngine.Object.DontDestroyOnLoad(_eventSystem);
            }

            #region 背景：主視覺蓋滿畫面＋下方壓暗
            var bg = Rect("Background", root, Vector2.zero, Vector2.one);
            AddImage(bg, new Color(0.09f, 0.06f, 0.14f), null);
            if (_assets.background != null)
            {
                var art = Rect("Art", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                AddImage(art, Color.white, _assets.background);
                var fitter = art.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = _assets.background.rect.width / _assets.background.rect.height;
            }
            var shade = Rect("Shade", bg, Vector2.zero, new Vector2(1f, 0.36f));
            AddImage(shade, new Color(0.06f, 0.03f, 0.1f, 0.72f), null);
            #endregion

            if (_assets.logo != null)
            {
                _logo = Rect("Logo", root, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f));
                _logo.sizeDelta = new Vector2(320f, 320f);
                AddImage(_logo, Color.white, _assets.logo);
            }

            _status = AddText(Rect("Status", root, new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f)), 40f, new Vector2(1600f, 64f));

            #region 進度條：外框＋填充（九宮格，寬度跟著進度）＋百分比
            var bar = Rect("Bar", root, new Vector2(0.5f, 0.14f), new Vector2(0.5f, 0.14f));
            bar.sizeDelta = new Vector2(BarWidth, BarHeight);
            var frame = AddImage(bar, Color.white, _assets.barFrame);
            if (_assets.barFrame == null)
                frame.color = new Color(0.17f, 0.11f, 0.29f);
            _fill = Rect("Fill", bar, new Vector2(0f, 0f), new Vector2(0f, 1f));
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.anchoredPosition = new Vector2(FillInsetX, 0f);
            _fill.offsetMin = new Vector2(FillInsetX, FillInsetY);
            _fill.offsetMax = new Vector2(FillInsetX, -FillInsetY);
            var fillImage = AddImage(_fill, Color.white, _assets.barFill);
            if (_assets.barFill == null)
                fillImage.color = new Color(1f, 0.55f, 0.2f);
            _percent = AddText(Rect("Percent", bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)), 34f, new Vector2(300f, BarHeight));
            #endregion

            _retry = AddButton(Rect("Retry", root, new Vector2(0.5f, 0.33f), new Vector2(0.5f, 0.33f)), "重試", true, onRetry).gameObject;
            _retry.SetActive(false);

            #region 提示窗：壓暗整個畫面（擋住後面的點擊）＋面板＋文字＋一排按鈕
            var dialog = Rect("Dialog", root, Vector2.zero, Vector2.one);
            AddImage(dialog, new Color(0f, 0f, 0f, 0.6f), null).raycastTarget = true;
            var panel = Rect("Panel", dialog, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.sizeDelta = new Vector2(1180f, 600f);
            var panelImage = AddImage(panel, Color.white, _assets.panel);
            if (_assets.panel == null)
                panelImage.color = new Color(0.17f, 0.11f, 0.29f, 0.96f);
            var textRect = Rect("Text", panel, new Vector2(0f, 0.3f), Vector2.one);
            textRect.offsetMin = new Vector2(90f, 0f);
            textRect.offsetMax = new Vector2(-90f, -70f);
            _dialogText = AddText(textRect, 38f, Vector2.zero);
            _dialogText.textWrappingMode = TextWrappingModes.Normal;
            _dialogButtons = Rect("Buttons", panel, new Vector2(0f, 0f), new Vector2(1f, 0.3f));
            _dialogButtons.offsetMin = new Vector2(90f, 60f);
            _dialogButtons.offsetMax = new Vector2(-90f, 0f);
            var layout = _dialogButtons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 60f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            _dialog = dialog.gameObject;
            _dialog.SetActive(false);
            #endregion

            SetStatus(string.Empty);
            SetProgress(0f, true);
        }

        public void SetStatus(string text) => _status.text = text ?? string.Empty;

        /// <summary>設定進度（0～1）。immediate＝不滑動直接跳到（例如重試時歸零）。</summary>
        public void SetProgress(float progress, bool immediate = false)
        {
            _target = Mathf.Clamp01(progress);
            if (immediate)
                _shown = _target;
            ApplyProgress();
        }

        public void ShowRetry(bool show) => _retry.SetActive(show);

        /// <summary>顯示提示窗，等玩家按下其中一個按鈕，回傳按鈕的索引（第一個是主要按鈕）。</summary>
        public UniTask<int> AskAsync(string text, params string[] buttons)
        {
            _dialogResult?.TrySetResult(-1);
            for (var i = _dialogButtons.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_dialogButtons.GetChild(i).gameObject);
            _dialogText.text = text;
            var result = new UniTaskCompletionSource<int>();
            _dialogResult = result;
            for (var i = 0; i < buttons.Length; i++)
            {
                var index = i;
                AddButton(Rect("Button" + i, _dialogButtons, Vector2.zero, Vector2.zero), buttons[i], i == 0, () =>
                {
                    if (_dialogResult != result)
                        return;
                    _dialogResult = null;
                    _dialog.SetActive(false);
                    result.TrySetResult(index);
                });
            }
            _dialog.SetActive(true);
            return result.Task;
        }

        /// <summary>進遊戲後拆掉整個畫面（自己建的 EventSystem 一起拆）。</summary>
        public void Dispose()
        {
            _dialogResult?.TrySetResult(-1);
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            if (_eventSystem != null)
                UnityEngine.Object.Destroy(_eventSystem);
        }

        private void Tick(float dt)
        {
            if (!Mathf.Approximately(_shown, _target))
            {
                _shown = Mathf.MoveTowards(_shown, _target, ProgressSpeed * dt);
                ApplyProgress();
            }
            if (_logo != null)
                _logo.anchoredPosition = new Vector2(0f, Mathf.Sin(Time.unscaledTime * 2.2f) * 8f);
        }

        private void ApplyProgress()
        {
            // 九宮格的兩端不能再縮，寬度從「兩端合起來」開始長；0 時整條藏起來
            var full = BarWidth - FillInsetX * 2f;
            var min = Mathf.Min(full, (BarHeight - FillInsetY * 2f) * 1.2f);
            _fill.gameObject.SetActive(_shown > 0.001f);
            _fill.sizeDelta = new Vector2(Mathf.Lerp(min, full, _shown), _fill.sizeDelta.y);
            _percent.text = $"{Mathf.RoundToInt(_shown * 100f)}%";
        }

        #region 建 UI 的小工具
        private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Image AddImage(RectTransform rt, Color color, Sprite sprite)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero)
                image.type = Image.Type.Sliced;
            return image;
        }

        private TextMeshProUGUI AddText(RectTransform rt, float size, Vector2 sizeDelta)
        {
            if (sizeDelta != Vector2.zero)
                rt.sizeDelta = sizeDelta;
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_assets.font != null)
                text.font = _assets.font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>按鈕：主要按鈕用填充條的漸層，次要的用外框；按下時壓暗。</summary>
        private Button AddButton(RectTransform rt, string label, bool primary, Action onClick)
        {
            rt.sizeDelta = new Vector2(420f, 110f);
            var sprite = primary ? _assets.barFill : _assets.barFrame;
            var image = AddImage(rt, Color.white, sprite);
            if (sprite == null)
                image.color = primary ? new Color(1f, 0.55f, 0.2f) : new Color(0.17f, 0.11f, 0.29f);
            image.raycastTarget = true;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());
            var text = AddText(Rect("Label", rt, Vector2.zero, Vector2.one), 40f, Vector2.zero);
            text.text = label;
            text.color = primary ? new Color(0.17f, 0.06f, 0.18f) : Color.white;
            return button;
        }
        #endregion

        /// <summary>每幀叫 <see cref="Tick"/>（進度滑動、Logo 浮動）。</summary>
        private sealed class Ticker : MonoBehaviour
        {
            public Action<float> OnTick;

            private void Update() => OnTick?.Invoke(Time.unscaledDeltaTime);
        }
    }
}
