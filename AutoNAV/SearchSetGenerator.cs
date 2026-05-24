using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using NavApp = Autodesk.Navisworks.Api.Application;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;

namespace AutoNAV
{
    public class SearchSetGenerator
    {
        private const string DISCIPLINES_FOLDER = "1. DISCIPLINES";
        private const string CLASH_SETS_FOLDER  = "2. CLASH SETS";

        public class DisciplinePatternResult
        {
            public string DisplayName { get; set; }
            public string SearchPattern { get; set; }
            public List<string> MatchedFiles { get; set; }
        }

        // Registry populated by GenerateFunction1SearchSets: discipline
        // search-set display name → canonical discipline ("Architectural",
        // "Mechanical", …) or null for unrecognised tokens that the user
        // didn't override. Function 2 reads this to pick discipline-appropriate
        // element-property defaults.
        public static readonly Dictionary<string, string> DisciplineRegistry =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Canonical discipline → list of (Label, Category, Property) tuples
        // used to populate Function 2's property dropdown per row.
        // Tuple "*-File" entries are the "everything in this file" catch-all
        // that guarantees no element is left unselectable.
        public static (string Label, string Category, string Property)[] PropertyOptionsFor(string canonicalDiscipline)
        {
            switch (canonicalDiscipline)
            {
                case "Mechanical":
                case "Plumbing":
                case "Electrical":
                    return new[]
                    {
                        ("Element System Abbreviation",        "Element", "System Abbreviation"),
                        ("Element System Classification",      "Element", "System Classification"),
                        ("Element Workset",                    "Element", "Workset"),
                        ("Element System Type",                "Element", "System Type"),
                        ("Element Properties System Abbrev.",  "Element Properties", "System Abbreviation"),
                        ("System Type → Name",                 "System Type", "Name"),
                        ("Element Category (Mech. Equipment)", "Element", "Category"),
                        ("Element → File (catch-all)",         "Element", "File"),
                    };
                case "Fire Protection":
                    return new[]
                    {
                        ("Element System Abbreviation",        "Element", "System Abbreviation"),
                        ("Element System Classification",      "Element", "System Classification"),
                        ("Element Workset",                    "Element", "Workset"),
                        ("Element System Type",                "Element", "System Type"),
                        ("Element Properties System Abbrev.",  "Element Properties", "System Abbreviation"),
                        ("System Type → Name",                 "System Type", "Name"),
                        ("Element Category",                   "Element", "Category"),
                        ("Item → Layer",                       "Item", "Layer"),
                        ("Item → Type",                        "Item", "Type"),
                        ("Element → File (catch-all)",         "Element", "File"),
                    };
                case "Telecommunications":
                case "Security":
                case "Audio/Visual":
                    return new[]
                    {
                        ("Element System Abbreviation",        "Element", "System Abbreviation"),
                        ("Element Workset",                    "Element", "Workset"),
                        ("Element Category",                   "Element", "Category"),
                        ("Element Type",                       "Element", "Type"),
                        ("Item → Layer",                       "Item", "Layer"),
                        ("Element → File (catch-all)",         "Element", "File"),
                    };
                default:
                    // Architectural, Structural, Interiors, Civil, Landscape,
                    // and everything else (including unknown / fallback-derived
                    // disciplines) gets the original generic option set.
                    return new[]
                    {
                        ("Element Category",   "Element", "Category"),
                        ("Element Workset",    "Element", "Workset"),
                        ("Element Level",      "Element", "Level"),
                        ("Element System",     "Element", "System Name"),
                        ("Element Type",       "Element", "Type"),
                        ("Element → File (catch-all)", "Element", "File"),
                    };
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Function 1 — Discipline Search Sets
        //
        //   Looks at ALL loaded model filenames together, groups files that
        //   belong to the same discipline (same non-level segments), then
        //   finds the MINIMUM CONTAINS pattern that:
        //     • Appears in every file of that discipline group
        //     • Does NOT appear in files from any other discipline group
        //     • Is as short as possible (shortest single segment first;
        //       falls back to multi-segment if needed)
        //
        //   One search set per unique pattern:
        //     DisplayName : clean name (separators stripped)  e.g. "MP"
        //     Search Cond : LcOaSceneBaseUserName CONTAINS "-MP-"
        //     Scope       : all items (locator="/")
        //
        //   Works with any separator ("-" or "_") and any naming convention.
        //   Multi-floor files (same discipline, different level) collapse into
        //   one search set because they share the same discriminating pattern.
        //
        //   User selection path (via dialog):
        //     - Displays preview of detected patterns with matched files
        //     - User can edit display names and search patterns
        //     - Creates search sets from user-confirmed selections
        // ─────────────────────────────────────────────────────────────────────
        public static void GenerateFunction1SearchSets()
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Function 1",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DocumentModels models = doc.Models;
                if (models.Count == 0)
                {
                    MessageBox.Show("No models are loaded in the current document.", "Function 1",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Collect all filenames (no extension) from loaded models
                var allFileNames = new List<string>();
                for (int i = 0; i < models.Count; i++)
                {
                    Model  m    = models[i];
                    string raw  = !string.IsNullOrEmpty(m.FileName) ? m.FileName : m.SourceFileName;
                    string noExt = System.IO.Path.GetFileNameWithoutExtension(raw ?? "");
                    if (!string.IsNullOrWhiteSpace(noExt))
                        allFileNames.Add(noExt);
                }

                // Classify every loaded file via the discipline dictionary, with
                // shortest-unique-discriminator as the per-file fallback.
                var picks = ClassifyFiles(allFileNames);
                if (picks.Count == 0)
                {
                    MessageBox.Show("Could not derive discipline patterns from the loaded model names.",
                        "Function 1", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // If any file landed on the fallback path, offer the user a
                // chance to override its discipline before search sets are
                // created.
                var unresolved = picks.Where(p => !p.FromDictionary).ToList();
                if (unresolved.Count > 0)
                {
                    var dlg = new UnknownDisciplineDialog(unresolved, DisciplineDictionary);
                    // Best-effort owner: find a WPF window that's actually
                    // visible.  Inside Navisworks Application.Current.MainWindow
                    // can be a Win32-hosted Window that hasn't been WPF-shown,
                    // and setting Owner to such a window throws InvalidOperationException
                    // "Cannot set Owner property to a Window that has not been
                    // shown previously."  Falling back to no owner still gives
                    // a working modal dialog.
                    try
                    {
                        Window owner = null;
                        if (System.Windows.Application.Current != null)
                        {
                            foreach (Window w in System.Windows.Application.Current.Windows)
                            {
                                if (w != dlg && w.IsVisible)
                                {
                                    owner = w;
                                    break;
                                }
                            }
                        }
                        if (owner != null) dlg.Owner = owner;
                    }
                    catch { /* dialog still works without an owner */ }
                    dlg.ShowDialog();

                    // Merge user choices back into the pick list.
                    char sep = DetectSeparator(string.Join("|", allFileNames));
                    foreach (var chosen in dlg.Choices)
                    {
                        // Match by source file; replace the pick in-place.
                        for (int i = 0; i < picks.Count; i++)
                        {
                            if (string.Equals(picks[i].SourceFile, chosen.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                string token = chosen.Value;
                                if (!string.IsNullOrWhiteSpace(token))
                                {
                                    var p = picks[i];
                                    p.DisplayName = token;
                                    p.Pattern = sep + token + sep;
                                    p.FromDictionary = true;
                                    p.CanonicalName = TryMatchDiscipline(token, out _, out string canon) ? canon : token;
                                    picks[i] = p;
                                }
                                break;
                            }
                        }
                    }
                }

                // Collapse to unique patterns (multiple files can share one
                // discipline → one search set).  Also populate the registry so
                // Function 2 can read each search-set's canonical discipline.
                var seenPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var patterns = new List<string>();
                foreach (var p in picks)
                {
                    if (seenPatterns.Add(p.Pattern))
                    {
                        patterns.Add(p.Pattern);
                        string display = StripSeparatorWrapping(p.Pattern);
                        DisciplineRegistry[display] =
                            p.FromDictionary
                                ? p.CanonicalName
                                : (TryMatchDiscipline(p.DisplayName, out _, out string canon) ? canon : null);
                    }
                }

                DocumentSelectionSets selSets = doc.SelectionSets;
                GroupItem root = selSets.RootItem as GroupItem;
                if (root == null)
                {
                    MessageBox.Show("Cannot access selection sets.", "Function 1",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Find or create "1. DISCIPLINES" folder
                FolderItem discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                if (discFolder == null)
                {
                    selSets.AddCopy(new FolderItem { DisplayName = DISCIPLINES_FOLDER });
                    root       = doc.SelectionSets.RootItem as GroupItem;
                    discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                }

                if (discFolder == null)
                {
                    MessageBox.Show(
                        "Failed to create '" + DISCIPLINES_FOLDER + "' folder.\n\nEnsure a model is open.",
                        "Function 1", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Existing set names — skip ones already present
                var existing = new HashSet<string>(
                    discFolder.Children.OfType<SavedItem>().Select(c => c.DisplayName.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                int created = 0;
                int skipped = 0;
                var errors = new List<string>();

                foreach (string pattern in patterns)
                {
                    string displayName = StripSeparatorWrapping(pattern);
                    if (existing.Contains(displayName)) { skipped++; continue; }

                    // Refresh folder reference each iteration
                    root       = doc.SelectionSets.RootItem as GroupItem;
                    discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                    if (discFolder == null) { errors.Add("Lost reference to DISCIPLINES folder"); break; }

                    try
                    {
                        var search = new Search();
                        search.Locations = SearchLocations.DescendantsAndSelf;
                        search.Selection.SelectAll();
                        search.SearchConditions.Add(
                            SearchCondition.HasPropertyByName("LcOaNode", "LcOaSceneBaseUserName")
                                .DisplayStringContains(pattern));

                        var ss = new SelectionSet(search) { DisplayName = displayName };

                        selSets.AddCopy(discFolder, ss);
                        existing.Add(displayName);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format("'{0}' (pattern: '{1}'): {2}", displayName, pattern, ex.Message));
                    }
                }

                string msg = string.Format("Function 1 complete.\nModels detected: {0}\nPatterns derived: {1}\nCreated: {2}  |  Skipped (existing): {3}",
                    allFileNames.Count, patterns.Count, created, skipped);

                if (errors.Count > 0)
                    msg += "\n\nErrors:\n" + string.Join("\n", errors);

                if (patterns.Count > 0)
                    msg += "\n\nPatterns: " + string.Join(", ", patterns.Select(p => StripSeparatorWrapping(p)));

                if (allFileNames.Count > 0 && allFileNames.Count <= 20)
                    msg += "\n\nFiles: " + string.Join(", ", allFileNames);

                MessageBox.Show(msg, "Function 1 — Diagnostic",
                    MessageBoxButton.OK, created > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Function 1:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "Function 1 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Function 2 — Property-based Clash Search Sets
        // ─────────────────────────────────────────────────────────────────────

        // Parameterless version - prompts user to select disciplines via UI
        public static void GenerateFunction2SearchSets()
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Function 2",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get all disciplines from 1. DISCIPLINES folder
                GroupItem root = doc.SelectionSets.RootItem as GroupItem;
                FolderItem discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                
                var disciplines = new List<string>();
                if (discFolder != null)
                {
                    foreach (SavedItem child in discFolder.Children)
                    {
                        if (child is SelectionSet)
                            disciplines.Add(child.DisplayName.Trim());
                    }
                }

                if (disciplines.Count == 0)
                {
                    MessageBox.Show("No disciplines found. Run Function 1 first.", "Function 2",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Use Element Category as default property
                GenerateFunction2SearchSets(disciplines, "Element", "Category");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Function 2:\n\n" + ex.Message, "Function 2 Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Parameter version - for programmatic use
        public static void GenerateFunction2SearchSets(
            List<string> selectedDisciplines, string propCat, string propName)
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Function 2",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DocumentSelectionSets selSets = doc.SelectionSets;

                GroupItem  root        = selSets.RootItem as GroupItem;
                FolderItem clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                if (clashFolder == null)
                {
                    selSets.AddCopy(new FolderItem { DisplayName = CLASH_SETS_FOLDER });
                    root        = doc.SelectionSets.RootItem as GroupItem;
                    clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                }

                if (clashFolder == null)
                {
                    MessageBox.Show("Failed to create '" + CLASH_SETS_FOLDER + "' folder.",
                        "Function 2", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Find "1. DISCIPLINES" folder — needed to scope each search
                FolderItem discSourceFolder = FindFolder(root, DISCIPLINES_FOLDER);
                if (discSourceFolder == null)
                {
                    MessageBox.Show("'" + DISCIPLINES_FOLDER + "' folder not found.\nRun Function 1 first.",
                        "Function 2", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int totalCreated = 0;
                var errors = new List<string>();

                foreach (string discipline in selectedDisciplines)
                {
                    root        = doc.SelectionSets.RootItem as GroupItem;
                    clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                    if (clashFolder == null) break;

                    // Find the discipline search set under "1. DISCIPLINES"
                    discSourceFolder = FindFolder(root, DISCIPLINES_FOLDER);
                    SavedItem discSearchSet = null;
                    if (discSourceFolder != null)
                    {
                        discSearchSet = discSourceFolder.Children
                            .OfType<SavedItem>()
                            .FirstOrDefault(c => c.DisplayName.Trim()
                                .Equals(discipline, StringComparison.OrdinalIgnoreCase));
                    }

                    if (discSearchSet == null)
                    {
                        errors.Add(string.Format("'{0}': no matching search set in {1}", discipline, DISCIPLINES_FOLDER));
                        continue;
                    }

                    // Create a SelectionSource from the discipline search set
                    SelectionSource discSource = selSets.CreateSelectionSource(discSearchSet);
                    if (discSource == null)
                    {
                        errors.Add(string.Format("'{0}': failed to create SelectionSource", discipline));
                        continue;
                    }

                    string searchPattern = discipline;
                    if (discSearchSet is SelectionSet)
                    {
                        SelectionSet selSet = discSearchSet as SelectionSet;
                        string extracted = GetSearchPatternFromSet(selSet);
                        if (!string.IsNullOrEmpty(extracted))
                            searchPattern = extracted;
                    }

                    // Find all models whose filename contains this discipline pattern
                    List<Model> discModels = FindModelsForDiscipline(doc, searchPattern);
                    if (discModels.Count == 0)
                    {
                        errors.Add(string.Format("'{0}': no matching model files found", discipline));
                        continue;
                    }

                    // Enumerate unique property values from those models
                    List<string> values = EnumeratePropertyValuesFromModels(discModels, propCat, propName);
                    if (values.Count == 0)
                    {
                        errors.Add(string.Format("'{0}': no '{1}/{2}' values found", discipline, propCat, propName));
                        continue;
                    }

                    // Find or create discipline subfolder under "2. CLASH SETS"
                    FolderItem discFolder = FindFolder(clashFolder, discipline);
                    if (discFolder == null)
                    {
                        selSets.AddCopy(clashFolder, new FolderItem { DisplayName = discipline });
                        root        = doc.SelectionSets.RootItem as GroupItem;
                        clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                        discFolder  = clashFolder != null ? FindFolder(clashFolder, discipline) : null;
                    }
                    if (discFolder == null) continue;

                    foreach (string value in values)
                    {
                        root        = doc.SelectionSets.RootItem as GroupItem;
                        clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                        discFolder  = clashFolder != null ? FindFolder(clashFolder, discipline) : null;
                        if (discFolder == null) break;

                        bool exists = discFolder.Children.OfType<SavedItem>()
                            .Any(c => c.DisplayName.Equals(value, StringComparison.OrdinalIgnoreCase));
                        if (exists) continue;

                        try
                        {
                            // Scope search to the discipline's search set (Sets mode)
                            // This produces locator: lcop_selection_set_tree/1. DISCIPLINES/<discipline>
                            var search = new Search();
                            search.Locations = SearchLocations.DescendantsAndSelf;
                            search.Selection.SelectionSources.Add(discSource);

                            search.SearchConditions.Add(
                                SearchCondition.HasPropertyByDisplayName(propCat, propName)
                                    .EqualValue(VariantData.FromDisplayString(value)));

                            var ss = new SelectionSet(search) { DisplayName = value };

                            selSets.AddCopy(discFolder, ss);
                            totalCreated++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add(string.Format("'{0}/{1}': {2}", discipline, value, ex.Message));
                        }
                    }
                }

                string msg = string.Format("Function 2 complete.\nCreated {0} new search set(s) in '{1}'.", totalCreated, CLASH_SETS_FOLDER);
                if (errors.Count > 0)
                    msg += "\n\nIssues:\n" + string.Join("\n", errors);

                MessageBox.Show(msg, "Function 2 — Results",
                    MessageBoxButton.OK, totalCreated > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Function 2:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "Function 2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Function 3 — Validation Report
        // ─────────────────────────────────────────────────────────────────────
        public static void GenerateFunction3Refinement()
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Function 3",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                GroupItem root = doc.SelectionSets.RootItem as GroupItem;
                if (root == null) return;

                FolderItem discFolder  = FindFolder(root, DISCIPLINES_FOLDER);
                FolderItem clashFolder = FindFolder(root, CLASH_SETS_FOLDER);

                var sb = new StringBuilder();
                sb.AppendLine("Function 3 — Structure Validation Report");
                sb.AppendLine(new string('─', 42));
                sb.AppendLine();

                if (discFolder != null)
                {
                    int count = discFolder.Children.OfType<SavedItem>().Count();
                    sb.AppendLine(string.Format("[OK] '{0}': {1} set(s) found.", DISCIPLINES_FOLDER, count));
                    foreach (SavedItem child in discFolder.Children)
                        sb.AppendLine(string.Format("       • {0}", child.DisplayName));
                }
                else
                {
                    sb.AppendLine(string.Format("[MISSING] '{0}' — run Function 1 first.", DISCIPLINES_FOLDER));
                }

                sb.AppendLine();

                if (clashFolder != null)
                {
                    int totalSets = 0, subFolders = 0;
                    foreach (SavedItem sub in clashFolder.Children)
                    {
                        if (sub is GroupItem)
                        {
                            GroupItem subGroup = sub as GroupItem;
                            int setCount = subGroup.Children.OfType<SavedItem>().Count();
                            sb.AppendLine(string.Format("[OK] '{0}\\{1}': {2} set(s).", CLASH_SETS_FOLDER, sub.DisplayName, setCount));
                            totalSets += setCount;
                            subFolders++;
                        }
                    }
                    sb.AppendLine();
                    sb.AppendLine(string.Format("Total: {0} discipline folder(s), {1} search set(s).", subFolders, totalSets));
                }
                else
                {
                    sb.AppendLine(string.Format("[MISSING] '{0}' — run Function 2 first.", CLASH_SETS_FOLDER));
                }

                MessageBox.Show(sb.ToString(), "Function 3 — Validation Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Function 3:\n\n" + ex.Message,
                    "Function 3 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Pattern computation
        // ─────────────────────────────────────────────────────────────────────

        // Given all loaded filenames (no extension), returns a list of minimal
        // unique CONTAINS patterns — one per discipline group.
        //
        // Algorithm:
        //  1. Detect separator (majority vote across all files).
        //  2. For each file strip the first segment (project prefix) and any
        //     level-code segments (e.g. L06, B01) → "discipline segments".
        //  3. Group files by their FULL discipline segment string (all segments joined).
        //  4. For each group, find the SHORTEST unique pattern that:
        //       a) appears in ALL files of this group, AND
        //       b) does NOT appear in files of any other group.
        //     If no single segment qualifies, try adjacent segment pairs,
        //     then fall back to full discipline segment string.
        // ─────────────────────────────────────────────────────────────────────
        // Discipline dictionary — US National CAD Standard codes (1-char) plus
        // common BIM filename variants seen in the wild.  Keys are canonical
        // discipline names; values are the recognised tokens (case-insensitive).
        //
        // Match priority is first-by-longest then by dictionary order, so 4-char
        // tokens (ARCH) win over 1-char tokens (A) when both appear.
        // ─────────────────────────────────────────────────────────────────────
        internal static readonly Dictionary<string, string[]> DisciplineDictionary =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Architectural",       new[] { "ARCHI", "ARCH", "ARCS", "ARC", "AR", "A" } },
                { "Structural",          new[] { "STRUCT", "STRC", "STRU", "STR", "ST", "S" } },
                { "Mechanical",          new[] { "HVAC", "MECH", "MEC", "ME", "M" } },
                { "Electrical",          new[] { "ELEX", "ELEC", "ELC", "EL", "E" } },
                { "Plumbing",            new[] { "PLUMB", "PLBG", "PLM", "PL", "P" } },
                { "Fire Protection",     new[] { "SPRINK", "FIRE", "SPRK", "FP", "SP", "F" } },
                { "Civil",               new[] { "CIVIL", "CIV", "CV", "C" } },
                { "Landscape",           new[] { "LAND", "LDS", "LS", "L" } },
                { "Telecommunications",  new[] { "TELE", "TEL", "COMM", "DATA", "IT", "T" } },
                { "Interiors",           new[] { "INTERIOR", "INT", "I" } },
                { "Equipment",           new[] { "EQUIP", "EQ", "Q" } },
                { "Hazardous",           new[] { "HAZ", "HZ", "H" } },
                { "Geotechnical",        new[] { "GEO", "GE", "B" } },
                { "Process",             new[] { "PROC", "PR", "D" } },
                { "Distributed Energy",  new[] { "DE", "W" } },
                { "Survey",              new[] { "SURV", "SV", "V" } },
                { "Resource",            new[] { "R" } },
                { "Other",               new[] { "OT", "X" } },
                { "Contractor",          new[] { "SHOP", "SH", "Z" } },
                { "Operations",          new[] { "OP", "O" } },
                { "Security",            new[] { "SECURITY", "SEC" } },
                { "Vertical Transport",  new[] { "ELEV", "VT" } },
                { "Roofing",             new[] { "ROOF", "RF" } },
                { "FF&E",                new[] { "FFE", "FF" } },
                { "Audio/Visual",        new[] { "AV" } },
                { "Acoustical",          new[] { "ACOUS", "AC" } },
                { "Demolition",          new[] { "DEMO", "DM" } },
            };

        // Pre-flattened (token → canonical name) lookup, longest-first.
        private static readonly KeyValuePair<string, string>[] DisciplineLookup =
            DisciplineDictionary
                .SelectMany(kv => kv.Value.Select(code => new KeyValuePair<string, string>(code, kv.Key)))
                .OrderByDescending(kv => kv.Key.Length)
                .ToArray();

        // Look up a token in the dictionary; returns the matching dictionary token
        // (original casing) and canonical name, or (null, null) for no hit.
        internal static bool TryMatchDiscipline(string token, out string matchedCode, out string canonicalName)
        {
            matchedCode = null;
            canonicalName = null;
            if (string.IsNullOrEmpty(token)) return false;
            foreach (var kv in DisciplineLookup)
            {
                if (string.Equals(kv.Key, token, StringComparison.OrdinalIgnoreCase))
                {
                    matchedCode = kv.Key;
                    canonicalName = kv.Value;
                    return true;
                }
            }
            return false;
        }

        // Per-file shortest-unique-subsequence pick used as the fallback when the
        // discipline dictionary doesn't hit.  Returns (pattern, displayName).
        private static (string Pattern, string DisplayName) PickFallbackDiscriminator(
            string file, List<string> candidates, List<string> allFiles, char sep)
        {
            var tokens = candidates;
            for (int len = 1; len <= tokens.Count; len++)
            {
                for (int start = 0; start + len <= tokens.Count; start++)
                {
                    var slice = tokens.GetRange(start, len);
                    string joined = string.Join(sep.ToString(), slice);
                    string candidate = sep + joined + sep;

                    if (file.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    bool inOther = false;
                    foreach (string g in allFiles)
                    {
                        if (string.Equals(g, file, StringComparison.OrdinalIgnoreCase)) continue;
                        if (g.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                        { inOther = true; break; }
                    }
                    if (inOther) continue;

                    return (candidate, joined);
                }
            }

            // Full candidate-token string fallback
            if (tokens.Count > 0)
            {
                string joined = string.Join(sep.ToString(), tokens);
                return (sep + joined + sep, joined);
            }
            // Last resort
            return (file, file);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Per-file discipline classifier.
        //
        // For every loaded file:
        //   1. Split the filename and remove level codes + tokens shared by
        //      every loaded file (project prefix etc.) so we get the per-file
        //      "candidates" pool.
        //   2. Walk the candidates in original order looking for any that hits
        //      the discipline dictionary.  First hit wins and becomes the
        //      search-set name + Navisworks search pattern.
        //   3. If no dictionary hit, fall back to the shortest contiguous
        //      subsequence of candidates that is unique across all loaded
        //      filenames (when wrapped in separators).
        //
        // Returns the same per-file pick struct used by both
        // ComputeDisciplinePatterns and ComputeDisciplinePatternsDetailed.
        // ─────────────────────────────────────────────────────────────────────
        public struct DisciplinePick
        {
            public string Pattern;          // wrapped in separators, e.g. "-ARCH-"
            public string DisplayName;      // unwrapped, e.g. "ARCH"
            public string SourceFile;       // the filename it came from
            public bool FromDictionary;     // true if step 2 hit, false for fallback
            public string CanonicalName;    // "Architectural" etc. when FromDictionary
        }

        internal static List<DisciplinePick> ClassifyFiles(List<string> fileNamesNoExt)
        {
            var picks = new List<DisciplinePick>();
            var allFiles = fileNamesNoExt
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (allFiles.Count == 0) return picks;

            char sep = DetectSeparator(string.Join("|", allFiles));

            // Split + compute per-file candidates (no level codes, no
            // universally-shared tokens).
            var fileSegments = allFiles.ToDictionary(
                f => f,
                f => f.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                StringComparer.OrdinalIgnoreCase);

            HashSet<string> universal = null;
            foreach (var segs in fileSegments.Values)
            {
                var thisSet = new HashSet<string>(segs, StringComparer.OrdinalIgnoreCase);
                if (universal == null) universal = thisSet;
                else universal.IntersectWith(thisSet);
            }
            universal = universal ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var candidates = fileSegments.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Where(s => !IsLevelCode(s)).Where(s => !universal.Contains(s)).ToList(),
                StringComparer.OrdinalIgnoreCase);

            foreach (string file in allFiles)
            {
                var tokens = candidates[file];

                // Stage 1: dictionary hit.  Walk candidates in original order;
                // first token that matches a discipline wins.
                string hitToken = null;
                string hitCanonical = null;
                foreach (string t in tokens)
                {
                    if (TryMatchDiscipline(t, out string matched, out string canonical))
                    {
                        hitToken = matched;
                        hitCanonical = canonical;
                        break;
                    }
                }

                if (hitToken != null)
                {
                    picks.Add(new DisciplinePick
                    {
                        Pattern = sep + hitToken + sep,
                        DisplayName = hitToken,
                        SourceFile = file,
                        FromDictionary = true,
                        CanonicalName = hitCanonical,
                    });
                    continue;
                }

                // Stage 2: per-file shortest unique discriminator fallback.
                var fallback = PickFallbackDiscriminator(file, tokens, allFiles, sep);
                picks.Add(new DisciplinePick
                {
                    Pattern = fallback.Pattern,
                    DisplayName = fallback.DisplayName,
                    SourceFile = file,
                    FromDictionary = false,
                    CanonicalName = null,
                });
            }

            return picks;
        }

        internal static List<string> ComputeDisciplinePatterns(List<string> fileNamesNoExt)
        {
            // De-dup patterns when two files would yield the same one (e.g. two
            // architectural models in the same project both pick "ARCH").
            var picks = ClassifyFiles(fileNamesNoExt);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var p in picks)
            {
                if (seen.Add(p.Pattern)) result.Add(p.Pattern);
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static FolderItem FindFolder(GroupItem parent, string name)
        {
            if (parent == null) return null;
            return parent.Children.OfType<FolderItem>()
                .FirstOrDefault(f => f.DisplayName.Trim()
                    .Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // Detect the dominant separator in a string ('-' beats '_').
        private static char DetectSeparator(string s)
        {
            int dashes      = s.Count(c => c == '-');
            int underscores = s.Count(c => c == '_');
            return dashes >= underscores ? '-' : '_';
        }

        // A "level code" segment is 1–3 letters followed by digits and optional
        // trailing non-letter/digit characters (e.g. L06, B01, RF01, L06_).
        private static bool IsLevelCode(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return false;

            // Strip trailing non-alphanumeric suffix (e.g. trailing '_')
            string s = segment.TrimEnd('_', ' ');
            if (s.Length == 0) return false;

            int i = 0;
            // Must start with 1–3 letters
            while (i < s.Length && char.IsLetter(s[i])) i++;
            if (i == 0 || i > 3) return false;

            // Must be followed by at least one digit
            int digitStart = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == digitStart) return false; // no digits found

            return i == s.Length; // nothing unexpected remaining
        }

        private static string StripSeparatorWrapping(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;
            if (pattern.Length < 2) return pattern;
            char first = pattern[0];
            char last = pattern[pattern.Length - 1];
            if ((first == '-' || first == '_') && first == last)
                return pattern.Substring(1, pattern.Length - 2);
            return pattern;
        }

        private static string GetSearchPatternFromSet(SelectionSet selectionSet)
        {
            if (selectionSet == null) return null;
            if (selectionSet.Search == null) return null;
            if (selectionSet.Search.SearchConditions == null) return null;
            foreach (SearchCondition cond in selectionSet.Search.SearchConditions)
            {
                if (cond.Value != null)
                {
                    string val = cond.Value.ToDisplayString();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            return null;
        }

        // Return all models whose filename contains the given search pattern.
        private static List<Model> FindModelsForDiscipline(Document doc, string searchPattern)
        {
            var result = new List<Model>();
            for (int i = 0; i < doc.Models.Count; i++)
            {
                Model  m        = doc.Models[i];
                string raw      = !string.IsNullOrEmpty(m.FileName) ? m.FileName : m.SourceFileName;
                string fileNoExt = System.IO.Path.GetFileNameWithoutExtension(raw ?? "");
                if (fileNoExt.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(m);
            }
            return result;
        }

        internal static List<DisciplinePatternResult> ComputeDisciplinePatternsDetailed(List<string> fileNamesNoExt)
        {
            // De-dup by pattern; multiple files that map to the same discipline
            // (e.g. two architectural models) share one result, with all source
            // files in MatchedFiles.
            var picks = ClassifyFiles(fileNamesNoExt);
            var byPattern = new Dictionary<string, DisciplinePatternResult>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<DisciplinePatternResult>();
            foreach (var p in picks)
            {
                if (!byPattern.TryGetValue(p.Pattern, out var existing))
                {
                    existing = new DisciplinePatternResult
                    {
                        DisplayName = p.DisplayName,
                        SearchPattern = p.Pattern,
                        MatchedFiles = new List<string>(),
                    };
                    byPattern[p.Pattern] = existing;
                    ordered.Add(existing);
                }
                existing.MatchedFiles.Add(p.SourceFile);
            }
            return ordered;
        }

        // Returns the picks whose classifier hit the fallback path (no dictionary
        // match). UI uses this to drive the unknown-discipline picker dialog
        // so the user can override the auto-derived name before search sets are
        // created.
        internal static List<DisciplinePick> GetUnclassifiedPicks(List<string> fileNamesNoExt)
        {
            return ClassifyFiles(fileNamesNoExt).Where(p => !p.FromDictionary).ToList();
        }

        public static void GenerateFunction1FromUserSelection(List<DisciplinePatternResult> patternGroups)
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Function 1",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (patternGroups == null || patternGroups.Count == 0)
                {
                    MessageBox.Show("No discipline patterns to create.", "Function 1",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DocumentSelectionSets selSets = doc.SelectionSets;
                GroupItem root = selSets.RootItem as GroupItem;
                if (root == null)
                {
                    MessageBox.Show("Cannot access selection sets.", "Function 1",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                FolderItem discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                if (discFolder == null)
                {
                    selSets.AddCopy(new FolderItem { DisplayName = DISCIPLINES_FOLDER });
                    root = doc.SelectionSets.RootItem as GroupItem;
                    discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                }

                if (discFolder == null)
                {
                    MessageBox.Show(
                        "Failed to create '" + DISCIPLINES_FOLDER + "' folder.\n\nEnsure a model is open.",
                        "Function 1", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var existing = new HashSet<string>(
                    discFolder.Children.OfType<SavedItem>().Select(c => c.DisplayName.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                int created = 0;
                int skipped = 0;
                var errors = new List<string>();

                foreach (var group in patternGroups)
                {
                    string displayName = group.DisplayName;
                    string searchPattern = group.SearchPattern;

                    if (existing.Contains(displayName)) { skipped++; continue; }

                    root = doc.SelectionSets.RootItem as GroupItem;
                    discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                    if (discFolder == null) { errors.Add("Lost reference to DISCIPLINES folder"); break; }

                    try
                    {
                        var search = new Search();
                        search.Locations = SearchLocations.DescendantsAndSelf;
                        search.Selection.SelectAll();
                        search.SearchConditions.Add(
                            SearchCondition.HasPropertyByName("LcOaNode", "LcOaSceneBaseUserName")
                                .DisplayStringContains(searchPattern));

                        var ss = new SelectionSet(search) { DisplayName = displayName };

                        selSets.AddCopy(discFolder, ss);
                        existing.Add(displayName);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format("'{0}': {1}", displayName, ex.Message));
                    }
                }

                string msg = string.Format("Function 1 complete.\nPatterns: {0}\nCreated: {1}  |  Skipped (existing): {2}",
                    patternGroups.Count, created, skipped);

                if (errors.Count > 0)
                    msg += "\n\nErrors:\n" + string.Join("\n", errors);

                MessageBox.Show(msg, "Function 1 — Results",
                    MessageBoxButton.OK, created > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Function 1:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "Function 1 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Enumerate unique property values from elements across a list of models.
        private static List<string> EnumeratePropertyValuesFromModels(
            List<Model> models, string propCat, string propName)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int maxItemsPerModel = 100000; // Increased from 10000

            foreach (Model model in models)
            {
                int count = 0;
                try
                {
                    CollectPropertyValues(model.RootItem, propCat, propName,
                                          values, ref count, maxItemsPerModel);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Enumerate error ({0}): {1}", model.FileName, ex.Message));
                }
            }

            return values.OrderBy(v => v).ToList();
        }

        // Recursive depth-first property value collector.
        private static void CollectPropertyValues(
            ModelItem item, string propCat, string propName,
            HashSet<string> values, ref int count, int maxItems)
        {
            if (count >= maxItems) return;
            if (item == null) return;

            try
            {
                foreach (PropertyCategory cat in item.PropertyCategories)
                {
                    if (cat.DisplayName == null) continue;
                    if (!cat.DisplayName.Equals(propCat, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (DataProperty prop in cat.Properties)
                    {
                        if (prop.DisplayName == null) continue;
                        if (!prop.DisplayName.Equals(propName, StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            string val = prop.Value.ToDisplayString();
                            if (!string.IsNullOrWhiteSpace(val)) 
                            {
                                values.Add(val);
                                count++;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Traverse children
            try
            {
                foreach (ModelItem child in item.Children)
                {
                    CollectPropertyValues(child, propCat, propName, values, ref count, maxItems);
                    if (count >= maxItems) return;
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Function 3 — Custom Search Set Creation from Property Values
        // Similar to Function 2, but allows user to select property from model
        // ─────────────────────────────────────────────────────────────────────
        
        public class PropertyCategoryInfo
        {
            public string DisplayName { get; set; }
            public string InternalName { get; set; }
            public List<PropertyInfo> Properties { get; set; }
            public PropertyCategoryInfo() { Properties = new List<PropertyInfo>(); }
        }

        public class PropertyInfo
        {
            public string DisplayName { get; set; }
            public string InternalName { get; set; }
        }

        public static List<string> GetAvailableDisciplines()
        {
            var result = new List<string>();
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null) return result;

                GroupItem root = doc.SelectionSets.RootItem as GroupItem;
                if (root == null) return result;

                FolderItem discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                if (discFolder == null) return result;

                foreach (SavedItem child in discFolder.Children)
                {
                    if (child is SelectionSet)
                        result.Add(child.DisplayName.Trim());
                }
            }
            catch { }
            return result;
        }

        public static List<PropertyCategoryInfo> GetPropertyCategoriesForDiscipline(string discipline)
        {
            var result = new List<PropertyCategoryInfo>();
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null) return result;

                List<Model> models = FindModelsForDiscipline(doc, discipline);
                if (models.Count == 0) return result;

                var categorySet = new HashSet<string>();

                foreach (Model model in models)
                {
                    try { CollectPropertyCategories(model.RootItem, categorySet, result); }
                    catch { }
                }
            }
            catch { }
            return result.OrderBy(c => c.DisplayName).ToList();
        }

        private static void CollectPropertyCategories(
            ModelItem item, 
            HashSet<string> categorySet, 
            List<PropertyCategoryInfo> result)
        {
            if (item == null) return;

            foreach (PropertyCategory cat in item.PropertyCategories)
            {
                string catName = cat.DisplayName;
                if (string.IsNullOrEmpty(catName)) continue;

                if (categorySet.Add(catName))
                {
                    var catInfo = new PropertyCategoryInfo
                    {
                        DisplayName = catName,
                        InternalName = cat.Name
                    };

                    foreach (DataProperty prop in cat.Properties)
                    {
                        catInfo.Properties.Add(new PropertyInfo
                        {
                            DisplayName = prop.DisplayName,
                            InternalName = prop.Name
                        });
                    }

                    result.Add(catInfo);
                }
            }

            foreach (ModelItem child in item.Children)
                CollectPropertyCategories(child, categorySet, result);
        }

        public static List<string> GetPropertyValuesForDiscipline(
            string discipline, 
            string propCategory, 
            string propName)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int maxItemsPerModel = 100000;

            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null) return values.OrderBy(v => v).ToList();

                List<Model> models = FindModelsForDiscipline(doc, discipline);
                System.Diagnostics.Debug.WriteLine($"[AutoNAV] Scanning up to {maxItemsPerModel} items across {models.Count} model(s) for discipline '{discipline}'...");
                foreach (Model model in models)
                {
                    int count = 0;
                    try { CollectPropertyValues(model.RootItem, propCategory, propName, values, ref count, maxItemsPerModel); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AutoNAV] Property scan error ({model.FileName}): {ex.Message}"); }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AutoNAV] GetPropertyValuesForDiscipline error: {ex.Message}"); }

            return values.OrderBy(v => v).ToList();
        }

        public static void GenerateCustomSearchSets(
            string discipline, 
            string propCategory, 
            string propName)
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Function 3",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DocumentSelectionSets selSets = doc.SelectionSets;
                GroupItem root = selSets.RootItem as GroupItem;
                if (root == null)
                {
                    MessageBox.Show("Cannot access selection sets.", "Function 3",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                FolderItem discSourceFolder = FindFolder(root, DISCIPLINES_FOLDER);
                if (discSourceFolder == null)
                {
                    MessageBox.Show(string.Format("'{0}' folder not found.\nRun Function 1 first.", DISCIPLINES_FOLDER),
                        "Function 3", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SavedItem discSearchSet = null;
                foreach (SavedItem child in discSourceFolder.Children)
                {
                    if (child.DisplayName.Trim().Equals(discipline, StringComparison.OrdinalIgnoreCase))
                    {
                        discSearchSet = child;
                        break;
                    }
                }

                if (discSearchSet == null)
                {
                    MessageBox.Show(string.Format("Discipline '{0}' not found in {1}.", discipline, DISCIPLINES_FOLDER),
                        "Function 3", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SelectionSource discSource = selSets.CreateSelectionSource(discSearchSet);
                if (discSource == null)
                {
                    MessageBox.Show("Failed to create selection source for discipline.",
                        "Function 3", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                List<string> values = GetPropertyValuesForDiscipline(discipline, propCategory, propName);
                if (values.Count == 0)
                {
                    MessageBox.Show(string.Format("No values found for property '{0}/{1}' in discipline '{2}'.", propCategory, propName, discipline),
                        "Function 3", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                FolderItem clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                if (clashFolder == null)
                {
                    selSets.AddCopy(new FolderItem { DisplayName = CLASH_SETS_FOLDER });
                    root = doc.SelectionSets.RootItem as GroupItem;
                    clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                }

                if (clashFolder == null)
                {
                    MessageBox.Show(string.Format("Failed to access '{0}' folder.", CLASH_SETS_FOLDER),
                        "Function 3", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string customFolderName = discipline + " - Custom (" + propName + ")";
                FolderItem customFolder = FindFolder(clashFolder, customFolderName);
                if (customFolder == null)
                {
                    selSets.AddCopy(clashFolder, new FolderItem { DisplayName = customFolderName });
                    root = doc.SelectionSets.RootItem as GroupItem;
                    clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                    customFolder = clashFolder != null ? FindFolder(clashFolder, customFolderName) : null;
                }

                if (customFolder == null)
                {
                    MessageBox.Show("Failed to create custom search set folder.",
                        "Function 3", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int created = 0;
                int skipped = 0;
                var errors = new List<string>();

                foreach (string value in values)
                {
                    root = doc.SelectionSets.RootItem as GroupItem;
                    clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                    customFolder = clashFolder != null ? FindFolder(clashFolder, customFolderName) : null;
                    if (customFolder == null) break;

                    bool exists = false;
                    foreach (SavedItem child in customFolder.Children)
                    {
                        if (child.DisplayName.Equals(value, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (exists) { skipped++; continue; }

                    try
                    {
                        var search = new Search();
                        search.Locations = SearchLocations.DescendantsAndSelf;
                        search.Selection.SelectionSources.Add(discSource);

                        search.SearchConditions.Add(
                            SearchCondition.HasPropertyByDisplayName(propCategory, propName)
                                .EqualValue(VariantData.FromDisplayString(value)));

                        var ss = new SelectionSet(search) { DisplayName = value };
                        selSets.AddCopy(customFolder, ss);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format("'{0}': {1}", value, ex.Message));
                    }
                }

                string msg = string.Format("Function 3 Complete - Custom Search Sets\n\nDiscipline: {0}\nProperty: {1}/{2}\nCreated: {3}  |  Skipped (existing): {4}\n\nFolder: {5}\\{6}",
                    discipline, propCategory, propName, created, skipped, CLASH_SETS_FOLDER, customFolderName);

                if (errors.Count > 0)
                    msg += "\n\nErrors:\n" + string.Join("\n", errors);

                MessageBox.Show(msg, "Function 3 - Custom Search Sets",
                    MessageBoxButton.OK, created > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Error in Function 3:\n\n{0}\n\n{1}", ex.Message, ex.StackTrace),
                    "Function 3 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
