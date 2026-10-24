using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.Flooid.Models;
using CardFactory.ProfitLoss.Flooid.Services;
using Microsoft.Web.WebView2.Core;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// Test only (owner, 24 Oct 2026): can the same Flooid account be signed in twice at once?
/// Pulling reports side by side inside one sign-in is not possible (every criteria page
/// carries the one struts.token_REPORT of that sign-in), so the only way would be a second
/// sign-in. This finds out whether Flooid allows that, and changes nothing about how reports
/// are pulled.
///
/// Once per run, after the sign-in pull and the quiet fetch have finished: signs in a second
/// time in a hidden browser with its own separate, in-memory profile (so it shares no cookies
/// with the app's own sign-in), checks that both sign-ins work straight away and a minute
/// later, signs the second one out, checks the first once more, and writes every step and a
/// RESULT line to the diagnostic log. The password is the one just typed (or the saved one),
/// held in memory only; it is never written anywhere.
///
/// "Working" is a plain request for Flooid's menu page from inside each browser: still signed
/// in, Flooid serves the menu; signed out, it sends the request to its login page.
/// </summary>
internal static class SecondSignInTest
{
    private const string Prefix = "Second sign-in test: ";

    public static async Task RunAsync(
        CoreWebView2Environment environment,
        string username,
        string password,
        Func<Task<string>> checkFirstSignIn,
        Func<bool> appStillSignedIn)
    {
        Microsoft.Web.WebView2.Wpf.WebView2? browser = null;
        Window? host = null;
        var secondSignedIn = false;
        var firstChecks = new List<string>();
        var secondChecks = new List<string>();
        string? firstAfterSecondSignedOut = null;

        try
        {
            Log("started");
            var before = await checkFirstSignIn();
            Log("the app's own sign-in before the test: " + before);
            if (!IsSignedIn(before))
            {
                Log("stopped - the app's own sign-in is not working, so the test would show nothing");
                return;
            }

            browser = new Microsoft.Web.WebView2.Wpf.WebView2();
            host = new Window
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
                Content = browser
            };
            host.Show();

            // Its own profile, in memory only: nothing shared with the app's sign-in, nothing
            // left on disk afterwards.
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "SecondSignInTest";
            options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options);
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;

            var clock = Stopwatch.StartNew();
            core.Navigate(FlooidSessionService.BackOfficeEntryUri.AbsoluteUri);

