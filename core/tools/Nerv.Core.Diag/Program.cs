using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nerv.Core;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Diag
{
    /// <summary>
    /// Диагностика горения: сцены клиента «Гремучий газ» и «Горение метана», искра как в клиенте
    /// (радиус 4 Å, 6000 K), перепись веществ по времени. Запуск: dotnet run -c Release -- [h2|ch4] [seeds] [ps]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string scene = args.Length > 0 ? args[0] : "h2";
            int seeds = args.Length > 1 ? int.Parse(args[1]) : 4;
            double ps = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 10;
            bool verbose = args.Contains("-v");
            Names = args.Contains("-n");
            ChemistryData data = ChemistryData.LoadDirectory(FindData());
            var finals = new List<string>();
            int water = 0;
            var perSeed = new List<int>();
            for (int s = 1; s <= seeds; s++)
            {
                var settings = new SimulationSettings { TargetTemperature = 298, BoxHalfSize = 6 };
                foreach (string a in args)
                {
                    string[] kv = a.Split('=');
                    if (kv.Length != 2) continue;
                    double v = double.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                    var prop = typeof(SimulationSettings).GetProperty(kv[0]);
                    if (prop.PropertyType == typeof(int)) prop.SetValue(settings, (int)v); else prop.SetValue(settings, v);
                }
                var sim = new Simulation(settings, (ulong)(1000 + s), data);
                if (scene == "ch4") BuildCh4(sim); else BuildH2(sim);
                sim.Step(400);
                if (verbose)
                {
                    int near = 0;
                    foreach (var b in sim.World.Bonds)
                        if ((sim.World.Positions[b.Ia]).Length < 4 && (sim.World.Positions[b.Ib]).Length < 4) near++;
                    Console.WriteLine($"seed {s}: связей в радиусе искры {near}");
                }
                sim.Apply(new Spark(Vec3.Zero, 4, 6000));
                int ticksPerReport = 1000; // 0.5 пс
                int reports = (int)(ps * 2);
                int lastReactions = 0;
                double maxT = 0;
                for (int r = 1; r <= reports; r++)
                {
                    sim.Step(ticksPerReport);
                    int rc = sim.ReactionLog.Count;
                    double t = sim.Temperature;
                    maxT = Math.Max(maxT, t);
                    if (verbose || r == reports)
                        Console.WriteLine($"seed {s} t={r * 0.5,5:0.0} пс  T={t,6:0} K  реакций +{rc - lastReactions,3} (всего {rc,3})  | {Census(sim)}");
                    lastReactions = rc;
                }
                var byName = sim.ReactionLog.GroupBy(x => x.Reaction.Id).Select(g => g.Key + "×" + g.Count());
                Console.WriteLine($"   maxT {maxT:0} K  реакции: {string.Join(", ", byName)}");
                finals.Add(Census(sim));
                int got = Count(sim, scene == "ch4" ? "CO2" : "H2O");
                water += got;
                perSeed.Add(got);
            }
            int max = scene == "ch4" ? 6 : 12;
            int fails = perSeed.Count(x => x < 0.4 * max);
            Console.WriteLine($"ИТОГО продукта ({(scene == "ch4" ? "CO2" : "H2O")}): {water}/{max * seeds} ({100.0 * water / (max * seeds):0}%), неудач {fails}/{seeds}  [{string.Join(" ", perSeed)}]");
            return 0;
        }

        private static bool Names;

        private static int Count(Simulation sim, string f) => sim.World.Molecules.Count(m => m.ToString() == f);

        private static string Census(Simulation sim)
        {
            var counts = new Dictionary<string, int>();
            foreach (Molecule m in sim.World.Molecules)
            {
                string k = Names ? (m.Name ?? "?" + m.CanonicalKey) : m.ToString();
                counts.TryGetValue(k, out int c);
                counts[k] = c + 1;
            }
            return string.Join(" ", counts.OrderByDescending(k => k.Value).Select(k => k.Value + "·" + k.Key));
        }

        private static void BuildH2(Simulation sim)
        {
            for (int k = 0; k < 18; k++)
            {
                var at = new Vec3((k % 3) * 3.2 - 3.2, ((k / 3) % 3) * 3.2 - 3.2, (k / 9) * 3.2 - 1.6);
                if (k % 3 == 2) O2(sim, at); else H2(sim, at);
            }
        }

        private static void BuildCh4(Simulation sim)
        {
            // Как «Гремучий газ»: решётка 3×3×2, метан : кислород = 1 : 2 (стехиометрия).
            for (int k = 0; k < 18; k++)
            {
                var at = new Vec3((k % 3) * 3.4 - 3.4, ((k / 3) % 3) * 3.4 - 3.4, (k / 9) * 3.4 - 1.7);
                if (k % 3 == 1) CH4(sim, at); else O2(sim, at);
            }
        }

        private static AtomId Atom(Simulation sim, string s, double x, double y, double z) => sim.Apply(new AddAtom(s, new Vec3(x, y, z), 0)).Result;

        private static void H2(Simulation sim, Vec3 at)
        {
            AtomId a = Atom(sim, "H", at.X - 0.37, at.Y, at.Z), b = Atom(sim, "H", at.X + 0.37, at.Y, at.Z);
            sim.Apply(new AddBond(a, b, 1));
        }

        private static void O2(Simulation sim, Vec3 at)
        {
            AtomId a = Atom(sim, "O", at.X - 0.6, at.Y, at.Z), b = Atom(sim, "O", at.X + 0.6, at.Y, at.Z);
            sim.Apply(new AddBond(a, b, 2));
        }

        private static void CH4(Simulation sim, Vec3 at)
        {
            double d = 1.09 / Math.Sqrt(3);
            AtomId c = Atom(sim, "C", at.X, at.Y, at.Z);
            foreach (var s in new[] { (1, 1, 1), (1, -1, -1), (-1, 1, -1), (-1, -1, 1) })
                sim.Apply(new AddBond(c, Atom(sim, "H", at.X + s.Item1 * d, at.Y + s.Item2 * d, at.Z + s.Item3 * d), 1));
        }

        private static string FindData()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "data"))) dir = Path.GetDirectoryName(dir);
            return Path.Combine(dir ?? ".", "data");
        }
    }
}
