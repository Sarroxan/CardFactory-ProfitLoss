using System.Diagnostics;
using System.Windows;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.Flooid.Services;
using Microsoft.Web.WebView2.Core;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// Two sign-ins (owner, 2 Nov 2026). Flooid lets the same account be signed in twice at once
/// (till test, 2 Nov 2026: both signed in straight away and a minute later; signing the second
/// out left the first signed in). Reports cannot be pulled side by side inside one sign-in -
/// every criteria page carries that sign-in's one struts.token_REPORT - so a second sign-in,
/// hidden, pulls reports at the same time as the app's own (see PullPlan).
///
/// It starts signing in as soon as the app's own sign-in is sent (Prepare), with the same
/// sign-in (typed or saved, held in memory only), and on an account with several stores it
/// waits on Flooid's Select Store page until the person has chosen, then chooses the same
/// store. Before each pull it checks it is still signed in, and signs in again if Flooid has
/// ended it. Anything that goes wrong only means the app's own sign-in pulls the report, as
/// before; after two failures in a row it is not used again until the next sign-in.
///
/// Till log of 2 Nov 2026, 11:37 (first version): it started before the store was known and
/// gave up on Select Store; on the next try it was still on that page and looked for a login
/// form that was not there. Now the store is asked for when it is needed (and waited for),
/// every page is handled where it is found, and after a failure it starts again from a fresh
/// browser profile.
/// </summary>
internal sealed class SecondSignIn
{
    /// <param name="StoreCode">Asked each time it is needed: the store the app's own sign-in
    /// chose, or null while it is still being chosen (or on a one-store account).</param>
    public sealed record Details(CoreWebView2Environment Environment, string Username, string Password, Func<string?> StoreCode);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Window? _host;
    private FlooidView? _view;
    private bool _signedIn;
    private string? _signedInStore;    // the store it chose (null: no Select Store page)
    private string? _signedInUser;
    private int _failures;
    private bool _disabled;
    private int _generation;           // moved on by Prepare / Sign out / closing: work begun before is dropped

    public bool Disabled => _disabled;

    /// <summary>
    /// The app's own sign-in has just been sent: start this one now, so it is ready (or nearly)
    /// by the time the first reports are wanted.
    /// </summary>
    public void Prepare(Details details)
    {
        _generation++;
        _failures = 0;
        _disabled = false;
        var generation = _generation;
        _ = PrepareAsync(details, generation);
    }

