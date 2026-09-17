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
        #region 1. FIELDS & THREAD LOCAL INSTANCES

        /// Maximum recursion/nesting depth for thread-local workspaces (e.g. cross-pawn evaluations).
        private const int MaxWorkspacePoolDepth = 4;

        /// Thread-local pool array for isolated workspaces per thread.
        [ThreadStatic]
        private static AnatomicalWorkspace[] threadWorkspaces;

        /// Thread-local lease stack index.
        [ThreadStatic]
        private static int poolIndex;

        /// Synchronization root for cross-thread tracking and mass invalidation.
        private static readonly object syncRoot = new object();

        /// Registry tracking all instantiated workspaces via weak references for lifecycle sweeps.
        private static readonly List<System.WeakReference<AnatomicalWorkspace>> allWorkspaces = 
            new List<System.WeakReference<AnatomicalWorkspace>>();

        #endregion

        #region 2. THREAD-LOCAL WORKSPACE RECYCLER

        /// <summary>
        /// Retrieves or instantiates a clean thread-local <see cref="AnatomicalWorkspace"/> instance.
        /// Supports re-entrant leases (up to 4 levels) during nested cross-pawn evaluations.
        /// </summary>
        /// <returns>A cleared and ready-to-use <see cref="AnatomicalWorkspace"/>.</returns>
        public static AnatomicalWorkspace GetWorkspace()
        {
            if (threadWorkspaces == null)
            {
                threadWorkspaces = new AnatomicalWorkspace[MaxWorkspacePoolDepth];
            }

            int index = poolIndex;
            if (index < MaxWorkspacePoolDepth)
            {
                poolIndex++;
            }
            else
            {
                // Safety fallback if depth is exceeded: reuse topmost slot
                index = MaxWorkspacePoolDepth - 1;
            }

            AnatomicalWorkspace ws = threadWorkspaces[index];
            if (ws == null || ws.IsNullified)
            {
                int settingsCapacity = SettingsDefaults.DefaultWorkspaceCapacity; 
                int globalMaxLayout = TopologyLayoutCompiler.globalMaxPartCount;
                int initialCapacity = Math.Max(settingsCapacity, globalMaxLayout);

                ws = new AnatomicalWorkspace(initialCapacity);
                threadWorkspaces[index] = ws;

                lock (syncRoot)
                {
                    allWorkspaces.Add(new System.WeakReference<AnatomicalWorkspace>(ws));
                }
            }

            ws.Clear();
            return ws;
        }

        /// <summary>
        /// Releases and clears a previously acquired <see cref="AnatomicalWorkspace"/>,
        /// restoring the thread-local pool lease index.
        /// </summary>
        /// <param name="workspace">The workspace instance to release.</param>
        public static void ReleaseWorkspace(AnatomicalWorkspace workspace)
        {
            if (workspace == null) return;

            workspace.Clear();

            if (poolIndex > 0)
            {
                poolIndex--;
            }
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
        /// Flushes and nullifies all instantiated workspaces across all threads.
        /// Typically invoked on game initialization, def reloading, or save transitions.
        /// </summary>
        public static void ClearPools()
        {
            lock (syncRoot)
            {
                for (int i = allWorkspaces.Count - 1; i >= 0; i--)
                {
                    if (allWorkspaces[i].TryGetTarget(out AnatomicalWorkspace ws))
                    {
                        ws.ResetBuffersToBaseline();
                    }
                }
                allWorkspaces.Clear();
            }

            poolIndex = 0;
            threadWorkspaces = null;
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