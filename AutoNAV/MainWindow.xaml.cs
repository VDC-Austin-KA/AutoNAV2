using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NavApp = Autodesk.Navisworks.Api.Application;
using NavGroupItem = Autodesk.Navisworks.Api.GroupItem;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using Autodesk.Navisworks.Api.Clash;

namespace AutoNAV
{
    public partial class MainWindow : Window
    {
        private SearchSetGenerator _searchSetGenerator;
        private ClashTestGeneratorEngine _clashEngine;
        private List<SearchSetGenerator.PropertyCategoryInfo> _currentPropertyCategories;

        public MainWindow()
        {
            InitializeComponent();
            _searchSetGenerator = new SearchSetGenerator();
            _clashEngine = new ClashTestGeneratorEngine();
            _currentPropertyCategories = new List<SearchSetGenerator.PropertyCategoryInfo>();

            // Route notifications from non-UI classes (SearchSetGenerator,
            // ClashTestGeneratorEngine, ClashGrouper) through the status panel
            // so they don't have to MessageBox.Show their own popups.
            Notifier.Sink = (msg, level, body) =>
            {
                Dispatcher.Invoke(() =>
                {
                    switch (level)
                    {
                        case NotifyLevel.Success: NotifySuccess(msg); break;
                        case NotifyLevel.Warning: NotifyWarning(msg); break;
                        case NotifyLevel.Error:   NotifyError(msg);   break;
                        case NotifyLevel.Result:  NotifyResult(msg, body); break;
                        default:                  NotifyInfo(msg);    break;
                    }
                });
            };

            Loaded += MainWindow_Loaded;
        }

        // Set true once the Rename tab has been opened at least once. Guards the
        // one-time discipline-list load that populates the Rename combo.
        private bool _renameTabInitialised;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDisciplineList();
            LoadFunction3Disciplines();
            LoadFunction3PropertyCategories();
        }

        // ── Discipline list helpers ───────────────────────────────────────────

