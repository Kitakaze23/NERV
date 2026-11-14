using System.Collections.Generic;
using System.Linq;
using Nerv.Client.UI;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Naming;
using Nerv.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Nerv.Client
{
    /// <summary>
    /// Интерфейс песочницы по концепту (экран «Главный»), на наборе NervUI:
    /// верхняя панель (режимы, сцена, замедление, качество, пауза), слева — сцена и вещества,
    /// справа — среда, инструменты и счётчики, внизу справа — журнал, внизу слева — мини-таблица
    /// Менделеева, сверху по центру — карточка шага реакции, над молекулами — подписи.
    /// </summary>
    public sealed class NervHud : MonoBehaviour
    {
        public const float Margin = 24f;
        private const float RightWidth = 240f, LeftWidth = 260f;
        /// <summary>Отступ справа для карточки вещества: правая колонка + зазор.</summary>
        public const float RightColumn = Margin + RightWidth + 16f;
        public const float TopBarBottom = 96f;

        private NervInteraction _interaction;
        private bool _built;

        // Верхняя панель.
        private Text _sceneButtonText;
        private Button _slowButton, _qualityButton, _pauseButton;
        private RectTransform _sceneMenu;

        // Левая панель.
        private Text _sceneTitle, _sceneHint, _flame;
        private readonly List<Text> _substanceLines = new List<Text>();

        // Правая панель.
        private Slider _temperature, _speed;
        private Text _temperatureValue, _temperatureNow, _speedValue, _atoms, _particles, _time;
        private Button _vacuum, _water;
        private readonly Dictionary<NervView.Mode, Button> _views = new Dictionary<NervView.Mode, Button>();
        private readonly Dictionary<NervTool, Button> _tools = new Dictionary<NervTool, Button>();
        private Button _tableTool, _measureTool, _sandboxButton, _labButton;
        private GameObject _miniTable;

        // Журнал.
        private sealed class JournalEntry
        {
            public string Main, Sub;
            public Color SubColor;
            public int Count = 1;
        }

        private const int JournalSize = 4;
        private readonly List<JournalEntry> _journal = new List<JournalEntry>();
        private readonly List<(Text main, Text sub)> _journalRows = new List<(Text, Text)>();
        private bool _journalDirty;

        // Мини-таблица.
        private readonly Dictionary<string, Image> _miniBorders = new Dictionary<string, Image>();
        private string _miniSelected;

        // Карточка шага.
        private CanvasGroup _stepGroup;
        private RectTransform _stepRect;
        private Text _stepKicker, _stepEquation, _stepEnergy, _stepText, _stepSummary;
        private NervDirector.Card _stepShown;

        // Подписи над молекулами.
        private sealed class Tag
        {
            public RectTransform Rect;
            public Image Box, Border;
            public Text Text;
            public CanvasGroup Group;
        }

        private RectTransform _labels;
        private readonly List<Tag> _tags = new List<Tag>();
        private readonly List<UnityEngine.Rect> _placed = new List<UnityEngine.Rect>();
        private readonly Dictionary<AtomId, int> _index = new Dictionary<AtomId, int>();
        private readonly HashSet<AtomId> _grouped = new HashSet<AtomId>();

        private Text _error;

        private void Start() => _interaction = GetComponent<NervInteraction>();

        private void LateUpdate()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;
            if (!_built) Build(app);

            WorldSnapshot snap = app.Snapshot;
            _index.Clear();
            for (int i = 0; i < snap.AtomCount; i++) _index[snap.Ids[i]] = i;
            _grouped.Clear();
            foreach (IonicGroup g in snap.IonicGroups)
                foreach (Molecule m in g.Members)
                    foreach (AtomId id in m.Atoms) _grouped.Add(id);

            foreach (SimEvent e in app.Events)
                if (e.Type == SimEventType.MoleculeIdentified && e.Text != null && !e.Text.StartsWith("Атомарный"))
                    AddJournal(e.Text, "опознано", NervUI.Energy);
                else if (e.Type == SimEventType.MoleculeDecayed && e.Text != null)
                    AddJournal(NervDirector.Pretty(e.Text, 0) + " распалось", "распад · слабая связь", NervUI.Heat);
            foreach (ReactionRecord r in app.Reactions)
            {
                string kind = StepTitle(r.Reaction.Name).ToLowerInvariant() + (r.Reaction.DeltaHKj < 0 ? " · экзо" : r.Reaction.DeltaHKj > 0 ? " · эндо" : "");
                AddJournal(NervDirector.Equation(r), kind, r.Reaction.DeltaHKj < 0 ? NervUI.Heat : NervEnvironment.Cold);
            }

            // Каждый блок обновляется отдельно: ошибка в одном не должна гасить весь HUD.
            Safe("TopBar", () => RefreshTopBar(app));
            Safe("Left", () => RefreshLeft(app, snap));
            Safe("Right", () => RefreshRight(app, snap));
            if (_journalDirty) Safe("Journal", () => RefreshJournal(snap));
            Safe("MiniTable", RefreshMiniTable);
            Safe("StepCard", RefreshStepCard);
            Safe("Labels", () => RefreshLabels(app, snap));
            Safe("GrabHint", () => RefreshGrabHint(snap));
            Safe("History", RefreshHistory);
            _error.text = app.DataError ?? "";
        }

        private readonly HashSet<string> _reported = new HashSet<string>();

        private void Safe(string block, System.Action a)
        {
            try { a(); }
            catch (System.Exception ex)
            {
                // В лог — один раз на блок, иначе консоль забивается каждым кадром.
                if (_reported.Add(block)) Debug.LogException(ex);
            }
        }

        /// <summary>Подпись кнопки; если её нет (старая сборка UI) — создаём.</summary>
        private static Text LabelOf(Button b)
        {
            Text t = b.GetComponentInChildren<Text>(true);
            if (t != null) return t;
            t = NervUI.Label(b.transform, "", NervUI.SemiBold, 13, NervUI.TextSoft, TextAnchor.MiddleCenter, "Label");
            NervUI.Stretch(t.rectTransform, 4);
            return t;
        }

        // ================= Построение =================

        private void Build(NervApp app)
        {
            _built = true;
            NervUI.Init();
            Transform root = NervUI.Root;

            // Подписи — самый нижний слой: непрозрачные панели закрывают их сами.
            _labels = NervUI.Stretch(NervUI.Rect(root, "Labels"));

            BuildTopBar(root, app);
            BuildLeft(root);
            BuildRight(root, app);
            BuildJournal(root);
            BuildHistory(root);
            BuildMiniTable(root);
            BuildStepCard(root);
            BuildDragAndHints(root);

            _error = NervUI.Label(root, "", NervUI.SemiBold, 14, new Color32(0xFF, 0x8A, 0x8A, 0xFF), TextAnchor.UpperCenter, "Error");
            RectTransform er = _error.rectTransform;
            er.anchorMin = new Vector2(0.5f, 1);
            er.anchorMax = new Vector2(0.5f, 1);
            er.pivot = new Vector2(0.5f, 1);
            er.sizeDelta = new Vector2(700, 40);
            er.anchoredPosition = new Vector2(0, -TopBarBottom);

            // Выпадающий список сцен — поверх остальных панелей.
            _sceneMenu.SetAsLastSibling();
        }

        private void BuildTopBar(Transform root, NervApp app)
        {
            Image bar = NervUI.Panel(root, "Top Bar", 16);
            RectTransform br = bar.rectTransform;
            br.anchorMin = new Vector2(0, 1);
            br.anchorMax = new Vector2(1, 1);
            br.pivot = new Vector2(0.5f, 1);
            br.offsetMin = new Vector2(Margin, -16 - 64);
            br.offsetMax = new Vector2(-Margin, -16);

            HorizontalLayoutGroup row = NervUI.Row(bar.transform, 6, "Content");
            NervUI.Stretch((RectTransform)row.transform);
            row.padding = new RectOffset(20, 10, 10, 10);
            row.childAlignment = TextAnchor.MiddleLeft;

            // Знак Mendal: буква M из атомов и связей (Resources/Textures/MendalMark.png).
            Image logo = NervUI.Rect(row.transform, "Logo").gameObject.AddComponent<Image>();
            Texture2D mark = Resources.Load<Texture2D>("Textures/MendalMark");
            if (mark != null)
            {
                mark.wrapMode = TextureWrapMode.Clamp;
                logo.sprite = Sprite.Create(mark, new UnityEngine.Rect(0, 0, mark.width, mark.height), new Vector2(0.5f, 0.5f));
                logo.preserveAspect = true;
            }
            else logo.color = NervUI.Energy;
            logo.raycastTarget = false;
            NervUI.Size(logo, 34, 34);
            Text name = NervUI.Label(row.transform, "Mendal", NervUI.Display, 18, Color.white, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            NervUI.Size(name, 96, 44);

            _sandboxButton = NervUI.Button(row.transform, "Песочница", NervUI.ButtonStyle.Ghost, () => SetLab(app, false), 14);
            NervUI.Size(_sandboxButton, 104, 44);
            // Уроков ещё нет: приглушены, «скоро» — мелкой второй строкой.
            Button lessons = NervUI.Button(row.transform, "Уроки\n<size=10>скоро</size>", NervUI.ButtonStyle.Plain, null, 13);
            lessons.interactable = false;
            LabelOf(lessons).lineSpacing = 0.85f;
            NervUI.Size(lessons, 76, 44);
            _labButton = NervUI.Button(row.transform, "Лаборатория", NervUI.ButtonStyle.Ghost, () => SetLab(app, true), 14);
            NervUI.Size(_labButton, 120, 44);

            RectTransform spacer = NervUI.Rect(row.transform, "Spacer");
            NervUI.Size(spacer, 10, 44, 1);

            // Вид молекул: сегментированный переключатель (V — следующий).
            Image seg = NervUI.Box(row.transform, "View", new Color(1, 1, 1, 0.04f), 12);
            NervUI.Border(seg.transform, new Color(1, 1, 1, 0.08f), 12);
            NervUI.Size(seg, 214, 44);
            HorizontalLayoutGroup segRow = NervUI.Row(seg.transform, 3, "Buttons");
            NervUI.Stretch((RectTransform)segRow.transform, 3);
            segRow.childForceExpandWidth = false;
            _views.Clear();
            foreach (NervView.Mode mode in new[] { NervView.Mode.Balls, NervView.Mode.Cpk, NervView.Mode.Effect })
            {
                NervView.Mode m = mode;
                Button b = NervUI.Button(segRow.transform, NervView.Name(m), NervUI.ButtonStyle.Plain, () => NervView.Current = m, 13, 9);
                NervUI.Size(b, m == NervView.Mode.Cpk ? 52 : 76, 38);
                _views[m] = b;
            }

            Button scene = NervUI.Button(row.transform, "Сцена", NervUI.ButtonStyle.Ghost, ToggleSceneMenu, 13);
            NervUI.Size(scene, 180, 44);
            _sceneButtonText = LabelOf(scene);
            _sceneButtonText.horizontalOverflow = HorizontalWrapMode.Wrap;

            _slowButton = NervUI.Button(row.transform, "Замедление", NervUI.ButtonStyle.Ghost, () =>
            {
                if (NervDirector.Instance != null) NervDirector.Instance.SlowMotion = !NervDirector.Instance.SlowMotion;
            }, 13);
            NervUI.Size(_slowButton, 112, 44);
            // Экономный режим графики — переключатель: подсвечен, когда включён.
            _qualityButton = NervUI.Button(row.transform, "Эко-режим", NervUI.ButtonStyle.Ghost, () =>
                NervQuality.Current = NervQuality.High ? NervQuality.Level.Low : NervQuality.Level.High, 13);
            NervUI.Size(_qualityButton, 104, 44);
            _pauseButton = NervUI.Button(row.transform, "Пауза", NervUI.ButtonStyle.Ghost, () => app.Paused = !app.Paused, 13);
            NervUI.Size(_pauseButton, 104, 44);
            Button clear = NervUI.Button(row.transform, "Очистить", NervUI.ButtonStyle.Ghost, app.ClearAtoms, 13);
            NervUI.Size(clear, 96, 44);

            // Выпадающий список сцен под кнопкой сцены (позиция уточняется при открытии).
            Image menu = NervUI.Panel(root, "Scene Menu", 14, NervUI.WithAlpha(NervUI.Energy, 0.3f));
            _sceneMenu = menu.rectTransform;
            _sceneMenu.anchorMin = _sceneMenu.anchorMax = new Vector2(1, 1);
            _sceneMenu.pivot = new Vector2(1, 1);
            NervUI.Column(menu.gameObject, 10, 4);
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _sceneMenu.sizeDelta = new Vector2(280, 0);
            menu.transform.Find("Border").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            foreach (NervScenes.Preset preset in NervScenes.All)
            {
                NervScenes.Preset p = preset;
                Button b = NervUI.Button(menu.transform, p.Name, NervUI.ButtonStyle.Plain, () =>
                {
                    app.LoadScene(p);
                    _sceneMenu.gameObject.SetActive(false);
                }, 14, 8, p.Name);
                NervUI.Size(b, -1, 38);
                Text t = b.GetComponentInChildren<Text>();
                t.alignment = TextAnchor.MiddleLeft;
                t.rectTransform.offsetMin = new Vector2(12, 0);
            }
            _sceneMenu.gameObject.SetActive(false);
            _sceneMenuAnchor = (RectTransform)scene.transform;
        }

        private RectTransform _sceneMenuAnchor;

        private void ToggleSceneMenu()
        {
            bool open = !_sceneMenu.gameObject.activeSelf;
            _sceneMenu.gameObject.SetActive(open);
            if (!open) return;
            // Под кнопкой сцены, правым краем к её правому краю.
            UnityEngine.Rect r = NervUI.ScreenRect(_sceneMenuAnchor);
            float scale = NervUI.Canvas.scaleFactor;
            float rightFromEdge = (Screen.width - r.xMax) / scale;
            _sceneMenu.anchoredPosition = new Vector2(-rightFromEdge, -(r.yMax / scale + 6f));
        }

        private void BuildLeft(Transform root)
        {
            Image panel = NervUI.Panel(root, "Scene Panel", 16);
            RectTransform pr = panel.rectTransform;
            NervUI.Place(pr, Margin, TopBarBottom + 4, LeftWidth, 100);
            NervUI.Column(panel.gameObject, 18, 8);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.transform.Find("Border").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            NervUI.Section(panel.transform, "Сцена");
            _sceneTitle = NervUI.Label(panel.transform, "", NervUI.DisplayMedium, 16, Color.white);
            _sceneHint = NervUI.Label(panel.transform, "", NervUI.Regular, 12, NervUI.TextSoft);
            _flame = NervUI.Label(panel.transform, "", NervUI.SemiBold, 13, NervUI.Heat);
            NervUI.Divider(panel.transform);
            NervUI.Section(panel.transform, "Вещества в сцене");
            for (int i = 0; i < 10; i++)
            {
                Text t = NervUI.Label(panel.transform, "", NervUI.Regular, 13, NervUI.Text);
                _substanceLines.Add(t);
            }
        }

        private void BuildRight(Transform root, NervApp app)
        {
            Image panel = NervUI.Panel(root, "Environment Panel", 16);
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(1, 1);
            pr.pivot = new Vector2(1, 1);
            pr.anchoredPosition = new Vector2(-Margin, -(TopBarBottom + 4));
            pr.sizeDelta = new Vector2(RightWidth, 100);
            NervUI.Column(panel.gameObject, 18, 10);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.transform.Find("Border").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            NervUI.Section(panel.transform, "Среда");
            NervUI.Property(panel.transform, "Температура", out _temperatureValue);
            _temperature = NervUI.Slider(panel.transform, 0f, 3000f, v =>
            {
                if (Mathf.Abs(v - (float)app.Temperature) > 0.5f) app.SetTemperature(Mathf.Round(v));
            });
            RawImage grad = NervUI.Gradient(panel.transform,
                new Color32(0x7F, 0xB8, 0xFF, 0xFF), new Color32(0xE8, 0xEC, 0xF4, 0xFF),
                new Color32(0xFF, 0xB5, 0x47, 0xFF), new Color32(0xFF, 0x6B, 0x3D, 0xFF));
            NervUI.Size(grad, -1, 4);
            _temperatureNow = NervUI.Label(panel.transform, "", NervUI.Regular, 12, NervUI.Muted);

            NervUI.Property(panel.transform, "Скорость времени", out _speedValue);
            // Логарифмическая шкала ×0,05 … ×4: значение ползунка — log₂ масштаба.
            _speed = NervUI.Slider(panel.transform, Mathf.Log(0.05f, 2f), 2f, v =>
            {
                float scale = Mathf.Pow(2f, v);
                if (Mathf.Abs(scale - 1f) < 0.06f) scale = 1f;
                if (Mathf.Abs(scale - (float)app.TimeScale) > 0.001f) app.SetTimeScale(scale);
            });

            HorizontalLayoutGroup medium = NervUI.Row(panel.transform, 4, "Medium");
            medium.childForceExpandWidth = true;
            _vacuum = NervUI.Button(medium.transform, "Вакуум", NervUI.ButtonStyle.Ghost, () => app.SetMedium(Medium.Vacuum), 13, 8);
            NervUI.Size(_vacuum, -1, 36, 1);
            _water = NervUI.Button(medium.transform, "Вода", NervUI.ButtonStyle.Ghost, () => app.SetMedium(Medium.Water), 13, 8);
            NervUI.Size(_water, -1, 36, 1);

            NervUI.Divider(panel.transform);
            NervUI.Section(panel.transform, "Инструменты");
            RectTransform grid = NervUI.Rect(panel.transform, "Tools");
            NervUI.Size(grid, -1, 44 * 3 + 16);
            GridLayoutGroup gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2((RightWidth - 36 - 8) / 2f, 44);
            gl.spacing = new Vector2(8, 8);
            AddTool(grid, "Рука", NervTool.Hand);
            AddTool(grid, "Искра", NervTool.Spark);
            AddTool(grid, "Нагрев", NervTool.Heat);
            AddTool(grid, "Холод", NervTool.Cold);
            AddTool(grid, "Удалить", NervTool.Erase);
            _tableTool = NervUI.Button(grid, "Таблица · T", NervUI.ButtonStyle.Ghost, () => PeriodicTablePanel.Instance?.Toggle(), 13);
            // В «Лаборатории» на месте таблицы — «Измерить» (таблица по-прежнему открывается клавишей T).
            AddTool(grid, "Измерить", NervTool.Measure);
            _measureTool = _tools[NervTool.Measure];
            _measureTool.gameObject.SetActive(false);

            NervUI.Divider(panel.transform);
            NervUI.Property(panel.transform, "Атомов", out _atoms);
            NervUI.Property(panel.transform, "Частиц", out _particles);
            NervUI.Property(panel.transform, "Время", out _time);
        }

        private void SetLab(NervApp app, bool lab)
        {
            app.LabMode = lab;
            if (lab) _interaction.Tool = NervTool.Measure;
            else if (_interaction.Tool == NervTool.Measure) _interaction.Tool = NervTool.Hand;
        }

        private void AddTool(Transform grid, string text, NervTool tool)
        {
            Button b = NervUI.Button(grid, text, NervUI.ButtonStyle.Ghost, () => _interaction.Tool = tool, 13);
            _tools[tool] = b;
        }

        private Button _undo, _redo;

        /// <summary>Отмена и повтор — маленькая панель внизу по центру (Cmd/Ctrl+Z, Cmd/Ctrl+Shift+Z).</summary>
        private void BuildHistory(Transform root)
        {
            Image panel = NervUI.Panel(root, "History", 14);
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0);
            pr.pivot = new Vector2(0.5f, 0);
            pr.anchoredPosition = new Vector2(0, Margin);
            pr.sizeDelta = new Vector2(268, 52);
            HorizontalLayoutGroup row = NervUI.Row(panel.transform, 6, "Buttons");
            NervUI.Stretch((RectTransform)row.transform, 6);
            row.childForceExpandWidth = true;
            _undo = NervUI.Button(row.transform, "↺  Отменить", NervUI.ButtonStyle.Ghost, () => NervHistory.Instance?.Undo(), 13);
            NervUI.Size(_undo, -1, 40, 1);
            _redo = NervUI.Button(row.transform, "Повторить  ↻", NervUI.ButtonStyle.Ghost, () => NervHistory.Instance?.Redo(), 13);
            NervUI.Size(_redo, -1, 40, 1);
        }

        private void RefreshHistory()
        {
            NervHistory h = NervHistory.Instance;
            bool u = h != null && h.CanUndo, r = h != null && h.CanRedo;
            if (_undo.interactable != u) { _undo.interactable = u; LabelOf(_undo).color = u ? NervUI.TextSoft : NervUI.Dim; }
            if (_redo.interactable != r) { _redo.interactable = r; LabelOf(_redo).color = r ? NervUI.TextSoft : NervUI.Dim; }
        }

        private void BuildJournal(Transform root)
        {
            Image panel = NervUI.Panel(root, "Journal", 16);
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(1, 0);
            pr.pivot = new Vector2(1, 0);
            pr.anchoredPosition = new Vector2(-Margin, Margin);
            pr.sizeDelta = new Vector2(RightWidth, 100);
            NervUI.Column(panel.gameObject, 16, 3);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.transform.Find("Border").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NervUI.Section(panel.transform, "Журнал");
            for (int i = 0; i < JournalSize; i++)
            {
                Text main = NervUI.Label(panel.transform, "", NervUI.SemiBold, 13, Color.white);
                Text sub = NervUI.Label(panel.transform, "", NervUI.Regular, 11, NervUI.Muted);
                _journalRows.Add((main, sub));
            }
            _journalDirty = true;
        }

        private void BuildMiniTable(Transform root)
        {
            const float cell = 25f, gap = 3f;
            float gridW = 18 * cell + 17 * gap, gridH = 7 * cell + 6 * gap;
            Image panel = NervUI.Panel(root, "Mini Table", 16);
            _miniTable = panel.gameObject;
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0, 0);
            pr.pivot = new Vector2(0, 0);
            pr.anchoredPosition = new Vector2(Margin, Margin);
            pr.sizeDelta = new Vector2(gridW + 32, gridH + 32 + 26);

            Text title = NervUI.Label(panel.transform, "ТАБЛИЦА МЕНДЕЛЕЕВА", NervUI.DisplayMedium, 11, NervUI.Muted, TextAnchor.MiddleLeft);
            NervUI.Place(title.rectTransform, 16, 12, 200, 18);
            Text hint = NervUI.Label(panel.transform, "перетащи элемент в сцену", NervUI.Regular, 11, NervUI.Dim, TextAnchor.MiddleLeft);
            NervUI.Place(hint.rectTransform, 214, 12, 170, 18);
            Button open = NervUI.Button(panel.transform, "вся таблица · T", NervUI.ButtonStyle.Plain, () => PeriodicTablePanel.Instance?.Toggle(), 12, 6);
            NervUI.Place((RectTransform)open.transform, gridW + 16 - 120, 8, 120, 24);
            open.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleRight;

            RectTransform grid = NervUI.Place(NervUI.Rect(panel.transform, "Grid"), 16, 42, gridW, gridH);
            foreach (Element e in ElementTable.All)
            {
                if (e.TableRow > 7) continue;
                Color cpk = MoleculeView.ElementColor(e);
                bool on = e.Availability == ElementAvailability.Enabled, opt = e.Availability == ElementAvailability.Optional;
                Color bg = on ? NervUI.WithAlpha(cpk, 0.2f) : opt ? new Color(1, 1, 1, 0.05f) : new Color(1, 1, 1, 0.025f);
                Image c = NervUI.Box(grid, e.Symbol, bg, 5);
                NervUI.Place(c.rectTransform, (e.TableColumn - 1) * (cell + gap), (e.TableRow - 1) * (cell + gap), cell, cell);
                Color border = on ? NervUI.WithAlpha(cpk, 0.8f) : opt ? NervUI.WithAlpha(cpk, 0.6f) : new Color(1, 1, 1, 0.05f);
                _miniBorders[e.Symbol] = NervUI.Border(c.transform, border, 5);
                Color fg = on ? Color.white : opt ? NervUI.TextSoft : new Color32(0x4C, 0x56, 0x6B, 0xFF);
                NervUI.Stretch(NervUI.Label(c.transform, e.Symbol, NervUI.SemiBold, 10, fg, TextAnchor.MiddleCenter).rectTransform);
                if (!e.IsAvailable) continue;
                string sym = e.Symbol;
                Button b = c.gameObject.AddComponent<Button>();
                b.onClick.AddListener(() =>
                {
                    _interaction.Element = sym;
                    _interaction.Tool = NervTool.Place;
                });
                AddDrag(c.gameObject, e);
            }
        }

        // ================= Перетаскивание элемента из таблицы =================

        private Image _ghost;
        private Text _ghostText;
        private RectTransform _grabHint;
        private Text _grabHintText;

        /// <summary>Клетку элемента можно перетащить в сцену — атом встанет под курсором.</summary>
        private void AddDrag(GameObject cell, Element e)
        {
            var trig = cell.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            void On(UnityEngine.EventSystems.EventTriggerType type, System.Action<UnityEngine.EventSystems.PointerEventData> a)
            {
                var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(d => a((UnityEngine.EventSystems.PointerEventData)d));
                trig.triggers.Add(entry);
            }
            On(UnityEngine.EventSystems.EventTriggerType.BeginDrag, d =>
            {
                _ghost.color = NervUI.WithAlpha(MoleculeView.ElementColor(e), 0.9f);
                _ghostText.text = e.Symbol;
                _ghost.gameObject.SetActive(true);
                MoveGhost(d.position);
            });
            On(UnityEngine.EventSystems.EventTriggerType.Drag, d => MoveGhost(d.position));
            On(UnityEngine.EventSystems.EventTriggerType.EndDrag, d =>
            {
                _ghost.gameObject.SetActive(false);
                // Отпустили над сценой (не над панелью) — ставим атом.
                bool overPanel = d.pointerCurrentRaycast.gameObject != null;
                if (!overPanel) _interaction.PlaceAtPointer(e.Symbol);
            });
        }

        private void MoveGhost(Vector2 screen)
        {
            float scale = NervUI.Canvas.scaleFactor;
            _ghost.rectTransform.anchoredPosition = screen / scale;
        }

        private void BuildDragAndHints(Transform root)
        {
            _ghost = NervUI.Soft(root, "Drag Ghost", Color.white);
            RectTransform gr = _ghost.rectTransform;
            gr.anchorMin = gr.anchorMax = Vector2.zero;
            gr.pivot = new Vector2(0.5f, 0.5f);
            gr.sizeDelta = new Vector2(56, 56);
            _ghostText = NervUI.Label(_ghost.transform, "", NervUI.SemiBold, 18, Color.white, TextAnchor.MiddleCenter);
            NervUI.Stretch(_ghostText.rectTransform);
            _ghost.gameObject.SetActive(false);

            Image hint = NervUI.Box(root, "Grab Hint", NervUI.WithAlpha(NervUI.Glass, 0.92f), 8, false);
            NervUI.Border(hint.transform, NervUI.WithAlpha(NervUI.Energy, 0.4f), 8);
            _grabHint = hint.rectTransform;
            _grabHint.anchorMin = _grabHint.anchorMax = Vector2.zero;
            _grabHint.pivot = new Vector2(0, 1);
            _grabHintText = NervUI.Label(hint.transform, "", NervUI.Regular, 13, new Color32(0x8E, 0xE9, 0xEC, 0xFF), TextAnchor.MiddleCenter);
            _grabHintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            NervUI.Stretch(_grabHintText.rectTransform);
            hint.gameObject.SetActive(false);
        }

        /// <summary>Подсказка у курсора при захвате: «Тяну углерод · 4 свободные связи».</summary>
        private void RefreshGrabHint(WorldSnapshot snap)
        {
            AtomId id = _interaction.Grabbed;
            bool show = !id.IsNone && _index.TryGetValue(id, out int i);
            _grabHint.gameObject.SetActive(show);
            if (!show) return;
            _index.TryGetValue(id, out int idx);
            Element e = ElementTable.Get(snap.AtomicNumbers[idx]);
            int free = snap.FreeValences[idx];
            string bonds = free <= 0 ? "все связи заняты"
                : free + " " + Plural(free, "свободная связь", "свободные связи", "свободных связей");
            string text = "Тяну " + Accusative(e.NameRu.ToLowerInvariant()) + " · " + bonds;
            if (_grabHintText.text != text) _grabHintText.text = text;
            _grabHint.sizeDelta = new Vector2(_grabHintText.preferredWidth + 24, 30);
            float scale = NervUI.Canvas.scaleFactor;
            _grabHint.anchoredPosition = NervInput.PointerPosition / scale + new Vector2(18, -18);
        }

        /// <summary>Винительный падеж названия элемента: «сера» → «серу», «углерод» → «углерод».</summary>
        private static string Accusative(string name) =>
            name.EndsWith("а") ? name.Substring(0, name.Length - 1) + "у" : name;

        private static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }

        private void BuildStepCard(Transform root)
        {
            Image card = NervUI.Panel(root, "Step Card", 18, NervUI.WithAlpha(NervUI.Heat, 0.35f));
            card.raycastTarget = false;
            _stepRect = card.rectTransform;
            _stepRect.anchorMin = _stepRect.anchorMax = new Vector2(0.5f, 1);
            _stepRect.pivot = new Vector2(0.5f, 1);
            _stepRect.sizeDelta = new Vector2(460, 100);
            _stepRect.anchoredPosition = new Vector2(0, -(TopBarBottom + 4));
            NervUI.Column(card.gameObject, 18, 6);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.transform.Find("Border").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _stepGroup = card.gameObject.AddComponent<CanvasGroup>();
            _stepGroup.blocksRaycasts = false;
            _stepKicker = NervUI.Label(card.transform, "", NervUI.DisplayMedium, 11, NervUI.Muted);
            _stepEquation = NervUI.Label(card.transform, "", NervUI.SemiBold, 22, Color.white);
            _stepEnergy = NervUI.Label(card.transform, "", NervUI.Regular, 13, NervUI.Heat);
            _stepText = NervUI.Label(card.transform, "", NervUI.Regular, 13, NervUI.TextSoft);
            _stepSummary = NervUI.Label(card.transform, "", NervUI.Regular, 12, NervUI.Muted);
            card.gameObject.SetActive(false);
        }

        // ================= Обновление =================

        private void RefreshTopBar(NervApp app)
        {
            _sceneButtonText.text = (string.IsNullOrEmpty(app.SceneName) ? "Сцена" : app.SceneName) + "  ⌄";
            NervUI.SetActive(_slowButton, NervDirector.Instance != null && NervDirector.Instance.SlowMotion);
            NervUI.SetActive(_qualityButton, !NervQuality.High);
            LabelOf(_pauseButton).text = app.Paused ? "Продолжить" : "Пауза";
            NervUI.SetActive(_pauseButton, app.Paused);
            NervUI.SetActive(_sandboxButton, !app.LabMode);
            NervUI.SetActive(_labButton, app.LabMode);
            foreach (KeyValuePair<NervView.Mode, Button> kv in _views) NervUI.SetActive(kv.Value, NervView.Current == kv.Key);
        }

        private void RefreshLeft(NervApp app, WorldSnapshot snap)
        {
            NervScenes.Preset current = NervScenes.All.FirstOrDefault(p => p.Name == app.SceneName);
            _sceneTitle.text = current != null ? current.Name : "Своя сцена";
            _sceneHint.text = current != null ? current.Hint : "";
            _flame.text = snap.FlameActive ? "● Идёт горение" : "";
            _flame.gameObject.SetActive(snap.FlameActive);

            var counts = new Dictionary<string, int>();
            foreach (Molecule m in snap.Molecules)
            {
                if (m.Charge != 0 && _grouped.Contains(m.Atoms[0])) continue; // ион учтён в составе соли
                string key = "<color=#FFFFFF>" + NervDirector.Pretty(m.Formula, m.Charge) + "</color>  " + (m.Name ?? "неизвестное соединение");
                if (m.IsRadical && m.Atoms.Count > 1) key = "<color=#FFB547>" + NervDirector.Pretty(m.Formula, m.Charge) + "</color>  " + (m.Name ?? "радикал");
                if (m.Unstable) key = "<color=#FFB547>" + NervDirector.Pretty(m.Formula, m.Charge) + "</color>  " + (m.Name ?? "неустойчивое соединение");
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
            foreach (IonicGroup g in snap.IonicGroups)
            {
                string key = "<color=#FFFFFF>" + FormulaText.Subscript(g.Formula) + "</color>  " + (g.Name ?? "ионное соединение") +
                             (g.Members.Count <= 2 ? " · ионная пара" : " · кристалл");
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
            List<KeyValuePair<string, int>> list = counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key).ToList();
            for (int i = 0; i < _substanceLines.Count; i++)
            {
                Text t = _substanceLines[i];
                bool show = i < list.Count;
                t.gameObject.SetActive(show || (i == 0));
                if (!show)
                {
                    t.text = i == 0 ? "<color=#7D879B>Пусто — добавь атомы из таблицы</color>" : "";
                    continue;
                }
                if (i == _substanceLines.Count - 1 && list.Count > _substanceLines.Count)
                {
                    t.text = "<color=#7D879B>… и ещё " + (list.Count - i) + "</color>";
                    continue;
                }
                t.text = (list[i].Value > 1 ? list[i].Value + " × " : "") + list[i].Key;
            }
        }

        private void RefreshRight(NervApp app, WorldSnapshot snap)
        {
            _temperatureValue.text = app.Temperature.ToString("0") + " K";
            _temperatureNow.text = "сейчас в сцене " + snap.Temperature.ToString("0") + " K";
            _temperature.SetValueWithoutNotify((float)app.Temperature);
            _speedValue.text = "×" + NervUI.Num(app.TimeScale, app.TimeScale < 1 ? "0.00" : "0.0");
            _speed.SetValueWithoutNotify(Mathf.Log((float)app.TimeScale, 2f));
            NervUI.SetActive(_vacuum, app.Medium == Medium.Vacuum);
            NervUI.SetActive(_water, app.Medium == Medium.Water);
            foreach (KeyValuePair<NervTool, Button> kv in _tools) NervUI.SetActive(kv.Value, _interaction.Tool == kv.Key);
            NervUI.SetActive(_tableTool, PeriodicTablePanel.Instance != null && PeriodicTablePanel.Instance.IsOpen);
            bool lab = app.LabMode;
            if (_tableTool.gameObject.activeSelf == lab) _tableTool.gameObject.SetActive(!lab);
            if (_measureTool.gameObject.activeSelf != lab) _measureTool.gameObject.SetActive(lab);
            if (_miniTable.activeSelf == lab) _miniTable.SetActive(!lab);
            _atoms.text = snap.AtomCount.ToString();
            _particles.text = (snap.Molecules.Count(m => !(m.Charge != 0 && _grouped.Contains(m.Atoms[0]))) + snap.IonicGroups.Count).ToString();
            _time.text = NervUI.Num(snap.Tick * 0.5 / 1000.0, "0.0") + " пс";
        }

        private void AddJournal(string main, string sub, Color color)
        {
            // Повтор того же — счётчик ×N у последней такой записи, а не новая строка.
            JournalEntry same = _journal.Find(j => j.Main == main);
            if (same != null)
            {
                same.Count++;
                _journal.Remove(same);
                _journal.Insert(0, same);
            }
            else
            {
                _journal.Insert(0, new JournalEntry { Main = main, Sub = sub, SubColor = color });
                if (_journal.Count > 20) _journal.RemoveAt(_journal.Count - 1);
            }
            _journalDirty = true;
        }

        private void RefreshJournal(WorldSnapshot snap)
        {
            _journalDirty = false;
            for (int i = 0; i < _journalRows.Count; i++)
            {
                (Text main, Text sub) = _journalRows[i];
                bool show = i < _journal.Count;
                main.gameObject.SetActive(show || i == 0);
                sub.gameObject.SetActive(show);
                if (!show)
                {
                    if (i == 0) main.text = "<color=#7D879B>Здесь появятся реакции и опознанные вещества</color>";
                    continue;
                }
                JournalEntry e = _journal[i];
                main.text = e.Main + (e.Count > 1 ? "  <color=#9AA4B8>×" + e.Count + "</color>" : "");
                sub.text = e.Sub;
                sub.color = Color.Lerp(e.SubColor, Color.white, 0.25f);
            }
        }

        private void RefreshMiniTable()
        {
            string sel = _interaction.Tool == NervTool.Place ? _interaction.Element : null;
            if (sel == _miniSelected) return;
            if (_miniSelected != null && _miniBorders.TryGetValue(_miniSelected, out Image old))
                old.color = NervUI.WithAlpha(MoleculeView.ElementColor(ElementTable.Get(_miniSelected)), 0.8f);
            _miniSelected = sel;
            if (sel != null && _miniBorders.TryGetValue(sel, out Image b)) b.color = NervUI.Energy;
        }

        private void RefreshStepCard()
        {
            NervDirector d = NervDirector.Instance;
            NervDirector.Card c = d != null ? d.Current : null;
            bool show = c != null && d.CardAlpha > 0.01f && (PeriodicTablePanel.Instance == null || !PeriodicTablePanel.Instance.IsOpen);
            _stepRect.gameObject.SetActive(show);
            if (!show) return;
            _stepGroup.alpha = d.CardAlpha;
            if (c != _stepShown)
            {
                _stepShown = c;
                _stepEquation.text = c.Equation;
                _stepEnergy.text = c.Energy;
                _stepEnergy.color = c.Accent;
                _stepText.text = c.Text ?? "";
                _stepText.gameObject.SetActive(!string.IsNullOrEmpty(c.Text));
                _stepSummary.text = string.IsNullOrEmpty(c.Summary) ? "" : "Итог процесса: " + c.Summary + (c.Simplified ? "  ·  упрощённый механизм" : "");
                _stepSummary.gameObject.SetActive(!string.IsNullOrEmpty(c.Summary));
            }
            _stepKicker.text = (d.IsSlowed ? "▶ ЗАМЕДЛЕНИЕ · " : "") + "НОВЫЙ ШАГ · " + c.Title.ToUpperInvariant();

            // Между левой колонкой и правой (или открытой карточкой вещества).
            float canvasW = NervUI.Root.rect.width;
            float left = Margin + LeftWidth + 16f;
            float right = canvasW - RightColumn;
            if (MoleculeCardPanel.Instance != null && MoleculeCardPanel.Instance.IsOpen) right -= 440f + 16f;
            float width = Mathf.Clamp(right - left, 320f, 460f);
            _stepRect.sizeDelta = new Vector2(width, _stepRect.sizeDelta.y);
            _stepRect.anchoredPosition = new Vector2((left + right) / 2f - canvasW / 2f, -(TopBarBottom + 4));
        }

        // ================= Подписи над молекулами =================

        private void RefreshLabels(NervApp app, WorldSnapshot snap)
        {
            Camera cam = NervEnvironment.MainCamera;
            _placed.Clear();
            int used = 0;
            bool hide = PeriodicTablePanel.Instance != null && PeriodicTablePanel.Instance.IsOpen;
            if (cam != null && !hide)
            {
                // Сначала соли (они «главнее» своих ионов), потом молекулы.
                foreach (IonicGroup g in snap.IonicGroups)
                {
                    if (used >= 40) break;
                    var atoms = new List<AtomId>();
                    foreach (Molecule m in g.Members) atoms.AddRange(m.Atoms);
                    if (PlaceTag(app, cam, used, atoms, FormulaText.Subscript(g.Formula), g.Name ?? "ионное соединение", g.Name == null, false, 0.5f))
                        used++;
                }
                foreach (Molecule m in snap.Molecules)
                {
                    if (used >= 40) break;
                    bool ion = m.Charge != 0;
                    if (m.Atoms.Count < 2 && !ion) continue;
                    if (ion && _grouped.Contains(m.Atoms[0])) continue;
                    string name = m.Name ?? "?";
                    if (m.Unstable) name = (m.Name ?? "") + (m.Name == null ? "неустойчиво" : " · неустойчиво");
                    else if (m.IsRadical && m.Atoms.Count > 1 && (m.Name == null || !m.Name.Contains("радикал"))) name += " · радикал";
                    if (PlaceTag(app, cam, used, m.Atoms, NervDirector.Pretty(m.Formula, m.Charge), name, m.Name == null && !m.Unstable, m.IsRadical || m.Unstable, 0.35f))
                        used++;
                }
            }
            for (int i = used; i < _tags.Count; i++)
                if (_tags[i].Rect.gameObject.activeSelf) _tags[i].Rect.gameObject.SetActive(false);
        }

        private bool PlaceTag(NervApp app, Camera cam, int slot, IReadOnlyList<AtomId> atoms, string formula, string name,
            bool unknown, bool radical, float lift)
        {
            Vector3 c = Vector3.zero;
            int n = 0;
            foreach (AtomId id in atoms)
            {
                if (!_index.TryGetValue(id, out int i)) continue;
                c += app.AtomWorld(i);
                n++;
            }
            if (n == 0) return false;
            c /= n;
            // Подпись — чуть выше верхнего края молекулы (в экранном «вверх»).
            Vector3 up = cam.transform.up;
            float top = 0f;
            foreach (AtomId id in atoms)
            {
                if (!_index.TryGetValue(id, out int i)) continue;
                float r = MoleculeView.VisualRadius(ElementTable.Get(app.Snapshot.AtomicNumbers[i])) * NervApp.MetersPerAngstrom;
                top = Mathf.Max(top, Vector3.Dot(app.AtomWorld(i) - c, up) + r);
            }
            Vector3 sp = cam.WorldToScreenPoint(c + up * (top + lift * 0.1f * NervApp.MetersPerAngstrom));
            if (sp.z <= 0) return false;

            Tag tag = slot < _tags.Count ? _tags[slot] : CreateTag();
            if (!tag.Rect.gameObject.activeSelf) tag.Rect.gameObject.SetActive(true);
            string text = "<color=#FFFFFF>" + formula + "</color>  " + name;
            if (tag.Text.text != text) tag.Text.text = text;
            tag.Text.color = radical ? NervUI.Heat : new Color32(0xB7, 0xC0, 0xD1, 0xFF);
            tag.Border.color = radical ? NervUI.WithAlpha(NervUI.Heat, 0.5f) : new Color(1, 1, 1, 0.10f);
            tag.Group.alpha = unknown ? 0.6f : 1f;

            float scale = NervUI.Canvas.scaleFactor;
            var size = new Vector2(tag.Text.preferredWidth + 20f, 26f);
            float canvasW = NervUI.Root.rect.width, canvasH = NervUI.Root.rect.height;
            float x = Mathf.Clamp(sp.x / scale - size.x / 2f, 4f, canvasW - size.x - 4f);
            float y = Mathf.Clamp(sp.y / scale + 4f, 4f, canvasH - size.y - 4f);
            var rect = new UnityEngine.Rect(x, y, size.x, size.y);
            // Раздвигаем пересекающиеся подписи: сдвигаем вверх, пока не найдётся свободное место.
            for (int tries = 0; tries < 6 && _placed.Any(o => o.Overlaps(rect)); tries++)
                rect.y += size.y + 2f;
            _placed.Add(rect);
            tag.Rect.anchoredPosition = new Vector2(rect.x, rect.y);
            tag.Rect.sizeDelta = size;
            return true;
        }

        private Tag CreateTag()
        {
            Image box = NervUI.Box(_labels, "Tag", NervUI.WithAlpha(NervUI.Glass, 0.88f), 8, false);
            RectTransform r = box.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            Image border = NervUI.Border(box.transform, new Color(1, 1, 1, 0.10f), 8);
            Text t = NervUI.Label(box.transform, "", NervUI.Regular, 12, NervUI.TextSoft, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            NervUI.Stretch(t.rectTransform);
            var tag = new Tag { Rect = r, Box = box, Border = border, Text = t, Group = box.gameObject.AddComponent<CanvasGroup>() };
            tag.Group.blocksRaycasts = false;
            _tags.Add(tag);
            return tag;
        }

        private static string StepTitle(string name)
        {
            if (string.IsNullOrEmpty(name)) return "реакция";
            int dash = name.IndexOf(" — ", System.StringComparison.Ordinal);
            return dash > 0 ? name.Substring(0, dash) : name;
        }
    }
}
