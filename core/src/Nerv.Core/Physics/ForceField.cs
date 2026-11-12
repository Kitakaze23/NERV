using System;
using System.Collections.Generic;
using Nerv.Core.Chemistry;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Physics
{
    /// <summary>Пружина захвата атома рукой/мышью.</summary>
    public struct GrabSpring
    {
        public AtomId Atom;
        public Vec3 Target;
    }

    /// <summary>
    /// Силовое поле на основе UFF:
    /// связи — потенциал Морзе (могут рваться), углы — косинусный ряд UFF,
    /// Ван-дер-Ваальс — 12-6 UFF, Кулон — формальные заряды с диэлектриком среды.
    /// Плюс «захват»: атомы со свободной валентностью притягиваются (рекомбинация радикалов без барьера).
    /// Торсионы пока не учитываются.
    /// </summary>
    public static class ForceField
    {
        /// <summary>Считает силы в <c>World.Forces</c> и возвращает потенциальную энергию, ккал/моль.</summary>
        public static double Compute(World w, SimulationSettings s, IReadOnlyList<GrabSpring> grabs = null)
        {
            // Координаты могли поменять снаружи — список соседей строим заново.
            w.TouchPositions();
            return ComputeCached(w, s, grabs);
        }

        /// <summary>То же, но с кэшированным списком соседей (ядро само отмечает изменения координат).</summary>
        internal static double ComputeCached(World w, SimulationSettings s, IReadOnlyList<GrabSpring> grabs = null)
        {
            w.EnsureTopology();
            int n = w.AtomCount;
            Vec3[] pos = w.Positions;
            Vec3[] f = w.Forces;
            for (int i = 0; i < n; i++) f[i] = Vec3.Zero;

            double energy = 0;
            energy += Bonds(w, s, pos, f);
            energy += Angles(w, pos, f);
            energy += Nonbonded(w, s, pos, f, n);
            if (grabs != null) energy += Grabs(w, s, grabs, pos, f);
            if (s.BoxHalfSize > 0) energy += Walls(s, pos, f, n);

            double max = s.MaxForce;
            for (int i = 0; i < n; i++)
            {
                double m = f[i].Length;
                if (m > max) f[i] *= max / m;
            }

            return energy;
        }

        internal static double Smooth(double p)
        {
            p = p < 0 ? 0 : p > 1 ? 1 : p;
            return p * p * (3.0 - 2.0 * p);
        }

        /// <summary>
        /// Вес связи при образовании: сразу 30 % (иначе атомы пролетают друг сквозь друга),
        /// дальше плавно до 100 %.
        /// </summary>
        internal static double BondWeight(double progress) => 0.3 + 0.7 * Smooth(progress);

        private static double Bonds(World w, SimulationSettings s, Vec3[] pos, Vec3[] f)
        {
            double e = 0;
            IReadOnlyList<Bond> bonds = w.Bonds;
            for (int k = 0; k < bonds.Count; k++)
            {
                Bond b = bonds[k];
                int i = b.Ia, j = b.Ib;
                Vec3 d = pos[j] - pos[i];
                double r = d.Length;
                if (r < 1e-9) continue;
                double weight = BondWeight(b.Progress);
                double ex = Math.Exp(-b.Alpha * (r - b.RestLength));
                double om = 1.0 - ex;
                e += weight * b.De * om * om;
                double dEdr = weight * 2.0 * b.De * b.Alpha * ex * om;

                // Пока связь образуется — дополнительная мягкая пружина: потенциал Морзе короткодействующий
                // и не удержал бы атомы, которые реакция связала на расстоянии контакта.
                if (!b.IsFormed && r > b.RestLength)
                {
                    double kc = s.CaptureStrength / s.CaptureRadius * (1.0 - Smooth(b.Progress));
                    double x = r - b.RestLength;
                    e += 0.5 * kc * x * x;
                    dEdr += kc * x;
                }
                Vec3 fi = d * (dEdr / r);
                f[i] += fi;
                f[j] -= fi;
            }
            return e;
        }

        private static double Angles(World w, Vec3[] pos, Vec3[] f)
        {
            double e = 0;
            List<Angle> angles = w.Angles;
            for (int k = 0; k < angles.Count; k++)
            {
                Angle a = angles[k];
                Vec3 u = pos[a.I] - pos[a.J];
                Vec3 v = pos[a.K] - pos[a.J];
                double lu = u.Length, lv = v.Length;
                if (lu < 1e-9 || lv < 1e-9) continue;
                double c = Vec3.Dot(u, v) / (lu * lv);
                if (c > 1) c = 1;
                if (c < -1) c = -1;

                double weight = Smooth(Math.Min(a.B1.Progress, a.B2.Progress));
                double energy, dEdc;
                if (a.Linear)
                {
                    energy = a.Kf * (1.0 + c);
                    dEdc = a.Kf;
                }
                else
                {
                    energy = a.Kf * (a.C0 + a.C1 * c + a.C2 * (2.0 * c * c - 1.0));
                    dEdc = a.Kf * (a.C1 + 4.0 * a.C2 * c);
                }
                e += weight * energy;
                dEdc *= weight;

                Vec3 dci = v / (lu * lv) - u * (c / (lu * lu));
                Vec3 dck = u / (lu * lv) - v * (c / (lv * lv));
                Vec3 fi = dci * -dEdc;
                Vec3 fk = dck * -dEdc;
                f[a.I] += fi;
                f[a.K] += fk;
                f[a.J] -= fi + fk;
            }
            return e;
        }

        /// <summary>Ниже этой доли расстояния Ван-дер-Ваальса отталкивание продолжается линейно.</summary>
        private const double SoftCoreFraction = 0.5;
        /// <summary>Квадрат длины сглаживания Кулона, Å².</summary>
        private const double CoulombSoftening2 = 0.3 * 0.3;

        /// <summary>Радиус списка соседей: покрывает Ван-дер-Ваальс и захват.</summary>
        internal static double PairCutoff(SimulationSettings s) => Math.Max(s.NonbondedCutoff, s.CaptureRadius);

        private static double Nonbonded(World w, SimulationSettings s, Vec3[] pos, Vec3[] f, int n)
        {
            double e = 0;
            double rc = s.CaptureRadius, rc2 = rc * rc;
            double cut = s.NonbondedCutoff, cut2 = cut * cut;
            int[] free = w.FreeValences;
            UffAtomType[] types = w.Types;

            // Ближние пары (захват и Ван-дер-Ваальс) — из списка соседей.
            NeighborList pairs = w.GetPairs(PairCutoff(s));
            for (int p = 0; p < pairs.Count; p++)
            {
                int i = pairs.I[p], j = pairs.J[p];
                Vec3 d = pos[j] - pos[i];
                double r2 = d.LengthSquared;
                if (r2 < 1e-12) continue;

                bool bondable = free[i] > 0 && free[j] > 0 && BondRules.CanBond(w, i, j);
                if (bondable ? r2 >= rc2 : r2 >= cut2) continue;
                if (w.IsExcluded(i, j)) continue;

                double r = Math.Sqrt(r2);
                double dEdr;
                if (bondable)
                {
                    // Захват: оба атома могут образовать связь — Ван-дер-Ваальс не мешает, работает притяжение.
                    double x = rc - r;
                    e += -s.CaptureStrength * x * x / (2.0 * rc);
                    dEdr = s.CaptureStrength * x / rc;
                }
                else
                {
                    double xij = Math.Sqrt(types[i].X1 * types[j].X1);
                    double dij = Math.Sqrt(types[i].D1 * types[j].D1);
                    // «Мягкое ядро»: ниже rs потенциал продолжается линейно (сила постоянна) — консервативно.
                    double rs = SoftCoreFraction * xij;
                    double rEff = r > rs ? r : rs;
                    double t = xij / rEff, t2 = t * t;
                    double s6 = t2 * t2 * t2;
                    double eLj = dij * (s6 * s6 - 2.0 * s6);
                    double dLj = dij * 12.0 * (s6 - s6 * s6) / rEff;
                    // Сдвиг энергии, чтобы на отсечке не было скачка.
                    double tc = xij / cut, tc2 = tc * tc, sc6 = tc2 * tc2 * tc2;
                    e += eLj - dij * (sc6 * sc6 - 2.0 * sc6) + (r < rs ? dLj * (r - rs) : 0.0);
                    dEdr = dLj;
                }

                Vec3 fi = d * (dEdr / r);
                f[i] += fi;
                f[j] -= fi;
            }

            // Кулон — отдельно и только между заряженными атомами (их мало, а отсечка большая).
            e += Coulomb(w, s, pos, f, n);
            return e;
        }

        [ThreadStatic] private static int[] _charged;

        private static double Coulomb(World w, SimulationSettings s, Vec3[] pos, Vec3[] f, int n)
        {
            int q = 0;
            if (_charged == null || _charged.Length < n) _charged = new int[Math.Max(n, 64)];
            for (int i = 0; i < n; i++) if (w.Charges[i] != 0) _charged[q++] = i;
            if (q < 2) return 0;

            double e = 0;
            double coulCut = s.CoulombCutoff, coulCut2 = coulCut * coulCut;
            double coulK = Units.Coulomb / s.Dielectric;
            double rce = Math.Sqrt(coulCut2 + CoulombSoftening2);
            for (int a = 0; a < q; a++)
            {
                int i = _charged[a];
                for (int b = a + 1; b < q; b++)
                {
                    int j = _charged[b];
                    Vec3 d = pos[j] - pos[i];
                    double r2 = d.LengthSquared;
                    if (r2 >= coulCut2 || r2 < 1e-12 || w.IsExcluded(i, j)) continue;
                    // Кулон со сдвигом силы (shifted force) — непрерывен на отсечке;
                    // плюс сглаживание на малых r (re = √(r² + a²)), чтобы ионы не «проваливались» друг в друга.
                    double r = Math.Sqrt(r2);
                    double qq = coulK * w.Charges[i] * w.Charges[j];
                    double re = Math.Sqrt(r2 + CoulombSoftening2);
                    e += qq * (1.0 / re - 1.0 / rce + (re - rce) / (rce * rce));
                    double dEdr = qq * (1.0 / (rce * rce) - 1.0 / (re * re)) * (r / re);
                    Vec3 fi = d * (dEdr / r);
                    f[i] += fi;
                    f[j] -= fi;
                }
            }
            return e;
        }

        /// <summary>Мягкие стенки коробки: гармоническая сила за пределами границы.</summary>
        private static double Walls(SimulationSettings s, Vec3[] pos, Vec3[] f, int n)
        {
            double e = 0, k = s.WallStiffness, l = s.BoxHalfSize;
            Vec3 c = s.BoxCenter;
            for (int i = 0; i < n; i++)
            {
                Vec3 d = pos[i] - c;
                double fx = Wall(d.X, l, k, ref e), fy = Wall(d.Y, l, k, ref e), fz = Wall(d.Z, l, k, ref e);
                f[i] += new Vec3(fx, fy, fz);
            }
            return e;
        }

        private static double Wall(double x, double l, double k, ref double e)
        {
            double over = x > l ? x - l : x < -l ? x + l : 0;
            if (over == 0) return 0;
            e += 0.5 * k * over * over;
            return -k * over;
        }

        private static double Grabs(World w, SimulationSettings s, IReadOnlyList<GrabSpring> grabs, Vec3[] pos, Vec3[] f)
        {
            double e = 0;
            for (int g = 0; g < grabs.Count; g++)
            {
                int i = w.IndexOf(grabs[g].Atom);
                if (i < 0) continue;
                Vec3 d = grabs[g].Target - pos[i];
                Vec3 force = d * s.GrabStiffness;
                double m = force.Length;
                if (m > s.GrabMaxForce) force *= s.GrabMaxForce / m;
                f[i] += force;
                e += 0.5 * s.GrabStiffness * d.LengthSquared;
            }
            return e;
        }
    }
}
