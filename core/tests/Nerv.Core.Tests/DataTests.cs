using System.Linq;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;

namespace Nerv.Core.Tests
{
    public static class DataTests
    {
        [Test]
        public static void ElementTable_Has118ElementsWithLayout()
        {
            Assert.Equal(118, ElementTable.All.Count, "число элементов");
            Assert.Equal("Железо", ElementTable.Get(26).NameRu);
            Assert.Equal("Оганесон", ElementTable.Get(118).NameRu);
            Assert.True(ElementTable.All.All(e => !string.IsNullOrEmpty(e.NameRu)), "у всех элементов есть русское название");
            Assert.Equal(8, ElementTable.Get("O").Z);
            Assert.Equal("Og", ElementTable.Get(118).Symbol);

            Element fe = ElementTable.Get("Fe");
            Assert.Equal(ElementCategory.TransitionMetal, fe.Category);
            Assert.Equal(4, fe.Period);
            Assert.Equal(8, fe.Group);
            Assert.Equal(ElementAvailability.ComingSoon, fe.Availability);

            Element la = ElementTable.Get("La");
            Assert.Equal(9, la.TableRow, "лантаноиды в строке 9");
            Assert.Equal(3, la.TableColumn);
            Assert.Equal(ElementCategory.Lanthanide, la.Category);

            Element hf = ElementTable.Get("Hf");
            Assert.Equal(4, hf.Group);

            Assert.Equal(ElementCategory.Halogen, ElementTable.Get("Cl").Category);
            Assert.True(ElementTable.Get("Na").IsMetal, "натрий — металл");
            Assert.False(ElementTable.Get("O").IsMetal, "кислород — не металл");
            Assert.Equal(ElementAvailability.Optional, ElementTable.Get("S").Availability);
            Assert.Equal("Кислород", ElementTable.Get("O").NameRu);
        }

        [Test]
        public static void ElectronRules_OctetValences()
        {
            Assert.Equal(1, ElectronRules.MaxValence(ElementTable.Get("H"), 0), "H");
            Assert.Equal(4, ElectronRules.MaxValence(ElementTable.Get("C"), 0), "C");
            Assert.Equal(3, ElectronRules.MaxValence(ElementTable.Get("N"), 0), "N");
            Assert.Equal(4, ElectronRules.MaxValence(ElementTable.Get("N"), +1), "N+ (аммоний)");
            Assert.Equal(2, ElectronRules.MaxValence(ElementTable.Get("O"), 0), "O");
            Assert.Equal(1, ElectronRules.MaxValence(ElementTable.Get("O"), -1), "O- (гидроксид)");
            Assert.Equal(3, ElectronRules.MaxValence(ElementTable.Get("O"), +1), "O+ (гидроксоний)");
            Assert.Equal(0, ElectronRules.MaxValence(ElementTable.Get("Cl"), -1), "Cl-");
            Assert.Equal(1, ElectronRules.MaxValence(ElementTable.Get("Na"), 0), "Na");
            Assert.Equal(0, ElectronRules.MaxValence(ElementTable.Get("Na"), +1), "Na+");

            Assert.Equal(2, ElectronRules.LonePairs(ElementTable.Get("O"), 0, 2), "пары O в воде");
            Assert.Equal(1, ElectronRules.LonePairs(ElementTable.Get("N"), 0, 3), "пара N в аммиаке");
            Assert.Equal(4, ElectronRules.LonePairs(ElementTable.Get("Cl"), -1, 0), "пары Cl-");
            Assert.Equal(2, ElectronRules.LonePairs(ElementTable.Get("O"), 0, 0), "пары свободного O");
        }

        [Test]
        public static void ElectronRules_IonicPairs()
        {
            Assert.True(ElectronRules.IsIonicPair(ElementTable.Get("Na"), ElementTable.Get("Cl"), 1.7), "NaCl ионная");
            Assert.False(ElectronRules.IsIonicPair(ElementTable.Get("H"), ElementTable.Get("Cl"), 1.7), "HCl ковалентная");
            Assert.False(ElectronRules.IsIonicPair(ElementTable.Get("C"), ElementTable.Get("O"), 1.7), "CO ковалентная");
        }

        [Test]
        public static void Formula_HillNotation()
        {
            Element c = ElementTable.Get("C"), h = ElementTable.Get("H"), o = ElementTable.Get("O"),
                na = ElementTable.Get("Na"), cl = ElementTable.Get("Cl");
            Assert.Equal("CH4", Formula.Hill(new[] { h, c, h, h, h }));
            Assert.Equal("C2H6O", Formula.Hill(new[] { c, c, o, h, h, h, h, h, h }));
            Assert.Equal("H2O", Formula.Hill(new[] { o, h, h }));
            Assert.Equal("ClNa", Formula.Hill(new[] { na, cl }));
            Assert.Equal("CO2", Formula.Hill(new[] { o, c, o }));
        }

        [Test]
        public static void Uff_RestLengthsAreChemicallySensible()
        {
            double oh = Uff.RestLength(Uff.Get("O_3"), Uff.Get("H_"), 1);
            double ch = Uff.RestLength(Uff.Get("C_3"), Uff.Get("H_"), 1);
            double cc1 = Uff.RestLength(Uff.Get("C_3"), Uff.Get("C_3"), 1);
            double cc2 = Uff.RestLength(Uff.Get("C_2"), Uff.Get("C_2"), 2);
            double cc3 = Uff.RestLength(Uff.Get("C_1"), Uff.Get("C_1"), 3);
            Assert.Near(0.97, oh, 0.05, "O–H");
            Assert.Near(1.10, ch, 0.05, "C–H");
            Assert.Near(1.53, cc1, 0.05, "C–C");
            Assert.True(cc2 < cc1 && cc3 < cc2, "кратные связи короче");
        }
    }
}
