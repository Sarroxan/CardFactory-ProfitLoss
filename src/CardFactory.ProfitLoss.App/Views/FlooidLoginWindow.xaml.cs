using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.Flooid.Models;
using CardFactory.ProfitLoss.Flooid.Services;

namespace CardFactory.ProfitLoss.App.Views;

public partial class FlooidLoginWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    private bool _closing;
    private bool _allowClose;

    public FlooidLoginWindow()
    {
        InitializeComponent();
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _autoCloseTimer.Tick += AutoCloseTimer_Tick;

        // Stage 6B.39: back office sizing lasts only while the window is showing it.
        IsVisibleChanged += (_, e) =>
        {
            // Stage 6B.41: record the size the window was closed at. A resize by hand that
            // looks right is only usable as a default once it is a number, and a photo of
            // the screen cannot give one.
            if (!(bool)e.NewValue)
            {
                var showing = CustomLoginPanel?.Visibility == Visibility.Visible ? "sign-in card"
                            : _backOfficeOpen ? "back office"
                            : !IsSignedIn ? "Flooid login page"
                            : "signed in";
                LogSignInStep($"window hidden at {Math.Round(ActualWidth)}x{Math.Round(ActualHeight)}, showing {showing}");
            }

            if ((bool)e.NewValue || !_backOfficeOpen) return;
            _backOfficeOpen = false;
            _backOfficeSized = false;
            SignInFooterNote.Visibility = Visibility.Visible;
            Width = 860;
            Height = 650;
        };
    }

    public event EventHandler<FlooidConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    public bool IsSignedIn { get; private set; }

    private void Window_SourceInitialized(object? sender, EventArgs e) => WindowAppearance.PreferRoundedCorners(this);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await FlooidView.OpenAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Flooid could not be opened: " + ex.Message;
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xC5, 0x3D, 0x46));
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); } catch { }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => FlooidView.Refresh();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    // Stage 6B.21: the signed-in store, read from the back office page's store-info div.
    public Task<string> TryReadStoreAsync() => FlooidView.TryReadStoreAsync();

    // Stage 6B.27: an explicit sign-out must not be undone by Stay signed in a second later.
    // _autoSignInTried is the existing guard - set, the automatic path does not fire, and
    // only a successful sign in re-arms it. So after signing out you stay out until you
    // sign in again. (Since 6B.63 every launch also starts that way.)
    public async Task<string> SignOutAsync()
    {
        _autoSignInTried = true;
        return await FlooidView.SignOutAsync();
    }

    public static string? RememberedUsername
    {
        get
        {
            try
            {
                return File.Exists(RememberedUsernamePath)
                    ? File.ReadAllText(RememberedUsernamePath).Trim()
                    : null;
            }
            catch { return null; }
        }
    }

    // Stage 6B.27: forget both - the saved password and the remembered username.
    public static void ForgetSavedSignIn()
    {
        CredentialStore.Clear();
        try { if (File.Exists(RememberedUsernamePath)) File.Delete(RememberedUsernamePath); }
        catch { }
        ForgetRememberedStore();   // Stage 6B.50
    }

    // ---------------------------------------------------------------- Stage 6B.50
    // Concept B: an account with several stores picks one the first time, "Remember this
    // store" (ticked by default) keeps it, and every later sign in - automatic included -
    // goes straight to it. "Change store" in the account menu brings the list back.

    private static string RememberedStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardFactory-ProfitLoss", "remembered-store.txt");

    public static string? RememberedStoreCode
    {
        get
        {
            try
            {
                if (!File.Exists(RememberedStorePath)) return null;
                var code = File.ReadAllText(RememberedStorePath).Trim();
                return code.Length == 0 ? null : code;
            }
            catch { return null; }
        }
        private set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RememberedStorePath)!);
                File.WriteAllText(RememberedStorePath, value ?? string.Empty);
            }
            catch { }
        }
    }

    public static void ForgetRememberedStore()
    {
        try { if (File.Exists(RememberedStorePath)) File.Delete(RememberedStorePath); }
        catch { }
    }

    /// <summary>True once Flooid has offered this PC's account a choice of stores.</summary>
    public static bool AccountHasSeveralStores => RememberedStoreCode is not null || _storeChoiceSeen;
    private static bool _storeChoiceSeen;

    private bool _storeCheckInFlight;
    private bool _storeChoiceInFlight;
    private bool _storeListShown;

    /// <summary>
    /// "Change store": forget the remembered store, sign out, and open the sign in. The
    /// automatic sign in is allowed straight away (a plain sign out blocks it), so with a
    /// saved password the next thing seen is the store list.
    /// </summary>
    public async Task<string> ChangeStoreAsync()
    {
        ForgetRememberedStore();
        LogSignInStep("change store: remembered store forgotten, signing out");
        var result = await FlooidView.SignOutAsync();
        _autoSignInTried = false;
        await EnsureOpenAsync();
        return result;
    }

    private async Task HandleStoreSelectionAsync()
    {
        _storeCheckInFlight = true;
        try
        {
            var stores = await FlooidView.ReadStoreChoicesAsync();
            if (stores is null || stores.Count == 0)
            {
                LogSignInStep("no store selection on this page");
                return;
            }

            _storeChoiceSeen = true;
            var pick = stores.FirstOrDefault(x => x.Preselected)?.Code ?? "none";
            LogSignInStep("store selection: " + string.Join(", ", stores.Select(x => x.Code)) + "; Flooid's pick " + pick);

            var remembered = RememberedStoreCode;
            if (remembered is not null && stores.Any(x => x.Code == remembered))
            {
                LogSignInStep("using remembered store " + remembered);
                await ChooseStoreAsync(remembered, remember: true);
                return;
            }
            if (remembered is not null)
                LogSignInStep("remembered store " + remembered + " is not in this account's list");

            ShowStoreList(stores);
        }
        finally
        {
            _storeCheckInFlight = false;
        }
    }

    private void ShowStoreList(IReadOnlyList<FlooidView.FlooidStoreChoice> stores)
    {
        _storeListShown = true;
        StoreList.ItemsSource = stores;
        StoreList.IsEnabled = true;
        StoreCountText.Text = "This account has " + stores.Count + " stores. Choose the one to open.";
        StoreErrorBox.Visibility = Visibility.Collapsed;
        StoreBusyText.Visibility = Visibility.Collapsed;
        RememberStoreCheck.IsChecked = true;
        SignInForm.Visibility = Visibility.Collapsed;
        StoreChoicePanel.Visibility = Visibility.Visible;
        BackToSimpleSignInLink.Visibility = Visibility.Collapsed;
        SetSignInCardVisible(true);
    }

    private void HideStoreList()
    {
        _storeListShown = false;
        StoreChoicePanel.Visibility = Visibility.Collapsed;
        SignInForm.Visibility = Visibility.Visible;
    }

    private void StoreChoice_Click(object sender, RoutedEventArgs e)
    {
        if (_storeChoiceInFlight || sender is not FrameworkElement { Tag: string code }) return;
        _ = ChooseStoreAsync(code, RememberStoreCheck.IsChecked == true);
    }

    private async Task ChooseStoreAsync(string code, bool remember)
    {
        if (_storeChoiceInFlight) return;
        _storeChoiceInFlight = true;
        var started = DateTime.UtcNow;
        try
        {
            StoreList.IsEnabled = false;
            StoreErrorBox.Visibility = Visibility.Collapsed;
            StoreBusyText.Text = "Opening " + code + "…";
            StoreBusyText.Visibility = Visibility.Visible;

            var result = await FlooidView.SelectStoreAsync(code);
            LogSignInStep("store " + code + " select returned: " + result);

            if (!result.StartsWith("CLICKED", StringComparison.Ordinal))
            {
                // The page was not as saved. Hand over to Flooid's own page, which is
                // loaded underneath and can always be used directly.
                HideStoreList();
                SetLoginBusy(false);
                _customLoginDismissed = true;
                SetSignInCardVisible(false);
                BackToSimpleSignInLink.Visibility = Visibility.Visible;
                return;
            }

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                if (!IsSignedIn) continue;

                LogSignInStep("store " + code + " opened after " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms");
                if (remember) RememberedStoreCode = code;
                else ForgetRememberedStore();

                HideStoreList();
                _autoSignInTried = false;   // re-arm, exactly as a successful sign in does
                LoginPasswordBox.Clear();
                SetLoginBusy(false);
                HideAfterSignIn();
                return;
            }

            LogSignInStep("store " + code + " not open after 20s; last page " + (_lastKnownUri?.ToString() ?? "<unknown>"));
            if (!_storeListShown) ShowStoreListAgain();
            StoreErrorText.Text = "Flooid did not open that store. Try again, or use the Flooid page.";
            StoreErrorBox.Visibility = Visibility.Visible;
            StoreBusyText.Visibility = Visibility.Collapsed;
            StoreList.IsEnabled = true;
        }
        finally
        {
            _storeChoiceInFlight = false;
        }
    }

    // A remembered store that failed to open: the list was never shown, so show it now
    // with whatever the page offers.
    private void ShowStoreListAgain()
    {
        _storeListShown = true;
        SignInForm.Visibility = Visibility.Collapsed;
        StoreChoicePanel.Visibility = Visibility.Visible;
        SetSignInCardVisible(true);
        _ = RefillStoreListAsync();
    }

    private async Task RefillStoreListAsync()
    {
        var stores = await FlooidView.ReadStoreChoicesAsync();
        if (stores is { Count: > 0 })
        {
            StoreList.ItemsSource = stores;
            StoreCountText.Text = "This account has " + stores.Count + " stores. Choose the one to open.";
        }
    }

    public Task<string> FetchRefundsVoidsHtmlAsync(DateTime fromDate, DateTime toDate) =>
        FlooidView.FetchRefundsVoidsHtmlAsync(fromDate, toDate);

    public Task<string> FetchBranchPerformanceHtmlAsync(DateTime fromDate, DateTime toDate) =>
        FlooidView.FetchBranchPerformanceHtmlAsync(fromDate, toDate);

    public Task<string> FetchGiftCardHtmlAsync(DateTime fromDate, DateTime toDate) =>
        FlooidView.FetchGiftCardHtmlAsync(fromDate, toDate);

    public Task<string> FetchDiscountsHtmlAsync(DateTime fromDate, DateTime toDate) =>
        FlooidView.FetchDiscountsHtmlAsync(fromDate, toDate);

    public async Task EnsureOpenAsync()
    {
        // Stage 6B.31: each time this window is opened for a sign in, start with our card.
        // _customLoginDismissed is set when a submit fails or when "Show the Flooid login
        // page" is used, and was only ever cleared by the Back link - and this window is
        // created once and reused, so one failure left every later sign in going straight
        // to Flooid's page until the link was found and clicked.
        _customLoginDismissed = false;

        if (!IsVisible) Show();
        Activate();
        await FlooidView.OpenAsync();
    }

    // ---------------------------------------------------------------- Stage 6B.39
    // "Open Flooid back office" from the account menu. The window was 860x650, sized for
    // the sign-in fallback, which is too small to browse the back office in. Chosen design (B): 90% of the screen's work area, centred on the screen.
    private bool _backOfficeOpen;
    private bool _backOfficeSized;

    public async Task OpenBackOfficeAsync()
    {
        _backOfficeOpen = true;
        SignInFooterNote.Visibility = Visibility.Collapsed;   // Stage 6B.70: sign-in wording, not back office
        SizeForBackOffice();
        await EnsureOpenAsync();
        SizeForBackOffice();   // again: the first Show of this window places it by the old size

        // Stage 6B.69: load the back office afresh now the window is full size.
        if (IsSignedIn)
        {
            LogSignInStep("back office opened at " + Math.Round(Width) + "x" + Math.Round(Height) + "; loading its home page at that size");
            FlooidView.OpenBackOfficeHome();
        }
    }

    private void SizeForBackOffice()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Round(area.Width * 0.9);
        Height = Math.Round(area.Height * 0.9);
        Left = area.Left + ((area.Width - Width) / 2);
        Top = area.Top + ((area.Height - Height) / 2);
        _backOfficeSized = true;
    }

    public void ShutdownAndClose()
    {
        _allowClose = true;
        Close();
    }

    private void FlooidView_ConnectionStatusChanged(object? sender, FlooidConnectionStatusChangedEventArgs e)
    {
        if (_closing) return;

        IsSignedIn = e.Snapshot.State == FlooidConnectionState.SignedIn;
        _lastKnownUri = e.Snapshot.CurrentUri;
        UpdateCustomLoginPanel(e.Snapshot.CurrentUri);
        StatusText.Text = e.Snapshot.StatusText;
        StatusDot.Fill = e.Snapshot.State switch
        {
            FlooidConnectionState.SignedIn => new SolidColorBrush(Color.FromRgb(0x1D, 0x8F, 0x4E)),
            FlooidConnectionState.NotInitialised => new SolidColorBrush(Color.FromRgb(0xD6, 0xA9, 0x00)),
            FlooidConnectionState.RuntimeMissing or FlooidConnectionState.Error => new SolidColorBrush(Color.FromRgb(0xC5, 0x3D, 0x46)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xB8))
        };
        ConnectedBadge.Visibility = IsSignedIn ? Visibility.Visible : Visibility.Collapsed;

        ConnectionStatusChanged?.Invoke(this, e);

        if (IsSignedIn)
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Start();
        }
        else
        {
            _autoCloseTimer.Stop();
        }
    }

    private void AutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        _autoCloseTimer.Stop();
        // Stage 6B.39: not while the back office was asked for. Every navigation restarts
        // this timer, so the first link clicked in the back office would close the window
        // 0.9 s later.
        if (!_closing && IsSignedIn && !_backOfficeOpen) Hide();
    }

    /// <summary>
    /// Stage 6A.94: hide immediately rather than waiting for the idle timer. Retrieval
    /// drives this window's own browser, and every navigation restarted that timer, so
    /// the window stayed up for the whole grab while the user watched it work.
    /// </summary>
    // ---------------------------------------------------------------- Stage 6B.18
    // Our own sign-in card. It never replaces Flooid's page - it sits over it, fills its
    // two fields and clicks its button, so Flooid's form does the submitting and holds
    // the session exactly as before.

    private bool _customLoginDismissed;
    private bool _loginInFlight;

    // Stage 6B.19: one automatic attempt per run of the application. If stored credentials
    // are refused - changed password, locked account - retrying them on every navigation
    // would lock the account faster than a person could.
    // Stage 6B.63: starts SET. Launching the app never signs in by itself - the first
    // sign in of each run is typed (or chosen from the store list). A successful sign in
    // re-arms it as before, so "Stay signed in" still recovers an expired session for the
    // rest of that run. Cookies are cleared at launch too (FlooidView), so nothing carries
    // the last run's session over either.
    private bool _autoSignInTried = true;

    // Stage 6B.20: remembered so "Back to the simple sign in" can re-evaluate without
    // waiting for the browser to navigate again - on the login page nothing navigates.
    private Uri? _lastKnownUri;

    private static string RememberedUsernamePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardFactory-ProfitLoss", "remembered-username.txt");

    /// <summary>
    /// Shown only while the browser is actually sitting on /authserver/login. Any other
    /// page - a session that expired somewhere unexpected, or a change at Flooid's end -
    /// and the real page is left visible rather than covered by a form that would post
    /// into nothing.
    /// </summary>
    private void UpdateCustomLoginPanel(Uri? currentUri)
    {
        if (CustomLoginPanel is null) return;

        var onLoginPage = currentUri is not null
            && currentUri.AbsolutePath.Contains("/authserver/login", StringComparison.OrdinalIgnoreCase);

        // Stage 6B.22: the card has now twice failed to appear with no way to tell why, so
        // every decision is recorded. One attempt on the next build says which of the four
        // reasons it was, instead of another round of guessing.
        LogSignInCardDecision(currentUri, onLoginPage);

        // Stage 6B.50: Flooid's "Select Store" step. Signed in means past it, and any other
        // page (a sign out, an expired step back at the login page) means it is gone.
        if (_storeListShown && !_storeChoiceInFlight
            && (IsSignedIn || !FlooidSessionService.IsLoginFlowUri(currentUri)))
            HideStoreList();
        if (!IsSignedIn && FlooidSessionService.IsLoginFlowUri(currentUri))
        {
            if (_storeListShown)
            {
                LogSignInCardReason("kept: choosing a store");
                SetSignInCardVisible(true);
                return;
            }
            if (!_storeCheckInFlight && !_storeChoiceInFlight) _ = HandleStoreSelectionAsync();
        }

        // Stage 6B.24: while our sign-in is in flight, hold the card up whatever the page.
        // Between submitting and being signed in Flooid passes through /web/spring/login -
        // seen in the 6B.22 log - which is "not the login page" but not yet signed in either.
        // The branch below then hid the card and revealed the browser, sitting on a page
        // literally called login. That was the "Flooid login appears" after completing.
        if (_loginInFlight && !IsSignedIn)
        {
            LogSignInCardReason("kept: sign-in in progress");
            SetSignInCardVisible(true);
            return;
        }

        if (!onLoginPage || IsSignedIn)
        {
            LogSignInCardReason(IsSignedIn ? "hidden: already signed in" : "hidden: not the login page");
            SetSignInCardVisible(false);
            BackToSimpleSignInLink.Visibility = Visibility.Collapsed;
            return;
        }

        if (_customLoginDismissed)
        {
            LogSignInCardReason("hidden: the Flooid page was asked for");
            SetSignInCardVisible(false);
            BackToSimpleSignInLink.Visibility = Visibility.Visible;
            return;
        }

        LogSignInCardReason("shown");

        BackToSimpleSignInLink.Visibility = Visibility.Collapsed;

        if (CustomLoginPanel.Visibility != Visibility.Visible)
        {
            LoadRememberedUsername();
            SetLoginBusy(false);
            SetSignInCardVisible(true);
            if (string.IsNullOrWhiteSpace(LoginUsernameBox.Text)) LoginUsernameBox.Focus();
            else LoginPasswordBox.Focus();
        }

        // Stage 6B.19: landing on the login page is exactly what an expired session does -
        // Flooid redirects here - so this covers the timeout without the application ever
        // needing to recognise what a timed-out page looks like. The same path handles the
        // first sign-in of the day.
        if (!_autoSignInTried && !_loginInFlight && CredentialStore.TryLoad(out var storedUser, out var storedPassword))
        {
            _autoSignInTried = true;
            LoginUsernameBox.Text = storedUser;
            LoginPasswordBox.Password = storedPassword;
            RememberPasswordCheck.IsChecked = true;
            RememberUsernameCheck.IsChecked = true;
            LoginBusyText.Text = "Signing in…";
            _ = SignInAsync(storedUser, storedPassword, automatic: true);
        }
    }

    private static void LogSignInStep(string step)
    {
        try
        {
            AppFiles.AppendDiagnostic(
                "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - sign-in step: " + step + " ===\r\n");
        }
        catch
        {
        }
    }

    private static void LogSignInCardReason(string reason)
    {
        try
        {
            AppFiles.AppendDiagnostic("  decision       : " + reason + "\r\n");
        }
        catch
        {
        }
    }

    private static void LogSignInCardDecision(Uri? currentUri, bool onLoginPage)
    {
        try
        {
            var line = string.Format(
                "=== {0} - sign-in card ===\r\n  uri            : {1}\r\n  absolute path  : {2}\r\n  on login page  : {3}\r\n",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                currentUri?.ToString() ?? "<null>",
                currentUri?.AbsolutePath ?? "<null>",
                onLoginPage);

            AppFiles.AppendDiagnostic(line);
        }
        catch
        {
        }
    }

    // Stage 6B.23: WebView2 is a native window hosted in WPF, not a WPF control, and a
    // native window always paints over WPF content occupying the same space whatever the
    // z-order says - the WPF "airspace" limitation. So the card was genuinely Visible and
    // the browser was drawn over it. The 6B.22 log proved it: three "decision: shown"
    // entries on /authserver/login, and a user looking at Flooid's own page throughout.
    //
    // The only reliable fix is not to overlap them. While the card is up the browser is
    // Hidden - not Collapsed, so it keeps its layout and its CoreWebView2 and carries on
    // loading and running script underneath, which SubmitLoginAsync depends on. Hiding the
    // whole login window during a grab has always worked the same way.
    private void SetSignInCardVisible(bool visible)
    {
        CustomLoginPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (FlooidViewHost is not null)
            FlooidViewHost.Visibility = visible ? Visibility.Hidden : Visibility.Visible;

        // Stage 6B.31: the window was sized for a full web page - 860x650 - which is far
        // more than a card with two fields needs. Size it to whichever is actually being
        // shown. The browser keeps its old dimensions, so nothing about the fallback to
        // Flooid's page changes.
        //
        // Stage 6B.39: in the back office, size once and then leave it alone - this runs on
        // every navigation, and would otherwise undo both the 90% size and any resize by
        // hand the moment a link is clicked.
        if (!visible && _backOfficeOpen)
        {
            if (!_backOfficeSized) SizeForBackOffice();
            return;
        }
        _backOfficeSized = false;

        // Stage 6B.40/6B.42: Flooid's own login page - the card hidden and not signed in -
        // at 462x656, the size the user dragged it to (logged by 6B.41). Signed in, it
        // stays 860x650: that is the size every retrieval has run at.
        if (visible) { Width = 440; Height = 430; }
        else if (!IsSignedIn) { Width = 462; Height = 656; }
        else { Width = 860; Height = 650; }
        if (IsLoaded) CentreOnScreen();
    }

    // Stage 6B.42: centred on the screen, as asked, rather than on the main window - in
    // the 23/09 photo the main window ran off the right edge, taking the sign in with it.
    private void CentreOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - Width) / 2);
        Top = area.Top + ((area.Height - Height) / 2);
    }

    private void LoadRememberedUsername()
    {
        // Stage 6B.19: reflect what is actually stored, so the box is not unticked while a
        // password sits on disk.
        RememberPasswordCheck.IsChecked = CredentialStore.Exists;

        try
        {
            if (!File.Exists(RememberedUsernamePath)) return;
            var saved = File.ReadAllText(RememberedUsernamePath).Trim();
            if (saved.Length == 0) return;
            LoginUsernameBox.Text = saved;
            RememberUsernameCheck.IsChecked = true;
        }
        catch
        {
            // A convenience only. If it cannot be read the field is simply empty.
        }
    }

    private void SaveRememberedUsername(string username)
    {
        try
        {
            var path = RememberedUsernamePath;
            if (RememberUsernameCheck.IsChecked == true)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, username);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void SetLoginBusy(bool busy)
    {
        _loginInFlight = busy;
        LoginSubmitButton.IsEnabled = !busy;
        LoginUsernameBox.IsEnabled = !busy;
        LoginPasswordBox.IsEnabled = !busy;
        LoginBusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) LoginErrorBox.Visibility = Visibility.Collapsed;
    }

    private void ShowLoginError(string message)
    {
        LoginErrorText.Text = message;
        LoginErrorBox.Visibility = Visibility.Visible;
        SetLoginBusy(false);
        LoginPasswordBox.Clear();
        LoginPasswordBox.Focus();
    }

    private void LoginPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_loginInFlight) LoginSubmit_Click(sender, new RoutedEventArgs());
    }

    private void LoginSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_loginInFlight) return;

        var username = LoginUsernameBox.Text.Trim();
        var password = LoginPasswordBox.Password;

        if (username.Length == 0) { ShowLoginError("Enter your username."); return; }
        if (password.Length == 0) { ShowLoginError("Enter your password."); return; }

        SaveRememberedUsername(username);

        // Stage 6B.19: written only on a deliberate sign in, so a stored password is never
        // created by the automatic path re-saving what it just read.
        if (RememberPasswordCheck.IsChecked == true) CredentialStore.TrySave(username, password);
        else CredentialStore.Clear();

        _ = SignInAsync(username, password, automatic: false);
    }

    private async Task SignInAsync(string username, string password, bool automatic)
    {
        SetLoginBusy(true);
        var started = DateTime.UtcNow;
        LogSignInStep("submitting (" + (automatic ? "automatic" : "typed") + ")");

        // Stage 6B.25: this await was the one unbounded wait in the whole routine. Everything
        // after it has a 20 second limit, but if the submit itself never returned, the card
        // sat on "Signing in…" with the button greyed out for ever - reported with a photo on
        // 22/09. Bounded now, so the worst case is an error message rather than a hang.
        var submitTask = FlooidView.SubmitLoginAsync(username, password);
        var finished = await Task.WhenAny(submitTask, Task.Delay(TimeSpan.FromSeconds(15)));
        if (finished != submitTask)
        {
            LogSignInStep("submit did not return within 15s");
            ShowLoginError("Flooid did not respond to the sign in. Try again, "
                         + "or show the Flooid login page.");
            return;
        }

        var submitted = await submitTask;
        LogSignInStep("submit returned: " + submitted);
        if (!submitted.StartsWith("CLICKED", StringComparison.Ordinal)
            && !submitted.StartsWith("SUBMITTED", StringComparison.Ordinal))
        {
            // The form was not where the saved page said it would be. Rather than guess,
            // hand over to the real page - it is already loaded underneath.
            //
            // Stage 6B.26: SetLoginBusy(false) is the fix for the hang. This path hid the
            // card but left _loginInFlight set, so the very next navigation hit 6B.24's
            // "kept: sign-in in progress" branch and put the card straight back up on
            // "Signing in…" - permanently, since nothing else would ever clear the flag.
            SetLoginBusy(false);
            _customLoginDismissed = true;
            SetSignInCardVisible(false);
            BackToSimpleSignInLink.Visibility = Visibility.Visible;
            return;
        }

        // Success is leaving /authserver/login, which FlooidSessionService already decides
        // and reports through ConnectionStatusChanged. Failure is still being there. The
        // saved failure page has not been seen yet, so the wording here is ours rather
        // than Flooid's; when that page arrives the real message can be read out instead.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);

            // Stage 6B.50: the password was accepted and Flooid is asking for a store. The
            // store list takes it from here; ChooseStoreAsync finishes the sign in.
            if (_storeListShown && !IsSignedIn)
            {
                LogSignInStep("password accepted; waiting for a store to be chosen");
                SetLoginBusy(false);
                return;
            }

            if (IsSignedIn)
            {
                LogSignInStep("signed in after " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms");

                // Stage 6B.24: go straight away. The window used to linger on the back
                // office until the idle timer fired, showing Flooid pages nobody asked to see.
                HideAfterSignIn();

                // Stage 6B.19: re-arm. Without this the automatic sign in fires once per run
                // of the application and a session that expires at 2pm would sit at the login
                // page doing nothing - which is the very problem this exists to solve. Only a
                // SUCCESS re-arms it, so refused credentials still get exactly one attempt.
                _autoSignInTried = false;
                LoginPasswordBox.Clear();
                SetLoginBusy(false);
                return;
            }
        }

        LogSignInStep("not signed in after 20s; last page " + (_lastKnownUri?.ToString() ?? "<unknown>"));

        // Stage 6B.50: stuck on the store step is not a refused password - never clear the
        // saved sign in for it.
        if (FlooidSessionService.IsLoginFlowUri(_lastKnownUri) || _storeChoiceInFlight || _storeListShown)
        {
            ShowLoginError("Signed in, but Flooid did not finish opening a store. Try again, "
                         + "or show the Flooid page.");
            return;
        }

        if (automatic)
        {
            // A stored sign in was refused. Clear it rather than keep presenting it: the
            // password has almost certainly changed, and a wrong one retried is how
            // accounts get locked.
            CredentialStore.Clear();
            RememberPasswordCheck.IsChecked = false;
            LoginPasswordBox.Clear();
            ShowLoginError("The saved sign in was not accepted, so it has been forgotten. "
                         + "Please sign in again.");
            return;
        }

        ShowLoginError("Flooid did not accept that sign in. Check the username and password, "
                     + "or use the Flooid page instead.");
    }

    private void UseFlooidPage_Click(object sender, RoutedEventArgs e)
    {
        // Stage 6B.18: the way out. Anything our form cannot cope with, the real page can -
        // it is loaded and live underneath, and this stops covering it.
        // Stage 6B.20: and a way back, because this was one-way and a stray click lost the
        // simple form until the window was reopened.
        _customLoginDismissed = true;
        SetSignInCardVisible(false);
        BackToSimpleSignInLink.Visibility = Visibility.Visible;
    }

    private void BackToSimpleSignIn_Click(object sender, RoutedEventArgs e)
    {
        _customLoginDismissed = false;
        BackToSimpleSignInLink.Visibility = Visibility.Collapsed;
        UpdateCustomLoginPanel(_lastKnownUri);
    }

    public void HideAfterSignIn()
    {
        if (_closing || !IsSignedIn) return;
        _autoCloseTimer.Stop();
        Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _closing = true;
        _autoCloseTimer.Stop();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _autoCloseTimer.Stop();
        _autoCloseTimer.Tick -= AutoCloseTimer_Tick;
        FlooidView.Shutdown();
    }
}
