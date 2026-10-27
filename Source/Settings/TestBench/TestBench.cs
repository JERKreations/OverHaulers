using System;
using System.Text;
using System.Runtime.CompilerServices;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [TEST-01] VIEW MODES

    /// <summary>
    /// [TEST-01] Defines the view modes available for the Test Bench section of the OverHaulers settings, including the InfoCard mode for detailed
    /// explanations and the TopologyXRay mode for visualizing anatomical topology and part weight distributions.
    /// </summary>
    public enum TestBenchViewMode
    {
        InfoCard = 0,
        TopologyXRay = 1
    }

    #endregion

    /// <summary>
    /// [TEST-02] Central facade orchestrator for the Mod Settings Interactive Test Bench.
    /// Operates entirely against the unified <see cref="SandboxPawnHarness"/> with 100% native RimWorld capacity evaluation.
    /// Supports live developer hot-recompilation of all skeletal topologies, medical catalogs, and test subjects.
    /// Resides under Source/Settings/TestBench/.
    /// </summary>
    public static class TestBench
    {
        #region 2. SESSION STATE & CHANNEL FLAGS

        public static TestSubjectEntry activeSubject;
        public static GroupingDimension activeGroupingDimension = GroupingDimension.Mod;
        public static TestBenchViewMode activeViewMode = TestBenchViewMode.InfoCard;

        private static SandboxPawnHarness activeHarness;

        /// <summary>[TEST-05] Active headless sandbox harness managing 1:1 pawn simulations for all subjects.</summary>
        public static SandboxPawnHarness Harness
        {
            get
            {
                if (activeHarness == null)
                {
                    activeHarness = SandboxPawnHarness.CreateForCurrentProgramState();
                }

                return activeHarness;
            }
        }

        public static float lastCalculatedFinalMass = 0.0f;
        public static float lastCalculatedDelta = 0.0f;

        private static bool isDirty = true;
        private static string cachedExplanation = string.Empty;
        private static float cachedSpeciesBaseline = 0.0f;
        private static readonly MassCapacityModel cachedModel = new MassCapacityModel();

        private static readonly StringBuilder pooledXRayBuilder = new StringBuilder(2048);

        private static bool isInitialized = false;

        /// <summary>
        /// [TEST-02] Ensures the Test Bench orchestrator, medical recipe catalog, test subject registry,
        /// and species baseline calibrations are fully initialized and bound.
        /// Idempotent; executes in 1 CPU cycle once initialized.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureInitialized()
        {
            if (isInitialized) return;
            InitializeInternal();
        }

        /// <summary>
        /// Initializes the internal state of the Test Bench, including the active harness and subject.
        /// Records the initialization duration in the activity journal for performance tracking.
        /// </summary>
        private static void InitializeInternal()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            OHLog.ActivityJournal.BeginSession("OverHaulers_Journal_TestBenchHeader".Translate("...").ToString());

            TopologyLayoutCompiler.EnsureInitialized();
            SpeciesBaselineCalibration.EnsureInitialized();
            MedicalRecipeCatalog.EnsureInitialized();
            TestSubjectRegistry.EnsureCacheInitialized();

            if (activeSubject == null)
            {
                activeSubject = TestSubjectRegistry.GetDefaultSubject();
            }

            if (activeHarness == null)
            {
                activeHarness = SandboxPawnHarness.CreateForCurrentProgramState();
                if (activeSubject != null)
                {
                    activeHarness.BindSubject(activeSubject);
                }
            }

            isInitialized = true;
            watch.Stop();

            OHLog.ActivityJournal.SetHeader("OverHaulers_Journal_TestBenchHeader".Translate(watch.Elapsed.TotalMilliseconds.ToString("F1")).ToString());
            OHLog.ActivityJournal.Dispatch();
        }

        /// <summary>
        /// Gets or sets the currently active test subject entry for the Test Bench.
        /// </summary>
        public static TestSubjectEntry ActiveSubject
        {
            get
            {
                EnsureInitialized();
                return activeSubject;
            }
            set
            {
                EnsureInitialized();
                if (activeSubject != value)
                {
                    activeSubject = value;
                    Harness.BindSubject(activeSubject);
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// Marks the Test Bench as dirty, indicating that cached calculations and explanations need to be refreshed.
        /// </summary>
        public static void MarkDirty()
        {
            isDirty = true;
        }

        /// <summary>
        /// Resets the Test Bench to its default state, ensuring the sandbox harness is unconditionally re-bound and rebuilt.
        /// </summary>
        public static void ResetTestBench()
        {
            activeGroupingDimension = GroupingDimension.Mod;
            activeViewMode = TestBenchViewMode.InfoCard;

            TestSubjectEntry defaultSubject = TestSubjectRegistry.GetDefaultSubject();
            activeSubject = defaultSubject;

            if (activeHarness != null)
            {
                activeHarness.Dispose();
                activeHarness = null;
            }

            // Unconditionally bind the default subject to guarantee the sandbox pawn is instantiated
            Harness.BindSubject(defaultSubject);
            MarkDirty();
        }

        /// <summary>
        /// [TEST-02] Triggers a full live re-compilation of all engine registries and refreshes the local Test Bench session.
        /// Hot-reloads Core and Integration systems via <see cref="OverHaulersLifecycle.ReinitializeRuntime"/>,
        /// then rebuilds local test subjects and the sandbox pawn for rapid developer turnaround.
        /// </summary>
        public static void RecompileAll()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            bool onlyCaravan = OverHaulers.settings == null || !OverHaulers.settings.devStressTestAllFauna;
            string context = "OverHaulers_Log_Calibration_Context_RecompileAll".Translate().ToString();

            // 1. Hot-recompile Core, Integration, and TestBench catalogs (Topologies, Calibrations, Surgeries, Caches)
            OverHaulersLifecycle.ReinitializeRuntime(onlyCaravan, context);

            // 2. Re-index subject entries
            TestSubjectRegistry.ClearCache();
            TestSubjectRegistry.EnsureCacheInitialized();

            // 3. Rebuild local testbench harness & subject bindings
            if (activeHarness != null)
            {
                activeHarness.Dispose();
                activeHarness = null;
            }

            if (activeSubject != null)
            {
                var updatedSubject = TestSubjectRegistry.AllSubjects.Find(s => 
                    s.RaceDef == activeSubject.RaceDef && s.BodyDef == activeSubject.BodyDef);

                activeSubject = updatedSubject ?? TestSubjectRegistry.GetDefaultSubject();
                Harness.BindSubject(activeSubject);
            }
            else
            {
                activeSubject = TestSubjectRegistry.GetDefaultSubject();
                if (activeSubject != null)
                {
                    Harness.BindSubject(activeSubject);
                }
            }

            isInitialized = true;

            // 4. Flush UI layout caches and mark Test Bench dirty for immediate recalculation
            SettingsViewUtilities.ClearInputBuffers();
            SettingsViewUtilities.ClearHeightCache();
            MarkDirty();

            watch.Stop();
            Messages.Message(
                "OverHaulers_Log_Lifecycle_FullRecompileComplete".Translate(watch.Elapsed.TotalMilliseconds.ToString("F1")).ToString(), 
                MessageTypeDefOf.PositiveEvent, 
                false
            );
        }

        #endregion

        #region 3. PASSIVE MODEL & PREVIEW ACCESSORS

        /// <summary>
        /// Retrieves the pre-calculated MassCapacityModel for passive, zero-calculation UI drawing.
        /// </summary>
        /// <param name="settings">The current Test Bench settings used for model calculation.</param>
        /// <param name="speciesBaseline">Outputs the baseline mass capacity for the species.</param>
        /// <remarks>
        /// This method ensures that the cached MassCapacityModel is up-to-date before returning it.
        /// The species baseline is also output for reference in UI calculations.
        /// </remarks>
        public static MassCapacityModel GetCachedModel(Settings settings, out float speciesBaseline)
        {
            EnsureModelFresh(settings);
            speciesBaseline = cachedSpeciesBaseline;
            return cachedModel;
        }

        /// <summary>
        /// Retrieves the cached explanation for the current Test Bench settings.
        /// </summary>
        /// <param name="settings">The current Test Bench settings used for explanation generation.</param>
        /// <param name="speciesBaseline">Outputs the baseline mass capacity for the species.</param>
        /// <returns>The cached explanation string.</returns>
        public static string GetCachedExplanation(Settings settings, out float speciesBaseline)
        {
            EnsureModelFresh(settings);
            speciesBaseline = cachedSpeciesBaseline;
            return cachedExplanation;
        }

        /// <summary>
        /// Ensures that the cached MassCapacityModel and explanation are up-to-date for the given settings.
        /// </summary>
        /// <param name="settings">The current Test Bench settings used for model validation and potential recalculation.</param>
        private static void EnsureModelFresh(Settings settings)
        {
            if (isDirty)
            {
                cachedExplanation = GenerateTestExplanation(settings, out cachedSpeciesBaseline);
                isDirty = false;
            }
        }

        /// <summary>
        /// Generates a detailed explanation for the current Test Bench settings, including mass capacity calculations and topology analysis.
        /// </summary>
        /// <param name="settings">The current Test Bench settings used for explanation generation.</param>
        /// <param name="speciesBaseline">Outputs the baseline mass capacity for the species.</param>
        /// <returns>A detailed explanation string for the current Test Bench settings.</returns>
        private static string GenerateTestExplanation(Settings settings, out float speciesBaseline)
        {
            speciesBaseline = 0.0f;

            try
            {
                TestSubjectEntry subject = ActiveSubject;
                if (subject?.BodyDef == null || !Harness.IsValid) return string.Empty;

                // =========================================================================
                // Unified Ingress: Evaluates via SandboxAnatomicalSource
                // Reflects authentic live colonist state when unmodified; reflects rated surgical potential when modified.
                // =========================================================================
                SandboxAnatomicalSource dataSource = Harness.CreateDataSource();
                speciesBaseline = dataSource.ResolveBaselineCapacity();

                MassCapacityEvaluationResult result = ViewProjection.InspectAndProject(
                    dataSource, speciesBaseline, cachedModel);

                lastCalculatedDelta = result.SolvedOffset;
                lastCalculatedFinalMass = result.FinalCapacity;

                if (activeViewMode == TestBenchViewMode.TopologyXRay)
                {
                    SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(subject.BodyDef);
                    return GenerateTopologyXRayReport(template, settings, subject);
                }

                string explanation = ReportFormatter.BuildExplanation(cachedModel);

                return explanation ?? string.Empty;
            }
            catch (Exception ex)
            {
                OHLog.TestBench.Warn("GenerateTestExplanation", ex, "An error occurred while generating the test explanation.");
                return string.Empty;
            }
        }

        /// <summary>
        /// Generates an X-Ray style report for the anatomical topology of the given species template.
        /// </summary>
        /// <param name="template">The species topology template to generate the X-Ray report for.</param>
        /// <param name="settings">The current Test Bench settings used for report generation.</param>
        /// <param name="subject">The test subject entry associated with the report.</param>
        /// <returns>A formatted X-Ray style report string for the anatomical topology of the species template.</returns>
        private static string GenerateTopologyXRayReport(SpeciesTopologyTemplate template, Settings settings, TestSubjectEntry subject)
        {
            if (template == null) return "OverHaulers_XRay_NoTemplate".Translate().ToString();

            pooledXRayBuilder.Clear();

            try
            {
                // X-Ray contains zero icons and renders purely as rich text; no sentinel tag required
                pooledXRayBuilder.AppendLine("OverHaulers_XRay_Header".Translate(subject.BodyDef.defName, template.PartCount).ToString().Colorize(Color.cyan));
                pooledXRayBuilder.AppendLine();

                float budgetTorso = settings.GetBudget(PartType.CorePart);
                float budgetArm = settings.GetBudget(PartType.ManipulationPart);
                float budgetLeg = settings.GetBudget(PartType.MovingPart);
                float totalBudget = budgetTorso + budgetArm + budgetLeg;

                pooledXRayBuilder.AppendLine("OverHaulers_XRay_RegionalBudgets".Translate().ToString());
                pooledXRayBuilder.AppendLine("OverHaulers_XRay_RegionalBudgetsValues".Translate(
                    (budgetTorso / totalBudget).ToStringPercent(),
                    (budgetArm / totalBudget).ToStringPercent(),
                    (budgetLeg / totalBudget).ToStringPercent()
                ).ToString());
                pooledXRayBuilder.AppendLine();

                pooledXRayBuilder.AppendLine("OverHaulers_XRay_AnatomicalTopology".Translate().ToString());
                string defaultParent = "OverHaulers_ParentRoot".Translate().ToString();

                for (int c = 0; c < template.PartCount; c++)
                {
                    int i = template.CanonicalDisplayIndices != null ? template.CanonicalDisplayIndices[c] : c;

                    BodyPartRecord part = template.IndexedParts[i];
                    PartType type = template.PartTypes[i];

                    float weightFactor = template.StaticWeightFactors[i];
                    string typeTag = GetLocalizedPartTypeTag(type, part);
                    string weightPct = (weightFactor * 100f).ToString("F1") + "%";
                    int parentIdx = template.ParentIndices[i];
                    string parentName = parentIdx != -1 ? template.IndexedParts[parentIdx].Label : defaultParent;

                    bool isVirtual = part.coverageAbs <= 0f && part != part.body?.corePart;
                    Color weightColor = (type == PartType.HeadPart || type == PartType.None || isVirtual) 
                        ? SettingsViewUtilities.DescriptionTextColor 
                        : (weightFactor > 0f ? Color.green : Color.white);

                    pooledXRayBuilder.AppendLine("OverHaulers_XRay_PartEntry".Translate(
                        part.LabelCap, weightPct.Colorize(weightColor), parentName, typeTag.Colorize(SettingsViewUtilities.DescriptionTextColor)
                    ).ToString());
                }

                return pooledXRayBuilder.ToString();
            }
            finally
            {
                pooledXRayBuilder.Clear();
            }
        }

        /// <summary>
        /// Retrieves the localized display tag for a given body part type, optionally considering the specific body part.
        /// </summary>
        /// <param name="type">The type of the body part.</param>
        /// <param name="part">The specific body part record (optional).</param>
        /// <returns>A localized string representing the body part type.</returns>
        public static string GetLocalizedPartTypeTag(PartType type, BodyPartRecord part = null)
        {
            if (part != null && part.coverageAbs <= 0f && part != part.body?.corePart)
            {
                return "OverHaulers_PartType_VirtualAnchor".Translate().ToString();
            }

            switch (type)
            {
                case PartType.None: return "OverHaulers_PartType_Organ".Translate().ToString();
                case PartType.HeadPart: return "OverHaulers_PartType_Head".Translate().ToString();
                case PartType.CorePart: return "OverHaulers_PartType_Core".Translate().ToString();
                case PartType.ManipulationPart: return "OverHaulers_PartType_Manipulation".Translate().ToString();
                case PartType.MovingPart: return "OverHaulers_PartType_Moving".Translate().ToString();
                case PartType.DualLimb: return "OverHaulers_PartType_DualLimb".Translate().ToString();
                default: return type.ToString();
            }
        }

        #endregion
    }
}