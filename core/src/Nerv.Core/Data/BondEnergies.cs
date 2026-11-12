using System;
using System.Collections.Generic;

namespace Nerv.Core.Data
{
    /// <summary>
    /// Средние энергии связей (ккал/моль) — глубина ямы Морзе. Слабые связи (O–O, N–N, F–F) рвутся
    /// при нагреве первыми, поэтому пероксидные цепочки в пламени распадаются, а вода и CO₂ — нет.
    /// Значения — типичные справочные средние; для пар без данных — 70 ккал/моль на порядок связи.
    /// </summary>
    public static class BondEnergies
    {
        public const double DefaultPerOrder = 70.0;

        private static readonly Dictionary<string, double> Table = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["H-H1"] = 104, ["C-H1"] = 99, ["H-N1"] = 93, ["H-O1"] = 111, ["F-H1"] = 136, ["Cl-H1"] = 103, ["H-S1"] = 88,
            ["C-C1"] = 83, ["C-C2"] = 146, ["C-C3"] = 200,
            ["C-N1"] = 73, ["C-N2"] = 147, ["C-N3"] = 213,
            ["C-O1"] = 86, ["C-O2"] = 178, ["C-O3"] = 257,
            ["C-F1"] = 116, ["C-Cl1"] = 81, ["C-S1"] = 65, ["C-S2"] = 128,
            ["N-N1"] = 38, ["N-N2"] = 100, ["N-N3"] = 226,
            ["N-O1"] = 48, ["N-O2"] = 145,
            ["F-N1"] = 65, ["Cl-N1"] = 46,
            ["O-O1"] = 35, ["O-O2"] = 119,
            ["F-O1"] = 45, ["Cl-O1"] = 49,
            ["F-F1"] = 38, ["Cl-Cl1"] = 58, ["Cl-F1"] = 61,
            ["S-S1"] = 54, ["Cl-S1"] = 65,
        };

        /// <summary>Энергия связи между элементами данного порядка, ккал/моль.</summary>
        public static double Get(Element a, Element b, int order, double fallbackPerOrder = DefaultPerOrder)
        {
            string x = a.Symbol, y = b.Symbol;
            string key = string.CompareOrdinal(x, y) <= 0 ? x + "-" + y + order : y + "-" + x + order;
            return Table.TryGetValue(key, out double e) ? e : fallbackPerOrder * order;
        }
    }
}
