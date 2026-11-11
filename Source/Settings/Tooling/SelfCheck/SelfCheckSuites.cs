using System.Collections.Generic;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-04] The self-check catalogue. Add new checks to <see cref="RegisterAll"/>; a check passes by returning,
    /// fails by throwing (use <see cref="Expect"/>), and can opt out via <see cref="SelfCheckContext.Skip"/>.
    /// Resides under Source/Settings/Tooling/SelfCheck/.
    /// </summary>
    internal static partial class SelfCheckSuites
    {
        #region 1. [DIAG-04] CATALOGUE KEYS

        // Keyed-translation identifiers; the UI translates them, exported reports print them verbatim.
        private const string GroupTable = "OverHaulers_SelfCheck_Group_SnapshotTable";
        private const string GroupCollections = "OverHaulers_SelfCheck_Group_Collections";
        private const string GroupTopology = "OverHaulers_SelfCheck_Group_Topology";
        private const string GroupSolver = "OverHaulers_SelfCheck_Group_Solver";
        private const string GroupConfiguration = "OverHaulers_SelfCheck_Group_Configuration";
        private const string GroupTestBench = "OverHaulers_SelfCheck_Group_TestBench";

        #endregion

        #region 2. [DIAG-04] CHECK REGISTRATION

        /// <summary>
        /// Registers every check in execution order; each runs on its own frame.
        /// </summary>
        /// <param name="list">The queue the checks are appended to.</param>
        public static void RegisterAll(List<SelfCheckDef> list)
        {
            list.Add(new SelfCheckDef(GroupConfiguration, "OverHaulers_SelfCheck_Check_CoreDefaultsInRange", CheckCoreDefaultsInRange));
            list.Add(new SelfCheckDef(GroupConfiguration, "OverHaulers_SelfCheck_Check_ConfigSanitisation", CheckConfigSanitisation));
            list.Add(new SelfCheckDef(GroupConfiguration, "OverHaulers_SelfCheck_Check_DisplayPrecision", CheckDisplayPrecision));
            list.Add(new SelfCheckDef(GroupTestBench, "OverHaulers_SelfCheck_Check_CaravanSubjectFilter", CheckCaravanSubjectFilter));
            list.Add(new SelfCheckDef(GroupTestBench, "OverHaulers_SelfCheck_Check_SubjectPickerSession", CheckSubjectPickerSession));
            list.Add(new SelfCheckDef(GroupTestBench, "OverHaulers_SelfCheck_Check_ButtonLayout", CheckButtonLayout));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_PackUnpack", CheckPackUnpack));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_NeutralDefaults", CheckNeutralDefaults));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_WriteOverwriteRead", CheckWriteOverwriteRead));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_EvictAndReset", CheckEvictAndReset));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_ClusterEviction", CheckClusterEviction));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_RandomAgainstModel", CheckRandomAgainstModel));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_Saturation", CheckSaturation));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_DynamicScaling", CheckDynamicScaling));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_ConcurrentReaders", CheckConcurrentReaders));
            list.Add(new SelfCheckDef(GroupCollections, "OverHaulers_SelfCheck_Check_UniqueList", CheckUniqueList));
            list.Add(new SelfCheckDef(GroupTopology, "OverHaulers_SelfCheck_Check_TopologyIntegrity", CheckTopologyIntegrity));
            list.Add(new SelfCheckDef(GroupSolver, "OverHaulers_SelfCheck_Check_SolverDeterminism", CheckSolverDeterminism));
            list.Add(new SelfCheckDef(GroupSolver, "OverHaulers_SelfCheck_Check_WoundsNeverIncrease", CheckWoundsNeverIncrease));
        }

        #endregion
    }
}
