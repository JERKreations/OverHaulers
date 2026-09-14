using System;
using System.Text;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    public static class ReportFormatter
    {
        private const string Indent2 = "  ";
        private const string Indent4 = "    ";

        private static readonly string[] CapacityTranslationKeys = new[]
        {
            "OverHaulers_Breathing",
            "OverHaulers_BloodPumping",
            "OverHaulers_Moving",
            "OverHaulers_Manipulation",
            "OverHaulers_Consciousness"
        };

        private static readonly StringBuilder pooledFinalReport = new StringBuilder(1024);
        private static readonly StringBuilder pooledDetailsBuilder = new StringBuilder(1024);
        private static readonly StringBuilder pooledListBuilder = new StringBuilder(1024);

        /// <summary>
        /// [VIEW-04] Builds the Over Haulers Caravan Mass Capacity breakdown report.
        /// Thread-gated to the main thread to protect static scratch buffers.
        /// </summary>
        /// <param name="massModel">The mass capacity model to format.</param>
        /// <param name="verbose">Whether to include unmodified parts.</param>
        /// <param name="biologicalBaseline">The baseline biological mass capacity in kg.</param>
        /// <param name="forceFullCard">If true, wraps with Base and Final lines for Test Bench previews.</param>
        public static string BuildExplanation(MassCapacityModel massModel, bool verbose, float biologicalBaseline, bool forceFullCard = false)
        {
            if (!UnityData.IsInMainThread || massModel == null)
            {
                return string.Empty;
            }

            pooledFinalReport.Clear();
            pooledDetailsBuilder.Clear();

            pooledFinalReport.Append(InfoCardOverlay.TagSentinel);

            // 1. Optional Test Bench Mock Header
            if (forceFullCard)
            {
                pooledFinalReport.AppendLine("StatsReport_BaseValue".Translate() + ": " + biologicalBaseline.ToStringMass());
                pooledFinalReport.AppendLine();
            }

            // 2. OverHaulers Total Body Rating Line
            string totalMultValStr = massModel.TotalMultiplier.ToString("F2");
            string signedOffsetValStr = massModel.Offset.ToStringMassOffset();
            pooledFinalReport.AppendLine("OverHaulers_TotalBodyRating".Translate(totalMultValStr, signedOffsetValStr).ToString());

            // 3. Capacities, Impacts & Anatomical Breakdown
            AppendCapacities(pooledDetailsBuilder, massModel, verbose);
            AppendAthleticImpacts(pooledDetailsBuilder, massModel, verbose);
            AppendStructuralSections(pooledDetailsBuilder, massModel, verbose);

            if (pooledDetailsBuilder.Length > 0 || verbose)
            {
                pooledFinalReport.Append(pooledDetailsBuilder.ToString());
            }

            // 4. Optional Test Bench Mock Footer
            if (forceFullCard)
            {
                float finalValue = MassCapacitySolver.EnforceSafetyFloor(biologicalBaseline + massModel.Offset, null);

                pooledFinalReport.AppendLine();
                pooledFinalReport.Append("StatsReport_FinalValue".Translate() + ": " + finalValue.ToStringMass());
            }

            string finalResultString = pooledFinalReport.ToString();
            massModel.Explanation = finalResultString;

            pooledFinalReport.Clear();
            pooledDetailsBuilder.Clear();

            return finalResultString;
        }

        /// <summary>
        /// [VIEW-02] Appends the list of mass capacity levels to the report builder.
        /// Includes a verbose flag to force display even if no capacity levels are modified.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the report to.</param>
        /// <param name="massModel">The mass capacity model containing the capacity levels.</param>
        /// <param name="verbose">If true, forces display even if no capacity levels are modified.</param>
        private static void AppendCapacities(StringBuilder builder, MassCapacityModel massModel, bool verbose)
        {
            bool hasAnyCapacities = verbose;
            if (!hasAnyCapacities)
            {
                for (int i = 0; i < massModel.CapacityLevels.Length; i++)
                {
                    if (Math.Abs(massModel.CapacityLevels[i] - 1.0f) >= SettingsDefaults.EfficiencyEpsilon)
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
                builder.AppendLine("OverHaulers_CapacitiesHeader".Translate().ToString());
                for (int i = 0; i < CapacityTranslationKeys.Length; i++)
                {
                    AppendCapacityLine(builder, CapacityTranslationKeys[i], massModel.CapacityLevels[i], verbose);
                }
            }
        }

        /// <summary>
        /// Appends the list of athletic impacts (ailments) to the report builder.
        /// Includes a verbose flag to force display even if no athletic impacts are present.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the report to.</param>
        /// <param name="massModel">The mass capacity model containing athletic impacts.</param>
        /// <param name="verbose">If true, forces display even if no athletic impacts are present.</param>
        private static void AppendAthleticImpacts(StringBuilder builder, MassCapacityModel massModel, bool verbose)
        {
            pooledListBuilder.Clear();
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
                builder.AppendLine("OverHaulers_AthleticImpactsHeader".Translate().ToString());
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

            pooledListBuilder.Clear();
        }

        /// <summary>
        /// Appends the structural sections of the report, including parts and sub-parts, to the report builder.
        /// Includes a verbose flag to force display even if no structural modifications are present.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the report sections to.</param>
        /// <param name="massModel">The mass model containing evaluated parts and ailments.</param>
        /// <param name="verbose">Flag to force display even if no structural modifications are present.</param>
        private static void AppendStructuralSections(StringBuilder builder, MassCapacityModel massModel, bool verbose)
        {
            for (int i = 0; i < massModel.EvaluatedParts.Count; i++)
            {
                PartViewNode part = massModel.EvaluatedParts[i];

                // Skip unmodified parts unless verbose is true or the part has modified descendants
                if (!verbose && Math.Abs(part.TotalOffset) < SettingsDefaults.EfficiencyEpsilon && !HasModifiedDescendants(part))
                {
                    continue;
                }

                builder.AppendLine();
                builder.Append(Indent2);
                builder.Append(part.Label);

                // Append efficiency rating if the part has a prosthetic or if its efficiency rating deviates from 1.0 by more than the epsilon threshold
                if (part.HasProsthetic || Math.Abs(part.EfficiencyRating - 1.0f) >= SettingsDefaults.EfficiencyEpsilon)
                {
                    builder.Append(" [");
                    string effStr = part.EfficiencyRating.ToStringPercent();
                    builder.Append(effStr.Colorize(GetMedicalColor(part.EfficiencyRating)));
                    builder.Append("]");
                }

                builder.Append(": ");
                builder.Append(part.TotalOffset.ToStringMassOffset());
                AppendAilmentSuffix(builder, part);
                builder.AppendLine();

                AppendGroupSubtotalRow(builder, part, verbose);
                AppendStructuralSubParts(builder, part, verbose);
            }
        }

        /// <summary>
        /// Appends a subtotal row for a given part, displaying the offsets for prosthetic, athletic, and health factors if they are present or if
        ///  verbose is true.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the subtotal row to.</param>
        /// <param name="part">The part for which to append the subtotal row.</param>
        /// <param name="verbose">Flag to force display even if no offsets are present.</param>
        private static void AppendGroupSubtotalRow(StringBuilder builder, PartViewNode part, bool verbose)
        {
            bool hasProsthetic = Math.Abs(part.ProstheticOffset) >= SettingsDefaults.EfficiencyEpsilon;
            bool hasAthletic = Math.Abs(part.AthleticOffset) >= SettingsDefaults.EfficiencyEpsilon;
            bool hasHealth = Math.Abs(part.HealthOffset) >= SettingsDefaults.EfficiencyEpsilon;

            int activeFactors = (hasProsthetic ? 1 : 0) + (hasAthletic ? 1 : 0) + (hasHealth ? 1 : 0);

            if (activeFactors > 0 || verbose)
            {
                builder.Append(Indent2);
                bool first = true;

                if (hasProsthetic || verbose)
                {
                    builder.Append(InfoCardOverlay.TagProsthetic + " ");
                    builder.Append(part.ProstheticOffset.ToStringMassOffset());
                    first = false;
                }

                if (hasAthletic || verbose)
                {
                    if (!first) builder.Append("  ");
                    builder.Append(InfoCardOverlay.TagAthletic + " ");
                    builder.Append(part.AthleticOffset.ToStringMassOffset());
                    first = false;
                }

                if (hasHealth || verbose)
                {
                    if (!first) builder.Append("  ");
                    builder.Append(InfoCardOverlay.TagInjury + " ");
                    builder.Append(part.HealthOffset.ToStringMassOffset());
                }

                builder.AppendLine();
            }
        }

        /// <summary>
        /// Appends the structural sub-parts of the specified part to the provided StringBuilder, including only modified sub-parts unless verbose
        ///  mode is enabled.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the structural sub-parts will be appended.</param>
        /// <param name="part">The parent part whose structural sub-parts are to be appended.</param>
        /// <param name="verbose">Indicates whether to include all sub-parts regardless of modification status.</param>
        private static void AppendStructuralSubParts(StringBuilder builder, PartViewNode part, bool verbose)
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
                AppendSubPartsRecursive(builder, part, 0, verbose);
            }
        }

        /// <summary>
        /// Recursively appends the structural sub-parts of the specified parent part to the provided StringBuilder, including only modified
        ///  sub-parts unless verbose mode is enabled.
        /// </summary>
        /// <param name="statsBuilder">The StringBuilder to which the structural sub-parts will be appended.</param>
        /// <param name="parent">The parent part whose structural sub-parts are to be appended.</param>
        /// <param name="depth">The current depth of the recursive traversal, used for indentation.</param>
        /// <param name="verbose">Indicates whether to include all sub-parts regardless of modification status.</param>
        private static void AppendSubPartsRecursive(StringBuilder statsBuilder, PartViewNode parent, int depth, bool verbose)
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
                    AppendSubPartLine(statsBuilder, sub, depth, verbose);
                    statsBuilder.AppendLine();

                    AppendSubPartsRecursive(statsBuilder, sub, depth + 1, verbose);
                }
            }
        }

        /// <summary>
        /// Appends a single structural sub-part line to the provided StringBuilder, including details about prosthetics, implants, and damage status.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the sub-part line will be appended.</param>
        /// <param name="sub">The sub-part whose details are to be appended.</param>
        /// <param name="depth">The current depth of the sub-part within the hierarchy, used for indentation.</param>
        /// <param name="verbose">Indicates whether to include all details regardless of modification status.</param>
        private static void AppendSubPartLine(StringBuilder builder, PartViewNode sub, int depth, bool verbose)
        {
            builder.Append("- ");
            builder.Append(sub.Label);

            bool isDamaged = sub.HealthFraction < 1.0f - SettingsDefaults.EfficiencyEpsilon;
            bool hasProsthetic = sub.HasProsthetic;
            bool isMissing = sub.IsMissing;
            bool hasImplant = !string.IsNullOrEmpty(sub.AthleticImplantName);

            if (hasProsthetic)
            {
                builder.Append(": [");
                builder.Append(sub.ProstheticName.Colorize(sub.ProstheticColor));
                builder.Append(" ");
                string effStr = sub.EfficiencyRating.ToStringPercent();
                builder.Append(effStr.Colorize(sub.ProstheticColor));

                if (isDamaged || verbose)
                {
                    builder.Append(", ");
                    string hpStr = sub.HealthFraction.ToStringPercent();
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
                    string ignoredLabel = "OverHaulers_MissingIgnored".Translate().ToString();
                    builder.Append(ignoredLabel.Colorize(SettingsViewUtilities.DescriptionTextColor));
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
                        string hpStr = sub.HealthFraction.ToStringPercent();
                        builder.Append(hpStr.Colorize(GetMedicalColor(sub.HealthFraction)));
                        builder.Append("]");
                    }

                    if (hasImplant)
                    {
                        builder.Append("[");
                        builder.Append(sub.AthleticImplantName.Colorize(sub.AthleticImplantColor));
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
                builder.Append(sub.AthleticImplantName.Colorize(sub.AthleticImplantColor));
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
        /// Appends a line representing the capacity of a specific attribute to the provided StringBuilder, including the value and its modification status.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the capacity line will be appended.</param>
        /// <param name="translationKey">The translation key for the capacity label.</param>
        /// <param name="value">The current value of the capacity.</param>
        /// <param name="verbose">Indicates whether to include all details regardless of modification status.</param>
        private static void AppendCapacityLine(StringBuilder builder, string translationKey, float value, bool verbose)
        {
            float difference = value - 1.0f;
            bool isModified = Math.Abs(difference) >= SettingsDefaults.EfficiencyEpsilon;

            if (isModified || verbose)
            {
                builder.Append(Indent4);
                string pctText = value.ToStringPercent();
                builder.AppendLine(translationKey.Translate(pctText.Colorize(GetMedicalColor(value))).ToString());
            }
        }

        /// <summary>
        /// Appends an ailment suffix for the specified part to the provided StringBuilder, if the part has a local ailment.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the ailment suffix will be appended.</param>
        /// <param name="part">The part whose local ailment is to be appended as a suffix.</param>
        private static void AppendAilmentSuffix(StringBuilder builder, PartViewNode part)
        {
            if (part == null || string.IsNullOrEmpty(part.LocalAilmentName)) return;

            builder.Append("OverHaulers_AilmentPrefix".Translate(part.LocalAilmentName.Colorize(part.LocalAilmentColor)).ToString());
        }

        /// <summary>
        /// Determines the appropriate color for a medical value based on its magnitude, using configured thresholds for healthy, critical, and
        ///  boosted states.
        /// </summary>
        /// <param name="value">The medical value for which to determine the color.</param>
        /// <returns>The color corresponding to the specified medical value.</returns>
        public static Color GetMedicalColor(float value)
        {
            Color healthy = OverHaulers.settings?.colorHealthy ?? SettingsDefaults.ColorHealthyDefault;
            Color critical = OverHaulers.settings?.colorCritical ?? SettingsDefaults.ColorCriticalDefault;
            Color boosted = OverHaulers.settings?.colorBoosted ?? SettingsDefaults.ColorBoostedDefault;

            if (value >= 2.0f) return boosted;
            if (value > 1.0f)
            {
                float t = (value - 1.0f) / 1.0f;
                return Color.Lerp(healthy, boosted, t);
            }
            if (value >= 0.2f)
            {
                float t = (value - 0.2f) / 0.8f;
                return Color.Lerp(critical, healthy, t);
            }
            
            return critical;
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
                && Mathf.Approximately(sub.EfficiencyRating, parent.EfficiencyRating)
                && (sub.IsInherited || Mathf.Approximately(sub.HealthFraction, parent.HealthFraction))
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
    }
}