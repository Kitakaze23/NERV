using System.Collections.Generic;
using System.Linq;
using Nerv.Core.Chemistry;
using Nerv.Core.Model;
using Nerv.Core.Naming;
using Nerv.Core.Numerics;
using Nerv.Core.Reactions;
using Nerv.Core.Sim;
using static Nerv.Core.Tests.TestUtil;

namespace Nerv.Core.Tests
{
    public static class NamingTests
    {
        [Test]
        public static void Data_LoadsAndCatalogIsValid()
        {
            ChemistryData d = ChemData;
            Assert.True(d.Compounds.Count >= 80, "веществ в базе: " + d.Compounds.Count);
            Assert.True(d.Ions.Count >= 30, "ионов: " + d.Ions.Count);
            Assert.Equal(15, d.Reactions.Reactions.Count, "реакций");
            IReadOnlyList<string> errors = d.Reactions.Validate();
            Assert.Equal(0, errors.Count, "ошибки каталога: " + string.Join("; ", errors));
        }

        [Test]
        public static void Database_FindsByAnySmilesSpelling()
        {
            CompoundDatabase db = ChemData.Compounds;
            Assert.Equal("Этанол", db.Find(Canonicalizer.KeyFromSmiles("OCC")).NameRu);
            Assert.Equal("Вода", db.Find(Canonicalizer.KeyFromSmiles("[H]O[H]")).NameRu);
            Assert.Equal("Бензол", db.Find(Canonicalizer.KeyFromSmiles("C1=CC=CC=C1")).NameRu);
            Assert.Equal("Хлорид натрия", db.Find(Canonicalizer.KeyFromSmiles("[Cl-].[Na+]")).NameRu);
            Assert.Equal("C2H6O", db.Find(Canonicalizer.KeyFromSmiles("CCO")).Formula);
            Assert.True(db.Find(Canonicalizer.KeyFromSmiles("CCCCCCCC")) == null, "октана в базе нет");
        }

        [Test]
        public static void Database_ListsIsomers()
        {
            CompoundDatabase db = ChemData.Compounds;
            string ethanol = Canonicalizer.KeyFromSmiles("CCO");
            IReadOnlyList<CompoundInfo> iso = db.Isomers("C2H6O", ethanol);
            Assert.True(iso.Any(c => c.NameRu == "Диметиловый эфир"), "изомер этанола — диметиловый эфир");
            Assert.False(iso.Any(c => c.Key == ethanol), "сам этанол не в списке");
        }

        [Test]
        public static void Ions_HaveNamesAndSaltParts()
        {
            IonInfo cl = ChemData.Ions.Find(Canonicalizer.KeyFromSmiles("[Cl-]"));
            Assert.Equal("хлорид-ион", cl.NameRu);
            Assert.Equal("хлорид", cl.SaltPartRu);
            Assert.Equal(-1, cl.Charge);
            IonInfo so4 = ChemData.Ions.Find(Canonicalizer.KeyFromSmiles("[O-]S(=O)(=O)[O-]"));
            Assert.Equal("SO₄²⁻", so4.DisplayFormula);
        }

        [Test]
        public static void Equation_ParsesFormulasAndCharges()
        {
            EquationParser.Parse("2H₂ + O₂ → 2H₂O", out List<Species> l, out List<Species> r);
            Assert.Equal(2, l.Count);
            Assert.Equal(2, l[0].Coefficient);
            Assert.Equal("H2", l[0].Key);
            Assert.Equal("H2O", r[0].Key);

            EquationParser.Parse("Ca(OH)₂ + 2H⁺ → Ca²⁺ + 2H₂O", out l, out r);
            Assert.Equal(2, l[0].Counts["H"], "H в Ca(OH)2");
            Assert.Equal(2, l[0].Counts["O"], "O в Ca(OH)2");
            Assert.Equal(1, l[1].Charge, "H+");
            Assert.Equal(2, r[0].Charge, "Ca2+");
        }

        [Test]
        public static void Yaml_ParsesNestedMapsAndLists()
        {
            object root = YamlLite.Parse("- id: a\n  tags: [x, \"y z\"]\n  nested:\n    k: 1 # комментарий\n    s: \"a: b # не комментарий\"\n- id: b\n");
            var list = (List<object>)root;
            Assert.Equal(2, list.Count);
            var first = (Dictionary<string, object>)list[0];
            Assert.Equal("a", first["id"]);
            Assert.Equal("y z", ((List<object>)first["tags"])[1]);
            var nested = (Dictionary<string, object>)first["nested"];
            Assert.Equal("1", nested["k"]);
            Assert.Equal("a: b # не комментарий", nested["s"]);
        }

