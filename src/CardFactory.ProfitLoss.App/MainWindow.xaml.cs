using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.ViewModels;
using CardFactory.ProfitLoss.App.Views;
using CardFactory.ProfitLoss.Core.Models;
using CardFactory.ProfitLoss.Core.Services;
using CardFactory.ProfitLoss.Flooid.Models;
using CardFactory.ProfitLoss.ReportParsing.Matching;
using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;
using CardFactory.ProfitLoss.Storage.Services;
using Microsoft.Win32;

namespace CardFactory.ProfitLoss.App;

public partial class MainWindow : Window
{
    private readonly PortableStateService _stateService = new();
    private bool _loadedAutoState;
    private bool _isClosing;
    private bool _teamWindowResizeReady;
    private int _visibleTeamRows = 1;
    private double _oneRowWindowHeight;
    private double _teamRowWindowGrowth = 43.5;
    private bool _threeRowCompact;
    private bool _branchPerformanceActive;
    private bool _reportsActive;
    // Targets from last year: last year's Team ABV/AUB per last-year period, so each day or
    // range is pulled from Flooid once per session, not on every Refresh.
    private readonly Dictionary<(DateTime From, DateTime To), (int Transactions, decimal Abv, decimal Aub)> _lastYearCache = new();
    private (DateTime From, DateTime To)? _staffDiscountPeriod;   // the day or range the Reports staff discounts are for
    // Reports pulled this run (owner, 19 Oct 2026, option 1): Daily Briefing and Reports use
    // what is already here; Refresh pulls fresh. Rules in Core/Services/ReportCache.
    private readonly ReportCache _reportCache = new();
    private bool _pullFresh;                 // set for the length of a Refresh
    private DateTime? _oldestPullUsed;       // the oldest copy a pull used, for "updated … ago"
    private bool _branchGiftCardsActive;
    private FlooidLoginWindow? _flooidLoginWindow;
    private bool _grabAfterFlooidSignIn;
    private bool _grabReportsOnlyAfterSignIn;   // the button pressed before sign-in was Generate reports
    private bool _briefingAfterSignIn;          // the button pressed before sign-in was Export to Daily Briefing
    private bool _flooidGrabBusy;
    // Stage 6A.93: signing in triggers a grab, so the signed-in state has to be read as
    // a transition rather than a level. The status event fires on every navigation and
    // the grab itself drives the same browser, so without a latch one sign-in would
    // start a grab, whose navigation would report signed-in again, and so on.
    private readonly CardFactory.ProfitLoss.Flooid.Services.FlooidSignInTracker _flooidSignIn = new();
    private DateTime _lastAutoGrabUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        MouseEnter += (_, _) => PointerDiagnostics.Log(this, "entered main window");   // Stage 6B.45
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Operators.CollectionChanged += Operators_CollectionChanged;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            viewModel.Notice += (_, text) => ShowNotice(text);
        }
    }

    // Deep check 1: every SetStatus used to land in a property nothing on screen showed.
    // Now it shows as a navy note at the bottom for a few seconds - green tick for done,
    // yellow "!" when something needs attention. Not while the retrieval box is up: that
    // box is already reporting progress and errors itself.
    private static readonly string[] NoticeProblemWords =
        { "fail", "could not", "couldn't", "partial", "partly", "choose", "unavailable", "left blank", "without", "error", "sign in to" };
    private System.Windows.Threading.DispatcherTimer? _noticeTimer;

    private void ShowNotice(string text)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(text) || RetrievalOverlay.Visibility == Visibility.Visible) return;

        var problem = NoticeProblemWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        NoticeText.Text = text;
        NoticeDot.Background = problem ? (Brush)FindResource("CfYellow") : new SolidColorBrush(Color.FromRgb(0x2F, 0xB8, 0x66));
        NoticeTick.Visibility = problem ? Visibility.Collapsed : Visibility.Visible;
        NoticeBang.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;

        NoticeToast.Visibility = Visibility.Visible;
        NoticeToast.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));

        _noticeTimer ??= new System.Windows.Threading.DispatcherTimer();
        _noticeTimer.Stop();
        _noticeTimer.Interval = TimeSpan.FromSeconds(problem ? 7 : 4);
        _noticeTimer.Tick -= NoticeTimer_Tick;
        _noticeTimer.Tick += NoticeTimer_Tick;
        _noticeTimer.Start();
    }

    private void NoticeTimer_Tick(object? sender, EventArgs e)
    {
        _noticeTimer?.Stop();
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) => { if (NoticeToast.Opacity == 0) NoticeToast.Visibility = Visibility.Collapsed; };
        NoticeToast.BeginAnimation(OpacityProperty, fade);
    }

    // The Top performers strip appears after a Refresh and adds 36px to the Team page.
    // Without this the window stays the same size and the whole dashboard is scaled down
    // to fit; instead the strip takes its room from the Team-only space at the bottom.
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasTopPerformers)) UpdateTeamSpacer();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => WindowAppearance.PreferRoundedCorners(this);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel initialViewModel)
        {
            ApplyTeamLayout(initialViewModel.Operators.Count);
        }
        SetBranchPerformanceView(false);
        SetBranchDetailView(false);

        FitToWorkArea();
        CaptureOneRowWindowGeometry();
        UpdateWindowChromeVisuals();

        if (_loadedAutoState) return;
        _loadedAutoState = true;
        _ = CheckForUpdateAsync();   // at launch, in the background

        // Then quietly every few hours, so a copy left open all day still learns of a new
        // release: the chip turns yellow, with no box popping up over someone's work.
        var updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = BackgroundUpdateCheckInterval };
        updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync(background: true);
        updateTimer.Start();

        // Freshness wording ("updated 12 min ago") moves on by itself.
        var freshnessTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        freshnessTimer.Tick += (_, _) => (DataContext as MainViewModel)?.RefreshFreshness();
        freshnessTimer.Start();
        if (DataContext is not MainViewModel viewModel) return;

        try
        {
            // Stage 6A.92: nothing is carried over between sessions. The automatic state
            // file is deleted on close, and it is deliberately not read here either -
            // that way a crash, which never reaches the close handler, still cannot leak
            // yesterday's figures into a fresh start. Save and Load remain for keeping a
            // day's work on purpose.
            _stateService.ClearAuto();
            // Stage 6A.92: the date reset that used to live here (6A.73) is gone with the
            // restore it existed to correct. MainViewModel already starts on today with a
            // seven day range, and nothing overwrites it before the window is shown.
            viewModel.SetStatus("Ready · figures clear on exit, use Save to keep a copy", notify: false);
        }
        catch
        {
            viewModel.SetStatus("Ready · figures clear on exit, use Save to keep a copy", notify: false);
        }
    }


    private void CaptureOneRowWindowGeometry()
    {
        if (WindowState == WindowState.Maximized) return;

        CalculatorView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var contentWidth = CalculatorView.DesiredSize.Width > 1 ? CalculatorView.DesiredSize.Width : 1220.0;
        var scale = Math.Max(0.1, (Width - 2) / contentWidth);

        _oneRowWindowHeight = Height;
        _teamRowWindowGrowth = 43.5 * scale;

        if (DataContext is MainViewModel viewModel)
        {
            _visibleTeamRows = Math.Clamp(viewModel.Operators.Count, 1, 3);
        }
        else
        {
            _visibleTeamRows = 1;
        }

        _teamWindowResizeReady = true;
        ResizeWindowForVisibleTeamRows(Math.Min(_visibleTeamRows, 2));
    }

    private void Operators_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        ApplyTeamLayout(viewModel.Operators.Count);

        if (!_teamWindowResizeReady || WindowState == WindowState.Maximized) return;
        ResizeWindowForVisibleTeamRows(Math.Clamp(viewModel.Operators.Count, 1, 2));
    }

    // L3: Team-only space at the bottom. The footnote (25.5px) and formula strip (54.5px)
    // are gone and the hint line under Targets takes 24px, so the spacer grows by 56px to
    // keep the Team view the same height as Branch and Reports (474px panels).
    private const double TeamSpacerHeight = 96.0;
    private const double TopPerformersStripHeight = 36.0;

    private void UpdateTeamSpacer()
    {
        var height = TeamSpacerHeight;
        if (_threeRowCompact) height -= 18.5;                       // what the formula strip and target row used to give up
        if (DataContext is MainViewModel { HasTopPerformers: true }) height -= TopPerformersStripHeight;
        CalculateSpacer.Height = Math.Max(0, height);
    }

    private void ApplyTeamLayout(int operatorCount)
    {
        var visibleRows = Math.Clamp(operatorCount, 1, 3);
        TeamScrollViewer.Height = visibleRows * 43.5;
        TeamScrollViewer.VerticalScrollBarVisibility = operatorCount > 3
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;

        var compact = operatorCount >= 3;
        if (_threeRowCompact == compact) { UpdateTeamSpacer(); return; }
        _threeRowCompact = compact;

        if (compact)
        {
            // Recover exactly 43.5px (one full team row) without making any operator row smaller.
            HistoricalTargetRow.Margin = new Thickness(18, 2, 18, 0);    // still saves 4px after the 6A.32 control enlargement
            TargetsRow.Margin = new Thickness(18, 2, 18, 0);             // 3px
            TeamPerformanceSection.Margin = new Thickness(0, 5, 0, 0);  // 6px
            AddPersonButton.Margin = new Thickness(28, 4, 0, 0);         // 3px
            PerformanceSummarySection.Margin = new Thickness(0, 6, 0, 0);// 6px
            CalculateSpacer.Margin = new Thickness(28, 6, 0, 0);          // 6px
        }
        else
        {
            HistoricalTargetRow.Margin = new Thickness(18, 6, 18, 0);
            TargetsRow.Margin = new Thickness(18, 5, 18, 0);
            TeamPerformanceSection.Margin = new Thickness(0, 11, 0, 0);
            AddPersonButton.Margin = new Thickness(28, 7, 0, 0);
            PerformanceSummarySection.Margin = new Thickness(0, 12, 0, 0);
            CalculateSpacer.Margin = new Thickness(28, 12, 0, 0);
        }

        UpdateTeamSpacer();
    }

    private void ResizeWindowForVisibleTeamRows(int rows)
    {
        rows = Math.Clamp(rows, 1, 2);
        if (!_teamWindowResizeReady || WindowState == WindowState.Maximized) return;

        var targetHeight = _oneRowWindowHeight + ((rows - 1) * _teamRowWindowGrowth);
        var currentBottom = Top + Height;

        // Keep the bottom edge steady so Team Performance gains room without making
        // the dashboard smaller. If the window reaches the top of the work area,
        // any remaining growth continues downward instead of changing the Viewbox scale.
        Height = targetHeight;
        var work = SystemParameters.WorkArea;
        Top = Math.Max(work.Top, currentBottom - targetHeight);
        _visibleTeamRows = rows;
    }

    private void FitToWorkArea()
    {
        if (WindowState == WindowState.Maximized) return;

        // Keep the HTML dashboard's proportions while sizing the actual window tightly around it.
        // This avoids the large left/right gutters created when a fixed 1180x850 window is used on
        // shorter displays. The dashboard therefore stays close to the rounded frame without any
        // page-level scrolling or non-uniform stretching.
        const double chromeHeight = 34.0;
        const double workAreaMargin = 12.0;
        const double fallbackContentWidth = 1220.0;
        const double fallbackContentHeight = 765.0;
        const double maximumUpscale = 1.00;
        const double preferredScaleFactor = 0.97;

        CalculatorView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = CalculatorView.DesiredSize;
        var contentWidth = desired.Width > 1 ? desired.Width : fallbackContentWidth;
        var contentHeight = desired.Height > 1 ? desired.Height : fallbackContentHeight;

        var work = SystemParameters.WorkArea;
        var availableWidth = Math.Max(1, work.Width - (workAreaMargin * 2));
        var availableClientHeight = Math.Max(1, work.Height - (workAreaMargin * 2) - chromeHeight);

        var scale = Math.Min(availableWidth / contentWidth, availableClientHeight / contentHeight);
        scale = Math.Min(scale, maximumUpscale) * preferredScaleFactor;

        // Respect the work area first. MinWidth/MinHeight are intentionally modest so they do not
        // force the dashboard to overflow on laptop displays with higher Windows scaling.
        Width = Math.Min(availableWidth, contentWidth * scale + 2);
        Height = Math.Min(work.Height - (workAreaMargin * 2), contentHeight * scale + chromeHeight + 2);

        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // Stage 6A.83: maximising is blocked. Removing the button is not enough on its
        // own - Win+Up, dragging to the top edge and Alt+Space all still ask for it - so
        // the state is refused here, which is the one place they all pass through.
        // Setting Normal raises this handler again with Normal, so it does not recurse.
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            return;
        }

        UpdateWindowChromeVisuals();
        if (WindowState == WindowState.Normal && _teamWindowResizeReady && DataContext is MainViewModel viewModel)
        {
            ApplyTeamLayout(viewModel.Operators.Count);
            ResizeWindowForVisibleTeamRows(Math.Clamp(viewModel.Operators.Count, 1, 2));
        }
    }

    private void UpdateWindowChromeVisuals()
    {
        // Stage 6A.83: the window can no longer be maximised, so there is no square
        // -cornered state to switch to and no restore icon to swap.
        if (WindowFrame is null) return;

        WindowFrame.CornerRadius = new CornerRadius(20);
        WindowAppearance.PreferRoundedCorners(this);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Stage 6A.83: a double click on the title bar used to maximise. It now does
        // nothing, rather than falling through to a drag on the second click.
        if (e.ClickCount == 2) return;

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if Windows changes mouse capture during a state transition.
        }
    }

    private void MinimiseButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();


    private void NumericTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void NumericTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        e.Handled = true;
        textBox.Focus();
        textBox.SelectAll();
    }

    private void NumericTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var valid = TryNormaliseNumericText(textBox, out _);
        if (valid)
        {
            textBox.ClearValue(Control.BackgroundProperty);
            textBox.ClearValue(Control.BorderBrushProperty);
            textBox.ClearValue(ToolTipService.ToolTipProperty);
            return;
        }

        textBox.Background = new SolidColorBrush(Color.FromRgb(255, 248, 249));
        textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(211, 92, 102));
        ToolTipService.SetToolTip(textBox, "Enter a valid non-negative number.");
    }

    private void NumericTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox || !TryNormaliseNumericText(textBox, out var normalised))
        {
            return;
        }

        if (!string.Equals(textBox.Text, normalised, StringComparison.Ordinal))
        {
            textBox.Text = normalised;
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    private static bool TryNormaliseNumericText(TextBox textBox, out string normalised)
    {
        normalised = textBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalised))
        {
            normalised = string.Empty;
            return true;
        }

        var path = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.ParentBinding.Path?.Path ?? string.Empty;
        var clean = normalised.Replace("£", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();

        if (path is "SalesTarget" or "AbvTarget" or "AubTarget" or "SalesEntry" or "GiftCardValueEntry")
        {
            if (!decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value < 0m)
            {
                return false;
            }

            normalised = value == 0m ? string.Empty : value.ToString("0.##", CultureInfo.InvariantCulture);
            return true;
        }

        if (path is "GiftCardQuantityEntry" or "TransactionsEntry" or "UnitsEntry")
        {
            if (!int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                return false;
            }

            if (path == "GiftCardQuantityEntry" && value > 99)
            {
                return false;
            }

            normalised = value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;

        // Stage 6A.92: was a SaveAuto of the whole calculator. The next start is meant
        // to be completely fresh, so the working file is removed rather than written.
        // ClearAuto swallows its own failures; nothing here may block shutdown.
        _stateService.ClearAuto();

        try { _flooidLoginWindow?.ShutdownAndClose(); } catch { }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Operators.CollectionChanged -= Operators_CollectionChanged;
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (_flooidLoginWindow is not null)
        {
            _flooidLoginWindow.ConnectionStatusChanged -= FlooidLoginWindow_ConnectionStatusChanged;
            _flooidLoginWindow = null;
        }
    }

    private UpdateInfo? _availableUpdate;
    private bool _updateCheckBusy;
    private DateTime? _lastUpdateCheck;
    private static readonly TimeSpan BackgroundUpdateCheckInterval = TimeSpan.FromHours(3);

    /// <param name="userAsked">From the Menu's "Check for updates" or a click on the version
    /// chip, rather than the automatic check at launch: shows "Checking…" on the chip and puts
    /// the outcome in the status line, since the menu has closed by then.</param>
    /// <param name="background">The timed check while the app is open: leaves the chip alone
    /// until there is news, never shows "Can't check" for a passing network blip, and never
    /// pops the update box up - a newer version just turns the chip yellow.</param>
    private async Task CheckForUpdateAsync(bool userAsked = false, bool background = false)
    {
        if (_updateCheckBusy) return;
        _updateCheckBusy = true;
        try
        {
            await CheckForUpdateCoreAsync(userAsked, background);
        }
        finally
        {
            _updateCheckBusy = false;
            _lastUpdateCheck = DateTime.Now;
        }
    }

    private async Task CheckForUpdateCoreAsync(bool userAsked, bool background)
    {
        var current = UpdateService.Display(UpdateService.CurrentVersion);
        if (!background)
        {
            VersionChip.Tag = "Unknown";
            VersionChip.Content = "v" + current;
            VersionChip.ToolTip = "Checking for updates…";
        }

        var check = await UpdateService.CheckAsync();
        if (!check.Reached)
        {
            if (background) return;   // try again next time; the chip keeps what it last knew
            VersionChip.Tag = "CantCheck";
            VersionChip.Content = "v" + current;
            VersionChip.ToolTip = "Could not reach GitHub to check for updates. Click to try again.";
            if (userAsked && DataContext is MainViewModel failedViewModel)
                failedViewModel.SetStatus("Could not reach GitHub to check for updates · try again later");
            return;
        }
        if (check.Update is not { } update)
        {
            _availableUpdate = null;
            VersionChip.Tag = "UpToDate";
            VersionChip.Content = "v" + current;
            VersionChip.ToolTip = "This is the latest version. Click to check again.";
            if (userAsked && DataContext is MainViewModel latestViewModel)
                latestViewModel.SetStatus("You have the latest version · v" + current);
            return;
        }

        _availableUpdate = update;
        VersionChip.Tag = "Available";
        VersionChip.Content = "v" + UpdateService.Display(update.Version);
        VersionChip.ToolTip = "You have " + current + ". Click to update.";

        // Offer it straight away at launch or when asked: update now, or Later and carry on
        // (the chip stays yellow for later). Not over a Refresh already in progress, and
        // never from the background check.
        if (!background && RetrievalOverlay.Visibility != Visibility.Visible && IsLoaded) ShowUpdateDialog();
    }

    private void ShowUpdateDialog()
    {
        if (_availableUpdate is null) return;
        var update = _availableUpdate;
        var dialog = new UpdateWindow(update, ShowUpdateProgressOnChip) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Installed)
        {
            Application.Current.Shutdown();
            return;
        }
        // Later, or the install failed: back to "ready to install".
        VersionChip.Tag = "Available";
        VersionChip.Content = "v" + UpdateService.Display(update.Version);
        VersionChip.MinWidth = 0;
        SetChipProgress(0);
        UpdateMenuVersionPill();
    }

    // V4 (owner, 16 Oct 2026): while the update box downloads, the chip fills with yellow and
    // shows the percentage, then "installing…" while the download is checked and swapped in.
    private void ShowUpdateProgressOnChip(UpdateProgress p)
    {
        // Hold the chip at its "ready to install" width while it fills, so it doesn't jump about
        // as the text changes.
        if (VersionChip.MinWidth == 0) VersionChip.MinWidth = Math.Max(VersionChip.ActualWidth, 140);
        if (p.Step == 0)
        {
            var share = p.Total > 0 ? Math.Min(1.0, (double)p.Bytes / p.Total) : 0.0;
            VersionChip.Tag = "Downloading";
            VersionChip.Content = Math.Round(share * 100) + "%";
            VersionChip.ToolTip = "Downloading the update";
            SetChipProgress(share);
        }
        else
        {
            VersionChip.Tag = "Installing";
            if (_availableUpdate is { } update) VersionChip.Content = "v" + UpdateService.Display(update.Version);
            VersionChip.ToolTip = p.Step == 1 ? "Checking the download" : "Installing and restarting";
            SetChipProgress(1.0);
        }
    }

    private void SetChipProgress(double share)
    {
        VersionChip.ApplyTemplate();
        if (VersionChip.Template?.FindName("ProgressFill", VersionChip) is Border fill)
        {
            var inner = Math.Max(0, VersionChip.ActualWidth - 3);   // inside the 1.5px outline
            fill.Width = inner * Math.Clamp(share, 0, 1);
            fill.CornerRadius = share >= 0.97 ? new CornerRadius(10) : new CornerRadius(10, 0, 0, 10);
        }
    }

    // Concept C: the chip checks again when clicked; with an update waiting it opens the box.
    private void VersionChip_Click(object sender, RoutedEventArgs e)
    {
        if (Equals(VersionChip.Tag, "Available")) ShowUpdateDialog();
        else _ = CheckForUpdateAsync(userAsked: true);
    }

    // Concept A: Menu > Settings > Check for updates.
    private void CheckForUpdatesMenu_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        _ = CheckForUpdateAsync(userAsked: true);
    }

    // The menu row's second line: "v1.3.0 · checked 5 min ago".
    private void UpdateCheckForUpdatesDetail()
    {
        var text = "v" + UpdateService.Display(UpdateService.CurrentVersion);
        if (_updateCheckBusy) text += " · checking…";
        else if (_lastUpdateCheck is { } at)
        {
            var minutes = (int)Math.Max(0, (DateTime.Now - at).TotalMinutes);
            text += minutes < 1 ? " · checked just now"
                : minutes < 60 ? " · checked " + minutes + " min ago"
                : " · checked " + (minutes / 60) + " hr " + (minutes % 60) + " min ago";
        }
        CheckForUpdatesDetail.Text = text;
    }

    // D1: the Menu button shows "Close" in yellow while the menu is open, however it closes.
    // Pressing "Close" first closes the popup (it closes on any click outside it), then the
    // click lands on the button; without this check that click would open it straight again.
    private DateTime _menuClosedAtUtc = DateTime.MinValue;
    private void MainMenuPopup_Opened(object? sender, EventArgs e) => MenuButton.Tag = "Open";
    private void MainMenuPopup_Closed(object? sender, EventArgs e)
    {
        MenuButton.Tag = null;
        _menuClosedAtUtc = DateTime.UtcNow;
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (!MainMenuPopup.IsOpen && DateTime.UtcNow - _menuClosedAtUtc < TimeSpan.FromMilliseconds(300)) return;
        MainMenuPopup.PlacementTarget = MenuButton;
        UpdateCheckForUpdatesDetail();
        UpdateDailyBriefingCardDetail();
        UpdateMenuVersionPill();
        if (!MainMenuPopup.IsOpen) ShowMenuTab(MenuTabBriefing);   // opens on Daily Briefing
        // Stage 6B.30: read the setting each time the menu opens, so it always shows what is
        // actually in force - including a value written into the file by hand.
        if (!MainMenuPopup.IsOpen) { LoadRetrievalSpeedOptions(); LoadLastYearTargetsHint(); }
        MainMenuPopup.IsOpen = !MainMenuPopup.IsOpen;
    }



    // Stage 6B.02: two segments, each selecting its own section, rather than one button
    // toggling to whatever the other one was. Selecting the section already showing is a
    // no-op by way of SetBranchPerformanceView, which is idempotent.
    private void TeamSection_Click(object sender, RoutedEventArgs e) => SetBranchPerformanceView(false);

    private void BranchSection_Click(object sender, RoutedEventArgs e) => SetBranchPerformanceView(true);

    // Reports section (design C): the third header tab. It takes the same slot as Branch
    // and hides the same Team-only chrome; Team and Branch figures are untouched.
    private void ReportsSection_Click(object sender, RoutedEventArgs e) => SetBranchPerformanceView(false, reports: true);

    private void BranchHourlyTab_Click(object sender, RoutedEventArgs e) => SetBranchDetailView(false);

    private void BranchGiftCardsTab_Click(object sender, RoutedEventArgs e) => SetBranchDetailView(true);

    private void SetBranchPerformanceView(bool branchActive, bool reports = false)
    {
        if (TeamModePanel is null || BranchModePanel is null) return;

        _branchPerformanceActive = branchActive;
        _reportsActive = reports;
        var teamActive = !branchActive && !reports;
        TeamModePanel.Visibility = teamActive ? Visibility.Visible : Visibility.Collapsed;
        BranchModePanel.Visibility = branchActive ? Visibility.Visible : Visibility.Collapsed;
        if (ReportsModePanel is not null)
            ReportsModePanel.Visibility = reports ? Visibility.Visible : Visibility.Collapsed;
        if (ReportsSectionSegment is not null)
            ReportsSectionSegment.Tag = reports ? "Active" : null;
        var otherThanTeam = branchActive || reports;

        // Stage 6A.35 gives Branch Performance the vertical space normally used by
        // Team-only summary/formula chrome. This keeps the whole application fixed
        // while showing far more Branch rows before an internal scrollbar is needed.
        if (PerformanceSummarySection is not null)
            PerformanceSummarySection.Visibility = otherThanTeam ? Visibility.Collapsed : Visibility.Visible;
        if (CalculateSpacer is not null)
            CalculateSpacer.Visibility = otherThanTeam ? Visibility.Collapsed : Visibility.Hidden;

        // Generate reports (concept B): the same button, renamed on Reports, with a report
        // mark in place of the refresh arrows. It pulls only what Reports shows.
        if (RefreshButton is not null)
        {
            RefreshButton.Content = reports ? "Generate reports" : "Refresh";
            RefreshButton.Tag = reports ? "Reports" : null;
            RefreshButton.ToolTip = reports
                ? "Generate voids, returns, no sales and staff discounts from Flooid"
                : "Refresh Team and Branch Performance from Flooid";
        }

        // Stage 6B.02: the header tabs. All are always visible; the active one is marked by Tag.
        if (TeamSectionSegment is not null)
            TeamSectionSegment.Tag = teamActive ? "Active" : null;
        if (BranchSectionSegment is not null)
            BranchSectionSegment.Tag = branchActive ? "Active" : null;

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetStatus(reports ? "Reports · voids, returns and no sales"
                : branchActive ? "Branch Performance · team figures preserved"
                : "Team Performance · branch figures preserved", notify: false);
        }
    }

    private void SetBranchDetailView(bool giftCards)
    {
        if (BranchHourlyPanel is null || BranchGiftCardsPanel is null) return;

        _branchGiftCardsActive = giftCards;
        BranchHourlyPanel.Visibility = giftCards ? Visibility.Hidden : Visibility.Visible;
        BranchGiftCardsPanel.Visibility = giftCards ? Visibility.Visible : Visibility.Hidden;
        if (BranchHourlyTabButton is not null) BranchHourlyTabButton.Tag = giftCards ? null : "Active";
        if (BranchGiftCardsTabButton is not null) BranchGiftCardsTabButton.Tag = giftCards ? "Active" : null;
    }

    // K1: one date button. Single day or date range is chosen inside the calendar, so the
    // main screen no longer needs its own Day / Range switch. The stored values are still
    // Daily and Date Range, so saved files and every report path are unchanged.
    private void PeriodDate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        var isRange = string.Equals(viewModel.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase);
        var dialog = new ThemedDatePickerWindow(
            viewModel.SelectedDate ?? DateTime.Today,
            isRange ? viewModel.RangeStartDate : null,
            isRange)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        if (dialog.IsRange && dialog.RangeStart is { } start)
        {
            viewModel.HistoricalTargetMode = "Date Range";
            viewModel.RangeStartDate = start;
            viewModel.SelectedDate = dialog.SelectedDate;
        }
        else
        {
            viewModel.HistoricalTargetMode = "Daily";
            viewModel.SelectedDate = dialog.SelectedDate;
        }
    }

    // Stage 6B.30: the speed control reflects the file rather than assuming, so opening
    // the menu always shows what is actually in force - including a value someone wrote by
    // hand. _loadingSpeedOptions stops the Checked handler firing while that is set up and
    // writing the file straight back.
    private bool _loadingSpeedOptions;

    private void LoadRetrievalSpeedOptions()
    {
        _loadingSpeedOptions = true;
        try
        {
            FlooidTimings.Refresh();
            var fast = FlooidTimings.Scale <= 0.001;
            SpeedFastOption.IsChecked = fast;
            SpeedSafeOption.IsChecked = !fast;
            RetrievalSpeedHint.Text = fast
                ? "No fixed waits. About 4 seconds quicker per refresh."
                : "The original proven timings.";
        }
        finally
        {
            _loadingSpeedOptions = false;
        }
    }

    private void RetrievalSpeed_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSpeedOptions || DataContext is not MainViewModel viewModel) return;

        var fast = SpeedFastOption.IsChecked == true;
        if (!FlooidTimings.TrySetScale(fast ? 0d : 1d))
        {
            viewModel.SetStatus("Retrieval speed could not be saved");
            return;
        }

        RetrievalSpeedHint.Text = fast
            ? "No fixed waits. About 4 seconds quicker per refresh."
            : "The original proven timings.";
        viewModel.SetStatus("Retrieval speed set to " + (fast ? "Fast" : "Safe"));
    }

    private void ClearFigures_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;

        // Stage 6B.30: the same reset the Preferences dialog performs, one click away.
        viewModel.ResetAppSettings();
        _stateService.ClearAuto();
        viewModel.SetStatus("Figures cleared");
    }


    private void LoadLastYearTargetsHint()
    {
        LastYearTargetSettings.Load();
        LastYearTargetsHint.Text = LastYearTargetSettings.Summary;
    }

    /// <summary>Menu → Settings → Targets from last year (owner, 16 Oct 2026, concept T2).</summary>
    private void LastYearTargetsMenu_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;

        LastYearTargetSettings.Load();
        var window = new LastYearTargetsWindow(
            LastYearTargetSettings.Enabled, LastYearTargetSettings.AbvPercent, LastYearTargetSettings.AubPercent,
            viewModel.LastYearResult)
        {
            Owner = this
        };
        if (window.ShowDialog() != true) return;

        if (!LastYearTargetSettings.TrySave(window.Enabled, window.AbvPercent, window.AubPercent))
        {
            viewModel.SetStatus("Targets from last year could not be saved");
            return;
        }

        // New percentages apply straight away to the figures already pulled, if any.
        if (window.Enabled && viewModel.LastYearResult is { } held)
        {
            var updated = LastYearTargetCalculator.Calculate(held.FromDate, held.ToDate, 1, held.LastYearAbv, held.LastYearAub,
                window.AbvPercent, window.AubPercent);
            if (updated is not null) viewModel.ApplyLastYearTargets(updated);
        }
        viewModel.SetStatus("Targets from last year · " + LastYearTargetSettings.Summary);
    }

    /// <summary>
    /// Targets from last year: last year's Team ABV and AUB for the same weekdays 52 weeks back,
    /// from the same two reports and the same calculation as today's Team figures (Refunds,
    /// Voids &amp; No Sales merged with Gift Cards, gift cards taken out). Returns a note when the
    /// targets could not be filled, or null when they were.
    /// </summary>
    /// <summary>One report: the copy already held when it is still good (and this is not a
    /// Refresh), otherwise pulled from Flooid and kept.</summary>
    private async Task<string> PullAsync(string report, DateTime from, DateTime to, Func<Task<string>> fetch)
    {
        var now = DateTime.Now;
        if (!_pullFresh && _reportCache.TryGet(report, from, to, now, out var held, out var pulledAt))
        {
            AppFiles.AppendDiagnostic($"[{now:HH:mm:ss}] {report} {from:dd/MM/yyyy} - {to:dd/MM/yyyy}: used the copy pulled at {pulledAt:HH:mm:ss}{Environment.NewLine}");
            NoteOldest(pulledAt);
            return held;
        }

        var html = await fetch();
        _reportCache.Store(report, from, to, html, now);
        NoteOldest(now);
        return html;
    }

    /// <summary>Owner, 19 Oct 2026: kept across a sign in after the session runs out (same
    /// store), forgotten on Sign out, Change store, or when another store signs in.</summary>
    private void ForgetPulledReports()
    {
        _reportCache.Clear();
        _lastYearCache.Clear();
    }

    /// <summary>Stamps sold (owner, 21 Oct 2026). Never stops a pull: a problem shows the tile
    /// as "—", leaves the Daily Briefing's Sold boxes blank, and returns a note for the status line.</summary>
    private async Task<string?> PullStampsAsync(MainViewModel viewModel, FlooidLoginWindow loginWindow, DateTime startDate, DateTime endDate)
    {
        try
        {
            viewModel.SetFlooidStatus("Retrieving Stamps…", "Working");
            SetRetrievalStep(5);
            var html = await PullAsync("Stamps", startDate, endDate, () => loginWindow.FetchStampsHtmlAsync(startDate, endDate));
            var report = new GiftCardReportParser("Stamps").Parse(html);
            viewModel.ApplyStampsSold(StampsSold.From(report.Items));
            return null;
        }
        catch (Exception ex)
        {
            viewModel.ApplyStampsSold(null);
            AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] Stamps ({startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}) failed: {ex}{Environment.NewLine}");
            return "Stamps not read: " + ex.Message;
        }
    }

    private void NoteOldest(DateTime pulledAt)
    {
        if (_oldestPullUsed is null || pulledAt < _oldestPullUsed) _oldestPullUsed = pulledAt;
    }

    private async Task<string?> FillTargetsFromLastYearAsync(MainViewModel viewModel, FlooidLoginWindow loginWindow, DateTime startDate, DateTime endDate)
    {
        var (lyFrom, lyTo) = LastYearTargetCalculator.LastYearRange(startDate, endDate);
        try
        {
            if (!_lastYearCache.TryGetValue((lyFrom, lyTo), out var held))
            {
                var refunds = new RefundsVoidsReportParser().Parse(await loginWindow.FetchRefundsVoidsHtmlAsync(lyFrom, lyTo));
                var giftCards = new GiftCardReportParser().Parse(await loginWindow.FetchGiftCardHtmlAsync(lyFrom, lyTo));
                var merged = new OperatorReportMerger().Merge(refunds, giftCards);
                var team = new TeamPerformanceCalculator().Calculate(merged, new PerformanceTargets(0m, 0m, 0m));
                held = (team.TotalTransactions, team.ActualAbv, team.ActualAub);
                _lastYearCache[(lyFrom, lyTo)] = held;
            }

            var targets = LastYearTargetCalculator.Calculate(lyFrom, lyTo, held.Transactions, held.Abv, held.Aub,
                LastYearTargetSettings.AbvPercent, LastYearTargetSettings.AubPercent);
            if (targets is null)
                return $"no sales on {(lyFrom == lyTo ? lyFrom.ToString("dd/MM/yyyy") : lyFrom.ToString("dd/MM/yyyy") + " - " + lyTo.ToString("dd/MM/yyyy"))} last year, targets left as they were";

            viewModel.ApplyLastYearTargets(targets);
            return null;
        }
        catch (Exception ex)
        {
            AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] Targets from last year ({lyFrom:dd/MM/yyyy} - {lyTo:dd/MM/yyyy}) failed: {ex}{Environment.NewLine}");
            return "last year's figures could not be read, targets left as they were · " + ex.Message;
        }
    }

    private void PreferencesMenu_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;

        var preferences = new PreferencesWindow(viewModel.StoreLocation)
        {
            Owner = this
        };

        if (preferences.ShowDialog() != true) return;

        if (preferences.ResetRequested)
        {
            viewModel.ResetAppSettings();
            try
            {
                // Stage 6A.92: nothing is restored on startup now, so writing the file
                // here would only leave data on disk that is never read. Clear instead.
                _stateService.ClearAuto();
                viewModel.SetStatus("App settings reset · team figures kept");
            }
            catch
            {
                viewModel.SetStatus("Settings reset · automatic save unavailable");
            }
            return;
        }

        viewModel.StoreLocation = preferences.StoreLocation.Trim();
        try
        {
            // Stage 6A.92: the store location is not carried between sessions either. It
            // is read back out of each Flooid report's Outlet field, so the first grab
            // repopulates it without anyone typing it again.
            _stateService.ClearAuto();
            viewModel.SetStatus(string.IsNullOrWhiteSpace(viewModel.StoreLocation)
                ? "Preferences saved"
                : $"Preferences saved · {viewModel.StoreLocation}");
        }
        catch
        {
            viewModel.SetStatus("Preferences updated · automatic save unavailable");
        }
    }

    private async void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save Profit & Loss Calculator File",
            Filter = "Card Factory P&L file (*.cfpl)|*.cfpl|JSON file (*.json)|*.json",
            DefaultExt = ".cfpl",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(viewModel.StoreLocation) ? "Profit-Loss" : $"Profit-Loss-{MakeSafeFileName(viewModel.StoreLocation)}"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _stateService.SaveAsync(dialog.FileName, viewModel.CaptureState());
            viewModel.SetStatus("File saved");
        }
        catch (Exception ex)
        {
            ShowProblem("Couldn't save this day", "The file could not be saved.", ex.Message + " Try saving to Documents.");
        }
    }

    // Deep check 5: the app's own message box instead of the grey Windows one.
    private void ShowProblem(string title, string question, string detail) =>
        new ThemedConfirmationWindow(title, question, detail, "OK", subtitle: "Nothing was changed", showCancel: false) { Owner = this }.ShowDialog();

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new OpenFileDialog
        {
            Title = "Load Profit & Loss Calculator File",
            Filter = "Card Factory P&L file (*.cfpl)|*.cfpl|JSON file (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var state = await _stateService.LoadAsync(dialog.FileName);
            if (state is null) throw new InvalidDataException("The selected file does not contain calculator data.");
            viewModel.ApplyState(state);
            viewModel.SetStatus("File loaded");
        }
        catch (Exception ex)
        {
            ShowProblem("Couldn't open that day", "The file could not be opened.", ex.Message);
        }
    }

    // Owner, 21 Oct 2026 (concept P1): "Open as a PDF" replaces "Export as a web page" - the
    // same report, opened straight away as a PDF in the browser, with no save box.
    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;
        var date = viewModel.SelectedDate ?? DateTime.Today;
        var stem = string.IsNullOrWhiteSpace(viewModel.StoreLocation)
            ? $"Profit-Loss-{date:yyyy-MM-dd}"
            : $"Profit-Loss-{MakeSafeFileName(viewModel.StoreLocation)}-{date:yyyy-MM-dd}";
        try
        {
            viewModel.SetStatus("Opening the report as a PDF…", notify: false);
            await ReportPdf.OpenAsync(this, BuildReportHtml(viewModel), stem);
            viewModel.SetStatus("Report opened as a PDF");
        }
        catch (Exception ex)
        {
            AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] Open as a PDF failed: {ex}{Environment.NewLine}");
            viewModel.SetStatus("The PDF could not be opened · " + ex.Message);
        }
    }

    // Menu design P3: three tabs on the left, one pane showing at a time.
    private void MenuTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button tab) ShowMenuTab(tab);
    }

    private void ShowMenuTab(Button open)
    {
        var tabs = new[] { (MenuTabBriefing, MenuPaneBriefing), (MenuTabShare, MenuPaneShare), (MenuTabSettings, MenuPaneSettings) };
        foreach (var (tab, pane) in tabs)
        {
            var isOpen = ReferenceEquals(tab, open);
            tab.Tag = isOpen ? "Active" : null;
            pane.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // The version chip's state, repeated in the menu's navy band.
    private void UpdateMenuVersionPill()
    {
        var current = "v" + UpdateService.Display(UpdateService.CurrentVersion);
        var (text, back, fore) = (VersionChip.Tag as string) switch
        {
            "UpToDate" => (current + " · up to date", "#EAF7EF", "#0B7A35"),
            "Available" => ("Update available · " + (VersionChip.Content?.ToString() ?? string.Empty), "#FFF1DC", "#9A5B00"),
            "CantCheck" => (current + " · can't check", "#EDF1F6", "#667891"),
            _ => (current, "#EDF1F6", "#004EA1")
        };
        MenuVersionText.Text = text;
        MenuVersionPill.Background = (Brush)new BrushConverter().ConvertFromString(back)!;
        MenuVersionText.Foreground = (Brush)new BrushConverter().ConvertFromString(fore)!;
    }

    // Export to Daily Briefing (Menu, Daily Briefing tab). One day only: the sheet is a daily form.
    private void UpdateDailyBriefingCardDetail()
    {
        if (DataContext is not MainViewModel viewModel) return;
        var range = viewModel.IsRangeMode;
        DailyBriefingDayText.Text = range
            ? "A date range is chosen"
            : (viewModel.SelectedDate ?? DateTime.Today).ToString("ddd dd/MM/yyyy");
        DailyBriefingExportButton.IsEnabled = !range;
        DailyBriefingNote.Text = range
            ? "The briefing is for one day · choose Single day in the calendar first"
            : "Opens a preview to print or save as PDF";
    }

    private async void ExportDailyBriefing_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        await ExportDailyBriefingAsync();
    }

    // Pulls every report the sheet uses - Team, Gift Cards, Branch (hourly), and Staff
    // Discounts - for the chosen day, then shows the sheet filled in. Not signed in: sign in
    // first, and the export carries on by itself afterwards, as Refresh does.
    private async Task ExportDailyBriefingAsync()
    {
        if (_flooidGrabBusy || DataContext is not MainViewModel viewModel) return;
        if (viewModel.IsRangeMode)
        {
            viewModel.SetStatus("The Daily Briefing is for one day · choose Single day in the calendar, then export it");
            return;
        }

        var loginWindow = GetOrCreateFlooidLoginWindow();
        if (!loginWindow.IsSignedIn)
        {
            _grabAfterFlooidSignIn = true;
            _grabReportsOnlyAfterSignIn = false;
            _briefingAfterSignIn = true;
            viewModel.SetFlooidStatus("Sign in to continue", "Working");
            viewModel.SetStatus("Sign in to Flooid, then the Daily Briefing will be filled in automatically");
            try { await loginWindow.EnsureOpenAsync(); }
            catch (Exception ex) { viewModel.SetStatus(ex.Message); }
            return;
        }

        // A failed pull leaves the retrieval box up with what went wrong; nothing opens.
        if (!await GrabFlooidReportsAsync(reportsOnly: false, forBriefing: true)) return;

        try
        {
            var data = DailyBriefingSheet.FromViewModel(viewModel);
            var preview = new DailyBriefingPreviewWindow(data) { Owner = this };
            preview.ShowDialog();
            var missing = DailyBriefingPreviewWindow.Missing(data);
            viewModel.SetStatus(preview.Outcome is null
                ? $"Daily Briefing for {data.Date:dd/MM/yyyy} closed without printing"
                : missing.Count == 0 ? preview.Outcome : $"{preview.Outcome} · left blank: {string.Join(", ", missing)}");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("The Daily Briefing could not be shown · " + ex.Message);
        }
    }

    // Owner, 21 Oct 2026 (concept V1): Print summary opens the same preview as the Daily
    // Briefing - whole page, zoom, page arrows - then Print or Save as PDF. The pages are A4
    // on screen and re-flowed to the printer's paper when printed, as they always were.
    private void PrintReport_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            var date = viewModel.SelectedDate ?? DateTime.Today;
            var preview = new DailyBriefingPreviewWindow(
                $"Print summary · {date:ddd dd/MM/yyyy}", $"Summary for {date:dd/MM/yyyy}",
                "Card Factory Profit & Loss Report", System.Printing.PageOrientation.Portrait,
                area => SummaryPages(viewModel, area ?? new Size(96 * 8.27, 96 * 11.69)))   // A4 on screen
            { Owner = this };
            preview.ShowDialog();
            viewModel.SetStatus(preview.Outcome ?? "Print summary closed without printing");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Print failed · " + ex.Message);
        }
    }

    private static DocumentPaginator SummaryPages(MainViewModel vm, Size area)
    {
        var document = BuildPrintableReport(vm);
        document.PageHeight = area.Height;
        document.PageWidth = area.Width;
        document.PagePadding = new Thickness(36);
        document.ColumnGap = 0;
        document.ColumnWidth = Math.Max(1, area.Width - 72);
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = area;
        paginator.ComputePageCount();
        return paginator;
    }

    private static string BuildReportHtml(MainViewModel vm)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var end = (vm.SelectedDate ?? DateTime.Today).Date;
        var weekly = string.Equals(vm.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase);
        var start = weekly ? (vm.RangeStartDate ?? end.AddDays(-6)).Date : end;
        var sb = new StringBuilder();
        // Polish 6 (owner, 15 Oct): the page opens with the app's logo band - smiley with its
        // bar badge, CARD FACTORY over Profit & Loss, store and dates on the right, a yellow
        // rule beneath. The smiley and the rounded font travel inside the file, so it looks
        // the same when emailed.
        var smiley = ResourceDataUri("Assets/HeaderSmileyFace.png", "image/png");
        var brandFont = ResourceDataUri("Assets/Fonts/Chewy-Regular.ttf", "font/ttf");
        var smallFont = ResourceDataUri("Assets/Fonts/Fredoka-SemiBold.ttf", "font/ttf");
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>Card Factory Profit &amp; Loss Report</title>");
        if (brandFont is not null) sb.Append($"<style>@font-face{{font-family:CfBrand;src:url({brandFont})}}</style>");
        if (smallFont is not null) sb.Append($"<style>@font-face{{font-family:CfSmall;src:url({smallFont})}}</style>");
        sb.Append("<style>.brand{background:#004EA1;border-radius:14px 14px 0 0;padding:14px 20px;display:flex;align-items:center;gap:14px}.mark{position:relative;width:56px;height:56px;flex:none}.mark img{width:56px;height:56px}.badge{position:absolute;right:-4px;bottom:-3px;width:22px;height:22px;border-radius:50%;background:#fff;border:2.5px solid #004EA1;display:flex;align-items:flex-end;justify-content:center;gap:2px;padding-bottom:5px;box-sizing:border-box}.badge i{width:3px;border-radius:2px;background:#004EA1;display:block}.badge i.y{background:#FFC907}.co{font-family:CfSmall,'Trebuchet MS',Arial,sans-serif;font-size:13px;color:#fff;line-height:1.1}.pl{font-family:CfBrand,'Trebuchet MS',Arial,sans-serif;font-size:30px;color:#fff;line-height:1.05}.calc{display:flex;align-items:center;gap:7px;margin-top:4px;font-family:CfSmall,Arial,sans-serif;font-size:11px;letter-spacing:3px;color:#fff}.calc i{flex:1;height:4px;border-radius:2px;background:#FFD100;display:block}.where{margin-left:auto;text-align:right;font-size:12px;color:#D6E4F5}.where b{color:#fff;font-size:14px}.rule{height:5px;background:#FFC907;border-radius:0 0 4px 4px}</style>");
        sb.Append("<style>body{font-family:Arial,sans-serif;color:#183153;margin:28px}h1{margin:0;color:#004EA1}h2{background:#004EA1;color:white;padding:9px 14px;border-radius:12px 12px 0 0;margin:24px 0 0}.meta{background:#FFF4A8;padding:10px 14px;border-radius:10px;margin-top:12px}.cards{display:flex;gap:8px;flex-wrap:wrap;margin:12px 0}.card{border:1px solid #D7E2EF;border-radius:10px;padding:8px 12px;min-width:125px}.label{font-size:10px;color:#60758E}.value{font-weight:bold;font-size:16px}table{width:100%;border-collapse:collapse;font-size:12px}th{background:#E3ECF7;color:#004EA1;text-align:left}th,td{padding:7px;border-bottom:1px solid #E4EBF2}tr.best{background:#FFF9D8}.yellow{color:#A87C00}td.flag{background:#FFF1DC;color:#9A5B00;font-weight:bold}.note{color:#667891;font-size:10px;margin-top:6px}.foot{margin-top:20px;color:#667891;font-size:10px}</style></head><body>");
        var dates = start == end ? $"{end:ddd dd/MM/yyyy}" : $"{start:dd/MM/yyyy} - {end:dd/MM/yyyy}";
        sb.Append("<div class='brand'><div class='mark'>");
        if (smiley is not null) sb.Append($"<img src='{smiley}' alt=''>");
        sb.Append("<span class='badge'><i style='height:5px'></i><i style='height:7px'></i><i class='y' style='height:9px'></i></span></div>");
        sb.Append($"<div><div class='co'>Card Factory</div><div class='pl'>Profit &amp; Loss</div><div class='calc'><i></i>CALCULATOR<i></i></div></div><div class='where'>{H(vm.StoreLocation)}<br><b>{H(dates)}</b></div></div><div class='rule'></div>");
        sb.Append("<div class='cards'>");
        sb.Append($"<div class='card'><div class='label'>COUNTED SALES</div><div class='value'>£{vm.TotalSales:N2}</div></div>");
        sb.Append($"<div class='card'><div class='label'>TRANSACTIONS</div><div class='value'>{vm.TotalTransactions:N0}</div></div>");
        sb.Append($"<div class='card'><div class='label'>COUNTED UNITS</div><div class='value'>{vm.TotalCountedUnits:N0}</div></div>");
        sb.Append($"<div class='card'><div class='label'>ABV</div><div class='value'>£{vm.ActualAbv:N2}</div></div>");
        sb.Append($"<div class='card'><div class='label'>AUB</div><div class='value'>{vm.ActualAub:N2}</div></div>");
        sb.Append("</div>");

        sb.Append("<h2>Team Performance</h2><table><thead><tr><th>Name</th><th>Sales</th><th>Gift Card Qty</th><th>Gift Card Sales</th><th>Transactions</th><th>Units</th><th>ABV</th><th>AUB</th></tr></thead><tbody>");
        foreach (var row in vm.Operators)
        {
            var input = row.ToInput();
            sb.Append($"<tr><td>{H(row.Name)}</td><td>£{input.Sales:N2}</td><td>{input.GiftCardQuantity}</td><td>£{input.GiftCardValue:N2}</td><td>{input.Transactions}</td><td>{input.Units}</td><td>£{row.Abv:N2}</td><td>{row.Aub:N2}</td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<h2>Branch Performance</h2><div class='cards'>");
        sb.Append($"<div class='card'><div class='label'>REPORT SALES</div><div class='value'>£{vm.BranchTotalSales:N2}</div></div>");
        sb.Append($"<div class='card'><div class='label'>TRANSACTIONS</div><div class='value'>{vm.BranchTotalTransactions:N0}</div></div>");
        sb.Append($"<div class='card'><div class='label'>UNITS</div><div class='value'>{vm.BranchTotalUnits:N0}</div></div>");
        sb.Append($"<div class='card'><div class='label'>ABV</div><div class='value'>£{vm.BranchActualAbv:N2}</div></div>");
        sb.Append($"<div class='card'><div class='label'>AUB</div><div class='value'>{vm.BranchActualAub:N2}</div></div>");
        sb.Append($"<div class='card'><div class='label'>GIFT CARD QTY</div><div class='value'>{vm.BranchGiftCardQuantity:N0}</div></div>");
        sb.Append($"<div class='card'><div class='label'>GIFT CARD SALES</div><div class='value'>£{vm.BranchGiftCardSales:N2}</div></div></div>");
        sb.Append("<table><thead><tr><th>Time Band</th><th>Sales</th><th>Transactions</th><th>Units</th><th>ABV</th><th>AUB</th><th>% Sales</th></tr></thead><tbody>");
        foreach (var row in vm.BranchHours)
            sb.Append($"<tr class='{(row.IsStrongestHour ? "best" : "")}'><td>{H(row.TimeBand)}</td><td>£{row.Sales:N2}</td><td>{row.Transactions}</td><td>{row.Units}</td><td>£{row.Abv:N2}</td><td>{row.Aub:N2}</td><td>{row.PercentOfSales:N2}%</td></tr>");
        sb.Append("</tbody></table>");

        sb.Append("<h2>Gift Cards</h2><table><thead><tr><th>Gift Card Qty</th><th>Gift Card Sales</th><th>Card / Item</th><th>Operator</th></tr></thead><tbody>");
        foreach (var row in vm.BranchGiftCards.Where(r => r.Quantity > 0 || r.Sales > 0 || !string.IsNullOrWhiteSpace(r.Item)))
            sb.Append($"<tr><td>{row.Quantity}</td><td>£{row.Sales:N2}</td><td>{H(row.Item)}</td><td>{H(row.OperatorName)}</td></tr>");
        sb.Append("</tbody></table>");

        // Reports: voids, returns, no sales and staff discounts, as the Reports section shows
        // them, with £20+ highlighted. Only for the date shown - the section is cleared when
        // the date changes - so an export never carries another day's figures.
        sb.Append("<h2>Reports</h2>");
        if (!vm.HasReportData)
        {
            sb.Append("<p class='note'>Not generated for this date. Press Generate reports in the Reports section, then export again.</p>");
        }
        else
        {
            sb.Append("<div class='cards'>");
            sb.Append($"<div class='card'><div class='label'>VOIDS</div><div class='value'>£{vm.ReportVoidsValue:N2}</div><div class='label'>{vm.ReportVoidsQuantity} items</div></div>");
            sb.Append($"<div class='card'><div class='label'>RETURNS</div><div class='value'>£{vm.ReportRefundsValue:N2}</div><div class='label'>{vm.ReportRefundsQuantity} refunds</div></div>");
            sb.Append($"<div class='card'><div class='label'>NO SALES</div><div class='value'>{vm.ReportNoSales}</div><div class='label'>drawer opens</div></div>");
            sb.Append($"<div class='card'><div class='label'>STAFF DISCOUNT</div><div class='value'>{H(vm.ReportStaffDiscountText)}</div><div class='label'>{H(vm.ReportStaffDiscountDetail)}</div></div></div>");
            sb.Append("<table><thead><tr><th>Name</th><th>Voids Qty</th><th>Voids Value</th><th>Returns Qty</th><th>Returns Value</th><th>No Sales</th><th>Staff Discount</th></tr></thead><tbody>");
            foreach (var row in vm.ReportRows)
            {
                sb.Append($"<tr><td>{H(row.Name)}</td><td>{row.VoidsQuantity}</td><td{(row.VoidsFlagged ? " class='flag'" : "")}>£{row.VoidsValue:N2}</td>");
                sb.Append($"<td>{row.RefundsQuantity}</td><td{(row.RefundsFlagged ? " class='flag'" : "")}>£{row.RefundsValue:N2}</td><td>{row.NoSales}</td><td>{H(row.StaffDiscount)}</td></tr>");
            }
            sb.Append("</tbody></table>");
            sb.Append($"<div class='note'>£{MainViewModel.ReportFlagValue:N2}+ highlighted to look into · voids include transaction and line voids</div>");
        }
        sb.Append($"<div class='foot'>Flooid: {H(vm.FlooidStatusText)} · {H(vm.FlooidLastUpdatedText)}. Gift-card exclusions are applied only to the P&amp;L counted figures; Branch Performance remains the Flooid report values.</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static FlowDocument BuildPrintableReport(MainViewModel vm)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Arial"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x31, 0x53)) };
        var end = (vm.SelectedDate ?? DateTime.Today).Date;
        var start = string.Equals(vm.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase)
            ? (vm.RangeStartDate ?? end.AddDays(-6)).Date
            : end;
        // Polish 6: the same logo band as the app and the exported page.
        doc.Blocks.Add(new BlockUIContainer(BuildPrintBrandBand(vm.StoreLocation,
            start == end ? $"{end:ddd dd/MM/yyyy}" : $"{start:dd/MM/yyyy} - {end:dd/MM/yyyy}")) { Margin = new Thickness(0, 0, 0, 10) });
        doc.Blocks.Add(new Paragraph(new Run($"Counted Sales £{vm.TotalSales:N2}    Transactions {vm.TotalTransactions:N0}    Counted Units {vm.TotalCountedUnits:N0}    ABV £{vm.ActualAbv:N2}    AUB {vm.ActualAub:N2}")) { FontWeight = FontWeights.Bold });
        doc.Blocks.Add(MakePrintHeading("Team Performance"));
        doc.Blocks.Add(MakePrintTable(new[] { "Name", "Sales", "GC Qty", "GC Sales", "Trans.", "Units", "ABV", "AUB" },
            vm.Operators.Select(r => { var i = r.ToInput(); return new[] { r.Name, $"£{i.Sales:N2}", i.GiftCardQuantity.ToString(), $"£{i.GiftCardValue:N2}", i.Transactions.ToString(), i.Units.ToString(), $"£{r.Abv:N2}", $"{r.Aub:N2}" }; })));
        doc.Blocks.Add(MakePrintHeading("Branch Performance"));
        doc.Blocks.Add(new Paragraph(new Run($"Sales £{vm.BranchTotalSales:N2}    Transactions {vm.BranchTotalTransactions:N0}    Units {vm.BranchTotalUnits:N0}    ABV £{vm.BranchActualAbv:N2}    AUB {vm.BranchActualAub:N2}    Gift Cards {vm.BranchGiftCardQuantity:N0} / £{vm.BranchGiftCardSales:N2}")) { FontWeight = FontWeights.Bold });
        doc.Blocks.Add(MakePrintTable(new[] { "Time", "Sales", "Trans.", "Units", "ABV", "AUB", "% Sales" },
            vm.BranchHours.Select(r => new[] { r.TimeBand, $"£{r.Sales:N2}", r.Transactions.ToString(), r.Units.ToString(), $"£{r.Abv:N2}", $"{r.Aub:N2}", $"{r.PercentOfSales:N2}%" })));
        doc.Blocks.Add(MakePrintHeading("Gift Cards"));
        doc.Blocks.Add(MakePrintTable(new[] { "Qty", "Sales", "Card / Item", "Operator" },
            vm.BranchGiftCards.Where(r => r.Quantity > 0 || r.Sales > 0 || !string.IsNullOrWhiteSpace(r.Item)).Select(r => new[] { r.Quantity.ToString(), $"£{r.Sales:N2}", r.Item, r.OperatorName })));

        // Reports, as on screen: £20+ voids and returns highlighted.
        doc.Blocks.Add(MakePrintHeading("Reports"));
        if (!vm.HasReportData)
        {
            doc.Blocks.Add(new Paragraph(new Run("Not generated for this date. Press Generate reports in the Reports section, then print again.")) { Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x78, 0x91)) });
        }
        else
        {
            doc.Blocks.Add(new Paragraph(new Run($"Voids £{vm.ReportVoidsValue:N2} ({vm.ReportVoidsQuantity} items)    Returns £{vm.ReportRefundsValue:N2} ({vm.ReportRefundsQuantity} refunds)    No sales {vm.ReportNoSales}    Staff discount {vm.ReportStaffDiscountText} ({vm.ReportStaffDiscountDetail})")) { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(MakePrintTable(new[] { "Name", "Voids Qty", "Voids Value", "Returns Qty", "Returns Value", "No Sales", "Staff Discount" },
                vm.ReportRows.Select(r => new (string, bool)[]
                {
                    (r.Name, false), (r.VoidsQuantity.ToString(), false), ($"£{r.VoidsValue:N2}", r.VoidsFlagged),
                    (r.RefundsQuantity.ToString(), false), ($"£{r.RefundsValue:N2}", r.RefundsFlagged),
                    (r.NoSales.ToString(), false), (r.StaffDiscount, false)
                })));
            doc.Blocks.Add(new Paragraph(new Run($"£{MainViewModel.ReportFlagValue:N2}+ highlighted to look into · voids include transaction and line voids")) { FontSize = 8.5, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x78, 0x91)), Margin = new Thickness(0, 4, 0, 0) });
        }
        return doc;
    }

    // Polish 6: a packed resource as a data: URI, so an exported page carries it inside.
    private static string? ResourceDataUri(string path, string mime)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/" + path));
            if (info is null) return null;
            using var stream = info.Stream;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return $"data:{mime};base64,{Convert.ToBase64String(copy.ToArray())}";
        }
        catch
        {
            return null;   // the page still exports, just without the picture or font
        }
    }

    // Polish 6: the printout's logo band - navy, smiley with its bar badge, CARD FACTORY over
    // Profit & Loss, store and dates on the right, and a yellow rule beneath.
    private static UIElement BuildPrintBrandBand(string? store, string dates)
    {
        var navy = new SolidColorBrush(Color.FromRgb(0x00, 0x4E, 0xA1));
        var yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x07));
        var pale = new SolidColorBrush(Color.FromRgb(0xD6, 0xE4, 0xF5));

        var mark = new Canvas { Width = 50, Height = 50, Margin = new Thickness(0, 0, 14, 0) };
        mark.Children.Add(new Image { Width = 46, Height = 46, Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/HeaderSmileyFace.png")) });
        var badge = new System.Windows.Shapes.Ellipse { Width = 20, Height = 20, Fill = Brushes.White, Stroke = navy, StrokeThickness = 2 };
        Canvas.SetLeft(badge, 30); Canvas.SetTop(badge, 30); mark.Children.Add(badge);
        foreach (var (x, h, fill) in new (double, double, Brush)[] { (35.5, 5.0, navy), (39.0, 7.0, navy), (42.5, 9.0, yellow) })
        {
            var bar = new System.Windows.Shapes.Rectangle { Width = 2.5, Height = h, RadiusX = 1, RadiusY = 1, Fill = fill };
            Canvas.SetLeft(bar, x); Canvas.SetTop(bar, 45 - h); mark.Children.Add(bar);
        }

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // Title Y1: small "Card Factory", "Profit & Loss" in Chewy, CALCULATOR between two yellow bars.
        var chewy = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Chewy");
        var small = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Fredoka SemiBold");
        name.Children.Add(new TextBlock { Text = "Card Factory", FontFamily = small, FontSize = 10, Foreground = Brushes.White });
        name.Children.Add(new TextBlock { Text = "Profit & Loss", FontFamily = chewy, FontSize = 24, Foreground = Brushes.White, Margin = new Thickness(0, -2, 0, 0) });
        var calc = new Grid { Margin = new Thickness(0, 1, 0, 0), Width = 122, HorizontalAlignment = HorizontalAlignment.Left };   // star columns need a set width inside a StackPanel
        calc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        calc.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        calc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var barLeft = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x00)), VerticalAlignment = VerticalAlignment.Center };
        var barRight = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x00)), VerticalAlignment = VerticalAlignment.Center };
        var calcText = new TextBlock { Text = "C A L C U L A T O R", FontFamily = small, FontSize = 7.5, Foreground = Brushes.White, Margin = new Thickness(6, 0, 6, 0) };
        Grid.SetColumn(calcText, 1); Grid.SetColumn(barRight, 2);
        calc.Children.Add(barLeft); calc.Children.Add(calcText); calc.Children.Add(barRight);
        name.Children.Add(calc);

        var where = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        where.Children.Add(new TextBlock { Text = store ?? string.Empty, FontSize = 10, Foreground = pale, HorizontalAlignment = HorizontalAlignment.Right });
        where.Children.Add(new TextBlock { Text = dates, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(mark);
        Grid.SetColumn(name, 1); row.Children.Add(name);
        Grid.SetColumn(where, 2); row.Children.Add(where);

        var band = new StackPanel();
        band.Children.Add(new Border { Background = navy, CornerRadius = new CornerRadius(10, 10, 0, 0), Padding = new Thickness(14, 10, 16, 10), Child = row });
        band.Children.Add(new Border { Background = yellow, Height = 4, CornerRadius = new CornerRadius(0, 0, 3, 3) });
        return band;
    }

    private static Paragraph MakePrintHeading(string text) => new(new Run(text))
    {
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(0x00, 0x4E, 0xA1)),   // CfDeepBlue, as the app
        Padding = new Thickness(7),
        Margin = new Thickness(0, 12, 0, 0)
    };

    private static Table MakePrintTable(IReadOnlyList<string> headers, IEnumerable<string[]> rows) =>
        MakePrintTable(headers, rows.Select(values => values.Select(v => (v, false)).ToArray()));

    /// <summary>A table whose cells can be highlighted (the Reports £20+ figures).</summary>
    private static Table MakePrintTable(IReadOnlyList<string> headers, IEnumerable<(string Text, bool Flag)[]> rows)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0xE2, 0xEF)), BorderThickness = new Thickness(0.5) };
        for (var i = 0; i < headers.Count; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        var header = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xEC, 0xF7)) };   // CfBlueSoft
        foreach (var text in headers) header.Cells.Add(new TableCell(new Paragraph(new Run(text)) { FontWeight = FontWeights.Bold, Margin = new Thickness(2) }) { Padding = new Thickness(3) });
        group.Rows.Add(header);
        foreach (var values in rows)
        {
            var row = new TableRow();
            foreach (var (text, flag) in values)
            {
                var paragraph = new Paragraph(new Run(text ?? string.Empty)) { Margin = new Thickness(2) };
                var cell = new TableCell(paragraph) { Padding = new Thickness(3) };
                if (flag)
                {
                    cell.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xDC));
                    paragraph.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x5B, 0x00));
                    paragraph.FontWeight = FontWeights.Bold;
                }
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private void OpenAccountMenu()
    {
        var store = (DataContext as MainViewModel)?.StoreLocation;
        AccountMenuStore.Text = string.IsNullOrWhiteSpace(store) ? "Flooid" : store;

        var user = FlooidLoginWindow.RememberedUsername;
        AccountMenuUser.Text = string.IsNullOrWhiteSpace(user) ? "signed in" : "signed in as " + user;

        // Nothing to forget, nothing to offer.
        AccountForgetButton.IsEnabled = CredentialStore.Exists || !string.IsNullOrWhiteSpace(user)
                                        || FlooidLoginWindow.RememberedStoreCode is not null;
        AccountChangeStoreButton.Visibility = FlooidLoginWindow.AccountHasSeveralStores ? Visibility.Visible : Visibility.Collapsed;
        FlooidAccountMenu.IsOpen = true;
    }

    // Stage 6B.50
    private async void AccountChangeStore_Click(object sender, RoutedEventArgs e)
    {
        FlooidAccountMenu.IsOpen = false;
        try
        {
            ForgetPulledReports();
            var result = await GetOrCreateFlooidLoginWindow().ChangeStoreAsync();
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.LastUpdatedAt = null;
                viewModel.ClearActivityReport();
                viewModel.SetStatus(result.StartsWith("ERROR", StringComparison.Ordinal)
                    ? "Change store did not complete · " + result
                    : "Signed out · sign in and choose a store");
            }
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel viewModel) viewModel.SetStatus(ex.Message);
        }
    }

    private async void AccountSignOut_Click(object sender, RoutedEventArgs e)
    {
        FlooidAccountMenu.IsOpen = false;
        ForgetPulledReports();
        var result = await GetOrCreateFlooidLoginWindow().SignOutAsync();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetStatus(result.StartsWith("ERROR", StringComparison.Ordinal)
                ? "Sign out did not complete · " + result
                : "Signed out of Flooid");
            viewModel.LastUpdatedAt = null;
                viewModel.ClearActivityReport();
        }
    }

    private void AccountForget_Click(object sender, RoutedEventArgs e)
    {
        FlooidAccountMenu.IsOpen = false;
        FlooidLoginWindow.ForgetSavedSignIn();
        if (DataContext is MainViewModel viewModel)
            viewModel.SetStatus("Saved sign-in forgotten · you stay signed in until you sign out");
    }

    private async void AccountOpenFlooid_Click(object sender, RoutedEventArgs e)
    {
        FlooidAccountMenu.IsOpen = false;
        try { await GetOrCreateFlooidLoginWindow().OpenBackOfficeAsync(); }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel viewModel) viewModel.SetStatus(ex.Message);
        }
    }

    private async void FlooidConnect_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = GetOrCreateFlooidLoginWindow();

        // Stage 6B.27: connected, the pill opens the account menu rather than the Flooid
        // window. The window is still one click away inside it.
        if (loginWindow.IsSignedIn)
        {
            OpenAccountMenu();
            return;
        }

        try
        {
            await loginWindow.EnsureOpenAsync();
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SetFlooidStatus("Flooid unavailable", "Bad");
                viewModel.SetStatus(ex.Message);
            }
        }
    }

    private FlooidLoginWindow GetOrCreateFlooidLoginWindow()
    {
        if (_flooidLoginWindow is not null) return _flooidLoginWindow;
        var loginWindow = new FlooidLoginWindow { Owner = this };
        loginWindow.ConnectionStatusChanged += FlooidLoginWindow_ConnectionStatusChanged;
        _flooidLoginWindow = loginWindow;
        return loginWindow;
    }

    // Stage 6B.14: restored. This helper sat between the two per-section refresh methods
    // removed in 6B.11, so cutting from one method's opening to the next method's opening
    // took it with them. It is still used by the combined grab, which is what the build
    // objected to: CS0103 on line 900. Anchor a deletion on the method's own end, not on
    // whatever happens to be declared next.
    // Stage 6B.15: the DateTime overload, which is the one the single call site needs -
    // BranchPerformanceReportParseResult declares FromDate and ToDate as DateTime.
    //
    // There were two overloads before 6B.11, one DateOnly and one DateTime. 6B.11 removed
    // both, 6B.14 restored only the DateOnly one, and the build has been red ever since:
    //     MainWindow.xaml.cs(914,59): cannot convert from 'DateTime' to 'DateOnly'
    // The DateOnly overload had no caller left, so it is not restored - putting back an
    // unused overload to sit beside the used one is how this went wrong in the first place.
    private static string DescribeFlooidPeriod(DateTime from, DateTime to, DateTime requestedStart, DateTime requestedEnd)
    {
        var effectiveFrom = from == default ? requestedStart : from;
        var effectiveTo = to == default ? requestedEnd : to;
        return effectiveFrom.Date == effectiveTo.Date
            ? effectiveFrom.ToString("dd/MM/yyyy")
            : $"{effectiveFrom:dd/MM/yyyy} - {effectiveTo:dd/MM/yyyy}";
    }

    private async void GrabFromFlooid_Click(object sender, RoutedEventArgs e)
    {
        if (_flooidGrabBusy || DataContext is not MainViewModel viewModel) return;
        var loginWindow = GetOrCreateFlooidLoginWindow();
        if (!loginWindow.IsSignedIn)
        {
            _grabAfterFlooidSignIn = true;
            // The pull that follows sign-in is the one this button asked for: from Reports,
            // Generate reports; from Team or Branch, the full Refresh.
            _grabReportsOnlyAfterSignIn = _reportsActive;
            _briefingAfterSignIn = false;
            viewModel.SetFlooidStatus("Sign in to continue", "Working");
            viewModel.SetStatus("Sign in to Flooid, then the reports will be retrieved automatically");
            try { await loginWindow.EnsureOpenAsync(); }
            catch (Exception ex) { viewModel.SetStatus(ex.Message); }
            return;
        }

        // Refresh always pulls fresh; Generate reports (the same button on Reports) uses
        // what was pulled a moment ago, by the rules in ReportCache.
        await GrabFlooidReportsAsync(fresh: !_reportsActive);
    }

    /// <param name="reportsOnly">True: only what the Reports section shows (Refunds/Voids and
    /// Discounts and Price Overrides). Null: whatever the section on screen needs.</param>
    /// <param name="forBriefing">Export to Daily Briefing: the full pull plus Staff Discounts,
    /// whichever section is showing.</param>
    /// <returns>True when every report asked for came back (the full pull only).</returns>
    /// <param name="fresh">Refresh: pull every report from Flooid even if a recent copy is held.</param>
    private async Task<bool> GrabFlooidReportsAsync(bool? reportsOnly = null, bool forBriefing = false, bool fresh = false)
    {
        if (_flooidGrabBusy || DataContext is not MainViewModel viewModel) return false;
        var loginWindow = GetOrCreateFlooidLoginWindow();
        if (!loginWindow.IsSignedIn) return false;
        _pullFresh = fresh;
        _oldestPullUsed = null;

        if (!forBriefing && (reportsOnly ?? _reportsActive))
        {
            await GenerateReportsAsync(viewModel, loginWindow);
            return false;
        }

        var succeeded = false;

        _flooidGrabBusy = true;
        // Stage 6A.93: remember which section is showing. Both Refresh buttons run this
        // now, so forcing the Branch view at the end would throw you out of Team
        // Performance every time you refreshed from there.
        var wasBranchView = _branchPerformanceActive;
        var wasReportsView = _reportsActive;
        var wasGiftCardsView = _branchGiftCardsActive;
        // Staff discounts come with the full pull from Reports, and always for the Daily Briefing.
        var includeDiscounts = wasReportsView || forBriefing;
        try
        {
            var endDate = (viewModel.SelectedDate ?? DateTime.Today).Date;
            var weekly = string.Equals(viewModel.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase);
            // Stage 6A.75: Date Range is an explicit From/To range, not a fixed seven
            // days back from the selected date. Stage 6A.89: normalised first, so an
            // inverted range can never be sent to Flooid whatever produced it.
            viewModel.NormaliseDateRange();
            var startDate = weekly ? (viewModel.RangeStartDate ?? endDate.AddDays(-6)).Date : endDate;
            viewModel.SetStatus($"Retrieving Flooid reports · {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}");

            // Stage 6B.11: the overlay replaces the two section labels 6A.94 was setting.
            RetrievalStep4Row.Visibility = includeDiscounts ? Visibility.Visible : Visibility.Collapsed;
            RetrievalStepStampsRow.Visibility = includeDiscounts ? Visibility.Visible : Visibility.Collapsed;
            LastYearTargetSettings.Load();
            var fillFromLastYear = LastYearTargetSettings.Enabled;
            RetrievalStep5Row.Visibility = fillFromLastYear ? Visibility.Visible : Visibility.Collapsed;
            ShowRetrievalOverlay();
            if (forBriefing) RetrievalTitle.Text = "Getting the Daily Briefing ready";

            RefundReportParseResult? refunds = null;
            GiftCardReportParseResult? giftCards = null;
            string? teamError = null;
            string? giftError = null;
            string? branchError = null;
            var teamUpdated = false;
            var giftUpdated = false;
            var branchUpdated = false;
            string? branchActualPeriod = null;

            try
            {
                viewModel.SetFlooidStatus("Retrieving Team Performance…", "Working");
                SetRetrievalStep(1);
                var refundsHtml = await PullAsync("Refunds", startDate, endDate, () => loginWindow.FetchRefundsVoidsHtmlAsync(startDate, endDate));
                refunds = new RefundsVoidsReportParser().Parse(refundsHtml);
                viewModel.ApplyActivityReport(refunds.Activity);   // Reports section: same report, no extra step
            }
            catch (Exception ex)
            {
                teamError = ex.Message;
            }

            try
            {
                viewModel.SetFlooidStatus("Retrieving Gift Cards…", "Working");
                SetRetrievalStep(2);
                var giftHtml = await PullAsync("GiftCards", startDate, endDate, () => loginWindow.FetchGiftCardHtmlAsync(startDate, endDate));
                giftCards = new GiftCardReportParser().Parse(giftHtml);
            }
            catch (Exception ex)
            {
                giftError = ex.Message;
            }

            if (giftCards is not null)
            {
                // Branch Gift Card detail is useful independently. Only touch Team Gift Card
                // entries here if the full Team merge succeeds below.
                viewModel.ApplyGiftCardReport(giftCards, updateTeam: false);
                giftUpdated = true;
            }

            if (refunds is not null && giftCards is not null)
            {
                try
                {
                    var mergedOperators = new OperatorReportMerger().Merge(refunds, giftCards);
                    viewModel.ApplyFlooidTeamReport(mergedOperators);
                    teamUpdated = true;
                }
                catch (Exception ex)
                {
                    teamError = ex.Message;
                }
            }
            else if (refunds is not null && giftCards is null && string.IsNullOrWhiteSpace(teamError))
            {
                teamError = "Gift Cards were not available, so Team figures were not replaced with incomplete data.";
            }

            try
            {
                viewModel.SetFlooidStatus("Retrieving Branch Performance…", "Working");
                SetRetrievalStep(3);
                // Stage 6A.75: a single day, for the same reason as the Branch refresh.
                var branchHtml = await PullAsync("Branch", endDate, endDate, () => loginWindow.FetchBranchPerformanceHtmlAsync(endDate, endDate));
                var branch = new BranchPerformanceReportParser().Parse(branchHtml);
                viewModel.ApplyBranchPerformance(branch);
                branchActualPeriod = DescribeFlooidPeriod(branch.FromDate, branch.ToDate, startDate, endDate);
                branchUpdated = true;
                // Stage 6A.93: the view is no longer switched here; it is restored below.
            }
            catch (Exception ex)
            {
                branchError = ex.Message;
                viewModel.SetBranchDataMessage("Branch Performance retrieval failed · " + ex.Message);
            }

            // Staff discounts for the Reports section, from Discounts and Price Overrides.
            // Pulled only when Refresh is pressed from the Reports section. Run last so a
            // problem with it cannot get in the way of the three reports above; a failure
            // shows the figures as "—" rather than keeping another day's.
            string? discountsError = null;
            var discountsUpdated = false;
            var period = (startDate, endDate);
            if (includeDiscounts)
            {
                try
                {
                    viewModel.SetFlooidStatus("Retrieving Staff Discounts…", "Working");
                    SetRetrievalStep(4);
                    var discountsHtml = await PullAsync("Discounts", startDate, endDate, () => loginWindow.FetchDiscountsHtmlAsync(startDate, endDate));
                    var discounts = new DiscountsReportParser().Parse(discountsHtml);
                    viewModel.ApplyStaffDiscounts(discounts.StaffDiscountLines);
                    _staffDiscountPeriod = period;
                    discountsUpdated = true;
                }
                catch (Exception ex)
                {
                    discountsError = ex.Message;
                    viewModel.ApplyStaffDiscounts(null);
                    _staffDiscountPeriod = null;
                }
            }
            else if (_staffDiscountPeriod != period)
            {
                // Not pulled this time, and what is held is for another day or range.
                viewModel.ApplyStaffDiscounts(null);
                viewModel.ApplyStampsSold(null);
                _staffDiscountPeriod = null;
            }
            var discountsOk = !includeDiscounts || discountsUpdated;

            // Stamps sold travel with Staff Discounts: Reports and the Daily Briefing.
            var stampsNote = includeDiscounts ? await PullStampsAsync(viewModel, loginWindow, startDate, endDate) : null;

            // Targets from last year. Last, after everything the screen shows, so it can never
            // get in the way of today's figures; a problem only leaves the targets as they were.
            string? lastYearNote = null;
            if (fillFromLastYear)
            {
                viewModel.SetFlooidStatus("Retrieving last year's figures…", "Working");
                SetRetrievalStep(6);
                lastYearNote = await FillTargetsFromLastYearAsync(viewModel, loginWindow, startDate, endDate);
            }

            if (teamUpdated || giftUpdated || branchUpdated)
                viewModel.SetFlooidLastUpdated(_oldestPullUsed ?? DateTime.Now);

            var errors = new List<string>();
            if (!teamUpdated && !string.IsNullOrWhiteSpace(teamError)) errors.Add("Team: " + teamError);
            if (!giftUpdated && !string.IsNullOrWhiteSpace(giftError)) errors.Add("Gift Cards: " + giftError);
            if (!branchUpdated && !string.IsNullOrWhiteSpace(branchError)) errors.Add("Branch: " + branchError);
            if (!discountsUpdated && !string.IsNullOrWhiteSpace(discountsError)) errors.Add("Staff Discounts: " + discountsError);

            // Stage 6B.11: the overlay carries the outcome now. On success it closes; on
            // any failure it stays up with the detail, because the labels that used to
            // report it are gone and an error with nowhere to appear is worse than a
            // cluttered header.
            succeeded = teamUpdated && giftUpdated && branchUpdated && discountsOk;
            if (succeeded) HideRetrievalOverlay();
            else ShowRetrievalError(string.Join(Environment.NewLine, errors));

            // Stage 6B.21: the header carries the retrieval time now.
            if (teamUpdated || branchUpdated || giftUpdated)
                viewModel.LastUpdatedAt = _oldestPullUsed ?? DateTime.Now;   // header shows "updated … ago" (design 1A); a reused copy keeps its own time

            if (teamUpdated && giftUpdated && branchUpdated && discountsOk)
            {
                viewModel.SetFlooidStatus("Updated", "Good");
                viewModel.SetStatus((branchActualPeriod is null
                    ? "Team Performance, Branch Performance and Gift Cards updated from Flooid"
                    : $"Team Performance, Branch Performance and Gift Cards updated from Flooid · report period {branchActualPeriod}")
                    + (lastYearNote is null ? string.Empty : " · Targets: " + lastYearNote));
            }
            else if (teamUpdated || giftUpdated || branchUpdated)
            {
                viewModel.SetFlooidStatus("Partial update", "Bad");
                viewModel.SetStatus("Flooid partial update · " + string.Join(" | ", errors));
            }
            else
            {
                viewModel.SetFlooidStatus("Retrieval failed", "Bad");
                viewModel.SetStatus("Flooid retrieval failed · " + string.Join(" | ", errors));
            }
            if (stampsNote is not null) viewModel.SetStatus(viewModel.StatusText + " · " + stampsNote);
        }
        catch (Exception ex)
        {
            viewModel.SetFlooidStatus("Retrieval failed", "Bad");
            viewModel.SetStatus("Flooid retrieval failed · " + ex.Message);
            // Stage 6B.11: a throw here would otherwise leave the overlay spinning for
            // ever, which reads as a hang rather than a failure.
            ShowRetrievalError(ex.Message);
        }
        finally
        {
            _flooidGrabBusy = false;
            _pullFresh = false;
            // Stage 6A.93: back to whichever section was showing when this started.
            SetBranchPerformanceView(wasBranchView, wasReportsView);
            SetBranchDetailView(wasGiftCardsView);
        }
        return succeeded;
    }

    // Generate reports (the Reports section's button): only the two reports that section
    // shows - Refunds/Voids for voids, returns and no sales, and Discounts and Price
    // Overrides for staff discounts. Gift Cards and Branch Performance are not pulled, and
    // Team, Gift Card and Branch figures and both "updated" times are left as they were:
    // Reports are separate from them.
    private async Task GenerateReportsAsync(MainViewModel viewModel, FlooidLoginWindow loginWindow)
    {
        _flooidGrabBusy = true;
        var wasBranchView = _branchPerformanceActive;
        var wasReportsView = _reportsActive;
        var wasGiftCardsView = _branchGiftCardsActive;
        try
        {
            var endDate = (viewModel.SelectedDate ?? DateTime.Today).Date;
            var weekly = string.Equals(viewModel.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase);
            viewModel.NormaliseDateRange();
            var startDate = weekly ? (viewModel.RangeStartDate ?? endDate.AddDays(-6)).Date : endDate;
            viewModel.SetStatus($"Generating reports · {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}");

            ShowRetrievalOverlay(reportsOnly: true);

            string? activityError = null;
            string? discountsError = null;
            var activityUpdated = false;
            var discountsUpdated = false;

            try
            {
                viewModel.SetFlooidStatus("Retrieving voids, returns and no sales…", "Working");
                SetRetrievalStep(1);
                var refundsHtml = await PullAsync("Refunds", startDate, endDate, () => loginWindow.FetchRefundsVoidsHtmlAsync(startDate, endDate));
                var refunds = new RefundsVoidsReportParser().Parse(refundsHtml);
                viewModel.ApplyActivityReport(refunds.Activity);
                activityUpdated = true;
            }
            catch (Exception ex)
            {
                activityError = ex.Message;
            }

            // A failure shows the staff discounts as "—" rather than keeping another day's.
            try
            {
                viewModel.SetFlooidStatus("Retrieving Staff Discounts…", "Working");
                SetRetrievalStep(4);
                var discountsHtml = await PullAsync("Discounts", startDate, endDate, () => loginWindow.FetchDiscountsHtmlAsync(startDate, endDate));
                var discounts = new DiscountsReportParser().Parse(discountsHtml);
                viewModel.ApplyStaffDiscounts(discounts.StaffDiscountLines);
                _staffDiscountPeriod = (startDate, endDate);
                discountsUpdated = true;
            }
            catch (Exception ex)
            {
                discountsError = ex.Message;
                viewModel.ApplyStaffDiscounts(null);
                _staffDiscountPeriod = null;
            }

            var stampsNote = await PullStampsAsync(viewModel, loginWindow, startDate, endDate);

            var errors = new List<string>();
            if (!activityUpdated && !string.IsNullOrWhiteSpace(activityError)) errors.Add("Voids, returns and no sales: " + activityError);
            if (!discountsUpdated && !string.IsNullOrWhiteSpace(discountsError)) errors.Add("Staff Discounts: " + discountsError);

            if (activityUpdated && discountsUpdated)
            {
                HideRetrievalOverlay();
                viewModel.SetFlooidStatus("Updated", "Good");
                viewModel.SetStatus("Reports generated from Flooid · voids, returns, no sales and staff discounts");
            }
            else
            {
                ShowRetrievalError(string.Join(Environment.NewLine, errors));
                viewModel.SetFlooidStatus(activityUpdated || discountsUpdated ? "Partial update" : "Retrieval failed", "Bad");
                viewModel.SetStatus((activityUpdated || discountsUpdated ? "Reports partly generated · " : "Reports could not be generated · ")
                    + string.Join(" | ", errors));
            }
            if (stampsNote is not null) viewModel.SetStatus(viewModel.StatusText + " · " + stampsNote);
        }
        catch (Exception ex)
        {
            viewModel.SetFlooidStatus("Retrieval failed", "Bad");
            viewModel.SetStatus("Reports could not be generated · " + ex.Message);
            ShowRetrievalError(ex.Message);
        }
        finally
        {
            _flooidGrabBusy = false;
            _pullFresh = false;
            SetBranchPerformanceView(wasBranchView, wasReportsView);
            SetBranchDetailView(wasGiftCardsView);
        }
    }

    // Stage 6B.11: the retrieval overlay. Three steps, marked pending, running or done,
    // so a twelve second grab shows progress rather than an undifferentiated spinner -
    // and a hang is visible as the step it hangs on.
    private const string StepPending = "\u25CB";   // hollow circle
    private const string StepRunning = "\u25CF";   // filled circle
    private const string StepDone    = "\u2713";   // tick

    private void StartRetrievalSpinner()
    {
        // Stage 6B.12: started here instead of from a XAML EventTrigger. It repeats
        // forever and is left running - the overlay is collapsed almost all the time, and
        // a hidden animation on one transform costs nothing.
        var spin = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(0.9)),
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        RetrievalSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void ShowRetrievalOverlay(bool reportsOnly = false)
    {
        StartRetrievalSpinner();
        // Generate reports shows just its own two steps: the Refunds/Voids report (step 1,
        // relabelled) and Staff Discounts (step 4). Gift Cards and Branch are not pulled.
        RetrievalStep1Text.Text = reportsOnly ? "Voids, returns and no sales" : "Team Performance";
        RetrievalStep2Row.Visibility = reportsOnly ? Visibility.Collapsed : Visibility.Visible;
        RetrievalStep3Row.Visibility = reportsOnly ? Visibility.Collapsed : Visibility.Visible;
        if (reportsOnly) RetrievalStep4Row.Visibility = Visibility.Visible;
        if (reportsOnly) RetrievalStepStampsRow.Visibility = Visibility.Visible;
        if (reportsOnly) RetrievalStep5Row.Visibility = Visibility.Collapsed;
        RetrievalTitle.Text = reportsOnly ? "Generating reports" : "Getting reports";
        RetrievalSubtitle.Text = DataContext is MainViewModel periodViewModel ? periodViewModel.PeriodDisplayText : string.Empty;
        RetrievalSteps.Visibility = Visibility.Visible;
        RetrievalSpinner.Visibility = Visibility.Visible;
        RetrievalErrorText.Visibility = Visibility.Collapsed;
        RetrievalDismissButton.Visibility = Visibility.Collapsed;
        SetRetrievalStep(0);
        RetrievalOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>0 is nothing started, 1 to 6 mark that step running and earlier ones done
    /// (5 is Stamps, 6 last year's figures).</summary>
    private void SetRetrievalStep(int running)
    {
        var rows = new[] { RetrievalStep1Row, RetrievalStep2Row, RetrievalStep3Row, RetrievalStep4Row, RetrievalStepStampsRow, RetrievalStep5Row };
        var dots = new[] { RetrievalStep1Dot, RetrievalStep2Dot, RetrievalStep3Dot, RetrievalStep4Dot, RetrievalStepStampsDot, RetrievalStep5Dot };
        var markers = new[] { RetrievalStep1Marker, RetrievalStep2Marker, RetrievalStep3Marker, RetrievalStep4Marker, RetrievalStepStampsMarker, RetrievalStep5Marker };
        var texts = new[] { RetrievalStep1Text, RetrievalStep2Text, RetrievalStep3Text, RetrievalStep4Text, RetrievalStepStampsText, RetrievalStep5Text };
        var navy = (Brush)FindResource("CfDeepBlue");

        int shown = 0, done = 0;
        for (var i = 0; i < markers.Length; i++)
        {
            var step = i + 1;
            var isDone = step < running;
            var active = step == running;
            // Polish 5: a circle that fills in - green with a tick, yellow while running, pale waiting.
            dots[i].Background = isDone ? new SolidColorBrush(Color.FromRgb(0x2F, 0xB8, 0x66))
                : active ? (Brush)FindResource("CfYellow")
                : new SolidColorBrush(Color.FromRgb(0xE3, 0xEC, 0xF7));
            markers[i].Text = isDone ? StepDone : active ? StepRunning : string.Empty;
            markers[i].Foreground = isDone ? Brushes.White : navy;
            markers[i].FontSize = active ? 8 : 11;
            texts[i].Foreground = isDone || active ? navy : (Brush)FindResource("CfMuted");
            texts[i].FontWeight = active ? FontWeights.Bold : FontWeights.Normal;

            if (rows[i].Visibility != Visibility.Visible) continue;
            shown++;
            if (isDone) done++;
        }

        // The yellow line along the bottom: the share of the visible steps already done.
        var full = RetrievalProgressFill.Parent is FrameworkElement track && track.ActualWidth > 0 ? track.ActualWidth : 330;
        RetrievalProgressFill.Width = shown == 0 ? 0 : full * done / shown;
    }

    private void HideRetrievalOverlay() => RetrievalOverlay.Visibility = Visibility.Collapsed;

    private void ShowRetrievalError(string detail)
    {
        RetrievalTitle.Text = "Retrieval failed";
        RetrievalSpinner.Visibility = Visibility.Collapsed;
        RetrievalErrorText.Text = string.IsNullOrWhiteSpace(detail) ? "No detail was reported." : detail;
        RetrievalErrorText.Visibility = Visibility.Visible;
        RetrievalDismissButton.Visibility = Visibility.Visible;
        RetrievalOverlay.Visibility = Visibility.Visible;
    }

    private void RetrievalDismiss_Click(object sender, RoutedEventArgs e) => HideRetrievalOverlay();

    private async Task ReadStoreFromFlooidAsync()
    {
        try
        {
            if (_flooidLoginWindow is null || DataContext is not MainViewModel viewModel) return;

            // The back office page is still settling at the moment sign-in is reported, so
            // give it a few attempts rather than one. Scaled, so it follows the timings
            // setting like every other wait.
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var store = await _flooidLoginWindow.TryReadStoreAsync();
                if (!string.IsNullOrWhiteSpace(store))
                {
                    // Another store than the reports were pulled for: forget them.
                    if (!string.IsNullOrWhiteSpace(viewModel.StoreLocation)
                        && !string.Equals(viewModel.StoreLocation.Trim(), store.Trim(), StringComparison.OrdinalIgnoreCase))
                        ForgetPulledReports();
                    viewModel.StoreLocation = store;
                    return;
                }

                await Task.Delay(400);
            }
        }
        catch
        {
            // The store is a convenience on the header. Never let it break a sign in.
        }
    }

    private void FlooidLoginWindow_ConnectionStatusChanged(object? sender, FlooidConnectionStatusChangedEventArgs e)
    {
        if (_isClosing || DataContext is not MainViewModel viewModel) return;

        var state = e.Snapshot.State switch
        {
            FlooidConnectionState.SignedIn => "Good",
            FlooidConnectionState.NotInitialised => "Working",
            FlooidConnectionState.RuntimeMissing or FlooidConnectionState.Error => "Bad",
            _ => "None"
        };

        FlooidConnectButton.Tag = state;
viewModel.SetFlooidStatus(e.Snapshot.StatusText, state);

        // Owner, 16 Oct 2026: a page change while signed in (opening the back office, a link
        // clicked in it) is not a sign in; it used to hide the window and refresh everything.
        var signedIn = e.Snapshot.State == FlooidConnectionState.SignedIn;
        var justSignedIn = _flooidSignIn.Update(e.Snapshot);

        // Stage 6B.21: name the store as soon as we are in, rather than waiting for a
        // report's Outlet field. Fire and forget - a store that cannot be read leaves the
        // header as it was, and must never hold up signing in.
        if (justSignedIn) _ = ReadStoreFromFlooidAsync();


        if (signedIn && _grabAfterFlooidSignIn)
        {
            _grabAfterFlooidSignIn = false;
            _lastAutoGrabUtc = DateTime.UtcNow;
            _flooidLoginWindow?.HideAfterSignIn();
            if (_briefingAfterSignIn)
            {
                _briefingAfterSignIn = false;
                _ = ExportDailyBriefingAsync();
            }
            else
            {
                _ = GrabFlooidReportsAsync(_grabReportsOnlyAfterSignIn);
            }
            return;
        }

        // Stage 6A.93: signing in retrieves the reports without being asked. Three guards,
        // because this event fires on every navigation and the grab navigates: only on the
        // transition into signed-in, never while a grab is running, and not within a minute
        // of the last automatic one. Without the last two a grab's own navigation could
        // start another grab.
        // Owner, 19 Oct 2026: when the session has run out while opening the back office,
        // signing back in is for the back office - it stays open, and nothing is pulled again.
        if (justSignedIn && _flooidLoginWindow?.BackOfficeRequested == true)
        {
            viewModel.SetStatus("Signed in to Flooid · back office open", notify: false);
            return;
        }

        if (justSignedIn
            && !_flooidGrabBusy
            && DateTime.UtcNow - _lastAutoGrabUtc > TimeSpan.FromMinutes(1))
        {
            _lastAutoGrabUtc = DateTime.UtcNow;
            if (DataContext is MainViewModel signedInViewModel)
                signedInViewModel.SetStatus("Signed in to Flooid · retrieving reports");
            // Stage 6A.94: the retrieval drives this same browser, so leaving the window
            // up means watching it navigate the criteria pages and the product group
            // picker. It used to hide on an idle timer, but every navigation restarted
            // that timer, so it stayed up for the whole grab and closed at the end.
            _flooidLoginWindow?.HideAfterSignIn();
            _ = GrabFlooidReportsAsync();
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
    }
}

