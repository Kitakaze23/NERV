using System;
using System.Collections.Generic;
using System.Globalization;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;
using Nerv.Core.Model;

namespace Nerv.Core.Reactions
{
    /// <summary>
    /// Шаблон реакции — один элементарный (не более чем двухчастичный) шаг.
    /// Задаётся reaction SMARTS с картой атомов: она говорит, какой атом куда переходит.
    /// </summary>
    public sealed class ReactionTemplate
    {
        public string Id { get; internal set; }
        /// <summary>Группа шагов одной суммарной реакции (по умолчанию — Id).</summary>
        public string Group { get; internal set; }
        public string Name { get; internal set; }
        public string Smarts { get; internal set; }
        /// <summary>Энергия активации, кДж/моль (игровая шкала).</summary>
        public double ActivationEnergyKj { get; internal set; }
        /// <summary>Тепловой эффект шага, кДж/моль (&lt; 0 — экзотермический).</summary>
        public double DeltaHKj { get; internal set; }
        /// <summary>Игровой коэффициент темпа: вероятность за проверку при бесконечной температуре.</summary>
        public double Rate { get; internal set; } = 0.05;
        /// <summary>Допустимые среды (пусто — любые).</summary>
        public IReadOnlyList<Medium> Media { get; internal set; } = Array.Empty<Medium>();
        public double? MinTemperature { get; internal set; }
        /// <summary>Пресет визуального эффекта для клиента.</summary>
        public string Effects { get; internal set; }
        public string Summary { get; internal set; }
        public string TextRu { get; internal set; }
        public string Level { get; internal set; }
        /// <summary>«simplified» — упрощённый механизм (так и показываем в интерфейсе).</summary>
        public string Mechanism { get; internal set; }
        public IReadOnlyList<string> Tags { get; internal set; } = Array.Empty<string>();

        internal MolGraph Reactants, Products;
        internal List<MolGraph> ReactantComponents = new List<MolGraph>();
        internal List<MolGraph> ProductComponents = new List<MolGraph>();
        /// <summary>Пара номеров карты → порядок связи (реагенты / продукты).</summary>
        internal Dictionary<long, int> ReactantBonds = new Dictionary<long, int>();
        internal Dictionary<long, int> ProductBonds = new Dictionary<long, int>();
        internal Dictionary<int, int> ProductCharges = new Dictionary<int, int>();

        public int Molecularity => ReactantComponents.Count;

        public bool AllowsMedium(Medium m)
        {
            if (Media.Count == 0) return true;
            foreach (Medium x in Media) if (x == m) return true;
            return false;
        }

        internal static long MapPair(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        internal static void SplitPair(long key, out int a, out int b)
        {
            a = (int)(key >> 32);
            b = (int)(key & 0xFFFFFFFF);
        }

        internal void ParseSmarts()
        {
            int arrow = Smarts.IndexOf(">>", StringComparison.Ordinal);
            if (arrow < 0) throw new FormatException("Реакция " + Id + ": в SMARTS нет «>>».");
            Reactants = Smiles.Parse(Smarts.Substring(0, arrow), query: true);
            Products = Smiles.Parse(Smarts.Substring(arrow + 2), query: true);
            foreach (List<int> c in Reactants.Components()) ReactantComponents.Add(Reactants.Subgraph(c));
            foreach (List<int> c in Products.Components()) ProductComponents.Add(Products.Subgraph(c));
            FillBonds(Reactants, ReactantBonds);
            FillBonds(Products, ProductBonds);
            foreach (MolAtom a in Products.Atoms)
                if (a.Map > 0) ProductCharges[a.Map] = a.Charge;
        }

        private static void FillBonds(MolGraph g, Dictionary<long, int> into)
        {
            foreach (MolBond b in g.Bonds)
            {
                int ma = g.Atoms[b.A].Map, mb = g.Atoms[b.B].Map;
                if (ma > 0 && mb > 0) into[MapPair(ma, mb)] = b.Order;
            }
        }

        public override string ToString() => Id;
    }

