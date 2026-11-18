using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.ViewModels;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// Automatic refresh (owner, 18 Nov 2026: "add 6"). While the app is open on today, it presses
/// Refresh by itself every 30 minutes - exactly as the button does (same progress panel, same
/// reports) - so the figures are never far behind. It only does so when it can't get in anyone's
/// way: signed in, nothing being pulled, no other window or the menu open, nobody has touched the
/// mouse or keyboard for 2 minutes, and nothing has been typed into the window since the last pull
/// (a Refresh would replace figures typed in by hand).
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan AutoRefreshEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AutoRefreshIdleFor = TimeSpan.FromMinutes(2);

    private DispatcherTimer? _autoRefreshTimer;
    private DateTime _lastPullAt = DateTime.MinValue;
    private DateTime _lastInputAt = DateTime.Now;
    private bool _typedSinceLastPull;

    private void StartAutoRefresh()
    {
        if (_autoRefreshTimer is not null) return;
        PreviewMouseMove += (_, _) => _lastInputAt = DateTime.Now;
        PreviewMouseDown += (_, _) => _lastInputAt = DateTime.Now;
        PreviewMouseWheel += (_, _) => _lastInputAt = DateTime.Now;
        PreviewKeyDown += (_, _) => _lastInputAt = DateTime.Now;
        PreviewTextInput += (_, _) => { _lastInputAt = DateTime.Now; _typedSinceLastPull = true; };

        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _autoRefreshTimer.Tick += async (_, _) => await AutoRefreshIfDueAsync();
        _autoRefreshTimer.Start();
    }

    /// <summary>Called when a pull starts (button, sign-in or automatic): the 30 minutes start again.</summary>
    private void NotePullStarted()
    {
        _lastPullAt = DateTime.Now;
        _typedSinceLastPull = false;
    }

    private async Task AutoRefreshIfDueAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        var now = DateTime.Now;
        if (_lastPullAt == DateTime.MinValue || now - _lastPullAt < AutoRefreshEvery) return;   // only after a first pull
        if (now - _lastInputAt < AutoRefreshIdleFor) return;
        if (_typedSinceLastPull) return;
        if (vm.IsRangeMode || (vm.SelectedDate ?? DateTime.Today).Date != DateTime.Today) return;
        if (_flooidGrabBusy || _quietFetch is { IsCompleted: false }) return;
        if (_flooidLoginWindow is not { IsSignedIn: true }) return;
        if (RetrievalOverlay.Visibility == Visibility.Visible) return;
        if (OwnedWindows.Cast<Window>().Any(w => w.IsVisible) || Application.Current.Windows.Cast<Window>().Any(w => w != this && w.IsVisible && w.IsActive)) return;
        if (Mouse.Captured is not null) return;
        if (AnyPopupOpen()) return;

        AppFiles.AppendDiagnostic($"[{now:HH:mm:ss}] Automatic refresh (30 minutes since the last pull, nobody using the app){Environment.NewLine}");
        await GrabFlooidReportsAsync(fresh: !_reportsActive);
    }

    private bool AnyPopupOpen() => MainMenuPopup.IsOpen || FlooidAccountMenu.IsOpen || AddOnSuggestPopup.IsOpen;
}
