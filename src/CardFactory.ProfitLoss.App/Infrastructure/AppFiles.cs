using System.Diagnostics;
using System.IO;
using System.Text;

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

    // Stage 6B.71: the log is capped at 2 MB. Every writer goes through here.
    // Owner, 16 Nov 2026 (option 1): the log is encrypted - one line per entry, each line the
    // entry encrypted (ProtectedFiles) and written as Base64. Over the cap the oldest lines go
    // and the newest 1.5 MB is kept; a line is a whole entry, so the file never starts
    // mid-entry. Menu > Settings > Diagnostic log saves a readable copy (ReadDiagnostic).
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
                        var lines = File.ReadAllLines(path);
                        var kept = new List<string>();
                        var size = 0;
                        for (var i = lines.Length - 1; i >= 0; i--)
                        {
                            size += lines[i].Length + 2;
                            if (size > DiagnosticKeepChars) break;
                            kept.Insert(0, lines[i]);
                        }
                        kept.Insert(0, EncryptLine("(earlier entries trimmed - the log is kept under 2 MB)" + Environment.NewLine + Environment.NewLine));
                        File.WriteAllLines(path, kept);
                    }
                }
                catch
                {
                    // Trimming is housekeeping; still write the entry.
                }
                File.AppendAllText(path, EncryptLine(text) + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never affect the app.
        }
    }

    private static string EncryptLine(string text) => Convert.ToBase64String(ProtectedFiles.Protect(text));

    /// <summary>The whole log as readable text, for Menu > Settings > Diagnostic log.</summary>
    public static string ReadDiagnostic()
    {
        lock (DiagnosticLock)
        {
            if (!File.Exists(DiagnosticPath)) return string.Empty;
            var text = new StringBuilder();
            foreach (var line in File.ReadAllLines(DiagnosticPath))
            {
                if (line.Length == 0) continue;
                text.Append(DecryptLine(line) ?? line + Environment.NewLine);
            }
            return text.ToString();
        }
    }

    private static string? DecryptLine(string line)
    {
        try
        {
            var blob = Convert.FromBase64String(line);
            return ProtectedFiles.IsEncrypted(blob) ? ProtectedFiles.Unprotect(blob) : null;
        }
        catch
        {
            return null;
        }
    }

    // ---- Owner, 16 Nov 2026 (option 1): nothing readable left on the PC -----------------

    /// <summary>The built-in browser's folders (Flooid sign-in, and the preview window's).</summary>
    private static string BrowserRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardFactory", "ProfitLoss");

    /// <summary>Where the preview window writes the page it shows; deleted after use.</summary>
    public static string TempFolder => Path.Combine(Path.GetTempPath(), "CardFactory-ProfitLoss");

    /// <summary>
    /// Once, at start-up, before any browser opens: the browser's folders from earlier runs
    /// (earlier versions kept cookies and cached pages there) and any preview pages left behind
    /// go; files earlier versions wrote in plain text are encrypted where they are.
    /// </summary>
    public static void TidyAtStartup()
    {
        TryDeleteFolder(BrowserRoot);
        TryDeleteFolder(TempFolder);
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("flooid-credentials.dat", StringComparison.OrdinalIgnoreCase)) continue;   // already encrypted
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) { TryDeleteFile(file); continue; }
                if (name.Equals(DiagnosticFileName, StringComparison.OrdinalIgnoreCase)) { EncryptOldLog(file); continue; }
                ProtectedFiles.EnsureEncrypted(file);
            }
        }
        catch
        {
            // Tidying must never stop the app starting.
        }
    }

    /// <summary>Plain-text lines from an earlier version become encrypted entries (each run of
    /// plain lines one entry); lines already encrypted are kept as they are.</summary>
    private static void EncryptOldLog(string path)
    {
        try
        {
            lock (DiagnosticLock)
            {
                var lines = File.ReadAllLines(path);
                if (lines.All(l => l.Length == 0 || DecryptLine(l) is not null)) return;
                var output = new List<string>();
                var plain = new StringBuilder();
                void FlushPlain()
                {
                    if (plain.Length == 0) return;
                    output.Add(EncryptLine(plain.ToString()));
                    plain.Clear();
                }
                foreach (var line in lines)
                {
                    if (line.Length > 0 && DecryptLine(line) is not null) { FlushPlain(); output.Add(line); }
                    else if (line.Length > 0 || plain.Length > 0) plain.Append(line).Append(Environment.NewLine);
                }
                FlushPlain();
                var temp = path + ".tmp";
                File.WriteAllLines(temp, output);
                File.Move(temp, path, overwrite: true);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Reset everything (owner, 16 Nov 2026, concept R1): every file the app has made on this
    /// PC is deleted now, and once the app has closed a hidden PowerShell removes what was still
    /// in use (the browser's folders) and anything written while closing. PDFs the person saved
    /// themselves and the app itself are not touched.
    /// </summary>
    public static void RemoveEverything()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CardFactory-ProfitLoss");
        TryDeleteFolder(appData);
        TryDeleteFolder(TempFolder);
        TryDeleteFolder(BrowserRoot);

        try
        {
            static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
            var paths = string.Join(",", new[] { appData, TempFolder, BrowserRoot }.Select(Quote));
            var script = $"Wait-Process -Id {Environment.ProcessId} -Timeout 60 -ErrorAction SilentlyContinue; "
                       + "Start-Sleep -Seconds 3; "
                       + $"Remove-Item -LiteralPath {paths} -Recurse -Force -ErrorAction SilentlyContinue";
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", script })
                start.ArgumentList.Add(arg);
            Process.Start(start);
        }
        catch
        {
            // Whatever was left is removed by TidyAtStartup the next time the app opens.
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch
        {
            // Something still in use: delete what can be deleted.
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) TryDeleteFile(file);
            }
            catch { }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
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
                // Its entries go into the (encrypted) log; the desktop copy goes.
                AppendDiagnostic(File.ReadAllText(oldDiagnostic));
                File.Delete(oldDiagnostic);
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
