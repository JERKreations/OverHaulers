using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-02] Thread-isolated, recyclable Structure of Arrays (SoA) container storing parallel
    /// contiguous memory streams for a pawn's active anatomical structure.
    /// Enables sequential cache-line prefetching and hardware-accelerated streaming math.
    /// Resides under Source/Core/Workspaces/.
    /// </summary>
    public class AnatomicalWorkspace
    {
        #region 1. CONTIGUOUS SoA PARALLEL ARRAYS

        // Parallel float streams for mathematical solver passes
        private float[] healthFractions;
        private float[] efficiencyRatings;
        private float[] athleticEfficiencies;

        private float[] calculatedProsthetics;
        private float[] calculatedAthletics;
        private float[] calculatedHealths;
        private float[] calculatedTotals;

        // Atomic byte flag stream
        private byte[] flags;

        // UI Presentation metadata (cold)
        private PartStateCold[] partStatesColdArray;

        // Intermediate math scratch buffer
        private float[] floatScratchBuffer;

        // Bound topology references (shared, read-only from template)
        private PartType[] partTypes;
        private float[] staticWeightFactors;
        private int[] parentIndices;
        private BodyPartRecord[] indexedParts;
        private int[] depthSortedIndices;
        private Dictionary<BodyPartRecord, int> partToIndexMap;

        private int partCount = 0;

        /// <summary>List of systemic ailments affecting the pawn, maintained as a unique collection.</summary>
        private readonly UniqueList<string> systemicAilments = new UniqueList<string>(16);

        /// <summary>Cached consciousness capacity level evaluated during ingress to prevent duplicate pawn hediff scans.</summary>
        public float CachedConsciousness { get; set; } = -1f;

        #endregion

        #region 2. PUBLIC SoA STREAM ACCESSORS

        /// <summary>Contiguous float stream of normalized part health fractions (0.0 to 1.0).</summary>
        public float[] HealthFractions => healthFractions;

        /// <summary>Contiguous float stream of prosthetic/biological efficiency ratings.</summary>
        public float[] EfficiencyRatings => efficiencyRatings;

        /// <summary>Contiguous float stream of athletic implant efficiency multipliers.</summary>
        public float[] AthleticEfficiencies => athleticEfficiencies;

        /// <summary>Contiguous float stream of solved prosthetic mass capacity offsets in kg.</summary>
        public float[] CalculatedProsthetics => calculatedProsthetics;

        /// <summary>Contiguous float stream of solved athletic mass capacity offsets in kg.</summary>
        public float[] CalculatedAthletics => calculatedAthletics;

        /// <summary>Contiguous float stream of solved health deficit mass capacity offsets in kg.</summary>
        public float[] CalculatedHealths => calculatedHealths;

        /// <summary>Contiguous float stream of final solved mass capacity offsets in kg.</summary>
        public float[] CalculatedTotals => calculatedTotals;

        /// <summary>Contiguous byte stream of atomic PartFlags bitmasks.</summary>
        public byte[] Flags => flags;

        /// <summary>Cold presentation metadata, index-aligned with SoA streams.</summary>
        public PartStateCold[] PartStatesColdArray => partStatesColdArray;

        /// <summary>Intermediate float scratch buffer for scratchpad math.</summary>
        public float[] FloatScratchBuffer => floatScratchBuffer;

        /// <summary>Anatomical classification per part index (borrowed from template).</summary>
        public PartType[] PartTypes => partTypes;

        /// <summary>Static weight factors per part index (borrowed from template).</summary>
        public float[] StaticWeightFactors => staticWeightFactors;

        /// <summary>Parent index per part index (borrowed from template).</summary>
        public int[] ParentIndices => parentIndices;

        /// <summary>Part indices pre-sorted parent-before-child (borrowed from template).</summary>
        public int[] DepthSortedIndices => depthSortedIndices;

        /// <summary>Number of active skeletal part slots currently bound.</summary>
        public int PartCount => partCount;

        /// <summary>Deduplicated collection of systemic ailment display labels.</summary>
        public UniqueList<string> SystemicAilments => systemicAilments;

        /// <summary>True if workspace internal arrays were released back for GC.</summary>
        public bool IsNullified { get; private set; } = false;

        #endregion

        #region 3. CONSTRUCTOR & INITIALIZATION

        /// <summary>
        /// Allocates a workspace with parallel contiguous SoA arrays sized to <paramref name="initialCapacity"/> slots.
        /// </summary>
        public AnatomicalWorkspace(int initialCapacity)
        {
            AllocateArrays(initialCapacity);
        }

        /// <summary>
        /// Allocates the internal arrays for the workspace based on the specified capacity.
        /// </summary>
        /// <param name="capacity">The number of part slots to allocate for the workspace.</param>
        private void AllocateArrays(int capacity)
        {
            healthFractions = new float[capacity];
            efficiencyRatings = new float[capacity];
            athleticEfficiencies = new float[capacity];

            calculatedProsthetics = new float[capacity];
            calculatedAthletics = new float[capacity];
            calculatedHealths = new float[capacity];
            calculatedTotals = new float[capacity];

            flags = new byte[capacity];
            partStatesColdArray = new PartStateCold[capacity];
            floatScratchBuffer = new float[capacity];
        }

        /// <summary>
        /// Binds this workspace's topology pointers to a shared precompiled species template.
        /// </summary>
        public void InitializeForPawn(SpeciesTopologyTemplate template)
        {
            this.partCount = template.PartCount;
            this.partTypes = template.PartTypes;
            this.staticWeightFactors = template.StaticWeightFactors;
            this.parentIndices = template.ParentIndices;
            this.indexedParts = template.IndexedParts;
            this.partToIndexMap = template.PartToIndex;
            this.depthSortedIndices = template.DepthSortedIndices;
        }

        #endregion

        #region 4. ATOMIC BITMASK & SLOT RESET HELPERS

        /// <summary>Evaluates if a specific bitmask flag is set at the slot index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasFlag(int index, byte flag) => (flags[index] & flag) != 0;

        /// <summary>Sets or clears a specific bitmask flag at the slot index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFlag(int index, byte flag, bool value)
        {
            if (value) flags[index] |= flag;
            else flags[index] &= (byte)~flag;
        }

        /// <summary>
        /// Resets all parallel SoA streams across active part slots with zero garbage allocation.
        /// </summary>
        public void ResetAllSlots(int count)
        {
            for (int i = 0; i < count; i++)
            {
                healthFractions[i] = 1.0f;
                efficiencyRatings[i] = 1.0f;
                athleticEfficiencies[i] = 1.0f;

                calculatedProsthetics[i] = 0f;
                calculatedAthletics[i] = 0f;
                calculatedHealths[i] = 0f;
                calculatedTotals[i] = 0f;

                flags[i] = PartFlags.None;
                partStatesColdArray[i].Reset();
            }

            int coldLength = partStatesColdArray.Length;
            for (int i = count; i < coldLength; i++)
            {
                partStatesColdArray[i].Reset();
            }

            // Reset systemic ailments collection
            CachedConsciousness = -1f;
        }

        #endregion

        #region 5. BUFFER EXPANSION & LIFECYCLE

        /// <summary>
        /// Doublifies parallel array capacities as needed to accommodate <paramref name="layoutSize"/> slots.
        /// </summary>
        public void EnsureArraySizes(int layoutSize)
        {
            if (healthFractions == null || healthFractions.Length < layoutSize)
            {
                int targetSize = healthFractions == null
                    ? Math.Max(SettingsDefaults.DefaultWorkspaceCapacity, TopologyLayoutCompiler.globalMaxPartCount)
                    : healthFractions.Length;
                
                // Double the target size until it meets or exceeds the required layout size.
                while (targetSize < layoutSize)
                {
                    targetSize *= 2;
                }

                if (healthFractions == null)
                {
                    AllocateArrays(targetSize);
                }
                else
                {
                    Array.Resize(ref healthFractions, targetSize);
                    Array.Resize(ref efficiencyRatings, targetSize);
                    Array.Resize(ref athleticEfficiencies, targetSize);

                    Array.Resize(ref calculatedProsthetics, targetSize);
                    Array.Resize(ref calculatedAthletics, targetSize);
                    Array.Resize(ref calculatedHealths, targetSize);
                    Array.Resize(ref calculatedTotals, targetSize);

                    Array.Resize(ref flags, targetSize);
                    Array.Resize(ref partStatesColdArray, targetSize);
                    Array.Resize(ref floatScratchBuffer, targetSize);
                }
            }
        }

        /// <summary>
        /// Resets all internal buffers to baseline state, clearing references and zeroing primitive arrays.
        /// This is used when a pawn is despawned or released to prevent memory leaks across saves. The workspace can be reused for another pawn
        ///  without reallocating arrays.
        /// </summary>
        public void ResetBuffersToBaseline()
        {
            // Clear all topological & pawn references to prevent memory leaks across saves
            Clear(); 
            
            // Do NOT nullify the primitive arrays (healthFractions, flags, etc.).
            // Just zero them out so they remain safely allocated in memory.
            if (healthFractions != null)
            {
                Array.Clear(healthFractions, 0, healthFractions.Length);
                Array.Clear(flags, 0, flags.Length);
                Array.Clear(calculatedTotals, 0, calculatedTotals.Length);
            }
        }

        /// <summary>Unbinds template references for reuse by another pawn without releasing arrays.</summary>
        public void Clear()
        {
            systemicAilments.Clear();
            CachedConsciousness = -1f;
            partCount = 0;
            partTypes = null;
            staticWeightFactors = null;
            parentIndices = null;
            indexedParts = null;
            partToIndexMap = null;
            depthSortedIndices = null;
        }

        #endregion

        #region 6. O(1) LOOKUP HELPERS

        /// <summary>Adds a systemic ailment label uniquely in O(1) time.</summary>
        public void AddSystemicAilmentUnique(string label)
        {
            systemicAilments.Add(label);
        }

        /// <summary>Resolves array slot index for a live BodyPartRecord in O(1) time.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetPartIndex(BodyPartRecord part)
        {
            if (part == null || partToIndexMap == null) return -1;
            return partToIndexMap.TryGetValue(part, out int index) ? index : -1;
        }

        /// <summary>Resolves the live BodyPartRecord bound to a slot index in O(1) time.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BodyPartRecord GetPartRecord(int index)
        {
            if (indexedParts == null || index < 0 || index >= partCount) return null;
            return indexedParts[index];
        }

        #endregion
    }
}