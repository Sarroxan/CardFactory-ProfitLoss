using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using CardFactory.ProfitLoss.App.ViewModels;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// The Team Scorecard (owner, 21 Oct 2026): the day - or the week, when the calendar is
/// on a date range - on one A4 page in the app's look, opened as a PDF (ReportPdf).
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

    public static string Build(MainViewModel vm)
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
            var flag = MainViewModel.ReportFlagValue;
            string Tile(string label, string value, string detail, bool flagged = false, bool wide = false) =>
                $"<div class='{(flagged ? "flag" : string.Empty)}'{(wide ? " style='grid-column:span 2'" : string.Empty)}><small>{label}</small><b>{H(value)}</b><i>{H(detail)}</i></div>";
            var r = new StringBuilder("<div class='rt'>");
            r.Append(Tile("VOIDS", M(vm.ReportVoidsValue), $"{vm.ReportVoidsQuantity} items", vm.ReportVoidsValue >= flag));
            r.Append(Tile("RETURNS", M(vm.ReportRefundsValue), $"{vm.ReportRefundsQuantity} refunds", vm.ReportRefundsValue >= flag));
            r.Append(Tile("NO SALES", vm.ReportNoSales.ToString(Uk), "drawer opens"));
            r.Append(Tile("STAFF DISCOUNT", vm.ReportStaffDiscountText, vm.ReportStaffDiscountDetail));
            r.Append(Tile("STAMPS SOLD", vm.ReportStampsText, vm.ReportStampsDetail, wide: true));
            r.Append("</div>");
            var flagged = vm.ReportRows
                .Where(p => p.VoidsFlagged || p.RefundsFlagged)
                .Select(p => H(p.Name) + (p.VoidsFlagged ? $" {M(p.VoidsValue)} voids" : string.Empty) + (p.RefundsFlagged ? $" {M(p.RefundsValue)} returns" : string.Empty))
                .ToList();
            r.Append($"<div class='fl'>{M(flag)}+ highlighted{(flagged.Count > 0 ? " · " + string.Join(" · ", flagged) : " · none today")}</div>");
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
        ".foot{display:flex;justify-content:space-between;color:#8A99AD;font-size:7px;margin-top:6px}";

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
