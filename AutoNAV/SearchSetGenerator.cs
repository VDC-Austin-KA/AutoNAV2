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

        // The summary is also written to %TEMP%\AutoNAV-<name>.txt so it can be
        // sent on in full. Returns the path, or null if it couldn't be written.
        private static string WriteLog(string name, string text)
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AutoNAV-" + name + ".txt");
                System.IO.File.WriteAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + text);
                return path;
            }
            catch { return null; }
        }

        // Shown in every summary so it is clear which build Navisworks loaded.
        private static readonly string BuildStamp = "AutoNAV build " +
            (typeof(SearchSetGenerator).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyFileVersionAttribute), false)
                .OfType<System.Reflection.AssemblyFileVersionAttribute>().FirstOrDefault()?.Version ?? "?");

        public class DisciplinePatternResult
        {
            public string DisplayName { get; set; }
            public string SearchPattern { get; set; }
            public List<string> MatchedFiles { get; set; }
            // Normalised level code (e.g. "L01") captured from the source
            // filename, or null when none was found. See DisciplinePick.Level.
            public string Level { get; set; }
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
        // Function 1 — one search set per loaded model
        //
        //   Every model (file) gets its own set in "1. DISCIPLINES" that selects
        //   that model's Selection Tree root, and so the whole model, and
        //   nothing else.  The search value is the shortest tail of the root
        //   name, extension included, that no other loaded model's name
        //   contains:
        //       AAA_BBB_CC.nwc / AAA_BBA_CC.nwc / AAB_BBB_CC.nwc
        //         -> "AAA_BBB_CC.nwc" / "BBA_CC.nwc" / "AAB_BBB_CC.nwc"
        //       ARCH_L01_PAGE.nwc / ARCH_L01_HOK.nwc -> "PAGE.nwc" / "HOK.nwc"
        //       A.nwc / M.nwc / S.nwc              -> "A.nwc" / "M.nwc" / "S.nwc"
        //   Keeping the extension in the value means it only ever matches file
        //   nodes, never elements named "A".  The set is named without the
        //   extension ("PAGE").  When even the full name is contained in
        //   another model's name (A.nwc inside DATA.nwc), or the root name has
        //   no extension, the search is "Name equals <full root name>".
        //   Cloud models whose root name is a GUID / URN are saved as a fixed
        //   set of that model instead.
        //
        //   The discipline dictionary (ARCH, STR, MECH, …) still tags each set
        //   with its discipline for Function 2's property defaults and
        //   Function 4's Selection-A priority; it no longer names or merges sets.
        // ─────────────────────────────────────────────────────────────────────
        public static void GenerateFunction1SearchSets() => GenerateFunction1SearchSets(false);

        // multiLevel is kept for callers; every model now gets its own set, so
        // per-level files are already separate sets.
        public static void GenerateFunction1SearchSets(bool multiLevel)
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    Notifier.Warning("No active document found.");
                    return;
                }

                DocumentModels models = doc.Models;
                if (models.Count == 0)
                {
                    Notifier.Warning("No models are loaded in the current document.");
                    return;
                }

                // Each model's Selection Tree root name (what the search tests);
                // models whose root name is a GUID / URN fall back to their file
                // name and get a fixed set.
                var treeNames = new List<string>();
                var fixedRoots = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < models.Count; i++)
                {
                    Model m = models[i];
                    string tree = ModelNames.TreeName(m, out bool searchable);
                    if (string.IsNullOrWhiteSpace(tree)) continue;
                    if (!treeNames.Contains(tree, StringComparer.OrdinalIgnoreCase)) treeNames.Add(tree);
                    if (!searchable && m.RootItem != null)
                    {
                        if (!fixedRoots.TryGetValue(tree, out var roots)) fixedRoots[tree] = roots = new List<ModelItem>();
                        roots.Add(m.RootItem);
                    }
                }

                List<FileSetSpec> specs = ComputeFileSetSpecs(treeNames);
                if (specs.Count == 0)
                {
                    Notifier.Warning("Could not read any model names from the loaded models.");
                    return;
                }

                // Discipline per model, for Function 2 / Function 4 only.  Files
                // the dictionary can't place are offered once in the picker.
                var picks = ClassifyFiles(specs.Select(sp => ModelNames.SplitExtension(sp.TreeName).Stem).ToList());
                var canonicalByStem = picks.ToDictionary(p => p.SourceFile, p => p.FromDictionary ? p.CanonicalName : null,
                                                         StringComparer.OrdinalIgnoreCase);
                var unresolved = picks.Where(p => !p.FromDictionary).ToList();
                if (unresolved.Count > 0)
                {
                    var dlg = new UnknownDisciplineDialog(unresolved, DisciplineDictionary);
                    // Best-effort owner: a visible WPF window.  Inside Navisworks
                    // Application.Current.MainWindow can be a Win32-hosted Window
                    // that was never WPF-shown, and using it as Owner throws.
                    try
                    {
                        Window owner = null;
                        if (System.Windows.Application.Current != null)
                        {
                            foreach (Window w in System.Windows.Application.Current.Windows)
                            {
                                if (w != dlg && w.IsVisible) { owner = w; break; }
                            }
                        }
                        if (owner != null) dlg.Owner = owner;
                    }
                    catch { /* dialog still works without an owner */ }
                    dlg.ShowDialog();

                    // The picked discipline only tags the set; the set's name and
                    // search stay unique to its file.
                    foreach (var chosen in dlg.Choices)
                    {
                        string token = chosen.Value?.Trim();
                        if (string.IsNullOrEmpty(token)) continue;
                        canonicalByStem[chosen.Key] = TryMatchDiscipline(token, out _, out string canon) ? canon : token;
                    }
                }

                DocumentSelectionSets selSets = doc.SelectionSets;
                GroupItem root = selSets.RootItem as GroupItem;
                if (root == null)
                {
                    Notifier.Error("Cannot access selection sets.");
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
                    Notifier.Error("Failed to create '" + DISCIPLINES_FOLDER + "' folder.\n\nEnsure a model is open.");
                    return;
                }

                // Existing set name → its search values, so a name clash can be
                // told apart from the very same set being created again.  A fixed
                // (model list) set has no values to compare: null means "treat as
                // the set from an earlier run".
                var existing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (SavedItem c in discFolder.Children)
                    existing[c.DisplayName.Trim()] = ModelNames.IsFixedSet(c as SelectionSet)
                        ? null : GetSearchPatternsFromSet(c as SelectionSet);

                int created = 0;
                int skipped = 0;
                var errors = new List<string>();
                var createdLines = new List<string>();
                var fixedNames = new List<string>();

                foreach (FileSetSpec spec in specs)
                {
                    var terms = new List<string> { spec.Value };

                    // A name already taken by a set that searches for something
                    // else gets "2" (then "3", …) appended, so every model still
                    // gets its set. The same name with the same search is the set
                    // from an earlier run: keep it, add nothing.
                    string displayName = spec.SetName;
                    bool alreadyPresent = false;
                    for (int n = 2; existing.TryGetValue(displayName, out List<string> existingTerms); n++)
                    {
                        if (existingTerms == null || SameTerms(existingTerms, terms)) { alreadyPresent = true; break; }
                        displayName = spec.SetName + n;
                    }

                    // Registry must be keyed on the *actual set name* —
                    // Function 2's discipline list and the clash-test priority
                    // sort look it up by that name.
                    canonicalByStem.TryGetValue(ModelNames.SplitExtension(spec.TreeName).Stem, out string canonical);
                    DisciplineRegistry[displayName] = canonical;
                    if (alreadyPresent) { skipped++; continue; }

                    // Refresh folder reference each iteration
                    root       = doc.SelectionSets.RootItem as GroupItem;
                    discFolder = FindFolder(root, DISCIPLINES_FOLDER);
                    if (discFolder == null) { errors.Add("Lost reference to DISCIPLINES folder"); break; }

                    try
                    {
                        SelectionSet ss;
                        if (fixedRoots.TryGetValue(spec.TreeName, out List<ModelItem> roots))
                        {
                            var items = new ModelItemCollection();
                            foreach (ModelItem r in roots) items.Add(r);
                            ss = new SelectionSet(items) { DisplayName = displayName };
                            fixedNames.Add(displayName);
                        }
                        else
                        {
                            var search = new Search();
                            search.Locations = SearchLocations.DescendantsAndSelf;
                            search.Selection.SelectAll();
                            var name = SearchCondition.HasPropertyByName("LcOaNode", "LcOaSceneBaseUserName");
                            search.SearchConditions.Add(spec.Exact
                                ? name.EqualValue(VariantData.FromDisplayString(spec.Value))
                                : name.DisplayStringContains(spec.Value));
                            ss = new SelectionSet(search) { DisplayName = displayName };
                        }

                        selSets.AddCopy(discFolder, ss);
                        existing[displayName] = terms;
                        createdLines.Add(string.Format("{0}  (Name {1} \"{2}\"{3})", displayName,
                            spec.Exact ? "=" : "contains", spec.Value,
                            string.IsNullOrEmpty(canonical) ? "" : ", " + canonical));
                        created++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format("'{0}' (value: '{1}'): {2}", displayName, spec.Value, ex.Message));
                    }
                }

                string msg = BuildStamp + "\n" + string.Format("Function 1 complete.\nModels: {0}\nCreated: {1}  |  Skipped (existing): {2}",
                    treeNames.Count, created, skipped);

                if (errors.Count > 0)
                    msg += "\n\nErrors:\n" + string.Join("\n", errors);
                if (createdLines.Count > 0)
                    msg += "\n\nCreated:\n" + string.Join("\n", createdLines);
                if (fixedNames.Count > 0)
                    msg += "\n\nSaved as a fixed model (Selection Tree name is a cloud ID, not a file name): " +
                           string.Join(", ", fixedNames);

                Notifier.Warning(msg);
            }
            catch (Exception ex)
            {
                Notifier.Error("Error in Function 1:\n\n" + ex.Message + "\n\n" + ex.StackTrace);
            }
        }

        // One model's Function 1 set: the Selection Tree name it was made for,
        // the search value, whether the search is "equals" rather than
        // "contains", and the set's name (value without the extension).
        public struct FileSetSpec
        {
            public string TreeName;
            public string Value;
            public bool   Exact;
            public string SetName;
        }

        // Shortest unique tail of each name (see the Function 1 header).  Tails
        // start at the beginning of a name segment (after - _ space or .), so
        // a value never starts mid-word.
        public static List<FileSetSpec> ComputeFileSetSpecs(List<string> treeNames)
        {
            var names = treeNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())
                                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var specs = new List<FileSetSpec>();
            foreach (string full in names)
            {
                var (stem, ext) = ModelNames.SplitExtension(full);
                var others = names.Where(o => !o.Equals(full, StringComparison.OrdinalIgnoreCase)).ToList();
                var otherStems = others.Select(o => ModelNames.SplitExtension(o).Stem).ToList();
                var spec = new FileSetSpec { TreeName = full, Value = full, Exact = true, SetName = stem };

                if (ext.Length > 0)
                {
                    var starts = new List<int> { 0 };
                    for (int i = 1; i < stem.Length; i++)
                        if (IsSep(stem[i - 1]) || stem[i - 1] == '.') starts.Add(i);
                    for (int k = starts.Count - 1; k >= 0; k--)
                    {
                        string tail = stem.Substring(starts[k]);
                        if (tail.Trim(AllSeparators).Length == 0) continue;
                        // Unique with and without the extension, so the set
                        // name (which drops it) is as unambiguous as the value.
                        string value = tail + ext;
                        if (others.Any(o => o.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        if (otherStems.Any(o => o.IndexOf(tail, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        spec = new FileSetSpec { TreeName = full, Value = value, Exact = false, SetName = tail };
                        break;
                    }
                }
                specs.Add(spec);
            }
            return specs;
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
                    Notifier.Warning("No active document found.");
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
                    Notifier.Warning("No disciplines found. Run Function 1 first.");
                    return;
                }

                // Use Element Category as default property
                GenerateFunction2SearchSets(disciplines, "Element", "Category");
            }
            catch (Exception ex)
            {
                Notifier.Error("Error in Function 2:\n\n" + ex.Message);
            }
        }

        // Parameter version - for programmatic use
        public static void GenerateFunction2SearchSets(
            List<string> selectedDisciplines, string propCat, string propName)
            => GenerateFunction2SearchSets(selectedDisciplines, propCat, propName, false);

        // multiLevel = false reproduces today's behaviour byte-for-byte (set
        // named after the raw property value, e.g. "Walls"). multiLevel =
        // true names the set "<discipline>-<value>" — since with the toggle
        // on, `discipline` is already the Function-1 name "L01-ARCH", the
        // result reads "L01-ARCH-Walls" with no separate level parsing needed.
        public static void GenerateFunction2SearchSets(
            List<string> selectedDisciplines, string propCat, string propName, bool multiLevel)
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    Notifier.Error("No active document found.");
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
                    Notifier.Error("Failed to create '" + CLASH_SETS_FOLDER + "' folder.");
                    return;
                }

                // Find "1. DISCIPLINES" folder — needed to scope each search
                FolderItem discSourceFolder = FindFolder(root, DISCIPLINES_FOLDER);
                if (discSourceFolder == null)
                {
                    Notifier.Warning("'" + DISCIPLINES_FOLDER + "' folder not found.\nRun Function 1 first.");
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

                    // The discipline's models are the ones its Function 1 set
                    // actually selects; sets from older versions fall back to
                    // matching the model name.
                    List<Model> discModels = ModelNames.ModelsSelectedBy(doc, discSearchSet as SelectionSet);
                    if (discModels.Count == 0)
                        discModels = FindModelsForDiscipline(doc, searchPattern);
                    if (discModels.Count == 0)
                    {
                        errors.Add(string.Format("'{0}': no matching model files found", discipline));
                        continue;
                    }

                    // Enumerate unique property values from those models
                    string cat = propCat;
                    List<string> values = EnumeratePropertyValuesFromModels(discModels, cat, propName);
                    if (values.Count == 0)
                    {
                        // Cloud models opened from Model Coordination (SVF) have no
                        // "Element" tab; their Revit parameters sit under the web
                        // viewer's groups. Use whichever tab holds the property.
                        string found = FindCategoryWithProperty(discModels, propName, propCat);
                        if (found != null)
                        {
                            cat = found;
                            values = EnumeratePropertyValuesFromModels(discModels, cat, propName);
                        }
                    }
                    if (values.Count == 0)
                    {
                        errors.Add(string.Format("'{0}': no '{1}/{2}' values found (Function 3 lists the properties these models do have)",
                            discipline, propCat, propName));
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
                        // multiLevel: discipline is already the Function-1 name
                        // "L01-ARCH", so prefixing it yields "L01-ARCH-Walls"
                        // with no separate level parsing needed.
                        // SVF category values read "Revit Walls"; name the set
                        // "Walls" so Function 5 still finds Walls / Floors.
                        string label = cat != propCat && value.StartsWith("Revit ", StringComparison.OrdinalIgnoreCase)
                            ? value.Substring(6) : value;
                        string setName = multiLevel ? (discipline + "-" + label) : label;

                        root        = doc.SelectionSets.RootItem as GroupItem;
                        clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                        discFolder  = clashFolder != null ? FindFolder(clashFolder, discipline) : null;
                        if (discFolder == null) break;

                        bool exists = discFolder.Children.OfType<SavedItem>()
                            .Any(c => c.DisplayName.Equals(setName, StringComparison.OrdinalIgnoreCase));
                        if (exists) continue;

                        try
                        {
                            // Scope search to the discipline's search set (Sets mode)
                            // This produces locator: lcop_selection_set_tree/1. DISCIPLINES/<discipline>
                            var search = new Search();
                            search.Locations = SearchLocations.DescendantsAndSelf;
                            search.Selection.SelectionSources.Add(discSource);

                            search.SearchConditions.Add(
                                SearchCondition.HasPropertyByDisplayName(cat, propName)
                                    .EqualValue(VariantData.FromDisplayString(value)));

                            var ss = new SelectionSet(search) { DisplayName = setName };

                            selSets.AddCopy(discFolder, ss);
                            totalCreated++;

                            // The discipline set must be this set's search scope,
                            // or e.g. "Walls" would pick up walls from every model.
                            // Read the saved copy back and say so if it isn't.
                            root        = doc.SelectionSets.RootItem as GroupItem;
                            clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                            var saved = (clashFolder != null ? FindFolder(clashFolder, discipline) : null)?
                                .Children.OfType<SelectionSet>()
                                .FirstOrDefault(c => c.DisplayName.Equals(setName, StringComparison.OrdinalIgnoreCase));
                            if (saved?.Search?.Selection?.SelectionSources == null || saved.Search.Selection.SelectionSources.Count == 0)
                                errors.Add(string.Format("'{0}/{1}': saved without the '{0}' scope", discipline, setName));
                        }
                        catch (Exception ex)
                        {
                            errors.Add(string.Format("'{0}/{1}': {2}", discipline, value, ex.Message));
                        }
                    }
                }

                string msg = BuildStamp + "\n" + string.Format("Function 2 complete.\nCreated {0} new search set(s) in '{1}'.", totalCreated, CLASH_SETS_FOLDER);
                if (errors.Count > 0)
                    msg += "\n\nIssues:\n" + string.Join("\n", errors);

                Notifier.Warning(msg);
            }
            catch (Exception ex)
            {
                Notifier.Error("Error in Function 2:\n\n" + ex.Message + "\n\n" + ex.StackTrace);
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
                    Notifier.Warning("No active document found.");
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

                Notifier.Info(sb.ToString());
            }
            catch (Exception ex)
            {
                Notifier.Error("Error in Function 3:\n\n" + ex.Message);
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
        // Picks the shortest contiguous token subsequence of `candidates` that
        // appears in `file` at separator-bounded positions AND in no other
        // file at separator-bounded positions.  The returned Pattern is the
        // bare token (no separator wrapping) — the search at runtime uses the
        // bare token via DisplayStringContains, which works for filenames
        // using any of '-', '_', or ' ' as separators.
        private static (string Pattern, string DisplayName) PickFallbackDiscriminator(
            string file, List<string> candidates, List<string> allFiles)
        {
            for (int len = 1; len <= candidates.Count; len++)
            {
                for (int start = 0; start + len <= candidates.Count; start++)
                {
                    var slice = candidates.GetRange(start, len);
                    // Join with '-' as a canonical pattern shape for multi-token
                    // discriminators; uniqueness still respects any separator
                    // because ContainsTokenAtBoundary is separator-agnostic.
                    string joined = string.Join("-", slice);

                    if (!ContainsTokenAtBoundary(file, joined)) continue;

                    bool inOther = false;
                    foreach (string g in allFiles)
                    {
                        if (string.Equals(g, file, StringComparison.OrdinalIgnoreCase)) continue;
                        if (ContainsTokenAtBoundary(g, joined)) { inOther = true; break; }
                    }
                    if (inOther) continue;

                    return (joined, joined);
                }
            }

            // Full candidate-token string fallback (still bare, no separator wrap).
            if (candidates.Count > 0)
            {
                string joined = string.Join("-", candidates);
                return (joined, joined);
            }
            // Last resort: the entire filename.
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
            // Normalised level code (e.g. "L01"/"B02") parsed from a level-code
            // segment (IsLevelCode) in the source filename via
            // ClashGrouper.NormaliseLevel, or null when the filename has none.
            // Only consumed when the Multi-Level Discipline toggle is on.
            public string Level;
        }

        internal static List<DisciplinePick> ClassifyFiles(List<string> fileNamesNoExt)
        {
            var picks = new List<DisciplinePick>();
            var allFiles = fileNamesNoExt
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (allFiles.Count == 0) return picks;

            // Split + compute per-file candidates.  Split on ANY of the three
            // separators (-, _, space) so the algorithm handles mixed-separator
            // filenames cleanly.
            var fileSegments = allFiles.ToDictionary(
                f => f,
                f => f.Split(AllSeparators, StringSplitOptions.RemoveEmptyEntries).ToList(),
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

            // Capture (rather than discard) the level-code segment stripped
            // above, canonicalised via ClashGrouper.NormaliseLevel so "L3" and
            // "L03" agree. Only consumed when the Multi-Level Discipline
            // toggle is on; null when the filename has no level segment.
            var fileLevels = fileSegments.ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    string raw = kv.Value.FirstOrDefault(IsLevelCode);
                    return raw != null ? ClashGrouper.NormaliseLevel(raw) : null;
                },
                StringComparer.OrdinalIgnoreCase);

            foreach (string file in allFiles)
            {
                var tokens = candidates[file];
                string level = fileLevels[file];

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
                    // Pattern is the BARE token; the Navisworks search uses
                    // DisplayStringContains which matches regardless of which
                    // separator characters the filename actually uses around
                    // the token.
                    picks.Add(new DisciplinePick
                    {
                        Pattern = hitToken,
                        DisplayName = hitToken,
                        SourceFile = file,
                        FromDictionary = true,
                        CanonicalName = hitCanonical,
                        Level = level,
                    });
                    continue;
                }

                // Stage 2: per-file shortest unique discriminator fallback.
                var fallback = PickFallbackDiscriminator(file, tokens, allFiles);
                picks.Add(new DisciplinePick
                {
                    Pattern = fallback.Pattern,
                    DisplayName = fallback.DisplayName,
                    SourceFile = file,
                    FromDictionary = false,
                    CanonicalName = null,
                    Level = level,
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

        // ─────────────────────────────────────────────────────────────────────
        // Separator handling.
        //
        // Filenames in the wild mix '-', '_' and spaces (sometimes within the
        // same file).  The old algorithm picked ONE dominant separator via
        // DetectSeparator and used it both for tokenisation and for wrapping
        // the search pattern.  That broke whenever a file used a different
        // separator than the majority.
        //
        // New approach: treat all three characters as equivalent separators
        // everywhere — tokenise on any of them, test uniqueness with
        // word-boundary matching, and store the search pattern as the bare
        // token (no separator wrap) so the runtime Navisworks substring search
        // works regardless of which separator the filename actually uses.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly char[] AllSeparators = { '-', '_', ' ' };

        private static bool IsSep(char c) => c == '-' || c == '_' || c == ' ';

        // True iff `token` appears in `text` with separator characters (or
        // string boundaries) on BOTH sides — i.e. it's a whole word and not
        // a substring of a longer token.
        private static bool ContainsTokenAtBoundary(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return false;
            int from = 0;
            while (true)
            {
                int idx = text.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return false;
                bool leftOk  = idx == 0                         || IsSep(text[idx - 1]);
                bool rightOk = idx + token.Length == text.Length || IsSep(text[idx + token.Length]);
                if (leftOk && rightOk) return true;
                from = idx + 1;
            }
        }

        // Retained for back-compat with any callers (e.g. PR #11's diagnostics).
        // No longer used by the discipline classifier itself.
        private static char DetectSeparator(string s)
        {
            int dashes      = s.Count(c => c == '-');
            int underscores = s.Count(c => c == '_');
            int spaces      = s.Count(c => c == ' ');
            if (spaces > dashes && spaces > underscores) return ' ';
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

        private static bool SameTerms(List<string> a, List<string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            return new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);
        }

        // Every search term of a Function-1 set: the discipline token and, for
        // multi-level sets, the level code.
        private static List<string> GetSearchPatternsFromSet(SelectionSet selectionSet)
        {
            var result = new List<string>();
            if (selectionSet?.Search?.SearchConditions == null) return result;
            foreach (SearchCondition cond in selectionSet.Search.SearchConditions)
            {
                string val = cond.Value?.ToDisplayString();
                if (!string.IsNullOrEmpty(val)) result.Add(val);
            }
            return result;
        }

        private static string ModelFileName(Model m) => ModelNames.Display(m);

        // Return all models with a name (file or Selection Tree) that contains
        // every one of the patterns.
        private static List<Model> FindModelsForDiscipline(Document doc, List<string> patterns)
        {
            var result = new List<Model>();
            for (int i = 0; i < doc.Models.Count; i++)
            {
                Model m = doc.Models[i];
                if (ModelNames.Matches(m, patterns))
                    result.Add(m);
            }
            return result;
        }

        // The models a discipline set actually covers. Matching on all of the
        // set's terms (not just the first) keeps a multi-level "L01-ARCH" set
        // from scanning L02's models and producing sets that select nothing;
        // it also works when the set was renamed away from its pattern.
        private static List<Model> ModelsForDisciplineSet(Document doc, SavedItem discSet, string discipline)
        {
            List<Model> selected = ModelNames.ModelsSelectedBy(doc, discSet as SelectionSet);
            if (selected.Count > 0) return selected;
            var patterns = GetSearchPatternsFromSet(discSet as SelectionSet);
            if (patterns.Count == 0) patterns.Add(discipline);
            List<Model> models = FindModelsForDiscipline(doc, patterns);
            // Level codes are normalised ("L3" -> "L03"), so a filename need not
            // contain the level term verbatim: fall back to the discipline
            // token alone, which is what Function 2 always matched on before.
            if (models.Count == 0 && patterns.Count > 1)
                models = FindModelsForDiscipline(doc, patterns.GetRange(0, 1));
            return models;
        }

        private static SavedItem FindDisciplineSet(Document doc, string discipline)
        {
            FolderItem folder = FindFolder(doc.SelectionSets.RootItem as GroupItem, DISCIPLINES_FOLDER);
            return folder?.Children.FirstOrDefault(c => c.DisplayName.Trim()
                .Equals(discipline, StringComparison.OrdinalIgnoreCase));
        }

        // Scope search to the discipline's search set (Sets mode)
        // This produces locator: lcop_selection_set_tree/1. DISCIPLINES/<discipline>
        private static SelectionSet PropertyValueSet(
            SelectionSource discSource, ScanResult scan, string value, string setName)
        {
            string propCat = scan.Category, propName = scan.Property;
            var search = new Search();
            search.Locations = SearchLocations.DescendantsAndSelf;
            search.Selection.SelectionSources.Add(discSource);
            search.SearchConditions.Add(
                (scan.ByInternalName
                    ? SearchCondition.HasPropertyByName(propCat, propName)
                    : SearchCondition.HasPropertyByDisplayName(propCat, propName))
                    .EqualValue(VariantData.FromDisplayString(value)));
            return new SelectionSet(search) { DisplayName = setName };
        }

        // Set by MainWindow: shows a line of status text in the AutoNAV window
        // and lets the window repaint while a long scan runs.
        public static Action<string> StatusSink;

        // Progress for a long step, shown as status text in the AutoNAV window
        // ("<title> — <message> 42%"). It deliberately does NOT use the
        // Navisworks progress dialog: the property scan worked before that
        // dialog was added and has found nothing on Navisworks 2027 since, so
        // the scan now runs exactly as it originally did, with no Navisworks
        // progress operation open. Status failures are swallowed.
        private sealed class StepProgress : IDisposable
        {
            private readonly string _base;
            private string _text;
            private readonly Stopwatch _sinceShown = Stopwatch.StartNew();

            public StepProgress(string title, string message)
            {
                _base = _text = title + " — " + message;
                Show(_text);
            }

            // Names the sub-step, e.g. the model being read.
            public void Step(string detail)
            {
                _text = _base + " — " + detail;
                Show(_text);
            }

            // Always true: there is no Cancel without the Navisworks dialog.
            public bool Update(double fraction)
            {
                if (_sinceShown.ElapsedMilliseconds >= 250)
                    Show(string.Format("{0}  {1:0}%", _text, 100 * Math.Max(0.0, Math.Min(1.0, fraction))));
                return true;
            }

            private void Show(string text)
            {
                _sinceShown.Restart();
                try { StatusSink?.Invoke(text); } catch { }
            }

            public void Dispose() { }
        }

        // Writes the sets named `setNames` into the `folderName` subfolder of
        // the group `getParent` resolves. Each set is built by `makeSet(i)` and
        // added with its own AddCopy straight away, exactly as Function 2
        // always has, so it is in the document the moment it is created. An
        // existing subfolder (re-run / resume) only receives the sets it lacks.
        private static (int Created, int Skipped, bool Cancelled) CommitSets(
            Document doc, Func<GroupItem> getParent, string folderName,
            List<string> setNames, Func<int, SelectionSet> makeSet,
            StepProgress progress, List<string> errors)
        {
            GroupItem parent = getParent();
            if (parent == null) { errors.Add("Lost reference to the parent folder of '" + folderName + "'"); return (0, 0, false); }

            if (FindFolder(parent, folderName) == null)
                doc.SelectionSets.AddCopy(parent, new FolderItem { DisplayName = folderName });

            FolderItem live = FindFolder(getParent(), folderName);
            if (live == null) { errors.Add("Failed to create folder '" + folderName + "'"); return (0, 0, false); }

            var existing = new HashSet<string>(live.Children.Select(c => c.DisplayName), StringComparer.OrdinalIgnoreCase);
            int created = 0, skipped = 0;
            for (int i = 0; i < setNames.Count; i++)
            {
                if (!existing.Add(setNames[i])) { skipped++; continue; }
                if (progress != null && !progress.Update((double)i / setNames.Count)) return (created, skipped, true);

                // Folder references go stale after each document edit.
                live = FindFolder(getParent(), folderName);
                if (live == null) { errors.Add("Lost reference to '" + folderName + "'"); break; }
                try
                {
                    doc.SelectionSets.AddCopy(live, makeSet(i));
                    created++;
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format("'{0}/{1}': {2}", folderName, setNames[i], ex.Message));
                }
            }
            return (created, skipped, false);
        }

        // What one model's scan found: the values, the property's actual
        // names as Navisworks reports them, and enough of what the model
        // does contain to say why nothing matched.
        private sealed class ModelScan
        {
            public readonly HashSet<string> Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public string Category, Property;   // names to build sets with
            public bool ByInternalName;          // matched on internal, not display, names
            public int Items;
            public readonly HashSet<string> CategoriesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> PropertiesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ScanResult
        {
            public List<string> Values;
            public string Category, Property;
            public bool ByInternalName;
            public string Diagnostics;           // set when nothing was found
        }

        // Unique propCat/propName values across `models`. A native Search
        // returns only the items that carry the property under that exact
        // display name; if it finds nothing (or fails) every node is walked
        // and the category / property are matched case-insensitively on
        // display OR internal name, so a renamed or re-cased property
        // (e.g. a different Navisworks / exporter version) is still found and
        // the sets are built with the names actually present. `cache` lets
        // disciplines that share a model reuse its scan. Returns null when
        // the user cancels.
        private static ScanResult ScanPropertyValues(
            Document doc, List<Model> models, string propCat, string propName,
            StepProgress progress, Dictionary<string, ModelScan> cache)
        {
            var scans = new List<KeyValuePair<string, ModelScan>>();
            for (int i = 0; i < models.Count; i++)
            {
                Model  model = models[i];
                string file  = ModelFileName(model);
                string key   = file + "|" + propCat + "|" + propName;
                double share = 1.0 / models.Count;
                progress.Step(string.Format("model {0} of {1}: {2}", i + 1, models.Count, file));

                if (!cache.TryGetValue(key, out ModelScan scan))
                {
                    scan = new ModelScan();
                    try
                    {
                        var search = new Search();
                        search.Selection.CopyFrom(new[] { model.RootItem });
                        search.Locations = SearchLocations.DescendantsAndSelf;
                        search.PruneBelowMatch = false;
                        search.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName(propCat, propName));
                        ModelItemCollection hits = search.FindAll(doc, false);

                        int n = 0, count = hits.Count;
                        foreach (ModelItem item in hits)
                        {
                            if (++n % 500 == 0 && !progress.Update((i + (double)n / count) * share)) return null;
                            try
                            {
                                string val = item.PropertyCategories
                                    .FindPropertyByDisplayName(propCat, propName)?.Value?.ToDisplayString();
                                if (!string.IsNullOrWhiteSpace(val)) scan.Values.Add(val);
                            }
                            catch { }
                        }
                        if (scan.Values.Count > 0) { scan.Category = propCat; scan.Property = propName; }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(string.Format("[AutoNAV] Property scan error ({0}): {1}", file, ex.Message));
                    }

                    try
                    {
                        if (scan.Values.Count == 0 &&
                            !WalkPropertyValues(model.RootItem, propCat, propName, scan, progress, i * share))
                            return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(string.Format("[AutoNAV] Property walk error ({0}): {1}", file, ex.Message));
                    }
                    cache[key] = scan;
                }
                scans.Add(new KeyValuePair<string, ModelScan>(file, scan));
            }

            var result = new ScanResult
            {
                Values = scans.SelectMany(s => s.Value.Values)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v).ToList(),
                Category = propCat,
                Property = propName,
            };
            var found = scans.Select(s => s.Value).FirstOrDefault(s => s.Values.Count > 0);
            if (found != null)
            {
                result.Category = found.Category;
                result.Property = found.Property;
                result.ByInternalName = found.ByInternalName;
            }
            else
            {
                result.Diagnostics = string.Join("\n", scans.Select(s => string.Format(
                    "    {0}: {1} item(s) read; categories: {2}{3}",
                    s.Key, s.Value.Items,
                    s.Value.CategoriesSeen.Count == 0 ? "(none)" : string.Join(", ", s.Value.CategoriesSeen.Take(25)),
                    s.Value.PropertiesSeen.Count == 0 ? "" :
                        string.Format("; '{0}' has: {1}", propCat, string.Join(", ", s.Value.PropertiesSeen.Take(25))))));
            }
            return result;
        }

        private static bool NameMatches(string displayName, string internalName, string wanted)
        {
            wanted = wanted?.Trim();
            return string.Equals(displayName?.Trim(), wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(internalName?.Trim(), wanted, StringComparison.OrdinalIgnoreCase);
        }

        // Every-node walk, structured exactly like the original scan: recurse
        // through item.Children, each node and each subtree in its own try, so
        // a node that can't be read only skips itself. Matches category and
        // property case-insensitively on display or internal name, and notes
        // what the model contains for the diagnostics. Returns false when the
        // user cancels.
        private static bool WalkPropertyValues(
            ModelItem item, string propCat, string propName, ModelScan scan,
            StepProgress progress, double fraction)
        {
            if (item == null) return true;
            if (++scan.Items % 2000 == 0 && !progress.Update(fraction)) return false;

            try
            {
                foreach (PropertyCategory cat in item.PropertyCategories)
                {
                    if (scan.CategoriesSeen.Count < 40 && !string.IsNullOrEmpty(cat.DisplayName))
                        scan.CategoriesSeen.Add(cat.DisplayName);
                    if (!NameMatches(cat.DisplayName, cat.Name, propCat)) continue;
                    foreach (DataProperty prop in cat.Properties)
                    {
                        if (scan.PropertiesSeen.Count < 40 && !string.IsNullOrEmpty(prop.DisplayName))
                            scan.PropertiesSeen.Add(prop.DisplayName);
                        if (!NameMatches(prop.DisplayName, prop.Name, propName)) continue;
                        try
                        {
                            string val = prop.Value.ToDisplayString();
                            if (string.IsNullOrWhiteSpace(val)) continue;
                            scan.Values.Add(val);
                            if (scan.Category == null)
                            {
                                // Build sets with the names Navisworks reports. If the
                                // display names don't match what was asked for, use the
                                // internal names, which don't vary by version / locale.
                                bool display = NameMatches(cat.DisplayName, null, propCat)
                                            && NameMatches(prop.DisplayName, null, propName);
                                scan.ByInternalName = !display;
                                scan.Category = display ? cat.DisplayName : cat.Name;
                                scan.Property = display ? prop.DisplayName : prop.Name;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                foreach (ModelItem child in item.Children)
                    if (!WalkPropertyValues(child, propCat, propName, scan, progress, fraction)) return false;
            }
            catch { }
            return true;
        }

        // ── Function 2's original helpers, restored unchanged ──

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

        // Return all models with a name (file or Selection Tree) that contains
        // the given search pattern.
        private static List<Model> FindModelsForDiscipline(Document doc, string searchPattern) =>
            FindModelsForDiscipline(doc, new List<string> { searchPattern });

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
        // The tab (other than the one already tried) under which the property
        // appears most often across the first items of these models, or null.
        private static string FindCategoryWithProperty(List<Model> models, string propName, string exceptCat)
        {
            var hits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int seen = 0;
            void Walk(ModelItem item)
            {
                if (item == null || seen >= 20000) return;
                seen++;
                try
                {
                    foreach (PropertyCategory cat in item.PropertyCategories)
                    {
                        if (cat.DisplayName == null || cat.DisplayName.Equals(exceptCat, StringComparison.OrdinalIgnoreCase)) continue;
                        if (cat.Properties.Any(pr => string.Equals(pr.DisplayName, propName, StringComparison.OrdinalIgnoreCase)))
                            hits[cat.DisplayName] = hits.TryGetValue(cat.DisplayName, out int n) ? n + 1 : 1;
                    }
                    foreach (ModelItem child in item.Children) Walk(child);
                }
                catch { }
            }
            foreach (Model m in models) Walk(m.RootItem);
            return hits.Count == 0 ? null : hits.OrderByDescending(h => h.Value).First().Key;
        }

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
                        Level = p.Level,
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
                    Notifier.Warning("No active document found.");
                    return;
                }

                if (patternGroups == null || patternGroups.Count == 0)
                {
                    Notifier.Warning("No discipline patterns to create.");
                    return;
                }

                DocumentSelectionSets selSets = doc.SelectionSets;
                GroupItem root = selSets.RootItem as GroupItem;
                if (root == null)
                {
                    Notifier.Error("Cannot access selection sets.");
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
                    Notifier.Error("Failed to create '" + DISCIPLINES_FOLDER + "' folder.\n\nEnsure a model is open.");
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

                Notifier.Warning(msg);
            }
            catch (Exception ex)
            {
                Notifier.Error("Error in Function 1:\n\n" + ex.Message + "\n\n" + ex.StackTrace);
            }
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

                List<Model> models = ModelsForDisciplineSet(doc, FindDisciplineSet(doc, discipline), discipline);
                if (models.Count == 0) return result;

                var categorySet = new HashSet<string>();

                // Every node is visited, so large models take a while: name the
                // model being read, and let Cancel keep the categories found so far.
                using (var progress = new StepProgress("AutoNAV — Function 3  ·  " + discipline, string.Format(
                    "Reading property categories from {0} model(s)…", models.Count)))
                {
                    for (int i = 0; i < models.Count; i++)
                    {
                        if (!progress.Update((double)i / models.Count)) break;
                        try
                        {
                            if (!CollectPropertyCategories(models[i].RootItem, categorySet, result, progress, (double)i / models.Count))
                                break;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result.OrderBy(c => c.DisplayName).ToList();
        }

        // Recurses through item.Children like the original scan, each node in
        // its own try. Returns false when the user cancels.
        private static bool CollectPropertyCategories(
            ModelItem item,
            HashSet<string> categorySet,
            List<PropertyCategoryInfo> result,
            StepProgress progress,
            double fraction)
        {
            int n = 0;
            return CollectPropertyCategories(item, categorySet, result, progress, fraction, ref n);
        }

        private static bool CollectPropertyCategories(
            ModelItem item,
            HashSet<string> categorySet,
            List<PropertyCategoryInfo> result,
            StepProgress progress,
            double fraction,
            ref int n)
        {
            if (item == null) return true;
            if (++n % 2000 == 0 && !progress.Update(fraction)) return false;

            try
            {
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
            }
            catch { }

            try
            {
                foreach (ModelItem child in item.Children)
                    if (!CollectPropertyCategories(child, categorySet, result, progress, fraction, ref n)) return false;
            }
            catch { }
            return true;
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
                    Notifier.Warning("No active document found.");
                    return;
                }

                DocumentSelectionSets selSets = doc.SelectionSets;
                GroupItem root = selSets.RootItem as GroupItem;
                if (root == null)
                {
                    Notifier.Error("Cannot access selection sets.");
                    return;
                }

                FolderItem discSourceFolder = FindFolder(root, DISCIPLINES_FOLDER);
                if (discSourceFolder == null)
                {
                    Notifier.Warning(string.Format("'{0}' folder not found.\nRun Function 1 first.", DISCIPLINES_FOLDER));
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
                    Notifier.Warning(string.Format("Discipline '{0}' not found in {1}.", discipline, DISCIPLINES_FOLDER));
                    return;
                }

                SelectionSource discSource = selSets.CreateSelectionSource(discSearchSet);
                if (discSource == null)
                {
                    Notifier.Error("Failed to create selection source for discipline.");
                    return;
                }

                FolderItem clashFolder = FindFolder(root, CLASH_SETS_FOLDER);
                if (clashFolder == null)
                {
                    selSets.AddCopy(new FolderItem { DisplayName = CLASH_SETS_FOLDER });
                    clashFolder = FindFolder(doc.SelectionSets.RootItem as GroupItem, CLASH_SETS_FOLDER);
                }

                if (clashFolder == null)
                {
                    Notifier.Error(string.Format("Failed to access '{0}' folder.", CLASH_SETS_FOLDER));
                    return;
                }

                string customFolderName = discipline + " - Custom (" + propName + ")";
                int created = 0;
                int skipped = 0;
                bool cancelled = false;
                var errors = new List<string>();

                string step = "AutoNAV — Function 3  ·  " + discipline;
                List<Model> models = ModelsForDisciplineSet(doc, discSearchSet, discipline);
                ScanResult scan;
                using (var progress = new StepProgress(step, string.Format(
                    "Reading '{0}/{1}' values from {2} model(s)…", propCategory, propName, models.Count)))
                {
                    scan = ScanPropertyValues(doc, models, propCategory, propName, progress,
                        new Dictionary<string, ModelScan>(StringComparer.OrdinalIgnoreCase));
                }

                if (scan == null) cancelled = true;
                else if (scan.Values.Count > 0)
                {
                    List<string> values = scan.Values;
                    using (new StepProgress(step, string.Format("saving {0} search set(s)…", values.Count))) { }
                    var r = CommitSets(doc, () => FindFolder(doc.SelectionSets.RootItem as GroupItem, CLASH_SETS_FOLDER),
                        customFolderName, values,
                        i => PropertyValueSet(discSource, scan, values[i], values[i]),
                        null, errors);
                    created = r.Created;
                    skipped = r.Skipped;
                }
                else
                {
                    errors.Add(string.Format("No values found for property '{0}/{1}' in discipline '{2}' ({3} model(s)):\n{4}",
                        propCategory, propName, discipline, models.Count, scan.Diagnostics));
                }

                if (cancelled)
                {
                    Notifier.Warning("Function 3 cancelled — no search sets were created.");
                    return;
                }

                string msg = BuildStamp + "\n" + string.Format("Function 3 Complete - Custom Search Sets\n\nDiscipline: {0}\nProperty: {1}/{2}\nCreated: {3}  |  Skipped (existing): {4}\n\nFolder: {5}\\{6}",
                    discipline, propCategory, propName, created, skipped, CLASH_SETS_FOLDER, customFolderName);

                if (errors.Count > 0)
                    msg += "\n\nErrors:\n" + string.Join("\n", errors);

                Notifier.Warning(msg);
            }
            catch (Exception ex)
            {
                Notifier.Error(string.Format("Error in Function 3:\n\n{0}\n\n{1}", ex.Message, ex.StackTrace));
            }
        }
    }
}
