using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nerv.Client.UI
{
    /// <summary>
    /// Набор для панелей интерфейса по концепту («стекло + неон»): холст, шрифты, скруглённые подложки,
    /// подписи и кнопки. Панели строятся из кода и не зависят от режима холста: на ПК холст экранный
    /// (поверх постобработки — цвета без тонмаппинга), для VR/AR тот же код строит панели в мире.
    /// </summary>
    public static class NervUI
    {
        // ---- Палитра концепта (раздел 7.5) ----
        // Подложка непрозрачная: на полупрозрачной сквозь текст просвечивали яркие атомы и ореолы.
        public static readonly Color Glass = new Color32(14, 20, 34, 255);
        public static readonly Color GlassLight = new Color32(255, 255, 255, 10);
        public static readonly Color Line = new Color32(255, 255, 255, 26);
        public static readonly Color Text = new Color32(0xE8, 0xEC, 0xF4, 0xFF);
        public static readonly Color TextSoft = new Color32(0xC9, 0xD1, 0xDE, 0xFF);
        public static readonly Color Muted = new Color32(0x9A, 0xA4, 0xB8, 0xFF);
        public static readonly Color Dim = new Color32(0x7D, 0x87, 0x9B, 0xFF);
        public static readonly Color Energy = new Color32(0x5C, 0xE1, 0xE6, 0xFF);
        public static readonly Color Heat = new Color32(0xFF, 0xB5, 0x47, 0xFF);
        public static readonly Color Ink = new Color32(0x0B, 0x0F, 0x1A, 0xFF);

        public static Font Regular { get; private set; }
        public static Font SemiBold { get; private set; }
        public static Font Display { get; private set; }
        public static Font DisplayMedium { get; private set; }
        public static Font Mono { get; private set; }

        public static Canvas Canvas { get; private set; }

        /// <summary>Открытые панели: старый IMGUI-HUD не рисует подписи поверх них.</summary>
        private static readonly System.Collections.Generic.List<RectTransform> Occluders = new System.Collections.Generic.List<RectTransform>();
        private static readonly Vector3[] Corners = new Vector3[4];

        public static void RegisterOccluder(RectTransform r)
        {
            if (!Occluders.Contains(r)) Occluders.Add(r);
        }

        /// <summary>
        /// Прямоугольник в пикселях экрана (начало — левый верхний угол, как у IMGUI)
        /// пересекается с открытой панелью.
        /// </summary>
        public static bool Covers(UnityEngine.Rect screenTopLeft)
        {
            for (int i = Occluders.Count - 1; i >= 0; i--)
            {
                RectTransform r = Occluders[i];
                if (r == null)
                {
                    Occluders.RemoveAt(i);
                    continue;
                }
                if (!r.gameObject.activeInHierarchy) continue;
                if (ScreenRect(r).Overlaps(screenTopLeft)) return true;
            }
            return false;
        }

        /// <summary>Экранный прямоугольник панели в координатах IMGUI (y вниз).</summary>
        public static UnityEngine.Rect ScreenRect(RectTransform r)
        {
            r.GetWorldCorners(Corners); // для экранного холста мировые углы = пиксели, y вверх
            float xMin = Corners[0].x, xMax = Corners[2].x;
            float yMin = Screen.height - Corners[2].y, yMax = Screen.height - Corners[0].y;
            return new UnityEngine.Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }
        public static RectTransform Root { get; private set; }

        private static Sprite _rounded, _ring, _soft;
        private const int SpriteRadius = 32;

        /// <summary>Курсор над панелью нового интерфейса (клики в сцену в этот момент не идут).</summary>
        public static bool PointerOverUi =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        public static void Init()
        {
            if (Canvas != null) return;
            Font fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Regular = Resources.Load<Font>("Fonts/Inter-Regular") ?? fallback;
            SemiBold = Resources.Load<Font>("Fonts/Inter-SemiBold") ?? Regular;
            Display = Resources.Load<Font>("Fonts/Unbounded-Bold") ?? SemiBold;
            DisplayMedium = Resources.Load<Font>("Fonts/Unbounded-Medium") ?? Display;
            Mono = Resources.Load<Font>("Fonts/JetBrainsMono-SemiBold") ?? SemiBold;

            _rounded = MakeRounded(false);
            _ring = MakeRounded(true);
            _soft = MakeSoftCircle();

            var go = new GameObject("NERV UI", typeof(RectTransform));
            Object.DontDestroyOnLoad(go);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            // По высоте: при любом соотношении сторон по вертикали помещаются 900 единиц —
            // правая колонка (среда + журнал) и левая (сцена + мини-таблица) не наезжают друг на друга.
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)go.transform;

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                Object.DontDestroyOnLoad(es);
#if ENABLE_INPUT_SYSTEM
                var module = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                module.AssignDefaultActions();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        // ================= Построение =================

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Привязка к верхнему левому углу родителя: позиция и размер в единицах холста.</summary>
        public static RectTransform Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static RectTransform Stretch(RectTransform r, float inset = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        /// <summary>Скруглённая подложка (радиус в единицах холста).</summary>
        public static Image Box(Transform parent, string name, Color color, float radius = 16, bool raycast = true)
        {
            RectTransform r = Rect(parent, name);
            Image img = r.gameObject.AddComponent<Image>();
            img.sprite = _rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(1f, radius);
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Тонкая рамка поверх подложки (растягивается на всего родителя).</summary>
        public static Image Border(Transform parent, Color color, float radius = 16)
        {
            RectTransform r = Stretch(Rect(parent, "Border"));
            Image img = r.gameObject.AddComponent<Image>();
            img.sprite = _ring;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(1f, radius);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Стеклянная панель концепта: тёмное стекло + тонкая светлая рамка.</summary>
        public static Image Panel(Transform parent, string name, float radius = 20, Color? border = null)
        {
            Image img = Box(parent, name, Glass, radius);
            Border(img.transform, border ?? Line, radius);
            return img;
        }

        /// <summary>Мягкий светящийся круг (для «атома» в превью и точек электронов).</summary>
        public static Image Soft(Transform parent, string name, Color color)
        {
            RectTransform r = Rect(parent, name);
            Image img = r.gameObject.AddComponent<Image>();
            img.sprite = _soft;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, Font font, int size, Color color,
            TextAnchor align = TextAnchor.UpperLeft, string name = "Text")
        {
            RectTransform r = Rect(parent, name);
            Text t = r.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.lineSpacing = 1.1f;
            return t;
        }

        public enum ButtonStyle { Primary, Ghost, Chip, Plain }

        public static Button Button(Transform parent, string text, ButtonStyle style, System.Action onClick,
            int fontSize = 14, float radius = 10, string name = "Button")
        {
            Color bg, fg, border;
            switch (style)
            {
                case ButtonStyle.Primary: bg = Energy; fg = Ink; border = Color.clear; break;
                case ButtonStyle.Chip: bg = new Color(Energy.r, Energy.g, Energy.b, 0.14f); fg = new Color32(0xE8, 0xFB, 0xFC, 0xFF); border = new Color(Energy.r, Energy.g, Energy.b, 0.4f); break;
                case ButtonStyle.Ghost: bg = new Color(1, 1, 1, 0.04f); fg = TextSoft; border = new Color(1, 1, 1, 0.12f); break;
                default: bg = Color.clear; fg = Muted; border = Color.clear; break;
            }
            Image img = Box(parent, name, bg, radius);
            if (border.a > 0) Border(img.transform, border, radius);
            Button b = img.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            cb.colorMultiplier = 1.4f;
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            // Подпись создаётся всегда (даже пустая): текст кнопки потом можно менять.
            Text t = Label(img.transform, text ?? "", SemiBold, fontSize, fg, TextAnchor.MiddleCenter, "Label");
            Stretch(t.rectTransform, 4);
            return b;
        }

        /// <summary>Вертикальная колонка с автоматической высотой.</summary>
        public static VerticalLayoutGroup Column(GameObject go, int padding, float spacing)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.spacing = spacing;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup Row(Transform parent, float spacing, string name = "Row")
        {
            RectTransform r = Rect(parent, name);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexibleWidth = -1)
        {
            LayoutElement le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) le.preferredWidth = width;
            if (height >= 0) le.preferredHeight = height;
            if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
            return le;
        }

        /// <summary>Ползунок концепта: тонкая дорожка, заливка цвета энергии, белая ручка.</summary>
        public static Slider Slider(Transform parent, float min, float max, System.Action<float> onChange, string name = "Slider")
        {
            RectTransform root = Rect(parent, name);
            Size(root, -1, 22);

            Image track = Box(root, "Track", new Color(1, 1, 1, 0.10f), 3, false);
            RectTransform tr = track.rectTransform;
            tr.anchorMin = new Vector2(0, 0.5f);
            tr.anchorMax = new Vector2(1, 0.5f);
            tr.sizeDelta = new Vector2(0, 6);

            RectTransform fillArea = Rect(root, "Fill Area");
            fillArea.anchorMin = new Vector2(0, 0.5f);
            fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(-16, 6);
            Image fill = Box(fillArea, "Fill", WithAlpha(Energy, 0.75f), 3, false);
            fill.rectTransform.sizeDelta = Vector2.zero;

            RectTransform handleArea = Stretch(Rect(root, "Handle Area"));
            handleArea.offsetMin = new Vector2(8, 0);
            handleArea.offsetMax = new Vector2(-8, 0);
            Image handle = Box(handleArea, "Handle", Color.white, 8);
            handle.rectTransform.sizeDelta = new Vector2(16, 0);

            Slider s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        /// <summary>Горизонтальная полоса-градиент (шкала температуры: холод → тепло).</summary>
        public static RawImage Gradient(Transform parent, params Color[] stops)
        {
            const int w = 256;
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < w; x++)
            {
                float t = x / (w - 1f) * (stops.Length - 1);
                int i = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(t));
                tex.SetPixel(x, 0, Color.Lerp(stops[i], stops[i + 1], t - i));
            }
            tex.Apply();
            RectTransform r = Rect(parent, "Gradient");
            RawImage img = r.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Строка «название — значение» (значение справа).</summary>
        public static Text Property(Transform parent, string name, out Text value, int size = 13)
        {
            RectTransform row = Rect(parent, name);
            Size(row, -1, size + 9);
            Text n = Label(row, name, Regular, size, Muted, TextAnchor.MiddleLeft);
            Stretch(n.rectTransform);
            value = Label(row, "", Mono, size, Text, TextAnchor.MiddleRight);
            Stretch(value.rectTransform);
            return n;
        }

        public static Image Divider(Transform parent)
        {
            Image d = Box(parent, "Divider", new Color(1, 1, 1, 0.08f), 1, false);
            Size(d, -1, 1);
            return d;
        }

        /// <summary>Подпись раздела: капителью, шрифт Unbounded, приглушённая.</summary>
        public static Text Section(Transform parent, string title)
        {
            Text t = Label(parent, title.ToUpperInvariant(), DisplayMedium, 11, Muted);
            Size(t, -1, 16);
            return t;
        }

        /// <summary>Переключатель-«пилюля»: заливка цвета энергии, когда включён.</summary>
        public static void SetActive(Button b, bool active)
        {
            if (b == null) return;
            Image img = b.GetComponent<Image>();
            // Активная кнопка: сплошная заливка «энергией», яркая рамка и жирная подпись —
            // чтобы включённый режим читался с одного взгляда.
            img.color = active ? Energy : new Color(1, 1, 1, 0.04f);
            Transform border = b.transform.Find("Border");
            if (border != null)
            {
                Image bi = border.GetComponent<Image>();
                if (bi != null) bi.color = active ? new Color(0.85f, 1f, 1f, 0.9f) : new Color(1, 1, 1, 0.12f);
            }
            Text t = b.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = active ? Ink : TextSoft;
                t.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Число с запятой, как принято в русском тексте: 15,999.</summary>
        public static string Num(double v, string format) =>
            v.ToString(format, System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

        // ================= Спрайты =================

        private static Sprite MakeRounded(bool ring)
        {
            int size = SpriteRadius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Расстояние до скруглённого квадрата во всю текстуру (углы радиусом SpriteRadius).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - SpriteRadius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - SpriteRadius);
                    float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0);
                    float d = outside - SpriteRadius; // < 0 внутри
                    float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d + 1.2f) + 0.2f) : Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float b = SpriteRadius + 1;
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static Sprite MakeSoftCircle()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Яркое ядро, полупрозрачное «стекло», мягкий край — как атом в концепте.
                    float a = r > 1f ? 0f : Mathf.Lerp(1f, 0.18f, Mathf.SmoothStep(0f, 1f, r / 0.75f)) * Mathf.Clamp01((1f - r) * 12f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