        [Test]
        public static void Validator_CatchesBrokenReactions()
        {
            string Errors(string smarts, string summary = null) =>
                string.Join("; ", ReactionCatalog.LoadYaml("- id: t\n  smarts: \"" + smarts + "\"\n" +
                    (summary != null ? "  education:\n    summary: \"" + summary + "\"\n" : "")).Validate());

            Assert.True(Errors("[H:1][H:2]>>[H:1]").Contains("пропал"), "атом пропал");
            Assert.True(Errors("[H:1][H:2]>>[H+:1].[H:2]").Contains("заряд"), "заряд не сохраняется");
            Assert.True(Errors("[O:1].[O:2]>>[O:1]#[O:2]").Contains("валентност"), "валентность O превышена");
            Assert.True(Errors("[H:1].[H:2]>>[H:1][H:2]", "H + H → H₃").Contains("не сбалансировано"), "несбалансированный итог");
            Assert.True(Errors("[H:1].[H:2]>>[H:1][H:2]", "O₂ → O₂").Contains("не совпадают"), "шаги ≠ итог");
            Assert.Equal("", Errors("[H:1].[H:2]>>[H:1][H:2]", "2H → H₂"), "корректная реакция");
        }

        [Test]
        public static void Scene_IdentifiesWaterAndReportsIt()
        {
            Simulation sim = NewSim(withData: true);
            Add(sim, "O", 0, 0, 0);
            Add(sim, "H", 2.5, 0, 0);
            Add(sim, "H", -1.25, 2.165, 0);
            RunPs(sim, 3);
            Molecule water = sim.World.Molecules.Single();
            Assert.Equal("Вода", water.Name);
            Assert.True(water.Compound.FactsRu.Count > 0, "у воды есть факты");
            Assert.True(sim.Events.Any(e => e.Type == SimEventType.MoleculeIdentified && e.Text == "Вода"), "событие «Вода»");
        }

        [Test]
        public static void Scene_SodiumChlorideGroupInVacuum_IonsInWater()
        {
            Simulation sim = NewSim(withData: true);
            Add(sim, "Na", 0, 0, 0);
            Add(sim, "Cl", 3.4, 0, 0);
            RunPs(sim, 2);
            IonicGroup g = sim.IonicGroups.Single();
            Assert.Equal("Хлорид натрия", g.Name);
            Assert.Equal("NaCl", g.Formula);
            Assert.True(sim.Events.Any(e => e.Type == SimEventType.MoleculeIdentified && e.Text == "Хлорид натрия"), "событие о соли");

            sim.Apply(new SetMedium(Medium.Water));
            sim.Step(30);
            Assert.Equal(0, sim.IonicGroups.Count, "в воде ионы по отдельности");
            Assert.Equal(1, CountNamed(sim, "катион натрия"));
            Assert.Equal(1, CountNamed(sim, "хлорид-ион"));
        }

        [Test]
        public static void Scene_SaltNameByRuleWhenNotInDatabase()
        {
            Simulation sim = NewSim(withData: true);
            AtomId s = Add(sim, "S", 0, 0, 0, charge: -1);
            AtomId h = Add(sim, "H", 1.34, 0, 0);
            sim.Apply(new AddBond(s, h));
            Add(sim, "Na", -2.8, 0, 0, charge: +1);
            sim.Step(30);
            IonicGroup g = sim.IonicGroups.Single();
            Assert.Equal("Гидросульфид натрия", g.Name, "по правилу «анион + катион»");
            Assert.Equal("NaHS", g.Formula);
        }

        [Test]
        public static void Scene_CrystalReducesToFormulaUnit()
        {
            Simulation sim = NewSim(withData: true);
            Add(sim, "Na", 0, 0, 0, +1);
            Add(sim, "Cl", 2.8, 0, 0, -1);
            Add(sim, "Cl", 0, 2.8, 0, -1);
            Add(sim, "Na", 2.8, 2.8, 0, +1);
            sim.Step(30);
            IonicGroup g = sim.IonicGroups.Single();
            Assert.Equal("NaCl", g.Formula);
            Assert.Equal(2, g.FormulaUnits);
            Assert.Equal("Хлорид натрия", g.Name);
        }

        [Test]
        public static void Scene_SodiumOxideFromAtoms()
        {
            Simulation sim = NewSim(withData: true);
            Add(sim, "O", 0, 0, 0);
            Add(sim, "Na", 2.9, 0, 0);
            Add(sim, "Na", -2.9, 0, 0);
            RunPs(sim, 3);
            IonicGroup g = sim.IonicGroups.Single();
            Assert.Equal("Na2O", g.Formula);
            Assert.Equal("Оксид натрия", g.Name);
        }
    }
}
