using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.App.Views;

namespace CardFactory.ProfitLoss.App;

/// <summary>
/// The lock (owner, 26 Nov 2026, concept P1). When it is on and a PIN is set, the app covers itself
/// after the chosen time (1 hour to start) with nobody clicking, typing or moving the mouse in any of
/// its windows. Flooid stays signed in underneath - a pull already running carries on and the hourly
/// refresh still happens - so only the 4-digit PIN is needed to carry on. Five wrong PINs in a row
/// mean a 30-second wait. Forgot the PIN? The Flooid password saved on this PC unlocks instead, and
/// then the Lock window opens to set a new PIN.
/// </summary>
public partial class MainWindow
{
    private const int LockWrongTriesBeforeWait = 5;
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(30);

    private bool _locked;
    private DateTime _lockActivityAt = DateTime.Now;
    private Point? _lockLastMouse;
    private string _lockTyped = "";
    private int _lockWrongTries;
    private DateTime _lockWaitUntil = DateTime.MinValue;
    private DispatcherTimer? _lockWaitTimer;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    private void StartLockWatch()
    {
        // Any click, key, wheel or real mouse movement in any of the app's windows counts as use.
        InputManager.Current.PreNotifyInput += (_, e) =>
        {
            switch (e.StagingItem.Input)
            {
                case MouseButtonEventArgs or KeyEventArgs or MouseWheelEventArgs:
                    _lockActivityAt = DateTime.Now;
                    break;
                case MouseEventArgs when GetCursorPos(out var at):
                    // WPF also sends mouse moves when things change under a still pointer: only a
                    // pointer that has really moved counts.
                    var now = new Point(at.X, at.Y);
                    if (_lockLastMouse is not { } last || Math.Abs(last.X - now.X) > 2 || Math.Abs(last.Y - now.Y) > 2)
                    {
                        _lockLastMouse = now;
                        _lockActivityAt = DateTime.Now;
                    }
                    break;
            }
        };
        PreviewKeyDown += Lock_PreviewKeyDown;
        PreviewTextInput += (_, e) => { if (_locked && !LockPasswordPanelFocused()) e.Handled = true; };
        UpdateLockMenuNote();
    }

    /// <summary>Called every minute by the refresh timer.</summary>
    private void LockIfIdle()
    {
        if (_locked) return;
        var choices = LockSettings.Load();
        if (!choices.Enabled || !choices.HasPin) return;
        if (DateTime.Now - _lockActivityAt < TimeSpan.FromMinutes(choices.Minutes)) return;
        // Another of the app's windows is open (a dialog can't be covered): it locks once that closes.
        if (OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) return;
        Lock();
    }

