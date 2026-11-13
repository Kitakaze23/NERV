using System;
using System.Collections.Generic;
using Nerv.Core;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Корень клиента: загружает химические данные, создаёт симуляцию в фоновом потоке
    /// и каждый кадр забирает снимок мира и события. Остальные компоненты читают их отсюда.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class NervApp : MonoBehaviour
    {
        public static NervApp Instance { get; private set; }

        /// <summary>Масштаб сцены: 1 Å = 0,1 м (молекула воды ~ 20 см).</summary>
        public const float MetersPerAngstrom = 0.1f;

        public SimulationHost Host { get; private set; }
        public ChemistryData Data { get; private set; }
        public string DataError { get; private set; }

        /// <summary>Снимок мира на этот кадр.</summary>
        public readonly WorldSnapshot Snapshot = new WorldSnapshot();
        /// <summary>События, пришедшие в этом кадре.</summary>
        public readonly List<SimEvent> Events = new List<SimEvent>();
        /// <summary>Реакции, сработавшие с прошлого кадра.</summary>
        public readonly List<ReactionRecord> Reactions = new List<ReactionRecord>();

        // Зеркало настроек для интерфейса (сами настройки меняются командами).
        public double Temperature { get; private set; } = 298;
        public Medium Medium { get; private set; } = Medium.Vacuum;
        public double TimeScale { get; private set; } = 1;
        public float BoxHalfSize { get; private set; } = 12f;
        public string SceneName { get; private set; } = "";
        /// <summary>Режим «Лаборатория»: графики и измерения поверх той же песочницы.</summary>
        public bool LabMode { get; set; }

        private void Awake()
        {
            Instance = this;
            Data = LoadData();
            NervEnvironment.Setup();

            gameObject.AddComponent<MoleculeView>();
            gameObject.AddComponent<NervInteraction>();
            gameObject.AddComponent<NervEffects>();
            gameObject.AddComponent<NervDirector>();
            gameObject.AddComponent<NervHistory>();
            gameObject.AddComponent<UI.PeriodicTablePanel>();
            gameObject.AddComponent<UI.MoleculeCardPanel>();
            gameObject.AddComponent<UI.LabPanel>();
            gameObject.AddComponent<NervHud>();
            NervEnvironment.MainCamera.gameObject.AddComponent<NervCameraController>();

            LoadScene(NervScenes.All[1]);
        }

        private void Update()
        {
            if (Host == null) return;
            if (!Host.IsRunning) Host.Advance(Time.deltaTime);
            Host.CopySnapshot(Snapshot);
            Events.Clear();
            Host.DrainEvents(Events);
            Reactions.Clear();
            Host.DrainReactions(Reactions);
        }

        private void OnDisable() => StopHost();
        private void OnApplicationQuit() => StopHost();

        private void StopHost()
        {
            try { Host?.Stop(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ================= Сцены =================

        public void LoadScene(NervScenes.Preset preset)
        {
            StopHost();
            BoxHalfSize = preset.BoxHalfSize;
            Medium = preset.Medium;
            var settings = new SimulationSettings
            {
                TargetTemperature = Temperature,
                BoxHalfSize = preset.BoxHalfSize,
                Medium = preset.Medium,
                TimeScale = TimeScale
            };
            var sim = new Simulation(settings, (ulong)UnityEngine.Random.Range(1, int.MaxValue), Data);
            preset.Build(sim);

            Host = new SimulationHost(sim);
            SceneName = preset.Name;
            NervHistory.Instance?.Clear();
            // Фоновый поток там, где он есть (на WebGL — ручной режим через Advance в Update).
            if (Application.platform != RuntimePlatform.WebGLPlayer) Host.Start();
        }

        // ================= Команды =================

        public void Submit(Command command) => Host?.Submit(command);

        public void SetTemperature(double kelvin)
        {
            Temperature = kelvin;
            Submit(new Nerv.Core.Sim.SetTemperature(kelvin));
        }

        public void SetMedium(Medium medium)
        {
            Medium = medium;
            Submit(new Nerv.Core.Sim.SetMedium(medium));
        }

        public void SetTimeScale(double scale)
        {
            TimeScale = scale;
            Submit(new Nerv.Core.Sim.SetTimeScale(scale));
        }

        public bool Paused
        {
            get => Host != null && Host.Paused;
            set
            {
                if (Host != null) Host.Paused = value;
            }
        }

        public void ClearAtoms()
        {
            if (Snapshot.AtomCount > 0) NervHistory.Instance?.Record();
            for (int i = 0; i < Snapshot.AtomCount; i++) Submit(new RemoveAtom(Snapshot.Ids[i]));
        }

        // ================= Координаты =================

        public static Vector3 ToWorld(Vec3 a) =>
            new Vector3((float)a.X, (float)a.Y, (float)a.Z) * MetersPerAngstrom;

        public Vector3 AtomWorld(int index) =>
            new Vector3(Snapshot.Positions[3 * index], Snapshot.Positions[3 * index + 1], Snapshot.Positions[3 * index + 2]) * MetersPerAngstrom;

        public static Vec3 ToSim(Vector3 w) =>
            new Vec3(w.x / MetersPerAngstrom, w.y / MetersPerAngstrom, w.z / MetersPerAngstrom);

        // ================= Данные =================

        private ChemistryData LoadData()
        {
            try
            {
                TextAsset c = Resources.Load<TextAsset>("NervData/compounds");
                TextAsset i = Resources.Load<TextAsset>("NervData/ions");
                TextAsset r = Resources.Load<TextAsset>("NervData/reactions");
                if (c == null || i == null || r == null)
                {
                    DataError = "Не найдены файлы в Resources/NervData — меню NERV → «Обновить химические данные».";
                    Debug.LogError(DataError);
                    return null;
                }
                return ChemistryData.Load(c.text, i.text, r.text);
            }
            catch (Exception e)
            {
                DataError = "Ошибка химических данных: " + e.Message;
                Debug.LogException(e);
                return null;
            }
        }
    }

    /// <summary>Создаёт NERV в любой сцене при запуске Play.</summary>
    internal static class NervBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (UnityEngine.Object.FindFirstObjectByType<NervApp>() == null)
                new GameObject("NERV").AddComponent<NervApp>();
        }
    }
}
