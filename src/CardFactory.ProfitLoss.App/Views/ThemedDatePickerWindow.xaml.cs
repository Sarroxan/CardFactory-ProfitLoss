using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CardFactory.ProfitLoss.App.Views;

public partial class ThemedDatePickerWindow : Window
{
    private DateTime _displayMonth;
    private DateTime _selectedDate;

    public ThemedDatePickerWindow(DateTime selectedDate, bool weeklyMode)
    {
        InitializeComponent();
        _selectedDate = selectedDate.Date;
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);

        DialogTitleText.Text = weeklyMode ? "Choose week ending" : "Choose date";
        DialogHintText.Text = weeklyMode
            ? "Select the week-ending date for this report"
            : "Select the date for this report";

        Loaded += (_, _) => RenderCalendar();
    }

    public DateTime SelectedDate => _selectedDate;

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

            if (date == _selectedDate)
                button.Tag = "Selected";
            else if (date == DateTime.Today)
                button.Tag = "Today";
            else if (date.Month != _displayMonth.Month)
                button.Tag = "Other";

            button.Click += Day_Click;
            DaysGrid.Children.Add(button);
        }
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: DateTime date }) return;
        // Stage 6B.46: a click on a day is the choice - use it and close. The "Use date"
        // button this replaces was a second click to confirm the first.
        _selectedDate = date.Date;
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
