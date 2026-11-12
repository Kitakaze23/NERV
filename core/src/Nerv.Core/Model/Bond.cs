namespace Nerv.Core.Model
{
    /// <summary>Ковалентная связь. Ионная связь связью не является — ионы держит кулоновское взаимодействие.</summary>
    public sealed class Bond
    {
        public int Id { get; }
        public AtomId A { get; }
        public AtomId B { get; }
        /// <summary>Порядок связи 1–3.</summary>
        public int Order { get; internal set; }
        /// <summary>0..1 — плавное «включение» связи, чтобы смена топологии не взрывала молекулу.</summary>
        public double Progress { get; internal set; }
        public bool IsFormed => Progress >= 1.0;

        /// <summary>Полярность по разнице электроотрицательностей (|ΔЭО| &gt; 0.4).</summary>
        public bool IsPolar { get; internal set; }

        // Кэш топологии (пересчитывается в World.RebuildTopology).
        internal int Ia, Ib;
        /// <summary>Равновесная длина, Å.</summary>
        public double RestLength { get; internal set; }
        /// <summary>Глубина ямы Морзе, ккал/моль.</summary>
        internal double De;
        /// <summary>Параметр ширины ямы Морзе, 1/Å.</summary>
        internal double Alpha;

        internal Bond(int id, AtomId a, AtomId b, int order, double progress)
        {
            Id = id;
            A = a;
            B = b;
            Order = order;
            Progress = progress;
        }

        public AtomId Other(AtomId id) => id == A ? B : A;
        public bool Contains(AtomId id) => id == A || id == B;

        public override string ToString() => "Bond#" + Id + "(" + A + "-" + B + ", order " + Order + ")";
    }
}
