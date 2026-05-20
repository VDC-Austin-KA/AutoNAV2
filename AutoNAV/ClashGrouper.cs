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

        public static void GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups)
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

                Dictionary<string, HashSet<ModelItem>> setMap =
                    BuildWallsFloorsSearchSetMap(doc);

                if (setMap.Count == 0)
                    return "No 'Walls' or 'Floors' search sets found in '" + CLASH_SETS_FOLDER + "'.\n\n" +
                           "Run Functions 1–3 first to create the required search sets.";

                int testsProcessed = 0;
                int testsSkipped   = 0;
                int wallsTotal     = 0;
                int floorsTotal    = 0;
                int otherTotal     = 0;
                var errorLog       = new List<string>();

                foreach (ClashTest test in documentClash.TestsData.Tests)
                {
                    try
                    {
                        List<ClashResult> all =
                            GetIndividualClashResults(test, false).ToList();

                        if (all.Count == 0) { testsSkipped++; continue; }

                        GroupByWallsAndFloorsViaSearchSets(
                            all, setMap,
                            out List<ClashResultGroup> groups,
                            out List<ClashResult> ungrouped);

                        int w = 0, f = 0;
                        foreach (var g in groups)
                        {
                            if (g.DisplayName.StartsWith("Walls",  StringComparison.OrdinalIgnoreCase)) w += g.Children.Count;
                            if (g.DisplayName.StartsWith("Floors", StringComparison.OrdinalIgnoreCase)) f += g.Children.Count;
                        }

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

                string summary =
                    string.Format(
                        "Function 6 — Walls / Floors Grouping Complete\n\n" +
                        "Tests processed : {0}\n" +
                        "Tests skipped   : {1}  (no results)\n\n" +
                        "Grouped as Walls  : {2}\n" +
                        "Grouped as Floors : {3}\n" +
                        "Left ungrouped    : {4}  (ready for Sherlock Distill)\n",
                        testsProcessed, testsSkipped,
                        wallsTotal, floorsTotal, otherTotal);

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
                string prop = mode == GroupingMode.ApprovedBy ? copy.ApprovedBy
                            : mode == GroupingMode.AssignedTo ? copy.AssignedTo
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

        private static void ProcessClashGroup(
            List<ClashResultGroup> clashGroups,
            List<ClashResult> ungroupedClashResults,
            ClashTest selectedClashTest)
        {
            Transaction tx = null;
            Progress progressBar = null;
            try
            {
                tx = Application.MainDocument.BeginTransaction("Group clashes");

                ClashTest copiedTest   = (ClashTest)selectedClashTest.CreateCopyWithoutChildren();
                ClashTest backupTest   = (ClashTest)selectedClashTest.CreateCopy();
                DocumentClash docClash = Application.MainDocument.GetClash();
                int idx                = docClash.TestsData.Tests.IndexOf(selectedClashTest);

                docClash.TestsData.TestsReplaceWithCopy(idx, copiedTest);

                int done  = 0;
                int total = ungroupedClashResults.Count + clashGroups.Count;
                progressBar = Application.BeginProgress("Grouping Clashes", "Processing...");

                foreach (ClashResultGroup g in clashGroups)
                {
                    if (progressBar.IsCanceled) break;
                    docClash.TestsData.TestsAddCopy((GroupItem)docClash.TestsData.Tests[idx], g);
                    progressBar.Update((double)++done / total);
                }

                foreach (ClashResult cr in ungroupedClashResults)
                {
                    if (progressBar.IsCanceled) break;
                    docClash.TestsData.TestsAddCopy((GroupItem)docClash.TestsData.Tests[idx], cr);
                    progressBar.Update((double)++done / total);
                }

                if (progressBar.IsCanceled)
                    docClash.TestsData.TestsReplaceWithCopy(idx, backupTest);

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
