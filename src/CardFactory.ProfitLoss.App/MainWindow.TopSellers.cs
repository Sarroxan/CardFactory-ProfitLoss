using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.App.ViewModels;
using CardFactory.ProfitLoss.App.Views;
using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// Add-on Sales › Top sellers (owner, 17 Nov 2026, concept T1; carrier bags left out). A switch
/// under the first blue bar goes between the tracked add-ons (as before) and the top 25 items
/// of the last 4 weeks (28 days to yesterday), by number sold or by £, for all items or one
/// group, each with "+ Track". The 4-week all-items report is pulled quietly after each sign-in
/// (owner, 17 Nov 2026: "make Top sellers instant") on the second sign-in, so it is usually ready
/// before Top sellers is opened. If it isn't, opening Top sellers pulls it as before - on the
/// second sign-in, or the app's own when there is no second sign-in and nothing else is pulling.
/// Kept in memory until the next day, Sign out or Change store. Its items also fill the add-on list.
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
    private int _topSellersGeneration;                    // a pull started before Sign out / Change store is thrown away

    // Progress while the 4 weeks come from Flooid (owner, 17 Nov 2026, concept P2): four steps,
    // each ticked when it has really happened, and a bar that fills over the usual time (till
    // log 17 Nov: 32-37 s) and finishes when the list arrives.
    private const double TopSellersUsualSeconds = 35;
    private int _topSellersStage;                         // 0 connecting · 1 asking · 2 Flooid building · 3 ranking
    private readonly DateTime[] _topSellersStageAt = new DateTime[4];
    private System.Windows.Threading.DispatcherTimer? _topSellersTicker;

    private void TopSellersStage(int stage)
    {
        if (stage <= _topSellersStage && stage != 0) return;
        _topSellersStage = stage;
        _topSellersStageAt[stage] = DateTime.Now;
        DrawTopSellers();
    }

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

    private void TopSellersTrackAll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AddOnItem item }) return;
        AddTracked(item);   // one item for the whole group (concept A1); redraws both views
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
        _topSellersGeneration++;
    }

    /// <summary>
    /// After each sign-in's quiet fetch: get the top sellers ready on the second sign-in, so
    /// opening Top sellers is instant. Nothing waits for it and nothing is shown if it fails -
    /// opening Top sellers then pulls it as before.
    /// </summary>
    private Task PrefetchTopSellersAsync() => LoadTopSellersAsync(quiet: true);

    private async Task LoadTopSellersAsync(bool quiet = false)
    {
        if (_topSellersLoading) return;
        if (_topSellersAll is not null && _topSellersDay == DateTime.Today) { DrawTopSellers(); return; }

        var loginWindow = _flooidLoginWindow;
        if (quiet && (loginWindow is null || !loginWindow.IsSignedIn || SecondSignInDetails(loginWindow) is null)) return;
        if (loginWindow is null || !loginWindow.IsSignedIn)
        {
            _topSellersProblem = "Sign in to Flooid to see the top sellers.";
            DrawTopSellers();
            return;
        }

        _topSellersLoading = true;
        _topSellersProblem = null;
        TopSellersStage(0);
        _topSellersTicker ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _topSellersTicker.Tick -= TopSellersTicker_Tick;
        _topSellersTicker.Tick += TopSellersTicker_Tick;
        _topSellersTicker.Start();
        var from = TopSellersFrom;
        var to = TopSellersTo;
        var generation = _topSellersGeneration;
        var clock = Stopwatch.StartNew();
        void Log(string text) => AppFiles.AppendDiagnostic(
            $"[{DateTime.Now:HH:mm:ss}] Top sellers{(quiet ? " ready in the background" : "")} ({from:dd/MM/yyyy} - {to:dd/MM/yyyy}): {text}{Environment.NewLine}");
        try
        {
            var html = await PullTopSellersHtmlAsync(loginWindow, from, to, Log, quiet);
            if (html is null)
            {
                if (quiet) Log("the second sign-in is not available - pulled when Top sellers is opened");
                if (!quiet || _addOnTopView) _topSellersProblem = "Flooid is busy with another report. Open Top sellers again in a moment.";
                return;
            }

            if (generation != _topSellersGeneration) { Log("thrown away - signed out or changed store meanwhile"); return; }
            TopSellersStage(3);
            var report = new AllItemSalesReportParser().Parse(html);
            _topSellersAll = TopSellers.Build(report.Items);
            _topSellersDay = DateTime.Today;

            // The same report fills the add-on list too.
            var seen = _addOnSeen ?? AddOnSettings.LoadSeen();
            var before = seen.Count;
            _addOnSeen = AddOnSettings.RememberSeen(seen, report.Items);
            if (!AddOnSettings.ListFilled)
            {
                // Once per till (owner, 17 Nov 2026): the add-on list filled from the last 4 weeks.
                AddOnSettings.MarkListFilled(from, to, _addOnSeen.Count);
                var added = Math.Max(0, _addOnSeen.Count - before);
                if (RetrievalOverlay.Visibility != Visibility.Visible && DataContext is MainViewModel viewModel)
                    viewModel.SetStatus($"Add-on list filled from the last 4 weeks · {added} items added", notify: false);
            }

            Log($"{report.Items.Count} lines, {_topSellersAll.Count} items, {html.Length / 1024} KB, {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            // In the background nothing is shown: opening Top sellers simply pulls it again.
            if ((!quiet || _addOnTopView) && generation == _topSellersGeneration)
                _topSellersProblem = "The top sellers couldn't be fetched from Flooid. Open Top sellers again to try once more.";
            Log($"failed after {clock.ElapsedMilliseconds} ms: {ex.Message}");
        }
        finally
        {
            _topSellersLoading = false;
            _topSellersTicker?.Stop();
            DrawTopSellers();
        }
    }

    private void TopSellersTicker_Tick(object? sender, EventArgs e)
    {
        if (_topSellersLoading) DrawTopSellers();
        else _topSellersTicker?.Stop();
    }

    /// <summary>The 4-week report: on the second sign-in, or on the app's own when there is no
    /// second sign-in and nothing else is pulling. Null when neither is free. In the background
    /// (<paramref name="quiet"/>) only the second sign-in is used.</summary>
    private async Task<string?> PullTopSellersHtmlAsync(FlooidLoginWindow loginWindow, DateTime from, DateTime to, Action<string> log, bool quiet = false)
    {
        if (SecondSignInDetails(loginWindow) is { } details)
        {
            using var lease = await _secondSignIn.AcquireAsync(details, () => quiet || _addOnTopView);
            if (lease is not null)
            {
                log("pulling on the second sign-in");
                TopSellersStage(1);
                try
                {
                    return await lease.FetchAsync(v => v.FetchAllItemSalesHtmlAsync(from, to, () => TopSellersStage(2)));
                }
                catch (Exception ex)
                {
                    lease.Failed("Top sellers", ex);
                    throw;
                }
            }
        }

        if (quiet || _flooidGrabBusy || _quietFetch is { IsCompleted: false }) return null;
        log("pulling on the app's own sign-in");
        _flooidGrabBusy = true;
        TopSellersStage(1);
        try
        {
            return await loginWindow.FetchAllItemSalesHtmlAsync(from, to, () => TopSellersStage(2));
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

        DrawTopSellersProgress();
        string? note = _topSellersLoading ? null : _topSellersProblem;
        var list = _topSellersAll is null || _topSellersLoading ? null : TopSellers.Top(_topSellersAll, _topSellersGroup, _topSellersByValue);
        if (note is null && list is { Count: 0 }) note = "Nothing sold in this group in the last 4 weeks.";
        TopSellersNote.Text = note ?? string.Empty;
        TopSellersNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        if (note is not null || list is null || _topSellersLoading) return;

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

        // Concept A1 (owner, 18 Nov 2026): Reusable bags and Stamps start with a starred "All" row -
        // everything in the group added together (every stamp, Christmas ones too, as tracked) -
        // whose "+ Track all" adds ONE item to Items to track.
        var first = 1;
        AddOnItem? groupItem = _topSellersGroup switch
        {
            TopSellerGroup.ReusableBags => AddOnItem.ReusableBags,
            TopSellerGroup.Stamps => AddOnItem.Stamps,
            _ => null,
        };
        if (groupItem is not null)
        {
            var members = _topSellersAll!.Where(t => t.Group == _topSellersGroup).ToList();
            Line(1, 36);
            var band = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEA)), BorderBrush = Res("CfYellow"),
                BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 2, 0, 2),
            };
            Grid.SetRow(band, 1);
            Grid.SetColumnSpan(band, 6);
            TopSellersTable.Children.Add(band);
            Put(Text("★", bold: true, blue: true), 1, 0);
            var allName = Text(groupItem.IsStamps ? "All stamps" : "All reusable bags", bold: true, blue: true);
            allName.ToolTip = groupItem.IsStamps
                ? "Every stamp added together, Christmas stamps included"
                : "Every item named \"reusable bag\" added together";
            Put(allName, 1, 1);
            Put(Text($"{members.Count} items", muted: true), 1, 2);
            Put(Text(members.Sum(t => t.Quantity).ToString("#,##0", AddOnCulture), right: true, bold: true), 1, 3);
            Put(Text(members.Sum(t => t.Value).ToString("£#,##0.00", AddOnCulture), right: true), 1, 4);
            if (IsTracked(groupItem.Key))
            {
                var tracking = Text("✓ Tracking", right: true, bold: true);
                tracking.FontSize = 11;
                tracking.Foreground = Res("CfGood");
                Put(tracking, 1, 5);
            }
            else
            {
                var trackAll = new Button
                {
                    Tag = groupItem, Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Track " + (groupItem.IsStamps ? "every stamp" : "every reusable bag") + " as one item in Add-on Sales",
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                    Template = TrackButtonTemplate("+ Track all"),
                };
                trackAll.Click += TopSellersTrackAll_Click;
                Put(trackAll, 1, 5);
            }
            first = 2;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var row = i + first;
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

    /// <summary>Concept P2: the four steps and the bar, while the 4 weeks come from Flooid.</summary>
    private void DrawTopSellersProgress()
    {
        TopSellersProgress.Children.Clear();
        TopSellersProgress.Visibility = _topSellersLoading ? Visibility.Visible : Visibility.Collapsed;
        if (!_topSellersLoading) return;

        var now = DateTime.Now;
        var steps = new[]
        {
            "Connected to Flooid",
            $"Asked for every item sold, {TopSellersFrom.ToString("d MMM", AddOnCulture)} - {TopSellersTo.ToString("d MMM", AddOnCulture)}",
            "Flooid is putting the report together",
            "Ranking the top sellers",
        };
        for (var i = 0; i < steps.Length; i++)
        {
            // Step i is done once the stage after it has started; it is the current one while its stage runs.
            var done = _topSellersStage > i;
            var current = _topSellersStage == i;
            var seconds = done ? (_topSellersStageAt[i + 1] - _topSellersStageAt[i]).TotalSeconds
                        : current ? (now - _topSellersStageAt[i]).TotalSeconds : -1;

            var row = new Grid { Height = 34 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement icon = done
                ? new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Res("CfGood"),
                    Child = new TextBlock { Text = "✓", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold,
                                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                }
                : current
                    ? new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(4),
                                   BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xC2)), Background = Res("CfYellow") }
                    : new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
                                   BorderBrush = Res("CfLine") };
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);

            var label = new TextBlock
            {
                Text = steps[i], FontFamily = new FontFamily("Arial"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = current ? FontWeights.Bold : FontWeights.Normal,
                Foreground = done || current ? Res("CfInk") : Res("CfMuted"),
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            if (seconds >= 0)
            {
                var time = new TextBlock
                {
                    Text = $"{Math.Max(0, (int)Math.Round(seconds)).ToString(AddOnCulture)} s", FontFamily = new FontFamily("Arial"), FontSize = 11,
                    Foreground = Res("CfMuted"), VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(time, 2);
                row.Children.Add(time);
            }
            TopSellersProgress.Children.Add(row);
        }

        // The bar: the usual time, never past 95% until the list is in.
        var elapsed = (now - _topSellersStageAt[0]).TotalSeconds;
        var share = Math.Min(0.95, elapsed / TopSellersUsualSeconds);
        var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Res("CfBlueSoft"), Margin = new Thickness(0, 12, 0, 0) };
        var fill = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Res("CfYellow"), HorizontalAlignment = HorizontalAlignment.Left,
                                Width = Math.Max(10, 520 * share) };
        track.Child = fill;
        TopSellersProgress.Children.Add(track);

        var left = TopSellersUsualSeconds - elapsed;
        TopSellersProgress.Children.Add(new TextBlock
        {
            Text = (left > 2 ? $"about {(int)Math.Ceiling(left)} s left" : "almost there") + " · you can keep using the app",
            FontFamily = new FontFamily("Arial"), FontSize = 11, Foreground = Res("CfMuted"),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0),
        });
    }

    /// <summary>The yellow "+ Track" pill, as on the suggestions list.</summary>
    private ControlTemplate TrackButtonTemplate(string label = "+ Track")
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Res("CfYellow"));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.PaddingProperty, new Thickness(9, 3, 9, 3));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, label);
        text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Arial"));
        text.SetValue(TextBlock.FontSizeProperty, 10.5);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        text.SetValue(TextBlock.ForegroundProperty, Res("CfDeepBlue"));
        border.AppendChild(text);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
