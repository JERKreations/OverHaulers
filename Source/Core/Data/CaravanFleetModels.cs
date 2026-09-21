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
        public float TotalProstheticBoost;
        public float TotalHealthDeficit;
        public float TotalAthleticOffset;

        public int TotalPawnCount;
        public int BoostedPawnCount;
        public int ImpairedPawnCount;

        public Pawn TopContributor;
        public float TopContributorCapacity;

        public Pawn MostImpaired;
        public float MostImpairedDeficit;

        public float TotalNetOffset => TotalCapacity - TotalBaseline;
        public float CollectiveMultiplier => TotalBaseline > 0f ? (TotalCapacity / TotalBaseline) : 1.0f;
        public bool HasSignificantModifications => (BoostedPawnCount > 0 || ImpairedPawnCount > 0);
    }

    #endregion
}