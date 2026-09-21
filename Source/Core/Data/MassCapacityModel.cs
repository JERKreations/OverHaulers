using System;
using System.Collections.Generic;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-01 to VIEW-04] Representation model containing all calculated presentation factors of a pawn's Caravan Mass Capacity.
    /// Manages an internal object pool of <see cref="PartViewNode"/> instances to guarantee 0 GC allocations during UI updates.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public class MassCapacityModel
    {
        #region 1. STATIC FALLBACK & SINGLETONS

        /// <summary>
        /// Immutable static fallback model returned during re-entrancy conflicts or invalid off-thread queries
        /// to guarantee zero runtime heap allocations.
        /// </summary>
        public static readonly MassCapacityModel Empty = new MassCapacityModel();

        #endregion

        #region 2. MODEL FIELDS & LOOKUP BUFFERS

        /// <summary>[VIEW-02] Array of capacity levels for Breathing, Blood Pumping, Moving, Manipulation, and Consciousness.</summary>
        public float[] CapacityLevels { get; set; } = new float[5];

        /// <summary>[VIEW-01] Total calculated physical Caravan Mass Capacity multiplier factor.</summary>
        public float TotalMultiplier { get; set; }

        /// <summary>[VIEW-01] Calculated aggregate Caravan Mass Capacity offset in kilograms (kg).</summary>
        public float Offset { get; set; }

        /// <summary>[VIEW-01] Fully clamped final Caravan Mass Capacity in kilograms (kg).</summary>
        public float FinalCapacity { get; set; }

        /// <summary>[VIEW-03] Unified deduplicated list of active systemic medical ailment labels.</summary>
        public UniqueList<string> Ailments { get; } = new UniqueList<string>(16);

        /// <summary>[VIEW-03 & VIEW-04] List of evaluated top-level visual body part view nodes.</summary>
        public List<PartViewNode> EvaluatedParts { get; set; } = new List<PartViewNode>();

        /// <summary>[VIEW-01 to VIEW-04] Raw explanation string containing formatting tags.</summary>
        public string Explanation { get; set; }

        /// <summary>Direct flat lookup mapping compiled species part-index slots to their visual node models for O(1) info-card hyperlinks.</summary>
        private PartViewNode[] nodeByPartIndex = new PartViewNode[64];

        private readonly List<PartViewNode> nodePool = new List<PartViewNode>(64);
        private int poolIndex = 0;

        /// <summary>Instantiates a new empty Caravan Mass Capacity model with baseline default parameters.</summary>
        public MassCapacityModel()
        {
            TotalMultiplier = 1.0f;
            Offset = 0.0f;
            FinalCapacity = 0.0f;
            Explanation = "";
        }

        #endregion

        #region 3. VISUAL NODE POOLING & RESET

        /// <summary>Acquires or instantiates a cached visual view node, avoiding backing-array allocations.</summary>
        public PartViewNode AcquireNode()
        {
            if (poolIndex < nodePool.Count)
            {
                PartViewNode node = nodePool[poolIndex];
                node.Reset();
                poolIndex++;
                return node;
            }
            else
            {
                PartViewNode node = new PartViewNode();
                nodePool.Add(node);
                poolIndex++;
                return node;
            }
        }

        /// <summary>Resets the pool pointer without clearing allocated node buffers.</summary>
        public void ResetPool()
        {
            poolIndex = 0;
            Array.Clear(nodeByPartIndex, 0, nodeByPartIndex.Length);
            Explanation = "";
        }

        /// <summary>
        /// Stores a visual node at a compiled species part-index slot for O(1) retrieval during info-card hyperlinking.
        /// </summary>
        /// <param name="partIndex">The index of the species part.</param>
        /// <param name="node">The visual node to associate with the part index.</param>
        public void SetNodeForPartIndex(int partIndex, PartViewNode node)
        {
            if (partIndex < 0) return;

            if (nodeByPartIndex.Length <= partIndex)
            {
                int targetSize = nodeByPartIndex.Length;
                while (targetSize <= partIndex) targetSize *= 2;
                Array.Resize(ref nodeByPartIndex, targetSize);
            }

            nodeByPartIndex[partIndex] = node;
        }

        /// <summary>
        /// Retrieves the visual node associated with the specified species part index, or null if no node is set.
        /// </summary>
        /// <param name="partIndex">The index of the species part to retrieve the visual node for.</param>
        /// <returns>The visual node associated with the specified part index, or null if no node is set.</returns>
        public PartViewNode GetNodeForPartIndex(int partIndex)
        {
            if (partIndex < 0 || partIndex >= nodeByPartIndex.Length) return null;
            return nodeByPartIndex[partIndex];
        }

        #endregion

        #region 4. AILMENT LIST DEDUPLICATION HELPERS

        /// <summary>Clears the ailments collection.</summary>
        public void ClearAilments()
        {
            Ailments.Clear();
        }

        /// <summary>
        /// Adds a range of unique ailment labels from the specified source collection to the current ailments list.
        /// </summary>
        /// <param name="source">The source collection of unique ailment labels to add.</param>
        public void AddAilmentsRangeUnique(UniqueList<string> source)
        {
            if (source == null) return;
            Ailments.AddRange(source);
        }

        /// <summary>
        /// Adds a single ailment label to the current ailments list if it is not already present.
        /// </summary>
        /// <param name="ailment">The ailment label to add.</param>
        public void AddAilmentUnique(string ailment)
        {
            Ailments.Add(ailment);
        }

        #endregion
    }
}