    /// <summary>Каталог реакций (core/data/reactions.yaml) и его проверка.</summary>
    public sealed class ReactionCatalog
    {
        private readonly List<ReactionTemplate> _reactions = new List<ReactionTemplate>();

        public IReadOnlyList<ReactionTemplate> Reactions => _reactions;

        public ReactionTemplate Find(string id)
        {
            foreach (ReactionTemplate r in _reactions) if (r.Id == id) return r;
            return null;
        }

        public static ReactionCatalog LoadYaml(string yaml)
        {
            var catalog = new ReactionCatalog();
            object root = YamlLite.Parse(yaml);
            if (root == null) return catalog;
            if (!(root is List<object> items)) throw new FormatException("reactions.yaml: корень должен быть списком реакций.");

            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> map)) throw new FormatException("reactions.yaml: элемент списка — не отображение.");
                var r = new ReactionTemplate
                {
                    Id = Str(map, "id") ?? throw new FormatException("reactions.yaml: реакция без id."),
                    Name = Str(map, "name"),
                    Smarts = Str(map, "smarts") ?? throw new FormatException("reactions.yaml: у реакции нет smarts."),
                    ActivationEnergyKj = Num(map, "activationEnergy") ?? 0,
                    DeltaHKj = Num(map, "deltaH") ?? 0,
                    Rate = Num(map, "rate") ?? 0.05,
                    Effects = Str(map, "effects"),
                    Tags = StrList(map, "tags")
                };
                r.Group = Str(map, "group") ?? r.Id;

                if (map.TryGetValue("conditions", out object condObj) && condObj is Dictionary<string, object> cond)
                {
                    var media = new List<Medium>();
                    foreach (string m in StrList(cond, "medium"))
                    {
                        if (m == "vacuum") media.Add(Medium.Vacuum);
                        else if (m == "water") media.Add(Medium.Water);
                        else throw new FormatException("Реакция " + r.Id + ": неизвестная среда «" + m + "».");
                    }
                    r.Media = media;
                    r.MinTemperature = Num(cond, "minTemperature");
                }

                if (map.TryGetValue("education", out object eduObj) && eduObj is Dictionary<string, object> edu)
                {
                    r.Summary = Str(edu, "summary");
                    r.TextRu = Str(edu, "textRu");
                    r.Level = Str(edu, "level");
                    r.Mechanism = Str(edu, "mechanism");
                }

