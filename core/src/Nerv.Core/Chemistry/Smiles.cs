using System;
using System.Collections.Generic;
using System.Text;
using Nerv.Core.Data;

namespace Nerv.Core.Chemistry
{
    /// <summary>
    /// Парсер SMILES и подмножества SMARTS (для шаблонов реакций).
    ///
    /// SMILES: органическое подмножество (B C N O P S F Cl Br I, ароматические b c n o p s) с неявными H,
    /// атомы в скобках [NH4+], [Na+], [O-2]; связи - = # :; ветвления; циклы 1–9 и %nn; «.» — несвязанные части.
    ///
    /// SMARTS (диалект NERV): атомы как в SMILES плюс примитивы в скобках
    /// Hn (число соседей-водородов), Dn (число связей), :n (карта атомов), разделители «;» и «&amp;»;
    /// связь «~» — любая. Заряд, не указанный в шаблоне, означает «заряд 0».
    /// </summary>
    public static class Smiles
    {
        private static readonly Dictionary<string, int[]> DefaultValences = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["B"] = new[] { 3 },
            ["C"] = new[] { 4 },
            ["N"] = new[] { 3, 5 },
            ["O"] = new[] { 2 },
            ["P"] = new[] { 3, 5 },
            ["S"] = new[] { 2, 4, 6 },
            ["F"] = new[] { 1 },
            ["Cl"] = new[] { 1 },
            ["Br"] = new[] { 1 },
            ["I"] = new[] { 1 },
        };

