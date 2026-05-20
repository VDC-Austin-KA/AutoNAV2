using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NavApp = Autodesk.Navisworks.Api.Application;
using NavGroupItem = Autodesk.Navisworks.Api.GroupItem;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace AutoNAV
{
    public class ClashResultGrouper
    {
        private const string CLASH_SETS_FOLDER = "2. CLASH SETS";

        public class SearchSetInfo
        {
            public string Name       { get; set; }
            public string Discipline { get; set; }
        }

        /// <summary>
        /// Returns unique search set names from "2. CLASH SETS" with their disciplines.
        /// </summary>
        public List<SearchSetInfo> GetAvailableSearchSets()
        {
            var result = new List<SearchSetInfo>();
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null) return result;

                var clashFolder = FindTopLevelFolder(doc, CLASH_SETS_FOLDER) as NavGroupItem;
                if (clashFolder == null) return result;

                foreach (SavedItem discItem in clashFolder.Children)
                {
                    if (!(discItem is NavGroupItem discFolder)) continue;
                    string discName = discItem.DisplayName.Trim();

                    foreach (SavedItem setItem in discFolder.Children)
                    {
                        string name = setItem.DisplayName.Trim();
                        if (!string.IsNullOrEmpty(name))
                            result.Add(new SearchSetInfo { Name = name, Discipline = discName });
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Groups individual clash results inside each test by the selected search sets.
        /// Returns a formatted text report.
        /// </summary>
        public string GroupClashResults(List<string> selectedSearchSetNames, bool requireBothSides)
        {
            var sb = new StringBuilder();

            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null) return "No active document.";

                DocumentClash docClash = doc.GetClash();
                if (docClash?.TestsData?.Tests == null)
                    return "Clash module not available or no tests exist.";

                // Step 1: For each selected search set, run its Search to get matching ModelItems
                var searchSetItems = BuildSearchSetItemMap(doc, selectedSearchSetNames);

                sb.AppendLine($"Clash Results Grouped by Search Set");
                sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine(new string('=', 56));

                if (searchSetItems.Count == 0)
                {
                    sb.AppendLine("\nNo items matched the selected search sets.");
                    sb.AppendLine("Ensure clash tests have been run and search sets have valid conditions.");
                    return sb.ToString();
                }

                sb.AppendLine($"\nSearch sets resolved: {searchSetItems.Count}");
                foreach (var kv in searchSetItems.OrderBy(k => k.Key))
                    sb.AppendLine($"  [{kv.Key}] {kv.Value.Count} item(s)");

                sb.AppendLine();

                // Step 2: Iterate each clash test and its results
                int testCount = 0;
                int totalGrouped = 0;
                int totalResults = 0;

                foreach (ClashTest test in docClash.TestsData.Tests)
                {
                    testCount++;
                    string testName = test.DisplayName.Trim();

                    // Collect all ClashResult objects from this test
                    var clashResults = new List<ClashResult>();
                    try
                    {
                        CollectClashResults(test, clashResults);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"{testName}:");
                        sb.AppendLine($"  Error reading results: {ex.Message}");
                        sb.AppendLine();
                        continue;
                    }

                    if (clashResults.Count == 0)
                    {
                        sb.AppendLine($"{testName}: (no results — test may not have been run)");
                        sb.AppendLine();
                        continue;
                    }

                    totalResults += clashResults.Count;

                    // Step 3: For each result, determine which search set(s) its items belong to
                    var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    var unmatched = new List<string>();

                    foreach (ClashResult cr in clashResults)
                    {
                        string resultLabel = cr.DisplayName ?? ("Clash #" + (clashResults.IndexOf(cr) + 1));
                        string matchedSet = null;

                        try
                        {
                            matchedSet = IdentifySearchSet(cr, searchSetItems, requireBothSides);
                        }
                        catch { }

                        if (matchedSet != null)
                        {
                            if (!groups.ContainsKey(matchedSet))
                                groups[matchedSet] = new List<string>();
                            groups[matchedSet].Add(resultLabel);
                            totalGrouped++;
                        }
                        else
                        {
                            unmatched.Add(resultLabel);
                        }
                    }

                    sb.AppendLine($"{testName}  ({clashResults.Count} result(s)):");

                    foreach (var g in groups.OrderByDescending(k => k.Value.Count))
                    {
                        sb.AppendLine($"  [{g.Key}]  {g.Value.Count} clash(es)");
                    }

                    if (unmatched.Count > 0)
                        sb.AppendLine($"  [Other]  {unmatched.Count} clash(es)");

                    sb.AppendLine();
                }

                sb.AppendLine(new string('-', 56));
                sb.AppendLine($"Tests analyzed: {testCount}");
                sb.AppendLine($"Total results: {totalResults}");
                sb.AppendLine($"Grouped: {totalGrouped}  |  Unmatched: {totalResults - totalGrouped}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"\nFatal error: {ex.Message}\n{ex.StackTrace}");
            }

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Executes each selected search set's Search and builds a map
        /// of searchSetName → set of matching ModelItems.
        /// </summary>
        private Dictionary<string, HashSet<ModelItem>> BuildSearchSetItemMap(
            Document doc, List<string> selectedNames)
        {
            var result = new Dictionary<string, HashSet<ModelItem>>(StringComparer.OrdinalIgnoreCase);

            var clashFolder = FindTopLevelFolder(doc, CLASH_SETS_FOLDER) as NavGroupItem;
            if (clashFolder == null) return result;

            foreach (SavedItem discItem in clashFolder.Children)
            {
                if (!(discItem is NavGroupItem discGroup)) continue;

                foreach (SavedItem setItem in discGroup.Children)
                {
                    string setName = setItem.DisplayName.Trim();

                    if (!selectedNames.Any(s => s.Equals(setName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (!(setItem is SelectionSet ss)) continue;
                    if (!ss.HasSearch) continue;

                    try
                    {
                        ModelItemCollection items = ss.Search.FindAll(doc, false);
                        if (items == null || items.Count == 0) continue;

                        if (!result.ContainsKey(setName))
                            result[setName] = new HashSet<ModelItem>();

                        foreach (ModelItem mi in items)
                            result[setName].Add(mi);
                    }
                    catch { }
                }
            }

            return result;
        }

        /// <summary>
        /// Recursively collects all ClashResult objects from a test or group.
        /// </summary>
        private void CollectClashResults(SavedItem parent, List<ClashResult> results)
        {
            if (parent is ClashResult cr)
            {
                results.Add(cr);
                return;
            }

            // ClashTest and ClashResultGroup both extend GroupItem — iterate children
            if (parent is NavGroupItem group)
            {
                foreach (SavedItem child in group.Children)
                    CollectClashResults(child, results);
            }
        }

        /// <summary>
        /// Determines which search set a clash result belongs to by checking
        /// whether its items are in any of the search set match collections.
        /// </summary>
        private string IdentifySearchSet(
            ClashResult cr,
            Dictionary<string, HashSet<ModelItem>> searchSetItems,
            bool requireBothSides)
        {
            ModelItem item1 = null;
            ModelItem item2 = null;

            try { item1 = cr.CompositeItem1; } catch { }
            try { item2 = cr.CompositeItem2; } catch { }

            if (item1 == null && item2 == null) return null;

            foreach (var entry in searchSetItems)
            {
                bool m1 = item1 != null && IsItemInSet(item1, entry.Value);
                bool m2 = item2 != null && IsItemInSet(item2, entry.Value);

                if (requireBothSides)
                {
                    if (m1 && m2) return entry.Key;
                }
                else
                {
                    if (m1 || m2) return entry.Key;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a ModelItem (or any of its ancestors) is in the given set.
        /// This handles the case where the search matched a parent node but
        /// the clash result references a descendant.
        /// </summary>
        private bool IsItemInSet(ModelItem item, HashSet<ModelItem> itemSet)
        {
            // Direct check first
            if (itemSet.Contains(item)) return true;

            // Walk up ancestors — the search set may match at a higher level
            foreach (ModelItem ancestor in item.Ancestors)
            {
                if (itemSet.Contains(ancestor)) return true;
            }

            return false;
        }

        private SavedItem FindTopLevelFolder(Document doc, string name)
        {
            NavGroupItem root = doc.SelectionSets.RootItem as NavGroupItem;
            if (root == null) return null;

            foreach (SavedItem item in root.Children)
            {
                if (item.DisplayName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)
                    && item is NavGroupItem)
                    return item;
            }
            return null;
        }
    }
}
