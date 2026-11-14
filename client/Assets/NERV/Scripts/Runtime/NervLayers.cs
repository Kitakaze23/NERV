using Nerv.Core.Data;
using Nerv.Core.Sim;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Слои «Лаборатории» (экран концепта «Молекула»): длины связей, углы, заряды δ±, потенциал и неподелённые пары.
    /// Работают только в режиме «Лаборатория»; выбор запоминается.
    /// Частичные заряды — оценка по разнице электроотрицательностей соседей (не квантовый расчёт):
    /// q ≈ 0,25 · Σ (χ соседа − χ атома) по связям + формальный заряд. Для воды это δ− на O и δ+ на H, C–H почти нейтральна.
    /// </summary>
    public static class NervLayers
    {
        public enum Layer { BondLengths, Angles, Charges, Potential, LonePairs }

        private static readonly bool[] State = new bool[5];
        private static bool _loaded;

        public static readonly Layer[] All = { Layer.BondLengths, Layer.Angles, Layer.Charges, Layer.Potential, Layer.LonePairs };

        public static string Name(Layer l) => l switch
        {
            Layer.BondLengths => "Длины связей",
            Layer.Angles => "Углы",
            Layer.Charges => "Заряды δ",
            Layer.Potential => "Потенциал",
            _ => "Неподелённые пары",
        };

        /// <summary>Слой включён и сейчас виден (только в «Лаборатории»).</summary>
        public static bool On(Layer l)
        {
            Load();
            return State[(int)l] && NervApp.Instance != null && NervApp.Instance.LabMode;
        }

        public static bool Selected(Layer l)
        {
            Load();
            return State[(int)l];
        }

        public static void Toggle(Layer l)
        {
            Load();
            State[(int)l] = !State[(int)l];
            PlayerPrefs.SetInt("nerv.layer." + (int)l, State[(int)l] ? 1 : 0);
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            for (int i = 0; i < State.Length; i++) State[i] = PlayerPrefs.GetInt("nerv.layer." + i, i == (int)Layer.Charges ? 1 : 0) == 1;
        }

        /// <summary>
        /// Оценка частичных зарядов всех атомов снимка в <paramref name="q"/> (длина ≥ AtomCount).
        /// </summary>
        public static void PartialCharges(WorldSnapshot snap, float[] q)
        {
            for (int i = 0; i < snap.AtomCount; i++) q[i] = snap.Charges[i];
            foreach (BondView b in snap.Bonds)
            {
                float xa = (float)ElementTable.Get(snap.AtomicNumbers[b.IndexA]).Electronegativity;
                float xb = (float)ElementTable.Get(snap.AtomicNumbers[b.IndexB]).Electronegativity;
                if (xa <= 0 || xb <= 0) continue;
                float d = 0.25f * (xb - xa);
                q[b.IndexA] += d;  // сосед электроотрицательнее — атом теряет электронную плотность
                q[b.IndexB] -= d;
            }
        }
    }
}
