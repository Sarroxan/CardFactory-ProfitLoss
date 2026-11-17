using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.App.Views;
using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// Add-on Sales › Top sellers (owner, 17 Nov 2026, concept T1; carrier bags left out). A switch
/// under the first blue bar goes between the tracked add-ons (as before) and the top 25 items
/// of the last 4 weeks (28 days to yesterday), by number sold or by £, for all items or one
/// group, each with "+ Track". The 4-week all-items report is pulled the first time Top sellers
/// is opened that day - on the second sign-in, so a Refresh never waits for it; the app's own
/// sign-in only when there is no second sign-in and nothing else is pulling - and kept in memory
/// until the next day, Sign out or Change store. Its items also fill the add-on list.
/// Ranking: <see cref="TopSellers"/>. No figure elsewhere is touched.
/// </summary>
public partial class MainWindow
{
    private const int TopSellersDays = 28;

    private bool _addOnTopView;
    private TopSellerGroup _topSellersGroup = TopSellerGroup.All;
    private bool _topSellersByValue;
    private IReadOnlyList<TopSeller>? _topSellersAll;
    private DateTime _topSellersDay;
    private bool _topSellersLoading;
    private string? _topSellersProblem;

    private static DateTime TopSellersFrom => DateTime.Today.AddDays(-TopSellersDays);
    private static DateTime TopSellersTo => DateTime.Today.AddDays(-1);

