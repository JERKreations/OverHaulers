using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Defines the interface for a pipeline driver responsible for managing mass capacity stats and related calculations within the Over Haulers mod.
    /// </summary>
    public interface IPipelineDriver
    {
        string DriverIdentifier { get; }
        bool IsStatDriven { get; }
        StatDef ActiveMassCapacityStat { get; }
        string UnitSuffix { get; }

        void Initialize(Harmony harmony);
        void Cleanup();
        float ResolveDriverBaseline(Pawn pawn);
        void OnMassUtilityCapacityPostfix(Pawn pawn, ref float result, StringBuilder explanation);
    }
}