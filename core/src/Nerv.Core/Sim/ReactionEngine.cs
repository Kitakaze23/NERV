using System;
using System.Collections.Generic;
using Nerv.Core.Chemistry;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Physics;
using Nerv.Core.Reactions;

namespace Nerv.Core.Sim
{
    /// <summary>Запись журнала: какая реакция сработала и какие конкретные атомы куда перешли.</summary>
    public sealed class ReactionRecord
    {
        public long Tick { get; internal set; }
        public ReactionTemplate Reaction { get; internal set; }
        /// <summary>Номер в карте атомов шаблона → атом сцены (для анимации следов и номеров атомов).</summary>
        public IReadOnlyDictionary<int, AtomId> AtomMap { get; internal set; }
        /// <summary>Центр реакции, Å.</summary>
        public Vec3 Position { get; internal set; }
        /// <summary>Эффективная температура столкновения, K.</summary>
        public double CollisionTemperature { get; internal set; }

        public override string ToString() => "[" + Tick + "] " + Reaction.Id + (Reaction.Summary != null ? " (" + Reaction.Summary + ")" : "");
    }

    /// <summary>
    /// Шаблонные реакции: находит молекулы в контакте, сопоставляет шаблоны, бросает «кубик» по Аррениусу
    /// (p = rate · exp(−Ea / R·T_эфф), T_эфф — кинетическая температура участвующих атомов),
    /// применяет изменения по карте атомов и выделяет/поглощает тепло ΔH вокруг центра реакции.
    /// </summary>
    internal sealed class ReactionEngine
    {
        /// <summary>
        /// Молекулы в контакте, если ближайшие атомы ближе этого расстояния, Å
        /// (≈ равновесное расстояние Ван-дер-Ваальса H…O, т. е. «касание» оболочек).
        /// </summary>
        public const double ContactDistance = 3.5;
        /// <summary>Радиус, в котором выделяется тепло реакции, Å.</summary>
        public const double HeatRadius = 4.5;

        private int[] _molOf = new int[64];

        public bool Apply(Simulation sim)
        {
            ChemistryData data = sim.Data;
            if (data == null || data.Reactions.Reactions.Count == 0) return false;

            World w = sim.World;
            w.EnsureTopology();
            int n = w.AtomCount;
            // Копия: при срабатывании реакции топология пересчитывается и список молекул в мире меняется.
            var mols = new List<Molecule>(w.Molecules);
            if (_molOf.Length < n) _molOf = new int[Math.Max(n, _molOf.Length * 2)];
            var molAtoms = new List<int>[mols.Count];
            for (int m = 0; m < mols.Count; m++)
            {
                molAtoms[m] = new List<int>(mols[m].Atoms.Count);
                foreach (AtomId id in mols[m].Atoms)
                {
                    int i = w.IndexOf(id);
                    _molOf[i] = m;
                    molAtoms[m].Add(i);
                }
            }

            var used = new bool[mols.Count];
            bool changed = false;
            Medium medium = sim.Settings.Medium;
            IReadOnlyList<ReactionTemplate> all = data.Reactions.Reactions;

            // Одночастичные шаги (диссоциация и т. п.).
            for (int m = 0; m < mols.Count; m++)
            {
                foreach (ReactionTemplate t in all)
                {
                    if (used[m] || t.Molecularity != 1 || !t.AllowsMedium(medium)) continue;
                    if (TryReact(sim, t, new[] { m }, molAtoms))
                    {
                        used[m] = true;
                        changed = true;
                    }
                }
            }

            // Двухчастичные шаги: только для молекул в контакте.
            bool anyBimolecular = false;
            foreach (ReactionTemplate t in all) if (t.Molecularity == 2 && t.AllowsMedium(medium)) anyBimolecular = true;
            if (!anyBimolecular) return changed;

            var contacts = new SortedSet<long>();
            double c2 = ContactDistance * ContactDistance;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    int mi = _molOf[i], mj = _molOf[j];
                    if (mi == mj) continue;
                    if ((w.Positions[j] - w.Positions[i]).LengthSquared >= c2) continue;
                    contacts.Add(mi < mj ? ((long)mi << 32) | (uint)mj : ((long)mj << 32) | (uint)mi);
                }
            }

