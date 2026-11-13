using System.Collections.Generic;
using System.Linq;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Nerv.Client.UI
{
    /// <summary>
    /// Режим «Лаборатория»: живые графики (вещества, температура, тепло) на месте мини-таблицы
    /// и инструмент «Измерить» — расстояние, длина и энергия связи, угол по 1–3 щелчкам на атомы.
    /// Показания измерений сглажены (~0,3 с): атомы колеблются, и мгновенные цифры прыгали бы.
    /// </summary>
    public sealed class LabPanel : MonoBehaviour
    {
        public static LabPanel Instance { get; private set; }

        private const float Width = 533, Height = 262;
        private const int MaxSamples = 600;          // 60 пс при шаге 0,1 пс
        private const long SampleTicks = 200;        // 0,1 пс
        private static readonly Color[] Palette =
        {
            new Color32(0x5C, 0xE1, 0xE6, 0xFF), new Color32(0xFF, 0xB5, 0x47, 0xFF), new Color32(0x7F, 0xB8, 0xFF, 0xFF),
            new Color32(0xFF, 0x7A, 0xA8, 0xFF), new Color32(0x7B, 0xE4, 0x95, 0xFF),
        };

        private enum Tab { Substances, Temperature, Heat }

        private struct Sample
        {
            public float TimePs, Temperature, HeatKj;
            public Dictionary<string, int> Counts;
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private long _lastSampleTick = -SampleTicks;
        private float _heat;
        private string _scene;
        private Tab _tab = Tab.Substances;

        private RectTransform _panel;
        private LineChart _chart;
        private Text _yMax, _yMin, _xMin, _xMax, _footer;
        private RectTransform _legend;
        private readonly Dictionary<Tab, Button> _tabs = new Dictionary<Tab, Button>();
        private readonly List<Text> _legendItems = new List<Text>();

        // Измерение.
        private readonly List<AtomId> _measure = new List<AtomId>();
        private RectTransform _readout;
        private Text _readoutText;
        private LineRenderer _lines;
        private float _smoothA, _smoothB, _smoothC;
        private bool _smoothInit;

        public bool Visible => _panel != null && _panel.gameObject.activeSelf;
        public int MeasuredCount => _measure.Count;
        public bool IsMeasured(AtomId id) => _measure.Contains(id);

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ================= Построение =================

        private void Build()
        {
            NervUI.Init();
            Transform root = NervUI.Root;
            Image panel = NervUI.Panel(root, "Lab Charts", 16);
            _panel = panel.rectTransform;
            _panel.anchorMin = _panel.anchorMax = Vector2.zero;
            _panel.pivot = Vector2.zero;
            _panel.anchoredPosition = new Vector2(NervHud.Margin, NervHud.Margin);
            _panel.sizeDelta = new Vector2(Width, Height);
            NervUI.RegisterOccluder(_panel);

            Text title = NervUI.Label(panel.transform, "ГРАФИКИ", NervUI.DisplayMedium, 11, NervUI.Muted, TextAnchor.MiddleLeft);
            NervUI.Place(title.rectTransform, 16, 12, 90, 24);
            float x = 112;
            foreach ((Tab t, string label, float w) in new[] { (Tab.Substances, "Вещества", 96f), (Tab.Temperature, "Температура", 112f), (Tab.Heat, "Тепло", 72f) })
            {
                Tab tab = t;
                Button b = NervUI.Button(panel.transform, label, NervUI.ButtonStyle.Ghost, () => _tab = tab, 12, 8);
                NervUI.Place((RectTransform)b.transform, x, 10, w, 28);
                _tabs[t] = b;
                x += w + 6;
            }
            Button table = NervUI.Button(panel.transform, "таблица · T", NervUI.ButtonStyle.Plain, () => PeriodicTablePanel.Instance?.Toggle(), 12, 6);
            NervUI.Place((RectTransform)table.transform, Width - 104, 10, 92, 28);

            // Поле графика.
            RectTransform plot = NervUI.Rect(panel.transform, "Plot");
            NervUI.Place(plot, 56, 48, Width - 72, Height - 48 - 62);
            Image bg = plot.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.03f);
            bg.raycastTarget = false;
            var chartGo = NervUI.Rect(plot, "Chart");
            NervUI.Stretch(chartGo, 2);
            _chart = chartGo.gameObject.AddComponent<LineChart>();
            _chart.raycastTarget = false;

            _yMax = Axis(panel.transform, 8, 44, TextAnchor.UpperRight);
            _yMin = Axis(panel.transform, 8, Height - 62 - 14, TextAnchor.LowerRight);
            _xMin = Axis(panel.transform, 56, Height - 60, TextAnchor.UpperLeft);
            _xMax = Axis(panel.transform, Width - 16 - 120, Height - 60, TextAnchor.UpperRight);
            _xMax.rectTransform.sizeDelta = new Vector2(120, 16);

            _legend = NervUI.Rect(panel.transform, "Legend");
            NervUI.Place(_legend, 16, Height - 40, Width - 32, 18);
            HorizontalLayoutGroup lg = _legend.gameObject.AddComponent<HorizontalLayoutGroup>();
            lg.spacing = 14;
            lg.childControlWidth = true;
            lg.childForceExpandWidth = false;
            lg.childAlignment = TextAnchor.MiddleLeft;
            for (int i = 0; i < Palette.Length; i++)
            {
                Text t = NervUI.Label(_legend, "", NervUI.SemiBold, 11, Palette[i], TextAnchor.MiddleLeft);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                _legendItems.Add(t);
            }

            _footer = NervUI.Label(panel.transform, "", NervUI.Regular, 11, NervUI.Dim, TextAnchor.MiddleLeft);
            NervUI.Place(_footer.rectTransform, 16, Height - 22, Width - 32, 16);

            // Показания измерения — плашка у измеряемых атомов, поверх подписей молекул.
            Image ro = NervUI.Panel(root, "Measure Readout", 10, NervUI.WithAlpha(NervUI.Energy, 0.6f));
            _readout = ro.rectTransform;
            _readout.anchorMin = _readout.anchorMax = Vector2.zero;
            _readout.pivot = new Vector2(0.5f, 0f);
            ro.raycastTarget = false;
            _readoutText = NervUI.Label(ro.transform, "", NervUI.SemiBold, 13, Color.white, TextAnchor.MiddleCenter);
            NervUI.Stretch(_readoutText.rectTransform, 8);
            _readout.gameObject.SetActive(false);

            var lineGo = new GameObject("Measure Lines");
            lineGo.transform.SetParent(transform, false);
            _lines = lineGo.AddComponent<LineRenderer>();
            _lines.material = new Material(Shader.Find("Sprites/Default"));
            _lines.startColor = _lines.endColor = new Color(0.6f, 2.2f, 2.3f, 0.9f);
            _lines.widthMultiplier = 0.012f;
            _lines.positionCount = 0;
            _lines.numCapVertices = 2;
        }

        private static Text Axis(Transform parent, float x, float y, TextAnchor anchor)
        {
            Text t = NervUI.Label(parent, "", NervUI.Mono, 10, NervUI.Dim, anchor);
            NervUI.Place(t.rectTransform, x, y, 44, 16);
            return t;
        }

        // ================= Кадр =================

        private void LateUpdate()
        {
            NervApp app = NervApp.Instance;
            if (app == null || app.Host == null) return;
            if (_panel == null) Build();

            Collect(app);

            bool lab = app.LabMode;
            if (_panel.gameObject.activeSelf != lab) _panel.gameObject.SetActive(lab);
            if (!lab || NervInteraction.Instance == null || NervInteraction.Instance.Tool != NervTool.Measure) ClearMeasure();
            if (lab) DrawChart();
            UpdateMeasure(app);
        }

        /// <summary>Сбор точек идёт и в «Песочнице»: включил лабораторию — история уже есть.</summary>
        private void Collect(NervApp app)
        {
            WorldSnapshot snap = app.Snapshot;
            if (_scene != app.SceneName || snap.Tick < _lastSampleTick)
            {
                _scene = app.SceneName;
                _samples.Clear();
                _heat = 0;
                _lastSampleTick = snap.Tick - SampleTicks;
            }
            foreach (SimEvent e in app.Events)
                if (e.Type == SimEventType.HeatReleased) _heat += (float)e.Value;
            if (snap.Tick - _lastSampleTick < SampleTicks) return;
            _lastSampleTick = snap.Tick;

            var counts = new Dictionary<string, int>();
            foreach (Molecule m in snap.Molecules)
            {
                string key = NervDirector.Pretty(m.Formula, m.Charge);
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
            _samples.Add(new Sample { TimePs = snap.Tick * 0.0005f, Temperature = (float)snap.Temperature, HeatKj = _heat, Counts = counts });
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        }

        private void DrawChart()
        {
            foreach (KeyValuePair<Tab, Button> kv in _tabs) NervUI.SetActive(kv.Value, kv.Key == _tab);
            var series = new List<(Color color, List<Vector2> pts)>();
            var names = new List<string>();
            float yMin = 0, yMax = 1;
            string unit = "";

            if (_samples.Count >= 2)
            {
                switch (_tab)
                {
                    case Tab.Substances:
                    {
                        // Пять веществ с наибольшим количеством за окно.
                        List<string> top = _samples.SelectMany(s => s.Counts)
                            .GroupBy(kv => kv.Key).Select(g => (g.Key, Max: g.Max(kv => kv.Value)))
                            .OrderByDescending(t => t.Max).ThenBy(t => t.Key, System.StringComparer.Ordinal)
                            .Take(Palette.Length).Select(t => t.Key).ToList();
                        foreach (string k in top)
                        {
                            var pts = _samples.Select(s => new Vector2(s.TimePs, s.Counts.TryGetValue(k, out int c) ? c : 0)).ToList();
                            yMax = Mathf.Max(yMax, pts.Max(p => p.y));
                            series.Add((Palette[series.Count], pts));
                            names.Add(k + "  " + (_samples[_samples.Count - 1].Counts.TryGetValue(k, out int now) ? now : 0));
                        }
                        unit = "шт.";
                        break;
                    }
                    case Tab.Temperature:
                    {
                        var pts = _samples.Select(s => new Vector2(s.TimePs, s.Temperature)).ToList();
                        yMax = Mathf.Max(400f, pts.Max(p => p.y) * 1.08f);
                        series.Add((NervUI.Heat, pts));
                        names.Add("температура сцены  " + Mathf.RoundToInt(pts[pts.Count - 1].y) + " K");
                        unit = "K";
                        break;
                    }
                    case Tab.Heat:
                    {
                        var pts = _samples.Select(s => new Vector2(s.TimePs, s.HeatKj)).ToList();
                        yMax = Mathf.Max(100f, pts.Max(p => p.y) * 1.08f);
                        yMin = Mathf.Min(0f, pts.Min(p => p.y) * 1.08f);
                        series.Add((NervUI.Heat, pts));
                        names.Add("выделилось тепла (сумма ΔH шагов)  " + Mathf.RoundToInt(pts[pts.Count - 1].y) + " кДж/моль");
                        unit = "кДж/моль";
                        break;
                    }
                }
            }

            float x0 = _samples.Count > 0 ? _samples[0].TimePs : 0, x1 = _samples.Count > 0 ? _samples[_samples.Count - 1].TimePs : 1;
            if (x1 - x0 < 1f) x1 = x0 + 1f;
            _chart.SetData(series, x0, x1, yMin, yMax);
            _yMax.text = Mathf.RoundToInt(yMax).ToString();
            _yMin.text = Mathf.RoundToInt(yMin).ToString();
            _xMin.text = NervUI.Num(x0, "0.0") + " пс";
            _xMax.text = NervUI.Num(x1, "0.0") + " пс";
            for (int i = 0; i < _legendItems.Count; i++)
            {
                bool on = i < names.Count;
                if (_legendItems[i].gameObject.activeSelf != on) _legendItems[i].gameObject.SetActive(on);
                if (on)
                {
                    _legendItems[i].text = "● " + names[i];
                    _legendItems[i].color = series[i].color;
                }
            }
            bool measuring = NervInteraction.Instance != null && NervInteraction.Instance.Tool == NervTool.Measure;
            _footer.text = _samples.Count < 2 ? "Запусти реакцию — графики появятся через доли пикосекунды."
                : measuring ? "Измерение: щёлкни 1–3 атома — свойства, расстояние или угол. Пустое место — сброс."
                : "Окно — последние " + NervUI.Num(x1 - x0, "0") + " пс · единицы: " + unit;
        }

        // ================= Измерение =================

        /// <summary>Щелчок инструментом «Измерить»: атом добавляется к измерению (четвёртый начинает новое).</summary>
        public void Pick(AtomId atom)
        {
            if (_measure.Count >= 3 || _measure.Contains(atom)) _measure.Clear();
            _measure.Add(atom);
            _smoothInit = false;
        }

        public void ClearMeasure()
        {
            if (_measure.Count == 0) return;
            _measure.Clear();
        }

        private void UpdateMeasure(NervApp app)
        {
            WorldSnapshot snap = app.Snapshot;
            var idx = new List<int>(3);
            foreach (AtomId id in _measure)
                for (int i = 0; i < snap.AtomCount; i++)
                    if (snap.Ids[i] == id) { idx.Add(i); break; }
            if (idx.Count != _measure.Count) // атом исчез (реакция, удаление, отмена)
            {
                _measure.Clear();
                idx.Clear();
            }

            Camera cam = NervEnvironment.MainCamera;
            if (idx.Count == 0 || cam == null)
            {
                if (_readout.gameObject.activeSelf) _readout.gameObject.SetActive(false);
                _lines.positionCount = 0;
                return;
            }

            _lines.positionCount = idx.Count;
            Vector3 center = Vector3.zero;
            for (int k = 0; k < idx.Count; k++)
            {
                Vector3 p = app.AtomWorld(idx[k]);
                _lines.SetPosition(k, p);
                center += p;
            }
            center /= idx.Count;

            float k2 = _smoothInit ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.3f) : 1f;
            _smoothInit = true;
            string text;
            if (idx.Count == 1)
            {
                int i = idx[0];
                Element e = ElementTable.Get(snap.AtomicNumbers[i]);
                _smoothA = Mathf.Lerp(_smoothA, snap.Temperatures[i], k2);
                int q = snap.Charges[i];
                text = e.Symbol + " · " + e.NameRu.ToLowerInvariant() +
                       "\nзаряд " + (q > 0 ? "+" + q : q < 0 ? "−" + (-q) : "0") +
                       " · свободных связей " + snap.FreeValences[i] +
                       "\nтемпература атома ≈ " + Mathf.RoundToInt(_smoothA) + " K";
            }
            else if (idx.Count == 2)
            {
                float d = Distance(snap, idx[0], idx[1]);
                _smoothA = Mathf.Lerp(_smoothA, d, k2);
                text = NervUI.Num(_smoothA, "0.00") + " Å";
                BondView? bond = FindBond(snap, idx[0], idx[1]);
                Element ea = ElementTable.Get(snap.AtomicNumbers[idx[0]]), eb = ElementTable.Get(snap.AtomicNumbers[idx[1]]);
                if (bond.HasValue)
                {
                    int order = bond.Value.Order;
                    double kcal = BondEnergies.Get(ea, eb, order);
                    text += "\nсвязь " + ea.Symbol + (order == 3 ? "≡" : order == 2 ? "=" : "–") + eb.Symbol + ", " +
                            (order == 3 ? "тройная" : order == 2 ? "двойная" : "одинарная") +
                            "\nэнергия ≈ " + Mathf.RoundToInt((float)(kcal * 4.184)) + " кДж/моль";
                }
                else text += "\nне связаны";
            }
            else
            {
                float a = Distance(snap, idx[0], idx[1]), b = Distance(snap, idx[1], idx[2]);
                Vector3 v1 = Pos(snap, idx[0]) - Pos(snap, idx[1]), v2 = Pos(snap, idx[2]) - Pos(snap, idx[1]);
                float ang = Vector3.Angle(v1, v2);
                _smoothA = Mathf.Lerp(_smoothA, ang, k2);
                _smoothB = Mathf.Lerp(_smoothB, a, k2);
                _smoothC = Mathf.Lerp(_smoothC, b, k2);
                string s0 = ElementTable.Get(snap.AtomicNumbers[idx[0]]).Symbol, s1 = ElementTable.Get(snap.AtomicNumbers[idx[1]]).Symbol,
                       s2 = ElementTable.Get(snap.AtomicNumbers[idx[2]]).Symbol;
                text = "угол " + s0 + "–" + s1 + "–" + s2 + " " + NervUI.Num(_smoothA, "0.0") + "°" +
                       "\n" + NervUI.Num(_smoothB, "0.00") + " Å  ·  " + NervUI.Num(_smoothC, "0.00") + " Å";
            }
            text += "\n<size=10><color=#8A94A8>среднее за 0,3 с</color></size>";

            Vector3 sp = cam.WorldToScreenPoint(center + cam.transform.up * 0.08f);
            if (sp.z <= 0)
            {
                _readout.gameObject.SetActive(false);
                return;
            }
            if (!_readout.gameObject.activeSelf) _readout.gameObject.SetActive(true);
            if (_readoutText.text != text) _readoutText.text = text;
            float scale = NervUI.Canvas.scaleFactor;
            var size = new Vector2(Mathf.Max(150f, _readoutText.preferredWidth + 20f), _readoutText.preferredHeight + 16f);
            _readout.sizeDelta = size;
            _readout.anchoredPosition = new Vector2(
                Mathf.Clamp(sp.x / scale, size.x / 2 + 4, NervUI.Root.rect.width - size.x / 2 - 4),
                Mathf.Clamp(sp.y / scale + 12f, 4, NervUI.Root.rect.height - size.y - 4));
        }

        private static Vector3 Pos(WorldSnapshot s, int i) => new Vector3(s.Positions[i * 3], s.Positions[i * 3 + 1], s.Positions[i * 3 + 2]);
        private static float Distance(WorldSnapshot s, int i, int j) => Vector3.Distance(Pos(s, i), Pos(s, j));

        private static BondView? FindBond(WorldSnapshot s, int i, int j)
        {
            foreach (BondView b in s.Bonds)
                if ((b.IndexA == i && b.IndexB == j) || (b.IndexA == j && b.IndexB == i)) return b;
            return null;
        }
    }

    /// <summary>Линейный график в uGUI: несколько рядов, толщина 2 единицы холста.</summary>
    public sealed class LineChart : MaskableGraphic
    {
        private List<(Color color, List<Vector2> pts)> _series = new List<(Color, List<Vector2>)>();
        private float _x0, _x1 = 1, _y0, _y1 = 1;

        public void SetData(List<(Color color, List<Vector2> pts)> series, float x0, float x1, float y0, float y1)
        {
            _series = series;
            _x0 = x0; _x1 = x1; _y0 = y0; _y1 = Mathf.Max(y1, y0 + 1e-3f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            UnityEngine.Rect r = rectTransform.rect;
            // Сетка: четыре горизонтальные линии.
            for (int g = 0; g <= 4; g++)
            {
                float y = r.yMin + r.height * g / 4f;
                Segment(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), 1f, new Color(1, 1, 1, 0.06f));
            }
            foreach ((Color color, List<Vector2> pts) in _series)
            {
                Vector2? prev = null;
                foreach (Vector2 p in pts)
                {
                    var q = new Vector2(r.xMin + (p.x - _x0) / (_x1 - _x0) * r.width, r.yMin + (p.y - _y0) / (_y1 - _y0) * r.height);
                    if (prev.HasValue) Segment(vh, prev.Value, q, 2f, color);
                    prev = q;
                }
            }
        }

        private static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            int i = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = c;
            v.position = a - n; vh.AddVert(v);
            v.position = a + n; vh.AddVert(v);
            v.position = b + n; vh.AddVert(v);
            v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
