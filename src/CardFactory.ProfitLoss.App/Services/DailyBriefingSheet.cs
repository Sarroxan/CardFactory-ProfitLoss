using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardFactory.ProfitLoss.App.ViewModels;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// The store's own Daily Briefing sheet (A4 landscape, two pages), filled in with the app's
/// figures. The blank sheet is the owner's form rendered at 300 dpi (Assets/DailyBriefing-1/2.png;
/// page 1 with its printed times removed, since the app writes them to match the hours traded).
/// Every box position below is in PDF points, taken from that form's PDF.
///
/// Filled by the app: Day, Date, Sales/ABV/AUB target and actual, the hourly rows, Voids,
/// Refunds and Staff discounts (Qty and £ Value), and Team Performance (up to 8 people).
/// Left for the team: the checklist, every Initials box, and all of page 2.
///
/// Design (chosen 14 Oct 2026, "C1 + D1"): figures in Fredoka SemiBold, CfDeepBlue; boxes the
/// app filled are tinted CfBlueSoft so printed and handwritten boxes are told apart; Actual
/// boxes solid green (on or above target) or red (below) with the difference inside; P&amp;L
/// boxes solid green or red; the best hour ringed in yellow; £20+ voids and refunds amber.
/// 16 Oct 2026 (A1 + S2): the best sales hour filled navy with white figures and a yellow
/// edge; hourly ABV and AUB boxes ringed yellow when they beat the day's target.
/// </summary>
public static class DailyBriefingSheet
{
    private const double Pt = 96.0 / 72.0;                  // PDF points to WPF units
    private const double PageWidthPt = 841.89, PageHeightPt = 595.28;

    private static readonly FontFamily Figures = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Fredoka SemiBold");
    private static readonly FontFamily Chewy = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Chewy");

    private static readonly Brush Navy = Frozen(0x00, 0x4E, 0xA1);       // CfDeepBlue
    private static readonly Brush SheetBlue = Frozen(0x00, 0x5B, 0xBB);  // CfBlue, the sheet's own times
    private static readonly Brush Tint = Frozen(0xE3, 0xEC, 0xF7);       // CfBlueSoft
    private static readonly Brush Yellow = Frozen(0xFF, 0xC9, 0x07);     // CfYellow
    private static readonly Brush Green = Frozen(0x2F, 0xB8, 0x66);
    private static readonly Brush Red = Frozen(0xE0, 0x4A, 0x55);
    private static readonly Brush AmberFill = Frozen(0xFF, 0xF1, 0xDC);
    private static readonly Brush AmberText = Frozen(0x9A, 0x5B, 0x00);
    private static readonly Brush Muted = Frozen(0x66, 0x78, 0x91);      // CfMuted

    public const int HourRows = 12, TeamSlots = 8;

    public sealed record Measure(decimal? Target, decimal? Actual, bool IsMoney);
    public sealed record Hour(int StartHour, decimal Sales, decimal Abv, decimal Aub);
    public sealed record Reports(int VoidsQuantity, decimal VoidsValue, int RefundsQuantity, decimal RefundsValue,
        decimal? StaffQuantity, decimal? StaffValue);
    /// <param name="ProfitLossState">"Good", "Bad" or "None" (no ABV target, so no P&amp;L).</param>
    public sealed record Person(string Name, decimal Abv, decimal Aub, string ProfitLossText, string ProfitLossState);
    public sealed record Data(DateTime Date, Measure Sales, Measure Abv, Measure Aub, IReadOnlyList<Hour> Hours,
        Reports? Reports, IReadOnlyList<Person> Team, int TeamNotShown);

