using System;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;
using static Nerv.Core.Tests.TestUtil;

namespace Nerv.Core.Tests
{
    /// <summary>Сценарии реакций MVP. Проверяем химию (что получилось), а не точные траектории.</summary>
    public static class ReactionTests
    {
        /// <summary>Плотная смесь 2:1 водорода и кислорода в коробке ~10 Å.</summary>
        private static Simulation HydrogenOxygenMix(ulong seed)
        {
            Simulation sim = NewSim(temperature: 300, seed: seed, withData: true);
            sim.Settings.BoxCenter = new Vec3(3.2, 1.6, 1.6);
            sim.Settings.BoxHalfSize = 5;
            for (int k = 0; k < 12; k++)
            {
                var at = new Vec3((k % 3) * 3.2, ((k / 3) % 2) * 3.2, (k / 6) * 3.2);
                if (k % 3 == 2) O2(sim, at);
                else H2(sim, at);
            }
            return sim;
        }

        private static readonly Vec3 MixCenter = new Vec3(3.2, 1.6, 1.6);

        [Test]
        public static void Hydrogen_DoesNotBurnWithoutSpark()
        {
            Simulation sim = HydrogenOxygenMix(1);
            RunPs(sim, 3);
            Assert.Equal(0, sim.ReactionLog.Count, "без искры реакции нет");
            Assert.Equal(0, CountNamed(sim, "Вода"));
        }

        [Test]
        public static void Hydrogen_BurnsAfterSpark()
        {
            // Горение вероятностное: проверяем статистику по нескольким зажиганиям.
            int ignited = 0, water = 0;
            bool chain = false, heat = false, mapOk = false;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                Simulation sim = HydrogenOxygenMix(seed);
                RunPs(sim, 0.5);
                sim.Apply(new Spark(MixCenter, radius: 6, kelvin: 6000));
                RunPs(sim, 6);

                int w = CountNamed(sim, "Вода");
                water += w;
                if (sim.ReactionLog.Count > 0) ignited++;
                chain |= sim.ReactionLog.Any(r => r.Reaction.Id.StartsWith("h2_chain", StringComparison.Ordinal));
                heat |= sim.Events.Any(e => e.Type == SimEventType.HeatReleased && e.Value > 0);
                ReactionRecord rec = sim.ReactionLog.FirstOrDefault(r => r.Reaction.Id == "h2_combustion_1");
                if (rec != null) mapOk |= rec.AtomMap.Count == 4 && rec.AtomMap.Values.Distinct().Count() == 4;
                Console.WriteLine("      seed " + seed + ": реакций " + sim.ReactionLog.Count + ", " + Formulas(sim));
            }
            Assert.True(ignited >= 3, "искра поджигает смесь в большинстве случаев (" + ignited + "/6)");
            Assert.True(water >= 6, "образуется вода (" + water + ")");
            Assert.True(chain, "работают цепные шаги с радикалами");
            Assert.True(heat, "выделяется тепло");
            Assert.True(mapOk, "карта атомов шага 1: 4 разных атома");
        }

        [Test]
        public static void Methane_Burns()
        {
            Simulation sim = NewSim(temperature: 300, seed: 5, withData: true);
            sim.Settings.BoxHalfSize = 5;
            CH4(sim, new Vec3(0, 0, 0));
            O2(sim, new Vec3(2.9, 0.3, 0));
            O2(sim, new Vec3(-2.9, -0.2, 0.3));
            O2(sim, new Vec3(0.2, 3.0, 0));
            O2(sim, new Vec3(0, -3.0, 0.2));
            RunPs(sim, 0.3);
            sim.Apply(new Spark(Vec3.Zero, radius: 5, kelvin: 4000));
            RunPs(sim, 10);

            Console.WriteLine("      состав: " + Formulas(sim) + ", реакций: " + sim.ReactionLog.Count);
            Assert.True(sim.ReactionLog.Any(r => r.Reaction.Id == "ch4_combustion_1"), "метан окислился");
            Assert.True(CountNamed(sim, "Вода") >= 1, "есть вода");
        }

