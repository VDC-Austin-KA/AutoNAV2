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
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDisciplineList();
            LoadFunction3Disciplines();
            LoadClashTests();
        }

        private void LoadClashTests()
        {
            try
            {
                if (cmbClashTest == null) return;
                
                cmbClashTest.Items.Clear();
                cmbClashTest.Items.Add(new ComboBoxItem { Content = "-- Select Clash Test --", Tag = "" });
                
                Document doc = NavApp.ActiveDocument;
                if (doc != null)
                {
                    DocumentClash documentClash = doc.GetClash();
                    if (documentClash != null && documentClash.TestsData != null)
                    {
                        foreach (ClashTest test in ClashCompat.EnumerateTests(documentClash.TestsData))
                        {
                            cmbClashTest.Items.Add(new ComboBoxItem 
                            { 
                                Content = test.DisplayName, 
                                Tag = test.DisplayName 
                            });
                        }
                        
                        if (cmbClashTest.Items.Count > 1)
                        {
                            cmbClashTest.IsEnabled = true;
                            cmbClashTest.SelectedIndex = 0;
                            cmbClashTest.SelectionChanged -= OnClashTestSelectionChanged;
                            cmbClashTest.SelectionChanged += OnClashTestSelectionChanged;
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AutoNAV] LoadClashTests error: {ex.Message}"); }
        }

        #region Function 2 Discipline List Management

        private void LoadDisciplineList()
        {
            disciplineCheckboxPanel.Children.Clear();
            List<string> disciplines = TryGetDisciplinesFromDocument();

            if (disciplines.Count == 0)
            {
                disciplineCheckboxPanel.Children.Add(new TextBlock
                {
                    Text = "No disciplines loaded — run Function 1 first.",
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(2, 2, 0, 2)
                });
                return;
            }

            foreach (string disc in disciplines)
            {
                StackPanel row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 3)
                };

                CheckBox cb = new CheckBox
                {
                    Tag = disc,
                    IsChecked = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 110
                };
                TextBlock cbText = new TextBlock { Text = disc, FontSize = 11 };
                cb.Content = cbText;

                ComboBox cmb = new ComboBox
                {
                    Tag = disc,
                    Margin = new Thickness(8, 0, 0, 0),
                    Width = 240,
                    Height = 24,
                    FontSize = 11
                };

                // Property options come from the canonical discipline the
                // search set was tagged with by Function 1.  When the registry
                // has no entry (e.g. the user re-opened AutoNAV without
                // re-running Function 1) we recover by looking the search set
                // name up in the dictionary one more time.
                string canonical = null;
                SearchSetGenerator.DisciplineRegistry.TryGetValue(disc, out canonical);
                if (string.IsNullOrEmpty(canonical))
                {
                    string _code; string _canon;
                    if (SearchSetGenerator.TryMatchDiscipline(disc, out _code, out _canon))
                        canonical = _canon;
                }

                if (!string.IsNullOrEmpty(canonical))
                {
                    var disciplineLabel = new TextBlock
                    {
                        Text = "(" + canonical + ")",
                        FontSize = 10,
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyles.Italic,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0),
                    };
                    row.Children.Add(disciplineLabel);
                }

                var options = SearchSetGenerator.PropertyOptionsFor(canonical);
                foreach (var opt in options)
                {
                    cmb.Items.Add(new ComboBoxItem
                    {
                        Content = opt.Label,
                        Tag = opt.Category + "|" + opt.Property,
                    });
                }
                cmb.SelectedIndex = 0;

                row.Children.Add(cb);
                row.Children.Add(cmb);
                disciplineCheckboxPanel.Children.Add(row);
            }
        }

        private List<string> TryGetDisciplinesFromDocument()
        {
            List<string> result = new List<string>();
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null) return result;

                NavGroupItem root = doc.SelectionSets.RootItem as NavGroupItem;
                if (root == null) return result;

                foreach (SavedItem item in root.Children)
                {
                    if (item.DisplayName.Trim().Equals("1. DISCIPLINES", StringComparison.OrdinalIgnoreCase))
                    {
                        NavGroupItem group = item as NavGroupItem;
                        if (group != null)
                        {
                            foreach (SavedItem child in group.Children)
                                result.Add(child.DisplayName.Trim());
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AutoNAV] TryGetDisciplinesFromDocument error: {ex.Message}"); }
            return result;
        }

        private List<string[]> GetSelectedDisciplineProps()
        {
            var result = new List<string[]>();
            foreach (var row in disciplineCheckboxPanel.Children)
            {
                if (row is StackPanel sp && sp.Children.Count >= 2)
                {
                    if (sp.Children[0] is CheckBox cb && cb.IsChecked == true)
                    {
                        string disc = cb.Tag as string;
                        if (sp.Children[1] is ComboBox cmb && cmb.SelectedItem is ComboBoxItem sel)
                        {
                            string tag = sel.Tag as string;
                            if (!string.IsNullOrEmpty(tag) && tag.Contains("|"))
                            {
                                string[] parts = tag.Split('|');
                                result.Add(new string[] { disc, parts[0], parts[1] });
                            }
                        }
                    }
                }
            }
            return result;
        }

        #endregion

        #region Function 1, 2, 3 Event Handlers (Search Set Generation)

        private void OnFunction1Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchSetGenerator.GenerateFunction1SearchSets();
                LoadDisciplineList();
                LoadFunction3Disciplines();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Function 1:\n\n" + ex.Message,
                    "Function 1 Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnFunction2Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<string[]> selectedProps = GetSelectedDisciplineProps();

                if (selectedProps.Count == 0)
                {
                    MessageBox.Show("Please select at least one discipline.", "Function 2",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var byType = selectedProps
                    .GroupBy(x => x[1] + "|" + x[2])
                    .ToList();

                foreach (var group in byType)
                {
                    List<string> discs = group.Select(x => x[0]).ToList();
                    string[] parts = group.Key.Split('|');
                    string propCat = parts[0];
                    string propName = parts.Length > 1 ? parts[1] : "Category";

                    SearchSetGenerator.GenerateFunction2SearchSets(discs, propCat, propName);
                }

                MessageBox.Show("Function 2 complete.", "Function 2",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Function 2:\n\n" + ex.Message,
                    "Function 2 Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnFunction3Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var discItem = cmbFunction3Discipline.SelectedItem as ComboBoxItem;
                var catItem = cmbFunction3PropertyCategory.SelectedItem as ComboBoxItem;
                var propItem = cmbFunction3PropertyName.SelectedItem as ComboBoxItem;

                string discipline = discItem?.Tag as string;
                string category = catItem?.Tag as string;
                string propTag = propItem?.Tag as string;

                if (string.IsNullOrEmpty(discipline) || string.IsNullOrEmpty(category) || string.IsNullOrEmpty(propTag))
                {
                    MessageBox.Show("Please select a discipline, property category, and property name.",
                        "Function 3", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string[] propParts = propTag.Split('|');
                string propName = propParts.Length > 1 ? propParts[1] : propParts[0];

                SetStatus("Running Function 3 - creating custom search sets...");
                SearchSetGenerator.GenerateCustomSearchSets(discipline, category, propName);
                SetStatus("Function 3 complete.");
            }
            catch (Exception ex)
            {
                SetStatus("Function 3 failed.");
                MessageBox.Show("Error in Function 3:\n\n" + ex.Message, "Function 3 Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Function 3 UI Handlers

        private void LoadFunction3Disciplines()
        {
            cmbFunction3Discipline.Items.Clear();
            cmbFunction3Discipline.Items.Add(new ComboBoxItem { Content = "-- Select Discipline --", IsSelected = true });

            var disciplines = SearchSetGenerator.GetAvailableDisciplines();
            foreach (string disc in disciplines)
            {
                cmbFunction3Discipline.Items.Add(new ComboBoxItem { Content = disc, Tag = disc });
            }
        }

        private void OnFunction3DisciplineChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbFunction3PropertyCategory == null || cmbFunction3PropertyName == null || btnFunction3 == null)
                return;

            cmbFunction3PropertyCategory.Items.Clear();
            cmbFunction3PropertyCategory.Items.Add(new ComboBoxItem { Content = "-- Select Category --", IsSelected = true });
            cmbFunction3PropertyCategory.IsEnabled = false;

            cmbFunction3PropertyName.Items.Clear();
            cmbFunction3PropertyName.Items.Add(new ComboBoxItem { Content = "-- Select Property --", IsSelected = true });
            cmbFunction3PropertyName.IsEnabled = false;
            btnFunction3.IsEnabled = false;

            var selectedItem = cmbFunction3Discipline.SelectedItem as ComboBoxItem;
            string selectedDiscipline = selectedItem?.Tag as string;

            if (string.IsNullOrEmpty(selectedDiscipline)) return;

            _currentPropertyCategories = SearchSetGenerator.GetPropertyCategoriesForDiscipline(selectedDiscipline);
            if (_currentPropertyCategories == null) return;

            foreach (var cat in _currentPropertyCategories)
            {
                cmbFunction3PropertyCategory.Items.Add(new ComboBoxItem
                {
                    Content = cat.DisplayName,
                    Tag = cat.DisplayName
                });
            }

            if (cmbFunction3PropertyCategory.Items.Count > 1)
                cmbFunction3PropertyCategory.IsEnabled = true;
        }

        private void OnFunction3PropertyCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbFunction3PropertyName == null || btnFunction3 == null)
                return;

            cmbFunction3PropertyName.Items.Clear();
            cmbFunction3PropertyName.Items.Add(new ComboBoxItem { Content = "-- Select Property --", IsSelected = true });
            cmbFunction3PropertyName.IsEnabled = false;
            btnFunction3.IsEnabled = false;

            var selectedCatItem = cmbFunction3PropertyCategory.SelectedItem as ComboBoxItem;
            string selectedCategory = selectedCatItem?.Tag as string;

            if (string.IsNullOrEmpty(selectedCategory)) return;
            if (_currentPropertyCategories == null) return;

            var category = _currentPropertyCategories.FirstOrDefault(c =>
                c.DisplayName.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase));

            if (category == null || category.Properties.Count == 0) return;

            foreach (var prop in category.Properties)
            {
                cmbFunction3PropertyName.Items.Add(new ComboBoxItem
                {
                    Content = prop.DisplayName,
                    Tag = prop.InternalName + "|" + prop.DisplayName
                });
            }

            if (cmbFunction3PropertyName.Items.Count > 1)
                cmbFunction3PropertyName.IsEnabled = true;
        }

        private void OnFunction3PropertyNameChanged(object sender, SelectionChangedEventArgs e)
        {
            if (btnFunction3 == null) return;
            var propItem = cmbFunction3PropertyName?.SelectedItem as ComboBoxItem;
            btnFunction3.IsEnabled = propItem?.Tag != null;
        }

        #endregion

        #region Function 4 Event Handlers (Clash Test Generation)

        private void OnFunction4Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _clashEngine.GenerateClashTests();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error generating clash tests:\n\n" + ex.Message,
                    "Function 4 - Clash Generation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnFunction5Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _clashEngine.RunClashTestsAndGroupResults();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error running clash tests and grouping results:\n\n" + ex.Message,
                    "Function 5 - Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Function 5 Event Handlers (Run Tests & Group Clashes)

        private void OnUpdateAllTestsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBox.Show(
                    "To run clash tests, please use the Clash Detective pane in Navisworks:\n\n" +
                    "1. Open the Clash Detective pane\n" +
                    "2. Select the tests you want to run\n" +
                    "3. Click 'Run Tests' button\n\n" +
                    "The Group Clashes feature can then be used to organize the results.",
                    "Update All Tests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error:\n\n" + ex.Message,
                    "Update All Tests Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Discipline Selection Event Handlers

        private void ForEachDisciplineCheckBox(Func<bool, bool> setter)
        {
            foreach (UIElement child in disciplineCheckboxPanel.Children)
                if (child is StackPanel sp && sp.Children.Count >= 1 && sp.Children[0] is CheckBox cb)
                    cb.IsChecked = setter(cb.IsChecked.GetValueOrDefault());
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e) =>
            ForEachDisciplineCheckBox(_ => true);

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e) =>
            ForEachDisciplineCheckBox(_ => false);

        private void BtnInvertSelection_Click(object sender, RoutedEventArgs e) =>
            ForEachDisciplineCheckBox(x => !x);

        private void BtnRefreshDisciplines_Click(object sender, RoutedEventArgs e)
        {
            LoadDisciplineList();
            LoadFunction3Disciplines();
        }

        #endregion

        #region Function 6 Event Handlers (Walls / Floors Grouping)

        private void OnFunction6Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirm = MessageBox.Show(
                    "Before continuing, make sure all clash tests have been run in Clash Detective.\n\n" +
                    "This will:\n" +
                    "  1. Group clashes into Walls and Floors using the matching search sets\n" +
                    "  2. Leave all other clashes ungrouped (ready for Sherlock Distill)\n\n" +
                    "Continue?",
                    "Function 6 — Walls / Floors Grouping",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;

                SetStatus("Running Function 6 — grouping by Walls / Floors...");
                string result = ClashGrouper.GroupAllTestsByWallsAndFloors();
                SetStatus("Function 6 complete.");

                MessageBox.Show(result, "Function 6 — Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus("Function 6 failed.");
                MessageBox.Show("Error in Function 6:\n\n" + ex.Message,
                    "Function 6 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Function 5 Event Handlers

        private void OnGroupClashesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = cmbClashTest.SelectedItem as ComboBoxItem;
                string testName = selectedItem?.Tag as string;

                if (string.IsNullOrEmpty(testName))
                {
                    MessageBox.Show("Please select a clash test to group.", "Group Clashes",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var groupingModeItem = cmbGroupingMode.SelectedItem as ComboBoxItem;
                var subGroupingModeItem = cmbSubGroupingMode.SelectedItem as ComboBoxItem;
                
                string groupingModeStr = groupingModeItem?.Tag as string;
                string subGroupingModeStr = subGroupingModeItem?.Tag as string;
                bool keepExisting = chkKeepExistingGroups.IsChecked == true;

                ClashGrouper.GroupingMode groupingMode = ParseGroupingMode(groupingModeStr);
                ClashGrouper.GroupingMode subGroupingMode = ParseGroupingMode(subGroupingModeStr);

                if (groupingMode == ClashGrouper.GroupingMode.None && subGroupingMode == ClashGrouper.GroupingMode.None)
                {
                    MessageBox.Show("Please select at least one grouping mode (Primary or Sub-Grouping).", "Group Clashes",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    MessageBox.Show("Clash Detective is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ClashTest selectedTest = null;
                foreach (ClashTest test in ClashCompat.EnumerateTests(documentClash.TestsData))
                {
                    if (test.DisplayName == testName)
                    {
                        selectedTest = test;
                        break;
                    }
                }

                if (selectedTest == null)
                {
                    MessageBox.Show("Clash test not found: " + testName, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string template = GetSelectedNamingTemplate();
                var newStatuses = GetNewStatusFilter();
                var regroupStatuses = GetRegroupStatusFilter();
                ClashGrouper.GroupClashes(selectedTest, groupingMode, subGroupingMode, keepExisting, template,
                                          newStatuses, regroupStatuses);

                MessageBox.Show("Clashes grouped successfully!\n\nCheck Clash Detective to see the results.",
                    "Group Clashes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error grouping clashes:\n\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnGroupAllTestsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var groupingModeItem = cmbGroupingMode.SelectedItem as ComboBoxItem;
                var subGroupingModeItem = cmbSubGroupingMode.SelectedItem as ComboBoxItem;
                
                string groupingModeStr = groupingModeItem?.Tag as string;
                string subGroupingModeStr = subGroupingModeItem?.Tag as string;
                bool keepExisting = chkKeepExistingGroups.IsChecked == true;

                ClashGrouper.GroupingMode groupingMode = ParseGroupingMode(groupingModeStr);
                ClashGrouper.GroupingMode subGroupingMode = ParseGroupingMode(subGroupingModeStr);

                if (groupingMode == ClashGrouper.GroupingMode.None && subGroupingMode == ClashGrouper.GroupingMode.None)
                {
                    MessageBox.Show("Please select at least one grouping mode (Primary or Sub-Grouping).", "Group All Tests",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    MessageBox.Show("Clash Detective is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var tests = ClashCompat.GetTopLevelTests(documentClash.TestsData);
                if (tests.Count == 0)
                {
                    MessageBox.Show("No clash tests found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string template = GetSelectedNamingTemplate();
                var newStatuses = GetNewStatusFilter();
                var regroupStatuses = GetRegroupStatusFilter();
                int groupedCount = 0;
                int failedCount = 0;

                foreach (ClashTest test in tests)
                {
                    try
                    {
                        ClashGrouper.GroupClashes(test, groupingMode, subGroupingMode, keepExisting, template,
                                                  newStatuses, regroupStatuses);
                        groupedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        System.Diagnostics.Debug.WriteLine($"[AutoNAV] GroupClashes failed for '{test.DisplayName}': {ex.Message}");
                    }
                }

                MessageBox.Show($"Grouping complete!\n\nTests Grouped: {groupedCount}\nFailed: {failedCount}",
                    "Group All Tests", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error:\n\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnUngroupClashesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = cmbClashTest.SelectedItem as ComboBoxItem;
                string testName = selectedItem?.Tag as string;

                if (string.IsNullOrEmpty(testName))
                {
                    MessageBox.Show("Please select a clash test to ungroup.", "Ungroup Clashes",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    MessageBox.Show("Clash Detective is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ClashTest selectedTest = null;
                foreach (ClashTest test in ClashCompat.EnumerateTests(documentClash.TestsData))
                {
                    if (test.DisplayName == testName)
                    {
                        selectedTest = test;
                        break;
                    }
                }

                if (selectedTest == null)
                {
                    MessageBox.Show("Clash test not found: " + testName, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ClashGrouper.UnGroupClashes(selectedTest);

                MessageBox.Show("Clashes have been ungrouped (reset to individual results).",
                    "Ungroup Clashes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error:\n\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnClashTestSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        // ─────────────────────────────────────────────────────────────────────
        // Naming-template UI helpers
        // ─────────────────────────────────────────────────────────────────────

        // The selected template's literal string (Tag of the selected ComboBoxItem),
        // or "" for the "legacy auto-naming" entry.
        private string GetSelectedNamingTemplate()
        {
            var cb = cmbNamingTemplate?.SelectedItem as ComboBoxItem;
            return cb?.Tag as string ?? "";
        }

        // Reads the "New Clashes" status checkboxes; empty set means no filter.
        private HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus> GetNewStatusFilter()
        {
            var s = new HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus>();
            if (chkNewStatusNew?.IsChecked      == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.New);
            if (chkNewStatusActive?.IsChecked   == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.Active);
            if (chkNewStatusReviewed?.IsChecked == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.Reviewed);
            return s;
        }

        // Reads the "Regroup & Rename" status checkboxes; only meaningful when the
        // section is enabled (no New-Clashes status selected). Empty set = nothing
        // to regroup.
        private HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus> GetRegroupStatusFilter()
        {
            var s = new HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus>();
            if (pnlRegroup?.IsEnabled != true) return s;
            if (chkRegroupStatusNew?.IsChecked      == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.New);
            if (chkRegroupStatusActive?.IsChecked   == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.Active);
            if (chkRegroupStatusReviewed?.IsChecked == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.Reviewed);
            if (chkRegroupStatusApproved?.IsChecked == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.Approved);
            if (chkRegroupStatusResolved?.IsChecked == true) s.Add(Autodesk.Navisworks.Api.Clash.ClashResultStatus.Resolved);
            return s;
        }

        // Toggles the Regroup section's enabled state based on the New Clashes
        // checkboxes. When ANY New Clashes status is selected, Regroup is
        // ghosted; when ALL are unselected, Regroup is editable.
        private void OnNewStatusChanged(object sender, RoutedEventArgs e)
        {
            if (pnlRegroup == null) return;
            bool anyNew = (chkNewStatusNew?.IsChecked == true)
                       || (chkNewStatusActive?.IsChecked == true)
                       || (chkNewStatusReviewed?.IsChecked == true);
            pnlRegroup.IsEnabled = !anyNew;
            pnlRegroup.Opacity = anyNew ? 0.5 : 1.0;
        }

        // Rebuilds txtNamingPreview live using a synthetic sample context.
        private void OnNamingTemplateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtNamingPreview == null) return;
            string template = GetSelectedNamingTemplate();
            if (string.IsNullOrEmpty(template))
            {
                txtNamingPreview.Text = "(legacy mode-specific names will be used)";
                return;
            }

            // Sample: Jan 10th 2027, Level 03 near B8:C7, ARCH vs STRC test,
            // Railings vs Structural Framing.  Match the user's spec example.
            string sample = template
                .Replace("{Month}", "01")
                .Replace("{Day}", "10")
                .Replace("{Year}", "2027")
                .Replace("{Level}", "L03")
                .Replace("{Area}", "B8:C7")
                .Replace("{TestName}", "ARCH vs STRC")
                .Replace("{SelectionA}", "Railings")
                .Replace("{SelectionB}", "Structural Framing")
                .Replace("{#}", "1");
            txtNamingPreview.Text = sample;
        }

        // Apply the current template to clash groups currently selected in
        // Clash Detective.  When the user has nothing selected, show a list
        // of every existing group with checkboxes for multi-pick.
        private void OnRenameSelectedClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string template = GetSelectedNamingTemplate();
                if (string.IsNullOrWhiteSpace(template))
                {
                    MessageBox.Show("Pick a naming template from the dropdown first.", "Rename Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var pairs = ClashGrouper.GetSelectedClashGroups();
                if (pairs.Count == 0)
                {
                    MessageBox.Show("No clash groups found in the document.", "Rename Selected",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Group by test, call RenameGroupsWithTemplate per test so the
                // sequence counter resets sensibly.
                int totalRenamed = 0;
                foreach (var byTest in pairs.GroupBy(kv => kv.Key))
                {
                    var groups = byTest.Select(kv => kv.Value).ToList();
                    totalRenamed += ClashGrouper.RenameGroupsWithTemplate(groups, byTest.Key, template);
                }

                MessageBox.Show($"Renamed {totalRenamed} clash group(s) with the current template.",
                    "Rename Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Rename failed:\n\n" + ex.Message, "Rename Selected",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // AutoNAVismate — one-button full workflow
        // ─────────────────────────────────────────────────────────────────────
        private void OnAutoNAVismateClick(object sender, RoutedEventArgs e)
        {
            btnAutoNAVismate.IsEnabled = false;
            try
            {
                SetAutoProgress("Step 1/5  Function 1 — discipline search sets…");
                SearchSetGenerator.GenerateFunction1SearchSets();

                SetAutoProgress("Step 2/5  Function 2 — element-property search sets…");
                // Refresh the discipline UI then run Function 2 with the
                // default (first) property option for every discipline.
                LoadDisciplineList();
                System.Windows.Forms.Application.DoEvents();   // let the WPF tree update
                try
                {
                    OnFunction2Click(this, new RoutedEventArgs());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[AutoNAV] AutoNAVismate Function 2 failed: " + ex.Message);
                }

                SetAutoProgress("Step 3/5  Function 4 — generating + running clash tests…");
                bool clashRunOk = false;
                try
                {
                    _clashEngine.GenerateClashTests();   // generate + auto-run inside
                    clashRunOk = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[AutoNAV] AutoNAVismate Function 4 failed: " + ex.Message);
                }
                if (!clashRunOk)
                {
                    var choice = MessageBox.Show(
                        "Function 4 couldn't run the clash tests automatically.\n\n" +
                        "Open Navisworks' Clash Detective panel and click 'Update All' on the Home tab, then click OK to continue. Cancel to abort AutoNAVismate.",
                        "AutoNAVismate — manual step required",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.OK)
                    {
                        SetAutoProgress("Aborted by user before Function 5.");
                        return;
                    }
                }

                SetAutoProgress("Step 4/5  Function 5 — grouping Walls / Floors…");
                try
                {
                    string summary = ClashGrouper.GroupAllTestsByWallsAndFloors();
                    System.Diagnostics.Debug.WriteLine("[AutoNAV] " + summary);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[AutoNAV] AutoNAVismate Function 5 failed: " + ex.Message);
                }

                SetAutoProgress("Step 5/5  Function 6 — grouping + naming remaining clashes…");
                RunFunction6WithDefaults();

                SetAutoProgress("AutoNAVismate complete. Open Clash Detective to review.");
                MessageBox.Show("AutoNAVismate finished.\n\nAll five steps ran. Check Clash Detective for results.",
                    "AutoNAVismate", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetAutoProgress("Fatal error: " + ex.Message);
                MessageBox.Show("AutoNAVismate hit a fatal error:\n\n" + ex.Message,
                    "AutoNAVismate", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnAutoNAVismate.IsEnabled = true;
            }
        }

        private void SetAutoProgress(string msg)
        {
            if (txtAutoNAVismateProgress != null) txtAutoNAVismateProgress.Text = msg;
            System.Diagnostics.Debug.WriteLine("[AutoNAVismate] " + msg);
        }

        // Loops every clash test, calls GroupClashes with the default template
        // and "Keep existing groups" semantics so Function 5's work survives.
        private void RunFunction6WithDefaults()
        {
            Document doc = NavApp.ActiveDocument;
            if (doc == null) return;
            DocumentClash documentClash = doc.GetClash();
            if (documentClash == null) return;
            var tests = ClashCompat.GetTopLevelTests(documentClash.TestsData);
            if (tests.Count == 0) return;

            // Default template = first preset (the user-spec example).
            string template = (cmbNamingTemplate.Items[0] as ComboBoxItem)?.Tag as string ?? "";
            var newStatuses = new HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus>
            {
                Autodesk.Navisworks.Api.Clash.ClashResultStatus.New,
            };
            var regroupStatuses = new HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus>();

            foreach (ClashTest test in tests)
            {
                try
                {
                    ClashGrouper.GroupClashes(
                        test,
                        ClashGrouper.GroupingMode.None,            // primary mode
                        ClashGrouper.GroupingMode.GridIntersection, // sub-group fallback so {Area} populates
                        keepExistingGroups: true,
                        namingTemplate: template,
                        newStatusFilter: newStatuses,
                        regroupStatusFilter: regroupStatuses);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoNAVismate] F6 failed for '{test.DisplayName}': {ex.Message}");
                }
            }
        }

        private ClashGrouper.GroupingMode ParseGroupingMode(string modeStr)
        {
            if (string.IsNullOrEmpty(modeStr)) return ClashGrouper.GroupingMode.None;
            
            switch (modeStr)
            {
                case "Level": return ClashGrouper.GroupingMode.Level;
                case "GridIntersection": return ClashGrouper.GroupingMode.GridIntersection;
                case "SelectionA": return ClashGrouper.GroupingMode.SelectionA;
                case "SelectionB": return ClashGrouper.GroupingMode.SelectionB;
                case "ModelA": return ClashGrouper.GroupingMode.ModelA;
                case "ModelB": return ClashGrouper.GroupingMode.ModelB;
                case "Status": return ClashGrouper.GroupingMode.Status;
                case "AssignedTo": return ClashGrouper.GroupingMode.AssignedTo;
                case "ApprovedBy": return ClashGrouper.GroupingMode.ApprovedBy;
                case "File": return ClashGrouper.GroupingMode.File;
                case "Layer": return ClashGrouper.GroupingMode.Layer;
                case "First": return ClashGrouper.GroupingMode.First;
                case "Last": return ClashGrouper.GroupingMode.Last;
                case "LastUnique": return ClashGrouper.GroupingMode.LastUnique;
                case "WallsAndFloors": return ClashGrouper.GroupingMode.WallsAndFloors;
                default: return ClashGrouper.GroupingMode.None;
            }
        }

        #endregion

        private void SetStatus(string message)
        {
            txtStatus.Text = message;
        }
    }
}
