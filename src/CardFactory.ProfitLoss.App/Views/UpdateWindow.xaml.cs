using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.Services;

namespace CardFactory.ProfitLoss.App.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;

    public UpdateWindow(UpdateInfo update)
    {
        InitializeComponent();
        _update = update;
        VersionText.Text = "Version " + UpdateService.Display(update.Version) + " · you have " + UpdateService.Display(UpdateService.CurrentVersion);
        NotesText.Text = string.IsNullOrWhiteSpace(update.Notes) ? "Improvements and fixes." : update.Notes;
    }

    /// <summary>True once the update is installed and the new copy started: the app must close.</summary>
    public bool Installed { get; private set; }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        UpdateNowButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        StatusText.Foreground = (Brush)FindResource("CfDeepBlue");
        try
        {
            await UpdateService.ApplyAsync(_update, new Progress<string>(text => StatusText.Text = text));
            Installed = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x3A));
            StatusText.Text = "The update could not be installed: " + ex.Message;
            UpdateNowButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
