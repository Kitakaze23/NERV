using System.IO;
using Nerv.Core.Naming;
using Nerv.Core.Reactions;

namespace Nerv.Core
{
    /// <summary>
    /// Химические данные NERV: база названий, таблица ионов, каталог реакций.
    /// Источники — текстовые файлы в core/data (TSV и YAML); клиент может передать их строками
    /// (например, из TextAsset в Unity).
    /// </summary>
    public sealed class ChemistryData
    {
        public CompoundDatabase Compounds { get; }
        public IonTable Ions { get; }
        public ReactionCatalog Reactions { get; }

        public ChemistryData(CompoundDatabase compounds, IonTable ions, ReactionCatalog reactions)
        {
            Compounds = compounds ?? new CompoundDatabase();
            Ions = ions ?? new IonTable();
            Reactions = reactions ?? new ReactionCatalog();
        }

        /// <summary>Загрузить из текстов; каталог реакций проверяется (ошибки → исключение).</summary>
        public static ChemistryData Load(string compoundsTsv, string ionsTsv, string reactionsYaml)
        {
            ReactionCatalog catalog = ReactionCatalog.LoadYaml(reactionsYaml ?? string.Empty);
            catalog.ThrowIfInvalid();
            return new ChemistryData(CompoundDatabase.LoadTsv(compoundsTsv), IonTable.LoadTsv(ionsTsv), catalog);
        }

        /// <summary>Загрузить compounds.tsv, ions.tsv и reactions.yaml из папки.</summary>
        public static ChemistryData LoadDirectory(string directory) =>
            Load(
                File.ReadAllText(Path.Combine(directory, "compounds.tsv")),
                File.ReadAllText(Path.Combine(directory, "ions.tsv")),
                File.ReadAllText(Path.Combine(directory, "reactions.yaml")));
    }
}
