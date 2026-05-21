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
                    Width = 170,
                    Height = 24,
                    FontSize = 11
                };

                // Property options - Element, Category, Workset, etc.
                string[,] propertyOptions = new string[,]
                {
                    { "Element Category", "Element", "Category" },
                    { "Element Workset", "Element", "Workset" },
                    { "Element Level", "Element", "Level" },
                    { "Element System", "Element", "System Name" },
                    { "Element Type", "Element", "Type" }
                };

                for (int i = 0; i < propertyOptions.GetLength(0); i++)
                {
                    cmb.Items.Add(new ComboBoxItem
                    {
                        Content = propertyOptions[i, 0],
                        Tag = propertyOptions[i, 1] + "|" + propertyOptions[i, 2]
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

                ClashGrouper.GroupClashes(selectedTest, groupingMode, subGroupingMode, keepExisting);

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

                int groupedCount = 0;
                int failedCount = 0;

                foreach (ClashTest test in tests)
                {
                    try
                    {
                        ClashGrouper.GroupClashes(test, groupingMode, subGroupingMode, keepExisting);
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
