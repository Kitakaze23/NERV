using System;
using System.Collections.Generic;

namespace Nerv.Core.Data
{
    /// <summary>Параметры типа атома UFF (Rappé et al., J. Am. Chem. Soc. 1992, 114, 10024).</summary>
    public sealed class UffAtomType
    {
        public string Label { get; }
        /// <summary>Радиус связи r1, Å.</summary>
        public double R1 { get; }
        /// <summary>Равновесный валентный угол θ0, градусы.</summary>
        public double Theta0Deg { get; }
        /// <summary>Расстояние Ван-дер-Ваальса x1, Å.</summary>
        public double X1 { get; }
        /// <summary>Глубина ямы Ван-дер-Ваальса D1, ккал/моль.</summary>
        public double D1 { get; }
        /// <summary>Эффективный заряд Z1.</summary>
        public double Z1 { get; }
        /// <summary>Электроотрицательность GMP χ.</summary>
        public double Chi { get; }

        public double Theta0Rad => Theta0Deg * Math.PI / 180.0;
        public bool IsLinear => Theta0Deg >= 179.0;

        internal UffAtomType(string label, double r1, double theta0, double x1, double d1, double z1, double chi)
        {
            Label = label;
            R1 = r1;
            Theta0Deg = theta0;
            X1 = x1;
            D1 = d1;
            Z1 = z1;
            Chi = chi;
        }

        public override string ToString() => Label;
    }

    public static class Uff
    {
        private static readonly Dictionary<string, UffAtomType> Types = new Dictionary<string, UffAtomType>(StringComparer.Ordinal);

        static Uff()
        {
            //   label     r1     θ0      x1     D1     Z1     χ
            Add("H_",    0.354, 180.0,  2.886, 0.044, 0.712, 4.528);
            Add("C_3",   0.757, 109.47, 3.851, 0.105, 1.912, 5.343);
            Add("C_R",   0.729, 120.0,  3.851, 0.105, 1.912, 5.343);
            Add("C_2",   0.732, 120.0,  3.851, 0.105, 1.912, 5.343);
            Add("C_1",   0.706, 180.0,  3.851, 0.105, 1.912, 5.343);
            Add("N_3",   0.700, 106.7,  3.660, 0.069, 2.544, 6.899);
            Add("N_R",   0.699, 120.0,  3.660, 0.069, 2.544, 6.899);
            Add("N_2",   0.685, 111.2,  3.660, 0.069, 2.544, 6.899);
            Add("N_1",   0.656, 180.0,  3.660, 0.069, 2.544, 6.899);
            Add("O_3",   0.658, 104.51, 3.500, 0.060, 2.300, 8.741);
            Add("O_2",   0.634, 120.0,  3.500, 0.060, 2.300, 8.741);
            Add("O_1",   0.639, 180.0,  3.500, 0.060, 2.300, 8.741);
            Add("F_",    0.668, 180.0,  3.364, 0.050, 1.735, 10.874);
            Add("Na",    1.539, 180.0,  2.983, 0.030, 1.081, 2.843);
            Add("S_3+2", 1.064, 92.1,   4.035, 0.274, 2.703, 6.928);
            Add("S_2",   0.854, 120.0,  4.035, 0.274, 2.703, 6.928);
            Add("Cl",    1.044, 180.0,  3.947, 0.227, 2.348, 8.564);
        }

        private static void Add(string label, double r1, double theta0, double x1, double d1, double z1, double chi) =>
            Types[label] = new UffAtomType(label, r1, theta0, x1, d1, z1, chi);

        public static UffAtomType Get(string label)
        {
            if (!Types.TryGetValue(label, out UffAtomType t)) throw new ArgumentException("Нет параметров UFF для типа " + label, nameof(label));
            return t;
        }

        /// <summary>Равновесная длина связи UFF: r = ri + rj + rBO − rEN, Å.</summary>
        public static double RestLength(UffAtomType a, UffAtomType b, double order)
        {
            double ri = a.R1, rj = b.R1;
            double rBo = -0.1332 * (ri + rj) * Math.Log(order);
            double d = Math.Sqrt(a.Chi) - Math.Sqrt(b.Chi);
            double rEn = ri * rj * d * d / (a.Chi * ri + b.Chi * rj);
            return ri + rj + rBo - rEn;
        }

        /// <summary>Силовая константа связи UFF, ккал/(моль·Å²).</summary>
        public static double BondForceConstant(UffAtomType a, UffAtomType b, double r0) =>
            664.12 * a.Z1 * b.Z1 / (r0 * r0 * r0);

        /// <summary>Силовая константа угла i–j–k UFF, ккал/(моль·рад²).</summary>
        public static double AngleForceConstant(UffAtomType i, UffAtomType k, double rij, double rjk, double theta0)
        {
            double c = Math.Cos(theta0);
            double rik2 = rij * rij + rjk * rjk - 2.0 * rij * rjk * c;
            double rik = Math.Sqrt(rik2);
            return 664.12 * i.Z1 * k.Z1 / Math.Pow(rik, 5) * (3.0 * rij * rjk * (1.0 - c * c) - rik2 * c);
        }
    }
}
