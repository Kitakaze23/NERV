using System;
using System.Collections.Generic;
using Nerv.Core.Model;

namespace Nerv.Core.Chemistry
{
    /// <summary>Атом графа молекулы (или атом-шаблон в SMARTS).</summary>
    public sealed class MolAtom
    {
        public int Z;
        public int Charge;
        /// <summary>Свёрнутые атомы водорода (в нормализованном графе).</summary>
        public int HCount;
        public bool Aromatic;
        /// <summary>Номер в карте атомов реакции (0 — нет).</summary>
        public int Map;

        // --- Ограничения шаблона (SMARTS) ---
        public bool ChargeSpecified;
        /// <summary>Требуемое число соседей-водородов (−1 — любое).</summary>
        public int QueryHCount = -1;
        /// <summary>Требуемое число связей (−1 — любое).</summary>
        public int QueryDegree = -1;

        /// <summary>Атом записан в квадратных скобках (для него нет неявных H).</summary>
        internal bool Bracket;
        /// <summary>Индекс атома в мире (для графов, построенных из World).</summary>
        public int Source = -1;

        public MolAtom Clone() => (MolAtom)MemberwiseClone();
    }

    public sealed class MolBond
    {
        public int A, B;
        /// <summary>1, 2, 3; <see cref="MolGraph.AromaticOrder"/> = ароматическая; <see cref="MolGraph.AnyOrder"/> = любая (SMARTS «~»).</summary>
        public int Order;

        public int Other(int i) => i == A ? B : A;
    }

    /// <summary>
    /// Граф молекулы для канонизации и сопоставления шаблонов.
    /// Нормализованная форма: водороды при тяжёлых атомах свёрнуты в <see cref="MolAtom.HCount"/>,
    /// ароматические 6-кольца отмечены.
    /// </summary>
    public sealed class MolGraph
    {
        public const int AromaticOrder = 4;
        public const int AnyOrder = 0;

        public readonly List<MolAtom> Atoms = new List<MolAtom>();
        public readonly List<MolBond> Bonds = new List<MolBond>();

        public int AddAtom(MolAtom atom)
        {
            Atoms.Add(atom);
            return Atoms.Count - 1;
        }

        public void AddBond(int a, int b, int order)
        {
            if (a == b) throw new FormatException("Связь атома с самим собой.");
            if (FindBond(a, b) != null) throw new FormatException("Повторная связь между атомами " + a + " и " + b + ".");
            Bonds.Add(new MolBond { A = a, B = b, Order = order });
        }

        public MolBond FindBond(int a, int b)
        {
            foreach (MolBond bond in Bonds)
                if ((bond.A == a && bond.B == b) || (bond.A == b && bond.B == a)) return bond;
            return null;
        }

        public List<int>[] Neighbors()
        {
            var nb = new List<int>[Atoms.Count];
            for (int i = 0; i < nb.Length; i++) nb[i] = new List<int>(4);
            foreach (MolBond b in Bonds)
            {
                nb[b.A].Add(b.B);
                nb[b.B].Add(b.A);
            }
            return nb;
        }

        /// <summary>Связные компоненты (списки индексов атомов), упорядочены по первому атому.</summary>
        public List<List<int>> Components()
        {
            List<int>[] nb = Neighbors();
            var seen = new bool[Atoms.Count];
            var result = new List<List<int>>();
            for (int s = 0; s < Atoms.Count; s++)
            {
                if (seen[s]) continue;
                var comp = new List<int>();
                var stack = new Stack<int>();
                stack.Push(s);
                seen[s] = true;
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    comp.Add(u);
                    foreach (int v in nb[u])
                    {
                        if (seen[v]) continue;
                        seen[v] = true;
                        stack.Push(v);
                    }
                }
                comp.Sort();
                result.Add(comp);
            }
            return result;
        }

        /// <summary>Подграф из указанных атомов (индексы перенумеровываются по порядку списка).</summary>
        public MolGraph Subgraph(IList<int> atoms)
        {
            var g = new MolGraph();
            var remap = new Dictionary<int, int>();
            foreach (int a in atoms) remap[a] = g.AddAtom(Atoms[a].Clone());
            foreach (MolBond b in Bonds)
                if (remap.TryGetValue(b.A, out int na) && remap.TryGetValue(b.B, out int nb))
                    g.Bonds.Add(new MolBond { A = na, B = nb, Order = b.Order });
            return g;
        }

        /// <summary>Граф из атомов мира (водороды пока явные; вызовите <see cref="Normalize"/>).</summary>
        public static MolGraph FromWorld(World w, IReadOnlyList<AtomId> atoms)
        {
            w.EnsureTopology();
            var g = new MolGraph();
            var local = new Dictionary<int, int>(atoms.Count);
            foreach (AtomId id in atoms)
            {
                int i = w.IndexOf(id);
                local[i] = g.AddAtom(new MolAtom { Z = w.Elements[i].Z, Charge = w.Charges[i], Source = i, Bracket = true });
            }
            foreach (Bond b in w.Bonds)
                if (local.TryGetValue(b.Ia, out int la) && local.TryGetValue(b.Ib, out int lb))
                    g.Bonds.Add(new MolBond { A = la, B = lb, Order = b.Order });
            return g;
        }

