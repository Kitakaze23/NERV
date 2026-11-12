using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Nerv.Core.Sim
{
    /// <summary>
    /// Хост симуляции для клиента: симуляция идёт в своём потоке, рендер берёт готовые снимки.
    ///
    /// Потокобезопасный интерфейс:
    /// <list type="bullet">
    /// <item><see cref="Submit"/> — команда из ввода (выполнится в начале ближайшего шага);</item>
    /// <item><see cref="CopySnapshot"/> — последний опубликованный снимок мира;</item>
    /// <item><see cref="DrainEvents"/>, <see cref="DrainReactions"/> — накопленные события и записи журнала реакций.</item>
    /// </list>
    /// Два режима: фоновый поток (<see cref="Start"/>) или ручной (<see cref="Advance"/> из кадра —
    /// для платформ без потоков, например WebGL). Детерминизм сохраняется: каждая команда пишется в журнал
    /// с номером тика, на котором выполнена, и повтор журнала даёт тот же результат.
    /// </summary>
    public sealed class SimulationHost : IDisposable
    {
        /// <summary>Не трогать снаружи, пока работает фоновый поток.</summary>
        public Simulation Simulation { get; }

        /// <summary>Тиков симуляции в секунду реального времени при TimeScale = 1 (3000 тиков ≈ 1,5 пс в секунду).</summary>
        public double BaseTicksPerSecond { get; set; } = 3000;

        /// <summary>Ограничение тиков за один вызов Advance (защита от «спирали смерти» после подвисания).</summary>
        public int MaxTicksPerAdvance { get; set; } = 300;

        /// <summary>Пауза: команды выполняются, время не идёт.</summary>
        public bool Paused
        {
            get => _paused;
            set => _paused = value;
        }

        public bool IsRunning => _thread != null;

        private readonly object _lock = new object();
        private readonly List<Command> _inbox = new List<Command>();
        private readonly List<Command> _inboxSwap = new List<Command>();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly List<ReactionRecord> _reactions = new List<ReactionRecord>();
        private WorldSnapshot _published = new WorldSnapshot();
        private WorldSnapshot _staging = new WorldSnapshot();
        private int _publishedReactions;
        private double _tickDebt;
        private volatile bool _paused;
        private volatile bool _running;
        private Thread _thread;
        private Exception _failure;

        public SimulationHost(Simulation simulation)
        {
            Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            Publish();
        }

        // ================= Интерфейс для клиента (потокобезопасный) =================

        public void Submit(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            lock (_lock) _inbox.Add(command);
        }

        /// <summary>Скопировать последний снимок в dst (массивы dst переиспользуются).</summary>
        public void CopySnapshot(WorldSnapshot dst)
        {
            ThrowIfFailed();
            lock (_lock) _published.CopyTo(dst);
        }

        /// <summary>Забрать накопленные события. Возвращает их число.</summary>
        public int DrainEvents(List<SimEvent> into)
        {
            lock (_lock)
            {
                int n = _events.Count;
                into.AddRange(_events);
                _events.Clear();
                return n;
            }
        }

        /// <summary>Забрать новые записи журнала реакций (для анимации следов атомов).</summary>
        public int DrainReactions(List<ReactionRecord> into)
        {
            lock (_lock)
            {
                int n = _reactions.Count;
                into.AddRange(_reactions);
                _reactions.Clear();
                return n;
            }
        }

        // ================= Ручной режим =================

        /// <summary>
        /// Продвинуть симуляцию на realSeconds реального времени в текущем потоке.
        /// Возвращает число выполненных тиков. Нельзя вызывать при запущенном фоновом потоке.
        /// </summary>
        public int Advance(double realSeconds)
        {
            if (_thread != null && Thread.CurrentThread != _thread)
                throw new InvalidOperationException("Хост работает в фоновом потоке — Advance вызывать нельзя.");
            return AdvanceCore(realSeconds);
        }

        private int AdvanceCore(double realSeconds)
        {
            lock (_lock)
            {
                _inboxSwap.AddRange(_inbox);
                _inbox.Clear();
            }
            foreach (Command c in _inboxSwap) Simulation.Apply(c);
            _inboxSwap.Clear();

            int ticks = 0;
            if (!_paused)
            {
                _tickDebt += Math.Max(0, realSeconds) * BaseTicksPerSecond * Simulation.Settings.TimeScale;
                ticks = (int)Math.Floor(_tickDebt);
                _tickDebt -= ticks;
                if (ticks > MaxTicksPerAdvance)
                {
                    ticks = MaxTicksPerAdvance;
                    _tickDebt = 0;
                }
                Simulation.Step(ticks);
            }
            Publish();
            return ticks;
        }

        // ================= Фоновый поток =================

        public void Start()
        {
            if (_thread != null) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "NERV simulation" };
            _thread.Start();
        }

        public void Stop()
        {
            Thread t = _thread;
            if (t == null) return;
            _running = false;
            t.Join();
            _thread = null;
            ThrowIfFailed();
        }

        public void Dispose() => Stop();

        private void Loop()
        {
            var clock = Stopwatch.StartNew();
            double last = 0;
            try
            {
                while (_running)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    AdvanceCore(now - last);
                    last = now;
                    Thread.Sleep(1);
                }
            }
            catch (Exception e)
            {
                lock (_lock) _failure = e;
                _running = false;
            }
        }

        private void ThrowIfFailed()
        {
            Exception e;
            lock (_lock) e = _failure;
            if (e != null) throw new InvalidOperationException("Поток симуляции упал: " + e.Message, e);
        }

        // ================= Публикация =================

        private void Publish()
        {
            Simulation.WriteSnapshot(_staging);
            IReadOnlyList<SimEvent> events = Simulation.Events;
            IReadOnlyList<ReactionRecord> log = Simulation.ReactionLog;
            lock (_lock)
            {
                WorldSnapshot t = _published;
                _published = _staging;
                _staging = t;
                for (int i = 0; i < events.Count; i++) _events.Add(events[i]);
                for (int i = _publishedReactions; i < log.Count; i++) _reactions.Add(log[i]);
                _publishedReactions = log.Count;
            }
            Simulation.ClearEvents();
        }
    }
}
