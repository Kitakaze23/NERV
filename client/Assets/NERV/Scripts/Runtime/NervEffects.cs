using System.Collections.Generic;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Sim;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Эффекты событий (первая версия): вспышка света и расширяющийся светящийся шар в центре реакции —
    /// янтарный для экзотермических, голубой для эндотермических; голубая вспышка искры.
    /// Все эффекты запускаются событиями ядра, а не опросом мира.
    /// </summary>
    public sealed class NervEffects : MonoBehaviour
    {
        public static NervEffects Instance { get; private set; }

        private sealed class Flash
        {
            public GameObject Go;
            public Light Light;
            public Transform Ball;
            public Material Material;
            public Color Color;
            public bool Halo;
            public float Age, Life, MinRadius, MaxRadius, Intensity, HaloIntensity;
        }

        // Следы атомов реакции (по карте атомов): видно, какой атом куда перешёл.
        private sealed class Trail
        {
            public LineRenderer Line;
            public AtomId Atom;
            public float Age;
            public Color Color;
            public readonly Vector3[] Points = new Vector3[TrailPoints];
            public int Count;
        }

        private const int TrailPoints = 32;
        private const float TrailLife = 1.1f;
        private readonly List<Trail> _trails = new List<Trail>();
        private readonly Stack<Trail> _trailPool = new Stack<Trail>();
        private readonly Dictionary<AtomId, int> _index = new Dictionary<AtomId, int>();
        private Material _trailMaterial;

        private readonly List<Flash> _active = new List<Flash>();
        private readonly Stack<Flash> _pool = new Stack<Flash>();
        private Mesh _sphere;

        private void Awake()
        {
            Instance = this;
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            // Sprites/Default есть в любом конвейере, понимает цвета вершин; HDR-оттенок отдаёт яркость Bloom.
            _trailMaterial = new Material(Shader.Find("Sprites/Default"));
            _trailMaterial.color = new Color(2.2f, 2.2f, 2.2f, 1f);
        }

        private void Update()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;

            foreach (ReactionRecord r in app.Reactions)
            {
                double dh = r.Reaction.DeltaHKj;
                Color c = dh < 0 ? NervEnvironment.Heat : NervEnvironment.Cold;
                float strength = Mathf.Clamp(Mathf.Abs((float)dh) / 300f, 0.3f, 1.5f);
                Spawn(NervApp.ToWorld(r.Position), c, 0.15f + 0.25f * strength, 0.5f + 0.4f * strength, 4f + 6f * strength);
                foreach (AtomId id in r.AtomMap.Values)
                    if (_trails.Count < NervQuality.MaxTrails) StartTrail(id, c);
            }
            foreach (SimEvent e in app.Events)
                if (e.Type == SimEventType.MoleculeIdentified) IdentifiedPulse(app, e);
            UpdateTrails(app);

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Flash f = _active[i];
                f.Age += Time.deltaTime;
                float t = f.Age / f.Life;
                if (t >= 1f)
                {
                    f.Go.SetActive(false);
                    _active.RemoveAt(i);
                    _pool.Push(f);
                    continue;
                }
                float fade = (1f - t) * (1f - t);
                f.Light.intensity = f.Intensity * fade;
                f.Ball.localScale = Vector3.one * (Mathf.Lerp(f.MinRadius, f.MaxRadius, Mathf.Sqrt(t)) * 2f);
                if (f.Halo)
                {
                    // Расходящаяся светящаяся «волна»: полое кольцо по Френелю.
                    f.Material.SetColor("_Color", f.Color);
                    f.Material.SetFloat("_Intensity", f.HaloIntensity * fade);
                }
                else
                {
                    f.Material.SetColor("_BaseColor", f.Color * (0.25f * fade));
                    f.Material.SetColor("_EmissionColor", f.Color * (2.5f * fade));
                }
            }
        }

        private readonly HashSet<string> _seenNames = new HashSet<string>();
        private string _scene;

        /// <summary>
        /// Кольцо-пульс вокруг только что опознанной молекулы (концепт 7.2). Вещество, впервые
        /// появившееся в сцене, — заметный пульс; повторное — лёгкий.
        /// </summary>
        private void IdentifiedPulse(NervApp app, SimEvent e)
        {
            if (_scene != app.SceneName)
            {
                _scene = app.SceneName;
                _seenNames.Clear();
            }
            if (e.Text == null || _active.Count > 40) return;
            WorldSnapshot snap = app.Snapshot;
            foreach (Molecule m in snap.Molecules)
            {
                if (m.Atoms.Count == 0 || !m.Atoms.Contains(e.A)) continue;
                if (m.Atoms.Count < 2) return; // одиночные атомы и радикалы-атомы не «празднуем»
                Vector3 c = Vector3.zero;
                int n = 0;
                var pos = new List<Vector3>(m.Atoms.Count);
                for (int i = 0; i < snap.AtomCount; i++)
                    if (m.Atoms.Contains(snap.Ids[i]))
                    {
                        Vector3 p = app.AtomWorld(i);
                        pos.Add(p);
                        c += p;
                        n++;
                    }
                if (n == 0) return;
                c /= n;
                float r = 0f;
                foreach (Vector3 p in pos) r = Mathf.Max(r, (p - c).magnitude);
                r += 0.06f; // ≈ радиус атома в метрах сцены
                bool first = _seenNames.Add(e.Text);
                Flash f = Spawn(c, NervEnvironment.Energy, r * (first ? 2.2f : 1.6f), first ? 0.9f : 0.6f, 0f);
                f.MinRadius = r;
                f.HaloIntensity = first ? 2.5f : 1.2f;
                return;
            }
        }

        private void StartTrail(AtomId atom, Color color)
        {
            // Один след на атом: если он уже есть — продлеваем.
            foreach (Trail t in _trails)
                if (t.Atom == atom)
                {
                    t.Age = 0;
                    t.Color = color;
                    return;
                }
            Trail tr = _trailPool.Count > 0 ? _trailPool.Pop() : CreateTrail();
            tr.Atom = atom;
            tr.Age = 0;
            tr.Count = 0;
            tr.Color = color;
            tr.Line.gameObject.SetActive(true);
            _trails.Add(tr);
        }

        private void UpdateTrails(NervApp app)
        {
            if (_trails.Count == 0) return;
            WorldSnapshot snap = app.Snapshot;
            _index.Clear();
            for (int i = 0; i < snap.AtomCount; i++) _index[snap.Ids[i]] = i;

            for (int k = _trails.Count - 1; k >= 0; k--)
            {
                Trail t = _trails[k];
                t.Age += Time.unscaledDeltaTime;
                bool alive = _index.TryGetValue(t.Atom, out int idx);
                if (t.Age > TrailLife || !alive)
                {
                    t.Line.gameObject.SetActive(false);
                    _trails.RemoveAt(k);
                    _trailPool.Push(t);
                    continue;
                }
                Vector3 p = app.AtomWorld(idx);
                // Новая точка, только если атом сдвинулся (на паузе след не «растёт» на месте).
                if (t.Count == 0 || (p - t.Points[t.Count - 1]).sqrMagnitude > 1e-6f)
                {
                    if (t.Count == TrailPoints)
                    {
                        System.Array.Copy(t.Points, 1, t.Points, 0, TrailPoints - 1);
                        t.Count--;
                    }
                    t.Points[t.Count++] = p;
                }
                t.Line.positionCount = t.Count;
                for (int q = 0; q < t.Count; q++) t.Line.SetPosition(q, t.Points[q]);
                float fade = 1f - t.Age / TrailLife;
                Color head = t.Color;
                head.a = fade;
                Color tail = t.Color;
                tail.a = 0f;
                t.Line.startColor = tail;
                t.Line.endColor = head;
            }
        }

        private Trail CreateTrail()
        {
            var go = new GameObject("Trail");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _trailMaterial;
            lr.useWorldSpace = true;
            lr.widthCurve = AnimationCurve.Linear(0f, 0f, 1f, 0.012f);
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return new Trail { Line = lr };
        }

        /// <summary>Мягкая волна под курсором инструментов «Нагрев» (тёплая) и «Холод» (голубая).</summary>
        public void ThermalPulse(Vector3 at, bool heat)
        {
            if (_active.Count > 40) return;
            float r = NervInteraction.HeatRadius * NervApp.MetersPerAngstrom;
            Flash f = Spawn(at, heat ? NervEnvironment.Heat : NervEnvironment.Cold, r, 0.5f, 0f);
            f.MinRadius = r * 0.4f;
            f.HaloIntensity = 1.2f;
        }

        public void SparkFlash(Vector3 at) => Spawn(at, NervEnvironment.Energy, 0.35f, 0.35f, 8f);

        private Flash Spawn(Vector3 at, Color color, float radius, float life, float intensity)
        {
            Flash f = _pool.Count > 0 ? _pool.Pop() : Create();
            f.Go.transform.position = at;
            f.Go.SetActive(true);
            f.Color = color;
            f.Age = 0;
            f.Life = life;
            f.MinRadius = 0f;
            f.MaxRadius = radius;
            f.Intensity = intensity;
            f.HaloIntensity = 3.5f;
            // Динамический свет — только на высоком качестве.
            f.Light.enabled = intensity > 0f && NervQuality.High;
            f.Light.color = color;
            f.Light.range = radius * 4f;
            _active.Add(f);
            return f;
        }

        private Flash Create()
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(transform, false);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;

            var ball = new GameObject("Ball");
            ball.transform.SetParent(go.transform, false);
            ball.AddComponent<MeshFilter>().sharedMesh = _sphere;
            MeshRenderer mr = ball.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Material m = MoleculeView.MakeHalo(Color.black, 0f, 0f);
            bool halo = m != null;
            if (halo) m.SetFloat("_Hollow", 3f);
            else
            {
                m = MoleculeView.MakeMaterial(Color.black, Color.black, 0.5f);
                MoleculeView.MakeAdditive(m);
            }
            mr.sharedMaterial = m;

            return new Flash { Go = go, Light = l, Ball = ball.transform, Material = m, Halo = halo };
        }
    }
}
