using System;
using System.Collections.Generic;
using System.Text;
using Nerv.Core.Data;

namespace Nerv.Core.Chemistry
{
    /// <summary>Брутто-формула в нотации Хилла (ASCII-цифры; подстрочные индексы делает UI).</summary>
    public static class Formula
    {
        /// <summary>
        /// Есть углерод: C, затем H, затем остальные по алфавиту. Нет углерода: все по алфавиту.
        /// </summary>
        public static string Hill(IEnumerable<Element> elements)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Element e in elements)
            {
                counts.TryGetValue(e.Symbol, out int c);
                counts[e.Symbol] = c + 1;
            }

            var sb = new StringBuilder();
            if (counts.ContainsKey("C"))
            {
                Append(sb, "C", counts["C"]);
                counts.Remove("C");
                if (counts.TryGetValue("H", out int h))
                {
                    Append(sb, "H", h);
                    counts.Remove("H");
                }
            }

            foreach (KeyValuePair<string, int> kv in counts) Append(sb, kv.Key, kv.Value);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string symbol, int count)
        {
            sb.Append(symbol);
            if (count > 1) sb.Append(count);
        }
    }
}
