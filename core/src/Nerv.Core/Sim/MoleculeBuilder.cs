using System;
using System.Collections.Generic;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Physics;

namespace Nerv.Core.Sim
{
    /// <summary>
    /// Готовая трёхмерная модель вещества: атомы, связи и координаты (центр масс в нуле).
    /// </summary>
    public sealed class MoleculeTemplate
    {
        public string Smiles { get; internal set; }
        public int[] Z { get; internal set; }
        public int[] Charges { get; internal set; }
        public Vec3[] Positions { get; internal set; }
        public (int A, int B, int Order)[] Bonds { get; internal set; }
        public int AtomCount => Z.Length;
        /// <summary>Радиус описанной сферы, Å.</summary>
        public double Radius { get; internal set; }
    }

    /// <summary>
    /// Сборка молекулы по SMILES: раскрытие водородов, кекулизация ароматики,
    /// грубая укладка по графу (расстояния 1-2 / 1-3 в 4D со сжатием в 3D)
    /// и доводка тем же силовым полем, что и в симуляции, — чтобы молекула появлялась уже в равновесии.
    /// Результат детерминирован (зерно — стабильный хеш строки) и кэшируется.
    /// </summary>
    public static class MoleculeBuilder
    {
        private static readonly Dictionary<string, MoleculeTemplate> Cache = new Dictionary<string, MoleculeTemplate>(StringComparer.Ordinal);
        private static readonly object CacheLock = new object();

        public static MoleculeTemplate Build(string smiles)
        {
            if (string.IsNullOrWhiteSpace(smiles)) throw new ArgumentException("Пустой SMILES.", nameof(smiles));
            lock (CacheLock)
                if (Cache.TryGetValue(smiles, out MoleculeTemplate t)) return t;

            MoleculeTemplate built = BuildUncached(smiles);
            lock (CacheLock) Cache[smiles] = built;
            return built;
        }

        private static MoleculeTemplate BuildUncached(string smiles)
        {
            MolGraph g = Smiles.Parse(smiles);
            Kekulize(g);

            // Раскрываем свёрнутые водороды в явные атомы.
            var z = new List<int>();
            var charges = new List<int>();
            var bonds = new List<(int, int, int)>();
            foreach (MolAtom a in g.Atoms)
            {
                z.Add(a.Z);
                charges.Add(a.Charge);
            }
            foreach (MolBond b in g.Bonds) bonds.Add((b.A, b.B, b.Order));
            for (int i = 0; i < g.Atoms.Count; i++)
                for (int h = 0; h < g.Atoms[i].HCount; h++)
                {
                    z.Add(1);
                    charges.Add(0);
                    bonds.Add((i, z.Count - 1, 1));
                }

            int n = z.Count;
            foreach (int el in z)
                if (!AtomTyping.IsSupported(ElementTable.Get(el)))
                    throw new NotSupportedException("Элемент " + ElementTable.Get(el).Symbol + " пока недоступен в симуляции.");

            var rng = new Xoshiro256(StableHash(smiles));
            Vec3[] pos = Embed(n, bonds, rng);
            Relax(z, charges, bonds, pos);

            // Центр масс — в ноль.
            Vec3 c = Vec3.Zero;
            double m = 0;
            for (int i = 0; i < n; i++)
            {
                double mi = ElementTable.Get(z[i]).Mass;
                c += pos[i] * mi;
                m += mi;
            }
            c /= m;
            double r = 0;
            for (int i = 0; i < n; i++)
            {
                pos[i] -= c;
                r = Math.Max(r, pos[i].Length);
            }

            return new MoleculeTemplate
            {
                Smiles = smiles,
                Z = z.ToArray(),
                Charges = charges.ToArray(),
                Positions = pos,
                Bonds = bonds.ToArray(),
                Radius = r,
            };
        }

        // ================= Кекулизация =================

