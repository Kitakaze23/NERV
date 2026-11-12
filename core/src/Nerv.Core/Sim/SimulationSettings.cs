using Nerv.Core.Model;

namespace Nerv.Core.Sim
{
    /// <summary>Параметры симуляции. «Игровые» коэффициенты подобраны под наглядность, а не под точность.</summary>
    public sealed class SimulationSettings
    {
        // --- Интегратор и термостат ---
        /// <summary>Шаг интегрирования, фс.</summary>
        public double TimeStepFs { get; set; } = 0.5;
        /// <summary>Целевая температура термостата, K.</summary>
        public double TargetTemperature { get; set; } = 298.0;
        public bool ThermostatEnabled { get; set; } = true;
        /// <summary>Время релаксации термостата Берендсена, фс.</summary>
        public double ThermostatTauFs { get; set; } = 100.0;
        /// <summary>
        /// Скорость времени для клиента (сколько тиков делать за кадр). Ядро её только хранит:
        /// число тиков на кадр решает хост.
        /// </summary>
        public double TimeScale { get; set; } = 1.0;

        // --- Среда ---
        public Medium Medium { get; set; } = Medium.Vacuum;
        public double WaterDielectric { get; set; } = 80.0;
        public double Dielectric => Medium == Medium.Water ? WaterDielectric : 1.0;

        // --- Невалентные взаимодействия ---
        /// <summary>Радиус отсечки Ван-дер-Ваальса, Å.</summary>
        public double NonbondedCutoff { get; set; } = 8.0;
        /// <summary>Радиус отсечки Кулона, Å.</summary>
        public double CoulombCutoff { get; set; } = 20.0;

        // --- Правила топологии ---
        /// <summary>Радиус «захвата»: атомы со свободной валентностью притягиваются друг к другу, Å.</summary>
        public double CaptureRadius { get; set; } = 3.5;
        /// <summary>Максимальная сила захвата, ккал/(моль·Å).</summary>
        public double CaptureStrength { get; set; } = 20.0;
        /// <summary>Ковалентная связь образуется при r &lt; множитель × равновесная длина.</summary>
        public double CovalentFormFactor { get; set; } = 1.25;
        /// <summary>Перенос электрона при r &lt; множитель × (r1 + r2) UFF.</summary>
        public double IonicFormFactor { get; set; } = 1.1;
        /// <summary>
        /// Порог |ΔЭО| для ионной связи (металл + неметалл). Классические 1.7 дают ковалентные NaH и Na–C,
        /// хотя в реальности это ионные соединения, поэтому порог снижен до 1.2.
        /// </summary>
        public double IonicElectronegativityThreshold { get; set; } = 1.2;
        /// <summary>За сколько тиков связь «включается» полностью.</summary>
        public int BondFormTicks { get; set; } = 200;
        /// <summary>
        /// Доля относительной скорости пары, сохраняемая за тик, пока связь образуется.
        /// Моделирует «третье тело»: энергия образования связи уходит в среду (это и есть выделение тепла),
        /// иначе молекула из двух атомов сразу разлетается обратно.
        /// </summary>
        public double FormingVelocityRetention { get; set; } = 0.97;
        /// <summary>Глубина ямы Морзе на единицу порядка, ккал/моль.</summary>
        public double BondEnergyPerOrder { get; set; } = 70.0;
        /// <summary>Связь рвётся, когда энергия Морзе превышает эту долю глубины ямы.</summary>
        public double BondBreakEnergyFraction { get; set; } = 0.81;

        // --- Взаимодействие с пользователем ---
        /// <summary>Жёсткость «пружины» захвата рукой/мышью, ккал/(моль·Å²).</summary>
        public double GrabStiffness { get; set; } = 20.0;
        /// <summary>Максимальная сила пружины захвата, ккал/(моль·Å).</summary>
        public double GrabMaxForce { get; set; } = 150.0;
        /// <summary>Доля скорости, сохраняемая схваченным атомом за тик (гасит раскачку).</summary>
        public double GrabVelocityRetention { get; set; } = 0.97;

        /// <summary>Ограничение модуля силы на атом (защита от «взрывов»), ккал/(моль·Å).</summary>
        public double MaxForce { get; set; } = 2000.0;

        public SimulationSettings Clone() => (SimulationSettings)MemberwiseClone();
    }
}
