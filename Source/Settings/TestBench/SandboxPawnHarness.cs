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
    #region 1. [TEST-05] SIMULATION METADATA MODELS

    /// <summary>
    /// [TEST-05] Identifies the simulated surgical procedure applied to a sandbox pawn joint.
    /// </summary>
    public enum SimulatedOpType
    {
        InstallReplacement = 0,
        InstallImplant = 1,
        Amputate = 2,
        Trauma = 3
    }

    /// <summary>
    /// [TEST-05] Lightweight metadata descriptor tracking user-applied surgical modifications on the sandbox pawn.
    /// Used for colorized inspect badges and UI state tracking.
    /// </summary>
    public struct SimulatedModification
    {
        public SimulatedOpType OpType;
        public HediffDef TargetHediff;
        public RecipeDef SourceRecipe;
        public string DisplayLabel;
        public float Efficiency;
    }

    #endregion

    /// <summary>
    /// [TEST-05] HEADLESS SANDBOX PAWN HARNESS
    /// Manages an ephemeral, unspawned Pawn instance mirroring any live colonist or species archetype.
    /// Executes genuine RimWorld health tracker and surgery methods in an isolated sandbox,
    /// guaranteeing 1:1 Caravan Mass Capacity (kg) and physiological capacity evaluation with zero world leaks.
    /// Safe for execution both in-game and on the Main Menu via direct backing-field age and life-stage pinning.
    /// Resides under Source/Settings/TestBench/.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURAL DESIGN RATIONALE (WHY THIS IS NOT USING PawnGenerator):
    /// In vanilla RimWorld, calling PawnGenerator.GeneratePawn() or ThingMaker.MakeThing() executes 
    /// deep engine hooks: registering the pawn into WorldPawns, firing TaleRecorder events, assigning 
    /// faction relations, and running InitializeComps().
    /// 
    /// On the Main Menu (e.g. Test Bench before loading a save), those managers do not exist, resulting 
    /// in fatal NullReferenceExceptions. Furthermore, in-game, generating throwaway pawns causes memory 
    /// leaks in world pawn caches.
    /// 
    /// This harness uses Activator.CreateInstance and uninitialized FormatterServices shells to construct 
    /// a 100% sterile, decoupled pawn shell with zero world registration. ThingComps are intentionally omitted 
    /// until LiveSandboxPawnHarness is invoked mid-game.
    /// </remarks>
    public class SandboxPawnHarness : IDisposable
    {
        #region 2. HARNESS CONSTANTS & SENTINELS

        /// <summary>
        /// [TEST-05] Reserved sentinel ThingID for the headless sandbox pawn.
        /// Uses unique negative "OverHaulers_Sandbox_Pawn" hash to guarantee zero collision with legitimate world pawns.
        /// </summary>
        public static readonly int SandboxPawnThingId = -Math.Abs("OverHaulers_Sandbox_Pawn".GetHashCode());

        #endregion

        #region 3. HARNESS FIELDS & STATE STORAGE

        protected Pawn sandboxPawn;
        protected Pawn sourcePawn;
        protected TestSubjectEntry boundSubject;

        /// <summary>Simulated replacement modifications (Prosthetics/Amputations/Trauma) indexed by target BodyPartRecord.</summary>
        public readonly Dictionary<BodyPartRecord, SimulatedModification> ReplacementDeltas = 
            new Dictionary<BodyPartRecord, SimulatedModification>();

        /// <summary>Simulated localized implants (Glands, Ribs) indexed by target BodyPartRecord.</summary>
        public readonly Dictionary<BodyPartRecord, List<SimulatedModification>> ImplantDeltas = 
            new Dictionary<BodyPartRecord, List<SimulatedModification>>();

        /// <summary>Active simulated systemic drugs, combat stimulants, and whole-body conditions.</summary>
        public readonly HashSet<HediffDef> ActiveSimulatedDrugs = 
            new HashSet<HediffDef>();

        /// <summary>Gets the active, unspawned sandbox pawn instance.</summary>
        public Pawn SandboxPawn => sandboxPawn;

        /// <summary>Gets the reference live colonist currently mirrored (null if generic archetype).</summary>
        public Pawn SourcePawn => sourcePawn;

        /// <summary>Gets the test subject entry currently blueprinting the harness.</summary>
        public TestSubjectEntry BoundSubject => boundSubject;

        /// <summary>True if the harness currently contains a valid, non-destroyed sandbox pawn.</summary>
        public bool IsValid => sandboxPawn != null && !sandboxPawn.Destroyed;

        /// <summary>True if any hypothetical modifications are currently applied to the sandbox pawn.</summary>
        public bool HasModifications => ReplacementDeltas.Count > 0 || ImplantDeltas.Count > 0 || ActiveSimulatedDrugs.Count > 0;

        #endregion

        #region 4. CONSTRUCTOR & SUBJECT BINDING

        /// <summary>
        /// Initializes a new instance of the <see cref="SandboxPawnHarness"/> class.
        /// </summary>
        /// <remarks>
        /// The constructor does not automatically bind a test subject or create a sandbox pawn.
        /// Use <see cref="BindSubject"/> to initialize the harness with a specific test subject.
        /// </remarks>
        public SandboxPawnHarness()
        {
        }

        /// <summary>
        /// Binds a test subject (live colonist or generic species) to the harness and builds a fresh, isolated sandbox pawn.
        /// </summary>
        /// <param name="subject">The test subject entry to blueprint.</param>
        public void BindSubject(TestSubjectEntry subject)
        {
            boundSubject = subject;
            sourcePawn = subject?.IsLivePawn == true ? subject.LivePawn : null;

            ClearModifications();
            RebuildSandboxPawn();
        }

        #endregion

        #region 5. HEADLESS PAWN ALLOCATION & STATE CLONING

        /// <summary>
        /// Allocates a detached Pawn shell and initializes physiological state.
        /// Uses direct backing-field age pinning and empty equipment/apparel trackers to prevent Main Menu stat NREs.
        /// Guaranteed zero registration into WorldPawns, MapPawns, or TaleRecorder.
        /// </summary>
        public virtual void RebuildSandboxPawn()
        {
            TeardownSandboxPawn();

            // BREAKPOINT ANCHOR: Subject Validation Guard
            if (boundSubject == null)
            {
                return;
            }

            ThingDef raceDef = boundSubject.RaceDef ?? boundSubject.LivePawn?.def ?? ThingDefOf.Human;
            if (raceDef == null) return;

            try
            {
                #region 5A. Raw Pawn Shell Allocation & Tracker Creation
                // This section handles the raw allocation of the sandbox pawn shell and the creation of essential trackers.
                // It ensures that the sandbox pawn is properly instantiated with the correct race, kind, gender, and faction.
                // Physiological, needs, and equipment trackers are initialized to prevent null reference issues during testing.

                // 1. Direct Instantiation: Bypasses ThingMaker.MakeThing / ThingIDMaker.GiveIDTo (Safe on Main Menu)
                sandboxPawn = (Pawn)Activator.CreateInstance(raceDef.thingClass);
                sandboxPawn.def = raceDef;
                sandboxPawn.SetStuffDirect(sourcePawn?.Stuff);

                // Sentinel ID Assignment
                sandboxPawn.thingIDNumber = SandboxPawnThingId;

                // 2. Resolve appropriate PawnKindDef and Gender
                sandboxPawn.kindDef = sourcePawn?.kindDef ?? ResolvePawnKindForRace(raceDef);
                sandboxPawn.gender = sourcePawn != null 
                    ? sourcePawn.gender 
                    : (raceDef.race != null && !raceDef.race.hasGenders ? Gender.None : Gender.Male);

                // 3. Safe Faction mirror (In-game only)
                if (sourcePawn?.Faction != null)
                {
                    sandboxPawn.SetFactionDirect(sourcePawn.Faction);
                }

                // 4. Targeted Physiological, Needs & Equipment Initialization
                sandboxPawn.health = new Pawn_HealthTracker(sandboxPawn);
                sandboxPawn.ageTracker = new Pawn_AgeTracker(sandboxPawn);
                sandboxPawn.apparel = new Pawn_ApparelTracker(sandboxPawn);
                sandboxPawn.equipment = new Pawn_EquipmentTracker(sandboxPawn);

                try
                {
                    Pawn_NeedsTracker needs = (Pawn_NeedsTracker)FormatterServices.GetUninitializedObject(typeof(Pawn_NeedsTracker));
                    SafeSetField(needs, "pawn", sandboxPawn);
                    SafeSetField(needs, "needs", new List<Need>());
                    sandboxPawn.needs = needs;
                }
                catch { }

                try
                {
                    Pawn_MindState mindState = (Pawn_MindState)FormatterServices.GetUninitializedObject(typeof(Pawn_MindState));
                    mindState.pawn = sandboxPawn;
                    mindState.mentalStateHandler = new MentalStateHandler(sandboxPawn);
                    mindState.inspirationHandler = new InspirationHandler(sandboxPawn);
                    mindState.priorityWork = new PriorityWork(sandboxPawn);
                    sandboxPawn.mindState = mindState;
                }
                catch { }

                #endregion

                #region 5B. Life-Stage & Backing-Field Pinning
                // This section ensures that the sandbox pawn's age and life stage are correctly initialized.
                // It calculates the appropriate biological and chronological ticks based on the source pawn or defaults to adult values.

                List<LifeStageAge> lifeStageAges = raceDef.race?.lifeStageAges;
                int adultStageIdx = (lifeStageAges != null && lifeStageAges.Count > 0) ? (lifeStageAges.Count - 1) : 0;
                float adultAgeYears = (lifeStageAges != null && lifeStageAges.Count > 0) ? lifeStageAges[adultStageIdx].minAge : 20f;
                long defaultAdultTicks = Math.Max((long)GenDate.TicksPerYear, (long)(adultAgeYears * GenDate.TicksPerYear));

                long targetBioTicks = sourcePawn?.ageTracker != null ? sourcePawn.ageTracker.AgeBiologicalTicks : defaultAdultTicks;
                long targetChronoTicks = targetBioTicks;
                int targetStageIdx = sourcePawn?.ageTracker != null ? sourcePawn.ageTracker.CurLifeStageIndex : adultStageIdx;

                SafeSetField(sandboxPawn.ageTracker, "ageBiologicalTicksInt", targetBioTicks);
                SafeSetField(sandboxPawn.ageTracker, "birthAbsTicksInt", -targetChronoTicks);
                SafeSetField(sandboxPawn.ageTracker, "cachedLifeStageIndex", targetStageIdx);
                SafeSetField(sandboxPawn.ageTracker, "curLifeStageIndex", targetStageIdx);

                #endregion

                #region 5C. Story & Biotech Gene Tracking
                // This section initializes the sandbox pawn's story tracker for humanoid pawns and sets up biotech gene tracking if the Biotech
                //  mod is active.
                // It ensures that body type, head type, xenotype, and genes are correctly copied from the source pawn when available.

                if (raceDef.race != null && raceDef.race.Humanlike && sandboxPawn.story == null)
                {
                    sandboxPawn.story = new Pawn_StoryTracker(sandboxPawn);
                    if (sourcePawn?.story != null)
                    {
                        sandboxPawn.story.bodyType = sourcePawn.story.bodyType;
                        sandboxPawn.story.headType = sourcePawn.story.headType;
                    }
                    else
                    {
                        sandboxPawn.story.bodyType = BodyTypeDefOf.Male ?? DefDatabase<BodyTypeDef>.GetNamedSilentFail("Male");
                    }
                }

                // Initialize Biotech Xenotype & Genes
                if (ModsConfig.BiotechActive && sandboxPawn.genes == null)
                {
                    sandboxPawn.genes = new Pawn_GeneTracker(sandboxPawn);

                    if (raceDef.race != null && raceDef.race.Humanlike)
                    {
                        if (sourcePawn?.genes != null)
                        {
                            sandboxPawn.genes.SetXenotypeDirect(sourcePawn.genes.Xenotype);

                            for (int i = 0; i < sourcePawn.genes.Endogenes.Count; i++)
                            {
                                GeneDef gene = sourcePawn.genes.Endogenes[i].def;
                                if (!sandboxPawn.genes.HasActiveGene(gene))
                                {
                                    sandboxPawn.genes.AddGene(gene, xenogene: false);
                                }
                            }
                            for (int i = 0; i < sourcePawn.genes.Xenogenes.Count; i++)
                            {
                                GeneDef gene = sourcePawn.genes.Xenogenes[i].def;
                                if (!sandboxPawn.genes.HasActiveGene(gene))
                                {
                                    sandboxPawn.genes.AddGene(gene, xenogene: true);
                                }
                            }
                        }
                    }
                    else if (sourcePawn?.genes != null)
                    {
                        // Clone custom animal/mechanoid genes if present from VEF Animal Genes
                        for (int i = 0; i < sourcePawn.genes.GenesListForReading.Count; i++)
                        {
                            GeneDef gene = sourcePawn.genes.GenesListForReading[i].def;
                            if (!sandboxPawn.genes.HasActiveGene(gene))
                            {
                                sandboxPawn.genes.AddGene(gene, xenogene: false);
                            }
                        }
                    }
                }

                #endregion

                #region 5D. Authentic Hediff Graph Synchronization
                // This section ensures that the sandbox pawn's health state mirrors the source pawn's authentic hediff graph.
                // Any missing body parts or hediffs are handled gracefully, and the health cache is marked dirty after synchronization.

                SyncHediffsFromSource();
                #endregion
            }
            catch (Exception ex)
            {
                OHLog.TestBench.Warn("SandboxPawnHarness:RebuildSandboxPawn", ex, "An error occurred while rebuilding the sandbox pawn.");
                TeardownSandboxPawn();
            }
        }

        /// <summary>
        /// Synchronizes the sandbox pawn's health with the source colonist's authentic hediff graph.
        /// If the harness is bound to a generic species archetype, clears all hediffs to pristine health.
        /// </summary>
        /// <remarks>
        /// This method ensures that the sandbox pawn's health state mirrors that of the source pawn.
        /// Any missing body parts or hediffs are handled gracefully, and the health cache is marked dirty after synchronization.
        /// </remarks>
        public void SyncHediffsFromSource()
        {
            if (sandboxPawn?.health?.hediffSet == null) return;

            sandboxPawn.health.hediffSet.Clear();

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

                        if (src.Part != null && sandboxPawn.health.hediffSet.PartIsMissing(src.Part))
                        {
                            continue;
                        }

                        try
                        {
                            Hediff cloneHediff = HediffMaker.MakeHediff(src.def, sandboxPawn, src.Part);
                            if (cloneHediff != null)
                            {
                                cloneHediff.Severity = src.Severity;
                                sandboxPawn.health.AddHediff(cloneHediff);
                            }
                        }
                        catch
                        {
                            // Defensive barrier: A third-party mod hediff failed to clone.
                        }
                    }
                }
            }

            DirtySandboxHealthCache();
        }

        /// <summary>
        /// Safely sets the value of a specified field on the target object using reflection, handling any exceptions that may occur.
        /// </summary>
        /// <typeparam name="TTarget">The type of the target object on which the field is to be set.</typeparam>
        /// <typeparam name="TVal">The type of the value to be assigned to the field.</typeparam>
        /// <param name="target">The target object on which to set the field value.</param>
        /// <param name="fieldName">The name of the field to set.</param>
        /// <param name="value">The value to assign to the specified field.</param>
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
        /// Calculates the depth of the specified body part within the hierarchy of the pawn's body.
        /// </summary>
        /// <param name="part">The BodyPartRecord for which to calculate the depth.</param>
        /// <returns>An integer representing the depth of the body part within the hierarchy, with root parts having a depth of 0.</returns>
        private static int GetPartDepth(BodyPartRecord part)
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
        /// Resolves the appropriate PawnKindDef for the specified race, falling back to a default colonist kind if necessary.
        /// </summary>
        /// <param name="raceDef">The ThingDef representing the race for which to resolve the PawnKindDef.</param>
        /// <returns>The resolved PawnKindDef for the specified race, or a default colonist kind if no specific kind is found.</returns>
        private static PawnKindDef ResolvePawnKindForRace(ThingDef raceDef)
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

        #endregion

        #region 6. NATIVE SURGICAL MUTATION PIPELINE

        #region 6A. Artificial Replacement Procedures

        /// <summary>
        /// Simulates installing an artificial prosthetic or bionic replacement onto a specific BodyPartRecord.
        /// Flushes existing socket conflicts, then cleanly installs the genuine Hediff.
        /// </summary>
        /// <param name="part">The BodyPartRecord representing the part to simulate replacement on.</param>
        /// <param name="recipe">The RecipeDef representing the replacement procedure.</param>
        /// <param name="hediff">The HediffDef representing the replacement to be installed.</param>
        /// <param name="label">The display label for the replacement operation.</param>
        /// <param name="efficiency">The efficiency value for the replacement operation.</param>
        /// <remarks>
        /// This method ensures that any existing conflicting implants or replacements are removed before installing the new replacement.
        /// </remarks>
        public void SimulateReplacement(BodyPartRecord part, RecipeDef recipe, HediffDef hediff, string label, float efficiency)
        {
            if (sandboxPawn?.health == null || part == null || hediff == null) return;

            // 1. Flush existing conflicting socket states on target part and all absorbed child parts
            ImplantDeltas.Remove(part);
            if (part.parts != null)
            {
                for (int i = 0; i < part.parts.Count; i++)
                {
                    CleanDeltasRecursive(part.parts[i]);
                }
            }

            // 2. Clear socket cleanly without premature state changes
            sandboxPawn.health.RestorePart(part, null, checkStateChange: false);

            // 3. Install replacement hediff explicitly onto target socket
            Hediff appliedHediff = HediffMaker.MakeHediff(hediff, sandboxPawn, part);
            if (appliedHediff != null)
            {
                sandboxPawn.health.AddHediff(appliedHediff, part);
            }

            ReplacementDeltas[part] = new SimulatedModification
            {
                OpType = SimulatedOpType.InstallReplacement,
                SourceRecipe = recipe,
                TargetHediff = hediff,
                DisplayLabel = label,
                Efficiency = efficiency
            };

            DirtySandboxHealthCache();
        }

        #endregion

        #region 6B. Destructive Amputation & Trauma Simulation

        /// <summary>
        /// Simulates a surgical amputation or permanent limb loss (0% HP) on the sandbox pawn.
        /// Strictly reserved for structural appendages (Arms, Legs, Hands, Feet, Dual Limbs).
        /// </summary>
        /// <param name="part">The BodyPartRecord representing the part to simulate amputation on.</param>
        /// <remarks>
        /// This method ensures that the specified part is cleanly removed and marked as missing, simulating a surgical amputation.
        /// </remarks>
        public void SimulateAmputation(BodyPartRecord part)
        {
            if (sandboxPawn?.health == null || part == null) return;

            // 1. Clean child limb deltas so severed digits do not retain phantom states
            CleanDeltasRecursive(part);

            // 2. Clear socket cleanly without premature state changes
            sandboxPawn.health.RestorePart(part, null, checkStateChange: false);

            Hediff missingPartHediff = HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, sandboxPawn, part);
            if (missingPartHediff != null)
            {
                sandboxPawn.health.AddHediff(missingPartHediff, part);
            }

            ReplacementDeltas[part] = new SimulatedModification
            {
                OpType = SimulatedOpType.Amputate,
                SourceRecipe = null,
                TargetHediff = null,
                DisplayLabel = "OverHaulers_Op_Amputated".Translate().ToString(),
                Efficiency = 0f
            };

            DirtySandboxHealthCache();
        }

        /// <summary>
        /// Simulates severe structural trauma leaving exactly 1 HP remaining on non-limb bones, craniums, and organs.
        /// Skips zero-coverage virtual anchor parts (e.g. Waist / apparel hooks) to prevent vanilla hit-validation errors.
        /// </summary>
        /// <param name="part">The BodyPartRecord representing the part to simulate severe trauma on.</param>
        /// <remarks>
        /// This method ensures that the part is left with exactly 1 HP, simulating severe trauma without causing immediate destruction.
        /// </remarks>
        public void SimulateSevereTrauma(BodyPartRecord part)
        {
            // BREAKPOINT ANCHOR: Null & Zero-Coverage Part Guard
            if (sandboxPawn?.health == null || part == null || part.coverageAbs <= 0f) return;

            // 1. Flush existing conflicting socket states
            ImplantDeltas.Remove(part);
            ReplacementDeltas.Remove(part);

            // 2. Clear socket cleanly without premature state changes
            sandboxPawn.health.RestorePart(part, null, checkStateChange: false);

            float maxHp = part.def.GetMaxHealth(sandboxPawn);
            float damageToApply = Math.Max(1f, maxHp - 1f);

            // Resolve native Crush / Blunt injury HediffDef via DamageDef
            HediffDef injuryDef = DamageDefOf.Crush?.hediff 
                               ?? DamageDefOf.Blunt?.hediff 
                               ?? DamageDefOf.Cut?.hediff 
                               ?? DefDatabase<HediffDef>.GetNamedSilentFail("Crush") 
                               ?? DefDatabase<HediffDef>.GetNamedSilentFail("Bruise");

            if (injuryDef != null)
            {
                Hediff injury = HediffMaker.MakeHediff(injuryDef, sandboxPawn, part);
                if (injury != null)
                {
                    injury.Severity = damageToApply;
                    sandboxPawn.health.AddHediff(injury, part);
                }
            }

            ReplacementDeltas[part] = new SimulatedModification
            {
                OpType = SimulatedOpType.Trauma,
                SourceRecipe = null,
                TargetHediff = null,
                DisplayLabel = "OverHaulers_Op_SevereTrauma".Translate().ToString(),
                Efficiency = 1f / Math.Max(1f, maxHp)
            };

            DirtySandboxHealthCache();
        }

        #endregion

        #region 6C. Localized Implants & Systemic Drugs

        /// <summary>
        /// Simulates adding a non-replacing localized implant (e.g., Glands, Ribs) onto the sandbox pawn.
        /// </summary>
        /// <param name="part">The BodyPartRecord where the implant will be added.</param>
        /// <param name="recipe">The RecipeDef representing the implant procedure.</param>
        /// <param name="hediff">The HediffDef representing the implant to be added.</param>
        /// <param name="label">The display label for the implant operation.</param>
        /// <remarks>
        /// This method ensures that the implant is added only if the part is healthy or has been restored from trauma.
        /// </remarks>
        public void SimulateAddImplant(BodyPartRecord part, RecipeDef recipe, HediffDef hediff, string label)
        {
            if (sandboxPawn?.health == null || part == null || hediff == null) return;

            // If socket was missing or severely traumatized, restore health so implant can attach
            if (ReplacementDeltas.TryGetValue(part, out var existingRepl) && 
                (existingRepl.OpType == SimulatedOpType.Amputate || existingRepl.OpType == SimulatedOpType.Trauma))
            {
                ReplacementDeltas.Remove(part);
                sandboxPawn.health.RestorePart(part, null, checkStateChange: false);
            }

            if (!ImplantDeltas.TryGetValue(part, out var list))
            {
                list = new List<SimulatedModification>(4);
                ImplantDeltas[part] = list;
            }

            if (!list.Exists(m => m.TargetHediff == hediff))
            {
                Hediff appliedHediff = HediffMaker.MakeHediff(hediff, sandboxPawn, part);
                if (appliedHediff != null)
                {
                    sandboxPawn.health.AddHediff(appliedHediff, part);

                    list.Add(new SimulatedModification
                    {
                        OpType = SimulatedOpType.InstallImplant,
                        SourceRecipe = recipe,
                        TargetHediff = hediff,
                        DisplayLabel = label,
                        Efficiency = 1.0f
                    });
                }
            }

            DirtySandboxHealthCache();
        }

        /// <summary>
        /// Removes a simulated localized implant from the sandbox pawn.
        /// --- Reserved for granular implant removal, maybe ---
        /// </summary>
        public void SimulateRemoveImplant(BodyPartRecord part, HediffDef hediff)
        {
            if (sandboxPawn?.health == null || part == null || hediff == null) return;

            Hediff existing = sandboxPawn.health.hediffSet.hediffs.Find(h => h.def == hediff && h.Part == part);
            if (existing != null)
            {
                sandboxPawn.health.RemoveHediff(existing);
            }

            if (ImplantDeltas.TryGetValue(part, out var list))
            {
                list.RemoveAll(m => m.TargetHediff == hediff);
                if (list.Count == 0) ImplantDeltas.Remove(part);
            }

            DirtySandboxHealthCache();
        }

        /// <summary>
        /// Toggles a simulated systemic drug, combat stimulant, or whole-body condition on the sandbox pawn.
        /// </summary>
        public void SimulateToggleDrug(HediffDef drugDef)
        {
            if (sandboxPawn?.health == null || drugDef == null) return;

            if (ActiveSimulatedDrugs.Contains(drugDef))
            {
                ActiveSimulatedDrugs.Remove(drugDef);
                Hediff existing = sandboxPawn.health.hediffSet.GetFirstHediffOfDef(drugDef);
                if (existing != null)
                {
                    sandboxPawn.health.RemoveHediff(existing);
                }
            }
            else
            {
                ActiveSimulatedDrugs.Add(drugDef);
                Hediff drugHediff = HediffMaker.MakeHediff(drugDef, sandboxPawn);

                if (drugHediff != null)
                {
                    float peakSeverity = MedicalRecipeCatalog.GetPeakImpactSeverity(drugDef, out _);
                    if (peakSeverity > 0f)
                    {
                        drugHediff.Severity = peakSeverity;
                    }

                    sandboxPawn.health.AddHediff(drugHediff);
                }
            }

            DirtySandboxHealthCache();
        }
        #endregion

        #region 6D. Branch Reversion & Delta Cleaning
        /// <summary>
        /// Reverts a specific body part and all its descendant branches back to the authentic live health state.
        /// </summary>
        public void SimulateRevertPart(BodyPartRecord part)
        {
            if (sandboxPawn?.health == null || part == null) return;

            sandboxPawn.health.RestorePart(part, null, checkStateChange: false);

            if (sourcePawn?.health?.hediffSet != null)
            {
                List<Hediff> sourceHediffs = sourcePawn.health.hediffSet.hediffs;
                List<Hediff> relevantHediffs = new List<Hediff>();

                for (int i = 0; i < sourceHediffs.Count; i++)
                {
                    Hediff src = sourceHediffs[i];
                    if (src?.def != null && src.Part != null && IsPartOrDescendantOf(src.Part, part))
                    {
                        relevantHediffs.Add(src);
                    }
                }

                relevantHediffs.Sort((a, b) => GetPartDepth(a?.Part).CompareTo(GetPartDepth(b?.Part)));

                for (int i = 0; i < relevantHediffs.Count; i++)
                {
                    Hediff src = relevantHediffs[i];
                    if (src.Part != null && sandboxPawn.health.hediffSet.PartIsMissing(src.Part))
                    {
                        continue;
                    }

                    try
                    {
                        Hediff cloneHediff = HediffMaker.MakeHediff(src.def, sandboxPawn, src.Part);
                        if (cloneHediff != null)
                        {
                            cloneHediff.Severity = src.Severity;
                            sandboxPawn.health.AddHediff(cloneHediff);
                        }
                    }
                    catch { }
                }
            }

            CleanDeltasRecursive(part);
            DirtySandboxHealthCache();
        }

        private static bool IsPartOrDescendantOf(BodyPartRecord candidate, BodyPartRecord root)
        {
            BodyPartRecord curr = candidate;
            while (curr != null)
            {
                if (curr == root) return true;
                curr = curr.parent;
            }
            return false;
        }

        private void CleanDeltasRecursive(BodyPartRecord part)
        {
            if (part == null) return;
            ReplacementDeltas.Remove(part);
            ImplantDeltas.Remove(part);

            if (part.parts != null)
            {
                for (int i = 0; i < part.parts.Count; i++)
                {
                    CleanDeltasRecursive(part.parts[i]);
                }
            }
        }

        /// <summary>
        /// Clears all simulated surgical operations and drug regimens, restoring the sandbox completely to the clean/live baseline.
        /// </summary>
        public void ResetToSource()
        {
            ClearModifications();
            SyncHediffsFromSource();
        }

        protected void ClearModifications()
        {
            ReplacementDeltas.Clear();
            ImplantDeltas.Clear();
            ActiveSimulatedDrugs.Clear();
        }

        protected void DirtySandboxHealthCache()
        {
            if (sandboxPawn?.health != null)
            {
                sandboxPawn.health.capacities?.Clear();
                sandboxPawn.health.hediffSet?.DirtyCache();
            }
        }
        #endregion

        #endregion

        #region 7. TEARDOWN & DISPOSAL LIFECYCLE

        protected void TeardownSandboxPawn()
        {
            if (sandboxPawn != null)
            {
                sandboxPawn.health?.hediffSet?.Clear();
                sandboxPawn.health?.capacities?.Clear();
                sandboxPawn = null;
            }
        }

        /// <summary>
        /// Disposes of the sandbox pawn instance and flushes all tracking dictionaries.
        /// </summary>
        public void Dispose()
        {
            ClearModifications();
            TeardownSandboxPawn();
            sourcePawn = null;
            boundSubject = null;
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    #region 8. [TEST-05B] LIVE SANDBOX PAWN HARNESS (EXTENDED COMP RUNTIME)

    /// <summary>
    /// [TEST-05B] LIVE SANDBOX PAWN HARNESS (EXTENDED)
    /// Used exclusively when a game is active (Current.ProgramState == Playing).
    /// Inherits the sterile structure of the base harness but fully initializes ThingComps,
    /// providing third-party mods (like Vanilla Genetics Expanded) the initialized data they require.
    /// </summary>
    public class LiveSandboxPawnHarness : SandboxPawnHarness
    {
        public override void RebuildSandboxPawn()
        {
            base.RebuildSandboxPawn();

            if (sandboxPawn == null || sandboxPawn.def == null) return;

            try
            {
                sandboxPawn.InitializeComps();
            }
            catch (Exception ex)
            {
                OHLog.TestBench.Warn("LiveSandboxPawnHarness:InitializeComps", ex, $"Failed to initialize comps for sandbox pawn ({sandboxPawn.def.defName}).");
            }
        }
    }

    #endregion
}