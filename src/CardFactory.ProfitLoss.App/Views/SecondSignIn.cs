using System.Diagnostics;
using System.Windows;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.Flooid.Services;
using Microsoft.Web.WebView2.Core;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// One report handed to the second sign-in. The app's own sign-in takes it back at the point
/// where it would have pulled it, if the second sign-in has not started on it by then.
/// </summary>
internal sealed class SideJob
{
    public SideJob(string report) => Report = report;

    public string Report { get; }

    /// <summary>The app's own sign-in pulled it instead; the second sign-in must not start it.</summary>
    public bool Taken { get; set; }

    /// <summary>The second sign-in has started pulling it; its result is to be waited for.</summary>
    public bool Started { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>The report's page, or null when the second sign-in did not pull it.</summary>
    public Task<string?> Result { get; set; } = Task.FromResult<string?>(null);
}

/// <summary>
/// Two sign-ins (owner, 2 Nov 2026). Flooid lets the same account be signed in twice at once
/// (till test, 2 Nov 2026: both signed in straight away and a minute later; signing the second
/// out left the first signed in). Reports cannot be pulled side by side inside one sign-in -
/// every criteria page carries that sign-in's one struts.token_REPORT - so a second sign-in,
/// hidden, pulls some of them at the same time as the app's own.
///
/// It uses the sign-in the app's own was given (typed or saved, held in memory only) and, on
/// an account with several stores, chooses the same store the app's own chose. Before each
/// report it checks it is still signed in, and signs in again if Flooid has ended it. Anything
/// that goes wrong only means the app's own sign-in pulls that report, as before; after two
/// failures in a row it is not used again until the next sign-in.
/// </summary>
internal sealed class SecondSignIn
{
    public sealed record Details(CoreWebView2Environment Environment, string Username, string Password, string? StoreCode);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Window? _host;
    private FlooidView? _view;
    private bool _signedIn;
    private string? _signedInFor;      // the store code it was asked for when it signed in
    private int _failures;
    private bool _disabled;
    private int _generation;           // moved on by Sign out: anything begun before is dropped

    /// <summary>Starts pulling a report on the second sign-in, in the background.</summary>
    public void Start(SideJob job, Details details, Func<FlooidView, Task<string>> fetch) =>
        job.Result = RunAsync(job, details, fetch);

    private async Task<string?> RunAsync(SideJob job, Details details, Func<FlooidView, Task<string>> fetch)
    {
        if (_disabled) return null;
        await _gate.WaitAsync();
        try
        {
            if (job.Taken || _disabled) return null;
            var generation = _generation;
            if (!await EnsureReadyAsync(details, generation)) return null;
            if (job.Taken || generation != _generation)
            {
                Log(job.Report + ": ready, but the app's own sign-in had already pulled it");
                return null;
            }

            job.Started = true;
            job.StartedAt = DateTime.Now;
            try
            {
                var html = await fetch(_view!);
                _failures = 0;
                return generation == _generation ? html : null;
            }
            catch (Exception ex)
            {
                Log(job.Report + " failed, the app's own sign-in pulls it instead: " + ex.Message);
                _signedIn = false;   // check it properly next time
                NoteFailure();
                return null;
            }
        }
        catch (Exception ex)
        {
            Log("stopped by an error: " + ex.Message);
            NoteFailure();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> EnsureReadyAsync(Details details, int generation)
    {
        if (_view is not null && _signedIn)
        {
            if (string.Equals(_signedInFor, details.StoreCode, StringComparison.Ordinal))
            {
                var check = await _view.CheckSessionAsync();
                if (check.StartsWith("SIGNED-IN", StringComparison.Ordinal)) return true;
                Log("no longer signed in (" + check + "), signing in again");
            }
            else
            {
                Log("the app's own sign-in is on store " + (details.StoreCode ?? "?") + ", signing out of " + (_signedInFor ?? "?") + " first");
                await SignOutViewAsync();
            }
            _signedIn = false;
        }

        var ok = await SignInAsync(details);
        if (ok && generation != _generation)
        {
            Log("signed out by the app while signing in - signing out again");
            await SignOutViewAsync();
            return false;
        }
        if (!ok) NoteFailure();
        return ok;
    }

    private async Task<bool> SignInAsync(Details details)
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
            await _view.OpenAsync();
        }
        else if (!(_view.CurrentSource?.AbsolutePath ?? string.Empty).StartsWith("/authserver/login", StringComparison.OrdinalIgnoreCase))
        {
            // Not on the login page (Flooid ended the sign-in while the page stayed put): ask
            // for the back office, which sends a signed-out browser to the login page. Wait for
            // that page, so the form filled is the new one, not the page being left.
            _view.OpenBackOfficeHome();
            var arrive = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < arrive
                   && !(_view.CurrentSource?.AbsolutePath ?? string.Empty).StartsWith("/authserver/login", StringComparison.OrdinalIgnoreCase))
                await Task.Delay(200);
        }