        [Test]
        public static void Sodium_ReactsWithChlorine()
        {
            Simulation sim = NewSim(temperature: 300, seed: 3, withData: true);
            Add(sim, "Na", -2.9, 0, 0);
            Cl2At(sim, new Vec3(0, 0, 0));
            Add(sim, "Na", 2.9, 0.4, 0);
            RunPs(sim, 4);

            Console.WriteLine("      соли: " + string.Join(", ", sim.IonicGroups) + ", состав: " + Formulas(sim));
            Assert.True(sim.ReactionLog.Any(r => r.Reaction.Id == "na_cl2_1"), "шаг 1: Na + Cl₂");
            int na = sim.World.Molecules.Count(m => m.Formula == "Na" && m.Charge == 1);
            Assert.Equal(2, na, "оба натрия стали ионами");
            Assert.True(sim.IonicGroups.Any(g => g.Name == "Хлорид натрия"), "получилась поваренная соль");
        }

        private static void Cl2At(Simulation sim, Vec3 at)
        {
            AtomId[] a = Mol(sim, at, ("Cl", -1.0, 0, 0, 0), ("Cl", 1.0, 0, 0, 0));
            sim.Apply(new AddBond(a[0], a[1], 1));
        }

        [Test]
        public static void Neutralization_InWater()
        {
            Simulation sim = NewSim(temperature: 300, seed: 9, withData: true);
            sim.Settings.BoxCenter = new Vec3(-1, 0, 0);
            sim.Settings.BoxHalfSize = 4;
            sim.Apply(new SetMedium(Medium.Water));
            // HCl
            AtomId h = Add(sim, "H", 0, 0, 0);
            AtomId cl = Add(sim, "Cl", 1.27, 0, 0);
            sim.Apply(new AddBond(h, cl));
            // NaOH как ионы Na⁺ и OH⁻
            AtomId o = Add(sim, "O", -2.6, 0.5, 0, charge: -1);
            AtomId oh = Add(sim, "H", -3.3, 1.1, 0);
            sim.Apply(new AddBond(o, oh));
            Add(sim, "Na", -2.9, -2.3, 0, charge: +1);
            RunPs(sim, 8);

            Console.WriteLine("      состав: " + Formulas(sim) + ", реакции: " + string.Join(", ", sim.ReactionLog.Select(r => r.Reaction.Id)));
            Assert.True(sim.ReactionLog.Any(r => r.Reaction.Id == "hcl_dissociation"), "HCl распался на ионы");
            Assert.True(sim.ReactionLog.Any(r => r.Reaction.Id == "neutralization"), "нейтрализация");
            Assert.Equal(1, CountNamed(sim, "Вода"), "получилась вода");
            Assert.Equal(1, CountNamed(sim, "катион натрия"), "Na⁺ в растворе");
            Assert.Equal(1, CountNamed(sim, "хлорид-ион"), "Cl⁻ в растворе");

            sim.Apply(new SetMedium(Medium.Vacuum));
            RunPs(sim, 2);
            Assert.True(sim.IonicGroups.Any(g => g.Name == "Хлорид натрия") || sim.IonicGroups.Count == 0,
                "после «выпаривания» ионы могут собраться в соль");
        }

        [Test]
        public static void Reactions_AreDeterministic()
        {
            string Run()
            {
                Simulation sim = HydrogenOxygenMix(5);
                RunPs(sim, 0.3);
                sim.Apply(new Spark(MixCenter, radius: 6, kelvin: 6000));
                RunPs(sim, 2);
                return string.Join(",", sim.ReactionLog.Select(r => r.Tick + r.Reaction.Id)) + "|" + Formulas(sim);
            }
            Assert.Equal(Run(), Run(), "одинаковые прогоны");
        }
    }
}
