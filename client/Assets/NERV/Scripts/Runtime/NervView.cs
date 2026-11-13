using System;
using Nerv.Core.Data;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Режим отображения молекул (переключатель «Шарики / CPK / Эффект» из концепта).
    /// <list type="bullet">
    /// <item><b>Эффект</b> — основной стиль Mendal: стеклянные атомы с неоновым ободком, точки валентности,
    /// свечение жара, иней, ореолы ионов.</item>
    /// <item><b>Шарики</b> — как модель из школьного кабинета: матовые шарики цвета CPK и стержни-связи,
    /// без свечения; точки свободной валентности и ореолы ионов остаются — они объясняют химию.</item>
    /// <item><b>CPK</b> — заполняющая пространство модель: атомы радиусом Ван-дер-Ваальса, связи не рисуются.
    /// Видно, какую форму и объём на самом деле занимает молекула.</item>
    /// </list>
    /// Цвета элементов одинаковы во всех режимах: меняется только материал и размер.
    /// </summary>
    public static class NervView
    {
        public enum Mode { Balls, Cpk, Effect }

        private const string PrefKey = "nerv.viewMode";
        private static Mode? _current;

        public static Mode Current
        {
            get
            {
                if (_current == null)
                {
                    int saved = PlayerPrefs.GetInt(PrefKey, (int)Mode.Effect);
                    _current = Enum.IsDefined(typeof(Mode), saved) ? (Mode)saved : Mode.Effect;
                }
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                PlayerPrefs.SetInt(PrefKey, (int)value);
                Changed?.Invoke();
            }
        }

        public static event Action Changed;

        public static bool Effects => Current == Mode.Effect;
        public static bool DrawBonds => Current != Mode.Cpk;
        /// <summary>Точки свободной валентности и ореолы ионов (в CPK их закрыли бы сами сферы).</summary>
        public static bool Annotations => Current != Mode.Cpk;

        /// <summary>Радиус шара атома в текущем режиме, Å.</summary>
        public static float Radius(Element e)
        {
            switch (Current)
            {
                case Mode.Cpk: return (float)e.VdwRadius;
                case Mode.Balls: return 0.16f + 0.24f * (float)e.CovalentRadius;
                default: return 0.2f + 0.3f * (float)e.CovalentRadius;
            }
        }

        public static string Name(Mode m) => m == Mode.Balls ? "Шарики" : m == Mode.Cpk ? "CPK" : "Эффект";
    }
}