                r.ParseSmarts();
                catalog._reactions.Add(r);
            }
            return catalog;
        }

        public void ThrowIfInvalid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0) throw new FormatException("Каталог реакций содержит ошибки:\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// Проверка каталога: сохранение атомов и заряда, полная карта атомов, валентности продуктов,
        /// баланс итогового уравнения и совпадение суммы шагов с ним.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (ReactionTemplate r in _reactions)
            {
                string p = "Реакция " + r.Id + ": ";
                if (!ids.Add(r.Id)) errors.Add(p + "повторный id.");
                if (r.Molecularity < 1 || r.Molecularity > 2) errors.Add(p + "шаг должен быть одно- или двухчастичным (сейчас " + r.Molecularity + ").");
                if (r.Rate <= 0 || r.Rate > 1) errors.Add(p + "rate должен быть в (0, 1].");
                if (r.ActivationEnergyKj < 0) errors.Add(p + "отрицательная энергия активации.");

                var reactMap = new Dictionary<int, int>();
                foreach (MolAtom a in r.Reactants.Atoms)
                {
                    if (a.Map <= 0) { errors.Add(p + "атом реагентов без номера в карте."); continue; }
                    if (reactMap.ContainsKey(a.Map)) errors.Add(p + "номер карты " + a.Map + " повторяется в реагентах.");
                    reactMap[a.Map] = a.Z;
                }
                var prodMap = new Dictionary<int, int>();
                foreach (MolAtom a in r.Products.Atoms)
                {
                    if (a.Map <= 0) { errors.Add(p + "атом продуктов без номера в карте."); continue; }
                    if (prodMap.ContainsKey(a.Map)) errors.Add(p + "номер карты " + a.Map + " повторяется в продуктах.");
                    prodMap[a.Map] = a.Z;
                }
                foreach (KeyValuePair<int, int> kv in reactMap)
                {
                    if (!prodMap.TryGetValue(kv.Key, out int z)) errors.Add(p + "атом " + kv.Key + " пропал в продуктах.");
                    else if (z != kv.Value) errors.Add(p + "атом " + kv.Key + " сменил элемент.");
                }
                foreach (int m in prodMap.Keys)
                    if (!reactMap.ContainsKey(m)) errors.Add(p + "атом " + m + " появился из ниоткуда.");

                int qr = 0, qp = 0;
                foreach (MolAtom a in r.Reactants.Atoms) qr += a.Charge;
                foreach (MolAtom a in r.Products.Atoms) qp += a.Charge;
                if (qr != qp) errors.Add(p + "заряд не сохраняется (" + qr + " → " + qp + ").");

                var sums = new Dictionary<int, int>();
                foreach (MolBond b in r.Products.Bonds)
                {
                    int o = b.Order == MolGraph.AromaticOrder ? 2 : b.Order;
                    Inc(sums, b.A, o);
                    Inc(sums, b.B, o);
                }
                for (int i = 0; i < r.Products.Atoms.Count; i++)
                {
                    MolAtom a = r.Products.Atoms[i];
                    Element e = ElementTable.Get(a.Z);
                    sums.TryGetValue(i, out int s);
                    int max = ElectronRules.MaxValence(e, a.Charge);
                    if (s > max) errors.Add(p + "у атома " + e.Symbol + ":" + a.Map + " в продуктах " + s + " связей при валентности " + max + ".");
                }
            }

            ValidateGroups(errors);
            return errors;
        }

        private void ValidateGroups(List<string> errors)
        {
            var groups = new Dictionary<string, List<ReactionTemplate>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (ReactionTemplate r in _reactions)
            {
                if (!groups.TryGetValue(r.Group, out List<ReactionTemplate> list))
                {
                    groups[r.Group] = list = new List<ReactionTemplate>();
                    order.Add(r.Group);
                }
                list.Add(r);
            }

            foreach (string g in order)
            {
                List<ReactionTemplate> steps = groups[g];
                string p = "Группа " + g + ": ";
                string summary = null;
                foreach (ReactionTemplate r in steps)
                {
                    if (r.Summary == null) continue;
                    if (summary != null && summary != r.Summary) errors.Add(p + "у шагов разные итоговые уравнения.");
                    summary = r.Summary;
                }
                if (summary == null) continue;

                List<Species> left, right;
                try { EquationParser.Parse(summary, out left, out right); }
                catch (FormatException e) { errors.Add(p + e.Message); continue; }

                Dictionary<string, int> el = Totals(left, out int ql), er = Totals(right, out int qr);
                if (!SameCounts(el, er) || ql != qr) { errors.Add(p + "итоговое уравнение «" + summary + "» не сбалансировано."); continue; }

                // Радикальная цепь: шаги повторяются разное число раз — сумму шагов не сравниваем.
                bool chain = false;
                foreach (ReactionTemplate r in steps) if (r.Mechanism == "chain") chain = true;
                if (chain) continue;

                // Сумма шагов: реагенты минус продукты по видам частиц.
                var net = new Dictionary<string, int>(StringComparer.Ordinal);
                var netCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
                foreach (ReactionTemplate r in steps)
                {
                    foreach (MolGraph c in r.ReactantComponents) AddSpecies(net, netCounts, c, -1);
                    foreach (MolGraph c in r.ProductComponents) AddSpecies(net, netCounts, c, +1);
                }

                var netLeft = new Dictionary<string, int>(StringComparer.Ordinal);
                var netRightElements = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, int> kv in net)
                {
                    if (kv.Value < 0) netLeft[kv.Key] = -kv.Value;
                    else if (kv.Value > 0)
                        foreach (KeyValuePair<string, int> e in netCounts[kv.Key]) Inc(netRightElements, e.Key, e.Value * kv.Value);
                }
                var sumLeft = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Species s in left) Inc(sumLeft, s.Key, s.Coefficient);

                if (!Proportional(netLeft, sumLeft, out int fNet, out int fSum))
                {
                    errors.Add(p + "реагенты суммы шагов (" + Describe(netLeft) + ") не совпадают с итоговым уравнением (" + Describe(sumLeft) + ").");
                    continue;
                }
                var scaledNet = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, int> kv in netRightElements) scaledNet[kv.Key] = kv.Value * fSum;
                var scaledSum = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, int> kv in er) scaledSum[kv.Key] = kv.Value * fNet;
                if (!SameCounts(scaledNet, scaledSum))
                    errors.Add(p + "продукты суммы шагов не совпадают с итоговым уравнением.");
            }
        }

        private static void AddSpecies(Dictionary<string, int> net, Dictionary<string, Dictionary<string, int>> counts, MolGraph c, int sign)
        {
            var el = new Dictionary<string, int>(StringComparer.Ordinal);
            int q = 0;
            foreach (MolAtom a in c.Atoms)
            {
                Inc(el, ElementTable.Get(a.Z).Symbol, 1);
                q += a.Charge;
            }
            string key = Species.HillKey(el, q);
            Inc(net, key, sign);
            counts[key] = el;
        }

        private static Dictionary<string, int> Totals(List<Species> side, out int charge)
        {
            var t = new Dictionary<string, int>(StringComparer.Ordinal);
            charge = 0;
            foreach (Species s in side)
            {
                foreach (KeyValuePair<string, int> kv in s.Counts) Inc(t, kv.Key, kv.Value * s.Coefficient);
                charge += s.Charge * s.Coefficient;
            }
            return t;
        }

        private static bool Proportional(Dictionary<string, int> a, Dictionary<string, int> b, out int fa, out int fb)
        {
            fa = fb = 0;
            if (a.Count == 0 || a.Count != b.Count) return false;
            foreach (KeyValuePair<string, int> kv in a)
            {
                if (!b.TryGetValue(kv.Key, out int bv)) return false;
                if (fa == 0) { fa = kv.Value; fb = bv; }
                else if (kv.Value * fb != bv * fa) return false;
            }
            // a·fb = b·fa: продукты сравниваем как netRight·fb и sumRight·fa.
            return true;
        }

        private static bool SameCounts(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            foreach (KeyValuePair<string, int> kv in a) if (kv.Value != 0 && (!b.TryGetValue(kv.Key, out int v) || v != kv.Value)) return false;
            foreach (KeyValuePair<string, int> kv in b) if (kv.Value != 0 && (!a.TryGetValue(kv.Key, out int v) || v != kv.Value)) return false;
            return true;
        }

        private static string Describe(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in d) parts.Add(kv.Value + "·" + kv.Key);
            return string.Join(" + ", parts);
        }

        private static void Inc<T>(Dictionary<T, int> d, T key, int n)
        {
            d.TryGetValue(key, out int c);
            d[key] = c + n;
        }

        private static string Str(Dictionary<string, object> m, string key) =>
            m.TryGetValue(key, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        private static double? Num(Dictionary<string, object> m, string key)
        {
            string s = Str(m, key);
            if (s == null) return null;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw new FormatException("reactions.yaml: «" + key + "» должно быть числом, получено «" + s + "».");
            return v;
        }

        private static IReadOnlyList<string> StrList(Dictionary<string, object> m, string key)
        {
            if (!m.TryGetValue(key, out object v) || v == null) return Array.Empty<string>();
            if (v is List<object> list)
            {
                var r = new List<string>();
                foreach (object o in list) if (o != null) r.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
                return r;
            }
            return new[] { Convert.ToString(v, CultureInfo.InvariantCulture) };
        }
    }
}
