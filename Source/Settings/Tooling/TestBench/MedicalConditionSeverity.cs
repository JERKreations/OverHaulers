using System;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>[TEST-06] Resolves condition severity and labels for the Test Bench.</summary>
    internal static class MedicalConditionSeverity
    {
        #region 1. [TEST-06] STAGE RESOLVER & LABELS

        /// <summary>
        /// Calculates the peak impact severity level for a staged condition.
        /// Caps finite lethal thresholds to 99%, skips zero-consciousness fatal stages, and uses
        /// a finite final-stage fallback when maxSeverity is the engine's unbounded sentinel.
        /// This does not guarantee survival when combined with other conditions.
        /// </summary>
        /// <param name="def">The target hediff definition.</param>
        /// <param name="stageSuffix">Outputs a human-readable stage suffix (e.g. "Extreme", "Major", "Withdrawal").</param>
        /// <returns>The initial target severity to assign to the simulated hediff.</returns>
        public static float GetPeakImpactSeverity(HediffDef def, out string stageSuffix)
        {
            stageSuffix = null;
            if (def == null) return 0.5f;

            // BREAKPOINT ANCHOR: Absolute Lethal Ceiling (99% of lethalSeverity)
            bool hasFiniteLethalSeverity = IsFinitePositiveSeverity(def.lethalSeverity);
            bool hasFiniteMaximumSeverity = IsFinitePositiveSeverity(def.maxSeverity);
            float safeLethalCeiling = hasFiniteLethalSeverity ? (def.lethalSeverity * 0.99f) : float.MaxValue;

            if (def.stages == null || def.stages.Count == 0)
            {
                float baseSeverity = IsFinitePositiveSeverity(def.initialSeverity) ? def.initialSeverity : 0.5f;
                return Math.Min(baseSeverity, safeLethalCeiling);
            }

            int bestStageIndex = -1;
            float maxImpactScore = 0f;

            for (int i = 0; i < def.stages.Count; i++)
            {
                HediffStage stage = def.stages[i];
                if (stage == null) continue;

                // 1. Guard against stages that cause immediate zero-consciousness death
                if (StageCausesImmediateZeroConsciousness(stage))
                {
                    continue;
                }

                // 2. Guard against stages starting at or above the lethal boundary
                if (hasFiniteLethalSeverity && stage.minSeverity >= def.lethalSeverity)
                {
                    continue;
                }

                float stageScore = 0f;

                if (stage.capMods != null)
                {
                    for (int c = 0; c < stage.capMods.Count; c++)
                    {
                        PawnCapacityModifier capMod = stage.capMods[c];
                        if (capMod != null &&
                            (MedicalClassifier.IsAthleticCapacity(capMod.capacity) ||
                             capMod.capacity == PawnCapacityDefOf.BloodFiltration))
                        {
                            stageScore += Math.Abs(capMod.offset);
                            if (capMod.postFactor != 1.0f) stageScore += Math.Abs(1.0f - capMod.postFactor);
                            if (capMod.setMax != 1.0f) stageScore += Math.Abs(1.0f - capMod.setMax) * 2f;
                        }
                    }
                }

                if (Math.Abs(stage.partEfficiencyOffset) >= PawnDataRegistry.EfficiencyEpsilon)
                {
                    stageScore += Math.Abs(stage.partEfficiencyOffset);
                }

                if (stage.painOffset > 0f) stageScore += stage.painOffset * 0.5f;
                if (stage.painFactor != 1.0f) stageScore += Math.Abs(1.0f - stage.painFactor) * 0.5f;

                if (stageScore > maxImpactScore)
                {
                    maxImpactScore = stageScore;
                    bestStageIndex = i;
                }
            }

            // If all stages were either zero impact or fatal, fall back to safe baseline
            if (bestStageIndex == -1)
            {
                float baseSeverity = IsFinitePositiveSeverity(def.initialSeverity) ? def.initialSeverity : 0.5f;
                return Math.Min(baseSeverity, safeLethalCeiling);
            }

            HediffStage bestStage = def.stages[bestStageIndex];
            if (!string.IsNullOrEmpty(bestStage.label))
            {
                stageSuffix = bestStage.label.CapitalizeFirst();
            }

            // Calculate target severity within the selected stage
            float targetSeverity;

            if (bestStageIndex + 1 < def.stages.Count)
            {
                // Stage has a defined upper boundary: place at 95% into the stage's range
                float nextMin = def.stages[bestStageIndex + 1].minSeverity;
                targetSeverity = bestStage.minSeverity + ((nextMin - bestStage.minSeverity) * 0.95f);
            }
            else
            {
                // Final stage: place at 99% of lethal severity or 95% of max severity
                if (hasFiniteLethalSeverity)
                {
                    targetSeverity = def.lethalSeverity * 0.99f;
                }
                else if (hasFiniteMaximumSeverity)
                {
                    targetSeverity = bestStage.minSeverity + ((def.maxSeverity - bestStage.minSeverity) * 0.95f);
                }
                else
                {
                    targetSeverity = GetUnboundedFinalStageSeverity(def, bestStageIndex);
                }
            }

            // Final safety clamp: never exceed 99% of lethal threshold
            targetSeverity = Math.Min(targetSeverity, safeLethalCeiling);

            if (hasFiniteMaximumSeverity)
            {
                targetSeverity = Math.Min(targetSeverity, def.maxSeverity);
            }

            if (float.IsNaN(targetSeverity) || float.IsInfinity(targetSeverity))
            {
                targetSeverity = bestStage.minSeverity;
            }

            return Math.Max(targetSeverity, bestStage.minSeverity);
        }

        /// <summary>
        /// Determines whether the specified severity value is finite and positive.
        /// </summary>
        /// <param name="severity">The severity value to evaluate.</param>
        /// <returns>True if the severity is finite and positive; otherwise, false.</returns>
        private static bool IsFinitePositiveSeverity(float severity)
        {
            return severity > 0f && severity < float.MaxValue &&
                   !float.IsNaN(severity) && !float.IsInfinity(severity);
        }

        /// <summary>
        /// Calculates the unbounded final stage severity for a given HediffDef and stage index.
        /// </summary>
        /// <param name="def">The HediffDef containing the stages.</param>
        /// <param name="stageIndex">The index of the stage to evaluate.</param>
        /// <returns>The calculated unbounded final stage severity.</returns>
        private static float GetUnboundedFinalStageSeverity(HediffDef def, int stageIndex)
        {
            HediffStage stage = def.stages[stageIndex];
            float increment = 0.1f;

            if (stageIndex > 0)
            {
                HediffStage previousStage = def.stages[stageIndex - 1];
                if (previousStage != null)
                {
                    float previousRange = stage.minSeverity - previousStage.minSeverity;
                    if (previousRange > 0f && !float.IsNaN(previousRange) && !float.IsInfinity(previousRange))
                    {
                        increment = Math.Min(previousRange, 1f);
                    }
                }
            }

            double target = (double)stage.minSeverity + ((double)increment * 0.95d);
            if (double.IsNaN(target) || double.IsInfinity(target) || target >= float.MaxValue)
            {
                return stage.minSeverity;
            }

            return (float)target;
        }

        /// <summary>
        /// Determines whether the specified HediffStage causes immediate zero consciousness.
        /// </summary>
        /// <param name="stage">The HediffStage to evaluate for immediate zero consciousness.</param>
        /// <returns>True if the stage causes immediate zero consciousness; otherwise, false.</returns>
        private static bool StageCausesImmediateZeroConsciousness(HediffStage stage)
        {
            if (stage?.capMods == null) return false;

            for (int c = 0; c < stage.capMods.Count; c++)
            {
                PawnCapacityModifier mod = stage.capMods[c];
                if (mod != null && mod.capacity == PawnCapacityDefOf.Consciousness)
                {
                    if (mod.setMax <= 0.001f || mod.offset <= -1.0f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a formatted display label for a condition definition, including its peak-impact stage suffix if applicable.
        /// </summary>
        /// <param name="def">The HediffDef for which to retrieve the formatted condition label.</param>
        /// <returns>A formatted string representing the condition label, including stage indicators if impactful.</returns>
        public static string GetFormattedConditionDefLabel(HediffDef def)
        {
            if (def == null) return string.Empty;

            GetPeakImpactSeverity(def, out string stageSuffix);
            string baseLabel = def.LabelCap.ToString();

            if (!string.IsNullOrEmpty(stageSuffix) && !baseLabel.ToLowerInvariant().Contains(stageSuffix.ToLowerInvariant()))
            {
                return $"{baseLabel} ({stageSuffix})";
            }

            return baseLabel;
        }

        /// <summary>
        /// [TEST-06] Returns the engine's label for an existing hediff, which reflects its actual current stage.
        /// Unlike <see cref="GetFormattedConditionDefLabel"/>, this method does not predict a peak-impact stage.
        /// </summary>
        /// <param name="hediff">The live or simulated hediff to format.</param>
        /// <returns>The current engine label, or an empty string when the hediff is null.</returns>
        public static string GetFormattedConditionLabel(Hediff hediff)
        {
            if (hediff == null) return string.Empty;

            string actualLabel = hediff.LabelCap.ToString();
            return string.IsNullOrEmpty(actualLabel)
                ? GetFormattedConditionDefLabel(hediff.def)
                : actualLabel;
        }

        #endregion

    }
}
