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
        public static readonly MassCapacityEvaluationResult Fallback =
            new MassCapacityEvaluationResult(0f, 0f, 0.01f, 1f, 0f, 0f, 0f, false);
        #endregion

        #region 1B. Instance Fields
        public readonly float SolvedOffset;
        public readonly float BiologicalBaseline;
        public readonly float FinalCapacity;
        public readonly float TotalMultiplier;
        public readonly float CalculatedProsthetics;
        public readonly float CalculatedHealths;
        public readonly float CalculatedAthletics;
        public readonly bool IsAtSafetyFloor;
        #endregion

        #region 1C. Lifecycle
        public MassCapacityEvaluationResult(
            float solvedOffset,
            float biologicalBaseline,
            float finalCapacity,
            float totalMultiplier,
            float calculatedProsthetics,
            float calculatedHealths,
            float calculatedAthletics,
            bool isAtSafetyFloor)
        {
            SolvedOffset = solvedOffset;
            BiologicalBaseline = biologicalBaseline;
            FinalCapacity = finalCapacity;
            TotalMultiplier = totalMultiplier;
            CalculatedProsthetics = calculatedProsthetics;
            CalculatedHealths = calculatedHealths;
            CalculatedAthletics = calculatedAthletics;
            IsAtSafetyFloor = isAtSafetyFloor;
        }
        #endregion
    }

    #endregion
}