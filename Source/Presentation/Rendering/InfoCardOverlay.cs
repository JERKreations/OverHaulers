using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-04] UNIFIED SINGLE-LABEL STAT DRAWER WITH SYNCHRONOUS ICON STAMPING
    /// Renders stat explanations using a single native Widgets.Label call to preserve
    /// exact vanilla height, font descender clearance, and hyperlink positioning.
    /// Utilizes a true zero-allocation doubly-linked LRU layout cache with node recycling
    /// to eliminate OnGUI string manipulation and IMGUI layout GC spikes.
    /// Resides under Source/Presentation/Rendering/.
    /// </summary>
    public static class InfoCardOverlay
    {
        #region 1. CONSTANTS, CONTEXT GATE & MODELS

        /// <summary>The sentinel tag used to identify OverHaulers stat report labels within the game's UI.</summary>
        public const string TagSentinel = "[OVERHAULERS_STATDEFCARD]";

        /// <summary>The tags used to identify prosthetic, athletic, and injury icons within the stat report labels.</summary>
        public const string TagProsthetic = "[P]";
        public const string TagAthletic = "[A]";
        public const string TagInjury = "[I]";

        /// <summary>Maximum layout cache entries retained before steady-state LRU recycling begins.</summary>
        private const int MaxCacheCapacity = 128;

        [ThreadStatic]
        private static int infoCardDepth;

        /// <summary>
        /// High-speed execution context gate.
        /// True only while an in-game InfoCard (StatsReportUtility) is actively drawing.
        /// </summary>
        public static bool IsRenderingInfoCard => infoCardDepth > 0;

        /// <summary>
        /// The key used to cache overlay layout data based on the full text, rectangle width, scale percent, and active UI scale.
        /// </summary>
        private struct OverlayCacheKey : IEquatable<OverlayCacheKey>
        {
            public readonly string FullText;
            public readonly float RectWidth;
            public readonly float ScalePercent;
            public readonly float UiScale;

            /// <summary>
            /// Initializes a new overlay cache key with the specified full text, rectangle width, scale percent, and UI scale.
            /// </summary>
            /// <param name="text">The full text of the overlay.</param>
            /// <param name="width">The width of the rectangle.</param>
            /// <param name="scale">The scale percent.</param>
            /// <param name="uiScale">The active UI scale.</param>
            public OverlayCacheKey(string text, float width, float scale, float uiScale)
            {
                FullText = text;
                RectWidth = width;
                ScalePercent = scale;
                UiScale = uiScale;
            }

            /// <summary>Returns true if this overlay cache key is equal to another.</summary>
            public bool Equals(OverlayCacheKey other) =>
                RectWidth == other.RectWidth &&
                ScalePercent == other.ScalePercent &&
                UiScale == other.UiScale &&
                FullText == other.FullText;

            /// <summary>Returns true if this overlay cache key is equal to another object.</summary>
            public override bool Equals(object obj) => obj is OverlayCacheKey other && Equals(other);

            /// <summary>
            /// Returns a hash code for the overlay cache key, combining the full text, rectangle width, scale percent, and UI scale.
            /// </summary>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (FullText != null ? FullText.GetHashCode() : 0);
                    hash = hash * 31 + RectWidth.GetHashCode();
                    hash = hash * 31 + ScalePercent.GetHashCode();
                    hash = hash * 31 + UiScale.GetHashCode();
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
        /// [VIEW-04] Intrusive doubly-linked LRU cache node holding pre-calculated layout data and recycled icon command buffers.
        /// </summary>
        private sealed class LruCacheNode
        {
            public OverlayCacheKey Key;
            public string PrintableText;
            public readonly List<CachedIconCommand> Icons = new List<CachedIconCommand>(8);
            public LruCacheNode Prev;
            public LruCacheNode Next;
        }

        #endregion

        #region 2. LRU CACHE ENGINE & NODE POOL

        /// <summary>Maximum number of cache nodes to retain in the LRU cache and node pool.</summary>
        private static readonly Dictionary<OverlayCacheKey, LruCacheNode> lruLookup =
            new Dictionary<OverlayCacheKey, LruCacheNode>(MaxCacheCapacity);

        /// <summary>Pool of reusable LRU cache nodes to minimize allocations.</summary>
        private static readonly Stack<LruCacheNode> nodePool =
            new Stack<LruCacheNode>(MaxCacheCapacity);

        /// <summary>Buffer for pooling lines of text to minimize allocations during layout calculations.</summary>
        private static readonly List<string> pooledLineBuffer =
            new List<string>(64);

        /// <summary>Head of the LRU linked list, representing the most recently used cache node.</summary>
        private static LruCacheNode lruHead;

        /// <summary>Tail of the LRU linked list, representing the least recently used cache node.</summary>
        private static LruCacheNode lruTail;

        /// <summary>Pushes an active InfoCard rendering context.</summary>
        public static void PushInfoCardContext() => infoCardDepth++;

        /// <summary>Pops an active InfoCard rendering context.</summary>
        public static void PopInfoCardContext()
        {
            if (infoCardDepth > 0) infoCardDepth--;
        }

        /// <summary>
        /// [VIEW-04] Flushes the IMGUI overlay typography layout cache and recycles active nodes into the node pool.
        /// Invoked when UI scale changes, caches are flushed, or game transitions occur.
        /// </summary>
        public static void ClearCache()
        {
            LruCacheNode curr = lruHead;
            while (curr != null)
            {
                LruCacheNode next = curr.Next;
                curr.Prev = null;
                curr.Next = null;
                curr.PrintableText = null;
                curr.Icons.Clear();
                if (nodePool.Count < MaxCacheCapacity)
                {
                    nodePool.Push(curr);
                }
                curr = next;
            }

            lruLookup.Clear();
            lruHead = null;
            lruTail = null;
        }

        /// <summary>
        /// Obtains or constructs layout data via O(1) LRU promotion and recycled node eviction.
        /// Guarantees zero node/list allocations in steady-state rendering.
        /// </summary>
        /// <param name="key">The cache key identifying the layout node.</param>
        /// <param name="fullText">The full text containing potential icon tags.</param>
        /// <param name="rectWidth">The width of the rectangle for text layout calculations.</param>
        /// <param name="scalePercent">The scale percent to apply to icon sizes and offsets.</param>
        /// <returns>The layout node corresponding to the specified cache key and layout parameters.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LruCacheNode GetOrCreateLayoutNode(in OverlayCacheKey key, string fullText, float rectWidth, float scalePercent)
        {
            // BREAKPOINT ANCHOR: LRU Cache Hit & Head Promotion
            if (lruLookup.TryGetValue(key, out LruCacheNode existingNode))
            {
                PromoteToHead(existingNode);
                return existingNode;
            }

            // BREAKPOINT ANCHOR: Steady-State Node Eviction / Recycling
            LruCacheNode targetNode;
            if (lruLookup.Count >= MaxCacheCapacity && lruTail != null)
            {
                targetNode = lruTail;
                DetachNode(targetNode);
                lruLookup.Remove(targetNode.Key);
                targetNode.Icons.Clear();
            }
            else if (nodePool.Count > 0)
            {
                targetNode = nodePool.Pop();
                targetNode.Icons.Clear();
            }
            else
            {
                targetNode = new LruCacheNode();
            }

            targetNode.Key = key;
            PopulateLayoutData(targetNode, fullText, rectWidth, scalePercent);

            AttachAtHead(targetNode);
            lruLookup[key] = targetNode;
            return targetNode;
        }

        /// <summary>
        /// Promotes the specified cache node to the head of the LRU linked list, indicating recent usage.
        /// </summary>
        /// <param name="node">The cache node to promote to the head of the LRU linked list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PromoteToHead(LruCacheNode node)
        {
            if (node == lruHead) return;
            DetachNode(node);
            AttachAtHead(node);
        }

        /// <summary>
        /// Detaches the specified cache node from the LRU linked list.
        /// </summary>
        /// <param name="node">The cache node to detach from the LRU linked list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DetachNode(LruCacheNode node)
        {
            if (node.Prev != null) node.Prev.Next = node.Next;
            else lruHead = node.Next;

            if (node.Next != null) node.Next.Prev = node.Prev;
            else lruTail = node.Prev;

            node.Prev = null;
            node.Next = null;
        }

        /// <summary>
        /// Populates the layout data for the specified cache node based on the provided text and layout parameters.
        /// </summary>
        /// <param name="node">The cache node to populate with layout data.</param>
        /// <param name="fullText">The full text containing potential icon tags.</param>
        /// <param name="rectWidth">The width of the rectangle for text layout calculations.</param>
        /// <param name="scalePercent">The scale percent to apply to icon sizes and offsets.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AttachAtHead(LruCacheNode node)
        {
            node.Prev = null;
            node.Next = lruHead;

            if (lruHead != null) lruHead.Prev = node;
            lruHead = node;

            if (lruTail == null) lruTail = node;
        }

        #endregion

        #region 3. HARMONY PREFIX ON WIDGETS.LABEL

        /// <summary>
        /// [VIEW-04] Scoped Harmony Prefix intercepting Widgets.Label.
        /// Bails out in 1 CPU cycle during all normal gameplay.
        /// Activates only when an in-game InfoCard stat report is actively drawing.
        /// </summary>
        /// <param name="rect">The rectangle within which the label is to be drawn.</param>
        /// <param name="label">The label text to be rendered.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Prefix(Rect rect, string label)
        {
            // EXECUTION CONTEXT GATE: Instant bailout during normal gameplay
            if (!IsRenderingInfoCard)
            {
                return true;
            }

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

        #region 4. UNIFIED ZERO-ALLOCATION RENDER ENGINE

        /// <summary>
        /// [VIEW-04] Draws unified stat explanation labels and stamps bionic/athletic/injury icons over space gaps.
        /// Queries an intrusive recycled LRU cache to guarantee 0 GC allocations per frame in steady state.
        /// </summary>
        /// <param name="rect">The rectangle within which to draw the label and icons.</param>
        /// <param name="fullText">The full text to render, potentially containing icon tags.</param>
        public static void DrawUnifiedExplanationLabel(Rect rect, string fullText)
        {
            GameFont originalFont = Text.Font;
            Color originalColor = GUI.color;
            Text.Font = GameFont.Small;

            try
            {
                float scalePercent = OverHaulers.settings?.iconScalePercent ?? SettingsDefaults.IconScalePercent;
                float currentUiScale = Prefs.UIScale;
                OverlayCacheKey cacheKey = new OverlayCacheKey(fullText, rect.width, scalePercent, currentUiScale);

                // 1. O(1) LRU FETCH WITH STEADY-STATE NODE RECYCLING
                LruCacheNode cachedNode = GetOrCreateLayoutNode(cacheKey, fullText, rect.width, scalePercent);

                // 2. RENDER BASE TEXT
                Widgets.Label(rect, cachedNode.PrintableText);

                // 3. RENDER ICONS DIRECTLY FROM PRE-CALCULATED COORDINATES
                GUI.color = Color.white; // Explicitly restore white to bypass IMGUI alpha culling

                List<CachedIconCommand> icons = cachedNode.Icons;
                for (int i = 0; i < icons.Count; i++)
                {
                    CachedIconCommand cmd = icons[i];
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
                OHLog.Presentation.Warn("InfoCardOverlay", ex, "Failed to render info card overlay.");
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = originalFont;
            }
        }

        #endregion

        #region 5. CACHE BUILDER & TYPOGRAPHY PRIMITIVES

        /// <summary>
        /// Populates the printable string and pre-calculates the exact pixel coordinates for all icons into a recycled node.
        /// Preserves the fragile Unity word-wrapping pitch and font descender clearance mathematically.
        /// </summary>
        /// <param name="node">The cache node to populate with layout data.</param>
        /// <param name="fullText">The full text containing potential icon tags.</param>
        /// <param name="rectWidth">The width of the rectangle for text layout calculations.</param>
        /// <param name="scalePercent">The scale percent to apply to icon sizes and offsets.</param>
        private static void PopulateLayoutData(LruCacheNode node, string fullText, float rectWidth, float scalePercent)
        {
            string tagReplacement = GetTagReplacement(scalePercent);

            string cleanText = fullText.Replace(TagSentinel, "");
            node.PrintableText = cleanText
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

            try
            {
                PopulateLineBuffer(cleanText, pooledLineBuffer);
                float relativeY = 0f;

                for (int i = 0; i < pooledLineBuffer.Count; i++)
                {
                    string line = pooledLineBuffer[i];

                    if (line.Contains(TagProsthetic) || line.Contains(TagAthletic) || line.Contains(TagInjury))
                    {
                        float targetY = relativeY + iconOffsetYBase;
                        if (line.Contains(TagProsthetic)) ExtractIconCommand('P', TagProsthetic, line, tagReplacement, iconSize, targetY, node.Icons);
                        if (line.Contains(TagAthletic)) ExtractIconCommand('A', TagAthletic, line, tagReplacement, iconSize, targetY, node.Icons);
                        if (line.Contains(TagInjury)) ExtractIconCommand('I', TagInjury, line, tagReplacement, iconSize, targetY, node.Icons);
                    }

                    // Calculate visual rows wrapped
                    float measuredLineWidth = string.IsNullOrEmpty(line) ? 0f : Text.CalcSize(line).x;
                    int lineVisualRows = (measuredLineWidth > rectWidth && rectWidth > 0f)
                        ? Mathf.Max(1, Mathf.CeilToInt(measuredLineWidth / rectWidth))
                        : 1;

                    relativeY += (lineVisualRows * exactFontPitch);
                }
            }
            finally
            {
                pooledLineBuffer.Clear();
            }
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