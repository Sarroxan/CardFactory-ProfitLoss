using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.ViewModels;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// Branch Performance › By hour (owner, 17 Nov 2026, concepts H2 + G3): each hour's P&amp;L
/// against the ABV target, then one row taking the gift cards off, then the day's P&amp;L.
///
/// Each hour comes straight from Flooid's Branch Performance report (Sales Value and Customer
/// Count), which includes gift cards - Flooid doesn't say which hour a gift card was sold in
/// (checked on the saved page 07: its daily total equals the Refunds report's, gift cards in).
/// So an hour's P&amp;L = its sales − ABV target × its transactions, gift cards in; the gift card
/// row is the Reports tab's gift card total; the Day P&amp;L row is the Reports tab's P&amp;L itself
/// (<see cref="MainViewModel.TotalProfitLoss"/>), so it can never disagree with it. If the hours
/// minus gift cards don't land on it (Flooid's two reports differing), a note says by how much.
/// Display only: no figure anywhere else is worked out or changed here.
/// </summary>
public partial class MainWindow
{
    private enum BranchDetail { Performance, ByHour, GiftCards }

    private static readonly Brush ByHourRed = new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x46));
    private static readonly Brush ByHourRedBar = new SolidColorBrush(Color.FromRgb(0xC8, 0x45, 0x4F));
    private const double ByHourHalfBar = 200;

    private void BranchByHourTab_Click(object sender, RoutedEventArgs e) => SetBranchDetailView(BranchDetail.ByHour);

    private void ByHour_ViewModelChanged(string? property)
    {
        if (_branchDetail != BranchDetail.ByHour) return;
        if (property is nameof(MainViewModel.BranchTotalSales) or nameof(MainViewModel.BranchTotalTransactions)
            or nameof(MainViewModel.TotalProfitLoss) or nameof(MainViewModel.TotalGiftCards)
            or nameof(MainViewModel.AbvTarget) or nameof(MainViewModel.TotalTransactions))
            DrawByHour();
    }

    private void DrawByHour()
    {
        if (BranchByHourTable is null || DataContext is not MainViewModel vm) return;
        var table = BranchByHourTable;
        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Clear();

        var hours = vm.BranchHours.Where(h => h.Sales != 0m || h.Transactions != 0).ToList();
        var target = vm.AbvTarget;
        string? note = hours.Count == 0 ? "No figures yet · Refresh to pull Branch Performance."
                     : target <= 0m ? "Set an ABV target to see the P&L hour by hour."
                     : null;
        BranchByHourNote.Text = note ?? string.Empty;
        BranchByHourNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        if (note is not null) return;

        // Same rule as each person's P&L: none without transactions.
        decimal HourPl(BranchHourlyRowViewModel h) => h.Transactions > 0 ? h.Sales - target * h.Transactions : 0m;
        var largest = Math.Max(1m, hours.Max(h => Math.Abs(HourPl(h))));

        foreach (var width in new[] { 110.0, 130, 100, 100, -1, 120 })
            table.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width)
            });

        var row = 0;
        Border Band(Brush? background = null, double height = 34)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
            var band = new Border
            {
                Background = background ?? Brushes.White,
                BorderBrush = Res("CfLineSoft"), BorderThickness = new Thickness(0, 0, 0, 1),
            };
            Grid.SetRow(band, row);
            Grid.SetColumnSpan(band, 6);
            table.Children.Add(band);
            return band;
        }

        void Put(UIElement element, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            table.Children.Add(element);
        }

        TextBlock Text(string text, bool right = true, bool bold = false, Brush? colour = null, double size = 12.5) => new()
        {
            Text = text,
            FontFamily = new FontFamily("Arial"),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = colour ?? Res("CfInk"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 12, 0),
        };

        static string Signed(decimal value) => value >= 0m ? $"+£{value:N2}" : $"−£{Math.Abs(value):N2}";
        Brush Colour(decimal value) => value >= 0m ? Res("CfGood") : ByHourRed;

        foreach (var hour in hours)
        {
            var pl = HourPl(hour);
            Band();
            Put(Text(hour.TimeBand, right: false, bold: true), 0);
            Put(Text($"£{hour.Sales:N2}"), 1);
            Put(Text(hour.Transactions.ToString("N0")), 2);
            Put(Text($"£{hour.Abv:N2}"), 3);
            Put(PlBar(pl, largest), 4);
            Put(Text(Signed(pl), bold: true, colour: Colour(pl)), 5);
            row++;
        }

        // G3: the gift cards as their own row - sold during the day, not counted.
        var giftCards = vm.TotalGiftCards;
        Band(new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB)));
        var label = new TextBlock
        {
            FontFamily = new FontFamily("Arial"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0), Foreground = Res("CfInk"),
        };
        label.Inlines.Add(new System.Windows.Documents.Run("Gift cards") { FontWeight = FontWeights.Bold });
        label.Inlines.Add(new System.Windows.Documents.Run("  any hour, not counted") { FontSize = 10.5, Foreground = Res("CfMuted") });
        Grid.SetColumnSpan(label, 4);
        Put(label, 0);
        Put(Text(giftCards > 0m ? $"−£{giftCards:N2}" : "£0.00", bold: true, colour: giftCards > 0m ? ByHourRed : Res("CfMuted")), 5);
        row++;

        // The day: exactly the Reports tab's P&L.
        var day = vm.TotalProfitLoss;
        var dayBand = Band(Res("CfDeepBlue"), 32);
        dayBand.CornerRadius = new CornerRadius(0, 0, 8, 8);
        dayBand.BorderThickness = new Thickness(0);
        Put(Text("DAY P&L", right: false, bold: true, colour: Res("CfYellow")), 0);
        Put(Text($"£{hours.Sum(h => h.Sales) - giftCards:N2}", colour: Brushes.White), 1);
        Put(Text(hours.Sum(h => h.Transactions).ToString("N0"), colour: Brushes.White), 2);
        Put(Text(Signed(day), bold: true, colour: Res("CfYellow")), 5);
        row++;

        // Flooid's hour report and its till report should agree; if they don't, say so.
        var difference = hours.Sum(h => HourPl(h)) - giftCards - day;
        if (Math.Abs(difference) >= 0.01m)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            var gap = Text($"The hours and the Reports tab differ by £{Math.Abs(difference):N2} · Flooid's hour report and till report don't quite match for this period.",
                           right: false, colour: Res("CfMuted"), size: 10.5);
            Grid.SetColumnSpan(gap, 6);
            Put(gap, 0);
        }
    }

    /// <summary>A bar either side of a centre line: green right when above target, red left when below.</summary>
    private FrameworkElement PlBar(decimal pl, decimal largest)
    {
        var grid = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var centre = new Border { Background = Res("CfLine"), Height = 22 };
        Grid.SetColumn(centre, 1);
        grid.Children.Add(centre);

        if (pl != 0m)
        {
            var bar = new Border
            {
                Height = 12, CornerRadius = new CornerRadius(4),
                Width = Math.Max(3, (double)(Math.Abs(pl) / largest) * ByHourHalfBar),
                Background = pl > 0m ? Res("CfGood") : ByHourRedBar,
                HorizontalAlignment = pl > 0m ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(bar, pl > 0m ? 2 : 0);
            grid.Children.Add(bar);
        }
        return grid;
    }
}