    /// <summary>The day's figures as the app holds them. Call only in single-day mode.</summary>
    public static Data FromViewModel(MainViewModel vm)
    {
        var date = (vm.SelectedDate ?? DateTime.Today).Date;
        var hasTeam = vm.TotalTransactions > 0;
        decimal? Target(decimal t) => t > 0m ? t : null;
        decimal? Actual(decimal a) => hasTeam ? a : null;

        var hours = new List<Hour>();
        foreach (var row in vm.BranchHours)
        {
            var start = StartHour(row.TimeBand);
            if (start is null || (row.Sales == 0m && row.Transactions == 0)) continue;
            hours.Add(new Hour(start.Value, row.Sales, row.Abv, row.Aub));
        }

        Reports? reports = vm.HasReportData
            ? new Reports(vm.ReportVoidsQuantity, vm.ReportVoidsValue, vm.ReportRefundsQuantity, vm.ReportRefundsValue,
                vm.ReportStaffDiscountQuantity, vm.ReportStaffDiscountValue)
            : null;

        var people = vm.Operators
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && (o.CountedSales != 0m || o.ToInput().Transactions > 0))
            .OrderByDescending(o => o.CountedSales)
            .ToList();
        var team = people.Take(TeamSlots)
            .Select(o => new Person(o.Name.Trim(), o.Abv, o.Aub, o.ProfitLossText, string.IsNullOrEmpty(o.ProfitLossState) ? "None" : o.ProfitLossState))
            .ToList();

