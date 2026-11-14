using System.Collections.Generic;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nerv.Client
{
    /// <summary>
    /// Рендер снимка мира через GPU-инстансинг (без GameObject на атом):
    /// атомы — сферы цвета CPK, связи — полу-цилиндры цветов своих атомов (кратные — несколько нитей),
    /// свободная валентность — светящиеся точки, направленные «подальше» от соседей (как в VSEPR),
    /// коробка сцены — тонкий каркас.
    /// </summary>
    public sealed class MoleculeView : MonoBehaviour
    {
        private const int Batch = 1023;

        private Mesh _sphere, _cylinder;
        private readonly Dictionary<int, Material> _elementMaterials = new Dictionary<int, Material>();
        private readonly Dictionary<int, List<Matrix4x4>> _atoms = new Dictionary<int, List<Matrix4x4>>();
        private readonly Dictionary<int, List<Matrix4x4>> _bonds = new Dictionary<int, List<Matrix4x4>>();
        private readonly List<Matrix4x4> _dots = new List<Matrix4x4>();
        // Слои «Лаборатории»: неподелённые пары (бледные парные точки) и потенциал (ореол по частичному заряду).
        private readonly List<Matrix4x4> _pairs = new List<Matrix4x4>();
        private readonly List<Matrix4x4>[] _potNeg = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        private readonly List<Matrix4x4>[] _potPos = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        private readonly Material[] _potNegMat = new Material[3], _potPosMat = new Material[3];
        private Material _pairMaterial;
        private float[] _q = new float[64];
        private readonly List<Matrix4x4> _highlight = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _cations = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _anions = new List<Matrix4x4>();
        /// <summary>Неустойчивые частицы: янтарный мерцающий ореол — «скоро распадётся».</summary>
        private readonly List<Matrix4x4> _unstable = new List<Matrix4x4>();
        private readonly HashSet<AtomId> _unstableAtoms = new HashSet<AtomId>();
        private Material _unstableMaterial;

        // Свечение раскалённых атомов в пламени: три ступени «чёрного тела» (тёмно-оранжевый → жёлто-белый).
        private static readonly float[] HeatSteps = { 900f, 1600f, 2500f };
        private readonly List<Matrix4x4>[] _hot = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        private readonly Material[] _hotMaterials = new Material[3];
        // Иней: холодные атомы (ниже FrostBelow, K) покрываются бледно-голубым свечением.
        private const float FrostBelow = 140f;
        private readonly List<Matrix4x4> _frost = new List<Matrix4x4>();
        private Material _frostMaterial;
        private Dictionary<AtomId, float> _heat = new Dictionary<AtomId, float>(), _heatNext = new Dictionary<AtomId, float>();
        private Material _dotMaterial, _highlightMaterial, _lineMaterial, _cationMaterial, _anionMaterial;
        private LineRenderer _box;
        private float _boxDrawn = -1;

        private List<int>[] _neighbors = new List<int>[0];
        private static readonly Vector3[] Directions = BuildDirections();
        private readonly List<Vector3> _used = new List<Vector3>(8);

        /// <summary>Атом под курсором (индекс снимка) — для подсветки.</summary>
        public int HoverIndex = -1;
        /// <summary>Атомы молекулы, открытой в карточке вещества, — для подсветки.</summary>
        public readonly HashSet<AtomId> Selected = new HashSet<AtomId>();
        /// <summary>Схваченный атом — для подсветки.</summary>
        public int GrabbedIndex = -1;

        /// <summary>Радиус шара атома, Å — зависит от режима отображения (<see cref="NervView"/>).</summary>
        public static float VisualRadius(Element e) => NervView.Radius(e);

        private void Awake()
        {
            _sphere = PrimitiveMesh(PrimitiveType.Sphere);
            _cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            _dotMaterial = MakeMaterial(new Color(0.85f, 1f, 1f), NervEnvironment.Energy * 3f, 0.9f);
            _pairMaterial = MakeMaterial(new Color(0.55f, 0.62f, 0.75f), new Color(0.08f, 0.1f, 0.14f), 0.6f);
            for (int k = 0; k < 3; k++)
            {
                float a = 0.45f + 0.35f * k;
                _potNegMat[k] = MakeHalo(new Color(1f, 0.3f, 0.3f), a, 0.04f);
                _potPosMat[k] = MakeHalo(new Color(0.35f, 0.55f, 1f), a, 0.04f);
            }
            _highlightMaterial = MakeHalo(new Color(0.36f, 0.88f, 0.9f), 1.6f, 0.05f);
            if (_highlightMaterial == null)
            {
                _highlightMaterial = MakeMaterial(new Color(0.36f, 0.88f, 0.9f, 0.25f), NervEnvironment.Energy * 0.6f, 0.9f);
                MakeAdditive(_highlightMaterial);
            }
            // Ореолы ионов: катион — тёплый, анион — холодный (как «плюс» и «минус» на схемах).
            _cationMaterial = MakeHalo(new Color(1f, 0.45f, 0.25f), 0.9f, 0.18f);
            _anionMaterial = MakeHalo(new Color(0.3f, 0.6f, 1f), 0.9f, 0.18f);
            _unstableMaterial = MakeHalo(new Color(1f, 0.62f, 0.18f), 1.1f, 0.55f);
            if (_unstableMaterial != null) _unstableMaterial.SetFloat("_PulseSpeed", 11f);
            _hotMaterials[0] = MakeHalo(new Color(1f, 0.28f, 0.05f), 0.7f, 0.25f);
            _hotMaterials[1] = MakeHalo(new Color(1f, 0.5f, 0.12f), 1.1f, 0.25f);
            _hotMaterials[2] = MakeHalo(new Color(1f, 0.85f, 0.55f), 1.6f, 0.25f);
            _frostMaterial = MakeHalo(new Color(0.7f, 0.88f, 1f), 0.75f, 0.05f);
            if (_frostMaterial != null) _frostMaterial.SetFloat("_Hollow", 2.5f);

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _lineMaterial = new Material(unlit);
            _lineMaterial.SetColor("_BaseColor", new Color(0.36f, 0.88f, 0.9f, 1f) * 0.35f);
            _lineMaterial.color = new Color(0.36f, 0.88f, 0.9f, 1f) * 0.35f;

            var boxGo = new GameObject("Scene Box");
            boxGo.transform.SetParent(transform, false);
            _box = boxGo.AddComponent<LineRenderer>();
            _box.material = _lineMaterial;
            _box.widthMultiplier = 0.004f;
            _box.useWorldSpace = true;
            _box.shadowCastingMode = ShadowCastingMode.Off;
            _box.receiveShadows = false;
        }

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return mesh;
        }

        public static Material MakeMaterial(Color baseColor, Color emission, float smoothness)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(lit) { enableInstancing = true };
            m.SetColor("_BaseColor", baseColor);
            m.color = baseColor;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        /// <summary>Аддитивная прозрачность URP Lit: светящийся ореол поверх атома.</summary>
        public static void MakeAdditive(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Shader _atomShader, _haloShader;
        private static bool _shadersLoaded;

        private static void LoadShaders()
        {
            if (_shadersLoaded) return;
            _shadersLoaded = true;
            _atomShader = Resources.Load<Shader>("Shaders/NervAtom");
            _haloShader = Resources.Load<Shader>("Shaders/NervHalo");
            if (_atomShader != null && !_atomShader.isSupported) _atomShader = null;
            if (_haloShader != null && !_haloShader.isSupported) _haloShader = null;
        }

        /// <summary>«Стеклянный» материал атома с неоновым ободком (NERV/Atom); при отсутствии шейдера — URP Lit.</summary>
        public static Material MakeAtomMaterial(Color c)
        {
            LoadShaders();
            if (_atomShader == null) return MakeMaterial(c, c * 0.18f, 0.82f);
            var m = new Material(_atomShader) { enableInstancing = true };
            m.SetColor("_BaseColor", c * 0.85f);
            // Ободок: насыщенный цвет элемента, чуть разбавленный белым; HDR-яркость отдаётся Bloom.
            Color rim = Color.Lerp(c, Color.white, 0.25f);
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            m.SetColor("_RimColor", rim);
            m.SetFloat("_RimStrength", Mathf.Lerp(2.2f, 1.1f, lum)); // светлым элементам — мягче
            m.SetFloat("_RimPower", 2.6f);
            m.SetFloat("_Gloss", 90f);
            m.SetFloat("_Core", 0.3f);
            return m;
        }

        /// <summary>Аддитивный ореол (NERV/Halo). Null, если шейдер недоступен.</summary>
        public static Material MakeHalo(Color c, float intensity, float pulse)
        {
            LoadShaders();
            if (_haloShader == null) return null;
            var m = new Material(_haloShader) { enableInstancing = true };
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Pulse", pulse);
            return m;
        }

        private Material ElementMaterial(int z)
        {
            int key = z * 4 + (int)NervView.Current;
            if (_elementMaterials.TryGetValue(key, out Material m)) return m;
            Color c = ElementColor(ElementTable.Get(z));
            // «Эффект» — стекло с неоновым ободком; «Шарики» и CPK — матовый пластик, как у учебной модели.
            m = NervView.Effects ? MakeAtomMaterial(c) : MakeMaterial(c, c * 0.04f, 0.35f);
            _elementMaterials[key] = m;
            return m;
        }

        public static Color ElementColor(Element e)
        {
            uint rgb = e.Color;
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
        }

        private void LateUpdate()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;
            WorldSnapshot snap = app.Snapshot;
            float s = NervApp.MetersPerAngstrom;

            foreach (List<Matrix4x4> l in _atoms.Values) l.Clear();
            foreach (List<Matrix4x4> l in _bonds.Values) l.Clear();
            _dots.Clear();
            _pairs.Clear();
            for (int k = 0; k < 3; k++) { _potNeg[k].Clear(); _potPos[k].Clear(); }
            bool lonePairs = NervLayers.On(NervLayers.Layer.LonePairs), potential = NervLayers.On(NervLayers.Layer.Potential);
            if (potential)
            {
                if (_q.Length < snap.AtomCount) _q = new float[Mathf.Max(snap.AtomCount, _q.Length * 2)];
                NervLayers.PartialCharges(snap, _q);
            }
            _highlight.Clear();
            _cations.Clear();
            _anions.Clear();
            _unstable.Clear();
            _unstableAtoms.Clear();
            if (NervView.Annotations)
                foreach (Molecule m in snap.Molecules)
                    if (m.Unstable)
                        foreach (AtomId id in m.Atoms) _unstableAtoms.Add(id);
            foreach (List<Matrix4x4> l in _hot) l.Clear();
            _frost.Clear();
            // Сглаживание температуры атома (~0,15 с): мгновенная скорость отдельного атома слишком шумная.
            float heatK = 1f - Mathf.Exp(-Time.deltaTime / 0.15f);
            _heatNext.Clear();

            int n = snap.AtomCount;
            if (_neighbors.Length < n)
            {
                _neighbors = new List<int>[Mathf.Max(n, _neighbors.Length * 2)];
                for (int i = 0; i < _neighbors.Length; i++) _neighbors[i] = new List<int>(4);
            }
            for (int i = 0; i < n; i++) _neighbors[i].Clear();
            foreach (BondView b in snap.Bonds)
            {
                _neighbors[b.IndexA].Add(b.IndexB);
                _neighbors[b.IndexB].Add(b.IndexA);
            }

            // Атомы.
            for (int i = 0; i < n; i++)
            {
                Element e = ElementTable.Get(snap.AtomicNumbers[i]);
                Vector3 p = app.AtomWorld(i);
                float r = VisualRadius(e) * s;
                List(_atoms, e.Z).Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (2f * r)));
                if ((Selected.Count > 0 && Selected.Contains(snap.Ids[i])) ||
                    (UI.LabPanel.Instance != null && UI.LabPanel.Instance.MeasuredCount > 0 && UI.LabPanel.Instance.IsMeasured(snap.Ids[i])))
                    _highlight.Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (2f * r * 1.4f)));
                else if (i == HoverIndex || i == GrabbedIndex)
                    _highlight.Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (2f * r * (i == GrabbedIndex ? 1.7f : 1.5f))));
                float tRaw = snap.Temperatures[i];
                float heat = _heat.TryGetValue(snap.Ids[i], out float prevHeat) ? Mathf.Lerp(prevHeat, tRaw, heatK) : (float)snap.Temperature;
                _heatNext[snap.Ids[i]] = heat;
                // Одиночный медленный атом в газе может «остыть» и при комнатной температуре, поэтому иней —
                // только когда холодна и вся сцена.
                if (NervView.Effects && heat < FrostBelow && snap.Temperature < 220)
                    _frost.Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (2f * r * (1.35f + 0.4f * (1f - heat / FrostBelow)))));
                for (int step = HeatSteps.Length - 1; step >= 0 && NervView.Effects; step--)
                {
                    if (heat < HeatSteps[step]) continue;
                    _hot[step].Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (2f * r * (2.0f + 0.3f * step))));
                    break;
                }

                int q = snap.Charges[i];
                if (q != 0 && NervView.Annotations)
                {
                    // Ореол растёт с величиной заряда: Ca²⁺ заметнее Na⁺.
                    float k = 2f * r * (1.6f + 0.2f * Mathf.Min(Mathf.Abs(q) - 1, 2));
                    (q > 0 ? _cations : _anions).Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * k));
                }
                if ((snap.FreeValences[i] > 0 && NervView.Annotations) || (lonePairs && snap.LonePairs[i] > 0))
                    AddValenceDots(app, snap, i, p, r, NervView.Annotations ? snap.FreeValences[i] : 0, lonePairs ? snap.LonePairs[i] : 0);
                if (potential)
                {
                    float qa = Mathf.Abs(_q[i]);
                    if (qa >= 0.12f)
                    {
                        int level = qa < 0.3f ? 0 : qa < 0.6f ? 1 : 2;
                        (_q[i] < 0 ? _potNeg : _potPos)[level].Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (2f * r * 1.75f)));
                    }
                }
                if (_unstableAtoms.Count > 0 && _unstableAtoms.Contains(snap.Ids[i]))
                    _unstable.Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (2f * r * 1.55f)));
            }

            // Связи.
            Vector3 camFwd = NervEnvironment.MainCamera.transform.forward;
            foreach (BondView b in snap.Bonds)
            {
                if (!NervView.DrawBonds) break; // CPK: атомы перекрываются, связи не видны.
                Vector3 a = app.AtomWorld(b.IndexA), c = app.AtomWorld(b.IndexB);
                Vector3 d = c - a;
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                Vector3 dir = d / len;
                Vector3 side = Vector3.Cross(dir, camFwd);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(dir, Vector3.up);
                side.Normalize();

                int strands = Mathf.Clamp(b.Order, 1, 3);
                float progress = Mathf.Clamp01(b.Progress);
                float radius = (strands == 1 ? 0.085f : 0.055f) * s * (0.35f + 0.65f * progress);
                float spacing = 0.14f * s;
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
                int za = snap.AtomicNumbers[b.IndexA], zb = snap.AtomicNumbers[b.IndexB];
                Vector3 mid = (a + c) * 0.5f;
                for (int k = 0; k < strands; k++)
                {
                    Vector3 off = side * ((k - (strands - 1) * 0.5f) * spacing);
                    var halfScale = new Vector3(2f * radius, len * 0.25f, 2f * radius);
                    List(_bonds, za).Add(Matrix4x4.TRS((a + mid) * 0.5f + off, rot, halfScale));
                    List(_bonds, zb).Add(Matrix4x4.TRS((mid + c) * 0.5f + off, rot, halfScale));
                }
            }

            foreach (KeyValuePair<int, List<Matrix4x4>> kv in _atoms) Draw(_sphere, ElementMaterial(kv.Key), kv.Value, true);
            foreach (KeyValuePair<int, List<Matrix4x4>> kv in _bonds) Draw(_cylinder, ElementMaterial(kv.Key), kv.Value, true);
            Draw(_sphere, _dotMaterial, _dots, false);
            if (_pairMaterial != null) Draw(_sphere, _pairMaterial, _pairs, false);
            for (int k = 0; k < 3; k++)
            {
                if (_potNegMat[k] != null) Draw(_sphere, _potNegMat[k], _potNeg[k], false);
                if (_potPosMat[k] != null) Draw(_sphere, _potPosMat[k], _potPos[k], false);
            }
            Draw(_sphere, _highlightMaterial, _highlight, false);
            if (_cationMaterial != null) Draw(_sphere, _cationMaterial, _cations, false);
            if (_anionMaterial != null) Draw(_sphere, _anionMaterial, _anions, false);
            if (_unstableMaterial != null) Draw(_sphere, _unstableMaterial, _unstable, false);
            if (_frostMaterial != null) Draw(_sphere, _frostMaterial, _frost, false);
            for (int step = 0; step < _hot.Length; step++)
                if (_hotMaterials[step] != null) Draw(_sphere, _hotMaterials[step], _hot[step], false);
            (_heat, _heatNext) = (_heatNext, _heat);

            UpdateBox(app.BoxHalfSize * s);
        }

        /// <summary>
        /// Точки свободной валентности: жадно выбираем направления, максимально удалённые
        /// от соседей и друг от друга (приближение VSEPR: у свободного C — тетраэдр, у O в OH — «в сторону»).
        /// </summary>
        private void AddValenceDots(NervApp app, WorldSnapshot snap, int i, Vector3 p, float r, int freeCount, int lonePairs)
        {
            _used.Clear();
            foreach (int j in _neighbors[i]) _used.Add((app.AtomWorld(j) - p).normalized);
            int free = Mathf.Min(freeCount, 4);
            int total = Mathf.Min(free + lonePairs, 4);
            float dotR = 0.07f * NervApp.MetersPerAngstrom;
            for (int k = 0; k < total; k++)
            {
                Vector3 best = Directions[0];
                float bestScore = float.MinValue;
                foreach (Vector3 cand in Directions)
                {
                    float worst = float.MaxValue;
                    foreach (Vector3 u in _used) worst = Mathf.Min(worst, -Vector3.Dot(cand, u));
                    float score = _used.Count == 0 ? cand.y : worst;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = cand;
                    }
                }
                _used.Add(best);
                Vector3 at = p + best * (r + 0.1f * NervApp.MetersPerAngstrom);
                if (k < free)
                {
                    _dots.Add(Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * (2f * dotR)));
                    continue;
                }
                // Неподелённая пара — две бледные точки рядом, поперёк направления.
                Vector3 side = Vector3.Cross(best, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(best, Vector3.right);
                side = side.normalized * (0.075f * NervApp.MetersPerAngstrom);
                _pairs.Add(Matrix4x4.TRS(at + side, Quaternion.identity, Vector3.one * (1.6f * dotR)));
                _pairs.Add(Matrix4x4.TRS(at - side, Quaternion.identity, Vector3.one * (1.6f * dotR)));
            }
        }

        private static Vector3[] BuildDirections()
        {
            // Равномерные направления на сфере (спираль Фибоначчи).
            const int count = 64;
            var dirs = new Vector3[count];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int k = 0; k < count; k++)
            {
                float y = 1f - (k + 0.5f) * 2f / count;
                float rad = Mathf.Sqrt(1f - y * y);
                float th = golden * k;
                dirs[k] = new Vector3(Mathf.Cos(th) * rad, y, Mathf.Sin(th) * rad);
            }
            return dirs;
        }

        private static List<Matrix4x4> List(Dictionary<int, List<Matrix4x4>> d, int z)
        {
            if (!d.TryGetValue(z, out List<Matrix4x4> l)) d[z] = l = new List<Matrix4x4>(64);
            return l;
        }

        private static void Draw(Mesh mesh, Material material, List<Matrix4x4> matrices, bool shadows)
        {
            if (matrices.Count == 0) return;
            var rp = new RenderParams(material)
            {
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = shadows
            };
            for (int start = 0; start < matrices.Count; start += Batch)
                Graphics.RenderMeshInstanced(rp, mesh, 0, matrices, Mathf.Min(Batch, matrices.Count - start), start);
        }

        private void UpdateBox(float h)
        {
            if (Mathf.Approximately(h, _boxDrawn)) return;
            _boxDrawn = h;
            if (h <= 0)
            {
                _box.positionCount = 0;
                return;
            }
            Vector3[] pts =
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h), new Vector3(-h, -h, -h),
                new Vector3(-h, h, -h), new Vector3(h, h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h),
                new Vector3(h, h, h), new Vector3(h, -h, h), new Vector3(h, h, h),
                new Vector3(-h, h, h), new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h)
            };
            _box.positionCount = pts.Length;
            _box.SetPositions(pts);
        }
    }
}
