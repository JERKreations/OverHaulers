using System;
using System.Text;
using System.Runtime.CompilerServices;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-04] Presentation text formatter compiling <see cref="MassCapacityModel"/> structures into
    /// rich-text explanation tooltips and InfoCard breakdown cards.
    /// Thread-gated to the main thread with strict try...finally boundaries protecting static scratch buffers.
    /// Resides under Source/Presentation/Rendering/.
    /// </summary>
    public static class ReportFormatter
    {
        #region 1. [VIEW-04-A] CONSTANTS & POOLED SCRATCHPADS

        private const string Indent2 = "  ";
        private const string Indent4 = "    ";

        /// <summary>Standard muted grey typography color for neutral/ignored part labels in tooltips.</summary>
        public static readonly Color MutedTextColor = new Color(0.63f, 0.65f, 0.67f);

        // Cached active presentation palette pushed downward from Settings
        private static Color colorHealthy = PresentationDefaults.ColorHealthyDefault;
        private static Color colorCritical = PresentationDefaults.ColorCriticalDefault;
        private static Color colorBoosted = PresentationDefaults.ColorBoostedDefault;
        private static bool isVerboseBreakdown = PresentationDefaults.VerboseBreakdown;
        private static bool prostheticColorOverride = PresentationDefaults.ProstheticColorOverride;
        private static Color prostheticCritical = PresentationDefaults.ColorCriticalDefault;
        private static Color prostheticHealthy = PresentationDefaults.ColorHealthyDefault;
        private static Color prostheticBoosted = PresentationDefaults.ColorBoostedDefault;

        private static readonly string[] CapacityTranslationKeys = new[]
        {
            "OverHaulers_InfoCard_Breathing",
            "OverHaulers_InfoCard_BloodPumping",
            "OverHaulers_InfoCard_Moving",
            "OverHaulers_InfoCard_Manipulation",
            "OverHaulers_InfoCard_Consciousness"
        };

        private static readonly StringBuilder pooledFinalReport = new StringBuilder(1024);
        private static readonly StringBuilder pooledDetailsBuilder = new StringBuilder(1024);
        private static readonly StringBuilder pooledListBuilder = new StringBuilder(1024);

        /// <summary>
        /// Updates the active color palette and verbose formatting rules pushed downward from Settings.
        /// Preserves strict downward layer directionality (Presentation never references Settings).
        /// </summary>
        public static void SetPalette(Color healthy, Color critical, Color boosted, bool verbose)
        {
            colorHealthy = healthy;
            colorCritical = critical;
            colorBoosted = boosted;
            isVerboseBreakdown = verbose;
        }

        /// <summary>Updates the optional InfoCard-only prosthetic and implant colour gradient.</summary>
        public static void SetProstheticPalette(bool enabled, Color critical, Color healthy, Color boosted)
        {
            prostheticColorOverride = enabled;
            prostheticCritical = critical;
            prostheticHealthy = healthy;
            prostheticBoosted = boosted;
        }

        #endregion

        #region 2. [VIEW-04-B] PUBLIC CANONICAL EXPLANATION API

        /// <summary>
        /// [VIEW-04] Canonical Single Source of Truth (SSoT) entry point formatting the Caravan Mass Capacity breakdown.
        /// Consumes downward-pushed layout rules and palette configuration to preserve strict layer directionality.
        /// Thread-gated to the main thread with pooled scratch buffers.
        /// </summary>
        /// <param name="massModel">The populated mass capacity model to format.</param>
        /// <param name="diagnosticPrecision">Use up to five decimal places instead of three.</param>
        /// <returns>A formatted rich-text explanation string containing typographic overlay sentinels.</returns>
        public static string BuildExplanation(MassCapacityModel massModel, bool diagnosticPrecision = false)
        {
            if (!UnityData.IsInMainThread || massModel == null)
            {
                return string.Empty;
            }

            pooledFinalReport.Clear();
            pooledDetailsBuilder.Clear();

            try
            {
                bool verbose = isVerboseBreakdown;

                pooledFinalReport.Append(InfoCardOverlay.TagSentinel);

                // 1. OverHaulers Physical Rating Line
                string totalMultValStr = FormatAdaptive(massModel.TotalMultiplier, diagnosticPrecision);
                string signedOffsetValStr = FormatMass(massModel.Offset, diagnosticPrecision, true);
                pooledFinalReport.AppendLine("OverHaulers_InfoCard_PhysicalRating".Translate(totalMultValStr, signedOffsetValStr).ToString());

                // 2. Minimum Floor Footnote (Unconditionally declared in muted grey)
                if (massModel.ConfigurablePercentileFloorKg > 0f)
                {
                    string floorKgStr = FormatMass(massModel.ConfigurablePercentileFloorKg, diagnosticPrecision, false);
                    string floorPctStr = FormatAdaptive(massModel.ConfigurablePercentileFloorPercent, diagnosticPrecision);
                    string floorNote = "OverHaulers_InfoCard_MinimumFloorNote".Translate(floorKgStr, floorPctStr).ToString();
                    pooledFinalReport.AppendLine(floorNote.Colorize(MutedTextColor));
                }

                // 3. Capacities, Impacts & Anatomical Breakdown
                AppendCapacities(pooledDetailsBuilder, massModel, verbose, diagnosticPrecision);
                AppendAthleticImpacts(pooledDetailsBuilder, massModel, verbose);
                AppendStructuralSections(pooledDetailsBuilder, massModel, verbose, diagnosticPrecision);

                if (pooledDetailsBuilder.Length > 0 || verbose)
                {
                    pooledFinalReport.Append(pooledDetailsBuilder.ToString());
                }

                string finalResultString = pooledFinalReport.ToString();
                massModel.Explanation = finalResultString;
                return finalResultString;
            }
            finally
            {
                // Strict re-entrancy clearing: guarantee buffers never retain dirty strings if an exception occurs
                pooledFinalReport.Clear();
                pooledDetailsBuilder.Clear();
            }
        }

        #endregion

        #region 3. [VIEW-04-C] ALGORITHMIC BREAKDOWN STAGES

        /// <summary>
        /// [VIEW-02] Appends the list of mass capacity levels to the report builder.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the report to.</param>
        /// <param name="massModel">The mass capacity model containing the capacity levels.</param>
        /// <param name="verbose">If true, forces display even if no capacity levels are modified.</param>
        /// <param name="diagnosticPrecision">Use diagnostic display precision.</param>
        private static void AppendCapacities(StringBuilder builder, MassCapacityModel massModel, bool verbose, bool diagnosticPrecision)
        {
            bool hasAnyCapacities = verbose;
            if (!hasAnyCapacities)
            {
                for (int i = 0; i < massModel.CapacityLevels.Length; i++)
                {
                    if (Math.Abs(massModel.CapacityLevels[i] - 1.0f) >= PawnDataRegistry.EfficiencyEpsilon)
                    {
                        hasAnyCapacities = true;
                        break;
                    }
                }
            }

            if (hasAnyCapacities)
            {
                builder.AppendLine();
                builder.Append(Indent2);
                builder.AppendLine("OverHaulers_InfoCard_CapacitiesHeader".Translate().ToString());
                for (int i = 0; i < CapacityTranslationKeys.Length; i++)
                {
                    AppendCapacityLine(builder, CapacityTranslationKeys[i], massModel.CapacityLevels[i], verbose, diagnosticPrecision);
                }
            }
        }

        /// <summary>
        /// Appends the list of athletic impacts (ailments) to the report builder.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the report to.</param>
        /// <param name="massModel">The mass capacity model containing athletic impacts.</param>
        /// <param name="verbose">If true, forces display even if no athletic impacts are present.</param>
        private static void AppendAthleticImpacts(StringBuilder builder, MassCapacityModel massModel, bool verbose)
        {
            pooledListBuilder.Clear();

            try
            {
                bool hasAnyAthleticImpacts = false;

                // 1. Append the list of ailments to the pooled list builder
                var ailments = massModel.Ailments.Items;
                int count = ailments.Count;

                if (count > 0)
                {
                    // 2. Flag that there are athletic impacts and append each ailment to the list
                    hasAnyAthleticImpacts = true;
                    for (int i = 0; i < count; i++)
                    {
                        pooledListBuilder.Append("    - ");
                        pooledListBuilder.AppendLine(ailments[i]);
                    }
                }

                // 3. Append the header and list to the main builder if there are any athletic impacts or if verbose is true
                if (hasAnyAthleticImpacts || verbose)
                {
                    builder.AppendLine();
                    builder.Append(Indent2);
                    builder.AppendLine("OverHaulers_InfoCard_ImpactsHeader".Translate().ToString());
                    if (hasAnyAthleticImpacts)
                    {
                        builder.Append(pooledListBuilder.ToString());
                    }
                    else
                    {
                        builder.Append("    - ");
                        builder.AppendLine("OverHaulers_None".Translate().ToString());
                    }
                }
            }
            finally
            {
                pooledListBuilder.Clear();
            }
        }

        /// <summary>
        /// Appends the structural sections of the report, including parts and sub-parts, to the report builder.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the report sections to.</param>
        /// <param name="massModel">The mass model containing evaluated parts and ailments.</param>
        /// <param name="verbose">Flag to force display even if no structural modifications are present.</param>
        private static void AppendStructuralSections(StringBuilder builder, MassCapacityModel massModel, bool verbose, bool diagnosticPrecision)
        {
            for (int i = 0; i < massModel.EvaluatedParts.Count; i++)
            {
                PartViewNode part = massModel.EvaluatedParts[i];

                // Skip neutral groups unless they contain a prosthetic or modified descendant.
                if (!verbose && Math.Abs(part.TotalOffset) < PawnDataRegistry.EfficiencyEpsilon &&
                    !part.HasProsthetic && !HasModifiedDescendants(part))
                {
                    continue;
                }

                builder.AppendLine();
                builder.Append(Indent2);
                builder.Append(part.Label);

                // Append efficiency rating if the part has a prosthetic or if its efficiency rating deviates from 1.0 by more than the epsilon threshold
                if (part.HasProsthetic || Math.Abs(part.EfficiencyRating - 1.0f) >= PawnDataRegistry.EfficiencyEpsilon)
                {
                    builder.Append(" [");
                    string effStr = FormatPercentage(part.EfficiencyRating, diagnosticPrecision);
                    Color efficiencyColor = prostheticColorOverride && part.Record != null && part.HasProsthetic
                        ? GetProstheticColor(part.ProstheticColor, part.EfficiencyRating)
                        : GetMedicalColor(part.EfficiencyRating);
                    builder.Append(effStr.Colorize(efficiencyColor));
                    builder.Append("]");
                }

                builder.Append(": ");
                builder.Append(FormatMass(part.TotalOffset, diagnosticPrecision, true));
                if (part.Record != null && part.HasProsthetic && !string.IsNullOrEmpty(part.ProstheticName))
                {
                    builder.Append(" [");
                    builder.Append(part.ProstheticName.Colorize(GetProstheticColor(part.ProstheticColor, part.EfficiencyRating)));
                    builder.Append("]");
                }
                AppendAilmentSuffix(builder, part);
                builder.AppendLine();

                AppendGroupSubtotalRow(builder, part, verbose, diagnosticPrecision);
                AppendStructuralSubParts(builder, part, verbose, diagnosticPrecision);
            }
        }

        /// <summary>
        /// Appends a subtotal row for a given part, displaying the offsets for prosthetic, athletic, and health factors.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the subtotal row to.</param>
        /// <param name="part">The part for which to append the subtotal row.</param>
        /// <param name="verbose">Flag to force display even if no offsets are present.</param>
        /// <param name="diagnosticPrecision">Use diagnostic display precision.</param>
        private static void AppendGroupSubtotalRow(StringBuilder builder, PartViewNode part, bool verbose, bool diagnosticPrecision)
        {
            bool hasProsthetic = Math.Abs(part.ProstheticOffset) >= PawnDataRegistry.EfficiencyEpsilon;
            bool hasAthletic = Math.Abs(part.AthleticOffset) >= PawnDataRegistry.EfficiencyEpsilon;
            bool hasHealth = Math.Abs(part.HealthOffset) >= PawnDataRegistry.EfficiencyEpsilon;

            int activeFactors = (hasProsthetic ? 1 : 0) + (hasAthletic ? 1 : 0) + (hasHealth ? 1 : 0);

            if (activeFactors > 0 || verbose)
            {
                builder.Append(Indent2);
                bool first = true;

                if (hasProsthetic || verbose)
                {
                    builder.Append(InfoCardOverlay.TagProsthetic + " ");
                    builder.Append(FormatMass(part.ProstheticOffset, diagnosticPrecision, true));
                    first = false;
                }

                if (hasAthletic || verbose)
                {
                    if (!first) builder.Append("  ");
                    builder.Append(InfoCardOverlay.TagAthletic + " ");
                    builder.Append(FormatMass(part.AthleticOffset, diagnosticPrecision, true));
                    first = false;
                }

                if (hasHealth || verbose)
                {
                    if (!first) builder.Append("  ");
                    builder.Append(InfoCardOverlay.TagInjury + " ");
                    builder.Append(FormatMass(part.HealthOffset, diagnosticPrecision, true));
                }

                builder.AppendLine();
            }
        }

        /// <summary>
        /// Appends the structural sub-parts of the specified part to the provided StringBuilder.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the structural sub-parts will be appended.</param>
        /// <param name="part">The parent part whose structural sub-parts are to be appended.</param>
        /// <param name="verbose">Indicates whether to include all sub-parts regardless of modification status.</param>
        /// <param name="diagnosticPrecision">Use diagnostic display precision.</param>
        private static void AppendStructuralSubParts(StringBuilder builder, PartViewNode part, bool verbose, bool diagnosticPrecision)
        {
            if (part.SubParts.Count == 0) return;

            bool hasModifiedSubParts = verbose;
            if (!hasModifiedSubParts)
            {
                for (int s = 0; s < part.SubParts.Count; s++)
                {
                    var sub = part.SubParts[s];
                    if (sub.IsModified || HasModifiedDescendants(sub))
                    {
                        hasModifiedSubParts = true;
                        break;
                    }
                }
            }

            if (hasModifiedSubParts)
            {
                AppendSubPartsRecursive(builder, part, 0, verbose, diagnosticPrecision);
            }
        }

        /// <summary>
        /// Recursively appends the structural sub-parts of the specified parent part to the provided StringBuilder.
        /// </summary>
        /// <param name="statsBuilder">The StringBuilder to which the structural sub-parts will be appended.</param>
        /// <param name="parent">The parent part whose structural sub-parts are to be appended.</param>
        /// <param name="depth">The current depth of the recursive traversal, used for indentation.</param>
        /// <param name="verbose">Indicates whether to include all sub-parts regardless of modification status.</param>
        /// <param name="diagnosticPrecision">Use diagnostic display precision.</param>
        private static void AppendSubPartsRecursive(StringBuilder statsBuilder, PartViewNode parent, int depth, bool verbose, bool diagnosticPrecision)
        {
            if (parent?.SubParts == null || parent.SubParts.Count == 0) return;

            for (int i = 0; i < parent.SubParts.Count; i++)
            {
                PartViewNode sub = parent.SubParts[i];
                bool hasModifications = sub.IsModified || HasModifiedDescendants(sub);

                if (verbose || hasModifications)
                {
                    if (!verbose && ShouldOmitSubPart(sub, parent))
                    {
                        continue;
                    }

                    AppendIndent(statsBuilder, depth);
                    AppendSubPartLine(statsBuilder, sub, depth, verbose, diagnosticPrecision);
                    statsBuilder.AppendLine();

                    AppendSubPartsRecursive(statsBuilder, sub, depth + 1, verbose, diagnosticPrecision);
                }
            }
        }

        #endregion

        #region 4. [VIEW-04-D] PRIVATE FORMATTING & MEDICAL COLOR HELPERS

        /// <summary>
        /// Appends a single structural sub-part line to the provided StringBuilder.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the sub-part line will be appended.</param>
        /// <param name="sub">The sub-part whose details are to be appended.</param>
        /// <param name="depth">The current depth of the sub-part within the hierarchy, used for indentation.</param>
        /// <param name="verbose">Indicates whether to include all details regardless of modification status.</param>
        /// <param name="diagnosticPrecision">Use diagnostic display precision.</param>
        private static void AppendSubPartLine(StringBuilder builder, PartViewNode sub, int depth, bool verbose, bool diagnosticPrecision)
        {
            builder.Append("- ");
            builder.Append(sub.Label);

            bool isDamaged = sub.HealthFraction < 1.0f - PawnDataRegistry.EfficiencyEpsilon;
            bool hasProsthetic = sub.HasProsthetic;
            bool isMissing = sub.IsMissing;
            bool hasImplant = !string.IsNullOrEmpty(sub.AthleticImplantName);

            if (hasProsthetic)
            {
                Color prostheticColor = GetProstheticColor(sub.ProstheticColor, sub.EfficiencyRating);
                builder.Append(": [");
                builder.Append(sub.ProstheticName.Colorize(prostheticColor));
                builder.Append(" ");
                string effStr = FormatPercentage(sub.EfficiencyRating, diagnosticPrecision);
                builder.Append(effStr.Colorize(prostheticColor));

                if (isDamaged || verbose)
                {
                    builder.Append(", ");
                    string hpStr = FormatPercentage(sub.HealthFraction, diagnosticPrecision);
                    builder.Append(hpStr.Colorize(GetMedicalColor(sub.HealthFraction)));
                }

                builder.Append("]");
            }
            else if (isMissing)
            {
                builder.Append(": [");
                if (sub.IsNeutralized)
                {
                    // Renders as a neutral grey string
                    string ignoredLabel = "OverHaulers_InfoCard_MissingIgnored".Translate().ToString();
                    builder.Append(ignoredLabel.Colorize(MutedTextColor));
                }
                else
                {
                    // Renders as selected critical colour missing part
                    string missingLabel = HediffDefOf.MissingBodyPart.LabelCap;
                    builder.Append(missingLabel.Colorize(GetMedicalColor(0f)));
                }
                builder.Append("]");
            }
            else
            {
                bool showHealth = isDamaged || verbose;

                if (showHealth || hasImplant)
                {
                    builder.Append(": ");

                    if (showHealth)
                    {
                        builder.Append("[");
                        string hpStr = FormatPercentage(sub.HealthFraction, diagnosticPrecision);
                        builder.Append(hpStr.Colorize(GetMedicalColor(sub.HealthFraction)));
                        builder.Append("]");
                    }

                    if (hasImplant)
                    {
                        builder.Append("[");
                        builder.Append(sub.AthleticImplantName.Colorize(GetImplantColor(sub.AthleticImplantColor)));
                        builder.Append("]");
                    }
                }
            }

            AppendAilmentSuffix(builder, sub);

            if (hasProsthetic && hasImplant &&
                !string.Equals(sub.AthleticImplantName, sub.ProstheticName, StringComparison.Ordinal))
            {
                builder.AppendLine();
                AppendIndent(builder, depth);
                builder.Append("  + ");
                builder.Append(sub.AthleticImplantName.Colorize(GetImplantColor(sub.AthleticImplantColor)));
            }
        }

        /// <summary>
        /// Appends indentation spaces to the provided StringBuilder based on the specified depth.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the indentation spaces will be appended.</param>
        /// <param name="depth">The current depth of the indentation, determining the number of spaces to append.</param>
        private static void AppendIndent(StringBuilder builder, int depth)
        {
            int spaceCount = 4 + depth;
            for (int i = 0; i < spaceCount; i++)
            {
                builder.Append(' ');
            }
        }

        /// <summary>
        /// Appends a line representing the capacity of a specific attribute to the provided StringBuilder.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the capacity line will be appended.</param>
        /// <param name="translationKey">The translation key for the capacity label.</param>
        /// <param name="value">The current value of the capacity.</param>
        /// <param name="verbose">Indicates whether to include all details regardless of modification status.</param>
        /// <param name="diagnosticPrecision">Use diagnostic display precision.</param>
        private static void AppendCapacityLine(StringBuilder builder, string translationKey, float value, bool verbose, bool diagnosticPrecision)
        {
            float difference = value - 1.0f;
            bool isModified = Math.Abs(difference) >= PawnDataRegistry.EfficiencyEpsilon;

            if (isModified || verbose)
            {
                builder.Append(Indent4);
                string pctText = FormatPercentage(value, diagnosticPrecision);
                builder.AppendLine(translationKey.Translate(pctText.Colorize(GetMedicalColor(value))).ToString());
            }
        }

        /// <summary>Formats a number with up to three gameplay or five diagnostic decimal places.</summary>
        public static string FormatAdaptive(float value, bool diagnosticPrecision = false)
        {
            return value.ToString(diagnosticPrecision ? "0.#####" : "0.###");
        }

        /// <summary>Formats a fraction as an adaptive percentage in the active numeric culture.</summary>
        public static string FormatPercentage(float value, bool diagnosticPrecision = false)
        {
            return value.ToString(diagnosticPrecision ? "0.#####%" : "0.###%");
        }

        /// <summary>Uses the game's mass formatter for gameplay and adaptive kilograms for diagnostics.</summary>
        public static string FormatMass(float value, bool diagnosticPrecision, bool offset)
        {
            if (!diagnosticPrecision) return offset ? value.ToStringMassOffset() : value.ToStringMass();
            string number = value.ToString(offset ? "+0.#####;-0.#####;0" : "0.#####");
            return "OverHaulers_InfoCard_DiagnosticMass".Translate(number).ToString();
        }

        /// <summary>
        /// Appends an ailment suffix for the specified part to the provided StringBuilder, if present.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the ailment suffix will be appended.</param>
        /// <param name="part">The part whose local ailment is to be appended as a suffix.</param>
        private static void AppendAilmentSuffix(StringBuilder builder, PartViewNode part)
        {
            if (part == null || string.IsNullOrEmpty(part.LocalAilmentName)) return;

            builder.Append("OverHaulers_InfoCard_AilmentPrefix".Translate(part.LocalAilmentName.Colorize(part.LocalAilmentColor)).ToString());
        }

        /// <summary>
        /// Determines the appropriate color for a medical value based on its magnitude.
        /// Zero heap allocations and zero Settings null-checks via cached presentation palette.
        /// </summary>
        /// <param name="value">The medical value for which to determine the color.</param>
        /// <returns>The color corresponding to the specified medical value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color GetMedicalColor(float value)
        {
            if (value >= 2.0f) return colorBoosted;
            if (value > 1.0f)
            {
                float t = (value - 1.0f) / 1.0f;
                return Color.Lerp(colorHealthy, colorBoosted, t);
            }
            if (value >= 0.2f)
            {
                float t = (value - 0.2f) / 0.8f;
                return Color.Lerp(colorCritical, colorHealthy, t);
            }
            
            return colorCritical;
        }

        /// <summary>Returns the original colour or the 50% / 100% / 150% prosthetic gradient colour.</summary>
        internal static Color GetProstheticColor(Color original, float efficiency)
        {
            if (!prostheticColorOverride) return original;
            if (float.IsNaN(efficiency))
            {
                OHLog.Warn("Presentation.ProstheticColor", null, "Prosthetic efficiency is NaN; retaining the original label colour.");
                return original;
            }
            if (efficiency <= 0.5f) return prostheticCritical;
            if (efficiency >= 1.5f) return prostheticBoosted;
            if (efficiency == 1f) return prostheticHealthy;
            if (efficiency < 1f) return Color.Lerp(prostheticCritical, prostheticHealthy, (efficiency - 0.5f) * 2f);
            return Color.Lerp(prostheticHealthy, prostheticBoosted, (efficiency - 1f) * 2f);
        }

        /// <summary>Separate athletic implants have no comparable part efficiency and use the neutral colour.</summary>
        internal static Color GetImplantColor(Color original)
        {
            return prostheticColorOverride ? prostheticHealthy : original;
        }

        /// <summary>
        /// Determines whether a sub-part should be omitted from the report based on its relationship to the parent part.
        /// </summary>
        /// <param name="sub">The sub-part to evaluate for omission.</param>
        /// <param name="parent">The parent part against which the sub-part is evaluated.</param>
        /// <returns>True if the sub-part should be omitted from the report; otherwise, false.</returns>
        private static bool ShouldOmitSubPart(PartViewNode sub, PartViewNode parent)
        {
            if (sub == null || parent == null || parent.Record == null) return false;

            if (parent.IsMissing && sub.IsMissing) return true;

            bool matchesParent = parent.HasProsthetic && sub.HasProsthetic
                && sub.ProstheticName == parent.ProstheticName
                && Math.Abs(sub.EfficiencyRating - parent.EfficiencyRating) < PawnDataRegistry.EfficiencyEpsilon
                && (sub.IsInherited || Math.Abs(sub.HealthFraction - parent.HealthFraction) < PawnDataRegistry.EfficiencyEpsilon)
                && sub.IsMissing == parent.IsMissing
                && sub.AthleticImplantName == parent.AthleticImplantName
                && sub.LocalAilmentName == parent.LocalAilmentName;

            if (!matchesParent) return false;

            if (sub.SubParts != null && sub.SubParts.Count > 0)
            {
                for (int i = 0; i < sub.SubParts.Count; i++)
                {
                    if (!ShouldOmitSubPart(sub.SubParts[i], sub))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether the specified part has any modified descendant sub-parts.
        /// </summary>
        /// <param name="model">The part to evaluate for modified descendants.</param>
        /// <returns>True if the part has any modified descendant sub-parts; otherwise, false.</returns>
        private static bool HasModifiedDescendants(PartViewNode model)
        {
            if (model?.SubParts == null) return false;
            for (int i = 0; i < model.SubParts.Count; i++)
            {
                var sub = model.SubParts[i];
                if (sub.IsModified || HasModifiedDescendants(sub))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}