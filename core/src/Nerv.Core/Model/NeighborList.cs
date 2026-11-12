using System;
using System.Collections.Generic;
using Nerv.Core.Numerics;

namespace Nerv.Core.Model
{
    /// <summary>
    /// Список пар атомов ближе радиуса отсечки, построенный через сетку ячеек (cell list):
    /// пространство делится на кубики со стороной = отсечке, и атом проверяется только против
    /// своего и соседних кубиков. Вместо O(N²) — примерно O(N).
    /// Порядок пар детерминирован (зависит только от координат и индексов).
    /// </summary>
    internal sealed class NeighborList
    {
        public int[] I = new int[512];
        public int[] J = new int[512];
        public int Count;
        public double Cutoff;
        /// <summary>Метка позиций, для которой построен список (см. World.PositionStamp).</summary>
        public int Stamp = -1;

        private long[] _keys = new long[64];
        private long[] _sortedKeys = new long[64];
        private int[] _order = new int[64];
        private readonly Dictionary<long, int> _cellStart = new Dictionary<long, int>();
        private readonly List<long> _cells = new List<long>();

        private const int Bias = 1 << 20;
        private static readonly int[][] HalfShell = BuildHalfShell();

        private static int[][] BuildHalfShell()
        {
            var list = new List<int[]>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (dx > 0 || (dx == 0 && dy > 0) || (dx == 0 && dy == 0 && dz > 0))
                            list.Add(new[] { dx, dy, dz });
            return list.ToArray();
        }

        private static long Pack(long x, long y, long z) => ((x + Bias) << 42) | ((y + Bias) << 21) | (z + Bias);

        public void Build(Vec3[] pos, int n, double cutoff)
        {
            Count = 0;
            Cutoff = cutoff;
            if (n < 2) return;
            if (_keys.Length < n)
            {
                int cap = Math.Max(n, _keys.Length * 2);
                _keys = new long[cap];
                _sortedKeys = new long[cap];
                _order = new int[cap];
            }

            double inv = 1.0 / cutoff;
            for (int i = 0; i < n; i++)
            {
                long key = Pack((long)Math.Floor(pos[i].X * inv), (long)Math.Floor(pos[i].Y * inv), (long)Math.Floor(pos[i].Z * inv));
                _keys[i] = key;
                _sortedKeys[i] = key;
                _order[i] = i;
            }
            Array.Sort(_sortedKeys, _order, 0, n);

            _cellStart.Clear();
            _cells.Clear();
            for (int k = 0; k < n; k++)
            {
                if (k > 0 && _sortedKeys[k] == _sortedKeys[k - 1]) continue;
                _cellStart[_sortedKeys[k]] = k;
                _cells.Add(_sortedKeys[k]);
            }

            double c2 = cutoff * cutoff;
            foreach (long cell in _cells)
            {
                int s0 = _cellStart[cell];
                int e0 = End(s0, n);

                // Пары внутри ячейки.
                for (int a = s0; a < e0; a++)
                    for (int b = a + 1; b < e0; b++)
                        TryAdd(pos, _order[a], _order[b], c2);

                // Пары с «половиной» соседних ячеек (каждая пара — один раз).
                long cx = ((cell >> 42) & 0x1FFFFF) - Bias, cy = ((cell >> 21) & 0x1FFFFF) - Bias, cz = (cell & 0x1FFFFF) - Bias;
                foreach (int[] off in HalfShell)
                {
                    if (!_cellStart.TryGetValue(Pack(cx + off[0], cy + off[1], cz + off[2]), out int s1)) continue;
                    int e1 = End(s1, n);
                    for (int a = s0; a < e0; a++)
                        for (int b = s1; b < e1; b++)
                            TryAdd(pos, _order[a], _order[b], c2);
                }
            }
        }

        private int End(int start, int n)
        {
            long key = _sortedKeys[start];
            int e = start + 1;
            while (e < n && _sortedKeys[e] == key) e++;
            return e;
        }

        private void TryAdd(Vec3[] pos, int i, int j, double c2)
        {
            if ((pos[j] - pos[i]).LengthSquared >= c2) return;
            if (Count == I.Length)
            {
                Array.Resize(ref I, I.Length * 2);
                Array.Resize(ref J, J.Length * 2);
            }
            I[Count] = i;
            J[Count] = j;
            Count++;
        }
    }
}
