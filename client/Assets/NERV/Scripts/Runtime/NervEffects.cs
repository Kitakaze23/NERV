using System.Collections.Generic;
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
            public float Age, Life, MaxRadius, Intensity;
        }

        private readonly List<Flash> _active = new List<Flash>();
        private readonly Stack<Flash> _pool = new Stack<Flash>();
        private Mesh _sphere;

        private void Awake()
        {
            Instance = this;
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
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
            }

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
                f.Ball.localScale = Vector3.one * (f.MaxRadius * 2f * Mathf.Sqrt(t));
                f.Material.SetColor("_BaseColor", f.Color * (0.25f * fade));
                f.Material.SetColor("_EmissionColor", f.Color * (2.5f * fade));
            }
        }

        public void SparkFlash(Vector3 at) => Spawn(at, NervEnvironment.Energy, 0.35f, 0.35f, 8f);

        private void Spawn(Vector3 at, Color color, float radius, float life, float intensity)
        {
            Flash f = _pool.Count > 0 ? _pool.Pop() : Create();
            f.Go.transform.position = at;
            f.Go.SetActive(true);
            f.Color = color;
            f.Age = 0;
            f.Life = life;
            f.MaxRadius = radius;
            f.Intensity = intensity;
            f.Light.color = color;
            f.Light.range = radius * 4f;
            _active.Add(f);
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
            Material m = MoleculeView.MakeMaterial(Color.black, Color.black, 0.5f);
            MoleculeView.MakeAdditive(m);
            mr.sharedMaterial = m;

            return new Flash { Go = go, Light = l, Ball = ball.transform, Material = m };
        }
    }
}
