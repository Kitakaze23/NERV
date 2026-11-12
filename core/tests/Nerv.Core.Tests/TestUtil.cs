using System;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Tests
{
    internal static class TestUtil
    {
        private static ChemistryData _data;

        /// <summary>Данные из core/data (ищем вверх от папки сборки).</summary>
        public static ChemistryData ChemData
        {
            get
            {
                if (_data != null) return _data;
                var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "data", "reactions.yaml")))
                    dir = dir.Parent;
                if (dir == null) throw new InvalidOperationException("Не найдена папка core/data.");
                _data = ChemistryData.LoadDirectory(System.IO.Path.Combine(dir.FullName, "data"));
                return _data;
            }
        }

        public static Simulation NewSim(double temperature = 10.0, ulong seed = 42, bool withData = false)
        {
            var settings = new SimulationSettings { TargetTemperature = temperature };
            return new Simulation(settings, seed, withData ? ChemData : null);
        }

        /// <summary>Готовая молекула из атомов и связей (для сценариев реакций).</summary>
        public static AtomId[] Mol(Simulation sim, Vec3 at, params (string sym, double x, double y, double z, int charge)[] atoms)
        {
            var ids = new AtomId[atoms.Length];
            for (int i = 0; i < atoms.Length; i++)
                ids[i] = sim.Apply(new AddAtom(atoms[i].sym, at + new Vec3(atoms[i].x, atoms[i].y, atoms[i].z), atoms[i].charge)).Result;
            return ids;
        }

        public static AtomId[] H2(Simulation sim, Vec3 at)
        {
            AtomId[] a = Mol(sim, at, ("H", -0.37, 0, 0, 0), ("H", 0.37, 0, 0, 0));
            sim.Apply(new AddBond(a[0], a[1], 1));
            return a;
        }

        public static AtomId[] O2(Simulation sim, Vec3 at)
        {
            AtomId[] a = Mol(sim, at, ("O", -0.6, 0, 0, 0), ("O", 0.6, 0, 0, 0));
            sim.Apply(new AddBond(a[0], a[1], 2));
            return a;
        }

        public static AtomId[] CH4(Simulation sim, Vec3 at)
        {
            double d = 1.09 / Math.Sqrt(3);
            AtomId[] a = Mol(sim, at, ("C", 0, 0, 0, 0), ("H", d, d, d, 0), ("H", d, -d, -d, 0), ("H", -d, d, -d, 0), ("H", -d, -d, d, 0));
            for (int i = 1; i < 5; i++) sim.Apply(new AddBond(a[0], a[i], 1));
            return a;
        }

        public static int CountNamed(Simulation sim, string name)
        {
            int n = 0;
            foreach (var m in sim.World.Molecules) if (m.Name == name) n++;
            return n;
        }

        public static AtomId Add(Simulation sim, string symbol, double x, double y, double z, int charge = 0) =>
            sim.Apply(new AddAtom(symbol, new Vec3(x, y, z), charge)).Result;

        /// <summary>Прогнать симуляцию на заданное число пикосекунд.</summary>
        public static void RunPs(Simulation sim, double ps) =>
            sim.Step((int)Math.Round(ps * 1000.0 / sim.Settings.TimeStepFs));

        public static double AngleDeg(Simulation sim, AtomId a, AtomId center, AtomId b)
        {
            Vec3 u = sim.World.GetPosition(a) - sim.World.GetPosition(center);
            Vec3 v = sim.World.GetPosition(b) - sim.World.GetPosition(center);
            double c = Vec3.Dot(u, v) / (u.Length * v.Length);
            return Math.Acos(Math.Max(-1, Math.Min(1, c))) * 180.0 / Math.PI;
        }

        public static double Distance(Simulation sim, AtomId a, AtomId b) =>
            Vec3.Distance(sim.World.GetPosition(a), sim.World.GetPosition(b));

        public static string Formulas(Simulation sim) =>
            string.Join(" ", sim.World.Molecules.Select(m => m.ToString()).OrderBy(s => s, StringComparer.Ordinal));
    }
}
