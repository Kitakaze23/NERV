using System;
using Nerv.Core.Data;

namespace Nerv.Core.Chemistry
{
    /// <summary>
    /// Электронный баланс атома. Всё вычисляется из элемента, формального заряда и суммы порядков связей —
    /// отдельно не хранится, поэтому рассинхрон «связь есть, а валентность не обновилась» невозможен.
    /// </summary>
    public static class ElectronRules
    {
        /// <summary>Число валентных электронов с учётом заряда.</summary>
        public static int ElectronCount(Element e, int charge) => e.ValenceElectrons - charge;

        /// <summary>
        /// Максимальная валентность: водород — дуплет, металлы отдают электроны,
        /// неметаллы — правило октета (n &lt; 4 → n, иначе 8 − n).
        /// Примеры: C → 4, N → 3, N⁺ → 4, O → 2, O⁻ → 1, Cl⁻ → 0, Na → 1, Na⁺ → 0.
        /// </summary>
        public static int MaxValence(Element e, int charge)
        {
            int n = ElectronCount(e, charge);
            if (n <= 0) return 0;
            if (e.Z == 1) return n == 1 ? 1 : 0;
            if (e.IsMetal) return n <= 3 ? n : 0;
            if (n >= 8) return 0;
            return n < 4 ? n : 8 - n;
        }

        /// <summary>Свободная валентность (сколько ещё связей может образовать атом).</summary>
        public static int FreeValence(Element e, int charge, int bondOrderSum) =>
            Math.Max(0, MaxValence(e, charge) - bondOrderSum);

        /// <summary>Неподелённые электронные пары.</summary>
        public static int LonePairs(Element e, int charge, int bondOrderSum)
        {
            int n = ElectronCount(e, charge);
            int free = FreeValence(e, charge, bondOrderSum);
            return Math.Max(0, (n - bondOrderSum - free) / 2);
        }

        /// <summary>Ионная пара: металл + неметалл с разностью электроотрицательностей больше порога.</summary>
        public static bool IsIonicPair(Element a, Element b, double threshold)
        {
            if (a.IsMetal == b.IsMetal) return false;
            if (double.IsNaN(a.Electronegativity) || double.IsNaN(b.Electronegativity)) return false;
            return Math.Abs(a.Electronegativity - b.Electronegativity) > threshold;
        }
    }
}
