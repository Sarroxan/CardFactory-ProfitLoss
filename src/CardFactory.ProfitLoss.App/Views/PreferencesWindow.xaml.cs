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
    public bool ResetRequested { get; private set; }

    // Owner, 16 Nov 2026 (concept R1): Reset settings became Reset everything - everything the
    // app keeps on this PC is removed and the app closes (MainWindow.ResetEverythingAsync).
    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ThemedConfirmationWindow(
            "Reset Everything",
            "Remove everything this app keeps on this PC?",
            string.Empty,
            "Reset everything",
            subtitle: "This removes the app's files from this PC")
        {
            Owner = this,
            Height = 352
        };
        var detail = confirmation.DetailText;
        detail.Inlines.Clear();
        detail.Inlines.Add(new Run("Removed: ") { FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("CfBad") });
        detail.Inlines.Add(new Run("saved Flooid sign-in and store, add-ons, print settings, speed and target settings, "
            + "last year's figures, the diagnostic log and the app's browser data."));
        detail.Inlines.Add(new LineBreak());
        detail.Inlines.Add(new LineBreak());
        detail.Inlines.Add(new Run("The app then signs out of Flooid and closes. Next time it opens like the first time: "
            + "sign in and choose the store. PDFs you saved yourself are kept."));

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
