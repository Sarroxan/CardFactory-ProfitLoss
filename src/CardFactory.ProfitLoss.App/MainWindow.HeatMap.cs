using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.ViewModels;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// By hour › Heat map (owner, 18 Nov 2026, concept K2): on a week, a Table / Heat map switch.
/// The heat map has a row per day and a column per hour; each square is that hour's P&amp;L
/// against the ABV target (sales − target × transactions, gift cards in - Flooid gives no hour
/// for them, as the By hour table), green above and red below, stronger the further off. Hours
/// with no sales are striped. On the right, each day's hours added up. BEST / WORST name the
/// best and worst hour of the week. Display only - Flooid's own day-by-hour figures.
/// </summary>
public partial class MainWindow
{
    private bool _byHourHeat = true;   // on a week the heat map shows first

    private static readonly Color HeatGood = Color.FromRgb(0x0B, 0x7A, 0x35);
    private static readonly Color HeatBad = Color.FromRgb(0xC8, 0x45, 0x4F);
    private static readonly Color HeatNone = Color.FromRgb(0xF6, 0xF9, 0xFC);

    // Striped, for an hour with no sales.
    private static readonly Brush ClosedHourBrush = MakeClosedHourBrush();

    private static Brush MakeClosedHourBrush()
    {
        var stripe = Color.FromRgb(0xF4, 0xF7, 0xFB);
        var brush = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(stripe, 0), new GradientStop(stripe, 0.5),
            new GradientStop(Colors.White, 0.5), new GradientStop(Colors.White, 1),
        }, new Point(0, 0), new Point(8, 8))
        {
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Repeat,
        };
        brush.Freeze();
        return brush;
    }

    private void ByHourTable_Click(object sender, RoutedEventArgs e) { _byHourHeat = false; UpdateByHourView(); }

    private void ByHourHeat_Click(object sender, RoutedEventArgs e) { _byHourHeat = true; UpdateByHourView(); }

    /// <summary>Which By hour view shows: the heat map only on a week that has each day's hours.</summary>
    private void UpdateByHourView()
    {
        if (BranchHeatPanel is null || BranchByHourPanel is null || DataContext is not MainViewModel vm) return;
        var onByHour = _branchDetail == BranchDetail.ByHour;
        var weekHours = vm.IsRangeMode && vm.BranchDayHours.Count > 0;
        ByHourViewSwitch.Visibility = onByHour && weekHours ? Visibility.Visible : Visibility.Collapsed;
        var heat = onByHour && weekHours && _byHourHeat;
        BranchHeatPanel.Visibility = heat ? Visibility.Visible : Visibility.Hidden;
        BranchByHourPanel.Visibility = onByHour && !heat ? Visibility.Visible : Visibility.Hidden;
        ByHourTableButton.Tag = _byHourHeat ? null : "Active";
        ByHourHeatButton.Tag = _byHourHeat ? "Active" : null;
        if (heat) DrawHeatMap();
        else if (onByHour) DrawByHour();
    }

    private void DrawHeatMap()
    {
        if (BranchHeatGrid is null || DataContext is not MainViewModel vm) return;
        var grid = BranchHeatGrid;
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        HeatFacts.Children.Clear();

        var days = vm.BranchDayHours;
        var target = vm.AbvTarget;
        var note = days.Count == 0 ? "No figures yet · Refresh to pull Branch Performance for the week."
                 : target <= 0m ? "Set an ABV target to see the P&L hour by hour."
                 : null;
        HeatNote.Text = note ?? string.Empty;
        HeatNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        if (note is not null) return;

        static string Start(string band) => band.Split('-')[0].Trim();
        var bands = days.SelectMany(d => d.Hours).Where(h => h.Sales != 0m || h.Transactions != 0)
            .Select(h => Start(h.TimeBand)).Distinct()
            .OrderBy(b => TimeSpan.TryParse(b, CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.MaxValue)
            .ToList();
        decimal Pl(BranchHourFigure h) => h.Transactions > 0 ? h.Sales - target * h.Transactions : 0m;
        var cells = days.SelectMany(d => d.Hours.Where(h => h.Sales != 0m || h.Transactions != 0)
                        .Select(h => (Day: d.Date, Band: Start(h.TimeBand), Pl: Pl(h))))
                        .ToList();
        if (cells.Count == 0)
        {
            HeatNote.Text = "No sales in these days yet.";
            HeatNote.Visibility = Visibility.Visible;
            return;
        }
        var largest = Math.Max(1m, cells.Max(c => Math.Abs(c.Pl)));

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        foreach (var _ in bands) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        TextBlock Label(string text, double size, bool bold, Brush colour, HorizontalAlignment align) => new()
        {
            Text = text, FontFamily = new FontFamily("Arial"), FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = colour,
            HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
        };
        void Put(UIElement e, int row, int column) { Grid.SetRow(e, row); Grid.SetColumn(e, column); grid.Children.Add(e); }
        static string Money(decimal v) => Math.Round(v) == 0m ? "£0" : (v > 0m ? "+£" : "−£") + Math.Abs(Math.Round(v)).ToString("N0");

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        for (var i = 0; i < bands.Count; i++) Put(Label(bands[i], 9.5, true, Res("CfDeepBlue"), HorizontalAlignment.Center), 0, i + 1);
        Put(Label("DAY", 9.5, true, Res("CfDeepBlue"), HorizontalAlignment.Right), 0, bands.Count + 1);

        var row = 1;
        foreach (var day in days)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            Put(Label(day.Date.ToString("ddd d", CultureInfo.GetCultureInfo("en-GB")), 12, true, Res("CfInk"), HorizontalAlignment.Left), row, 0);
            decimal dayTotal = 0m;
            for (var i = 0; i < bands.Count; i++)
            {
                var hour = day.Hours.FirstOrDefault(h => Start(h.TimeBand) == bands[i] && (h.Sales != 0m || h.Transactions != 0));
                FrameworkElement cell;
                if (hour is null)
                {
                    cell = new Border
                    {
                        Margin = new Thickness(1.5), CornerRadius = new CornerRadius(6),
                        Background = ClosedHourBrush,
                        ToolTip = $"{day.Date:ddd d MMM} {bands[i]} · no sales",
                    };
                }
                else
                {
                    var pl = Pl(hour);
                    dayTotal += pl;
                    var strength = (double)Math.Min(1m, Math.Abs(pl) / largest);
                    var to = pl >= 0m ? HeatGood : HeatBad;
                    var colour = Color.FromRgb(
                        (byte)(HeatNone.R + (to.R - HeatNone.R) * strength),
                        (byte)(HeatNone.G + (to.G - HeatNone.G) * strength),
                        (byte)(HeatNone.B + (to.B - HeatNone.B) * strength));
                    cell = new Border
                    {
                        Margin = new Thickness(1.5), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(colour),
                        Child = Label(Money(pl), 11, true, strength > 0.55 ? Brushes.White : Res("CfInk"), HorizontalAlignment.Center),
                        ToolTip = $"{day.Date:ddd d MMM} {bands[i]} · £{hour.Sales:N2} · {hour.Transactions} trans · ABV £{hour.Abv:N2} · P&L {Money(pl)}",
                    };
                }
                Put(cell, row, i + 1);
            }
            Put(Label(Money(dayTotal), 12, true, dayTotal >= 0m ? Res("CfGood") : ByHourRed, HorizontalAlignment.Right), row, bands.Count + 1);
            row++;
        }

        var best = cells.MaxBy(c => c.Pl);
        var worst = cells.MinBy(c => c.Pl);
        void Fact(string label, string text)
        {
            var chip = new Border { Background = Res("CfBlueSoft"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
            var line = new TextBlock { FontFamily = new FontFamily("Arial"), FontSize = 11, Foreground = Res("CfDeepBlue"), VerticalAlignment = VerticalAlignment.Center };
            line.Inlines.Add(new System.Windows.Documents.Run(label + "  ") { FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Res("CfMuted") });
            line.Inlines.Add(new System.Windows.Documents.Run(text) { FontWeight = FontWeights.Bold });
            chip.Child = line;
            HeatFacts.Children.Add(chip);
        }
        Fact("BEST", $"{best.Day:ddd} {best.Band} · {Money(best.Pl)}");
        Fact("WORST", $"{worst.Day:ddd} {worst.Band} · {Money(worst.Pl)}");
    }
}
