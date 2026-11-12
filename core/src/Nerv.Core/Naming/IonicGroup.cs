using System.Collections.Generic;
using Nerv.Core.Model;

namespace Nerv.Core.Naming
{
    /// <summary>
    /// Ионная ассоциация: ионы противоположных зарядов, держащиеся рядом (кристаллик NaCl, пара Na⁺ OH⁻).
    /// На симуляцию не влияет — нужна для формулы и названия. В среде «вода» ассоциации не образуются
    /// (ионы в растворе показываются по отдельности).
    /// </summary>
    public sealed class IonicGroup
    {
        public IReadOnlyList<Molecule> Members { get; internal set; }
        /// <summary>Ключ формульной единицы: ключи ионов, сокращённые на НОД и отсортированные.</summary>
        public string Key { get; internal set; }
        /// <summary>Формула в принятой записи: «NaCl», «Na2O», «Ca(OH)2».</summary>
        public string Formula { get; internal set; }
        /// <summary>Сколько формульных единиц в ассоциации (Na₄Cl₄ → 4).</summary>
        public int FormulaUnits { get; internal set; }
        public int Charge { get; internal set; }
        public CompoundInfo Compound { get; internal set; }
        /// <summary>Название: из базы или по правилу «анион + катион в родительном падеже».</summary>
        public string Name { get; internal set; }

        public override string ToString() => (Name ?? "?") + " " + Formula + (FormulaUnits > 1 ? " ×" + FormulaUnits : "");
    }
}
