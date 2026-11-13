using System.Collections.Generic;
using Nerv.Core.Naming;

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

        /// <summary>Канонический ключ (заполняется распознаванием после изменения топологии).</summary>
        public string CanonicalKey { get; internal set; }
        /// <summary>Запись базы названий, если вещество известно.</summary>
        public CompoundInfo Compound { get; internal set; }
        /// <summary>Сведения об ионе, если частица заряжена и есть в таблице ионов.</summary>
        public IonInfo Ion { get; internal set; }
        /// <summary>Название для интерфейса или null («неизвестное соединение»).</summary>
        public string Name => Compound?.NameRu ?? Ion?.NameRu ?? AtomName;
        /// <summary>
        /// Суммарная свободная валентность (неспаренные электроны). Больше нуля — радикал:
        /// частица нестабильна и охотно вступает в реакции.
        /// </summary>
        public int FreeValence { get; internal set; }
        public bool IsRadical => FreeValence > 0;
        /// <summary>
        /// Неустойчивая частица: неизвестна базе и держится на слабой связи (O–O, N–N, N–O, Cl–O…),
        /// либо помечена в базе как неустойчивая. Со временем сама распадается по самой слабой связи
        /// (быстрее при нагреве, медленнее на холоде). Известные радикалы сюда не входят — они просто реакционноспособны.
        /// </summary>
        public bool Unstable { get; internal set; }
        /// <summary>Энергия связи, по которой пойдёт распад, ккал/моль (0 — не неустойчива).</summary>
        public double DecayBondEnergy { get; internal set; }
        /// <summary>Для одиночного нейтрального атома без записи в базе: «Натрий (атом)».</summary>
        public string AtomName { get; internal set; }

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
