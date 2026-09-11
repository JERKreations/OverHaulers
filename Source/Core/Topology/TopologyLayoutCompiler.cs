using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;

namespace OverHaulers
{
    #region 1. TOPOLOGICAL ENUMS & TAXONOMY

    public enum TorsoRole
    {
        Peripheral = 0,
        AnchorBridge = 1,
        AxialColumn = 2
    }

    #endregion

    /// <summary>
    /// [TOPO-01 to TOPO-03] TOPOLOGY LAYOUT COMPILER ORCHESTRATOR
    /// Thread-safe layout compiler orchestrating modular sub-compilers to pre-compute species <see cref="BodyDef"/> trees
    /// into flat, immutable Array-of-Structs (AoS) parallel vectors.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TopologyLayoutCompiler
    {
        #region 2. STATIC DEF POINTERS & CACHED TAGS

        public static readonly HashSet<BodyPartTagDef> armTags = new HashSet<BodyPartTagDef>();
        public static readonly HashSet<BodyPartTagDef> legTags = new HashSet<BodyPartTagDef>();
        public static readonly HashSet<BodyPartGroupDef> limbGroups = new HashSet<BodyPartGroupDef>();
        
        public static readonly BodyPartGroupDef TorsoGroup;
        public static readonly BodyPartGroupDef UpperTorsoGroup;
        public static readonly BodyPartTagDef spineTag;

        public static readonly BodyPartGroupDef HeadAttackToolGroup;
        public static readonly BodyPartGroupDef FullHeadGroup;
        public static readonly BodyPartGroupDef UpperHeadGroup;

        public static readonly BodyPartTagDef ConsciousnessSourceTag;
        public static readonly BodyPartTagDef SightSourceTag;
        public static readonly BodyPartTagDef HearingSourceTag;
        public static readonly BodyPartTagDef BloodPumpingSourceTag;
        public static readonly BodyPartTagDef BreathingSourceTag;
        public static readonly BodyPartTagDef BloodFiltrationKidneyTag;
        public static readonly BodyPartTagDef BloodFiltrationLiverTag;
        public static readonly BodyPartTagDef BloodFiltrationSourceTag;
        public static readonly BodyPartTagDef MetabolismSourceTag;
        public static readonly BodyPartTagDef DigestionSourceTag;

        static TopologyLayoutCompiler()
        {
            // Initialize static def pointers and cached tags.
            BodyPartTagDef manipCore = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("ManipulationLimbCore");
            BodyPartTagDef manipSeg = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("ManipulationLimbSegment");
            BodyPartTagDef manipDigit = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("ManipulationLimbDigit");
            if (manipCore != null) armTags.Add(manipCore);
            if (manipSeg != null) armTags.Add(manipSeg);
            if (manipDigit != null) armTags.Add(manipDigit);

            // Initialize moving limb tags.
            BodyPartTagDef moveCore = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("MovingLimbCore");
            BodyPartTagDef moveSeg = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("MovingLimbSegment");
            BodyPartTagDef moveDigit = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("MovingLimbDigit");
            if (moveCore != null) legTags.Add(moveCore);
            if (moveSeg != null) legTags.Add(moveSeg);
            if (moveDigit != null) legTags.Add(moveDigit);

            // Initialize limb group definitions.
            BodyPartGroupDef shoulders = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Shoulders");
            BodyPartGroupDef arms = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Arms");
            BodyPartGroupDef hands = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Hands");
            BodyPartGroupDef legs = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Legs");
            BodyPartGroupDef feet = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Feet");
            if (shoulders != null) limbGroups.Add(shoulders);
            if (arms != null) limbGroups.Add(arms);
            if (hands != null) limbGroups.Add(hands);
            if (legs != null) limbGroups.Add(legs);
            if (feet != null) limbGroups.Add(feet);

            // Initialize torso and head groups.
            TorsoGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Torso");
            UpperTorsoGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("UpperTorso");

            // Initialize spine tag.
            spineTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("Spine");

            // Initialize head-related groups.
            HeadAttackToolGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("HeadAttackTool");
            FullHeadGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("FullHead");
            UpperHeadGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("UpperHead");

            // Initialize vital source tags.
            ConsciousnessSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("ConsciousnessSource");
            SightSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("SightSource");
            HearingSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("HearingSource");
            BloodPumpingSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("BloodPumpingSource");
            BreathingSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("BreathingSource");
            BloodFiltrationKidneyTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("BloodFiltrationKidney");
            BloodFiltrationLiverTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("BloodFiltrationLiver");
            BloodFiltrationSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("BloodFiltrationSource");
            MetabolismSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("MetabolismSource");
            DigestionSourceTag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail("DigestionSource");
        }

        #endregion

        #region 3. COMPILATION LIFECYCLE & CACHE MANAGEMENT

        // Cache for compiled species topology templates.
        private static readonly ConcurrentDictionary<BodyDef, Lazy<SpeciesTopologyTemplate>> speciesTopologyCache = 
            new ConcurrentDictionary<BodyDef, Lazy<SpeciesTopologyTemplate>>();

        private static bool isCompiled = false;
        private static readonly object compilationLock = new object();

        public static int globalMaxPartCount = 0;
        public static string globalMaxPartBodyDefName = "None";
        public static int globalMaxSkeletalDepth = 0;

        /// <summary>
        /// Ensures that all loaded species BodyDefs are compiled into flat, immutable templates.
        /// Thread-safe and re-entrant: if already compiled, returns immediately.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (isCompiled) return;

            lock (compilationLock)
            {
                if (isCompiled) return;

                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    CompileAll();
                    watch.Stop();
                    
                    int totalSpecies = DefDatabase<BodyDef>.DefCount;

                    OHLog.Topology.CompilationCompleted(
                        (float)watch.Elapsed.TotalMilliseconds,
                        totalSpecies,
                        globalMaxPartCount, 
                        globalMaxPartBodyDefName, 
                        globalMaxSkeletalDepth
                    );
                }
                catch (Exception ex)
                {
                    OHLog.Topology.Warn("EnsureInitialized", ex, "An error occurred during topology compilation.");
                }
                finally
                {
                    isCompiled = true;
                }
            }
        }

        /// <summary>
        /// Compiles topology templates for all loaded species BodyDefs.
        /// </summary>
        public static void CompileAll()
        {
            List<BodyDef> allBodies = DefDatabase<BodyDef>.AllDefsListForReading;
            if (allBodies == null) return;

            for (int i = 0; i < allBodies.Count; i++)
            {
                BodyDef body = allBodies[i];
                if (body != null)
                {
                    GetOrCreateTopologyTemplate(body);
                }
            }
        }

        /// <summary>
        /// Resolves or compiles the layout template for a given BodyDef.
        /// Verifies reference integrity in real-time to catch runtime mutations from genetics or alien mods.
        /// </summary>
        /// <param name="bodyDef">The target species BodyDef to resolve or compile.</param>
        /// <returns>The compiled species topology template for the given BodyDef.</returns>
        public static SpeciesTopologyTemplate GetOrCreateTopologyTemplate(BodyDef bodyDef)
        {
            if (bodyDef == null) return null;

            var lazyTemplate = speciesTopologyCache.GetOrAdd(bodyDef, def => 
                new Lazy<SpeciesTopologyTemplate>(
                    () => BuildSpeciesTopologyTemplate(def), 
                    System.Threading.LazyThreadSafetyMode.ExecutionAndPublication
                )
            );

            SpeciesTopologyTemplate template = lazyTemplate.Value;

            // Real-time mutation guard: if a mod altered bodyDef.AllParts at runtime, re-compile dynamically
            if (!VerifyTemplateReferenceIntegrity(template, bodyDef))
            {
                OHLog.Topology.Warn("GetOrCreateTopologyTemplate", null, $"Recompiling topology template for {bodyDef.defName} due to reference desync.");

                SpeciesTopologyTemplate freshTemplate = BuildSpeciesTopologyTemplate(bodyDef);
                speciesTopologyCache[bodyDef] = new Lazy<SpeciesTopologyTemplate>(() => freshTemplate);
                return freshTemplate;
            }

            return template;
        }

        /// <summary>
        /// Verifies that the given species topology template matches the current state of the BodyDef.
        /// </summary>
        /// <param name="template">The species topology template to verify.</param>
        /// <param name="bodyDef">The target species BodyDef to check against.</param>
        /// <returns>True if the template is consistent with the BodyDef; otherwise, false.</returns>
        private static bool VerifyTemplateReferenceIntegrity(SpeciesTopologyTemplate template, BodyDef bodyDef)
        {
            if (template == null || bodyDef == null) return false;
            if (template.PartCount != bodyDef.AllParts.Count) return false;
            if (template.CorePart != bodyDef.corePart) return false;

            if (template.PartCount > 0)
            {
                if (template.IndexedParts[0] != bodyDef.AllParts[0]) return false;
                if (template.IndexedParts[template.PartCount - 1] != bodyDef.AllParts[template.PartCount - 1]) return false;
            }

            for (int i = 0; i < template.PartCount; i++)
            {
                if (template.IndexedParts[i] != bodyDef.AllParts[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Invalidates a specific species topology template when dynamic surgery or alien mutation alters its skeleton.
        /// </summary>
        /// <param name="bodyDef">The target species BodyDef whose topology should be invalidated.</param>
        /// <returns>True if the topology was successfully invalidated; otherwise, false.</returns>
        public static bool InvalidateTopology(BodyDef bodyDef)
        {
            if (bodyDef == null) return false;

            if (speciesTopologyCache.TryRemove(bodyDef, out _))
            {
                OHLog.Topology.TopologyInvalidated(bodyDef.defName ?? "Unknown");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Completely flushes all compiled layout templates and unlocks the compiler lifecycle.
        /// </summary>
        public static void InvalidateAllTopologies()
        {
            lock (compilationLock)
            {
                speciesTopologyCache.Clear();
                globalMaxPartCount = 0;
                globalMaxPartBodyDefName = "None";
                globalMaxSkeletalDepth = 0;
                isCompiled = false; // UN-LOCK: Allows EnsureInitialized() to run cleanly again
            }
        }

        /// <summary>
        /// Flushes all cached topology templates, resets global part metrics, and un-locks the compilation lifecycle.
        /// </summary>
        public static void ClearStaticCaches()
        {
            InvalidateAllTopologies();
        }

        #endregion

        #region 4. REGIONAL BUDGET QUERY ENDPOINTS

        /// <summary>
        /// Retrieves the weight budget for a specific body part within a given species topology template.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <param name="bodyDef">The target species BodyDef containing the part.</param>
        /// <param name="context">The systemic evaluation context providing relevant factors.</param>
        /// <param name="budget">The calculated weight budget for the part.</param>
        /// <returns>True if the budget was successfully retrieved; otherwise, false.</returns>
        public static bool TryGetWeightBudget(
            BodyPartRecord part, 
            BodyDef bodyDef, 
            in SystemicEvaluationContext context, 
            out float budget)
        {
            budget = 0f;
            if (bodyDef == null || part == null) return false;

            SpeciesTopologyTemplate template = GetOrCreateTopologyTemplate(bodyDef);
            if (template == null) return false;

            int index = template.GetPartIndex(part);
            if (index == -1) return false;

            budget = context.TotalMassImpactWeight * template.StaticWeightFactors[index];
            return true;
        }

        #endregion

        #region 5. ANATOMICAL PREDICATES

        /// <summary>
        /// Determines whether the specified body part belongs to the torso group.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <returns>True if the part belongs to the torso group; otherwise, false.</returns>
        public static bool IsTorsoGroupPart(BodyPartRecord part)
        {
            if (part?.groups == null || part.groups.Count == 0) return false;
            for (int i = 0; i < part.groups.Count; i++)
            {
                var g = part.groups[i];
                if (g != null && (g == TorsoGroup || g == UpperTorsoGroup))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether the specified body part belongs to the limb group.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <returns>True if the part belongs to the limb group; otherwise, false.</returns>
        public static bool IsLimbGroupPart(BodyPartRecord part)
        {
            if (part?.groups == null || part.groups.Count == 0) return false;
            for (int i = 0; i < part.groups.Count; i++)
            {
                if (limbGroups.Contains(part.groups[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Determines whether the specified body part definition represents a trunk segment.
        /// </summary>
        /// <param name="def">The body part definition to evaluate.</param>
        /// <returns>True if the definition represents a trunk segment; otherwise, false.</returns>
        public static bool IsTrunkDef(BodyPartDef def)
        {
            if (def == null || spineTag == null || def.tags == null) return false;
            return def.tags.Contains(spineTag);
        }

        /// <summary>
        /// Determines whether the specified body part definition has any of the given tags.
        /// </summary>
        /// <param name="def">The body part definition to evaluate.</param>
        /// <param name="tags">The set of tags to check against.</param>
        /// <returns>True if the definition has any of the specified tags; otherwise, false.</returns>
        public static bool HasAnyTag(BodyPartDef def, HashSet<BodyPartTagDef> tags)
        {
            if (def?.tags == null || tags == null) return false;
            for (int i = 0; i < def.tags.Count; i++)
            {
                if (tags.Contains(def.tags[i])) return true;
            }
            return false;
        }

        #endregion

        #region 6. ORCHESTRATION PIPELINE

        /// <summary>
        /// Builds a species topology template based on the specified body definition.
        /// </summary>
        /// <param name="bodyDef">The body definition to use for template construction.</param>
        /// <returns>A species topology template representing the body structure.</returns>
        private static SpeciesTopologyTemplate BuildSpeciesTopologyTemplate(BodyDef bodyDef)
        {
            SpeciesTopologyTemplate template = new SpeciesTopologyTemplate(bodyDef);

            // PASS 1: Skeletal Depth Traversal & Cranial Sub-Tree Ingress
            Pass01_CranialSubCompiler.CompileCranialAncestry(
                bodyDef, template, out int[] parentIndices, out bool[] headAncestry, out int maxDepth);

            int currentMaxDepth;
            do
            {
                currentMaxDepth = globalMaxSkeletalDepth;
                if (maxDepth <= currentMaxDepth) break;
            } 
            while (System.Threading.Interlocked.CompareExchange(
                ref globalMaxSkeletalDepth, 
                maxDepth, 
                currentMaxDepth) != currentMaxDepth);

            // PASS 2: Bidirectional Limb Classification & Root Discovery
            PartTopologyInfo[] tempPartTopologies = Pass02_AppendageSubCompiler.CompilePartClassifications(
                bodyDef, template, parentIndices, headAncestry, out PartType[] resolvedTypes, out PartCounts counts);
            template.Counts = counts;

            // PASS 3: Axial Torso Roles & HitPoint Density Power Curves
            Pass03_AxialTrunkSubCompiler.CompileTorsoRolesAndWeights(
                bodyDef, template, resolvedTypes, tempPartTopologies, out float[] torsoRawWeights, out float torsoWeightSum);

            // PASS 4: Geometric Limb Depth Decay & 3-Channel Weight Normalization
            Pass02_AppendageSubCompiler.CompileLimbAndFinalWeights(
                bodyDef, template, counts, tempPartTopologies, torsoRawWeights, torsoWeightSum);

            // PASS 5: Two-Pass Canonical Topological Sort & 1D Jump Stride Matrices
            Pass05_CanonicalSortCompiler.CompileTopologyIndices(
                bodyDef, template, resolvedTypes, parentIndices);

            return template;
        }

        #endregion
    }
}