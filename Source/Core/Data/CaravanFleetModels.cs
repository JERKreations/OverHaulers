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
        /// <summary>Total baseline mass capacity of all pawns in the caravan.</summary>
        public float TotalBaseline;

        /// <summary>Total actual mass capacity of all pawns in the caravan.</summary>
        public float TotalCapacity;

        /// <summary>Total number of pawns in the caravan.</summary>
        public int TotalPawnCount;

        /// <summary>Pawn with the highest gross mass capacity in the caravan.</summary>
        public Pawn TopGrossContributor;

        /// <summary>Gross mass capacity of the top contributor.</summary>
        public float TopGrossCapacity;

        /// <summary>Pawn with the highest net contribution to the caravan's capacity.</summary>
        public Pawn TopNetContributor;

        /// <summary>Net offset of the top net contributor.</summary>
        public float TopNetOffset;

        /// <summary>Actual capacity of the top net contributor.</summary>
        public float TopNetCapacity;

        /// <summary>Pawn with the most impaired net contribution in the caravan.</summary>
        public Pawn MostImpaired;

        /// <summary>Net offset of the most impaired contributor.</summary>
        public float MostImpairedNetOffset;

        /// <summary>Actual capacity of the most impaired contributor.</summary>
        public float MostImpairedCapacity;

        /// <summary>Total net offset of the caravan's capacity.</summary>
        public float TotalNetOffset => TotalCapacity - TotalBaseline;

        /// <summary>Collective multiplier of the caravan's capacity relative to the baseline.</summary>
        public float CollectiveMultiplier => TotalBaseline > 0f ? (TotalCapacity / TotalBaseline) : 1.0f;
    }

    #endregion
}