            // 1. The login page, then the same typing and click the app's own sign-in uses.
            var submitted = "NO_FORM";
            var formDeadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < formDeadline)
            {
                await Task.Delay(250);
                try { submitted = await RunScriptAsync(core, LoginScript(username, password)); }
                catch { continue; }
                if (submitted is "CLICKED" or "SUBMITTED") break;
            }
            if (submitted is not ("CLICKED" or "SUBMITTED"))
            {
                Log("stopped - the second browser never showed Flooid's login form (" + submitted + ", at " + PathOf(browser.Source) + ")");
                return;
            }
            Log("second sign-in submitted after " + clock.ElapsedMilliseconds + " ms");

            // 2. Signed in, refused, or stopped on Flooid's Select Store page.
            var session = new FlooidSessionService();
            var signInDeadline = DateTime.UtcNow.AddSeconds(30);
            var outcome = "no answer within 30 s";
            while (DateTime.UtcNow < signInDeadline)
            {
                await Task.Delay(250);
                var source = browser.Source;
                if (session.Evaluate(source).State == FlooidConnectionState.SignedIn)
                {
                    secondSignedIn = true;
                    outcome = "signed in after " + clock.ElapsedMilliseconds + " ms, at " + PathOf(source);
                    break;
                }
                if (FlooidSessionService.IsLoginFlowUri(source))
                {
                    outcome = "stopped on Flooid's Select Store page (an account with several stores) - this test does not choose a store";
                    break;
                }
                string error;
                try { error = await RunScriptAsync(core, LoginErrorScript); }
                catch { continue; }
                if (error.Length > 0)
                {
                    outcome = "Flooid refused it: " + error;
                    break;
                }
            }
            Log("second sign-in " + outcome);
            if (!secondSignedIn) return;

            // 3. Both straight away, then both a minute later.
            await CheckBothAsync("straight after the second sign-in", core, checkFirstSignIn, firstChecks, secondChecks);

            var waitUntil = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < waitUntil)
            {
                if (!appStillSignedIn())
                {
                    Log("stopped - the app was signed out during the test");
                    return;
                }
                await Task.Delay(1000);
            }
            await CheckBothAsync("a minute later", core, checkFirstSignIn, firstChecks, secondChecks);

            // 4. Sign the second one out through Flooid's own Log Out, then check the first.
            var signOut = await SignOutSecondAsync(browser);
            secondSignedIn = false;
            Log("second sign-in signed out: " + signOut);
            await Task.Delay(3000);
            firstAfterSecondSignedOut = await checkFirstSignIn();
            Log("the app's own sign-in after the second signed out: " + firstAfterSecondSignedOut);

            Log("RESULT: " + Verdict(firstChecks, secondChecks, firstAfterSecondSignedOut));
        }
        catch (Exception ex)
        {
            Log("stopped by an error: " + ex.Message);
        }
        finally
        {
            if (secondSignedIn && browser is not null)
            {
                try { Log("second sign-in signed out on the way out: " + await SignOutSecondAsync(browser)); }
                catch { }
            }
            try { browser?.Dispose(); } catch { }
            try { host?.Close(); } catch { }
            Log("finished");
        }
    }

    private static async Task CheckBothAsync(string when, CoreWebView2 second, Func<Task<string>> checkFirstSignIn, List<string> firstChecks, List<string> secondChecks)
    {
        var first = await checkFirstSignIn();
        var other = await CheckSessionOnAsync(second);
        firstChecks.Add(first);
        secondChecks.Add(other);
        Log(when + " - the app's own sign-in: " + first + " · the second sign-in: " + other);
    }

    private static string Verdict(List<string> firstChecks, List<string> secondChecks, string? firstAfter)
    {
        var firstHeld = firstChecks.All(IsSignedIn);
        var secondHeld = secondChecks.All(IsSignedIn);
        if (firstHeld && secondHeld && firstAfter is not null && IsSignedIn(firstAfter))
            return "both sign-ins worked side by side, and signing the second out left the first signed in - pulling reports at the same time is worth building";
        if (firstHeld && secondHeld)
            return "both sign-ins worked side by side, but signing the second out also ended the app's own sign-in - possible, with care";
        if (!firstHeld && secondHeld)
            return "the second sign-in ended the app's own sign-in - Flooid allows one sign-in per account, so pulling at the same time is not possible";
        if (firstHeld && !secondHeld)
            return "the second sign-in was ended while the app's own carried on - Flooid keeps only the first, so pulling at the same time is not possible";
        return "neither sign-in held - unclear, send the log";
    }

    private static bool IsSignedIn(string check) => check.StartsWith("SIGNED-IN", StringComparison.Ordinal);

    /// <summary>
    /// Asks Flooid for its menu page from inside a browser, without moving that browser off the
    /// page it is on. Still signed in, Flooid serves the menu; signed out, it answers with its
    /// login page.
    /// </summary>
    internal static async Task<string> CheckSessionOnAsync(CoreWebView2? core)
    {
        if (core is null) return "ERROR browser not ready";
        const string script = """
            (() => {
              try {
                const x = new XMLHttpRequest();
                x.open('GET', '/web/spring/menu', false);
                x.send(null);
                const url = String(x.responseURL || '');
                const body = String(x.responseText || '');
                let path = url;
                try { path = new URL(url).pathname; } catch (_) {}
                const login = url.toLowerCase().includes('/authserver/login')
                           || body.includes('id="Uname"') || body.includes("id='Uname'");
                return (login ? 'SIGNED-OUT ' : 'SIGNED-IN ') + x.status + ' ' + path;
              } catch (e) {
                return 'ERROR ' + String(e && e.message ? e.message : e);
              }
            })()
            """;
        try { return await RunScriptAsync(core, script); }
        catch (Exception ex) { return "ERROR " + ex.Message; }
    }

    private static async Task<string> SignOutSecondAsync(Microsoft.Web.WebView2.Wpf.WebView2 browser)
    {
        const string script = """
            (() => {
              const docs = [document];
              for (const frame of Array.from(document.querySelectorAll('iframe'))) {
                try { if (frame.contentDocument) docs.push(frame.contentDocument); } catch (_) {}
              }
              for (const doc of docs) {
                const link = Array.from(doc.querySelectorAll('a, button'))
                  .find(el => (el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase() === 'log out');
                if (link) { setTimeout(() => link.click(), 0); return 'CLICKED'; }
              }
              return 'NO_LINK';
            })()
            """;
        var core = browser.CoreWebView2;
        var clicked = await RunScriptAsync(core, script);
        if (clicked != "CLICKED") return "no Log Out link on " + PathOf(browser.Source);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        var session = new FlooidSessionService();
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            if (session.Evaluate(browser.Source).State != FlooidConnectionState.SignedIn)
                return "Log Out clicked, now at " + PathOf(browser.Source);
        }
        return "Log Out clicked, still at " + PathOf(browser.Source) + " after 10 s";
    }

    private static string LoginScript(string username, string password) => """
        (() => {
          const form = document.getElementById('login');
          if (!form) return 'NO_FORM';
          const user = document.getElementById('Uname');
          const pass = document.getElementById('Pass');
          const button = document.getElementById('basicLoginButton');
          if (!user || !pass) return 'NO_FIELDS';
          const setValue = (element, value) => {
            const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            setter.call(element, value);
            element.dispatchEvent(new Event('input', { bubbles: true }));
            element.dispatchEvent(new Event('change', { bubbles: true }));
          };
          setValue(user, __USER__);
          setValue(pass, __PASS__);
          if (button) { setTimeout(() => button.click(), 0); return 'CLICKED'; }
          setTimeout(() => form.submit(), 0);
          return 'SUBMITTED';
        })()
        """
        .Replace("__USER__", JsonSerializer.Serialize(username))
        .Replace("__PASS__", JsonSerializer.Serialize(password));

    private const string LoginErrorScript = """
        (() => {
          const box = document.getElementById('loginError');
          if (!box) return '';
          return (box.textContent || '').replace(/\s+/g, ' ').trim() || 'Sign in refused';
        })()
        """;

    private static async Task<string> RunScriptAsync(CoreWebView2 core, string script)
    {
        var raw = await core.ExecuteScriptAsync(script);
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return string.Empty;
        try { return JsonSerializer.Deserialize<string>(raw) ?? string.Empty; }
        catch { return raw.Trim().Trim('"'); }
    }

    private static string PathOf(Uri? uri) => uri?.AbsolutePath ?? "no page";

    private static void Log(string text) =>
        AppFiles.AppendDiagnostic($"[{DateTime.Now:HH:mm:ss}] {Prefix}{text}{Environment.NewLine}");
}
