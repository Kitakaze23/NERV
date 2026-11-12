using System;
using System.Collections.Generic;
using System.Text;
using Nerv.Core.Data;
using Nerv.Core.Model;

namespace Nerv.Core.Chemistry
{
    /// <summary>
    /// Канонический ключ молекулы — канонический SMILES в диалекте NERV:
    /// все атомы в скобках (<c>[CH3][CH2][OH]</c>), явные символы кратных и ароматических связей,
    /// несвязанные части отсортированы.
    ///
    /// Один и тот же код используется и для базы названий (из SMILES), и для молекул из сцены,
    /// поэтому ключи совпадают по построению.
    ///
    /// Алгоритм: начальные инварианты → итеративное уточнение рангов (Морган / Вейсфейлер–Леман) →
    /// разрыв оставшихся ничьих → обход в глубину по рангам.
    /// Ограничение: для редких «неразличимых, но не симметричных» графов результат может зависеть
    /// от порядка атомов на входе.
    /// </summary>
    public static class Canonicalizer
    {
        /// <summary>Ключ для нормализованного графа.</summary>
        public static string Key(MolGraph g)
        {
            if (g.Atoms.Count == 0) return string.Empty;
            List<int>[] nb = g.Neighbors();
            int[] rank = Ranks(g, nb);

            var parts = new List<string>();
            var visited = new bool[g.Atoms.Count];
            foreach (List<int> comp in g.Components())
            {
                int start = comp[0];
                foreach (int a in comp) if (rank[a] < rank[start]) start = a;
                parts.Add(WriteComponent(g, nb, rank, start, visited));
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join(".", parts);
        }

        public static string KeyFromSmiles(string smiles) => Key(Smiles.ParseNormalized(smiles));

        public static string KeyFromWorld(World w, IReadOnlyList<AtomId> atoms)
        {
            MolGraph g = MolGraph.FromWorld(w, atoms);
            g.Normalize();
            return Key(g);
        }

        // ================= Ранги =================

        internal static int[] Ranks(MolGraph g, List<int>[] nb)
        {
            int n = g.Atoms.Count;
            var inv = new long[n];
            for (int i = 0; i < n; i++)
            {
                MolAtom a = g.Atoms[i];
                int orderSum = 0;
                foreach (int j in nb[i]) orderSum += g.FindBond(i, j).Order;
                inv[i] = ((((((long)a.Z * 32 + (a.Charge + 8)) * 16 + a.HCount) * 16 + nb[i].Count) * 2 + (a.Aromatic ? 1 : 0)) * 64) + orderSum;
            }
            int[] rank = DenseRank(n, (x, y) => inv[x].CompareTo(inv[y]));

            var orders = new Dictionary<long, int>();
            foreach (MolBond b in g.Bonds)
            {
                orders[PairKey(b.A, b.B)] = b.Order;
            }

            rank = Refine(n, nb, rank, orders);
            while (Distinct(rank) < n)
            {
                // Ничья: выбираем атом с наименьшим индексом среди наименьшего неразличимого ранга.
                int tied = -1;
                var counts = new Dictionary<int, int>();
                foreach (int r in rank)
                {
                    counts.TryGetValue(r, out int c);
                    counts[r] = c + 1;
                }
                for (int i = 0; i < n; i++)
                    if (counts[rank[i]] > 1 && (tied < 0 || rank[i] < rank[tied])) tied = i;

                var next = new int[n];
                for (int i = 0; i < n; i++) next[i] = rank[i] * 2 + (i == tied || rank[i] != rank[tied] ? 0 : 1);
                rank = Refine(n, nb, DenseRank(n, (x, y) => next[x].CompareTo(next[y])), orders);
            }
            return rank;
        }

        private static int[] Refine(int n, List<int>[] nb, int[] rank, Dictionary<long, int> orders)
        {
            while (true)
            {
                var keys = new long[n][];
                for (int i = 0; i < n; i++)
                {
                    var k = new long[nb[i].Count];
                    for (int t = 0; t < nb[i].Count; t++)
                    {
                        int j = nb[i][t];
                        k[t] = (long)rank[j] * 8 + orders[PairKey(i, j)];
                    }
                    Array.Sort(k);
                    keys[i] = k;
                }
                int[] current = rank;
                int[] next = DenseRank(n, (x, y) =>
                {
                    int c = current[x].CompareTo(current[y]);
                    if (c != 0) return c;
                    long[] kx = keys[x], ky = keys[y];
                    c = kx.Length.CompareTo(ky.Length);
                    if (c != 0) return c;
                    for (int t = 0; t < kx.Length; t++)
                    {
                        c = kx[t].CompareTo(ky[t]);
                        if (c != 0) return c;
                    }
                    return 0;
                });
                if (Distinct(next) == Distinct(rank)) return next;
                rank = next;
            }
        }

        private static int[] DenseRank(int n, Comparison<int> cmp)
        {
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            // Устойчивая сортировка: при равенстве сохраняем порядок индексов.
            Array.Sort(idx, (x, y) =>
            {
                int c = cmp(x, y);
                return c != 0 ? c : x.CompareTo(y);
            });
            var rank = new int[n];
            int r = 0;
            for (int k = 0; k < n; k++)
            {
                if (k > 0 && cmp(idx[k - 1], idx[k]) != 0) r++;
                rank[idx[k]] = r;
            }
            return rank;
        }

        private static int Distinct(int[] rank)
        {
            var set = new HashSet<int>(rank);
            return set.Count;
        }

        private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        // ================= Запись SMILES =================

        private static string WriteComponent(MolGraph g, List<int>[] nb, int[] rank, int start, bool[] visited)
        {
            var children = new Dictionary<int, List<int>>();
            var closures = new Dictionary<int, List<int>>();
            var closureSet = new HashSet<long>();
            var onStack = new HashSet<int>();

            void Dfs(int u, int parent)
            {
                visited[u] = true;
                onStack.Add(u);
                children[u] = new List<int>();
                if (!closures.ContainsKey(u)) closures[u] = new List<int>();
                var order = new List<int>(nb[u]);
                order.Sort((x, y) => rank[x].CompareTo(rank[y]));
                foreach (int v in order)
                {
                    if (v == parent) continue;
                    if (visited[v])
                    {
                        if (onStack.Contains(v) && closureSet.Add(PairKey(u, v)))
                        {
                            closures[u].Add(v);
                            if (!closures.ContainsKey(v)) closures[v] = new List<int>();
                            closures[v].Add(u);
                        }
                        continue;
                    }
                    children[u].Add(v);
                    Dfs(v, u);
                }
                onStack.Remove(u);
            }

            Dfs(start, -1);

            var sb = new StringBuilder();
            var openDigits = new Dictionary<long, int>();
            var freeDigits = new SortedSet<int>();
            int nextDigit = 1;

            void Emit(int u)
            {
                sb.Append(AtomText(g.Atoms[u]));
                List<int> cl = closures[u];
                cl.Sort((x, y) => rank[x].CompareTo(rank[y]));
                foreach (int v in cl)
                {
                    long key = PairKey(u, v);
                    sb.Append(BondText(g, u, v));
                    if (openDigits.TryGetValue(key, out int d))
                    {
                        openDigits.Remove(key);
                        freeDigits.Add(d);
                    }
                    else
                    {
                        if (freeDigits.Count > 0)
                        {
                            d = freeDigits.Min;
                            freeDigits.Remove(d);
                        }
                        else d = nextDigit++;
                        openDigits[key] = d;
                    }
                    sb.Append(d < 10 ? d.ToString() : "%" + d.ToString("00"));
                }

                List<int> ch = children[u];
                for (int k = 0; k < ch.Count; k++)
                {
                    bool branch = k < ch.Count - 1;
                    if (branch) sb.Append('(');
                    sb.Append(BondText(g, u, ch[k]));
                    Emit(ch[k]);
                    if (branch) sb.Append(')');
                }
            }

            Emit(start);
            return sb.ToString();
        }

        internal static string AtomText(MolAtom a)
        {
            var sb = new StringBuilder("[");
            string sym = ElementTable.Get(a.Z).Symbol;
            sb.Append(a.Aromatic ? sym.ToLowerInvariant() : sym);
            if (a.HCount == 1) sb.Append('H');
            else if (a.HCount > 1) sb.Append('H').Append(a.HCount);
            if (a.Charge > 0) sb.Append('+');
            if (a.Charge < 0) sb.Append('-');
            if (Math.Abs(a.Charge) > 1) sb.Append(Math.Abs(a.Charge));
            sb.Append(']');
            return sb.ToString();
        }

        private static string BondText(MolGraph g, int a, int b)
        {
            int order = g.FindBond(a, b).Order;
            switch (order)
            {
                case 2: return "=";
                case 3: return "#";
                case MolGraph.AromaticOrder: return ":";
                default: return g.Atoms[a].Aromatic && g.Atoms[b].Aromatic ? "-" : "";
            }
        }
    }
}