        private void LoadDisciplineList()
        {
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;

                var disciplines = new List<string>();
                foreach (SavedItem item in doc.SelectionSets.RootItem.Children)
                    disciplines.Add(item.DisplayName);

                // Function 2
                CmbDisciplines.ItemsSource = null;
                CmbDisciplines.ItemsSource = disciplines;
                if (CmbDisciplines.Items.Count > 0)
                    CmbDisciplines.SelectedIndex = 0;

                // Clash grouper
                CmbClashTest.ItemsSource = null;
                CmbClashTest.ItemsSource = GetClashTestNames();
                if (CmbClashTest.Items.Count > 0)
                    CmbClashTest.SelectedIndex = 0;
            }
            catch { }
        }

        private List<string> GetClashTestNames()
        {
            var names = new List<string>();
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return names;
                var clash = doc.GetClash();

#if NW2025
                foreach (var t in clash.TestsData.Tests)
                    names.Add(t.DisplayName);
#else
                foreach (SavedItem item in clash.TestsData.TestsRoot.Children)
                    if (item is ClashTest ct)
                        names.Add(ct.DisplayName);
#endif
            }
            catch { }
            return names;
        }

        // ── Function 3 property helpers ───────────────────────────────────────

        private void LoadFunction3Disciplines()
        {
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;

                var disciplines = new List<string>();
                foreach (SavedItem item in doc.SelectionSets.RootItem.Children)
                    disciplines.Add(item.DisplayName);

                CmbF3Discipline.ItemsSource = null;
                CmbF3Discipline.ItemsSource = disciplines;
                if (CmbF3Discipline.Items.Count > 0)
                    CmbF3Discipline.SelectedIndex = 0;
            }
            catch { }
        }

        private void LoadFunction3PropertyCategories()
        {
            try
            {
                _currentPropertyCategories = SearchSetGenerator.GetAvailablePropertyCategories();
                var categoryNames = _currentPropertyCategories.Select(c => c.CategoryName).ToList();

                CmbF3Category.ItemsSource = null;
                CmbF3Category.ItemsSource = categoryNames;
                if (CmbF3Category.Items.Count > 0)
                    CmbF3Category.SelectedIndex = 0;
            }
            catch { }
        }

        private void OnF3CategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selectedCategory = CmbF3Category.SelectedItem as string;
                if (selectedCategory == null) return;

                var category = _currentPropertyCategories.FirstOrDefault(c => c.CategoryName == selectedCategory);
                if (category == null) return;

                CmbF3Property.ItemsSource = null;
                CmbF3Property.ItemsSource = category.PropertyNames;
                if (CmbF3Property.Items.Count > 0)
                    CmbF3Property.SelectedIndex = 0;
            }
            catch { }
        }

        // ── Function 2 property helpers ───────────────────────────────────────

        private void OnF2RefreshPropertiesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedDisciplines = GetSelectedDisciplines();
                if (!selectedDisciplines.Any())
                {
                    NotifyWarning("Please select at least one discipline first.");
                    return;
                }

                var categories = SearchSetGenerator.GetAvailablePropertyCategoriesForDisciplines(selectedDisciplines);
                var categoryNames = categories.Select(c => c.CategoryName).ToList();

                _currentPropertyCategories = categories;

                CmbF2Category.ItemsSource = null;
                CmbF2Category.ItemsSource = categoryNames;
                if (CmbF2Category.Items.Count > 0)
                    CmbF2Category.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                NotifyError("Error loading properties:\n\n" + ex.Message);
            }
        }

        private void OnF2CategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selectedCategory = CmbF2Category.SelectedItem as string;
                if (selectedCategory == null) return;

                var category = _currentPropertyCategories.FirstOrDefault(c => c.CategoryName == selectedCategory);
                if (category == null) return;

                CmbF2Property.ItemsSource = null;
                CmbF2Property.ItemsSource = category.PropertyNames;
                if (CmbF2Property.Items.Count > 0)
                    CmbF2Property.SelectedIndex = 0;
            }
            catch { }
        }

        private List<string> GetSelectedDisciplines()
        {
            // Return all discipline names from the ListBox selection (or all if none selected)
            var selected = new List<string>();
            if (LstDisciplines == null) return selected;

            foreach (var item in LstDisciplines.SelectedItems)
                selected.Add(item.ToString());

            if (!selected.Any())
                foreach (var item in LstDisciplines.Items)
                    selected.Add(item.ToString());

            return selected;
        }

        // ── Core function handlers ────────────────────────────────────────────

        private void OnFunction1Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchSetGenerator.GenerateFunction1SearchSets();
                MacroNAVBridge.RecordFunction1();
                LoadDisciplineList();
                LoadFunction3Disciplines();
            }
            catch (Exception ex)
            {
                NotifyError("Error in Function 1:\n\n" + ex.Message);
            }
        }

        private void OnFunction2Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var discs = GetSelectedDisciplines();
                if (!discs.Any())
                {
                    NotifyWarning("No disciplines selected.");
                    return;
                }

                var propCat  = CmbF2Category.SelectedItem as string ?? string.Empty;
                var propName = CmbF2Property.SelectedItem as string ?? string.Empty;

                if (string.IsNullOrWhiteSpace(propCat) || string.IsNullOrWhiteSpace(propName))
                {
                    NotifyWarning("Please refresh and select a property category and name first.");
                    return;
                }

                SearchSetGenerator.GenerateFunction2SearchSets(discs, propCat, propName);
                MacroNAVBridge.RecordFunction2(string.Join(",", discs), propCat, propName);
                LoadDisciplineList();
                LoadFunction3Disciplines();
            }
            catch (Exception ex)
            {
                NotifyError("Error in Function 2:\n\n" + ex.Message);
            }
        }

        private void OnFunction3Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var discipline = CmbF3Discipline.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(discipline))
                {
                    NotifyWarning("Please select a discipline.");
                    return;
                }

                var category = CmbF3Category.SelectedItem as string ?? string.Empty;
                var propName = CmbF3Property.SelectedItem as string ?? string.Empty;

                if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(propName))
                {
                    NotifyWarning("Please select a property category and name.");
                    return;
                }

                SearchSetGenerator.GenerateCustomSearchSets(discipline, category, propName);
                MacroNAVBridge.RecordFunction3(discipline, category, propName);
            }
            catch (Exception ex)
            {
                NotifyError("Error in Function 3:\n\n" + ex.Message);
            }
        }

        private async void OnFunction4Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true, "Generating clash tests…");
                await Task.Run(() => _clashEngine.GenerateClashTests());
                MacroNAVBridge.RecordClashTestGen();
                LoadDisciplineList();
                SetBusy(false);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                NotifyError("Error generating clash tests:\n\n" + ex.Message);
            }
        }

        private async void OnFunction5Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true, "Running clash tests and grouping results…");
                await Task.Run(() => _clashEngine.RunClashTestsAndGroupResults());
                MacroNAVBridge.RecordClashRunAndGroup(Function6PrimaryMode.ToString(), Function6SubGroupingMode.ToString());
                SetBusy(false);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                NotifyError("Error running clash tests:\n\n" + ex.Message);
            }
        }

        // ── Clash grouper UI ──────────────────────────────────────────────────

        private ClashGrouper.PrimaryGroupingMode Function6PrimaryMode =>
            CmbF6Primary.SelectedItem is string s
                ? (ClashGrouper.PrimaryGroupingMode)Enum.Parse(typeof(ClashGrouper.PrimaryGroupingMode), s)
                : ClashGrouper.PrimaryGroupingMode.Discipline;

        private ClashGrouper.SubGroupingMode Function6SubGroupingMode =>
            CmbF6Sub.SelectedItem is string s
                ? (ClashGrouper.SubGroupingMode)Enum.Parse(typeof(ClashGrouper.SubGroupingMode), s)
                : ClashGrouper.SubGroupingMode.None;

        private void LoadClashGrouperOptions()
        {
            CmbF6Primary.ItemsSource = Enum.GetNames(typeof(ClashGrouper.PrimaryGroupingMode));
            CmbF6Primary.SelectedIndex = 0;
            CmbF6Sub.ItemsSource = Enum.GetNames(typeof(ClashGrouper.SubGroupingMode));
            CmbF6Sub.SelectedIndex = 0;
        }

        private async void OnFunction6Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var testName = CmbClashTest.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(testName))
                {
                    NotifyWarning("Please select a clash test to group.");
                    return;
                }

                var primary = Function6PrimaryMode;
                var sub     = Function6SubGroupingMode;

                SetBusy(true, $"Grouping '{testName}'…");
                await Task.Run(() => ClashGrouper.GroupClashes(testName, primary, sub));
                SetBusy(false);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                NotifyError("Error grouping clashes:\n\n" + ex.Message);
            }
        }

        private async void OnFunction7Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var testName = CmbClashTest.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(testName))
                {
                    NotifyWarning("Please select a clash test to ungroup.");
                    return;
                }

                SetBusy(true, $"Ungrouping '{testName}'…");
                await Task.Run(() => ClashGrouper.UnGroupClashes(testName));
                SetBusy(false);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                NotifyError("Error ungrouping clashes:\n\n" + ex.Message);
            }
        }

        // ── Rename tab ────────────────────────────────────────────────────────

        private void OnRenameTabSelected(object sender, RoutedEventArgs e)
        {
            if (_renameTabInitialised) return;
            _renameTabInitialised = true;
            LoadRenameDisciplines();
        }

        private void LoadRenameDisciplines()
        {
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;

                var disciplines = new List<string>();
                foreach (SavedItem item in doc.SelectionSets.RootItem.Children)
                    disciplines.Add(item.DisplayName);

                CmbRenameDiscipline.ItemsSource = null;
                CmbRenameDiscipline.ItemsSource = disciplines;
                if (CmbRenameDiscipline.Items.Count > 0)
                    CmbRenameDiscipline.SelectedIndex = 0;
            }
            catch { }
        }

        private void OnRenamePreviewClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var discipline = CmbRenameDiscipline.SelectedItem as string;
                var prefix     = TxtRenamePrefix.Text?.Trim() ?? string.Empty;
                var suffix     = TxtRenameSuffix.Text?.Trim() ?? string.Empty;
                var separator  = TxtRenameSeparator.Text ?? string.Empty;

                var previews = SearchSetGenerator.PreviewRename(discipline, prefix, suffix, separator);

                LstRenamePreview.ItemsSource = null;
                LstRenamePreview.ItemsSource = previews;
            }
            catch (Exception ex)
            {
                NotifyError("Preview error:\n\n" + ex.Message);
            }
        }

        private void OnRenameApplyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var discipline = CmbRenameDiscipline.SelectedItem as string;
                var prefix     = TxtRenamePrefix.Text?.Trim() ?? string.Empty;
                var suffix     = TxtRenameSuffix.Text?.Trim() ?? string.Empty;
                var separator  = TxtRenameSeparator.Text ?? string.Empty;

                SearchSetGenerator.RenameSearchSets(discipline, prefix, suffix, separator);
                LoadDisciplineList();
                LoadFunction3Disciplines();
                LoadRenameDisciplines();
            }
            catch (Exception ex)
            {
                NotifyError("Rename error:\n\n" + ex.Message);
            }
        }

        // ── Clash results tab ─────────────────────────────────────────────────

        private bool _clashResultsTabInitialised;

        private void OnClashResultsTabSelected(object sender, RoutedEventArgs e)
        {
            if (_clashResultsTabInitialised) return;
            _clashResultsTabInitialised = true;
            RefreshClashResultsTab();
        }

        private void OnRefreshClashResultsClick(object sender, RoutedEventArgs e)
            => RefreshClashResultsTab();

        private void RefreshClashResultsTab()
        {
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;

                var clash = doc.GetClash();
                var rows  = new List<ClashResultRow>();

#if NW2025
                foreach (var test in clash.TestsData.Tests)
                {
                    int total    = test.Results?.Count ?? 0;
                    int active   = test.Results?.Count(r => r.Status == ClashResultStatus.Active)  ?? 0;
                    int approved = test.Results?.Count(r => r.Status == ClashResultStatus.Approved) ?? 0;
                    int reviewed = test.Results?.Count(r => r.Status == ClashResultStatus.Reviewed) ?? 0;
                    int resolved = test.Results?.Count(r => r.Status == ClashResultStatus.Resolved) ?? 0;
                    rows.Add(new ClashResultRow
                    {
                        TestName = test.DisplayName,
                        Total    = total,
                        Active   = active,
                        Approved = approved,
                        Reviewed = reviewed,
                        Resolved = resolved,
                    });
                }
#else
                foreach (SavedItem item in clash.TestsData.TestsRoot.Children)
                {
                    if (!(item is ClashTest ct)) continue;
                    int total    = ct.Results?.Count ?? 0;
                    int active   = ct.Results?.Count(r => r.Status == ClashResultStatus.Active)   ?? 0;
                    int approved = ct.Results?.Count(r => r.Status == ClashResultStatus.Approved) ?? 0;
                    int reviewed = ct.Results?.Count(r => r.Status == ClashResultStatus.Reviewed) ?? 0;
                    int resolved = ct.Results?.Count(r => r.Status == ClashResultStatus.Resolved) ?? 0;
                    rows.Add(new ClashResultRow
                    {
                        TestName = ct.DisplayName,
                        Total    = total,
                        Active   = active,
                        Approved = approved,
                        Reviewed = reviewed,
                        Resolved = resolved,
                    });
                }
#endif

                DgClashResults.ItemsSource = null;
                DgClashResults.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                NotifyError("Error loading clash results:\n\n" + ex.Message);
            }
        }

        private class ClashResultRow
        {
            public string TestName { get; set; }
            public int Total    { get; set; }
            public int Active   { get; set; }
            public int Approved { get; set; }
            public int Reviewed { get; set; }
            public int Resolved { get; set; }
        }

        // ── Clash assign tab ──────────────────────────────────────────────────

        private bool _clashAssignTabInitialised;

        private void OnClashAssignTabSelected(object sender, RoutedEventArgs e)
        {
            if (_clashAssignTabInitialised) return;
            _clashAssignTabInitialised = true;
            RefreshClashAssignTab();
        }

        private void OnRefreshClashAssignClick(object sender, RoutedEventArgs e)
            => RefreshClashAssignTab();

        private void RefreshClashAssignTab()
        {
            try
            {
                CmbAssignTest.ItemsSource = null;
                CmbAssignTest.ItemsSource = GetClashTestNames();
                if (CmbAssignTest.Items.Count > 0)
                    CmbAssignTest.SelectedIndex = 0;
            }
            catch { }
        }

        private void OnAssignAllClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var testName = CmbAssignTest.SelectedItem as string;
                var assignee = TxtAssignee.Text?.Trim();
                var status   = (CmbAssignStatus.SelectedItem as ComboBoxItem)?.Content?.ToString();

                if (string.IsNullOrWhiteSpace(testName))
                { NotifyWarning("Select a test."); return; }
                if (string.IsNullOrWhiteSpace(assignee))
                { NotifyWarning("Enter an assignee."); return; }

                var doc   = NavApp.ActiveDocument;
                var clash = doc.GetClash();

