using System;
using System.Collections.Generic;
using System.Text;

namespace Nerv.Core.Reactions
{
    /// <summary>
    /// Минимальный разбор YAML для каталога реакций (без внешних библиотек):
    /// отображения и списки по отступам (пробелы), «- ключ: значение», строки в "..." и '...',
    /// встроенные списки [a, b], null/~, комментарии #. Якоря, многострочные строки и т.п. не поддерживаются.
    /// Результат: Dictionary&lt;string, object&gt;, List&lt;object&gt;, string или null.
    /// </summary>
    public static class YamlLite
    {
        private sealed class Line
        {
            public int Indent;
            public string Text;
            public int No;
        }

        public static object Parse(string text)
        {
            var lines = new List<Line>();
            string[] raw = (text ?? string.Empty).Split('\n');
            for (int n = 0; n < raw.Length; n++)
            {
                string s = raw[n].TrimEnd('\r');
                if (s.IndexOf('\t') >= 0 && s.Trim().Length > 0 && s.Substring(0, s.Length - s.TrimStart().Length).IndexOf('\t') >= 0)
                    throw new FormatException("YAML, строка " + (n + 1) + ": табуляция в отступе.");
                string stripped = StripComment(s);
                if (stripped.Trim().Length == 0) continue;
                int indent = stripped.Length - stripped.TrimStart(' ').Length;
                lines.Add(new Line { Indent = indent, Text = stripped.Trim(), No = n + 1 });
            }
            if (lines.Count == 0) return null;
            int idx = 0;
            object result = ParseBlock(lines, ref idx, lines[0].Indent);
            if (idx < lines.Count) throw Error(lines[idx], "неожиданное содержимое");
            return result;
        }

        private static bool IsListItem(string t) => t == "-" || t.StartsWith("- ", StringComparison.Ordinal);

        private static object ParseBlock(List<Line> l, ref int i, int indent) =>
            IsListItem(l[i].Text) ? (object)ParseList(l, ref i, indent) : ParseMap(l, ref i, indent);

        private static List<object> ParseList(List<Line> l, ref int i, int indent)
        {
            var list = new List<object>();
            while (i < l.Count && l[i].Indent == indent && IsListItem(l[i].Text))
            {
                Line line = l[i];
                string afterDash = line.Text.Length > 1 ? line.Text.Substring(2) : string.Empty;
                string rest = afterDash.TrimStart();
                if (rest.Length == 0)
                {
                    i++;
                    list.Add(i < l.Count && l[i].Indent > indent ? ParseBlock(l, ref i, l[i].Indent) : null);
                }
                else if (FindColon(rest) > 0 && rest[0] != '"' && rest[0] != '\'' && rest[0] != '[')
                {
                    int childIndent = indent + 2 + (afterDash.Length - rest.Length);
                    l[i] = new Line { Indent = childIndent, Text = rest, No = line.No };
                    list.Add(ParseMap(l, ref i, childIndent));
                }
                else
                {
                    list.Add(Scalar(rest, line));
                    i++;
                }
            }
            if (i < l.Count && l[i].Indent > indent) throw Error(l[i], "неожиданный отступ");
            return list;
        }

        private static Dictionary<string, object> ParseMap(List<Line> l, ref int i, int indent)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            while (i < l.Count && l[i].Indent == indent && !IsListItem(l[i].Text))
            {
                Line line = l[i];
                int colon = FindColon(line.Text);
                if (colon <= 0) throw Error(line, "ожидалось «ключ: значение»");
                string key = line.Text.Substring(0, colon).Trim();
                string val = line.Text.Substring(colon + 1).Trim();
                if (map.ContainsKey(key)) throw Error(line, "повторный ключ «" + key + "»");
                i++;
                if (val.Length > 0) map[key] = Scalar(val, line);
                else if (i < l.Count && l[i].Indent > indent) map[key] = ParseBlock(l, ref i, l[i].Indent);
                else if (i < l.Count && l[i].Indent == indent && IsListItem(l[i].Text)) map[key] = ParseList(l, ref i, indent);
                else map[key] = null;
            }
            if (i < l.Count && l[i].Indent > indent) throw Error(l[i], "неожиданный отступ");
            return map;
        }

        private static int FindColon(string s)
        {
            bool dq = false, sq = false;
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (c == '"' && !sq) dq = !dq;
                else if (c == '\'' && !dq) sq = !sq;
                else if (c == ':' && !dq && !sq && (k + 1 == s.Length || s[k + 1] == ' ')) return k;
            }
            return -1;
        }

        private static string StripComment(string s)
        {
            bool dq = false, sq = false;
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (c == '"' && !sq) dq = !dq;
                else if (c == '\'' && !dq) sq = !sq;
                else if (c == '#' && !dq && !sq && (k == 0 || s[k - 1] == ' ')) return s.Substring(0, k);
            }
            return s;
        }

        private static object Scalar(string s, Line line)
        {
            s = s.Trim();
            if (s == "null" || s == "~") return null;
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                if (s.Length < 2 || !s.EndsWith("\"", StringComparison.Ordinal)) throw Error(line, "не закрыта кавычка");
                var sb = new StringBuilder();
                for (int k = 1; k < s.Length - 1; k++)
                {
                    char c = s[k];
                    if (c == '\\' && k + 1 < s.Length - 1)
                    {
                        char e = s[++k];
                        sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }
            if (s.StartsWith("'", StringComparison.Ordinal))
            {
                if (s.Length < 2 || !s.EndsWith("'", StringComparison.Ordinal)) throw Error(line, "не закрыта кавычка");
                return s.Substring(1, s.Length - 2).Replace("''", "'");
            }
            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                if (!s.EndsWith("]", StringComparison.Ordinal)) throw Error(line, "не закрыт список «[»");
                var items = new List<object>();
                string inner = s.Substring(1, s.Length - 2);
                if (inner.Trim().Length == 0) return items;
                var cur = new StringBuilder();
                bool dq = false, sq = false;
                foreach (char c in inner)
                {
                    if (c == '"' && !sq) dq = !dq;
                    else if (c == '\'' && !dq) sq = !sq;
                    if (c == ',' && !dq && !sq)
                    {
                        items.Add(Scalar(cur.ToString(), line));
                        cur.Clear();
                    }
                    else cur.Append(c);
                }
                items.Add(Scalar(cur.ToString(), line));
                return items;
            }
            return s;
        }

        private static FormatException Error(Line line, string message) =>
            new FormatException("YAML, строка " + line.No + ": " + message);
    }
}
