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
    private bool _branchGiftCardsActive;
    private FlooidLoginWindow? _flooidLoginWindow;
    private bool _grabAfterFlooidSignIn;
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
        }
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
        _ = CheckForUpdateAsync();   // once per launch, in the background

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
            viewModel.SetStatus("Ready · figures clear on exit, use Save to keep a copy");
        }
        catch
        {
            viewModel.SetStatus("Ready · figures clear on exit, use Save to keep a copy");
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

    private void ApplyTeamLayout(int operatorCount)
    {
        var visibleRows = Math.Clamp(operatorCount, 1, 3);
        TeamScrollViewer.Height = visibleRows * 43.5;
        TeamScrollViewer.VerticalScrollBarVisibility = operatorCount > 3
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;

        var compact = operatorCount >= 3;
        if (_threeRowCompact == compact) return;
        _threeRowCompact = compact;

        if (compact)
        {
            // Recover exactly 43.5px (one full team row) without making any operator row smaller.
            HistoricalTargetRow.Margin = new Thickness(18, 2, 18, 0);    // still saves 4px after the 6A.32 control enlargement
            TargetsRow.Margin = new Thickness(18, 2, 18, 0);             // 3px
            TeamPerformanceSection.Margin = new Thickness(0, 5, 0, 0);  // 6px
            AddPersonButton.Margin = new Thickness(28, 4, 0, 0);         // 3px
            PerformanceSummarySection.Margin = new Thickness(0, 6, 0, 0);// 6px
            CalculateSpacer.Margin = new Thickness(28, 6, 0, 0);          // preserve old Calculate slot while saving 6px
            FormulaStrip.Margin = new Thickness(0, 5, 0, 0);              // 7px
            FormulaStrip.Height = 34.0;                                    // 8.5px
        }
        else
        {
            HistoricalTargetRow.Margin = new Thickness(18, 6, 18, 0);
            TargetsRow.Margin = new Thickness(18, 5, 18, 0);
            TeamPerformanceSection.Margin = new Thickness(0, 11, 0, 0);
            AddPersonButton.Margin = new Thickness(28, 7, 0, 0);
            PerformanceSummarySection.Margin = new Thickness(0, 12, 0, 0);
            CalculateSpacer.Margin = new Thickness(28, 12, 0, 0);
            FormulaStrip.Margin = new Thickness(0, 12, 0, 0);
            FormulaStrip.Height = 42.5;
        }
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
        }

        if (_flooidLoginWindow is not null)
        {
            _flooidLoginWindow.ConnectionStatusChanged -= FlooidLoginWindow_ConnectionStatusChanged;
            _flooidLoginWindow = null;
        }
    }

    private UpdateInfo? _availableUpdate;

    private async Task CheckForUpdateAsync()
    {
        var current = UpdateService.Display(UpdateService.CurrentVersion);
        VersionChip.Content = "v" + current;
        VersionChip.ToolTip = "Checking for updates…";

        var check = await UpdateService.CheckAsync();
        if (!check.Reached)
        {
            VersionChip.Tag = "CantCheck";
            VersionChip.Content = "Can't check · v" + current;
            VersionChip.ToolTip = "Could not reach GitHub to check for updates";
            return;
        }
        if (check.Update is not { } update)
        {
            VersionChip.Tag = "UpToDate";
            VersionChip.Content = "Up to date · v" + current;
            VersionChip.ToolTip = "This is the latest version";
            return;
        }

        _availableUpdate = update;
        VersionChip.Tag = "Available";
        VersionChip.Content = "Update available · v" + UpdateService.Display(update.Version);
        VersionChip.ToolTip = "You have " + current + ". Click to update.";

        // Offer it straight away, once per launch: update now, or Later and carry on (the
        // Update button stays for later). Not over a Refresh already in progress.
        if (RetrievalOverlay.Visibility != Visibility.Visible && IsLoaded) ShowUpdateDialog();
    }

    private void ShowUpdateDialog()
    {
        if (_availableUpdate is null) return;
        var dialog = new UpdateWindow(_availableUpdate) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Installed) Application.Current.Shutdown();
    }

    // Copy summary (design 8A).
    private System.Windows.Threading.DispatcherTimer? _copiedToastTimer;

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            Clipboard.SetText(viewModel.BuildSummaryText());
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Could not copy the summary · " + ex.Message);
            return;
        }

        CopiedToast.IsOpen = true;
        _copiedToastTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _copiedToastTimer.Tick -= CopiedToastTimer_Tick;
        _copiedToastTimer.Tick += CopiedToastTimer_Tick;
        _copiedToastTimer.Stop();
        _copiedToastTimer.Start();
    }

    private void CopiedToastTimer_Tick(object? sender, EventArgs e)
    {
        _copiedToastTimer?.Stop();
        CopiedToast.IsOpen = false;
    }

    private void VersionChip_Click(object sender, RoutedEventArgs e)
    {
        if (Equals(VersionChip.Tag, "Available")) ShowUpdateDialog();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.PlacementTarget = MenuButton;
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
        if (FormulaStrip is not null)
            FormulaStrip.Visibility = otherThanTeam ? Visibility.Collapsed : Visibility.Visible;

        // Stage 6B.02: the header tabs. All are always visible; the active one is marked by Tag.
        if (TeamSectionSegment is not null)
            TeamSectionSegment.Tag = teamActive ? "Active" : null;
        if (BranchSectionSegment is not null)
            BranchSectionSegment.Tag = branchActive ? "Active" : null;

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetStatus(reports ? "Reports · voids, returns and no sales"
                : branchActive ? "Branch Performance · team figures preserved"
                : "Team Performance · branch figures preserved");
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

    private void PeriodDate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        var dialog = new ThemedDatePickerWindow(
            viewModel.SelectedDate ?? DateTime.Today,
            // Stage 6A.76: there is no week-ending concept any more, so the picker
            // uses its plain "Choose date" wording for both ends of a range.
            weeklyMode: false)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        // Stage 6A.75: in Date Range mode this button is the END of the range, so it
        // cannot be moved before the start.
        if (string.Equals(viewModel.HistoricalTargetMode, "Date Range", StringComparison.OrdinalIgnoreCase)
            && viewModel.RangeStartDate is { } rangeStart
            && dialog.SelectedDate.Date < rangeStart.Date)
        {
            viewModel.SetStatus($"The end of the range cannot be before {rangeStart:dd/MM/yyyy}");
            return;
        }

        viewModel.SelectedDate = dialog.SelectedDate;
    }

    private void RangeStartDate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        var dialog = new ThemedDatePickerWindow(
            viewModel.RangeStartDate ?? (viewModel.SelectedDate ?? DateTime.Today).AddDays(-6),
            weeklyMode: false)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        var end = (viewModel.SelectedDate ?? DateTime.Today).Date;
        if (dialog.SelectedDate.Date > end)
        {
            viewModel.SetStatus($"The start of the range cannot be after {end:dd/MM/yyyy}");
            return;
        }

        viewModel.RangeStartDate = dialog.SelectedDate;
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
            MessageBox.Show(this, $"The calculator file could not be saved.\n\n{ex.Message}", "Save File", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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
            MessageBox.Show(this, $"The calculator file could not be loaded.\n\n{ex.Message}", "Load File", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        sb.Append("<style>body{font-family:Arial,sans-serif;color:#0B3972;margin:28px}h1{margin:0;color:#0057B8}h2{background:#0057B8;color:white;padding:9px 14px;border-radius:12px 12px 0 0;margin:24px 0 0}.meta{background:#FFF4A8;padding:10px 14px;border-radius:10px;margin-top:12px}.cards{display:flex;gap:8px;flex-wrap:wrap;margin:12px 0}.card{border:1px solid #D7E2EF;border-radius:10px;padding:8px 12px;min-width:125px}.label{font-size:10px;color:#60758E}.value{font-weight:bold;font-size:16px}table{width:100%;border-collapse:collapse;font-size:12px}th{background:#F2F7FD;color:#0B3972;text-align:left}th,td{padding:7px;border-bottom:1px solid #E4EBF2}tr.best{background:#FFF9D8}.yellow{color:#A87C00}.foot{margin-top:20px;color:#6D7E91;font-size:10px}</style></head><body>");
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
        sb.Append($"<div class='foot'>Flooid: {H(vm.FlooidStatusText)} · {H(vm.FlooidLastUpdatedText)}. Gift-card exclusions are applied only to the P&amp;L counted figures; Branch Performance remains the Flooid report values.</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static FlowDocument BuildPrintableReport(MainViewModel vm)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Arial"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x39, 0x72)) };
        doc.Blocks.Add(new Paragraph(new Run("Card Factory Profit & Loss Report")) { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x57, 0xB8)), Margin = new Thickness(0, 0, 0, 8) });
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
        return doc;
    }

    private static Paragraph MakePrintHeading(string text) => new(new Run(text))
    {
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(0x00, 0x57, 0xB8)),
        Padding = new Thickness(7),
        Margin = new Thickness(0, 12, 0, 0)
    };

    private static Table MakePrintTable(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0xE2, 0xEF)), BorderThickness = new Thickness(0.5) };
        for (var i = 0; i < headers.Count; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        var header = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF7, 0xFD)) };
        foreach (var text in headers) header.Cells.Add(new TableCell(new Paragraph(new Run(text)) { FontWeight = FontWeights.Bold, Margin = new Thickness(2) }) { Padding = new Thickness(3) });
        group.Rows.Add(header);
        foreach (var values in rows)
        {
            var row = new TableRow();
            foreach (var value in values) row.Cells.Add(new TableCell(new Paragraph(new Run(value ?? string.Empty)) { Margin = new Thickness(2) }) { Padding = new Thickness(3) });
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
            viewModel.SetFlooidStatus("Sign in to continue", "Working");
            viewModel.SetStatus("Sign in to Flooid, then the reports will be retrieved automatically");
            try { await loginWindow.EnsureOpenAsync(); }
            catch (Exception ex) { viewModel.SetStatus(ex.Message); }
            return;
        }

        await GrabFlooidReportsAsync();
    }

    private async Task GrabFlooidReportsAsync()
    {
        if (_flooidGrabBusy || DataContext is not MainViewModel viewModel) return;
        var loginWindow = GetOrCreateFlooidLoginWindow();
        if (!loginWindow.IsSignedIn) return;

        _flooidGrabBusy = true;
        // Stage 6A.93: remember which section is showing. Both Refresh buttons run this
        // now, so forcing the Branch view at the end would throw you out of Team
        // Performance every time you refreshed from there.
        var wasBranchView = _branchPerformanceActive;
        var wasReportsView = _reportsActive;
        var wasGiftCardsView = _branchGiftCardsActive;
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
            ShowRetrievalOverlay();

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

            if (teamUpdated || giftUpdated || branchUpdated)
                viewModel.SetFlooidLastUpdated(DateTime.Now);

            var errors = new List<string>();
            if (!teamUpdated && !string.IsNullOrWhiteSpace(teamError)) errors.Add("Team: " + teamError);
            if (!giftUpdated && !string.IsNullOrWhiteSpace(giftError)) errors.Add("Gift Cards: " + giftError);
            if (!branchUpdated && !string.IsNullOrWhiteSpace(branchError)) errors.Add("Branch: " + branchError);

            // Stage 6B.11: the overlay carries the outcome now. On success it closes; on
            // any failure it stays up with the detail, because the labels that used to
            // report it are gone and an error with nowhere to appear is worse than a
            // cluttered header.
            if (teamUpdated && giftUpdated && branchUpdated) HideRetrievalOverlay();
            else ShowRetrievalError(string.Join(Environment.NewLine, errors));

            // Stage 6B.21: the header carries the retrieval time now.
            if (teamUpdated || branchUpdated || giftUpdated)
                viewModel.LastUpdatedAt = DateTime.Now;   // header shows "updated … ago" (design 1A)

            if (teamUpdated && giftUpdated && branchUpdated)
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

    private void ShowRetrievalOverlay()
    {
        StartRetrievalSpinner();
        RetrievalTitle.Text = "Retrieving reports";
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
        var markers = new[] { RetrievalStep1Marker, RetrievalStep2Marker, RetrievalStep3Marker };
        var texts = new[] { RetrievalStep1Text, RetrievalStep2Text, RetrievalStep3Text };

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
            _ = GrabFlooidReportsAsync();
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