        /// <summary>
        /// Ароматические связи → чередование одинарных и двойных. Атомам, которым не хватает одной связи
        /// до валентности, подбирается пара через ароматическую связь (паросочетание с увеличивающими путями).
        /// </summary>
        internal static void Kekulize(MolGraph g)
        {
            int n = g.Atoms.Count;
            var need = new bool[n];
            var arom = new List<MolBond>();
            for (int i = 0; i < n; i++)
            {
                MolAtom a = g.Atoms[i];
                if (!a.Aromatic) continue;
                int used = a.HCount;
                foreach (MolBond b in g.Bonds)
                    if (b.A == i || b.B == i) used += b.Order == MolGraph.AromaticOrder ? 1 : b.Order;
                need[i] = ElectronRules.MaxValence(ElementTable.Get(a.Z), a.Charge) - used >= 1;
            }
            foreach (MolBond b in g.Bonds)
                if (b.Order == MolGraph.AromaticOrder) arom.Add(b);
            if (arom.Count == 0) return;

            var mate = new int[n];
            for (int i = 0; i < n; i++) mate[i] = -1;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            foreach (MolBond b in arom)
                if (need[b.A] && need[b.B])
                {
                    adj[b.A].Add(b.B);
                    adj[b.B].Add(b.A);
                }

            for (int i = 0; i < n; i++)
                if (need[i] && mate[i] < 0)
                    Augment(i, adj, mate, new bool[n]);

            foreach (MolBond b in arom)
                b.Order = mate[b.A] == b.B ? 2 : 1;
            foreach (MolAtom a in g.Atoms) a.Aromatic = false;
        }

        private static bool Augment(int u, List<int>[] adj, int[] mate, bool[] seen)
        {
            foreach (int v in adj[u])
            {
                if (seen[v]) continue;
                seen[v] = true;
                if (mate[v] < 0 || Augment(mate[v], adj, mate, seen))
                {
                    mate[u] = v;
                    mate[v] = u;
                    return true;
                }
            }
            return false;
        }

        // ================= Укладка =================

        /// <summary>
        /// Грубые координаты по графу: связанные — 1,3 Å, через один атом — 2,2 Å, остальные не ближе 2,6 Å.
        /// Считаем в 4D (меньше застреваний), затем четвёртую координату «выдавливаем» штрафом.
        /// Отдельные ионы/молекулы (точки в SMILES) раскладываются тем же способом.
        /// </summary>
        private static Vec3[] Embed(int n, List<(int A, int B, int Order)> bonds, Xoshiro256 rng)
        {
            var target = new double[n, n];
            var kind = new int[n, n]; // 1 — связь, 2 — 1-3, 0 — прочие
            var nb = new List<int>[n];
            for (int i = 0; i < n; i++) nb[i] = new List<int>();
            foreach (var b in bonds)
            {
                nb[b.A].Add(b.B);
                nb[b.B].Add(b.A);
                double d = b.Order >= 3 ? 1.15 : b.Order == 2 ? 1.25 : 1.35;
                target[b.A, b.B] = target[b.B, b.A] = d;
                kind[b.A, b.B] = kind[b.B, b.A] = 1;
            }
            for (int j = 0; j < n; j++)
                for (int x = 0; x < nb[j].Count; x++)
                    for (int y = x + 1; y < nb[j].Count; y++)
                    {
                        int a = nb[j][x], c = nb[j][y];
                        if (kind[a, c] == 1) continue;
                        // Линейный центр (тройная связь или две двойные) — 180°, иначе ~115°.
                        bool linear = nb[j].Count == 2 && LinearCenter(j, bonds);
                        double ang = linear ? Math.PI : 115.0 * Math.PI / 180.0;
                        double d1 = target[a, j], d2 = target[c, j];
                        double d = Math.Sqrt(d1 * d1 + d2 * d2 - 2 * d1 * d2 * Math.Cos(ang));
                        target[a, c] = target[c, a] = d;
                        kind[a, c] = kind[c, a] = 2;
                    }

            double spread = 1.2 * Math.Pow(n, 1.0 / 3.0) + 0.5;
            var p = new double[n, 4];
            for (int i = 0; i < n; i++)
                for (int k = 0; k < 4; k++) p[i, k] = (rng.NextDouble() * 2 - 1) * spread;

            const int Iterations = 1500;
            var grad = new double[n, 4];
            for (int it = 0; it < Iterations; it++)
            {
                double squash = it < Iterations / 3 ? 0.0 : (it - Iterations / 3) / (double)(Iterations / 3);
                Array.Clear(grad, 0, grad.Length);
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        double dx0 = p[i, 0] - p[j, 0], dx1 = p[i, 1] - p[j, 1], dx2 = p[i, 2] - p[j, 2], dx3 = p[i, 3] - p[j, 3];
                        double d = Math.Sqrt(dx0 * dx0 + dx1 * dx1 + dx2 * dx2 + dx3 * dx3) + 1e-9;
                        double e;
                        if (kind[i, j] != 0) e = (d - target[i, j]) * (kind[i, j] == 1 ? 2.0 : 1.0);
                        else if (d < 2.6) e = (d - 2.6) * 0.5;
                        else continue;
                        double s = e / d;
                        grad[i, 0] += s * dx0; grad[j, 0] -= s * dx0;
                        grad[i, 1] += s * dx1; grad[j, 1] -= s * dx1;
                        grad[i, 2] += s * dx2; grad[j, 2] -= s * dx2;
                        grad[i, 3] += s * dx3; grad[j, 3] -= s * dx3;
                    }
                // Разные компоненты (ионы) слабо стягиваем к центру, чтобы не разбегались.
                for (int i = 0; i < n; i++)
                {
                    for (int k = 0; k < 3; k++) grad[i, k] += 0.01 * p[i, k];
                    grad[i, 3] += squash * 2.0 * p[i, 3];
                }
                double step = 0.1;
                for (int i = 0; i < n; i++)
                    for (int k = 0; k < 4; k++) p[i, k] -= step * grad[i, k];
            }

