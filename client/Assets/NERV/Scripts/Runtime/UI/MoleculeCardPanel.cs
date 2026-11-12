using System.Collections.Generic;
using System.Linq;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Naming;
using Nerv.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Nerv.Client.UI
{
    /// <summary>
    /// Карточка вещества (концепт, экран «Карточка молекулы»). Открывается щелчком по атому рукой.
    /// Показывает название, тривиальные названия, формулу, категорию и опасность, свойства, факты
    /// и изомеры («та же формула — другое вещество»). Следит за молекулой: если она прореагировала,
    /// карточка показывает то, во что превратился выбранный атом.
    /// </summary>
    public sealed class MoleculeCardPanel : MonoBehaviour
    {
        public static MoleculeCardPanel Instance { get; private set; }
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        private const float Width = 440f;

        private RectTransform _root, _content;
        private AtomId _atom = AtomId.None;
        private string _signature;
        private MoleculeView _view;

        private void Awake() => Instance = this;

        public void Open(AtomId atom)
        {
            if (_root == null) Build();
            _atom = atom;
            _signature = null;
            _root.gameObject.SetActive(true);
        }

        public void Close()
        {
            _atom = AtomId.None;
            if (_root != null) _root.gameObject.SetActive(false);
            if (_view != null) _view.Selected.Clear();
        }

        private void Build()
        {
            NervUI.Init();
            Image panel = NervUI.Panel(NervUI.Root, "Molecule Card", 20, new Color(1, 1, 1, 0.12f));
            _root = panel.rectTransform;
            _root.anchorMin = _root.anchorMax = new Vector2(1, 1);
            _root.pivot = new Vector2(1, 1);
            _root.sizeDelta = new Vector2(Width, 200);
            NervUI.Column(panel.gameObject, 24, 12);
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _content = _root;
        }

        private void LateUpdate()
        {
            if (!IsOpen) return;
            NervApp app = NervApp.Instance;
            if (app == null) return;
            if (_view == null) _view = app.GetComponent<MoleculeView>();

            // Справа от карточки — правая панель HUD; ставим карточку левее неё.
            float scale = NervUI.Canvas.scaleFactor;
            float right = (Screen.width - NervHud.RightPanelLeftPx) / Mathf.Max(0.01f, scale) + 16f;
            _root.anchoredPosition = new Vector2(-right, -16f);

            WorldSnapshot snap = app.Snapshot;
            Molecule m = snap.Molecules.FirstOrDefault(x => x.Atoms.Contains(_atom));
            if (m == null)
            {
                Close();
                return;
            }
            _view.Selected.Clear();
            foreach (AtomId id in m.Atoms) _view.Selected.Add(id);

            string sig = m.CanonicalKey + "|" + m.Name + "|" + m.BondCount + "|" + m.Charge + "|" + m.Atoms.Count + "|" + m.FreeValence;
            if (sig == _signature) return;
            _signature = sig;
            Rebuild(app, m);
        }

        // ================= Содержимое =================

        private void Rebuild(NervApp app, Molecule m)
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Transform c = _content.GetChild(i);
                if (c.name == "Border") continue;
                Destroy(c.gameObject);
            }
            // Рамка не должна участвовать в раскладке.
            Transform border = _content.Find("Border");
            if (border != null && border.GetComponent<LayoutElement>() == null)
                border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            CompoundInfo info = m.Compound;
            bool known = m.Name != null;

            // Шапка: статус + закрыть.
            HorizontalLayoutGroup head = NervUI.Row(_content, 8, "Head");
            string badge = m.IsRadical ? "Радикал" : known ? "Опознано" : "Неизвестное соединение";
            Color badgeColor = m.IsRadical ? NervUI.Heat : known ? NervUI.Energy : NervUI.Muted;
            Image b = NervUI.Box(head.transform, "Badge", badgeColor, 6, false);
            Text bt = NervUI.Label(b.transform, badge, NervUI.SemiBold, 12, NervUI.Ink, TextAnchor.MiddleCenter);
            NervUI.Stretch(bt.rectTransform);
            bt.horizontalOverflow = HorizontalWrapMode.Overflow;
            NervUI.Size(b, bt.preferredWidth + 20, 24);
            string source = info != null ? (info.Curated ? "курированная запись" : "запись базы") : m.Ion != null ? "таблица ионов" : "";
            Text src = NervUI.Label(head.transform, source, NervUI.Regular, 12, NervUI.Muted, TextAnchor.MiddleLeft);
            NervUI.Size(src, -1, 24, 1);
            Button close = NervUI.Button(head.transform, "✕", NervUI.ButtonStyle.Ghost, Close, 14, 10, "Close");
            NervUI.Size(close, 36, 36);

            // Название и формула.
            string title = m.Name ?? FormulaText.Subscript(m.Formula) + FormulaText.ChargeSuperscript(m.Charge);
            Text name = NervUI.Label(_content, title, NervUI.Display, title.Length > 18 ? 26 : 34, Color.white);
            NervUI.Size(name, -1, -1);
            if (info != null && info.TrivialRu.Count > 0)
                NervUI.Label(_content, string.Join(" · ", info.TrivialRu), NervUI.Regular, 14, new Color32(0xB7, 0xC0, 0xD1, 0xFF));

            HorizontalLayoutGroup fr = NervUI.Row(_content, 12, "Formula");
            Text formula = NervUI.Label(fr.transform, NervDirector.Pretty(m.Formula, m.Charge),
                NervUI.SemiBold, 26, Color.white, TextAnchor.MiddleLeft);
            formula.horizontalOverflow = HorizontalWrapMode.Overflow;
            NervUI.Size(formula, formula.preferredWidth, 34);
            if (info?.Category != null) Tag(fr.transform, info.Category, NervUI.Energy);
            if (!string.IsNullOrEmpty(info?.Hazard)) Tag(fr.transform, info.Hazard, NervUI.Heat);
            if (m.IsRadical && (info == null || info.Category != "радикал")) Tag(fr.transform, "нестабильно", NervUI.Heat);

            Divider();

            // Свойства.
            Section("Свойства");
            double mass = 0;
            WorldSnapshot snap = app.Snapshot;
            var atomSet = new HashSet<AtomId>(m.Atoms);
            for (int i = 0; i < snap.AtomCount; i++)
                if (atomSet.Contains(snap.Ids[i])) mass += ElementTable.Get(snap.AtomicNumbers[i]).Mass;
            Property("Молярная масса", NervUI.Num(mass, "0.00") + " г/моль");
            Property("Атомов", m.Atoms.Count.ToString());
            Property("Связей", m.BondCount.ToString());
            if (m.Charge != 0) Property("Заряд", (m.Charge > 0 ? "+" : "−") + Mathf.Abs(m.Charge));
            if (m.IsRadical) Property("Неспаренных электронов", m.FreeValence.ToString());

            // Соль, в которую входит ион.
            foreach (IonicGroup g in snap.IonicGroups)
                if (g.Members.Contains(m))
                {
                    Property("Входит в", (g.Name ?? "ионное соединение") + " " + FormulaText.Subscript(g.Formula));
                    break;
                }

            // Факты.
            if (info != null && info.FactsRu.Count > 0)
            {
                Section("Факты");
                foreach (string f in info.FactsRu)
                    NervUI.Label(_content, f, NervUI.Regular, 14, NervUI.TextSoft);
            }
            else if (!known)
            {
                Section("Что это");
                NervUI.Label(_content, m.IsRadical
                    ? "Частица с неспаренными электронами (радикал). Такие частицы живут недолго: при первой встрече вступают в реакцию."
                    : "Такого вещества пока нет в базе NERV. Возможно, оно неустойчиво или встречается редко.",
                    NervUI.Regular, 14, NervUI.TextSoft);
            }

            // Изомеры.
            IReadOnlyList<CompoundInfo> isomers = app.Data?.Compounds.Isomers(m.Formula, m.CanonicalKey) ?? new List<CompoundInfo>();
            if (isomers.Count > 0)
            {
                Image box = NervUI.Box(_content, "Isomers", new Color(1, 1, 1, 0.04f), 14, false);
                NervUI.Border(box.transform, new Color(1, 1, 1, 0.10f), 14);
                NervUI.Column(box.gameObject, 14, 4);
                box.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 12, 12);
                NervUI.Label(box.transform, "Та же формула " + FormulaText.Subscript(m.Formula) + " — другое вещество", NervUI.Regular, 12, NervUI.Muted);
                foreach (CompoundInfo iso in isomers.Take(3))
                    NervUI.Label(box.transform, iso.NameRu, NervUI.SemiBold, 15, Color.white);
            }

            string footer = info?.WikidataId != null ? "Источник: Wikidata " + info.WikidataId
                : info != null ? "Источник: база NERV" : "Формула посчитана по атомам в сцене";
            NervUI.Label(_content, footer, NervUI.Regular, 12, NervUI.Dim);
        }

        private void Tag(Transform parent, string text, Color color)
        {
            Image t = NervUI.Box(parent, "Tag", NervUI.WithAlpha(color, 0.12f), 6, false);
            Text l = NervUI.Label(t.transform, text, NervUI.Regular, 12, Color.Lerp(color, Color.white, 0.45f), TextAnchor.MiddleCenter);
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            NervUI.Stretch(l.rectTransform);
            NervUI.Size(t, l.preferredWidth + 20, 24);
        }

        private void Divider()
        {
            Image d = NervUI.Box(_content, "Divider", new Color(1, 1, 1, 0.08f), 1, false);
            NervUI.Size(d, -1, 1);
        }

        private void Section(string title)
        {
            Text t = NervUI.Label(_content, title.ToUpperInvariant(), NervUI.DisplayMedium, 11, NervUI.Muted);
            NervUI.Size(t, -1, 18);
        }

        private void Property(string name, string value)
        {
            RectTransform row = NervUI.Rect(_content, name);
            NervUI.Size(row, -1, 22);
            NervUI.Stretch(NervUI.Label(row, name, NervUI.Regular, 14, new Color32(0xB7, 0xC0, 0xD1, 0xFF), TextAnchor.MiddleLeft).rectTransform);
            NervUI.Stretch(NervUI.Label(row, value, NervUI.SemiBold, 14, Color.white, TextAnchor.MiddleRight).rectTransform);
        }
    }
}
