using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// The report date picker. K1: "Single day" or "Date range" is chosen here, in the
/// calendar, rather than with a separate switch on the main screen. A single day is
/// used as soon as it is clicked; a range is the first day clicked, then the last, then
/// "Use dates".
/// </summary>
public partial class ThemedDatePickerWindow : Window
{
    private DateTime _displayMonth;
    private DateTime _selectedDate;
    private bool _rangeMode;
    private DateTime? _rangeStart;
    private DateTime? _rangeEnd;

    public ThemedDatePickerWindow(DateTime selectedDate, DateTime? rangeStart, bool rangeMode)
    {
        InitializeComponent();
        _selectedDate = selectedDate.Date;
        _rangeMode = rangeMode;
        if (rangeMode && rangeStart is { } start && start.Date <= _selectedDate)
        {
            _rangeStart = start.Date;
            _rangeEnd = _selectedDate;
        }
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);

        Loaded += (_, _) => ApplyMode();
    }

    /// <summary>True when a date range was chosen; false for a single day.</summary>
    public bool IsRange => _rangeMode;

    /// <summary>The single day, or the last day of the range.</summary>
    public DateTime SelectedDate => _rangeMode && _rangeEnd is { } end ? end : _selectedDate;

    /// <summary>The first day of the range (only meaningful when <see cref="IsRange"/>).</summary>
    public DateTime? RangeStart => _rangeMode ? _rangeStart : null;

    private void ApplyMode()
    {
        SingleDayTab.Tag = _rangeMode ? null : "Active";
        DateRangeTab.Tag = _rangeMode ? "Active" : null;
        DialogTitleText.Text = _rangeMode ? "Choose dates" : "Choose date";
        DialogHintText.Text = _rangeMode ? "Click the first day, then the last" : "Click a day to use it";
        TodayButton.Visibility = _rangeMode ? Visibility.Collapsed : Visibility.Visible;
        ThisWeekButton.Visibility = _rangeMode ? Visibility.Visible : Visibility.Collapsed;
        RangeSummaryText.Visibility = _rangeMode ? Visibility.Visible : Visibility.Collapsed;
        UseDatesButton.Visibility = _rangeMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateRangeSummary();
        RenderCalendar();
    }

    private void UpdateRangeSummary()
    {
        var ready = _rangeStart is not null && _rangeEnd is not null;
        UseDatesButton.IsEnabled = ready;
        UseDatesButton.Opacity = ready ? 1.0 : 0.5;

        if (_rangeStart is { } start && _rangeEnd is { } end)
        {
            var days = (end - start).Days + 1;
            RangeSummaryText.Text = $"{start:d MMM} – {end:d MMM} · {(days == 1 ? "1 day" : days + " days")}";
        }
        else if (_rangeStart is { } first)
        {
            RangeSummaryText.Text = $"From {first:d MMM} – now pick the last day";
        }
        else
        {
            RangeSummaryText.Text = "Pick the first day";
        }
    }

    private void RenderCalendar()
    {
        MonthTitleText.Text = _displayMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        DaysGrid.Children.Clear();

        var first = _displayMonth;
        var mondayIndex = ((int)first.DayOfWeek + 6) % 7;
        var start = first.AddDays(-mondayIndex);

        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var button = new Button
            {
                Content = date.Day.ToString(CultureInfo.InvariantCulture),
                CommandParameter = date,
                Style = (Style)FindResource("CalendarDayButtonStyle")
            };

            if (IsSelected(date))
                button.Tag = "Selected";
            else if (IsInsideRange(date))
                button.Tag = "InRange";
            else if (date == DateTime.Today)
                button.Tag = "Today";
            else if (date.Month != _displayMonth.Month)
                button.Tag = "Other";

            button.Click += Day_Click;
            DaysGrid.Children.Add(button);
        }
    }

    private bool IsSelected(DateTime date) => _rangeMode
        ? date == _rangeStart || date == _rangeEnd
        : date == _selectedDate;

    private bool IsInsideRange(DateTime date) =>
        _rangeMode && _rangeStart is { } start && _rangeEnd is { } end && date > start && date < end;

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: DateTime date }) return;
        date = date.Date;

        if (!_rangeMode)
        {
            // Stage 6B.46: a click on a day is the choice - use it and close.
            _selectedDate = date;
            DialogResult = true;
            Close();
            return;
        }

        // Range: first click is the start, second the end. A second click before the
        // start makes that the new start; a click once both are set starts again.
        if (_rangeStart is null || _rangeEnd is not null || date < _rangeStart)
        {
            _rangeStart = date;
            _rangeEnd = null;
        }
        else
        {
            _rangeEnd = date;
        }

        UpdateRangeSummary();
        RenderCalendar();
    }

    private void SingleDayTab_Click(object sender, RoutedEventArgs e)
    {
        if (!_rangeMode) return;
        _rangeMode = false;
        if (_rangeEnd is { } end) _selectedDate = end;
        ApplyMode();
    }

    private void DateRangeTab_Click(object sender, RoutedEventArgs e)
    {
        if (_rangeMode) return;
        _rangeMode = true;
        _rangeStart = null;
        _rangeEnd = null;
        ApplyMode();
    }

    private void UseDates_Click(object sender, RoutedEventArgs e)
    {
        if (_rangeStart is null || _rangeEnd is null) return;
        DialogResult = true;
        Close();
    }

    private void ThisWeek_Click(object sender, RoutedEventArgs e)
    {
        // Monday of this week to today, used straight away.
        var today = DateTime.Today;
        _rangeStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        _rangeEnd = today;
        DialogResult = true;
        Close();
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        RenderCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        RenderCalendar();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        // Stage 6B.46: uses today and closes, like a click on a day.
        _selectedDate = DateTime.Today;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
