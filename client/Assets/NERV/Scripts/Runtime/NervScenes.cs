using System;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Client
{
    /// <summary>
    /// Готовые сцены. Строятся прямо в симуляции до запуска фонового потока —
    /// поэтому id созданных атомов сразу известны и связи заготовок можно задать командами.
    /// </summary>
    public static class NervScenes
    {
        public sealed class Preset
        {
            public string Name;
            public string Hint;
            public float BoxHalfSize = 12f;
            public Medium Medium = Medium.Vacuum;
            public Action<Simulation> Build;
        }

        public static readonly Preset[] All =
        {
            new Preset
            {
                Name = "Пустая",
                Hint = "Выбери элемент слева и кликай в сцену.",
                Build = sim => { }
            },
            new Preset
            {
                Name = "Собери воду",
                Hint = "Атомы со свободной валентностью притягиваются сами. Перетащи водород к кислороду.",
                Build = sim =>
                {
                    Atom(sim, "O", 0, 0, 0);
                    Atom(sim, "H", 4, 1, 0);
                    Atom(sim, "H", -4, -1, 1);
                    Atom(sim, "C", 0, 6, -2);
                    for (int k = 0; k < 4; k++) Atom(sim, "H", -6 + 4 * k, -6, 2);
                }
            },
            new Preset
            {
                Name = "Гремучий газ",
                Hint = "Смесь водорода и кислорода 2 : 1. Возьми «Искру» и щёлкни в центр — по смеси побежит пламя.",
                BoxHalfSize = 6f,
                Build = sim =>
                {
                    for (int k = 0; k < 18; k++)
                    {
                        var at = new Vec3((k % 3) * 3.2 - 3.2, ((k / 3) % 3) * 3.2 - 3.2, (k / 9) * 3.2 - 1.6);
                        if (k % 3 == 2) O2(sim, at);
                        else H2(sim, at);
                    }
                }
            },
            new Preset
            {
                Name = "Горение метана",
                Hint = "Метан и кислород 1 : 2. Щёлкни «Искрой» в центр — пламя пройдёт по смеси, останутся CO₂ и вода (и немного угарного газа CO).",
                BoxHalfSize = 6f,
                Build = sim =>
                {
                    for (int k = 0; k < 18; k++)
                    {
                        var at = new Vec3((k % 3) * 3.4 - 3.4, ((k / 3) % 3) * 3.4 - 3.4, (k / 9) * 3.4 - 1.7);
                        if (k % 3 == 1) CH4(sim, at);
                        else O2(sim, at);
                    }
                }
            },
            new Preset
            {
                Name = "Натрий + хлор",
                Hint = "Натрий отдаёт электрон хлору — получается соль.",
                BoxHalfSize = 8f,
                Build = sim =>
                {
                    Atom(sim, "Na", -3, 0, 0);
                    Atom(sim, "Na", 3, 0.5, 0);
                    Atom(sim, "Na", 0, 3, 1);
                    Atom(sim, "Na", 0, -3, -1);
                    Cl2(sim, new Vec3(0, 0, 0));
                    Cl2(sim, new Vec3(0, 0, 3.5));
                }
            },
            new Preset
            {
                Name = "Нейтрализация",
                Hint = "В воде HCl распадается на ионы, H⁺ встречает OH⁻ — получается вода. Переключи среду на вакуум, чтобы «выпарить» соль.",
                BoxHalfSize = 5f,
                Medium = Medium.Water,
                Build = sim =>
                {
                    AtomId h = Atom(sim, "H", 1.5, 0, 0);
                    AtomId cl = Atom(sim, "Cl", 2.8, 0, 0);
                    sim.Apply(new AddBond(h, cl));
                    AtomId o = Atom(sim, "O", -1.5, 0.5, 0, -1);
                    AtomId oh = Atom(sim, "H", -2.2, 1.1, 0);
                    sim.Apply(new AddBond(o, oh));
                    Atom(sim, "Na", -1.5, -2.5, 0, +1);
                }
            },
        };

        // ================= Строительные блоки =================

        public static AtomId Atom(Simulation sim, string symbol, double x, double y, double z, int charge = 0) =>
            sim.Apply(new AddAtom(symbol, new Vec3(x, y, z), charge)).Result;

        public static void H2(Simulation sim, Vec3 at)
        {
            AtomId a = Atom(sim, "H", at.X - 0.37, at.Y, at.Z);
            AtomId b = Atom(sim, "H", at.X + 0.37, at.Y, at.Z);
            sim.Apply(new AddBond(a, b, 1));
        }

        public static void O2(Simulation sim, Vec3 at)
        {
            AtomId a = Atom(sim, "O", at.X - 0.6, at.Y, at.Z);
            AtomId b = Atom(sim, "O", at.X + 0.6, at.Y, at.Z);
            sim.Apply(new AddBond(a, b, 2));
        }

        public static void Cl2(Simulation sim, Vec3 at)
        {
            AtomId a = Atom(sim, "Cl", at.X - 1.0, at.Y, at.Z);
            AtomId b = Atom(sim, "Cl", at.X + 1.0, at.Y, at.Z);
            sim.Apply(new AddBond(a, b, 1));
        }

        public static void CH4(Simulation sim, Vec3 at)
        {
            double d = 1.09 / Math.Sqrt(3);
            AtomId c = Atom(sim, "C", at.X, at.Y, at.Z);
            foreach (var s in new[] { (1, 1, 1), (1, -1, -1), (-1, 1, -1), (-1, -1, 1) })
            {
                AtomId h = Atom(sim, "H", at.X + s.Item1 * d, at.Y + s.Item2 * d, at.Z + s.Item3 * d);
                sim.Apply(new AddBond(c, h, 1));
            }
        }
    }
}
