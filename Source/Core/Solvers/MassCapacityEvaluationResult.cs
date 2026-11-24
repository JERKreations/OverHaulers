namespace OverHaulers
{
    #region 1. MASS CAPACITY EVALUATION RESULT

    /// <summary>
    /// [SOLV-06] Readonly value-type evaluation result containing all solved Caravan Mass Capacity metrics.
    /// Encapsulates solver execution completely within Core, eliminating leaky workspace lifetimes.
    /// Resides under Source/Core/Solvers/.
    /// </summary>
    public readonly struct MassCapacityEvaluationResult
    {
        #region 1A. Constants & Fallbacks

        /// <summary>A fallback instance of <see cref="MassCapacityEvaluationResult"/> with default values.</summary>
        public static readonly MassCapacityEvaluationResult Fallback =
            new MassCapacityEvaluationResult(0f, 0f, 0.01f, 1f, 0f, 0f, 0f);

        #endregion

        #region 1B. Instance Fields

        /// <summary>The net solved offset from the biological baseline, accounting for deficits and boosts.</summary>
        public readonly float SolvedOffset;

        /// <summary>The biological baseline capacity before any deficits or boosts are applied.</summary>
        public readonly float BiologicalBaseline;

        /// <summary>The final solved capacity after applying all deficits and boosts.</summary>
        public readonly float FinalCapacity;

        /// <summary>The total multiplier applied to the biological baseline to obtain the final capacity.</summary>
        public readonly float TotalMultiplier;

        /// <summary>The aggregate positive boost from enhanced prosthetics (negative prosthetic deficits are reflected in <see cref="SolvedOffset"/> and per-part outputs).</summary>
        public readonly float CalculatedProsthetics;

        /// <summary>The total contribution from health deficits.</summary>
        public readonly float CalculatedHealths;

        /// <summary>The total contribution from athletic enhancements.</summary>
        public readonly float CalculatedAthletics;

        #endregion

        #region 1C. Lifecycle

        /// <summary>
        /// Initializes a new instance of the <see cref="MassCapacityEvaluationResult"/> struct with the specified values.
        /// </summary>
        /// <param name="solvedOffset">The net solved offset from the biological baseline.</param>
        /// <param name="biologicalBaseline">The biological baseline capacity before any deficits or boosts.</param>
        /// <param name="finalCapacity">The final solved capacity after applying all deficits and boosts.</param>
        /// <param name="totalMultiplier">The total multiplier applied to the biological baseline to obtain the final capacity.</param>
        /// <param name="calculatedProsthetics">The aggregate positive boost from enhanced prosthetics.</param>
        /// <param name="calculatedHealths">The total contribution from health deficits.</param>
        /// <param name="calculatedAthletics">The total contribution from athletic enhancements.</param>
        public MassCapacityEvaluationResult(
            float solvedOffset,
            float biologicalBaseline,
            float finalCapacity,
            float totalMultiplier,
            float calculatedProsthetics,
            float calculatedHealths,
            float calculatedAthletics)
        {
            SolvedOffset = solvedOffset;
            BiologicalBaseline = biologicalBaseline;
            FinalCapacity = finalCapacity;
            TotalMultiplier = totalMultiplier;
            CalculatedProsthetics = calculatedProsthetics;
            CalculatedHealths = calculatedHealths;
            CalculatedAthletics = calculatedAthletics;
        }

        #endregion
    }

    #endregion
}