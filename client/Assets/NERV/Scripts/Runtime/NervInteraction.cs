using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;
using UnityEngine;

namespace Nerv.Client
{
    public enum NervTool
    {
        /// <summary>Схватить и тащить атом (пружина, а не телепорт).</summary>
        Hand,
        /// <summary>Поставить атом выбранного элемента.</summary>
        Place,
        /// <summary>Искра: нагрев + радикалы.</summary>
        Spark,
        /// <summary>Удалить атом.</summary>
        Erase,
        /// <summary>Нагрев: пока кнопка зажата, атомы под курсором разогреваются.</summary>
        Heat,
        /// <summary>Холод: пока кнопка зажата, атомы под курсором остывают.</summary>
        Cold
    }

    /// <summary>
    /// Слой намерений: «схватить», «потянуть», «поставить атом», «искра».
    /// Получает луч из камеры (на десктопе — от мыши; в VR будет луч контроллера) и превращает
    /// его в команды ядра.
    /// </summary>
    public sealed class NervInteraction : MonoBehaviour
    {
        public NervTool Tool = NervTool.Hand;
        public string Element = "H";

        private MoleculeView _view;
        private AtomId _grabbed = AtomId.None;
        private float _grabDepth;
        private LineRenderer _spring;
        // Щелчок (без перетаскивания) по атому рукой открывает карточку вещества.
        private Vector2 _pressPos;
        private float _pressTime;
        private AtomId _pressAtom = AtomId.None;
        private Checkpoint _grabRecord;
        private float _lastThermalFx;
        /// <summary>Радиус зоны нагрева/холода, Å.</summary>
        public const float HeatRadius = 4f;

        /// <summary>Схваченный атом (для подсказки «Тяну углерод · 4 свободные связи»).</summary>
        public AtomId Grabbed => _grabbed;

        /// <summary>Поставить атом элемента под курсором (перетаскивание из таблицы).</summary>
        public void PlaceAtPointer(string element)
        {
            NervApp app = NervApp.Instance;
            Camera cam = NervEnvironment.MainCamera;
            if (app == null || cam == null) return;
            Ray ray = cam.ScreenPointToRay(NervInput.PointerPosition);
            NervHistory.Instance?.Record();
            app.Submit(new AddAtom(element, NervApp.ToSim(PlanePoint(cam, ray, app))));
            Element = element;
        }

        public static readonly string[] Palette = { "H", "C", "N", "O", "Na", "Cl", "S", "F" };

        private void Start()
        {
            _view = GetComponent<MoleculeView>();
            var go = new GameObject("Grab Spring");
            go.transform.SetParent(transform, false);
            _spring = go.AddComponent<LineRenderer>();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(unlit);
            m.SetColor("_BaseColor", NervEnvironment.Energy * 1.5f);
            m.color = NervEnvironment.Energy * 1.5f;
            _spring.material = m;
            _spring.widthMultiplier = 0.004f;
            _spring.positionCount = 0;
            _spring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;
            Camera cam = NervEnvironment.MainCamera;

            int digit = NervInput.DigitPressed;
            if (digit >= 1 && digit <= Palette.Length)
            {
                Element = Palette[digit - 1];
                Tool = NervTool.Place;
            }
            if (NervInput.TablePressed) UI.PeriodicTablePanel.Instance?.Toggle();
            if (NervInput.ViewPressed) NervView.Current = (NervView.Mode)(((int)NervView.Current + 1) % 3);
            if (NervInput.EscapePressed)
            {
                // Esc сначала закрывает панели, потом сбрасывает инструмент.
                if (UI.PeriodicTablePanel.Instance != null && UI.PeriodicTablePanel.Instance.IsOpen) UI.PeriodicTablePanel.Instance.Close();
                else if (UI.MoleculeCardPanel.Instance != null && UI.MoleculeCardPanel.Instance.IsOpen) UI.MoleculeCardPanel.Instance.Close();
                else Tool = NervTool.Hand;
            }
            if (NervInput.SpacePressed) app.Paused = !app.Paused;

            Ray ray = cam.ScreenPointToRay(NervInput.PointerPosition);
            bool overUi = UI.NervUI.PointerOverUi;
            int hit = overUi ? -1 : Pick(app, ray);
            _view.HoverIndex = (Tool == NervTool.Hand || Tool == NervTool.Erase) ? hit : -1;

            bool click = NervInput.LeftPressed && !overUi && !NervInput.AltHeld;
            if (click)
            {
                switch (Tool)
                {
                    case NervTool.Hand:
                        if (hit >= 0)
                        {
                            _grabbed = app.Snapshot.Ids[hit];
                            _pressAtom = _grabbed;
                            _pressPos = NervInput.PointerPosition;
                            _pressTime = Time.unscaledTime;
                            Vector3 p = app.AtomWorld(hit);
                            _grabDepth = Vector3.Dot(p - cam.transform.position, cam.transform.forward);
                            _grabRecord = NervHistory.Instance?.Record();
                            app.Submit(new Grab(_grabbed, NervApp.ToSim(p)));
                        }
                        break;
                    case NervTool.Place:
                        NervHistory.Instance?.Record();
                        app.Submit(new AddAtom(Element, NervApp.ToSim(PlanePoint(cam, ray, app))));
                        break;
                    case NervTool.Spark:
                        NervHistory.Instance?.Record();
                        app.Submit(new Spark(NervApp.ToSim(hit >= 0 ? app.AtomWorld(hit) : PlanePoint(cam, ray, app)), radius: 4, kelvin: 6000));
                        NervEffects.Instance?.SparkFlash(hit >= 0 ? app.AtomWorld(hit) : PlanePoint(cam, ray, app));
                        break;
                    case NervTool.Erase:
                        if (hit >= 0)
                        {
                            NervHistory.Instance?.Record();
                            app.Submit(new RemoveAtom(app.Snapshot.Ids[hit]));
                        }
                        break;
                }
            }

            // Нагрев и холод действуют, пока кнопка зажата: зона продлевается каждый кадр.
            if ((Tool == NervTool.Heat || Tool == NervTool.Cold) && NervInput.LeftHeld && !overUi && !NervInput.AltHeld)
            {
                Vector3 at = hit >= 0 ? app.AtomWorld(hit) : PlanePoint(cam, ray, app);
                bool heat = Tool == NervTool.Heat;
                if (NervInput.LeftPressed) NervHistory.Instance?.Record(); // один шаг истории на всё нажатие
                app.Submit(new LocalTemperature(NervApp.ToSim(at), HeatRadius, heat ? 1500 : 20, 200));
                if (Time.unscaledTime - _lastThermalFx > 0.18f)
                {
                    _lastThermalFx = Time.unscaledTime;
                    NervEffects.Instance?.ThermalPulse(at, heat);
                }
            }

            UpdateGrab(app, cam, ray);
        }

