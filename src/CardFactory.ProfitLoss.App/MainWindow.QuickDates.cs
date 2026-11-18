using System.Windows;
using CardFactory.ProfitLoss.App.ViewModels;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// Quick date buttons beside the date card (owner, 18 Nov 2026, concept Q1): Today, Yesterday
/// and This week (Monday to today, as the calendar's own This week button). Each sets the date
/// exactly as choosing it in the calendar would; the button for the date on show is highlighted.
/// </summary>
public partial class MainWindow
{
    private void QuickToday_Click(object sender, RoutedEventArgs e) => SetSingleDay(DateTime.Today);

    private void QuickYesterday_Click(object sender, RoutedEventArgs e) => SetSingleDay(DateTime.Today.AddDays(-1));

    private void QuickThisWeek_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        if (monday == today) { SetSingleDay(today); return; }   // Monday: this week is just today
        vm.HistoricalTargetMode = "Date Range";
        vm.RangeStartDate = monday;
        vm.SelectedDate = today;
        UpdateQuickDateButtons();
    }

    private void SetSingleDay(DateTime day)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.HistoricalTargetMode = "Daily";
        vm.SelectedDate = day;
        UpdateQuickDateButtons();
    }

    private void QuickDates_ViewModelChanged(string? property)
    {
        if (property is nameof(MainViewModel.SelectedDate) or nameof(MainViewModel.HistoricalTargetMode)
            or nameof(MainViewModel.RangeStartDate))
            UpdateQuickDateButtons();
    }

    private void UpdateQuickDateButtons()
    {
        if (QuickTodayButton is null || DataContext is not MainViewModel vm) return;
        var today = DateTime.Today;
        var end = (vm.SelectedDate ?? today).Date;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var single = !vm.IsRangeMode;
        QuickTodayButton.Tag = single && end == today ? "Active" : null;
        QuickYesterdayButton.Tag = single && end == today.AddDays(-1) ? "Active" : null;
        QuickThisWeekButton.Tag = !single && end == today && (vm.RangeStartDate ?? end).Date == monday ? "Active" : null;
    }
}
