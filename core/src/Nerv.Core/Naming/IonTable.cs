using System;
using System.Collections.Generic;
using Nerv.Core.Chemistry;

namespace Nerv.Core.Naming
{
    public sealed class IonInfo
    {
        public string Key { get; internal set; }
        public string Smiles { get; internal set; }
        /// <summary>Формула в принятой записи («SO4», «OH», «CH3COO»).</summary>
        public string Formula { get; internal set; }
        public int Charge { get; internal set; }
        /// <summary>Название иона: «хлорид-ион», «катион натрия».</summary>
        public string NameRu { get; internal set; }
        /// <summary>Часть названия соли: анион — «хлорид», катион — «натрия» (родительный падеж).</summary>
        public string SaltPartRu { get; internal set; }

        public bool IsCation => Charge > 0;

        /// <summary>Формула со знаком заряда: «Na⁺», «SO₄²⁻».</summary>
        public string DisplayFormula => FormulaText.Subscript(Formula) + FormulaText.ChargeSuperscript(Charge);

        public override string ToString() => NameRu;
    }

    /// <summary>Таблица ионов (core/data/ions.tsv): названия ионов и правило названия солей.</summary>
    public sealed class IonTable
    {
        private readonly Dictionary<string, IonInfo> _byKey = new Dictionary<string, IonInfo>(StringComparer.Ordinal);

        public int Count => _byKey.Count;
        public IEnumerable<IonInfo> All => _byKey.Values;

        public static IonTable LoadTsv(string text)
        {
            var table = new IonTable();
            foreach (string[] f in Tsv.Rows(text))
            {
                string smiles = Tsv.Field(f, 0);
                if (smiles.Length == 0) continue;
                MolGraph g = Smiles.ParseNormalized(smiles);
                var ion = new IonInfo
                {
                    Key = Canonicalizer.Key(g),
                    Smiles = smiles,
                    Formula = Tsv.Field(f, 1),
                    Charge = g.TotalCharge(),
                    NameRu = Tsv.Field(f, 2),
                    SaltPartRu = Tsv.Field(f, 3)
                };
                if (ion.Charge == 0) throw new FormatException("ions.tsv: «" + smiles + "» не заряжен.");
                if (table._byKey.ContainsKey(ion.Key)) throw new FormatException("ions.tsv: дубликат «" + smiles + "».");
                table._byKey[ion.Key] = ion;
            }
            return table;
        }

        public IonInfo Find(string key) => key != null && _byKey.TryGetValue(key, out IonInfo i) ? i : null;
    }

    /// <summary>Оформление формул для интерфейса: подстрочные индексы и заряды.</summary>
    public static class FormulaText
    {
        private const string Sub = "₀₁₂₃₄₅₆₇₈₉";
        private const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹";

        public static string Subscript(string formula)
        {
            if (formula == null) return null;
            var chars = formula.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] >= '0' && chars[i] <= '9') chars[i] = Sub[chars[i] - '0'];
            return new string(chars);
        }

        public static string ChargeSuperscript(int charge)
        {
            if (charge == 0) return string.Empty;
            string mag = Math.Abs(charge) == 1 ? string.Empty : Sup[Math.Abs(charge)].ToString();
            return mag + (charge > 0 ? "⁺" : "⁻");
        }
    }
}
