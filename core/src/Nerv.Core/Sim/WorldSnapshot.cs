using System;
using System.Collections.Generic;
using Nerv.Core.Model;
using Nerv.Core.Naming;

namespace Nerv.Core.Sim
{
    public struct BondView
    {
        public int Id;
        /// <summary>Индексы атомов в массивах снимка.</summary>
        public int IndexA, IndexB;
        public int Order;
        public float Progress;
        public bool IsPolar;
    }

    /// <summary>
    /// Снимок состояния для рендера. Заполняется ядром в переиспользуемые массивы (без аллокаций в кадре),
    /// поэтому рендер может читать его, пока симуляция в своём потоке считает следующий шаг.
    /// </summary>
    public sealed class WorldSnapshot
    {
        public long Tick;
        public int TopologyVersion;
        public double Temperature;
        public int AtomCount;

        public AtomId[] Ids = Array.Empty<AtomId>();
        public int[] AtomicNumbers = Array.Empty<int>();
        public int[] Charges = Array.Empty<int>();
        public int[] FreeValences = Array.Empty<int>();
        public int[] LonePairs = Array.Empty<int>();
        /// <summary>
        /// Мгновенная «температура» атома по его кинетической энергии, K (m·v²/3k). Шумная —
        /// для свечения пламени клиент сглаживает её во времени.
        /// </summary>
        public float[] Temperatures = Array.Empty<float>();
        /// <summary>Идёт горение (недавно была экзотермическая реакция).</summary>
        public bool FlameActive;
        /// <summary>Координаты x, y, z подряд, Å.</summary>
        public float[] Positions = Array.Empty<float>();

        public readonly List<BondView> Bonds = new List<BondView>();
        /// <summary>Молекулы (неизменяемые объекты, безопасно держать ссылки).</summary>
        public IReadOnlyList<Molecule> Molecules = Array.Empty<Molecule>();
        /// <summary>Ионные ассоциации (соли), неизменяемые объекты.</summary>
        public IReadOnlyList<IonicGroup> IonicGroups = Array.Empty<IonicGroup>();

        /// <summary>Копия снимка в dst (массивы dst переиспользуются, если хватает места).</summary>
        public void CopyTo(WorldSnapshot dst)
        {
            dst.EnsureCapacity(AtomCount);
            dst.Tick = Tick;
            dst.TopologyVersion = TopologyVersion;
            dst.Temperature = Temperature;
            dst.AtomCount = AtomCount;
            Array.Copy(Ids, dst.Ids, AtomCount);
            Array.Copy(AtomicNumbers, dst.AtomicNumbers, AtomCount);
            Array.Copy(Charges, dst.Charges, AtomCount);
            Array.Copy(FreeValences, dst.FreeValences, AtomCount);
            Array.Copy(LonePairs, dst.LonePairs, AtomCount);
            Array.Copy(Temperatures, dst.Temperatures, AtomCount);
            dst.FlameActive = FlameActive;
            Array.Copy(Positions, dst.Positions, AtomCount * 3);
            dst.Bonds.Clear();
            dst.Bonds.AddRange(Bonds);
            dst.Molecules = Molecules;
            dst.IonicGroups = IonicGroups;
        }

        internal void EnsureCapacity(int n)
        {
            if (Ids.Length >= n) return;
            int cap = Math.Max(n, Ids.Length * 2);
            Array.Resize(ref Ids, cap);
            Array.Resize(ref AtomicNumbers, cap);
            Array.Resize(ref Charges, cap);
            Array.Resize(ref FreeValences, cap);
            Array.Resize(ref LonePairs, cap);
            Array.Resize(ref Temperatures, cap);
            Array.Resize(ref Positions, cap * 3);
        }
    }
}
