using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.Services;

namespace CardFactory.ProfitLoss.App.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly Action<UpdateProgress>? _onProgress;
    // Small updates: worked out as soon as the box opens, so the size shows before Update.
    private readonly Task<UpdatePlan?> _plan;

    /// <param name="onProgress">Also told of each step, so the version chip can show the
    /// download filling up (V4, owner 16 Oct 2026).</param>
    public UpdateWindow(UpdateInfo update, Action<UpdateProgress>? onProgress = null)
    {
        InitializeComponent();
        _update = update;
        _onProgress = onProgress;
        _plan = UpdateService.PlanAsync(update);
        Loaded += async (_, _) => ShowSize(await _plan);
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
        StatusText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        for (var i = 0; i < 3; i++) SetStep(i, StepState.Todo);
        DownloadBar.Value = 0;
        Step0Detail.Text = string.Empty;
        try
        {
            var plan = await _plan;
            Step0Label.Text = plan is null ? "Downloading" : "Downloading the changes";
            await UpdateService.ApplyAsync(_update, new Progress<UpdateProgress>(ShowProgress), plan);
            SetStep(2, StepState.Done);
            Installed = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Visibility = Visibility.Visible;
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x3A));
            StatusText.Text = "The update could not be installed: " + ex.Message;
            UpdateNowButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
        }
    }

    private void ShowSize(UpdatePlan? plan)
    {
        string Mb(long bytes) => (bytes / 1048576.0).ToString(bytes < 10 * 1048576L ? "0.0" : "0") + " MB";
        if (plan is not null)
        {
            SizePill.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF7, 0xEF));
            SizePill.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xDF, 0xC8));
            SizeText.Text = "✓ Small update: only the changes";
            SizeAmount.Text = Mb(Math.Max(plan.DownloadBytes, 100 * 1024));
            SizeText.Foreground = SizeAmount.Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x35));
        }
        else
        {
            SizePill.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF7, 0xE0));
            SizePill.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xDE, 0x94));
            SizeText.Text = "! Full download this time";
            SizeAmount.Text = _update.ZipSize > 0 ? Mb(_update.ZipSize) : string.Empty;
            SizeText.Foreground = SizeAmount.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x5B, 0x00));
        }
    }

    private enum StepState { Todo, Now, Done }

    private void ShowProgress(UpdateProgress p)
    {
        _onProgress?.Invoke(p);
        for (var i = 0; i < 3; i++) SetStep(i, i < p.Step ? StepState.Done : i == p.Step ? StepState.Now : StepState.Todo);
        if (p.Step == 0 && p.Total > 0)
        {
            DownloadBar.Value = Math.Min(100, p.Bytes * 100.0 / p.Total);
            Step0Detail.Text = (p.Bytes / 1048576.0).ToString("0.0") + " of " + (p.Total / 1048576.0).ToString("0.0") + " MB";
        }
        else if (p.Step > 0)
        {
            DownloadBar.Value = 100;
        }
    }

    private void SetStep(int index, StepState state)
    {
        var ring = (System.Windows.Shapes.Ellipse)FindName("Step" + index + "Ring");
        var done = (System.Windows.Shapes.Ellipse)FindName("Step" + index + "Done");
        var tick = (System.Windows.Shapes.Path)FindName("Step" + index + "Tick");
        var label = (System.Windows.Controls.TextBlock)FindName("Step" + index + "Label");
        var blue = (Brush)FindResource("CfBlue");
        var navy = (Brush)FindResource("CfDeepBlue");

        done.Visibility = tick.Visibility = state == StepState.Done ? Visibility.Visible : Visibility.Collapsed;
        ring.Visibility = state == StepState.Done ? Visibility.Collapsed : Visibility.Visible;
        ring.Stroke = state == StepState.Now ? blue : new SolidColorBrush(Color.FromRgb(0xC9, 0xD6, 0xE6));
        ring.StrokeThickness = state == StepState.Now ? 2.5 : 1.5;
        label.Foreground = state == StepState.Todo ? new SolidColorBrush(Color.FromRgb(0x98, 0xA6, 0xB8)) : navy;
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
