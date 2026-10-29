using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. MODELS & ENUMS

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
    /// [TEST-05] INTERACTIVE SANDBOX PAWN HARNESS
    /// Extends Core <see cref="HeadlessPawnShell"/> with interactive surgical procedures,
    /// simulated condition toggling, and UI inspection delta tracking.
    /// Resides strictly under Source/Settings/TestBench/.
    /// </summary>
    public class SandboxPawnHarness : HeadlessPawnShell
    {
        #region 2. INSTANCE FIELDS & BACKING STATE

        /// <summary>The test subject entry currently bound to this harness.</summary>
        protected TestSubjectEntry boundSubject;

        /// <summary>Authentic live part efficiencies snapshotted from the source colonist upon binding.</summary>
        protected readonly Dictionary<BodyPartRecord, float> livePartEfficiencies =
            new Dictionary<BodyPartRecord, float>();

        /// <summary>Simulated replacement modifications (Prosthetics/Amputations/Trauma) indexed by target BodyPartRecord.</summary>
        public readonly Dictionary<BodyPartRecord, SimulatedModification> ReplacementDeltas =
            new Dictionary<BodyPartRecord, SimulatedModification>();

        /// <summary>Simulated localized implants (Glands, Ribs) indexed by target BodyPartRecord.</summary>
        public readonly Dictionary<BodyPartRecord, List<SimulatedModification>> ImplantDeltas =
            new Dictionary<BodyPartRecord, List<SimulatedModification>>();

        /// <summary>Active simulated systemic drugs, combat stimulants, and whole-body conditions.</summary>
        public readonly HashSet<HediffDef> ActiveSimulatedDrugs =
            new HashSet<HediffDef>();

        #endregion

        #region 3. PROPERTIES & ENCAPSULATED ACCESSORS

        /// <summary>Gets the active, unspawned sandbox pawn instance (alias to base Pawn).</summary>
        public Pawn SandboxPawn => headlessPawn;

        /// <summary>Gets the reference live colonist currently mirrored (null if generic archetype).</summary>
        public Pawn SourcePawn => sourcePawn;

        /// <summary>Gets the test subject entry currently blueprinting the harness.</summary>
        public TestSubjectEntry BoundSubject => boundSubject;

        /// <summary>True if any hypothetical modifications are currently applied to the sandbox pawn.</summary>
        public bool HasModifications => ReplacementDeltas.Count > 0 || ImplantDeltas.Count > 0 || ActiveSimulatedDrugs.Count > 0;

        #endregion

        #region 4. LIFECYCLE, FACTORIES & TEARDOWN

        /// <summary>Initializes a new unbound instance of the <see cref="SandboxPawnHarness"/> class.</summary>
        public SandboxPawnHarness()
        {
        }

        /// <summary>
        /// Canonical factory for an unbound interactive harness.
        /// Comp initialization is handled automatically by <see cref="HeadlessPawnShell"/> based on active program state.
        /// </summary>
        public static SandboxPawnHarness CreateForCurrentProgramState()
        {
            return new SandboxPawnHarness();
        }

        /// <summary>Tears down the sandbox pawn and clears modification tracking collections.</summary>
        public override void Teardown()
        {
            ClearModifications();
            boundSubject = null;
            base.Teardown();
        }

        #endregion

        #region 5. PUBLIC CANONICAL BINDING & REBUILD API

        /// <summary>
        /// Binds a test subject (live colonist or generic species) to the harness and builds an isolated sandbox pawn.
        /// Snapshots authentic live part efficiencies directly from the live pawn if bound to an active colonist.
        /// </summary>
        /// <param name="subject">The test subject entry to blueprint.</param>
        public void BindSubject(TestSubjectEntry subject)
        {
            boundSubject = subject;
            sourcePawn = subject?.IsLivePawn == true ? subject.LivePawn : null;

            ClearModifications();
            SnapshotLivePartEfficiencies();
            RebuildSandboxPawn();
        }

        /// <summary>Snapshots the genuine runtime part efficiencies from the authentic source colonist.</summary>
        private void SnapshotLivePartEfficiencies()
        {
            livePartEfficiencies.Clear();
            if (sourcePawn?.RaceProps?.body != null && sourcePawn.health?.hediffSet != null)
            {
                var allParts = sourcePawn.RaceProps.body.AllParts;
                for (int i = 0; i < allParts.Count; i++)
                {
                    BodyPartRecord part = allParts[i];
                    float defaultEff = 1.0f;
                    livePartEfficiencies[part] = MedicalClassifier.GetSafePartEfficiency(sourcePawn, part, defaultEff);
                }
            }
        }

        /// <summary>Rebuilds the underlying headless pawn shell from the currently bound test subject.</summary>
        public virtual void RebuildSandboxPawn()
        {
            if (boundSubject == null)
            {
                Teardown();
                return;
            }

            ThingDef raceDef = boundSubject.RaceDef ?? boundSubject.LivePawn?.def ?? ThingDefOf.Human;
            Pawn source = boundSubject.IsLivePawn ? boundSubject.LivePawn : null;
            bool initializeComps = Current.ProgramState == ProgramState.Playing;

            RebuildPawnCore(raceDef, source, initializeComps);
        }

        /// <summary>Clears all simulated surgical operations and drug regimens, restoring the sandbox to baseline.</summary>
        public void ResetToSource()
        {
            ClearModifications();
            SnapshotLivePartEfficiencies();
            SyncHediffsFromSource();
        }

        #endregion

        #region 6. SURGICAL SIMULATION PIPELINE

        #region 6A. ARTIFICIAL REPLACEMENT PROCEDURES

        /// <summary>
        /// Simulates installing an artificial prosthetic or bionic replacement onto a specific BodyPartRecord.
        /// Flushes existing socket conflicts, then cleanly installs the genuine Hediff.
        /// </summary>
        /// <param name="part">The BodyPartRecord representing the part to simulate replacement on.</param>
        /// <param name="recipe">The RecipeDef representing the replacement procedure.</param>
        /// <param name="hediff">The HediffDef representing the replacement to be installed.</param>
        /// <param name="label">The display label for the replacement operation.</param>
        /// <param name="efficiency">The efficiency value for the replacement operation.</param>
        public void SimulateReplacement(BodyPartRecord part, RecipeDef recipe, HediffDef hediff, string label, float efficiency)
        {
            if (headlessPawn?.health == null || part == null || hediff == null) return;

            ImplantDeltas.Remove(part);
            if (part.parts != null)
            {
                for (int i = 0; i < part.parts.Count; i++)
                {
                    CleanDeltasRecursive(part.parts[i]);
                }
            }

            headlessPawn.health.RestorePart(part, null, checkStateChange: false);

            Hediff appliedHediff = HediffMaker.MakeHediff(hediff, headlessPawn, part);
            if (appliedHediff != null)
            {
                headlessPawn.health.AddHediff(appliedHediff, part);
            }

            ReplacementDeltas[part] = new SimulatedModification
            {
                OpType = SimulatedOpType.InstallReplacement,
                SourceRecipe = recipe,
                TargetHediff = hediff,
                DisplayLabel = label,
                Efficiency = efficiency
            };

            DirtyHealthCache();
        }

        #endregion

        #region 6B. AMPUTATIONS & TRAUMA SIMULATION

        /// <summary>
        /// Simulates a surgical amputation or permanent limb loss (0% HP) on the sandbox pawn.
        /// Strictly reserved for structural appendages (Arms, Legs, Hands, Feet, Dual Limbs).
        /// </summary>
        /// <param name="part">The BodyPartRecord representing the part to simulate amputation on.</param>
        public void SimulateAmputation(BodyPartRecord part)
        {
            if (headlessPawn?.health == null || part == null) return;

            CleanDeltasRecursive(part);
            headlessPawn.health.RestorePart(part, null, checkStateChange: false);

            Hediff missingPartHediff = HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, headlessPawn, part);
            if (missingPartHediff != null)
            {
                headlessPawn.health.AddHediff(missingPartHediff, part);
            }

            ReplacementDeltas[part] = new SimulatedModification
            {
                OpType = SimulatedOpType.Amputate,
                SourceRecipe = null,
                TargetHediff = null,
                DisplayLabel = "OverHaulers_Op_Amputated".Translate().ToString(),
                Efficiency = 0f
            };

            DirtyHealthCache();
        }

        /// <summary>
        /// Simulates severe structural trauma leaving exactly 1 HP remaining on non-limb bones, craniums, and organs.
        /// Skips zero-coverage virtual anchor parts (e.g. Waist / apparel hooks) to prevent vanilla hit-validation errors.
        /// </summary>
        /// <param name="part">The BodyPartRecord representing the part to simulate severe trauma on.</param>
        public void SimulateSevereTrauma(BodyPartRecord part)
        {
            if (headlessPawn?.health == null || part == null || part.coverageAbs <= 0f) return;

            ImplantDeltas.Remove(part);
            ReplacementDeltas.Remove(part);
            headlessPawn.health.RestorePart(part, null, checkStateChange: false);

            float maxHp = part.def.GetMaxHealth(headlessPawn);
            float damageToApply = Math.Max(1f, maxHp - 1f);

            HediffDef injuryDef = DamageDefOf.Crush?.hediff
                               ?? DamageDefOf.Blunt?.hediff
                               ?? DamageDefOf.Cut?.hediff
                               ?? DefDatabase<HediffDef>.GetNamedSilentFail("Crush")
                               ?? DefDatabase<HediffDef>.GetNamedSilentFail("Bruise");

            if (injuryDef != null)
            {
                Hediff injury = HediffMaker.MakeHediff(injuryDef, headlessPawn, part);
                if (injury != null)
                {
                    injury.Severity = damageToApply;
                    headlessPawn.health.AddHediff(injury, part);
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

            DirtyHealthCache();
        }

        /// <summary>
        /// Applies a fixed, representative wound set to the bound subject: amputates one root arm and one root leg and leaves
        /// up to three torso parts on 1 HP, so the solver's deficit, symmetry and clamping branches are exercised.
        /// Used by the diagnostics benchmark and self-check so both measure the same "wounded" profile.
        /// </summary>
        public void SimulateRepresentativeWounds()
        {
            SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(boundSubject?.BodyDef);
            if (template == null) return;

            bool armDone = false;
            bool legDone = false;
            int traumaCount = 0;

            for (int i = 0; i < template.PartCount; i++)
            {
                PartType type = template.PartTypes[i];
                int parent = template.ParentIndices[i];
                bool isRoot = parent == -1 || template.PartTypes[parent] != type;
                BodyPartRecord part = template.IndexedParts[i];

                if (isRoot && type == PartType.ManipulationPart && !armDone)
                {
                    SimulateAmputation(part);
                    armDone = true;
                }
                else if (isRoot && type == PartType.MovingPart && !legDone)
                {
                    SimulateAmputation(part);
                    legDone = true;
                }
                else if (type == PartType.CorePart && parent != -1 && traumaCount < 3 && part.coverageAbs > 0f)
                {
                    SimulateSevereTrauma(part);
                    traumaCount++;
                }
            }
        }

        #endregion

        #region 6C. LOCALIZED IMPLANTS & SYSTEMIC DRUGS

        /// <summary>
        /// Simulates adding a non-replacing localized implant (e.g. Glands, Ribs) onto the sandbox pawn.
        /// </summary>
        /// <param name="part">The BodyPartRecord where the implant will be added.</param>
        /// <param name="recipe">The RecipeDef representing the implant procedure.</param>
        /// <param name="hediff">The HediffDef representing the implant to be added.</param>
        /// <param name="label">The display label for the implant operation.</param>
        public void SimulateAddImplant(BodyPartRecord part, RecipeDef recipe, HediffDef hediff, string label)
        {
            if (headlessPawn?.health == null || part == null || hediff == null) return;

            if (ReplacementDeltas.TryGetValue(part, out var existingRepl) &&
                (existingRepl.OpType == SimulatedOpType.Amputate || existingRepl.OpType == SimulatedOpType.Trauma))
            {
                ReplacementDeltas.Remove(part);
                headlessPawn.health.RestorePart(part, null, checkStateChange: false);
            }

            if (!ImplantDeltas.TryGetValue(part, out var list))
            {
                list = new List<SimulatedModification>(4);
                ImplantDeltas[part] = list;
            }

            if (!list.Exists(m => m.TargetHediff == hediff))
            {
                Hediff appliedHediff = HediffMaker.MakeHediff(hediff, headlessPawn, part);
                if (appliedHediff != null)
                {
                    headlessPawn.health.AddHediff(appliedHediff, part);

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

            DirtyHealthCache();
        }

        /// <summary>
        /// Removes a simulated localized implant from the sandbox pawn.
        /// --- Reserved for granular implant removal, maybe ---
        /// </summary>
        /// <param name="part">The BodyPartRecord from which to remove the implant.</param>
        /// <param name="hediff">The HediffDef representing the implant to remove.</param>
        public void SimulateRemoveImplant(BodyPartRecord part, HediffDef hediff)
        {
            if (headlessPawn?.health == null || part == null || hediff == null) return;

            Hediff existing = headlessPawn.health.hediffSet.hediffs.Find(h => h.def == hediff && h.Part == part);
            if (existing != null)
            {
                headlessPawn.health.RemoveHediff(existing);
            }

            if (ImplantDeltas.TryGetValue(part, out var list))
            {
                list.RemoveAll(m => m.TargetHediff == hediff);
                if (list.Count == 0) ImplantDeltas.Remove(part);
            }

            DirtyHealthCache();
        }

        /// <summary>
        /// Toggles a simulated systemic drug, combat stimulant, or whole-body condition on the sandbox pawn.
        /// </summary>
        /// <param name="drugDef">The drug definition representing the condition to toggle.</param>
        public void SimulateToggleDrug(HediffDef drugDef)
        {
            if (headlessPawn?.health == null || drugDef == null) return;

            if (ActiveSimulatedDrugs.Contains(drugDef))
            {
                ActiveSimulatedDrugs.Remove(drugDef);
                Hediff existing = headlessPawn.health.hediffSet.GetFirstHediffOfDef(drugDef);
                if (existing != null)
                {
                    headlessPawn.health.RemoveHediff(existing);
                }
            }
            else
            {
                ActiveSimulatedDrugs.Add(drugDef);
                Hediff drugHediff = HediffMaker.MakeHediff(drugDef, headlessPawn);

                if (drugHediff != null)
                {
                    float peakSeverity = MedicalRecipeCatalog.GetPeakImpactSeverity(drugDef, out _);
                    if (peakSeverity > 0f)
                    {
                        drugHediff.Severity = peakSeverity;
                    }

                    headlessPawn.health.AddHediff(drugHediff);
                }
            }

            DirtyHealthCache();
        }

        #endregion

        #region 6D. BRANCH REVERSION

        /// <summary>
        /// Reverts a specific body part and all its descendant branches back to the authentic live health state.
        /// </summary>
        /// <param name="part">The body part to revert to the authentic live health state.</param>
        public void SimulateRevertPart(BodyPartRecord part)
        {
            if (headlessPawn?.health == null || part == null) return;

            headlessPawn.health.RestorePart(part, null, checkStateChange: false);

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
                            headlessPawn.health.AddHediff(cloneHediff);
                        }
                    }
                    catch { }
                }
            }

            CleanDeltasRecursive(part);
            DirtyHealthCache();
        }

        #endregion

        #endregion

        #region 7. PRIVATE HELPERS & DELTA CLEANING

        /// <summary>
        /// Determines whether the specified candidate body part is the same as or a descendant of the given root body part.
        /// </summary>
        /// <param name="candidate">The body part to check.</param>
        /// <param name="root">The root body part to compare against.</param>
        /// <returns>True if the candidate is the same as or a descendant of the root; otherwise, false.</returns>
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

        /// <summary>
        /// Recursively removes replacement and implant deltas for the specified body part and all its descendants.
        /// </summary>
        /// <param name="part">The root body part for which to clean deltas.</param>
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
        /// Clears all active modification tracking collections.
        /// </summary>
        protected void ClearModifications()
        {
            ReplacementDeltas.Clear();
            ImplantDeltas.Clear();
            ActiveSimulatedDrugs.Clear();
        }

        #endregion

        #region 8. TEST BENCH DATA SOURCE & EVALUATION ACCESSORS

        /// <summary>
        /// Resolves the effective biological or prosthetic efficiency for a body part on the sandbox pawn.
        /// Simulates rated surgical potential for user-applied procedures while preserving authentic live state for unmodified parts.
        /// </summary>
        public float GetEffectivePartEfficiency(BodyPartRecord part, float defaultRatedEfficiency)
        {
            if (part == null) return defaultRatedEfficiency;

            // 1. If explicitly modified by a simulated operation in the harness, use simulated surgical potential
            if (ReplacementDeltas.TryGetValue(part, out var mod))
            {
                return mod.Efficiency;
            }

            // 2. If unmodified and cloned from an active live colonist, return the authentic snapshotted live rating
            if (livePartEfficiencies.TryGetValue(part, out float liveEff))
            {
                return liveEff;
            }

            // 3. Otherwise (clean species archetype), return rated biological potential
            return defaultRatedEfficiency;
        }

        /// <summary>
        /// Resolves the effective performance level for a systemic pawn capacity.
        /// When unmodified, mirrors the authentic source colonist's capacities (e.g. 50% manipulation during limb training).
        /// </summary>
        public float GetEffectiveCapacityLevel(PawnCapacityDef capacity)
        {
            if (!HasModifications && sourcePawn != null)
            {
                return MedicalClassifier.GetCapacityLevel(sourcePawn, capacity);
            }

            return MedicalClassifier.GetCapacityLevel(headlessPawn, capacity);
        }

        /// <summary>
        /// Creates an unboxed struct data source adapting this harness into the solver pipeline.
        /// </summary>
        public SandboxAnatomicalSource CreateDataSource()
        {
            return new SandboxAnatomicalSource(this);
        }

        #endregion
    }

    /// <summary>
    /// [TEST-05] Specialized stack-allocated anatomical data source adapting the interactive sandbox harness.
    /// Bridges unmodified live colonist snapshots and simulated surgical operations into the zero-allocation solver.
    /// </summary>
    public readonly struct SandboxAnatomicalSource : IAnatomicalDataSource
    {
        #region 1. FIELDS & CONSTRUCTOR

        private readonly SandboxPawnHarness harness;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SandboxAnatomicalSource(SandboxPawnHarness harness)
        {
            this.harness = harness;
        }

        #endregion

        #region 2. PROPERTIES & ENCAPSULATED ACCESSORS

        public int EntityId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => harness?.SandboxPawn?.thingIDNumber ?? 0;
        }

        public string EntityLabel => harness?.BoundSubject?.Label ?? "SandboxPawn";

        public BodyDef BodyDef
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => harness?.BoundSubject?.BodyDef ?? harness?.SandboxPawn?.RaceProps?.body;
        }

        public float BaseBodySize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MedicalClassifier.GetSafeBodySize(harness?.SandboxPawn);
        }

        public bool IsValidBiologicalState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => harness != null && harness.IsValid;
        }

        #endregion

        #region 3. INGRESS & CAPACITY METHODS

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCapacityLevel(PawnCapacityDef capacity)
        {
            return harness != null ? harness.GetEffectiveCapacityLevel(capacity) : 1.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetPartEfficiency(BodyPartRecord part, float defaultRatedEfficiency)
        {
            return harness != null ? harness.GetEffectivePartEfficiency(part, defaultRatedEfficiency) : defaultRatedEfficiency;
        }

        /// <summary>
        /// Resolves the maximum health points for a body part on the sandbox pawn or source colonist.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetPartMaxHealth(BodyPartRecord part)
        {
            if (part == null || harness == null) return 1.0f;

            Pawn targetPawn = (!harness.HasModifications && harness.SourcePawn != null)
                ? harness.SourcePawn
                : harness.SandboxPawn;

            if (targetPawn == null) return 1.0f;
            return part.def.GetMaxHealth(targetPawn);
        }

        public void IngressPathology(AnatomicalWorkspace workspace, bool populateColdMetadata)
        {
            if (harness == null) return;

            // When no simulated operations are applied, ingress authentic live hediffs directly from source
            Pawn targetPawn = (!harness.HasModifications && harness.SourcePawn != null)
                ? harness.SourcePawn
                : harness.SandboxPawn;

            List<Hediff> safeHediffs = targetPawn?.health?.hediffSet?.hediffs;
            if (safeHediffs != null)
            {
                AnatomicalIngressSolver.IngressLivePawnPathology(targetPawn, safeHediffs, workspace, populateColdMetadata, this);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ResolveBaselineCapacity()
        {
            if (harness?.SandboxPawn == null) return 0f;
            return SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
        }

        #endregion
    }
}