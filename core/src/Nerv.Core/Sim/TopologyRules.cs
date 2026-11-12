using System;
using System.Collections.Generic;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Numerics;

namespace Nerv.Core.Sim
{
    /// <summary>
    /// Слой топологии: решает, какие связи существуют.
    /// Порядок: (шаблонные реакции — этап 2) → свободная сборка / перенос электрона → разрыв → повышение порядка.
    /// </summary>
    internal sealed class TopologyRules
    {
        private struct Candidate
        {
            public double R2;
            public int I, J;
            public bool Ionic;
        }

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly List<Bond> _bondScratch = new List<Bond>();
        private int[] _free = new int[64];
        private bool[] _touched = new bool[64];

        /// <summary>Возвращает true, если топология изменилась.</summary>
        public bool Apply(Simulation sim)
        {
            World w = sim.World;
            w.EnsureTopology();
            SimulationSettings s = sim.Settings;

            bool changed = FormBonds(sim, w, s);
            changed |= BreakBonds(sim, w, s);
            if (!changed) changed |= UpgradeOrders(sim, w, s);
            return changed;
        }

        private bool FormBonds(Simulation sim, World w, SimulationSettings s)
        {
            int n = w.AtomCount;
            if (_free.Length < n)
            {
                _free = new int[Math.Max(n, _free.Length * 2)];
                _touched = new bool[_free.Length];
            }

            _candidates.Clear();
            for (int i = 0; i < n; i++)
            {
                _free[i] = w.FreeValences[i];
                _touched[i] = false;
            }

            double maxForm = s.CaptureRadius;
            for (int i = 0; i < n; i++)
            {
                if (_free[i] <= 0) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (_free[j] <= 0 || w.IsExcluded(i, j)) continue;
                    double r2 = (w.Positions[j] - w.Positions[i]).LengthSquared;
                    if (r2 > maxForm * maxForm) continue;

                    Element ei = w.Elements[i], ej = w.Elements[j];
                    bool ionic = ElectronRules.IsIonicPair(ei, ej, s.IonicElectronegativityThreshold);
                    double formR = ionic
                        ? s.IonicFormFactor * (w.Types[i].R1 + w.Types[j].R1)
                        : s.CovalentFormFactor * Uff.RestLength(w.Types[i], w.Types[j], 1);
                    if (r2 < formR * formR) _candidates.Add(new Candidate { R2 = r2, I = i, J = j, Ionic = ionic });
                }
            }

            if (_candidates.Count == 0) return false;

            // Ближайшие пары первыми; при равенстве — по индексам (детерминизм).
            _candidates.Sort((a, b) =>
            {
                int c = a.R2.CompareTo(b.R2);
                if (c != 0) return c;
                c = a.I.CompareTo(b.I);
                return c != 0 ? c : a.J.CompareTo(b.J);
            });

            bool changed = false;
            foreach (Candidate c in _candidates)
            {
                int i = c.I, j = c.J;
                // Не больше одной новой связи на атом за тик — кэш исключений (1-3) иначе устаревает.
                if (_touched[i] || _touched[j] || _free[i] <= 0 || _free[j] <= 0) continue;

                AtomId idI = w.IdAt(i), idJ = w.IdAt(j);
                if (c.Ionic)
                {
                    int metal = w.Elements[i].IsMetal ? i : j;
                    int nonmetal = metal == i ? j : i;
                    w.SetCharge(metal, w.Charges[metal] + 1);
                    w.SetCharge(nonmetal, w.Charges[nonmetal] - 1);
                    sim.Emit(SimEventType.ElectronTransferred, w.IdAt(metal), w.IdAt(nonmetal), 1);
                }
                else
                {
                    Bond b = w.AddBond(idI, idJ, 1, 0.0);
                    sim.Emit(SimEventType.BondForming, idI, idJ, b.Id);
                }

                _free[i]--;
                _free[j]--;
                _touched[i] = _touched[j] = true;
                changed = true;
            }

            return changed;
        }

        private bool BreakBonds(Simulation sim, World w, SimulationSettings s)
        {
            if (w.TopologyDirty) return false; // Индексы связей из этого тика ещё не пересчитаны.

            _bondScratch.Clear();
            IReadOnlyList<Bond> bonds = w.Bonds;
            double breakShift = -Math.Log(1.0 - Math.Sqrt(s.BondBreakEnergyFraction));
            for (int k = 0; k < bonds.Count; k++)
            {
                Bond b = bonds[k];
                double r = Vec3.Distance(w.Positions[b.Ia], w.Positions[b.Ib]);
                double limit = b.IsFormed ? b.RestLength + breakShift / b.Alpha : s.CaptureRadius;
                if (r > limit) _bondScratch.Add(b);
            }

            foreach (Bond b in _bondScratch)
            {
                w.RemoveBond(b);
                sim.Emit(SimEventType.BondBroken, b.A, b.B, b.Id);
            }

            return _bondScratch.Count > 0;
        }

        /// <summary>
        /// Если у двух связанных атомов осталась свободная валентность и рядом нет других партнёров —
        /// связь становится кратной (O–O → O=O, N–N → N≡N).
        /// </summary>
        private bool UpgradeOrders(Simulation sim, World w, SimulationSettings s)
        {
            w.EnsureTopology();
            bool changed = false;
            IReadOnlyList<Bond> bonds = w.Bonds;
            double rc2 = s.CaptureRadius * s.CaptureRadius;

            for (int k = 0; k < bonds.Count; k++)
            {
                Bond b = bonds[k];
                if (!b.IsFormed || b.Order >= 3) continue;
                int i = b.Ia, j = b.Ib;
                if (w.FreeValences[i] <= 0 || w.FreeValences[j] <= 0) continue;
                if (HasOtherPartner(w, i, j, rc2) || HasOtherPartner(w, j, i, rc2)) continue;

                w.SetBondOrder(b, b.Order + 1);
                sim.Emit(SimEventType.BondOrderChanged, b.A, b.B, b.Order);
                changed = true;
                break; // одна за тик: свободные валентности в кэше уже устарели
            }
            return changed;
        }

        private static bool HasOtherPartner(World w, int i, int except, double rc2)
        {
            for (int k = 0; k < w.AtomCount; k++)
            {
                if (k == i || k == except || w.FreeValences[k] <= 0 || w.IsExcluded(i, k)) continue;
                if ((w.Positions[k] - w.Positions[i]).LengthSquared < rc2) return true;
            }
            return false;
        }
    }
}