        private void UpdateGrab(NervApp app, Camera cam, Ray ray)
        {
            int idx = IndexOf(app.Snapshot, _grabbed);
            if (_grabbed.IsNone || idx < 0)
            {
                _grabbed = AtomId.None;
                _view.GrabbedIndex = -1;
                _spring.positionCount = 0;
                return;
            }

            // Цель — на плоскости, параллельной экрану, на глубине схваченного атома.
            Vector3 target = ray.origin + ray.direction * (_grabDepth / Mathf.Max(0.05f, Vector3.Dot(ray.direction, cam.transform.forward)));
            if (NervInput.LeftHeld)
            {
                app.Submit(new MoveGrab(NervApp.ToSim(target)));
                _view.GrabbedIndex = idx;
                _spring.positionCount = 2;
                _spring.SetPosition(0, app.AtomWorld(idx));
                _spring.SetPosition(1, target);
            }
            else
            {
                if (_pressAtom == _grabbed && (NervInput.PointerPosition - _pressPos).sqrMagnitude < 36f &&
                    Time.unscaledTime - _pressTime < 0.35f)
                {
                    // Это был щелчок, а не перетаскивание: открываем карточку, в истории шаг не нужен.
                    UI.MoleculeCardPanel.Instance?.Open(_grabbed);
                    NervHistory.Instance?.Discard(_grabRecord);
                }
                _grabRecord = null;
                _pressAtom = AtomId.None;
                app.Submit(new Release());
                _grabbed = AtomId.None;
                _view.GrabbedIndex = -1;
                _spring.positionCount = 0;
            }
        }

        /// <summary>Точка на плоскости через центр сцены, перпендикулярной взгляду.</summary>
        private static Vector3 PlanePoint(Camera cam, Ray ray, NervApp app)
        {
            Vector3 center = cam.GetComponent<NervCameraController>()?.Target ?? Vector3.zero;
            var plane = new Plane(-cam.transform.forward, center);
            return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : center;
        }

        /// <summary>Ближайший атом, в который попадает луч (без коллайдеров — по снимку).</summary>
        public static int Pick(NervApp app, Ray ray)
        {
            WorldSnapshot snap = app.Snapshot;
            int best = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < snap.AtomCount; i++)
            {
                Vector3 c = app.AtomWorld(i);
                float r = MoleculeView.VisualRadius(ElementTable.Get(snap.AtomicNumbers[i])) * NervApp.MetersPerAngstrom * 1.3f;
                Vector3 oc = ray.origin - c;
                float b = Vector3.Dot(oc, ray.direction);
                float cc = oc.sqrMagnitude - r * r;
                float disc = b * b - cc;
                if (disc < 0) continue;
                float t = -b - Mathf.Sqrt(disc);
                if (t > 0 && t < bestT)
                {
                    bestT = t;
                    best = i;
                }
            }
            return best;
        }

        public static int IndexOf(WorldSnapshot snap, AtomId id)
        {
            if (id.IsNone) return -1;
            for (int i = 0; i < snap.AtomCount; i++) if (snap.Ids[i] == id) return i;
            return -1;
        }
    }
}
