using System;
using System.Linq;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Physics;
using Nerv.Core.Sim;
using static Nerv.Core.Tests.TestUtil;

namespace Nerv.Core.Tests
{
    public static class EngineTests
    {
        private static Simulation Scenario(ulong seed)
        {
            Simulation sim = NewSim(temperature: 298, seed: seed);
            Add(sim, "O", 0, 0, 0);
            Add(sim, "H", 2.5, 0, 0);
            Add(sim, "H", -1.25, 2.165, 0);
            Add(sim, "C", 6, 0, 0);
            Add(sim, "H", 7.2, 0.8, 0.8);
            Add(sim, "Na", 0, 6, 0);
            Add(sim, "Cl", 0, 9.2, 0);
            sim.Step(500);
            sim.Apply(new Spark(new Vec3(6, 0, 0), 3, 3000));
            sim.Step(500);
            return sim;
        }

        [Test]
        public static void Simulation_IsDeterministic()
        {
            Simulation a = Scenario(7), b = Scenario(7), c = Scenario(8);
            Assert.Equal(a.World.AtomCount, b.World.AtomCount);
            bool sameAsDifferentSeed = true;
            for (int i = 0; i < a.World.AtomCount; i++)
            {
                Assert.True(a.World.Positions[i] == b.World.Positions[i], "позиции совпадают побитово, атом " + i);
                if (a.World.Positions[i] != c.World.Positions[i]) sameAsDifferentSeed = false;
            }
            Assert.False(sameAsDifferentSeed, "другой seed даёт другую траекторию");
            Assert.Equal(a.Events.Count, b.Events.Count, "одинаковые события");
        }

        [Test]
        public static void RemoveAtom_InvalidatesIdAndReusesSlotSafely()
        {
            Simulation sim = NewSim();
            AtomId a = Add(sim, "C", 0, 0, 0);
            AtomId b = Add(sim, "C", 10, 0, 0);
            AtomId c = Add(sim, "C", 20, 0, 0);
            sim.Apply(new RemoveAtom(b));
            Assert.False(sim.World.IsAlive(b), "удалённый id недействителен");
            Assert.True(sim.World.IsAlive(a) && sim.World.IsAlive(c), "остальные живы");
            Assert.Equal(2, sim.World.AtomCount);
            Assert.Near(20, sim.World.GetPosition(c).X, 1e-9, "атом c на месте после перестановки");

            AtomId d = Add(sim, "O", 5, 5, 5);
            Assert.Equal(b.Slot, d.Slot, "слот переиспользован");
            Assert.False(d == b, "но поколение другое");
            Assert.False(sim.World.IsAlive(b), "старый id всё ещё недействителен");
        }

