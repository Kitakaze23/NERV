using System;

namespace Nerv.Core.Numerics
{
    /// <summary>
    /// Детерминированный генератор xoshiro256**. Одинаковый seed + одинаковая последовательность
    /// вызовов = одинаковые числа на любой платформе (в отличие от System.Random).
    /// </summary>
    public sealed class Xoshiro256
    {
        private ulong _s0, _s1, _s2, _s3;

        public Xoshiro256(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            _s2 = SplitMix(ref x);
            _s3 = SplitMix(ref x);
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextULong()
        {
            ulong result = Rotl(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }

        /// <summary>Равномерно в [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Нормальное распределение N(0, 1), метод Бокса — Мюллера.</summary>
        public double NextGaussian()
        {
            double u1 = 1.0 - NextDouble();
            double u2 = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        public ulong[] GetState() => new[] { _s0, _s1, _s2, _s3 };

        public void SetState(ulong[] state)
        {
            if (state == null || state.Length != 4) throw new ArgumentException("Нужно 4 слова состояния.", nameof(state));
            _s0 = state[0];
            _s1 = state[1];
            _s2 = state[2];
            _s3 = state[3];
        }
    }
}
