using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sidescroller
{
    /// <summary>
    /// 選擇遊戲選單的畫面與輸入（<see cref="GameSelectMenu"/> 建立）。建在目前的 active 場景（<c>GameSelect</c>）：
    /// 黑底相機＋Overlay Canvas。海報式版面：左邊遊戲 Logo 與選項，右邊主角主視覺，背景夕陽城市慢速視差捲動。
    /// 先等 <see cref="SsArt"/> 讀完（最多 <see cref="LoadTimeout"/> 秒，讀不到就用純色）才建畫面、播進場動畫；
    /// 選項彈出後才接受輸入（<see cref="MenuList"/> 出現的那一幀不處理輸入），一律用 unscaled time。
    /// 選項可以用滑鼠或手指直接點；操作提示依輸入裝置切換；直向時蓋直向提示。
    /// 右下角（安全區內）一行主角模型的授權標註。
    /// </summary>
    public sealed class GameSelectMenuView : MonoBehaviour
    {
        /// <summary>選單列出的遊戲（依序）。目前只有《熱浪急件》；新增遊戲時在 <see cref="GameSelectMenu"/> 加選項常數並列在這裡。</summary>
        private static readonly int[] Choices = { GameSelectMenu.ChoiceSidescroller };

        private const float LoadTimeout = 4f;
        /// <summary>選單專用素材（交了才用）：背景主視覺取代城市視差、主角主視覺放右邊。</summary>
        private const string MenuBgLocation = "SsMenuBg_Idle";
        private const string MenuHeroLocation = "SsMenuHero_Idle";
        private const string MenuLogoBgLocation = "SsMenuLogoBg_Idle";

        #region 進場時間軸（秒）
        private const float BgFadeEnd = 0.5f;
        private const float LogoAt = 0.25f;
        private const float HeroAt = 0.45f;
        private const float HeroSlide = 0.6f;
        private const float ListAt = 0.75f;
        private const float ListPop = 0.35f;
        /// <summary>選項彈出完成才開放輸入。</summary>
        private const float ListOpenAt = ListAt + ListPop;
        private const float HintAt = 1.0f;
        #endregion

        private TMP_FontAsset _font;
        private RectTransform _root;
        private UiSkin _skin;
        private Texture2D _shade;
        private CancellationTokenSource _cts;
        private bool _loadDone;
        private float _loadStart;
        /// <summary>false＝建畫面失敗後的簡易版面：不用任何素材（背景純色、沒有主角與 Logo 底圖）。</summary>
        private bool _useArt = true;
        private bool _buildTried;
        private bool _built;
        private string _pendingError;
        private float _time;
        private bool _listOpened;

        private CanvasGroup _bgGroup;
        private readonly List<ParallaxLayer> _layers = new List<ParallaxLayer>();
        private RectTransform _burst;
        private RectTransform _hero;
        private CanvasGroup _heroGroup;
        private UiPop _logoPop;
        private UiPop _listPop;
        private CanvasGroup _hintGroup;

        private MenuList _list;
        private TextMeshProUGUI _error;
        private TextMeshProUGUI _hint;
        private RotateHintView _rotate;
        private RectTransform _safe;
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
            view._font = font;
            view._root = (RectTransform)canvas.transform;
            view._cts = new CancellationTokenSource();
            view._loadStart = Time.realtimeSinceStartup;
            view.LoadAsync(view._cts.Token).Forget();
            return view;
        }

        public void SetError(string error)
        {
            if (!_built)
            {
                _pendingError = error;
                return;
            }
            var show = !string.IsNullOrEmpty(error);
            _error.gameObject.SetActive(show);
            if (show)
                _error.text = StoryTexts.MenuErrorPrefix + error.Replace('!', '！'); // 思源黑體字集沒有半形 !（例外訊息常見）
        }

        #region 讀取
        /// <summary>
        /// 讀 <see cref="SsArt"/>（UI 外框與背景都在裡面）；逾時或失敗照樣建畫面，<see cref="UiSkin"/> 與背景退回純色。
        /// 遊戲開機時 <see cref="SidescrollerGame.ResetStatics"/> 會清掉快取重讀（AssetPackage 的素材常駐，第二次很快）。
        /// </summary>
        private async UniTaskVoid LoadAsync(CancellationToken ct)
        {
            try
            {
                await SsArt.LoadAllAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // 選單關掉，或遊戲開機時 ResetCache 打斷：都不用再建
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Sidescroller.GameSelectMenu] 讀素材失敗，選單用純色：{ex.Message}");
            }
            finally
            {
                _loadDone = true;
            }
        }

        /// <summary>
        /// 素材讀完或逾時（真實時間，卡頓時也準）就建畫面，只建一次。建失敗時拆掉重建不用素材的簡易版面，
        /// 不會停在 Entry 已隱藏的黑畫面。
        /// </summary>
        private void TickLoading()
        {
            var waited = Time.realtimeSinceStartup - _loadStart;
            if (!_loadDone && waited < LoadTimeout)
                return;
            _buildTried = true;
            if (!_loadDone)
                Debug.LogWarning($"[Sidescroller.GameSelectMenu] 素材 {LoadTimeout} 秒內沒讀完，選單先用純色");
            else
                Debug.Log($"[Sidescroller.GameSelectMenu] 素材讀取 {waited:0.00} 秒");
            try
            {
                Build();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError("[Sidescroller.GameSelectMenu] 建選單畫面失敗，改用不含素材的簡易版面");
                Teardown();
                _useArt = false;
                Build();
            }
        }

        /// <summary>拆掉建到一半的畫面（Destroy 在這一幀結束才生效，重建的物件不受影響）。</summary>
        private void Teardown()
        {
            for (var i = _root.childCount - 1; i >= 0; i--)
                Destroy(_root.GetChild(i).gameObject);
            _skin?.Dispose();
            _skin = null;
            if (_shade != null)
                Destroy(_shade);
            _shade = null;
            _layers.Clear();
            _burst = null;
            _hero = null;
            _heroGroup = null;
        }

        /// <summary>素材讀完且是真素材才回傳（清單外的不去要，避免 SsArt 記警告）。</summary>
        private Sprite TryArt(string location, int variant = 0)
        {
            if (!_useArt || !SsArt.IsLoaded || !SsArt.IsKnownLocation(location))
                return null;
            var set = SsArt.Get(location);
            return set.IsPlaceholder || set.FrameCount <= variant ? null : set.Variant(variant);
        }
        #endregion

        #region 建畫面
        private void Build()
        {
            _skin = new UiSkin();
            var root = _root;

            #region 背景：主視覺或城市視差、左側壓暗、地面帶
            var bg = SsUiKit.Fill("Background", root);
            SsUiKit.AddImage(bg, new Color(0.09f, 0.06f, 0.14f));
            _bgGroup = bg.gameObject.AddComponent<CanvasGroup>();
            _bgGroup.alpha = 0f;
            var menuBg = TryArt(MenuBgLocation);
            if (menuBg != null)
            {
                _layers.Add(new ParallaxLayer(bg, "Key", menuBg, 1f, 0f, Color.white, true, true));
            }
            else
            {
                var far = TryArt("SsBgFar_Idle");
                if (far != null)
                    _layers.Add(new ParallaxLayer(bg, "Far", far, 1.12f, 14f, Color.white, false, true));
                var mid = TryArt("SsBgMid_Idle");
                if (mid != null)
                    _layers.Add(new ParallaxLayer(bg, "Mid", mid, 0.52f, 42f, new Color(0.5f, 0.45f, 0.66f), false, false));
            }

            // 左側由深到透明的壓暗，Logo 與選項在亮背景上也讀得到
            _shade = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MenuShade" };
            for (var x = 0; x < 64; x++)
            {
                var a = 1f - x / 63f;
                _shade.SetPixel(x, 0, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
            _shade.Apply(false, true);
            var shade = SsUiKit.Rect("Shade", bg, Vector2.zero, new Vector2(0.7f, 1f), new Vector2(0f, 0.5f));
            var shadeImg = shade.gameObject.AddComponent<RawImage>();
            shadeImg.texture = _shade;
            shadeImg.color = new Color(0.07f, 0.04f, 0.12f, 0.85f);
            shadeImg.raycastTarget = false;

            var hero = TryArt(MenuHeroLocation);
            if (hero != null)
            {
                // 腳底踩在地面帶上緣；在授權小字之下（小字建在後面，永遠疊在上面）
                _hero = SsUiKit.Rect("Hero", bg, new Vector2(0.75f, 0.115f), new Vector2(0.75f, 0.115f), new Vector2(0.5f, 0f));
                _hero.sizeDelta = new Vector2(700f, 820f); // 素材約 1000×1200：等比放進來時高度撐滿、腳底貼齊
                SsUiKit.AddImage(_hero, Color.white, hero);
                _heroGroup = _hero.gameObject.AddComponent<CanvasGroup>();
                _heroGroup.alpha = 0f;
            }

            var ground = SsUiKit.Rect("Ground", bg, Vector2.zero, new Vector2(1f, 0.12f), new Vector2(0.5f, 0f));
            SsUiKit.AddImage(ground, new Color(0.06f, 0.03f, 0.1f, 0.92f));
            var line = SsUiKit.Rect("Line", ground, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 0f));
            line.sizeDelta = new Vector2(0f, 4f);
            SsUiKit.AddImage(line, new Color(SsUiKit.Neon.r, SsUiKit.Neon.g, SsUiKit.Neon.b, 0.7f));
            #endregion

            // 前景都放在安全區裡，避開瀏海、圓角與 Home 指示條
            _safe = SsUiKit.Fill("SafeArea", root);
            SsUiKit.FitSafeArea(_safe);
            var column = SsUiKit.Rect("Column", _safe, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));

            #region Logo：光芒＋外框標題字＋霓虹底線，微斜
            var logo = SsUiKit.Rect("Logo", column, new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f));
            logo.sizeDelta = new Vector2(900f, 240f);
            logo.localRotation = Quaternion.Euler(0f, 0f, 4f);
            var logoBg = TryArt(MenuLogoBgLocation);
            if (logoBg != null)
            {
                var plate = SsUiKit.Rect("LogoBg", logo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                plate.sizeDelta = new Vector2(1100f, 400f);
                SsUiKit.AddImage(plate, Color.white, logoBg);
            }
            else
            {
                // 沒有 Logo 底圖時用外框素材的光芒慢慢轉
                _burst = SsUiKit.Rect("Burst", logo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                _burst.sizeDelta = new Vector2(1000f, 1000f);
                _skin.Add(_burst, UiSkin.Sunburst, 1f, new Color(1f, 0.78f, 0.23f, 0.3f));
            }
            var title = SsUiKit.Fill("Title", logo);
            _skin.Title(title, _font, 156f, SsUiKit.Gold, StoryTexts.GameTitle, 10f, 8f, 6f);
            var underline = SsUiKit.Rect("Underline", logo, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            underline.anchoredPosition = new Vector2(0f, -14f);
            underline.sizeDelta = new Vector2(560f, 10f);
            SsUiKit.AddImage(underline, SsUiKit.Neon);
            _logoPop = new UiPop(logo, 0.55f) { From = 0.6f };
            _logoPop.Play(LogoAt);
            #endregion

            #region 選項（彈出後才 Open、才接受輸入）
            _list = new MenuList(column, _font, 0.42f, 0f, _skin);
            _listPop = new UiPop(_list.Root, ListPop) { From = 0.8f };
            _listPop.Play(ListAt);
            #endregion

            #region 操作提示（左欄下方的膠囊）與錯誤訊息
            var hint = SsUiKit.Rect("Hint", column, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            hint.anchoredPosition = new Vector2(0f, 22f);
            hint.sizeDelta = new Vector2(620f, 68f);
            _skin.Add(SsUiKit.Fill("Pill", hint), UiSkin.Pill, 0.5f, new Color(1f, 1f, 1f, 0.92f));
            _hint = SsUiKit.AddText(SsUiKit.Fill("Text", hint), _font, 34f, SsUiKit.Paper);
            _hint.textWrappingMode = TextWrappingModes.NoWrap;
            _hint.text = HintText();
            _hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
            _hintGroup.alpha = 0f;

            var error = SsUiKit.Rect("Error", column, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            error.anchoredPosition = new Vector2(0f, 110f);
            error.sizeDelta = new Vector2(-60f, 90f);
            _error = SsUiKit.AddText(error, _font, 32f, SsUiKit.Danger);
            // 例外訊息可能很長：縮字塞進左欄，不蓋到選項與提示
            _error.enableAutoSizing = true;
            _error.fontSizeMin = 20f;
            _error.fontSizeMax = 32f;
            _error.gameObject.SetActive(false);
            #endregion

            // 授權標註：右下角、不換行；在安全區與地面帶裡，建在主角之後所以不會被蓋住
            var credit = SsUiKit.Rect("Credit", _safe, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            credit.anchoredPosition = new Vector2(-24f, 16f);
            credit.sizeDelta = new Vector2(700f, 36f);
            var c = SsUiKit.AddText(credit, _font, 24f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.BottomRight);
            c.textWrappingMode = TextWrappingModes.NoWrap;
            c.text = StoryTexts.MenuModelCredit;

            _rotate = new RotateHintView(root, _font, null);
            _built = true;
            Debug.Log("[Sidescroller.GameSelectMenu] 選單畫面建好，播進場動畫");
            SetError(_pendingError);
            _pendingError = null;
        }

        private static string HintText() => SsInput.TouchActive ? StoryTexts.MenuHintTouch : StoryTexts.MenuHint;

        /// <summary>只有一款遊戲時選項寫「開始遊戲」（Logo 已經是遊戲名稱）；多款時列遊戲名稱。</summary>
        private static string[] Labels()
        {
            var labels = new string[Choices.Length];
            for (var i = 0; i < Choices.Length; i++)
                labels[i] = Choices.Length == 1 ? StoryTexts.MenuStart : StoryTexts.GameTitle;
            return labels;
        }
        #endregion

        private void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (!_buildTried)
                TickLoading();
            if (!_built)
                return;
            SsUiKit.FitSafeArea(_safe);
            _time += dt;
            Animate(dt);
            if (_chosen)
                return;
            var hint = HintText();
            if (_hint.text != hint)
                _hint.text = hint;
            // 直向時蓋提示、不接受輸入（選單沒有遊戲進行，不必暫停）
            if (_rotate.Refresh())
                return;
            if (!_listOpened)
            {
                if (_time < ListOpenAt)
                    return;
                _listOpened = true;
                _list.Open(Labels());
            }
            var i = _list.Tick();
            if (i < 0)
                return;
            _chosen = true;
            GameSelectMenu.OnChosen(Choices[i]);
        }

        #region 動畫
        private void Animate(float dt)
        {
            _bgGroup.alpha = Mathf.Clamp01(_time / BgFadeEnd);
            var size = _root.rect.size;
            foreach (var layer in _layers)
                layer.Tick(size, dt);
            if (_burst != null)
                _burst.localRotation = Quaternion.Euler(0f, 0f, -_time * 8f);
            _logoPop.Tick(dt);
            _listPop.Tick(dt);
            if (_hero != null)
            {
                var t = Mathf.Clamp01((_time - HeroAt) / HeroSlide);
                var e = 1f - (1f - t) * (1f - t) * (1f - t);
                _hero.anchoredPosition = new Vector2((1f - e) * 500f, 0f);
                _heroGroup.alpha = Mathf.Clamp01(t * 2f);
            }
            // 提示淡入後慢慢呼吸（0.7～1）
            var h = _time - HintAt;
            _hintGroup.alpha = h < 0f ? 0f : Mathf.Min(Mathf.Clamp01(h / 0.4f), 0.85f + 0.15f * Mathf.Cos(h * Mathf.PI));
        }

        /// <summary>
        /// 一層背景：同一張圖橫向接成一排，貼齊畫面底部、高度＝畫面高×heightFrac，往左慢慢捲（速度 0＝不捲）。
        /// cover＝主視覺：單張等比放大蓋滿畫面、置中（不捲）。畫面大小變了（旋轉）才重排。
        /// </summary>
        private sealed class ParallaxLayer
        {
            private readonly RectTransform _holder;
            private readonly Sprite _sprite;
            private readonly float _heightFrac;
            private readonly float _speed;
            private readonly Color _tint;
            private readonly bool _cover;
            /// <summary>不透明的圖每塊多 1 單位蓋住接縫；上方透明的圖不重疊（半透明邊緣疊兩次會出現細線）。</summary>
            private readonly float _overlap;
            private readonly List<RectTransform> _tiles = new List<RectTransform>();
            private Vector2 _size;
            private float _tileW;
            private float _offset;

            public ParallaxLayer(RectTransform parent, string name, Sprite sprite, float heightFrac, float speed, Color tint, bool cover, bool opaque)
            {
                _holder = SsUiKit.Fill(name, parent);
                _sprite = sprite;
                _heightFrac = heightFrac;
                _speed = speed;
                _tint = tint;
                _cover = cover;
                _overlap = opaque ? 1f : 0f;
            }

            public void Tick(Vector2 size, float dt)
            {
                if (size.x <= 0f || size.y <= 0f)
                    return;
                if (size != _size)
                    Layout(size);
                if (_speed <= 0f || _tileW <= 0f)
                    return;
                _offset = Mathf.Repeat(_offset + _speed * dt, _tileW);
                for (var i = 0; i < _tiles.Count; i++)
                    _tiles[i].anchoredPosition = new Vector2(i * _tileW - _offset, 0f);
            }

            private void Layout(Vector2 size)
            {
                _size = size;
                var aspect = _sprite.rect.width / Mathf.Max(1f, _sprite.rect.height);
                if (_cover)
                {
                    var h = Mathf.Max(size.y, size.x / aspect);
                    var rt = Tile(0);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(h * aspect, h);
                    return;
                }
                var tileH = size.y * _heightFrac;
                _tileW = tileH * aspect;
                var n = Mathf.CeilToInt(size.x / _tileW) + 1;
                for (var i = 0; i < n; i++)
                {
                    var rt = Tile(i);
                    rt.gameObject.SetActive(true);
                    rt.sizeDelta = new Vector2(_tileW + _overlap, tileH);
                    rt.anchoredPosition = new Vector2(i * _tileW - _offset, 0f);
                }
                for (var i = n; i < _tiles.Count; i++)
                    _tiles[i].gameObject.SetActive(false);
            }

            private RectTransform Tile(int i)
            {
                while (_tiles.Count <= i)
                {
                    var rt = SsUiKit.Rect($"Tile{_tiles.Count}", _holder, Vector2.zero, Vector2.zero, Vector2.zero);
                    var img = SsUiKit.AddImage(rt, _tint, _sprite);
                    img.preserveAspect = false;
                    _tiles.Add(rt);
                }
                return _tiles[i];
            }
        }
        #endregion

        private void OnDestroy()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            _skin?.Dispose();
            _skin = null;
            if (_shade != null)
                Destroy(_shade);
        }
    }
}
