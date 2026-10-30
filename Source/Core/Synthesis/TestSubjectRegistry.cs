using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [TEST-03] GROUPING ENUMS & DATA MODELS

    public enum GroupingDimension
    {
        Mod = 0,
        BodyDef = 1,
        Category = 2,
        FleshType = 3,
        Colony = 4
    }

    /// <summary>
    /// [TEST-03] Lightweight descriptor blueprinting a test subject for the Test Bench.
    /// Represents either an unspawned species archetype or an authentic live pawn.
    /// </summary>
    public class TestSubjectEntry
    {
        public string Label;
        public BodyDef BodyDef;
        public ThingDef RaceDef;
        public string ModName;
        public string CategoryName;
        public string FleshTypeName;
        public Pawn LivePawn;

        public bool IsLivePawn => LivePawn != null && !LivePawn.Destroyed;

        public TestSubjectEntry(
            string label, 
            BodyDef bodyDef, 
            ThingDef raceDef, 
            string modName,
            string categoryName,
            string fleshTypeName,
            Pawn livePawn = null)
        {
            Label = label;
            BodyDef = bodyDef;
            RaceDef = raceDef;
            ModName = modName;
            CategoryName = categoryName;
            FleshTypeName = fleshTypeName;
            LivePawn = livePawn;
        }
    }

    /// <summary>
    /// Represents a group of test subjects categorized under a specific label.
    /// </summary>
    public class TestSubjectGroup
    {
        public string GroupLabel;
        public List<TestSubjectEntry> Entries = new List<TestSubjectEntry>();

        public TestSubjectGroup(string groupLabel)
        {
            GroupLabel = groupLabel;
        }
    }

    #endregion

    /// <summary>
    /// [TEST-03] Central indexing registry categorizing all loaded species and live colony pawns
    /// across 5 dimensions (Mod, BodyDef, Category, FleshType, and Colony Map Pawns).
    /// </summary>
    public static class TestSubjectRegistry
    {
        #region 2. STORAGE & STATIC CACHES

        private static readonly List<TestSubjectEntry> allSubjects = new List<TestSubjectEntry>();
        private static readonly List<TestSubjectGroup> groupsByMod = new List<TestSubjectGroup>();
        private static readonly List<TestSubjectGroup> groupsByBodyDef = new List<TestSubjectGroup>();
        private static readonly List<TestSubjectGroup> groupsByCategory = new List<TestSubjectGroup>();
        private static readonly List<TestSubjectGroup> groupsByFleshType = new List<TestSubjectGroup>();

        private static bool isInitialized = false;
        private static readonly object initLock = new object();

        public static List<TestSubjectEntry> AllSubjects
        {
            get
            {
                EnsureCacheInitialized();
                return allSubjects;
            }
        }

        #endregion

        #region 3. PUBLIC QUERY API

        /// <summary>
        /// Retrieves the default test subject, prioritizing the human body definition if available.
        /// </summary>
        /// <returns>The default test subject entry, or null if no subjects are available.</returns>
        public static TestSubjectEntry GetDefaultSubject()
        {
            EnsureCacheInitialized();

            for (int i = 0; i < allSubjects.Count; i++)
            {
                if (allSubjects[i].BodyDef == BodyDefOf.Human)
                {
                    return allSubjects[i];
                }
            }

            return allSubjects.Count > 0 ? allSubjects[0] : null;
        }

        /// <summary>
        /// Retrieves the test subject groups filtered by the specified dimension and search text.
        /// </summary>
        /// <param name="dimension">The dimension by which to group the test subjects.</param>
        /// <param name="filterText">The text to filter the group labels by.</param>
        /// <returns>A list of test subject groups matching the filter criteria.</returns>
        public static List<TestSubjectGroup> GetFilteredGroups(GroupingDimension dimension, string filterText)
        {
            if (dimension == GroupingDimension.Colony)
            {
                return BuildLiveColonyGroups(filterText);
            }

            EnsureCacheInitialized();

            List<TestSubjectGroup> sourceGroups;
            switch (dimension)
            {
                case GroupingDimension.BodyDef:
                    sourceGroups = groupsByBodyDef;
                    break;
                case GroupingDimension.Category:
                    sourceGroups = groupsByCategory;
                    break;
                case GroupingDimension.FleshType:
                    sourceGroups = groupsByFleshType;
                    break;
                default:
                    sourceGroups = groupsByMod;
                    break;
            }

            if (string.IsNullOrEmpty(filterText))
            {
                return sourceGroups;
            }

            string cleanFilter = filterText.Trim().ToLowerInvariant();
            List<TestSubjectGroup> filteredResult = new List<TestSubjectGroup>();

            for (int g = 0; g < sourceGroups.Count; g++)
            {
                TestSubjectGroup group = sourceGroups[g];
                bool groupMatches = group.GroupLabel != null && group.GroupLabel.ToLowerInvariant().Contains(cleanFilter);

                TestSubjectGroup matchingGroup = new TestSubjectGroup(group.GroupLabel);

                for (int i = 0; i < group.Entries.Count; i++)
                {
                    TestSubjectEntry entry = group.Entries[i];
                    if (groupMatches || MatchesFilter(entry, cleanFilter))
                    {
                        matchingGroup.Entries.Add(entry);
                    }
                }

                if (matchingGroup.Entries.Count > 0)
                {
                    filteredResult.Add(matchingGroup);
                }
            }

            return filteredResult;
        }

        /// <summary>
        /// Determines whether a given test subject entry matches the specified filter text.
        /// </summary>
        /// <param name="entry">The test subject entry to evaluate against the filter.</param>
        /// <param name="cleanFilter">The normalized filter text to match against the entry's properties.</param>
        /// <returns>True if the entry matches the filter; otherwise, false.</returns>
        private static bool MatchesFilter(TestSubjectEntry entry, string cleanFilter)
        {
            if (entry.Label != null && entry.Label.ToLowerInvariant().Contains(cleanFilter)) return true;
            if (entry.ModName != null && entry.ModName.ToLowerInvariant().Contains(cleanFilter)) return true;
            if (entry.BodyDef?.defName != null && entry.BodyDef.defName.ToLowerInvariant().Contains(cleanFilter)) return true;
            if (entry.CategoryName != null && entry.CategoryName.ToLowerInvariant().Contains(cleanFilter)) return true;
            if (entry.FleshTypeName != null && entry.FleshTypeName.ToLowerInvariant().Contains(cleanFilter)) return true;
            return false;
        }

        /// <summary>
        /// Clears the cached test subject data, including all subjects and grouped collections, and resets the initialization state.
        /// </summary>
        public static void ClearCache()
        {
            lock (initLock)
            {
                allSubjects.Clear();
                groupsByMod.Clear();
                groupsByBodyDef.Clear();
                groupsByCategory.Clear();
                groupsByFleshType.Clear();
                isInitialized = false;
            }
        }

        #endregion

        #region 4. LIVE COLONY PAWN INGRESS ENGINE

        /// <summary>
        /// Builds and retrieves the groups of live colony pawns, filtered by the specified search text.
        /// </summary>
        /// <param name="filterText">The text to filter the live colony pawns by.</param>
        /// <returns>A list of test subject groups containing the live colony pawns that match the filter.</returns>
        private static List<TestSubjectGroup> BuildLiveColonyGroups(string filterText)
        {
            List<TestSubjectGroup> colonyGroups = new List<TestSubjectGroup>();

            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.Maps == null)
            {
                TestSubjectGroup emptyGroup = new TestSubjectGroup("OverHaulers_NoColonyActive".Translate().ToString());
                colonyGroups.Add(emptyGroup);
                return colonyGroups;
            }

            TestSubjectGroup colonistGroup = new TestSubjectGroup("OverHaulers_GroupColony_Colonists".Translate().ToString());
            TestSubjectGroup prisonerGroup = new TestSubjectGroup("OverHaulers_GroupColony_Prisoners".Translate().ToString());
            TestSubjectGroup animalGroup = new TestSubjectGroup("OverHaulers_GroupColony_Animals".Translate().ToString());
            TestSubjectGroup mechGroup = new TestSubjectGroup("OverHaulers_GroupColony_Mechanoids".Translate().ToString());

            string cleanFilter = string.IsNullOrEmpty(filterText) ? null : filterText.Trim().ToLowerInvariant();

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map?.mapPawns == null) continue;

                IReadOnlyList<Pawn> mapPawns = map.mapPawns.AllPawnsSpawned;
                for (int p = 0; p < mapPawns.Count; p++)
                {
                    Pawn pawn = mapPawns[p];
                    if (pawn == null || pawn.Dead || pawn.RaceProps?.body == null) continue;

                    if (pawn.IsColonist)
                        AddLivePawnIfMatches(colonistGroup, pawn, cleanFilter);
                    else if (pawn.IsPrisonerOfColony)
                        AddLivePawnIfMatches(prisonerGroup, pawn, cleanFilter);
                    else if (pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Animal)
                        AddLivePawnIfMatches(animalGroup, pawn, cleanFilter);
                    else if (pawn.Faction == Faction.OfPlayer && pawn.RaceProps.IsMechanoid)
                        AddLivePawnIfMatches(mechGroup, pawn, cleanFilter);
                }
            }

            if (colonistGroup.Entries.Count > 0) colonyGroups.Add(colonistGroup);
            if (prisonerGroup.Entries.Count > 0) colonyGroups.Add(prisonerGroup);
            if (animalGroup.Entries.Count > 0) colonyGroups.Add(animalGroup);
            if (mechGroup.Entries.Count > 0) colonyGroups.Add(mechGroup);

            if (colonyGroups.Count == 0)
            {
                TestSubjectGroup noneGroup = new TestSubjectGroup("OverHaulers_NoColonyPawnsFound".Translate().ToString());
                colonyGroups.Add(noneGroup);
            }

            return colonyGroups;
        }

        /// <summary>
        /// Adds a live pawn to the specified test subject group if it matches the given filter text.
        /// </summary>
        /// <param name="group">The test subject group to add the pawn to.</param>
        /// <param name="pawn">The live pawn to evaluate and potentially add.</param>
        /// <param name="cleanFilter">The normalized filter text to match against the pawn's properties.</param>
        /// <remarks>
        /// This method evaluates the pawn against the filter and only adds it to the group if it matches.
        /// </remarks>
        private static void AddLivePawnIfMatches(TestSubjectGroup group, Pawn pawn, string cleanFilter)
        {
            string label = pawn.LabelShortCap.ToString();
            string raceName = pawn.def.LabelCap.ToString();
            string displayLabel = $"{label} ({raceName})";

            if (cleanFilter != null)
            {
                bool matches = displayLabel.ToLowerInvariant().Contains(cleanFilter) ||
                               pawn.def.defName.ToLowerInvariant().Contains(cleanFilter) ||
                               pawn.RaceProps.body.defName.ToLowerInvariant().Contains(cleanFilter);

                if (!matches) return;
            }

            TestSubjectEntry entry = new TestSubjectEntry(
                displayLabel,
                pawn.RaceProps.body,
                pawn.def,
                pawn.Faction?.Name ?? "Colony",
                ResolveCategory(pawn.def),
                ResolveFleshType(pawn.def),
                pawn
            );

            group.Entries.Add(entry);
        }

        #endregion

        #region 5. INITIALIZATION & STATIC INDEXING PIPELINE

        /// <summary>
        /// Ensures that the test subject cache is initialized, populating all subjects and grouped collections if necessary.
        /// This method is thread-safe and will only perform initialization once.
        /// </summary>
        public static void EnsureCacheInitialized()
        {
            if (isInitialized) return;

            lock (initLock)
            {
                if (isInitialized) return;

                allSubjects.Clear();
                groupsByMod.Clear();
                groupsByBodyDef.Clear();
                groupsByCategory.Clear();
                groupsByFleshType.Clear();

                Dictionary<ModContentPack, List<TestSubjectEntry>> modMap = new Dictionary<ModContentPack, List<TestSubjectEntry>>();
                Dictionary<BodyDef, List<TestSubjectEntry>> bodyMap = new Dictionary<BodyDef, List<TestSubjectEntry>>();
                Dictionary<string, List<TestSubjectEntry>> categoryMap = new Dictionary<string, List<TestSubjectEntry>>();
                Dictionary<string, List<TestSubjectEntry>> fleshMap = new Dictionary<string, List<TestSubjectEntry>>();

                List<ThingDef> allThings = DefDatabase<ThingDef>.AllDefsListForReading;
                if (allThings != null)
                {
                    for (int i = 0; i < allThings.Count; i++)
                    {
                        ThingDef thing = allThings[i];
                        if (thing?.race?.body == null || thing.category != ThingCategory.Pawn) continue;

                        string readableName = thing.LabelCap.ToString();
                        if (string.IsNullOrEmpty(readableName)) readableName = thing.defName;

                        ModContentPack modPack = thing.modContentPack;
                        string modName = modPack?.Name ?? "Core";
                        
                        string catName = ResolveCategory(thing);
                        string fleshName = ResolveFleshType(thing);

                        TestSubjectEntry entry = new TestSubjectEntry(
                            readableName,
                            thing.race.body,
                            thing,
                            modName,
                            catName,
                            fleshName
                        );

                        allSubjects.Add(entry);

                        // A. Map by ModContentPack
                        if (modPack != null)
                        {
                            if (!modMap.TryGetValue(modPack, out var mList))
                            {
                                mList = new List<TestSubjectEntry>();
                                modMap[modPack] = mList;
                            }
                            mList.Add(entry);
                        }

                        // B. Map by BodyDef
                        if (thing.race.body != null)
                        {
                            if (!bodyMap.TryGetValue(thing.race.body, out var bList))
                            {
                                bList = new List<TestSubjectEntry>();
                                bodyMap[thing.race.body] = bList;
                            }
                            bList.Add(entry);
                        }

                        // C. Map by Category
                        if (!categoryMap.TryGetValue(catName, out var cList))
                        {
                            cList = new List<TestSubjectEntry>();
                            categoryMap[catName] = cList;
                        }
                        cList.Add(entry);

                        // D. Map by FleshType
                        if (!fleshMap.TryGetValue(fleshName, out var fList))
                        {
                            fList = new List<TestSubjectEntry>();
                            fleshMap[fleshName] = fList;
                        }
                        fList.Add(entry);
                    }
                }

                // Compile and Sort Groups
                PopulateGroupList(modMap, groupsByMod, true);
                PopulateGroupList(bodyMap, groupsByBodyDef, false);
                PopulateGroupList(categoryMap, groupsByCategory, false);
                PopulateGroupList(fleshMap, groupsByFleshType, false);

                allSubjects.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

                isInitialized = true;
            }
        }

        /// <summary>
        /// Populates a list of test subject groups from a dictionary mapping keys to lists of test subject entries.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys in the source dictionary.</typeparam>
        /// <param name="map">The source dictionary mapping keys to lists of test subject entries.</param>
        /// <param name="targetList">The target list to populate with test subject groups.</param>
        /// <param name="coreFirst">Indicates whether groups labeled "Core" should be prioritized at the top of the list.</param>
        private static void PopulateGroupList<TKey>(Dictionary<TKey, List<TestSubjectEntry>> map, List<TestSubjectGroup> targetList, bool coreFirst)
        {
            foreach (var kvp in map)
            {
                string label = (kvp.Key is ModContentPack mcp) ? mcp.Name : 
                               (kvp.Key is BodyDef bd) ? bd.defName : 
                               kvp.Key.ToString();

                kvp.Value.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
                targetList.Add(new TestSubjectGroup(label) { Entries = kvp.Value });
            }

            targetList.Sort((a, b) =>
            {
                if (coreFirst)
                {
                    if (a.GroupLabel == "Core") return -1;
                    if (b.GroupLabel == "Core") return 1;
                }
                return string.Compare(a.GroupLabel, b.GroupLabel, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Resolves the category of the given ThingDef, typically based on its thing categories or intelligence level.
        /// </summary>
        /// <param name="thing">The ThingDef for which to resolve the category.</param>
        /// <returns>A string representing the resolved category of the ThingDef.</returns>
        private static string ResolveCategory(ThingDef thing)
        {
            if (thing?.race == null) return "Unknown";
            
            if (thing.thingCategories != null && thing.thingCategories.Count > 0)
                return thing.thingCategories[0].LabelCap.ToString();
                
            return thing.race.intelligence.ToString().CapitalizeFirst(); 
        }

        /// <summary>
        /// Resolves the flesh type of the given ThingDef, typically based on its race's FleshType.
        /// </summary>
        /// <param name="thing">The ThingDef for which to resolve the flesh type.</param>
        /// <returns>A string representing the resolved flesh type of the ThingDef.</returns>
        private static string ResolveFleshType(ThingDef thing)
        {
            if (thing?.race?.FleshType == null) return "Unknown";
            
            return !string.IsNullOrEmpty(thing.race.FleshType.label) 
                ? thing.race.FleshType.label.CapitalizeFirst() 
                : thing.race.FleshType.defName.CapitalizeFirst();
        }

        #endregion
    }
}