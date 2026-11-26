using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>Tracks one synthetic damage intent applied to the isolated sandbox pawn.</summary>
    public sealed class SimulatedDamage
    {
        public readonly BodyPartRecord Part;
        public readonly DamageDef Damage;
        public readonly Hediff Injury;
        public readonly float RequestedSeverity;
        public readonly float HealthBefore;
        public readonly float HealthAfter;
        public readonly float AppliedDamage;

        public SimulatedDamage(
            BodyPartRecord part,
            DamageDef damage,
            Hediff injury,
            float requestedSeverity,
            float healthBefore,
            float healthAfter,
            float appliedDamage)
        {
            Part = part;
            Damage = damage;
            Injury = injury;
            RequestedSeverity = requestedSeverity;
            HealthBefore = healthBefore;
            HealthAfter = healthAfter;
            AppliedDamage = appliedDamage;
        }
    }

    public partial class SandboxPawnHarness
    {
        #region 1. [TEST-05-DAMAGE] DAMAGE INTENT STATE

        /// <summary>Synthetic damage intents grouped by their selected body-part record.</summary>
        public readonly Dictionary<BodyPartRecord, List<SimulatedDamage>> DamageDeltas =
            new Dictionary<BodyPartRecord, List<SimulatedDamage>>();

        #endregion

        #region 2. [TEST-05-DAMAGE] DAMAGE APPLICATION

        /// <summary>
        /// Applies a synthetic injury to one selected sandbox part and records the resulting health state.
        /// This intentionally avoids the full native damage pipeline, which depends on live-world pawn state.
        /// </summary>
        public void SimulateDamage(BodyPartRecord part, DamageDef damage, float remainingHealthFraction)
        {
            if (!TrySimulateDamage(part, damage, remainingHealthFraction))
            {
                throw new InvalidOperationException(
                    "The requested synthetic damage cannot be represented on this body part.");
            }
        }

        /// <summary>
        /// Tries to apply synthetic damage without surfacing an error for a random candidate that becomes incompatible.
        /// </summary>
        private bool TrySimulateDamage(BodyPartRecord part, DamageDef damage, float remainingHealthFraction)
        {
            if (headlessPawn?.health?.hediffSet == null || part == null || damage == null)
                return false;
            if (float.IsNaN(remainingHealthFraction) || float.IsInfinity(remainingHealthFraction) ||
                remainingHealthFraction <= 0f || remainingHealthFraction > 1f)
                return false;
            if (headlessPawn.health.hediffSet.PartIsMissing(part))
                return false;
            if (!CanSimulateDamage(part, damage, remainingHealthFraction))
                return false;

            using (EnterSafetyFallbackScope())
            {
                float healthBefore = headlessPawn.health.hediffSet.GetPartHealth(part);
                float requestedDamage = Math.Max(0.01f, healthBefore * remainingHealthFraction);
                HediffDef injuryDef = ResolveInjuryDef(damage, part);
                if (injuryDef == null)
                    return false;

                Hediff injury = HediffMaker.MakeHediff(injuryDef, headlessPawn, part);
                if (injury == null)
                    return false;
                injury.Severity = requestedDamage;
                headlessPawn.health.AddHediff(injury, part);

                float healthAfter = headlessPawn.health.hediffSet.GetPartHealth(part);
                if (healthAfter >= healthBefore)
                {
                    if (headlessPawn.health.hediffSet.hediffs.Contains(injury))
                        headlessPawn.health.RemoveHediff(injury);
                    return false;
                }

                if (!DamageDeltas.TryGetValue(part, out List<SimulatedDamage> changes))
                {
                    changes = new List<SimulatedDamage>(2);
                    DamageDeltas[part] = changes;
                }

                changes.Add(new SimulatedDamage(
                    part,
                    damage,
                    injury,
                    remainingHealthFraction,
                    healthBefore,
                    healthAfter,
                    Math.Max(0f, healthBefore - healthAfter)));

                PruneRemovedConditions();
                DirtyHealthCache();
                BumpLiveIntentGeneration();
                return true;
            }
        }

        /// <summary>
        /// Returns whether the requested synthetic damage can be represented by a compatible injury.
        /// </summary>
        public bool CanSimulateDamage(BodyPartRecord part, DamageDef damage, float remainingHealthFraction)
        {
            if (headlessPawn?.health?.hediffSet == null || part == null || damage == null ||
                float.IsNaN(remainingHealthFraction) || float.IsInfinity(remainingHealthFraction) ||
                remainingHealthFraction <= 0f || remainingHealthFraction > 1f ||
                headlessPawn.health.hediffSet.PartIsMissing(part))
                return false;

            using (EnterSafetyFallbackScope())
            {
                HediffDef injuryDef = ResolveInjuryDef(damage, part);
                return injuryDef != null &&
                    typeof(Hediff_Injury).IsAssignableFrom(injuryDef.hediffClass) &&
                    headlessPawn.health.hediffSet.GetPartHealth(part) > 0f &&
                    remainingHealthFraction < 1f;
            }
        }

        /// <summary>
        /// Rerolls randomized synthetic crush injuries for a selection of currently eligible sandbox parts.
        /// Existing sandbox damage on the selected parts is removed first, while source hediffs remain unchanged.
        /// Each chosen part loses 15-75% of its remaining health; at least one eligible part is affected when possible.
        /// </summary>
        public void SimulateRandomDamage(IReadOnlyList<BodyPartRecord> parts)
        {
            if (parts == null || parts.Count == 0) return;

            for (int i = 0; i < parts.Count; i++)
            {
                ClearSimulatedDamage(parts[i]);
            }

            BodyPartRecord firstEligible = null;
            bool applied = false;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (headlessPawn?.health?.hediffSet == null || part == null ||
                    part.coverageAbs <= 0f || headlessPawn.health.hediffSet.PartIsMissing(part))
                    continue;

                if (firstEligible == null) firstEligible = part;
                if (!Rand.Chance(0.65f)) continue;

                float severity = Rand.Range(0.15f, 0.75f);
                while (severity >= 0.05f && !CanSimulateDamage(part, DamageDefOf.Crush, severity))
                    severity *= 0.5f;
                if (severity < 0.05f) continue;

                if (TrySimulateDamage(part, DamageDefOf.Crush, severity))
                    applied = true;
            }

            if (!applied && firstEligible != null)
            {
                float severity = Rand.Range(0.15f, 0.75f);
                while (severity >= 0.05f && !CanSimulateDamage(firstEligible, DamageDefOf.Crush, severity))
                    severity *= 0.5f;
                if (severity >= 0.05f)
                    TrySimulateDamage(firstEligible, DamageDefOf.Crush, severity);
            }
        }

        /// <summary>Removes synthetic damage from one part while preserving authentic source hediffs.</summary>
        public void ClearSimulatedDamage(BodyPartRecord part)
        {
            if (part == null || !DamageDeltas.TryGetValue(part, out List<SimulatedDamage> changes))
                return;

            using (EnterSafetyFallbackScope())
            {
                for (int i = changes.Count - 1; i >= 0; i--)
                {
                    Hediff injury = changes[i].Injury;
                    if (injury != null && headlessPawn?.health?.hediffSet != null &&
                        headlessPawn.health.hediffSet.hediffs.Contains(injury))
                    {
                        headlessPawn.health.RemoveHediff(injury);
                    }
                }
                DamageDeltas.Remove(part);
                DirtyHealthCache();
                BumpLiveIntentGeneration();
            }
        }

        /// <summary>
        /// Simulates removal of one part for deterministic diagnostics and self-check fixtures.
        /// Not exposed by the interactive damage picker.
        /// </summary>
        public void SimulatePartRemoval(BodyPartRecord part)
        {
            if (headlessPawn?.health?.hediffSet == null || part == null)
                throw new InvalidOperationException("A bound sandbox pawn and target part are required.");

            using (EnterSafetyFallbackScope())
            {
                CleanDeltasRecursive(part);
                float healthBefore = headlessPawn.health.hediffSet.GetPartHealth(part);
                headlessPawn.health.RestorePart(part, null, checkStateChange: false);
                Hediff missing = HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, headlessPawn, part);
                if (missing == null)
                    throw new InvalidOperationException("Could not create the missing-part hediff.");
                headlessPawn.health.AddHediff(missing, part);
                float healthAfter = 0f;

                if (!DamageDeltas.TryGetValue(part, out List<SimulatedDamage> changes))
                {
                    changes = new List<SimulatedDamage>(2);
                    DamageDeltas[part] = changes;
                }

                changes.Add(new SimulatedDamage(
                    part,
                    DamageDefOf.SurgicalCut,
                    missing,
                    1f,
                    healthBefore,
                    healthAfter,
                    Math.Max(0f, healthBefore - healthAfter)));

                PruneRemovedConditions();
                DirtyHealthCache();
                BumpLiveIntentGeneration();
            }
        }

        private void BumpLiveIntentGeneration()
        {
            liveIntentGeneration++;
        }

        #endregion

        #region 3. [TEST-05-DAMAGE] PRIVATE RESOLUTION

        private HediffDef ResolveInjuryDef(DamageDef damage, BodyPartRecord part)
        {
            HediffDef result = damage?.hediff;
            if (part?.def != null && damage != null)
            {
                if (part.def.IsSkinCovered(part, headlessPawn.health.hediffSet) &&
                    damage.hediffSkin != null)
                    result = damage.hediffSkin;
                if (part.def.IsSolid(part, headlessPawn.health.hediffSet.hediffs) &&
                    damage.hediffSolid != null)
                    result = damage.hediffSolid;
            }

            return result;
        }

        #endregion
    }
}
