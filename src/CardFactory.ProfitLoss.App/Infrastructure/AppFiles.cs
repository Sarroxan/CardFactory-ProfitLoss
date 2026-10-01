using System.IO;

namespace CardFactory.ProfitLoss.App.Infrastructure;

/// <summary>
/// Stage 6B.52: where the application keeps its own working files. The diagnostic log
/// and the retrieval-speed setting used to be written to the desktop, which made sense
/// while they had to be found and edited by hand. Both are now reached from inside the
/// app (the Fast/Safe control; the log is found in the folder itself) in the menu - so they live in
/// %APPDATA%\CardFactory-ProfitLoss beside the remembered username and store.
/// </summary>
internal static class AppFiles
{
    public const string DiagnosticFileName = "CardFactory-PL-diagnostic.txt";

    public static string Folder
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CardFactory-ProfitLoss");
            try { Directory.CreateDirectory(folder); } catch { }
            return folder;
        }
    }

    public static string DiagnosticPath => Path.Combine(Folder, DiagnosticFileName);

    // Stage 6B.71: the log is capped at 2 MB. Every writer goes through here. Over the cap,
    // the oldest entries go: the newest 1.5 MB is kept, cut forward to the next "=== "
    // header so the file never starts mid-entry. Trimming to 1.5 rather than exactly 2 MB
    // means it happens once per ~0.5 MB of logging, not on every write.
    private const long DiagnosticCapBytes = 2L * 1024 * 1024;
    private const int DiagnosticKeepChars = 1536 * 1024;
    private static readonly object DiagnosticLock = new();

    public static void AppendDiagnostic(string text)
    {
        try
        {
            lock (DiagnosticLock)
            {
                var path = DiagnosticPath;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length + text.Length > DiagnosticCapBytes)
                    {
                        var all = File.ReadAllText(path);
                        var kept = all.Length > DiagnosticKeepChars ? all.Substring(all.Length - DiagnosticKeepChars) : all;
                        var next = kept.IndexOf("=== ", StringComparison.Ordinal);
                        if (next > 0) kept = kept.Substring(next);
                        File.WriteAllText(path, "(earlier entries trimmed - the log is kept under 2 MB)" + Environment.NewLine + Environment.NewLine + kept);
                    }
                }
                catch
                {
                    // Trimming is housekeeping; still write the entry.
                }
                File.AppendAllText(path, text);
            }
        }
        catch
        {
            // Diagnostics must never affect the app.
        }
    }

    /// <summary>
    /// Once, at start-up: move files earlier builds left on the desktop into the app's
    /// folder, so nothing is lost - a Fast setting stays Fast - and nothing is left behind.
    /// </summary>
    public static void MoveOffDesktop()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop)) return;
            var folder = Folder;

            var oldDiagnostic = Path.Combine(desktop, DiagnosticFileName);
            if (File.Exists(oldDiagnostic))
            {
                if (File.Exists(DiagnosticPath))
                {
                    // Keep both histories, older first.
                    var newer = File.ReadAllText(DiagnosticPath);
                    File.WriteAllText(DiagnosticPath, File.ReadAllText(oldDiagnostic) + newer);
                    File.Delete(oldDiagnostic);
                }
                else
                {
                    File.Move(oldDiagnostic, DiagnosticPath);
                }
            }

            // Any name starting CardFactory-PL-timings - see 6A.98 on hidden extensions.
            var oldTimings = Directory.GetFiles(desktop, "CardFactory-PL-timings*");
            var keptTimings = Path.Combine(folder, "CardFactory-PL-timings.txt");
            foreach (var file in oldTimings)
            {
                if (!File.Exists(keptTimings)) File.Move(file, keptTimings);
                else File.Delete(file);
            }
        }
        catch
        {
            // Tidying must never stop the app starting.
        }
    }
}