    private void Lock()
    {
        MainMenuPopup.IsOpen = false;
        FlooidAccountMenu.IsOpen = false;
        AddOnSuggestPopup.IsOpen = false;
        _locked = true;
        _lockTyped = "";
        ShowLockPin();
        LockError.Text = "";
        LockOverlay.Visibility = Visibility.Visible;
        Keyboard.Focus(LockOverlay);
        AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] Locked (not used for a while){Environment.NewLine}");
    }

    private void Unlock()
    {
        _locked = false;
        _lockTyped = "";
        _lockWrongTries = 0;
        LockPasswordBox.Clear();
        LockOverlay.Visibility = Visibility.Collapsed;
        _lockActivityAt = DateTime.Now;
        AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] Unlocked{Environment.NewLine}");
    }

    private bool LockPasswordPanelFocused() =>
        LockPasswordPanel.Visibility == Visibility.Visible && LockPasswordBox.IsKeyboardFocusWithin;

    private void Lock_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_locked) return;
        if (LockPasswordPanelFocused()) return;   // typing the Flooid password
        e.Handled = true;                           // nothing reaches the figures underneath
        if (LockPinPanel.Visibility != Visibility.Visible) { LockPasswordBox.Focus(); return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is >= Key.D0 and <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None) LockPress(((int)(key - Key.D0)).ToString());
        else if (key is >= Key.NumPad0 and <= Key.NumPad9) LockPress(((int)(key - Key.NumPad0)).ToString());
        else if (key == Key.Back) LockPress("back");
    }

    private void LockKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag }) LockPress(tag);
    }

    private void LockPress(string key)
    {
        if (DateTime.Now < _lockWaitUntil) return;
        if (key == "back")
        {
            if (_lockTyped.Length > 0) _lockTyped = _lockTyped[..^1];
        }
        else if (_lockTyped.Length < 4)
        {
            _lockTyped += key;
            LockError.Text = "";
        }
        DrawLockDots();
        if (_lockTyped.Length == 4) CheckLockPin();
    }

    private void CheckLockPin()
    {
        if (LockSettings.CheckPin(LockSettings.Load(), _lockTyped))
        {
            Unlock();
            return;
        }

        _lockTyped = "";
        _lockWrongTries++;
        DrawLockDots();
        if (_lockWrongTries >= LockWrongTriesBeforeWait)
        {
            _lockWrongTries = 0;
            StartLockWait();
        }
        else
        {
            LockError.Text = "That PIN isn't right - try again.";
        }
    }

    private void StartLockWait()
    {
        _lockWaitUntil = DateTime.Now + LockWait;
        LockKeypad.IsEnabled = false;
        _lockWaitTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockWaitTimer.Tick -= LockWait_Tick;
        _lockWaitTimer.Tick += LockWait_Tick;
        _lockWaitTimer.Start();
        LockWait_Tick(null, EventArgs.Empty);
    }

    private void LockWait_Tick(object? sender, EventArgs e)
    {
        var left = _lockWaitUntil - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            _lockWaitTimer?.Stop();
            LockKeypad.IsEnabled = true;
            LockError.Text = "";
            return;
        }
        LockError.Text = $"Too many wrong PINs - wait {Math.Ceiling(left.TotalSeconds):0} seconds.";
    }

    private void DrawLockDots()
    {
        var dots = new[] { LockDot1, LockDot2, LockDot3, LockDot4 };
        for (var i = 0; i < dots.Length; i++)
        {
            var filled = i < _lockTyped.Length;
            dots[i].BorderBrush = filled ? (Brush)FindResource("CfBlue") : new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDC));
            if (dots[i].Child is UIElement dot) dot.Visibility = filled ? Visibility.Visible : Visibility.Hidden;
        }
    }

    private void ShowLockPin()
    {
        LockPinPanel.Visibility = Visibility.Visible;
        LockPasswordPanel.Visibility = Visibility.Collapsed;
        var minutes = LockSettings.Load().Minutes;
        var idle = minutes >= 120 ? "2 hours" : minutes >= 60 ? "an hour" : $"{minutes} minutes";
        LockSubtitle.Text = $"Not used for {idle} · enter your 4-digit PIN";
        DrawLockDots();
    }

    private void LockForgot_Click(object sender, RoutedEventArgs e)
    {
        LockPinPanel.Visibility = Visibility.Collapsed;
        LockPasswordPanel.Visibility = Visibility.Visible;
        LockPasswordError.Visibility = Visibility.Collapsed;
        LockSubtitle.Text = "Type the Flooid password saved on this PC, then set a new PIN";
        LockPasswordBox.Clear();
        LockPasswordBox.Focus();
    }

    private void LockBackToPin_Click(object sender, RoutedEventArgs e)
    {
        LockPasswordBox.Clear();
        ShowLockPin();
        Keyboard.Focus(LockOverlay);
    }

    private void LockPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) LockPasswordUnlock_Click(sender, e);
    }

    private void LockPasswordUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.Now < _lockWaitUntil) return;
        if (!CredentialStore.TryLoad(out _, out var saved) || string.IsNullOrEmpty(saved))
        {
            ShowLockPasswordError("No Flooid password is saved on this PC, so the PIN is the only way in.");
            return;
        }

        var typed = LockPasswordBox.Password;
        LockPasswordBox.Clear();
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(typed), Encoding.UTF8.GetBytes(saved));
        if (!matches)
        {
            _lockWrongTries++;
            if (_lockWrongTries >= LockWrongTriesBeforeWait)
            {
                _lockWrongTries = 0;
                _lockWaitUntil = DateTime.Now + LockWait;
                ShowLockPasswordError("Too many wrong tries - wait 30 seconds.");
            }
            else
            {
                ShowLockPasswordError("That isn't the saved Flooid password.");
            }
            return;
        }

        Unlock();
        Dispatcher.InvokeAsync(() => OpenLockSettings(newPin: true), DispatcherPriority.Background);
    }

    private void ShowLockPasswordError(string text)
    {
        LockPasswordError.Text = text;
        LockPasswordError.Visibility = Visibility.Visible;
        LockPasswordBox.Focus();
    }

    private void LockMenu_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPopup.IsOpen = false;
        OpenLockSettings();
    }

    private void OpenLockSettings(bool newPin = false)
    {
        new LockSettingsWindow(newPin) { Owner = this }.ShowDialog();
        _lockActivityAt = DateTime.Now;
        UpdateLockMenuNote();
    }

    private void UpdateLockMenuNote()
    {
        var choices = LockSettings.Load();
        LockMenuNote.Text = choices.Enabled && choices.HasPin
            ? $"On · after {(choices.Minutes >= 120 ? "2 hours" : choices.Minutes >= 60 ? "1 hour" : $"{choices.Minutes} min")} unused"
            : "A 4-digit PIN after a while unused";
    }
}
