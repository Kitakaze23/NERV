using System;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Уровни качества (архитектура, 7.6). «Высокое» — ПК: полноэкранное свечение, мягкие тени,
    /// точечный свет во вспышках, много следов. «Экономное» — шлем, браузер, слабые ноутбуки:
    /// без Bloom и теней (ореолы атомов и так яркие — они аддитивные), без динамического света, меньше следов.
    /// </summary>
    public static class NervQuality
    {
        public enum Level { High, Low }

        private static Level? _current;

        public static Level Current
        {
            get
            {
                if (_current == null)
                    _current = Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer
                        ? Level.Low : Level.High;
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                Changed?.Invoke();
            }
        }

        public static bool High => Current == Level.High;

        /// <summary>Сколько следов атомов держать одновременно.</summary>
        public static int MaxTrails => High ? 64 : 20;

        public static event Action Changed;

        public static string Label => High ? "Качество: высокое" : "Качество: экономное";
    }
}
