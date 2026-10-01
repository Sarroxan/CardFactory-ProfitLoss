using System.Windows;
using System.Windows.Input;

namespace CardFactory.ProfitLoss.App.Views;

public partial class ThemedConfirmationWindow : Window
{
    public ThemedConfirmationWindow(string title, string question, string details, string confirmLabel)
    {
        InitializeComponent();
        TitleText.Text = title;
        QuestionText.Text = question;
        DetailText.Text = details;
        ConfirmButton.Content = confirmLabel;
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
