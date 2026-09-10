using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Passive, read-only node structure representing a single body part in the compiled anatomical UI view tree.
    /// Stores pre-calculated caravan mass capacity offsets and colorized display labels for tooltips and inspect cards.
    /// Annotated with [VIEW-XX] tags indicating layout section mapping.
    /// </summary>
    public class PartViewNode
    {
        #region 1. FIELDS & PROPERTIES

        private List<PartViewNode> subParts;

        /// <summary>[VIEW-04] The engine BodyPartRecord associated with this visual view node.</summary>
        public BodyPartRecord Record { get; set; }

        /// <summary>[VIEW-04] Localized display label name for this body part.</summary>
        public string Label { get; set; }

        /// <summary>[VIEW-03] True if this part represents a metabolic organ rather than a structural bone or limb.</summary>
        public bool IsOrgan { get; set; }
        
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

        /// <summary>[VIEW-04] Evaluates whether this part deviates from organic baseline defaults in a mass-relevant way.</summary>
        public bool IsModified => IsMissing 
            || (HasProsthetic && Math.Abs(EfficiencyRating - 1.0f) >= SettingsDefaults.EfficiencyEpsilon)
            || HealthFraction < 1.0f 
            || !string.IsNullOrEmpty(AthleticImplantName) 
            || !string.IsNullOrEmpty(LocalAilmentName);

        #endregion

        #region 2. SUB-PART COLLECTION MANAGEMENT

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
        }

        #endregion
    }
}