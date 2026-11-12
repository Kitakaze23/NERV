using System;
using System.Collections.Generic;
using System.IO;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;

namespace Nerv.Core.Naming
{
    /// <summary>Запись базы названий.</summary>
    public sealed class CompoundInfo
    {
        /// <summary>Канонический ключ (диалект NERV).</summary>
        public string Key { get; internal set; }
        /// <summary>SMILES из исходных данных.</summary>
        public string Smiles { get; internal set; }
        /// <summary>Брутто-формула (Хилл), для ионных — без зарядов.</summary>
        public string Formula { get; internal set; }
        public string NameRu { get; internal set; }
        public IReadOnlyList<string> TrivialRu { get; internal set; } = Array.Empty<string>();
        public string NameEn { get; internal set; }
        public string Category { get; internal set; }
        public string Hazard { get; internal set; }
        public IReadOnlyList<string> FactsRu { get; internal set; } = Array.Empty<string>();
        public string WikidataId { get; internal set; }
        /// <summary>Курируемая запись (с фактами для школьника), а не автоматическая.</summary>
        public bool Curated { get; internal set; }

        public override string ToString() => NameRu + " (" + Formula + ")";
    }

    /// <summary>
    /// База названий: канонический ключ → вещество. Ключи строятся тем же канонизатором, что и для сцены.
    /// Формат источника — TSV (см. core/data/compounds.tsv).
    /// </summary>
    public sealed class CompoundDatabase
    {
        private readonly Dictionary<string, CompoundInfo> _byKey = new Dictionary<string, CompoundInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CompoundInfo>> _byFormula = new Dictionary<string, List<CompoundInfo>>(StringComparer.Ordinal);

        public int Count => _byKey.Count;
        public IEnumerable<CompoundInfo> All => _byKey.Values;

        public static CompoundDatabase LoadTsv(string text, bool curated = true)
        {
            var db = new CompoundDatabase();
            int lineNo = 0;
            foreach (string[] f in Tsv.Rows(text))
            {
                lineNo++;
                string smiles = Tsv.Field(f, 0);
                if (string.IsNullOrEmpty(smiles)) continue;
                MolGraph g;
                try { g = Smiles.ParseNormalized(smiles); }
                catch (FormatException e) { throw new FormatException("compounds.tsv, запись " + lineNo + ": " + e.Message); }

                var info = new CompoundInfo
                {
                    Key = Canonicalizer.Key(g),
                    Smiles = smiles,
                    Formula = HillOf(g),
                    NameRu = Tsv.Field(f, 1),
                    TrivialRu = Tsv.List(Tsv.Field(f, 2)),
                    NameEn = Tsv.Field(f, 3),
                    Category = Tsv.Field(f, 4),
                    Hazard = Tsv.Field(f, 5),
                    FactsRu = Tsv.List(Tsv.Field(f, 6)),
                    WikidataId = Tsv.Field(f, 7),
                    Curated = curated
                };
                db.Add(info);
            }
            return db;
        }

        public void Add(CompoundInfo info)
        {
            if (_byKey.ContainsKey(info.Key))
                throw new InvalidOperationException("Дубликат в базе названий: " + info.Smiles + " (" + info.NameRu + ") и " + _byKey[info.Key].NameRu);
            _byKey[info.Key] = info;
            if (!_byFormula.TryGetValue(info.Formula, out List<CompoundInfo> list))
            {
                list = new List<CompoundInfo>();
                _byFormula[info.Formula] = list;
            }
            list.Add(info);
        }

        public CompoundInfo Find(string key) =>
            key != null && _byKey.TryGetValue(key, out CompoundInfo c) ? c : null;

        /// <summary>Известные вещества с той же брутто-формулой (изомеры), кроме указанного ключа.</summary>
        public IReadOnlyList<CompoundInfo> Isomers(string formula, string exceptKey = null)
        {
            var result = new List<CompoundInfo>();
            if (formula != null && _byFormula.TryGetValue(formula, out List<CompoundInfo> list))
                foreach (CompoundInfo c in list)
                    if (c.Key != exceptKey) result.Add(c);
            return result;
        }

        internal static string HillOf(MolGraph g)
        {
            var elements = new List<Element>();
            foreach (MolAtom a in g.Atoms)
            {
                elements.Add(ElementTable.Get(a.Z));
                for (int h = 0; h < a.HCount; h++) elements.Add(ElementTable.Get(1));
            }
            return Chemistry.Formula.Hill(elements);
        }
    }

    internal static class Tsv
    {
        public static IEnumerable<string[]> Rows(string text)
        {
            using (var reader = new StringReader(text ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    yield return line.Split('\t');
                }
            }
        }

        public static string Field(string[] f, int i) => i < f.Length ? f[i].Trim() : string.Empty;

        public static IReadOnlyList<string> List(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
            var parts = new List<string>();
            foreach (string p in s.Split(';'))
                if (p.Trim().Length > 0) parts.Add(p.Trim());
            return parts;
        }
    }
}
