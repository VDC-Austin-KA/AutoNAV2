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
            LoadRenameTree();
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
                // Type-based lookup so the row order doesn't break us when we
                // add adornments (e.g. the "(Mechanical)" italic label PR #9
                // inserted in front of the CheckBox).
                if (!(row is StackPanel sp)) continue;

                CheckBox cb = sp.Children.OfType<CheckBox>().FirstOrDefault();
                ComboBox cmb = sp.Children.OfType<ComboBox>().FirstOrDefault();
                if (cb == null || cmb == null) continue;
                if (cb.IsChecked != true) continue;

                string disc = cb.Tag as string;
                if (!(cmb.SelectedItem is ComboBoxItem sel)) continue;
                string tag = sel.Tag as string;
                if (string.IsNullOrEmpty(tag) || !tag.Contains("|")) continue;

                string[] parts = tag.Split('|');
                result.Add(new string[] { disc, parts[0], parts[1] });
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
            {
                if (!(child is StackPanel sp)) continue;
                CheckBox cb = sp.Children.OfType<CheckBox>().FirstOrDefault();
                if (cb != null) cb.IsChecked = setter(cb.IsChecked.GetValueOrDefault());
            }
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

        #region Function 6 + 7 grouping handlers

        // Function 6 uses GridIntersection as a fixed proximity-grouping mode.
        // Spatially-close clashes cluster on the same grid intersection so the
        // user can see problem areas at a glance, and the naming template adds
        // the discipline / selection-set / level context to each cluster's
        // name.  No mode dropdown is exposed in the UI for this function — the
        // user only picks the template.
        private const ClashGrouper.GroupingMode Function6PrimaryMode    = ClashGrouper.GroupingMode.GridIntersection;
        private const ClashGrouper.GroupingMode Function6SubGroupingMode = ClashGrouper.GroupingMode.None;

        // Function 6 — Group selected test (proximity-driven).
        private void OnGroupClashesClick(object sender, RoutedEventArgs e)
        {
            GroupSingleTest("Function 6 — Group Clashes",
                            Function6PrimaryMode, Function6SubGroupingMode);
        }

        // Function 6 — Group all tests (proximity-driven).
        private void OnGroupAllTestsClick(object sender, RoutedEventArgs e)
        {
            GroupAllTestsCore("Function 6 — Group All Tests",
                              Function6PrimaryMode, Function6SubGroupingMode);
        }

        // Function 7 — Group selected test using the user-picked primary/sub modes.
        private void OnFunction7GroupSelectedClick(object sender, RoutedEventArgs e)
        {
            var modes = GetFunction7Modes(out string error);
            if (error != null)
            {
                MessageBox.Show(error, "Function 7 — Manual Grouping",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            GroupSingleTest("Function 7 — Manual Grouping", modes.primary, modes.sub);
        }

        // Function 7 — Group all tests using the user-picked primary/sub modes.
        private void OnFunction7GroupAllClick(object sender, RoutedEventArgs e)
        {
            var modes = GetFunction7Modes(out string error);
            if (error != null)
            {
                MessageBox.Show(error, "Function 7 — Manual Grouping",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            GroupAllTestsCore("Function 7 — Manual Grouping", modes.primary, modes.sub);
        }

        // Reads the Function 7 ComboBoxes; emits a friendly error string when
        // neither mode is set, otherwise returns the (primary, sub) pair.
        private (ClashGrouper.GroupingMode primary, ClashGrouper.GroupingMode sub) GetFunction7Modes(out string error)
        {
            error = null;
            string p = (cmbGroupingMode.SelectedItem    as ComboBoxItem)?.Tag as string;
            string s = (cmbSubGroupingMode.SelectedItem as ComboBoxItem)?.Tag as string;
            var primary = ParseGroupingMode(p);
            var sub     = ParseGroupingMode(s);
            if (primary == ClashGrouper.GroupingMode.None && sub == ClashGrouper.GroupingMode.None)
                error = "Pick a Primary or Sub-Grouping mode before clicking Function 7's group button.";
            return (primary, sub);
        }

        // Shared single-test grouping core used by both Function 6 and Function 7.
        private void GroupSingleTest(string caption, ClashGrouper.GroupingMode primary, ClashGrouper.GroupingMode sub)
        {
            try
            {
                var selectedItem = cmbClashTest.SelectedItem as ComboBoxItem;
                string testName = selectedItem?.Tag as string;

                if (string.IsNullOrEmpty(testName))
                {
                    MessageBox.Show("Please select a clash test to group.", caption,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    MessageBox.Show("Clash Detective is not available.", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ClashTest selectedTest = null;
                foreach (ClashTest test in ClashCompat.EnumerateTests(documentClash.TestsData))
                {
                    if (test.DisplayName == testName) { selectedTest = test; break; }
                }
                if (selectedTest == null)
                {
                    MessageBox.Show("Clash test not found: " + testName, caption, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool keepExisting = chkKeepExistingGroups.IsChecked == true;
                string template = GetSelectedNamingTemplate();
                var newStatuses = GetNewStatusFilter();
                var regroupStatuses = GetRegroupStatusFilter();

                // 2-step group-then-rename: group via the legacy mode (no
                // template), THEN apply the template to every group except
                // "Walls" / "Floors".  This pattern produces more consistent
                // groups + names than embedding the template inside the
                // grouping pass and preserves any prior Walls/Floors work.
                ClashGrouper.GroupClashes(selectedTest, primary, sub, keepExisting,
                                          namingTemplate: "",
                                          newStatusFilter: newStatuses,
                                          regroupStatusFilter: regroupStatuses);
                if (!string.IsNullOrWhiteSpace(template))
                    RenameGroupsExcludingWallsFloors(selectedTest, template);

                MessageBox.Show("Clashes grouped successfully!\n\nCheck Clash Detective to see the results.",
                    caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error grouping clashes:\n\n" + ex.Message, caption,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Shared all-tests grouping core used by both Function 6 and Function 7.
        private void GroupAllTestsCore(string caption, ClashGrouper.GroupingMode primary, ClashGrouper.GroupingMode sub)
        {
            try
            {
                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No active document found.", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    MessageBox.Show("Clash Detective is not available.", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var tests = ClashCompat.GetTopLevelTests(documentClash.TestsData);
                if (tests.Count == 0)
                {
                    MessageBox.Show("No clash tests found.", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool keepExisting = chkKeepExistingGroups.IsChecked == true;
                string template = GetSelectedNamingTemplate();
                var newStatuses = GetNewStatusFilter();
                var regroupStatuses = GetRegroupStatusFilter();
                int groupedCount = 0;
                int failedCount = 0;

                foreach (ClashTest test in tests)
                {
                    try
                    {
                        // Same 2-step pattern as GroupSingleTest.
                        ClashGrouper.GroupClashes(test, primary, sub, keepExisting,
                                                  namingTemplate: "",
                                                  newStatusFilter: newStatuses,
                                                  regroupStatusFilter: regroupStatuses);
                        if (!string.IsNullOrWhiteSpace(template))
                            RenameGroupsExcludingWallsFloors(test, template);
                        groupedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        System.Diagnostics.Debug.WriteLine($"[AutoNAV] GroupClashes failed for '{test.DisplayName}': {ex.Message}");
                    }
                }

                MessageBox.Show($"Grouping complete!\n\nTests Grouped: {groupedCount}\nFailed: {failedCount}",
                    caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error:\n\n" + ex.Message, caption,
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

        // Walks `test`'s top-level ClashResultGroup children, applies the
        // naming template to every group whose DisplayName isn't exactly
        // "Walls" or "Floors" (which are Function 5's output and should be
        // preserved as-is).  Used by both AutoNAVismate and Function 6 when a
        // template is set — the "group-via-legacy-then-rename" 2-step that
        // produces more reliable / consistent group names than embedding the
        // template inside the grouping pass.
        private static void RenameGroupsExcludingWallsFloors(ClashTest test, string template)
        {
            if (test == null || string.IsNullOrWhiteSpace(template)) return;
            var toRename = new List<Autodesk.Navisworks.Api.Clash.ClashResultGroup>();
            foreach (var child in test.Children)
            {
                if (!(child is Autodesk.Navisworks.Api.Clash.ClashResultGroup grp)) continue;
                string n = grp.DisplayName?.Trim() ?? "";
                if (n.Equals("Walls",  StringComparison.OrdinalIgnoreCase)) continue;
                if (n.Equals("Floors", StringComparison.OrdinalIgnoreCase)) continue;
                toRename.Add(grp);
            }
            if (toRename.Count > 0)
                ClashGrouper.RenameGroupsWithTemplate(toRename, test, template);
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

            // AutoNAVismate workflow per Keith's spec (Nov 2026):
            //   1. Group every test using the legacy proximity mode WITHOUT a
            //      template (so groups get their legacy mode-derived names,
            //      and any existing Walls/Floors groups from Function 5 keep
            //      their names too).
            //   2. Rename every resulting group EXCEPT those literally named
            //      "Walls" or "Floors" using the default template.  This way
            //      Function 5's output stays untouched and the rest of the
            //      groups land on the project's standard naming convention.
            var newStatuses = new HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus>
            {
                Autodesk.Navisworks.Api.Clash.ClashResultStatus.New,
            };
            var noRegroup = new HashSet<Autodesk.Navisworks.Api.Clash.ClashResultStatus>();

            foreach (ClashTest test in tests)
            {
                try
                {
                    // Step 1: legacy grouping (no template → legacy auto-names).
                    ClashGrouper.GroupClashes(
                        test,
                        Function6PrimaryMode,
                        Function6SubGroupingMode,
                        keepExistingGroups: true,
                        namingTemplate: "",                // ← legacy names
                        newStatusFilter: newStatuses,
                        regroupStatusFilter: noRegroup);

                    // Step 2: rename every group in this test using the template,
                    // excluding "Walls" / "Floors" groups (Function 5's output).
                    RenameGroupsExcludingWallsFloors(test, template);
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

        // ─────────────────────────────────────────────────────────────────────
        // Rename tab — TreeView-driven multi-rename
        // ─────────────────────────────────────────────────────────────────────

        // Populates the treeRenameTargets TreeView with one TreeViewItem per
        // clash test, each with a child item per ClashResultGroup.  Each row
        // has a CheckBox so the user can pick any combination of tests and
        // groups to rename.  Auto-refreshed on tab activation via
        // OnRenameRefreshClick (also invoked from the Loaded handler).
        private void LoadRenameTree()
        {
            if (treeRenameTargets == null) return;
            treeRenameTargets.Items.Clear();

            Document doc = NavApp.ActiveDocument;
            if (doc == null) { SetRenameStatus("No active document."); return; }
            DocumentClash documentClash = doc.GetClash();
            if (documentClash == null || documentClash.TestsData == null)
            {
                SetRenameStatus("Clash Detective is not available.");
                return;
            }
            var tests = ClashCompat.GetTopLevelTests(documentClash.TestsData);
            if (tests.Count == 0)
            {
                SetRenameStatus("No clash tests in the document yet — run Function 4 first.");
                return;
            }

            foreach (ClashTest test in ClashCompat.EnumerateTests(documentClash.TestsData))
            {
                var testCb = new CheckBox { Content = test.DisplayName, FontWeight = FontWeights.SemiBold };
                var testItem = new TreeViewItem
                {
                    Header = testCb,
                    Tag = test,
                    IsExpanded = false,
                };
                foreach (var child in test.Children)
                {
                    if (!(child is Autodesk.Navisworks.Api.Clash.ClashResultGroup grp)) continue;
                    int childCount = grp.Children.Count;
                    string label = $"{grp.DisplayName}  ({childCount} clash{(childCount == 1 ? "" : "es")})";
                    var groupCb = new CheckBox { Content = label, FontSize = 11 };
                    var groupItem = new TreeViewItem { Header = groupCb, Tag = new RenameTarget(test, grp) };
                    testItem.Items.Add(groupItem);
                }
                treeRenameTargets.Items.Add(testItem);
            }

            SetRenameStatus($"{tests.Count} test(s) loaded.  Tick rows then click Rename Selected.");
            UpdateRenamePreview();
        }

        // Tag payload for group-level tree items.
        private class RenameTarget
        {
            public ClashTest Test { get; }
            public Autodesk.Navisworks.Api.Clash.ClashResultGroup Group { get; }
            public RenameTarget(ClashTest t, Autodesk.Navisworks.Api.Clash.ClashResultGroup g) { Test = t; Group = g; }
        }

        // Returns the active template string from either the preset dropdown
        // or the custom textbox.
        private string GetRenameTemplate()
        {
            var sel = cmbRenameTemplate?.SelectedItem as ComboBoxItem;
            string tag = sel?.Tag as string;
            if (string.Equals(tag, "__CUSTOM__", StringComparison.Ordinal))
                return txtRenameCustomTemplate?.Text?.Trim() ?? "";
            return tag ?? "";
        }

        private void UpdateRenamePreview()
        {
            if (txtRenamePreview == null) return;
            string template = GetRenameTemplate();
            if (string.IsNullOrEmpty(template))
            {
                txtRenamePreview.Text = "(no template selected)";
                return;
            }
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
            txtRenamePreview.Text = sample;
        }

        private void OnRenameTemplateChanged(object sender, RoutedEventArgs e)
        {
            UpdateRenamePreview();
        }

        private void OnRenameRefreshClick(object sender, RoutedEventArgs e)
        {
            LoadRenameTree();
        }

        private void SetTreeCheckedState(bool value)
        {
            if (treeRenameTargets == null) return;
            foreach (var i in treeRenameTargets.Items)
            {
                if (!(i is TreeViewItem tvi)) continue;
                if (tvi.Header is CheckBox testCb) testCb.IsChecked = value;
                foreach (var c in tvi.Items)
                {
                    if (c is TreeViewItem gtvi && gtvi.Header is CheckBox gcb) gcb.IsChecked = value;
                }
            }
        }

        private void OnRenameSelectAllClick(object sender, RoutedEventArgs e)  => SetTreeCheckedState(true);
        private void OnRenameSelectNoneClick(object sender, RoutedEventArgs e) => SetTreeCheckedState(false);

        private void SetRenameStatus(string msg) { if (txtRenameStatus != null) txtRenameStatus.Text = msg; }

        // Apply the selected template to the user's chosen targets.
        //   - If a test row is checked, every non-Walls/non-Floors group inside
        //     it is renamed (Walls/Floors stay untouched).
        //   - If only some groups under a test are checked, just those are
        //     renamed.
        //   - When "Keep existing group names" is checked, groups whose name
        //     is non-empty and looks pre-existing (i.e. they have a current
        //     DisplayName that doesn't equal the empty / placeholder default)
        //     are preserved.  When unchecked, EVERY selected group plus any
        //     ungrouped clashes inside selected tests get renamed.
        private void OnRenameApplyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string template = GetRenameTemplate();
                if (string.IsNullOrWhiteSpace(template))
                {
                    MessageBox.Show("Pick a preset or enter a custom template before clicking Rename.",
                        "Rename Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (treeRenameTargets == null || treeRenameTargets.Items.Count == 0)
                {
                    SetRenameStatus("No tests loaded — click Refresh.");
                    return;
                }

                bool keepExisting = chkRenameKeepExisting?.IsChecked == true;

                // Collect targets: { test → list of groups to rename }.
                // A test-level check applies to every group under it; a group
                // check is taken as-is.
                var perTest = new Dictionary<ClashTest, List<Autodesk.Navisworks.Api.Clash.ClashResultGroup>>();
                foreach (var item in treeRenameTargets.Items)
                {
                    if (!(item is TreeViewItem tvi)) continue;
                    var test = tvi.Tag as ClashTest;
                    if (test == null) continue;

                    bool testChecked = tvi.Header is CheckBox testCb && testCb.IsChecked == true;

                    foreach (var c in tvi.Items)
                    {
                        if (!(c is TreeViewItem gtvi)) continue;
                        if (!(gtvi.Tag is RenameTarget rt)) continue;
                        bool groupChecked = gtvi.Header is CheckBox gcb && gcb.IsChecked == true;
                        if (!testChecked && !groupChecked) continue;

                        // When keep-existing is set, skip groups that already have a
                        // meaningful name (not empty/whitespace).  Walls / Floors are
                        // ALWAYS skipped because Function 5 owns those.
                        string name = rt.Group.DisplayName?.Trim() ?? "";
                        if (name.Equals("Walls",  StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.Equals("Floors", StringComparison.OrdinalIgnoreCase)) continue;
                        if (keepExisting && !string.IsNullOrEmpty(name)) continue;

                        if (!perTest.TryGetValue(rt.Test, out var list))
                        {
                            list = new List<Autodesk.Navisworks.Api.Clash.ClashResultGroup>();
                            perTest[rt.Test] = list;
                        }
                        list.Add(rt.Group);
                    }
                }

                if (perTest.Count == 0)
                {
                    SetRenameStatus("Nothing matched — tick some rows (and remember Walls/Floors are always preserved).");
                    return;
                }

                int totalRenamed = 0;
                foreach (var kv in perTest)
                    totalRenamed += ClashGrouper.RenameGroupsWithTemplate(kv.Value, kv.Key, template);

                SetRenameStatus($"Renamed {totalRenamed} group(s) across {perTest.Count} test(s).");
                MessageBox.Show($"Renamed {totalRenamed} clash group(s).",
                    "Rename Selected", MessageBoxButton.OK, MessageBoxImage.Information);

                // Refresh the tree so the new names show.
                LoadRenameTree();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Rename failed:\n\n" + ex.Message, "Rename Selected",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