        var submit = _view.SubmitLoginAsync(details.Username, details.Password);
        if (await Task.WhenAny(submit, Task.Delay(TimeSpan.FromSeconds(15))) != submit)
            return Fail("Flooid's login page did not answer within 15 s");
        var submitted = await submit;
        if (submitted is not ("CLICKED" or "SUBMITTED"))
            return Fail("Flooid's login form was not found (" + submitted + ", at " + PathOf(_view.CurrentSource) + ")");

        var storeChosen = false;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            var source = _view.CurrentSource;

            // Signed in means on the menu: Flooid passes through other signed-in looking pages
            // (/web/spring/redirect?authKey=…) before its Select Store page.
            if (_view.IsCurrentlySignedIn
                && string.Equals(source?.AbsolutePath.TrimEnd('/'), "/web/spring/menu", StringComparison.OrdinalIgnoreCase))
            {
                var check = await _view.CheckSessionAsync();
                if (!check.StartsWith("SIGNED-IN", StringComparison.Ordinal)) continue;
                _signedIn = true;
                _signedInFor = details.StoreCode;
                Log("signed in after " + clock.ElapsedMilliseconds + " ms" + (storeChosen ? " (store " + details.StoreCode + ")" : string.Empty));
                return true;
            }

            if (FlooidSessionService.IsLoginFlowUri(source) && !storeChosen)
            {
                var stores = await _view.ReadStoreChoicesAsync();
                if (stores is { Count: > 0 })
                {
                    if (string.IsNullOrEmpty(details.StoreCode) || !stores.Any(x => x.Code == details.StoreCode))
                        return Fail("Flooid offers stores " + string.Join(", ", stores.Select(x => x.Code))
                                    + " but the app's own store " + (details.StoreCode ?? "(not known)") + " is not one of them");
                    var chose = await _view.SelectStoreAsync(details.StoreCode);
                    if (!chose.StartsWith("CLICKED", StringComparison.Ordinal))
                        return Fail("could not choose store " + details.StoreCode + " (" + chose + ")");
                    storeChosen = true;
                }
                continue;
            }

            var refused = await _view.ReadLoginErrorAsync();
            if (refused.Length > 0)
            {
                _disabled = true;   // the same password again would only be refused again
                return Fail("Flooid refused the sign-in: " + refused);
            }
        }
        return Fail("not signed in within 30 s (at " + PathOf(_view.CurrentSource) + ")");
    }

    private bool Fail(string why)
    {
        Log("could not sign in - " + why);
        _signedIn = false;
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

    /// <summary>Sign out and Change store: sign the second sign-in out too, and start afresh.</summary>
    public async Task SignOutAsync()
    {
        _generation++;
        _failures = 0;
        _disabled = false;
        if (_view is not null && _signedIn) await SignOutViewAsync();
        _signedIn = false;
    }

    private async Task SignOutViewAsync()
    {
        try
        {
            var result = await _view!.SignOutAsync();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && _view.IsCurrentlySignedIn)
                await Task.Delay(250);
            Log("signed out (" + result + ", now at " + PathOf(_view.CurrentSource) + ")");
        }
        catch (Exception ex)
        {
            Log("sign out did not complete: " + ex.Message);
        }
        _signedIn = false;
    }

    /// <summary>The app is closing. Like the app's own sign-in, Flooid ends it when it runs out.</summary>
    public void Shutdown()
    {
        _generation++;
        try { _view?.Shutdown(); } catch { }
        try { _host?.Close(); } catch { }
        _view = null;
        _host = null;
        _signedIn = false;
    }

    private static string PathOf(Uri? uri) => uri?.AbsolutePath ?? "no page";

    public static void Log(string text) =>
        AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] Second sign-in: {text}{Environment.NewLine}");
}
