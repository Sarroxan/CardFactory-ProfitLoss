using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using CardFactory.ProfitLoss.App.ViewModels;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// The Team Scorecard (owner, 21 Oct 2026): the day - or the week, when the calendar is
/// on a date range - on one A4 page in the app's look, opened in the same preview as the Daily Briefing (7 Nov 2026;
/// before that as a PDF in the browser, ReportPdf - no longer used).
/// Concept P1 for a day: the brand band; Sales / ABV / AUB / P&amp;L against target; Top
/// Performers; Team with red/green ABV, AUB and P&amp;L; Branch by hour beside Reports; Notes
/// filling the rest of the page. Concept W1 for a week: the same, with the hours replaced by
/// the seven days (Flooid's Branch Performance for the range). No Gift Cards section.
/// Every figure is read from the app as it stands; nothing is worked out differently here.
/// </summary>
internal static class TeamScorecard
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    public static string Title(MainViewModel vm) => vm.IsRangeMode ? "Weekly Team Scorecard" : "Team Scorecard";

    public static string FileStem(MainViewModel vm)
    {
        var end = (vm.SelectedDate ?? DateTime.Today).Date;
        var store = new string((vm.StoreLocation ?? string.Empty).Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        var name = vm.IsRangeMode ? "Weekly-Team-Scorecard" : "Team-Scorecard";
        return string.IsNullOrWhiteSpace(store) ? $"{name}-{end:yyyy-MM-dd}" : $"{name}-{store}-{end:yyyy-MM-dd}";
    }

    /// <param name="addOns">The add-ons tracked in the Add-on Sales tab (for the week's sheet).</param>
    public static string Build(MainViewModel vm, IReadOnlyList<AddOnItem>? addOns = null)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string M(decimal v) => v.ToString("£#,##0.00", Uk);
        static string Cls(string? state) => state switch { "Good" => "g", "Bad" => "r", _ => string.Empty };

        var smiley = ResourceDataUri("Assets/HeaderSmileyFace.png", "image/png");
        var chewy = ResourceDataUri("Assets/Fonts/Chewy-Regular.ttf", "font/ttf");
        var fredoka = ResourceDataUri("Assets/Fonts/Fredoka-SemiBold.ttf", "font/ttf");
        var smileyImg = smiley is null ? string.Empty : $"<img src='{smiley}' alt=''>";

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>").Append(H(Title(vm))).Append("</title><style>");
        if (chewy is not null) sb.Append($"@font-face{{font-family:CfBrand;src:url({chewy})}}");
        if (fredoka is not null) sb.Append($"@font-face{{font-family:CfSmall;src:url({fredoka})}}");
        sb.Append(Css);
        sb.Append("</style></head><body><div class='sheet'>");

        // Brand band: the app's logo lettering, store, what this is and the day or week.
        sb.Append("<div class='brand'>").Append(smileyImg)
          .Append("<div><div class='co'>Card Factory</div><div class='pl'>Profit &amp; Loss</div><div class='calc'><i></i>CALCULATOR<i></i></div></div>")
          .Append($"<div class='where'>{H(vm.StoreLocation)}<br><span class='kind'>{H(Title(vm))}</span><br><b>{H(vm.PeriodDisplayText)}</b></div></div><div class='rule'></div>");

        // A week (owner, 16 Nov 2026, concept X2): its own layout.
        if (vm.IsRangeMode)
        {
            AppendWeek(sb, vm, addOns, smileyImg);
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        // The day against its targets.
        string Target(decimal t, bool money) => t > 0m ? "target " + (money ? M(t) : t.ToString("0.00", Uk)) : "no target";
        string Arrow(string text, string state) => state is "Good" or "Bad" ? $"<span class='{(state == "Good" ? "up" : "dn")}'>{H(text)}</span>" : string.Empty;
        sb.Append("<div class='kpis'>");
        sb.Append($"<div class='k'><div class='h'><span>SALES</span><span>{Target(vm.SalesTarget, true)}</span></div><div class='v'><b>{M(vm.TotalSales)}</b>{Arrow(vm.SalesSummaryText, vm.SalesVarianceState)}</div></div>");
        sb.Append($"<div class='k'><div class='h'><span>ABV</span><span>{Target(vm.AbvTarget, true)}</span></div><div class='v'><b>{M(vm.ActualAbv)}</b>{Arrow(vm.AbvSummaryText, vm.AbvSummaryState)}</div></div>");
        sb.Append($"<div class='k'><div class='h'><span>AUB</span><span>{Target(vm.AubTarget, false)}</span></div><div class='v'><b>{vm.ActualAub.ToString("0.00", Uk)}</b>{Arrow(vm.AubSummaryText, vm.AubSummaryState)}</div></div>");
        var plClass = vm.TotalProfitLossState == "Good" ? "up" : vm.TotalProfitLossState == "Bad" ? "dn" : string.Empty;
        sb.Append($"<div class='k'><div class='h'><span>P&amp;L</span><span>{vm.TotalTransactions.ToString("N0", Uk)} trans</span></div><div class='v'><b class='{plClass}'>{H(vm.TotalProfitLossState == "None" ? "—" : vm.TotalProfitLossText)}</b></div></div>");
        sb.Append("</div>");

        string Section(string title, string body) =>
            $"<div class='sec'><div class='t'>{smileyImg}{H(title)}</div>{body}</div>";
        var week = vm.IsRangeMode ? " · week" : string.Empty;

        // Top Performers, as the strip in the app shows them.
        var chips = vm.TopPerformerChips;
        var top = chips.Count == 0
            ? "<div class='empty'>Not enough transactions yet to name a top performer.</div>"
            : "<div class='tp'>" + string.Concat(chips.Select(c =>
                $"<div><small>★ {H(c.Label)}</small><b>{H(c.Names)}</b><span class='{(c.State == "Good" ? "up" : c.State == "Bad" ? "dn" : string.Empty)}'>{H(c.Value)}</span></div>")) + "</div>";
        sb.Append(Section("Top Performers" + week, top));

        // Team.
        var people = vm.Operators
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && (o.CountedSales != 0m || o.ToInput().Transactions > 0))
            .ToList();
        var team = new StringBuilder("<table><tr><th>NAME</th><th>TOTAL SALES</th><th>GIFT CARDS</th><th>TRANS</th><th>UNITS</th><th>COUNTED SALES</th><th>ABV</th><th>AUB</th><th>P&amp;L</th></tr>");
        foreach (var o in people)
        {
            var input = o.ToInput();
            var gift = input.GiftCardQuantity > 0 || input.GiftCardValue > 0m ? $"{input.GiftCardQuantity} · {M(input.GiftCardValue)}" : string.Empty;
            team.Append($"<tr><td>{H(o.Name.Trim())}</td><td>{M(input.Sales)}</td><td>{gift}</td><td>{input.Transactions}</td><td>{input.Units}</td><td>{M(o.CountedSales)}</td>")
                .Append($"<td class='{Cls(o.AbvState)}'>{M(o.Abv)}</td><td class='{Cls(o.AubState)}'>{o.Aub.ToString("0.00", Uk)}</td>")
                .Append($"<td class='{Cls(o.ProfitLossState)}'>{H(o.ProfitLossState == "None" ? "—" : o.ProfitLossText)}</td></tr>");
        }
        if (people.Count == 0)
        {
            team.Append("<tr><td colspan='9' class='none'>No team figures yet - press Refresh in Team Performance.</td></tr>");
        }
        else
        {
            var inputs = people.Select(o => o.ToInput()).ToList();
            var giftTotal = vm.TotalGiftCardQuantity > 0 || vm.TotalGiftCards > 0m ? $"{vm.TotalGiftCardQuantity} · {M(vm.TotalGiftCards)}" : string.Empty;
            team.Append($"<tr class='tot'><td>Totals</td><td>{M(inputs.Sum(i => i.Sales))}</td><td>{giftTotal}</td><td>{vm.TotalTransactions.ToString("N0", Uk)}</td><td>{inputs.Sum(i => i.Units).ToString("N0", Uk)}</td><td>{M(vm.TotalSales)}</td>")
                .Append($"<td class='{Cls(vm.AbvSummaryState)}'>{M(vm.ActualAbv)}</td><td class='{Cls(vm.AubSummaryState)}'>{vm.ActualAub.ToString("0.00", Uk)}</td>")
                .Append($"<td class='{Cls(vm.TotalProfitLossState)}'>{H(vm.TotalProfitLossState == "None" ? "—" : vm.TotalProfitLossText)}</td></tr>");
        }
        team.Append("</table>");
        sb.Append(Section("Team Performance" + week, team.ToString()));

        // A day: the hours. A week: the days (W1).
        string Bar(decimal share, decimal biggest) =>
            $"<span class='bar' style='width:{(biggest > 0m ? Math.Round(share / biggest * 46m, 1) : 0m).ToString(CultureInfo.InvariantCulture)}px'></span>{share.ToString("0", Uk)}%";
        string left;
        if (vm.IsRangeMode)
        {
            var days = vm.BranchDays;
            if (days.Count == 0)
            {
                left = Section("Day by day", "<div class='empty'>Not retrieved for this week - press Refresh, then open the sheet again.</div>");
            }
            else
            {
                var weekSales = days.Sum(d => d.Sales);
                var shares = days.Select(d => weekSales > 0m ? d.Sales / weekSales * 100m : 0m).ToList();
                var best = days.MaxBy(d => d.Sales);
                var t = new StringBuilder("<table><tr><th>DAY</th><th>SALES</th><th>TRANS</th><th>ABV</th><th>% OF WEEK</th></tr>");
                for (var i = 0; i < days.Count; i++)
                {
                    var d = days[i];
                    var isBest = ReferenceEquals(d, best) && d.Sales > 0m;
                    t.Append($"<tr class='{(isBest ? "best" : string.Empty)}'><td>{(isBest ? "★ " : string.Empty)}{d.Date.ToString("ddd dd/MM", Uk)}</td><td>{M(d.Sales)}</td><td>{d.Transactions}</td><td>{M(d.Abv)}</td><td class='l'>{Bar(shares[i], shares.Max())}</td></tr>");
                }
                left = Section("Day by day", t.Append("</table>").ToString());
            }
        }
        else
        {
            var hours = vm.BranchHours.Where(h => h.Sales > 0m || h.Transactions > 0).ToList();
            if (hours.Count == 0)
            {
                left = Section("Branch · by hour", "<div class='empty'>Not retrieved for this day - press Refresh, then open the sheet again.</div>");
            }
            else
            {
                var biggest = hours.Max(h => h.PercentOfSales);
                var t = new StringBuilder("<table><tr><th>TIME</th><th>SALES</th><th>TRANS</th><th>% SALES</th></tr>");
                foreach (var h in hours)
                {
                    var time = DailyBriefingSheet.StartHour(h.TimeBand) is { } hour ? $"{hour:00}:00" : h.TimeBand;
                    t.Append($"<tr class='{(h.IsStrongestHour ? "best" : string.Empty)}'><td>{(h.IsStrongestHour ? "★ " : string.Empty)}{H(time)}</td><td>{M(h.Sales)}</td><td>{h.Transactions}</td><td class='l'>{Bar(h.PercentOfSales, biggest)}</td></tr>");
                }
                left = Section("Branch · by hour", t.Append("</table>").ToString());
            }
        }

        // Reports.
        string right;
        if (!vm.HasReportData)
        {
            right = Section("Reports" + week, "<div class='empty'>Not generated for this " + (vm.IsRangeMode ? "week" : "day") + " - press Generate reports in Reports, then open the sheet again.</div>");
        }
        else
        {
            // Stamps sold tile removed (owner, 16 Nov 2026): stamps are an add-on now.
            string Tile(string label, string value, string detail, bool flagged = false) =>
                $"<div class='{(flagged ? "flag" : string.Empty)}'><small>{label}</small><b>{H(value)}</b><i>{H(detail)}</i></div>";
            var r = new StringBuilder("<div class='rt'>");
            r.Append(Tile("VOIDS", M(vm.ReportVoidsValue), $"{vm.ReportVoidsQuantity} items", vm.ReportVoidsValue >= MainViewModel.VoidsFlagValue));
            r.Append(Tile("RETURNS", M(vm.ReportRefundsValue), $"{vm.ReportRefundsQuantity} refunds", vm.ReportRefundsValue >= MainViewModel.RefundsFlagValue));
            r.Append(Tile("NO SALES", vm.ReportNoSales.ToString(Uk), "drawer opens"));
            r.Append(Tile("STAFF DISCOUNT", vm.ReportStaffDiscountText, vm.ReportStaffDiscountDetail));
            r.Append("</div>");
            var flagged = vm.ReportRows
                .Where(p => p.VoidsFlagged || p.RefundsFlagged)
                .Select(p => H(p.Name) + (p.VoidsFlagged ? $" {M(p.VoidsValue)} voids" : string.Empty) + (p.RefundsFlagged ? $" {M(p.RefundsValue)} returns" : string.Empty))
                .ToList();
            r.Append($"<div class='fl'>Voids {M(MainViewModel.VoidsFlagValue)}+ · Refunds {M(MainViewModel.RefundsFlagValue)}+ highlighted{(flagged.Count > 0 ? " · " + string.Join(" · ", flagged) : " · none today")}</div>");
            right = Section("Reports" + week, r.ToString());
        }
        sb.Append("<div class='two'>").Append(left).Append(right).Append("</div>");

        // Notes: the rest of the page, lined, for writing on.
        sb.Append("<div class='notes'><div class='t'>Notes</div></div>");
        sb.Append("<div class='foot'><span>Gift cards are taken out of sales, ABV and AUB · P&amp;L = counted sales − (ABV target × transactions) · ")
          .Append(vm.IsRangeMode ? "Day by day" : "Branch")
          .Append(" shows Flooid's own figures</span><span>Card Factory Profit &amp; Loss</span></div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    // The Weekly Team Scorecard (owner, 16 Nov 2026, concept X2): the week's boxes with a bar to
    // target; a podium for the top three on P&L; everyone ranked on P&L (with their add-ons);
    // the seven days as tiles, each against an even share of the week's sales target; Reports
    // and Add-on Sales side by side; Notes. Every figure is read from the app as it stands -
    // the same team, branch, reports and add-on figures the screens show; nothing is worked out
    // differently, the bars and percentages only compare them with the targets.
    private static void AppendWeek(StringBuilder sb, MainViewModel vm, IReadOnlyList<AddOnItem>? tracked, string smileyImg)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string M(decimal v) => v.ToString("£#,##0.00", Uk);
        static string Cls(string? state) => state switch { "Good" => "g", "Bad" => "r", _ => string.Empty };
        string Section(string title, string body) => $"<div class='sec'><div class='t'>{smileyImg}{H(title)}</div>{body}</div>";
        string Pct(decimal actual, decimal target) => target > 0m ? Math.Round(actual / target * 100m).ToString("0", Uk) : "0";
        static string Width(decimal actual, decimal target) =>
            target > 0m ? Math.Min(100m, Math.Max(0m, Math.Round(actual / target * 100m, 1))).ToString(CultureInfo.InvariantCulture) : "0";

        // The week's boxes: actual, a bar to target and "n% of target".
        string Box(string label, string value, decimal actual, decimal target, string targetText, bool higherIsGood = true)
        {
            var good = target > 0m && (higherIsGood ? actual >= target : actual <= target);
            var colour = target <= 0m ? "#004EA1" : good ? "#1D8F4E" : "#C53D46";
            var left = target > 0m ? $"{Pct(actual, target)}% of target" : "no target";
            return $"<div class='c'><small>{label}</small><b style='color:{colour}'>{H(value)}</b><div class='pb'><i style='width:{Width(actual, target)}%;background:{colour}'></i></div><em><span>{left}</span><span>{H(targetText)}</span></em></div>";
        }
        var plColour = vm.TotalProfitLossState == "Good" ? "#1D8F4E" : vm.TotalProfitLossState == "Bad" ? "#C53D46" : "#004EA1";
        sb.Append("<div class='k2'>")
          .Append(Box("WEEK SALES", M(vm.TotalSales), vm.TotalSales, vm.SalesTarget, vm.SalesTarget > 0m ? M(vm.SalesTarget) : string.Empty))
          .Append(Box("ABV", M(vm.ActualAbv), vm.ActualAbv, vm.AbvTarget, vm.AbvTarget > 0m ? M(vm.AbvTarget) : string.Empty))
          .Append(Box("AUB", vm.ActualAub.ToString("0.00", Uk), vm.ActualAub, vm.AubTarget, vm.AubTarget > 0m ? vm.AubTarget.ToString("0.00", Uk) : string.Empty))
          .Append($"<div class='c'><small>P&amp;L</small><b style='color:{plColour}'>{H(vm.TotalProfitLossState == "None" ? "—" : vm.TotalProfitLossText)}</b><div class='pb'></div><em><span>{vm.TotalTransactions.ToString("N0", Uk)} transactions</span><span></span></em></div>")
          .Append("</div>");

        // Add-ons for the week, by person (matched on the name, as the reports give it).
        var summary = tracked is null ? null : vm.AddOnSummary(tracked);
        int? AddOnsFor(string name) => summary is null
            ? null
            : summary.People.Where(p => string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).Sum(p => p.Total);

        // Everyone, ranked on P&L (on counted sales when there is no ABV target).
        var withPl = vm.TotalProfitLossState != "None";
        var people = vm.Operators
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && (o.CountedSales != 0m || o.ToInput().Transactions > 0))
            .OrderByDescending(o => withPl ? o.ProfitLoss : o.CountedSales)
            .ThenByDescending(o => o.CountedSales)
            .ToList();
        string Score(OperatorRowViewModel o) => withPl ? (o.ProfitLossState == "None" ? "—" : o.ProfitLossText) : M(o.CountedSales);

        if (people.Count > 0)
        {
            string Step(OperatorRowViewModel o, string place, int height, string background)
            {
                var add = AddOnsFor(o.Name) is { } n ? $" · {n} add-ons" : string.Empty;
                var cls = !withPl ? string.Empty : o.ProfitLoss >= 0m ? "up" : "dn";
                return $"<div style='height:{height}px;background:{background}'><em>{place}</em><b>{H(o.Name.Trim())}</b><span class='{cls}' style='font-size:11px'>{H(Score(o))}</span>"
                     + $"<small>ABV {M(o.Abv)} · AUB {o.Aub.ToString("0.00", Uk)}{add}</small></div>";
            }
            var podium = new StringBuilder("<div class='pod'>");
            podium.Append(people.Count > 1 ? Step(people[1], "2nd", 92, "#E9EEF4") : "<div></div>");
            podium.Append(Step(people[0], "1st", 108, "#FFF1B8"));
            podium.Append(people.Count > 2 ? Step(people[2], "3rd", 82, "#F6E3CF") : "<div></div>");
            sb.Append(Section("Podium of the week · " + (withPl ? "P&L" : "counted sales"), podium.Append("</div>").ToString()));
        }

        var showAdd = summary is not null;
        var cols = "22px 1.4fr 1fr 0.7fr 0.8fr 0.8fr 1fr" + (showAdd ? " 0.8fr" : string.Empty);
        var rank = new StringBuilder("<div class='lb'>");
        rank.Append($"<div class='p h' style='grid-template-columns:{cols}'><span></span><span>NAME</span><span>COUNTED SALES</span><span>TRANS</span><span>ABV</span><span>AUB</span><span>P&amp;L</span>{(showAdd ? "<span>ADD-ONS</span>" : string.Empty)}</div>");
        if (people.Count == 0)
        {
            rank.Append("<div class='empty'>No team figures yet - press Refresh in Team Performance.</div>");
        }
        else
        {
            for (var i = 0; i < people.Count; i++)
            {
                var o = people[i];
                var medal = i switch { 0 => "background:#FFC907", 1 => "background:#C9D3DF", 2 => "background:#E3B07A", _ => string.Empty };
                rank.Append($"<div class='p' style='grid-template-columns:{cols}'><span class='rk' style='{medal}'>{i + 1}</span><span class='n'>{H(o.Name.Trim())}</span>")
                    .Append($"<span>{M(o.CountedSales)}</span><span>{o.ToInput().Transactions}</span><span class='{Cls(o.AbvState)}'>{M(o.Abv)}</span>")
                    .Append($"<span class='{Cls(o.AubState)}'>{o.Aub.ToString("0.00", Uk)}</span><span class='{Cls(o.ProfitLossState)}'>{H(o.ProfitLossState == "None" ? "—" : o.ProfitLossText)}</span>")
                    .Append(showAdd ? $"<span>{AddOnsFor(o.Name) ?? 0}</span>" : string.Empty).Append("</div>");
            }
            rank.Append($"<div class='p tm' style='grid-template-columns:{cols}'><span></span><span class='n'>Team</span><span>{M(vm.TotalSales)}</span><span>{vm.TotalTransactions.ToString("N0", Uk)}</span>")
                .Append($"<span class='{Cls(vm.AbvSummaryState)}'>{M(vm.ActualAbv)}</span><span class='{Cls(vm.AubSummaryState)}'>{vm.ActualAub.ToString("0.00", Uk)}</span>")
                .Append($"<span class='{Cls(vm.TotalProfitLossState)}'>{H(vm.TotalProfitLossState == "None" ? "—" : vm.TotalProfitLossText)}</span>")
                .Append(showAdd ? $"<span>{summary!.TotalQuantity}</span>" : string.Empty).Append("</div>");
        }
        sb.Append(Section("Everyone · the week, ranked by " + (withPl ? "P&L" : "counted sales"), rank.Append("</div>").ToString()));

        // The seven days, each against an even share of the week's sales target.
        var days = vm.BranchDays;
        if (days.Count == 0)
        {
            sb.Append(Section("Day by day", "<div class='empty'>Not retrieved for this week - press Refresh, then open the sheet again.</div>"));
        }
        else
        {
            var share = vm.SalesTarget > 0m ? vm.SalesTarget / days.Count : 0m;
            var best = days.MaxBy(d => d.Sales);
            var tiles = new StringBuilder($"<div class='dt' style='grid-template-columns:repeat({days.Count},1fr)'>");
            foreach (var d in days)
            {
                var isBest = ReferenceEquals(d, best) && d.Sales > 0m;
                var on = share > 0m && d.Sales >= share;
                var colour = share <= 0m ? "#A5C0DD" : on ? "#1D8F4E" : "#C53D46";
                var abvColour = vm.AbvTarget <= 0m ? "#004EA1" : d.Abv >= vm.AbvTarget ? "#1D8F4E" : "#C53D46";
                tiles.Append($"<div class='{(isBest ? "best" : string.Empty)}'><small>{(isBest ? "★ " : string.Empty)}{d.Date.ToString("ddd", Uk)}</small><span>{d.Date.ToString("dd/MM", Uk)}</span><b>{M(d.Sales)}</b>")
                     .Append(share > 0m ? $"<em class='{(on ? "g" : "r")}'>{Pct(d.Sales, share)}% of target</em>" : "<em>no target</em>")
                     .Append($"<div class='pb'><i style='width:{Width(d.Sales, share)}%;background:{colour}'></i></div>")
                     .Append($"<span>{d.Transactions} trans · ABV <b class='a' style='color:{abvColour}'>{d.Abv.ToString("0.00", Uk)}</b></span></div>");
            }
            sb.Append(Section("Day by day · against an even share of the week's target", tiles.Append("</div>").ToString()));
        }

        // Reports and Add-on Sales, side by side.
        string reports;
        if (!vm.HasReportData)
        {
            reports = Section("Reports · week", "<div class='empty'>Not generated for this week - press Generate reports in Reports, then open the sheet again.</div>");
        }
        else
        {
            string Tile(string label, string value, string detail, bool flagged = false) =>
                $"<div class='{(flagged ? "flag" : string.Empty)}'><small>{label}</small><b>{H(value)}</b><i>{H(detail)}</i></div>";
            var flagged = vm.ReportRows
                .Where(p => p.VoidsFlagged || p.RefundsFlagged)
                .Select(p => H(p.Name) + (p.VoidsFlagged ? $" {M(p.VoidsValue)} voids" : string.Empty) + (p.RefundsFlagged ? $" {M(p.RefundsValue)} returns" : string.Empty))
                .ToList();
            reports = Section("Reports · week", "<div class='strip'>"
                + Tile("VOIDS", M(vm.ReportVoidsValue), $"{vm.ReportVoidsQuantity} items", vm.ReportVoidsValue >= MainViewModel.VoidsFlagValue)
                + Tile("RETURNS", M(vm.ReportRefundsValue), $"{vm.ReportRefundsQuantity} refunds", vm.ReportRefundsValue >= MainViewModel.RefundsFlagValue)
                + Tile("NO SALES", vm.ReportNoSales.ToString(Uk), "drawer opens")
                + Tile("STAFF DISCOUNT", vm.ReportStaffDiscountText, vm.ReportStaffDiscountDetail)
                + "</div>"
                + $"<div class='fl'>Voids {M(MainViewModel.VoidsFlagValue)}+ · Refunds {M(MainViewModel.RefundsFlagValue)}+ highlighted{(flagged.Count > 0 ? " · " + string.Join(" · ", flagged) : " · none")}</div>");
        }

        string addOnsSection;
        if (tracked is null || tracked.Count == 0)
        {
            addOnsSection = Section("Add-on Sales · week", "<div class='empty'>No add-ons tracked - add them in the Add-on Sales tab.</div>");
        }
        else if (summary is null)
        {
            addOnsSection = Section("Add-on Sales · week", "<div class='empty'>Not generated for this week - press Generate reports, then open the sheet again.</div>");
        }
        else
        {
            var top = summary.Top(4);
            var tiles = string.Concat(top.Columns.Select(c =>
                $"<div class='ad'><small>{H(c.Item.Name)}</small><b>{c.Quantity}</b><i>{M(c.Value)}{(c.TopSeller is null ? string.Empty : $" · ★ {H(c.TopSeller)} · {c.TopQuantity}")}</i></div>"));
            addOnsSection = Section($"Add-on Sales · {summary.TotalQuantity.ToString("N0", Uk)} · {M(summary.TotalValue)}", $"<div class='strip'>{tiles}</div>");
        }
        sb.Append("<div class='duo'>").Append(reports).Append(addOnsSection).Append("</div>");

        sb.Append("<div class='notes'><div class='t'>Notes</div></div>");
        sb.Append("<div class='foot'><span>Gift cards are taken out of sales, ABV and AUB · P&amp;L = counted sales − (ABV target × transactions) · Day by day shows Flooid's own figures</span><span>Card Factory Profit &amp; Loss</span></div>");
    }

    private const string Css =
        "@page{size:A4;margin:9mm}" +
        "*{box-sizing:border-box;-webkit-print-color-adjust:exact;print-color-adjust:exact}" +
        "html,body{margin:0}body{font-family:Arial,sans-serif;color:#183153;font-size:10px}" +
        ".sheet{min-height:278mm;display:flex;flex-direction:column}" +
        ".brand{background:#004EA1;border-radius:12px 12px 0 0;padding:10px 15px;display:flex;align-items:center;gap:11px}" +
        ".brand img{width:42px;height:42px}" +
        ".co{font-family:CfSmall,Arial,sans-serif;color:#fff;font-size:10px;line-height:1}" +
        ".pl{font-family:CfBrand,'Trebuchet MS',Arial,sans-serif;color:#fff;font-size:23px;line-height:1.05}" +
        ".calc{display:flex;align-items:center;gap:4px;font-family:CfSmall,Arial,sans-serif;color:#fff;font-size:7px;letter-spacing:3px}.calc i{width:16px;height:3px;background:#FFD100;border-radius:2px;display:block}" +
        ".where{margin-left:auto;text-align:right;color:#D6E4F5;font-size:10px;line-height:1.35}.where b{color:#fff;font-size:12px}.where .kind{font-family:CfBrand,Arial,sans-serif;color:#FFC907;font-size:13px}" +
        ".rule{height:4px;background:#FFC907;border-radius:0 0 4px 4px}" +
        ".kpis{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin:10px 0 2px}" +
        ".k{border:1px solid #A5C0DD;border-radius:10px;overflow:hidden}.k .h{background:#E3ECF7;color:#005BBB;font-weight:bold;font-size:7.5px;padding:3px 8px;display:flex;justify-content:space-between}" +
        ".k .v{display:flex;align-items:baseline;gap:6px;padding:4px 8px 6px}.k .v b{font-size:15px;color:#004EA1}" +
        ".up{color:#1D8F4E!important;font-weight:bold;font-size:9px}.dn{color:#C53D46!important;font-weight:bold;font-size:9px}.k .v b.up,.k .v b.dn{font-size:15px}" +
        ".sec{border:1px solid #A5C0DD;border-radius:10px;overflow:hidden;margin-top:8px;break-inside:avoid}" +
        ".sec .t{position:relative;background:#005BBB;border-bottom:3px solid #FFC907;color:#fff;font-family:CfBrand,'Trebuchet MS',Arial,sans-serif;font-size:14px;text-align:center;padding:3px 0}" +
        ".sec .t img{position:absolute;left:7px;top:2px;width:19px;height:19px}" +
        "table{width:100%;border-collapse:collapse}th{background:#E3ECF7;color:#004EA1;font-size:7.5px;padding:4px 3px;text-align:center}" +
        "td{text-align:center;padding:3.5px 3px;border-bottom:1px solid #EDF1F6;color:#004EA1;font-weight:bold;font-size:9px}" +
        "td:first-child,th:first-child{text-align:left;padding-left:8px}td.l{text-align:left}" +
        "td.g{background:#E7F5EC;color:#1D8F4E}td.r{background:#FBE8EA;color:#C53D46}" +
        "tr.tot td{border-top:2px solid #004EA1;background:#F5F8FC}tr.tot td.g{background:#E7F5EC}tr.tot td.r{background:#FBE8EA}tr.best td{background:#FFF4C2}td.none{text-align:center;color:#667891;font-weight:normal}" +
        ".bar{display:inline-block;height:6px;background:#FFC907;border-radius:3px;vertical-align:middle;margin-right:4px}" +
        ".tp{display:grid;grid-template-columns:repeat(3,1fr);gap:7px;padding:7px}" +
        ".tp div{border:1.5px solid #FFC907;border-radius:9px;background:#FFFBEA;padding:3px 8px}.tp small{display:block;color:#9A5B00;font-weight:bold;font-size:7px}.tp b{font-size:10px;color:#004EA1}.tp span{float:right;font-weight:bold;color:#004EA1;font-size:10px}" +
        ".two{display:grid;grid-template-columns:1.1fr 1fr;gap:8px}.two .sec{margin-top:8px}" +
        ".rt{display:grid;grid-template-columns:1fr 1fr;gap:6px;padding:7px}.rt div{border:1px solid #DCE5F0;border-radius:8px;padding:4px 8px}" +
        ".rt small{display:block;color:#005BBB;font-weight:bold;font-size:7px}.rt b{font-size:13px;color:#004EA1}.rt i{font-style:normal;color:#667891;font-size:8px;margin-left:4px}" +
        ".rt div.flag{background:#FFF1DC;border-color:#F0DE94}.rt div.flag b{color:#9A5B00}" +
        ".fl{padding:0 9px 6px;color:#667891;font-size:7.5px}.empty{padding:10px;color:#667891;text-align:center}" +
        ".notes{flex:1;min-height:32mm;margin-top:8px;border:1.5px dashed #A5C0DD;border-radius:10px;padding:4px 11px;break-inside:avoid;" +
        "background:repeating-linear-gradient(to bottom,transparent 0,transparent 21px,#E3ECF7 21px,#E3ECF7 22px);background-position:0 26px}" +
        ".notes .t{font-family:CfBrand,'Trebuchet MS',Arial,sans-serif;color:#004EA1;font-size:14px}" +
        ".foot{display:flex;justify-content:space-between;color:#8A99AD;font-size:7px;margin-top:6px}" +
        // The week (X2): boxes with a bar to target, podium, ranking, day tiles, side-by-side strips.
        ".k2{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin:10px 0 2px}" +
        ".k2 div.c{border-radius:12px;padding:7px 10px;background:#F4F7FB;border:1px solid #A5C0DD}" +
        ".k2 small{display:block;color:#005BBB;font-weight:bold;font-size:7.5px;letter-spacing:.5px}.k2 b{font-size:19px;display:block;margin:2px 0}" +
        ".k2 .pb{height:6px;border-radius:3px;background:#E3ECF7;overflow:hidden}.k2 .pb i{display:block;height:100%;border-radius:3px}" +
        ".k2 em{font-style:normal;font-size:8px;color:#667891;display:flex;justify-content:space-between;margin-top:3px}" +
        ".pod{display:grid;grid-template-columns:1fr 1.15fr 1fr;gap:8px;align-items:end;padding:8px 10px 6px}" +
        ".pod div{border-radius:10px 10px 0 0;text-align:center;padding:6px 4px;color:#004EA1;display:flex;flex-direction:column;justify-content:flex-end}" +
        ".pod b{display:block;font-family:CfBrand,Arial,sans-serif;font-size:16px}.pod span{display:block;font-weight:bold}" +
        ".pod small{display:block;font-size:7.5px;color:#4F6683}.pod em{display:block;font-style:normal;font-family:CfBrand,Arial,sans-serif;font-size:22px;line-height:1}" +
        ".lb{padding:6px;display:grid;gap:5px}" +
        ".lb .p{display:grid;align-items:center;border:1px solid #DCE5F0;border-radius:9px;padding:4px 8px;font-size:9px;color:#004EA1;font-weight:bold;text-align:center}" +
        ".lb .p span:nth-child(2){text-align:left}.lb .p.h{border:0;padding:0 8px;color:#005BBB;font-size:7px}.lb .p.tm{background:#F5F8FC;border-color:#004EA1}" +
        ".lb .n{font-size:11px}.lb .rk{width:18px;height:18px;border-radius:50%;background:#E3ECF7;display:flex;align-items:center;justify-content:center;font-size:9px}" +
        ".lb .g{color:#1D8F4E}.lb .r{color:#C53D46}" +
        ".dt{display:grid;gap:5px;padding:7px}.dt div{border:1px solid #DCE5F0;border-radius:9px;padding:5px 6px;text-align:center;color:#004EA1}" +
        ".dt small{display:block;font-family:CfBrand,Arial,sans-serif;font-size:12px}.dt span{display:block;font-size:7px;color:#667891}" +
        ".dt b{display:block;font-size:12px;margin:2px 0}.dt b.a{display:inline;font-size:7.5px}.dt em{display:block;font-style:normal;font-size:7.5px;font-weight:bold;color:#667891}" +
        ".dt em.g{color:#1D8F4E}.dt em.r{color:#C53D46}.dt div.best{background:#FFF4C2;border-color:#FFC907}" +
        ".dt .pb{height:5px;border-radius:3px;background:#E3ECF7;overflow:hidden;margin-top:3px;padding:0;border:0}.dt .pb i{display:block;height:100%}" +
        ".duo{display:grid;grid-template-columns:1fr 1fr;gap:8px}.duo .sec{margin-top:8px}" +
        ".strip{display:grid;grid-template-columns:1fr 1fr;gap:6px;padding:7px}.strip div{border:1px solid #DCE5F0;border-radius:8px;padding:4px 8px}" +
        ".strip small{display:block;color:#005BBB;font-weight:bold;font-size:7px}.strip b{font-size:13px;color:#004EA1}.strip i{font-style:normal;color:#667891;font-size:8px;margin-left:4px}" +
        ".strip div.flag{background:#FFF1DC;border-color:#F0DE94}.strip div.flag b{color:#9A5B00}" +
        ".strip div.ad small{font-family:CfBrand,Arial,sans-serif;font-size:10px;font-weight:normal;color:#004EA1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}";

    private static string? ResourceDataUri(string path, string mime)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/" + path));
            if (info is null) return null;
            using var stream = info.Stream;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return $"data:{mime};base64,{Convert.ToBase64String(copy.ToArray())}";
        }
        catch
        {
            return null;   // the sheet still opens, just without the picture or font
        }
    }
}
