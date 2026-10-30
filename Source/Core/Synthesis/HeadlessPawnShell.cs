using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace OverHaulers
{
    /// <summary>
    /// [CORE-SYNTH-01] HEADLESS PAWN SHELL
    /// Canonical Single Source of Truth for ephemeral, unspawned Pawn instances.
    /// Allocates detached pawn shells bypassing PawnGenerator, WorldPawns, TaleRecorder, and Factions.
    /// Safe for execution both in-game and on the Main Menu via direct backing-field age and life-stage pinning.
    /// Resides under Source/Core/Synthesis/ with zero Presentation or UI dependencies.
    /// </summary>
    public class HeadlessPawnShell : IDisposable
    {
        #region 1. CONSTANTS & SENTINELS

        /// <summary>
        /// [CORE-SYNTH-02] Reserved sentinel ThingID for all headless sandbox and baseline dummy pawns.
        /// Unique negative hash guaranteeing zero collision with legitimate world pawns.
        /// </summary>
        public static readonly int HeadlessPawnThingId = -Math.Abs("OverHaulers_Sandbox_Pawn".GetHashCode());

        #endregion

        #region 2. INSTANCE FIELDS & BACKING STATE

        protected Pawn headlessPawn;
        protected Pawn sourcePawn;

        #endregion

        #region 3. PROPERTIES & ENCAPSULATED ACCESSORS

        /// <summary>Gets the active unspawned pawn shell.</summary>
        public Pawn Pawn => headlessPawn;

        /// <summary>Gets the optional live pawn reference cloned by this shell.</summary>
        public Pawn ClonedSourcePawn => sourcePawn;

        /// <summary>True if the shell holds a valid, non-destroyed pawn instance.</summary>
        public bool IsValid => headlessPawn != null && !headlessPawn.Destroyed;

        #endregion

        #region 4. LIFECYCLE, FACTORIES & DISPOSAL

        /// <summary>
        /// Initializes a new empty instance of the <see cref="HeadlessPawnShell"/> class.
        /// </summary>
        public HeadlessPawnShell()
        {
        }

        /// <summary>
        /// Canonical factory: Allocates a pristine, undamaged headless pawn of the target species.
        /// </summary>
        /// <param name="raceDef">The species ThingDef to synthesize.</param>
        /// <param name="initializeComps">If true (mid-game), fully initializes ThingComps.</param>
        /// <returns>A new clean headless pawn shell instance.</returns>
        public static HeadlessPawnShell CreateClean(ThingDef raceDef, bool initializeComps = false)
        {
            HeadlessPawnShell shell = new HeadlessPawnShell();
            shell.RebuildPawnCore(raceDef, null, initializeComps);
            return shell;
        }

        /// <summary>
        /// Canonical factory: Allocates a headless pawn cloning an authentic live colonist's traits and hediffs.
        /// </summary>
        /// <param name="source">The live pawn to clone.</param>
        /// <param name="initializeComps">If true (mid-game), fully initializes ThingComps.</param>
        /// <returns>A new cloned headless pawn shell instance.</returns>
        public static HeadlessPawnShell CreateCloned(Pawn source, bool initializeComps = false)
        {
            HeadlessPawnShell shell = new HeadlessPawnShell();
            shell.RebuildPawnCore(source?.def, source, initializeComps);
            return shell;
        }

        /// <summary>
        /// Performs cleanup and disposal of the headless pawn shell, clearing references to the headless and source pawns.
        /// This method should be called when the headless pawn shell is no longer needed to ensure proper resource management.
        /// </summary>
        public virtual void Teardown()
        {
            if (headlessPawn != null)
            {
                headlessPawn.health?.hediffSet?.Clear();
                headlessPawn.health?.capacities?.Clear();
                headlessPawn.abilities?.abilities?.Clear();
                headlessPawn = null;
            }
            sourcePawn = null;
        }

        /// <summary>
        /// Disposes of the headless pawn shell by invoking the Teardown method and suppressing finalization.
        /// This ensures that all resources are properly released and prevents the finalizer from running.
        /// </summary>
        public void Dispose()
        {
            Teardown();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region 5. CANONICAL SYNTHESIS API

        /// <summary>
        /// Allocates a detached Pawn shell and initializes physiological state.
        /// Guaranteed zero registration into WorldPawns, MapPawns, or TaleRecorder.
        /// </summary>
        /// <param name="raceDef">The species ThingDef to synthesize.</param>
        /// <param name="source">The live pawn to clone traits and hediffs from, if any.</param>
        /// <param name="initializeComps">If true (mid-game), fully initializes ThingComps.</param>
        public virtual void RebuildPawnCore(ThingDef raceDef, Pawn source, bool initializeComps)
        {
            Teardown();

            sourcePawn = source;
            if (raceDef == null && source != null) raceDef = source.def;
            if (raceDef == null) raceDef = ThingDefOf.Human;

            try
            {
                // 1. Direct Instantiation: Bypasses ThingMaker.MakeThing / ThingIDMaker.GiveIDTo (Safe on Main Menu)
                headlessPawn = (Pawn)Activator.CreateInstance(raceDef.thingClass);
                headlessPawn.def = raceDef;
                headlessPawn.SetStuffDirect(sourcePawn?.Stuff);

                // Sentinel ID Assignment
                headlessPawn.thingIDNumber = HeadlessPawnThingId;

                // 2. Resolve PawnKindDef and Gender
                headlessPawn.kindDef = sourcePawn?.kindDef ?? ResolvePawnKindForRace(raceDef);
                headlessPawn.gender = sourcePawn != null
                    ? sourcePawn.gender
                    : (raceDef.race != null && !raceDef.race.hasGenders ? Gender.None : Gender.Male);

                // 3. Faction mirror (In-game only)
                if (sourcePawn?.Faction != null)
                {
                    headlessPawn.SetFactionDirect(sourcePawn.Faction);
                }

                // 4. Targeted Trackers; direct instantiation to avoid ThingMaker/ID assignment.
                headlessPawn.health = new Pawn_HealthTracker(headlessPawn);
                headlessPawn.ageTracker = new Pawn_AgeTracker(headlessPawn);
                headlessPawn.apparel = new Pawn_ApparelTracker(headlessPawn);
                headlessPawn.equipment = new Pawn_EquipmentTracker(headlessPawn);
                headlessPawn.abilities = new Pawn_AbilityTracker(headlessPawn);
                headlessPawn.skills = new Pawn_SkillTracker(headlessPawn);

                try
                {
                    Pawn_NeedsTracker needs = (Pawn_NeedsTracker)FormatterServices.GetUninitializedObject(typeof(Pawn_NeedsTracker));
                    SafeSetField(needs, "pawn", headlessPawn);
                    SafeSetField(needs, "needs", new List<Need>());
                    headlessPawn.needs = needs;
                }
                catch { }

                try
                {
                    Pawn_MindState mindState = (Pawn_MindState)FormatterServices.GetUninitializedObject(typeof(Pawn_MindState));
                    mindState.pawn = headlessPawn;
                    mindState.mentalStateHandler = new MentalStateHandler(headlessPawn);
                    mindState.inspirationHandler = new InspirationHandler(headlessPawn);
                    mindState.priorityWork = new PriorityWork(headlessPawn);
                    headlessPawn.mindState = mindState;
                }
                catch { }

                // 5. Life-Stage & Backing-Field Pinning
                List<LifeStageAge> lifeStageAges = raceDef.race?.lifeStageAges;
                int adultStageIdx = (lifeStageAges != null && lifeStageAges.Count > 0) ? (lifeStageAges.Count - 1) : 0;
                float adultAgeYears = (lifeStageAges != null && lifeStageAges.Count > 0) ? lifeStageAges[adultStageIdx].minAge : 20f;
                long defaultAdultTicks = Math.Max((long)GenDate.TicksPerYear, (long)(adultAgeYears * GenDate.TicksPerYear));

                long targetBioTicks = sourcePawn?.ageTracker != null ? sourcePawn.ageTracker.AgeBiologicalTicks : defaultAdultTicks;
                long targetChronoTicks = targetBioTicks;
                int targetStageIdx = sourcePawn?.ageTracker != null ? sourcePawn.ageTracker.CurLifeStageIndex : adultStageIdx;

                SafeSetField(headlessPawn.ageTracker, "ageBiologicalTicksInt", targetBioTicks);
                SafeSetField(headlessPawn.ageTracker, "birthAbsTicksInt", -targetChronoTicks);
                SafeSetField(headlessPawn.ageTracker, "cachedLifeStageIndex", targetStageIdx);
                SafeSetField(headlessPawn.ageTracker, "curLifeStageIndex", targetStageIdx);

                // 6. Story & Biotech Gene Tracking
                if (raceDef.race != null && raceDef.race.Humanlike && headlessPawn.story == null)
                {
                    headlessPawn.story = new Pawn_StoryTracker(headlessPawn);
                    if (sourcePawn?.story != null)
                    {
                        headlessPawn.story.bodyType = sourcePawn.story.bodyType;
                        headlessPawn.story.headType = sourcePawn.story.headType;
                        headlessPawn.story.hairDef = sourcePawn.story.hairDef;
                    }
                    else
                    {
                        headlessPawn.story.bodyType = BodyTypeDefOf.Male 
                            ?? DefDatabase<BodyTypeDef>.GetNamedSilentFail("Male") 
                            ?? DefDatabase<BodyTypeDef>.AllDefsListForReading?.FirstOrDefault();

                        headlessPawn.story.headType = DefDatabase<HeadTypeDef>.GetNamedSilentFail("Male_AverageNormal") 
                            ?? DefDatabase<HeadTypeDef>.AllDefsListForReading?.FirstOrDefault();

                        headlessPawn.story.hairDef = HairDefOf.Bald 
                            ?? DefDatabase<HairDef>.GetNamedSilentFail("Bald") 
                            ?? DefDatabase<HairDef>.AllDefsListForReading?.FirstOrDefault();
                    }
                }

                if (ModsConfig.BiotechActive && headlessPawn.genes == null)
                {
                    try
                    {
                        headlessPawn.genes = new Pawn_GeneTracker(headlessPawn);

                        if (raceDef.race != null && raceDef.race.Humanlike)
                        {
                            if (sourcePawn?.genes != null)
                            {
                                headlessPawn.genes.SetXenotypeDirect(sourcePawn.genes.Xenotype);

                                for (int i = 0; i < sourcePawn.genes.Endogenes.Count; i++)
                                {
                                    GeneDef gene = sourcePawn.genes.Endogenes[i].def;
                                    if (!headlessPawn.genes.HasActiveGene(gene))
                                    {
                                        headlessPawn.genes.AddGene(gene, xenogene: false);
                                    }
                                }
                                for (int i = 0; i < sourcePawn.genes.Xenogenes.Count; i++)
                                {
                                    GeneDef gene = sourcePawn.genes.Xenogenes[i].def;
                                    if (!headlessPawn.genes.HasActiveGene(gene))
                                    {
                                        headlessPawn.genes.AddGene(gene, xenogene: true);
                                    }
                                }
                            }
                        }
                        else if (sourcePawn?.genes != null)
                        {
                            for (int i = 0; i < sourcePawn.genes.GenesListForReading.Count; i++)
                            {
                                GeneDef gene = sourcePawn.genes.GenesListForReading[i].def;
                                if (!headlessPawn.genes.HasActiveGene(gene))
                                {
                                    headlessPawn.genes.AddGene(gene, xenogene: false);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OHLog.Integration.Warn("HeadlessPawnShell:BiotechGenes", ex, $"Failed to synthesize Biotech genes for {raceDef.defName}.");
                    }
                }

                // 7. Initialize Comps (Mid-game only)
                if (initializeComps)
                {
                    try
                    {
                        headlessPawn.InitializeComps();
                    }
                    catch (Exception ex)
                    {
                        OHLog.Integration.Warn("HeadlessPawnShell:InitializeComps", ex, $"Failed to initialize comps for {raceDef.defName}.");
                    }
                }

                // 8. Clone authentic health graph if cloning a live pawn
                SyncHediffsFromSource();
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("HeadlessPawnShell:RebuildPawnCore", ex, "Failed to allocate headless pawn shell.");
                Teardown();
            }
        }

        /// <summary>
        /// Synchronizes the headless pawn's health with the source colonist's authentic hediff graph.
        /// </summary>
        public void SyncHediffsFromSource()
        {
            if (headlessPawn?.health?.hediffSet == null) return;

            headlessPawn.health.hediffSet.Clear();

            if (sourcePawn?.health?.hediffSet != null)
            {
                List<Hediff> sourceHediffs = sourcePawn.health.hediffSet.hediffs;
                if (sourceHediffs != null && sourceHediffs.Count > 0)
                {
                    List<Hediff> sortedHediffs = new List<Hediff>(sourceHediffs);
                    sortedHediffs.Sort((a, b) => GetPartDepth(a?.Part).CompareTo(GetPartDepth(b?.Part)));

                    for (int i = 0; i < sortedHediffs.Count; i++)
                    {
                        Hediff src = sortedHediffs[i];
                        if (src?.def == null) continue;

                        if (src.Part != null && headlessPawn.health.hediffSet.PartIsMissing(src.Part))
                        {
                            continue;
                        }

                        try
                        {
                            Hediff cloneHediff = HediffMaker.MakeHediff(src.def, headlessPawn, src.Part);
                            if (cloneHediff != null)
                            {
                                cloneHediff.Severity = src.Severity;
                                cloneHediff.ageTicks = src.ageTicks;

                                // Copy internal comp states (ticks, progress, adaptation) from source to clone, ignoring the base
                                // HediffComp.parent pointer.
                                if (src is HediffWithComps srcComps && cloneHediff is HediffWithComps cloneComps)
                                {
                                    CopyCompStates(srcComps, cloneComps);
                                }

                                headlessPawn.health.AddHediff(cloneHediff);
                            }
                        }
                        catch { }
                    }
                }
            }

            DirtyHealthCache();
        }

        #endregion

        #region 6. SYNTHESIS PIPELINE STAGES

        /// <summary>
        /// Marks the headless pawn's health cache as dirty, forcing a recalculation of capacities and hediffs.
        /// </summary>
        public void DirtyHealthCache()
        {
            if (headlessPawn?.health != null)
            {
                headlessPawn.health.capacities?.Clear();
                headlessPawn.health.hediffSet?.DirtyCache();
            }
        }

        #endregion

        #region 7. PRIVATE REFLECTION & STRUCTURAL HELPERS

        /// <summary>
        /// Safely sets the value of a field on the target object using reflection, catching any exceptions that may occur.
        /// </summary>
        /// <typeparam name="TTarget">The type of the target object containing the field.</typeparam>
        /// <typeparam name="TVal">The type of the value to set.</typeparam>
        /// <param name="target">The target object whose field is to be set.</param>
        /// <param name="fieldName">The name of the field to set.</param>
        /// <param name="value">The value to assign to the field.</param>
        protected static void SafeSetField<TTarget, TVal>(TTarget target, string fieldName, TVal value) where TTarget : class
        {
            if (target == null) return;
            try
            {
                FieldInfo field = AccessTools.Field(typeof(TTarget), fieldName);
                if (field != null)
                {
                    field.SetValue(target, value);
                }
            }
            catch { }
        }

        /// <summary>
        /// Calculates the depth of the specified body part within the pawn's body hierarchy. A root part has a depth of 0, its immediate
        /// children have a depth of 1, and so on.
        /// </summary>
        /// <param name="part">The body part record whose depth is to be calculated.</param>
        /// <returns>The depth of the body part within the body hierarchy, or -1 if the part is null.</returns>
        protected static int GetPartDepth(BodyPartRecord part)
        {
            if (part == null) return -1;
            int depth = 0;
            BodyPartRecord curr = part.parent;
            while (curr != null)
            {
                depth++;
                curr = curr.parent;
            }
            return depth;
        }

        /// <summary>
        /// Resolves the appropriate PawnKindDef for the specified race definition. If the race has a predefined AnyPawnKind, it is returned.
        /// Otherwise, it searches the database for a matching PawnKindDef. Falls back to "Colonist" or the first available PawnKindDef if no
        /// match is found.
        /// </summary>
        /// <param name="raceDef">The ThingDef representing the race for which to resolve the PawnKindDef.</param>
        /// <returns>The resolved PawnKindDef for the specified race, or a fallback if none is found.</returns>
        protected static PawnKindDef ResolvePawnKindForRace(ThingDef raceDef)
        {
            if (raceDef == null) return PawnKindDefOf.Colonist ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist");

            if (raceDef.race?.AnyPawnKind != null)
            {
                return raceDef.race.AnyPawnKind;
            }

            List<PawnKindDef> allKinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
            if (allKinds != null)
            {
                for (int i = 0; i < allKinds.Count; i++)
                {
                    if (allKinds[i].race == raceDef)
                    {
                        return allKinds[i];
                    }
                }
            }

            return DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist") ?? DefDatabase<PawnKindDef>.AllDefsListForReading?.FirstOrDefault();
        }

        /// <summary>
        /// Copies internal comp fields (ticks, progress, adaptation) from a source hediff to a cloned hediff.
        /// Ignores the base HediffComp.parent pointer to maintain proper ownership.
        /// </summary>
        /// <param name="src">The source HediffWithComps from which to copy internal comp states.</param>
        /// <param name="dest">The destination HediffWithComps to which internal comp states are copied.</param>
        protected static void CopyCompStates(HediffWithComps src, HediffWithComps dest)
        {
            if (src?.comps == null || dest?.comps == null) return;

            for (int i = 0; i < src.comps.Count; i++)
            {
                HediffComp sc = src.comps[i];
                if (sc == null) continue;

                Type scType = sc.GetType();
                HediffComp dc = dest.comps.Find(c => c != null && c.GetType() == scType);
                if (dc == null) continue;

                Type currType = scType;
                while (currType != null && currType != typeof(HediffComp) && currType != typeof(object))
                {
                    FieldInfo[] fields = currType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int f = 0; f < fields.Length; f++)
                    {
                        FieldInfo fi = fields[f];
                        if (fi.IsLiteral || fi.IsInitOnly) continue;
                        try
                        {
                            fi.SetValue(dc, fi.GetValue(sc));
                        }
                        catch { }
                    }
                    currType = currType.BaseType;
                }
            }
        }

        #endregion
    }
}