        public static MolGraph Parse(string text, bool query = false)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var g = new MolGraph();
            var branches = new Stack<int>();
            var rings = new Dictionary<int, KeyValuePair<int, int>>();
            int prev = -1;
            int pendingBond = -1;
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) throw Error(text, i, "пробел внутри SMILES");

                if (c == '(')
                {
                    if (prev < 0) throw Error(text, i, "ветвление без атома");
                    branches.Push(prev);
                    i++;
                    continue;
                }
                if (c == ')')
                {
                    if (branches.Count == 0) throw Error(text, i, "лишняя «)»");
                    prev = branches.Pop();
                    i++;
                    continue;
                }
                if (c == '.')
                {
                    prev = -1;
                    pendingBond = -1;
                    i++;
                    continue;
                }
                if (c == '-' || c == '=' || c == '#' || c == ':' || c == '~')
                {
                    if (c == '~' && !query) throw Error(text, i, "«~» допустима только в SMARTS");
                    pendingBond = c == '-' ? 1 : c == '=' ? 2 : c == '#' ? 3 : c == ':' ? MolGraph.AromaticOrder : MolGraph.AnyOrder;
                    i++;
                    continue;
                }
                if (char.IsDigit(c) || c == '%')
                {
                    if (prev < 0) throw Error(text, i, "номер цикла без атома");
                    int num;
                    if (c == '%')
                    {
                        if (i + 2 >= text.Length || !char.IsDigit(text[i + 1]) || !char.IsDigit(text[i + 2])) throw Error(text, i, "ожидалось %nn");
                        num = (text[i + 1] - '0') * 10 + (text[i + 2] - '0');
                        i += 3;
                    }
                    else
                    {
                        num = c - '0';
                        i++;
                    }

                    if (rings.TryGetValue(num, out KeyValuePair<int, int> open))
                    {
                        int order = pendingBond >= 0 ? pendingBond : open.Value >= 0 ? open.Value : DefaultBond(g, open.Key, prev);
                        g.AddBond(open.Key, prev, order);
                        rings.Remove(num);
                    }
                    else
                    {
                        rings[num] = new KeyValuePair<int, int>(prev, pendingBond);
                    }
                    pendingBond = -1;
                    continue;
                }

                int atom = c == '[' ? ParseBracket(g, text, ref i, query) : ParseOrganic(g, text, ref i);
                if (prev >= 0)
                {
                    int order = pendingBond >= 0 ? pendingBond : DefaultBond(g, prev, atom);
                    g.AddBond(prev, atom, order);
                }
                else if (pendingBond >= 0)
                {
                    throw Error(text, i, "связь без начального атома");
                }
                pendingBond = -1;
                prev = atom;
            }

            if (branches.Count > 0) throw Error(text, text.Length, "не закрыта «(»");
            if (rings.Count > 0) throw Error(text, text.Length, "не закрыт цикл");
            if (!query) AddImplicitHydrogens(g);
            return g;
        }

        /// <summary>SMILES → нормализованный граф.</summary>
        public static MolGraph ParseNormalized(string text)
        {
            MolGraph g = Parse(text);
            g.Normalize();
            return g;
        }

        private static int DefaultBond(MolGraph g, int a, int b) =>
            g.Atoms[a].Aromatic && g.Atoms[b].Aromatic ? MolGraph.AromaticOrder : 1;

        private static int ParseOrganic(MolGraph g, string s, ref int i)
        {
            string sym;
            bool aromatic = false;
            if (i + 1 < s.Length && ((s[i] == 'C' && s[i + 1] == 'l') || (s[i] == 'B' && s[i + 1] == 'r')))
            {
                sym = s.Substring(i, 2);
                i += 2;
            }
            else
            {
                char c = s[i];
                if ("BCNOPSFI".IndexOf(c) >= 0) sym = c.ToString();
                else if ("bcnops".IndexOf(c) >= 0)
                {
                    sym = char.ToUpperInvariant(c).ToString();
                    aromatic = true;
                }
                else throw Error(s, i, "неожиданный символ «" + c + "»");
                i++;
            }
            return g.AddAtom(new MolAtom { Z = ElementTable.Get(sym).Z, Aromatic = aromatic });
        }

        private static int ParseBracket(MolGraph g, string s, ref int i, bool query)
        {
            int close = s.IndexOf(']', i);
            if (close < 0) throw Error(s, i, "не закрыта «[»");
            int p = i + 1;
            while (p < close && char.IsDigit(s[p])) p++; // изотоп игнорируем

            if (p >= close) throw Error(s, p, "пустой атом");
            string sym;
            bool aromatic = false;
            char c0 = s[p];
            if (char.IsUpper(c0))
            {
                if (p + 1 < close && char.IsLower(s[p + 1]) && ElementTable.TryGet(s.Substring(p, 2), out _))
                {
                    sym = s.Substring(p, 2);
                    p += 2;
                }
                else
                {
                    sym = c0.ToString();
                    p++;
                }
            }
            else if (char.IsLower(c0))
            {
                aromatic = true;
                if (p + 1 < close && (s.Substring(p, 2) == "se" || s.Substring(p, 2) == "as"))
                {
                    sym = char.ToUpperInvariant(c0) + s[p + 1].ToString();
                    p += 2;
                }
                else
                {
                    sym = char.ToUpperInvariant(c0).ToString();
                    p++;
                }
            }
            else throw Error(s, p, "ожидался символ элемента");

            if (!ElementTable.TryGet(sym, out Element element)) throw Error(s, p, "неизвестный элемент " + sym);
            var atom = new MolAtom { Z = element.Z, Aromatic = aromatic, Bracket = true };

            while (p < close)
            {
                char c = s[p];
                if (c == 'H')
                {
                    p++;
                    int n = ReadInt(s, ref p, close, 1);
                    if (query) atom.QueryHCount = n;
                    else atom.HCount = n;
                }
                else if (c == '+' || c == '-')
                {
                    int sign = c == '+' ? 1 : -1;
                    p++;
                    int mag;
                    if (p < close && char.IsDigit(s[p])) mag = ReadInt(s, ref p, close, 1);
                    else
                    {
                        mag = 1;
                        while (p < close && s[p] == c) { mag++; p++; }
                    }
                    atom.Charge = sign * mag;
                    atom.ChargeSpecified = true;
                }
                else if (c == 'D' && query)
                {
                    p++;
                    atom.QueryDegree = ReadInt(s, ref p, close, 1);
                }
                else if (c == ':')
                {
                    p++;
                    atom.Map = ReadInt(s, ref p, close, -1);
                    if (atom.Map <= 0) throw Error(s, p, "ожидался номер карты атома");
                }
                else if (c == ';' || c == '&' || c == '@')
                {
                    p++;
                }
                else throw Error(s, p, "неподдерживаемый примитив «" + c + "»");
            }

            i = close + 1;
            return g.AddAtom(atom);
        }

        private static int ReadInt(string s, ref int p, int end, int fallback)
        {
            int start = p;
            int v = 0;
            while (p < end && char.IsDigit(s[p]))
            {
                v = v * 10 + (s[p] - '0');
                p++;
            }
            return p == start ? fallback : v;
        }

        private static void AddImplicitHydrogens(MolGraph g)
        {
            var used = new int[g.Atoms.Count];
            foreach (MolBond b in g.Bonds)
            {
                int o = b.Order == MolGraph.AromaticOrder ? 1 : b.Order;
                used[b.A] += o;
                used[b.B] += o;
            }
            for (int i = 0; i < g.Atoms.Count; i++)
            {
                MolAtom a = g.Atoms[i];
                if (a.Bracket) continue;
                string sym = ElementTable.Get(a.Z).Symbol;
                if (!DefaultValences.TryGetValue(sym, out int[] valences)) continue;
                int u = used[i] + (a.Aromatic ? 1 : 0);
                foreach (int v in valences)
                {
                    if (v < u) continue;
                    a.HCount = v - u;
                    break;
                }
            }
        }

        private static FormatException Error(string s, int pos, string message) =>
            new FormatException("SMILES «" + s + "», позиция " + pos + ": " + message);
    }
}
