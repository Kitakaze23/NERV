using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Tests
{
    /// <summary>Самопроизвольный распад неустойчивых частиц.</summary>
    public static class DecayTests
    {
        private static Simulation Lone(string smiles, double kelvin, ulong seed)
        {
            Simulation sim = TestUtil.NewSim(kelvin, seed, withData: true);
            sim.Apply(new BuildMolecule(smiles, Vec3.Zero));
            sim.Step(4);
            if (sim.World.Molecules.Count != 1)
                throw new System.Exception(smiles + " после сборки: " + string.Join(", ", sim.World.Molecules.Select(m => m.Formula)) +
                    " / события: " + string.Join(", ", sim.Events.Select(e => e.Type + ":" + e.Text)));
            return sim;
        }

        private static System.Collections.Generic.List<SimEvent> Drain(Simulation sim)
        {
            var list = sim.Events.ToList();
            sim.ClearEvents();
            return list;
        }

        private static bool Decayed(Simulation sim) =>
            Drain(sim).Any(e => e.Type == SimEventType.MoleculeDecayed);

        [Test]
        public static void Decay_UnknownPeroxideFallsApart()
        {
            // Диэтилпероксида нет в базе, а связь O–O слабая — частица неустойчива и распадается на два радикала.
            int decayed = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                Simulation sim = Lone("CCOOCC", 300, seed);
                Assert.True(sim.World.Molecules.Single().Unstable, "пероксид помечен как неустойчивый");
                TestUtil.RunPs(sim, 20);
                if (sim.World.Molecules.Count >= 2) decayed++;
            }
            Assert.True(decayed >= 5, "распалось за 20 пс: " + decayed + " из 6");
        }

        [Test]
        public static void Decay_ColdStabilizes()
        {
            for (ulong seed = 1; seed <= 4; seed++)
            {
                Simulation sim = Lone("CCOOCC", 80, seed);
                Drain(sim);
                TestUtil.RunPs(sim, 20);
                Assert.True(!Decayed(sim) && sim.World.Molecules.Count == 1, "на холоде пероксид живёт");
            }
        }

        [Test]
        public static void Decay_KnownUnstableAndEventText()
        {
            int decayed = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                Simulation sim = Lone("COO", 300, seed);
                Assert.True(sim.World.Molecules.Single().Unstable, "метилгидропероксид помечен в базе");
                Drain(sim);
                TestUtil.RunPs(sim, 20);
                SimEvent e = Drain(sim).FirstOrDefault(x => x.Type == SimEventType.MoleculeDecayed);
                if (e.Type == SimEventType.MoleculeDecayed)
                {
                    decayed++;
                    Assert.Equal("CH4O2", e.Text, "формула распавшейся частицы");
                }
            }
            Assert.True(decayed >= 5, "распалось за 20 пс: " + decayed + " из 6");
        }

        [Test]
        public static void Decay_StableAndRadicalsStay()
        {
            foreach (string s in new[] { "OO", "NN", "[O]O", "O", "[O][O]", "[N][N]" })
            {
                Simulation sim = Lone(s, 300, 3);
                Assert.True(!sim.World.Molecules.Single().Unstable, s + " не помечен неустойчивым");
                Drain(sim);
                TestUtil.RunPs(sim, 10);
                Assert.True(!Decayed(sim), s + " не распадается сам");
            }
        }
    }
}
