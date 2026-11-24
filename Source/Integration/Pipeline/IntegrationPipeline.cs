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
    /// Contains metadata about a patch discovered by the CIL bytecode scanner, including its owner, target stat, and candidate stats.
    /// </summary>
    public struct DiscoveredPatchInfo
    {
        public string Owner;
        public string PrimaryType;
        public List<StatDef> CandidateStats;
        public List<StatDef> AllAssemblyStats;
    }

    /// <summary>
    /// Metadata describing a valid active <see cref="PawnCapacityFactor"/> ascertained directly from a candidate <see cref="StatDef"/>.
    /// </summary>
    public readonly struct AscertainedCapacityFactor
    {
        public readonly PawnCapacityDef Capacity;
        public readonly string DefName;
        public readonly string Label;
        public readonly float Weight;
        public readonly bool OverlapsOverHaulers;

        public AscertainedCapacityFactor(PawnCapacityDef capacity, float weight, bool overlapsOverHaulers)
        {
            Capacity = capacity;
            DefName = capacity.defName;
            Label = !string.IsNullOrEmpty(capacity.label) ? capacity.LabelCap.ToString() : capacity.defName;
            Weight = weight;
            OverlapsOverHaulers = overlapsOverHaulers;
        }
    }

    /// <summary>
    /// Identifies the resolution mechanism backing the active InfoCard stat presentation.
    /// </summary>
    public enum StatAdoptionSource : byte
    {
        Native = 0,
        XmlProfile = 1,
        CilDiscovered = 2
    }

    /// <summary>
    /// Orchestrates stat card presentation, deduplication, and external mod adoption.
    /// Manages InfoCard deduplication, declarative XML profiles, and dynamic CIL patch discovery.
    /// Numerical capacity delivery is decoupled and universally handled by HarmonySetup.CapacityBridge.
    /// </summary>
    public static class IntegrationPipeline
    {
        #region 1. CONSTANTS & ACTIVE PRESENTATION BINDING

        /// <summary>The presentation key indicating how the mass capacity stat should be displayed.</summary>
        public const string PresentationKeyAuto = "AUTO";

        /// <summary>The presentation key indicating native standalone display mode.</summary>
        public const string PresentationKeyNative = "STANDALONE";

        /// <summary>The presentation key prefix for XML-defined profiles.</summary>
        public const string PresentationKeyXmlPrefix = "XML:";

        /// <summary>The presentation key prefix for CIL-discovered profiles.</summary>
        public const string PresentationKeyCilPrefix = "CIL:";

        /// <summary>
        /// [INT-NEUT-01] Canonical set of biological capacities actively simulated by Over Haulers' kinetic solver.
        /// Any adopted foreign StatDef containing capacity factors for these capacities is a candidate for heuristic neutralization.
        /// </summary>
        private static readonly HashSet<string> SimulatedBiologicalCapacities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Moving",
            "Manipulation",
            "Breathing",
            "BloodPumping"
        };

        /// <summary>The currently active mass capacity StatDef adopted for presentation and inspection.</summary>
        private static StatDef activeMassCapacityStat;

        /// <summary>Capacity factors stripped from the adopted stat, kept so they can be restored on detach.</summary>
        private static List<PawnCapacityFactor> cachedRemovedCapacityFactors;

        /// <summary>Active XML profile definition bound during foreign stat adoption, if any.</summary>
        private static MassCapacityStatProfileDef activeProfileDef;

        /// <summary>Indicates whether the integration pipeline has been initialized.</summary>
        private static bool isInitialized = false;

        /// <summary>Keywords used to identify relevant mass capacity stats in external mods.</summary>
        private static readonly string[] StatKeywords = { "mass", "weight", "capacity", "caravan", "carry", "load", "pack" };

        /// <summary>The currently preferred presentation key for mass capacity display.</summary>
        private static string preferredPresentationKey = PresentationKeyAuto;

        /// <summary>Optional user-specified capacity factor defNames for the active CIL stat; null uses automatic overlap detection.</summary>
        private static List<string> customCilNeutralizedFactors;

        /// <summary>When true, a CIL-adopted foreign stat represents an additive delta (base 0) rather than full capacity.</summary>
        private static bool preferredCilAdditiveBonus = false;

        /// <summary>Raised when a stale preference is reset to automatic; carries the replacement key so the owner can persist it.</summary>
        public static event Action<string> PreferredPresentationKeyReset;

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

        /// <summary>Resolution source backing the active InfoCard stat presentation.</summary>
        public static StatAdoptionSource ActiveAdoptionSource { get; private set; } = StatAdoptionSource.Native;

        /// <summary>True when the currently adopted CIL stat is configured as an additive bonus rather than a full capacity replacement.</summary>
        public static bool IsAdoptedStatAdditiveBonus { get; private set; }

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

        /// <summary>Indicates whether the integration pipeline has been initialized.</summary>
        public static bool IsInitialized => isInitialized;

        /// <summary>Indicates whether the integration pipeline has performed a scan during the current session.</summary>
        public static bool HasScannedThisSession { get; private set; } = false;

        /// <summary>A list of all discovered patches relevant to mass capacity stat adoption.</summary>
        public static List<DiscoveredPatchInfo> DiscoveredPatches { get; } = new List<DiscoveredPatchInfo>();

        /// <summary>True if at least one loaded XML MassCapacityStatProfileDef targets an active mod.</summary>
        public static bool HasAnyActiveXmlProfile
        {
            get
            {
                List<MassCapacityStatProfileDef> allXmlProfiles = DefDatabase<MassCapacityStatProfileDef>.AllDefsListForReading;
                if (allXmlProfiles == null) return false;
                for (int i = 0; i < allXmlProfiles.Count; i++)
                {
                    if (allXmlProfiles[i] != null && allXmlProfiles[i].IsValidAndActive(out _))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        #endregion

        #region 2. PRESENTATION BINDING & RE-BINDING

        /// <summary>
        /// Receives the player's presentation preference from the Settings layer (pushed downward, never pulled).
        /// </summary>
        /// <param name="key">The preference key; null or empty selects automatic resolution.</param>
        public static void SetPreferredPresentationKey(string key)
        {
            preferredPresentationKey = string.IsNullOrEmpty(key) ? PresentationKeyAuto : key;
        }

        /// <summary>
        /// Pushes optional per-factor neutralization overrides for CIL-discovered stats downward from Settings.
        /// Pass null to use automatic overlapping biological factor detection.
        /// </summary>
        public static void SetCustomCilNeutralizedFactors(IReadOnlyList<string> factors)
        {
            if (factors == null)
            {
                customCilNeutralizedFactors = null;
                return;
            }

            if (customCilNeutralizedFactors == null)
            {
                customCilNeutralizedFactors = new List<string>(factors.Count);
            }
            else
            {
                customCilNeutralizedFactors.Clear();
            }

            for (int i = 0; i < factors.Count; i++)
            {
                string f = factors[i];
                if (!string.IsNullOrEmpty(f) && !customCilNeutralizedFactors.Contains(f))
                {
                    customCilNeutralizedFactors.Add(f);
                }
            }
        }

        /// <summary>
        /// Pushes whether a CIL-adopted stat represents an additive bonus rather than full capacity downward from Settings.
        /// </summary>
        public static void SetCilAdditiveBonusMode(bool isAdditiveBonus)
        {
            preferredCilAdditiveBonus = isAdditiveBonus;
        }

        /// <summary>
        /// Idempotently verifies that an adopted stat still holds <see cref="MassCapacityStatPart"/> and
        /// neutralized capacity factors after all mods complete static startup initialization.
        /// </summary>
        public static void VerifyAdoptedStatIntegrity()
        {
            if (!isInitialized)
            {
                if (HarmonySetup.HarmonyInstance != null)
                {
                    Initialize(HarmonySetup.HarmonyInstance);
                }
                return;
            }

            StatDef nativeStat = OverHaulersStatDefOf.OverHaulers_CaravanMassCapacity;

            if (IsForeignStatAdopted && ActiveMassCapacityStat != null)
            {
                if (nativeStat != null)
                {
                    nativeStat.showOnPawns = false;
                }

                AttachStatPart(ActiveMassCapacityStat);

                if (IsAuthoritativeTakeoverActive)
                {
                    NeutralizeForeignCapacityFactors(ActiveMassCapacityStat, activeProfileDef);
                }
            }
            else if (nativeStat != null)
            {
                nativeStat.showOnPawns = true;
            }
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
            ActiveAdoptionSource = StatAdoptionSource.Native;
            IsAdoptedStatAdditiveBonus = false;
            activeProfileDef = null;
            ActiveProfileHostLabelShort = null;
            ActiveProfileAllowsAuthoritativeTakeover = false;
            IsAuthoritativeTakeoverActive = false;
            ActiveProfileSyncFormationCapacity = false;

            IntegrationLogEvents.StatPresentationBound("Native", nativeStat?.defName ?? "OverHaulers_CaravanMassCapacity", localizedSuffix);
        }

        /// <summary>
        /// Adopts an external mod's StatDef for presentation, attaching our explanation/hyperlinks,
        /// neutralizing overlapping biological factors, and hiding OverHaulers_CaravanMassCapacity.
        /// SSoT for foreign stat presentation binding.
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

            // BREAKPOINT ANCHOR: Authoritative Takeover Resolution
            // Profile mode obeys declarative XML directives; Unknown mod mode uses heuristic biological detection.
            bool shouldTakeOver = false;
            if (profile != null)
            {
                shouldTakeOver = profile.allowAuthoritativeTakeover;
                ActiveProfileAllowsAuthoritativeTakeover = shouldTakeOver;
                ActiveProfileSyncFormationCapacity = profile.syncFormationCapacity;
                IsAdoptedStatAdditiveBonus = false;
            }
            else
            {
                IsAdoptedStatAdditiveBonus = preferredCilAdditiveBonus;
                // In de-duplicate-only additive mode, only neutralize capacity factors when explicitly configured by the user.
                shouldTakeOver = preferredCilAdditiveBonus
                    ? (customCilNeutralizedFactors != null && HasMatchingActiveFactor(foreignStat, customCilNeutralizedFactors))
                    : (customCilNeutralizedFactors != null
                        ? HasMatchingActiveFactor(foreignStat, customCilNeutralizedFactors)
                        : DetectOverlappingBiologicalFactors(foreignStat));
                ActiveProfileAllowsAuthoritativeTakeover = shouldTakeOver;
                ActiveProfileSyncFormationCapacity = true;
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

            // Establish primary foreign stat adoption state
            ActiveMassCapacityStat = foreignStat;
            ActiveStatOwner = !string.IsNullOrEmpty(owner) ? owner : foreignStat.defName;
            IsForeignStatAdopted = true;
            ActiveAdoptionSource = profile != null ? StatAdoptionSource.XmlProfile : StatAdoptionSource.CilDiscovered;
            activeProfileDef = profile;

            // 1. Emit primary adoption bullet first: "Stat Integration: Adopted ..."
            IntegrationLogEvents.StatPresentationBound(ActiveStatOwner, foreignStat.defName, localizedSuffix);

            // 2. Consequential Action: Execute Authoritative Takeover and factor neutralization second
            if (shouldTakeOver)
            {
                NeutralizeForeignCapacityFactors(foreignStat, profile);
                IsAuthoritativeTakeoverActive = true;
            }
            else
            {
                IsAuthoritativeTakeoverActive = false;
            }
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
        /// and restores any original capacity factors that were neutralized during adoption.
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
                if (cachedRemovedCapacityFactors != null && cachedRemovedCapacityFactors.Count > 0)
                {
                    if (ActiveMassCapacityStat.capacityFactors == null)
                    {
                        ActiveMassCapacityStat.capacityFactors = new List<PawnCapacityFactor>();
                    }
                    ActiveMassCapacityStat.capacityFactors.AddRange(cachedRemovedCapacityFactors);
                    cachedRemovedCapacityFactors.Clear();
                }
            }

            activeProfileDef = null;
            IsAdoptedStatAdditiveBonus = false;
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
            customCilNeutralizedFactors = null;
            preferredCilAdditiveBonus = false;
            PreferredPresentationKeyReset?.Invoke(PresentationKeyAuto);
        }

        #endregion

        #region 3. DECLARATIVE XML PROFILE RESOLUTION

        /// <summary>
        /// Attempts to resolve the highest priority active XML profile based on available <see cref="MassCapacityStatProfileDef"/> definitions.
        /// </summary>
        /// <param name="profile">Outputs the highest-priority active profile definition, or null if none is active.</param>
        /// <param name="stat">Outputs the resolved StatDef associated with the profile, or null if none is active.</param>
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
        /// Attempts to resolve an active XML profile by its <c>defName</c>.
        /// </summary>
        /// <param name="defName">The name of the profile definition to resolve.</param>
        /// <param name="profile">Outputs the matching active profile definition, or null.</param>
        /// <param name="stat">Outputs the resolved StatDef associated with the profile, or null.</param>
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
        /// Attempts to resolve a CIL-locked stat key ("CIL:Owner:StatDefName"), tolerating colons inside third-party Harmony owner IDs.
        /// </summary>
        /// <param name="key">The CIL-locked stat key to resolve. Format: "CIL:Owner:StatDefName".</param>
        /// <param name="owner">Outputs the parsed owner name from the key, or null if resolution fails.</param>
        /// <param name="stat">Outputs the resolved StatDef instance, or null if resolution fails.</param>
        /// <returns>True if a valid CIL-locked stat is found and resolved; otherwise, false.</returns>
        private static bool TryResolveCilLockedStat(string key, out string owner, out StatDef stat)
        {
            owner = null;
            stat = null;
            if (string.IsNullOrEmpty(key) || !key.StartsWith(PresentationKeyCilPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string remainder = key.Substring(PresentationKeyCilPrefix.Length);
            int lastColon = remainder.LastIndexOf(':');
            if (lastColon > 0 && lastColon + 1 < remainder.Length)
            {
                owner = remainder.Substring(0, lastColon);
                string statDefName = remainder.Substring(lastColon + 1);
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

            ProcessPatchList(patchInfo.Transpilers, "Transpiler", map);
            ProcessPatchList(patchInfo.Prefixes, "Prefix", map);
            ProcessPatchList(patchInfo.Postfixes, "Postfix", map);

            foreach (var kvp in map)
            {
                DiscoveredPatches.Add(kvp.Value);
            }

            HasScannedThisSession = true;
        }

        /// <summary>
        /// Processes a list of patches of a specific type (Transpiler, Prefix, Postfix) and merges discovered candidate stats per patch owner.
        /// </summary>
        /// <param name="patches">The list of patches to process.</param>
        /// <param name="typeName">The type of the patches (Transpiler, Prefix, Postfix).</param>
        /// <param name="map">The map of discovered patch information to update.</param>
        private static void ProcessPatchList(IReadOnlyCollection<Patch> patches, string typeName, Dictionary<string, DiscoveredPatchInfo> map)
        {
            if (patches == null) return;

            foreach (var p in patches)
            {
                // Skip patches that have no owner or belong to the current mod itself.
                if (string.IsNullOrEmpty(p.owner) || p.owner == "com.overhaulers.mod") continue;

                // Create owner record from highest-priority patch type first, then merge candidates across all patches for that owner.
                if (!map.TryGetValue(p.owner, out var info))
                {
                    info = new DiscoveredPatchInfo
                    {
                        Owner = p.owner,
                        PrimaryType = typeName,
                        CandidateStats = new List<StatDef>(),
                        AllAssemblyStats = new List<StatDef>()
                    };
                }

                CollectCandidateStatsForPatch(p, info.CandidateStats, info.AllAssemblyStats);
                map[p.owner] = info;
            }
        }

        /// <summary>
        /// Collects and sorts candidate StatDef instances for a patch by inspecting its method CIL and declaring assembly.
        /// </summary>
        /// <param name="patch">The patch for which to collect candidate stats.</param>
        /// <param name="candidates">Accumulates deduplicated candidate stats ordered by relevance.</param>
        /// <param name="allAssemblyStats">Accumulates all StatDefs owned by the patch's mod assembly.</param>
        private static void CollectCandidateStatsForPatch(Patch patch, List<StatDef> candidates, List<StatDef> allAssemblyStats)
        {
            if (patch?.PatchMethod == null) return;

            InspectMethodCilForStats(patch.PatchMethod, candidates);

            // Inspect the assembly containing the patch method for additional StatDef instances.
            Assembly patchAssembly = patch.PatchMethod.DeclaringType?.Assembly;
            if (patchAssembly != null)
            {
                InspectAssemblyForStats(patchAssembly, candidates, allAssemblyStats);
            }

            candidates.Sort((a, b) => GetStatRelevanceScore(b).CompareTo(GetStatRelevanceScore(a)));
            allAssemblyStats.Sort((a, b) => string.Compare(a.defName, b.defName, StringComparison.OrdinalIgnoreCase));
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
            if (lowerDef.Contains("caravan")) score += 15;
            if (lowerDef.Contains("mass")) score += 15;
            if (lowerDef.Contains("weight")) score += 10;
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

        #region 5. [INT-NEUT-02] ALGORITHMIC FACTOR NEUTRALIZATION & DETECTION

        /// <summary>
        /// Ascertains all valid active <see cref="PawnCapacityFactor"/> entries attached to <paramref name="stat"/>,
        /// including factors temporarily neutralized into <see cref="cachedRemovedCapacityFactors"/> when <paramref name="stat"/>
        /// is the currently adopted stat.
        /// </summary>
        public static void PopulateAscertainedCapacityFactors(StatDef stat, List<AscertainedCapacityFactor> results)
        {
            if (results == null) return;
            results.Clear();
            if (stat == null) return;

            CollectAscertainedFactorsFromList(stat.capacityFactors, results);

            if (stat == ActiveMassCapacityStat && IsForeignStatAdopted && cachedRemovedCapacityFactors != null)
            {
                CollectAscertainedFactorsFromList(cachedRemovedCapacityFactors, results);
            }

            results.Sort((a, b) =>
            {
                int overlapCmp = b.OverlapsOverHaulers.CompareTo(a.OverlapsOverHaulers);
                return overlapCmp != 0 ? overlapCmp : string.Compare(a.DefName, b.DefName, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void CollectAscertainedFactorsFromList(List<PawnCapacityFactor> source, List<AscertainedCapacityFactor> results)
        {
            if (source == null || source.Count == 0) return;

            for (int i = 0; i < source.Count; i++)
            {
                PawnCapacityFactor factor = source[i];
                if (factor?.capacity == null || string.IsNullOrEmpty(factor.capacity.defName)) continue;
                if (Math.Abs(factor.weight) <= 0.0001f) continue;

                bool duplicate = false;
                for (int r = 0; r < results.Count; r++)
                {
                    if (results[r].Capacity == factor.capacity ||
                        string.Equals(results[r].DefName, factor.capacity.defName, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;

                bool overlaps = SimulatedBiologicalCapacities.Contains(factor.capacity.defName);
                results.Add(new AscertainedCapacityFactor(factor.capacity, factor.weight, overlaps));
            }
        }

        private static bool HasMatchingActiveFactor(StatDef foreignStat, List<string> targetDefNames)
        {
            if (foreignStat?.capacityFactors == null || targetDefNames == null || targetDefNames.Count == 0) return false;

            for (int i = 0; i < foreignStat.capacityFactors.Count; i++)
            {
                PawnCapacityFactor factor = foreignStat.capacityFactors[i];
                if (factor?.capacity == null || Math.Abs(factor.weight) <= 0.0001f) continue;

                if (ContainsOrdinalIgnoreCase(targetDefNames, factor.capacity.defName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsOrdinalIgnoreCase(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Evaluates whether a foreign StatDef contains active capacity factors that overlap with Over Haulers' kinetic simulation.
        /// SSoT for biological factor overlap detection.
        /// </summary>
        /// <param name="foreignStat">The foreign StatDef being evaluated.</param>
        /// <returns>True if at least one active simulated biological capacity factor is attached; otherwise, false.</returns>
        private static bool DetectOverlappingBiologicalFactors(StatDef foreignStat)
        {
            if (foreignStat?.capacityFactors == null || foreignStat.capacityFactors.Count == 0) return false;

            for (int i = 0; i < foreignStat.capacityFactors.Count; i++)
            {
                PawnCapacityFactor factor = foreignStat.capacityFactors[i];
                if (factor?.capacity == null || Math.Abs(factor.weight) <= 0.0001f) continue;

                if (SimulatedBiologicalCapacities.Contains(factor.capacity.defName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Surgically removes and buffers conflicting active capacity factors from an adopted StatDef.
        /// Supports explicit profile lists, user CIL factor overrides, and heuristic biological detection for unknown mods.
        /// SSoT for capacity factor neutralization.
        /// </summary>
        /// <param name="foreignStat">The foreign StatDef undergoing authoritative takeover.</param>
        /// <param name="profile">The declarative XML profile, or null if adopting an unlisted third-party mod.</param>
        private static void NeutralizeForeignCapacityFactors(StatDef foreignStat, MassCapacityStatProfileDef profile)
        {
            if (foreignStat?.capacityFactors == null || foreignStat.capacityFactors.Count == 0) return;

            List<string> explicitList = null;
            if (profile != null)
            {
                if (profile.neutraliseCapacityFactors != null && profile.neutraliseCapacityFactors.Count > 0)
                {
                    explicitList = profile.neutraliseCapacityFactors;
                }
            }
            else if (customCilNeutralizedFactors != null)
            {
                explicitList = customCilNeutralizedFactors;
            }

            if (cachedRemovedCapacityFactors == null)
            {
                cachedRemovedCapacityFactors = new List<PawnCapacityFactor>();
            }

            for (int i = foreignStat.capacityFactors.Count - 1; i >= 0; i--)
            {
                PawnCapacityFactor factor = foreignStat.capacityFactors[i];
                if (factor?.capacity == null || Math.Abs(factor.weight) <= 0.0001f) continue;

                string capName = factor.capacity.defName;
                bool shouldNeutralize = explicitList != null
                    ? ContainsOrdinalIgnoreCase(explicitList, capName)
                    : SimulatedBiologicalCapacities.Contains(capName);

                if (shouldNeutralize)
                {
                    cachedRemovedCapacityFactors.Add(factor);
                    foreignStat.capacityFactors.RemoveAt(i);
                    IntegrationLogEvents.CapacityFactorNeutralized(capName, foreignStat.defName);
                }
            }
        }

        #endregion
    }

    #endregion
}