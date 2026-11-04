namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-04] CONFIGURATION BOUNDS PRIMITIVES
    /// Single canonical clamping logic shared by <see cref="SolverConfiguration.Sanitized"/>, <see cref="RuntimeConfiguration.Sanitized"/>
    /// and any layer that must range-check a tunable against the <see cref="CoreDefaults"/> Min/Max pairs. Resides under Source/Core/Configs/.
    /// Values stray in a direction, so they are clamped to the bound they exceeded (infinities included);
    /// only NaN, which has no direction, falls back to the supplied default.
    /// </summary>
    internal static class ConfigBounds
    {
        #region 1. CANONICAL CLAMPS

        /// <summary>
        /// Clamps a float into a range. Positive infinity resolves to <paramref name="max"/>, negative infinity to <paramref name="min"/>,
        /// and NaN to <paramref name="nanFallback"/> (clamped into range).
        /// </summary>
        /// <param name="value">The value to range-check.</param>
        /// <param name="nanFallback">The default used when the value is NaN.</param>
        /// <param name="min">Lower bound.</param>
        /// <param name="max">Upper bound.</param>
        /// <param name="changed">Set to true when the returned value differs from the input; never reset to false.</param>
        /// <returns>A finite value within the range.</returns>
        public static float ClampFloat(float value, float nanFallback, float min, float max, ref bool changed)
        {
            float result = value;
            if (float.IsNaN(value)) result = nanFallback;
            if (result < min) result = min;
            else if (result > max) result = max;

            // NaN != NaN, so a NaN input always registers as changed.
            if (result != value) changed = true;
            return result;
        }

        /// <summary>
        /// Clamps an integer into a range.
        /// </summary>
        /// <param name="value">The value to range-check.</param>
        /// <param name="min">Lower bound.</param>
        /// <param name="max">Upper bound.</param>
        /// <param name="changed">Set to true when the returned value differs from the input; never reset to false.</param>
        /// <returns>A value within the range.</returns>
        public static int ClampInt(int value, int min, int max, ref bool changed)
        {
            int result = value < min ? min : (value > max ? max : value);
            if (result != value) changed = true;
            return result;
        }

        #endregion

        #region 2. THIN PASS-THROUGHS

        /// <summary>Clamps a 0 to 1 percentage-style value against <see cref="CoreDefaults.PercentageMin"/> and <see cref="CoreDefaults.PercentageMax"/>.</summary>
        public static float ClampPercent(float value, float nanFallback, ref bool changed)
        {
            return ClampFloat(value, nanFallback, CoreDefaults.PercentageMin, CoreDefaults.PercentageMax, ref changed);
        }

        /// <summary>Clamps a float into a range without change tracking, for callers that do not report modifications.</summary>
        public static float ClampFloatUntracked(float value, float nanFallback, float min, float max)
        {
            bool unused = false;
            return ClampFloat(value, nanFallback, min, max, ref unused);
        }

        #endregion
    }
}
