using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

        public const string DriverKeyAuto = "AUTO";
        public const string DriverKeyStandalone = "STANDALONE";
        public const string DriverKeyXmlPrefix = "XML:";
        public const string DriverKeyCSharpPrefix = "CSHARP:";
        public const string DriverKeyCilPrefix = "CIL:";

        public static StatDef ActiveMassCapacityStat { get; private set; }
        public static bool IsForeignStatAdopted { get; private set; }
        public static string ActiveStatOwner { get; private set; } = "Native";
        public static bool IsInitialized => isInitialized;
        public static bool HasScannedThisSession { get; private set; } = false;

        public static List<DiscoveredPatchInfo> DiscoveredPatches { get; } = new List<DiscoveredPatchInfo>();

        private static bool isInitialized = false;
        private static readonly string[] StatKeywords = { "mass", "weight", "capacity", "caravan", "carry", "load", "pack" };

        #endregion

        #region 2. PRESENTATION BINDING & RE-BINDING

        /// <summary>
        /// Rebinds the active presentation stat, detaching from any previously adopted foreign stats
        /// and re-evaluating preferences and detected mods.
        /// </summary>
        public static void RebindDriver()
        {
            if (HarmonySetup.HarmonyInstance == null) return;

            CleanupAdoptedForeignStat();
            ActiveMassCapacityStat = null;
            isInitialized = false;

            Initialize(HarmonySetup.HarmonyInstance);
            PawnDataRegistry.ClearAllCaches();
            TestBench.MarkDirty();
        }

        /// <summary>
        /// Initializes stat presentation and deduplication based on user preferences and detected external mods.
        /// </summary>
        /// <param name="harmony">The Harmony instance used if additional inspection or patching is needed.</param>
        public static void Initialize(Harmony harmony)
        {
            if (isInitialized) return;

            string localizedSuffix = " " + "kg".Translate();
            Settings settings = OverHaulers.settings;
            string key = settings?.selectedDriverKey ?? SettingsDefaults.DefaultSelectedDriverKey;

            // -------------------------------------------------------------------------
            // PATH A: EXPLICIT PLAYER OVERRIDES (Highest Authority)
            // -------------------------------------------------------------------------

            // Case 1: Player Forced Native Standalone Card
            if (key == DriverKeyStandalone)
            {
                BindNativePresentation(localizedSuffix);
            }
            // Case 2: Player Locked an XML Declared Stat
            else if (key.StartsWith(DriverKeyXmlPrefix))
            {
                string defName = key.Substring(DriverKeyXmlPrefix.Length);
                if (TryResolveXmlStatByDefName(defName, out string owner, out StatDef targetStat))
                {
                    AdoptForeignStatPresentation(owner, targetStat, localizedSuffix);
                }
                else
                {
                    RecoverStaleDriverPreference(settings, key);
                    ResolveAutoPresentation(settings, localizedSuffix);
                }
            }
            // Case 3: Player Locked a CIL Discovered Stat
            else if (key.StartsWith(DriverKeyCilPrefix))
            {
                if (TryResolveCilLockedStat(key, out string owner, out StatDef targetStat))
                {
                    AdoptForeignStatPresentation(owner, targetStat, localizedSuffix);
                }
                else
                {
                    RecoverStaleDriverPreference(settings, key);
                    ResolveAutoPresentation(settings, localizedSuffix);
                }
            }
            // -------------------------------------------------------------------------
            // PATH B: AUTOMATIC STAT ADOPTION (Recommended Default)
            // -------------------------------------------------------------------------
            else
            {
                ResolveAutoPresentation(settings, localizedSuffix);
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

            StatDef nativeStat = EnsureNativeStatRegistered(localizedSuffix);
            nativeStat.showOnPawns = true;

            ActiveMassCapacityStat = nativeStat;
            ActiveStatOwner = "Native";
            IsForeignStatAdopted = false;

            OHLog.Integration.StatDrivenBound("Native", nativeStat.defName, localizedSuffix);
        }

        /// <summary>
        /// Adopts an external mod's StatDef for presentation, attaching our explanation/hyperlinks
        /// and hiding OverHaulers_CaravanMassCapacity to eliminate duplicate inspect rows.
        /// </summary>
        /// <param name="owner">The owner identifier or mod name associated with the foreign stat.</param>
        /// <param name="foreignStat">The foreign StatDef being adopted.</param>
        /// <param name="localizedSuffix">The localized suffix used for unit display.</param>
        private static void AdoptForeignStatPresentation(string owner, StatDef foreignStat, string localizedSuffix)
        {
            if (foreignStat == null)
            {
                BindNativePresentation(localizedSuffix);
                return;
            }

            CleanupAdoptedForeignStat();

            // DEDUPLICATION: Hide native stat on pawns so only the adopted foreign stat card is visible
            StatDef nativeStat = DefDatabase<StatDef>.GetNamedSilentFail("OverHaulers_CaravanMassCapacity");
            if (nativeStat != null)
            {
                nativeStat.showOnPawns = false;
            }

            // Rescue labels if the foreign stat is missing them
            if (string.IsNullOrEmpty(foreignStat.label))
            {
                foreignStat.label = "OverHaulers_StatLabel".Translate().ToString();
            }
            if (string.IsNullOrEmpty(foreignStat.description))
            {
                foreignStat.description = "OverHaulers_StatDesc".Translate().ToString();
            }

            // Inject MassCapacityStatPart for InfoCard explanation and hyperlinks
            AttachStatPart(foreignStat);

            ActiveMassCapacityStat = foreignStat;
            ActiveStatOwner = !string.IsNullOrEmpty(owner) ? owner : foreignStat.defName;
            IsForeignStatAdopted = true;

            OHLog.Integration.StatDrivenBound(ActiveStatOwner, foreignStat.defName, localizedSuffix);
        }

        /// <summary>
        /// Automatically resolves the most appropriate presentation card.
        /// Prioritizes declarative XML profiles, then CIL transpiler patches, falling back to Native.
        /// </summary>
        /// <param name="settings">The current settings object containing user preferences and configuration.</param>
        /// <param name="localizedSuffix">The localized suffix used for unit display.</param>
        private static void ResolveAutoPresentation(Settings settings, string localizedSuffix)
        {
            // Tier 1: Declarative XML Driver Defs (High-Speed O(1) Matching)
            if (TryResolveHighestPriorityXmlStat(out string xmlOwner, out StatDef xmlStat))
            {
                AdoptForeignStatPresentation(xmlOwner, xmlStat, localizedSuffix);
                return;
            }

            // Tier 2: Dynamic Heuristic CIL Discovery (Fallback for unlisted mods)
            EnsurePatchesInspected();

            if (DiscoveredPatches.Count > 0)
            {
                List<string> modOwners = new List<string>(DiscoveredPatches.Count);
                for (int i = 0; i < DiscoveredPatches.Count; i++)
                {
                    modOwners.Add(DiscoveredPatches[i].Owner);
                }
                OHLog.Integration.ExternalPatchesDetected(string.Join(", ", modOwners));

                DiscoveredPatchInfo selectedPatch = SelectTargetPatch();
                StatDef chosenStat = selectedPatch.TargetStat;

                // Tier 2a: If a valid target stat is found within the discovered patch, adopt it for presentation.
                if (chosenStat != null)
                {
                    string owner = !string.IsNullOrEmpty(selectedPatch.Owner) ? selectedPatch.Owner : chosenStat.defName;
                    AdoptForeignStatPresentation(owner, chosenStat, localizedSuffix);
                    return;
                }
            }

            // Tier 3: Pure Vanilla Standalone Default
            BindNativePresentation(localizedSuffix);
        }

        /// <summary>
        /// Ensures OverHaulers_CaravanMassCapacity exists in DefDatabase.
        /// </summary>
        /// <param name="unitSuffix">The localized unit suffix to attach to the StatDef.</param>
        /// <returns>The resolved or registered native StatDef.</returns>
        private static StatDef EnsureNativeStatRegistered(string unitSuffix)
        {
            StatDef existing = DefDatabase<StatDef>.GetNamedSilentFail("OverHaulers_CaravanMassCapacity");
            if (existing != null)
            {
                return existing;
            }

            StatDef statDef = new StatDef
            {
                defName = "OverHaulers_CaravanMassCapacity",
                label = "OverHaulers_StatLabel".Translate().ToString(),
                description = "OverHaulers_StatDesc".Translate().ToString(),
                category = DefDatabase<StatCategoryDef>.GetNamedSilentFail("BasicsPawn"),
                displayPriorityInCategory = 80,
                toStringStyle = ToStringStyle.FloatTwo,
                formatString = "{0}" + unitSuffix,
                showOnPawns = true,
                workerClass = typeof(MassCapacityStatWorker)
            };

            DefDatabase<StatDef>.Add(statDef);
            return statDef;
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
        /// Detaches MassCapacityStatPart from the currently adopted foreign stat.
        /// </summary>
        private static void CleanupAdoptedForeignStat()
        {
            if (ActiveMassCapacityStat != null && IsForeignStatAdopted)
            {
                if (ActiveMassCapacityStat.parts != null)
                {
                    ActiveMassCapacityStat.parts.RemoveAll(p => p is MassCapacityStatPart);
                }
            }
        }

        /// <summary>
        /// Recovers from a stale presentation preference by reverting to automatic selection if a previously selected mod is removed.
        /// </summary>
        /// <param name="settings">The current settings object containing user preferences and configuration.</param>
        /// <param name="staleKey">The key representing the previously selected preference that is now considered stale.</param>
        private static void RecoverStaleDriverPreference(Settings settings, string staleKey)
        {
            Log.Message($"[Over Haulers] Saved presentation selection '{staleKey}' is no longer active (mod uninstalled?). Reverting to Automatic selection.");
            if (settings != null)
            {
                settings.selectedDriverKey = SettingsDefaults.DefaultSelectedDriverKey;
            }
        }

        #endregion

        #region 3. DECLARATIVE XML PROFILE RESOLUTION

        /// <summary>
        /// Attempts to resolve the highest priority XML profile based on available MassCapacityDriverDef definitions.
        /// </summary>
        /// <param name="owner">The resolved owner name of the driver definition.</param>
        /// <param name="stat">The resolved StatDef associated with the profile.</param>
        /// <returns>True if a valid XML profile is found and resolved; otherwise, false.</returns>
        public static bool TryResolveHighestPriorityXmlStat(out string owner, out StatDef stat)
        {
            owner = null;
            stat = null;
            List<MassCapacityDriverDef> allXmlDrivers = DefDatabase<MassCapacityDriverDef>.AllDefsListForReading;
            if (allXmlDrivers == null || allXmlDrivers.Count == 0) return false;

            List<MassCapacityDriverDef> sortedDrivers = new List<MassCapacityDriverDef>(allXmlDrivers);
            sortedDrivers.Sort((a, b) => b.priority.CompareTo(a.priority));

            // Iterate through the sorted drivers and attempt to resolve the highest priority valid profile.
            for (int i = 0; i < sortedDrivers.Count; i++)
            {
                if (sortedDrivers[i].IsValidAndActive(out StatDef resolved))
                {
                    owner = sortedDrivers[i].label ?? sortedDrivers[i].defName;
                    stat = resolved;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve an XML profile by defName.
        /// </summary>
        /// <param name="defName">The name of the driver definition to resolve.</param>
        /// <param name="owner">The resolved owner name of the driver definition.</param>
        /// <param name="stat">The resolved StatDef associated with the profile.</param>
        /// <returns>True if a valid XML profile is found and resolved; otherwise, false.</returns>
        public static bool TryResolveXmlStatByDefName(string defName, out string owner, out StatDef stat)
        {
            owner = null;
            stat = null;
            MassCapacityDriverDef def = DefDatabase<MassCapacityDriverDef>.GetNamedSilentFail(defName);
            if (def != null && def.IsValidAndActive(out StatDef resolved))
            {
                owner = def.label ?? def.defName;
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
        /// Inspects the CIL instructions of the specified method to identify references to StatDef fields and adds them to the list of candidate stats.
        /// </summary>
        /// <param name="method">The method whose CIL instructions are to be inspected.</param>
        /// <param name="candidates">The list of candidate StatDef instances to populate based on the inspection.</param>
        private static void InspectMethodCilForStats(MethodInfo method, List<StatDef> candidates)
        {
            if (method == null) return;

            try
            {
                List<CodeInstruction> instructions = PatchProcessor.GetCurrentInstructions(method);
                if (instructions == null || instructions.Count == 0) return;

                for (int i = 0; i < instructions.Count; i++)
                {
                    var instr = instructions[i];
                    if (instr?.operand == null) continue;

                    // Skip any instructions that do not reference a static StatDef field, a string, or a StatDef instance directly.
                    if (instr.operand is FieldInfo fieldInfo && fieldInfo.IsStatic && typeof(StatDef).IsAssignableFrom(fieldInfo.FieldType))
                    {
                        StatDef resolvedStat = null;

                        // COMPATIBILITY SHIELD (.cctor STATIC CONSTRUCTOR LANDMINE):
                        // In RimWorld, reading fieldInfo.GetValue(null) on an external mod's [DefOf] class
                        // can prematurely trigger that class's static constructor (.cctor). If the third-party 
                        // mod's static constructor expects certain state to be initialized, it will throw a fatal 
                        // crash on boot.
                        // 
                        // Strategy A checks DefDatabase by field name first. This resolves 99% of modded DefOf fields 
                        // without ever executing the foreign class's static constructor.
                        if (!string.IsNullOrEmpty(fieldInfo.Name))
                        {
                            resolvedStat = DefDatabase<StatDef>.GetNamedSilentFail(fieldInfo.Name);
                        }
                        // Strategy B falls back to direct field access if DefDatabase lookup fails. This may trigger the static constructor.
                        if (resolvedStat == null)
                        {
                            try
                            {
                                resolvedStat = fieldInfo.GetValue(null) as StatDef;
                            }
                            catch (Exception ex)
                            {
                                string fieldName = $"{fieldInfo.DeclaringType?.Name}.{fieldInfo.Name}";
                                OHLog.Integration.Warn("StatDefFieldResolution", ex, $"Failed to resolve field {fieldName}.");
                            }
                        }

                        AddCandidateIfValid(resolvedStat, candidates);
                    }
                    // If the instruction does not match any of the expected operand types, it is ignored.
                    else if (instr.operand is string strOperand && !string.IsNullOrEmpty(strOperand))
                    {
                        AddCandidateIfValid(DefDatabase<StatDef>.GetNamedSilentFail(strOperand), candidates);
                    }
                    // If the instruction is neither a field reference, string, nor StatDef, it is ignored.
                    else if (instr.operand is StatDef statDef)
                    {
                        AddCandidateIfValid(statDef, candidates);
                    }
                }
            }
            catch (Exception ex)
            {
                string methodName = method.DeclaringType != null ? $"{method.DeclaringType.FullName}.{method.Name}" : method.Name;
                OHLog.Integration.Warn("CilMethodInspection", ex, $"Failed to inspect method {methodName}.");
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