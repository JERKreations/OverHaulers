using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-04] UNIFIED SINGLE-LABEL STAT DRAWER WITH SYNCHRONOUS ICON STAMPING
    /// Renders stat explanations using a single native Widgets.Label call to preserve
    /// exact vanilla height, font descender clearance, and hyperlink positioning.
    /// Utilizes a zero-allocation DOD LRU layout cache to prevent OnGUI string manipulation GC spikes.
    /// </summary>
    public static class InfoCardOverlay
    {
        #region 1. CONSTANTS & CACHE DATA STRUCTURES

        /// <summary>The sentinel tag used to identify OverHaulers stat report labels within the game's UI.</summary>
        public const string TagSentinel = "[OVERHAULERS_STATDEFCARD]";

        /// <summary>The tags used to identify prosthetic, athletic, and injury icons within the stat report labels.</summary>
        public const string TagProsthetic = "[P]";
        public const string TagAthletic = "[A]";
        public const string TagInjury = "[I]";

        /// <summary>
        /// The key used to cache overlay layout data based on the full text, rectangle width, and scale percent.
        /// </summary>
        private struct OverlayCacheKey : IEquatable<OverlayCacheKey>
        {
            public readonly string FullText;
            public readonly float RectWidth;
            public readonly float ScalePercent;

            public OverlayCacheKey(string text, float width, float scale)
            {
                FullText = text;
                RectWidth = width;
                ScalePercent = scale;
            }

            public bool Equals(OverlayCacheKey other) => 
                RectWidth == other.RectWidth && 
                ScalePercent == other.ScalePercent && 
                FullText == other.FullText;

            public override bool Equals(object obj) => obj is OverlayCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (FullText != null ? FullText.GetHashCode() : 0);
                    hash = hash * 31 + RectWidth.GetHashCode();
                    hash = hash * 31 + ScalePercent.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// Represents a cached command for rendering an icon within the overlay, including its type, position, and size.
        /// </summary>
        private struct CachedIconCommand
        {
            public char IconType; // 'P', 'A', or 'I'
            public float OffsetX;
            public float OffsetY;
            public float Size;
        }

        /// <summary>
        /// Represents the cached overlay data, including the printable text and the list of icon commands.
        /// </summary>
        private class CachedOverlayData
        {
            public string PrintableText;
            public readonly List<CachedIconCommand> Icons = new List<CachedIconCommand>(8);
        }

        /// <summary>The cache storing overlay layout data keyed by the overlay cache key.</summary>
        private static readonly Dictionary<OverlayCacheKey, CachedOverlayData> layoutCache = 
            new Dictionary<OverlayCacheKey, CachedOverlayData>(64);

        /// <summary>The pooled line buffer used to minimize memory allocations when processing overlay text.</summary>
        private static readonly List<string> pooledLineBuffer = new List<string>(64);

        #endregion

        #region 2. HARMONY PREFIX ON WIDGETS.LABEL

        /// <summary>
        /// [VIEW-04] Harmony Prefix intercepting Widgets.Label to redirect OverHaulers stat reports containing TagSentinel string to the unified
        ///  label drawer.
        /// This allows OverHaulers to maintain a consistent and visually unified presentation of stat explanations within the game's UI.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Prefix(Rect rect, string label)
        {
            // Fast length filter (TagSentinel.Length is 25)
            if (label == null || label.Length < TagSentinel.Length)
            {
                return true;
            }

            // Fast-path: Index 0 match (Single char check: label[0] == '[')
            if (label[0] == '[' && label.StartsWith(TagSentinel, StringComparison.Ordinal))
            {
                DrawUnifiedExplanationLabel(rect, label);
                return false;
            }

            // Compatibility fallback: Third-party StatWorkers (e.g. VEF) where external text precedes our StatPart
            if (label.IndexOf(TagSentinel, StringComparison.Ordinal) >= 0)
            {
                DrawUnifiedExplanationLabel(rect, label);
                return false;
            }

            return true;
        }

        #endregion

        #region 3. UNIFIED ZERO-ALLOCATION RENDER ENGINE

        /// <summary>
        /// [VIEW-04] Draws unified stat explanation labels and stamps bionic/athletic/injury icons over space gaps.
        /// Queries a pre-calculated layout cache to guarantee 0 GC allocations per frame.
        /// </summary>
        public static void DrawUnifiedExplanationLabel(Rect rect, string fullText)
        {
            GameFont originalFont = Text.Font;
            Color originalColor = GUI.color;
            Text.Font = GameFont.Small;

            try
            {
                float scalePercent = OverHaulers.settings?.iconScalePercent ?? SettingsDefaults.IconScalePercent;
                OverlayCacheKey cacheKey = new OverlayCacheKey(fullText, rect.width, scalePercent);

                // 1. CACHE LOOKUP (O(1) zero-allocation fetch)
                if (!layoutCache.TryGetValue(cacheKey, out CachedOverlayData cachedData))
                {
                    // Evict cache to prevent memory creep over extended play sessions
                    if (layoutCache.Count > 128) layoutCache.Clear();

                    cachedData = BuildLayoutCache(fullText, rect.width, scalePercent);
                    layoutCache[cacheKey] = cachedData;
                }

                // 2. RENDER BASE TEXT
                Widgets.Label(rect, cachedData.PrintableText);

                // 3. RENDER ICONS DIRECTLY FROM PRE-CALCULATED COORDINATES
                GUI.color = Color.white; // Explicitly restore white to bypass IMGUI alpha culling

                for (int i = 0; i < cachedData.Icons.Count; i++)
                {
                    CachedIconCommand cmd = cachedData.Icons[i];
                    Texture2D tex = null;

                    if (cmd.IconType == 'P') tex = Textures.ProstheticIcon;
                    else if (cmd.IconType == 'A') tex = Textures.AthleticIcon;
                    else if (cmd.IconType == 'I') tex = Textures.BandageIcon;

                    if (tex != null)
                    {
                        float iconY = Mathf.Round(rect.y + cmd.OffsetY);
                        Rect iconRect = new Rect(rect.x + cmd.OffsetX, iconY, cmd.Size, cmd.Size);
                        GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
                    }
                }
            }
            catch (Exception ex)
            {
                OHLog.Presentation.WarnException("InfoCardOverlay", ex);
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = originalFont;
            }
        }

        #endregion

        #region 4. CACHE BUILDER (Executes ONLY on Cache Miss)

        /// <summary>
        /// Builds the printable string and pre-calculates the exact pixel coordinates for all icons.
        /// Preserves the fragile Unity word-wrapping pitch and font descender clearance mathematically.
        /// </summary>
        private static CachedOverlayData BuildLayoutCache(string fullText, float rectWidth, float scalePercent)
        {
            CachedOverlayData data = new CachedOverlayData();
            string tagReplacement = GetTagReplacement(scalePercent);

            string cleanText = fullText.Replace(TagSentinel, "");
            data.PrintableText = cleanText
                .Replace(TagProsthetic, tagReplacement)
                .Replace(TagAthletic, tagReplacement)
                .Replace(TagInjury, tagReplacement);

            // IMGUI TYPOGRAPHY RATIONALE (TRUE FONT PITCH VS LINEHEIGHT):
            // Text.LineHeight returns typographical font height without word-wrapping spacing.
            // By measuring the difference between a 2-line string ("A\nB") and a 1-line string ("A"),
            // we calculate the engine's true vertical line pitch.
            // This guarantees that stamped icons ([P], [A], [I]) remain mathematically locked to
            // their text baselines, even when descriptions wrap across 3 or 4 rows.
            float singleLineHeight = Text.CalcHeight("A", rectWidth);
            float doubleLineHeight = Text.CalcHeight("A\nB", rectWidth);
            float exactFontPitch = doubleLineHeight - singleLineHeight;

            float iconSize = Mathf.Round(exactFontPitch * scalePercent);
            float centeredOffset = (exactFontPitch - iconSize) / 2f;
            float opticalNudge = exactFontPitch * 0.10f;
            float iconOffsetYBase = centeredOffset + opticalNudge;

            PopulateLineBuffer(cleanText, pooledLineBuffer);
            float relativeY = 0f;

            for (int i = 0; i < pooledLineBuffer.Count; i++)
            {
                string line = pooledLineBuffer[i];

                if (line.Contains(TagProsthetic) || line.Contains(TagAthletic) || line.Contains(TagInjury))
                {
                    float targetY = relativeY + iconOffsetYBase;
                    if (line.Contains(TagProsthetic)) ExtractIconCommand('P', TagProsthetic, line, tagReplacement, iconSize, targetY, data.Icons);
                    if (line.Contains(TagAthletic)) ExtractIconCommand('A', TagAthletic, line, tagReplacement, iconSize, targetY, data.Icons);
                    if (line.Contains(TagInjury)) ExtractIconCommand('I', TagInjury, line, tagReplacement, iconSize, targetY, data.Icons);
                }

                // Calculate visual rows wrapped
                float measuredLineWidth = string.IsNullOrEmpty(line) ? 0f : Text.CalcSize(line).x;
                int lineVisualRows = (measuredLineWidth > rectWidth && rectWidth > 0f) 
                    ? Mathf.Max(1, Mathf.CeilToInt(measuredLineWidth / rectWidth)) 
                    : 1;

                relativeY += (lineVisualRows * exactFontPitch);
            }

            pooledLineBuffer.Clear();
            return data;
        }

        /// <summary>
        /// Extracts an icon command from a line of text if the specified tag is present, calculating its position and adding it to the list of commands.
        /// </summary>
        /// <param name="type">The type of the icon to extract.</param>
        /// <param name="tag">The tag to look for in the line of text.</param>
        /// <param name="line">The line of text to process.</param>
        /// <param name="tagReplacement">The string to replace the tag with.</param>
        /// <param name="size">The size of the icon.</param>
        /// <param name="relY">The relative Y position of the icon.</param>
        /// <param name="commands">The list of cached icon commands to add to.</param>
        private static void ExtractIconCommand(char type, string tag, string line, string tagReplacement, float size, float relY, List<CachedIconCommand> commands)
        {
            int idx = line.IndexOf(tag, StringComparison.Ordinal);
            if (idx >= 0)
            {
                string substringLeft = line.Substring(0, idx)
                    .Replace(TagProsthetic, tagReplacement)
                    .Replace(TagAthletic, tagReplacement)
                    .Replace(TagInjury, tagReplacement)
                    .StripTags();

                float offsetX = Text.CalcSize(substringLeft).x;
                commands.Add(new CachedIconCommand { IconType = type, OffsetX = offsetX, OffsetY = relY, Size = size });
            }
        }

        /// <summary>
        /// Gets the appropriate tag replacement string based on the given scale percent.
        /// </summary>
        /// <param name="scalePercent">The scale percent to determine the tag replacement for.</param>
        /// <returns>The appropriate tag replacement string based on the scale percent.</returns>
        private static string GetTagReplacement(float scalePercent)
        {
            if (scalePercent > 1.05f) return "     "; 
            if (scalePercent > 0.85f) return "    ";  
            return "   ";                             
        }

        /// <summary>
        /// Populates the provided list with lines of text extracted from the given string, splitting by newline characters.
        /// </summary>
        /// <param name="text">The input text to split into lines.</param>
        /// <param name="lines">The list to populate with the extracted lines of text.</param>
        private static void PopulateLineBuffer(string text, List<string> lines)
        {
            lines.Clear();
            int start = 0;
            int length = text.Length;

            while (start < length)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0)
                {
                    string lastLine = text.Substring(start).TrimEnd('\r');
                    lines.Add(lastLine);
                    break;
                }

                string line = text.Substring(start, end - start).TrimEnd('\r');
                lines.Add(line);
                start = end + 1;
            }
        }

        #endregion
    }
}