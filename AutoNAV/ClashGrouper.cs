using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api;

namespace AutoNAV
{
    public class ClashGrouper
    {
        private const string CLASH_SETS_FOLDER = "2. CLASH SETS";

        public enum GroupingMode
        {
            None,
            Level,
            GridIntersection,
            SelectionA,
            SelectionB,
            ModelA,
            ModelB,
            AssignedTo,
            ApprovedBy,
            Status,
            File,
            Layer,
            First,
            Last,
            LastUnique,
            WallsAndFloors
        }

        // ─────────────────────────────────────────────────────────────
        // Public entry points
        // ─────────────────────────────────────────────────────────────

        // Default values matched in MainWindow.xaml so the UI and the API agree.
        public const int DefaultMaxClashesPerGroup = 15;
        public const int MinClashesPerGroup        = 1;
        public const int MaxClashesPerGroup        = 200;

        public static void GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups)
        {
            // Back-compat overload: legacy callers get the old behaviour with no
            // template and no per-group cap.
            GroupClashes(selectedClashTest, groupingMode, subgroupingMode, keepExistingGroups,
                         namingTemplate: null, maxClashesPerGroup: int.MaxValue);
        }

        public static void GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups,
            string namingTemplate,
            int maxClashesPerGroup)
        {
            try
            {
                if (maxClashesPerGroup < MinClashesPerGroup) maxClashesPerGroup = MinClashesPerGroup;
                if (maxClashesPerGroup > MaxClashesPerGroup && maxClashesPerGroup != int.MaxValue)
                    maxClashesPerGroup = MaxClashesPerGroup;

                List<ClashResult> clashResults =
                    GetIndividualClashResults(selectedClashTest, keepExistingGroups).ToList();

                List<ClashResultGroup> clashResultGroups = new List<ClashResultGroup>();
                List<ClashResult> ungroupedClashResults = new List<ClashResult>();

                if (groupingMode == GroupingMode.WallsAndFloors)
                {
                    GroupByWallsAndFloorsViaSearchSets(
                        clashResults,
                        out clashResultGroups,
                        out ungroupedClashResults);
                }
                else
                {
                    CreateGroup(ref clashResultGroups, groupingMode, clashResults, "");

                    if (subgroupingMode != GroupingMode.None)
                        CreateSubGroups(ref clashResultGroups, subgroupingMode);

                    ungroupedClashResults = RemoveOneClashGroup(ref clashResultGroups);
                }

                // Apply user naming template + per-group size cap. Empty template
                // preserves the legacy DisplayNames computed by GroupBy*.
                clashResultGroups = ApplyTemplateAndSplit(
                    clashResultGroups, selectedClashTest, namingTemplate, maxClashesPerGroup);

                if (keepExistingGroups)
                {
                    var existingGroups = BackupExistingClashGroups(selectedClashTest).ToList();
                    clashResultGroups.AddRange(existingGroups);
                }

                ProcessClashGroup(clashResultGroups, ungroupedClashResults, selectedClashTest);
            }
            catch (Exception ex)
            {
                throw new Exception("Error grouping clashes: " + ex.Message, ex);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Naming template + max-group-size machinery
        // ─────────────────────────────────────────────────────────────

        private struct NamingContext
        {
            public string Month, Day, Year;
            public string Grid, Level;
            public string TestName;
            public string SelectionA, SelectionB;
        }

        private static NamingContext BuildContext(ClashResultGroup group, ClashTest test)
        {
            var now = DateTime.Now;
            var ctx = new NamingContext
            {
                Month = now.ToString("MM"),
                Day = now.ToString("dd"),
                Year = now.ToString("yyyy"),
                TestName = test?.DisplayName ?? "",
                Grid = "",
                Level = "",
                SelectionA = "",
                SelectionB = ""
            };

            // Pull Grid + Level from the first clash result in the group.
            ClashResult first = null;
            foreach (var child in group.Children)
            {
                if (child is ClashResult cr) { first = cr; break; }
            }
            if (first != null)
            {
                try
                {
                    var grids = Application.MainDocument.Grids;
                    var gsys = grids != null ? grids.ActiveSystem : null;
                    if (gsys != null)
                    {
                        var gi = gsys.ClosestIntersection(first.Center);
                        if (gi != null)
                        {
                            ctx.Grid = string.IsNullOrEmpty(gi.DisplayName) ? "" : gi.DisplayName;
                            if (gi.Level != null && !string.IsNullOrEmpty(gi.Level.DisplayName))
                                ctx.Level = gi.Level.DisplayName;
                        }
                    }
                }
                catch { /* best effort — grid system not available */ }
            }

            // Selection A / B from test DisplayName split on " vs " (the project's
            // own naming convention used by ClashTestGeneratorEngine). For tests
            // that don't follow it, Selection A = entire test name, Selection B = "".
            if (!string.IsNullOrEmpty(ctx.TestName))
            {
                int vs = ctx.TestName.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase);
                if (vs > 0)
                {
                    ctx.SelectionA = ctx.TestName.Substring(0, vs).Trim();
                    ctx.SelectionB = ctx.TestName.Substring(vs + 4).Trim();
                }
                else
                {
                    ctx.SelectionA = ctx.TestName;
                }
            }

            return ctx;
        }

        private static string ApplyNamingTemplate(
            string template, NamingContext ctx,
            Dictionary<string, int> sequenceCounter)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            string baseName = template
                .Replace("{Month}", ctx.Month)
                .Replace("{Day}", ctx.Day)
                .Replace("{Year}", ctx.Year)
                .Replace("{Grid}", ctx.Grid)
                .Replace("{Level}", ctx.Level)
                .Replace("{Test Name}", ctx.TestName)
                .Replace("{Selection A}", ctx.SelectionA)
                .Replace("{Selection B}", ctx.SelectionB);

            // {#} bumps every time the same base (post-substitution, with the {#}
            // placeholder stripped) is requested. Reserves a sequence per unique
            // base name.
            string key = baseName.Replace("{#}", "").Trim();
            int n = sequenceCounter.TryGetValue(key, out var c) ? c + 1 : 1;
            sequenceCounter[key] = n;

            return baseName.Replace("{#}", n.ToString());
        }

        // Walks every group: renames per template (when set) and splits any group
        // whose ClashResult child count exceeds the cap. Returns a new list.
        private static List<ClashResultGroup> ApplyTemplateAndSplit(
            List<ClashResultGroup> groups,
            ClashTest test,
            string namingTemplate,
            int maxClashesPerGroup)
        {
            bool hasTemplate = !string.IsNullOrWhiteSpace(namingTemplate);
            bool hasCap = maxClashesPerGroup > 0 && maxClashesPerGroup != int.MaxValue;
            if (!hasTemplate && !hasCap) return groups;

            var seqCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ClashResultGroup>();

            foreach (var group in groups)
            {
                // Snapshot children so chunking is deterministic.
                var children = new List<SavedItem>();
                foreach (SavedItem c in group.Children) children.Add(c);

                string legacyName = group.DisplayName;

                // Empty group: keep with new/legacy name, no splitting.
                if (children.Count == 0)
                {
                    if (hasTemplate)
                    {
                        var ctx = BuildContext(group, test);
                        group.DisplayName = ApplyNamingTemplate(namingTemplate, ctx, seqCounter) ?? legacyName;
                    }
                    result.Add(group);
                    continue;
                }

                int chunkSize = (hasCap && maxClashesPerGroup < children.Count)
                                    ? maxClashesPerGroup
                                    : children.Count;
                int chunkCount = (int)Math.Ceiling(children.Count / (double)chunkSize);

                for (int idx = 0; idx < chunkCount; idx++)
                {
                    int offset = idx * chunkSize;
                    var slice = children.Skip(offset).Take(chunkSize).ToList();

                    ClashResultGroup outGroup;
                    if (chunkCount == 1)
                    {
                        // No split — reuse the original group instance.
                        outGroup = group;
                    }
                    else
                    {
                        outGroup = new ClashResultGroup();
                        foreach (var c in slice) outGroup.Children.Add(c);
                    }

                    string newName;
                    if (hasTemplate)
                    {
                        var ctx = BuildContext(outGroup, test);
                        newName = ApplyNamingTemplate(namingTemplate, ctx, seqCounter) ?? legacyName;
                    }
                    else
                    {
                        newName = chunkCount > 1
                                      ? legacyName + " (" + (idx + 1) + ")"
                                      : legacyName;
                    }

                    outGroup.DisplayName = newName;
                    result.Add(outGroup);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Function 6 — Group ALL tests by Walls / Floors via search sets
        // Returns a formatted summary string for the result dialog.
        //
        // Steps:
        //   1. Run every clash test to get fresh results.
        //   2. For each test, resolve which disciplines are involved
        //      (parsed from the "A vs B" test name) and load only those
        //      disciplines' Floors / Walls search sets.
        //   3. Partition each clash result: Walls → "Walls" group,
        //      Floors → "Floors" group, neither → left ungrouped
        //      (ready for Sherlock Distill).
        // ─────────────────────────────────────────────────────────────
        public static string GroupAllTestsByWallsAndFloors()
        {
            try
            {
                Document doc = Application.ActiveDocument;
                if (doc == null)
                    return "No active document found.";

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null || documentClash.TestsData == null)
                    return "Clash Detective is not available or no tests exist.";

                var allTests = ClashCompat.GetTopLevelTests(documentClash.TestsData).OfType<ClashTest>().ToList();
                if (allTests.Count == 0)
                    return "No clash tests found.";

                // Step 1 — build per-discipline Floors/Walls item sets once
                var disciplineMap = BuildDisciplineWallsFloorsMap(doc);

                if (disciplineMap.Count == 0)
                    return "No 'Walls' or 'Floors' search sets found under '" + CLASH_SETS_FOLDER + "'.\n\n" +
                           "Run Functions 1–3 first to create the required search sets.";

                int testsProcessed = 0, testsSkipped = 0;
                int wallsTotal = 0, floorsTotal = 0, otherTotal = 0;
                var errorLog = new List<string>();

                foreach (ClashTest test in allTests)
                {
                    try
                    {
                        var all = GetIndividualClashResults(test, false).ToList();
                        if (all.Count == 0) { testsSkipped++; continue; }

                        // Scope search sets to this test's disciplines only
                        var setMap = BuildSetMapForTest(test, disciplineMap);

                        GroupByWallsAndFloorsViaSearchSets(all, setMap,
                            out List<ClashResultGroup> groups,
                            out List<ClashResult> ungrouped);

                        int w = groups
                            .Where(g => g.DisplayName.StartsWith("Walls",  StringComparison.OrdinalIgnoreCase))
                            .Sum(g => g.Children.Count);
                        int f = groups
                            .Where(g => g.DisplayName.StartsWith("Floors", StringComparison.OrdinalIgnoreCase))
                            .Sum(g => g.Children.Count);

                        ProcessClashGroup(groups, ungrouped, test);

                        wallsTotal  += w;
                        floorsTotal += f;
                        otherTotal  += ungrouped.Count;
                        testsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        errorLog.Add(string.Format("  {0}: {1}", test.DisplayName, ex.Message));
                    }
                }

                string summary = string.Format(
                    "Function 6 — Walls / Floors Grouping Complete\n\n" +
                    "Tests processed : {0}\n" +
                    "Tests skipped   : {1}  (no results)\n\n" +
                    "Grouped as Walls  : {2}\n" +
                    "Grouped as Floors : {3}\n" +
                    "Left ungrouped    : {4}  (ready for Sherlock Distill)\n",
                    testsProcessed, testsSkipped, wallsTotal, floorsTotal, otherTotal);

                if (errorLog.Count > 0)
                    summary += "\nErrors:\n" + string.Join("\n", errorLog);

                return summary;
            }
            catch (Exception ex)
            {
                return "Fatal error in Function 6:\n\n" + ex.Message + "\n\n" + ex.StackTrace;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Core Walls/Floors grouping — search-set membership approach
        //
        // The old approach (checking PropertyCategory display names like
        // "Walls" / "Floors") failed because:
        //   1. Element category values vary across exporters and NWC versions.
        //   2. The fallback OR condition was duplicated ("Element" || "Element")
        //      so it never caught alternate category names.
        //
        // The fix uses Search.FindAll() — the same mechanism Navisworks uses
        // internally — to build a HashSet<ModelItem> per search set, then
        // checks each clash result's items (and their ancestors) for membership.
        // ─────────────────────────────────────────────────────────────

        private static void GroupByWallsAndFloorsViaSearchSets(
            List<ClashResult> results,
            Dictionary<string, HashSet<ModelItem>> setMap,
            out List<ClashResultGroup> groups,
            out List<ClashResult> ungrouped)
        {
            groups    = new List<ClashResultGroup>();
            ungrouped = new List<ClashResult>();

            var wallsGroup  = new ClashResultGroup { DisplayName = "Walls"  };
            var floorsGroup = new ClashResultGroup { DisplayName = "Floors" };

            bool hasWalls  = setMap.ContainsKey("Walls");
            bool hasFloors = setMap.ContainsKey("Floors");

            foreach (ClashResult cr in results)
            {
                ClashResult copy = (ClashResult)cr.CreateCopy();

                ModelItem item1 = null;
                ModelItem item2 = null;
                try { item1 = cr.CompositeItem1; }
                catch (Exception ex) { Debug.WriteLine("[AutoNAV] CompositeItem1 read failed: " + ex.Message); }
                try { item2 = cr.CompositeItem2; }
                catch (Exception ex) { Debug.WriteLine("[AutoNAV] CompositeItem2 read failed: " + ex.Message); }

                bool inWalls  = hasWalls  && (IsInSet(item1, setMap["Walls"])  || IsInSet(item2, setMap["Walls"]));
                bool inFloors = hasFloors && (IsInSet(item1, setMap["Floors"]) || IsInSet(item2, setMap["Floors"]));

                // Walls takes priority when an element matches both
                if (inWalls)
                    wallsGroup.Children.Add(copy);
                else if (inFloors)
                    floorsGroup.Children.Add(copy);
                else
                    ungrouped.Add(copy);
            }

            if (wallsGroup.Children.Count  > 0) groups.Add(wallsGroup);
            if (floorsGroup.Children.Count > 0) groups.Add(floorsGroup);
        }

        // Overload used by GroupClashes — builds setMap internally for one test
        private static void GroupByWallsAndFloorsViaSearchSets(
            List<ClashResult> results,
            out List<ClashResultGroup> groups,
            out List<ClashResult> ungrouped)
        {
            Document doc = Application.ActiveDocument;
            Dictionary<string, HashSet<ModelItem>> setMap =
                doc != null ? BuildWallsFloorsSearchSetMap(doc) : new Dictionary<string, HashSet<ModelItem>>();

            GroupByWallsAndFloorsViaSearchSets(results, setMap, out groups, out ungrouped);
        }

        // Build { "Walls" → HashSet<ModelItem>, "Floors" → … } from search sets
        // anywhere under "2. CLASH SETS". Each hit is expanded to include all
        // descendants so per-clash membership is a single HashSet lookup.
        private static Dictionary<string, HashSet<ModelItem>> BuildWallsFloorsSearchSetMap(Document doc)
        {
            var result = new Dictionary<string, HashSet<ModelItem>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                GroupItem root = doc.SelectionSets.RootItem as GroupItem;
                if (root == null) return result;

                GroupItem clashFolder = FindFolderInGroup(root, CLASH_SETS_FOLDER);
                if (clashFolder == null) return result;

                CollectWallsFloorsSearchSets(doc, clashFolder, result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AutoNAV] BuildWallsFloorsSearchSetMap failed: " + ex.Message);
            }

            return result;
        }

        // Build { discipline → { "Floors" → HashSet<ModelItem>, "Walls" → HashSet<ModelItem> } }
        // Scans every subfolder of "2. CLASH SETS" once.
        private static Dictionary<string, Dictionary<string, HashSet<ModelItem>>> BuildDisciplineWallsFloorsMap(Document doc)
        {
            var result = new Dictionary<string, Dictionary<string, HashSet<ModelItem>>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                GroupItem root = doc.SelectionSets.RootItem as GroupItem;
                if (root == null) return result;

                GroupItem clashFolder = FindFolderInGroup(root, CLASH_SETS_FOLDER);
                if (clashFolder == null) return result;

                foreach (SavedItem discItem in clashFolder.Children)
                {
                    if (!(discItem is GroupItem discGroup)) continue;
                    string discName = discItem.DisplayName?.Trim() ?? "";
                    if (string.IsNullOrEmpty(discName)) continue;

                    var discMap = new Dictionary<string, HashSet<ModelItem>>(StringComparer.OrdinalIgnoreCase);

                    foreach (SavedItem setItem in discGroup.Children)
                    {
                        string setName = setItem.DisplayName?.Trim() ?? "";
                        if (!setName.Equals("Walls",  StringComparison.OrdinalIgnoreCase) &&
                            !setName.Equals("Floors", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!(setItem is SelectionSet ss) || !ss.HasSearch) continue;

                        try
                        {
                            ModelItemCollection hits = ss.Search.FindAll(doc, false);
                            if (hits == null || hits.Count == 0) continue;

                            if (!discMap.TryGetValue(setName, out HashSet<ModelItem> bucket))
                            {
                                bucket = new HashSet<ModelItem>();
                                discMap[setName] = bucket;
                            }
                            foreach (ModelItem hit in hits)
                                AddWithDescendants(hit, bucket);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[AutoNAV] FindAll " + discName + "/" + setName + ": " + ex.Message);
                        }
                    }

                    if (discMap.Count > 0)
                        result[discName] = discMap;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AutoNAV] BuildDisciplineWallsFloorsMap: " + ex.Message);
            }
            return result;
        }

        // Merge only the Floors/Walls sets for the disciplines involved in a specific clash test.
        // Discipline names are parsed from the test's display name ("DiscA vs DiscB").
        // Falls back to the union of all disciplines if the name cannot be parsed.
        private static Dictionary<string, HashSet<ModelItem>> BuildSetMapForTest(
            ClashTest test,
            Dictionary<string, Dictionary<string, HashSet<ModelItem>>> disciplineMap)
        {
            var result = new Dictionary<string, HashSet<ModelItem>>(StringComparer.OrdinalIgnoreCase);

            string[] vsParts = test.DisplayName.Split(new[] { " vs " }, StringSplitOptions.None);
            var disciplines = new List<string>();
            foreach (string part in vsParts)
            {
                // Strip qualifiers like "Floors (MP)" → "MP", or "ST (excluding Floors)" → "ST"
                string d = part.Trim();
                int paren = d.IndexOf('(');
                if (paren > 0)
                    d = d.Substring(0, paren).Trim();
                else if (paren == 0)
                {
                    int close = d.IndexOf(')');
                    if (close > 0) d = d.Substring(1, close - 1).Trim();
                }
                if (!string.IsNullOrEmpty(d) &&
                    !d.Equals("Remaining", StringComparison.OrdinalIgnoreCase))
                    disciplines.Add(d);
            }

            IEnumerable<string> toCheck = disciplines.Count > 0
                ? (IEnumerable<string>)disciplines
                : disciplineMap.Keys;

            foreach (string disc in toCheck)
            {
                if (!disciplineMap.TryGetValue(disc, out var discMap)) continue;
                foreach (var kvp in discMap)
                {
                    if (!result.TryGetValue(kvp.Key, out var bucket))
                    {
                        bucket = new HashSet<ModelItem>();
                        result[kvp.Key] = bucket;
                    }
                    foreach (ModelItem mi in kvp.Value) bucket.Add(mi);
                }
            }

            return result;
        }

        private static void CollectWallsFloorsSearchSets(
            Document doc, GroupItem folder, Dictionary<string, HashSet<ModelItem>> result)
        {
            foreach (SavedItem child in folder.Children)
            {
                if (child is GroupItem nested)
                {
                    CollectWallsFloorsSearchSets(doc, nested, result);
                    continue;
                }

                string name = child.DisplayName?.Trim() ?? "";
                if (!name.Equals("Walls",  StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("Floors", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!(child is SelectionSet ss) || !ss.HasSearch) continue;

                try
                {
                    ModelItemCollection hits = ss.Search.FindAll(doc, false);
                    if (hits == null || hits.Count == 0) continue;

                    if (!result.TryGetValue(name, out HashSet<ModelItem> bucket))
                    {
                        bucket = new HashSet<ModelItem>();
                        result[name] = bucket;
                    }

                    foreach (ModelItem hit in hits)
                        AddWithDescendants(hit, bucket);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[AutoNAV] Search.FindAll failed for '" + name + "': " + ex.Message);
                }
            }
        }

        private static void AddWithDescendants(ModelItem item, HashSet<ModelItem> bucket)
        {
            if (item == null) return;
            try
            {
                foreach (ModelItem mi in item.DescendantsAndSelf)
                    bucket.Add(mi);
            }
            catch
            {
                bucket.Add(item);
            }
        }

        // O(1) membership check — descendants were pre-expanded into the set.
        private static bool IsInSet(ModelItem item, HashSet<ModelItem> set)
        {
            return item != null && set != null && set.Count > 0 && set.Contains(item);
        }

        // ─────────────────────────────────────────────────────────────
        // Existing grouping modes (unchanged)
        // ─────────────────────────────────────────────────────────────

        private static void CreateGroup(
            ref List<ClashResultGroup> clashResultGroups,
            GroupingMode groupingMode,
            List<ClashResult> clashResults,
            string initialName)
        {
            switch (groupingMode)
            {
                case GroupingMode.None:             return;
                case GroupingMode.Level:            clashResultGroups = GroupByLevel(clashResults, initialName); break;
                case GroupingMode.GridIntersection: clashResultGroups = GroupByGridIntersection(clashResults, initialName); break;
                case GroupingMode.SelectionA:
                case GroupingMode.SelectionB:       clashResultGroups = GroupByElementOfAGivenSelection(clashResults, groupingMode, initialName); break;
                case GroupingMode.ModelA:
                case GroupingMode.ModelB:           clashResultGroups = GroupByElementOfAGivenModel(clashResults, groupingMode, initialName); break;
                case GroupingMode.ApprovedBy:
                case GroupingMode.AssignedTo:
                case GroupingMode.Status:           clashResultGroups = GroupByProperties(clashResults, groupingMode, initialName); break;
                case GroupingMode.File:             clashResultGroups = GroupByFile(clashResults, initialName); break;
                case GroupingMode.Layer:            clashResultGroups = GroupByLayer(clashResults, initialName); break;
                case GroupingMode.First:            clashResultGroups = GroupByElement(clashResults, initialName, useItem2: false); break;
                case GroupingMode.Last:             clashResultGroups = GroupByElement(clashResults, initialName, useItem2: true); break;
                case GroupingMode.LastUnique:       clashResultGroups = GroupByLastUnique(clashResults, initialName); break;
                case GroupingMode.WallsAndFloors:
                    GroupByWallsAndFloorsViaSearchSets(clashResults, out clashResultGroups, out _);
                    break;
            }
        }

        private static void CreateSubGroups(
            ref List<ClashResultGroup> clashResultGroups, GroupingMode mode)
        {
            List<ClashResultGroup> clashResultSubGroups = new List<ClashResultGroup>();

            foreach (ClashResultGroup group in clashResultGroups)
            {
                List<ClashResult> clashResults = new List<ClashResult>();
                foreach (SavedItem item in group.Children)
                {
                    if (item is ClashResult cr) clashResults.Add(cr);
                }

                List<ClashResultGroup> tempSubs = new List<ClashResultGroup>();
                CreateGroup(ref tempSubs, mode, clashResults, group.DisplayName + "_");
                clashResultSubGroups.AddRange(tempSubs);
            }

            clashResultGroups = clashResultSubGroups;
        }

        public static void UnGroupClashes(ClashTest selectedClashTest)
        {
            List<ClashResult> results = GetIndividualClashResults(selectedClashTest, false).ToList();
            List<ClashResult> copies  = results.Select(r => (ClashResult)r.CreateCopy()).ToList();
            ProcessClashGroup(new List<ClashResultGroup>(), copies, selectedClashTest);
        }

        #region Grouping functions

        private static List<ClashResultGroup> GroupByLevel(List<ClashResult> results, string initialName)
        {
            GridSystem gridSystem = Application.MainDocument.Grids.ActiveSystem;
            Dictionary<GridLevel, ClashResultGroup> groups = new Dictionary<GridLevel, ClashResultGroup>();

            ClashResultGroup nullGridGroup = new ClashResultGroup { DisplayName = initialName + "No Level" };

            foreach (ClashResult result in results)
            {
                ClashResult copy = (ClashResult)result.CreateCopy();
                GridIntersection closest = gridSystem.ClosestIntersection(copy.Center);
                if (closest != null)
                {
                    GridLevel level = closest.Level;
                    if (!groups.TryGetValue(level, out ClashResultGroup g))
                    {
                        string name = string.IsNullOrEmpty(level.DisplayName) ? "Unnamed Level" : level.DisplayName;
                        g = new ClashResultGroup { DisplayName = initialName + name };
                        groups.Add(level, g);
                    }
                    g.Children.Add(copy);
                }
                else
                {
                    nullGridGroup.Children.Add(copy);
                }
            }

            var sorted = groups.OrderBy(k => k.Key.Elevation).ToDictionary(k => k.Key, k => k.Value);
            List<ClashResultGroup> list = sorted.Values.ToList();
            if (nullGridGroup.Children.Count > 0) list.Add(nullGridGroup);
            return list;
        }

        private static List<ClashResultGroup> GroupByGridIntersection(List<ClashResult> results, string initialName)
        {
            GridSystem gridSystem = Application.MainDocument.Grids.ActiveSystem;
            Dictionary<GridIntersection, ClashResultGroup> groups = new Dictionary<GridIntersection, ClashResultGroup>();

            ClashResultGroup nullGroup = new ClashResultGroup { DisplayName = initialName + "No Grid intersection" };

            foreach (ClashResult result in results)
            {
                ClashResult copy = (ClashResult)result.CreateCopy();
                GridIntersection gi = gridSystem.ClosestIntersection(copy.Center);
                if (gi != null)
                {
                    if (!groups.TryGetValue(gi, out ClashResultGroup g))
                    {
                        string name = string.IsNullOrEmpty(gi.DisplayName) ? "Unnamed Grid Intersection" : gi.DisplayName;
                        g = new ClashResultGroup { DisplayName = initialName + name };
                        groups.Add(gi, g);
                    }
                    g.Children.Add(copy);
                }
                else
                {
                    nullGroup.Children.Add(copy);
                }
            }

            var sorted = groups.OrderBy(k => k.Key.Position.X)
                               .OrderBy(k => k.Key.Level.Elevation)
                               .ToDictionary(k => k.Key, k => k.Value);
            List<ClashResultGroup> list = sorted.Values.ToList();
            if (nullGroup.Children.Count > 0) list.Add(nullGroup);
            return list;
        }

        private static List<ClashResultGroup> GroupByElementOfAGivenSelection(
            List<ClashResult> results, GroupingMode mode, string initialName)
        {
            Dictionary<ModelItem, ClashResultGroup> groups = new Dictionary<ModelItem, ClashResultGroup>();
            List<ClashResultGroup> emptyGroups = new List<ClashResultGroup>();

            foreach (ClashResult result in results)
            {
                ClashResult copy = (ClashResult)result.CreateCopy();
                ModelItem mi = null;

                if (mode == GroupingMode.SelectionA)
                    mi = copy.CompositeItem1 != null
                        ? GetSignificantAncestorOrSelf(copy.CompositeItem1)
                        : (copy.CompositeItem2 != null ? GetSignificantAncestorOrSelf(copy.CompositeItem2) : null);
                else
                    mi = copy.CompositeItem2 != null
                        ? GetSignificantAncestorOrSelf(copy.CompositeItem2)
                        : (copy.CompositeItem1 != null ? GetSignificantAncestorOrSelf(copy.CompositeItem1) : null);

                if (mi != null)
                {
                    if (!groups.TryGetValue(mi, out ClashResultGroup g))
                    {
                        string name = !string.IsNullOrEmpty(mi.DisplayName) ? mi.DisplayName
                                      : (mi.Parent != null ? mi.Parent.DisplayName : "Unnamed Parent");
                        g = new ClashResultGroup { DisplayName = initialName + (name ?? "Unnamed") };
                        groups.Add(mi, g);
                    }
                    g.Children.Add(copy);
                }
                else
                {
                    var solo = new ClashResultGroup { DisplayName = "Empty clash" };
                    solo.Children.Add(copy);
                    emptyGroups.Add(solo);
                }
            }

            List<ClashResultGroup> all = groups.Values.ToList();
            all.AddRange(emptyGroups);
            return all;
        }

        private static List<ClashResultGroup> GroupByElementOfAGivenModel(
            List<ClashResult> results, GroupingMode mode, string initialName)
        {
            Dictionary<ModelItem, ClashResultGroup> groups = new Dictionary<ModelItem, ClashResultGroup>();
            List<ClashResultGroup> emptyGroups = new List<ClashResultGroup>();

            foreach (ClashResult result in results)
            {
                ClashResult copy = (ClashResult)result.CreateCopy();
                ModelItem root = mode == GroupingMode.ModelA
                    ? (copy.CompositeItem1 != null ? GetFileAncestor(copy.CompositeItem1) : GetFileAncestor(copy.CompositeItem2))
                    : (copy.CompositeItem2 != null ? GetFileAncestor(copy.CompositeItem2) : GetFileAncestor(copy.CompositeItem1));

                if (root != null)
                {
                    if (!groups.TryGetValue(root, out ClashResultGroup g))
                    {
                        string name = !string.IsNullOrEmpty(root.DisplayName) ? root.DisplayName : "Unnamed Model";
                        g = new ClashResultGroup { DisplayName = initialName + name };
                        groups.Add(root, g);
                    }
                    g.Children.Add(copy);
                }
                else
                {
                    var solo = new ClashResultGroup { DisplayName = "Empty clash" };
                    solo.Children.Add(copy);
                    emptyGroups.Add(solo);
                }
            }

            List<ClashResultGroup> all = groups.Values.ToList();
            all.AddRange(emptyGroups);
            return all;
        }

        private static List<ClashResultGroup> GroupByProperties(
            List<ClashResult> results, GroupingMode mode, string initialName)
        {
            Dictionary<string, ClashResultGroup> groups = new Dictionary<string, ClashResultGroup>();

            foreach (ClashResult result in results)
            {
                ClashResult copy = (ClashResult)result.CreateCopy();
                string prop = mode == GroupingMode.ApprovedBy ? ClashCompat.GetApprovedBy(copy)
                            : mode == GroupingMode.AssignedTo ? ClashCompat.GetAssignedTo(copy)
                            : copy.Status.ToString();

                if (string.IsNullOrEmpty(prop)) prop = "Unspecified";

                if (!groups.TryGetValue(prop, out ClashResultGroup g))
                {
                    g = new ClashResultGroup { DisplayName = initialName + prop };
                    groups.Add(prop, g);
                }
                g.Children.Add(copy);
            }
            return groups.Values.ToList();
        }

        private static List<ClashResultGroup> GroupByFile(List<ClashResult> results, string initialName)
        {
            List<ClashResultGroup> list = new List<ClashResultGroup>();
            foreach (ClashResult cr in results)
            {
                string fileName = "Unknown File";
                try
                {
                    ModelItem fa = GetFileAncestor(cr.CompositeItem1)
                                ?? GetFileAncestor(cr.CompositeItem2);
                    if (fa != null && !string.IsNullOrEmpty(fa.DisplayName))
                        fileName = fa.DisplayName;
                }
                catch { }

                string groupName = initialName + fileName;
                ClashResultGroup g = list.FirstOrDefault(x => x.DisplayName == groupName);
                if (g == null) { g = new ClashResultGroup { DisplayName = groupName }; list.Add(g); }
                g.Children.Add(cr.CreateCopy());
            }
            return list.OrderBy(x => x.DisplayName).ToList();
        }

        private static List<ClashResultGroup> GroupByLayer(List<ClashResult> results, string initialName)
        {
            List<ClashResultGroup> list = new List<ClashResultGroup>();
            foreach (ClashResult cr in results)
            {
                string layerName = "No Layer";
                try
                {
                    layerName = ExtractLayer(GetSignificantAncestorOrSelf(cr.CompositeItem1))
                             ?? ExtractLayer(GetSignificantAncestorOrSelf(cr.CompositeItem2))
                             ?? "No Layer";
                }
                catch { }

                string groupName = initialName + layerName;
                ClashResultGroup g = list.FirstOrDefault(x => x.DisplayName == groupName);
                if (g == null) { g = new ClashResultGroup { DisplayName = groupName }; list.Add(g); }
                g.Children.Add(cr.CreateCopy());
            }
            return list.OrderBy(x => x.DisplayName).ToList();
        }

        private static string ExtractLayer(ModelItem item)
        {
            if (item?.PropertyCategories == null) return null;
            foreach (var cat in item.PropertyCategories)
                foreach (var prop in cat.Properties)
                    if (prop.DisplayName.ToLower().Contains("layer"))
                        return prop.Value.ToDisplayString();
            return null;
        }

        private static List<ClashResultGroup> GroupByElement(
            List<ClashResult> results, string initialName, bool useItem2)
        {
            List<ClashResultGroup> list = new List<ClashResultGroup>();
            foreach (ClashResult cr in results)
            {
                string name = "Empty clash";
                try
                {
                    ModelItem mi = GetSignificantAncestorOrSelf(
                        useItem2 ? cr.CompositeItem2 : cr.CompositeItem1);
                    if (mi != null)
                        name = !string.IsNullOrEmpty(mi.DisplayName) ? mi.DisplayName : "Unnamed Element";
                }
                catch { }

                string groupName = initialName + name;
                ClashResultGroup g = list.FirstOrDefault(x => x.DisplayName == groupName);
                if (g == null) { g = new ClashResultGroup { DisplayName = groupName }; list.Add(g); }
                g.Children.Add(cr.CreateCopy());
            }
            return list.OrderBy(x => x.DisplayName).ToList();
        }

        private static List<ClashResultGroup> GroupByLastUnique(List<ClashResult> results, string initialName)
        {
            List<ClashResultGroup> list = new List<ClashResultGroup>();
            foreach (ClashResult cr in results)
            {
                string n1 = "Unknown1", n2 = "Unknown2";
                try
                {
                    n1 = GetSignificantAncestorOrSelf(cr.CompositeItem1)?.DisplayName ?? "Unknown1";
                    n2 = GetSignificantAncestorOrSelf(cr.CompositeItem2)?.DisplayName ?? "Unknown2";
                }
                catch { }

                string groupName = initialName + n1 + " vs " + n2;
                ClashResultGroup g = list.FirstOrDefault(x => x.DisplayName == groupName);
                if (g == null) { g = new ClashResultGroup { DisplayName = groupName }; list.Add(g); }
                g.Children.Add(cr.CreateCopy());
            }
            return list.OrderBy(x => x.DisplayName).ToList();
        }

        #endregion

        #region Helpers

        // ProcessClashGroup — writes groups and ungrouped results back to the document.
        //
        // Critical pattern: TestsAddCopy does NOT deep-copy a ClashResultGroup's children
        // when the group was built in memory (children added via .Children.Add before the
        // group existed in the document). The fix is:
        //   1. Add the empty group shell via TestsAddCopy → it now lives in the document.
        //   2. Retrieve the live document reference to that group.
        //   3. Add each ClashResult to the live group via TestsAddCopy.
        private static void ProcessClashGroup(
            List<ClashResultGroup> clashGroups,
            List<ClashResult> ungroupedClashResults,
            ClashTest selectedClashTest)
        {
            Transaction tx = null;
            Progress progressBar = null;
            try
            {
                DocumentClash docClash = Application.MainDocument.GetClash();
                int idx = ClashCompat.IndexOfTest(docClash.TestsData, selectedClashTest);
                if (idx < 0) return;

                tx = Application.MainDocument.BeginTransaction("Group clashes");

                // Replace the test with an empty copy to clear existing children
                ClashCompat.TestsReplaceAtRoot(
                    docClash.TestsData,
                    idx, (ClashTest)selectedClashTest.CreateCopyWithoutChildren());

                int totalItems = clashGroups.Sum(g => g.Children.Count) + ungroupedClashResults.Count;
                progressBar = Application.BeginProgress("Grouping Clashes", "Processing...");
                int done = 0;

                foreach (ClashResultGroup grp in clashGroups)
                {
                    if (progressBar.IsCanceled) break;

                    // Step 1 — add the empty shell so the group exists in the document
                    docClash.TestsData.TestsAddCopy(
                        (GroupItem)ClashCompat.TestAt(docClash.TestsData, idx),
                        new ClashResultGroup { DisplayName = grp.DisplayName });

                    // Step 2 — walk back to find the live group reference (last ClashResultGroup)
                    ClashTest liveTest = (ClashTest)ClashCompat.TestAt(docClash.TestsData, idx);
                    ClashResultGroup liveGroup = null;
                    for (int i = liveTest.Children.Count - 1; i >= 0; i--)
                    {
                        if (liveTest.Children[i] is ClashResultGroup crg)
                        {
                            liveGroup = crg;
                            break;
                        }
                    }

                    if (liveGroup == null) continue;

                    // Step 3 — add each result to the live document-bound group
                    foreach (SavedItem child in grp.Children)
                    {
                        if (progressBar.IsCanceled) break;
                        if (child is ClashResult cr)
                            docClash.TestsData.TestsAddCopy(liveGroup, cr);
                        progressBar.Update((double)++done / Math.Max(totalItems, 1));
                    }
                }

                foreach (ClashResult cr in ungroupedClashResults)
                {
                    if (progressBar.IsCanceled) break;
                    docClash.TestsData.TestsAddCopy((GroupItem)ClashCompat.TestAt(docClash.TestsData, idx), cr);
                    progressBar.Update((double)++done / Math.Max(totalItems, 1));
                }

                tx.Commit();
            }
            finally
            {
                if (progressBar != null) Application.EndProgress();
                if (tx != null) tx.Dispose();
            }
        }

        private static List<ClashResult> RemoveOneClashGroup(ref List<ClashResultGroup> groups)
        {
            List<ClashResult> ungrouped = new List<ClashResult>();
            var temp = groups.ToList();
            foreach (ClashResultGroup g in temp)
            {
                if (g.Children.Count == 1)
                {
                    ungrouped.Add((ClashResult)g.Children.First());
                    groups.Remove(g);
                }
            }
            return ungrouped;
        }

        private static IEnumerable<ClashResult> GetIndividualClashResults(
            ClashTest clashTest, bool keepExistingGroup)
        {
            for (int i = 0; i < clashTest.Children.Count; i++)
            {
                if (clashTest.Children[i].IsGroup)
                {
                    if (!keepExistingGroup)
                        foreach (ClashResult cr in GetGroupResults((ClashResultGroup)clashTest.Children[i]))
                            yield return cr;
                }
                else
                {
                    yield return (ClashResult)clashTest.Children[i];
                }
            }
        }

        private static IEnumerable<ClashResultGroup> BackupExistingClashGroups(ClashTest clashTest)
        {
            for (int i = 0; i < clashTest.Children.Count; i++)
                if (clashTest.Children[i].IsGroup)
                    yield return (ClashResultGroup)clashTest.Children[i].CreateCopy();
        }

        private static IEnumerable<ClashResult> GetGroupResults(ClashResultGroup g)
        {
            for (int i = 0; i < g.Children.Count; i++)
                yield return (ClashResult)g.Children[i];
        }

        private static ModelItem GetSignificantAncestorOrSelf(ModelItem item)
        {
            if (item == null) return null;
            ModelItem original  = item;
            ModelItem composite = null;
            while (item.Parent != null)
            {
                item = item.Parent;
                if (item.IsComposite) composite = item;
            }
            return composite ?? original;
        }

        private static ModelItem GetFileAncestor(ModelItem item)
        {
            if (item == null) return null;
            ModelItem original = item;
            while (item.Parent != null)
            {
                item = item.Parent;
                if (item.HasModel) return item;
            }
            return original;
        }

        private static GroupItem FindFolderInGroup(GroupItem parent, string name)
        {
            if (parent == null) return null;
            foreach (SavedItem child in parent.Children)
                if (child is GroupItem g &&
                    g.DisplayName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return g;
            return null;
        }

        #endregion
    }
}