        /// <summary>Свёртка водородов + ароматичность.</summary>
        public void Normalize()
        {
            FoldHydrogens();
            PerceiveAromaticity();
        }

        /// <summary>
        /// Водород (заряд 0) с единственной одинарной связью к не-водороду сворачивается в HCount соседа.
        /// H₂, H⁺, H⁻ и мостиковые водороды остаются явными атомами.
        /// </summary>
        public void FoldHydrogens()
        {
            List<int>[] nb = Neighbors();
            var remove = new bool[Atoms.Count];
            for (int i = 0; i < Atoms.Count; i++)
            {
                MolAtom a = Atoms[i];
                if (a.Z != 1 || a.Charge != 0 || a.HCount != 0 || a.Map != 0 || nb[i].Count != 1) continue;
                int j = nb[i][0];
                if (Atoms[j].Z == 1 || FindBond(i, j).Order != 1) continue;
                remove[i] = true;
                Atoms[j].HCount++;
            }

            var remap = new int[Atoms.Count];
            var atoms = new List<MolAtom>();
            for (int i = 0; i < Atoms.Count; i++)
            {
                if (remove[i]) { remap[i] = -1; continue; }
                remap[i] = atoms.Count;
                atoms.Add(Atoms[i]);
            }
            var bonds = new List<MolBond>();
            foreach (MolBond b in Bonds)
            {
                if (remap[b.A] < 0 || remap[b.B] < 0) continue;
                bonds.Add(new MolBond { A = remap[b.A], B = remap[b.B], Order = b.Order });
            }
            Atoms.Clear();
            Atoms.AddRange(atoms);
            Bonds.Clear();
            Bonds.AddRange(bonds);
        }

        /// <summary>
        /// Ароматичность (MVP): 6-кольца из C/N с чередованием одинарных и двойных связей
        /// (или уже ароматические) → порядок 1.5. Обе кекулевские формы бензола дают один граф.
        /// 5-членные гетероциклы (пиррол, фуран) пока не распознаются.
        /// </summary>
        public void PerceiveAromaticity()
        {
            List<int>[] nb = Neighbors();
            foreach (int[] ring in SixRings(nb))
            {
                bool ok = true;
                int doubles = 0, aromatic = 0;
                for (int k = 0; k < 6 && ok; k++)
                {
                    MolAtom a = Atoms[ring[k]];
                    if (a.Z != 6 && a.Z != 7) ok = false;
                    int order = FindBond(ring[k], ring[(k + 1) % 6]).Order;
                    if (order == 2) doubles++;
                    else if (order == AromaticOrder) aromatic++;
                    else if (order != 1) ok = false;
                }
                if (!ok) continue;
                bool kekule = doubles == 3 && aromatic == 0 && Alternates(ring);
                if (!kekule && aromatic != 6) continue;
                for (int k = 0; k < 6; k++)
                {
                    Atoms[ring[k]].Aromatic = true;
                    FindBond(ring[k], ring[(k + 1) % 6]).Order = AromaticOrder;
                }
            }
        }

        private bool Alternates(int[] ring)
        {
            for (int k = 0; k < 6; k++)
            {
                int o1 = FindBond(ring[k], ring[(k + 1) % 6]).Order;
                int o2 = FindBond(ring[(k + 1) % 6], ring[(k + 2) % 6]).Order;
                if (o1 == o2) return false;
            }
            return true;
        }

        private List<int[]> SixRings(List<int>[] nb)
        {
            var rings = new List<int[]>();
            var keys = new HashSet<string>();
            var path = new int[6];
            for (int s = 0; s < Atoms.Count; s++)
            {
                path[0] = s;
                Walk(nb, path, 1, s, rings, keys);
            }
            return rings;
        }

        private static void Walk(List<int>[] nb, int[] path, int depth, int start, List<int[]> rings, HashSet<string> keys)
        {
            int u = path[depth - 1];
            foreach (int v in nb[u])
            {
                if (depth == 6)
                {
                    if (v != start) continue;
                    var ring = (int[])path.Clone();
                    var sorted = (int[])ring.Clone();
                    Array.Sort(sorted);
                    if (keys.Add(string.Join(",", sorted))) rings.Add(ring);
                    continue;
                }
                if (v <= start) continue; // старт — минимальный индекс кольца
                bool used = false;
                for (int k = 0; k < depth; k++) if (path[k] == v) { used = true; break; }
                if (used) continue;
                path[depth] = v;
                Walk(nb, path, depth + 1, start, rings, keys);
            }
        }

        /// <summary>Число атомов каждого элемента (включая свёрнутые водороды).</summary>
        public Dictionary<int, int> ElementCounts()
        {
            var c = new Dictionary<int, int>();
            foreach (MolAtom a in Atoms)
            {
                c.TryGetValue(a.Z, out int n);
                c[a.Z] = n + 1;
                if (a.HCount > 0)
                {
                    c.TryGetValue(1, out int h);
                    c[1] = h + a.HCount;
                }
            }
            return c;
        }

        public int TotalCharge()
        {
            int q = 0;
            foreach (MolAtom a in Atoms) q += a.Charge;
            return q;
        }
    }
}
