using System;
using Nerv.Core.Model;
using Nerv.Core.Numerics;

namespace Nerv.Core.Physics
{
    public static class Thermostat
    {
        /// <summary>Кинетическая энергия, ккал/моль.</summary>
        public static double KineticEnergy(World w)
        {
            double ke = 0;
            for (int i = 0; i < w.AtomCount; i++) ke += 0.5 * w.Masses[i] * w.Velocities[i].LengthSquared;
            return ke / Units.AccelFactor;
        }

        /// <summary>Мгновенная температура, K (3N степеней свободы).</summary>
        public static double Temperature(World w)
        {
            int n = w.AtomCount;
            if (n == 0) return 0;
            return 2.0 * KineticEnergy(w) / (3.0 * n * Units.Kb);
        }

        /// <summary>Термостат Берендсена: плавно масштабирует скорости к целевой температуре.</summary>
        public static void Berendsen(World w, double target, double dtFs, double tauFs)
        {
            double t = Temperature(w);
            if (t < 1e-9) return;
            double lambda = Math.Sqrt(1.0 + dtFs / tauFs * (target / t - 1.0));
            if (double.IsNaN(lambda)) lambda = 0.9;
            lambda = Math.Max(0.9, Math.Min(1.1, lambda));
            for (int i = 0; i < w.AtomCount; i++) w.Velocities[i] *= lambda;
        }

        /// <summary>Скорость из распределения Максвелла при температуре T, Å/фс.</summary>
        public static Vec3 SampleVelocity(Xoshiro256 rng, double mass, double temperature)
        {
            if (temperature <= 0 || mass <= 0) return Vec3.Zero;
            double sigma = Math.Sqrt(Units.Kb * temperature * Units.AccelFactor / mass);
            return new Vec3(rng.NextGaussian() * sigma, rng.NextGaussian() * sigma, rng.NextGaussian() * sigma);
        }
    }
}
