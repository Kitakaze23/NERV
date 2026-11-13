using System;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Numerics;

namespace Nerv.Core.Sim
{
    /// <summary>
    /// Любое изменение мира извне — команда. Команды проходят через очередь и журнал:
    /// это даёт повторы, сохранения, отмену и (позже) сетевую игру поверх тех же команд.
    /// </summary>
    public abstract class Command
    {
        internal abstract void Execute(Simulation sim);
    }

    public sealed class AddAtom : Command
    {
        public int AtomicNumber { get; }
        public Vec3 Position { get; }
        public int Charge { get; }
        /// <summary>Id созданного атома (заполняется при выполнении).</summary>
        public AtomId Result { get; internal set; }

        public AddAtom(int atomicNumber, Vec3 position, int charge = 0)
        {
            Element e = ElementTable.Get(atomicNumber);
            if (!e.IsAvailable) throw new ArgumentException("Элемент " + e.Symbol + " пока недоступен.", nameof(atomicNumber));
            AtomicNumber = atomicNumber;
            Position = position;
            Charge = charge;
        }

        public AddAtom(string symbol, Vec3 position, int charge = 0) : this(ElementTable.Get(symbol).Z, position, charge) { }

        internal override void Execute(Simulation sim) => Result = sim.ExecAddAtom(this);
    }

    public sealed class RemoveAtom : Command
    {
        public AtomId Atom { get; }
        public RemoveAtom(AtomId atom) { Atom = atom; }
        internal override void Execute(Simulation sim) => sim.ExecRemoveAtom(this);
    }

    /// <summary>Мгновенная связь (для заготовок и тестов). Без проверки валентности.</summary>
    public sealed class AddBond : Command
    {
        public AtomId A { get; }
        public AtomId B { get; }
        public int Order { get; }
        public AddBond(AtomId a, AtomId b, int order = 1) { A = a; B = b; Order = order; }
        internal override void Execute(Simulation sim) => sim.ExecAddBond(this);
    }

    /// <summary>Схватить атом «рукой». HandId позволяет нескольким рукам/игрокам держать разные атомы.</summary>
    public sealed class Grab : Command
    {
        public int HandId { get; }
        public AtomId Atom { get; }
        public Vec3 Target { get; }
        public Grab(AtomId atom, Vec3 target, int handId = 0) { Atom = atom; Target = target; HandId = handId; }
        internal override void Execute(Simulation sim) => sim.ExecGrab(this);
    }

    public sealed class MoveGrab : Command
    {
        public int HandId { get; }
        public Vec3 Target { get; }
        public MoveGrab(Vec3 target, int handId = 0) { Target = target; HandId = handId; }
        internal override void Execute(Simulation sim) => sim.ExecMoveGrab(this);
    }

    public sealed class Release : Command
    {
        public int HandId { get; }
        public Release(int handId = 0) { HandId = handId; }
        internal override void Execute(Simulation sim) => sim.ExecRelease(this);
    }

    public sealed class SetTemperature : Command
    {
        public double Kelvin { get; }
        public SetTemperature(double kelvin)
        {
            if (kelvin < 0) throw new ArgumentOutOfRangeException(nameof(kelvin));
            Kelvin = kelvin;
        }
        internal override void Execute(Simulation sim) => sim.Settings.TargetTemperature = Kelvin;
    }

    public sealed class SetMedium : Command
    {
        public Medium Medium { get; }
        public SetMedium(Medium medium) { Medium = medium; }
        internal override void Execute(Simulation sim) => sim.Settings.Medium = Medium;
    }

    public sealed class SetTimeScale : Command
    {
        public double Scale { get; }
        public SetTimeScale(double scale)
        {
            if (scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
            Scale = scale;
        }
        internal override void Execute(Simulation sim) => sim.Settings.TimeScale = Scale;
    }

    /// <summary>
    /// «Искра» (электрический разряд): резкий локальный нагрев атомов в радиусе и расщепление нескольких
    /// связей на радикалы — именно радикалы запускают цепные реакции (горение).
    /// </summary>
    public sealed class Spark : Command
    {
        public Vec3 Center { get; }
        public double Radius { get; }
        public double Kelvin { get; }
        /// <summary>Сколько связей расщепить на радикалы (0 — только нагрев).</summary>
        public int Radicals { get; }
        public Spark(Vec3 center, double radius = 3.0, double kelvin = 5000.0, int radicals = 3)
        {
            Center = center;
            Radius = radius;
            Kelvin = kelvin;
            Radicals = radicals;
        }
        internal override void Execute(Simulation sim) => sim.ExecSpark(this);
    }

    /// <summary>
    /// Инструменты «Нагрев» и «Холод»: на время <see cref="DurationTicks"/> у атомов в сфере
    /// температура подтягивается к <see cref="Kelvin"/> (свой термостат зоны, сильнее общего).
    /// Клиент повторяет команду, пока держит кнопку, — зона продлевается.
    /// </summary>
    public sealed class LocalTemperature : Command
    {
        public Vec3 Center { get; }
        public double Radius { get; }
        public double Kelvin { get; }
        public int DurationTicks { get; }
        public LocalTemperature(Vec3 center, double radius, double kelvin, int durationTicks = 200)
        {
            Center = center;
            Radius = radius;
            Kelvin = kelvin;
            DurationTicks = durationTicks;
        }
        internal override void Execute(Simulation sim) => sim.ExecLocalTemperature(this);
    }

    /// <summary>
    /// Собрать в сцене готовую молекулу по SMILES (кнопка «Собрать в сцене» у изомеров, вставка из базы).
    /// Ядро ставит её около <see cref="Center"/>, но не впритык к уже имеющимся атомам; ориентация случайная.
    /// </summary>
    public sealed class BuildMolecule : Command
    {
        public string Smiles { get; }
        public Vec3 Center { get; }
        /// <summary>Атомы новой молекулы (заполняется при исполнении; пусто, если SMILES не разобран).</summary>
        public AtomId[] Result { get; internal set; } = Array.Empty<AtomId>();
        public BuildMolecule(string smiles, Vec3 center)
        {
            Smiles = smiles ?? throw new ArgumentNullException(nameof(smiles));
            Center = center;
        }
        internal override void Execute(Simulation sim) => sim.ExecBuildMolecule(this);
    }
}
