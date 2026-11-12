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

        /// <summary>Время по фазам шага.</summary>
        public SimulationProfile Profile { get; } = new SimulationProfile();

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

        /// <summary>Тик последней экзотермической реакции (для «режима пламени» термостата).</summary>
        private long _lastExothermicTick = long.MinValue / 2;

        /// <summary>Идёт горение/экзотермический процесс: реакция с выделением тепла была недавно.</summary>
        public bool FlameActive => Tick - _lastExothermicTick < Settings.FlameHoldTicks;

        internal int AddReactionRecord(ReactionRecord record)
        {
            if (record.Reaction.DeltaHKj < 0) _lastExothermicTick = Tick;
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
            long tStart = SimulationProfile.Now();
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
            w.TouchPositions();

            long t0 = SimulationProfile.Now();
            _rules.Apply(this);
            Profile.RulesMs += SimulationProfile.Ms(t0);
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
            {
                // Пока идёт горение, тепло реакций отводится медленно — пламя успевает поджечь соседей.
                // Выше потолка температуры — обычное (быстрое) охлаждение, чтобы продукты не распадались.
                double tau = Settings.ThermostatTauFs;
                if (FlameActive && Thermostat.Temperature(w) < Settings.FlameMaxTemperature) tau = Settings.FlameThermostatTauFs;
                Thermostat.Berendsen(w, Settings.TargetTemperature, dt, tau);
            }

            AdvanceBondProgress();

            w.EnsureTopology();
            bool topologyChanged = w.TopologyVersion != _lastTopologyVersion;
            if (topologyChanged)
            {
                _lastTopologyVersion = w.TopologyVersion;
                Emit(SimEventType.TopologyChanged, AtomId.None, AtomId.None, w.TopologyVersion);
                long ti = SimulationProfile.Now();
                _identifier.IdentifyMolecules(this);
                Profile.IdentifyMs += SimulationProfile.Ms(ti);
            }
            if (topologyChanged || Tick % Math.Max(1, IonicGroupInterval) == 0)
            {
                long tg = SimulationProfile.Now();
                _identifier.UpdateGroups(this);
                Profile.GroupsMs += SimulationProfile.Ms(tg);
            }

            Tick++;
            Profile.Ticks++;
            Profile.TotalMs += SimulationProfile.Ms(tStart);
        }

        private void ComputeForces()
        {
            long t0 = SimulationProfile.Now();
            PotentialEnergy = ForceField.ComputeCached(World, Settings, _grabList);
            Profile.ForcesMs += SimulationProfile.Ms(t0);
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
                double keRel = 0.5 * mi * (w.Velocities[i] - vcm).LengthSquared + 0.5 * mj * (w.Velocities[j] - vcm).LengthSquared;
                w.Velocities[i] = vcm + (w.Velocities[i] - vcm) * keep;
                w.Velocities[j] = vcm + (w.Velocities[j] - vcm) * keep;
                b.DrainedEnergy += keRel * (1.0 - keep * keep);

                b.Progress = Math.Min(1.0, b.Progress + inc);
                if (b.IsFormed)
                {
                    Emit(SimEventType.BondFormed, b.A, b.B, b.Id);
                    // В пламени рекомбинация радикалов (H + OH → H₂O и т. п.) — главный источник тепла:
                    // погашенную при сборке энергию отдаём соседям, а не теряем.
                    if (FlameActive && Settings.AssemblyHeatFraction > 0 && b.DrainedEnergy > 0)
                        ReactionEngine.AddHeat(this, (w.Positions[i] + w.Positions[j]) * 0.5,
                            b.DrainedEnergy / Units.AccelFactor * Settings.AssemblyHeatFraction, i, j);
                    b.DrainedEnergy = 0;
                }
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
            snap.FlameActive = FlameActive;
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
                snap.Temperatures[i] = (float)(w.Masses[i] * w.Velocities[i].LengthSquared / Units.AccelFactor / (3.0 * Units.Kb));
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
            w.EnsureTopology();
            // Искра включает «режим пламени»: её тепло не сбрасывается термостатом мгновенно.
            _lastExothermicTick = Tick;
            double r2 = c.Radius * c.Radius;
            for (int i = 0; i < w.AtomCount; i++)
            {
                if ((w.Positions[i] - c.Center).LengthSquared > r2) continue;
                w.Velocities[i] += Thermostat.SampleVelocity(_rng, w.Masses[i], c.Kelvin);
            }

            // Расщепление связей на радикалы (гомолиз): выбираем случайные связи в радиусе.
            // Если щёлкнули в пустоту — ищем молекулы чуть дальше (до двух радиусов).
            var candidates = new List<Bond>();
            for (double reach = r2; candidates.Count == 0 && reach <= 4.0 * r2; reach *= 2.0)
                foreach (Bond b in w.Bonds)
                    if (b.IsFormed &&
                        (w.Positions[b.Ia] - c.Center).LengthSquared <= reach &&
                        (w.Positions[b.Ib] - c.Center).LengthSquared <= reach)
                        candidates.Add(b);

            // Разные виды связей по очереди (H–H, O=O, …): в смеси искра даёт радикалы каждого сорта,
            // иначе можно получить только атомы H, которые тут же снова соберутся в H₂.
            var kinds = new List<List<Bond>>();
            var kindIndex = new Dictionary<(int, int, int), int>();
            foreach (Bond b in candidates)
            {
                int za = w.Elements[b.Ia].Z, zb = w.Elements[b.Ib].Z;
                var key = (Math.Min(za, zb), Math.Max(za, zb), b.Order);
                if (!kindIndex.TryGetValue(key, out int ki))
                {
                    kindIndex[key] = ki = kinds.Count;
                    kinds.Add(new List<Bond>());
                }
                kinds[ki].Add(b);
            }
            int kindStart = kinds.Count == 0 ? 0 : (int)(_rng.NextDouble() * kinds.Count);

            for (int k = 0, left = candidates.Count; k < c.Radicals && left > 0; k++)
            {
                List<Bond> pool = null;
                for (int t = 0; t < kinds.Count && pool == null; t++)
                {
                    List<Bond> kl = kinds[(kindStart + k + t) % kinds.Count];
                    if (kl.Count > 0) pool = kl;
                }
                int pick = (int)(_rng.NextDouble() * pool.Count);
                Bond b = pool[pick];
                pool.RemoveAt(pick);
                left--;
                int i = b.Ia, j = b.Ib;

                // Двухатомную молекулу (O₂, Cl₂) искра разбивает на атомы целиком — как в разряде;
                // в многоатомных кратная связь лишь понижает порядок, чтобы не крошить молекулу.
                bool diatomic = w.AdjacentBonds[i].Count == 1 && w.AdjacentBonds[j].Count == 1;
                if (b.Order > 1 && !diatomic) w.SetBondOrder(b, b.Order - 1);
                else w.RemoveBond(b);
                Emit(SimEventType.BondBroken, b.A, b.B, b.Id);
                _rules.ForbidRebond(b.A, b.B, Tick + SparkRebondCooldownTicks);

                // Осколки разлетаются с равными и противоположными импульсами.
                Vec3 axis = (w.Positions[j] - w.Positions[i]).Normalized();
                double mi = w.Masses[i], mj = w.Masses[j];
                double mu = mi * mj / (mi + mj);
                double p = Math.Sqrt(2.0 * SparkFragmentEnergyKcal * Units.AccelFactor * mu);
                w.Velocities[i] -= axis * (p / mi);
                w.Velocities[j] += axis * (p / mj);
            }
        }

        /// <summary>Энергия разлёта осколков после расщепления связи искрой, ккал/моль.</summary>
        private const double SparkFragmentEnergyKcal = 30.0;
        /// <summary>Сколько тиков осколкам искры нельзя снова соединиться друг с другом.</summary>
        private const int SparkRebondCooldownTicks = 200;

        private void RebuildGrabList()
        {
            _grabList.Clear();
            foreach (KeyValuePair<int, GrabSpring> kv in _grabs) _grabList.Add(kv.Value);
        }
    }
}
