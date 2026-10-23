using System;
using System.Collections.Generic;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-02] THREAD-STATIC WORKSPACE POOL
    /// Provides allocation-free, thread-safe scratchpad recyclers for capacity calculations.
    /// Replaces complex pooling arrays with high-speed ThreadStatic buffers, guarded against memory leaks.
    /// Resides under Source/Core/Workspaces/.
    /// </summary>
    public static class WorkspacePool
    {
        #region 1. CONSTANTS, ENUMS & STRUCTS

        /// <summary>Maximum recursion/nesting depth for thread-local workspaces (e.g. cross-pawn evaluations).</summary>
        public const int MaxWorkspacePoolDepth = 4;

        /// <summary>
        /// Threshold count of registered workspace references before triggering an automatic dead reference sweep.
        /// Prevents weak reference accumulation from transient worker threads without per-tick overhead.
        /// </summary>
        public const int DynamicCleanupWatermark = 16;

        /// <summary>
        /// Lightweight IDisposable lease wrapper for <see cref="AnatomicalWorkspace"/>
        /// to guarantee deterministic release via C# using blocks without boxing.
        /// </summary>
        public struct WorkspaceLease : IDisposable
        {
            public readonly AnatomicalWorkspace Workspace;
            private bool isDisposed;

            public WorkspaceLease(AnatomicalWorkspace workspace)
            {
                Workspace = workspace;
                isDisposed = false;
            }

            public void Dispose()
            {
                if (!isDisposed)
                {
                    isDisposed = true;
                    if (Workspace != null)
                    {
                        ReleaseWorkspace(Workspace);
                    }
                }
            }
        }

        #endregion

        #region 2. STATIC STATE, THREAD-LOCAL POOLS & LOCKS

        /// <summary>Thread-local pool array for isolated workspaces per thread.</summary>
        [ThreadStatic]
        private static AnatomicalWorkspace[] threadWorkspaces;

        /// <summary>Thread-local lease stack index.</summary>
        [ThreadStatic]
        private static int poolIndex;

        /// <summary>Synchronization root for cross-thread tracking and mass invalidation.</summary>
        private static readonly object syncRoot = new object();

        /// <summary>Registry tracking all instantiated workspaces via weak references for lifecycle sweeps.</summary>
        private static readonly List<System.WeakReference<AnatomicalWorkspace>> allWorkspaces = 
            new List<System.WeakReference<AnatomicalWorkspace>>();

        #endregion

        #region 3. PUBLIC CANONICAL LEASE API

        /// <summary>
        /// Leases a workspace wrapped in a deterministic <see cref="WorkspaceLease"/> token.
        /// Single public canonical entry point enforcing RAII scope discipline.
        /// </summary>
        /// <returns>A disposable lease token containing the leased workspace.</returns>
        public static WorkspaceLease LeaseWorkspace()
        {
            return new WorkspaceLease(GetWorkspaceInternal());
        }

        #endregion

        #region 4. INTERNAL LEASE & RELEASE ENGINE

        /// <summary>
        /// Retrieves or instantiates a clean thread-local <see cref="AnatomicalWorkspace"/> instance.
        /// Supports re-entrant leases up to <see cref="MaxWorkspacePoolDepth"/> levels.
        /// If depth is exceeded, allocates an unpooled standalone instance without corrupting poolIndex.
        /// </summary>
        /// <returns>A cleared and ready-to-use <see cref="AnatomicalWorkspace"/>.</returns>
        internal static AnatomicalWorkspace GetWorkspaceInternal()
        {
            if (threadWorkspaces == null)
            {
                threadWorkspaces = new AnatomicalWorkspace[MaxWorkspacePoolDepth];
            }

            if (poolIndex < MaxWorkspacePoolDepth)
            {
                int index = poolIndex++;
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
                        if (allWorkspaces.Count > DynamicCleanupWatermark)
                        {
                            SweepDeadReferencesLocked();
                        }
                    }
                }

                ws.Clear();
                ws.ActiveLeaseSlot = index;
                ws.IsLeased = true;
                return ws;
            }

            // Safety fallback if depth is exceeded: allocate an unpooled fallback workspace instance without incrementing poolIndex
            // so the pooled stack pointer remains uncorrupted.
            OHLog.Solver.Warn("WorkspacePool", null, 
                $"Workspace pool depth exceeded ({poolIndex + 1} > {MaxWorkspacePoolDepth}). Allocated unpooled fallback workspace.");

            int fallbackCapacity = Math.Max(SettingsDefaults.DefaultWorkspaceCapacity, TopologyLayoutCompiler.globalMaxPartCount);
            AnatomicalWorkspace unpooledWs = new AnatomicalWorkspace(fallbackCapacity);
            unpooledWs.ActiveLeaseSlot = -1;
            unpooledWs.IsLeased = true;

            lock (syncRoot)
            {
                allWorkspaces.Add(new System.WeakReference<AnatomicalWorkspace>(unpooledWs));
                if (allWorkspaces.Count > DynamicCleanupWatermark)
                {
                    SweepDeadReferencesLocked();
                }
            }

            return unpooledWs;
        }


        /// <summary>
        /// Releases and clears a previously acquired <see cref="AnatomicalWorkspace"/>,
        /// validating LIFO stack integrity and restoring the thread-local pool lease index.
        /// Completely idempotent: ignores unleased or already released instances.
        /// </summary>
        /// <param name="workspace">The workspace instance to release.</param>
        internal static void ReleaseWorkspace(AnatomicalWorkspace workspace)
        {
            if (workspace == null || !workspace.IsLeased) return;

            int slot = workspace.ActiveLeaseSlot;
            workspace.Clear(); // Sets IsLeased = false and ActiveLeaseSlot = -1

            // Unpooled fallback workspaces do not occupy pool slots; return without modifying poolIndex
            if (slot < 0) return;

            // Pooled workspace release: enforce LIFO stack invariant
            if (poolIndex > 0)
            {
                if (poolIndex - 1 != slot)
                {
                    OHLog.Solver.Warn("WorkspacePool", null, 
                        $"Out-of-order workspace release detected (expected slot {poolIndex - 1}, released {slot}). Repairing stack pointer.");
                    poolIndex = slot;
                }
                else
                {
                    poolIndex--;
                }
            }
        }

        #endregion

        #region 5. GARBAGE SWEEP & LIFECYCLE MANAGEMENT

        /// <summary>
        /// Internal sweep executing strictly within an existing syncRoot lock.
        /// Prunes collected ThreadStatic workspace references in-place from back to front.
        /// </summary>
        private static void SweepDeadReferencesLocked()
        {
            for (int i = allWorkspaces.Count - 1; i >= 0; i--)
            {
                if (!allWorkspaces[i].TryGetTarget(out _))
                {
                    allWorkspaces.RemoveAt(i);
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