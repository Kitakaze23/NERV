using System;
using Nerv.Core.Chemistry;
using Nerv.Core.Model;
using Nerv.Core.Sim;
using static Nerv.Core.Tests.TestUtil;

namespace Nerv.Core.Tests
{
    public static class CanonicalTests
    {
        private static string K(string smiles) => Canonicalizer.KeyFromSmiles(smiles);

        [Test]
        public static void Smiles_ParsesImplicitHydrogens()
        {
            MolGraph g = Smiles.ParseNormalized("CCO");
            Assert.Equal(3, g.Atoms.Count, "тяжёлых атомов");
            Assert.Equal(3, g.Atoms[0].HCount, "CH3");
            Assert.Equal(2, g.Atoms[1].HCount, "CH2");
            Assert.Equal(1, g.Atoms[2].HCount, "OH");
            Assert.Equal("C2H6O", Formula.Hill(Expand(g)), "формула");
        }

        [Test]
        public static void Smiles_ParsesBracketsChargesRings()
        {
            MolGraph nh4 = Smiles.ParseNormalized("[NH4+]");
            Assert.Equal(4, nh4.Atoms[0].HCount);
            Assert.Equal(1, nh4.Atoms[0].Charge);

            MolGraph o2m = Smiles.ParseNormalized("[O-2]");
            Assert.Equal(-2, o2m.Atoms[0].Charge);

            MolGraph cyclo = Smiles.ParseNormalized("C1CCCCC1");
            Assert.Equal(6, cyclo.Bonds.Count, "циклогексан — 6 связей");
            Assert.Equal(2, cyclo.Atoms[0].HCount);

            MolGraph salt = Smiles.ParseNormalized("[Na+].[Cl-]");
            Assert.Equal(2, salt.Components().Count, "две части");

            Assert.Throws<FormatException>(() => Smiles.Parse("C1CC"));
            Assert.Throws<FormatException>(() => Smiles.Parse("C(C"));
            Assert.Throws<FormatException>(() => Smiles.Parse("[Xx]"));
        }

        [Test]
        public static void Canonical_IsIndependentOfWriting()
        {
            Assert.Equal(K("CCO"), K("OCC"), "этанол");
            Assert.Equal(K("CCO"), K("C(O)C"), "этанол с ветвлением");
            Assert.Equal(K("CC(C)O"), K("OC(C)C"), "изопропанол");
            Assert.Equal(K("CC(=O)O"), K("OC(C)=O"), "уксусная кислота");
            Assert.Equal(K("C1CCCCC1"), K("C1CCCCC1".Replace("1", "2")), "номер цикла не важен");
            Assert.Equal(K("[Na+].[Cl-]"), K("[Cl-].[Na+]"), "порядок частей");
            Assert.Equal(K("O"), K("[OH2]"), "вода");
            Assert.Equal(K("O"), K("[H]O[H]"), "явные водороды сворачиваются");
            Assert.Equal(K("NC(N)=O"), K("O=C(N)N"), "мочевина");
        }

        [Test]
        public static void Canonical_DistinguishesIsomers()
        {
            Assert.False(K("CCO") == K("COC"), "этанол ≠ диметиловый эфир");
            Assert.False(K("CCCO") == K("CC(C)O"), "пропанол-1 ≠ пропанол-2");
            Assert.False(K("CC=O") == K("C=CO"), "ацетальдегид ≠ виниловый спирт");
            Assert.False(K("[H][H]") == K("[H]"), "H2 ≠ H");
        }

        [Test]
        public static void Canonical_AromaticityUnifiesKekuleForms()
        {
            string a = K("C1=CC=CC=C1");
            string b = K("C1C=CC=CC=1");
            string arom = K("c1ccccc1");
            Assert.Equal(a, b, "две формы Кекуле");
            Assert.Equal(a, arom, "Кекуле = ароматическая запись");
            Assert.Equal(K("Cc1ccccc1"), K("CC1=CC=CC=C1"), "толуол");
            Assert.Equal(K("c1ccncc1"), K("C1=CC=NC=C1"), "пиридин");
            Assert.False(K("C1=CCCC=C1") == K("c1ccccc1"), "циклогексадиен не ароматичен");
        }

        [Test]
        public static void Canonical_KeyIsValidSmilesRoundTrip()
        {
            foreach (string s in new[] { "CCO", "c1ccccc1O", "OC(=O)C(=O)O", "[Na+].[OH-]", "C1CC1", "N#N", "[H][H]", "ClC(Cl)(Cl)Cl" })
            {
                string key = K(s);
                Assert.Equal(key, K(key), "повторная канонизация «" + s + "» → «" + key + "»");
            }
        }

        [Test]
        public static void Canonical_SceneMoleculeMatchesSmiles()
        {
            Simulation sim = NewSim();
            Add(sim, "O", 0, 0, 0);
            Add(sim, "H", 2.5, 0, 0);
            Add(sim, "H", -1.25, 2.165, 0);
            RunPs(sim, 2);
            Molecule water = sim.World.Molecules[0];
            Assert.Equal(K("O"), Canonicalizer.KeyFromWorld(sim.World, water.Atoms), "вода из сцены = вода из SMILES");
        }

        [Test]
        public static void Canonical_IsIndependentOfAtomOrder()
        {
            var rng = new Numerics.Xoshiro256(77);
            string[] smiles =
            {
                "CCO", "OCC(O)CO", "CC(C)(C)C", "c1ccccc1O", "Cc1ccncc1", "NCC(=O)O", "C1CCC2CCCCC2C1",
                "OC(=O)C(=O)O", "ClC(Cl)(F)F", "[Na+].[Na+].[O-]C([O-])=O", "C1CC1C1CC1", "CC(C)CC(C)C"
            };
            foreach (string s in smiles)
            {
                MolGraph g = Smiles.ParseNormalized(s);
                string expected = Canonicalizer.Key(g);
                for (int trial = 0; trial < 20; trial++)
                {
                    MolGraph p = Permute(g, rng);
                    Assert.Equal(expected, Canonicalizer.Key(p), "перестановка атомов «" + s + "»");
                }
            }
        }

        private static MolGraph Permute(MolGraph g, Numerics.Xoshiro256 rng)
        {
            int n = g.Atoms.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = (int)(rng.NextDouble() * (i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            var where = new int[n];
            var p = new MolGraph();
            for (int k = 0; k < n; k++)
            {
                where[order[k]] = k;
                p.AddAtom(g.Atoms[order[k]].Clone());
            }
            var bonds = new System.Collections.Generic.List<MolBond>(g.Bonds);
            for (int i = bonds.Count - 1; i > 0; i--)
            {
                int j = (int)(rng.NextDouble() * (i + 1));
                (bonds[i], bonds[j]) = (bonds[j], bonds[i]);
            }
            foreach (MolBond b in bonds) p.Bonds.Add(new MolBond { A = where[b.B], B = where[b.A], Order = b.Order });
            return p;
        }

        private static System.Collections.Generic.IEnumerable<Data.Element> Expand(MolGraph g)
        {
            foreach (MolAtom a in g.Atoms)
            {
                yield return Data.ElementTable.Get(a.Z);
                for (int h = 0; h < a.HCount; h++) yield return Data.ElementTable.Get(1);
            }
        }
    }
}
