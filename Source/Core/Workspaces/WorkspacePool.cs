using System;
using System.Collections.Generic;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-02] THREAD-STATIC WORKSPACE POOL
    /// Provides allocation-free, thread-safe scratchpad recyclers for capacity calculations.
    /// Replaces complex pooling arrays with high-speed ThreadStatic buffers, guarded against memory leaks.
    /// </summary>
    public static class WorkspacePool
    {
        #region 1. FIELDS & STORAGE

        [ThreadStatic]
        private static AnatomicalWorkspace activeWorkspace;

        // Fully qualified System namespace to resolve RimWorld's legacy Verse collision
        private static readonly List<System.WeakReference<AnatomicalWorkspace>> allWorkspaces = 
            new List<System.WeakReference<AnatomicalWorkspace>>();

        private static readonly object syncRoot = new object();

        #endregion

        #region 2. THREAD-LOCAL WORKSPACE RECYCLER

        /// <summary>
        /// Retrieves and automatically clears the thread-local anatomical evaluation workspace.
        /// Presets the workspace array structures to accommodate the game's compiled maximum species layouts.
        /// </summary>
        /// <returns>The thread-isolated, cleared anatomical workspace instance.</returns>
        public static AnatomicalWorkspace GetWorkspace()
        {
            // BREAKPOINT ANCHOR: ThreadStatic Workspace Allocation Gate
            if (activeWorkspace == null || activeWorkspace.IsNullified)
            {
                int settingsCapacity = SettingsDefaults.DefaultWorkspaceCapacity; 
                int globalMaxLayout = TopologyLayoutCompiler.globalMaxPartCount;
                
                // Establish initial size directly from the pre-compiled global maximum of all species
                int initialCapacity = Math.Max(settingsCapacity, globalMaxLayout);
                activeWorkspace = new AnatomicalWorkspace(initialCapacity);
                
                // BREAKPOINT ANCHOR: Sync Lock for WeakReference Tracking
                lock (syncRoot)
                {
                    allWorkspaces.Add(new System.WeakReference<AnatomicalWorkspace>(activeWorkspace));
                }
            }

            // BREAKPOINT ANCHOR: Thread-Local Workspace Clearance
            activeWorkspace.Clear();
            return activeWorkspace;
        }

        #endregion

        #region 3. GARBAGE SWEEP & LIFECYCLE MANAGEMENT

        /// <summary>
        /// Scans the static reference collection and sweeps dead ThreadStatic pointers out of memory.
        /// Invoked periodically from the main-thread out-of-band registry cleanup loop to prevent memory leaks.
        /// </summary>
        public static void CleanDeadReferences()
        {
            // BREAKPOINT ANCHOR: Weak Reference Eviction Sweep Lock
            lock (syncRoot)
            {
                // Sweep backward to safely prune invalid target wrappers in-place
                for (int i = allWorkspaces.Count - 1; i >= 0; i--)
                {
                    if (!allWorkspaces[i].TryGetTarget(out _))
                    {
                        allWorkspaces.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// Clears all thread-local workspace references and resets the backing arrays to their baseline state.
        /// This is a main-thread operation and should be invoked during application shutdown or when a full reset is required.
        /// </summary>
        public static void ClearPools()
        {
            // On the main thread, simply unbind references
            activeWorkspace?.Clear();

            lock (syncRoot)
            {
                for (int i = allWorkspaces.Count - 1; i >= 0; i--)
                {
                    if (allWorkspaces[i].TryGetTarget(out var workspace))
                    {
                        // Clears references without destroying backing arrays
                        workspace.ResetBuffersToBaseline(); 
                    }
                    else
                    {
                        allWorkspaces.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// Traverses the reference collection to evaluate the exact number of active thread-local workspace allocations.
        /// </summary>
        /// <returns>The total count of active thread-local workspace instances.</returns>
        public static int GetAllocatedWorkspaceCount()
        {
            // BREAKPOINT ANCHOR: Active Workspace Count Query
            lock (syncRoot)
            {
                int count = 0;
                for (int i = 0; i < allWorkspaces.Count; i++)
                {
                    if (allWorkspaces[i].TryGetTarget(out _))
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        #endregion
    }
}