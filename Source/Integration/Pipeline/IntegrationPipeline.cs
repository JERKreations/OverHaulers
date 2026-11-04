using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-02] STAT PRESENTATION ORCHESTRATOR & CIL SCANNER

    /// <summary>
    /// Represents information about an external patch discovered by the CIL bytecode scanner.
    /// </summary>
    public struct DiscoveredPatchInfo
    {
        public string Owner;
        public string PrimaryType;
        public bool IsRecommended;
        public StatDef TargetStat;
        public List<StatDef> CandidateStats;
        public List<StatDef> AllAssemblyStats;
    }

    /// <summary>
    /// Orchestrates stat card presentation, deduplication, and external mod adoption.
    /// Manages InfoCard deduplication, declarative XML profiles, and dynamic CIL patch discovery.
    /// Numerical capacity delivery is decoupled and universally handled by HarmonySetup.CapacityBridge.
    /// </summary>
    public static class IntegrationPipeline
    {
        #region 1. CONSTANTS & ACTIVE PRESENTATION BINDING

        public const string PresentationKeyAuto = "AUTO";
        public const string PresentationKeyNative = "STANDALONE";
        public const string PresentationKeyXmlPrefix = "XML:";
        public const string PresentationKeyCilPrefix = "CIL:";

        private static StatDef activeMassCapacityStat;

        /// <summary>
        /// The currently adopted mass capacity StatDef used for presentation and inspection.
        /// When assigned, pushes the stat downward into MedicalClassifier to preserve downward layer directionality.
        /// </summary>
        public static StatDef ActiveMassCapacityStat
        {
            get => activeMassCapacityStat;
            private set
            {
                activeMassCapacityStat = value;
                MedicalClassifier.ActiveMassCapacityStat = value;
            }
        }

        /// <summary>Indicates whether a foreign mass capacity stat has been adopted for presentation.</summary>
        public static bool IsForeignStatAdopted { get; private set; }

        /// <summary>The owner of the currently active mass capacity stat (e.g., "Native", "XML:ProfileName", "CIL:PatchName").</summary>
        public static string ActiveStatOwner { get; private set; } = "Native";

        /// <summary>Short label for the host mod of the active profile.</summary>
        public static string ActiveProfileHostLabelShort { get; private set; }

        /// <summary>Indicates whether the active profile allows authoritative takeover of adopted stats.</summary>
        public static bool ActiveProfileAllowsAuthoritativeTakeover { get; private set; }

        /// <summary>Indicates whether authoritative takeover of adopted stats is currently active.</summary>
        public static bool IsAuthoritativeTakeoverActive { get; private set; }

        /// <summary>Indicates whether the active profile requires caravan formation explanation string synchronization.</summary>
        public static bool ActiveProfileSyncFormationCapacity { get; private set; }

        /// <summary>Indicates whether authoritative takeover of adopted stats is enabled in the current session.</summary>
        private static List<PawnCapacityFactor> cachedRemovedCapacityFactors;

        /// <summary>Indicates whether authoritative takeover of adopted stats is enabled in the current session.</summary>
        private static List<StatDef> cachedRemovedStatFactors;

        /// <summary>Indicates whether the integration pipeline has been initialized.</summary>
        public static bool IsInitialized => isInitialized;

        /// <summary>Indicates whether the integration pipeline has performed a scan during the current session.</summary>
        public static bool HasScannedThisSession { get; private set; } = false;

        /// <summary>A list of all discovered patches relevant to mass capacity stat adoption.</summary>
        public static List<DiscoveredPatchInfo> DiscoveredPatches { get; } = new List<DiscoveredPatchInfo>();

        /// <summary>Indicates whether the integration pipeline has been initialized.</summary>
        private static bool isInitialized = false;

        /// <summary>Keywords used to identify relevant mass capacity stats in external mods.</summary>
        private static readonly string[] StatKeywords = { "mass", "weight", "capacity", "caravan", "carry", "load", "pack" };

        #endregion

        #region 2. PRESENTATION BINDING & RE-BINDING

        /// <summary>Raised when a stale preference is reset to automatic; carries the replacement key so the owner can persist it.</summary>
        public static event Action<string> PreferredPresentationKeyReset;

        private static string preferredPresentationKey = PresentationKeyAuto;

        /// <summary>
        /// Receives the player's presentation preference from the Settings layer (pushed downward, never pulled).
        /// </summary>
        /// <param name="key">The preference key; null or empty selects automatic resolution.</param>
        public static void SetPreferredPresentationKey(string key)
        {
            preferredPresentationKey = string.IsNullOrEmpty(key) ? PresentationKeyAuto : key;
        }

        /// <summary>
        /// Rebinds the active presentation stat, detaching from any previously adopted foreign stats
        /// and re-evaluating preferences and detected mods.
        /// </summary>
        /// <param name="preferredKey">The player's presentation preference, owned by the Settings layer.</param>
        public static void RebindPresentation(string preferredKey)
        {
            SetPreferredPresentationKey(preferredKey);
            if (HarmonySetup.HarmonyInstance == null) return;

            CleanupAdoptedForeignStat();
            ActiveMassCapacityStat = null;
            isInitialized = false;

            Initialize(HarmonySetup.HarmonyInstance);
            PawnDataRegistry.ClearAllCaches();
        }

        /// <summary>
        /// Initializes stat presentation and deduplication based on user preferences and detected external mods.
        /// </summary>
        /// <param name="harmony">The Harmony instance used if additional inspection or patching is needed.</param>
        public static void Initialize(Harmony harmony)
        {
            if (isInitialized) return;

            string localizedSuffix = " " + "kg".Translate();
            string key = preferredPresentationKey;

            // -------------------------------------------------------------------------
            // PATH A: EXPLICIT PLAYER OVERRIDES (Highest Authority)
            // -------------------------------------------------------------------------

            // Case 1: Player Forced Native Standalone Card
            if (key == PresentationKeyNative)
            {
                BindNativePresentation(localizedSuffix);
            }
            // Case 2: Player Locked an XML Declared Stat Profile
            else if (key.StartsWith(PresentationKeyXmlPrefix))
            {
                string defName = key.Substring(PresentationKeyXmlPrefix.Length);
                if (TryResolveXmlProfileByDefName(defName, out MassCapacityStatProfileDef profile, out StatDef targetStat))
                {
                    AdoptForeignStatPresentation(profile.label ?? profile.defName, targetStat, localizedSuffix, profile);
                }
                else
                {
                    RecoverStalePreference(key);
                    ResolveAutoPresentation(localizedSuffix);
                }
            }
            // Case 3: Player Locked a CIL Discovered Stat
            else if (key.StartsWith(PresentationKeyCilPrefix))
            {
                if (TryResolveCilLockedStat(key, out string owner, out StatDef targetStat))
                {
                    AdoptForeignStatPresentation(owner, targetStat, localizedSuffix);
                }
                else
                {
                    RecoverStalePreference(key);
                    ResolveAutoPresentation(localizedSuffix);
                }
            }
            // -------------------------------------------------------------------------
            // PATH B: AUTOMATIC STAT ADOPTION (Recommended Default)
            // -------------------------------------------------------------------------
            else
            {
                ResolveAutoPresentation(localizedSuffix);
            }

            isInitialized = true;
        }

        /// <summary>
        /// Activates native OverHaulers_CaravanMassCapacity as the active inspectable stat on pawns.
        /// </summary>
        /// <param name="localizedSuffix">The localized suffix used for unit display.</param>
        private static void BindNativePresentation(string localizedSuffix)
        {
            CleanupAdoptedForeignStat();

            StatDef nativeStat = OverHaulersStatDefOf.OverHaulers_CaravanMassCapacity;
            if (nativeStat != null)
            {
                nativeStat.showOnPawns = true;
            }

            ActiveMassCapacityStat = nativeStat;
            ActiveStatOwner = "Native";
            IsForeignStatAdopted = false;
            ActiveProfileHostLabelShort = null;
            ActiveProfileAllowsAuthoritativeTakeover = false;
            IsAuthoritativeTakeoverActive = false;
            ActiveProfileSyncFormationCapacity = false;

            IntegrationLogEvents.StatPresentationBound("Native", nativeStat?.defName ?? "OverHaulers_CaravanMassCapacity", localizedSuffix);
        }

        /// <summary>
        /// Adopts an external mod's StatDef for presentation, attaching our explanation/hyperlinks
        /// and hiding OverHaulers_CaravanMassCapacity to eliminate duplicate inspect rows.
        /// </summary>
        /// <param name="owner">The owner identifier or mod name associated with the foreign stat.</param>
        /// <param name="foreignStat">The foreign StatDef being adopted.</param>
        /// <param name="localizedSuffix">The localized suffix used for unit display.</param>
        /// <param name="profile">The mass capacity stat profile associated with the foreign stat, if any.</param>
        private static void AdoptForeignStatPresentation(
            string owner,
            StatDef foreignStat,
            string localizedSuffix,
            MassCapacityStatProfileDef profile = null)
        {
            if (foreignStat == null)
            {
                BindNativePresentation(localizedSuffix);
                return;
            }

            CleanupAdoptedForeignStat();

            ActiveProfileHostLabelShort = (profile != null && !string.IsNullOrEmpty(profile.hostLabelShort))
                ? profile.hostLabelShort
                : (!string.IsNullOrEmpty(owner) ? owner : "Host");

            ActiveProfileAllowsAuthoritativeTakeover = profile != null && profile.allowAuthoritativeTakeover;
            ActiveProfileSyncFormationCapacity = profile != null && profile.syncFormationCapacity;

            bool shouldTakeOver = ActiveProfileAllowsAuthoritativeTakeover;

            if (shouldTakeOver)
            {
                // 1. Surgical Capacity Factor Neutralization (e.g. Removing 'Moving' on CE)
                if (profile.neutraliseCapacityFactors != null && profile.neutraliseCapacityFactors.Count > 0 && foreignStat.capacityFactors != null)
                {
                    cachedRemovedCapacityFactors = new List<PawnCapacityFactor>();
                    for (int i = foreignStat.capacityFactors.Count - 1; i >= 0; i--)
                    {
                        PawnCapacityFactor factor = foreignStat.capacityFactors[i];
                        if (factor.capacity != null && profile.neutraliseCapacityFactors.Contains(factor.capacity.defName))
                        {
                            cachedRemovedCapacityFactors.Add(factor);
                            foreignStat.capacityFactors.RemoveAt(i);
                        }
                    }
                }

                // TODO: This needs to be handled on a per pawn basis rather than globally?
                // 2. Surgical Stat Factor Neutralization
                // if (profile.neutraliseStatFactors != null && profile.neutraliseStatFactors.Count > 0 && foreignStat.statFactors != null)
                // {
                //     cachedRemovedStatFactors = new List<StatDef>();
                //     for (int i = foreignStat.statFactors.Count - 1; i >= 0; i--)
                //     {
                //         StatDef factor = foreignStat.statFactors[i];
                //         if (factor != null && profile.neutraliseStatFactors.Contains(factor.defName))
                //         {
                //             // `bodySizeFactor` must never be neutralized as it is a critical factor for mass capacity calculations.
                //             if (factor.defName == "bodySizeFactor"){
                //                 continue;
                //             }

                //             cachedRemovedStatFactors.Add(factor);
                //             foreignStat.statFactors.RemoveAt(i);
                //         }
                //     }
                // }

                IsAuthoritativeTakeoverActive = true;
            }
            else
            {
                IsAuthoritativeTakeoverActive = false;
            }

            // DEDUPLICATION: Hide native stat on pawns so only the adopted foreign stat card is visible
            StatDef nativeStat = OverHaulersStatDefOf.OverHaulers_CaravanMassCapacity;
            if (nativeStat != null)
            {
                nativeStat.showOnPawns = false;
            }

            // Rescue labels if the foreign stat is missing them (Primary: Native Def SSOT -> Secondary: Keyed Fallback)
            if (string.IsNullOrEmpty(foreignStat.label))
            {
                foreignStat.label = !string.IsNullOrEmpty(nativeStat?.label) 
                    ? nativeStat.label 
                    : "OverHaulers_StatLabel_Fallback".Translate().ToString();
            }
            if (string.IsNullOrEmpty(foreignStat.description))
            {
                foreignStat.description = !string.IsNullOrEmpty(nativeStat?.description) 
                    ? nativeStat.description 
                    : "OverHaulers_StatDesc_Fallback".Translate().ToString();
            }

            // Inject MassCapacityStatPart for InfoCard explanation and hyperlinks
            AttachStatPart(foreignStat);

            ActiveMassCapacityStat = foreignStat;
            ActiveStatOwner = !string.IsNullOrEmpty(owner) ? owner : foreignStat.defName;
            IsForeignStatAdopted = true;

            IntegrationLogEvents.StatPresentationBound(ActiveStatOwner, foreignStat.defName, localizedSuffix);
        }

        /// <summary>
        /// Automatically resolves the most appropriate presentation card on boot.
        /// Prioritizes declarative XML profiles, falling back to Native Standalone mode.
        /// Dynamic CIL bytecode scanning is intentionally decoupled and only executes upon explicit player request.
        /// </summary>
        /// <param name="localizedSuffix">The localized suffix used for unit display.</param>
        private static void ResolveAutoPresentation(string localizedSuffix)
        {
            // Tier 1: Declarative XML Profiles (High-Speed O(1) Matching, 100% verified safety)
            if (TryResolveHighestPriorityXmlProfile(out MassCapacityStatProfileDef profile, out StatDef xmlStat))
            {
                AdoptForeignStatPresentation(profile.label ?? profile.defName, xmlStat, localizedSuffix, profile);
                return;
            }

            // Tier 2: Pure Vanilla Standalone Default
            // Dynamic CIL scanning is strictly quarantined to on-demand player activation in Mod Settings
            // to prevent unvetted third-party patches from hijacking presentation or causing .cctor landmines on boot.
            BindNativePresentation(localizedSuffix);
        }

        /// <summary>
        /// Injects MassCapacityStatPart into a foreign stat's parts list if not already present.
        /// </summary>
        /// <param name="stat">The StatDef to attach the StatPart to.</param>
        private static void AttachStatPart(StatDef stat)
        {
            if (stat == null) return;
            if (stat.parts == null)
            {
                stat.parts = new List<StatPart>();
            }
            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is MassCapacityStatPart) return;
            }
            stat.parts.Add(new MassCapacityStatPart { parentStat = stat });
        }

        /// <summary>
        /// Detaches MassCapacityStatPart from the currently adopted foreign stat
        /// and restores any original capacity factors or stat factors that were neutralized during adoption.
        /// </summary>
        private static void CleanupAdoptedForeignStat()
        {
            if (ActiveMassCapacityStat != null && IsForeignStatAdopted)
            {
                if (ActiveMassCapacityStat.parts != null)
                {
                    ActiveMassCapacityStat.parts.RemoveAll(p => p is MassCapacityStatPart);
                }

                // Restore any surgically neutralized capacityFactors
                if (cachedRemovedCapacityFactors != null)
                {
                    if (ActiveMassCapacityStat.capacityFactors == null)
                    {
                        ActiveMassCapacityStat.capacityFactors = new List<PawnCapacityFactor>();
                    }
                    ActiveMassCapacityStat.capacityFactors.AddRange(cachedRemovedCapacityFactors);
                    cachedRemovedCapacityFactors = null;
                }

                // Restore any surgically neutralized statFactors
                if (cachedRemovedStatFactors != null)
                {
                    if (ActiveMassCapacityStat.statFactors == null)
                    {
                        ActiveMassCapacityStat.statFactors = new List<StatDef>();
                    }
                    ActiveMassCapacityStat.statFactors.AddRange(cachedRemovedStatFactors);
                    cachedRemovedStatFactors = null;
                }
            }

            IsAuthoritativeTakeoverActive = false;
            ActiveProfileAllowsAuthoritativeTakeover = false;
            ActiveProfileSyncFormationCapacity = false;
        }

        /// <summary>
        /// Recovers from a stale presentation preference by reverting to automatic selection if a previously selected mod is removed.
        /// </summary>
        /// <param name="staleKey">The key representing the previously selected preference that is now considered stale.</param>
        private static void RecoverStalePreference(string staleKey)
        {
            IntegrationLogEvents.StalePreferenceRecovered(staleKey);
            preferredPresentationKey = PresentationKeyAuto;
            PreferredPresentationKeyReset?.Invoke(PresentationKeyAuto);
        }

        #endregion

        #region 3. DECLARATIVE XML PROFILE RESOLUTION

        /// <summary>
        /// Attempts to resolve the highest priority XML profile based on available MassCapacityStatProfileDef definitions.
        /// </summary>
        /// <param name="owner">The resolved owner name of the profile definition.</param>
        /// <param name="stat">The resolved StatDef associated with the profile.</param>
        /// <param name="hostLabelShort">The short label for the host mod, used in UI displays.</param>
        /// <param name="hostDeltaScaling">Indicates whether host delta scaling is enabled for the resolved profile.</param>
        /// <returns>True if a valid XML profile is found and resolved; otherwise, false.</returns>
        public static bool TryResolveHighestPriorityXmlProfile(out MassCapacityStatProfileDef profile, out StatDef stat)
        {
            profile = null;
            stat = null;
            List<MassCapacityStatProfileDef> allXmlProfiles = DefDatabase<MassCapacityStatProfileDef>.AllDefsListForReading;
            if (allXmlProfiles == null || allXmlProfiles.Count == 0) return false;

            List<MassCapacityStatProfileDef> sortedProfiles = new List<MassCapacityStatProfileDef>(allXmlProfiles);
            sortedProfiles.Sort((a, b) => b.priority.CompareTo(a.priority));

            // Iterate through the sorted profiles and attempt to resolve the highest priority valid profile.
            for (int i = 0; i < sortedProfiles.Count; i++)
            {
                if (sortedProfiles[i].IsValidAndActive(out StatDef resolved))
                {
                    profile = sortedProfiles[i];
                    stat = resolved;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve an XML profile by defName.
        /// </summary>
        /// <param name="defName">The name of the profile definition to resolve.</param>
        /// <param name="owner">The resolved owner name of the profile definition.</param>
        /// <param name="stat">The resolved StatDef associated with the profile.</param>
        /// <param name="hostDeltaScaling">Indicates whether host delta scaling is enabled for the resolved profile.</param>
        /// <returns>True if a valid XML profile is found and resolved; otherwise, false.</returns>
        public static bool TryResolveXmlProfileByDefName(string defName, out MassCapacityStatProfileDef profile, out StatDef stat)
        {
            profile = null;
            stat = null;
            MassCapacityStatProfileDef def = DefDatabase<MassCapacityStatProfileDef>.GetNamedSilentFail(defName);
            if (def != null && def.IsValidAndActive(out StatDef resolved))
            {
                profile = def;
                stat = resolved;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempts to resolve a CIL-locked stat key ("CIL:Owner:StatDefName").
        /// </summary>
        /// <param name="key">The key used to identify the CIL-locked stat. Format: "CIL:Owner:StatDefName".</param>
        /// <param name="owner">The parsed owner name from the key.</param>
        /// <param name="stat">The resolved StatDef instance.</param>
        /// <returns>True if a valid CIL-locked stat is found and resolved; otherwise, false.</returns>
        private static bool TryResolveCilLockedStat(string key, out string owner, out StatDef stat)
        {
            owner = null;
            stat = null;
            // Format: "CIL:Owner:StatDefName"
            string[] parts = key.Split(':');
            if (parts.Length == 3)
            {
                owner = parts[1];
                string statDefName = parts[2];
                stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
                return stat != null;
            }
            return false;
        }

        #endregion

        #region 4. DYNAMIC CIL BYTECODE DISCOVERY ENGINE

        /// <summary>
        /// Triggers a manual scan for external patches if it hasn't been done in the current session.
        /// This ensures that any CIL modifications by external patches are discovered and processed.
        /// </summary>
        public static void TriggerManualScan()
        {
            if (HasScannedThisSession) return;
            EnsurePatchesInspected();
        }

        /// <summary>
        /// Ensures that external patches are inspected and processed if it hasn't been done in the current session.
        /// </summary>
        public static void EnsurePatchesInspected()
        {
            if (HasScannedThisSession) return;
            InspectExternalPatches();
            HasScannedThisSession = true;
        }

        /// <summary>
        /// Inspects and processes external Harmony patches applied to MassUtility.Capacity.
        /// </summary>
        public static void InspectExternalPatches()
        {
            DiscoveredPatches.Clear();

            var targetMethod = AccessTools.Method(typeof(MassUtility), nameof(MassUtility.Capacity));
            if (targetMethod == null) return;

            Patches patchInfo = Harmony.GetPatchInfo(targetMethod);
            if (patchInfo == null) return;

            Dictionary<string, DiscoveredPatchInfo> map = new Dictionary<string, DiscoveredPatchInfo>();

            ProcessPatchList(patchInfo.Transpilers, "Transpiler", targetMethod, map);
            ProcessPatchList(patchInfo.Prefixes, "Prefix", targetMethod, map);
            ProcessPatchList(patchInfo.Postfixes, "Postfix", targetMethod, map);

            foreach (var kvp in map)
            {
                DiscoveredPatches.Add(kvp.Value);
            }

            HasScannedThisSession = true;
        }

        /// <summary>
        /// Processes a list of patches of a specific type (Transpiler, Prefix, Postfix) for a given target method and updates the map of discovered patch information.
        /// </summary>
        /// <param name="patches">The list of patches to process.</param>
        /// <param name="typeName">The type of the patches (Transpiler, Prefix, Postfix).</param>
        /// <param name="targetMethod">The method that the patches are applied to.</param>
        /// <param name="map">The map of discovered patch information to update.</param>
        private static void ProcessPatchList(IReadOnlyCollection<Patch> patches, string typeName, MethodInfo targetMethod, Dictionary<string, DiscoveredPatchInfo> map)
        {
            if (patches == null) return;

            foreach (var p in patches)
            {
                // Skip patches that have no owner or belong to the current mod itself.
                if (string.IsNullOrEmpty(p.owner) || p.owner == "com.overhaulers.mod") continue;

                // Retrieve or create the discovered patch info for this owner.
                if (!map.TryGetValue(p.owner, out var info))
                {
                    List<StatDef> candidates = ResolveCandidateStatsForPatch(p, targetMethod, out List<StatDef> allAssemblyStats);
                    StatDef defaultTarget = candidates.Count > 0 ? candidates[0] : null;

                    // Initialize the discovered patch info with the resolved candidate stats.
                    info = new DiscoveredPatchInfo
                    {
                        Owner = p.owner,
                        PrimaryType = typeName,
                        IsRecommended = (typeName == "Transpiler"),
                        TargetStat = defaultTarget,
                        CandidateStats = candidates,
                        AllAssemblyStats = allAssemblyStats
                    };
                }

                map[p.owner] = info;
            }
        }

        /// <summary>
        /// Selects the most appropriate discovered patch, prioritizing recommended patches.
        /// </summary>
        /// <returns>The selected discovered patch information, or default if none are available.</returns>
        private static DiscoveredPatchInfo SelectTargetPatch()
        {
            for (int i = 0; i < DiscoveredPatches.Count; i++)
            {
                if (DiscoveredPatches[i].IsRecommended) return DiscoveredPatches[i];
            }
            return DiscoveredPatches.Count > 0 ? DiscoveredPatches[0] : default;
        }

        /// <summary>
        /// Resolves candidate StatDef instances for a given patch by inspecting its method CIL and assembly.
        /// </summary>
        /// <param name="patch">The patch for which to resolve candidate stats.</param>
        /// <param name="targetMethod">The method that the patch is applied to.</param>
        /// <param name="allAssemblyStats">Outputs the list of all StatDef instances found in the patch's assembly.</param>
        /// <returns>The list of candidate StatDef instances relevant to the patch.</returns>
        private static List<StatDef> ResolveCandidateStatsForPatch(Patch patch, MethodInfo targetMethod, out List<StatDef> allAssemblyStats)
        {
            List<StatDef> candidates = new List<StatDef>();
            allAssemblyStats = new List<StatDef>();
            if (patch?.PatchMethod == null) return candidates;

            InspectMethodCilForStats(patch.PatchMethod, candidates);

            // Inspect the assembly containing the patch method for additional StatDef instances.
            Assembly patchAssembly = patch.PatchMethod.DeclaringType?.Assembly;
            if (patchAssembly != null)
            {
                InspectAssemblyForStats(patchAssembly, candidates, allAssemblyStats);
            }

            candidates.Sort((a, b) => GetStatRelevanceScore(b).CompareTo(GetStatRelevanceScore(a)));
            allAssemblyStats.Sort((a, b) => string.Compare(a.defName, b.defName, StringComparison.OrdinalIgnoreCase));

            return candidates;
        }

        /// <summary>
        /// Inspects the CIL instructions of the specified method to identify references to StatDef fields and adds them to the list
        ///  of candidate stats.
        /// Fully shielded against .cctor static constructor poisoning, DynamicMethods, and missing method bodies.
        /// </summary>
        /// <param name="method">The method whose CIL instructions are to be inspected.</param>
        /// <param name="candidates">The list of candidate StatDef instances to populate based on the inspection.</param>
        private static void InspectMethodCilForStats(MethodInfo method, List<StatDef> candidates)
        {
            if (!CanSafelyInspectMethod(method)) return;

            try
            {
                // Check if this method is an iterator state machine transpiler (yield return)
                InspectTranspilerStateMachineIfPresent(method, candidates);

                List<CodeInstruction> instructions = PatchProcessor.GetCurrentInstructions(method);
                if (instructions == null || instructions.Count == 0) return;

                for (int i = 0; i < instructions.Count; i++)
                {
                    CodeInstruction instr = instructions[i];
                    if (instr?.operand == null) continue;

                    // Case 1: Static Field reference (e.g. ldsfld StatDefOf.CarryingCapacity)
                    if (instr.operand is FieldInfo fieldInfo && fieldInfo.IsStatic && typeof(StatDef).IsAssignableFrom(fieldInfo.FieldType))
                    {
                        StatDef resolvedStat = null;

                        // COMPATIBILITY SHIELD (.cctor STATIC CONSTRUCTOR LANDMINE):
                        // In RimWorld, invoking fieldInfo.GetValue(null) on an uninitialized foreign class forces
                        // the CLR/Mono runtime to execute its static constructor (.cctor). If that foreign class
                        // expects uninitialized game state, it throws an unhandled exception, permanently poisoning
                        // the type for the lifetime of the process.
                        //
                        // Strategy: Resolve strictly by field name from DefDatabase<StatDef>.
                        // If unresolved, only read via reflection if the type has NO static constructor (TypeInitializer == null).
                        if (!string.IsNullOrEmpty(fieldInfo.Name))
                        {
                            resolvedStat = DefDatabase<StatDef>.GetNamedSilentFail(fieldInfo.Name);
                        }

                        if (resolvedStat == null && fieldInfo.DeclaringType != null && fieldInfo.DeclaringType.TypeInitializer == null)
                        {
                            try
                            {
                                resolvedStat = fieldInfo.GetValue(null) as StatDef;
                            }
                            catch
                            {
                                resolvedStat = null;
                            }
                        }

                        AddCandidateIfValid(resolvedStat, candidates);
                    }
                    // Case 2: String operand representing a StatDef defName (e.g. ldstr "CaravanMassCapacity")
                    else if (instr.operand is string strOperand && !string.IsNullOrEmpty(strOperand))
                    {
                        AddCandidateIfValid(DefDatabase<StatDef>.GetNamedSilentFail(strOperand), candidates);
                    }
                    // Case 3: Direct StatDef object reference
                    else if (instr.operand is StatDef statDef)
                    {
                        AddCandidateIfValid(statDef, candidates);
                    }
                }
            }
            catch (Exception ex)
            {
                string methodName = method.DeclaringType != null ? $"{method.DeclaringType.FullName}.{method.Name}" : method.Name;
                OHLog.Warn("Integration.CilMethodInspection", ex, $"Failed to inspect method {methodName}.");
            }
        }

        /// <summary>
        /// Pre-flight safety check determining whether a method can be safely disassembled by Harmony.
        /// Shields against DynamicMethods, unmanaged extern stubs, and missing method bodies.
        /// </summary>
        /// <param name="method">The method candidate to evaluate.</param>
        /// <returns>True if the method has a valid managed IL body and can be safely disassembled; false otherwise.</returns>
        private static bool CanSafelyInspectMethod(MethodInfo method)
        {
            if (method == null || method.IsAbstract) return false;

            // Shield against DynamicMethod without metadata tokens
            if (method.DeclaringType == null) return false;
            string typeName = method.GetType().FullName ?? string.Empty;
            if (typeName.IndexOf("DynamicMethod", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            try
            {
                // Verify the method actually contains managed IL bytes
                MethodBody body = method.GetMethodBody();
                return body != null && body.GetILAsByteArray() != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detects if a Transpiler method was compiled as an iterator state machine (using yield return)
        /// and inspects the compiler-generated MoveNext() method body where the actual CIL resides.
        /// </summary>
        /// <param name="method">The transpiler method being inspected.</param>
        /// <param name="candidates">The list of candidate StatDefs to populate.</param>
        private static void InspectTranspilerStateMachineIfPresent(MethodInfo method, List<StatDef> candidates)
        {
            if (method.DeclaringType == null) return;
            if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(method.ReturnType)) return;

            Type[] nestedTypes = null;
            try
            {
                nestedTypes = method.DeclaringType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);
            }
            catch
            {
                return;
            }

            if (nestedTypes == null || nestedTypes.Length == 0) return;

            string expectedPrefix = $"<{method.Name}>";
            for (int i = 0; i < nestedTypes.Length; i++)
            {
                Type nested = nestedTypes[i];
                if (nested.Name.IndexOf(expectedPrefix, StringComparison.Ordinal) >= 0)
                {
                    MethodInfo moveNextMethod = nested.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (moveNextMethod != null && CanSafelyInspectMethod(moveNextMethod))
                    {
                        InspectMethodCilForStats(moveNextMethod, candidates);
                    }
                }
            }
        }

        /// <summary>
        /// Inspects the given assembly for StatDef instances and adds them to the candidate list if they are relevant.
        /// </summary>
        /// <param name="assembly">The assembly to inspect for StatDef instances.</param>
        /// <param name="candidates">The list of candidate StatDef instances to populate.</param>
        /// <param name="allAssemblyStats">The list of all StatDef instances found in the assembly.</param>
        private static void InspectAssemblyForStats(Assembly assembly, List<StatDef> candidates, List<StatDef> allAssemblyStats)
        {
            if (assembly == null) return;

            try
            {
                // Get the mod content pack associated with the assembly.
                ModContentPack pack = GetModContentPackForAssembly(assembly);
                List<StatDef> allStats = DefDatabase<StatDef>.AllDefsListForReading;
                if (allStats == null) return;

                // Iterate through all StatDef instances in the assembly.
                for (int i = 0; i < allStats.Count; i++)
                {
                    StatDef stat = allStats[i];
                    if (stat == null) continue;

                    // Check if the StatDef belongs to the current mod content pack.
                    if (pack != null && stat.modContentPack == pack)
                    {
                        if (!allAssemblyStats.Contains(stat)) allAssemblyStats.Add(stat);
                        AddCandidateIfValid(stat, candidates);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Adds the given StatDef to the candidate list if it is considered valid.
        /// </summary>
        /// <param name="stat">The StatDef to evaluate for candidacy.</param>
        /// <param name="candidates">The list of candidate StatDef instances to potentially add to.</param>
        private static void AddCandidateIfValid(StatDef stat, List<StatDef> candidates)
        {
            if (stat == null || candidates == null || stat == StatDefOf.CarryingCapacity || stat == StatDefOf.Mass || candidates.Contains(stat)) return;
            if (IsLikelyMassCapacityStat(stat)) candidates.Add(stat);
        }

        /// <summary>
        /// Determines whether the given StatDef is likely related to mass or carrying capacity.
        /// </summary>
        /// <param name="stat">The StatDef to evaluate.</param>
        /// <returns>True if the StatDef is likely related to mass or carrying capacity; otherwise, false.</returns>
        private static bool IsLikelyMassCapacityStat(StatDef stat)
        {
            if (stat.HasModExtension<MassCapacityStatModExtension>()) return true;

            string lowerDef = stat.defName?.ToLowerInvariant() ?? "";
            string lowerLabel = stat.label?.ToLowerInvariant() ?? "";

            // Check if the stat's defName or label contains any of the predefined keywords.
            for (int i = 0; i < StatKeywords.Length; i++)
            {
                string kw = StatKeywords[i];
                if (lowerDef.Contains(kw) || lowerLabel.Contains(kw)) return true;
            }
            return false;
        }

        /// <summary>
        /// Calculates a relevance score for the given StatDef based on its characteristics.
        /// Higher scores indicate greater relevance to caravan mass or carrying capacity.
        /// </summary>
        /// <param name="stat">The StatDef for which to calculate the relevance score.</param>
        /// <returns>An integer representing the relevance score of the StatDef.</returns>
        private static int GetStatRelevanceScore(StatDef stat)
        {
            if (stat == null) return 0;
            int score = 0;
            if (stat.HasModExtension<MassCapacityStatModExtension>()) score += 100;
            string lowerDef = stat.defName?.ToLowerInvariant() ?? "";
            if (lowerDef.Contains("caravan")) score += 10;
            if (lowerDef.Contains("mass")) score += 10;
            if (lowerDef.Contains("weight")) score += 5;
            if (lowerDef.Contains("carry")) score += 5;
            if (lowerDef.Contains("capacity")) score += 5;
            if (stat.showOnPawns) score += 2;
            return score;
        }

        /// <summary>
        /// Gets the ModContentPack associated with the specified assembly, if any.
        /// </summary>
        /// <param name="assembly">The assembly for which to find the associated ModContentPack.</param>
        /// <returns>The ModContentPack associated with the specified assembly, or null if none is found.</returns>
        private static ModContentPack GetModContentPackForAssembly(Assembly assembly)
        {
            if (assembly == null) return null;
            List<ModContentPack> runningMods = LoadedModManager.RunningModsListForReading;
            if (runningMods == null) return null;

            // Iterate through all running mods to find the one that contains the specified assembly.
            for (int i = 0; i < runningMods.Count; i++)
            {
                ModContentPack pack = runningMods[i];
                if (pack?.assemblies?.loadedAssemblies != null && pack.assemblies.loadedAssemblies.Contains(assembly))
                {
                    return pack;
                }
            }
            return null;
        }

        #endregion
    }

    #endregion
}