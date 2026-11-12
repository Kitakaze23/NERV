using System;
using System.Collections.Generic;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;
using Nerv.Core.Numerics;

namespace Nerv.Core.Model
{
    /// <summary>Валентный угол i–j–k (j — центральный атом), параметры UFF.</summary>
    internal struct Angle
    {
        public int I, J, K;
        public Bond B1, B2;
        public double Kf, C0, C1, C2;
        public bool Linear;
    }

    /// <summary>
    /// Состояние мира. Атомы хранятся по столбцам (SoA) в плотных массивах; стабильные <see cref="AtomId"/>
    /// отображаются на индексы через таблицу слотов с поколениями.
    /// Кэш топологии (соседи, типы UFF, углы, исключения, молекулы) пересчитывается лениво при изменениях.
    /// </summary>
    public sealed class World
    {
        private const int InitialCapacity = 64;

        // --- Плотные массивы атомов (индекс 0..AtomCount-1) ---
        internal Vec3[] Positions = new Vec3[InitialCapacity];
        internal Vec3[] Velocities = new Vec3[InitialCapacity];
        internal Vec3[] Forces = new Vec3[InitialCapacity];
        internal Element[] Elements = new Element[InitialCapacity];
        internal int[] Charges = new int[InitialCapacity];
        internal double[] Masses = new double[InitialCapacity];
        internal int[] DenseToSlot = new int[InitialCapacity];

        // --- Кэш топологии ---
        internal UffAtomType[] Types = new UffAtomType[InitialCapacity];
        internal int[] BondOrderSums = new int[InitialCapacity];
        internal int[] FreeValences = new int[InitialCapacity];
        internal int[] LonePairCounts = new int[InitialCapacity];
        internal List<Bond>[] AdjacentBonds = new List<Bond>[InitialCapacity];
        internal readonly List<Angle> Angles = new List<Angle>();
        private readonly HashSet<long> _excluded = new HashSet<long>();
        private readonly List<Molecule> _molecules = new List<Molecule>();

        // --- Таблица слотов ---
        private int[] _slotToDense = new int[InitialCapacity];
        private int[] _slotGeneration = new int[InitialCapacity];
        private readonly Stack<int> _freeSlots = new Stack<int>();
        private int _slotCount;

        private readonly List<Bond> _bonds = new List<Bond>();
        private int _nextBondId = 1;

        public int AtomCount { get; private set; }
        public IReadOnlyList<Bond> Bonds => _bonds;
        public bool TopologyDirty { get; private set; } = true;
        /// <summary>Растёт при каждом пересчёте топологии.</summary>
        public int TopologyVersion { get; private set; }

        /// <summary>Глубина ямы Морзе на единицу порядка связи, ккал/моль (UFF рекомендует ~70).</summary>
        public double BondEnergyPerOrder { get; set; } = 70.0;

        public IReadOnlyList<Molecule> Molecules
        {
            get
            {
                EnsureTopology();
                return _molecules;
            }
        }

        // ================= Атомы =================

        public AtomId AddAtom(Element element, Vec3 position, int charge = 0)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (!AtomTyping.IsSupported(element))
                throw new NotSupportedException("Элемент " + element.Symbol + " пока недоступен в симуляции.");

            int slot;
            if (_freeSlots.Count > 0)
            {
                slot = _freeSlots.Pop();
            }
            else
            {
                slot = _slotCount++;
                EnsureSlotCapacity(_slotCount);
                _slotGeneration[slot] = 1;
            }

            int i = AtomCount++;
            EnsureDenseCapacity(AtomCount);
            Positions[i] = position;
            Velocities[i] = Vec3.Zero;
            Forces[i] = Vec3.Zero;
            Elements[i] = element;
            Charges[i] = charge;
            Masses[i] = element.Mass;
            DenseToSlot[i] = slot;
            _slotToDense[slot] = i;

            MarkTopologyDirty();
            return new AtomId(slot, _slotGeneration[slot]);
        }

        /// <summary>Удаляет атом и все его связи. Возвращает false, если id устарел.</summary>
        public bool RemoveAtom(AtomId id)
        {
            int i = IndexOf(id);
            if (i < 0) return false;

            _bonds.RemoveAll(b => b.Contains(id));

            int last = AtomCount - 1;
            if (i != last)
            {
                Positions[i] = Positions[last];
                Velocities[i] = Velocities[last];
                Forces[i] = Forces[last];
                Elements[i] = Elements[last];
                Charges[i] = Charges[last];
                Masses[i] = Masses[last];
                DenseToSlot[i] = DenseToSlot[last];
                _slotToDense[DenseToSlot[i]] = i;
            }

            AtomCount--;
            Elements[last] = null;
            _slotGeneration[id.Slot]++;
            _slotToDense[id.Slot] = -1;
            _freeSlots.Push(id.Slot);

            MarkTopologyDirty();
            return true;
        }

