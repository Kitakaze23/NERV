using System;
using System.Collections.Generic;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Naming;
using Nerv.Core.Numerics;
using Nerv.Core.Physics;

namespace Nerv.Core.Sim
{
    /// <summary>
    /// Ядро симуляции: очередь команд → шаг (Верле + правила топологии + термостат) → события и снимки.
    /// Детерминировано: фиксированный шаг, собственный ГСЧ с seed, детерминированный порядок обходов.
    /// Не потокобезопасно: хост вызывает Step из одного потока и читает снимки.
    /// </summary>
    public sealed class Simulation
    {
        public World World { get; } = new World();
        public SimulationSettings Settings { get; }
        public long Tick { get; private set; }
        public ulong Seed { get; }
        /// <summary>Химические данные (названия, ионы, реакции). Без них ядро работает только на правилах валентности.</summary>
        public ChemistryData Data { get; }

        /// <summary>Как часто пересчитывать ионные ассоциации, тиков.</summary>
        public int IonicGroupInterval { get; set; } = 25;

        /// <summary>Потенциальная энергия на последнем шаге, ккал/моль.</summary>
        public double PotentialEnergy { get; private set; }

        private readonly Xoshiro256 _rng;
        private readonly Queue<Command> _pending = new Queue<Command>();
        private readonly List<KeyValuePair<long, Command>> _log = new List<KeyValuePair<long, Command>>();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly SortedDictionary<int, GrabSpring> _grabs = new SortedDictionary<int, GrabSpring>();
        private readonly List<GrabSpring> _grabList = new List<GrabSpring>();
        private readonly TopologyRules _rules = new TopologyRules();
        private readonly Identifier _identifier = new Identifier();
        private readonly List<ReactionRecord> _reactionLog = new List<ReactionRecord>();
        private bool _forcesStale = true;
        private int _lastTopologyVersion = -1;

        public Simulation(SimulationSettings settings = null, ulong seed = 1, ChemistryData data = null)
        {
            Settings = settings ?? new SimulationSettings();
            Seed = seed;
            Data = data;
            _rng = new Xoshiro256(seed);
            World.BondEnergyPerOrder = Settings.BondEnergyPerOrder;
        }

        /// <summary>События, накопленные с последнего <see cref="ClearEvents"/>.</summary>
        public IReadOnlyList<SimEvent> Events => _events;
        public void ClearEvents() => _events.Clear();

        /// <summary>Журнал выполненных команд с номером тика — для повторов и сохранений.</summary>
        public IReadOnlyList<KeyValuePair<long, Command>> CommandLog => _log;

        /// <summary>Журнал сработавших реакций каталога.</summary>
        public IReadOnlyList<ReactionRecord> ReactionLog => _reactionLog;

        /// <summary>Ионные ассоциации (кристаллики солей); в воде — пусто.</summary>
        public IReadOnlyList<IonicGroup> IonicGroups => _identifier.Groups;

        internal Xoshiro256 Rng => _rng;

        internal int AddReactionRecord(ReactionRecord record)
        {
            _reactionLog.Add(record);
            return _reactionLog.Count - 1;
        }

        public double Temperature => Thermostat.Temperature(World);
        public double KineticEnergy => Thermostat.KineticEnergy(World);

        /// <summary>Поставить команду в очередь: выполнится в начале следующего тика.</summary>
        public void Enqueue(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            _pending.Enqueue(command);
        }

        /// <summary>Выполнить команду немедленно (до следующего шага).</summary>
        public T Apply<T>(T command) where T : Command
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            FlushPending();
            Execute(command);
            return command;
        }

        public void Step(int ticks = 1)
        {
            for (int t = 0; t < ticks; t++) StepOnce();
        }

        private void FlushPending()
        {
            while (_pending.Count > 0) Execute(_pending.Dequeue());
        }

        private void Execute(Command command)
        {
            _log.Add(new KeyValuePair<long, Command>(Tick, command));
            command.Execute(this);
            _forcesStale = true;
        }

