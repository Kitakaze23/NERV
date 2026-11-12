using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nerv.Client
{
    /// <summary>
    /// Окружение «пустота лаборатории»: тёмный фон, свет, свечение (bloom) для неоновых событий.
    /// </summary>
    public static class NervEnvironment
    {
        public static readonly Color Background = new Color32(0x10, 0x18, 0x2C, 0xFF); // после тонмаппинга ≈ #0A0F1C
        public static readonly Color Energy = new Color32(0x5C, 0xE1, 0xE6, 0xFF);
        public static readonly Color Heat = new Color32(0xFF, 0xB5, 0x47, 0xFF);
        public static readonly Color Cold = new Color32(0x7F, 0xB8, 0xFF, 0xFF);

        public static Camera MainCamera { get; private set; }

        public static void Setup()
        {
            MainCamera = Camera.main;
            if (MainCamera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                MainCamera = go.AddComponent<Camera>();
            }
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = Background;
            MainCamera.nearClipPlane = 0.01f;
            MainCamera.farClipPlane = 100f;
            MainCamera.fieldOfView = 45f;

            UniversalAdditionalCameraData urp = MainCamera.GetUniversalAdditionalCameraData();
            if (urp != null) urp.renderPostProcessing = true;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.33f, 0.45f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.18f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.07f, 0.1f);

            // Свет: один направленный «ключевой» + мягкий контровой.
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.enabled = false;
            CreateLight("Key Light", new Vector3(40, -30, 0), new Color(1f, 0.97f, 0.92f), 1.2f, true);
            CreateLight("Rim Light", new Vector3(-20, 150, 0), new Color(0.45f, 0.8f, 1f), 0.5f, false);

            CreatePostProcessing();
        }

        private static void CreateLight(string name, Vector3 euler, Color color, float intensity, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.rotation = Quaternion.Euler(euler);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        private static void CreatePostProcessing()
        {
            var go = new GameObject("NERV Post Processing");
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.7f);

            Tonemapping tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.25f);

            volume.profile = profile;
        }
    }
}
