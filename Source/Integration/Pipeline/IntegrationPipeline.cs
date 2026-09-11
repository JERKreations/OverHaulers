using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-02] CENTRAL PIPELINE ORCHESTRATOR

    /// <summary>
    /// Represents information about a discovered patch within the integration pipeline.
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
    /// Orchestrates the integration pipeline, managing driver registration, initialization, and patch discovery.
    /// </summary>
    public static class IntegrationPipeline
    {
        #region 1. CONSTANTS & ACTIVE DRIVER BINDING

        public const string DriverKeyAuto = "AUTO";
        public const string DriverKeyStandalone = "STANDALONE";
        public const string DriverKeyXmlPrefix = "XML:";
        public const string DriverKeyCSharpPrefix = "CSHARP:";
        public const string DriverKeyCilPrefix = "CIL:";

        public static IPipelineDriver ActiveDriver { get; private set; }
        public static bool HasScannedThisSession { get; private set; } = false;

        private static readonly List<IPipelineDriver> customDrivers = new List<IPipelineDriver>();
        public static List<DiscoveredPatchInfo> DiscoveredPatches { get; } = new List<DiscoveredPatchInfo>();

        private static bool isInitialized = false;
        private static readonly string[] StatKeywords = { "mass", "weight", "capacity", "caravan", "carry", "load", "pack" };

        #endregion

        #region 2. DRIVER REGISTRATION & RE-BINDING

        /// <summary>
        /// Registers a custom pipeline driver with the integration pipeline. If the driver is already registered, it will not be added again.
        /// </summary>
        /// <param name="driver">The custom pipeline driver to register.</param>
        public static void RegisterDriver(IPipelineDriver driver)
        {
            if (driver == null) return;

            if (!customDrivers.Contains(driver))
            {
                customDrivers.Add(driver);
                OHLog.Integration.CustomDriverRegistered(driver.DriverIdentifier);
            }
        }

        /// <summary>
        /// Rebinds the active pipeline driver, cleaning up the current driver and initializing the appropriate one based on the current settings.
        /// </summary>
        public static void RebindDriver()
        {
            if (HarmonySetup.HarmonyInstance == null) return;

            ActiveDriver?.Cleanup();
            ActiveDriver = null;
            isInitialized = false;

            Initialize(HarmonySetup.HarmonyInstance);
            PawnDataRegistry.ClearAllCaches();
            TestBench.MarkDirty();
        }

        /// <summary>
        /// Initializes the integration pipeline with the specified Harmony instance. This sets up the active driver based on the current settings
        ///  and prepares the pipeline for operation.
        /// </summary>
        /// <param name="harmony">The Harmony instance to use for patching.</param>
        public static void Initialize(Harmony harmony)
        {
            if (isInitialized) return;

            string localizedSuffix = " " + "kg".Translate();
            Settings settings = OverHaulers.settings;
            string key = settings?.selectedDriverKey ?? DriverKeyAuto;

            // -------------------------------------------------------------------------
            // PATH A: EXPLICIT PLAYER OVERRIDES (Highest Authority)
            // -------------------------------------------------------------------------

            // Case 1: Player Forced Standalone
            if (key == DriverKeyStandalone)
            {
                ActiveDriver = new VanillaStatDriver(localizedSuffix);
            }
            // Case 2: Player Locked an XML Driver
            else if (key.StartsWith(DriverKeyXmlPrefix))
            {
                string defName = key.Substring(DriverKeyXmlPrefix.Length);
                if (TryResolveXmlDriverByDefName(defName, localizedSuffix, out IPipelineDriver xmlDriver))
                {
                    ActiveDriver = xmlDriver;
                }
                else
                {
                    RecoverStaleDriverPreference(settings, key);
                    ActiveDriver = ResolveAutoDriver(settings, localizedSuffix);
                }
            }
            // Case 3: Player Locked a C# Custom Driver
            else if (key.StartsWith(DriverKeyCSharpPrefix))
            {
                string identifier = key.Substring(DriverKeyCSharpPrefix.Length);
                IPipelineDriver custom = customDrivers.Find(d => d.DriverIdentifier == identifier);
                if (custom != null)
                {
                    ActiveDriver = custom;
                }
                else
                {
                    RecoverStaleDriverPreference(settings, key);
                    ActiveDriver = ResolveAutoDriver(settings, localizedSuffix);
                }
            }
            // Case 4: Player Locked a CIL Discovered Stat
            else if (key.StartsWith(DriverKeyCilPrefix))
            {
                if (TryResolveCilLockedDriver(key, localizedSuffix, out IPipelineDriver cilDriver))
                {
                    ActiveDriver = cilDriver;
                }
                else
                {
                    RecoverStaleDriverPreference(settings, key);
                    ActiveDriver = ResolveAutoDriver(settings, localizedSuffix);
                }
            }
            // -------------------------------------------------------------------------
            // PATH B: AUTOMATIC RESOLUTION (Recommended Default)
            // -------------------------------------------------------------------------
            else
            {
                ActiveDriver = ResolveAutoDriver(settings, localizedSuffix);
            }

            ActiveDriver.Initialize(harmony);
            isInitialized = true;
        }

        /// <summary>
        /// Resolves the appropriate pipeline driver automatically based on the available custom drivers, XML driver definitions, discovered
        ///  CIL patches, and vanilla defaults.
        /// </summary>
        /// <param name="settings">The current settings object containing user preferences and configuration.</param>
        /// <param name="localizedSuffix">The localized suffix used for driver resolution.</param>
        /// <returns>The resolved pipeline driver instance based on the automatic resolution logic.</returns>
        private static IPipelineDriver ResolveAutoDriver(Settings settings, string localizedSuffix)
        {
            // Tier 1: Explicit C# Registration
            if (customDrivers.Count > 0)
            {
                return customDrivers[0];
            }

            // Tier 2: Declarative XML Driver Defs (High-Speed O(1) Matching)
            if (TryResolveHighestPriorityXmlDriver(localizedSuffix, out IPipelineDriver xmlDriver))
            {
                return xmlDriver;
            }

            // Tier 3: Dynamic Heuristic CIL Discovery (Fallback for unlisted mods)
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

                // Tier 3a: If a valid target stat is found within the discovered patch, create a generic stat driver for it.
                if (chosenStat != null)
                {
                    string owner = !string.IsNullOrEmpty(selectedPatch.Owner) ? selectedPatch.Owner : chosenStat.defName;
                    return new GenericStatDriver(owner, chosenStat, localizedSuffix);
                }
            }

            // Tier 4: Pure Vanilla Standalone Default
            return new VanillaStatDriver(localizedSuffix);
        }

        /// <summary>
        /// Recovers from a stale driver preference by reverting to the automatic driver selection if the previously selected driver is no longer active.
        /// </summary>
        /// <param name="settings">The current settings object containing user preferences and configuration.</param>
        /// <param name="staleKey">The key representing the previously selected driver that is now considered stale.</param>
        private static void RecoverStaleDriverPreference(Settings settings, string staleKey)
        {
            Log.Message($"[Over Haulers] Saved driver selection '{staleKey}' is no longer active (mod uninstalled?). Reverting to Automatic selection.");
            if (settings != null)
            {
                settings.selectedDriverKey = DriverKeyAuto;
            }
        }

        #endregion

        #region 3. XML DRIVER RESOLVER HELPERS

        /// <summary>
        /// Attempts to resolve the highest priority XML driver based on the available XML driver definitions.
        /// </summary>
        /// <param name="localizedSuffix">The localized suffix used for driver resolution.</param>
        /// <param name="driver">The resolved pipeline driver if successful; otherwise, null.</param>
        /// <returns>True if a valid XML driver is found and resolved; otherwise, false.</returns>
        /// <remarks>
        /// This method iterates through all available XML driver definitions, sorts them by priority, and attempts to instantiate the highest
        ///  priority valid driver.
        /// </remarks>
        public static bool TryResolveHighestPriorityXmlDriver(string localizedSuffix, out IPipelineDriver driver)
        {
            driver = null;
            List<MassCapacityDriverDef> allXmlDrivers = DefDatabase<MassCapacityDriverDef>.AllDefsListForReading;
            if (allXmlDrivers == null || allXmlDrivers.Count == 0) return false;

            List<MassCapacityDriverDef> sortedDrivers = new List<MassCapacityDriverDef>(allXmlDrivers);
            sortedDrivers.Sort((a, b) => b.priority.CompareTo(a.priority));

            // Iterate through the sorted drivers and attempt to resolve the highest priority valid driver.
            for (int i = 0; i < sortedDrivers.Count; i++)
            {
                if (sortedDrivers[i].IsValidAndActive(out StatDef stat))
                {
                    driver = InstantiateXmlDriver(sortedDrivers[i], stat, localizedSuffix);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve an XML driver based on the specified driver definition name.
        /// </summary>
        /// <param name="defName">The name of the driver definition to resolve.</param>
        /// <param name="localizedSuffix">The localized suffix used for driver resolution.</param>
        /// <param name="driver">The resolved pipeline driver if successful; otherwise, null.</param>
        /// <returns>True if a valid XML driver is found and resolved; otherwise, false.</returns>
        public static bool TryResolveXmlDriverByDefName(string defName, string localizedSuffix, out IPipelineDriver driver)
        {
            driver = null;
            MassCapacityDriverDef def = DefDatabase<MassCapacityDriverDef>.GetNamedSilentFail(defName);
            if (def != null && def.IsValidAndActive(out StatDef stat))
            {
                driver = InstantiateXmlDriver(def, stat, localizedSuffix);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Instantiates an XML driver based on the specified driver definition, stat, and localized suffix.
        /// </summary>
        /// <param name="def">The driver definition used for instantiation.</param>
        /// <param name="stat">The stat associated with the driver.</param>
        /// <param name="localizedSuffix">The localized suffix used for driver resolution.</param>
        /// <returns>The instantiated pipeline driver.</returns>
        private static IPipelineDriver InstantiateXmlDriver(MassCapacityDriverDef def, StatDef stat, string localizedSuffix)
        {
            string identifier = def.label ?? def.defName;
            if (def.driverClass != null && typeof(IPipelineDriver).IsAssignableFrom(def.driverClass))
            {
                return (IPipelineDriver)Activator.CreateInstance(def.driverClass, identifier, stat, localizedSuffix);
            }
            return new GenericStatDriver(identifier, stat, localizedSuffix);
        }

        /// <summary>
        /// Attempts to resolve a CIL-locked driver based on the specified key and localized suffix.
        /// </summary>
        /// <param name="key">The key used to identify the CIL-locked driver. Format: "CIL:Owner:StatDefName".</param>
        /// <param name="localizedSuffix">The localized suffix used for driver resolution.</param>
        /// <param name="driver">The resolved pipeline driver if successful; otherwise, null.</param>
        /// <returns>True if a valid CIL-locked driver is found and resolved; otherwise, false.</returns>
        private static bool TryResolveCilLockedDriver(string key, string localizedSuffix, out IPipelineDriver driver)
        {
            driver = null;
            // Format: "CIL:Owner:StatDefName"
            string[] parts = key.Split(':');
            if (parts.Length == 3)
            {
                string owner = parts[1];
                string statDefName = parts[2];
                StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
                if (stat != null)
                {
                    driver = new GenericStatDriver(owner, stat, localizedSuffix);
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region 4. ON-DEMAND / LAZY CIL DISCOVERY ENGINE

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
        /// Inspects and processes external patches for the current session.
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
        /// Resolves the candidate StatDef instances for a given patch by inspecting its method and assembly.
        /// </summary>
        /// <param name="patch">The patch for which to resolve candidate stats.</param>
        /// <param name="targetMethod">The method that the patch is applied to.</param>
        /// <param name="allAssemblyStats">The list of all StatDef instances found in the patch's assembly.</param>
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
                            try {
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
        /// Calculates a relevance score for the given StatDef based on its characteristics. Higher scores indicate greater relevance to mass or
        ///  carrying capacity.
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