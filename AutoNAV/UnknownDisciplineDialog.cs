using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoNAV
{
    // Modal dialog presented when Function 1's discipline classifier hits the
    // shortest-unique-discriminator fallback for one or more loaded files.
    // The user sees one row per unresolved file with:
    //   - the filename (read-only)
    //   - the auto-derived token (read-only, for context)
    //   - a discipline dropdown (canonical names from the dictionary) + free-text
    //   - "Use auto" toggle that disables the dropdown
    // On Apply the dialog exposes the user's chosen token per filename via
    // the Choices dictionary so the caller can rewrite the picks before
    // search sets are created.
    public class UnknownDisciplineDialog : Window
    {
        public Dictionary<string, string> Choices { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly List<RowControls> _rows = new List<RowControls>();

        private class RowControls
        {
            public string FileName;
            public string AutoToken;
            public CheckBox UseAuto;
            public ComboBox DisciplineCombo;
            public TextBox FreeText;
        }

        public UnknownDisciplineDialog(
            List<SearchSetGenerator.DisciplinePick> unresolved,
            Dictionary<string, string[]> dictionary)
        {
            Title = "Unrecognized Disciplines — confirm or override";
            Width = 760;
            Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)new BrushConverter().ConvertFromString("#F5F5F5");
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;

            var root = new DockPanel { LastChildFill = true };

            // Header
            var header = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#0078D4"),
                Padding = new Thickness(18, 12, 18, 12),
            };
            DockPanel.SetDock(header, Dock.Top);
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "AutoNAV — Unknown Disciplines",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"Function 1 couldn't match {unresolved.Count} file(s) to a known discipline. " +
                       "Pick a discipline or keep the auto-derived token.",
                Foreground = (Brush)new BrushConverter().ConvertFromString("#E0E0E0"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            header.Child = headerStack;
            root.Children.Add(header);

            // Footer buttons
            var footer = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F0F0F0"),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D0D0D0"),
                BorderThickness = new Thickness(0, 1, 0, 0),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnSkipAll = new Button { Content = "Use auto for all", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), Width = 140 };
            btnSkipAll.Click += (_, __) => { foreach (var r in _rows) r.UseAuto.IsChecked = true; };
            var btnApply = new Button { Content = "Apply", Padding = new Thickness(12, 6, 12, 6), Background = (Brush)new BrushConverter().ConvertFromString("#107C10"), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Width = 100 };
            btnApply.Click += OnApply;
            btnRow.Children.Add(btnSkipAll);
            btnRow.Children.Add(btnApply);
            footer.Child = btnRow;
            root.Children.Add(footer);

            // Rows
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12),
            };
            var rowsPanel = new StackPanel();
            scroll.Content = rowsPanel;
            root.Children.Add(scroll);

            // Header row
            var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            AddHeaderCell(headerRow, "File", 0);
            AddHeaderCell(headerRow, "Auto token", 1);
            AddHeaderCell(headerRow, "Use auto", 2);
            AddHeaderCell(headerRow, "Discipline", 3);
            AddHeaderCell(headerRow, "Or custom token", 4);
            rowsPanel.Children.Add(headerRow);

            var canonicalNames = dictionary.Keys.OrderBy(k => k).ToList();

            foreach (var pick in unresolved)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

                var nameBlock = new TextBlock { Text = pick.SourceFile, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(nameBlock, 0);
                row.Children.Add(nameBlock);

                var autoBlock = new TextBlock { Text = pick.DisplayName, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)new BrushConverter().ConvertFromString("#666666"), FontStyle = FontStyles.Italic };
                Grid.SetColumn(autoBlock, 1);
                row.Children.Add(autoBlock);

                var useAuto = new CheckBox { IsChecked = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(useAuto, 2);
                row.Children.Add(useAuto);

                var combo = new ComboBox { Height = 24, FontSize = 11 };
                combo.Items.Add(new ComboBoxItem { Content = "-- pick a discipline --", IsSelected = true });
                foreach (var name in canonicalNames)
                {
                    // Use the first (longest) code from the dictionary entry as the token to apply.
                    string firstCode = dictionary[name].FirstOrDefault() ?? name;
                    combo.Items.Add(new ComboBoxItem { Content = name, Tag = firstCode });
                }
                Grid.SetColumn(combo, 3);
                row.Children.Add(combo);

                var freeText = new TextBox { Height = 24, FontSize = 11, Margin = new Thickness(4, 0, 0, 0) };
                Grid.SetColumn(freeText, 4);
                row.Children.Add(freeText);

                // Mutual disable
                useAuto.Checked   += (_, __) => { combo.IsEnabled = false; freeText.IsEnabled = false; };
                useAuto.Unchecked += (_, __) => { combo.IsEnabled = true;  freeText.IsEnabled = true;  };

                rowsPanel.Children.Add(row);
                _rows.Add(new RowControls
                {
                    FileName = pick.SourceFile,
                    AutoToken = pick.DisplayName,
                    UseAuto = useAuto,
                    DisciplineCombo = combo,
                    FreeText = freeText,
                });
            }

            Content = root;
        }

        private static void AddHeaderCell(Grid g, string text, int col)
        {
            var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 11 };
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows)
            {
                string chosen;
                if (r.UseAuto.IsChecked == true)
                {
                    chosen = r.AutoToken;
                }
                else if (!string.IsNullOrWhiteSpace(r.FreeText.Text))
                {
                    chosen = r.FreeText.Text.Trim();
                }
                else if (r.DisciplineCombo.SelectedItem is ComboBoxItem cb && cb.Tag is string tag)
                {
                    chosen = tag;
                }
                else
                {
                    chosen = r.AutoToken; // nothing picked → fall back to auto
                }
                Choices[r.FileName] = chosen;
            }
            DialogResult = true;
            Close();
        }
    }
}
