using System;
using System.Collections.Concurrent;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Centralized, thread-safe, domain-agnostic logging engine for OverHaulers.
    /// Callers identify themselves with a plain call-site string (convention: "Domain.Site", e.g. "Solver.WorkspacePool"),
    /// which doubles as the throttle key. Hardcoded error logs are throttled (max 3 prints per unique source),
    /// while informative events are routed through localized XML translation keys into the active
    /// <see cref="ActivityJournal"/> session when one is open, or straight to the console otherwise.
    /// Resides under Source/Core/Logging/ and knows nothing about the layers that call it.
    /// </summary>
    public static partial class OHLog
    {
        #region 1. DYNAMIC ATOMIC THROTTLING ENGINE

        public const int MaxWarnCutoff = 3;
        private static readonly ConcurrentDictionary<string, int> warnCounts = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, int> errorCounts = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// Emits a hardcoded, unlocalized warning with full exception details,
        /// throttled to a maximum of 3 prints per unique <paramref name="source"/> to prevent log spam.
        /// </summary>
        /// <param name="source">The call site, formatted as "Domain.Site". Also the throttle key.</param>
        /// <param name="ex">An optional exception to print in full.</param>
        /// <param name="message">An optional human-readable description.</param>
        public static void Warn(string source, Exception ex = null, string message = null) =>
            EmitThrottled(warnCounts, false, source, ex, message);

        /// <summary>
        /// Emits a hardcoded, unlocalized critical error with full exception details,
        /// throttled to a maximum of 3 prints per unique <paramref name="source"/>.
        /// Errors keep their own budget, independent of warnings raised from the same source.
        /// </summary>
        /// <param name="source">The call site, formatted as "Domain.Site". Also the throttle key.</param>
        /// <param name="ex">An optional exception to print in full.</param>
        /// <param name="message">An optional human-readable description.</param>
        public static void Error(string source, Exception ex = null, string message = null) =>
            EmitThrottled(errorCounts, true, source, ex, message);

        /// <summary>
        /// Canonical throttled emitter shared by <see cref="Warn"/> and <see cref="Error"/>.
        /// </summary>
        private static void EmitThrottled(ConcurrentDictionary<string, int> counts, bool isError, string source, Exception ex, string message)
        {
            int count = counts.AddOrUpdate(source, 1, (_, cur) => cur + 1);
            if (count > MaxWarnCutoff) return;

            string msgPart = !string.IsNullOrEmpty(message) ? $" {message}" : "";
            string exPart = ex != null ? $": {ex}" : "";

            if (isError)
            {
                Log.Error($"[Over Haulers] Error #{count}/{MaxWarnCutoff} [{source}]{msgPart}{exPart}");
            }
            else
            {
                Log.Warning($"[Over Haulers] Warning #{count}/{MaxWarnCutoff} [{source}]{msgPart}{exPart}");
            }
        }

        #endregion

        #region 2. LOCALIZED EVENT ROUTING

        /// <summary>
        /// Emits a localized informative event. While an <see cref="ActivityJournal"/> session is open the journal text is
        /// recorded as a bullet; otherwise the console text is logged. Both keys receive the same positional arguments.
        /// </summary>
        /// <param name="journalKey">Keyed translation used for the journal bullet.</param>
        /// <param name="logKey">Keyed translation used for the console message.</param>
        /// <param name="args">Positional arguments shared by both translations.</param>
        public static void Event(string journalKey, string logKey, params NamedArgument[] args)
        {
            if (ActivityJournal.IsActive)
            {
                ActivityJournal.RecordBullet(journalKey.Translate(args).ToString());
            }
            else
            {
                Log.Message(logKey.Translate(args).ToString());
            }
        }

        /// <summary>
        /// Emits a localized informative console message with no journal counterpart.
        /// </summary>
        /// <param name="logKey">Keyed translation used for the console message.</param>
        /// <param name="args">Positional arguments for the translation.</param>
        public static void Info(string logKey, params NamedArgument[] args)
        {
            Log.Message(logKey.Translate(args).ToString());
        }

        /// <summary>
        /// Emits a localized informative console message only when no <see cref="ActivityJournal"/> session is open,
        /// preventing duplicate output while the journal is already summarising the same phase.
        /// </summary>
        /// <param name="logKey">Keyed translation used for the console message.</param>
        /// <param name="args">Positional arguments for the translation.</param>
        public static void InfoUnlessJournaled(string logKey, params NamedArgument[] args)
        {
            if (ActivityJournal.IsActive) return;

            Log.Message(logKey.Translate(args).ToString());
        }

        #endregion
    }
}
