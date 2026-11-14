using System;
using System.Linq;
using Nerv.Core.Model;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

namespace Nerv.Core.Tests
{
    /// <summary>Сохранение и загрузка сцен (`.mendal`).</summary>
    public static class SceneFileTests
    {
        private static string Composition(Simulation sim) =>
            string.Join(" ", sim.World.Molecules.Select(m => m.Formula + (m.Charge != 0 ? m.Charge.ToString("+0;-0") : "")).OrderBy(f => f, StringComparer.Ordinal));

        [Test]
        public static void SceneFile_RoundTripRestoresWorld()
        {
            Simulation a = TestUtil.NewSim(350, 4, withData: true);
            a.Apply(new BuildMolecule("C", new Vec3(-3, 0, 0)));
            a.Apply(new BuildMolecule("O", new Vec3(3, 0, 0)));
            a.Apply(new BuildMolecule("[Na+].[Cl-]", new Vec3(0, 4, 0)));
            a.Apply(new BuildMolecule("[O][O]", new Vec3(0, -4, 0))); // недособранная связь — прогресс и свободные валентности
            a.Step(300);
            WorldState state = a.Apply(new Checkpoint()).State;
            string text = SceneFile.Write(new SceneData { Name = "Опыт\nс переводом строки", Temperature = 350, Medium = Medium.Water, BoxHalfSize = 6, State = state });

            SceneData d = SceneFile.Read(text);
            Assert.Equal("Опыт с переводом строки", d.Name, "имя в одну строку");
            Assert.Equal(Medium.Water, d.Medium, "среда");
            Assert.True(Math.Abs(d.Temperature - 350) < 1e-9 && Math.Abs(d.BoxHalfSize - 6) < 1e-9, "температура и коробка");

            Simulation b = TestUtil.NewSim(10, 9, withData: true);
            b.Apply(new RestoreState(d.State));
            Assert.Equal(Composition(a), Composition(b), "состав после загрузки");
            for (int i = 0; i < a.World.AtomCount; i++)
            {
                Assert.True(Vec3.Distance(a.World.GetPosition(a.World.IdAt(i)), b.World.GetPosition(b.World.IdAt(i))) < 1e-12, "координаты атома " + i);
                Assert.True(Vec3.Distance(a.World.GetVelocity(a.World.IdAt(i)), b.World.GetVelocity(b.World.IdAt(i))) < 1e-12, "скорость атома " + i);
            }
            // Записанное повторно совпадает побайтно — формат устойчив.
            Assert.Equal(text.Replace("Опыт\nс", "Опыт с"), SceneFile.Write(d), "повторная запись");
        }

        [Test]
        public static void SceneFile_RejectsBrokenFiles()
        {
            foreach (string bad in new[]
            {
                "привет",
                SceneFile.Header + "\natom 1 0 0 0 0",
                SceneFile.Header + "\natom 999 0 0 0 0 0 0 0",
                SceneFile.Header + "\natom 1 0 0 0 0 0 0 0\nbond 0 5 1 1 0",
                SceneFile.Header + "\natom 1 0 0 0 0 0 0 0\natom 1 0 1 0 0 0 0 0\nbond 0 1 4 1 0",
                SceneFile.Header + "\natom 1 0 x 0 0 0 0 0",
            })
            {
                bool threw = false;
                try { SceneFile.Read(bad); }
                catch (FormatException) { threw = true; }
                Assert.True(threw, "битый файл не принят: " + bad.Replace("\n", " | "));
            }
            // Пустая сцена и незнакомые ключи — нормально.
            SceneData d = SceneFile.Read(SceneFile.Header + "\nfuture=1\n");
            Assert.Equal(0, d.State.AtomCount, "пустая сцена");
        }
    }
}
