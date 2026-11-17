using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.App.Views;
using CardFactory.ProfitLoss.ReportParsing.Parsers;
using CardFactory.ProfitLoss.App.ViewModels;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// The Add-on Sales tab (owner, 16 Nov 2026, concept T1). Add-ons are added by SKU - the box
/// offers names from every item Flooid has listed on this till - and kept on the till
/// (Services/AddOnSettings). Stamps is one ready-made add-on covering every stamp (the Stamps
/// department). The figures come from Item Sales By Operator with every item, pulled with the reports.
/// </summary>
public partial class MainWindow
{
    /// <summary>A line in the tracked list or the SKU box's suggestions.</summary>
    public sealed record AddOnListRow(string Key, string Name, string Detail);

    private bool _addOnsActive;
    private List<AddOnItem> _addOnTracked = new();
    private Dictionary<string, string>? _addOnSeen;
    private readonly ObservableCollection<AddOnListRow> _addOnTrackedRows = new();
    private readonly ObservableCollection<AddOnListRow> _addOnSuggestions = new();
    private MainViewModel? _addOnViewModel;

    private static readonly CultureInfo AddOnCulture = CultureInfo.GetCultureInfo("en-GB");

    // Owner, 17 Nov 2026 ("there's still stuff not showing up"; option 1, 4 weeks): the SKU
    // box only knew items seen in reports pulled on this till, so anything not sold since was
    // missing. Once per till (AddOnSettings.ListFilled), after the quiet fetch following a
    // sign-in, the second sign-in pulls the all-items report for the last 28 days (to
    // yesterday) and every item in it is remembered. Only the second sign-in is used, so a
    // Refresh never waits for it (the app's own sign-in simply pulls everything meanwhile); if
    // the second sign-in isn't available it is tried after the next sign-in. Nothing on screen
    // but a status line when it has finished; figures are not touched.
    private const int AddOnListFillDays = 28;
    private bool _addOnListFilling;

