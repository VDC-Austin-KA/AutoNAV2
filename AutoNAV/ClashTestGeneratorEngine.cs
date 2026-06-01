using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.DocumentParts;

namespace AutoNAV
{
    public class ClashTestGeneratorEngine
    {
        private const string DISCIPLINES_FOLDER = "1. DISCIPLINES";
        private const string CLASH_SETS_FOLDER = "2. CLASH SETS";
        private static readonly string[] PRECURSOR_NAMES = { "Floors", "Walls" };
        private StringBuilder _executionLog;

        // Triggers Navisworks' "Update All" on every clash test. Returns the
        // number of tests executed, or -1 if Clash Detective wasn't available.
        // Throws on hard errors so callers (AutoNAVismate) can decide whether
        // to prompt the user to run tests manually.
        public static int RunAllClashTests()
        {
            Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return -1;
            DocumentClash documentClash = doc.GetClash();
            if (documentClash == null || documentClash.TestsData == null) return -1;
            documentClash.TestsData.TestsRunAllTests();
            return ClashCompat.GetTopLevelTests(documentClash.TestsData).OfType<ClashTest>().Count();
        }

        // Function 4 - Standard Clash Test Generation
        public void GenerateClashTests()
        {
            _executionLog = new StringBuilder();
            LogMessage(string.Format("[{0:HH:mm:ss}] Clash test generation started", DateTime.Now));

            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null)
                {
                    ShowError("No active document found");
                    return;
                }

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    ShowError("Clash Detective is not available in this document");
                    return;
                }

                LogMessage("Validating document and search sets...");

                SavedItem disciplinesFolder = FindDisciplineFolder(doc);
                if (disciplinesFolder == null)
                {
                    ShowError(string.Format("Folder '{0}' not found in search sets\n\n" +
                        "Please complete Functions 1-3 first to create the required folder structure.", DISCIPLINES_FOLDER));
                    return;
                }

                SavedItem clashSetsFolder = FindClashSetsFolder(doc);
                if (clashSetsFolder == null)
                {
                    ShowError(string.Format("Folder '{0}' not found in search sets\n\n" +
                        "Please complete Functions 1-3 first to create the required folder structure.", CLASH_SETS_FOLDER));
                    return;
                }

                LogMessage("Extracting discipline information...");

                List<string> disciplines = GetDisciplinesFromFolder(disciplinesFolder);
                if (disciplines.Count < 2)
                {
                    ShowError("At least 2 disciplines required to generate tests.\n\n" +
                        "Please ensure '1. DISCIPLINES' contains at least 2 discipline search sets.");
                    return;
                }

                LogMessage(string.Format("Found {0} disciplines: {1}", disciplines.Count, string.Join(", ", disciplines)));

                // Sort disciplines into a "SelectionA priority" list so Structural /
                // Architectural land on the left side of the clash tests they're
                // involved in.  Identified disciplines (canonical assigned in
                // SearchSetGenerator.DisciplineRegistry) win over name-pattern
                // matches, and Structural beats Architectural at every tier.
                List<string> prioritySorted = SortDisciplinesByPriority(disciplines);
                List<string> alphabetical = disciplines.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

                LogMessage("Selection-A priority order: " + string.Join(", ", prioritySorted));

                Dictionary<string, SavedItem> clashDisciplineFolders = BuildDisciplineFolderMap(clashSetsFolder);

                int createdCount = 0;
                int skippedCount = 0;
                int failedCount = 0;

                LogMessage("Generating clash test combinations...");

