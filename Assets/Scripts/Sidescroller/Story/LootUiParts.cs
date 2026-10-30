using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Sidescroller
{
    #region 指標（滑鼠＋觸控）
    /// <summary>
    /// 背包與強化介面用的指標狀態（每幀 <see cref="Update"/> 一次）：滑鼠左鍵或「點擊」那根手指的按下、按住、放開與位置，
    /// 給拖曳用。觸控讀 <see cref="SsTouch"/>（介面開著時遊戲暫停，所有手指都算點擊）；剛觸控過的那段時間不理滑鼠（觸控模擬出來的）。
    /// 只讀 wasPressedThisFrame／isPressed 與 AxisControl 的 float，不呼叫 ReadValue&lt;Vector2&gt;。
    /// </summary>
    internal sealed class LootPointer
    {
        private Vector2 _lastMouse;
        private bool _mouseSeen;

        /// <summary>這一幀按下（位置在 <see cref="DownPos"/>）。</summary>
        public bool Down { get; private set; }
        public Vector2 DownPos { get; private set; }
        /// <summary>還按著。</summary>
        public bool Held { get; private set; }
        /// <summary>這一幀放開（同一幀按下又放開時 Down 與 Released 都成立）。</summary>
        public bool Released { get; private set; }
        /// <summary>這一幀有指標動作（滑鼠移動、按下、按住或放開；觸控按下、按住或放開）：游標跟著指標走。</summary>
        public bool Moved { get; private set; }
        /// <summary>目前位置（螢幕座標）。</summary>
        public Vector2 Pos { get; private set; }

        public void Update()
        {
            Down = Held = Released = Moved = false;

            // 觸控只看「點擊那根手指」：拖曳中另一根手指點一下不算按下
            SsTouch.PointerState(out var touchDown, out var touchHeld, out var touchReleased, out var touchPos);
            if (touchDown || touchHeld || touchReleased)
            {
                Down = touchDown;
                DownPos = touchPos;
                Held = touchHeld;
                Released = touchReleased;
                Pos = touchPos;
                Moved = true;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
                return;
            var mp = new Vector2(mouse.position.x.ReadValue(), mouse.position.y.ReadValue());
            var moved = _mouseSeen && (mp - _lastMouse).sqrMagnitude > 0.25f;
            _lastMouse = mp;
            _mouseSeen = true;
            if (SsTouch.MouseSuppressed)
                return;
            Down = mouse.leftButton.wasPressedThisFrame;
            DownPos = mp;
            Held = mouse.leftButton.isPressed;
            Released = mouse.leftButton.wasReleasedThisFrame;
            Moved = moved || Down || Released;
            // 滑鼠沒動也沒按時不改位置：觸控拿起道具後，手上的道具留在手指最後的位置
            if (Moved || Held)
                Pos = mp;
        }
    }
    #endregion

    #region 程式產生的圖形
    /// <summary>背包與強化介面共用的程式產生貼圖（柔邊圓）；跟著 <see cref="LootScreens.Dispose"/> 釋放。</summary>
    internal sealed class LootUiArt
    {
        private readonly Texture2D _discTexture;
        public readonly Sprite Disc;

        public LootUiArt()
        {
            const int size = 64;
            _discTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SsLootDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            var c = (size - 1) / 2f;
            var r = size / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / r;
                    // 中心實、外圈柔：光與煙都用這張
                    var a = Mathf.Clamp01(1f - d);
                    a = Mathf.SmoothStep(0f, 1f, a * 1.6f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            _discTexture.SetPixels32(pixels);
            _discTexture.Apply(false, true);
            Disc = Sprite.Create(_discTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            Disc.name = "SsLootDisc";
        }

        public void Dispose()
        {
            if (Disc != null)
                Object.Destroy(Disc);
            if (_discTexture != null)
                Object.Destroy(_discTexture);
        }
    }
    #endregion

    #region 面板外框
    /// <summary>背包、強化、丟掉確認共用的面板外框：全螢幕暗底、置中的面板（陰影＋外框）、面板上緣的緞帶標題。</summary>
    internal static class LootFrame
    {
        private static readonly Color BackdropColor = new Color(0.03f, 0.02f, 0.07f, 0.72f);
        /// <summary>進場動畫（縮放＋淡入）的長度（秒，unscaled）。</summary>
        public const float PopTime = 0.2f;
        /// <summary>緞帶中心在面板上緣往下多少（畫布單位）；緞帶上半截露出面板外。</summary>
        public const float RibbonInset = 18f;
        public const float RibbonH = 112f;

        /// <summary>
        /// 在 root（撐滿）加暗底，回傳置中的面板（大小由呼叫端設）。面板放在 window 底下，pop 播放時整組縮放淡入；
        /// 面板自己沒有 Image，外框是第一個子物件（之後加的子物件畫在外框上面）。
        /// </summary>
        public static RectTransform Panel(RectTransform root, UiSkin skin, out UiPop pop)
        {
            SsUiKit.AddImage(root, BackdropColor);
            var window = SsUiKit.Fill("Window", root);
            pop = new UiPop(window, PopTime) { From = 0.9f };
            var panel = SsUiKit.Rect("Panel", window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var shadow = SsUiKit.Fill("Shadow", panel);
            shadow.offsetMin = new Vector2(-20f, -36f);
            shadow.offsetMax = new Vector2(32f, 4f);
            skin.Add(shadow, UiSkin.Shadow, 0.8f);
            skin.Add(SsUiKit.Fill("Frame", panel), UiSkin.Panel, 0.6f);
            return panel;
        }

        /// <summary>面板上緣置中的緞帶標題（寬度跟著字長）。</summary>
        public static RectTransform Ribbon(RectTransform panel, UiSkin skin, TMP_FontAsset font, string text, float size)
        {
            var ribbon = SsUiKit.Rect("Ribbon", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            ribbon.anchoredPosition = new Vector2(0f, -RibbonInset);
            skin.Add(ribbon, UiSkin.Ribbon, 0.5f);
            var titleRt = SsUiKit.Fill("Title", ribbon);
            titleRt.anchoredPosition = new Vector2(0f, 4f);
            var title = skin.Title(titleRt, font, size, SsUiKit.Paper, text, 4f, 2f, 3.5f);
            ribbon.sizeDelta = new Vector2(title.GetPreferredValues(text).x + 220f, RibbonH);
            return ribbon;
        }
    }
    #endregion

    #region 按鈕
    /// <summary>
    /// 按鈕（<see cref="UiSkin"/> 外框＋有陰影的字）；點擊由呼叫端用 <see cref="Contains"/> 判斷（不建 EventSystem）。
    /// 選中用 ButtonOn（橘粉漸層）、其他用 ButtonOff（深紫）；停用時外框與字都變淡。
    /// </summary>
    internal sealed class LootButton
    {
        /// <summary>外框素材 1 px 在畫布上的大小。</summary>
        private const float SkinScale = 0.4f;
        private static readonly Color DisabledTint = new Color(1f, 1f, 1f, 0.4f);

        public readonly RectTransform Rt;
        public readonly Image Bg;
        public readonly TextMeshProUGUI Label;
        private readonly TextMeshProUGUI _shadow;
        private readonly UiSkin _skin;
        /// <summary>目前外框：0＝未選、1＝選中、2＝停用；-1＝還沒設定。</summary>
        private int _look = -1;

        public LootButton(string name, Transform parent, TMP_FontAsset font, string text, float fontSize, UiSkin skin)
        {
            _skin = skin;
            Rt = SsUiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            Bg = skin.Add(Rt, UiSkin.ButtonOff, SkinScale);
            var labelRt = SsUiKit.Fill("Label", Rt);
            (Label, _shadow) = UiSkin.Label(labelRt, font, fontSize, SsUiKit.Paper);
            SetText(text);
            SetState(false);
        }

        public bool Visible
        {
            get => Rt.gameObject.activeSelf;
            set
            {
                if (Rt.gameObject.activeSelf != value)
                    Rt.gameObject.SetActive(value);
            }
        }

        /// <summary>改字（陰影一起改）。</summary>
        public void SetText(string text)
        {
            if (Label.text == text)
                return;
            Label.text = text;
            _shadow.text = text;
        }

        /// <summary>字的大小、自動縮放與對齊（陰影一起改）。</summary>
        public void SetFont(float max, float min, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            foreach (var t in new[] { Label, _shadow })
            {
                t.fontSize = max;
                t.enableAutoSizing = min < max;
                t.fontSizeMax = max;
                t.fontSizeMin = min;
                t.alignment = align;
            }
        }

        /// <summary>字的左右內距（陰影一起改）。</summary>
        public void SetPadding(float left, float right)
        {
            foreach (var t in new[] { Label, _shadow })
            {
                var rt = t.rectTransform;
                rt.offsetMin = new Vector2(left, rt.offsetMin.y);
                rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
            }
        }

        /// <summary>字太長時結尾用「…」（陰影一起改）。</summary>
        public void SetEllipsis()
        {
            Label.overflowMode = TextOverflowModes.Ellipsis;
            _shadow.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>點擊判定（上下左右各多算 pad 個畫布單位，手指比較好按）。</summary>
        public bool Contains(Vector2 screenPos, float pad = 8f)
        {
            if (!Rt.gameObject.activeInHierarchy)
                return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, screenPos, null, out var local))
                return false;
            var r = Rt.rect;
            r.xMin -= pad;
            r.yMin -= pad;
            r.xMax += pad;
            r.yMax += pad;
            return r.Contains(local);
        }

        public void Place(Vector2 topLeft, Vector2 size)
        {
            Rt.anchoredPosition = topLeft;
            Rt.sizeDelta = size;
        }

        /// <summary>selected：鍵盤選到（ButtonOn）；enabled＝false 時變淡。字色用預設（白／米白）。</summary>
        public void SetState(bool selected, bool enabled = true) =>
            SetState(selected, enabled, enabled ? (selected ? Color.white : SsUiKit.Paper) : new Color(1f, 1f, 1f, 0.35f));

        /// <summary>同上，但字色由呼叫端指定（例如稀有度顏色）。</summary>
        public void SetState(bool selected, bool enabled, Color labelColor)
        {
            var look = !enabled ? 2 : selected ? 1 : 0;
            if (look != _look)
            {
                _look = look;
                _skin.Set(Bg, look == 1 ? UiSkin.ButtonOn : UiSkin.ButtonOff, SkinScale, look == 2 ? DisabledTint : (Color?)null);
            }
            if (Label.color != labelColor)
                Label.color = labelColor;
            var shadow = new Color(_shadow.color.r, _shadow.color.g, _shadow.color.b, enabled ? 0.7f : 0.25f);
            if (_shadow.color != shadow)
                _shadow.color = shadow;
        }
    }
    #endregion

    #region 道具格
    /// <summary>
    /// 一件道具的畫面：稀有度色的底與框、圖示（<see cref="LootVisuals.Icon"/>；還沒有圖示時用種類名稱代替）、
    /// 右上強化等級、右下零件數。
    /// </summary>
    internal sealed class ItemWidget
    {
        public readonly RectTransform Rt;
        private readonly Image _bg;
        private readonly Image _frame;
        private readonly Image _icon;
        private readonly TextMeshProUGUI _fallback;
        private readonly TextMeshProUGUI _level;
        private readonly TextMeshProUGUI _count;
        public ItemData Item { get; private set; }

        public ItemWidget(string name, Transform parent, TMP_FontAsset font)
        {
            Rt = SsUiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            var frameRt = SsUiKit.Fill("Frame", Rt);
            _frame = SsUiKit.AddImage(frameRt, Color.white);
            var bgRt = SsUiKit.Fill("Bg", Rt);
            bgRt.offsetMin = new Vector2(3f, 3f);
            bgRt.offsetMax = new Vector2(-3f, -3f);
            _bg = SsUiKit.AddImage(bgRt, Color.black);
            var iconRt = SsUiKit.Fill("Icon", Rt);
            iconRt.offsetMin = new Vector2(8f, 8f);
            iconRt.offsetMax = new Vector2(-8f, -8f);
            _icon = SsUiKit.AddImage(iconRt, Color.white);
            _fallback = SsUiKit.AddText(SsUiKit.Fill("Kind", Rt), font, 26f, SsUiKit.Paper);
            var levelRt = SsUiKit.Fill("Level", Rt);
            levelRt.offsetMin = new Vector2(6f, 4f);
            levelRt.offsetMax = new Vector2(-8f, -4f);
            _level = SsUiKit.AddText(levelRt, font, 28f, SsUiKit.Gold, TextAlignmentOptions.TopRight);
            var countRt = SsUiKit.Fill("Count", Rt);
            countRt.offsetMin = new Vector2(6f, 4f);
            countRt.offsetMax = new Vector2(-8f, -2f);
            _count = SsUiKit.AddText(countRt, font, 30f, Color.white, TextAlignmentOptions.BottomRight);
        }

        public bool Visible
        {
            get => Rt.gameObject.activeSelf;
            set
            {
                if (Rt.gameObject.activeSelf != value)
                    Rt.gameObject.SetActive(value);
            }
        }

        /// <summary>item＝null 時隱藏。cell＝一格的畫布單位（文字大小跟著縮放）。</summary>
        public void Set(ItemData item, float cell)
        {
            Item = item;
            Visible = item != null;
            if (item == null)
                return;
            var rarity = ItemData.RarityColor(item.Rarity);
            _frame.color = new Color(rarity.r, rarity.g, rarity.b, 0.85f);
            _bg.color = new Color(rarity.r * 0.22f, rarity.g * 0.22f, rarity.b * 0.22f, 0.95f);
            var sprite = LootVisuals.Icon(item);
            _icon.sprite = sprite;
            _icon.enabled = sprite != null;
            _icon.preserveAspect = true;
            _fallback.gameObject.SetActive(sprite == null);
            _fallback.text = sprite == null ? item.KindName : string.Empty;
            _fallback.fontSize = Mathf.Clamp(cell * 0.26f, 18f, 34f);
            _level.text = item.IsEquipment && item.Level > 0 ? "+" + item.Level : string.Empty;
            _level.fontSize = Mathf.Clamp(cell * 0.28f, 18f, 34f);
            _count.text = item.Kind == ItemKind.Part ? item.Count.ToString() : string.Empty;
            _count.fontSize = Mathf.Clamp(cell * 0.3f, 18f, 36f);
        }
    }
    #endregion

    #region 說明框
    /// <summary>
    /// 道具說明框（loot-design.md §5）：名稱（稀有度顏色）、種類、基礎數值、詞條、強化等級。
    /// 主框旁邊可以並列第二框（身上同欄的裝備）做比較。位置由呼叫端給「要貼著的矩形」，自動選左或右、不超出 root。
    /// </summary>
    internal sealed class LootTooltip
    {
        private const float Width = 440f;
        private const float Margin = 18f;
        private const float Gap = 10f;

        private readonly RectTransform _root;
        private readonly RectTransform _main;
        private readonly TextMeshProUGUI _mainText;
        private readonly RectTransform _other;
        private readonly TextMeshProUGUI _otherText;
        private readonly StringBuilder _sb = new StringBuilder();
        /// <summary>LocalRect 的暫存（只在主執行緒、呼叫內使用，不保留狀態）。</summary>
        private static readonly Vector3[] Corners = new Vector3[4];

        public LootTooltip(RectTransform root, TMP_FontAsset font, UiSkin skin)
        {
            _root = root;
            _main = Box("Tooltip", font, skin, out _mainText);
            _other = Box("Compare", font, skin, out _otherText);
            Hide();
        }

        private RectTransform Box(string name, TMP_FontAsset font, UiSkin skin, out TextMeshProUGUI text)
        {
            // 面板外框縮小當說明框底（不透明，疊在格子上也讀得清楚）
            var rt = SsUiKit.Rect(name, _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f));
            skin.Add(rt, UiSkin.Panel, 0.3f);
            var textRt = SsUiKit.Fill("Text", rt);
            textRt.offsetMin = new Vector2(Margin + 6f, Margin);
            textRt.offsetMax = new Vector2(-Margin, -Margin);
            text = SsUiKit.AddText(textRt, font, 30f, SsUiKit.Paper, TextAlignmentOptions.TopLeft);
            text.richText = true;
            text.lineSpacing = 6f;
            return rt;
        }

        public void Hide()
        {
            SetActive(_main, false);
            SetActive(_other, false);
        }

        /// <summary>
        /// 顯示 item 的說明（header 不是 null 時在名稱上方加一行小字），compare 不是 null 時並列（標「身上裝備」）。
        /// anchor：要貼著的矩形（root 的本地座標，xMin/yMin 是左下）。
        /// </summary>
        public void Show(ItemData item, string header, ItemData compare, Rect anchor)
        {
            if (item == null)
            {
                Hide();
                return;
            }
            var h1 = Fill(_mainText, item, header);
            var h2 = compare != null ? Fill(_otherText, compare, StoryTexts.TooltipEquipped) : 0f;
            SetActive(_main, true);
            SetActive(_other, compare != null);
            _main.sizeDelta = new Vector2(Width, h1);
            _other.sizeDelta = new Vector2(Width, h2);

            var area = _root.rect;
            var total = compare != null ? Width * 2f + Gap : Width;
            // 右邊放得下就放右邊，否則左邊；兩邊都放不下就靠右緣
            var x = anchor.xMax + Gap;
            if (x + total > area.xMax)
                x = anchor.xMin - Gap - total;
            if (x < area.xMin)
                x = Mathf.Max(area.xMin, area.xMax - total);
            var top = anchor.yMax;
            var tallest = Mathf.Max(h1, h2);
            if (top - tallest < area.yMin)
                top = area.yMin + tallest;
            if (top > area.yMax)
                top = area.yMax;
            // 比較框放在靠 anchor 遠的那一側，主框貼著道具
            var leftSide = x + total <= anchor.xMin;
            _main.anchoredPosition = new Vector2(leftSide && compare != null ? x + Width + Gap : x, top);
            _other.anchoredPosition = new Vector2(leftSide ? x : x + Width + Gap, top);
        }

        /// <summary>
        /// 把顏色寫成 RRGGBB。不用 ColorUtility.ToHtmlStringRGB：已發佈的主體（AOT 端）沒有引用它而被裁掉，
        /// 只更新代碼包時呼叫會找不到方法。
        /// </summary>
        private void AppendHexRgb(Color color)
        {
            Color32 c = color;
            AppendHexByte(c.r);
            AppendHexByte(c.g);
            AppendHexByte(c.b);
        }

        private void AppendHexByte(byte value)
        {
            const string digits = "0123456789ABCDEF";
            _sb.Append(digits[value >> 4]).Append(digits[value & 0xF]);
        }

        private float Fill(TextMeshProUGUI text, ItemData item, string header)
        {
            _sb.Length = 0;
            if (header != null)
                _sb.Append("<size=24><color=#B8B0C8>").Append(header).Append("</color></size>\n");
            _sb.Append("<size=38><color=#");
            AppendHexRgb(ItemData.RarityColor(item.Rarity));
            _sb.Append('>').Append(item.DisplayName).Append("</color></size>\n");
            _sb.Append("<color=#A9A3B8>").Append(item.KindName).Append("</color>");
            foreach (var line in item.DescribeBase())
                _sb.Append('\n').Append(line);
            foreach (var line in item.DescribeAffixes())
                _sb.Append("\n<color=#7FC8FF>").Append(line).Append("</color>");
            if (item.IsEquipment)
                _sb.Append("\n<color=#FFC83A>").Append(StoryTexts.TooltipLevel).Append(item.Level).Append("</color>");
            text.text = _sb.ToString();
            var inner = Width - Margin * 2f - 6f;
            return text.GetPreferredValues(text.text, inner, 0f).y + Margin * 2f + 4f;
        }

        /// <summary>rt 在 root 本地座標的矩形。</summary>
        public static Rect LocalRect(RectTransform root, RectTransform rt)
        {
            var corners = Corners;
            rt.GetWorldCorners(corners);
            var a = (Vector2)root.InverseTransformPoint(corners[0]);
            var b = (Vector2)root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static void SetActive(Component c, bool active)
        {
            if (c.gameObject.activeSelf != active)
                c.gameObject.SetActive(active);
        }
    }
    #endregion
}
