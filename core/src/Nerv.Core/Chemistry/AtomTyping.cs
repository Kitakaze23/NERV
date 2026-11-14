using System;
using Nerv.Core.Data;

namespace Nerv.Core.Chemistry
{
    /// <summary>
    /// Определение типа атома UFF по топологии. Стерическое число = соседи + неподелённые пары + неспаренные электроны;
    /// из него — гибридизация и равновесный угол (поэтому углы VSEPR получаются сами).
    /// </summary>
    public static class AtomTyping
    {
        public static string Assign(Element e, int degree, int lonePairs, int unpaired)
        {
            int sn = degree + lonePairs + unpaired;
            switch (e.Symbol)
            {
                case "H": return "H_";
                case "F": return "F_";
                case "Cl": return "Cl";
                case "Na": return "Na";
                case "Li": return "Li";
                case "K": return "K_";
                case "Mg": return "Mg3+2";
                case "Ca": return "Ca6+2";
                case "Br": return "Br";
                case "I": return "I_";
                case "P": return "P_3+3";
                case "C": return sn >= 4 ? "C_3" : sn == 3 ? "C_2" : "C_1";
                case "N": return sn >= 4 ? "N_3" : sn == 3 ? "N_2" : "N_1";
                case "O": return sn >= 4 ? "O_3" : sn == 3 ? "O_2" : "O_1";
                case "S": return sn >= 4 ? "S_3+2" : "S_2";
                default:
                    throw new NotSupportedException("Элемент " + e.Symbol + " пока не поддерживается силовым полем.");
            }
        }

        public static bool IsSupported(Element e)
        {
            switch (e.Symbol)
            {
                case "H": case "C": case "N": case "O": case "F": case "Na": case "S": case "Cl":
                case "Li": case "K": case "Mg": case "Ca": case "Br": case "I": case "P":
                    return true;
                default:
                    return false;
            }
        }
    }
}