            foreach (long pair in contacts)
            {
                int a = (int)(pair >> 32), b = (int)(pair & 0xFFFFFFFF);
                if (used[a] || used[b]) continue;
                foreach (ReactionTemplate t in all)
                {
                    if (t.Molecularity != 2 || !t.AllowsMedium(medium)) continue;
                    if (TryReact(sim, t, new[] { a, b }, molAtoms) || TryReact(sim, t, new[] { b, a }, molAtoms))
                    {
                        used[a] = used[b] = true;
                        changed = true;
                        break;
                    }
                }
            }
            return changed;
        }

        /// <summary>Перебирает совпадения шаблона; для первого геометрически возможного бросает кубик.</summary>
        private bool TryReact(Simulation sim, ReactionTemplate t, int[] molOrder, List<int>[] molAtoms)
        {
            World w = sim.World;
            bool decided = false, fired = false;
            var mapping = new Dictionary<int, int>();

            bool OnFull()
            {
                if (!Feasible(w, t, mapping)) return false; // ищем дальше
                decided = true;
                fired = Roll(sim, t, molOrder, molAtoms, mapping, out double tEff);
                if (fired) Fire(sim, t, mapping, tEff);
                return true; // решение принято — останавливаем перебор
            }

            bool OnFirst()
            {
                if (t.Molecularity == 1) return OnFull();
                return Match(w, t.ReactantComponents[1], molAtoms[molOrder[1]], mapping, OnFull);
            }

            Match(w, t.ReactantComponents[0], molAtoms[molOrder[0]], mapping, OnFirst);
            return decided && fired;
        }

        // ================= Сопоставление шаблона =================

        /// <summary>Поиск вложений шаблона в молекулу; onMatch возвращает true, чтобы остановить перебор.</summary>
        private static bool Match(World w, MolGraph q, List<int> candidates, Dictionary<int, int> mapping, Func<bool> onMatch)
        {
            List<int>[] qnb = q.Neighbors();
            var assigned = new int[q.Atoms.Count];
            for (int k = 0; k < assigned.Length; k++) assigned[k] = -1;
            var usedAtoms = new HashSet<int>();

            bool Step(int k)
            {
                if (k == q.Atoms.Count) return onMatch();
                MolAtom qa = q.Atoms[k];

                // Кандидаты: соседи уже сопоставленного соседа по шаблону, иначе — все атомы молекулы.
                IEnumerable<int> pool = candidates;
                foreach (int qn in qnb[k])
                {
                    if (qn < k && assigned[qn] >= 0)
                    {
                        pool = WorldNeighbors(w, assigned[qn]);
                        break;
                    }
                }

                foreach (int i in pool)
                {
                    if (usedAtoms.Contains(i) || !AtomMatches(w, qa, i)) continue;
                    bool bondsOk = true;
                    foreach (int qn in qnb[k])
                    {
                        if (qn >= k || assigned[qn] < 0) continue;
                        Bond wb = BondBetween(w, i, assigned[qn]);
                        int qo = q.FindBond(k, qn).Order;
                        if (wb == null || (qo != MolGraph.AnyOrder && wb.Order != qo)) { bondsOk = false; break; }
                    }
                    if (!bondsOk) continue;

                    assigned[k] = i;
                    usedAtoms.Add(i);
                    mapping[qa.Map] = i;
                    if (Step(k + 1)) return true;
                    mapping.Remove(qa.Map);
                    usedAtoms.Remove(i);
                    assigned[k] = -1;
                }
                return false;
            }

            return Step(0);
        }

        private static IEnumerable<int> WorldNeighbors(World w, int i)
        {
            foreach (Bond b in w.AdjacentBonds[i]) yield return b.Ia == i ? b.Ib : b.Ia;
        }

        private static Bond BondBetween(World w, int i, int j)
        {
            foreach (Bond b in w.AdjacentBonds[i])
                if ((b.Ia == i && b.Ib == j) || (b.Ib == i && b.Ia == j)) return b;
            return null;
        }

        private static bool AtomMatches(World w, MolAtom q, int i)
        {
            if (w.Elements[i].Z != q.Z) return false;
            if (w.Charges[i] != (q.ChargeSpecified ? q.Charge : 0)) return false;
            if (q.QueryDegree >= 0 && w.AdjacentBonds[i].Count != q.QueryDegree) return false;
            if (q.QueryHCount >= 0)
            {
                int h = 0;
                foreach (Bond b in w.AdjacentBonds[i])
                    if (w.Elements[b.Ia == i ? b.Ib : b.Ia].Z == 1) h++;
                if (h != q.QueryHCount) return false;
            }
            return true;
        }

        // ================= Решение и применение =================

        private static bool Feasible(World w, ReactionTemplate t, Dictionary<int, int> mapping)
        {
            // Новые связи — только между атомами, которые уже рядом.
            double c2 = ContactDistance * ContactDistance;
            foreach (KeyValuePair<long, int> kv in t.ProductBonds)
            {
                if (t.ReactantBonds.ContainsKey(kv.Key)) continue;
                ReactionTemplate.SplitPair(kv.Key, out int ma, out int mb);
                if ((w.Positions[mapping[ma]] - w.Positions[mapping[mb]]).LengthSquared >= c2) return false;
            }

            // Валентности после реакции (с учётом связей вне шаблона).
            foreach (KeyValuePair<int, int> kv in mapping)
            {
                int i = kv.Value;
                int sum = w.BondOrderSums[i];
                foreach (KeyValuePair<long, int> rb in t.ReactantBonds)
                    if (Involves(rb.Key, kv.Key)) sum -= OrderInWorld(w, t, mapping, rb.Key);
                foreach (KeyValuePair<long, int> pb in t.ProductBonds)
                    if (Involves(pb.Key, kv.Key)) sum += pb.Value;
                t.ProductCharges.TryGetValue(kv.Key, out int q);
                if (sum > ElectronRules.MaxValence(w.Elements[i], q)) return false;
            }
            return true;
        }

        private static bool Involves(long pair, int map)
        {
            ReactionTemplate.SplitPair(pair, out int a, out int b);
            return a == map || b == map;
        }

        private static int OrderInWorld(World w, ReactionTemplate t, Dictionary<int, int> mapping, long pair)
        {
            ReactionTemplate.SplitPair(pair, out int a, out int b);
            Bond wb = BondBetween(w, mapping[a], mapping[b]);
            return wb?.Order ?? 0;
        }

        private static bool Roll(Simulation sim, ReactionTemplate t, int[] molOrder, List<int>[] molAtoms, Dictionary<int, int> mapping, out double tEff)
        {
            World w = sim.World;
            double ke = 0;
            int count = 0;
            foreach (int m in molOrder)
                foreach (int i in molAtoms[m])
                {
                    ke += 0.5 * w.Masses[i] * w.Velocities[i].LengthSquared;
                    count++;
                }
            tEff = 2.0 * (ke / Units.AccelFactor) / (3.0 * count * Units.Kb);
            if (t.MinTemperature.HasValue && tEff < t.MinTemperature.Value) return false;

            double eaKcal = t.ActivationEnergyKj / 4.184;
            double p = eaKcal <= 0 ? t.Rate : tEff <= 1e-9 ? 0 : t.Rate * Math.Exp(-eaKcal / (Units.Kb * tEff));
            return sim.Rng.NextDouble() < p;
        }

        private static void Fire(Simulation sim, ReactionTemplate t, Dictionary<int, int> mapping, double tEff)
        {
            World w = sim.World;
            // Энергетический баланс: потенциальная энергия до и после смены топологии.
            // Шаблон меняет связи мгновенно, и фрагменты могут оказаться «внутри» друг друга;
            // этот артефакт компенсируем, чтобы суммарно выделилось ровно −ΔH из каталога.
            double before = ForceField.Compute(w, sim.Settings);

            var pairs = new SortedSet<long>(t.ReactantBonds.Keys);
            pairs.UnionWith(t.ProductBonds.Keys);
            foreach (long pair in pairs)
            {
                ReactionTemplate.SplitPair(pair, out int ma, out int mb);
                AtomId a = w.IdAt(mapping[ma]), b = w.IdAt(mapping[mb]);
                t.ProductBonds.TryGetValue(pair, out int newOrder);
                Bond existing = w.FindBond(a, b);
                if (existing != null)
                {
                    if (newOrder == 0)
                    {
                        w.RemoveBond(existing);
                        sim.Emit(SimEventType.BondBroken, a, b, existing.Id);
                    }
                    else if (existing.Order != newOrder)
                    {
                        w.SetBondOrder(existing, newOrder);
                        sim.Emit(SimEventType.BondOrderChanged, a, b, newOrder);
                    }
                }
                else if (newOrder > 0)
                {
                    Bond nb = w.AddBond(a, b, newOrder, 0.0);
                    nb.FromReaction = true;
                    sim.Emit(SimEventType.BondForming, a, b, nb.Id);
                }
            }

            foreach (KeyValuePair<int, int> kv in mapping)
            {
                t.ProductCharges.TryGetValue(kv.Key, out int q);
                if (w.Charges[kv.Value] != q) w.SetCharge(kv.Value, q);
            }

            Separate(w, mapping);

            Vec3 center = Vec3.Zero;
            foreach (int i in mapping.Values) center += w.Positions[i];
            center /= mapping.Count;
            double after = ForceField.Compute(w, sim.Settings);
            AddHeat(sim, center, -t.DeltaHKj / 4.184 * sim.Settings.ReactionHeatFraction - (after - before));

            var map = new SortedDictionary<int, AtomId>();
            foreach (KeyValuePair<int, int> kv in mapping) map[kv.Key] = w.IdAt(kv.Value);
            var record = new ReactionRecord { Tick = sim.Tick, Reaction = t, AtomMap = map, Position = center, CollisionTemperature = tEff };
            int index = sim.AddReactionRecord(record);

            AtomId first = map.Count > 0 ? map[FirstKey(map)] : AtomId.None;
            sim.Emit(SimEventType.ReactionFired, first, AtomId.None, index, t.Id);
            if (t.DeltaHKj != 0)
                sim.Emit(SimEventType.HeatReleased, first, AtomId.None, (int)Math.Round(-t.DeltaHKj), t.Effects);
        }

        private static int FirstKey(SortedDictionary<int, AtomId> map)
        {
            foreach (int k in map.Keys) return k;
            return 0;
        }

        /// <summary>
        /// Раздвигает фрагменты-продукты, которые после мгновенной смены связей оказались «внутри» друг друга.
        /// Фрагменты сдвигаются как твёрдые тела (без изменения скоростей), поэтому энергия не добавляется.
        /// </summary>
        private static void Separate(World w, Dictionary<int, int> mapping)
        {
            w.EnsureTopology();
            IReadOnlyList<Molecule> mols = w.Molecules;
            var fragOf = new Dictionary<int, int>();
            var fragments = new List<List<int>>();
            var fragMass = new List<double>();
            foreach (int i in mapping.Values)
            {
                if (fragOf.ContainsKey(i)) continue;
                foreach (Molecule m in mols)
                {
                    bool contains = false;
                    foreach (AtomId id in m.Atoms) if (w.IndexOf(id) == i) { contains = true; break; }
                    if (!contains) continue;
                    var atoms = new List<int>();
                    double mass = 0;
                    foreach (AtomId id in m.Atoms)
                    {
                        int k = w.IndexOf(id);
                        atoms.Add(k);
                        fragOf[k] = fragments.Count;
                        mass += w.Masses[k];
                    }
                    fragments.Add(atoms);
                    fragMass.Add(mass);
                    break;
                }
            }
            if (fragments.Count < 2) return;

            for (int pass = 0; pass < 20; pass++)
            {
                bool moved = false;
                for (int fa = 0; fa < fragments.Count; fa++)
                    for (int fb = fa + 1; fb < fragments.Count; fb++)
                        foreach (int i in fragments[fa])
                            foreach (int j in fragments[fb])
                            {
                                if (w.FreeValences[i] > 0 && w.FreeValences[j] > 0) continue; // эти пусть притягиваются
                                double rMin = 0.75 * Math.Sqrt(w.Types[i].X1 * w.Types[j].X1);
                                Vec3 d = w.Positions[j] - w.Positions[i];
                                double r = d.Length;
                                if (r >= rMin) continue;
                                Vec3 dir = r > 1e-6 ? d / r : new Vec3(1, 0, 0);
                                double gap = rMin - r;
                                double ma = fragMass[fa], mb = fragMass[fb];
                                Vec3 shiftA = dir * (-gap * mb / (ma + mb));
                                Vec3 shiftB = dir * (gap * ma / (ma + mb));
                                foreach (int k in fragments[fa]) w.Positions[k] += shiftA;
                                foreach (int k in fragments[fb]) w.Positions[k] += shiftB;
                                moved = true;
                            }
                if (!moved) break;
            }
        }

        /// <summary>Отнять до amount (а.е.м.·Å²/фс²) кинетической энергии у атомов; возвращает, сколько отнято.</summary>
        private static double Drain(World w, List<int> atoms, double amount)
        {
            double ke = 0;
            foreach (int i in atoms) ke += 0.5 * w.Masses[i] * w.Velocities[i].LengthSquared;
            if (ke <= 0) return 0;
            double taken = Math.Min(ke, amount);
            double lambda = Math.Sqrt(1.0 - taken / ke);
            foreach (int i in atoms) w.Velocities[i] *= lambda;
            return taken;
        }

        /// <summary>Добавить (kcal &gt; 0) или отнять кинетическую энергию у атомов вокруг точки.</summary>
        private static void AddHeat(Simulation sim, Vec3 center, double kcal)
        {
            if (kcal == 0) return;
            World w = sim.World;
            var atoms = new List<int>();
            double r2 = HeatRadius * HeatRadius;
            for (int i = 0; i < w.AtomCount; i++)
                if ((w.Positions[i] - center).LengthSquared < r2) atoms.Add(i);
            if (atoms.Count == 0) return;

            if (kcal > 0)
            {
                var kicks = new Vec3[atoms.Count];
                double keKick = 0;
                for (int k = 0; k < atoms.Count; k++)
                {
                    int i = atoms[k];
                    Vec3 u = Thermostat.SampleVelocity(sim.Rng, w.Masses[i], 1000.0);
                    kicks[k] = u;
                    keKick += 0.5 * w.Masses[i] * u.LengthSquared;
                }
                if (keKick <= 0) return;
                double c = Math.Sqrt(kcal * Units.AccelFactor / keKick);
                for (int k = 0; k < atoms.Count; k++) w.Velocities[atoms[k]] += kicks[k] * c;
            }
            else
            {
                // Забираем энергию сначала у атомов рядом, остаток — у всей сцены.
                double need = -kcal * Units.AccelFactor;
                need -= Drain(w, atoms, need);
                if (need > 0)
                {
                    var all = new List<int>(w.AtomCount);
                    for (int i = 0; i < w.AtomCount; i++) all.Add(i);
                    Drain(w, all, need);
                }
            }
        }
    }
}
