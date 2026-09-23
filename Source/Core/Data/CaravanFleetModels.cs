using Verse;

namespace OverHaulers
{
    #region 1. [VIEW-05] CARAVAN FLEET SUMMARY DATA STRUCTURE

    /// <summary>
    /// [VIEW-05] Lightweight stack-allocated value structure containing aggregate Caravan Mass Capacity metrics
    /// across all pawns selected for a caravan or transport pod launch.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public struct CaravanFleetSummary
    {
        public float TotalBaseline;
        public float TotalCapacity;
        public int TotalPawnCount;

        // Standout Performers: Gross (Tonnage) vs Net (Biomechanical Augmentation)
        public Pawn TopGrossContributor;
        public float TopGrossCapacity;

        public Pawn TopNetContributor;
        public float TopNetOffset;
        public float TopNetCapacity;

        public Pawn MostImpaired;
        public float MostImpairedNetOffset;
        public float MostImpairedCapacity;

        public float TotalNetOffset => TotalCapacity - TotalBaseline;
        public float CollectiveMultiplier => TotalBaseline > 0f ? (TotalCapacity / TotalBaseline) : 1.0f;
    }

    #endregion
}