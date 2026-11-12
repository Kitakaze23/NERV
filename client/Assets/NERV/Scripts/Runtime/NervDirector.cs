using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nerv.Core.Naming;
using Nerv.Core.Sim;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Режиссура реакций (концепт, раздел 7.3): когда в сцене впервые срабатывает шаг реакции —
    /// замедляем время и показываем карточку шага (уравнение, ΔH, пояснение). Повторы того же шага
    /// идут без замедления, только в журнал: в пламени их десятки.
    /// </summary>
    public sealed class NervDirector : MonoBehaviour
    {
        public sealed class Card
        {
            public string Title, Equation, Energy, Text, Summary;
            public bool Simplified;
            public Color Accent;
            public float Shown;
        }

        public static NervDirector Instance { get; private set; }

        /// <summary>Замедлять время на новых шагах реакций.</summary>
        public bool SlowMotion = true;

        /// <summary>Текущая карточка (null — нет).</summary>
        public Card Current { get; private set; }
        public float CardAlpha { get; private set; }

        private const float CardLife = 6f;
        private const float SlowFactor = 0.12f, SlowHold = 1.4f, SlowRelease = 0.8f;

        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Queue<Card> _queue = new Queue<Card>();
        private string _scene;
        private float _slowSince = -100f;

        private void Awake() => Instance = this;

        private void Update()
        {
            NervApp app = NervApp.Instance;
            if (app == null || app.Host == null) return;

            if (_scene != app.SceneName)
            {
                // Новая сцена — всё снова «впервые».
                _scene = app.SceneName;
                _seen.Clear();
                _queue.Clear();
                Current = null;
                _slowSince = -100f;
            }

            foreach (ReactionRecord r in app.Reactions)
            {
                if (!_seen.Add(r.Reaction.Id)) continue;
                if (_queue.Count < 3) _queue.Enqueue(MakeCard(r));
                if (SlowMotion) _slowSince = Time.unscaledTime;
            }

            // Карточки по очереди; следующая сменяет текущую раньше срока, если ждёт.
            float now = Time.unscaledTime;
            if (Current != null)
            {
                float age = now - Current.Shown;
                float life = _queue.Count > 0 ? CardLife * 0.5f : CardLife;
                if (age > life) Current = null;
            }
            if (Current == null && _queue.Count > 0)
            {
                Current = _queue.Dequeue();
                Current.Shown = now;
            }
            if (Current != null)
            {
                float age = now - Current.Shown;
                float life = _queue.Count > 0 ? CardLife * 0.5f : CardLife;
                CardAlpha = Mathf.Clamp01(age / 0.25f) * Mathf.Clamp01((life - age) / 0.6f);
            }

            // Замедление: резко вниз, держим, плавно отпускаем.
            float t = now - _slowSince;
            float factor = 1f;
            if (t < SlowHold) factor = SlowFactor;
            else if (t < SlowHold + SlowRelease) factor = Mathf.Lerp(SlowFactor, 1f, Mathf.SmoothStep(0, 1, (t - SlowHold) / SlowRelease));
            app.Host.PlaybackFactor = factor;
        }

        public bool IsSlowed => NervApp.Instance != null && NervApp.Instance.Host != null && NervApp.Instance.Host.PlaybackFactor < 0.99;

        private static Card MakeCard(ReactionRecord r)
        {
            double dh = r.Reaction.DeltaHKj;
            string energy = dh < 0
                ? "−" + (-dh).ToString("0") + " кДж/моль · тепло выделяется"
                : dh > 0 ? "+" + dh.ToString("0") + " кДж/моль · тепло поглощается" : "без теплового эффекта";
            return new Card
            {
                Title = StepTitle(r.Reaction.Name),
                Equation = Equation(r),
                Energy = energy,
                Text = r.Reaction.TextRu,
                Summary = r.Reaction.Summary,
                Simplified = r.Reaction.Mechanism == "chain" || r.Reaction.Mechanism == "simplified",
                Accent = dh < 0 ? NervEnvironment.Heat : dh > 0 ? NervEnvironment.Cold : NervEnvironment.Energy
            };
        }

        /// <summary>«Горение водорода — H + O₂» → «Горение водорода».</summary>
        private static string StepTitle(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Реакция";
            int dash = name.IndexOf(" — ", System.StringComparison.Ordinal);
            return dash > 0 ? name.Substring(0, dash) : name;
        }

        /// <summary>Уравнение шага по настоящим частицам сцены: «H + O₂ → OH + O», одинаковые — «2 OH».</summary>
        public static string Equation(ReactionRecord r) => Side(r.Reactants) + "  →  " + Side(r.Products);

        private static string Side(IReadOnlyList<(string Formula, int Charge)> parts)
        {
            var sb = new StringBuilder();
            foreach (IGrouping<(string, int), (string Formula, int Charge)> g in parts.GroupBy(p => (p.Formula, p.Charge)))
            {
                if (sb.Length > 0) sb.Append(" + ");
                int n = g.Count();
                if (n > 1) sb.Append(n);
                sb.Append(Pretty(g.Key.Item1, g.Key.Item2));
            }
            return sb.ToString();
        }

        // Формулы Хилла привычнее читать в «химической» записи радикалов и кислот.
        private static readonly Dictionary<string, string> Display = new Dictionary<string, string>
        {
            { "HO", "OH" }, { "CHO", "HCO" }, { "CHO2", "HOCO" }, { "CH2O2", "HCOOH" },
            { "CH4O", "CH3OH" }, { "CH3O2", "CH3OO" }, { "ClH", "HCl" }, { "FH", "HF" },
            { "ClNa", "NaCl" }, { "HNaO", "NaOH" }, { "H3N", "NH3" }
        };

        public static string Pretty(string formula, int charge)
        {
            if (formula == null) return "?";
            if (Display.TryGetValue(formula, out string d)) formula = d;
            return FormulaText.Subscript(formula) + FormulaText.ChargeSuperscript(charge);
        }
    }
}
