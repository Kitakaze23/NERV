using System.Collections.Generic;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Nerv.Client
{
    /// <summary>
    /// «Повтор реакции» (экран «Реакция» концепта): клиент постоянно держит последние ~2,5 с кадров,
    /// а когда срабатывает шаг реакции — вырезает клип «1,2 с до и 1,2 с после».
    /// В повторе симуляция на паузе, сцена рисуется из клипа в ×0,5, атомы реакции пронумерованы
    /// (номера из карты атомов шаблона — видно, кто куда перешёл), есть полоса прокрутки.
    /// Ядро и журнал команд повтор не трогает: это чистое представление.
    /// </summary>
    public sealed class NervReplay : MonoBehaviour
    {
        public static NervReplay Instance { get; private set; }
        /// <summary>Идёт повтор: ввод в сцену выключен.</summary>
        public static bool Active => Instance != null && Instance._playing;

        private const float Before = 1.2f, After = 1.2f, Keep = 2.6f, Speed = 0.5f;

        private struct Frame
        {
            public float Time;
            public WorldSnapshot Snap;
        }

        private readonly List<Frame> _ring = new List<Frame>();
        private readonly Stack<WorldSnapshot> _pool = new Stack<WorldSnapshot>();

        // Ожидающая реакция и готовый клип.
        private ReactionRecord _pending;
        private float _pendingAt;
        private readonly List<Frame> _clip = new List<Frame>();
        private ReactionRecord _clipReaction;
        private float _clipReactionAt;

        // Воспроизведение.
        private bool _playing, _paused, _wasPaused;
        private float _cursor; // секунды от начала клипа
        private string _scene;

        // Интерфейс.
        private RectTransform _panel;
        private Button _open, _playPause, _close;
        private Text _openText, _time;
        private Slider _scrub;
        private bool _scrubbing;
        private readonly List<Text> _numbers = new List<Text>();
        private RectTransform _numbersLayer;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Вызывается из <see cref="NervApp"/> сразу после того, как снимок взят у ядра.</summary>
        public void AfterCopy(NervApp app)
        {
            float now = Time.unscaledTime;
            if (_scene != app.SceneName)
            {
                _scene = app.SceneName;
                Stop(app);
                _clip.Clear();
                _clipReaction = null;
                _pending = null;
            }

            if (_playing)
            {
                if (_clip.Count == 0) { Stop(app); return; }
                if (!_paused && !_scrubbing)
                {
                    _cursor += Time.unscaledDeltaTime * Speed;
                    if (_cursor > Duration) _cursor = 0; // по кругу
                }
                FrameAt(_cursor).CopyTo(app.Snapshot);
                app.Events.Clear();
                app.Reactions.Clear();
                return;
            }

            // Запись: копия текущего снимка в кольцо.
            WorldSnapshot copy = _pool.Count > 0 ? _pool.Pop() : new WorldSnapshot();
            app.Snapshot.CopyTo(copy);
            _ring.Add(new Frame { Time = now, Snap = copy });
            while (_ring.Count > 0 && now - _ring[0].Time > Keep)
            {
                _pool.Push(_ring[0].Snap);
                _ring.RemoveAt(0);
            }

            if (_pending == null && app.Reactions.Count > 0)
            {
                _pending = app.Reactions[0];
                _pendingAt = now;
            }
            if (_pending != null && now - _pendingAt >= After)
            {
                foreach (Frame f in _clip) _pool.Push(f.Snap);
                _clip.Clear();
                foreach (Frame f in _ring)
                {
                    if (f.Time < _pendingAt - Before) continue;
                    var s = new WorldSnapshot();
                    f.Snap.CopyTo(s);
                    _clip.Add(new Frame { Time = f.Time, Snap = s });
                }
                _clipReaction = _pending;
                _clipReactionAt = _pendingAt;
                _pending = null;
            }
        }

        private float Duration => _clip.Count < 2 ? 0 : _clip[_clip.Count - 1].Time - _clip[0].Time;

        private WorldSnapshot FrameAt(float t)
        {
            float target = _clip[0].Time + t;
            for (int i = 1; i < _clip.Count; i++)
                if (_clip[i].Time >= target) return _clip[i - 1].Snap;
            return _clip[_clip.Count - 1].Snap;
        }

        public void Play()
        {
            NervApp app = NervApp.Instance;
            if (app == null || _clip.Count < 2 || _playing) return;
            _wasPaused = app.Paused;
            app.Paused = true;
            _playing = true;
            _paused = false;
            _cursor = 0;
            UI.MoleculeCardPanel.Instance?.Close();
        }

        public void Stop(NervApp app)
        {
            if (!_playing) return;
            _playing = false;
            if (app != null) app.Paused = _wasPaused;
        }

        // ================= Интерфейс =================

        private void Build()
        {
            UI.NervUI.Init();
            Transform root = UI.NervUI.Root;

            _numbersLayer = UI.NervUI.Stretch(UI.NervUI.Rect(root, "Replay Numbers"));

            Image panel = UI.NervUI.Panel(root, "Replay", 14);
            _panel = panel.rectTransform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0);
            _panel.pivot = new Vector2(0.5f, 0);
            _panel.anchoredPosition = new Vector2(0, NervHud.Margin + 52 + 10);
            _panel.sizeDelta = new Vector2(460, 52);
            UI.NervUI.RegisterOccluder(_panel);

            _open = UI.NervUI.Button(panel.transform, "", UI.NervUI.ButtonStyle.Ghost, Play, 13);
            UI.NervUI.Stretch((RectTransform)_open.transform, 6);
            _openText = _open.GetComponentInChildren<Text>();

            _playPause = UI.NervUI.Button(panel.transform, "Пауза", UI.NervUI.ButtonStyle.Ghost, () => _paused = !_paused, 12, 8);
            UI.NervUI.Place((RectTransform)_playPause.transform, 6, 6, 72, 40);
            RectTransform scrubHost = UI.NervUI.Rect(panel.transform, "Scrub");
            UI.NervUI.Place(scrubHost, 88, 15, 230, 22);
            _scrub = UI.NervUI.Slider(scrubHost, 0, 1, v =>
            {
                if (!_playing) return;
                _cursor = v * Duration;
            });
            ((RectTransform)_scrub.transform).anchorMin = Vector2.zero;
            ((RectTransform)_scrub.transform).anchorMax = Vector2.one;
            _time = UI.NervUI.Label(panel.transform, "", UI.NervUI.Mono, 12, UI.NervUI.TextSoft, TextAnchor.MiddleCenter);
            UI.NervUI.Place(_time.rectTransform, 322, 6, 92, 40);
            _close = UI.NervUI.Button(panel.transform, "×", UI.NervUI.ButtonStyle.Ghost, () => Stop(NervApp.Instance), 18, 8);
            UI.NervUI.Place((RectTransform)_close.transform, 414, 6, 40, 40);
        }

        private void LateUpdate()
        {
            NervApp app = NervApp.Instance;
            if (app == null || app.Host == null) return;
            if (_panel == null) Build();

            if (_playing && NervInput.EscapePressed) Stop(app);

            bool has = _clip.Count >= 2 && _clipReaction != null;
            if (_panel.gameObject.activeSelf != has) _panel.gameObject.SetActive(has);
            if (!has)
            {
                HideNumbers(0);
                return;
            }

            _open.gameObject.SetActive(!_playing);
            _playPause.gameObject.SetActive(_playing);
            _scrub.gameObject.SetActive(_playing);
            _time.gameObject.SetActive(_playing);
            _close.gameObject.SetActive(_playing);

            if (!_playing)
            {
                string eq = NervDirector.Equation(_clipReaction);
                string text = "▶  Повтор реакции:  " + eq;
                if (_openText.text != text) _openText.text = text;
                HideNumbers(0);
                return;
            }

            _scrubbing = NervInput.LeftHeld && UI.NervUI.PointerOverUi;
            _scrub.SetValueWithoutNotify(Duration > 0 ? _cursor / Duration : 0);
            Text pp = _playPause.GetComponentInChildren<Text>();
            pp.text = _paused ? "▶" : "Пауза";
            _time.text = UI.NervUI.Num(_cursor, "0.0") + " / " + UI.NervUI.Num(Duration, "0.0") + " с";
            // Момент реакции на полосе: до него — «до», после — «после».
            float rel = _clipReactionAt - _clip[0].Time;
            _time.color = _cursor < rel ? UI.NervUI.TextSoft : UI.NervUI.Heat;

            DrawNumbers(app);
        }

        /// <summary>Номера атомов из карты шаблона над атомами сцены — как «1–4» в концепте.</summary>
        private void DrawNumbers(NervApp app)
        {
            Camera cam = NervEnvironment.MainCamera;
            WorldSnapshot snap = app.Snapshot;
            int used = 0;
            if (cam != null)
                foreach (KeyValuePair<int, AtomId> kv in _clipReaction.AtomMap.OrderBy(k => k.Key))
                {
                    int idx = -1;
                    for (int i = 0; i < snap.AtomCount; i++)
                        if (snap.Ids[i] == kv.Value) { idx = i; break; }
                    if (idx < 0) continue;
                    Vector3 sp = cam.WorldToScreenPoint(app.AtomWorld(idx));
                    if (sp.z <= 0) continue;
                    Text t = used < _numbers.Count ? _numbers[used] : NewNumber();
                    used++;
                    t.gameObject.SetActive(true);
                    t.text = kv.Key.ToString();
                    float scale = UI.NervUI.Canvas.scaleFactor;
                    t.rectTransform.anchoredPosition = new Vector2(sp.x / scale + 12, sp.y / scale + 12);
                }
            HideNumbers(used);
        }

        private Text NewNumber()
        {
            Text t = UI.NervUI.Label(_numbersLayer, "", UI.NervUI.Mono, 14, UI.NervUI.Energy, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
            t.rectTransform.sizeDelta = new Vector2(28, 20);
            t.raycastTarget = false;
            Outline o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.85f);
            _numbers.Add(t);
            return t;
        }

        private void HideNumbers(int from)
        {
            for (int i = from; i < _numbers.Count; i++)
                if (_numbers[i].gameObject.activeSelf) _numbers[i].gameObject.SetActive(false);
        }
    }
}