        public bool IsAlive(AtomId id) => IndexOf(id) >= 0;

        /// <summary>Плотный индекс атома или −1, если id устарел.</summary>
        public int IndexOf(AtomId id)
        {
            if (id.Generation == 0 || id.Slot < 0 || id.Slot >= _slotCount) return -1;
            if (_slotGeneration[id.Slot] != id.Generation) return -1;
            return _slotToDense[id.Slot];
        }

        public AtomId IdAt(int index)
        {
            if (index < 0 || index >= AtomCount) throw new ArgumentOutOfRangeException(nameof(index));
            int slot = DenseToSlot[index];
            return new AtomId(slot, _slotGeneration[slot]);
        }

        public Element GetElement(AtomId id) => Elements[Require(id)];
        public Vec3 GetPosition(AtomId id) => Positions[Require(id)];
        public Vec3 GetVelocity(AtomId id) => Velocities[Require(id)];
        public int GetCharge(AtomId id) => Charges[Require(id)];

        public int GetFreeValence(AtomId id)
        {
            EnsureTopology();
            return FreeValences[Require(id)];
        }

        public int GetLonePairs(AtomId id)
        {
            EnsureTopology();
            return LonePairCounts[Require(id)];
        }

        public UffAtomType GetAtomType(AtomId id)
        {
            EnsureTopology();
            return Types[Require(id)];
        }

        internal void SetCharge(int index, int charge)
        {
            Charges[index] = charge;
            MarkTopologyDirty();
        }

        private int Require(AtomId id)
        {
            int i = IndexOf(id);
            if (i < 0) throw new ArgumentException("Атом не существует: " + id, nameof(id));
            return i;
        }

        // ================= Связи =================

        public Bond AddBond(AtomId a, AtomId b, int order, double progress)
        {
            if (a == b) throw new ArgumentException("Связь атома с самим собой.");
            Require(a);
            Require(b);
            if (order < 1 || order > 3) throw new ArgumentOutOfRangeException(nameof(order));
            if (FindBond(a, b) != null) throw new InvalidOperationException("Связь уже существует.");

            var bond = new Bond(_nextBondId++, a, b, order, Math.Max(0.0, Math.Min(1.0, progress)));
            _bonds.Add(bond);
            MarkTopologyDirty();
            return bond;
        }

        public bool RemoveBond(Bond bond)
        {
            bool removed = _bonds.Remove(bond);
            if (removed) MarkTopologyDirty();
            return removed;
        }

        public Bond FindBond(AtomId a, AtomId b)
        {
            for (int i = 0; i < _bonds.Count; i++)
            {
                Bond bond = _bonds[i];
                if ((bond.A == a && bond.B == b) || (bond.A == b && bond.B == a)) return bond;
            }
            return null;
        }

        internal void SetBondOrder(Bond bond, int order)
        {
            bond.Order = order;
            MarkTopologyDirty();
        }

        // ================= Топология =================

        internal void MarkTopologyDirty() => TopologyDirty = true;

        public void EnsureTopology()
        {
            if (TopologyDirty) RebuildTopology();
        }

        /// <summary>Пара связана через 1 или 2 связи (1-2, 1-3) — для неё не считаются невалентные взаимодействия.</summary>
        internal bool IsExcluded(int i, int j) => _excluded.Contains(PairKey(i, j));

        private static long PairKey(int i, int j) => i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;

        private void RebuildTopology()
        {
            int n = AtomCount;

            for (int i = 0; i < n; i++)
            {
                if (AdjacentBonds[i] == null) AdjacentBonds[i] = new List<Bond>(4);
                else AdjacentBonds[i].Clear();
                BondOrderSums[i] = 0;
            }

            foreach (Bond b in _bonds)
            {
                b.Ia = IndexOf(b.A);
                b.Ib = IndexOf(b.B);
                AdjacentBonds[b.Ia].Add(b);
                AdjacentBonds[b.Ib].Add(b);
                BondOrderSums[b.Ia] += b.Order;
                BondOrderSums[b.Ib] += b.Order;
            }

            for (int i = 0; i < n; i++)
            {
                Element e = Elements[i];
                FreeValences[i] = ElectronRules.FreeValence(e, Charges[i], BondOrderSums[i]);
                LonePairCounts[i] = ElectronRules.LonePairs(e, Charges[i], BondOrderSums[i]);
                Types[i] = Uff.Get(AtomTyping.Assign(e, AdjacentBonds[i].Count, LonePairCounts[i], FreeValences[i]));
            }

            foreach (Bond b in _bonds)
            {
                UffAtomType ta = Types[b.Ia], tb = Types[b.Ib];
                b.RestLength = Uff.RestLength(ta, tb, b.Order);
                double k = Uff.BondForceConstant(ta, tb, b.RestLength);
                b.De = BondEnergyPerOrder * b.Order;
                b.Alpha = Math.Sqrt(k / (2.0 * b.De));
                double ea = Elements[b.Ia].Electronegativity, eb = Elements[b.Ib].Electronegativity;
                b.IsPolar = Math.Abs(ea - eb) > 0.4;
            }

            RebuildAngles(n);
            RebuildExclusions(n);
            RebuildMolecules(n);

            TopologyDirty = false;
            TopologyVersion++;
        }

