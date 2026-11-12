using System.Collections.Generic;

namespace Nerv.Core.Model
{
    /// <summary>
    /// Молекула — производная сущность: связная компонента графа ковалентных связей.
    /// Пересчитывается при каждом изменении топологии; объект неизменяемый.
    /// </summary>
    public sealed class Molecule
    {
        public IReadOnlyList<AtomId> Atoms { get; }
        /// <summary>Брутто-формула (нотация Хилла), например "C2H6O".</summary>
        public string Formula { get; }
        public int Charge { get; }
        public int BondCount { get; }

        internal Molecule(IReadOnlyList<AtomId> atoms, string formula, int charge, int bondCount)
        {
            Atoms = atoms;
            Formula = formula;
            Charge = charge;
            BondCount = bondCount;
        }

        public override string ToString() => Charge == 0 ? Formula : Formula + (Charge > 0 ? "+" + Charge : Charge.ToString());
    }
}
