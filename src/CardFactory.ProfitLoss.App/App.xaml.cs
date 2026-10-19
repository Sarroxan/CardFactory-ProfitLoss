using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.App.Views;

namespace CardFactory.ProfitLoss.App;

public partial class App : Application
{
    // One copy at a time. Two copies would share the Flooid browser session and the app's
    // files, so they could sign each other out or collide mid-retrieval. Opening the app
    // again brings the copy already running to the front instead. "Local\" is per Windows
    // sign-in, so two people signed in to the same PC each still get their own.
    private const string SingleInstanceName = @"Local\CardFactory-ProfitLoss-SingleInstance";
    private Mutex? _singleInstance;
    private bool _showingCrashMessage;

    // Stage 6B.52: before any window can write a diagnostic, move the files earlier builds
    // left on the desktop into the app's own folder.
    protected override void OnStartup(StartupEventArgs e)
    {
        // Owner, 19 Oct 2026: the target boxes update their figure on every keystroke but keep
        // showing what was typed. Must be set before the first TextBox exists. Only the three
        // target boxes convert their text, so nothing else is affected.
        try { FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty = false; }
        catch (InvalidOperationException) { /* already sealed: keep the default */ }

        Services.UpdateService.FinishPendingUpdate(e.Args);   // after an update: wait for the old copy, remove it

        if (!TryBecomeTheOnlyCopy(afterUpdate: Array.IndexOf(e.Args, "--wait-for") >= 0))
        {
            BringOtherCopyToFront();
            Shutdown();
            return;
        }

        // The safety net: an error nothing else caught is written to the diagnostic log
        // (with its stack trace) and explained, instead of the window vanishing silently.
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        AppFiles.MoveOffDesktop();
        base.OnStartup(e);

        // Created here rather than by StartupUri, so a second copy never opens a window.
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); } catch { /* not held */ }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private bool TryBecomeTheOnlyCopy(bool afterUpdate)
    {
        try
        {
            _singleInstance = new Mutex(false, SingleInstanceName);
            // Straight after an update the old copy may still be closing (FinishPendingUpdate
            // waits for it, but give it a little longer); otherwise any copy holding it is
            // another one the user opened.
            if (_singleInstance.WaitOne(afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero)) return true;
        }
        catch (AbandonedMutexException)
        {
            return true;   // the previous copy ended without releasing it; it is ours now
        }
        catch (Exception ex)
        {
            Log("single copy check failed, starting anyway: " + ex.Message);
            return true;   // never refuse to start because of the check itself
        }

        _singleInstance.Dispose();
        _singleInstance = null;
        Log("another copy is already running; bringing it to the front");
        return false;
    }

    private static void BringOtherCopyToFront()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(current.ProcessName))
            {
                using (other)
                {
                    if (other.Id == current.Id || other.SessionId != current.SessionId) continue;
                    var handle = other.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;
                    if (IsIconic(handle)) ShowWindow(handle, SwRestore);
                    SetForegroundWindow(handle);
                    return;
                }
            }
        }
        catch
        {
            // Best effort: the other copy is running either way.
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("unexpected error (app kept running)\r\n" + e.Exception);
        e.Handled = true;

        // One box at a time: if something keeps failing, the log has every occurrence.
        if (_showingCrashMessage) return;
        _showingCrashMessage = true;
        try
        {
            var box = new ThemedConfirmationWindow(
                "Something went wrong",
                "The app hit an unexpected problem and has carried on.",
                "If anything looks wrong, close the app and open it again. The details were saved to "
                    + "CardFactory-PL-diagnostic.txt in %APPDATA%\\CardFactory-ProfitLoss - send it with "
                    + "any fault report.\r\n\r\n" + e.Exception.Message,
                "OK",
                subtitle: "Details saved to the diagnostic log",
                showCancel: false);
            if (MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, box)) box.Owner = owner;
            box.ShowDialog();
        }
        catch (Exception boxError)
        {
            Log("could not show the error box: " + boxError.Message);
        }
        finally
        {
            _showingCrashMessage = false;
        }
    }

    // Errors on other threads cannot be recovered from; record them before the app closes.
    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log("fatal error, the app is closing\r\n" + e.ExceptionObject);

    // Background work started and not awaited (such as the update check) whose error
    // nothing looked at. Recorded, and marked as seen so it never closes the app.
    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log("background task error\r\n" + e.Exception);
        e.SetObserved();
    }

    private static void Log(string text) =>
        AppFiles.AppendDiagnostic("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - app: " + text + " ===\r\n");

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
}
