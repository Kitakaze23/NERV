using Nerv.Core.Model;

namespace Nerv.Core.Sim
{
    public enum SimEventType
    {
        AtomAdded,
        AtomRemoved,
        /// <summary>Связь начала образовываться (progress = 0). Value = id связи.</summary>
        BondForming,
        /// <summary>Связь полностью образовалась (progress = 1). Value = id связи.</summary>
        BondFormed,
        /// <summary>Связь разорвана. Value = id связи.</summary>
        BondBroken,
        /// <summary>Изменился порядок связи. Value = новый порядок.</summary>
        BondOrderChanged,
        /// <summary>Электрон перешёл от A (металл) к B (неметалл).</summary>
        ElectronTransferred,
        /// <summary>Изменилась топология (состав молекул). Value = версия топологии.</summary>
        TopologyChanged,
        /// <summary>
        /// Появилась известная частица. Text = название, A = один из её атомов,
        /// Value = 0 для молекулы/иона, 1 для ионного соединения (ассоциации ионов).
        /// </summary>
        MoleculeIdentified,
        /// <summary>Сработала реакция из каталога. Text = id реакции, Value = индекс в Simulation.ReactionLog.</summary>
        ReactionFired,
        /// <summary>Выделилось (Value &gt; 0) или поглотилось тепло, кДж/моль. Text = пресет эффекта.</summary>
        HeatReleased
    }

    /// <summary>
    /// Событие ядра для слоя представления: эффекты, журнал, уроки подписываются на события,
    /// а не опрашивают мир.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly SimEventType Type;
        public readonly long Tick;
        public readonly AtomId A;
        public readonly AtomId B;
        public readonly int Value;
        public readonly string Text;

        public SimEvent(SimEventType type, long tick, AtomId a, AtomId b, int value, string text = null)
        {
            Type = type;
            Tick = tick;
            A = a;
            B = b;
            Value = value;
            Text = text;
        }

        public override string ToString() => "[" + Tick + "] " + Type + " " + A + " " + B + " " + Value + (Text != null ? " «" + Text + "»" : "");
    }
}