    private void AddOnViewSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }) SetAddOnView(tag == "top");
    }

    private void SetAddOnView(bool top)
    {
        _addOnTopView = top;
        TopSellersPanel.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
        RedrawAddOns();
        if (top) _ = LoadTopSellersAsync();
    }

    private void TopSellersSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        _topSellersByValue = tag == "value";
        DrawTopSellers();
    }

    private void TopSellersGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<TopSellerGroup>(tag, out var group)) return;
        _topSellersGroup = group;
        DrawTopSellers();
    }

    private void TopSellersTrack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TopSeller item }) return;
        AddTracked(new AddOnItem(item.Code, item.Name));   // redraws both views
    }

    /// <summary>Called when reports are forgotten (Sign out, Change store, another store).</summary>
    private void ForgetTopSellers()
    {
        _topSellersAll = null;
        _topSellersProblem = null;
    }

    private async Task LoadTopSellersAsync()
    {
        if (_topSellersLoading) return;
        if (_topSellersAll is not null && _topSellersDay == DateTime.Today) { DrawTopSellers(); return; }

        var loginWindow = _flooidLoginWindow;
        if (loginWindow is null || !loginWindow.IsSignedIn)
        {
            _topSellersProblem = "Sign in to Flooid to see the top sellers.";
            DrawTopSellers();
            return;
        }

        _topSellersLoading = true;
        _topSellersProblem = null;
        DrawTopSellers();
        var from = TopSellersFrom;
        var to = TopSellersTo;
        var clock = Stopwatch.StartNew();
        void Log(string text) => AppFiles.AppendDiagnostic(
            $"[{DateTime.Now:HH:mm:ss}] Top sellers ({from:dd/MM/yyyy} - {to:dd/MM/yyyy}): {text}{Environment.NewLine}");
        try
        {
            var html = await PullTopSellersHtmlAsync(loginWindow, from, to, Log);
            if (html is null)
            {
                _topSellersProblem = "Flooid is busy with another report. Open Top sellers again in a moment.";
                return;
            }

            var report = new AllItemSalesReportParser().Parse(html);
            _topSellersAll = TopSellers.Build(report.Items);
            _topSellersDay = DateTime.Today;

            // The same report fills the add-on list too.
            _addOnSeen = AddOnSettings.RememberSeen(_addOnSeen ?? AddOnSettings.LoadSeen(), report.Items);
            if (!AddOnSettings.ListFilled) AddOnSettings.MarkListFilled(from, to, _addOnSeen.Count);

            Log($"{report.Items.Count} lines, {_topSellersAll.Count} items, {html.Length / 1024} KB, {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            _topSellersProblem = "The top sellers couldn't be fetched from Flooid. Open Top sellers again to try once more.";
            Log($"failed after {clock.ElapsedMilliseconds} ms: {ex.Message}");
        }
        finally
        {
            _topSellersLoading = false;
            DrawTopSellers();
        }
    }

    /// <summary>The 4-week report: on the second sign-in, or on the app's own when there is no
    /// second sign-in and nothing else is pulling. Null when neither is free.</summary>
    private async Task<string?> PullTopSellersHtmlAsync(FlooidLoginWindow loginWindow, DateTime from, DateTime to, Action<string> log)
    {
        if (SecondSignInDetails(loginWindow) is { } details)
        {
            using var lease = await _secondSignIn.AcquireAsync(details, () => _addOnTopView);
            if (lease is not null)
            {
                log("pulling on the second sign-in");
                try
                {
                    return await lease.FetchAsync(v => v.FetchAllItemSalesHtmlAsync(from, to));
                }
                catch (Exception ex)
                {
                    lease.Failed("Top sellers", ex);
                    throw;
                }
            }
        }

        if (_flooidGrabBusy || _quietFetch is { IsCompleted: false }) return null;
        log("pulling on the app's own sign-in");
        _flooidGrabBusy = true;
        try
        {
            return await loginWindow.FetchAllItemSalesHtmlAsync(from, to);
        }
        finally
        {
            _flooidGrabBusy = false;
        }
    }

    // ---- Drawing ----------------------------------------------------------------------

    private void DrawAddOnSwitches()
    {
        foreach (var (button, on) in new[]
                 {
                     (AddOnSwitchTrackedA, !_addOnTopView), (AddOnSwitchTopA, _addOnTopView),
                     (AddOnSwitchTrackedB, !_addOnTopView), (AddOnSwitchTopB, _addOnTopView),
                     (AddOnSwitchTrackedC, !_addOnTopView), (AddOnSwitchTopC, _addOnTopView),
                     (TopSellersByNumber, !_topSellersByValue), (TopSellersByValue, _topSellersByValue),
                 })
        {
            button.Background = on ? Res("CfDeepBlue") : Brushes.White;
            button.Foreground = on ? Brushes.White : Res("CfDeepBlue");
        }

        foreach (var chip in TopSellersGroups.Children.OfType<Button>())
        {
            var on = chip.Tag is string tag && tag == _topSellersGroup.ToString();
            chip.Background = on ? Res("CfYellow") : Brushes.White;
            chip.BorderBrush = on ? Res("CfYellow") : Res("CfLine");
        }
    }

    private void DrawTopSellers()
    {
        if (TopSellersTable is null) return;
        DrawAddOnSwitches();
        TopSellersPeriodText.Text =
            $"{TopSellersFrom.ToString("d MMM", AddOnCulture)} - {TopSellersTo.ToString("d MMM", AddOnCulture)} · gift cards and carrier bags left out";

        TopSellersTable.Children.Clear();
        TopSellersTable.RowDefinitions.Clear();
        TopSellersTable.ColumnDefinitions.Clear();

        string? note = _topSellersLoading
            ? "Getting the last 4 weeks from Flooid… this can take a minute the first time each day."
            : _topSellersProblem;
        var list = _topSellersAll is null ? null : TopSellers.Top(_topSellersAll, _topSellersGroup, _topSellersByValue);
        if (note is null && list is { Count: 0 }) note = "Nothing sold in this group in the last 4 weeks.";
        TopSellersNote.Text = note ?? string.Empty;
        TopSellersNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        if (note is not null || list is null) return;

        foreach (var width in new[] { 40.0, -1, 110, 80, 110, 112 })
            TopSellersTable.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width)
            });

        void Line(int row, double height)
        {
            TopSellersTable.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
            var band = new Border { BorderBrush = Res("CfLineSoft"), BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid.SetRow(band, row);
            Grid.SetColumnSpan(band, 6);
            TopSellersTable.Children.Add(band);
        }

        void Put(FrameworkElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            TopSellersTable.Children.Add(element);
        }

        TextBlock Text(string text, bool right = false, bool bold = false, bool header = false, bool muted = false, bool blue = false) => new()
        {
            Text = text,
            FontFamily = new FontFamily("Arial"),
            FontSize = header ? 9.5 : 12.5,
            FontWeight = bold || header ? FontWeights.Bold : FontWeights.Normal,
            Foreground = header || muted ? Res("CfMuted") : blue ? Res("CfDeepBlue") : Res("CfInk"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 8, 0),
        };

        Line(0, 26);
        Put(Text("#", header: true), 0, 0);
        Put(Text("ITEM", header: true), 0, 1);
        Put(Text("SKU", header: true), 0, 2);
        Put(Text("SOLD", right: true, header: true), 0, 3);
        Put(Text("£ VALUE", right: true, header: true), 0, 4);

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var row = i + 1;
            Line(row, 32);

            Put(new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(6, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = i < 3 ? Res("CfYellow") : Res("CfBlueSoft"),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(AddOnCulture), FontFamily = new FontFamily("Arial"), FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = Res("CfDeepBlue"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            }, row, 0);
            var name = Text(item.Name, bold: true, blue: true);
            name.ToolTip = item.Name;
            Put(name, row, 1);
            Put(Text(item.Code, muted: true), row, 2);
            Put(Text(item.Quantity.ToString("#,##0", AddOnCulture), right: true, bold: true), row, 3);
            Put(Text(item.Value.ToString("£#,##0.00", AddOnCulture), right: true), row, 4);

            if (IsTracked(item.Code))
            {
                var tracking = Text("✓ Tracking", right: true, bold: true);
                tracking.FontSize = 11;
                tracking.Foreground = Res("CfGood");
                Put(tracking, row, 5);
            }
            else
            {
                var track = new Button
                {
                    Tag = item, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Track " + item.Name + " in Add-on Sales",
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                    Template = TrackButtonTemplate(),
                };
                track.Click += TopSellersTrack_Click;
                Put(track, row, 5);
            }
        }
    }

    /// <summary>The yellow "+ Track" pill, as on the suggestions list.</summary>
    private ControlTemplate TrackButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Res("CfYellow"));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.PaddingProperty, new Thickness(9, 3, 9, 3));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "+ Track");
        text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Arial"));
        text.SetValue(TextBlock.FontSizeProperty, 10.5);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        text.SetValue(TextBlock.ForegroundProperty, Res("CfDeepBlue"));
        border.AppendChild(text);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
