using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;
using static Nerv.Core.Tests.TestUtil;

namespace Nerv.Core.Tests
{
    public static class HostTests
    {
        [Test]
        public static void Host_ManualAdvanceRunsRealTimeTicks()
        {
            var host = new SimulationHost(NewSim(withData: true)) { BaseTicksPerSecond = 3000 };
            host.Submit(new AddAtom("O", new Vec3(0, 0, 0)));
            host.Submit(new AddAtom("H", new Vec3(2.5, 0, 0)));
            host.Submit(new AddAtom("H", new Vec3(-1.25, 2.165, 0)));
            int ticks = 0;
            for (int frame = 0; frame < 120; frame++) ticks += host.Advance(1.0 / 60.0); // 2 секунды
            Assert.Equal(6000, ticks, "тиков за 2 с при 3000 тиков/с");

            var snap = new WorldSnapshot();
            host.CopySnapshot(snap);
            Assert.Equal(6000L, snap.Tick);
            Assert.Equal("Вода", snap.Molecules.Single().Name);

            var events = new List<SimEvent>();
            host.DrainEvents(events);
            Assert.True(events.Any(e => e.Type == SimEventType.MoleculeIdentified && e.Text == "Вода"), "событие дошло до клиента");
            Assert.Equal(0, host.DrainEvents(events), "события забираются один раз");
        }

        [Test]
        public static void Host_PauseStopsTimeButAppliesCommands()
        {
            var host = new SimulationHost(NewSim()) { Paused = true };
            host.Submit(new AddAtom("C", Vec3.Zero));
            Assert.Equal(0, host.Advance(1.0), "на паузе время стоит");
            var snap = new WorldSnapshot();
            host.CopySnapshot(snap);
            Assert.Equal(1, snap.AtomCount, "команда выполнена");
            host.Paused = false;
            host.Simulation.Settings.TimeScale = 0.5;
            Assert.Equal(150, host.Advance(0.1), "скорость времени ×0,5");
        }

        [Test]
        public static void Host_BackgroundThreadRuns()
        {
            using (var host = new SimulationHost(NewSim(withData: true)))
            {
                host.Start();
                host.Submit(new AddAtom("Na", new Vec3(0, 0, 0)));
                host.Submit(new AddAtom("Cl", new Vec3(3.4, 0, 0)));

                var snap = new WorldSnapshot();
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 5000)
                {
                    host.CopySnapshot(snap); // читаем, пока поток пишет
                    if (snap.IonicGroups.Count > 0) break;
                    Thread.Sleep(5);
                }
                host.Stop();
                Assert.True(snap.Tick > 0, "время шло");
                Assert.Equal("Хлорид натрия", snap.IonicGroups.Single().Name);

                var events = new List<SimEvent>();
                host.DrainEvents(events);
                Assert.True(events.Any(e => e.Type == SimEventType.ElectronTransferred || e.Type == SimEventType.ReactionFired), "события из фонового потока");
            }
        }
    }
}
