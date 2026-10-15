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
    private (DateTime From, DateTime To)? _staffDiscountPeriod;   // the day or range the Reports staff discounts are for
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
    private bool _flooidWasSignedIn;
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
            VersionChip.Content = userAsked ? "Checking… · v" + current : "v" + current;
            VersionChip.ToolTip = "Checking for updates…";
        }

        var check = await UpdateService.CheckAsync();
        if (!check.Reached)
        {
            if (background) return;   // try again next time; the chip keeps what it last knew
            VersionChip.Tag = "CantCheck";
            VersionChip.Content = "Can't check · v" + current;
            VersionChip.ToolTip = "Could not reach GitHub to check for updates. Click to try again.";
            if (userAsked && DataContext is MainViewModel failedViewModel)
                failedViewModel.SetStatus("Could not reach GitHub to check for updates · try again later");
            return;
        }
        if (check.Update is not { } update)
        {
            _availableUpdate = null;
            VersionChip.Tag = "UpToDate";
            VersionChip.Content = "Up to date · v" + current;
            VersionChip.ToolTip = "This is the latest version. Click to check again.";
            if (userAsked && DataContext is MainViewModel latestViewModel)
                latestViewModel.SetStatus("You have the latest version · v" + current);
            return;
        }

        _availableUpdate = update;
        VersionChip.Tag = "Available";
        VersionChip.Content = "Update available · v" + UpdateService.Display(update.Version);
        VersionChip.ToolTip = "You have " + current + ". Click to update.";

        // Offer it straight away at launch or when asked: update now, or Later and carry on
        // (the chip stays yellow for later). Not over a Refresh already in progress, and
        // never from the background check.
        if (!background && RetrievalOverlay.Visibility != Visibility.Visible && IsLoaded) ShowUpdateDialog();
    }

    private void ShowUpdateDialog()
    {
        if (_availableUpdate is null) return;
        var dialog = new UpdateWindow(_availableUpdate) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Installed) Application.Current.Shutdown();
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
        if (!MainMenuPopup.IsOpen) LoadRetrievalSpeedOptions();
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

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export Profit & Loss Report",
            Filter = "HTML report (*.html)|*.html",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(viewModel.StoreLocation)
                ? $"Profit-Loss-{(viewModel.SelectedDate ?? DateTime.Today):yyyy-MM-dd}"
                : $"Profit-Loss-{MakeSafeFileName(viewModel.StoreLocation)}-{(viewModel.SelectedDate ?? DateTime.Today):yyyy-MM-dd}"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, BuildReportHtml(viewModel), Encoding.UTF8);
            viewModel.SetStatus("Report exported");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Export failed · " + ex.Message);
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
            "Available" => (VersionChip.Content?.ToString() ?? "Update available", "#FFF1DC", "#9A5B00"),
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

    private void PrintReport_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            var document = BuildPrintableReport(viewModel);
            document.PageHeight = dialog.PrintableAreaHeight;
            document.PageWidth = dialog.PrintableAreaWidth;
            document.PagePadding = new Thickness(36);
            document.ColumnGap = 0;
            document.ColumnWidth = Math.Max(1, dialog.PrintableAreaWidth - 72);
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Card Factory Profit & Loss Report");
            viewModel.SetStatus("Report sent to printer");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Print failed · " + ex.Message);
        }
    }

    private static string BuildReportHtml(MainViewModel vm)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var end = (vm.SelectedDate ?? DateTime.Today).Date;
        var weekly = string.Equals(vm.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase);
        var start = weekly ? (vm.RangeStartDate ?? end.AddDays(-6)).Date : end;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>Card Factory Profit &amp; Loss Report</title>");
        sb.Append("<style>body{font-family:Arial,sans-serif;color:#183153;margin:28px}h1{margin:0;color:#004EA1}h2{background:#004EA1;color:white;padding:9px 14px;border-radius:12px 12px 0 0;margin:24px 0 0}.meta{background:#FFF4A8;padding:10px 14px;border-radius:10px;margin-top:12px}.cards{display:flex;gap:8px;flex-wrap:wrap;margin:12px 0}.card{border:1px solid #D7E2EF;border-radius:10px;padding:8px 12px;min-width:125px}.label{font-size:10px;color:#60758E}.value{font-weight:bold;font-size:16px}table{width:100%;border-collapse:collapse;font-size:12px}th{background:#E3ECF7;color:#004EA1;text-align:left}th,td{padding:7px;border-bottom:1px solid #E4EBF2}tr.best{background:#FFF9D8}.yellow{color:#A87C00}td.flag{background:#FFF1DC;color:#9A5B00;font-weight:bold}.note{color:#667891;font-size:10px;margin-top:6px}.foot{margin-top:20px;color:#667891;font-size:10px}</style></head><body>");
        sb.Append("<h1>Card Factory Profit &amp; Loss Calculator</h1>");
        sb.Append($"<div class='meta'><b>{H(vm.StoreLocation)}</b><br>{start:dd/MM/yyyy} - {end:dd/MM/yyyy} · {H(vm.HistoricalTargetMode)}</div>");
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
        doc.Blocks.Add(new Paragraph(new Run("Card Factory Profit & Loss Report")) { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x4E, 0xA1)), Margin = new Thickness(0, 0, 0, 8) });
        var end = (vm.SelectedDate ?? DateTime.Today).Date;
        var start = string.Equals(vm.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase)
            ? (vm.RangeStartDate ?? end.AddDays(-6)).Date
            : end;
        doc.Blocks.Add(new Paragraph(new Run($"{vm.StoreLocation}   {start:dd/MM/yyyy} - {end:dd/MM/yyyy}   {vm.HistoricalTargetMode}")) { FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xA8)), Padding = new Thickness(8) });
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

        await GrabFlooidReportsAsync();
    }

    /// <param name="reportsOnly">True: only what the Reports section shows (Refunds/Voids and
    /// Discounts and Price Overrides). Null: whatever the section on screen needs.</param>
    /// <param name="forBriefing">Export to Daily Briefing: the full pull plus Staff Discounts,
    /// whichever section is showing.</param>
    /// <returns>True when every report asked for came back (the full pull only).</returns>
    private async Task<bool> GrabFlooidReportsAsync(bool? reportsOnly = null, bool forBriefing = false)
    {
        if (_flooidGrabBusy || DataContext is not MainViewModel viewModel) return false;
        var loginWindow = GetOrCreateFlooidLoginWindow();
        if (!loginWindow.IsSignedIn) return false;

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
                var refundsHtml = await loginWindow.FetchRefundsVoidsHtmlAsync(startDate, endDate);
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
                var giftHtml = await loginWindow.FetchGiftCardHtmlAsync(startDate, endDate);
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
                var branchHtml = await loginWindow.FetchBranchPerformanceHtmlAsync(endDate, endDate);
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
                    var discountsHtml = await loginWindow.FetchDiscountsHtmlAsync(startDate, endDate);
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
                _staffDiscountPeriod = null;
            }
            var discountsOk = !includeDiscounts || discountsUpdated;

            if (teamUpdated || giftUpdated || branchUpdated)
                viewModel.SetFlooidLastUpdated(DateTime.Now);

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
                viewModel.LastUpdatedAt = DateTime.Now;   // header shows "updated … ago" (design 1A)

            if (teamUpdated && giftUpdated && branchUpdated && discountsOk)
            {
                viewModel.SetFlooidStatus("Updated", "Good");
                viewModel.SetStatus(branchActualPeriod is null
                    ? "Team Performance, Branch Performance and Gift Cards updated from Flooid"
                    : $"Team Performance, Branch Performance and Gift Cards updated from Flooid · report period {branchActualPeriod}");
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
                var refundsHtml = await loginWindow.FetchRefundsVoidsHtmlAsync(startDate, endDate);
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
                var discountsHtml = await loginWindow.FetchDiscountsHtmlAsync(startDate, endDate);
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
        if (RetrievalStep2Marker.Parent is UIElement step2Row) step2Row.Visibility = reportsOnly ? Visibility.Collapsed : Visibility.Visible;
        if (RetrievalStep3Marker.Parent is UIElement step3Row) step3Row.Visibility = reportsOnly ? Visibility.Collapsed : Visibility.Visible;
        if (reportsOnly) RetrievalStep4Row.Visibility = Visibility.Visible;
        RetrievalStep4Row.Margin = reportsOnly ? new Thickness(0) : new Thickness(0, 5, 0, 0);   // step 1 already leaves 5px below it
        RetrievalTitle.Text = reportsOnly ? "Generating reports" : "Retrieving reports";
        RetrievalSteps.Visibility = Visibility.Visible;
        RetrievalSpinner.Visibility = Visibility.Visible;
        RetrievalErrorText.Visibility = Visibility.Collapsed;
        RetrievalDismissButton.Visibility = Visibility.Collapsed;
        SetRetrievalStep(0);
        RetrievalOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>0 is nothing started, 1 to 3 mark that step running and earlier ones done.</summary>
    private void SetRetrievalStep(int running)
    {
        var markers = new[] { RetrievalStep1Marker, RetrievalStep2Marker, RetrievalStep3Marker, RetrievalStep4Marker };
        var texts = new[] { RetrievalStep1Text, RetrievalStep2Text, RetrievalStep3Text, RetrievalStep4Text };

        for (var i = 0; i < markers.Length; i++)
        {
            var step = i + 1;
            var done = step < running;
            var active = step == running;

            markers[i].Text = done ? StepDone : active ? StepRunning : StepPending;
            markers[i].Foreground = done
                ? new SolidColorBrush(Color.FromRgb(0x1D, 0x8F, 0x4E))
                : active
                    ? (Brush)FindResource("CfBlue")
                    : (Brush)FindResource("CfMuted");
            texts[i].Foreground = active ? (Brush)FindResource("CfDeepBlue") : (Brush)FindResource("CfMuted");
            texts[i].FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }
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

        var signedIn = e.Snapshot.State == FlooidConnectionState.SignedIn;
        var justSignedIn = signedIn && !_flooidWasSignedIn;
        _flooidWasSignedIn = signedIn;

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