#if NW2025
                var test = clash.TestsData.Tests.FirstOrDefault(t => t.DisplayName == testName);
                if (test?.Results == null) { NotifyWarning("Test not found or has no results."); return; }
                doc.Models.OverrideLock = false;
                clash.TestsData.BeginChange(null);
                foreach (var r in test.Results)
                {
                    r.AssignedTo = assignee;
                    if (status != null && Enum.TryParse<ClashResultStatus>(status, out var st))
                        r.Status = st;
                }
                clash.TestsData.EndChange();
#else
                ClashTest ct = null;
                foreach (SavedItem item in clash.TestsData.TestsRoot.Children)
                    if (item is ClashTest c && c.DisplayName == testName) { ct = c; break; }
                if (ct?.Results == null) { NotifyWarning("Test not found or has no results."); return; }
                doc.Models.OverrideLock = false;
                using (var t = clash.TestsData.BeginChange(null))
                {
                    foreach (var r in ct.Results)
                    {
                        r.AssignedTo = new Assignee { Name = assignee };
                        if (status != null && Enum.TryParse<ClashResultStatus>(status, out var st))
                            r.Status = st;
                    }
                }
#endif
                NotifySuccess($"Assigned {testName} results to '{assignee}'.");
            }
            catch (Exception ex)
            {
                NotifyError("Error assigning:\n\n" + ex.Message);
            }
        }

        // ── Selection set viewer tab ──────────────────────────────────────────

        private bool _selectionSetTabInitialised;

        private void OnSelectionSetTabSelected(object sender, RoutedEventArgs e)
        {
            if (_selectionSetTabInitialised) return;
            _selectionSetTabInitialised = true;
            RefreshSelectionSetsTab();
        }

        private void OnRefreshSelectionSetsClick(object sender, RoutedEventArgs e)
            => RefreshSelectionSetsTab();

        private void RefreshSelectionSetsTab()
        {
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;

                var rows = new List<SelectionSetRow>();
                foreach (SavedItem item in doc.SelectionSets.RootItem.Children)
                    BuildSelectionSetRows(rows, item, 0);

                DgSelectionSets.ItemsSource = null;
                DgSelectionSets.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                NotifyError("Error loading selection sets:\n\n" + ex.Message);
            }
        }

        private void BuildSelectionSetRows(List<SelectionSetRow> rows, SavedItem item, int depth)
        {
            var indent = new string(' ', depth * 4);
            if (item is SelectionSet ss)
            {
                int count = 0;
                try { count = ss.GetSelection().Count; } catch { }
                rows.Add(new SelectionSetRow { Name = indent + ss.DisplayName, ItemCount = count, Type = "Selection Set" });
            }
            else if (item is GroupItem gi)
            {
                rows.Add(new SelectionSetRow { Name = indent + gi.DisplayName, ItemCount = 0, Type = "Group" });
                foreach (SavedItem child in gi.Children)
                    BuildSelectionSetRows(rows, child, depth + 1);
            }
        }

        private class SelectionSetRow
        {
            public string Name      { get; set; }
            public int    ItemCount { get; set; }
            public string Type      { get; set; }
        }

        // ── Model info tab ────────────────────────────────────────────────────

        private bool _modelInfoTabInitialised;

        private void OnModelInfoTabSelected(object sender, RoutedEventArgs e)
        {
            if (_modelInfoTabInitialised) return;
            _modelInfoTabInitialised = true;
            RefreshModelInfoTab();
        }

        private void OnRefreshModelInfoClick(object sender, RoutedEventArgs e)
            => RefreshModelInfoTab();

        private void RefreshModelInfoTab()
        {
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;

                var rows = new List<ModelInfoRow>();
                rows.Add(new ModelInfoRow { Key = "Title",          Value = doc.Title ?? "" });
                rows.Add(new ModelInfoRow { Key = "File Name",      Value = doc.FileName ?? "" });
                rows.Add(new ModelInfoRow { Key = "Model Count",    Value = doc.Models.Count.ToString() });

                foreach (var m in doc.Models)
                {
                    rows.Add(new ModelInfoRow
                    {
                        Key   = "  Model",
                        Value = m.FileName ?? m.RootItem?.DisplayName ?? "(unknown)"
                    });
                }

                DgModelInfo.ItemsSource = null;
                DgModelInfo.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                NotifyError("Error loading model info:\n\n" + ex.Message);
            }
        }

        private class ModelInfoRow
        {
            public string Key   { get; set; }
            public string Value { get; set; }
        }

        // ── Property explorer tab ─────────────────────────────────────────────

        private bool _propExplorerInitialised;

        private void OnPropertyExplorerTabSelected(object sender, RoutedEventArgs e)
        {
            if (_propExplorerInitialised) return;
            _propExplorerInitialised = true;
            RefreshPropertyExplorerTab();
        }

        private void OnRefreshPropertyExplorerClick(object sender, RoutedEventArgs e)
            => RefreshPropertyExplorerTab();

        private void RefreshPropertyExplorerTab()
        {
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null) return;

                var rows = SearchSetGenerator.GetAvailablePropertyCategories();
                DgPropExplorer.ItemsSource = null;
                DgPropExplorer.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                NotifyError("Error loading properties:\n\n" + ex.Message);
            }
        }

        // ── Status panel helpers ──────────────────────────────────────────────

        private void NotifySuccess(string msg) => AppendStatus(msg, "#44BB44");
        private void NotifyWarning(string msg) => AppendStatus(msg, "#FFAA00");
        private void NotifyError(string msg)   => AppendStatus(msg, "#FF5555");
        private void NotifyInfo(string msg)    => AppendStatus(msg, "#CCCCCC");

        private void NotifyResult(string msg, string body)
        {
            AppendStatus(msg, "#88DDFF");
            if (!string.IsNullOrWhiteSpace(body))
                AppendStatus(body, "#AAAAAA");
        }

        private void AppendStatus(string msg, string hexColor)
        {
            try
            {
                var para = new System.Windows.Documents.Paragraph();
                para.Inlines.Add(new System.Windows.Documents.Run(msg)
                {
                    Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(hexColor))
                });
                TxtStatus.Document.Blocks.Add(para);
                TxtStatus.ScrollToEnd();
            }
            catch { }
        }

        private void OnClearStatusClick(object sender, RoutedEventArgs e)
        {
            try { TxtStatus.Document.Blocks.Clear(); } catch { }
        }

        private void SetBusy(bool busy, string msg = null)
        {
            BtnF4.IsEnabled = !busy;
            BtnF5.IsEnabled = !busy;
            BtnF6.IsEnabled = !busy;
            BtnF7.IsEnabled = !busy;
            if (busy && msg != null) NotifyInfo(msg);
        }
    }
}
