using System.Windows;
using System.Windows.Input;

namespace CardFactory.ProfitLoss.App.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow(string? storeLocation)
    {
        InitializeComponent();
        LocationTextBox.Text = storeLocation ?? string.Empty;
        Loaded += (_, _) => LocationTextBox.Focus();
    }

    public string StoreLocation => LocationTextBox.Text ?? string.Empty;
    public bool ResetRequested { get; private set; }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ThemedConfirmationWindow(
            "Reset App Settings",
            "Reset all saved app settings to their defaults?",
            "Store / Location, target values and the Daily / Weekly period setting will be reset. Current team figures and exported or manually saved files will be kept.",
            "Reset settings")
        {
            Owner = this
        };

        if (confirmation.ShowDialog() != true) return;

        ResetRequested = true;
        DialogResult = true;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
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
