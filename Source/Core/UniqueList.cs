using System.Collections;
using System.Collections.Generic;

namespace OverHaulers
{
    /// <summary>
    /// Thread-isolated, zero-allocation container pairing a <see cref="List{T}"/> with a <see cref="HashSet{T}"/> 
    /// to guarantee O(1) unique element insertions while maintaining linear index-based iteration order.
    /// Reuses backing buffers across reset cycles to guarantee 0 GC allocations during calculation passes.
    /// Annotated with Canonical Semantic Tags [PASS-01] and [VIEW-03] for pipeline tracking.
    /// </summary>
    /// <typeparam name="T">Value or reference type being tracked uniquely.</typeparam>
    public class UniqueList<T> : IReadOnlyList<T>
    {
        #region 1. FIELDS & STORAGE

        private readonly List<T> items;
        private readonly HashSet<T> set;

        /// <summary>Gets the number of unique elements stored in the collection.</summary>
        public int Count => items.Count;

        /// <summary>Gets the element at the specified index.</summary>
        public T this[int index] => items[index];

        /// <summary>Direct access to the underlying backing list for allocation-free read-only loops.</summary>
        public List<T> Items => items;

        #endregion

        #region 2. CONSTRUCTORS

        /// <summary>
        /// Initializes a new instance of the <see cref="UniqueList{T}"/> class with the specified initial capacity.
        /// </summary>
        /// <param name="capacity">The initial capacity of the list and set.</param>
        public UniqueList(int capacity = 16)
        {
            items = new List<T>(capacity);
            set = new HashSet<T>(capacity);
        }

        #endregion

        #region 3. MUTATION & BUFFER MANAGEMENT

        /// <summary>
        /// Adds an item to the collection if it is not already present.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <returns>True if the item was added; false if it was already present or null.</returns>
        public bool Add(T item)
        {
            if (item == null) return false;

            // BREAKPOINT ANCHOR: $O(1)$ Set Insertion Check
            if (set.Add(item))
            {
                items.Add(item);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Adds a range of items from another <see cref="UniqueList{T}"/> to the collection.
        /// </summary>
        /// <param name="source">The source <see cref="UniqueList{T}"/> containing items to add.</param>
        public void AddRange(UniqueList<T> source)
        {
            if (source == null) return;
            int count = source.Count;
            for (int i = 0; i < count; i++)
            {
                Add(source.items[i]);
            }
        }

        /// <summary>Clears all items from the collection.</summary>
        public void Clear()
        {
            items.Clear();
            set.Clear();
        }

        #endregion

        #region 4. ZERO-ALLOCATION ENUMERATOR IMPLEMENTATIONS

        // NOTE ON "0 REFERENCES" IN IDE:
        // Although Roslyn / VS Code may flag these methods as having 0 explicit call sites,
        // this region MUST remain intact for two critical compiler-level reasons:
        //
        // 1. Interface Contract (Compile-Time):
        //    'IReadOnlyList<T>' inherits from 'IEnumerable<T>' and 'IEnumerable'. 
        //    Removing the explicit interface implementations below will immediately cause
        //    compiler error CS0535 ('UniqueList<T>' does not implement interface member...).
        //
        // 2. Zero-Allocation Pattern-Based 'foreach' (Runtime Performance):
        //    The public 'GetEnumerator()' returns 'List<T>.Enumerator', which is a value-type (struct).
        //    When iterating via 'foreach (var x in uniqueList)', the C# compiler uses duck typing /
        //    pattern matching to bind directly to this struct method, completely bypassing the heap
        //    and avoiding the 32-48 byte IEnumerator object allocation required by interface dispatch.

        /// <summary>
        /// Provides a zero-allocation struct enumerator for pattern-based <c>foreach</c> loops.
        /// </summary>
        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();

        /// <summary>Explicit interface implementation satisfying <see cref="IEnumerable{T}"/>.</summary>
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();

        /// <summary>Explicit fallback interface implementation satisfying <see cref="IEnumerable"/>.</summary>
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

        #endregion
    }
}