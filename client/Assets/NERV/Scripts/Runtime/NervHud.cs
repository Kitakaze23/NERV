using System.Collections.Generic;
using System.Linq;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Naming;
using Nerv.Core.Sim;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Временный интерфейс на IMGUI: инструменты, элементы, сцены, среда, список веществ, журнал,
    /// подписи над молекулами. Дальше его заменят 3D-панели в стиле концепта (они нужны и для VR).
    /// </summary>
    public sealed class NervHud : MonoBehaviour
    {
        /// <summary>Курсор над панелью интерфейса (камера и инструменты его игнорируют).</summary>
        public static bool PointerOverUi { get; private set; }

        private NervInteraction _interaction;
        private readonly List<Rect> _panels = new List<Rect>();
        private sealed class LogEntry
        {
            public string Text;
            public float Time;
            public Color Color;
            public int Count;
        }

        private const float LogLife = 14f;
        private readonly List<LogEntry> _log = new List<LogEntry>();
        private readonly Dictionary<AtomId, int> _index = new Dictionary<AtomId, int>();
        private GUIStyle _panel, _title, _label, _small, _button, _active, _tag, _logStyle;
        private Texture2D _panelTex, _buttonTex, _activeTex, _tagTex;
        private float _uiScale = 1f;

        private void Start() => _interaction = GetComponent<NervInteraction>();

        private void Update()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;

            Vector2 p = NervInput.PointerPosition;
            var gui = new Vector2(p.x, Screen.height - p.y) / _uiScale;
            PointerOverUi = _panels.Any(r => r.Contains(gui));

            foreach (SimEvent e in app.Events)
                if (e.Type == SimEventType.MoleculeIdentified && e.Text != null && !e.Text.StartsWith("Атомарный"))
                    Log("Опознано: " + e.Text, NervEnvironment.Energy);
            foreach (ReactionRecord r in app.Reactions)
                Log(r.Reaction.Name + (r.Reaction.DeltaHKj < 0 ? "  · " + (-r.Reaction.DeltaHKj).ToString("0") + " кДж/моль" : ""),
                    r.Reaction.DeltaHKj < 0 ? NervEnvironment.Heat : NervEnvironment.Cold);
        }

        private void Log(string text, Color color)
        {
            // Повтор той же записи (одно и то же вещество опознано снова, цепная реакция) — счётчик ×N, а не новая строка.
            LogEntry same = _log.Find(e => e.Text == text && Time.time - e.Time < LogLife);
            if (same != null)
            {
                same.Count++;
                same.Time = Time.time;
                _log.Remove(same);
                _log.Add(same);
                return;
            }
            _log.Add(new LogEntry { Text = text, Time = Time.time, Color = color, Count = 1 });
            if (_log.Count > 6) _log.RemoveAt(0);
        }

        private void OnGUI()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;
            EnsureStyles();
            _uiScale = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 110f : 1f, 1f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(_uiScale, _uiScale, 1f));
            float w = Screen.width / _uiScale, h = Screen.height / _uiScale;

            _panels.Clear();
            DrawLabels(app);
            DrawLeft(app, h);
            DrawRight(app, w, h);
            DrawLog(h);

            if (app.DataError != null)
                GUI.Label(new Rect(240, 16, w - 520, 40), app.DataError, _label);
        }

        // ================= Левая панель: инструменты, элементы, сцены =================

        private void DrawLeft(NervApp app, float h)
        {
            var r = new Rect(16, 16, 220, Mathf.Min(h - 32, 560));
            _panels.Add(r);
            GUILayout.BeginArea(r, _panel);
            GUILayout.Label("NERV", _title);

            GUILayout.Label("ИНСТРУМЕНТ", _small);
            GUILayout.BeginHorizontal();
            ToolButton("Рука", NervTool.Hand);
            ToolButton("Искра", NervTool.Spark);
            ToolButton("Удалить", NervTool.Erase);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("ЭЛЕМЕНТЫ  (клавиши 1–8)", _small);
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 4; col++)
                {
                    string sym = NervInteraction.Palette[row * 4 + col];
                    bool on = _interaction.Tool == NervTool.Place && _interaction.Element == sym;
                    Color prev = GUI.contentColor;
                    GUI.contentColor = MoleculeView.ElementColor(ElementTable.Get(sym));
                    if (GUILayout.Button(sym, on ? _active : _button, GUILayout.Height(30)))
                    {
                        _interaction.Element = sym;
                        _interaction.Tool = NervTool.Place;
                    }
                    GUI.contentColor = prev;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("СЦЕНЫ", _small);
            foreach (NervScenes.Preset preset in NervScenes.All)
                if (GUILayout.Button(preset.Name, preset.Name == app.SceneName ? _active : _button))
                    app.LoadScene(preset);

            NervScenes.Preset current = NervScenes.All.FirstOrDefault(p => p.Name == app.SceneName);
            if (current != null)
            {
                GUILayout.Space(6);
                GUILayout.Label(current.Hint, _small);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("ПКМ — вращать · колесо — приблизить\nShift+ПКМ — сдвиг · Пробел — пауза", _small);
            GUILayout.EndArea();
        }

        private void ToolButton(string text, NervTool tool)
        {
            if (GUILayout.Button(text, _interaction.Tool == tool ? _active : _button, GUILayout.Height(28)))
                _interaction.Tool = tool;
        }

        // ================= Правая панель: среда и вещества =================

        private void DrawRight(NervApp app, float w, float h)
        {
            var r = new Rect(w - 266, 16, 250, Mathf.Min(h - 32, 620));
            _panels.Add(r);
            GUILayout.BeginArea(r, _panel);
            WorldSnapshot snap = app.Snapshot;

            GUILayout.Label("СРЕДА", _small);
            GUILayout.Label("Температура: " + app.Temperature.ToString("0") + " K   (сейчас " + snap.Temperature.ToString("0") + " K)", _label);
            float t = GUILayout.HorizontalSlider((float)app.Temperature, 0f, 3000f);
            if (Mathf.Abs(t - (float)app.Temperature) > 0.5f) app.SetTemperature(Mathf.Round(t));

            GUILayout.Label("Скорость времени: ×" + app.TimeScale.ToString(app.TimeScale < 1 ? "0.00" : "0.0"), _label);
            // Логарифмическая шкала ×0,05 … ×4: середина ползунка — ×1.
            float logNow = Mathf.Log((float)app.TimeScale, 2f);
            float logNew = GUILayout.HorizontalSlider(logNow, Mathf.Log(0.05f, 2f), 2f);
            if (Mathf.Abs(logNew - logNow) > 0.01f)
            {
                float scale = Mathf.Pow(2f, logNew);
                if (Mathf.Abs(scale - 1f) < 0.06f) scale = 1f;
                app.SetTimeScale(scale);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Вакуум", app.Medium == Medium.Vacuum ? _active : _button)) app.SetMedium(Medium.Vacuum);
            if (GUILayout.Button("Вода", app.Medium == Medium.Water ? _active : _button)) app.SetMedium(Medium.Water);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(app.Paused ? "Продолжить" : "Пауза", _button)) app.Paused = !app.Paused;
            if (GUILayout.Button("Очистить", _button)) app.ClearAtoms();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Атомов: " + snap.AtomCount + "   ·   " + (snap.Tick * 0.5 / 1000.0).ToString("0.0") + " пс", _small);

            GUILayout.Space(8);
            GUILayout.Label("ВЕЩЕСТВА В СЦЕНЕ", _small);
            var counts = new SortedDictionary<string, int>();
            foreach (Molecule m in snap.Molecules)
            {
                string key = (m.Name ?? "неизвестное соединение") + "  " + FormulaText.Subscript(m.Formula) + FormulaText.ChargeSuperscript(m.Charge);
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
            foreach (IonicGroup g in snap.IonicGroups)
            {
                string key = (g.Name ?? "ионное соединение") + "  " + FormulaText.Subscript(g.Formula) + "  (кристалл)";
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
            int shown = 0;
            foreach (KeyValuePair<string, int> kv in counts.OrderByDescending(k => k.Value))
            {
                if (shown++ >= 14) break;
                GUILayout.Label((kv.Value > 1 ? kv.Value + " × " : "") + kv.Key, _label);
            }
            GUILayout.EndArea();
        }

        // ================= Журнал =================

        private void DrawLog(float h)
        {
            _log.RemoveAll(e => Time.time - e.Time > LogLife);
            if (_log.Count == 0) return;
            const float line = 22f;
            float width = 0f;
            foreach (LogEntry e in _log) width = Mathf.Max(width, _logStyle.CalcSize(new GUIContent(LogText(e))).x);
            float y = h - 16 - _log.Count * line;
            var r = new Rect(252, y - 8, width + 24, _log.Count * line + 16);
            _panels.Add(r);
            GUI.Box(r, GUIContent.none, _panel);
            for (int i = 0; i < _log.Count; i++)
            {
                LogEntry e = _log[i];
                Color c = e.Color;
                c.a = Mathf.Clamp01((LogLife - (Time.time - e.Time)) / 4f);
                _logStyle.normal.textColor = c;
                GUI.Label(new Rect(264, y + i * line, width, line), LogText(e), _logStyle);
            }
        }

        private static string LogText(LogEntry e) => e.Count > 1 ? e.Text + "   ×" + e.Count : e.Text;

        // ================= Подписи над молекулами =================

        private void DrawLabels(NervApp app)
        {
            WorldSnapshot snap = app.Snapshot;
            Camera cam = NervEnvironment.MainCamera;
            _index.Clear();
            for (int i = 0; i < snap.AtomCount; i++) _index[snap.Ids[i]] = i;

            int drawn = 0;
            foreach (Molecule m in snap.Molecules)
            {
                if (drawn >= 40) break;
                bool ion = m.Charge != 0;
                if (m.Atoms.Count < 2 && !ion) continue;
                string text = (m.Name ?? "?") + "  " + FormulaText.Subscript(m.Formula) + FormulaText.ChargeSuperscript(m.Charge);
                if (DrawTag(app, cam, m.Atoms, text, m.Name == null)) drawn++;
            }
            foreach (IonicGroup g in snap.IonicGroups)
            {
                var atoms = new List<AtomId>();
                foreach (Molecule m in g.Members) atoms.AddRange(m.Atoms);
                DrawTag(app, cam, atoms, (g.Name ?? "?") + "  " + FormulaText.Subscript(g.Formula), g.Name == null, 0.5f);
            }
        }

        private bool DrawTag(NervApp app, Camera cam, IReadOnlyList<AtomId> atoms, string text, bool unknown, float lift = 0.35f)
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
            Vector2 g = new Vector2(sp.x, Screen.height - sp.y) / _uiScale;
            Vector2 size = _tag.CalcSize(new GUIContent(text));
            Color prev = GUI.color;
            if (unknown) GUI.color = new Color(1, 1, 1, 0.55f);
            // Не даём подписи уйти за край экрана.
            float x = Mathf.Clamp(g.x - size.x / 2, 4, Screen.width / _uiScale - size.x - 4);
            float yTop = Mathf.Clamp(g.y - size.y - 4, 4, Screen.height / _uiScale - size.y - 4);
            GUI.Label(new Rect(x, yTop, size.x, size.y), text, _tag);
            GUI.color = prev;
            return true;
        }

        // ================= Стили =================

        private void EnsureStyles()
        {
            if (_panel != null) return;
            _panelTex = Tex(new Color(0.063f, 0.086f, 0.15f, 0.82f));
            _buttonTex = Tex(new Color(1f, 1f, 1f, 0.06f));
            _activeTex = Tex(new Color(0.36f, 0.88f, 0.9f, 0.28f));
            _tagTex = Tex(new Color(0.063f, 0.086f, 0.15f, 0.7f));

            _panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 12, 12) };
            _panel.normal.background = _panelTex;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _title.normal.textColor = Color.white;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _label.normal.textColor = new Color(0.91f, 0.93f, 0.96f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _small.normal.textColor = new Color(0.6f, 0.64f, 0.72f);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            _button.normal.background = _buttonTex;
            _button.hover.background = _activeTex;
            _button.normal.textColor = _button.hover.textColor = new Color(0.85f, 0.88f, 0.93f);
            _active = new GUIStyle(_button);
            _active.normal.background = _activeTex;
            _active.normal.textColor = Color.white;
            _tag = new GUIStyle(GUI.skin.label) { fontSize = 12, padding = new RectOffset(6, 6, 2, 2), alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _tag.normal.background = _tagTex;
            _tag.normal.textColor = Color.white;
            _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = false };
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
