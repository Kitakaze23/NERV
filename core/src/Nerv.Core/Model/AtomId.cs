using System;

namespace Nerv.Core.Model
{
    /// <summary>
    /// Стабильный идентификатор атома: слот + поколение. Индексы в массивах меняются при удалении,
    /// id — нет. Если атом удалён, его id никогда не перейдёт к новому атому (поколение растёт).
    /// </summary>
    public readonly struct AtomId : IEquatable<AtomId>
    {
        public readonly int Slot;
        public readonly int Generation;

        public AtomId(int slot, int generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public static readonly AtomId None = default;
        public bool IsNone => Generation == 0;

        public bool Equals(AtomId other) => Slot == other.Slot && Generation == other.Generation;
        public override bool Equals(object obj) => obj is AtomId other && Equals(other);
        public override int GetHashCode() => (Slot * 397) ^ Generation;
        public static bool operator ==(AtomId a, AtomId b) => a.Equals(b);
        public static bool operator !=(AtomId a, AtomId b) => !a.Equals(b);

        /// <summary>Порядок для детерминированных обходов.</summary>
        public int CompareTo(AtomId other) => Slot != other.Slot ? Slot.CompareTo(other.Slot) : Generation.CompareTo(other.Generation);

        public override string ToString() => IsNone ? "Atom(none)" : "Atom(" + Slot + "#" + Generation + ")";
    }
}