        private void RebuildAngles(int n)
        {
            Angles.Clear();
            for (int j = 0; j < n; j++)
            {
                List<Bond> adj = AdjacentBonds[j];
                if (adj.Count < 2) continue;
                UffAtomType tj = Types[j];
                double theta0 = tj.Theta0Rad;
                double c0 = Math.Cos(theta0);

                for (int a = 0; a < adj.Count; a++)
                {
                    for (int b = a + 1; b < adj.Count; b++)
                    {
                        Bond b1 = adj[a], b2 = adj[b];
                        int i = b1.Ia == j ? b1.Ib : b1.Ia;
                        int k = b2.Ia == j ? b2.Ib : b2.Ia;
                        var angle = new Angle
                        {
                            I = i,
                            J = j,
                            K = k,
                            B1 = b1,
                            B2 = b2,
                            Kf = Uff.AngleForceConstant(Types[i], Types[k], b1.RestLength, b2.RestLength, theta0),
                            Linear = tj.IsLinear
                        };
                        if (!angle.Linear)
                        {
                            double s2 = 1.0 - c0 * c0;
                            angle.C2 = 1.0 / (4.0 * s2);
                            angle.C1 = -4.0 * angle.C2 * c0;
                            angle.C0 = angle.C2 * (2.0 * c0 * c0 + 1.0);
                        }
                        Angles.Add(angle);
                    }
                }
            }
        }

        private void RebuildExclusions(int n)
        {
            _excluded.Clear();
            for (int i = 0; i < n; i++)
            {
                foreach (Bond b1 in AdjacentBonds[i])
                {
                    int j = b1.Ia == i ? b1.Ib : b1.Ia;
                    _excluded.Add(PairKey(i, j));
                    foreach (Bond b2 in AdjacentBonds[j])
                    {
                        int k = b2.Ia == j ? b2.Ib : b2.Ia;
                        if (k != i) _excluded.Add(PairKey(i, k));
                    }
                }
            }
        }

        private void RebuildMolecules(int n)
        {
            _molecules.Clear();
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (Bond b in _bonds)
            {
                int ra = Find(b.Ia), rb = Find(b.Ib);
                if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }

            // Корень — минимальный индекс компоненты, поэтому порядок молекул детерминирован.
            var groups = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (!groups.TryGetValue(r, out List<int> list))
                {
                    list = new List<int>();
                    groups[r] = list;
                    order.Add(r);
                }
                list.Add(i);
            }

            foreach (int root in order)
            {
                List<int> members = groups[root];
                var ids = new AtomId[members.Count];
                var elements = new Element[members.Count];
                int charge = 0;
                int bondTwice = 0;
                for (int m = 0; m < members.Count; m++)
                {
                    int idx = members[m];
                    ids[m] = IdAt(idx);
                    elements[m] = Elements[idx];
                    charge += Charges[idx];
                    bondTwice += AdjacentBonds[idx].Count;
                }
                _molecules.Add(new Molecule(ids, Formula.Hill(elements), charge, bondTwice / 2));
            }
        }

        // ================= Ёмкость массивов =================

        private void EnsureDenseCapacity(int size)
        {
            if (size <= Positions.Length) return;
            int cap = Math.Max(size, Positions.Length * 2);
            Array.Resize(ref Positions, cap);
            Array.Resize(ref Velocities, cap);
            Array.Resize(ref Forces, cap);
            Array.Resize(ref Elements, cap);
            Array.Resize(ref Charges, cap);
            Array.Resize(ref Masses, cap);
            Array.Resize(ref DenseToSlot, cap);
            Array.Resize(ref Types, cap);
            Array.Resize(ref BondOrderSums, cap);
            Array.Resize(ref FreeValences, cap);
            Array.Resize(ref LonePairCounts, cap);
            Array.Resize(ref AdjacentBonds, cap);
        }

        private void EnsureSlotCapacity(int size)
        {
            if (size <= _slotToDense.Length) return;
            int cap = Math.Max(size, _slotToDense.Length * 2);
            Array.Resize(ref _slotToDense, cap);
            Array.Resize(ref _slotGeneration, cap);
        }
    }
}
