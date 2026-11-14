using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Nerv.Core.Data;
using Nerv.Core.Model;
using Nerv.Core.Numerics;

namespace Nerv.Core.Sim
{
    /// <summary>Сохранённая сцена: слепок мира плюс настройки среды.</summary>
    public sealed class SceneData
    {
        public string Name { get; set; } = "";
        public double Temperature { get; set; } = 298;
        public Medium Medium { get; set; } = Medium.Vacuum;
        public double BoxHalfSize { get; set; }
        public WorldState State { get; set; }
    }

    /// <summary>
    /// Текстовый формат сцены Mendal (`.mendal`): построчно, числа в инвариантной культуре.
    /// <code>
    /// MENDAL-SCENE 1
    /// name=Моя сцена
    /// temperature=298
    /// medium=vacuum
    /// box=6
    /// flame=0
    /// atom Z заряд x y z vx vy vz
    /// bond a b порядок прогресс изРеакции
    /// </code>
    /// Атомы нумеруются по порядку строк, связи ссылаются на эти номера. Неизвестные ключи пропускаются —
    /// так новые версии смогут дописывать поля, не ломая старые файлы.
    /// </summary>
    public static class SceneFile
    {
        public const string Header = "MENDAL-SCENE 1";
        public const string Extension = ".mendal";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Write(SceneData d)
        {
            if (d?.State == null) throw new ArgumentException("Нет слепка мира для сохранения.", nameof(d));
            WorldState s = d.State;
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            sb.Append("name=").Append(OneLine(d.Name)).Append('\n');
            sb.Append("temperature=").Append(d.Temperature.ToString("R", Inv)).Append('\n');
            sb.Append("medium=").Append(d.Medium == Medium.Water ? "water" : "vacuum").Append('\n');
            sb.Append("box=").Append(d.BoxHalfSize.ToString("R", Inv)).Append('\n');
            sb.Append("flame=").Append(s.FlameTicksLeft.ToString(Inv)).Append('\n');
            for (int i = 0; i < s.AtomCount; i++)
            {
                Vec3 p = s.Positions[i], v = s.Velocities[i];
                sb.Append("atom ").Append(s.Z[i].ToString(Inv)).Append(' ').Append(s.Charges[i].ToString(Inv));
                foreach (double x in new[] { p.X, p.Y, p.Z, v.X, v.Y, v.Z }) sb.Append(' ').Append(x.ToString("R", Inv));
                sb.Append('\n');
            }
            foreach (var b in s.Bonds)
                sb.Append("bond ").Append(b.A.ToString(Inv)).Append(' ').Append(b.B.ToString(Inv)).Append(' ')
                  .Append(b.Order.ToString(Inv)).Append(' ').Append(b.Progress.ToString("R", Inv)).Append(' ')
                  .Append(b.FromReaction ? '1' : '0').Append('\n');
            return sb.ToString();
        }

        public static SceneData Read(string text)
        {
            if (text == null) throw new FormatException("Файл сцены пуст.");
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header)
                throw new FormatException("Это не файл сцены Mendal (нет заголовка «" + Header + "»).");

            var d = new SceneData();
            var z = new List<int>();
            var q = new List<int>();
            var pos = new List<Vec3>();
            var vel = new List<Vec3>();
            var bonds = new List<(int, int, int, double, bool)>();
            long flame = 0;

            for (int n = 1; n < lines.Length; n++)
            {
                string line = lines[n].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string where = "строка " + (n + 1) + ": ";
                if (line.StartsWith("atom ", StringComparison.Ordinal))
                {
                    string[] f = line.Split(' ');
                    if (f.Length != 9) throw new FormatException(where + "у атома должно быть 8 чисел.");
                    int zi = Int(f[1], where);
                    if (zi < 1 || zi > ElementTable.Count) throw new FormatException(where + "нет элемента с номером " + zi + ".");
                    z.Add(zi);
                    q.Add(Int(f[2], where));
                    pos.Add(new Vec3(Num(f[3], where), Num(f[4], where), Num(f[5], where)));
                    vel.Add(new Vec3(Num(f[6], where), Num(f[7], where), Num(f[8], where)));
                }
                else if (line.StartsWith("bond ", StringComparison.Ordinal))
                {
                    string[] f = line.Split(' ');
                    if (f.Length != 6) throw new FormatException(where + "у связи должно быть 5 полей.");
                    int a = Int(f[1], where), b = Int(f[2], where), order = Int(f[3], where);
                    if (a < 0 || b < 0 || a == b) throw new FormatException(where + "связь ссылается на несуществующий атом.");
                    if (order < 1 || order > 3) throw new FormatException(where + "порядок связи должен быть от 1 до 3.");
                    double progress = Math.Max(0, Math.Min(1, Num(f[4], where)));
                    bonds.Add((a, b, order, progress, f[5] == "1"));
                }
                else
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "name": d.Name = val; break;
                        case "temperature": d.Temperature = Math.Max(1, Num(val, where)); break;
                        case "medium": d.Medium = val == "water" ? Medium.Water : Medium.Vacuum; break;
                        case "box": d.BoxHalfSize = Math.Max(0, Num(val, where)); break;
                        case "flame": flame = Math.Max(0, (long)Num(val, where)); break;
                    }
                }
            }
            foreach (var b in bonds)
                if (b.Item1 >= z.Count || b.Item2 >= z.Count)
                    throw new FormatException("Связь ссылается на атом " + Math.Max(b.Item1, b.Item2) + ", а атомов всего " + z.Count + ".");

            d.State = new WorldState
            {
                Z = z.ToArray(),
                Charges = q.ToArray(),
                Positions = pos.ToArray(),
                Velocities = vel.ToArray(),
                Bonds = bonds.ToArray(),
                FlameTicksLeft = flame,
            };
            return d;
        }

        private static string OneLine(string s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();

        private static int Int(string s, string where)
        {
            if (!int.TryParse(s, NumberStyles.Integer, Inv, out int v)) throw new FormatException(where + "«" + s + "» — не целое число.");
            return v;
        }

        private static double Num(string s, string where)
        {
            if (!double.TryParse(s, NumberStyles.Float, Inv, out double v) || double.IsNaN(v) || double.IsInfinity(v))
                throw new FormatException(where + "«" + s + "» — не число.");
            return v;
        }
    }
}