        [Test]
        public static void UnavailableElement_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new AddAtom("Fe", Vec3.Zero));
        }

        [Test]
        public static void Grab_PullsAtomToTarget()
        {
            Simulation sim = NewSim();
            AtomId o = Add(sim, "O", 0, 0, 0);
            sim.Apply(new Grab(o, new Vec3(5, 0, 0)));
            RunPs(sim, 1);
            Assert.True(Vec3.Distance(sim.World.GetPosition(o), new Vec3(5, 0, 0)) < 1.0, "атом подтянут к руке");
            sim.Apply(new Release());
        }

        [Test]
        public static void Events_ReportBondLifecycle()
        {
            Simulation sim = NewSim();
            Add(sim, "O", 0, 0, 0);
            Add(sim, "H", 2.5, 0, 0);
            Add(sim, "H", -1.25, 2.165, 0);
            RunPs(sim, 3);
            Assert.Equal(3, sim.Events.Count(e => e.Type == SimEventType.AtomAdded), "AtomAdded");
            Assert.Equal(2, sim.Events.Count(e => e.Type == SimEventType.BondForming), "BondForming");
            Assert.Equal(2, sim.Events.Count(e => e.Type == SimEventType.BondFormed), "BondFormed");
            Assert.True(sim.Events.Any(e => e.Type == SimEventType.TopologyChanged), "TopologyChanged");
            Assert.Equal(3, sim.CommandLog.Count, "журнал команд");
        }

        [Test]
        public static void Snapshot_CopiesState()
        {
            Simulation sim = NewSim();
            Add(sim, "O", 0, 0, 0);
            Add(sim, "H", 2.5, 0, 0);
            Add(sim, "H", -1.25, 2.165, 0);
            RunPs(sim, 2);
            var snap = new WorldSnapshot();
            sim.WriteSnapshot(snap);
            Assert.Equal(3, snap.AtomCount);
            Assert.Equal(2, snap.Bonds.Count);
            Assert.Equal("H2O", snap.Molecules.Single().Formula);
            Assert.Equal(8, snap.AtomicNumbers[0]);
            Assert.Near(sim.World.Positions[1].X, snap.Positions[3], 1e-5, "координаты");
        }

        [Test]
        public static void Medium_ChangesDielectric()
        {
            Simulation sim = NewSim();
            Assert.Near(1, sim.Settings.Dielectric, 1e-12);
            sim.Apply(new SetMedium(Medium.Water));
            Assert.Near(80, sim.Settings.Dielectric, 1e-12);
        }

        /// <summary>Силы должны быть градиентом энергии: проверка конечными разностями.</summary>
        [Test]
        public static void ForceField_MatchesFiniteDifferences()
        {
            var sim = NewSim();
            var w = sim.World;
            // Метан с искажённой геометрией.
            AtomId c = Add(sim, "C", 0, 0, 0);
            AtomId[] hs =
            {
                Add(sim, "H", 0.70, 0.62, 0.60),
                Add(sim, "H", 0.55, -0.70, -0.66),
                Add(sim, "H", -0.68, 0.58, -0.75),
                Add(sim, "H", -0.60, -0.66, 0.71)
            };
            foreach (AtomId h in hs) sim.Apply(new AddBond(c, h));
            // Вода рядом — работает Ван-дер-Ваальс между молекулами.
            AtomId o = Add(sim, "O", 3.4, 0.2, 0.1);
            AtomId h1 = Add(sim, "H", 4.3, 0.5, 0.0);
            AtomId h2 = Add(sim, "H", 3.2, -0.7, 0.4);
            sim.Apply(new AddBond(o, h1));
            sim.Apply(new AddBond(o, h2));
            // Ионы — Кулон.
            Add(sim, "Na", 0, 5, 0, charge: +1);
            Add(sim, "Cl", 0.4, 7.6, 0.3, charge: -1);

            SimulationSettings s = sim.Settings;
            ForceField.Compute(w, s);
            Vec3[] analytic = new Vec3[w.AtomCount];
            Array.Copy(w.Forces, analytic, w.AtomCount);

            const double step = 1e-5;
            for (int i = 0; i < w.AtomCount; i++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    Vec3 orig = w.Positions[i];
                    w.Positions[i] = Shift(orig, axis, +step);
                    double ePlus = ForceField.Compute(w, s);
                    w.Positions[i] = Shift(orig, axis, -step);
                    double eMinus = ForceField.Compute(w, s);
                    w.Positions[i] = orig;
                    double numeric = -(ePlus - eMinus) / (2 * step);
                    double an = axis == 0 ? analytic[i].X : axis == 1 ? analytic[i].Y : analytic[i].Z;
                    Assert.Near(numeric, an, 1e-3 * Math.Max(1, Math.Abs(numeric)),
                        "сила атома " + i + " (" + w.Elements[i].Symbol + ") по оси " + axis);
                }
            }
        }

        private static Vec3 Shift(Vec3 v, int axis, double d) =>
            axis == 0 ? new Vec3(v.X + d, v.Y, v.Z) : axis == 1 ? new Vec3(v.X, v.Y + d, v.Z) : new Vec3(v.X, v.Y, v.Z + d);

        [Test]
        public static void Performance_200AtomsStepsFast()
        {
            Simulation sim = NewSim(temperature: 298);
            var rng = new Xoshiro256(3);
            string[] pool = { "H", "H", "H", "C", "O", "N" };
            for (int i = 0; i < 200; i++)
            {
                string sym = pool[(int)(rng.NextDouble() * pool.Length)];
                Add(sim, sym, rng.NextDouble() * 30, rng.NextDouble() * 30, rng.NextDouble() * 30);
            }
            sim.Step(20); // прогрев
            var sw = System.Diagnostics.Stopwatch.StartNew();
            sim.Step(200);
            double msPerTick = sw.Elapsed.TotalMilliseconds / 200;
            Console.WriteLine("      200 атомов: " + msPerTick.ToString("0.000") + " мс/тик, молекул: " + sim.World.Molecules.Count);
            Assert.True(msPerTick < 5.0, "шаг должен быть быстрым, получено " + msPerTick + " мс");
        }
    }
}

namespace Nerv.Core.Tests
{
    public static class StabilityTests
    {
        /// <summary>Случайная смесь H/C/N/O/Na/Cl, 20 пс: нет NaN, валентности не превышены, термостат держит температуру.</summary>
        [Test]
        public static void Sandbox_RandomMixStaysStable()
        {
            Simulation sim = NewSim(temperature: 298, seed: 11);
            var rng = new Xoshiro256(5);
            string[] pool = { "H", "H", "H", "H", "C", "O", "O", "N", "Na", "Cl" };
            for (int i = 0; i < 80; i++)
            {
                string sym = pool[(int)(rng.NextDouble() * pool.Length)];
                Add(sim, sym, rng.NextDouble() * 16, rng.NextDouble() * 16, rng.NextDouble() * 16);
            }
            RunPs(sim, 20);

            World w = sim.World;
            for (int i = 0; i < w.AtomCount; i++)
            {
                Vec3 p = w.Positions[i];
                Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z), "NaN в координатах");
                int max = Nerv.Core.Chemistry.ElectronRules.MaxValence(w.Elements[i], w.Charges[i]);
                Assert.True(w.BondOrderSums[i] <= max, "валентность превышена у " + w.Elements[i].Symbol);
            }
            int totalCharge = 0;
            for (int i = 0; i < w.AtomCount; i++) totalCharge += w.Charges[i];
            Assert.Equal(0, totalCharge, "заряд сохраняется");
            Assert.Near(298, sim.Temperature, 150, "температура");

            var hist = w.Molecules.GroupBy(m => m.ToString()).OrderByDescending(g => g.Count())
                .Select(g => g.Key + "×" + g.Count());
            Console.WriteLine("      состав: " + string.Join(", ", hist));
        }
    }
}
