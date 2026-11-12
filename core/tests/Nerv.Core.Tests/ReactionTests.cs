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
            Assert.True(ignited >= 5, "искра поджигает смесь почти всегда (" + ignited + "/6)");
            Assert.True(water >= 18, "образуется вода (" + water + " из 48)");
            Assert.True(chain, "работают цепные шаги с радикалами");
            Assert.True(heat, "выделяется тепло");
            Assert.True(mapOk, "карта атомов шага 1: 4 разных атома");
        }

        /// <summary>Сцена клиента «Гремучий газ»: 12 H₂ + 6 O₂, коробка ±6 Å, искра в центр (как щелчок в клиенте).</summary>
        [Test]
        public static void Flame_BurnsMostOfHydrogen()
        {
            int water = 0, fizzled = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                Simulation sim = NewSim(temperature: 298, seed: 1000 + seed, withData: true);
                sim.Settings.BoxHalfSize = 6;
                for (int k = 0; k < 18; k++)
                {
                    var at = new Vec3((k % 3) * 3.2 - 3.2, ((k / 3) % 3) * 3.2 - 3.2, (k / 9) * 3.2 - 1.6);
                    if (k % 3 == 2) O2(sim, at); else H2(sim, at);
                }
                sim.Step(400);
                sim.Apply(new Spark(Vec3.Zero, radius: 4, kelvin: 6000));
                RunPs(sim, 10);
                int w = sim.World.Molecules.Count(m => m.ToString() == "H2O");
                water += w;
                if (w < 5) fizzled++;
                Console.WriteLine("      seed " + seed + ": H₂O " + w + "/12, " + Formulas(sim));
            }
            Assert.True(fizzled <= 1, "пламя проходит по смеси, а не гаснет (не выгорело: " + fizzled + "/8)");
            Assert.True(water >= 0.7 * 96, "выгорает большая часть смеси (воды " + water + " из 96)");
        }

        /// <summary>Метан 6 CH₄ + 12 O₂: основные продукты — CO₂ и вода, часть углерода остаётся в CO и промежуточных.</summary>
        [Test]
        public static void Flame_BurnsMethane()
        {
            int co2 = 0, water = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                Simulation sim = NewSim(temperature: 298, seed: 1000 + seed, withData: true);
                sim.Settings.BoxHalfSize = 6;
                for (int k = 0; k < 18; k++)
                {
                    var at = new Vec3((k % 3) * 3.4 - 3.4, ((k / 3) % 3) * 3.4 - 3.4, (k / 9) * 3.4 - 1.7);
                    if (k % 3 == 1) CH4(sim, at); else O2(sim, at);
                }
                sim.Step(400);
                sim.Apply(new Spark(Vec3.Zero, radius: 4, kelvin: 6000));
                RunPs(sim, 10);
                co2 += sim.World.Molecules.Count(m => m.ToString() == "CO2");
                water += sim.World.Molecules.Count(m => m.ToString() == "H2O");
                Console.WriteLine("      seed " + seed + ": " + Formulas(sim));
            }
            Assert.True(co2 >= 0.4 * 36, "углерод сгорает до CO₂ (" + co2 + " из 36)");
            Assert.True(water >= 0.6 * 72, "водород сгорает до воды (" + water + " из 72)");
        }

        [Test]
        public static void Methane_Burns()
        {
            int burned = 0, water = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                Simulation sim = NewSim(temperature: 300, seed: seed, withData: true);
                sim.Settings.BoxHalfSize = 5;
                CH4(sim, new Vec3(0, 0, 0));
                O2(sim, new Vec3(2.9, 0.3, 0));
                O2(sim, new Vec3(-2.9, -0.2, 0.3));
                O2(sim, new Vec3(0.2, 3.0, 0));
                O2(sim, new Vec3(0, -3.0, 0.2));
                RunPs(sim, 0.3);
                sim.Apply(new Spark(Vec3.Zero, radius: 5, kelvin: 4000));
                RunPs(sim, 8);
                bool methaneGone = !sim.World.Molecules.Any(m => m.Name == "Метан");
                if (methaneGone && sim.ReactionLog.Any(r => r.Reaction.Group == "ch4_combustion")) burned++;
                water += CountNamed(sim, "Вода");
                Console.WriteLine("      seed " + seed + ": " + Formulas(sim) + ", шаги: " +
                                  string.Join(",", sim.ReactionLog.Select(r => r.Reaction.Id.Replace("ch4_", "").Replace("h2_", "h2:"))));
            }
            Assert.True(burned >= 4, "метан сгорает после искры (" + burned + "/6)");
            Assert.True(water >= 3, "образуется вода (" + water + ")");
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
