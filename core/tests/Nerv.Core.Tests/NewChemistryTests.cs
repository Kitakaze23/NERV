using System;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Tests
{
    /// <summary>Второй набор химии: щелочные металлы и вода, горение магния, галогены с водородом и метаном.</summary>
    public static class NewChemistryTests
    {
        /// <summary>Сцена в коробке: вещества по SMILES раскладываются по сетке (ядро само раздвигает при тесноте).</summary>
        private static Simulation Scene(ulong seed, double kelvin, params (string smiles, int count)[] parts)
        {
            Simulation sim = TestUtil.NewSim(kelvin, seed, withData: true);
            sim.Settings.BoxHalfSize = 6;
            int k = 0;
            foreach ((string smiles, int count) in parts)
                for (int i = 0; i < count; i++, k++)
                {
                    double x = -4.5 + 3 * (k % 4), y = -4.5 + 3 * (k / 4 % 4), z = -2 + 3.5 * (k / 16);
                    sim.Apply(new BuildMolecule(smiles, new Vec3(x, y, z)));
                }
            sim.Step(20);
            return sim;
        }

        private static int Count(Simulation sim, string formula, int charge = 0) =>
            sim.World.Molecules.Count(m => m.Formula == formula && m.Charge == charge);

        private static int Seeds(Func<ulong, bool> trial, int seeds = 4)
        {
            int ok = 0;
            for (ulong s = 1; s <= (ulong)seeds; s++) if (trial(s)) ok++;
            return ok;
        }

        [Test]
        public static void Alkali_ReactsWithWater()
        {
            foreach (string metal in new[] { "Na", "K" })
            {
                int ok = Seeds(seed =>
                {
                    Simulation sim = Scene(seed, 300, ("[" + metal + "]", 4), ("O", 8));
                    TestUtil.RunPs(sim, 15);
                    int hydroxide = Count(sim, "HO", -1), cations = Count(sim, metal, +1), h2 = Count(sim, "H2");
                    return hydroxide >= 2 && cations >= 2 && h2 >= 1;
                });
                Assert.True(ok >= 3, metal + " + вода: гидроксид и водород в " + ok + " из 4 прогонов");
            }
        }

        [Test]
        public static void Magnesium_BurnsAfterSpark()
        {
            int ok = Seeds(seed =>
            {
                Simulation sim = Scene(seed, 300, ("[Mg]", 6), ("O=O", 4));
                TestUtil.RunPs(sim, 2);
                bool quiet = Count(sim, "O", -2) == 0; // без поджига магний на воздухе не горит
                sim.Apply(new Spark(Vec3.Zero, 6));
                TestUtil.RunPs(sim, 10);
                return quiet && Count(sim, "O", -2) >= 3;
            });
            Assert.True(ok >= 3, "магний сгорел в MgO в " + ok + " из 4 прогонов");
        }

        [Test]
        public static void HydrogenChlorine_ChainNeedsSpark()
        {
            int ok = Seeds(seed =>
            {
                Simulation sim = Scene(seed, 300, ("[H][H]", 6), ("ClCl", 6));
                TestUtil.RunPs(sim, 3);
                bool dark = Count(sim, "ClH") == 0; // «в темноте» смесь стоит
                sim.Apply(new Spark(Vec3.Zero, 6));
                TestUtil.RunPs(sim, 10);
                return dark && Count(sim, "ClH") >= 6;
            });
            Assert.True(ok >= 3, "H₂ + Cl₂ → HCl после искры в " + ok + " из 4 прогонов");
        }

        [Test]
        public static void Methane_IsChlorinated()
        {
            int ok = Seeds(seed =>
            {
                Simulation sim = Scene(seed, 300, ("C", 4), ("ClCl", 8));
                sim.Apply(new Spark(Vec3.Zero, 6));
                TestUtil.RunPs(sim, 12);
                int chloro = sim.World.Molecules.Count(m => m.Formula.StartsWith("C", StringComparison.Ordinal) && m.Formula.Contains("Cl") && m.Charge == 0);
                return chloro >= 2 && Count(sim, "ClH") >= 2;
            });
            Assert.True(ok >= 3, "хлорметаны и HCl в " + ok + " из 4 прогонов");
        }

        [Test]
        public static void Potassium_BeatsLithium()
        {
            // Калий реагирует с водой быстрее лития — как в настоящем ряду активности.
            int k = 0, li = 0;
            for (ulong seed = 1; seed <= 4; seed++)
            {
                Simulation a = Scene(seed, 300, ("[K]", 4), ("O", 8));
                TestUtil.RunPs(a, 3);
                k += Count(a, "K", +1);
                Simulation b = Scene(seed, 300, ("[Li]", 4), ("O", 8));
                TestUtil.RunPs(b, 3);
                li += Count(b, "Li", +1);
            }
            Assert.True(k > li, "K⁺ " + k + " против Li⁺ " + li + " за 3 пс");
        }
    }
}