        private void StepOnce()
        {
            FlushPending();
            World w = World;
            World.BondEnergyPerOrder = Settings.BondEnergyPerOrder;
            double dt = Settings.TimeStepFs;
            int n = w.AtomCount;

            if (_forcesStale || w.TopologyDirty) ComputeForces();

            // Скоростной Верле: полушаг скорости + шаг координат.
            for (int i = 0; i < n; i++)
            {
                Vec3 a = w.Forces[i] * (Units.AccelFactor / w.Masses[i]);
                w.Velocities[i] += a * (0.5 * dt);
                w.Positions[i] += w.Velocities[i] * dt;
            }

            _rules.Apply(this);
            ComputeForces();

            for (int i = 0; i < n; i++)
            {
                Vec3 a = w.Forces[i] * (Units.AccelFactor / w.Masses[i]);
                w.Velocities[i] += a * (0.5 * dt);
            }

            foreach (GrabSpring g in _grabList)
            {
                int i = w.IndexOf(g.Atom);
                if (i >= 0) w.Velocities[i] *= Settings.GrabVelocityRetention;
            }

            if (Settings.ThermostatEnabled)
                Thermostat.Berendsen(w, Settings.TargetTemperature, dt, Settings.ThermostatTauFs);

            AdvanceBondProgress();

            w.EnsureTopology();
            bool topologyChanged = w.TopologyVersion != _lastTopologyVersion;
            if (topologyChanged)
            {
                _lastTopologyVersion = w.TopologyVersion;
                Emit(SimEventType.TopologyChanged, AtomId.None, AtomId.None, w.TopologyVersion);
                _identifier.IdentifyMolecules(this);
            }
            if (topologyChanged || Tick % Math.Max(1, IonicGroupInterval) == 0)
                _identifier.UpdateGroups(this);

            Tick++;
        }

        private void ComputeForces()
        {
            PotentialEnergy = ForceField.Compute(World, Settings, _grabList);
            _forcesStale = false;
        }

        private void AdvanceBondProgress()
        {
            double inc = 1.0 / Math.Max(1, Settings.BondFormTicks);
            double keep = Settings.FormingVelocityRetention;
            World w = World;
            IReadOnlyList<Bond> bonds = w.Bonds;
            for (int k = 0; k < bonds.Count; k++)
            {
                Bond b = bonds[k];
                if (b.IsFormed) continue;

                // Гасим относительное движение пары, сохраняя импульс центра масс
                // (для связей из реакций энергия уже учтена в ΔH).
                int i = b.Ia, j = b.Ib;
                if (b.FromReaction)
                {
                    b.Progress = Math.Min(1.0, b.Progress + inc);
                    if (b.IsFormed) Emit(SimEventType.BondFormed, b.A, b.B, b.Id);
                    continue;
                }
                double mi = w.Masses[i], mj = w.Masses[j];
                Vec3 vcm = (w.Velocities[i] * mi + w.Velocities[j] * mj) / (mi + mj);
                w.Velocities[i] = vcm + (w.Velocities[i] - vcm) * keep;
                w.Velocities[j] = vcm + (w.Velocities[j] - vcm) * keep;

                b.Progress = Math.Min(1.0, b.Progress + inc);
                if (b.IsFormed) Emit(SimEventType.BondFormed, b.A, b.B, b.Id);
            }
        }

        internal void Emit(SimEventType type, AtomId a, AtomId b, int value, string text = null) =>
            _events.Add(new SimEvent(type, Tick, a, b, value, text));

        // ================= Снимок =================

