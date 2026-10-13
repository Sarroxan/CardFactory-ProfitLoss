using System.Windows;
using System.Windows.Input;

namespace CardFactory.ProfitLoss.App.Views;

public partial class ThemedConfirmationWindow : Window
{
    /// <param name="subtitle">The line under the title; null keeps the original wording.</param>
    /// <param name="showCancel">False for a message with a single button (the error box).</param>
    public ThemedConfirmationWindow(string title, string question, string details, string confirmLabel,
        string? subtitle = null, bool showCancel = true)
    {
        InitializeComponent();
        TitleText.Text = title;
        QuestionText.Text = question;
        DetailText.Text = details;
        ConfirmButton.Content = confirmLabel;
        if (subtitle is not null) SubtitleText.Text = subtitle;
        if (!showCancel) CancelButton.Visibility = Visibility.Collapsed;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
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
