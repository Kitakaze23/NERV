using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Nerv.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Nerv.Client.UI
{
    /// <summary>
    /// Журнал опыта: каждое событие сцены по времени — шаги реакций (с уравнением и ΔH), опознанные вещества, распады.
    /// В отличие от журнала HUD ничего не сворачивает. Окно с фильтрами (клавиша J), выгрузка в CSV
    /// (разделитель «;», UTF-8 с меткой — открывается в Excel, Numbers и Google Таблицах).
    /// Записи копятся с загрузки сцены, до 5000 (старые вытесняются).
    /// </summary>
    public sealed class ExperimentLogPanel : MonoBehaviour
    {
        public static ExperimentLogPanel Instance { get; private set; }

        public enum Kind { Reaction, Identified, Decayed }

        public struct Entry
        {
            public double TimePs;
            public Kind Kind;
            public string What, Detail;
            public double DeltaH; // кДж/моль, NaN — нет
        }

        private const int MaxEntries = 5000, MaxRows = 400;
        private readonly List<Entry> _entries = new List<Entry>();
        private string _scene;
        private Kind? _filter;
        private bool _dirty = true;
        private float _nextRebuild;

        private RectTransform _root, _content;
        private Text _count;
        private readonly Dictionary<string, Button> _filters = new Dictionary<string, Button>();

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public IReadOnlyList<Entry> Entries => _entries;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ================= Сбор =================

        private void Collect(NervApp app)
        {
            if (_scene != app.SceneName || app.Host == null)
            {
                _scene = app.SceneName;
                _entries.Clear();
                _dirty = true;
            }
            if (NervReplay.Active) return; // в повторе — старые кадры, не новые события
            double now = app.Snapshot.Tick * 0.0005;
            foreach (ReactionRecord r in app.Reactions)
                Add(new Entry
                {
                    TimePs = r.Tick * 0.0005, Kind = Kind.Reaction, What = NervDirector.Equation(r),
                    Detail = r.Reaction.Name, DeltaH = r.Reaction.DeltaHKj,
                });
            foreach (SimEvent e in app.Events)
            {
                if (e.Type == SimEventType.MoleculeIdentified && e.Text != null && !e.Text.StartsWith("Атомарный", StringComparison.Ordinal))
                    Add(new Entry { TimePs = now, Kind = Kind.Identified, What = e.Text, Detail = "опознано", DeltaH = double.NaN });
                else if (e.Type == SimEventType.MoleculeDecayed && e.Text != null)
                    Add(new Entry { TimePs = now, Kind = Kind.Decayed, What = NervDirector.Pretty(e.Text, 0) + " распалось", Detail = "распад по слабой связи", DeltaH = double.NaN });
            }
        }

        private void Add(Entry e)
        {
            _entries.Add(e);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            _dirty = true;
        }

        // ================= CSV =================

        public static string ToCsv(IEnumerable<Entry> entries, string scene)
        {
            var ci = CultureInfo.GetCultureInfo("ru-RU");
            var sb = new StringBuilder();
            sb.Append("Время, пс;Событие;Что произошло;Подробности;ΔH, кДж/моль;Сцена\n");
            foreach (Entry e in entries)
            {
                sb.Append(e.TimePs.ToString("0.000", ci)).Append(';')
                  .Append(Csv(KindName(e.Kind))).Append(';')
                  .Append(Csv(e.What)).Append(';')
                  .Append(Csv(e.Detail)).Append(';')
                  .Append(double.IsNaN(e.DeltaH) ? "" : e.DeltaH.ToString("0", ci)).Append(';')
                  .Append(Csv(scene)).Append('\n');
            }
            return sb.ToString();
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ';', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static string KindName(Kind k) => k == Kind.Reaction ? "реакция" : k == Kind.Identified ? "вещество" : "распад";

        private void Export()
        {
            try
            {
                Directory.CreateDirectory(NervSaves.ExportsDir);
                string name = "Журнал — " + NervSaves.SafeFileName(_scene ?? "сцена") + " — " + DateTime.Now.ToString("yyyy-MM-dd HH-mm");
                string path = NervSaves.UniquePath(NervSaves.ExportsDir, name, ".csv");
                File.WriteAllText(path, ToCsv(Filtered(), _scene), new UTF8Encoding(true));
                NervSaves.Toast("Журнал выгружен: " + Path.GetFileName(path));
                NervSaves.OpenFolder(NervSaves.ExportsDir);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                NervSaves.Toast("Не удалось выгрузить журнал: " + e.Message);
            }
        }

        private IEnumerable<Entry> Filtered() => _filter == null ? _entries : _entries.Where(e => e.Kind == _filter.Value);

        // ================= Окно =================

        public void Toggle()
        {
            if (_root == null) Build();
            bool open = !_root.gameObject.activeSelf;
            _root.gameObject.SetActive(open);
            if (open)
            {
                _root.SetAsLastSibling();
                _dirty = true;
            }
        }

        private void Build()
        {
            NervUI.Init();
            Transform root = NervUI.Root;
            Button dim = NervUI.Button(root, "", NervUI.ButtonStyle.Plain, Toggle, 12, 0, "Experiment Log");
            _root = (RectTransform)dim.transform;
            NervUI.Stretch(_root);
            dim.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.6f);
            dim.transition = Selectable.Transition.None;

            Image panel = NervUI.Panel(_root, "Window", 18);
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(940, 640);
            NervUI.RegisterOccluder(pr);

            Text title = NervUI.Label(panel.transform, "Журнал опыта", NervUI.Display, 22, Color.white, TextAnchor.MiddleLeft);
            NervUI.Place(title.rectTransform, 28, 22, 360, 36);
            _count = NervUI.Label(panel.transform, "", NervUI.Regular, 13, NervUI.TextSoft, TextAnchor.MiddleLeft);
            NervUI.Place(_count.rectTransform, 28, 58, 520, 20);

            float x = 28;
            foreach ((string label, Kind? k, float w) in new (string, Kind?, float)[]
                { ("Все", null, 64), ("Реакции", Kind.Reaction, 96), ("Вещества", Kind.Identified, 100), ("Распады", Kind.Decayed, 96) })
            {
                Kind? kind = k;
                Button b = NervUI.Button(panel.transform, label, NervUI.ButtonStyle.Ghost, () => { _filter = kind; _dirty = true; }, 13, 8);
                NervUI.Place((RectTransform)b.transform, x, 92, w, 34);
                _filters[label] = b;
                x += w + 6;
            }
            Button csv = NervUI.Button(panel.transform, "Выгрузить CSV", NervUI.ButtonStyle.Primary, Export, 13, 8);
            NervUI.Place((RectTransform)csv.transform, 940 - 28 - 312, 22, 150, 36);
            Button close = NervUI.Button(panel.transform, "Закрыть", NervUI.ButtonStyle.Ghost, Toggle, 13, 8);
            NervUI.Place((RectTransform)close.transform, 940 - 28 - 150, 22, 150, 36);

            // Заголовок таблицы.
            Header(panel.transform, 140);

            // Прокручиваемый список.
            RectTransform view = NervUI.Rect(panel.transform, "Viewport");
            NervUI.Place(view, 20, 170, 900, 450);
            view.gameObject.AddComponent<RectMask2D>();
            Image hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0.001f); // ловит колесо мыши
            _content = NervUI.Rect(view, "Content");
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup v = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = _content;
            sr.viewport = view;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24;

            _root.gameObject.SetActive(false);
        }

        private static readonly float[] Cols = { 0, 92, 196, 620, 780 };

        private static void Header(Transform parent, float y)
        {
            string[] names = { "ВРЕМЯ, ПС", "СОБЫТИЕ", "ЧТО ПРОИЗОШЛО", "ПОДРОБНОСТИ", "ТЕПЛО, КДЖ/МОЛЬ" };
            for (int i = 0; i < names.Length; i++)
            {
                Text t = NervUI.Label(parent, names[i], NervUI.DisplayMedium, 10, NervUI.Muted, TextAnchor.MiddleLeft);
                NervUI.Place(t.rectTransform, 32 + Cols[i], y, (i + 1 < Cols.Length ? Cols[i + 1] - Cols[i] : 120) - 8, 22);
            }
        }

        private void Rebuild()
        {
            _dirty = false;
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            List<Entry> rows = Filtered().ToList();
            int reactions = _entries.Count(e => e.Kind == Kind.Reaction);
            _count.text = _entries.Count == 0
                ? "Пока пусто. Запусти реакцию — каждое событие попадёт сюда с точным временем."
                : _entries.Count + " событий · реакций " + reactions + " · показаны последние " + Math.Min(rows.Count, MaxRows);
            foreach (KeyValuePair<string, Button> kv in _filters)
                NervUI.SetActive(kv.Value, (kv.Key == "Все" && _filter == null) ||
                                           (kv.Key == "Реакции" && _filter == Kind.Reaction) ||
                                           (kv.Key == "Вещества" && _filter == Kind.Identified) ||
                                           (kv.Key == "Распады" && _filter == Kind.Decayed));
            var ci = CultureInfo.GetCultureInfo("ru-RU");
            // Новые сверху.
            for (int i = rows.Count - 1, shown = 0; i >= 0 && shown < MaxRows; i--, shown++)
            {
                Entry e = rows[i];
                RectTransform row = NervUI.Rect(_content, "Row");
                NervUI.Size(row, -1, 26);
                Color kc = e.Kind == Kind.Reaction ? (e.DeltaH < 0 ? NervUI.Heat : NervEnvironment.Cold)
                         : e.Kind == Kind.Identified ? NervUI.Energy : NervUI.Heat;
                Cell(row, 0, e.TimePs.ToString("0.000", ci), NervUI.Mono, NervUI.TextSoft);
                Cell(row, 1, KindName(e.Kind), NervUI.SemiBold, kc);
                Cell(row, 2, e.What, NervUI.SemiBold, Color.white);
                Cell(row, 3, e.Detail, NervUI.Regular, NervUI.Muted);
                Cell(row, 4, double.IsNaN(e.DeltaH) ? "" : e.DeltaH.ToString("+0;−0;0", ci), NervUI.Mono, kc);
            }
        }

        private static void Cell(RectTransform row, int col, string text, Font font, Color color)
        {
            Text t = NervUI.Label(row, text ?? "", font, 13, color, TextAnchor.MiddleLeft);
            float w = (col + 1 < Cols.Length ? Cols[col + 1] - Cols[col] : 120) - 8;
            NervUI.Place(t.rectTransform, 12 + Cols[col], 3, w, 20);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ================= Кадр =================

        private void LateUpdate()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;
            Collect(app);
            if (NervInput.JournalPressed) Toggle();
            if (IsOpen && NervInput.EscapePressed) Toggle();
            // Во время горения события идут каждый кадр — перестраиваем таблицу не чаще двух раз в секунду.
            if (IsOpen && _dirty && Time.unscaledTime >= _nextRebuild)
            {
                _nextRebuild = Time.unscaledTime + 0.5f;
                Rebuild();
            }
        }
    }
}