    private async Task FillAddOnListOnceAsync(FlooidLoginWindow loginWindow)
    {
        if (_addOnListFilling || AddOnSettings.ListFilled) return;
        if (SecondSignInDetails(loginWindow) is not { } details) return;
        _addOnListFilling = true;
        var from = DateTime.Today.AddDays(-AddOnListFillDays);
        var to = DateTime.Today.AddDays(-1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Log(string text) => AppFiles.AppendDiagnostic(
            $"[{DateTime.Now:HH:mm:ss}] Add-on list fill ({from:dd/MM/yyyy} - {to:dd/MM/yyyy}): {text}{Environment.NewLine}");
        Log("started on the second sign-in");
        try
        {
            using var lease = await _secondSignIn.AcquireAsync(details, () => true);
            if (lease is null)
            {
                Log("the second sign-in is not available - tried again after the next sign-in");
                return;
            }

            string html;
            try
            {
                html = await lease.FetchAsync(v => v.FetchAllItemSalesHtmlAsync(from, to));
            }
            catch (Exception ex)
            {
                lease.Failed("Add-on list fill", ex);
                throw;
            }

            var report = new AllItemSalesReportParser().Parse(html);
            var seen = _addOnSeen ?? AddOnSettings.LoadSeen();
            var before = seen.Count;
            _addOnSeen = AddOnSettings.RememberSeen(seen, report.Items);
            var added = Math.Max(0, _addOnSeen.Count - before);
            AddOnSettings.MarkListFilled(from, to, _addOnSeen.Count);
            Log($"{report.Items.Count} lines, {added} new items, {_addOnSeen.Count} known now, {html.Length / 1024} KB, {clock.ElapsedMilliseconds} ms");
            if (RetrievalOverlay.Visibility != Visibility.Visible && DataContext is MainViewModel viewModel)
                viewModel.SetStatus($"Add-on list filled from the last 4 weeks · {added} items added", notify: false);
        }
        catch (Exception ex)
        {
            Log($"failed after {clock.ElapsedMilliseconds} ms - tried again after the next sign-in: {ex.Message}");
        }
        finally
        {
            _addOnListFilling = false;
        }
    }

    private void InitialiseAddOns()
    {
        _addOnTracked = AddOnSettings.LoadTracked();
        _addOnSeen = AddOnSettings.LoadSeen();
        AddOnTrackedList.ItemsSource = _addOnTrackedRows;
        AddOnSuggestList.ItemsSource = _addOnSuggestions;
        if (DataContext is MainViewModel viewModel && !ReferenceEquals(viewModel, _addOnViewModel))
        {
            if (_addOnViewModel is not null) _addOnViewModel.AddOnDataChanged -= AddOnData_Changed;
            _addOnViewModel = viewModel;
            viewModel.AddOnDataChanged += AddOnData_Changed;
        }
        RedrawAddOns();
    }

    private void AddOnData_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) RedrawAddOns();
        else Dispatcher.BeginInvoke(new Action(RedrawAddOns));
    }

    /// <summary>The tracked add-ons, for the Daily Briefing.</summary>
    public IReadOnlyList<AddOnItem> TrackedAddOns => _addOnTracked;

    // ---- Adding and removing ------------------------------------------------------------

    private void AddOnSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddOnSearchNote.Visibility = Visibility.Collapsed;
        var query = AddOnSearchBox.Text.Trim();
        _addOnSuggestions.Clear();
        if (query.Length == 0)
        {
            AddOnSuggestPopup.IsOpen = false;
            return;
        }

        if ("stamps".Contains(query, StringComparison.OrdinalIgnoreCase) && query.Length >= 3 && !IsTracked(AddOnItem.StampsKey))
            _addOnSuggestions.Add(new AddOnListRow(AddOnItem.StampsKey, "Stamps", "every stamp"));

        foreach (var (code, name) in (_addOnSeen ?? new Dictionary<string, string>())
                     .Where(p => p.Key.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                              || p.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p.Key.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
                     .Take(40))
        {
            if (IsTracked(code)) continue;
            _addOnSuggestions.Add(new AddOnListRow(code, name, "SKU " + code));
        }

        AddOnSuggestPopup.IsOpen = _addOnSuggestions.Count > 0;
    }

    private void AddOnSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when AddOnSuggestPopup.IsOpen && _addOnSuggestions.Count > 0:
                AddOnSuggestList.SelectedIndex = Math.Min(AddOnSuggestList.SelectedIndex + 1, _addOnSuggestions.Count - 1);
                AddOnSuggestList.ScrollIntoView(AddOnSuggestList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when AddOnSuggestPopup.IsOpen && _addOnSuggestions.Count > 0:
                AddOnSuggestList.SelectedIndex = Math.Max(AddOnSuggestList.SelectedIndex - 1, 0);
                AddOnSuggestList.ScrollIntoView(AddOnSuggestList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                if (AddOnSuggestPopup.IsOpen && AddOnSuggestList.SelectedItem is AddOnListRow picked) AddFromRow(picked);
                else AddTypedSku();
                e.Handled = true;
                break;
            case Key.Escape:
                AddOnSuggestPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void AddOnSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Clicking a suggestion moves the focus into the list; that must not close it first.
        if (e.NewFocus is DependencyObject target && (ReferenceEquals(target, AddOnSuggestList) || AddOnSuggestList.IsAncestorOf(target))) return;
        AddOnSuggestPopup.IsOpen = false;
    }

    private void AddOnSuggestList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(AddOnSuggestList, source) is ListBoxItem { DataContext: AddOnListRow row })
        {
            AddFromRow(row);
            e.Handled = true;
        }
    }

    private void AddOnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (AddOnSuggestPopup.IsOpen && AddOnSuggestList.SelectedItem is AddOnListRow picked) AddFromRow(picked);
        else AddTypedSku();
    }

    private void AddFromRow(AddOnListRow row) =>
        AddTracked(row.Key == AddOnItem.StampsKey ? AddOnItem.Stamps : new AddOnItem(row.Key, row.Name));

    /// <summary>What is typed in the box, taken as a SKU. A SKU seen in a report gets its name;
    /// one not seen yet is added as it is, and gets its name once it sells.</summary>
    private void AddTypedSku()
    {
        var typed = AddOnSearchBox.Text.Trim();
        if (typed.Length == 0) return;

        if (typed.Equals("stamps", StringComparison.OrdinalIgnoreCase) || typed.Equals("stamp", StringComparison.OrdinalIgnoreCase))
        {
            AddTracked(AddOnItem.Stamps);
            return;
        }

        var seen = (_addOnSeen ?? new Dictionary<string, string>())
            .FirstOrDefault(p => AddOnSalesSummary.SameSku(p.Key, typed));
        if (seen.Key is not null)
        {
            AddTracked(new AddOnItem(seen.Key, seen.Value));
            return;
        }

        if (typed.Contains(' ') || typed.Length > 20)
        {
            ShowAddOnNote("That isn't a SKU. Pick an item from the list, or type its SKU.");
            return;
        }

        AddTracked(new AddOnItem(typed.ToUpperInvariant(), typed.ToUpperInvariant()));
        ShowAddOnNote($"Added SKU {typed.ToUpperInvariant()}. It hasn't come up in a report on this till yet, so its name shows once it sells.");
    }

    private void AddTracked(AddOnItem item)
    {
        AddOnSuggestPopup.IsOpen = false;
        if (IsTracked(item.Key))
        {
            ShowAddOnNote($"{item.Name} is already tracked.");
            return;
        }

        _addOnTracked.Add(item);
        AddOnSettings.SaveTracked(_addOnTracked);
        AddOnSearchBox.Text = string.Empty;
        RedrawAddOns();
    }

    private void AddOnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
        _addOnTracked.RemoveAll(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
        AddOnSettings.SaveTracked(_addOnTracked);
        RedrawAddOns();
    }

    private bool IsTracked(string key) =>
        _addOnTracked.Any(i => key == AddOnItem.StampsKey ? i.IsStamps : !i.IsStamps && AddOnSalesSummary.SameSku(i.Key, key));

    private void ShowAddOnNote(string text)
    {
        AddOnSearchNote.Text = text;
        AddOnSearchNote.Visibility = Visibility.Visible;
    }

    // ---- Drawing ----------------------------------------------------------------------

    private void RedrawAddOns()
    {
        if (AddOnTrackedList is null || AddOnTrackedList.ItemsSource is null) return;
        var viewModel = DataContext as MainViewModel;

        // A SKU added before it was ever seen takes its name once a report lists it.
        var renamed = false;
        for (var i = 0; i < _addOnTracked.Count; i++)
        {
            var item = _addOnTracked[i];
            if (item.IsStamps || item.Name != item.Key || _addOnSeen is null) continue;
            var seen = _addOnSeen.FirstOrDefault(p => AddOnSalesSummary.SameSku(p.Key, item.Key));
            if (seen.Key is null) continue;
            _addOnTracked[i] = item with { Name = seen.Value };
            renamed = true;
        }
        if (renamed) AddOnSettings.SaveTracked(_addOnTracked);

        var summary = viewModel?.AddOnSummary(_addOnTracked);

        _addOnTrackedRows.Clear();
        for (var i = 0; i < _addOnTracked.Count; i++)
        {
            var item = _addOnTracked[i];
            var where = item.IsStamps ? "every stamp" : "SKU " + item.Key;
            var column = summary?.Columns.FirstOrDefault(c => string.Equals(c.Item.Key, item.Key, StringComparison.OrdinalIgnoreCase));
            var sold = summary is null ? string.Empty : column is null ? " · not fetched yet" : $" · {column.Quantity} sold";
            _addOnTrackedRows.Add(new AddOnListRow(item.Key, item.Name, where + sold));
        }
        AddOnTrackingTitle.Text = $"Tracking ({_addOnTracked.Count})";
        AddOnPeriodText.Text = viewModel?.PeriodDisplayText ?? string.Empty;

        AddOnEmptyState.Visibility = summary is null ? Visibility.Visible : Visibility.Collapsed;
        AddOnEmptyText.Text = _addOnTracked.Count == 0
            ? "Add a SKU on the left, then press Generate reports to see how many sold."
            : "They come with the reports. Press Generate reports to fetch them.";

        DrawAddOnCards(summary);
        DrawAddOnTable(summary);
    }

    private void DrawAddOnCards(AddOnSalesSummary? summary)
    {
        AddOnCardsPanel.Children.Clear();
        if (summary is null) return;
        if (summary.Columns.Count == 0)
        {
            AddOnCardsPanel.Children.Add(new TextBlock
            {
                Text = "Add a SKU on the left to start.",
                FontFamily = new FontFamily("Arial"), FontSize = 12,
                Foreground = Res("CfMuted"), VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        foreach (var column in summary.Columns)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = column.Item.Name,
                FontFamily = (FontFamily)FindResource("CfTitleFont"), FontSize = 16,
                Foreground = Res("CfDeepBlue"), TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = column.Item.IsStamps ? "Every stamp" : "SKU " + column.Item.Key,
            });
            var figures = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
            figures.Inlines.Add(new System.Windows.Documents.Run(column.Quantity.ToString(AddOnCulture))
            {
                FontFamily = new FontFamily("Arial"), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Res("CfDeepBlue"),
            });
            figures.Inlines.Add(new System.Windows.Documents.Run("  " + column.Value.ToString("£#,##0.00", AddOnCulture))
            {
                FontFamily = new FontFamily("Arial"), FontSize = 12, Foreground = Res("CfMuted"),
            });
            stack.Children.Add(figures);
            var best = new TextBlock { FontFamily = new FontFamily("Arial"), FontSize = 11, Foreground = Res("CfInk"), TextTrimming = TextTrimming.CharacterEllipsis };
            if (column.TopSeller is null) best.Text = "none sold";
            else
            {
                best.Inlines.Add(new System.Windows.Documents.Run("★ Most: "));
                best.Inlines.Add(new System.Windows.Documents.Run($"{column.TopSeller} · {column.TopQuantity}")
                {
                    FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x35)),
                });
            }
            stack.Children.Add(best);

            AddOnCardsPanel.Children.Add(new Border
            {
                Width = 188, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5),
                BorderBrush = Res("CfLine"), Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC)),
                Child = stack,
            });
        }
    }

    private void DrawAddOnTable(AddOnSalesSummary? summary)
    {
        AddOnTable.Children.Clear();
        AddOnTable.RowDefinitions.Clear();
        AddOnTable.ColumnDefinitions.Clear();
        if (summary is null || summary.Columns.Count == 0) return;

        AddOnTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        foreach (var _ in summary.Columns) AddOnTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        AddOnTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        AddOnTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        var lastColumn = summary.Columns.Count + 2;

        void Row(int row, double height, Brush? background)
        {
            AddOnTable.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
            var band = new Border { Background = background ?? Brushes.Transparent, BorderBrush = Res("CfLineSoft"), BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid.SetRow(band, row);
            Grid.SetColumnSpan(band, lastColumn + 1);
            AddOnTable.Children.Add(band);
        }

        void Cell(int row, int column, string text, bool left = false, bool bold = false, bool header = false, bool green = false)
        {
            var block = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Arial"),
                FontSize = header ? 9.5 : 12.5,
                FontWeight = bold || header ? FontWeights.Bold : FontWeights.Normal,
                Foreground = green ? Brushes.White : header ? Res("CfDeepBlue") : Res("CfInk"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = left ? TextAlignment.Left : TextAlignment.Center,
            };
            FrameworkElement element = block;
            if (green)
            {
                element = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2F, 0xB8, 0x66)), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Child = block,
                };
            }
            element.Margin = left ? new Thickness(14, 0, 6, 0) : new Thickness(4, 0, 4, 0);
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            AddOnTable.Children.Add(element);
        }

        Row(0, 30, Res("CfBlueSoft"));
        Cell(0, 0, "NAME", left: true, header: true);
        for (var c = 0; c < summary.Columns.Count; c++)
        {
            Cell(0, c + 1, summary.Columns[c].Item.Name.ToUpperInvariant(), header: true);
            ((FrameworkElement)AddOnTable.Children[^1]).ToolTip = summary.Columns[c].Item.Name;
        }
        Cell(0, lastColumn - 1, "ITEMS", header: true);
        Cell(0, lastColumn, "£ VALUE", header: true);

        if (summary.People.Count == 0)
        {
            Row(1, 38, null);
            Cell(1, 0, "None of these sold yet.", left: true);
            return;
        }

        for (var p = 0; p < summary.People.Count; p++)
        {
            var person = summary.People[p];
            var row = p + 1;
            Row(row, 36, null);
            Cell(row, 0, person.Name, left: true, bold: true);
            for (var c = 0; c < summary.Columns.Count; c++)
            {
                var quantity = person.Quantities[c];
                var best = summary.Columns[c].TopQuantity;
                Cell(row, c + 1, quantity.ToString(AddOnCulture), green: quantity > 0 && quantity == best);
            }
            Cell(row, lastColumn - 1, person.Total.ToString(AddOnCulture));
            Cell(row, lastColumn, person.Value.ToString("£#,##0.00", AddOnCulture));
        }

        var team = summary.People.Count + 1;
        Row(team, 36, new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xF4)));
        Cell(team, 0, "Team", left: true, bold: true);
        for (var c = 0; c < summary.Columns.Count; c++) Cell(team, c + 1, summary.Columns[c].Quantity.ToString(AddOnCulture), bold: true);
        Cell(team, lastColumn - 1, summary.TotalQuantity.ToString(AddOnCulture), bold: true);
        Cell(team, lastColumn, summary.TotalValue.ToString("£#,##0.00", AddOnCulture), bold: true);
    }

    private Brush Res(string key) => (Brush)FindResource(key);
}
