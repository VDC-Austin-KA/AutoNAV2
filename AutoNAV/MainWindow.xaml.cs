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
                NotifyError("Error in Function 1:\n\n" + ex.Message);
            }
        }

        private void OnFunction2Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<string[]> selectedProps = GetSelectedDisciplineProps();

                if (selectedProps.Count == 0)
                {
                    NotifyWarning("Please select at least one discipline.");
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

                NotifyInfo("Function 2 complete.");
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
                var discItem = cmbFunction3Discipline.SelectedItem as ComboBoxItem;
                var catItem = cmbFunction3PropertyCategory.SelectedItem as ComboBoxItem;
                var propItem = cmbFunction3PropertyName.SelectedItem as ComboBoxItem;

                string discipline = discItem?.Tag as string;
                string category = catItem?.Tag as string;
                string propTag = propItem?.Tag as string;

                if (string.IsNullOrEmpty(discipline) || string.IsNullOrEmpty(category) || string.IsNullOrEmpty(propTag))
                {
                    NotifyWarning("Please select a discipline, property category, and property name.");
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
                NotifyError("Error in Function 3:\n\n" + ex.Message);
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
                NotifyError("Error generating clash tests:\n\n" + ex.Message);
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
                NotifyError("Error running clash tests and grouping results:\n\n" + ex.Message);
            }
        }

        #endregion

        #region Function 5 Event Handlers (Run Tests & Group Clashes)

        private void OnUpdateAllTestsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                NotifyInfo("To run clash tests, please use the Clash Detective pane in Navisworks:\n\n" +
                    "1. Open the Clash Detective pane\n" +
                    "2. Select the tests you want to run\n" +
                    "3. Click 'Run Tests' button\n\n" +
                    "The Group Clashes feature can then be used to organize the results.");
            }
            catch (Exception ex)
            {
                NotifyError("Error:\n\n" + ex.Message);
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

                NotifyInfo(result);
            }
            catch (Exception ex)
            {
                SetStatus("Function 6 failed.");
                NotifyError("Error in Function 6:\n\n" + ex.Message);
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
                NotifyWarning(error);
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
                NotifyWarning(error);
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
                    NotifyWarning("Please select a clash test to group.");
                    return;
                }

                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    NotifyError("No active document found.");
                    return;
                }
                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    NotifyError("Clash Detective is not available.");
                    return;
                }

                ClashTest selectedTest = null;
                foreach (ClashTest test in ClashCompat.EnumerateTests(documentClash.TestsData))
                {
                    if (test.DisplayName == testName) { selectedTest = test; break; }
                }
                if (selectedTest == null)
                {
                    NotifyError("Clash test not found: " + testName);
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

                NotifyInfo("Clashes grouped successfully!\n\nCheck Clash Detective to see the results.");
            }
            catch (Exception ex)
            {
                NotifyError("Error grouping clashes:\n\n" + ex.Message);
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
                    NotifyError("No active document found.");
                    return;
                }
                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    NotifyError("Clash Detective is not available.");
                    return;
                }

                var tests = ClashCompat.GetTopLevelTests(documentClash.TestsData);
                if (tests.Count == 0)
                {
                    NotifyError("No clash tests found.");
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

                NotifyInfo($"Grouping complete!\n\nTests Grouped: {groupedCount}\nFailed: {failedCount}");
            }
            catch (Exception ex)
            {
                NotifyError("Error:\n\n" + ex.Message);
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
                    NotifyWarning("Please select a clash test to ungroup.");
                    return;
                }

                Document doc = NavApp.ActiveDocument;
                if (doc == null)
                {
                    NotifyError("No active document found.");
                    return;
                }

                DocumentClash documentClash = doc.GetClash();
                if (documentClash == null)
                {
                    NotifyError("Clash Detective is not available.");
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
                    NotifyError("Clash test not found: " + testName);
                    return;
                }

                ClashGrouper.UnGroupClashes(selectedTest);

                NotifyInfo("Clashes have been ungrouped (reset to individual results).");
            }
            catch (Exception ex)
            {
                NotifyError("Error:\n\n" + ex.Message);
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
                    NotifyWarning("Pick a naming template from the dropdown first.");
                    return;
                }

                var pairs = ClashGrouper.GetSelectedClashGroups();
                if (pairs.Count == 0)
                {
                    NotifyInfo("No clash groups found in the document.");
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

                NotifyInfo($"Renamed {totalRenamed} clash group(s) with the current template.");
            }
            catch (Exception ex)
            {
                NotifyError("Rename failed:\n\n" + ex.Message);
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
                    // No popup interrupts the workflow.  We just log the issue
                    // to the status panel and continue — Functions 5 & 6 will
                    // simply find no clash results if tests didn't actually
                    // run, which is fine.
                    NotifyWarning("AutoNAVismate: Function 4 auto-run failed. Open Clash Detective and click \"Update All\" after the workflow finishes if results are empty.");
                }

                SetAutoProgress("Step 4/5  Function 5 — grouping Walls / Floors…");
                try
                {
                    string summary = ClashGrouper.GroupAllTestsByWallsAndFloors();
                    NotifyResult("Function 5 — Walls / Floors grouping", summary);
                }
                catch (Exception ex)
                {
                    NotifyError("Function 5 failed: " + ex.Message);
                }

                SetAutoProgress("Step 5/5  Function 6 — grouping + naming remaining clashes…");
                RunFunction6WithDefaults();

                SetAutoProgress("AutoNAVismate complete. Open Clash Detective to review.");
                NotifySuccess("AutoNAVismate finished — all five steps ran. Open Clash Detective to review results.");
            }
            catch (Exception ex)
            {
                SetAutoProgress("Fatal error: " + ex.Message);
                NotifyError("AutoNAVismate hit a fatal error: " + ex.Message);
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
        // In-app status panel — replaces MessageBox popups so the user isn't
        // constantly clicking OK to dismiss notifications.  Three flavours:
        //
        //   NotifyInfo    — neutral info, dark-blue text
        //   NotifySuccess — green tick, useful for "finished" messages
        //   NotifyError   — red, for the rare hard failure
        //   NotifyResult  — multi-line summary (e.g. clash-grouping counts)
        //
        // All append to txtAppStatus and auto-scroll to the bottom; the user
        // can click "Clear" in the footer to reset the panel.
        // ─────────────────────────────────────────────────────────────────────

        private void NotifyInfo(string message)    => AppendStatus(message, System.Windows.Media.Brushes.DarkBlue);
        private void NotifySuccess(string message) => AppendStatus("✓ " + message, System.Windows.Media.Brushes.DarkGreen);
        private void NotifyError(string message)   => AppendStatus("✗ " + message, System.Windows.Media.Brushes.DarkRed);
        private void NotifyWarning(string message) => AppendStatus("⚠ " + message, System.Windows.Media.Brushes.DarkOrange);
        // Multi-line summary panel — preserves \n formatting for things like
        // grouping counts ("Tests grouped: 5\nFailed: 0").
        private void NotifyResult(string title, string body)
        {
            AppendStatus(title, System.Windows.Media.Brushes.DarkBlue);
            if (!string.IsNullOrWhiteSpace(body))
                AppendStatus("    " + body.Replace("\n", "\n    "), System.Windows.Media.Brushes.Black);
        }

        private void AppendStatus(string message, System.Windows.Media.Brush color)
        {
            if (txtAppStatus == null) return;
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            var run = new System.Windows.Documents.Run("[" + stamp + "] " + message + Environment.NewLine)
            {
                Foreground = color,
            };
            txtAppStatus.Inlines.Add(run);
            // auto-scroll to bottom
            svAppStatus?.ScrollToBottom();
            // also mirror the latest line to the small secondary status text so
            // the user sees the most recent message at a glance.
            SetStatus(message.Length > 80 ? message.Substring(0, 80) + "…" : message);
        }

        private void OnStatusClearClick(object sender, RoutedEventArgs e)
        {
            txtAppStatus?.Inlines.Clear();
            SetStatus("");
        }

        // Show / hide the full activity log so the status bar can stay one line tall
        // by default and the function buttons remain visible without scrolling.
        private void OnStatusToggleClick(object sender, RoutedEventArgs e)
        {
            if (svAppStatus == null) return;
            bool collapsed = svAppStatus.Visibility != System.Windows.Visibility.Visible;
            svAppStatus.Visibility = collapsed ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (btnStatusToggle != null)
                btnStatusToggle.Content = collapsed ? "▴ Activity" : "▾ Activity";
            if (collapsed) svAppStatus.ScrollToBottom();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Rename tab — DataGrid-driven Current → Proposed preview
        // ─────────────────────────────────────────────────────────────────────

        // Public so the DataGrid's IsSelected column binding can write it.
        public class RenameRow : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                        OnSelectionChanged?.Invoke();
                    }
                }
            }

            public string TestName { get; set; }
            public string CurrentName { get; set; }
            public string ProposedName { get; set; }
            public int ClashCount { get; set; }

            // Bookkeeping — not displayed in the grid.
            public ClashTest Test { get; set; }
            public Autodesk.Navisworks.Api.Clash.ClashResultGroup Group { get; set; }
            public int GroupIndex { get; set; }
            public bool IsWallsFloors { get; set; }
            public string FirstResultStatus { get; set; }

            // Fired by the row when its checkbox toggles so the parent window
            // can refresh the counts panel.  Static so we don't need a back-
            // pointer per row.
            internal static Action OnSelectionChanged;
        }

        // The visible row list (bound to dgRenameRows.ItemsSource).
        private readonly System.Collections.ObjectModel.ObservableCollection<RenameRow> _renameRows
            = new System.Collections.ObjectModel.ObservableCollection<RenameRow>();

        // The 5 preset template strings; index 0 is the default.  Used by the
        // "matches a known preset" filter so we can fingerprint group names.
        private static readonly string[] RenamePresetTemplates = new[]
        {
            "{Month}/{Day}_{Level}_{Area} | {TestName} - {SelectionA} vs {SelectionB} {#}",
            "{Level}_{Area} | {TestName} - {SelectionA} vs {SelectionB} {#}",
            "{TestName} | {Level}_{Area} {#}",
            "{TestName} | {Level}_ {SelectionA} vs {SelectionB} {#}",
        };

        // Build the test ComboBox + initial row set.
        private void LoadRenameTree()
        {
            if (cmbRenameTest == null) return;

            // Hook the row-selection callback once so the counts panel auto-updates.
            RenameRow.OnSelectionChanged = UpdateRenameCounts;

            // Bind the grid to the observable collection (idempotent).
            if (dgRenameRows != null && dgRenameRows.ItemsSource == null)
                dgRenameRows.ItemsSource = _renameRows;

            // Populate the test selector.  Items hold the ClashTest in Tag.
            cmbRenameTest.Items.Clear();
            cmbRenameTest.Items.Add(new ComboBoxItem { Content = "All tests", Tag = null });

            Document doc = NavApp.ActiveDocument;
            if (doc == null) { SetRenameStatus("No active document."); _renameRows.Clear(); UpdateRenameCounts(); return; }
            DocumentClash documentClash = doc.GetClash();
            if (documentClash == null || documentClash.TestsData == null)
            {
                SetRenameStatus("Clash Detective is not available.");
                _renameRows.Clear();
                UpdateRenameCounts();
                return;
            }
            var tests = ClashCompat.GetTopLevelTests(documentClash.TestsData);
            foreach (ClashTest t in ClashCompat.EnumerateTests(documentClash.TestsData))
            {
                cmbRenameTest.Items.Add(new ComboBoxItem { Content = t.DisplayName, Tag = t });
            }
            if (cmbRenameTest.SelectedIndex < 0) cmbRenameTest.SelectedIndex = 0;

            if (tests.Count == 0)
            {
                SetRenameStatus("No clash tests in the document yet — run Function 4 first.");
                _renameRows.Clear();
                UpdateRenameCounts();
                return;
            }

            SetRenameStatus($"{tests.Count} test(s) loaded.");
            RebuildRenameRows();
        }

        // Rebuilds _renameRows for the currently-selected test (or all tests).
        // Applies the filter + computes the proposed name for every row using
        // the currently-selected template.
        private void RebuildRenameRows()
        {
            if (dgRenameRows == null || cmbRenameTest == null) return;
            _renameRows.Clear();

            var selected = cmbRenameTest.SelectedItem as ComboBoxItem;
            ClashTest scope = selected?.Tag as ClashTest;
            string filter = (cmbRenameFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            string template = GetRenameTemplate();

            Document doc = NavApp.ActiveDocument;
            if (doc == null) { UpdateRenameCounts(); return; }
            DocumentClash documentClash = doc.GetClash();
            if (documentClash == null || documentClash.TestsData == null) { UpdateRenameCounts(); return; }

            var testsToWalk = scope != null
                ? new[] { scope }.AsEnumerable()
                : ClashCompat.EnumerateTests(documentClash.TestsData);

            // Single shared sequence counter scopes per-test so {#} restarts
            // cleanly per test (matches Function 6 behaviour).
            foreach (ClashTest test in testsToWalk)
            {
                var seq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int idx = 0;
                foreach (var child in test.Children)
                {
                    if (!(child is Autodesk.Navisworks.Api.Clash.ClashResultGroup grp)) continue;
                    idx++;

                    string current = grp.DisplayName?.Trim() ?? "";
                    bool isWf = current.Equals("Walls", StringComparison.OrdinalIgnoreCase)
                             || current.Equals("Floors", StringComparison.OrdinalIgnoreCase);

                    // Filter:
                    if (filter == "all" && isWf) continue;
                    if (filter == "preset" && !LooksLikePresetMatch(current)) continue;
                    if (filter == "empty"  && !string.IsNullOrWhiteSpace(current)) continue;
                    // "all+wf" keeps everything including Walls/Floors.

                    // Compute the proposed name using ClashGrouper's helpers.
                    string proposed = string.IsNullOrWhiteSpace(template)
                        ? "(no template)"
                        : ClashGrouper.ComputeProposedName(template, test, grp, idx, seq);

                    _renameRows.Add(new RenameRow
                    {
                        IsSelected   = false,
                        TestName     = test.DisplayName ?? "",
                        CurrentName  = current,
                        ProposedName = proposed,
                        ClashCount   = CountClashResults(grp),
                        Test         = test,
                        Group        = grp,
                        GroupIndex   = idx,
                        IsWallsFloors= isWf,
                    });
                }
            }

            UpdateRenameCounts();
        }

        // Recursively counts ClashResult leaves under a group.
        private static int CountClashResults(Autodesk.Navisworks.Api.Clash.ClashResultGroup grp)
        {
            int n = 0;
            foreach (var c in grp.Children)
            {
                if (c is Autodesk.Navisworks.Api.Clash.ClashResult) n++;
                else if (c is Autodesk.Navisworks.Api.Clash.ClashResultGroup nested) n += CountClashResults(nested);
            }
            return n;
        }

        // Heuristic: does this name look like one of our preset templates was
        // applied?  We just check for a few unique-ish characters / tokens the
        // presets always emit ('|' and ' vs ' both appear in every preset).
        private static bool LooksLikePresetMatch(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.Contains(" | ") || name.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase) >= 0;
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

        // Live recomputes ProposedName for every existing row when the template
        // changes (without rebuilding the row list from scratch).  Faster than
        // a full RebuildRenameRows when the user is typing.
        private void RecomputeProposedNames()
        {
            if (dgRenameRows == null) return;
            string template = GetRenameTemplate();
            // Sequence counter is per-test so multiple visible tests don't
            // share a sequence.
            var perTestSeq = new Dictionary<ClashTest, Dictionary<string, int>>();
            foreach (var row in _renameRows)
            {
                if (!perTestSeq.TryGetValue(row.Test, out var seq))
                {
                    seq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    perTestSeq[row.Test] = seq;
                }
                row.ProposedName = string.IsNullOrWhiteSpace(template)
                    ? "(no template)"
                    : ClashGrouper.ComputeProposedName(template, row.Test, row.Group, row.GroupIndex, seq);
            }
            // Force the grid to refresh ProposedName since the property isn't
            // INotifyPropertyChanged-bound.
            dgRenameRows.Items.Refresh();
            UpdateRenameCounts();
        }

        private void UpdateRenameCounts()
        {
            if (txtRenameCounts == null) return;
            int total = _renameRows.Count;
            int selected = _renameRows.Count(r => r.IsSelected);
            int wallsFloors = _renameRows.Count(r => r.IsWallsFloors);
            int eligible = 0;
            int unchanged = 0;
            bool keepExisting = chkRenameKeepExisting?.IsChecked == true;
            foreach (var r in _renameRows)
            {
                if (r.IsWallsFloors) continue;
                if (!r.IsSelected) { unchanged++; continue; }
                if (keepExisting && !string.IsNullOrWhiteSpace(r.CurrentName)) { unchanged++; continue; }
                eligible++;
            }

            int distinctTests = _renameRows.Select(r => r.Test).Distinct().Count();
            txtRenameCounts.Text =
                $"Visible rows: {total}   |   Ticked: {selected}   |   Will rename: {eligible}   |   " +
                $"Will stay unchanged: {unchanged}   |   Walls/Floors preserved: {wallsFloors}   |   " +
                $"Tests covered: {distinctTests}";
        }

        // ── Event handlers ──────────────────────────────────────────────────

        private void OnRenameRefreshClick(object sender, RoutedEventArgs e) => LoadRenameTree();

        private void OnRenameTestChanged(object sender, SelectionChangedEventArgs e) => RebuildRenameRows();

        private void OnRenameFilterChanged(object sender, SelectionChangedEventArgs e) => RebuildRenameRows();

        private void OnRenameKeepExistingChanged(object sender, RoutedEventArgs e) => UpdateRenameCounts();

        private void OnRenameTemplateChanged(object sender, RoutedEventArgs e) => RecomputeProposedNames();

        private void OnRenameSelectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var r in _renameRows) r.IsSelected = !r.IsWallsFloors;
        }

        private void OnRenameSelectNoneClick(object sender, RoutedEventArgs e)
        {
            foreach (var r in _renameRows) r.IsSelected = false;
        }

        private void SetRenameStatus(string msg) { if (txtRenameStatus != null) txtRenameStatus.Text = msg; }

        // Apply the selected template to every ticked row, respecting
        // the keep-existing toggle.  Walls / Floors rows are always
        // preserved (the IsWallsFloors guard mirrors what RebuildRenameRows
        // already filters out — defensive double-check here too).
        private void OnRenameApplyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string template = GetRenameTemplate();
                if (string.IsNullOrWhiteSpace(template))
                {
                    NotifyWarning("Pick a preset or enter a custom template before clicking Rename.");
                    return;
                }
                if (_renameRows.Count == 0)
                {
                    SetRenameStatus("No rows to rename — click Refresh and pick a test.");
                    return;
                }

                bool keepExisting = chkRenameKeepExisting?.IsChecked == true;

                var perTest = new Dictionary<ClashTest, List<Autodesk.Navisworks.Api.Clash.ClashResultGroup>>();
                foreach (var r in _renameRows)
                {
                    if (!r.IsSelected) continue;
                    if (r.IsWallsFloors) continue;
                    if (keepExisting && !string.IsNullOrWhiteSpace(r.CurrentName)) continue;

                    if (!perTest.TryGetValue(r.Test, out var list))
                    {
                        list = new List<Autodesk.Navisworks.Api.Clash.ClashResultGroup>();
                        perTest[r.Test] = list;
                    }
                    list.Add(r.Group);
                }

                if (perTest.Count == 0)
                {
                    SetRenameStatus("Nothing matched — tick some rows (Walls/Floors are always preserved).");
                    return;
                }

                int totalRenamed = 0;
                foreach (var kv in perTest)
                    totalRenamed += ClashGrouper.RenameGroupsWithTemplate(kv.Value, kv.Key, template);

                SetRenameStatus($"Renamed {totalRenamed} group(s) across {perTest.Count} test(s).");
                NotifyInfo($"Renamed {totalRenamed} clash group(s).");

                // Reload the rows to show new names.
                RebuildRenameRows();
            }
            catch (Exception ex)
            {
                NotifyError("Rename failed:\n\n" + ex.Message);
            }
        }
    }
}
