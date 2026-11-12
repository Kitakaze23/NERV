using System.Diagnostics;

namespace Nerv.Core.Sim
{
    /// <summary>
    /// Накопленное время по фазам шага (для отладочного оверлея клиента и бенчмарков).
    /// Значения в миллисекундах с последнего <see cref="Reset"/>.
    /// </summary>
    public sealed class SimulationProfile
    {
        public long Ticks;
        public double ForcesMs;
        public double RulesMs;
        public double ReactionsMs;
        public double IdentifyMs;
        public double GroupsMs;
        public double TotalMs;

        public void Reset()
        {
            Ticks = 0;
            ForcesMs = RulesMs = ReactionsMs = IdentifyMs = GroupsMs = TotalMs = 0;
        }

        public double MsPerTick => Ticks > 0 ? TotalMs / Ticks : 0;

        internal static long Now() => Stopwatch.GetTimestamp();
        internal static double Ms(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        public override string ToString() =>
            Ticks == 0 ? "нет данных" :
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.000} мс/тик: силы {1:0.000}, правила {2:0.000} (реакции {3:0.000}), распознавание {4:0.000}, ассоциации {5:0.000}",
                TotalMs / Ticks, ForcesMs / Ticks, RulesMs / Ticks, ReactionsMs / Ticks, IdentifyMs / Ticks, GroupsMs / Ticks);
    }
}
