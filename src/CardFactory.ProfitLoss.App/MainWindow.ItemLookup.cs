using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.App.ViewModels;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// Add-on Sales › Item lookup (owner, 26 Nov 2026, concept L1). Type a SKU or part of a name to see
/// how many sold, for how much and who sold it, with "+ Track this item". Two periods: the last 4
/// weeks - the report Top sellers already fetches after sign-in, so usually instant - and the date on
/// the calendar (the all-items report that comes with Generate reports). Nothing extra is pulled
/// from Flooid except the 4 weeks when they aren't ready yet. The item names known on this PC are
/// the add-on list, kept encrypted. Matching: <see cref="ItemLookup"/>. No figure elsewhere is touched.
/// </summary>
public partial class MainWindow
{
    private bool _addOnLookupView;
    private bool _itemLookupCalendar;
    private string? _itemLookupPick;              // another match clicked: shown instead of the best
    private DispatcherTimer? _itemLookupTyping;

    private void OpenItemLookup()
    {
        DrawItemLookup();
        if (!_itemLookupCalendar) _ = LoadTopSellersAsync();   // instant when the 4 weeks are ready
        Dispatcher.InvokeAsync(() => ItemLookupBox.Focus(), DispatcherPriority.Input);
    }

    private void ItemLookupPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        _itemLookupCalendar = tag == "calendar";
        DrawItemLookup();
        if (!_itemLookupCalendar) _ = LoadTopSellersAsync();
    }

    private void ItemLookupBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _itemLookupPick = null;
        // Wait for a pause in the typing; 4 weeks of lines is a lot to search on every key.
        _itemLookupTyping ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _itemLookupTyping.Tick -= ItemLookupTyping_Tick;
        _itemLookupTyping.Tick += ItemLookupTyping_Tick;
        _itemLookupTyping.Stop();
        _itemLookupTyping.Start();
    }

    private void ItemLookupTyping_Tick(object? sender, EventArgs e)
    {
        _itemLookupTyping?.Stop();
        DrawItemLookup();
    }

    private void ItemLookupOther_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code }) return;
        _itemLookupPick = code;
        DrawItemLookup();
    }

    private void ItemLookupTrack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ItemLookupMatch item }) return;
        AddTracked(new AddOnItem(item.Code, item.Name));   // redraws, so it then shows "✓ Tracking"
    }

    // ---- Drawing ----------------------------------------------------------------------

    private void DrawItemLookup()
    {
        if (ItemLookupResults is null || !_addOnLookupView) return;
        DrawAddOnSwitches();
        ItemLookupKnownText.Text = $"🔒 names kept encrypted on this PC · {(_addOnSeen?.Count ?? 0).ToString("#,##0", AddOnCulture)} items known";
        ItemLookupResults.Children.Clear();

        var viewModel = DataContext as MainViewModel;
        var lines = _itemLookupCalendar ? viewModel?.AllItemLines : _topSellersLines;
        var period = _itemLookupCalendar
            ? viewModel?.PeriodDisplayText ?? "the date on the calendar"
            : $"{TopSellersFrom.ToString("d MMM", AddOnCulture)} - {TopSellersTo.ToString("d MMM", AddOnCulture)}";
        var typed = ItemLookupBox.Text.Trim();

        if (lines is null)
        {
            if (_itemLookupCalendar)
                Note("Nothing for the date on the calendar yet. Press Generate reports first, or look up the last 4 weeks.");
            else if (_topSellersLoading)
                Note("Getting the last 4 weeks from Flooid… (about half a minute; it's the same report as Top sellers).");
            else if (_flooidLoginWindow is not { IsSignedIn: true })
                Note("Sign in to Flooid to look up the last 4 weeks.");
            else
                Note(_topSellersProblem is null
                    ? "The last 4 weeks aren't ready yet. Press Last 4 weeks to get them."
                    : "The last 4 weeks couldn't be fetched from Flooid. Press Last 4 weeks to try again.");
            return;
        }

        if (typed.Length == 0)
        {
            Note($"Type a SKU or part of a name to see how many sold ({period}), for how much and who sold it.");
            return;
        }

        var found = ItemLookup.Find(lines, typed, max: 9);
        if (found.Count == 0)
        {
            Note($"Nothing matching \"{typed}\" sold in {period}.");
            return;
        }

        var best = found.FirstOrDefault(f => f.Code == _itemLookupPick) ?? found[0];
        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.Children.Add(BestMatchCard(best, period));
        var who = WhoSoldItCard(best);
        Grid.SetColumn(who, 2);
        cards.Children.Add(who);
        ItemLookupResults.Children.Add(cards);

        var others = found.Where(f => f != best).ToList();
        if (others.Count > 0)
        {
            var line = new WrapPanel { Margin = new Thickness(2, 12, 0, 0) };
            line.Children.Add(LookupText("Other matches:", muted: true, margin: new Thickness(0, 0, 6, 0)));
            foreach (var other in others)
            {
                var link = new Button
                {
                    Tag = other.Code, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 12, 4),
                    ToolTip = "SKU " + other.Code,
                    Template = LookupLinkTemplate($"{other.Name} ({other.Quantity.ToString("#,##0", AddOnCulture)})"),
                };
                link.Click += ItemLookupOther_Click;
                line.Children.Add(link);
            }
            ItemLookupResults.Children.Add(line);
        }

        void Note(string text) => ItemLookupResults.Children.Add(LookupText(text, muted: true, size: 12, margin: new Thickness(4, 8, 4, 0)));
    }

    private Border BestMatchCard(ItemLookupMatch item, string period)
    {
        var panel = new StackPanel();
        panel.Children.Add(LookupText(item.Name, size: 16, bold: true, blue: true));
        var where = "SKU " + item.Code + (item.Department.Length > 0 ? " · " + item.Department : string.Empty);
        panel.Children.Add(LookupText(where, muted: true, size: 11, margin: new Thickness(0, 2, 0, 0)));

        var figures = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        figures.Children.Add(Figure("SOLD", item.Quantity.ToString("#,##0", AddOnCulture)));
        figures.Children.Add(Figure("VALUE", item.Value.ToString("£#,##0.00", AddOnCulture)));
        panel.Children.Add(figures);
        panel.Children.Add(LookupText(period, muted: true, size: 10, margin: new Thickness(0, 4, 0, 0)));

        if (IsTracked(item.Code))
        {
            var tracking = LookupText("✓ Tracking", bold: true, margin: new Thickness(0, 12, 0, 0));
            tracking.Foreground = Res("CfGood");
            panel.Children.Add(tracking);
        }
        else
        {
            var track = new Button
            {
                Tag = item, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0),
                ToolTip = "Add it to Items to track",
                Template = TrackButtonTemplate("+ Track this item"),
            };
            track.Click += ItemLookupTrack_Click;
            panel.Children.Add(track);
        }
        return LookupCard(panel);

        StackPanel Figure(string title, string value)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 26, 0) };
            stack.Children.Add(LookupText(title, muted: true, size: 9.5, bold: true));
            var big = LookupText(value, size: 24, bold: true);
            big.Foreground = Res("CfInk");
            stack.Children.Add(big);
            return stack;
        }
    }

    private Border WhoSoldItCard(ItemLookupMatch item)
    {
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

        var row = 0;
        void Row(double height, Brush? background, params TextBlock[] cells)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
            var band = new Border
            {
                Background = background ?? Brushes.Transparent, BorderBrush = Res("CfLineSoft"),
                BorderThickness = new Thickness(0, 0, 0, background is null ? 1 : 0),
            };
            Grid.SetRow(band, row);
            Grid.SetColumnSpan(band, 3);
            table.Children.Add(band);
            for (var c = 0; c < cells.Length; c++)
            {
                cells[c].VerticalAlignment = VerticalAlignment.Center;
                cells[c].Margin = new Thickness(8, 0, 8, 0);
                if (c > 0) cells[c].HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(cells[c], row);
                Grid.SetColumn(cells[c], c);
                table.Children.Add(cells[c]);
            }
            row++;
        }

        Row(26, Res("CfBlueSoft"), Header("WHO SOLD IT"), Header("QTY"), Header("£"));
        foreach (var seller in item.Sellers)
            Row(30, null,
                LookupText(seller.OperatorName.Length > 0 ? seller.OperatorName : seller.OperatorId, size: 12.5),
                LookupText(seller.Quantity.ToString("#,##0", AddOnCulture), size: 12.5),
                LookupText(seller.Value.ToString("#,##0.00", AddOnCulture), size: 12.5));
        return LookupCard(table);

        TextBlock Header(string text)
        {
            var header = LookupText(text, size: 9.5, bold: true);
            header.Foreground = Res("CfDeepBlue");
            return header;
        }
    }

    private Border LookupCard(UIElement content) => new()
    {
        Background = Brushes.White, BorderBrush = Res("CfLine"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Child = content,
    };

    private TextBlock LookupText(string text, bool muted = false, bool bold = false, bool blue = false, double size = 12, Thickness? margin = null) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Arial"),
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = muted ? Res("CfMuted") : blue ? Res("CfDeepBlue") : Res("CfInk"),
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0),
    };

    private ControlTemplate LookupLinkTemplate(string label)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, label);
        text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Arial"));
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetValue(TextBlock.ForegroundProperty, Res("CfBlue"));
        text.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
        text.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        return new ControlTemplate(typeof(Button)) { VisualTree = text };
    }
}