            var pos = new Vec3[n];
            for (int i = 0; i < n; i++) pos[i] = new Vec3(p[i, 0], p[i, 1], p[i, 2]);
            return pos;
        }

        private static bool LinearCenter(int j, List<(int A, int B, int Order)> bonds)
        {
            int doubles = 0;
            foreach (var b in bonds)
            {
                if (b.A != j && b.B != j) continue;
                if (b.Order >= 3) return true;
                if (b.Order == 2) doubles++;
            }
            return doubles >= 2;
        }

        /// <summary>Доводка силовым полем симуляции: градиентный спуск с ограничением шага.</summary>
        private static void Relax(List<int> z, List<int> charges, List<(int A, int B, int Order)> bonds, Vec3[] pos)
        {
            var settings = new SimulationSettings { BoxHalfSize = 0 };
            var w = new World();
            var ids = new AtomId[z.Count];
            for (int i = 0; i < z.Count; i++) ids[i] = w.AddAtom(ElementTable.Get(z[i]), pos[i], charges[i]);
            foreach (var b in bonds) w.AddBond(ids[b.A], ids[b.B], b.Order, 1.0);

            int n = z.Count;
            double step = 0.002; // Å на (ккал/моль/Å)
            double prev = double.MaxValue;
            for (int it = 0; it < 3000; it++)
            {
                double e = ForceField.Compute(w, settings);
                double maxF = 0;
                for (int i = 0; i < n; i++)
                {
                    Vec3 d = w.Forces[i] * step;
                    double len = d.Length;
                    if (len > 0.05) d *= 0.05 / len;
                    w.Positions[i] += d;
                    maxF = Math.Max(maxF, w.Forces[i].Length);
                }
                step = e < prev ? Math.Min(step * 1.05, 0.01) : step * 0.5;
                prev = e;
                if (maxF < 0.5) break;
            }
            for (int i = 0; i < n; i++) pos[i] = w.Positions[i];
        }

        /// <summary>FNV-1a: в отличие от string.GetHashCode, одинаков между запусками (нужно для повторов).</summary>
        internal static ulong StableHash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