                // For each SelectionA in priority order, pair with every other
                // discipline in alphabetical order.  A processed-pair set keeps
                // every (A, B) combo from being emitted twice in either direction.
                var processedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string leftName in prioritySorted)
                {
                    foreach (string rightName in alphabetical)
                    {
                        if (string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase)) continue;

                        // Order-independent pair key (alphabetical) so (A,B) and
                        // (B,A) collapse to the same entry.
                        string a = leftName, b = rightName;
                        if (string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0) { var t = a; a = b; b = t; }
                        string pairKey = a + "||" + b;
                        if (!processedPairs.Add(pairKey)) continue;

                        if (!clashDisciplineFolders.ContainsKey(leftName))
                        {
                            LogMessage(string.Format("Skipping: '{0}' folder not found in {1}", leftName, CLASH_SETS_FOLDER));
                            continue;
                        }
                        if (!clashDisciplineFolders.ContainsKey(rightName))
                        {
                            LogMessage(string.Format("Skipping: '{0}' folder not found in {1}", rightName, CLASH_SETS_FOLDER));
                            continue;
                        }

                        string testName = string.Format("{0} vs {1}", leftName, rightName);

                        if (TestExists(documentClash, testName))
                        {
                            LogMessage(string.Format("Skipped (exists): {0}", testName));
                            skippedCount++;
                            continue;
                        }

                        try
                        {
                            SavedItem leftFolder = clashDisciplineFolders[leftName];
                            SavedItem rightFolder = clashDisciplineFolders[rightName];

                            ClashTest test = CreateClashTestFromDisciplineFolders(
                                doc,
                                testName,
                                leftFolder,
                                rightFolder);

                            if (test != null)
                            {
                                ClashCompat.TestsAddCopyAtRoot(documentClash.TestsData, test);
                                LogMessage(string.Format("Created: {0}", testName));
                                createdCount++;
                            }
                            else
                            {
                                LogMessage(string.Format("Failed: {0} (no search sets found)", testName));
                                failedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage(string.Format("Error creating {0}: {1}", testName, ex.Message));
                            failedCount++;
                        }
                    }
                }

                LogMessage(string.Format("[{0:HH:mm:ss}] Generation complete", DateTime.Now));
                LogMessage(string.Format("Summary: Created={0}, Skipped={1}, Failed={2}", createdCount, skippedCount, failedCount));

                // Auto-run all clash tests so the user doesn't have to click
                // "Update All" in Clash Detective afterwards.
                int runCount = 0, runFailed = 0;
                try
                {
                    LogMessage(string.Format("[{0:HH:mm:ss}] Running all clash tests...", DateTime.Now));
                    documentClash.TestsData.TestsRunAllTests();
                    runCount = ClashCompat.GetTopLevelTests(documentClash.TestsData).OfType<ClashTest>().Count();
                    LogMessage(string.Format("[{0:HH:mm:ss}] Run complete: {1} test(s) executed", DateTime.Now, runCount));
                }
                catch (Exception runEx)
                {
                    runFailed = 1;
                    LogMessage("Run-all failed: " + runEx.Message);
                }

                string summary = string.Format(
                    "Clash Test Generation Complete\n\n" +
                    "Created: {0}\nSkipped: {1}\nFailed: {2}\n\n" +
                    "Total: {3}\n\n" +
                    (runFailed == 0
                        ? "All {4} test(s) were executed. Results are ready for grouping in Functions 5 / 6."
                        : "Auto-run failed — open Clash Detective and click \"Update All\" manually."),
                    createdCount, skippedCount, failedCount, createdCount + skippedCount + failedCount, runCount);

                Notifier.Info(summary);
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("FATAL ERROR: {0}", ex.Message));
                LogMessage(string.Format("Stack Trace: {0}", ex.StackTrace));
                ShowError(string.Format("Unexpected error:\n\n{0}", ex.Message));
            }
        }

        // Function 5 - Clash Test Generation with Precursor Grouping (Floors/Walls)
        public void GenerateClashTestsWithPrecursor()
        {
            _executionLog = new StringBuilder();
            LogMessage(string.Format("[{0:HH:mm:ss}] Clash test generation (with precursor grouping) started", DateTime.Now));

            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null)
                {
                    ShowError("No active document found");
                    return;
                }

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    ShowError("Clash Detective is not available in this document");
                    return;
                }

                LogMessage("Validating document and search sets...");

                SavedItem disciplinesFolder = FindDisciplineFolder(doc);
                if (disciplinesFolder == null)
                {
                    ShowError(string.Format("Folder '{0}' not found in search sets\n\n" +
                        "Please complete Functions 1-3 first to create the required folder structure.", DISCIPLINES_FOLDER));
                    return;
                }

                SavedItem clashSetsFolder = FindClashSetsFolder(doc);
                if (clashSetsFolder == null)
                {
                    ShowError(string.Format("Folder '{0}' not found in search sets\n\n" +
                        "Please complete Functions 1-3 first to create the required folder structure.", CLASH_SETS_FOLDER));
                    return;
                }

                LogMessage("Extracting discipline information...");

                List<string> disciplines = GetDisciplinesFromFolder(disciplinesFolder);
                if (disciplines.Count < 2)
                {
                    ShowError("At least 2 disciplines required to generate tests.\n\n" +
                        "Please ensure '1. DISCIPLINES' contains at least 2 discipline search sets.");
                    return;
                }

                LogMessage(string.Format("Found {0} disciplines: {1}", disciplines.Count, string.Join(", ", disciplines)));

                Dictionary<string, SavedItem> clashDisciplineFolders = BuildDisciplineFolderMap(clashSetsFolder);

                int createdCount = 0;
                int skippedCount = 0;
                int failedCount = 0;
                int groupedCount = 0;

                LogMessage("Generating clash test combinations...");
                LogMessage("Checking for Floors/Walls precursor grouping...");

                Dictionary<string, List<string>> disciplineSearchSets = BuildSearchSetNamesMap(clashDisciplineFolders);

                for (int i = 0; i < disciplines.Count; i++)
                {
                    for (int j = i + 1; j < disciplines.Count; j++)
                    {
                        string leftName = disciplines[i];
                        string rightName = disciplines[j];

                        if (!clashDisciplineFolders.ContainsKey(leftName))
                        {
                            LogMessage(string.Format("Skipping: '{0}' folder not found in {1}", leftName, CLASH_SETS_FOLDER));
                            continue;
                        }

                        if (!clashDisciplineFolders.ContainsKey(rightName))
                        {
                            LogMessage(string.Format("Skipping: '{0}' folder not found in {1}", rightName, CLASH_SETS_FOLDER));
                            continue;
                        }

                        SavedItem leftFolder = clashDisciplineFolders[leftName];
                        SavedItem rightFolder = clashDisciplineFolders[rightName];

                        string leftPrecursor = FindPrecursorSearchSet(disciplineSearchSets, leftName);
                        string rightPrecursor = FindPrecursorSearchSet(disciplineSearchSets, rightName);

                        bool hasPrecursor = !string.IsNullOrEmpty(leftPrecursor) || !string.IsNullOrEmpty(rightPrecursor);

                        if (hasPrecursor)
                        {
                            createdCount += CreatePrecursorGroupedTests(
                                doc, documentClash, leftName, rightName,
                                leftFolder, rightFolder,
                                leftPrecursor, rightPrecursor,
                                disciplineSearchSets, disciplineSearchSets,
                                ref skippedCount, ref failedCount, ref groupedCount);
                        }
                        else
                        {
                            string testName = string.Format("{0} vs {1}", leftName, rightName);

                            if (TestExists(documentClash, testName))
                            {
                                LogMessage(string.Format("Skipped (exists): {0}", testName));
                                skippedCount++;
                                continue;
                            }

                            try
                            {
                                ClashTest test = CreateClashTestFromDisciplineFolders(
                                    doc,
                                    testName,
                                    leftFolder,
                                    rightFolder);

                                if (test != null)
                                {
                                    ClashCompat.TestsAddCopyAtRoot(documentClash.TestsData, test);
                                    LogMessage(string.Format("Created: {0}", testName));
                                    createdCount++;
                                }
                                else
                                {
                                    LogMessage(string.Format("Failed: {0} (no search sets found)", testName));
                                    failedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage(string.Format("Error creating {0}: {1}", testName, ex.Message));
                                failedCount++;
                            }
                        }
                    }
                }

                LogMessage(string.Format("[{0:HH:mm:ss}] Generation complete", DateTime.Now));
                LogMessage(string.Format("Summary: Created={0}, Skipped={1}, Failed={2}, Grouped={3}", createdCount, skippedCount, failedCount, groupedCount));

                string summary = string.Format("Clash Test Generation (Precursor Grouping) Complete\n\nCreated: {0}\nSkipped: {1}\nFailed: {2}\nGrouped (Floors/Walls): {3}\n\nTotal: {4}\n\nYour clash tests are now ready for execution in Clash Detective.",
                    createdCount, skippedCount, failedCount, groupedCount, createdCount + skippedCount + failedCount);

                Notifier.Info(summary);
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("FATAL ERROR: {0}", ex.Message));
                LogMessage(string.Format("Stack Trace: {0}", ex.StackTrace));
                ShowError(string.Format("Unexpected error:\n\n{0}", ex.Message));
            }
        }

        // Function 5 - Run Clash Tests and Group Results by Floors/Walls
        public void RunClashTestsAndGroupResults()
        {
            _executionLog = new StringBuilder();
            LogMessage(string.Format("[{0:HH:mm:ss}] Running clash tests and grouping by Floors/Walls", DateTime.Now));

            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null)
                {
                    ShowError("No active document found");
                    return;
                }

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    ShowError("Clash Detective is not available in this document");
                    return;
                }

                var tests = ClashCompat.GetTopLevelTests(documentClash.TestsData);
                if (tests.Count == 0)
                {
                    ShowError("No clash tests found.\n\nPlease run Function 4 first.");
                    return;
                }

                // Check for Floors and Walls search sets
                SavedItem clashSetsFolder = FindClashSetsFolder(doc);
                if (clashSetsFolder == null)
                {
                    ShowError(string.Format("Folder '{0}' not found.", CLASH_SETS_FOLDER));
                    return;
                }

                // Build map of search set name to SelectionSet
                var searchSetMap = new Dictionary<string, SelectionSet>(StringComparer.OrdinalIgnoreCase);
                
                GroupItem clashGroup = clashSetsFolder as GroupItem;
                if (clashGroup != null)
                {
                    foreach (SavedItem discItem in clashGroup.Children)
                    {
                        if (discItem is GroupItem discFolder)
                        {
                            foreach (SavedItem setItem in discFolder.Children)
                            {
                                if (setItem is SelectionSet ss)
                                {
                                    string setName = ss.DisplayName.Trim();
                                    if (setName.Equals("Floors", StringComparison.OrdinalIgnoreCase) ||
                                        setName.Equals("Walls", StringComparison.OrdinalIgnoreCase))
                                    {
                                        searchSetMap[setName] = ss;
                                    }
                                }
                            }
                        }
                    }
                }

                if (searchSetMap.Count == 0)
                {
                    ShowError("No 'Floors' or 'Walls' search sets found in " + CLASH_SETS_FOLDER + ".\n\nPlease run Function 2 or 3 first.");
                    return;
                }

                // Build search set to matching items map
                var searchSetItems = new Dictionary<string, HashSet<ModelItem>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in searchSetMap)
                {
                    SelectionSet ss = kvp.Value;
                    if (ss.HasSearch)
                    {
                        try
                        {
                            var items = ss.Search.FindAll(doc, false);
                            if (items != null && items.Count > 0)
                            {
                                searchSetItems[kvp.Key] = new HashSet<ModelItem>(items);
                            }
                        }
                        catch { }
                    }
                }

                int totalGrouped = 0;
                int totalTests = 0;
                var floorsCount = 0;
                var wallsCount = 0;

                // Process each test
                foreach (ClashTest test in tests)
                {
                    totalTests++;

                    // Get all clash results
                    var clashResults = new List<ClashResult>();
                    CollectClashResults(test, clashResults);

                    if (clashResults.Count == 0)
                        continue;

                    // Group results by Floors or Walls
                    var floorsResults = new List<ClashResult>();
                    var wallsResults = new List<ClashResult>();

                    foreach (ClashResult cr in clashResults)
                    {
                        ModelItem item1 = null;
                        ModelItem item2 = null;

                        try { item1 = cr.CompositeItem1; } catch { }
                        try { item2 = cr.CompositeItem2; } catch { }

                        bool isFloors = false;
                        bool isWalls = false;

                        // Check if items match Floors or Walls search sets
                        if (searchSetItems.ContainsKey("Floors"))
                        {
                            if (item1 != null && IsItemInSet(item1, searchSetItems["Floors"])) isFloors = true;
                            if (item2 != null && IsItemInSet(item2, searchSetItems["Floors"])) isFloors = true;
                        }
                        if (searchSetItems.ContainsKey("Walls"))
                        {
                            if (item1 != null && IsItemInSet(item1, searchSetItems["Walls"])) isWalls = true;
                            if (item2 != null && IsItemInSet(item2, searchSetItems["Walls"])) isWalls = true;
                        }

                        if (isFloors)
                            floorsResults.Add(cr);
                        else if (isWalls)
                            wallsResults.Add(cr);
                    }

                    // Create groups using the API
                    if (floorsResults.Count > 0)
                    {
                        floorsCount += floorsResults.Count;
                        
                        // Create a group for Floors
                        var floorsGroup = new ClashResultGroup();
                        floorsGroup.DisplayName = "Floors (" + floorsResults.Count + ")";
                        
                        // Add results to group
                        foreach (var cr in floorsResults)
                        {
                            try
                            {
                                floorsGroup.Children.Add(cr);
                            }
                            catch { }
                        }

                        // Add group to test using TestsAddCopy
                        try
                        {
                            documentClash.TestsData.TestsAddCopy(test, floorsGroup);
                            totalGrouped += floorsResults.Count;
                        }
                        catch { }
                    }

                    if (wallsResults.Count > 0)
                    {
                        wallsCount += wallsResults.Count;
                        
                        // Create a group for Walls
                        var wallsGroup = new ClashResultGroup();
                        wallsGroup.DisplayName = "Walls (" + wallsResults.Count + ")";
                        
                        // Add results to group
                        foreach (var cr in wallsResults)
                        {
                            try
                            {
                                wallsGroup.Children.Add(cr);
                            }
                            catch { }
                        }

                        // Add group to test using TestsAddCopy
                        try
                        {
                            documentClash.TestsData.TestsAddCopy(test, wallsGroup);
                            totalGrouped += wallsResults.Count;
                        }
                        catch { }
                    }
                }

                string summary = string.Format(
                    "Clash Results Grouped Successfully!\n\n" +
                    "Tests Processed: {0}\n" +
                    "Clashes Grouped as Floors: {1}\n" +
                    "Clashes Grouped as Walls: {2}\n" +
                    "Total Grouped: {3}\n\n" +
                    "Check Clash Detective to see the grouped results.",
                    totalTests, floorsCount, wallsCount, totalGrouped);

                Notifier.Info(summary);
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("FATAL ERROR: {0}", ex.Message));
                ShowError(string.Format("Error:\n\n{0}", ex.Message));
            }
        }

        private void CollectClashResults(SavedItem parent, List<ClashResult> results)
        {
            if (parent is ClashResult cr)
            {
                results.Add(cr);
                return;
            }

            if (parent is GroupItem group)
            {
                foreach (SavedItem child in group.Children)
                    CollectClashResults(child, results);
            }
        }

        private bool IsItemInSet(ModelItem item, HashSet<ModelItem> itemSet)
        {
            if (itemSet.Contains(item)) return true;
            try
            {
                foreach (ModelItem ancestor in item.Ancestors)
                {
                    if (itemSet.Contains(ancestor)) return true;
                }
            }
            catch { }
            return false;
        }

        private SavedItem FindDisciplineFolder(Document doc)
        {
            try
            {
                DocumentSelectionSets selSetsDoc = doc.SelectionSets;
                if (selSetsDoc == null || selSetsDoc.RootItem == null)
                    return null;

                return FindTopLevelFolder(selSetsDoc.RootItem, DISCIPLINES_FOLDER);
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error finding discipline folder: {0}", ex.Message));
                return null;
            }
        }

        private SavedItem FindClashSetsFolder(Document doc)
        {
            try
            {
                DocumentSelectionSets selSetsDoc = doc.SelectionSets;
                if (selSetsDoc == null || selSetsDoc.RootItem == null)
                    return null;

                return FindTopLevelFolder(selSetsDoc.RootItem, CLASH_SETS_FOLDER);
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error finding clash sets folder: {0}", ex.Message));
                return null;
            }
        }

        private SavedItem FindTopLevelFolder(SavedItem root, string name)
        {
            GroupItem group = root as GroupItem;
            if (group == null)
                return null;

            foreach (SavedItem child in group.Children)
            {
                if (child.IsGroup &&
                    child.DisplayName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        // Ranks each discipline name so user-identified Structural/Architectural
        // (canonical assigned in SearchSetGenerator.DisciplineRegistry) wind up
        // on the SelectionA side of every clash test they participate in.
        //   0 = identified Structural
        //   1 = identified Architectural
        //   2 = name-matches Structural but no canonical yet
        //   3 = name-matches Architectural but no canonical yet
        //   4 = everything else (alphabetical within)
        // Final order is rank asc, then alphabetical for ties.
        private static List<string> SortDisciplinesByPriority(IEnumerable<string> disciplines)
        {
            int Rank(string name)
            {
                if (SearchSetGenerator.DisciplineRegistry.TryGetValue(name, out string canonical) && !string.IsNullOrEmpty(canonical))
                {
                    if (canonical.Equals("Structural",    StringComparison.OrdinalIgnoreCase)) return 0;
                    if (canonical.Equals("Architectural", StringComparison.OrdinalIgnoreCase)) return 1;
                }
                if (SearchSetGenerator.TryMatchDiscipline(name, out _, out string dictCanonical))
                {
                    if (dictCanonical.Equals("Structural",    StringComparison.OrdinalIgnoreCase)) return 2;
                    if (dictCanonical.Equals("Architectural", StringComparison.OrdinalIgnoreCase)) return 3;
                }
                return 4;
            }
            return disciplines
                .OrderBy(Rank)
                .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> GetDisciplinesFromFolder(SavedItem disciplinesFolder)
        {
            List<string> result = new List<string>();

            try
            {
                GroupItem group = disciplinesFolder as GroupItem;
                if (group == null)
                    return result;

                foreach (SavedItem child in group.Children)
                {
                    if (child is SelectionSet)
                    {
                        result.Add(child.DisplayName.Trim());
                    }
                }

                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error extracting disciplines: {0}", ex.Message));
            }

            return result;
        }

        private Dictionary<string, SavedItem> BuildDisciplineFolderMap(SavedItem clashSetsFolder)
        {
            Dictionary<string, SavedItem> map = new Dictionary<string, SavedItem>(StringComparer.OrdinalIgnoreCase);

            try
            {
                GroupItem group = clashSetsFolder as GroupItem;
                if (group == null)
                    return map;

                foreach (SavedItem child in group.Children)
                {
                    if (child.IsGroup)
                    {
                        string key = child.DisplayName.Trim();
                        if (!map.ContainsKey(key))
                        {
                            map.Add(key, child);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error building discipline folder map: {0}", ex.Message));
            }

            return map;
        }

        private bool TestExists(DocumentClash documentClash, string testName)
        {
            try
            {
                foreach (ClashTest test in ClashCompat.EnumerateTests(documentClash.TestsData))
                {
                    if (test.DisplayName.Trim().Equals(testName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error checking test existence: {0}", ex.Message));
            }

            return false;
        }

        private ClashTest CreateClashTestFromDisciplineFolders(
            Document doc,
            string testName,
            SavedItem leftDisciplineFolder,
            SavedItem rightDisciplineFolder)
        {
            try
            {
                List<SelectionSource> leftSources = BuildSelectionSourcesFromFolder(doc, leftDisciplineFolder);
                List<SelectionSource> rightSources = BuildSelectionSourcesFromFolder(doc, rightDisciplineFolder);

                if (leftSources.Count == 0 || rightSources.Count == 0)
                {
                    LogMessage(string.Format("No search sets found for test: {0}", testName));
                    return null;
                }

                ClashTest clashTest = new ClashTest();
                clashTest.DisplayName = testName;
                clashTest.CustomTestName = testName;
                clashTest.TestType = ClashTestType.Hard;
                clashTest.Tolerance = 0.0;

                clashTest.SelectionA.SelfIntersect = false;
                clashTest.SelectionA.PrimitiveTypes = PrimitiveTypes.Triangles;
                foreach (SelectionSource source in leftSources)
                {
                    clashTest.SelectionA.Selection.SelectionSources.Add(source);
                }

                clashTest.SelectionB.SelfIntersect = false;
                clashTest.SelectionB.PrimitiveTypes = PrimitiveTypes.Triangles;
                foreach (SelectionSource source in rightSources)
                {
                    clashTest.SelectionB.Selection.SelectionSources.Add(source);
                }

                return clashTest;
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error creating clash test '{0}': {1}", testName, ex.Message));
                return null;
            }
        }

        private List<SelectionSource> BuildSelectionSourcesFromFolder(
            Document doc,
            SavedItem disciplineFolder)
        {
            List<SelectionSource> result = new List<SelectionSource>();

            try
            {
                GroupItem group = disciplineFolder as GroupItem;
                if (group == null)
                    return result;

                foreach (SavedItem child in group.Children)
                {
                    if (child is SelectionSet)
                    {
                        SelectionSet ss = child as SelectionSet;
                        SelectionSource source = doc.SelectionSets.CreateSelectionSource(ss);
                        result.Add(source);
                    }
                    else if (child.IsGroup)
                    {
                        result.AddRange(BuildSelectionSourcesFromFolder(doc, child));
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error building selection sources: {0}", ex.Message));
            }

            return result;
        }

        private void LogMessage(string message)
        {
            if (_executionLog != null)
                _executionLog.AppendLine(message);
        }

        private void ShowError(string message)
        {
            Notifier.Error(message);
            LogMessage(string.Format("ERROR: {0}", message));
        }

        private Dictionary<string, List<string>> BuildSearchSetNamesMap(Dictionary<string, SavedItem> clashDisciplineFolders)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in clashDisciplineFolders)
            {
                string disciplineName = kvp.Key;
                SavedItem folder = kvp.Value;
                List<string> searchSetNames = new List<string>();

                GroupItem group = folder as GroupItem;
                if (group != null)
                {
                    foreach (SavedItem child in group.Children)
                    {
                        if (child is SelectionSet)
                        {
                            SelectionSet ss = child as SelectionSet;
                            searchSetNames.Add(ss.DisplayName.Trim());
                        }
                    }
                }

                map[disciplineName] = searchSetNames;
            }

            return map;
        }

        private string FindPrecursorSearchSet(Dictionary<string, List<string>> searchSetMap, string disciplineName)
        {
            if (!searchSetMap.ContainsKey(disciplineName))
                return null;

            foreach (string precursor in PRECURSOR_NAMES)
            {
                foreach (string s in searchSetMap[disciplineName])
                {
                    if (s.Equals(precursor, StringComparison.OrdinalIgnoreCase))
                        return precursor;
                }
            }

            return null;
        }

        private int CreatePrecursorGroupedTests(
            Document doc,
            DocumentClash documentClash,
            string leftName,
            string rightName,
            SavedItem leftFolder,
            SavedItem rightFolder,
            string leftPrecursor,
            string rightPrecursor,
            Dictionary<string, List<string>> leftSearchSets,
            Dictionary<string, List<string>> rightSearchSets,
            ref int skippedCount,
            ref int failedCount,
            ref int groupedCount)
        {
            int created = 0;

            try
            {
                var leftSourcesAll = BuildSelectionSourcesFromFolder(doc, leftFolder);
                var rightSourcesAll = BuildSelectionSourcesFromFolder(doc, rightFolder);

                if (leftSourcesAll.Count == 0 || rightSourcesAll.Count == 0)
                {
                    LogMessage(string.Format("No search sets found for {0} vs {1}", leftName, rightName));
                    return 0;
                }

                var leftPrecursorSources = GetPrecursorSources(doc, leftFolder, leftPrecursor);
                var rightPrecursorSources = GetPrecursorSources(doc, rightFolder, rightPrecursor);

                bool leftHasPrecursor = leftPrecursorSources.Count > 0;
                bool rightHasPrecursor = rightPrecursorSources.Count > 0;

                if (leftHasPrecursor && rightHasPrecursor)
                {
                    string testName1 = string.Format("{0} ({1}) vs {2} ({3})", leftPrecursor, leftName, rightPrecursor, rightName);
                    if (!TestExists(documentClash, testName1))
                    {
                        var test1 = CreateClashTestWithSources(testName1, leftPrecursorSources, rightPrecursorSources);
                        if (test1 != null)
                        {
                            ClashCompat.TestsAddCopyAtRoot(documentClash.TestsData, test1);
                            LogMessage(string.Format("Created (Grouped): {0}", testName1));
                            created++;
                            groupedCount++;
                        }
                    }
                    else
                    {
                        skippedCount++;
                        LogMessage(string.Format("Skipped (exists): {0}", testName1));
                    }
                }

                if (leftHasPrecursor)
                {
                    var rightNonPrecursor = GetNonPrecursorSources(doc, rightFolder, rightPrecursor);
                    if (rightNonPrecursor.Count > 0)
                    {
                        string testName2 = string.Format("{0} ({1}) vs {2} (excluding {3})", leftPrecursor, leftName, rightName, rightPrecursor);
                        if (!TestExists(documentClash, testName2))
                        {
                            var test2 = CreateClashTestWithSources(testName2, leftPrecursorSources, rightNonPrecursor);
                            if (test2 != null)
                            {
                                ClashCompat.TestsAddCopyAtRoot(documentClash.TestsData, test2);
                                LogMessage(string.Format("Created (Grouped): {0}", testName2));
                                created++;
                                groupedCount++;
                            }
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                }

                if (rightHasPrecursor)
                {
                    var leftNonPrecursor = GetNonPrecursorSources(doc, leftFolder, leftPrecursor);
                    if (leftNonPrecursor.Count > 0)
                    {
                        string testName3 = string.Format("{0} (excluding {1}) vs {2} ({3})", leftName, leftPrecursor, rightPrecursor, rightName);
                        if (!TestExists(documentClash, testName3))
                        {
                            var test3 = CreateClashTestWithSources(testName3, leftNonPrecursor, rightPrecursorSources);
                            if (test3 != null)
                            {
                                ClashCompat.TestsAddCopyAtRoot(documentClash.TestsData, test3);
                                LogMessage(string.Format("Created (Grouped): {0}", testName3));
                                created++;
                                groupedCount++;
                            }
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                }

                string testNameRemainder = string.Format("{0} vs {1} (Remaining)", leftName, rightName);
                if (!TestExists(documentClash, testNameRemainder))
                {
                    var testRemainder = CreateClashTestFromDisciplineFolders(doc, testNameRemainder, leftFolder, rightFolder);
                    if (testRemainder != null)
                    {
                        ClashCompat.TestsAddCopyAtRoot(documentClash.TestsData, testRemainder);
                        LogMessage(string.Format("Created: {0}", testNameRemainder));
                        created++;
                    }
                }
                else
                {
                    skippedCount++;
                }
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error creating grouped tests for {0} vs {1}: {2}", leftName, rightName, ex.Message));
                failedCount++;
            }

            return created;
        }

        private List<SelectionSource> GetPrecursorSources(Document doc, SavedItem folder, string precursorName)
        {
            var result = new List<SelectionSource>();

            try
            {
                GroupItem group = folder as GroupItem;
                if (group == null) return result;

                foreach (SavedItem child in group.Children)
                {
                    if (child is SelectionSet)
                    {
                        SelectionSet ss = child as SelectionSet;
                        if (ss.DisplayName.Trim().Equals(precursorName, StringComparison.OrdinalIgnoreCase))
                        {
                            SelectionSource source = doc.SelectionSets.CreateSelectionSource(ss);
                            result.Add(source);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error getting precursor sources: {0}", ex.Message));
            }

            return result;
        }

        private List<SelectionSource> GetNonPrecursorSources(Document doc, SavedItem folder, string excludePrecursor)
        {
            var result = new List<SelectionSource>();

            try
            {
                GroupItem group = folder as GroupItem;
                if (group == null) return result;

                foreach (SavedItem child in group.Children)
                {
                    if (child is SelectionSet)
                    {
                        SelectionSet ss = child as SelectionSet;
                        if (!ss.DisplayName.Trim().Equals(excludePrecursor, StringComparison.OrdinalIgnoreCase))
                        {
                            SelectionSource source = doc.SelectionSets.CreateSelectionSource(ss);
                            result.Add(source);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error getting non-precursor sources: {0}", ex.Message));
            }

            return result;
        }

        private ClashTest CreateClashTestWithSources(
            string testName,
            List<SelectionSource> leftSources,
            List<SelectionSource> rightSources)
        {
            if (leftSources.Count == 0 || rightSources.Count == 0)
                return null;

            try
            {
                var clashTest = new ClashTest
                {
                    DisplayName = testName,
                    CustomTestName = testName,
                    TestType = ClashTestType.Hard,
                    Tolerance = 0.0
                };

                clashTest.SelectionA.SelfIntersect = false;
                clashTest.SelectionA.PrimitiveTypes = PrimitiveTypes.Triangles;
                foreach (SelectionSource source in leftSources)
                {
                    clashTest.SelectionA.Selection.SelectionSources.Add(source);
                }

                clashTest.SelectionB.SelfIntersect = false;
                clashTest.SelectionB.PrimitiveTypes = PrimitiveTypes.Triangles;
                foreach (SelectionSource source in rightSources)
                {
                    clashTest.SelectionB.Selection.SelectionSources.Add(source);
                }

                return clashTest;
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Error creating test '{0}': {1}", testName, ex.Message));
                return null;
            }
        }
    }
}
