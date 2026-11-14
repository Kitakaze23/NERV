using System;
using System.Collections.Generic;
using System.Linq;
using Nerv.Core.Chemistry;
using Nerv.Core.Model;
using Nerv.Core.Naming;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Tests
{
    /// <summary>Сборка молекул по SMILES («Собрать в сцене»).</summary>
    public static class BuilderTests
    {
        private static IEnumerable<CompoundInfo> Buildable() =>
            TestUtil.ChemData.Compounds.All.Where(c => c.Smiles.Any(char.IsLetter)).OrderBy(c => c.Smiles, StringComparer.Ordinal);

        [Test]
        public static void Builder_GeometryIsSaneForWholeDatabase()
        {
            foreach (CompoundInfo c in Buildable())
            {
                MoleculeTemplate t = MoleculeBuilder.Build(c.Smiles);
                var bonded = new HashSet<(int, int)>();
                var nb = new List<int>[t.AtomCount];
                for (int i = 0; i < t.AtomCount; i++) nb[i] = new List<int>();
                foreach (var b in t.Bonds)
                {
                    bonded.Add((Math.Min(b.A, b.B), Math.Max(b.A, b.B)));
                    nb[b.A].Add(b.B);
                    nb[b.B].Add(b.A);
                    double d = Vec3.Distance(t.Positions[b.A], t.Positions[b.B]);
                    Assert.True(d > 0.6 && d < 3.0, c.Smiles + ": длина связи " + d.ToString("F2"));
                }
                // Несвязанные атомы (не 1-2 и не 1-3) не должны налезать друг на друга.
                for (int i = 0; i < t.AtomCount; i++)
                    for (int j = i + 1; j < t.AtomCount; j++)
                    {
                        if (bonded.Contains((i, j))) continue;
                        if (nb[i].Intersect(nb[j]).Any()) continue;
                        if (t.Charges[i] * t.Charges[j] < 0) continue; // ионная пара (Li⁺…H⁻ ≈ 1,6 Å) — это контакт, а не налезание
                        double d = Vec3.Distance(t.Positions[i], t.Positions[j]);
                        Assert.True(d > 1.6, c.Smiles + ": атомы " + i + " и " + j + " слишком близко (" + d.ToString("F2") + ")");
                    }
                foreach (Vec3 p in t.Positions) Assert.True(!double.IsNaN(p.X), c.Smiles + ": NaN");
            }
        }

        [Test]
        public static void Builder_KekulizesAromatics()
        {
            MoleculeTemplate t = MoleculeBuilder.Build("c1ccncc1");
            Assert.Equal(3, t.Bonds.Count(b => b.Order == 2), "двойных связей в пиридине");
            Assert.Equal(11, t.AtomCount, "атомов в пиридине");
        }

        [Test]
        public static void Builder_MoleculeSurvivesAndIsRecognized()
        {
            // Каждое ковалентное вещество базы: собрали при 300 K, прогнали 1 пс — молекула цела и опознана.
            // Неустойчивые по базе (метилгидропероксид, диазен…) распадаются сами — их проверяет DecayTests.
            foreach (CompoundInfo c in Buildable().Where(x => !x.Smiles.Contains('.') &&
                         !((x.Hazard ?? "").Contains("неустойч") && !x.Smiles.Contains('['))))
            {
                Simulation sim = TestUtil.NewSim(300, 7, withData: true);
                BuildMolecule cmd = sim.Apply(new BuildMolecule(c.Smiles, Vec3.Zero));
                Assert.True(cmd.Result.Length > 0, c.Smiles + ": не собрано");
                sim.Step(2000);
                string key = Canonicalizer.KeyFromSmiles(c.Smiles);
                IReadOnlyList<Molecule> mols = sim.World.Molecules;
                Assert.True(mols.Count == 1 && mols[0].CanonicalKey == key,
                    c.Smiles + ": после 1 пс — " + string.Join(", ", mols.Select(m => m.Formula)));
            }
        }

        [Test]
        public static void Builder_SaltIsAssembledAsIons()
        {
            Simulation sim = TestUtil.NewSim(300, 3, withData: true);
            BuildMolecule cmd = sim.Apply(new BuildMolecule("[Na+].[Na+].[O-2]", Vec3.Zero));
            Assert.Equal(3, cmd.Result.Length, "ионов");
            int charge = cmd.Result.Sum(id => sim.World.GetCharge(id));
            Assert.Equal(0, charge, "суммарный заряд");
            sim.Step(1000);
            Assert.Equal(3, sim.World.AtomCount, "ионы на месте");
        }

        [Test]
        public static void Builder_DoesNotOverlapExistingAtoms()
        {
            Simulation sim = TestUtil.NewSim(10, 1);
            BuildMolecule a = sim.Apply(new BuildMolecule("c1ccccc1", Vec3.Zero));
            BuildMolecule b = sim.Apply(new BuildMolecule("c1ccccc1", Vec3.Zero));
            double min = double.MaxValue;
            foreach (AtomId i in a.Result)
                foreach (AtomId j in b.Result)
                    min = Math.Min(min, Vec3.Distance(sim.World.GetPosition(i), sim.World.GetPosition(j)));
            Assert.True(min >= 2.2, "вторая молекула налезла на первую: " + min.ToString("F2"));
        }

        [Test]
        public static void Builder_BadSmilesIsIgnored()
        {
            Simulation sim = TestUtil.NewSim();
            BuildMolecule cmd = sim.Apply(new BuildMolecule("C1CC", Vec3.Zero));
            Assert.Equal(0, cmd.Result.Length, "атомов из битого SMILES");
            Assert.Equal(0, sim.World.AtomCount, "мир пуст");
        }
    }
}
