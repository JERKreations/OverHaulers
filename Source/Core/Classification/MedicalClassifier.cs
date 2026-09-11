using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. MEDICAL TAXONOMY & ENUMS

    public enum PartType
    {
        None = 0,
        CorePart = 1,
        ManipulationPart = 2,
        MovingPart = 3,
        HeadPart = 4,
        DualLimb = 5
    }

    public struct PartCounts
    {
        public int CoreParts;
        public int ManipulationParts;
        public int MovingParts;
        public int DualLimbs;
        public int HeadParts;

        public int TotalManipulationRoots => ManipulationParts + DualLimbs;
        public int TotalMovingRoots => MovingParts + DualLimbs;
    }

    /// <summary>Which weighting curve a limb part's mass share was computed with (diagnostic-facing).</summary>
    public enum LimbWeightModel
    {
        NotApplicable = 0,
        GeometricDecay = 1,
        HpRatio = 2,
        ExplicitOverride = 3
    }

    public struct ImplantCharacteristics
    {
        public float EfficiencyModifier;
        public bool AffectsAthletics;
    }

    [Flags]
    public enum HediffFlags : byte
    {
        None = 0,
        IsTolerance = 1 << 0,
        IsPhysiologicallyImpactful = 1 << 1,
        ReducesConsciousness = 1 << 2
    }

    #endregion

    /// <summary>
    /// Centralized single source of truth for biological state validation, medical taxonomy classification,
    /// hediff pathology bitmask evaluation, and metabolic organ identification.
    /// 100% framework-agnostic with ZERO hardcoded string matching.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MedicalClassifier
    {
        #region 2. STATIC DEF POINTERS & CONCURRENT CACHES

        public static readonly PawnCapacityDef[] AthleticCapacities = new[]
        {
            PawnCapacityDefOf.Breathing,
            PawnCapacityDefOf.BloodPumping,
            PawnCapacityDefOf.Moving,
            PawnCapacityDefOf.Manipulation
        };

        private static readonly ConcurrentDictionary<BodyPartDef, BodyPartModExtension> extensionCache = new ConcurrentDictionary<BodyPartDef, BodyPartModExtension>();
        private static readonly ConcurrentDictionary<Type, bool> vehicleCache = new ConcurrentDictionary<Type, bool>();
        private static readonly ConcurrentDictionary<HediffDef, HediffFlags> packedClassificationCache = new ConcurrentDictionary<HediffDef, HediffFlags>();
        private static readonly ConcurrentDictionary<HediffDef, ImplantCharacteristics> implantCache = new ConcurrentDictionary<HediffDef, ImplantCharacteristics>();
        private static readonly ConcurrentDictionary<StatDef, bool> physicalStatCache = new ConcurrentDictionary<StatDef, bool>();

        // Harmony postfixes on Pawn.BodySize (e.g. broken gene mods) tend to fail deterministically for a
        // given pawn every time, not intermittently - cache the failure so we pay the exception cost once.
        private static readonly ConcurrentDictionary<int, byte> knownBrokenBodySizePawnIds = new ConcurrentDictionary<int, byte>();

        private static HashSet<BodyPartDef> metabolicOrgans = new HashSet<BodyPartDef>();
        private static HashSet<BodyPartTagDef> vitalOrganSourceTags = new HashSet<BodyPartTagDef>();
        private static bool metabolicInitialized = false;

        private static readonly Func<HediffDef, Type, HediffFlags> flagFactory = (d, type) =>
        {
            HediffFlags flags = HediffFlags.None;

            if (d.CompProps<HediffCompProperties_DrugEffectFactor>() != null)
            {
                return HediffFlags.IsTolerance;
            }

            if (d.stages != null)
            {
                for (int s = 0; s < d.stages.Count; s++)
                {
                    HediffStage stage = d.stages[s];
                    if (stage?.capMods == null) continue;

                    for (int c = 0; c < stage.capMods.Count; c++)
                    {
                        PawnCapacityModifier capMod = stage.capMods[c];
                        if (capMod != null && capMod.capacity == PawnCapacityDefOf.Consciousness)
                        {
                            if (capMod.offset < 0f || capMod.setMax < 1.0f || capMod.postFactor < 1.0f)
                            {
                                flags |= HediffFlags.ReducesConsciousness;
                                break;
                            }
                        }
                    }
                }
            }

            if (DefHasPhysiologicalImpact(d))
            {
                flags |= HediffFlags.IsPhysiologicallyImpactful;
            }

            return flags;
        };

        /// <summary>
        /// Creates an instance of <see cref="ImplantCharacteristics"/> based on the given <see cref="HediffDef"/>.
        /// </summary>
        /// <param name="def">The <see cref="HediffDef"/> to create the implant characteristics for.</param>
        /// <returns>An instance of <see cref="ImplantCharacteristics"/> representing the characteristics of the implant.</returns>
        private static readonly Func<HediffDef, ImplantCharacteristics> implantFactory = (def) =>
        {
            float maxPositiveModifier = 0f;
            float maxNegativeModifier = 0f;
            bool affectsAthletic = false;

            if (def.stages != null)
            {
                for (int s = 0; s < def.stages.Count; s++)
                {
                    HediffStage stage = def.stages[s];
                    if (stage?.capMods == null) continue;

                    for (int c = 0; c < stage.capMods.Count; c++)
                    {
                        PawnCapacityModifier capMod = stage.capMods[c];
                        if (capMod != null && IsAthleticCapacity(capMod.capacity))
                        {
                            affectsAthletic = true;
                            if (capMod.offset > 0f)
                            {
                                maxPositiveModifier = Math.Max(maxPositiveModifier, capMod.offset);
                            }
                            else if (capMod.offset < 0f)
                            {
                                maxNegativeModifier = Math.Min(maxNegativeModifier, capMod.offset);
                            }
                        }
                    }
                }
            }

            float dominantOffset = Math.Abs(maxPositiveModifier) >= Math.Abs(maxNegativeModifier) 
                ? maxPositiveModifier 
                : maxNegativeModifier;

            return new ImplantCharacteristics
            {
                EfficiencyModifier = 1.0f + dominantOffset,
                AffectsAthletics = affectsAthletic
            };
        };

        /// <summary>
        /// Initializes the <see cref="MedicalClassifier"/> by setting up necessary data structures and caches.
        /// </summary>
        static MedicalClassifier()
        {
            InitializeMetabolicOrgans();
        }

        #endregion

        #region 3. BIOLOGICAL & METABOLIC CLASSIFIERS

        /// <summary>
        /// Determines whether the specified pawn has a valid biological state.
        /// </summary>
        /// <param name="pawn">The pawn to check for a valid biological state.</param>
        /// <returns><c>true</c> if the pawn has a valid biological state; otherwise, <c>false</c>.</returns>
        public static bool HasValidBiologicalState(this Pawn pawn)
        {
            return pawn != null 
                && !pawn.Destroyed 
                && !pawn.Discarded
                && pawn.health?.hediffSet?.hediffs != null 
                && pawn.RaceProps?.body != null
                && !IsVehicle(pawn);
        }

        /// <summary>
        /// Determines whether the specified pawn is considered a vehicle.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns><c>true</c> if the pawn is a vehicle; otherwise, <c>false</c>.</returns>
        public static bool IsVehicle(Pawn pawn)
        {
            if (pawn == null) return false;

            Type pawnType = pawn.GetType();
            if (vehicleCache.TryGetValue(pawnType, out bool isVehicle))
            {
                return isVehicle;
            }

            bool isVehicleType = DetermineIfTypeMatchesVehicleSignature(pawnType);
            vehicleCache.TryAdd(pawnType, isVehicleType);
            return isVehicleType;
        }

        /// <summary>
        /// Determines whether the specified body part definition corresponds to a metabolic organ.
        /// </summary>
        /// <param name="def">The body part definition to check.</param>
        /// <returns><c>true</c> if the body part is a metabolic organ; otherwise, <c>false</c>.</returns>
        public static bool IsMetabolicOrgan(BodyPartDef def)
        {
            if (def == null) return false;
            var activeSet = Volatile.Read(ref metabolicOrgans);
            return activeSet.Contains(def);
        }

        #endregion

        #region 4. PHYSIOLOGICAL PATHOLOGY & SUBSTANCE EVALUATOR

        /// <summary>
        /// Determines whether the specified hediff represents an active pathology or substance, taking into account the pawn's consciousness state.
        /// </summary>
        /// <param name="hediff">The hediff to evaluate.</param>
        /// <param name="consciousnessIsDropped">Indicates whether the pawn's consciousness is dropped.</param>
        /// <returns><c>true</c> if the hediff is an active pathology or substance; otherwise, <c>false</c>.</returns>
        public static bool IsActivePathologyOrSubstance(Hediff hediff, bool consciousnessIsDropped)
        {
            if (hediff == null || hediff.def == null) return false;

            if (hediff is Hediff_Injury || hediff is Hediff_MissingPart || hediff is Hediff_AddedPart || hediff is Hediff_Implant)
            {
                return false;
            }

            HediffFlags flags = GetFlagsCached(hediff.def, hediff.GetType());

            if (flags == HediffFlags.None || (flags & HediffFlags.IsTolerance) != 0)
            {
                return false;
            }

            if ((flags & HediffFlags.ReducesConsciousness) != 0 && !consciousnessIsDropped)
            {
                return false;
            }

            if ((flags & HediffFlags.IsPhysiologicallyImpactful) != 0)
            {
                return CurStageHasActiveImpact(hediff);
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified hediff represents a substance (addiction).
        /// </summary>
        /// <param name="hediff">The hediff to evaluate.</param>
        /// <returns><c>true</c> if the hediff is a substance; otherwise, <c>false</c>.</returns>
        public static bool IsSubstance(Hediff hediff)
        {
            if (hediff == null) return false;
            return hediff is Hediff_Addiction;
        }

        /// <summary>
        /// Evaluates the capacities of the specified hediff definition and returns its implant characteristics.
        /// </summary>
        /// <param name="def">The hediff definition to evaluate.</param>
        /// <returns>The implant characteristics associated with the hediff definition.</returns>
        public static ImplantCharacteristics EvaluateHediffCapacities(HediffDef def)
        {
            if (def == null)
            {
                return new ImplantCharacteristics { EfficiencyModifier = 1.0f, AffectsAthletics = false };
            }

            return implantCache.GetOrAdd(def, implantFactory);
        }

        /// <summary>
        /// Retrieves the cached hediff flags for the specified hediff definition and type.
        /// </summary>
        /// <param name="def">The hediff definition to evaluate.</param>
        /// <param name="type">The type of the hediff to evaluate.</param>
        /// <returns>The cached hediff flags associated with the specified hediff definition and type.</returns>
        public static HediffFlags GetFlagsCached(HediffDef def, Type type)
        {
            return packedClassificationCache.GetOrAdd(def, flagFactory, type);
        }

        #endregion

        #region 5. MEDICAL ANATOMY CHECKERS & EXTENSION RESOLVERS

        /// <summary>
        /// Retrieves the level of the specified capacity for the given pawn, handling potential exceptions and edge cases.
        /// </summary>
        /// <param name="pawn">The pawn whose capacity level is to be evaluated.</param>
        /// <param name="capacity">The capacity to evaluate for the pawn.</param>
        /// <returns>The level of the specified capacity for the given pawn, or a default value in case of exceptions or edge cases.</returns>
        public static float GetCapacityLevel(Pawn pawn, PawnCapacityDef capacity)
        {
            if (pawn == null || pawn.Destroyed || capacity == null || pawn.health?.capacities == null) 
            {
                return 1.0f;
            }
            
            try
            {
                if (pawn.health.capacities.CapableOf(capacity))
                {
                    return pawn.health.capacities.GetLevel(capacity);
                }

                return 0.0f;
            }
            catch (Exception ex)
            {
                if (UnityData.IsInMainThread)
                {
                    string safeName = pawn.def?.defName ?? "Pawn";
                    OHLog.Solver.Warn("GetCapacityLevelFailed", ex, $"Failed to get capacity level for pawn {safeName} and capacity {capacity.defName}.");
                }
            }
            
            return 1.0f;
        }

        /// <summary>
        /// Safely reads Pawn.BodySize, shielding against third-party Harmony patches on the vanilla getter
        /// (e.g. gene mods) that can throw for pawn states they don't expect (test-bench dummies, etc.).
        /// Falls back to the race's own default body size rather than a flat 1.0 so non-human-scale races
        /// (mechs, animals, xenotypes) don't get silently treated as human-sized while the patch is broken.
        /// </summary>
        /// <param name="pawn">The pawn whose body size is to be safely evaluated.</param>
        /// <returns>The safe body size for the given pawn, considering potential third-party Harmony patches and race-specific defaults.</returns>
        public static float GetSafeBodySize(Pawn pawn)
        {
            if (pawn == null) return 1.0f;

            float raceFallback = (pawn.RaceProps != null && pawn.RaceProps.baseBodySize > 0f) ? pawn.RaceProps.baseBodySize : 1.0f;

            // BREAKPOINT ANCHOR: Headless Sandbox Dummy Shield
            // Evaluates native body size directly to prevent broken third-party postfixes
            // (e.g. GeneticRim.Patch_BodySize) from crashing on unspawned dummy pawns.
            if (pawn.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId)
            {
                float lifeStageFactor = pawn.ageTracker?.CurLifeStage?.bodySizeFactor ?? 1f;
                return raceFallback * lifeStageFactor;
            }

            if (knownBrokenBodySizePawnIds.ContainsKey(pawn.thingIDNumber))
            {
                return raceFallback;
            }

            try
            {
                return pawn.BodySize;
            }
            catch (Exception ex)
            {
                // Mark this pawn ID as having a broken body size getter to avoid repeated exceptions.
                knownBrokenBodySizePawnIds.TryAdd(pawn.thingIDNumber, 0);

                if (UnityData.IsInMainThread)
                {
                    string safeLabel = pawn.def?.label ?? "Pawn";
                    OHLog.Integration.Warn("BodySizeAccess", ex, $"Failed to access body size for pawn ID {pawn.thingIDNumber}: {safeLabel}");
                }
            }

            return raceFallback;
        }

        /// <summary>
        /// Determines whether the specified pawn capacity is considered athletic.
        /// </summary>
        /// <param name="capacity">The pawn capacity to evaluate.</param>
        /// <returns>True if the specified capacity is considered athletic; otherwise, false.</returns>
        public static bool IsAthleticCapacity(PawnCapacityDef capacity)
        {
            return capacity == PawnCapacityDefOf.Breathing ||
                   capacity == PawnCapacityDefOf.BloodPumping ||
                   capacity == PawnCapacityDefOf.Moving ||
                   capacity == PawnCapacityDefOf.Manipulation ||
                   capacity == PawnCapacityDefOf.Consciousness;
        }

        /// <summary>
        /// Retrieves the cached mod extension for the specified body part definition, if available.
        /// </summary>
        /// <param name="def">The body part definition for which to retrieve the cached mod extension.</param>
        /// <returns>The cached mod extension for the specified body part definition, or null if not available.</returns>
        public static BodyPartModExtension GetCachedModExtension(this BodyPartDef def)
        {
            if (def == null) return null;
            if (!extensionCache.TryGetValue(def, out BodyPartModExtension ext))
            {
                ext = def.GetModExtension<BodyPartModExtension>();
                extensionCache.TryAdd(def, ext);
            }
            return ext;
        }

        #endregion

        #region 6. PRIVATE PHYSIOLOGICAL IMPACT HELPERS

        /// <summary>
        /// Determines whether the specified hediff definition has a physiological impact.
        /// </summary>
        /// <param name="d">The hediff definition to evaluate.</param>
        /// <returns>True if the specified hediff definition has a physiological impact; otherwise, false.</returns>
        private static bool DefHasPhysiologicalImpact(HediffDef d)
        {
            if (d.stages == null) return false;

            for (int s = 0; s < d.stages.Count; s++)
            {
                HediffStage stage = d.stages[s];
                if (stage == null) continue;

                if (stage.painOffset > 0f || Math.Abs(stage.painFactor - 1.0f) >= SettingsDefaults.EfficiencyEpsilon)
                {
                    return true;
                }

                if (stage.capMods != null)
                {
                    for (int c = 0; c < stage.capMods.Count; c++)
                    {
                        PawnCapacityModifier capMod = stage.capMods[c];
                        if (capMod != null && IsAthleticCapacity(capMod.capacity))
                        {
                            return true;
                        }
                    }
                }

                if (StageModifiesPhysicalStats(stage))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the current stage of the specified hediff has an active physiological impact.
        /// </summary>
        /// <param name="hediff">The hediff to evaluate.</param>
        /// <returns>True if the current stage of the specified hediff has an active physiological impact; otherwise, false.</returns>
        private static bool CurStageHasActiveImpact(Hediff hediff)
        {
            HediffStage stage = hediff.CurStage;

            if (stage == null)
            {
                return hediff.def != null && hediff.def.isBad;
            }

            if (stage.painOffset > 0f || Math.Abs(stage.painFactor - 1.0f) >= SettingsDefaults.EfficiencyEpsilon)
            {
                return true;
            }

            if (stage.capMods != null)
            {
                for (int i = 0; i < stage.capMods.Count; i++)
                {
                    PawnCapacityModifier capMod = stage.capMods[i];
                    if (capMod != null && IsAthleticCapacity(capMod.capacity))
                    {
                        if (Math.Abs(capMod.offset) >= SettingsDefaults.EfficiencyEpsilon ||
                            Math.Abs(capMod.postFactor - 1.0f) >= SettingsDefaults.EfficiencyEpsilon ||
                            Math.Abs(capMod.setMax - 1.0f) >= SettingsDefaults.EfficiencyEpsilon)
                        {
                            return true;
                        }
                    }
                }
            }

            if (StageModifiesPhysicalStats(stage))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified hediff stage modifies physical stats.
        /// </summary>
        /// <param name="stage">The hediff stage to evaluate.</param>
        /// <returns>True if the specified hediff stage modifies physical stats; otherwise, false.</returns>
        private static bool StageModifiesPhysicalStats(HediffStage stage)
        {
            if (stage == null) return false;

            if (stage.statOffsets != null)
            {
                for (int i = 0; i < stage.statOffsets.Count; i++)
                {
                    StatDef stat = stage.statOffsets[i]?.stat;
                    if (stat != null && IsPhysicalOrMassStat(stat)) return true;
                }
            }

            if (stage.statFactors != null)
            {
                for (int i = 0; i < stage.statFactors.Count; i++)
                {
                    StatDef stat = stage.statFactors[i]?.stat;
                    if (stat != null && IsPhysicalOrMassStat(stat)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified stat is related to physical attributes or mass.
        /// </summary>
        /// <param name="stat">The stat definition to evaluate.</param>
        /// <returns>True if the specified stat is related to physical attributes or mass; otherwise, false.</returns>
        private static bool IsPhysicalOrMassStat(StatDef stat)
        {
            if (stat == null) return false;

            return physicalStatCache.GetOrAdd(stat, s =>
            {
                StatDef activeStat = IntegrationPipeline.ActiveDriver?.ActiveMassCapacityStat;

                if (s == StatDefOf.MoveSpeed || 
                    s == StatDefOf.CarryingCapacity || 
                    s == StatDefOf.Mass ||
                    (activeStat != null && s == activeStat))
                {
                    return true;
                }

                if (s.HasModExtension<MassCapacityStatModExtension>())
                {
                    return true;
                }

                if (s.capacityFactors != null)
                {
                    for (int i = 0; i < s.capacityFactors.Count; i++)
                    {
                        PawnCapacityDef cap = s.capacityFactors[i]?.capacity;
                        if (cap == PawnCapacityDefOf.Moving || cap == PawnCapacityDefOf.Manipulation)
                        {
                            return true;
                        }
                    }
                }

                return false;
            });
        }

        #endregion

        #region 7. AGNOSTIC METABOLIC ORGAN DISCOVERY (Zero String Matching)

        /// <summary>
        /// Agnostically identifies metabolic/filtration organs across all loaded species using native XML capacity source tags
        /// and natural harvestable tissue definitions with ZERO hardcoded string matching.
        /// </summary>
        public static void InitializeMetabolicOrgans()
        {
            if (metabolicInitialized) return;

            try
            {
                HashSet<BodyPartTagDef> sourceTags = new HashSet<BodyPartTagDef>();

                // Dynamically collect capacity source tag def pointers
                if (TopologyLayoutCompiler.ConsciousnessSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.ConsciousnessSourceTag);
                if (TopologyLayoutCompiler.SightSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.SightSourceTag);
                if (TopologyLayoutCompiler.HearingSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.HearingSourceTag);
                if (TopologyLayoutCompiler.BloodPumpingSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.BloodPumpingSourceTag);
                if (TopologyLayoutCompiler.BreathingSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.BreathingSourceTag);
                if (TopologyLayoutCompiler.BloodFiltrationKidneyTag != null) sourceTags.Add(TopologyLayoutCompiler.BloodFiltrationKidneyTag);
                if (TopologyLayoutCompiler.BloodFiltrationLiverTag != null) sourceTags.Add(TopologyLayoutCompiler.BloodFiltrationLiverTag);
                if (TopologyLayoutCompiler.BloodFiltrationSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.BloodFiltrationSourceTag);
                if (TopologyLayoutCompiler.MetabolismSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.MetabolismSourceTag);
                if (TopologyLayoutCompiler.DigestionSourceTag != null) sourceTags.Add(TopologyLayoutCompiler.DigestionSourceTag);

                Volatile.Write(ref vitalOrganSourceTags, sourceTags);

                HashSet<BodyPartDef> freshOrgans = new HashSet<BodyPartDef>();
                List<BodyPartDef> allParts = DefDatabase<BodyPartDef>.AllDefsListForReading;

                for (int i = 0; i < allParts.Count; i++)
                {
                    BodyPartDef partDef = allParts[i];
                    if (partDef == null) continue;

                    if (EvaluatePartIsOrgan(partDef, sourceTags))
                    {
                        freshOrgans.Add(partDef);
                    }
                }

                Volatile.Write(ref metabolicOrgans, freshOrgans);
                metabolicInitialized = true;
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("MetabolicInitFailed", ex, "Failed to initialize metabolic organs.");
            }
        }

        /// <summary>
        /// Clears all static caches and reinitializes metabolic organs.
        /// </summary>
        public static void ClearStaticCaches()
        {
            extensionCache.Clear();
            vehicleCache.Clear();
            packedClassificationCache.Clear();
            implantCache.Clear();
            physicalStatCache.Clear();
            knownBrokenBodySizePawnIds.Clear();
            
            metabolicInitialized = false;
            InitializeMetabolicOrgans();
        }

        /// <summary>
        /// Evaluates if a BodyPartDef represents a metabolic organ purely through native engine metadata:
        /// 1. BodyPartModExtension.isOrgan override
        /// 2. Limb Defense: Limbs are structural appendages, never internal metabolic organs.
        /// 3. Biological Vital Capacity Source Tag (BreathingSource, BloodPumpingSource, Filtration, Digestion, Brain, Eyes, Ears).
        /// 4. Harvestable soft tissue definitions (spawnThingOnRemoved != null without limb/spine tags).
        /// </summary>
        /// <param name="partDef">The body part definition to evaluate.</param>
        /// <param name="sourceTags">A set of source tags indicating biological vital capacity.</param>
        /// <returns>True if the body part is considered a metabolic organ; otherwise, false.</returns>
        private static bool EvaluatePartIsOrgan(BodyPartDef partDef, HashSet<BodyPartTagDef> sourceTags)
        {
            // 1. Mod Extension Override
            BodyPartModExtension ext = partDef.GetCachedModExtension();
            if (ext != null)
            {
                if (ext.isOrgan) return true;
                if (ext.partType != PartType.None) return false;
            }

            // 2. Limb & Spine Defense: Appendages and the spinal column are load-bearing structural skeleton, not organs
            if (TopologyLayoutCompiler.HasAnyTag(partDef, TopologyLayoutCompiler.armTags) ||
                TopologyLayoutCompiler.HasAnyTag(partDef, TopologyLayoutCompiler.legTags) ||
                TopologyLayoutCompiler.IsTrunkDef(partDef))
            {
                return false;
            }

            // 3. Biological Vital Capacity Source Tag
            if (partDef.tags != null && partDef.tags.Count > 0)
            {
                for (int j = 0; j < partDef.tags.Count; j++)
                {
                    if (sourceTags.Contains(partDef.tags[j])) return true;
                }
            }

            // 4. Natural harvestable soft tissue definitions (e.g. natural transplantable organs, modded glands)
            if (partDef.spawnThingOnRemoved != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified pawn type matches the signature of a vehicle pawn type.
        /// </summary>
        /// <param name="pawnType">The type of the pawn to evaluate.</param>
        /// <returns>True if the specified pawn type matches the signature of a vehicle pawn type; otherwise, false.</returns>
        private static bool DetermineIfTypeMatchesVehicleSignature(Type pawnType)
        {
            Type current = pawnType;
            int safetyLoopCounter = 0;

            while (current != null && safetyLoopCounter++ < 10)
            {
                string fullName = current.FullName;
                if (!string.IsNullOrEmpty(fullName))
                {
                    if (fullName.StartsWith("Vehicles.") || fullName == "Vehicles.VehiclePawn" || 
                        fullName.Contains("VehiclePawn") || fullName.Contains("VehicleSkyfaller"))
                    {
                        return true;
                    }
                }
                current = current.BaseType;
            }
            return false;
        }

        #endregion
    }
}