        public void WriteSnapshot(WorldSnapshot snap)
        {
            World w = World;
            w.EnsureTopology();
            int n = w.AtomCount;
            snap.EnsureCapacity(n);
            snap.Tick = Tick;
            snap.TopologyVersion = w.TopologyVersion;
            snap.Temperature = Temperature;
            snap.AtomCount = n;
            for (int i = 0; i < n; i++)
            {
                snap.Ids[i] = w.IdAt(i);
                snap.AtomicNumbers[i] = w.Elements[i].Z;
                snap.Charges[i] = w.Charges[i];
                snap.FreeValences[i] = w.FreeValences[i];
                snap.LonePairs[i] = w.LonePairCounts[i];
                snap.Positions[3 * i] = (float)w.Positions[i].X;
                snap.Positions[3 * i + 1] = (float)w.Positions[i].Y;
                snap.Positions[3 * i + 2] = (float)w.Positions[i].Z;
            }

            snap.Bonds.Clear();
            foreach (Bond b in w.Bonds)
            {
                snap.Bonds.Add(new BondView
                {
                    Id = b.Id,
                    IndexA = b.Ia,
                    IndexB = b.Ib,
                    Order = b.Order,
                    Progress = (float)b.Progress,
                    IsPolar = b.IsPolar
                });
            }
            snap.Molecules = new List<Molecule>(w.Molecules);
            snap.IonicGroups = new List<IonicGroup>(_identifier.Groups);
        }

        // ================= Исполнение команд =================

        internal AtomId ExecAddAtom(AddAtom c)
        {
            Element e = ElementTable.Get(c.AtomicNumber);
            AtomId id = World.AddAtom(e, c.Position, c.Charge);
            int i = World.IndexOf(id);
            World.Velocities[i] = Thermostat.SampleVelocity(_rng, e.Mass, Settings.TargetTemperature);
            Emit(SimEventType.AtomAdded, id, AtomId.None, c.AtomicNumber);
            return id;
        }

        internal void ExecRemoveAtom(RemoveAtom c)
        {
            if (!World.IsAlive(c.Atom)) return;
            var handsToRelease = new List<int>();
            foreach (KeyValuePair<int, GrabSpring> kv in _grabs)
                if (kv.Value.Atom == c.Atom) handsToRelease.Add(kv.Key);
            foreach (int h in handsToRelease) _grabs.Remove(h);
            RebuildGrabList();

            foreach (Bond b in World.Bonds)
                if (b.Contains(c.Atom)) Emit(SimEventType.BondBroken, b.A, b.B, b.Id);
            World.RemoveAtom(c.Atom);
            Emit(SimEventType.AtomRemoved, c.Atom, AtomId.None, 0);
        }

        internal void ExecAddBond(AddBond c)
        {
            if (!World.IsAlive(c.A) || !World.IsAlive(c.B) || World.FindBond(c.A, c.B) != null) return;
            Bond b = World.AddBond(c.A, c.B, c.Order, 1.0);
            Emit(SimEventType.BondFormed, c.A, c.B, b.Id);
        }

        internal void ExecGrab(Grab c)
        {
            if (!World.IsAlive(c.Atom)) return;
            _grabs[c.HandId] = new GrabSpring { Atom = c.Atom, Target = c.Target };
            RebuildGrabList();
        }

        internal void ExecMoveGrab(MoveGrab c)
        {
            if (!_grabs.TryGetValue(c.HandId, out GrabSpring g)) return;
            g.Target = c.Target;
            _grabs[c.HandId] = g;
            RebuildGrabList();
        }

        internal void ExecRelease(Release c)
        {
            if (_grabs.Remove(c.HandId)) RebuildGrabList();
        }

        internal void ExecSpark(Spark c)
        {
            World w = World;
            double r2 = c.Radius * c.Radius;
            for (int i = 0; i < w.AtomCount; i++)
            {
                if ((w.Positions[i] - c.Center).LengthSquared > r2) continue;
                w.Velocities[i] += Thermostat.SampleVelocity(_rng, w.Masses[i], c.Kelvin);
            }
        }

        private void RebuildGrabList()
        {
            _grabList.Clear();
            foreach (KeyValuePair<int, GrabSpring> kv in _grabs) _grabList.Add(kv.Value);
        }
    }
}
