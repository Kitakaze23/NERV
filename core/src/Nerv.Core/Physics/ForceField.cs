using System;
using System.Collections.Generic;
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
            w.EnsureTopology();
            int n = w.AtomCount;
            Vec3[] pos = w.Positions;
            Vec3[] f = w.Forces;
            for (int i = 0; i < n; i++) f[i] = Vec3.Zero;

            double energy = 0;
            energy += Bonds(w, pos, f);
            energy += Angles(w, pos, f);
            energy += Nonbonded(w, s, pos, f, n);
            if (grabs != null) energy += Grabs(w, s, grabs, pos, f);

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

        private static double Bonds(World w, Vec3[] pos, Vec3[] f)
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

        private static double Nonbonded(World w, SimulationSettings s, Vec3[] pos, Vec3[] f, int n)
        {
            double e = 0;
            double rc = s.CaptureRadius, rc2 = rc * rc;
            double cut2 = s.NonbondedCutoff * s.NonbondedCutoff;
            double coulCut = s.CoulombCutoff, coulCut2 = coulCut * coulCut;
            double coulK = Units.Coulomb / s.Dielectric;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    Vec3 d = pos[j] - pos[i];
                    double r2 = d.LengthSquared;
                    if (r2 < 1e-12) continue;

                    int qi = w.Charges[i], qj = w.Charges[j];
                    bool charged = qi != 0 && qj != 0;
                    bool bondable = w.FreeValences[i] > 0 && w.FreeValences[j] > 0;
                    bool capture = bondable && r2 < rc2;
                    bool lj = !bondable && r2 < cut2;
                    bool coulomb = charged && r2 < coulCut2;
                    // Дешёвая проверка расстояния — до поиска в таблице исключений.
                    if (!(capture || lj || coulomb) || w.IsExcluded(i, j)) continue;

                    double r = Math.Sqrt(r2);
                    double dEdr = 0;

                    // Захват: оба атома могут образовать связь — Ван-дер-Ваальс не мешает, работает притяжение.
                    if (capture)
                    {
                        double x = rc - r;
                        e += -s.CaptureStrength * x * x / (2.0 * rc);
                        dEdr += s.CaptureStrength * x / rc;
                    }

                    if (lj)
                    {
                        double xij = Math.Sqrt(w.Types[i].X1 * w.Types[j].X1);
                        double dij = Math.Sqrt(w.Types[i].D1 * w.Types[j].D1);
                        // «Мягкое ядро»: ниже rs потенциал продолжается линейно (сила постоянна) — консервативно.
                        double rs = SoftCoreFraction * xij;
                        double rEff = r > rs ? r : rs;
                        double t = xij / rEff, t2 = t * t;
                        double s6 = t2 * t2 * t2;
                        double eLj = dij * (s6 * s6 - 2.0 * s6);
                        double dLj = dij * 12.0 * (s6 - s6 * s6) / rEff;
                        // Сдвиг энергии, чтобы на отсечке не было скачка.
                        double tc = xij / s.NonbondedCutoff, tc2 = tc * tc, sc6 = tc2 * tc2 * tc2;
                        e += eLj - dij * (sc6 * sc6 - 2.0 * sc6) + (r < rs ? dLj * (r - rs) : 0.0);
                        dEdr += dLj;
                    }

                    if (coulomb)
                    {
                        // Кулон со сдвигом силы (shifted force) — непрерывен на отсечке;
                        // плюс сглаживание на малых r (re = √(r² + a²)), чтобы ионы не «проваливались» друг в друга.
                        double qq = coulK * qi * qj;
                        double re = Math.Sqrt(r2 + CoulombSoftening2);
                        double rce = Math.Sqrt(coulCut2 + CoulombSoftening2);
                        e += qq * (1.0 / re - 1.0 / rce + (re - rce) / (rce * rce));
                        dEdr += qq * (1.0 / (rce * rce) - 1.0 / (re * re)) * (r / re);
                    }

                    if (dEdr != 0)
                    {
                        Vec3 fi = d * (dEdr / r);
                        f[i] += fi;
                        f[j] -= fi;
                    }
                }
            }
            return e;
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
