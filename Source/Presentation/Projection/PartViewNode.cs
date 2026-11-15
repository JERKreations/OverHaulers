using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Passive, read-only node structure representing a single body part in the compiled anatomical UI view tree.
    /// Stores pre-calculated caravan mass capacity offsets and colorized display labels for tooltips and inspect cards.
    /// Annotated with [VIEW-01]..[VIEW-04] tags indicating layout section mapping.
    /// </summary>
    public class PartViewNode
    {
        #region 1. FIELDS & PROPERTIES

        private List<PartViewNode> subParts;
        private List<PartImplantCold> implants;

        /// <summary>[VIEW-04] The engine BodyPartRecord associated with this visual view node.</summary>
        public BodyPartRecord Record { get; set; }

        /// <summary>[VIEW-04] Localized display label name for this body part.</summary>
        public string Label { get; set; }

        /// <summary>[VIEW-03] True if this part represents a metabolic organ rather than a structural bone or limb.</summary>
        public bool IsOrgan { get; set; }

        /// <summary>True when this part has a load-bearing structural role.</summary>
        public bool IsStructural { get; set; }

        /// <summary>True when this part declares a source for a modeled capacity.</summary>
        public bool IsCapacitySource { get; set; }

        /// <summary>True when the installed replacement directly modifies a modeled capacity.</summary>
        public bool ProstheticAffectsAthletics { get; set; }
        
        /// <summary>[VIEW-04] True if this part is completely missing or destroyed.</summary>
        public bool IsMissing { get; set; }

        /// <summary>[VIEW-04] True if this missing part was removed by a prosthetic and should be mathematically and visually ignored.</summary>
        public bool IsNeutralized { get; set; }

        /// <summary>[VIEW-04] Normalized health fraction (0.0f = destroyed, 1.0f = full health).</summary>
        public float HealthFraction { get; set; }

        /// <summary>[VIEW-03 & VIEW-04] Label name of any local physical wound or disease condition.</summary>
        public string LocalAilmentName { get; set; }

        /// <summary>[VIEW-03 & VIEW-04] UI display color for the local ailment label.</summary>
        public Color LocalAilmentColor { get; set; } = Color.white;

        /// <summary>[VIEW-04] True if an artificial prosthetic or bionic part is installed here.</summary>
        public bool HasProsthetic { get; set; }

        /// <summary>[VIEW-04] Label name of the installed prosthetic or bionic part.</summary>
        public string ProstheticName { get; set; }

        /// <summary>[VIEW-04] Efficiency multiplier of the installed prosthetic part.</summary>
        public float EfficiencyRating { get; set; }

        /// <summary>[VIEW-04] UI display color for the prosthetic label.</summary>
        public Color ProstheticColor { get; set; } = Color.white;

        /// <summary>[VIEW-04] True if prosthetic efficiency is inherited from a parent limb anchor.</summary>
        public bool IsInherited { get; set; }

        /// <summary>[VIEW-03 & VIEW-04] Label name of any installed athletic or physiological implant.</summary>
        public string AthleticImplantName { get; set; }

        /// <summary>[VIEW-03 & VIEW-04] UI display color for the athletic implant label.</summary>
        public Color AthleticImplantColor { get; set; } = Color.white;

        /// <summary>[VIEW-04] Proportional load-bearing weight budget assigned to this part.</summary>
        public float ProportionalWeight { get; set; }

        /// <summary>[VIEW-04] Pre-calculated prosthetic mass capacity offset contributed by this part in kg.</summary>
        public float ProstheticOffset { get; set; }

        /// <summary>[VIEW-04] Pre-calculated athletic mass capacity offset contributed by this part in kg.</summary>
        public float AthleticOffset { get; set; }

        /// <summary>[VIEW-04] Pre-calculated health deficit penalty applied to this part in kg.</summary>
        public float HealthOffset { get; set; }

        /// <summary>[VIEW-04] Total combined caravan mass capacity offset for this view node in kg.</summary>
        public float TotalOffset { get; set; }

        /// <summary>[VIEW-04] Lazy-initialized child sub-parts list. Thread-isolated to prevent corruption during nesting.</summary>
        public List<PartViewNode> SubParts
        {
            get => subParts ?? (subParts = new List<PartViewNode>());
            set => subParts = value;
        }

        /// <summary>Installed implant definitions attached directly to this part.</summary>
        public List<PartImplantCold> Implants => implants ?? (implants = new List<PartImplantCold>());

        public bool HasImplants => implants != null && implants.Count > 0;

        public bool HasRelevantImplant
        {
            get
            {
                if (implants == null) return false;
                for (int i = 0; i < implants.Count; i++)
                {
                    if (implants[i].AffectsAthletics) return true;
                }
                return false;
            }
        }

        /// <summary>True when an added part belongs in the standard breakdown and link tray.</summary>
        public bool IsRelevantAddedPart => IsRelevantReplacement(ProstheticAffectsAthletics);

        /// <summary>True when this part is relevant to the standard breakdown.</summary>
        public bool IsRelevant
        {
            get
            {
                if (IsRelevantAddedPart ||
                    (!HasProsthetic && !string.IsNullOrEmpty(AthleticImplantName))) return true;
                if (HasRelevantImplant) return true;

                if (IsNeutralized || (!IsStructural && !IsCapacitySource)) return false;
                return IsMissing || HealthFraction < 1.0f ||
                    !string.IsNullOrEmpty(LocalAilmentName);
            }
        }

        #endregion

        #region 2. PART RELEVANCE & COLLECTION MANAGEMENT

        /// <summary>Evaluates a replacement's relevance using its own capacity effects.</summary>
        public bool IsRelevantReplacement(bool affectsAthletics)
        {
            return HasProsthetic && !IsNeutralized && (IsStructural || IsCapacitySource || affectsAthletics);
        }

        /// <summary>
        /// Adds a sub-part to this part's collection of child sub-parts.
        /// </summary>
        /// <param name="sub">The sub-part to be added to this part's collection of child sub-parts.</param>
        public void AddSubPart(PartViewNode sub)
        {
            if (sub == null) return;
            subParts = subParts ?? new List<PartViewNode>();
            subParts.Add(sub);
        }

        /// <summary>Adds a directly installed implant to this part's presentation metadata.</summary>
        public void AddImplant(PartImplantCold implant)
        {
            Implants.Add(implant);
        }

        #endregion

        #region 3. NODE STATE RECYCLING

        /// <summary>
        /// Resets the state of this part view node, clearing all cached data and returning it to its default state.
        /// </summary>
        /// <remarks>
        /// This method is typically used to recycle part view nodes for reuse, ensuring that no stale data persists between evaluations.
        /// </remarks>
        public void Reset()
        {
            Record = null;
            Label = null;
            IsOrgan = false;
            IsStructural = false;
            IsCapacitySource = false;
            ProstheticAffectsAthletics = false;
            IsMissing = false;
            IsNeutralized = false;
            HealthFraction = 1.0f;
            LocalAilmentName = null;
            LocalAilmentColor = Color.white;
            HasProsthetic = false;
            ProstheticName = null;
            EfficiencyRating = 1.0f;
            ProstheticColor = Color.white;
            IsInherited = false;
            AthleticImplantName = null;
            AthleticImplantColor = Color.white;
            ProportionalWeight = 0f;
            ProstheticOffset = 0f;
            AthleticOffset = 0f;
            HealthOffset = 0f;
            TotalOffset = 0f;
            
            subParts?.Clear();
            implants?.Clear();
        }

        #endregion
    }
}