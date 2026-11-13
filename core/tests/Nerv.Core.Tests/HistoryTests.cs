using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Tests
{
    /// <summary>Слепки мира для отмены и повтора.</summary>
    public static class HistoryTests
    {
        private static string Composition(Simulation sim) =>
            string.Join(" ", sim.World.Molecules.Select(m => m.Formula).OrderBy(f => f, System.StringComparer.Ordinal));

        [Test]
        public static void History_RestoreUndoesCombustion()
        {
            Simulation sim = TestUtil.NewSim(300, 5, withData: true);
            sim.Settings.BoxHalfSize = 8;
            for (int i = 0; i < 4; i++) TestUtil.H2(sim, new Vec3(-4 + 2.6 * i, -2, 0));
            for (int i = 0; i < 2; i++) TestUtil.O2(sim, new Vec3(-3 + 4 * i, 2.5, 0));
            sim.Step(200);
            string before = Composition(sim);
            WorldState state = sim.Apply(new Checkpoint()).State;
            Assert.Equal(sim.World.AtomCount, state.AtomCount, "атомов в слепке");

            sim.Apply(new Spark(Vec3.Zero, 6));
            TestUtil.RunPs(sim, 3);
            Assert.True(Composition(sim) != before, "искра должна что-то изменить");

            sim.Apply(new RestoreState(state));
            Assert.Equal(before, Composition(sim), "состав после отмены");
            Assert.True(!sim.FlameActive, "пламя после отмены");
            sim.Step(400);
            Assert.Equal(before, Composition(sim), "смесь не загорается сама после отмены");
        }

        [Test]
        public static void History_RestoreKeepsGeometryAndVelocities()
        {
            Simulation sim = TestUtil.NewSim(300, 9);
            TestUtil.CH4(sim, Vec3.Zero);
            WorldState state = sim.Apply(new Checkpoint()).State;
            Vec3 p0 = sim.World.GetPosition(sim.World.IdAt(0)), v0 = sim.World.GetVelocity(sim.World.IdAt(0));
            sim.Step(500);
            sim.Apply(new RestoreState(state));
            Assert.Equal(5, sim.World.AtomCount, "атомов");
            Assert.Equal(4, sim.World.Bonds.Count, "связей");
            Assert.True(Vec3.Distance(sim.World.GetPosition(sim.World.IdAt(0)), p0) < 1e-9, "координаты");
            Assert.True(Vec3.Distance(sim.World.GetVelocity(sim.World.IdAt(0)), v0) < 1e-9, "скорости");
        }

        [Test]
        public static void History_RestoreEmptyClearsWorld()
        {
            Simulation sim = TestUtil.NewSim();
            WorldState empty = sim.Apply(new Checkpoint()).State;
            TestUtil.H2(sim, Vec3.Zero);
            sim.Apply(new Grab(sim.World.IdAt(0), Vec3.Zero));
            sim.Apply(new RestoreState(empty));
            Assert.Equal(0, sim.World.AtomCount, "мир пуст");
            sim.Step(10);
        }
    }
}
