using System;
using System.IO;
using Nerv.Core;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Bench
{
    /// <summary>
    /// Бенчмарк: случайная смесь атомов MVP в коробке, «живая» песочница (реакции, распознавание).
    /// Печатает время на тик по фазам. Цель — меньше 0,2 мс/тик на 200 атомах.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            ChemistryData data = ChemistryData.LoadDirectory(FindData());
            foreach (int n in new[] { 100, 200, 500 })
            {
                Simulation sim = Build(n, data);
                sim.Step(2000); // прогрев: атомы собираются в молекулы
                sim.Profile.Reset();
                sim.Step(2000);
                Console.WriteLine(n + " атомов: " + sim.Profile + "  | молекул " + sim.World.Molecules.Count);
            }
            return 0;
        }

        private static Simulation Build(int n, ChemistryData data)
        {
            // Плотность ~ как в песочнице: 200 атомов в кубе ~22 Å.
            double half = 0.5 * Math.Pow(n / 200.0 * 22.0 * 22.0 * 22.0, 1.0 / 3.0);
            var settings = new SimulationSettings { TargetTemperature = 298, BoxHalfSize = half };
            var sim = new Simulation(settings, 7, data);
            var rng = new Xoshiro256(11);
            string[] pool = { "H", "H", "H", "H", "C", "O", "O", "N", "Na", "Cl" };
            for (int i = 0; i < n; i++)
            {
                string sym = pool[(int)(rng.NextDouble() * pool.Length)];
                var p = new Vec3((rng.NextDouble() * 2 - 1) * half, (rng.NextDouble() * 2 - 1) * half, (rng.NextDouble() * 2 - 1) * half);
                sim.Apply(new AddAtom(sym, p));
            }
            return sim;
        }

        private static string FindData()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "data", "reactions.yaml"))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? ".", "data");
        }
    }
}
