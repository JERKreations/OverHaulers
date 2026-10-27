using System.Collections.Generic;
using System.Text;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 1. [DIAG-JOURNAL] ACTIVITY JOURNAL STATE & BUFFERS

        /// <summary>
        /// [DIAG-JOURNAL] Modular action accumulator that aggregates correlated lifecycle, 
        /// compilation, and calibration events into unified, structured console reports.
        /// Supports multi-tick deferred execution windows (e.g. Tick 0 -> Tick 10 calibration sweeps).
        /// Resides under Source/Diagnostics/Logging/.
        /// </summary>
        public static class ActivityJournal
        {
            private static readonly List<string> pendingBullets = new List<string>(16);
            private static readonly HashSet<string> pendingOperations = new HashSet<string>();
            private static readonly StringBuilder journalBuilder = new StringBuilder(1024);
            private static readonly object journalLock = new object();

            private static string activeHeader = null;
            private static bool isSessionActive = false;
            private static bool isDispatchAwaitingOperations = false;

            /// <summary>Indicates whether an activity journal session is currently open and gathering events.</summary>
            public static bool IsActive
            {
                get
                {
                    lock (journalLock) return isSessionActive;
                }
            }

            #endregion

            #region 2. [DIAG-JOURNAL] LIFECYCLE API & BUFFERING

            /// <summary>
            /// Begins a new activity journal session with a formatted header.
            /// Discards any un-dispatched stale sessions cleanly.
            /// </summary>
            /// <param name="headerText">The formatted headline text.</param>
            public static void BeginSession(string headerText)
            {
                lock (journalLock)
                {
                    pendingBullets.Clear();
                    pendingOperations.Clear();
                    activeHeader = headerText;
                    isSessionActive = true;
                    isDispatchAwaitingOperations = false;
                }
            }

            /// <summary>
            /// Updates the headline text of an active journal session without clearing accumulated bullets.
            /// Used to stamp final stopwatch elapsed durations prior to dispatch.
            /// </summary>
            /// <param name="headerText">The updated headline text.</param>
            public static void SetHeader(string headerText)
            {
                lock (journalLock)
                {
                    if (isSessionActive)
                    {
                        activeHeader = headerText;
                    }
                }
            }

            /// <summary>
            /// Registers a pending asynchronous or deferred operation token (e.g. "Calibration").
            /// While any registered operations remain unresolved, calls to <see cref="Dispatch"/> will hold the report in memory.
            /// </summary>
            /// <param name="operationKey">Unique token identifier for the operation.</param>
            public static void RegisterDeferredOperation(string operationKey)
            {
                if (string.IsNullOrEmpty(operationKey)) return;

                lock (journalLock)
                {
                    if (isSessionActive)
                    {
                        pendingOperations.Add(operationKey);
                    }
                }
            }

            /// <summary>
            /// Completes a registered deferred operation token.
            /// If all registered operations have resolved and a dispatch was pending, triggers final report emission.
            /// </summary>
            /// <param name="operationKey">Unique token identifier for the operation.</param>
            public static void CompleteDeferredOperation(string operationKey)
            {
                if (string.IsNullOrEmpty(operationKey)) return;

                lock (journalLock)
                {
                    if (!isSessionActive) return;

                    pendingOperations.Remove(operationKey);

                    if (isDispatchAwaitingOperations && pendingOperations.Count == 0)
                    {
                        ExecuteDispatchInternal();
                    }
                }
            }

            /// <summary>
            /// Appends a bulleted item to the active journal session.
            /// If no session is active, falls back to an immediate single message.
            /// </summary>
            /// <param name="bulletText">The formatted bullet text.</param>
            public static void RecordBullet(string bulletText)
            {
                if (string.IsNullOrEmpty(bulletText)) return;

                lock (journalLock)
                {
                    if (isSessionActive)
                    {
                        pendingBullets.Add(bulletText);
                    }
                    else
                    {
                        Log.Message($"[Over Haulers] {bulletText}");
                    }
                }
            }

            /// <summary>
            /// Requests emission of the accumulated journal report.
            /// If deferred operations are currently pending, holds the report until all operations call <see cref="CompleteDeferredOperation"/>.
            /// </summary>
            public static void Dispatch()
            {
                lock (journalLock)
                {
                    if (!isSessionActive) return;

                    if (pendingOperations.Count > 0)
                    {
                        isDispatchAwaitingOperations = true;
                        return;
                    }

                    ExecuteDispatchInternal();
                }
            }

            /// <summary>
            /// Internal formatting and emission routine.
            /// </summary>
            private static void ExecuteDispatchInternal()
            {
                if (pendingBullets.Count == 0)
                {
                    DiscardInternal();
                    return;
                }

                journalBuilder.Clear();

                try
                {
                    journalBuilder.Append("[Over Haulers] ==== ");
                    journalBuilder.Append(activeHeader);
                    journalBuilder.Append(" ====");

                    for (int i = 0; i < pendingBullets.Count; i++)
                    {
                        journalBuilder.AppendLine();
                        journalBuilder.Append("  • ");
                        journalBuilder.Append(pendingBullets[i]);
                    }

                    Log.Message(journalBuilder.ToString());
                }
                finally
                {
                    DiscardInternal();
                }
            }

            /// <summary>
            /// Discards the active journal session and clears all buffers without logging.
            /// Invoked on map unload, session cancellation, or error boundaries.
            /// </summary>
            public static void Discard()
            {
                lock (journalLock)
                {
                    DiscardInternal();
                }
            }

            private static void DiscardInternal()
            {
                journalBuilder.Clear();
                pendingBullets.Clear();
                pendingOperations.Clear();
                activeHeader = null;
                isSessionActive = false;
                isDispatchAwaitingOperations = false;
            }

            #endregion
        }
    }
}