    private async Task PrepareAsync(Details details, int generation)
    {
        await _gate.WaitAsync();
        try
        {
            if (generation != _generation) return;
            Log("starting alongside the app's own sign-in");
            if (!await EnsureReadyAsync(details, generation, TimeSpan.FromMinutes(3)) && generation == _generation)
                NoteFailure();
        }
        catch (Exception ex)
        {
            Log("stopped by an error: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// For a pull: waits until this sign-in is ready and holds it for that pull. Null when it
    /// can't be used, or is no longer wanted by the time it is ready.
    /// </summary>
    public async Task<Lease?> AcquireAsync(Details details, Func<bool> stillWanted)
    {
        if (_disabled) return null;
        await _gate.WaitAsync();
        var leased = false;
        try
        {
            if (_disabled || !stillWanted()) return null;
            var generation = _generation;
            if (!await EnsureReadyAsync(details, generation, TimeSpan.FromSeconds(30)))
            {
                if (generation == _generation) NoteFailure();
                return null;
            }
            if (generation != _generation || !stillWanted()) return null;
            leased = true;
            return new Lease(this, generation);
        }
        catch (Exception ex)
        {
            Log("stopped by an error: " + ex.Message);
            NoteFailure();
            return null;
        }
        finally
        {
            if (!leased) _gate.Release();
        }
    }

    /// <summary>This sign-in, held for one pull. Dispose to let it go.</summary>
    internal sealed class Lease : IDisposable
    {
        private readonly SecondSignIn _owner;
        private readonly int _generation;
        private bool _released;

        public Lease(SecondSignIn owner, int generation)
        {
            _owner = owner;
            _generation = generation;
        }

        public async Task<string> FetchAsync(Func<FlooidView, Task<string>> fetch)
        {
            if (_generation != _owner._generation || _owner._view is null)
                throw new InvalidOperationException("the second sign-in was stopped");
            var html = await fetch(_owner._view);
            if (_generation != _owner._generation)
                throw new InvalidOperationException("the second sign-in was stopped while pulling");
            _owner._failures = 0;
            return html;
        }

        public void Failed(string report, Exception ex)
        {
            Log(report + " failed, the app's own sign-in pulls it instead: " + ex.Message);
            _owner._signedIn = false;   // check it properly next time
            _owner.NoteFailure();
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _owner._gate.Release();
        }
    }

    private async Task<bool> EnsureReadyAsync(Details details, int generation, TimeSpan waitForStore)
    {
        if (_view is not null && _signedIn)
        {
            var wanted = details.StoreCode();
            var otherStore = wanted is not null && _signedInStore is not null && wanted != _signedInStore;
            var otherUser = !string.Equals(_signedInUser, details.Username, StringComparison.OrdinalIgnoreCase);
            if (!otherStore && !otherUser)
            {
                var check = await _view.CheckSessionAsync();
                if (check.StartsWith("SIGNED-IN", StringComparison.Ordinal)) return true;
                Log("no longer signed in (" + check + "), signing in again");
            }
            else
            {
                Log(otherUser ? "another person signed in, starting again"
                              : "the app's own sign-in is on store " + wanted + ", not " + _signedInStore + ", starting again");
                await SignOutViewAsync();
            }
            TearDown();
        }

        return await SignInAsync(details, generation, waitForStore);
    }

    private async Task<bool> SignInAsync(Details details, int generation, TimeSpan waitForStore)
    {
        var clock = Stopwatch.StartNew();
        if (_view is null)
        {
            _view = new FlooidView { LogPrefix = "Second sign-in · " };
            _view.UseSeparateProfile(details.Environment, "SecondSignIn");
            _host = new Window
            {
                Width = 1200,
                Height = 800,
                Left = -32000,
                Top = -32000,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Opacity = 0.01,
                Content = _view
            };
            _host.Show();
            await _view.OpenAsync();   // opens the back office, which shows Flooid's login page
        }
        else
        {
            _view.OpenBackOfficeHome();
        }

        // Each page is handled where it is found: Flooid's login page (sign in), its Select
        // Store page (choose the app's store, waiting for it to be chosen if need be), its menu
        // (signed in). Flooid passes through other signed-in looking pages
        // (/web/spring/redirect?authKey=…) on the way, so only the menu counts.
        var submitted = false;
        var storeChosen = false;
        var waitingLogged = false;
        var storeDeadline = DateTime.UtcNow + waitForStore;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            if (generation != _generation) { TearDown(); return false; }
            var source = _view.CurrentSource;
            var path = source?.AbsolutePath ?? string.Empty;

            if (_view.IsCurrentlySignedIn
                && string.Equals(path.TrimEnd('/'), "/web/spring/menu", StringComparison.OrdinalIgnoreCase))
            {
                var check = await _view.CheckSessionAsync();
                if (!check.StartsWith("SIGNED-IN", StringComparison.Ordinal)) continue;
                _signedIn = true;
                _signedInUser = details.Username;
                if (!storeChosen) _signedInStore = null;
                var signedInAt = clock.ElapsedMilliseconds;

                // Till log, 2 Nov 2026 12:18 and 12:19: its first report opened 5.2 and 5.6 s after
                // the click (later ones about 1 s; the app's own first one 1.5 s). It went straight
                // from reaching the menu to clicking it; wait for the menu's report links first, so
                // that wait happens here, before any report is asked of it.
                var menuDeadline = DateTime.UtcNow.AddSeconds(10);
                var menuReady = false;
                while (DateTime.UtcNow < menuDeadline && generation == _generation)
                {
                    if (await _view.IsMenuReadyAsync()) { menuReady = true; break; }
                    await Task.Delay(150);
                }
                Log("signed in after " + signedInAt + " ms" + (storeChosen ? " (store " + _signedInStore + ")" : string.Empty)
                    + (menuReady ? ", menu ready after " + clock.ElapsedMilliseconds + " ms" : ", menu not seen within 10 s"));
                return true;
            }

            if (path.StartsWith("/authserver/login", StringComparison.OrdinalIgnoreCase))
            {
                if (!submitted)
                {
                    var submit = _view.SubmitLoginAsync(details.Username, details.Password);
                    if (await Task.WhenAny(submit, Task.Delay(TimeSpan.FromSeconds(15))) != submit)
                        return Fail("Flooid's login page did not answer within 15 s");
                    var result = await submit;
                    if (result is not ("CLICKED" or "SUBMITTED"))
                        return Fail("Flooid's login form was not found (" + result + ")");
                    submitted = true;
                    continue;
                }
                var refused = await _view.ReadLoginErrorAsync();
                if (refused.Length > 0)
                {
                    TearDown();
                    _disabled = true;   // the same password again would only be refused again
                    Log("could not sign in - Flooid refused the sign-in: " + refused + "; not used again until the next sign-in");
                    return false;
                }
                continue;
            }

            if (FlooidSessionService.IsLoginFlowUri(source) && !storeChosen)
            {
                var stores = await _view.ReadStoreChoicesAsync();
                if (stores is not { Count: > 0 }) continue;   // passing through, or still loading

                var code = details.StoreCode();
                if (code is null)
                {
                    // The person is still choosing on the app's own sign-in. Wait for them.
                    if (DateTime.UtcNow >= storeDeadline)
                        return Fail("the store was not chosen in time on the app's own sign-in");
                    if (!waitingLogged)
                    {
                        waitingLogged = true;
                        Log("at Select Store after " + clock.ElapsedMilliseconds + " ms, waiting for the store to be chosen");
                    }
                    deadline = DateTime.UtcNow.AddSeconds(30);
                    continue;
                }
                if (!stores.Any(x => x.Code == code))
                    return Fail("Flooid offers stores " + string.Join(", ", stores.Select(x => x.Code)) + " but not " + code);
                var chose = await _view.SelectStoreAsync(code);
                if (!chose.StartsWith("CLICKED", StringComparison.Ordinal))
                    return Fail("could not choose store " + code + " (" + chose + ")");
                storeChosen = true;
                _signedInStore = code;
                deadline = DateTime.UtcNow.AddSeconds(30);
            }
        }
        return Fail("not signed in within 30 s (at " + PathOf(_view.CurrentSource) + ")");
    }

    /// <summary>Starts again from a fresh, empty browser profile next time.</summary>
    private bool Fail(string why)
    {
        Log("could not sign in - " + why);
        TearDown();
        return false;
    }

    private void NoteFailure()
    {
        _failures++;
        if (_failures >= 2 && !_disabled)
        {
            _disabled = true;
            Log("not used again until the next sign-in; the app's own sign-in pulls everything");
        }
    }

    /// <summary>Sign out and Change store: sign the second sign-in out too.</summary>
    public async Task SignOutAsync()
    {
        _generation++;
        _failures = 0;
        if (_view is not null && _signedIn) await SignOutViewAsync();
        TearDown();
    }

    private async Task SignOutViewAsync()
    {
        try
        {
            var result = await _view!.SignOutAsync();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && _view is not null && _view.IsCurrentlySignedIn)
                await Task.Delay(200);
            Log("signed out (" + result + ")");
        }
        catch (Exception ex)
        {
            Log("sign out did not complete: " + ex.Message);
        }
        _signedIn = false;
    }

    private void TearDown()
    {
        _signedIn = false;
        _signedInStore = null;
        _signedInUser = null;
        var view = _view;
        var host = _host;
        _view = null;
        _host = null;
        try { view?.Shutdown(); } catch { }
        try { host?.Close(); } catch { }
    }

    /// <summary>The app is closing. Like the app's own sign-in, Flooid ends it when it runs out.</summary>
    public void Shutdown()
    {
        _generation++;
        TearDown();
    }

    private static string PathOf(Uri? uri) => uri?.AbsolutePath ?? "no page";

    public static void Log(string text) =>
        AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] Second sign-in: {text}{Environment.NewLine}");
}
