using System;
using System.Collections.Generic;
using System.Text;
using Nerv.Core.Data;

namespace Nerv.Core.Reactions
{
    /// <summary>Частица уравнения: коэффициент, состав, заряд.</summary>
    public sealed class Species
    {
        public int Coefficient;
        public Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Charge;

        /// <summary>Ключ сравнения: формула Хилла + заряд («H2O», «H+», «HO-»).</summary>
        public string Key => HillKey(Counts, Charge);

        internal static string HillKey(Dictionary<string, int> counts, int charge)
        {
            var sb = new StringBuilder();
            void Append(string sym)
            {
                sb.Append(sym);
                if (counts[sym] > 1) sb.Append(counts[sym]);
            }
            var rest = new List<string>(counts.Keys);
            if (counts.ContainsKey("C"))
            {
                Append("C");
                rest.Remove("C");
                if (counts.ContainsKey("H"))
                {
                    Append("H");
                    rest.Remove("H");
                }
            }
            rest.Sort(StringComparer.Ordinal);
            foreach (string s in rest) Append(s);
            if (charge != 0) sb.Append(charge > 0 ? "+" : "-").Append(Math.Abs(charge) > 1 ? Math.Abs(charge).ToString() : "");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Разбор уравнений вида «2H₂ + O₂ → 2H₂O», «H⁺ + OH⁻ → H₂O», «Ca(OH)₂ → …».
    /// Понимает подстрочные/надстрочные цифры, ⁺ ⁻, скобки, стрелки → и -&gt;.
    /// </summary>
    public static class EquationParser
    {
        public static void Parse(string equation, out List<Species> left, out List<Species> right)
        {
            if (string.IsNullOrWhiteSpace(equation)) throw new FormatException("Пустое уравнение.");
            string s = Normalize(equation);
            int arrow = s.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) throw new FormatException("Нет стрелки в уравнении «" + equation + "».");
            left = Side(s.Substring(0, arrow), equation);
            right = Side(s.Substring(arrow + 2), equation);
        }

        private static string Normalize(string eq)
        {
            var sb = new StringBuilder();
            foreach (char c in eq)
            {
                int sub = "₀₁₂₃₄₅₆₇₈₉".IndexOf(c);
                int sup = "⁰¹²³⁴⁵⁶⁷⁸⁹".IndexOf(c);
                if (sub >= 0) sb.Append((char)('0' + sub));
                else if (sup >= 0) sb.Append('^').Append((char)('0' + sup));
                else if (c == '⁺') sb.Append("^+");
                else if (c == '⁻') sb.Append("^-");
                else if (c == '→') sb.Append("->");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static List<Species> Side(string side, string original)
        {
            var result = new List<Species>();
            foreach (string part in side.Split(new[] { " + " }, StringSplitOptions.None))
            {
                string t = part.Trim();
                if (t.Length == 0) throw new FormatException("Пустая частица в «" + original + "».");
                var sp = new Species();
                int p = 0;
                while (p < t.Length && char.IsDigit(t[p])) p++;
                sp.Coefficient = p > 0 ? int.Parse(t.Substring(0, p)) : 1;

                int caret = t.IndexOf('^');
                string formula = caret >= 0 ? t.Substring(p, caret - p) : t.Substring(p);
                if (caret >= 0)
                {
                    string ch = t.Substring(caret).Replace("^", "");
                    int sign = ch.EndsWith("+", StringComparison.Ordinal) ? 1 : ch.EndsWith("-", StringComparison.Ordinal) ? -1 : 0;
                    if (sign == 0) throw new FormatException("Не понят заряд в «" + t + "».");
                    string mag = ch.Substring(0, ch.Length - 1);
                    sp.Charge = sign * (mag.Length > 0 ? int.Parse(mag) : 1);
                }

                int q = 0;
                ParseGroup(formula, ref q, sp.Counts, 1);
                if (q != formula.Length) throw new FormatException("Не понята формула «" + formula + "».");
                result.Add(sp);
            }
            return result;
        }

        private static void ParseGroup(string f, ref int p, Dictionary<string, int> counts, int mult)
        {
            while (p < f.Length && f[p] != ')')
            {
                if (f[p] == '(')
                {
                    p++;
                    var inner = new Dictionary<string, int>(StringComparer.Ordinal);
                    ParseGroup(f, ref p, inner, 1);
                    if (p >= f.Length || f[p] != ')') throw new FormatException("Не закрыта скобка в «" + f + "».");
                    p++;
                    int n = ReadCount(f, ref p);
                    foreach (KeyValuePair<string, int> kv in inner) Add(counts, kv.Key, kv.Value * n * mult);
                    continue;
                }
                if (!char.IsUpper(f[p])) throw new FormatException("Ожидался символ элемента в «" + f + "».");
                string sym = f[p].ToString();
                p++;
                if (p < f.Length && char.IsLower(f[p]))
                {
                    sym += f[p];
                    p++;
                }
                if (!ElementTable.TryGet(sym, out _)) throw new FormatException("Неизвестный элемент «" + sym + "».");
                Add(counts, sym, ReadCount(f, ref p) * mult);
            }
        }

        private static int ReadCount(string f, ref int p)
        {
            int start = p;
            while (p < f.Length && char.IsDigit(f[p])) p++;
            return p > start ? int.Parse(f.Substring(start, p - start)) : 1;
        }

        private static void Add(Dictionary<string, int> counts, string sym, int n)
        {
            counts.TryGetValue(sym, out int c);
            counts[sym] = c + n;
        }
    }
}
