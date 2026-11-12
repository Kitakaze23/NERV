namespace Nerv.Core
{
    /// <summary>
    /// Единицы ядра: длина — Å, время — фс, масса — а.е.м., энергия — ккал/моль, заряд — e, температура — K.
    /// </summary>
    public static class Units
    {
        /// <summary>Постоянная Больцмана, ккал/(моль·K).</summary>
        public const double Kb = 0.0019872041;

        /// <summary>(ккал/моль/Å) / а.е.м. → Å/фс². Также 1 ккал/моль = AccelFactor а.е.м.·Å²/фс².</summary>
        public const double AccelFactor = 4.184e-4;

        /// <summary>Кулоновская константа, ккал·Å/(моль·e²).</summary>
        public const double Coulomb = 332.0637;
    }
}
