using Nerv.Core.Model;

namespace Nerv.Core.Chemistry
{
    /// <summary>
    /// Дополнительные химические запреты для свободной сборки (поверх валентности).
    /// Используются и правилами топологии, и силой «захвата», чтобы запрещённые пары не притягивались.
    /// </summary>
    internal static class BondRules
    {
        private const int Oxygen = 8;

        /// <summary>
        /// Можно ли соединить атомы i и j свободной сборкой (валентность проверяется отдельно).
        /// Запрет: цепочки из трёх и более атомов кислорода (O–O–O) — они неустойчивы, а озон без формальных
        /// зарядов модель не описывает. Пероксиды (H–O–O–H, HO₂) разрешены.
        /// </summary>
        public static bool CanBond(World w, int i, int j)
        {
            // Металлы не соединяются друг с другом ковалентно: металлической связи в модели нет,
            // а «молекулы» Mg₂ или Na₂ съедали бы атомы металла, которые должны отдавать электроны.
            if (w.Elements[i].IsMetal && w.Elements[j].IsMetal) return false;
            if (w.Elements[i].Z == Oxygen && w.Elements[j].Z == Oxygen)
                return !HasOxygenNeighbor(w, i) && !HasOxygenNeighbor(w, j);
            return true;
        }

        private static bool HasOxygenNeighbor(World w, int i)
        {
            foreach (Bond b in w.AdjacentBonds[i])
                if (w.Elements[b.Ia == i ? b.Ib : b.Ia].Z == Oxygen) return true;
            return false;
        }
    }
}
