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

        // Back-compat overload (legacy callers).
        public static void GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups)
        {
            GroupClashes(selectedClashTest, groupingMode, subgroupingMode, keepExistingGroups, namingTemplate: null);
        }

        // Overload with status filters.
        //
        // newStatusFilter      - clashes whose Status is in this set are eligible
        //                        for fresh grouping/naming.  Empty set ≡ "no New
        //                        Clashes pass set" (the regroup path is taken).
        // regroupStatusFilter  - when newStatusFilter is empty, existing
        //                        ClashResultGroups whose children include any
        //                        clash matching this set are renamed in place
        //                        via the template.  Ignored when newStatusFilter
        //                        is non-empty (UI ghosts those checkboxes).
        public static void GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups,
            string namingTemplate,
            ISet<ClashResultStatus> newStatusFilter,
            ISet<ClashResultStatus> regroupStatusFilter)
        {
            // When the user has unchecked every "New Clashes" status, the
            // workflow is "regroup & rename" only: walk existing groups, filter
            // by regroupStatusFilter, and rename via the template.  No fresh
            // grouping happens.
            bool isRegroupOnly = (newStatusFilter == null || newStatusFilter.Count == 0)
                              && (regroupStatusFilter != null && regroupStatusFilter.Count > 0)
                              && !string.IsNullOrWhiteSpace(namingTemplate);

            if (isRegroupOnly)
            {
                RegroupAndRenameExisting(selectedClashTest, regroupStatusFilter, namingTemplate);
                return;
            }

            // Otherwise, normal grouping path with optional status filter on the
            // input clashResults.
            GroupClashes(selectedClashTest, groupingMode, subgroupingMode, keepExistingGroups, namingTemplate,
                         (IEnumerable<ClashResultStatus>)newStatusFilter);
        }

        // Variant that accepts a status filter on the source clash results.
        public static void GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups,
            string namingTemplate,
            IEnumerable<ClashResultStatus> newStatusFilter)
        {
            HashSet<ClashResultStatus> filter = null;
            if (newStatusFilter != null)
            {
                filter = new HashSet<ClashResultStatus>(newStatusFilter);
                if (filter.Count == 0) filter = null;
            }

            // Fresh membership lookups per grouping pass, then reused across all
            // groups of this test rather than rebuilt per group.
            ResetNamingCaches();

            try
            {
                List<ClashResult> clashResults =
                    GetIndividualClashResults(selectedClashTest, keepExistingGroups).ToList();
                if (filter != null)
                    clashResults = clashResults.Where(cr => filter.Contains(cr.Status)).ToList();

                List<ClashResultGroup> clashResultGroups = new List<ClashResultGroup>();
                List<ClashResult> ungroupedClashResults = new List<ClashResult>();

                if (groupingMode == GroupingMode.WallsAndFloors)
                {
                    GroupByWallsAndFloorsViaSearchSets(
                        clashResults, out clashResultGroups, out ungroupedClashResults);
                }
                else
                {
                    CreateGroup(ref clashResultGroups, groupingMode, clashResults, "");
                    if (subgroupingMode != GroupingMode.None)
                        CreateSubGroups(ref clashResultGroups, subgroupingMode);
                    ungroupedClashResults = RemoveOneClashGroup(ref clashResultGroups);
                }

                clashResultGroups = ApplyTemplateToGroups(clashResultGroups, selectedClashTest, namingTemplate);

                if (!string.IsNullOrWhiteSpace(namingTemplate) && ungroupedClashResults.Count > 0)
                {
                    var fallback = new ClashResultGroup { DisplayName = "" };
                    foreach (var cr in ungroupedClashResults) fallback.Children.Add(cr);
                    ungroupedClashResults = new List<ClashResult>();
                    var wrapped = new List<ClashResultGroup> { fallback };
                    wrapped = ApplyTemplateToGroups(wrapped, selectedClashTest, namingTemplate);
                    clashResultGroups.AddRange(wrapped);
                }

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

        // Walks an existing test's top-level ClashResultGroup children; for each
        // group whose descendant clashes include any with a status in the
        // filter, applies the template via TestsEditDisplayName.  No clashes
        // are moved, only renamed.
        private static void RegroupAndRenameExisting(
            ClashTest test, ISet<ClashResultStatus> statusFilter, string template)
        {
            if (test == null || statusFilter == null || statusFilter.Count == 0) return;
            if (string.IsNullOrWhiteSpace(template)) return;

            var toRename = new List<ClashResultGroup>();
            foreach (var child in test.Children)
            {
                if (!(child is ClashResultGroup grp)) continue;
                if (GroupContainsAnyStatus(grp, statusFilter))
                    toRename.Add(grp);
            }

            if (toRename.Count > 0)
                RenameGroupsWithTemplate(toRename, test, template);
        }

        private static bool GroupContainsAnyStatus(ClashResultGroup grp, ISet<ClashResultStatus> statuses)
        {
            foreach (var item in grp.Children)
            {
                if (item is ClashResult cr && statuses.Contains(cr.Status)) return true;
                if (item is ClashResultGroup nested && GroupContainsAnyStatus(nested, statuses)) return true;
            }
            return false;
        }

        // Overload that accepts a naming-template string.  When non-empty, every
        // produced ClashResultGroup is renamed by ApplyTemplateToGroups using
        // tokens {Month} {Day} {Year} {Level} {Area} {TestName} {SelectionA}
        // {SelectionB} {#}.  Empty template preserves the legacy mode-specific
        // names.
        public static void GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups,
            string namingTemplate)
        {
            try
            {
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

                // Apply naming template (no-op when template is null/empty).
                clashResultGroups = ApplyTemplateToGroups(clashResultGroups, selectedClashTest, namingTemplate);

                // Ensure no naked-ungrouped clashes — if a template is set, wrap
                // every leftover into a fallback group named by the template.
                if (!string.IsNullOrWhiteSpace(namingTemplate) && ungroupedClashResults.Count > 0)
                {
                    var fallback = new ClashResultGroup { DisplayName = "" };
                    foreach (var cr in ungroupedClashResults) fallback.Children.Add(cr);
                    ungroupedClashResults = new List<ClashResult>();
                    var wrapped = new List<ClashResultGroup> { fallback };
                    wrapped = ApplyTemplateToGroups(wrapped, selectedClashTest, namingTemplate);
                    clashResultGroups.AddRange(wrapped);
                }

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

        // ─────────────────────────────────────────────────────────────
        // Naming template engine
        // ─────────────────────────────────────────────────────────────

        private struct NamingContext
        {
            public string Month, Day, Year;
            public string Level, Area;
            public string TestName, SelectionA, SelectionB;
        }

        // Substitutes tokens. Empty values get a parameter-name placeholder so
        // every clash group ends up with a non-empty name (per the user spec:
        // Public preview hook used by the Rename tab's DataGrid to compute the
        // "Proposed Name" column for an existing group + clash test.  Mirrors
        // the substitution ApplyTemplateToGroups does internally, plus extra
        // tokens that are useful purely at preview time (ClashCount, Status,
        // GroupIndex, Time).  Caller supplies the per-test sequence counter so
        // {#} numbers are stable across the visible row set.
        public static string ComputeProposedName(
            string template,
            ClashTest test,
            ClashResultGroup group,
            int groupIndex,
            Dictionary<string, int> sequenceCounter)
        {
            if (string.IsNullOrWhiteSpace(template) || group == null) return "";

            var ctx = BuildContext(group, test);

            // Extra tokens beyond the substitution set the grouping pass uses.
            string clashCount  = CountClashLeaves(group).ToString();
            string status      = GetFirstClashStatus(group);
            string groupIndexS = groupIndex.ToString();
            string time        = DateTime.Now.ToString("HH:mm");

            string baseName = ApplyNamingTemplate(template, ctx, sequenceCounter) ?? "";
            return baseName
                .Replace("{ClashCount}",  clashCount)
                .Replace("{Status}",      status)
                .Replace("{GroupIndex}",  groupIndexS)
                .Replace("{Time}",        time);
        }

        private static int CountClashLeaves(ClashResultGroup g)
        {
            int n = 0;
            foreach (var c in g.Children)
            {
                if (c is ClashResult) n++;
                else if (c is ClashResultGroup nested) n += CountClashLeaves(nested);
            }
            return n;
        }

        private static string GetFirstClashStatus(ClashResultGroup g)
        {
            foreach (var c in g.Children)
            {
                if (c is ClashResult cr) return cr.Status.ToString();
                if (c is ClashResultGroup nested)
                {
                    var s = GetFirstClashStatus(nested);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            return "";
        }

        // missing level → "LXX", missing area → "AREA").
        private static string ApplyNamingTemplate(string template, NamingContext ctx, Dictionary<string, int> sequenceCounter)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            string baseName = template
                .Replace("{Month}",      ctx.Month)
                .Replace("{Day}",        ctx.Day)
                .Replace("{Year}",       ctx.Year)
                .Replace("{Level}",      string.IsNullOrEmpty(ctx.Level)      ? "LXX"  : ctx.Level)
                .Replace("{Area}",       string.IsNullOrEmpty(ctx.Area)       ? "AREA" : ctx.Area)
                .Replace("{TestName}",   string.IsNullOrEmpty(ctx.TestName)   ? "TestName"   : ctx.TestName)
                .Replace("{SelectionA}", string.IsNullOrEmpty(ctx.SelectionA) ? "SelectionA" : ctx.SelectionA)
                .Replace("{SelectionB}", string.IsNullOrEmpty(ctx.SelectionB) ? "SelectionB" : ctx.SelectionB);

            string key = baseName.Replace("{#}", "").Trim();
            int n = sequenceCounter.TryGetValue(key, out var c) ? c + 1 : 1;
            sequenceCounter[key] = n;
            return baseName.Replace("{#}", n.ToString());
        }

        // Walks every group, builds a context (with fallbacks for level/area)
        // and renames each via the template.
        private static List<ClashResultGroup> ApplyTemplateToGroups(
            List<ClashResultGroup> groups, ClashTest test, string template)
        {
            if (string.IsNullOrWhiteSpace(template)) return groups;

            var seq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var ctx = BuildContext(group, test);
                string newName = ApplyNamingTemplate(template, ctx, seq);
                if (!string.IsNullOrEmpty(newName)) group.DisplayName = newName;
            }
            return groups;
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
                Level = "",
                Area = "",
                SelectionA = "",
                SelectionB = ""
            };

            // Selection A / B: prefer the names of the search sets that actually
            // contain the clashed elements in this group (so a clash between a
            // Window and a Structural Column is named "Window vs Structural Column",
            // not the whole selection list). Fall back to the full selection names
            // when membership can't be resolved, and finally to splitting TestName
            // on " vs ".
            ctx.SelectionA = ResolveClashedSetNames(test, group, true);
            ctx.SelectionB = ResolveClashedSetNames(test, group, false);
            if (string.IsNullOrEmpty(ctx.SelectionA)) ctx.SelectionA = ResolveSelectionNames(test, true);
            if (string.IsNullOrEmpty(ctx.SelectionB)) ctx.SelectionB = ResolveSelectionNames(test, false);
            if (string.IsNullOrEmpty(ctx.SelectionA) || string.IsNullOrEmpty(ctx.SelectionB))
            {
                if (!string.IsNullOrEmpty(ctx.TestName))
                {
                    int vs = ctx.TestName.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase);
                    if (vs > 0)
                    {
                        if (string.IsNullOrEmpty(ctx.SelectionA)) ctx.SelectionA = ctx.TestName.Substring(0, vs).Trim();
                        if (string.IsNullOrEmpty(ctx.SelectionB)) ctx.SelectionB = ctx.TestName.Substring(vs + 4).Trim();
                    }
                    else if (string.IsNullOrEmpty(ctx.SelectionA))
                    {
                        ctx.SelectionA = ctx.TestName;
                    }
                }
            }

            // Pull Level + Area from the first clash result in the group.
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
                            ctx.Area = string.IsNullOrEmpty(gi.DisplayName) ? "" : gi.DisplayName;
                            if (gi.Level != null && !string.IsNullOrEmpty(gi.Level.DisplayName))
                                ctx.Level = NormaliseLevel(gi.Level.DisplayName);
                        }
                    }
                }
                catch { /* best effort */ }

                // Fallback: try to read a "Room" property off either composite item.
                if (string.IsNullOrEmpty(ctx.Area))
                {
                    string room = TryGetRoomName(first.CompositeItem1) ?? TryGetRoomName(first.CompositeItem2);
                    if (!string.IsNullOrEmpty(room)) ctx.Area = room;
                }

                // Fallback: parse level from one of the clashed items' file name.
                if (string.IsNullOrEmpty(ctx.Level))
                {
                    string lvl = TryParseLevelFromFile(first.CompositeItem1) ?? TryParseLevelFromFile(first.CompositeItem2);
                    if (!string.IsNullOrEmpty(lvl)) ctx.Level = lvl;
                }
            }

            return ctx;
        }

        // Converts "Level 3", "L3", "L03", "Floor 03" to "L03"; "Basement 1" / "B1" to "B01";
        // returns the original string if no clean pattern matches.
        private static string NormaliseLevel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            string s = raw.Trim();

            // Pull the first numeric run; preserve the lead character if it's a
            // recognised level prefix (L, B, M, R, T, P).
            int firstDigit = -1;
            for (int i = 0; i < s.Length; i++) { if (char.IsDigit(s[i])) { firstDigit = i; break; } }
            if (firstDigit < 0) return s; // no number — return as-is

            int end = firstDigit;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            string numPart = s.Substring(firstDigit, end - firstDigit);
            int num;
            if (!int.TryParse(numPart, out num)) return s;
            string numFmt = num.ToString("D2");

            string lower = s.ToLowerInvariant();
            if (lower.Contains("base") || lower.StartsWith("b")) return "B" + numFmt;
            if (lower.Contains("roof")) return "R" + numFmt;
            if (lower.Contains("mezz")) return "M" + numFmt;
            if (lower.Contains("park")) return "P" + numFmt;
            if (lower.Contains("term")) return "T" + numFmt;
            // Default to "L" prefix for levels / floors / generic.
            return "L" + numFmt;
        }

        // Pulls "Lnn" / "Bnn" / etc. from a model file's name (e.g. UTUSB-ARCH-L03 → "L03").
        private static string TryParseLevelFromFile(ModelItem item)
        {
            if (item == null) return null;
            ModelItem fa = GetFileAncestor(item);
            if (fa == null) return null;
            string name = fa.DisplayName ?? "";
            // Try common level token shapes inside the filename.
            foreach (string sep in new[] { "-", "_", " ", "." })
            {
                foreach (string token in name.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = token.Trim();
                    if (t.Length >= 2 && t.Length <= 4)
                    {
                        char lead = char.ToUpperInvariant(t[0]);
                        if ("LBMRPT".IndexOf(lead) >= 0)
                        {
                            string rest = t.Substring(1);
                            int n;
                            if (int.TryParse(rest, out n)) return lead + n.ToString("D2");
                        }
                    }
                }
            }
            return null;
        }

        // Pulls "Room" property from a ModelItem if any property category exposes it.
        private static string TryGetRoomName(ModelItem item)
        {
            if (item == null) return null;
            try
            {
                foreach (var cat in item.PropertyCategories)
                {
                    foreach (var prop in cat.Properties)
                    {
                        string n = prop.DisplayName ?? "";
                        if (n.IndexOf("room", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string v = prop.Value?.ToDisplayString();
                            if (!string.IsNullOrWhiteSpace(v)) return v;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // Walks ClashTest.SelectionA / SelectionB's SelectionSources and resolves
        // each to its SavedItem (search set) DisplayName.  Empty string when
        // the selection holds nothing resolvable.
        private static string ResolveSelectionNames(ClashTest test, bool selectionA)
        {
            if (test == null) return "";
            try
            {
                var clashSel = selectionA ? test.SelectionA : test.SelectionB;
                if (clashSel == null) return "";
                var sel = clashSel.Selection;
                if (sel == null) return "";

                var names = new List<string>();
                var doc = Application.MainDocument;
                foreach (var src in sel.SelectionSources)
                {
                    try
                    {
                        var saved = doc?.SelectionSets?.ResolveSelectionSource(src);
                        if (saved != null && !string.IsNullOrWhiteSpace(saved.DisplayName))
                            names.Add(saved.DisplayName);
                    }
                    catch { }
                }
                return string.Join(" + ", names);
            }
            catch { return ""; }
        }

        // Reference-equality comparer so the membership cache keys on the exact
        // ClashTest instance (Navisworks may override Equals for value semantics).
        private sealed class RefComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly RefComparer<T> Instance = new RefComparer<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        // Per-(test, side) membership maps. Building one runs a full-model
        // Search.FindAll for every selection source, so without this cache a
        // naming/preview pass over N groups of one test would run 2·N full-model
        // searches. Index 0 = SelectionA, index 1 = SelectionB.
        private static readonly Dictionary<ClashTest, Dictionary<ModelItem, string>[]> _membershipCache
            = new Dictionary<ClashTest, Dictionary<ModelItem, string>[]>(RefComparer<ClashTest>.Instance);

        // Clears memoized naming lookups. Call once at the start of each user
        // operation (group, rename, or preview rebuild) so results reflect the
        // current search sets while staying fast within that operation.
        public static void ResetNamingCaches()
        {
            _membershipCache.Clear();
        }

        // Cached front for ComputeSelectionMembershipMap.
        private static Dictionary<ModelItem, string> BuildSelectionMembershipMap(ClashTest test, bool selectionA)
        {
            if (test == null) return new Dictionary<ModelItem, string>();

            int idx = selectionA ? 0 : 1;
            if (!_membershipCache.TryGetValue(test, out var arr))
            {
                arr = new Dictionary<ModelItem, string>[2];
                _membershipCache[test] = arr;
            }
            if (arr[idx] != null) return arr[idx];

            var built = ComputeSelectionMembershipMap(test, selectionA);
            arr[idx] = built;
            return built;
        }

        // Resolves search-set membership for the given side (A or B) into a map of
        // ModelItem → search-set DisplayName (including walking ancestors so the
        // composite items returned by ClashResult.CompositeItem1/2 still match
        // their parent's containing set). Empty map on failure.
        private static Dictionary<ModelItem, string> ComputeSelectionMembershipMap(ClashTest test, bool selectionA)
        {
            var map = new Dictionary<ModelItem, string>();
            if (test == null) return map;
            try
            {
                var clashSel = selectionA ? test.SelectionA : test.SelectionB;
                if (clashSel == null) return map;
                var sel = clashSel.Selection;
                if (sel == null) return map;
                var doc = Application.MainDocument;
                if (doc == null) return map;

                foreach (var src in sel.SelectionSources)
                {
                    string name;
                    ModelItemCollection items = null;
                    try
                    {
                        var saved = doc.SelectionSets.ResolveSelectionSource(src);
                        if (saved == null) continue;
                        name = saved.DisplayName;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var ss = saved as SelectionSet;
                        if (ss != null)
                        {
                            if (ss.HasSearch)
                            {
                                try { items = ss.Search.FindAll(doc, false); } catch { }
                            }
                            if (items == null)
                            {
                                // Document-resolving overload handles both explicit and
                                // search-based selection sets.
                                try { items = ss.GetSelectedItems(doc); } catch { }
                            }
                            if (items == null)
                            {
                                try { items = ss.GetSelectedItems(); } catch { }
                            }
                        }
                    }
                    catch { continue; }

                    if (items == null) continue;
                    foreach (ModelItem mi in items)
                    {
                        if (mi != null && !map.ContainsKey(mi)) map[mi] = name;
                    }
                }
            }
            catch { }
            return map;
        }

        // Resolves an item to its containing search-set name by looking up the item
        // itself, then walking ancestors (composite items returned by ClashResult
        // typically aren't the same instance as the leaves the search sets return).
        private static string LookupSetName(ModelItem item, Dictionary<ModelItem, string> map)
        {
            if (item == null || map == null || map.Count == 0) return null;
            if (map.TryGetValue(item, out var n)) return n;
            try
            {
                foreach (ModelItem anc in item.Ancestors)
                {
                    if (anc != null && map.TryGetValue(anc, out var an)) return an;
                }
            }
            catch { }
            return null;
        }

        // For the side requested, walks every ClashResult under this group and
        // collects the unique names of search sets that the clashed element on
        // that side belongs to. Returns " + "-joined names, or "" if nothing
        // could be resolved (caller should fall back to the full selection list).
        private static string ResolveClashedSetNames(ClashTest test, ClashResultGroup group, bool selectionA)
        {
            if (test == null || group == null) return "";
            var map = BuildSelectionMembershipMap(test, selectionA);
            if (map.Count == 0) return "";

            var hits = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectClashedSetNames(group, map, selectionA, hits, seen);
            return string.Join(" + ", hits);
        }

        private static void CollectClashedSetNames(
            object container,
            Dictionary<ModelItem, string> map,
            bool selectionA,
            List<string> hits,
            HashSet<string> seen)
        {
            var grp = container as ClashResultGroup;
            if (grp == null) return;
            foreach (var child in grp.Children)
            {
                var cr = child as ClashResult;
                if (cr != null)
                {
                    ModelItem item = null;
                    try { item = selectionA ? cr.CompositeItem1 : cr.CompositeItem2; } catch { }
                    var name = LookupSetName(item, map);
                    if (!string.IsNullOrEmpty(name) && seen.Add(name)) hits.Add(name);
                    continue;
                }
                var nested = child as ClashResultGroup;
                if (nested != null) CollectClashedSetNames(nested, map, selectionA, hits, seen);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Rename existing groups using the template
        // ─────────────────────────────────────────────────────────────

        // Applies the template to the supplied groups (typically the ones the
        // user has selected in Clash Detective).  Renames in place via the
        // ClashTest's TestsEditDisplayName transaction so the change is visible
        // in Clash Detective without a full rebuild.  Returns the number of
        // groups renamed.
        public static int RenameGroupsWithTemplate(List<ClashResultGroup> groups, ClashTest test, string template)
        {
            if (groups == null || groups.Count == 0 || string.IsNullOrWhiteSpace(template) || test == null)
                return 0;

            var doc = Application.MainDocument;
            if (doc == null) return 0;
            var dct = doc.GetClash().TestsData;

            // One membership build for this test, reused across all its groups.
            ResetNamingCaches();

            var seq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int renamed = 0;
            foreach (var g in groups)
            {
                if (g == null) continue;
                var ctx = BuildContext(g, test);
                string newName = ApplyNamingTemplate(template, ctx, seq);
                if (string.IsNullOrEmpty(newName)) continue;
                try
                {
                    dct.TestsEditDisplayName(g, newName);
                    renamed++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoNAV] RenameGroupsWithTemplate '{g.DisplayName}' failed: {ex.Message}");
                }
            }
            return renamed;
        }

        // For each top-level clash test, returns its currently-selected
        // descendant ClashResultGroup instances.  Uses Navisworks' active
        // document selection.
        public static List<KeyValuePair<ClashTest, ClashResultGroup>> GetSelectedClashGroups()
        {
            var pairs = new List<KeyValuePair<ClashTest, ClashResultGroup>>();
            var doc = Application.MainDocument;
            if (doc == null) return pairs;
            DocumentClash docClash = doc.GetClash();
            if (docClash == null || docClash.TestsData == null) return pairs;

            // Collect every (ClashTest, ClashResultGroup) currently in the
            // document so we can match them against the active selection.
            foreach (ClashTest test in ClashCompat.EnumerateTests(docClash.TestsData))
            {
                foreach (var child in test.Children)
                {
                    if (child is ClashResultGroup crg) pairs.Add(new KeyValuePair<ClashTest, ClashResultGroup>(test, crg));
                }
            }

            // Try to filter to "selected" via ActiveSelection.SelectedItems — these
            // are model items, not clash-tree items, so the API doesn't expose
            // "which clash groups the user clicked".  We return ALL groups; the
            // UI surface should let the user multi-select from a checkbox list.
            return pairs;
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
