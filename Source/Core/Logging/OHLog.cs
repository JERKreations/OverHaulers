using System;
using System.Collections.Concurrent;
using System.Text;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-07] Centralized, thread-safe, domain-agnostic logging engine for OverHaulers.
    /// Callers identify themselves with a plain call-site string (convention: "Domain.Site", e.g. "Solver.WorkspacePool"),
    /// which doubles as the throttle key. Hardcoded error logs are throttled (max 3 prints per unique source),
    /// while informative events are routed through localized XML translation keys into the active
    /// <see cref="ActivityJournal"/> session when one is open, or straight to the console otherwise.
    /// Resides under Source/Core/Logging/ and knows nothing about the layers that call it.
    /// </summary>
    public static partial class OHLog
    {
        #region 1. [DIAG-07] DYNAMIC ATOMIC THROTTLING ENGINE

        /// <summary>Canonical prefix for Over Haulers console and report output.</summary>
        internal const string LogPrefix = "[Over Haulers]";

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
                Log.Error(WithLogPrefix($"Error #{count}/{MaxWarnCutoff} [{source}]{msgPart}{exPart}"));
            }
            else
            {
                Log.Warning(WithLogPrefix($"Warning #{count}/{MaxWarnCutoff} [{source}]{msgPart}{exPart}"));
            }
        }

        #endregion

        #region 2. LOCALIZED EVENT ROUTING

        /// <summary>
        /// Appends the standard Over Haulers report heading to a producer-owned buffer.
        /// </summary>
        /// <param name="reportBuilder">The buffer receiving the report heading.</param>
        /// <param name="title">The localized report title.</param>
        internal static void AppendReportHeader(StringBuilder reportBuilder, string title)
        {
            reportBuilder.Append(LogPrefix);
            reportBuilder.Append(" ==== ");
            reportBuilder.Append(title);
            reportBuilder.Append(" ====");
        }

        /// <summary>
        /// Dispatches a log message through the shared sink, adding the canonical prefix when it is absent.
        /// </summary>
        /// <param name="message">The complete message or report text to emit.</param>
        internal static void DispatchLogMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            Log.Message(WithLogPrefix(message));
        }

        private static string WithLogPrefix(string message)
        {
            if (message.StartsWith(LogPrefix, StringComparison.Ordinal)) return message;

            return string.Concat(LogPrefix, " ", message);
        }

        /// <summary>
        /// Emits a localized event using one translation for both journal and standalone output.
        /// </summary>
        /// <param name="key">Translation key used in either output context.</param>
        /// <param name="args">Positional translation arguments.</param>
        public static void Event(string key, params NamedArgument[] args)
        {
            if (ActivityJournal.IsActive)
            {
                ActivityJournal.RecordBullet(key.Translate(args).ToString());
            }
            else
            {
                DispatchLogMessage(key.Translate(args).ToString());
            }
        }

        /// <summary>
        /// Emits a localized informative console message with no journal counterpart.
        /// </summary>
        /// <param name="logKey">Keyed translation used for the console message.</param>
        /// <param name="args">Positional arguments for the translation.</param>
        public static void Info(string logKey, params NamedArgument[] args)
        {
            DispatchLogMessage(logKey.Translate(args).ToString());
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

            DispatchLogMessage(logKey.Translate(args).ToString());
        }

        #endregion
    }
}
