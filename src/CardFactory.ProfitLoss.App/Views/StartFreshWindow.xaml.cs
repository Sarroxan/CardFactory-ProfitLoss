using System.Windows;
using System.Windows.Input;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// Menu › Start fresh (owner, 18 Nov 2026, concept F1): one window for both resets. "Clear the
/// figures" (picked first) is the old Start fresh; "Erase everything on this PC" is the old
/// Reset everything from Store &amp; defaults. The button says which will happen.
/// </summary>
public partial class StartFreshWindow : Window
{
    public StartFreshWindow()
    {
        InitializeComponent();
    }

    /// <summary>True when "Erase everything on this PC" was chosen and confirmed.</summary>
    public bool EraseEverything => EraseEverythingOption.IsChecked == true;

    private void Option_Checked(object sender, RoutedEventArgs e)
    {
        if (ClearButton is null || EraseButton is null) return;
        ClearButton.Visibility = EraseEverything ? Visibility.Collapsed : Visibility.Visible;
        EraseButton.Visibility = EraseEverything ? Visibility.Visible : Visibility.Collapsed;
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
