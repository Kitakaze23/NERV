using System;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Sim;
using static Nerv.Core.Tests.TestUtil;

namespace Nerv.Core.Tests
{
    /// <summary>Сценарии «собери молекулу»: проверяем химию, а не конкретные числа траекторий.</summary>
    public static class ChemistryTests
    {
        [Test]
        public static void Water_FormsWithBentGeometry()
        {
            Simulation sim = NewSim();
            AtomId o = Add(sim, "O", 0, 0, 0);
            AtomId h1 = Add(sim, "H", 2.5, 0, 0);
            AtomId h2 = Add(sim, "H", -1.25, 2.165, 0);
            RunPs(sim, 3);

            Assert.Equal("H2O", Formulas(sim), "формула");
            Assert.Equal(2, sim.World.Bonds.Count, "связей");
            Assert.True(sim.World.Bonds.All(b => b.IsFormed && b.IsPolar), "связи сформированы и полярны");
            Assert.Equal(0, sim.World.GetFreeValence(o), "свободная валентность O");
            Assert.Equal(2, sim.World.GetLonePairs(o), "неподелённые пары O");
            Assert.Equal("O_3", sim.World.GetAtomType(o).Label, "тип O");
            Assert.Near(104.5, AngleDeg(sim, h1, o, h2), 6.0, "угол H–O–H");
            Assert.Near(0.99, Distance(sim, o, h1), 0.08, "длина O–H");
        }

        [Test]
        public static void Methane_FormsTetrahedron()
        {
            Simulation sim = NewSim();
            AtomId c = Add(sim, "C", 0, 0, 0);
            double d = 2.3 / Math.Sqrt(3);
            AtomId[] h =
            {
                Add(sim, "H", d, d, d),
                Add(sim, "H", d, -d, -d),
                Add(sim, "H", -d, d, -d),
                Add(sim, "H", -d, -d, d)
            };
            RunPs(sim, 4);

            Assert.Equal("CH4", Formulas(sim), "формула");
            Assert.Equal("C_3", sim.World.GetAtomType(c).Label, "sp3");
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    Assert.Near(109.47, AngleDeg(sim, h[i], c, h[j]), 6.0, "угол H–C–H");
        }

        [Test]
        public static void Hydrogen_FormsH2()
        {
            Simulation sim = NewSim();
            Add(sim, "H", 0, 0, 0);
            Add(sim, "H", 2.0, 0, 0);
            RunPs(sim, 1);
            Assert.Equal("H2", Formulas(sim));
            Assert.Equal(1, sim.World.Bonds[0].Order, "порядок");
        }

        [Test]
        public static void Oxygen_FormsDoubleBond()
        {
            Simulation sim = NewSim();
            AtomId a = Add(sim, "O", 0, 0, 0);
            AtomId b = Add(sim, "O", 2.5, 0, 0);
            RunPs(sim, 2);
            Assert.Equal("O2", Formulas(sim));
            Assert.Equal(2, sim.World.FindBond(a, b).Order, "O=O");
            Assert.Equal(0, sim.World.GetFreeValence(a), "валентность насыщена");
            Assert.Equal("O_2", sim.World.GetAtomType(a).Label, "sp2");
        }

        [Test]
        public static void Nitrogen_FormsTripleBond()
        {
            Simulation sim = NewSim();
            AtomId a = Add(sim, "N", 0, 0, 0);
            AtomId b = Add(sim, "N", 2.5, 0, 0);
            RunPs(sim, 3);
            Assert.Equal("N2", Formulas(sim));
            Assert.Equal(3, sim.World.FindBond(a, b).Order, "N≡N");
            Assert.Equal("N_1", sim.World.GetAtomType(a).Label, "sp");
        }

        [Test]
        public static void CarbonDioxide_IsLinear()
        {
            Simulation sim = NewSim();
            AtomId c = Add(sim, "C", 0, 0, 0);
            AtomId o1 = Add(sim, "O", 2.0, 0.3, 0);
            AtomId o2 = Add(sim, "O", -2.0, 0, 0.3);
            RunPs(sim, 5);
            Assert.Equal("CO2", Formulas(sim));
            Assert.Equal(2, sim.World.FindBond(c, o1).Order, "C=O");
            Assert.Equal(2, sim.World.FindBond(c, o2).Order, "C=O");
            Assert.Near(180, AngleDeg(sim, o1, c, o2), 12, "O=C=O линейна");
        }

        [Test]
        public static void SodiumChloride_TransfersElectron()
        {
            Simulation sim = NewSim();
            AtomId na = Add(sim, "Na", 0, 0, 0);
            AtomId cl = Add(sim, "Cl", 3.4, 0, 0);
            RunPs(sim, 2);

            Assert.Equal(1, sim.World.GetCharge(na), "Na+");
            Assert.Equal(-1, sim.World.GetCharge(cl), "Cl-");
            Assert.Equal(0, sim.World.Bonds.Count, "ионная связь — не ковалентная");
            Assert.True(sim.Events.Any(e => e.Type == SimEventType.ElectronTransferred), "событие переноса");
            Assert.True(Distance(sim, na, cl) < 3.6, "ионы держатся вместе: " + Distance(sim, na, cl));
        }

        [Test]
        public static void HotHydrogen_BondBreaks()
        {
            Simulation sim = NewSim(temperature: 300);
            AtomId a = Add(sim, "H", 0, 0, 0);
            AtomId b = Add(sim, "H", 0.74, 0, 0);
            sim.Apply(new AddBond(a, b));
            sim.Apply(new SetTemperature(40000));
            RunPs(sim, 3);
            Assert.True(sim.Events.Any(e => e.Type == SimEventType.BondBroken), "связь должна порваться при нагреве");
        }

        [Test]
        public static void SaturatedMolecules_DoNotStick()
        {
            // Две готовые молекулы воды не должны «слипаться» ковалентно.
            Simulation sim = NewSim();
            AtomId o1 = Add(sim, "O", 0, 0, 0);
            Add(sim, "H", 2.5, 0, 0);
            Add(sim, "H", -1.25, 2.165, 0);
            AtomId o2 = Add(sim, "O", 0, 0, 7);
            Add(sim, "H", 2.5, 0, 7);
            Add(sim, "H", -1.25, 2.165, 7);
            RunPs(sim, 3);
            Assert.Equal("H2O H2O", Formulas(sim));
            Assert.True(Distance(sim, o1, o2) > 2.4, "молекулы не проникают друг в друга");
        }
    }
}
