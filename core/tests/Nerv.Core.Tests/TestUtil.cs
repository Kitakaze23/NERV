using System;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Tests
{
    internal static class TestUtil
    {
        public static Simulation NewSim(double temperature = 10.0, ulong seed = 42)
        {
            var settings = new SimulationSettings { TargetTemperature = temperature };
            return new Simulation(settings, seed);
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
