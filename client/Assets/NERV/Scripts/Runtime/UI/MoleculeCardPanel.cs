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
        public RectTransform Root => _root;

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
            NervUI.RegisterOccluder(_root);
        }

        private void LateUpdate()
        {
            if (!IsOpen) return;
            NervApp app = NervApp.Instance;
            if (app == null) return;
            if (_view == null) _view = app.GetComponent<MoleculeView>();

            // Справа — колонка среды и журнала; карточка встаёт левее, под верхней панелью.
            _root.anchoredPosition = new Vector2(-NervHud.RightColumn, -(NervHud.TopBarBottom + 4f));

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
            Button close = NervUI.Button(head.transform, "×", NervUI.ButtonStyle.Ghost, Close, 20, 10, "Close");
            NervUI.Size(close, 36, 36);

            // Название и формула.
            string title = m.Name ?? FormulaText.Subscript(m.Formula) + FormulaText.ChargeSuperscript(m.Charge);
            Text name = NervUI.Label(_content, title, NervUI.Display, title.Length > 22 ? 22 : title.Length > 14 ? 26 : 34, Color.white);
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

            // Структурная формула: проекция настоящей 3D-молекулы на её главную плоскость.
            if (m.Atoms.Count >= 2 && m.Atoms.Count <= 24)
            {
                Section("Структурная формула");
                BuildStructure(app.Snapshot, m);
            }

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
                {
                    HorizontalLayoutGroup row = NervUI.Row(box.transform, 10, "Isomer");
                    row.childAlignment = TextAnchor.MiddleLeft;
                    row.childControlWidth = true;
                    row.childForceExpandWidth = false;
                    Text isoName = NervUI.Label(row.transform, iso.NameRu, NervUI.SemiBold, 15, Color.white);
                    NervUI.Size(isoName, -1, 34, 1);
                    string smiles = iso.Smiles;
                    Button build = NervUI.Button(row.transform, "Собрать в сцене", NervUI.ButtonStyle.Chip,
                        () => BuildNear(smiles), 12, 8);
                    NervUI.Size(build, 132, 30);
                }
            }

            // Ещё одна такая же молекула — удобно для реакций «два одинаковых».
            if (info != null && !string.IsNullOrEmpty(info.Smiles))
            {
                string own = info.Smiles;
                Button more = NervUI.Button(_content, "Добавить ещё одну", NervUI.ButtonStyle.Ghost, () => BuildNear(own), 13);
                NervUI.Size(more, -1, 38);
            }

            string footer = !string.IsNullOrEmpty(info?.WikidataId) ? "Источник: Wikidata " + info.WikidataId
                : info != null ? "Источник: база NERV" : "Формула посчитана по атомам в сцене";
            NervUI.Label(_content, footer, NervUI.Regular, 12, NervUI.Dim);
        }

        private static readonly Dictionary<string, Color> StructColors = new Dictionary<string, Color>
        {
            { "H", new Color32(0xE8, 0xEC, 0xF4, 0xFF) }, { "C", new Color32(0xC9, 0xD1, 0xDE, 0xFF) },
            { "N", new Color32(0x8F, 0xA5, 0xFF, 0xFF) }, { "O", new Color32(0xFF, 0x7A, 0x7A, 0xFF) },
            { "F", new Color32(0xA8, 0xE0, 0x80, 0xFF) }, { "Na", new Color32(0xC4, 0x9B, 0xFF, 0xFF) },
            { "S", new Color32(0xF2, 0xD3, 0x3A, 0xFF) }, { "Cl", new Color32(0x6E, 0xE0, 0x7F, 0xFF) },
        };

        /// <summary>
        /// Собрать вещество рядом с открытой молекулой: ядро само отодвинет его, если место занято.
        /// </summary>
        private void BuildNear(string smiles)
        {
            NervApp app = NervApp.Instance;
            WorldSnapshot snap = app != null ? app.Snapshot : null;
            if (snap == null) return;
            Molecule m = snap.Molecules.FirstOrDefault(x => x.Atoms.Contains(_atom));
            var atoms = new HashSet<AtomId>(m != null ? m.Atoms : (IEnumerable<AtomId>)new[] { _atom });
            Vector3 c = Vector3.zero;
            int count = 0;
            for (int i = 0; i < snap.AtomCount; i++)
            {
                if (!atoms.Contains(snap.Ids[i])) continue;
                c += new Vector3(snap.Positions[i * 3], snap.Positions[i * 3 + 1], snap.Positions[i * 3 + 2]);
                count++;
            }
            if (count > 0) c /= count;
            // Сбоку от исходной, примерно в плоскости экрана.
            var at = new Nerv.Core.Numerics.Vec3(c.x + 6, c.y, c.z);
            app.Submit(new BuildMolecule(smiles, at));
        }

        private void BuildStructure(WorldSnapshot snap, Molecule m)
        {
            const float W = Width - 48f, H = 170f, pad = 22f;
            RectTransform box = NervUI.Rect(_content, "Structure");
            NervUI.Size(box, -1, H);

            var atoms = new HashSet<AtomId>(m.Atoms);
            var idx = new List<int>();
            for (int i = 0; i < snap.AtomCount; i++) if (atoms.Contains(snap.Ids[i])) idx.Add(i);
            if (idx.Count < 2) return;
            var p = new Vector3[idx.Count];
            Vector3 c = Vector3.zero;
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                p[k] = new Vector3(snap.Positions[i * 3], snap.Positions[i * 3 + 1], snap.Positions[i * 3 + 2]);
                c += p[k];
            }
            c /= p.Length;

            // Главные оси (метод степеней для ковариации): на них молекула «раскрыта» сильнее всего.
            float[,] cov = new float[3, 3];
            foreach (Vector3 q0 in p)
            {
                Vector3 q = q0 - c;
                for (int r = 0; r < 3; r++)
                    for (int col = 0; col < 3; col++) cov[r, col] += q[r] * q[col];
            }
            Vector3 Mul(Vector3 v) => new Vector3(
                cov[0, 0] * v.x + cov[0, 1] * v.y + cov[0, 2] * v.z,
                cov[1, 0] * v.x + cov[1, 1] * v.y + cov[1, 2] * v.z,
                cov[2, 0] * v.x + cov[2, 1] * v.y + cov[2, 2] * v.z);
            Vector3 e1 = new Vector3(1f, 0.31f, 0.17f).normalized;
            for (int it = 0; it < 40; it++) { Vector3 n = Mul(e1); if (n.sqrMagnitude < 1e-12f) break; e1 = n.normalized; }
            Vector3 e2 = Vector3.Cross(e1, new Vector3(0.23f, 1f, 0.41f)).normalized;
            for (int it = 0; it < 40; it++)
            {
                Vector3 n = Mul(e2);
                n -= Vector3.Dot(n, e1) * e1;
                if (n.sqrMagnitude < 1e-12f) break;
                e2 = n.normalized;
            }

            var pts = new Vector2[p.Length];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int k = 0; k < p.Length; k++)
            {
                Vector3 q = p[k] - c;
                pts[k] = new Vector2(Vector3.Dot(q, e1), Vector3.Dot(q, e2));
                minX = Mathf.Min(minX, pts[k].x); maxX = Mathf.Max(maxX, pts[k].x);
                minY = Mathf.Min(minY, pts[k].y); maxY = Mathf.Max(maxY, pts[k].y);
            }
            float sx = (W - 2 * pad) / Mathf.Max(0.01f, maxX - minX), sy = (H - 2 * pad) / Mathf.Max(0.01f, maxY - minY);
            float scale = Mathf.Min(Mathf.Min(sx, sy), 70f); // не раздуваем маленькие молекулы
            Vector2 mid = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
            for (int k = 0; k < pts.Length; k++) pts[k] = (pts[k] - mid) * scale;

            var local = new Dictionary<int, int>();
            for (int k = 0; k < idx.Count; k++) local[idx[k]] = k;
            Color line = new Color32(0x6B, 0x74, 0x88, 0xFF);
            foreach (BondView b in snap.Bonds)
            {
                if (!local.TryGetValue(b.IndexA, out int ka) || !local.TryGetValue(b.IndexB, out int kb)) continue;
                Vector2 a = pts[ka], d = pts[kb] - pts[ka];
                float len = d.magnitude;
                if (len < 1f) continue;
                Vector2 dir = d / len, nrm = new Vector2(-dir.y, dir.x);
                // Линия не заходит под подписи атомов.
                const float gap = 11f;
                if (len <= 2 * gap + 2) continue;
                int order = Mathf.Clamp(b.Order, 1, 3);
                for (int o = 0; o < order; o++)
                {
                    float off = (o - (order - 1) / 2f) * 5f;
                    Image l = NervUI.Box(box, "Bond", line, 1, false);
                    RectTransform lr = l.rectTransform;
                    lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
                    lr.pivot = new Vector2(0.5f, 0.5f);
                    lr.sizeDelta = new Vector2(len - 2 * gap, 1.8f);
                    lr.anchoredPosition = a + d / 2f + nrm * off;
                    lr.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                }
            }
            for (int k = 0; k < idx.Count; k++)
            {
                string sym = ElementTable.Get(snap.AtomicNumbers[idx[k]]).Symbol;
                int q = snap.Charges[idx[k]];
                // В JetBrains Mono нет надстрочных ⁺⁻ — обычные знаки.
                string label = sym + (q > 0 ? "+" : q < 0 ? "−" : "");
                Text t = NervUI.Label(box, label, NervUI.Mono, 15, StructColors.TryGetValue(sym, out Color col) ? col : NervUI.Text, TextAnchor.MiddleCenter);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                RectTransform tr = t.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.sizeDelta = new Vector2(30, 20);
                tr.anchoredPosition = pts[k];
            }
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
