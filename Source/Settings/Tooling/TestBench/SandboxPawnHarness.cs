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
        InstallImplant = 1
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

    /// <summary>Tracks a native worker-backed body-part state change in the sandbox.</summary>
    public sealed class SimulatedPartStateChange
    {
        public readonly BodyPartRecord Part;
        public readonly RecipeDef Recipe;
        public readonly string DisplayLabel;
        public readonly bool IsRemoval;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimulatedPartStateChange"/> class.
        /// </summary>
        /// <param name="part">The body part affected by the state change.</param>
        /// <param name="recipe">The RecipeDef representing the procedure applied.</param>
        /// <param name="displayLabel">The display label for the operation.</param>
        /// <param name="isRemoval">Indicates if the operation is a removal.</param>
        public SimulatedPartStateChange(
            BodyPartRecord part,
            RecipeDef recipe,
            string displayLabel,
            bool isRemoval)
        {
            Part = part;
            Recipe = recipe;
            DisplayLabel = displayLabel;
            IsRemoval = isRemoval;
        }
    }

    /// <summary>A simulated condition and the state needed to undo it without removing a pre-existing hediff.</summary>
    public sealed class SimulatedCondition
    {
        public readonly Hediff Hediff;
        internal readonly bool WasExisting;
        internal readonly float OriginalSeverity;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimulatedCondition"/> class.
        /// </summary>
        /// <param name="hediff">The health condition being simulated.</param>
        /// <param name="wasExisting">Indicates if the condition was pre-existing.</param>
        /// <param name="originalSeverity">The original severity of the condition.</param>
        internal SimulatedCondition(Hediff hediff, bool wasExisting, float originalSeverity)
        {
            Hediff = hediff;
            WasExisting = wasExisting;
            OriginalSeverity = originalSeverity;
        }
    }

    #endregion

    /// <summary>
    /// [TEST-05] INTERACTIVE SANDBOX PAWN HARNESS
    /// Extends Core <see cref="HeadlessPawnShell"/> with interactive surgical procedures,
    /// simulated condition toggling, and UI inspection delta tracking.
    /// Resides under Source/Settings/Tooling/TestBench/.
    /// </summary>
    public partial class SandboxPawnHarness
        : HeadlessPawnShell
    {
        #region 2. INSTANCE FIELDS & BACKING STATE

        /// <summary>The test subject entry currently bound to this harness.</summary>
        protected TestSubjectEntry boundSubject;

        /// <summary>Authentic live part efficiencies snapshotted from the source colonist upon binding.</summary>
        protected readonly Dictionary<BodyPartRecord, float> livePartEfficiencies =
            new Dictionary<BodyPartRecord, float>();

        /// <summary>Native/modded final-efficiency scale relative to the cloned headless shell.</summary>
        private readonly Dictionary<BodyPartRecord, float> livePartEfficiencyScales =
            new Dictionary<BodyPartRecord, float>();

        /// <summary>Simulated prosthetic and bionic replacement modifications indexed by target BodyPartRecord.</summary>
        public readonly Dictionary<BodyPartRecord, SimulatedModification> ReplacementDeltas =
            new Dictionary<BodyPartRecord, SimulatedModification>();

        /// <summary>Simulated localized implants (Glands, Ribs) indexed by target BodyPartRecord.</summary>
        public readonly Dictionary<BodyPartRecord, List<SimulatedModification>> ImplantDeltas =
            new Dictionary<BodyPartRecord, List<SimulatedModification>>();

        /// <summary>Worker-backed removals/restorations indexed by target body part.</summary>
        public readonly Dictionary<BodyPartRecord, SimulatedPartStateChange> PartStateDeltas =
            new Dictionary<BodyPartRecord, SimulatedPartStateChange>();

        /// <summary>Simulated health conditions applied to the sandbox pawn.</summary>
        private readonly List<SimulatedCondition> simulatedConditions = new List<SimulatedCondition>();

        /// <summary>Generation captured by live plans to detect sandbox changes after review.</summary>
        private int liveIntentGeneration;

        #endregion

        #region 3. PROPERTIES & ENCAPSULATED ACCESSORS

        /// <summary>Gets the active, unspawned sandbox pawn instance (alias to base Pawn).</summary>
        public Pawn SandboxPawn => headlessPawn;

        /// <summary>Gets the reference live colonist currently mirrored (null if generic archetype).</summary>
        public Pawn SourcePawn => sourcePawn;

        /// <summary>Gets the test subject entry currently blueprinting the harness.</summary>
        public TestSubjectEntry BoundSubject => boundSubject;

        /// <summary>Conditions applied to the sandbox, including their individual target parts.</summary>
        public IReadOnlyList<SimulatedCondition> SimulatedConditions => simulatedConditions;

        /// <summary>Generation captured by live plans to detect sandbox changes after review.</summary>
        public int LiveIntentGeneration => liveIntentGeneration;

        /// <summary>True if any hypothetical modifications are currently applied to the sandbox pawn.</summary>
        public bool HasModifications =>
            ReplacementDeltas.Count > 0 ||
            ImplantDeltas.Count > 0 ||
            DamageDeltas.Count > 0 ||
            PartStateDeltas.Count > 0 ||
            simulatedConditions.Count > 0;

        #endregion

        #region 4. LIFECYCLE, FACTORIES & TEARDOWN

        /// <summary>Initializes a new unbound instance of the <see cref="SandboxPawnHarness"/> class.</summary>
        public SandboxPawnHarness()
        {
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
            RebuildSandboxPawn();
            SnapshotLivePartEfficiencies();
        }

        /// <summary>Snapshots the genuine runtime part efficiencies from the authentic source colonist.</summary>
        private void SnapshotLivePartEfficiencies()
        {
            livePartEfficiencies.Clear();
            livePartEfficiencyScales.Clear();
            if (sourcePawn?.RaceProps?.body != null && sourcePawn.health?.hediffSet != null)
            {
                var allParts = sourcePawn.RaceProps.body.AllParts;
                for (int i = 0; i < allParts.Count; i++)
                {
                    BodyPartRecord part = allParts[i];
                    Hediff_AddedPart addedPart = sourcePawn.health.hediffSet.GetFirstHediffMatchingPart<Hediff_AddedPart>(part);
                    float defaultEff = addedPart?.def?.addedPartProps?.partEfficiency ?? 1.0f;
                    float finalEfficiency = MedicalClassifier.GetFinalPartEfficiency(sourcePawn, part, defaultEff);
                    livePartEfficiencies[part] = finalEfficiency;

                    float headlessEfficiency = headlessPawn?.health?.hediffSet != null
                        ? PawnCapacityUtility.CalculatePartEfficiency(
                            headlessPawn.health.hediffSet,
                            part,
                            ignoreAddedParts: false)
                        : defaultEff;
                    livePartEfficiencyScales[part] =
                        Math.Abs(headlessEfficiency) > PawnDataRegistry.EfficiencyEpsilon
                            ? finalEfficiency / headlessEfficiency
                            : 1.0f;
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
            SyncHediffsFromSource();
            SnapshotLivePartEfficiencies();
        }

        /// <summary>
        /// Simulates a native removal-recipe outcome for the selected part.
        /// </summary>
        /// <param name="part">The body part to be removed.</param>
        /// <param name="recipe">The RecipeDef representing the removal procedure.</param>
        /// <param name="label">The display label for the removal operation.</param>
        public void SimulateRecipePartRemoval(BodyPartRecord part, RecipeDef recipe, string label)
        {
            if (headlessPawn?.health?.hediffSet == null || part == null || !(recipe?.Worker is Recipe_RemoveBodyPart))
                throw new InvalidOperationException("A supported body-part removal recipe and target are required.");

            using (EnterSafetyFallbackScope())
            {
                CleanDeltasRecursive(part);
                headlessPawn.health.RestorePart(part, null, checkStateChange: false);
                Hediff missing = HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, headlessPawn, part);
                if (missing == null)
                    throw new InvalidOperationException("Could not create the sandbox missing-part state.");
                headlessPawn.health.AddHediff(missing, part);
                PartStateDeltas[part] = new SimulatedPartStateChange(part, recipe, label, true);
                PruneRemovedConditions();
                DirtyHealthCache();
                liveIntentGeneration++;
            }
        }

        /// <summary>
        /// Simulates a native natural-part restoration outcome for the selected missing part.
        /// </summary>
        /// <param name="part">The missing body part to be restored.</param>
        /// <param name="recipe">The RecipeDef representing the restoration procedure.</param>
        /// <param name="label">The display label for the restoration operation.</param>
        public void SimulateRecipeNaturalRestoration(BodyPartRecord part, RecipeDef recipe, string label)
        {
            if (headlessPawn?.health?.hediffSet == null || part == null ||
                !(recipe?.Worker is Recipe_InstallNaturalBodyPart))
                throw new InvalidOperationException("A supported natural-part restoration recipe and target are required.");

            using (EnterSafetyFallbackScope())
            {
                ClearSimulatedDamage(part);
                CleanDeltasRecursive(part);
                headlessPawn.health.RestorePart(part, null, checkStateChange: false);
                PartStateDeltas[part] = new SimulatedPartStateChange(part, recipe, label, false);
                PruneRemovedConditions();
                DirtyHealthCache();
                liveIntentGeneration++;
            }
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

            using (EnterSafetyFallbackScope())
            {
                DamageDeltas.Remove(part);
                ImplantDeltas.Remove(part);
                PartStateDeltas.Remove(part);
                if (part.parts != null)
                {
                    for (int i = 0; i < part.parts.Count; i++)
                    {
                        CleanDeltasRecursive(part.parts[i]);
                    }
                }

                headlessPawn.health.RestorePart(part, null, checkStateChange: false);
                PruneRemovedConditions();

                bool added = false;
                try
                {
                    Hediff appliedHediff = HediffMaker.MakeHediff(hediff, headlessPawn, part);
                    if (appliedHediff != null)
                    {
                        headlessPawn.health.AddHediff(appliedHediff, part);
                        added = true;
                    }
                }
                catch (Exception ex)
                {
                    OHLog.Warn("TestBench.SimulateReplacement", ex, $"Could not simulate replacement '{hediff.defName}' on '{part.Label}'.");
                }
                PruneRemovedConditions();

                if (added)
                {
                    ReplacementDeltas[part] = new SimulatedModification
                    {
                        OpType = SimulatedOpType.InstallReplacement,
                        SourceRecipe = recipe,
                        TargetHediff = hediff,
                        DisplayLabel = label,
                        Efficiency = efficiency
                    };
                    liveIntentGeneration++;
                }

                DirtyHealthCache();
            }
        }

        #endregion

        #region 6B. REPRESENTATIVE NATIVE DAMAGE PROFILE

        /// <summary>
        /// Applies a fixed, representative native wound set to the bound subject: removes one root arm and one root leg
        /// and damages up to three torso parts, exercising the solver's deficit, symmetry and clamping branches.
        /// Used by the diagnostics benchmark and self-check so both measure the same "wounded" profile.
        /// </summary>
        public void SimulateRepresentativeWounds()
        {
            SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(boundSubject?.BodyDef);
            if (template == null) return;

            using (EnterSafetyFallbackScope())
            {
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
                        SimulatePartRemoval(part);
                        armDone = true;
                    }
                    else if (isRoot && type == PartType.MovingPart && !legDone)
                    {
                        SimulatePartRemoval(part);
                        legDone = true;
                    }
                    else if (type == PartType.CorePart && parent != -1 && traumaCount < 3 && part.coverageAbs > 0f)
                    {
                        SimulateDamage(part, DamageDefOf.Crush, 0.9f);
                        traumaCount++;
                    }
                }
            }
        }

        #endregion

        #region 6C. LOCALIZED IMPLANTS & SYSTEMIC CONDITIONS

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

            using (EnterSafetyFallbackScope())
            {
                if (PartStateDeltas.TryGetValue(part, out SimulatedPartStateChange partState) &&
                    partState.IsRemoval)
                {
                    PartStateDeltas.Remove(part);
                }
                if (!ImplantDeltas.TryGetValue(part, out var list))
                {
                    list = new List<SimulatedModification>(4);
                    ImplantDeltas[part] = list;
                }

                if (!list.Exists(m => m.TargetHediff == hediff))
                {
                    try
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
                            liveIntentGeneration++;
                        }
                    }
                    catch (Exception ex)
                    {
                        OHLog.Warn("TestBench.SimulateAddImplant", ex, $"Could not simulate implant '{hediff.defName}' on '{part.Label}'.");
                        if (list.Count == 0) ImplantDeltas.Remove(part);
                    }
                }

                DirtyHealthCache();
            }
        }

        /// <summary>
        /// Returns whether a present, natural part can receive a simulated condition.
        /// </summary>
        /// <param name="pawn">The sandbox pawn to check.</param>
        /// <param name="part">The body part to check for condition applicability.</param>
        public static bool CanApplyConditionToPart(Pawn pawn, BodyPartRecord part)
        {
            return MedicalCatalog.CanApplyConditionToPart(pawn, part);
        }

        /// <summary>
        /// Finds an active simulated condition on the specified target, if present.
        /// </summary>
        /// <param name="def">The HediffDef representing the condition to find.</param>
        /// <param name="part">The body part to which the condition should be applied, or null for the whole body.</param>
        public SimulatedCondition FindSimulatedCondition(HediffDef def, BodyPartRecord part)
        {
            for (int i = 0; i < simulatedConditions.Count; i++)
            {
                Hediff active = simulatedConditions[i].Hediff;
                if (active.def == def && active.Part == part) return simulatedConditions[i];
            }
            return null;
        }

        /// <summary>
        /// Applies a condition to the whole body or a specific part of the sandbox pawn.
        /// </summary>
        /// <param name="def">The HediffDef representing the condition to apply.</param>
        /// <param name="part">The body part to which the condition should be applied, or null for the whole body.</param>
        public void SimulateCondition(HediffDef def, BodyPartRecord part)
        {
            if (headlessPawn?.health?.hediffSet == null || def == null)
                throw new InvalidOperationException("A bound sandbox pawn and condition are required.");
            if (part != null && !CanApplyConditionToPart(headlessPawn, part))
                throw new InvalidOperationException("The selected body part cannot receive a simulated condition.");

            if (FindSimulatedCondition(def, part) != null)
                throw new InvalidOperationException("The simulated condition is already active on this target.");

            using (EnterSafetyFallbackScope())
            {
                List<Hediff> hediffs = headlessPawn.health.hediffSet.hediffs;
                Hediff target = null;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i].def == def && hediffs[i].Part == part)
                    {
                        target = hediffs[i];
                        break;
                    }
                }

                bool wasExisting = target != null;
                float originalSeverity = wasExisting ? target.Severity : 0f;
                if (!wasExisting)
                {
                    target = HediffMaker.MakeHediff(def, headlessPawn, part);
                    if (target == null)
                        throw new InvalidOperationException("Could not create the simulated condition " + def.defName + ".");
                }

                float severity = MedicalCatalog.GetPeakImpactSeverity(def, out _);
                if (severity > 0f) target.Severity = severity;
                if (!wasExisting)
                {
                    headlessPawn.health.AddHediff(target, part);
                    if (!hediffs.Contains(target))
                        throw new InvalidOperationException("The simulated condition was not retained: " + def.defName + ".");
                }

                simulatedConditions.Add(new SimulatedCondition(target, wasExisting, originalSeverity));
                liveIntentGeneration++;
                DirtyHealthCache();
            }
        }

        /// <summary>
        /// Removes a simulated condition, restoring its original severity if it existed beforehand.
        /// </summary>
        /// <param name="condition">The simulated condition to remove.</param>
        public void RemoveSimulatedCondition(SimulatedCondition condition)
        {
            if (condition == null || !simulatedConditions.Contains(condition))
                throw new InvalidOperationException("The simulated condition is not active.");

            using (EnterSafetyFallbackScope())
            {
                if (headlessPawn.health.hediffSet.hediffs.Contains(condition.Hediff))
                {
                    if (condition.WasExisting)
                        condition.Hediff.Severity = condition.OriginalSeverity;
                    else
                        headlessPawn.health.RemoveHediff(condition.Hediff);
                }

                simulatedConditions.Remove(condition);
                liveIntentGeneration++;
                DirtyHealthCache();
            }
        }

        /// <summary>
        /// Returns defined severity bounds for conditions with multiple stages.
        /// </summary>
        /// <param name="condition">The simulated condition to evaluate.</param>
        /// <param name="min">Outputs the minimum severity for the condition's stages.</param>
        /// <param name="max">Outputs the maximum severity for the condition's stages.</param>
        public static bool TryGetConditionSeverityRange(SimulatedCondition condition, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            HediffDef def = condition?.Hediff?.def;
            if (def?.stages == null || def.stages.Count < 2) return false;
            min = def.minSeverity;
            max = def.maxSeverity;
            return !float.IsNaN(min) && !float.IsInfinity(min) &&
                !float.IsNaN(max) && !float.IsInfinity(max) &&
                max < float.MaxValue && max > min &&
                condition.Hediff.Severity >= min && condition.Hediff.Severity <= max;
        }

        /// <summary>
        /// Returns a reachable severity at the start of a condition's defined stage.
        /// </summary>
        /// <param name="condition">The simulated condition to evaluate.</param>
        /// <param name="stageIndex">The index of the stage to check.</param>
        /// <param name="severity">Outputs the reachable severity at the start of the specified stage.</param>
        public static bool TryGetConditionStageSeverity(SimulatedCondition condition, int stageIndex, out float severity)
        {
            severity = 0f;
            HediffDef def = condition?.Hediff?.def;
            if (def?.stages == null || stageIndex < 0 || stageIndex >= def.stages.Count ||
                def.stages[stageIndex] == null) return false;

            severity = Math.Max(def.minSeverity, def.stages[stageIndex].minSeverity);
            if (float.IsNaN(severity) || float.IsInfinity(severity) || severity > def.maxSeverity)
                return false;
            return stageIndex + 1 >= def.stages.Count || def.stages[stageIndex + 1] == null ||
                severity < def.stages[stageIndex + 1].minSeverity;
        }

        /// <summary>
        /// Sets the severity of an active condition within its defined bounds.
        /// </summary>
        /// <param name="condition">The simulated condition to modify.</param>
        /// <param name="severity">The new severity value to set.</param>
        public void SetConditionSeverity(SimulatedCondition condition, float severity)
        {
            if (!simulatedConditions.Contains(condition))
                throw new InvalidOperationException("The simulated condition is not active.");

            HediffDef def = condition.Hediff.def;
            if (float.IsNaN(severity) || float.IsInfinity(severity) ||
                severity < def.minSeverity || severity > def.maxSeverity)
                throw new ArgumentOutOfRangeException(nameof(severity), "The condition severity is outside its defined range.");

            using (EnterSafetyFallbackScope())
            {
                condition.Hediff.Severity = severity;
                liveIntentGeneration++;
                DirtyHealthCache();
            }
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

            using (EnterSafetyFallbackScope())
            {
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

                        TryCloneAndAddHediff(src);
                    }
                }

                CleanDeltasRecursive(part);
                PruneRemovedConditions();
                DirtyHealthCache();
                liveIntentGeneration++;
            }
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
            DamageDeltas.Remove(part);
            PartStateDeltas.Remove(part);

            if (part.parts != null)
            {
                for (int i = 0; i < part.parts.Count; i++)
                {
                    CleanDeltasRecursive(part.parts[i]);
                }
            }
        }

        /// <summary>
        /// Removes simulated conditions that no longer exist on the sandbox pawn.
        /// </summary>
        private void PruneRemovedConditions()
        {
            List<Hediff> hediffs = headlessPawn.health.hediffSet.hediffs;
            for (int i = simulatedConditions.Count - 1; i >= 0; i--)
            {
                if (!hediffs.Contains(simulatedConditions[i].Hediff))
                    simulatedConditions.RemoveAt(i);
            }
        }

        /// <summary>
        /// Clears all active modification tracking collections.
        /// </summary>
        protected void ClearModifications()
        {
            ReplacementDeltas.Clear();
            ImplantDeltas.Clear();
            DamageDeltas.Clear();
            PartStateDeltas.Clear();
            simulatedConditions.Clear();
            liveIntentGeneration++;
        }

        #endregion

        #region 8. TEST BENCH DATA SOURCE & EVALUATION ACCESSORS

        /// <summary>
        /// Resolves the effective biological or prosthetic efficiency for a body part on the sandbox pawn.
        /// Resolves native current efficiency for damaged/simulated parts and preserves authentic live state for untouched parts.
        /// </summary>
        /// <param name="part">The body part for which to resolve the effective efficiency.</param>
        /// <param name="defaultRatedEfficiency">The default rated efficiency for the body part, used if no live or modified data is available.</param>
        public float GetEffectivePartEfficiency(BodyPartRecord part, float defaultRatedEfficiency)
        {
            if (part == null) return defaultRatedEfficiency;

            if ((ReplacementDeltas.ContainsKey(part) || DamageDeltas.ContainsKey(part)) &&
                headlessPawn?.health?.hediffSet != null)
            {
                float currentEfficiency = PawnCapacityUtility.CalculatePartEfficiency(
                    headlessPawn.health.hediffSet,
                    part,
                    ignoreAddedParts: false);

                if (!HasSyntheticReplacementAtOrAbove(part) &&
                    !HasPendingNaturalRestorationAtOrAbove(part) &&
                    livePartEfficiencyScales.TryGetValue(part, out float scale))
                {
                    currentEfficiency *= scale;
                }
                return currentEfficiency;
            }

            // 1. If unmodified and cloned from an active live colonist, return the authentic snapshotted live rating
            if (livePartEfficiencies.TryGetValue(part, out float liveEff))
            {
                return liveEff;
            }

            // 2. Otherwise (clean species archetype), return rated biological potential
            return defaultRatedEfficiency;
        }

        private bool HasSyntheticReplacementAtOrAbove(BodyPartRecord part)
        {
            for (BodyPartRecord cursor = part; cursor != null; cursor = cursor.parent)
            {
                if (ReplacementDeltas.ContainsKey(cursor)) return true;
            }
            return false;
        }

        private bool HasPendingNaturalRestorationAtOrAbove(BodyPartRecord part)
        {
            for (BodyPartRecord cursor = part; cursor != null; cursor = cursor.parent)
            {
                if (PartStateDeltas.TryGetValue(cursor, out SimulatedPartStateChange state) &&
                    !state.IsRemoval)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Resolves the effective performance level for a systemic pawn capacity.
        /// When unmodified, mirrors the authentic source colonist's capacities (e.g. 50% manipulation during limb training).
        /// </summary>
        /// <param name="capacity">The systemic pawn capacity to evaluate.</param>
        /// <returns>The effective capacity level, considering both live state and simulated modifications.</returns>
        public float GetEffectiveCapacityLevel(PawnCapacityDef capacity)
        {
            if (!HasModifications && sourcePawn != null)
            {
                return MedicalClassifier.GetCapacityLevel(sourcePawn, capacity);
            }

            using (EnterSafetyFallbackScope())
            {
                return MedicalClassifier.GetCapacityLevel(headlessPawn, capacity);
            }
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
    /// [TEST-05] Value-type anatomical data source adapting the interactive sandbox harness.
    /// Bridges unmodified live colonist snapshots and simulated surgical operations into the solver.
    /// </summary>
    public readonly struct SandboxAnatomicalSource : IAnatomicalDataSource
    {
        #region 1. FIELDS & PROPERTIES

        /// <summary>The sandbox pawn harness being adapted.</summary>
        private readonly SandboxPawnHarness harness;

        /// <summary>The unique identifier of the sandbox pawn entity.</summary>
        public int EntityId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => harness?.SandboxPawn?.thingIDNumber ?? 0;
        }

        /// <summary>The label of the sandbox pawn entity.</summary>
        public string EntityLabel => harness?.BoundSubject?.Label ?? "SandboxPawn";

        /// <summary>The body definition of the sandbox pawn.</summary>
        public BodyDef BodyDef
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => harness?.BoundSubject?.BodyDef ?? harness?.SandboxPawn?.RaceProps?.body;
        }

        /// <summary>The base body size of the sandbox pawn, as determined by the medical classifier.</summary>
        public float BaseBodySize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MedicalClassifier.GetSafeBodySize(harness?.SandboxPawn);
        }

        /// <summary>Indicates whether the sandbox harness is in a valid biological state.</summary>
        public bool IsValidBiologicalState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => harness != null && harness.IsValid;
        }

        #endregion

        #region 2. CONSTRUCTOR, INGRESS & CAPACITY METHODS

        /// <summary>
        /// Initializes a new instance of the <see cref="SandboxAnatomicalSource"/> struct with the specified sandbox harness.
        /// </summary>
        /// <param name="harness">The sandbox harness to adapt.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SandboxAnatomicalSource(SandboxPawnHarness harness)
        {
            this.harness = harness;
        }

        /// <summary>
        /// Resolves the effective capacity level for the specified capacity on the sandbox pawn or source colonist.
        /// </summary>
        /// <param name="capacity">The capacity to query.</param>
        /// <returns>The effective capacity level for the specified capacity.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCapacityLevel(PawnCapacityDef capacity)
        {
            return harness != null ? harness.GetEffectiveCapacityLevel(capacity) : 1.0f;
        }

        /// <summary>
        /// Resolves the effective efficiency for a body part on the sandbox pawn or source colonist.
        /// </summary>
        /// <param name="part">The body part to query for efficiency.</param>
        /// <param name="defaultRatedEfficiency">The default efficiency to fall back on if no modifications are present.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetPartEfficiency(BodyPartRecord part, float defaultRatedEfficiency)
        {
            return harness != null ? harness.GetEffectivePartEfficiency(part, defaultRatedEfficiency) : defaultRatedEfficiency;
        }

        /// <summary>
        /// Resolves the maximum health points for a body part on the sandbox pawn or source colonist.
        /// </summary>
        /// <param name="part">The body part to query for maximum health.</param>
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

        /// <summary>
        /// Ingresses the pathology from the sandbox pawn or source colonist into the provided anatomical workspace.
        /// </summary>
        /// <param name="workspace">The anatomical workspace to populate with pathology data.</param>
        /// <param name="populateColdMetadata">Whether to populate cold metadata during ingress.</param>
        /// <remarks>
        /// This method only ingresses authentic pathology from the sandbox pawn or source colonist and does not consider synthetic damage.
        /// </remarks>
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
                AnatomicalIngressSolver.IngressSourcePathology(targetPawn, safeHediffs, workspace, populateColdMetadata, this);
            }
        }

        /// <summary>
        /// Resolves the baseline capacity for the sandbox pawn.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ResolveBaselineCapacity()
        {
            if (harness?.SandboxPawn == null) return 0f;
            return SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
        }

        #endregion
    }
}