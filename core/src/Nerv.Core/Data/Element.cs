namespace Nerv.Core.Data
{
    public enum ElementCategory
    {
        Nonmetal,
        AlkaliMetal,
        AlkalineEarthMetal,
        TransitionMetal,
        PostTransitionMetal,
        Metalloid,
        Halogen,
        NobleGas,
        Lanthanide,
        Actinide
    }

    /// <summary>Доступность элемента в продукте: в MVP включены не все 118.</summary>
    public enum ElementAvailability
    {
        /// <summary>Показывается в таблице приглушённым («скоро»).</summary>
        ComingSoon,
        /// <summary>Опционально в MVP (S, F).</summary>
        Optional,
        /// <summary>Полностью поддерживается.</summary>
        Enabled
    }

    /// <summary>Неизменяемые справочные данные элемента.</summary>
    public sealed class Element
    {
        public int Z { get; }
        public string Symbol { get; }
        public string NameRu { get; }
        public string NameEn { get; }
        /// <summary>Атомная масса, а.е.м. (0 — нет данных).</summary>
        public double Mass { get; }
        /// <summary>Электроотрицательность по Полингу (NaN — нет данных).</summary>
        public double Electronegativity { get; }
        public int ValenceElectrons { get; }
        /// <summary>Ковалентный радиус, Å.</summary>
        public double CovalentRadius { get; }
        /// <summary>Радиус Ван-дер-Ваальса, Å.</summary>
        public double VdwRadius { get; }
        /// <summary>Цвет CPK в палитре NERV, 0xRRGGBB.</summary>
        public uint Color { get; }
        public int Period { get; }
        /// <summary>Группа 1–18 (для лантаноидов и актиноидов — 3).</summary>
        public int Group { get; }
        /// <summary>Строка в раскладке таблицы (1–7, f-блок — 9 и 10).</summary>
        public int TableRow { get; }
        /// <summary>Столбец в раскладке таблицы (1–18).</summary>
        public int TableColumn { get; }
        public ElementCategory Category { get; }
        public ElementAvailability Availability { get; }

        public bool IsAvailable => Availability != ElementAvailability.ComingSoon;

        public bool IsMetal =>
            Category == ElementCategory.AlkaliMetal ||
            Category == ElementCategory.AlkalineEarthMetal ||
            Category == ElementCategory.TransitionMetal ||
            Category == ElementCategory.PostTransitionMetal ||
            Category == ElementCategory.Lanthanide ||
            Category == ElementCategory.Actinide;

        internal Element(
            int z, string symbol, string nameRu, string nameEn, double mass, double electronegativity,
            int valenceElectrons, double covalentRadius, double vdwRadius, uint color,
            int period, int group, int tableRow, int tableColumn,
            ElementCategory category, ElementAvailability availability)
        {
            Z = z;
            Symbol = symbol;
            NameRu = nameRu;
            NameEn = nameEn;
            Mass = mass;
            Electronegativity = electronegativity;
            ValenceElectrons = valenceElectrons;
            CovalentRadius = covalentRadius;
            VdwRadius = vdwRadius;
            Color = color;
            Period = period;
            Group = group;
            TableRow = tableRow;
            TableColumn = tableColumn;
            Category = category;
            Availability = availability;
        }

        public override string ToString() => Symbol;
    }
}
