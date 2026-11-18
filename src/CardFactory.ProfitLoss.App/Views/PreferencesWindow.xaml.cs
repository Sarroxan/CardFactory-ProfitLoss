using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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
    // Reset everything moved to Menu › Start fresh (owner, 18 Nov 2026, concept F1).

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
