using System;
using System.Collections.Generic;

namespace Nerv.Core.Data
{
    /// <summary>
    /// Таблица Менделеева: все 118 элементов с раскладкой и категориями.
    /// Подробные данные (масса, ЭО, радиусы, цвет) пока заданы только для элементов MVP;
    /// остальные помечены <see cref="ElementAvailability.ComingSoon"/> и будут вынесены в файл данных.
    /// </summary>
    public static class ElementTable
    {
        public const int Count = 118;

        private const string Symbols =
            "H He Li Be B C N O F Ne Na Mg Al Si P S Cl Ar K Ca Sc Ti V Cr Mn Fe Co Ni Cu Zn Ga Ge As Se Br Kr " +
            "Rb Sr Y Zr Nb Mo Tc Ru Rh Pd Ag Cd In Sn Sb Te I Xe Cs Ba La Ce Pr Nd Pm Sm Eu Gd Tb Dy Ho Er Tm Yb Lu " +
            "Hf Ta W Re Os Ir Pt Au Hg Tl Pb Bi Po At Rn Fr Ra Ac Th Pa U Np Pu Am Cm Bk Cf Es Fm Md No Lr " +
            "Rf Db Sg Bh Hs Mt Ds Rg Cn Nh Fl Mc Lv Ts Og";

        private static readonly Element[] ByZ = new Element[Count + 1];
        private static readonly Dictionary<string, Element> BySymbol = new Dictionary<string, Element>(StringComparer.Ordinal);
        private static readonly Element[] AllElements = new Element[Count];

        private struct Detail
        {
            public string NameRu, NameEn;
            public double Mass, En, Cov, Vdw;
            public int Valence;
            public uint Color;
            public ElementAvailability Availability;
        }

        private static readonly Dictionary<string, Detail> Details = new Dictionary<string, Detail>(StringComparer.Ordinal)
        {
            // Цвета — палитра NERV на основе CPK/Jmol (см. docs/architecture.md, раздел 7.5).
            ["H"] = D("Водород", "Hydrogen", 1.008, 2.20, 1, 0.31, 1.20, 0xFFFFFF, ElementAvailability.Enabled),
            ["C"] = D("Углерод", "Carbon", 12.011, 2.55, 4, 0.76, 1.70, 0x909090, ElementAvailability.Enabled),
            ["N"] = D("Азот", "Nitrogen", 14.007, 3.04, 5, 0.71, 1.55, 0x4D6BFF, ElementAvailability.Enabled),
            ["O"] = D("Кислород", "Oxygen", 15.999, 3.44, 6, 0.66, 1.52, 0xFF4D4D, ElementAvailability.Enabled),
            ["F"] = D("Фтор", "Fluorine", 18.998, 3.98, 7, 0.57, 1.47, 0x90E050, ElementAvailability.Optional),
            ["Na"] = D("Натрий", "Sodium", 22.990, 0.93, 1, 1.66, 2.27, 0xAB5CF2, ElementAvailability.Enabled),
            ["S"] = D("Сера", "Sulfur", 32.06, 2.58, 6, 1.05, 1.80, 0xF2D33A, ElementAvailability.Optional),
            ["Cl"] = D("Хлор", "Chlorine", 35.45, 3.16, 7, 1.02, 1.75, 0x3DDC5A, ElementAvailability.Enabled),
        };

        private static Detail D(string ru, string en, double mass, double en2, int valence, double cov, double vdw, uint color, ElementAvailability a) =>
            new Detail { NameRu = ru, NameEn = en, Mass = mass, En = en2, Valence = valence, Cov = cov, Vdw = vdw, Color = color, Availability = a };

        static ElementTable()
        {
            string[] symbols = Symbols.Split(' ');
            if (symbols.Length != Count) throw new InvalidOperationException("Таблица символов повреждена.");

            for (int z = 1; z <= Count; z++)
            {
                string s = symbols[z - 1];
                Layout(z, out int period, out int group, out int row, out int column);
                ElementCategory cat = CategoryOf(z);
                Element e = Details.TryGetValue(s, out Detail d)
                    ? new Element(z, s, d.NameRu, d.NameEn, d.Mass, d.En, d.Valence, d.Cov, d.Vdw, d.Color,
                        period, group, row, column, cat, d.Availability)
                    : new Element(z, s, null, null, 0, double.NaN, 0, 0, 0, 0x808080,
                        period, group, row, column, cat, ElementAvailability.ComingSoon);
                ByZ[z] = e;
                AllElements[z - 1] = e;
                BySymbol[s] = e;
            }
        }

        public static IReadOnlyList<Element> All => AllElements;

        public static Element Get(int z)
        {
            if (z < 1 || z > Count) throw new ArgumentOutOfRangeException(nameof(z));
            return ByZ[z];
        }

        public static Element Get(string symbol)
        {
            if (!BySymbol.TryGetValue(symbol, out Element e)) throw new ArgumentException("Неизвестный элемент: " + symbol, nameof(symbol));
            return e;
        }

        public static bool TryGet(string symbol, out Element element) => BySymbol.TryGetValue(symbol, out element);

        private static void Layout(int z, out int period, out int group, out int row, out int column)
        {
            if (z == 1) { period = 1; group = 1; }
            else if (z == 2) { period = 1; group = 18; }
            else if (z <= 10) { period = 2; group = z <= 4 ? z - 2 : z + 8; }
            else if (z <= 18) { period = 3; group = z <= 12 ? z - 10 : z; }
            else if (z <= 36) { period = 4; group = z - 18; }
            else if (z <= 54) { period = 5; group = z - 36; }
            else if (z <= 86) { period = 6; group = z <= 56 ? z - 54 : z <= 71 ? 3 : z - 68; }
            else { period = 7; group = z <= 88 ? z - 86 : z <= 103 ? 3 : z - 100; }

            row = period;
            column = group;
            if (z >= 57 && z <= 71) { row = 9; column = z - 54; }
            else if (z >= 89 && z <= 103) { row = 10; column = z - 86; }
        }

        private static ElementCategory CategoryOf(int z)
        {
            if (In(z, 1, 6, 7, 8, 15, 16, 34)) return ElementCategory.Nonmetal;
            if (In(z, 3, 11, 19, 37, 55, 87)) return ElementCategory.AlkaliMetal;
            if (In(z, 4, 12, 20, 38, 56, 88)) return ElementCategory.AlkalineEarthMetal;
            if (In(z, 2, 10, 18, 36, 54, 86, 118)) return ElementCategory.NobleGas;
            if (In(z, 9, 17, 35, 53, 85, 117)) return ElementCategory.Halogen;
            if (In(z, 5, 14, 32, 33, 51, 52)) return ElementCategory.Metalloid;
            if (z >= 57 && z <= 71) return ElementCategory.Lanthanide;
            if (z >= 89 && z <= 103) return ElementCategory.Actinide;
            if (In(z, 13, 31, 49, 50, 81, 82, 83, 84, 113, 114, 115, 116)) return ElementCategory.PostTransitionMetal;
            return ElementCategory.TransitionMetal;
        }

        private static bool In(int z, params int[] list) => Array.IndexOf(list, z) >= 0;
    }
}
