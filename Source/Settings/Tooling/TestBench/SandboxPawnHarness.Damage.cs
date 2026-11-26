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
        public readonly float DamageFraction;
        public readonly float HealthBefore;
        public readonly float HealthAfter;
        public readonly float AppliedDamage;

        public SimulatedDamage(
            BodyPartRecord part,
            DamageDef damage,
            Hediff injury,
            float damageFraction,
            float healthBefore,
            float healthAfter,
            float appliedDamage)
        {
            Part = part;
            Damage = damage;
            Injury = injury;
            DamageFraction = damageFraction;
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
        /// <param name="part">The body part to apply synthetic damage to.</param>
        /// <param name="damage">The type of damage to simulate.</param>
        /// <param name="damageFraction">The fraction of the part's health to apply as damage.</param>
        /// <remarks>
        /// The damage is applied synthetically and does not go through the full native damage pipeline.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the synthetic damage cannot be represented on the specified body part.
        /// </exception>
        public void SimulateDamage(BodyPartRecord part, DamageDef damage, float damageFraction)
        {
            if (!TrySimulateDamage(part, damage, damageFraction))
            {
                throw new InvalidOperationException(
                    "The requested synthetic damage cannot be represented on this body part.");
            }
        }

        /// <summary>
        /// Tries to apply synthetic damage without surfacing an error for a random candidate that becomes incompatible.
        /// </summary>
        /// <param name="part">The body part to apply synthetic damage to.</param>
        /// <param name="damage">The type of damage to simulate.</param>
        /// <param name="damageFraction">The fraction of the part's health to apply as damage.</param>
        /// <returns>True if the synthetic damage was successfully applied; otherwise, false.</returns>
        /// <remarks>
        /// The damage is applied synthetically and does not go through the full native damage pipeline.
        /// </remarks>
        private bool TrySimulateDamage(BodyPartRecord part, DamageDef damage, float damageFraction)
        {
            if (headlessPawn?.health?.hediffSet == null || part == null || damage == null)
                return false;
            if (float.IsNaN(damageFraction) || float.IsInfinity(damageFraction) ||
                damageFraction <= 0f || damageFraction > 1f)
                return false;
            if (headlessPawn.health.hediffSet.PartIsMissing(part))
                return false;
            if (!CanSimulateDamage(part, damage, damageFraction))
                return false;

            using (EnterSafetyFallbackScope())
            {
                float healthBefore = headlessPawn.health.hediffSet.GetPartHealth(part);
                float requestedDamage = Math.Max(0.01f, healthBefore * damageFraction);
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
                    damageFraction,
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
        /// <param name="part">The body part to check for synthetic damage eligibility.</param>
        /// <param name="damage">The type of damage to simulate.</param>
        /// <param name="damageFraction">The fraction of the part's health to apply as damage.</param>
        /// <returns>True if the synthetic damage can be represented on the specified body part; otherwise, false.</returns>
        /// <remarks>
        /// The check considers whether the part is missing, the damage fraction is valid, and a compatible injury can be applied.
        /// </remarks>
        public bool CanSimulateDamage(BodyPartRecord part, DamageDef damage, float damageFraction)
        {
            if (headlessPawn?.health?.hediffSet == null || part == null || damage == null ||
                float.IsNaN(damageFraction) || float.IsInfinity(damageFraction) ||
                damageFraction <= 0f || damageFraction > 1f ||
                headlessPawn.health.hediffSet.PartIsMissing(part))
                return false;

            using (EnterSafetyFallbackScope())
            {
                HediffDef injuryDef = ResolveInjuryDef(damage, part);
                return injuryDef != null &&
                    typeof(Hediff_Injury).IsAssignableFrom(injuryDef.hediffClass) &&
                    headlessPawn.health.hediffSet.GetPartHealth(part) > 0f &&
                    damageFraction < 1f;
            }
        }

        /// <summary>
        /// Rerolls randomized synthetic crush injuries for a selection of currently eligible sandbox parts.
        /// Existing sandbox damage on the selected parts is removed first, while source hediffs remain unchanged.
        /// Each chosen part loses 15-75% of its remaining health; at least one eligible part is affected when possible.
        /// </summary>
        /// <param name="parts">The list of body parts to consider for random synthetic damage.</param>
        /// <remarks>
        /// The method first clears any existing synthetic damage on the specified parts, then attempts to apply new random crush damage.
        /// At least one eligible part will receive damage if possible.
        /// </remarks>
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

        /// <summary>
        /// Removes synthetic damage from one part while preserving authentic source hediffs.
        /// </summary>
        /// <param name="part">The body part from which to clear synthetic damage.</param>
        /// <remarks>
        /// This method only removes synthetic damage applied through the sandbox harness and preserves any authentic injuries.
        /// </remarks>
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
        /// <param name="part">The body part to simulate removal for.</param>
        /// <remarks>
        /// This method simulates the removal of a body part by applying a missing-part hediff and recording the synthetic damage.
        /// </remarks>
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

        /// <summary>
        /// Increments the live intent generation counter, indicating that new synthetic damage has been applied.
        /// </summary>
        private void BumpLiveIntentGeneration()
        {
            liveIntentGeneration++;
        }

        #endregion

        #region 3. [TEST-05-DAMAGE] PRIVATE RESOLUTION

        /// <summary>
        /// Resolves the appropriate hediff definition for a given damage type and body part.
        /// </summary>
        /// <param name="damage">The type of damage being applied.</param>
        /// <param name="part">The body part receiving the damage.</param>
        /// <returns>The resolved hediff definition, or null if none is applicable.</returns>
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
