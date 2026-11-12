using System.Collections.Generic;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nerv.Client.UI
{
    /// <summary>
    /// Таблица Менделеева (концепт, экран «Таблица»): 118 клеток по периодам и группам,
    /// цвет категории, доступность (доступно / опционально / скоро) и карточка элемента справа
    /// с превью атома, свойствами и кнопкой «Добавить в сцену».
    /// </summary>
    public sealed class PeriodicTablePanel : MonoBehaviour
    {
        public static PeriodicTablePanel Instance { get; private set; }
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        private const float Cell = 56f, Gap = 5f;

        private RectTransform _root;
        private readonly Dictionary<int, Image> _cellBorders = new Dictionary<int, Image>();
        private readonly List<GameObject> _comingSoon = new List<GameObject>();
        private Element _selected, _shown;
        private bool _onlyAvailable;
        private Button _allButton, _availableButton;

        // Карточка элемента.
        private Image _previewAtom;
        private Text _previewSymbol, _name, _subtitle, _propNames, _propValues, _hint;
        private readonly List<Image> _dots = new List<Image>();
        private Button _addButton;

        private static readonly Dictionary<ElementCategory, (string Name, Color Color)> Categories = new Dictionary<ElementCategory, (string, Color)>
        {
            { ElementCategory.Nonmetal, ("Неметаллы", new Color32(0x5C, 0xE1, 0xE6, 0xFF)) },
            { ElementCategory.AlkaliMetal, ("Щелочные", new Color32(0xFF, 0x8A, 0x65, 0xFF)) },
            { ElementCategory.AlkalineEarthMetal, ("Щёлочноземельные", new Color32(0xFF, 0xB5, 0x47, 0xFF)) },
            { ElementCategory.TransitionMetal, ("Переходные", new Color32(0x7F, 0xB8, 0xFF, 0xFF)) },
            { ElementCategory.PostTransitionMetal, ("Постпереходные", new Color32(0x8F, 0xD3, 0xC1, 0xFF)) },
            { ElementCategory.Metalloid, ("Полуметаллы", new Color32(0xC6, 0xA8, 0xFF, 0xFF)) },
            { ElementCategory.Halogen, ("Галогены", new Color32(0x9B, 0xE3, 0x6B, 0xFF)) },
            { ElementCategory.NobleGas, ("Благородные газы", new Color32(0xE2, 0x8C, 0xFF, 0xFF)) },
            { ElementCategory.Lanthanide, ("Лантаноиды", new Color32(0xF7, 0xA8, 0xC4, 0xFF)) },
            { ElementCategory.Actinide, ("Актиноиды", new Color32(0xF2, 0x8B, 0x82, 0xFF)) },
        };

        private static readonly Dictionary<ElementCategory, string> CategoryNameRu = new Dictionary<ElementCategory, string>
        {
            { ElementCategory.Nonmetal, "неметалл" }, { ElementCategory.AlkaliMetal, "щелочной металл" },
            { ElementCategory.AlkalineEarthMetal, "щёлочноземельный металл" }, { ElementCategory.TransitionMetal, "переходный металл" },
            { ElementCategory.PostTransitionMetal, "постпереходный металл" }, { ElementCategory.Metalloid, "полуметалл" },
            { ElementCategory.Halogen, "галоген" }, { ElementCategory.NobleGas, "благородный газ" },
            { ElementCategory.Lanthanide, "лантаноид" }, { ElementCategory.Actinide, "актиноид" },
        };

        private void Awake() => Instance = this;

        public void Toggle()
        {
            if (_root == null) Build();
            bool open = !_root.gameObject.activeSelf;
            _root.gameObject.SetActive(open);
            if (open)
            {
                NervInteraction ia = NervApp.Instance != null ? NervApp.Instance.GetComponent<NervInteraction>() : null;
                Select(ElementTable.Get(ia != null ? ia.Element : "O"));
            }
        }

        public void Close()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        // ================= Построение =================

        private void Build()
        {
            NervUI.Init();
            _root = NervUI.Stretch(NervUI.Rect(NervUI.Root, "Periodic Table"));
            // Затемнение сцены под таблицей; щелчок по нему закрывает таблицу.
            Image dim = _root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.03f, 0.05f, 0.55f);
            _root.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            float gridW = 18 * Cell + 17 * Gap, gridH = 9 * Cell + 8 * Gap + 10f;
            float panelW = gridW + 36, panelH = gridH + 36 + 52 + 40;
            Image panel = NervUI.Panel(_root, "Table Panel", 20);
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(panelW, panelH);
            pr.anchoredPosition = new Vector2(-150f, 0f);
            // Щелчок по самой панели не должен закрывать таблицу (иначе он «всплывёт» к затемнению).
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            NervUI.Place(NervUI.Label(panel.transform, "Таблица Менделеева", NervUI.DisplayMedium, 18, Color.white, TextAnchor.MiddleLeft).rectTransform, 18, 18, 400, 36);
            _allButton = NervUI.Button(panel.transform, "Все 118", NervUI.ButtonStyle.Ghost, () => SetFilter(false), 13, 9);
            NervUI.Place((RectTransform)_allButton.transform, panelW - 18 - 240, 18, 110, 36);
            int available = 0;
            foreach (Element e in ElementTable.All) if (e.IsAvailable) available++;
            _availableButton = NervUI.Button(panel.transform, "Доступные " + available, NervUI.ButtonStyle.Ghost, () => SetFilter(true), 13, 9);
            NervUI.Place((RectTransform)_availableButton.transform, panelW - 18 - 124, 18, 124, 36);

            RectTransform grid = NervUI.Place(NervUI.Rect(panel.transform, "Grid"), 18, 70, gridW, gridH);
            foreach (Element e in ElementTable.All) BuildCell(grid, e);
            // Ссылки на ряды лантаноидов и актиноидов в основной таблице.
            BuildRangeCell(grid, 6, "57–71", Categories[ElementCategory.Lanthanide].Color);
            BuildRangeCell(grid, 7, "89–103", Categories[ElementCategory.Actinide].Color);

            BuildLegend(panel.transform, 18, 70 + gridH + 14, gridW);
            BuildCard();
            SetFilter(false);
        }

        private static Vector2 CellPos(int row, int column)
        {
            // Ряды 8–9 (лантаноиды, актиноиды) отделены небольшим зазором.
            float y = (row - 1) * (Cell + Gap) + (row >= 8 ? 10f : 0f);
            return new Vector2((column - 1) * (Cell + Gap), y);
        }

        private void BuildCell(RectTransform grid, Element e)
        {
            Color cat = Categories[e.Category].Color;
            Color cpk = MoleculeView.ElementColor(e);
            bool on = e.Availability == ElementAvailability.Enabled;
            bool opt = e.Availability == ElementAvailability.Optional;

            Color bg = on ? NervUI.WithAlpha(cpk, 0.16f) : opt ? NervUI.WithAlpha(cat, 0.10f) : NervUI.WithAlpha(cat, 0.06f);
            Image cell = NervUI.Box(grid, e.Symbol, bg, 8);
            Vector2 p = CellPos(e.TableRow, e.TableColumn);
            NervUI.Place(cell.rectTransform, p.x, p.y, Cell, Cell);
            Color borderColor = on ? NervUI.WithAlpha(cpk, 0.85f) : opt ? NervUI.WithAlpha(cpk, 0.6f) : NervUI.WithAlpha(cat, 0.14f);
            _cellBorders[e.Z] = NervUI.Border(cell.transform, borderColor, 8);

            Color fg = (on || opt) ? Color.white : NervUI.WithAlpha(NervUI.Text, 0.42f);
            NervUI.Place(NervUI.Label(cell.transform, e.Z.ToString(), NervUI.Mono, 9, NervUI.WithAlpha(fg, 0.8f)).rectTransform, 5, 3, 40, 12);
            Text sym = NervUI.Label(cell.transform, e.Symbol, NervUI.SemiBold, (on || opt) ? 18 : 16, fg, TextAnchor.MiddleCenter);
            NervUI.Place(sym.rectTransform, 0, (on || opt) ? 11 : 14, Cell, 26);
            if (on || opt)
            {
                Text name = NervUI.Label(cell.transform, e.NameRu, NervUI.Regular, 8, NervUI.WithAlpha(Color.white, 0.9f), TextAnchor.MiddleCenter);
                NervUI.Place(name.rectTransform, 0, Cell - 15, Cell, 12);
            }

            Button b = cell.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            cb.colorMultiplier = 1.6f;
            b.colors = cb;
            b.onClick.AddListener(() => Select(e));
            // Наведение показывает элемент в карточке, не меняя выбора.
            EventTrigger trig = cell.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Show(e));
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => Show(_selected));
            trig.triggers.Add(enter);
            trig.triggers.Add(exit);

            if (!e.IsAvailable) _comingSoon.Add(cell.gameObject);
        }

        private void BuildRangeCell(RectTransform grid, int row, string text, Color color)
        {
            Image cell = NervUI.Box(grid, "Range " + text, Color.clear, 8, false);
            Vector2 p = CellPos(row, 3);
            NervUI.Place(cell.rectTransform, p.x, p.y, Cell, Cell);
            NervUI.Border(cell.transform, NervUI.WithAlpha(color, 0.4f), 8);
            NervUI.Stretch(NervUI.Label(cell.transform, text, NervUI.Mono, 10, NervUI.WithAlpha(NervUI.Text, 0.5f), TextAnchor.MiddleCenter).rectTransform);
            _comingSoon.Add(cell.gameObject);
        }

        private void BuildLegend(Transform parent, float x, float y, float width)
        {
            HorizontalLayoutGroup row = NervUI.Row(parent, 14, "Legend");
            NervUI.Place((RectTransform)row.transform, x, y, width, 20);
            foreach (KeyValuePair<ElementCategory, (string Name, Color Color)> kv in Categories)
            {
                HorizontalLayoutGroup item = NervUI.Row(row.transform, 6, kv.Value.Name);
                Image dot = NervUI.Box(item.transform, "Dot", kv.Value.Color, 3, false);
                NervUI.Size(dot, 10, 10);
                Text t = NervUI.Label(item.transform, kv.Value.Name, NervUI.Regular, 12, NervUI.Muted);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                NervUI.Size(t, t.preferredWidth, 16);
            }
        }

        private void BuildCard()
        {
            Image card = NervUI.Panel(_root, "Element Card", 20, NervUI.WithAlpha(NervUI.Energy, 0.30f));
            RectTransform cr = card.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(268, 620);
            float tableHalf = (18 * Cell + 17 * Gap + 36) / 2f;
            cr.anchoredPosition = new Vector2(-150f + tableHalf + 20 + 134, 0f);
            // Карточка не закрывает таблицу по щелчку на ней.
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            RectTransform preview = NervUI.Place(NervUI.Rect(card.transform, "Preview"), 26, 20, 216, 190);
            _previewAtom = NervUI.Soft(preview, "Atom", Color.white);
            RectTransform ar = _previewAtom.rectTransform;
            ar.anchorMin = ar.anchorMax = ar.pivot = new Vector2(0.5f, 0.5f);
            ar.sizeDelta = new Vector2(124, 124);
            for (int i = 0; i < 8; i++)
            {
                Image d = NervUI.Soft(preview, "Dot", Color.white);
                RectTransform dr = d.rectTransform;
                dr.anchorMin = dr.anchorMax = dr.pivot = new Vector2(0.5f, 0.5f);
                _dots.Add(d);
            }
            _previewSymbol = NervUI.Label(preview, "", NervUI.SemiBold, 30, Color.white, TextAnchor.MiddleCenter);
            NervUI.Stretch(_previewSymbol.rectTransform);
            _previewSymbol.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);

            _name = NervUI.Label(card.transform, "", NervUI.Display, 24, Color.white);
            NervUI.Place(_name.rectTransform, 20, 218, 228, 32);
            _subtitle = NervUI.Label(card.transform, "", NervUI.Regular, 13, NervUI.Muted);
            NervUI.Place(_subtitle.rectTransform, 20, 252, 228, 20);
            _propNames = NervUI.Label(card.transform, "", NervUI.Regular, 13, new Color32(0xB7, 0xC0, 0xD1, 0xFF));
            _propNames.lineSpacing = 1.35f;
            NervUI.Place(_propNames.rectTransform, 20, 282, 228, 120);
            _propValues = NervUI.Label(card.transform, "", NervUI.Mono, 13, Color.white, TextAnchor.UpperRight);
            _propValues.lineSpacing = 1.35f;
            NervUI.Place(_propValues.rectTransform, 20, 282, 228, 120);

            Image hintBox = NervUI.Box(card.transform, "Hint", NervUI.WithAlpha(NervUI.Energy, 0.08f), 10, false);
            NervUI.Place(hintBox.rectTransform, 20, 408, 228, 110);
            _hint = NervUI.Label(hintBox.transform, "", NervUI.Regular, 12, NervUI.TextSoft);
            NervUI.Stretch(_hint.rectTransform, 10);

            _addButton = NervUI.Button(card.transform, "Добавить в сцену", NervUI.ButtonStyle.Primary, AddToScene, 14, 12);
            NervUI.Place((RectTransform)_addButton.transform, 20, 532, 228, 46);
            Text alt = NervUI.Label(card.transform, "или клавиши 1–8 на клавиатуре", NervUI.Regular, 11, NervUI.Muted, TextAnchor.MiddleCenter);
            NervUI.Place(alt.rectTransform, 20, 584, 228, 18);
        }

        // ================= Состояние =================

        private void SetFilter(bool onlyAvailable)
        {
            _onlyAvailable = onlyAvailable;
            foreach (GameObject g in _comingSoon) g.SetActive(!onlyAvailable);
            SetChip(_allButton, !onlyAvailable);
            SetChip(_availableButton, onlyAvailable);
        }

        private static void SetChip(Button b, bool active)
        {
            var img = b.GetComponent<Image>();
            img.color = active ? NervUI.Energy : new Color(1, 1, 1, 0.04f);
            Text t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = active ? NervUI.Ink : NervUI.Muted;
        }

        private void Select(Element e)
        {
            if (e == null) return;
            if (_selected != null && _cellBorders.TryGetValue(_selected.Z, out Image old)) RestoreBorder(_selected, old);
            _selected = e;
            if (_cellBorders.TryGetValue(e.Z, out Image border)) border.color = NervUI.Energy;
            Show(e);
        }

        private static void RestoreBorder(Element e, Image border)
        {
            Color cat = Categories[e.Category].Color, cpk = MoleculeView.ElementColor(e);
            border.color = e.Availability == ElementAvailability.Enabled ? NervUI.WithAlpha(cpk, 0.85f)
                : e.Availability == ElementAvailability.Optional ? NervUI.WithAlpha(cpk, 0.6f) : NervUI.WithAlpha(cat, 0.14f);
        }

        private void Show(Element e)
        {
            if (e == null || e == _shown) return;
            _shown = e;
            bool available = e.IsAvailable;
            Color cpk = available ? MoleculeView.ElementColor(e) : Categories[e.Category].Color;

            _previewAtom.color = NervUI.WithAlpha(cpk, available ? 1f : 0.35f);
            _previewSymbol.text = e.Symbol;
            _name.text = e.NameRu;
            _subtitle.text = "<color=#E8ECF4>" + e.Symbol + "</color> · Z " + e.Z + " · " + CategoryNameRu[e.Category];

            int unpaired = available ? ElectronRules.MaxValence(e, 0) : 0;
            int lonePairs = available ? ElectronRules.LonePairs(e, 0, 0) : 0;
            if (available)
            {
                _propNames.text = "Атомная масса\nЭлектроотриц.\nВалентность\nВалентных e⁻\nКовал. радиус";
                _propValues.text = NervUI.Num(e.Mass, "0.000") + "\n" + NervUI.Num(e.Electronegativity, "0.00") + "\n" +
                                   Roman(unpaired) + "\n" + e.ValenceElectrons + "\n" +
                                   Mathf.RoundToInt((float)(e.CovalentRadius * 100)) + " пм";
                _hint.text = Hint(e, unpaired, lonePairs);
            }
            else
            {
                _propNames.text = "Свойства этого элемента появятся,\nкогда он станет доступен в песочнице.";
                _propValues.text = "";
                _hint.text = "Пока в песочнице 8 элементов: H, C, N, O, Na, Cl, а также S и F. Остальные — скоро.";
            }
            _addButton.interactable = available;
            PlaceDots(unpaired, lonePairs);
        }

        private void PlaceDots(int unpaired, int lonePairs)
        {
            // Яркие — неспаренные электроны (свободная валентность), бледные пары — неподелённые электроны.
            var angles = new List<(float deg, bool bright)>();
            float start = -90f - (unpaired - 1) * 30f;
            for (int i = 0; i < unpaired; i++) angles.Add((start + i * 60f, true));
            float pairStart = 90f - (lonePairs - 1) * 35f;
            for (int p = 0; p < lonePairs; p++)
            {
                float c = pairStart + p * 70f;
                angles.Add((c - 6f, false));
                angles.Add((c + 6f, false));
            }
            for (int i = 0; i < _dots.Count; i++)
            {
                Image d = _dots[i];
                if (i >= angles.Count)
                {
                    d.gameObject.SetActive(false);
                    continue;
                }
                d.gameObject.SetActive(true);
                (float deg, bool bright) = angles[i];
                float t = deg * Mathf.Deg2Rad, r = 62f + 8f;
                d.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(t) * r, -Mathf.Sin(t) * r);
                float size = bright ? 18f : 11f;
                d.rectTransform.sizeDelta = new Vector2(size, size);
                d.color = bright ? new Color(0.85f, 1f, 1f, 1f) : new Color(0.8f, 0.85f, 0.95f, 0.55f);
            }
        }

        private static string Hint(Element e, int unpaired, int lonePairs)
        {
            if (unpaired == 0) return e.NameRu + " не образует ковалентных связей в нейтральном виде.";
            string s = unpaired + " " + Plural(unpaired, "яркая точка", "яркие точки", "ярких точек") +
                       " — неспаренные электроны: " + e.NameRu.ToLowerInvariant() + " образует " + unpaired + " " +
                       Plural(unpaired, "связь", "связи", "связей") + ".";
            if (lonePairs > 0)
                s += " Бледные пары — неподелённые электроны (" + lonePairs + " " + Plural(lonePairs, "пара", "пары", "пар") + ").";
            if (e.Category == ElementCategory.AlkaliMetal)
                s += " Металл охотно отдаёт электрон неметаллу и становится катионом.";
            return s;
        }

        private static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }

        private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", _ => "—" };

        private void AddToScene()
        {
            if (_selected == null || !_selected.IsAvailable) return;
            NervInteraction ia = NervApp.Instance != null ? NervApp.Instance.GetComponent<NervInteraction>() : null;
            if (ia != null)
            {
                ia.Element = _selected.Symbol;
                ia.Tool = NervTool.Place;
            }
            Close();
        }
    }
}