        return new Data(date,
            new Measure(Target(vm.SalesTarget), Actual(vm.TotalSales), true),
            new Measure(Target(vm.AbvTarget), Actual(vm.ActualAbv), true),
            new Measure(Target(vm.AubTarget), Actual(vm.ActualAub), false),
            hours, reports, team, Math.Max(0, people.Count - TeamSlots));
    }

    /// <summary>"09:00 - 09:59", "09:00–10:00" or "9:00" → 9.</summary>
    public static int? StartHour(string? timeBand)
    {
        var m = Regex.Match(timeBand ?? string.Empty, @"^\s*(\d{1,2})\s*[:.]");
        return m.Success && int.TryParse(m.Groups[1].Value, out var h) && h is >= 0 and <= 23 ? h : null;
    }

    /// <summary>The first hour on the sheet: 10:00 as printed, or earlier if the store traded earlier,
    /// and moved later only as far as needed to fit the last hour traded into the 12 rows.</summary>
    public static int FirstRowHour(IReadOnlyList<Hour> hours)
    {
        if (hours.Count == 0) return 10;
        var first = hours.Min(h => h.StartHour);
        var last = hours.Max(h => h.StartHour);
        var start = Math.Min(10, first);
        if (last >= start + HourRows) start = Math.Min(first, last - HourRows + 1);
        return Math.Max(0, start);
    }

    public static FixedDocument Build(Data data)
    {
        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(PageWidthPt * Pt, PageHeightPt * Pt);
        doc.Pages.Add(Page("DailyBriefing-1.png", canvas => FillPageOne(canvas, data)));
        doc.Pages.Add(Page("DailyBriefing-2.png", _ => { }));   // page 2 is for the team to write on
        return doc;
    }

    private static PageContent Page(string background, Action<Canvas> fill)
    {
        var page = new FixedPage { Width = PageWidthPt * Pt, Height = PageHeightPt * Pt, Background = Brushes.White };
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Assets/" + background);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        page.Children.Add(new Image { Source = image, Width = page.Width, Height = page.Height, Stretch = Stretch.Fill });
        var canvas = new Canvas { Width = page.Width, Height = page.Height };
        fill(canvas);
        page.Children.Add(canvas);
        var content = new PageContent();
        ((System.Windows.Markup.IAddChild)content).AddChild(page);
        return content;
    }

    private static void FillPageOne(Canvas c, Data d)
    {
        var uk = CultureInfo.GetCultureInfo("en-GB");
        Box(c, 527.1, 30.8, 644.2, 56.7, d.Date.ToString("dddd", uk), 11);
        Box(c, 727.7, 30.8, 810.3, 56.7, d.Date.ToString("dd/MM/yyyy", uk), 11);

        TargetAndActual(c, 105.6, 194.0, d.Sales);
        TargetAndActual(c, 366.8, 455.1, d.Abv);
        TargetAndActual(c, 627.9, 716.2, d.Aub);

        // Hours: the times are written by the app (the form's own were removed from the
        // background) in the sheet's lettering - Chewy 16.32pt, CfBlue, baseline as printed.
        var firstHour = FirstRowHour(d.Hours);
        var best = d.Hours.Count == 0 ? null : d.Hours.MaxBy(h => h.Sales);
        for (var i = 0; i < HourRows; i++)
        {
            var hour = firstHour + i;
            if (hour > 23) break;
            PlaceText(c, 36.51, 206.77 + 31.84 * i, $"{hour:00}:00", Chewy, 16.32, SheetBlue);
            var row = d.Hours.FirstOrDefault(h => h.StartHour == hour);
            if (row is null) continue;
            var y = 187.2 + 31.86 * i;
            // Owner, 16 Oct 2026 (A1 + S2): every hour whose ABV or AUB beats the day's target
            // (above it, not equal) is ringed yellow; the best sales hour is filled navy with
            // white figures and a yellow edge, so it can't be mistaken for those rings.
            var isBest = ReferenceEquals(row, best) && row.Sales > 0m;
            Box(c, 83.6, y, 156.2, y + 27.5, Money(row.Sales), 10.5, ring: isBest,
                fill: isBest ? Navy : null, ink: isBest ? Brushes.White : null);
            Box(c, 162.9, y, 235.6, y + 27.5, Money(row.Abv), 10.5, ring: BeatsTarget(row.Abv, d.Abv));
            Box(c, 242.3, y, 314.9, y + 27.5, row.Aub.ToString("0.00", uk), 10.5, ring: BeatsTarget(row.Aub, d.Aub));
        }

        // Voids, Refunds, Staff discounts - only when Reports were generated for this date.
        if (d.Reports is { } r)
        {
            ReportLine(c, 352.8, r.VoidsQuantity.ToString(uk), r.VoidsValue, r.VoidsValue >= MainViewModel.ReportFlagValue);
            ReportLine(c, 369.2, r.RefundsQuantity.ToString(uk), r.RefundsValue, r.RefundsValue >= MainViewModel.ReportFlagValue);
            if (r.StaffValue is { } staff)
                ReportLine(c, 385.5, (r.StaffQuantity ?? 0m).ToString("0.##", uk), staff, false);
        }

        // Team Performance: left column first, then right; top people by sales.
        for (var i = 0; i < d.Team.Count && i < TeamSlots; i++)
        {
            var p = d.Team[i];
            var off = i < 4 ? 0.0 : 231.9;
            var y = new[] { 483.9, 505.0, 526.1, 547.2 }[i % 4];
            Box(c, 360.0 + off, y, 436.8 + off, y + 17.7, p.Name, 8, left: true);
            Box(c, 441.6 + off, y, 484.0 + off, y + 17.7, Money(p.Abv), 8);
            Box(c, 488.8 + off, y, 531.2 + off, y + 17.7, p.Aub.ToString("0.00", uk), 8);
            var (bg, fg, text) = p.ProfitLossState switch
            {
                "Good" => (Green, Brushes.White, p.ProfitLossText),
                "Bad" => (Red, Brushes.White, p.ProfitLossText),
                _ => (Tint, Navy, "—"),                     // no ABV target set, so no P&L
            };
            Box(c, 536.0 + off, y, 578.4 + off, y + 17.7, text, 8, fill: bg, ink: fg);
        }
        if (d.TeamNotShown > 0)
            PlaceText(c, 360.0, 572.0, $"+{d.TeamNotShown} more {(d.TeamNotShown == 1 ? "person" : "people")} not shown - top 8 by sales",
                Figures, 6.5, Muted);
    }

    /// <summary>An hour's figure above the day's target. No target set: never.</summary>
    public static bool BeatsTarget(decimal hourly, Measure measure) =>
        measure.Target is { } target && hourly > target;

    // Target box tinted; Actual box green or red with the difference inside (D1), or tinted
    // when there is no target to compare with.
    private static void TargetAndActual(Canvas c, double targetX, double actualX, Measure m)
    {
        var uk = CultureInfo.GetCultureInfo("en-GB");
        string F(decimal v) => m.IsMoney ? Money(v) : v.ToString("0.00", uk);
        if (m.Target is { } t) Box(c, targetX, 97, targetX + 80.7, 122, F(t), 11);
        if (m.Actual is not { } a) return;
        if (m.Target is not { } target)
        {
            Box(c, actualX, 97, actualX + 80.6, 122, F(a), 11);
            return;
        }
        var diff = a - target;
        var up = diff >= 0m;
        var sign = up ? "+" : "-";
        var difference = m.IsMoney ? $"({sign}£{Math.Abs(diff):0.00})" : $"({sign}{Math.Abs(diff):0.00})";
        Box(c, actualX, 97, actualX + 80.6, 122, F(a), 10.5, fill: up ? Green : Red, ink: Brushes.White, small: difference);
    }

    private static void ReportLine(Canvas c, double y, string quantity, decimal value, bool flag)
    {
        Box(c, 608.7, y, 647.1, y + 13.5, quantity, 8.4);
        Box(c, 654.8, y, 722.0, y + 13.5, Money(value), 8.4, fill: flag ? AmberFill : null, ink: flag ? AmberText : null);
    }

    private static string Money(decimal v) => v.ToString("£#,##0.00", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>A filled box over one of the sheet's rounded boxes (its outline stays visible),
    /// with the text centred (or left-aligned) and shrunk if it would not fit.</summary>
    private static void Box(Canvas c, double x0, double y0, double x1, double y1, string text, double sizePt,
        bool ring = false, bool left = false, Brush? fill = null, Brush? ink = null, string? small = null)
    {
        double w = (x1 - x0) * Pt, h = (y1 - y0) * Pt, inset = 1.6 * Pt, radius = h / 2.3;
        var shape = new Border
        {
            Width = Math.Max(0, w - 2 * inset),
            Height = Math.Max(0, h - 2 * inset),
            CornerRadius = new CornerRadius(Math.Max(1, radius - inset)),
            Background = fill ?? Tint,
        };
        Canvas.SetLeft(shape, x0 * Pt + inset);
        Canvas.SetTop(shape, y0 * Pt + inset);

        var size = sizePt * Pt;
        var smallSize = sizePt * 0.7 * Pt;
        var room = w - 9 * Pt;
        while (size > 5 && Width(text, size) + (small is null ? 0 : 4 * Pt + Width(small, smallSize)) > room)
        {
            size -= 0.5;
            smallSize = size * 0.7;
        }
        var block = new TextBlock
        {
            FontFamily = Figures,
            FontSize = size,
            Foreground = ink ?? Navy,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Margin = left ? new Thickness(6 * Pt - inset, 0, 0, 0) : new Thickness(0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        block.Inlines.Add(new Run(text));
        if (small is not null)
            block.Inlines.Add(new Run("  " + small) { FontSize = smallSize, BaselineAlignment = BaselineAlignment.Center });
        shape.Child = block;
        c.Children.Add(shape);

        if (ring)
        {
            var outline = new Border
            {
                Width = w + 2, Height = h + 2,
                CornerRadius = new CornerRadius(radius),
                BorderBrush = Yellow,
                BorderThickness = new Thickness(2.2 * Pt),
            };
            Canvas.SetLeft(outline, x0 * Pt - 1);
            Canvas.SetTop(outline, y0 * Pt - 1);
            c.Children.Add(outline);
        }
    }

    /// <summary>Text placed by its baseline, as the sheet's own printed text is.</summary>
    private static void PlaceText(Canvas c, double xPt, double baselinePt, string text, FontFamily family, double sizePt, Brush ink)
    {
        var size = sizePt * Pt;
        var block = new TextBlock { Text = text, FontFamily = family, FontSize = size, Foreground = ink };
        Canvas.SetLeft(block, xPt * Pt);
        Canvas.SetTop(block, baselinePt * Pt - family.Baseline * size);
        c.Children.Add(block);
    }

    private static double Width(string text, double size) =>
        new FormattedText(text, CultureInfo.GetCultureInfo("en-GB"), FlowDirection.LeftToRight,
            new Typeface(Figures, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
