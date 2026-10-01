using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CardFactory.ProfitLoss.App.Infrastructure;

namespace CardFactory.ProfitLoss.App.Services;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string ZipUrl, string ChecksumUrl);

/// <summary>Step 0 downloading (with bytes), 1 checking the download, 2 installing and restarting.</summary>
public sealed record UpdateProgress(int Step, long Bytes = 0, long Total = 0);

/// <summary>
/// Checks this repository's GitHub releases for a newer numbered version, and installs it.
/// Only full releases tagged vX.Y.Z count; test builds are never published as releases. The repository is public, so no token is involved.
///
/// Installing: download the release zip, check it against the release's .sha256 file,
/// take the exe out of it, rename the running exe aside (Windows allows renaming a running
/// program, not overwriting it), put the new one in its place and start it. The new copy
/// waits for this one to exit, then deletes the renamed old exe.
/// </summary>
public static class UpdateService
{
    private const string Repository = "Sarroxan/CardFactory-ProfitLoss";
    private const string ZipName = "CardFactory-ProfitLoss-win-x64.zip";
    private const string ExeName = "CardFactory.ProfitLoss.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CardFactory-ProfitLoss-Updater");
        return client;
    }

    public static Version CurrentVersion => Normalise(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    private static Version Normalise(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    public static string Display(Version v) => v.Major + "." + v.Minor + "." + v.Build;

    /// <summary>The newest release if it is newer than this copy; null otherwise or on any failure.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var response = await Http.GetAsync("https://api.github.com/repos/" + Repository + "/releases/latest");
            if (!response.IsSuccessStatusCode) { Log("check: GitHub answered " + (int)response.StatusCode); return null; }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) { Log("check: latest tag '" + tag + "' is not a version"); return null; }

            var latest = Normalise(parsed);
            if (latest <= CurrentVersion) { Log("check: up to date (" + Display(CurrentVersion) + ", latest " + Display(latest) + ")"); return null; }

            string? zip = null, checksum = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (name == ZipName) zip = url;
                else if (name == ZipName + ".sha256") checksum = url;
            }
            if (zip is null || checksum is null) { Log("check: " + tag + " is missing its zip or checksum"); return null; }

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
            Log("check: " + Display(latest) + " available (this is " + Display(CurrentVersion) + ")");
            return new UpdateInfo(latest, tag, notes.Trim(), zip, checksum);
        }
        catch (Exception ex)
        {
            Log("check failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>Downloads, verifies and swaps in the update, then starts it. The caller shuts down.</summary>
    public static async Task ApplyAsync(UpdateInfo update, IProgress<UpdateProgress> progress)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Could not tell where the app is running from.");
        var work = Path.Combine(Path.GetTempPath(), "CardFactory-ProfitLoss-update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        progress.Report(new UpdateProgress(0));
        var zipPath = Path.Combine(work, ZipName);
        using (var response = await Http.GetAsync(update.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var download = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0, lastReported = 0;
            int read;
            while ((read = await download.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (done - lastReported >= 256 * 1024 || done == total)
                {
                    lastReported = done;
                    progress.Report(new UpdateProgress(0, done, total));
                }
            }
        }

        progress.Report(new UpdateProgress(1));
        var expected = (await Http.GetStringAsync(update.ChecksumUrl)).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
        string actual;
        await using (var file = File.OpenRead(zipPath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
        if (expected.Length != 64 || expected != actual)
        {
            Log("apply: checksum mismatch, expected " + expected + " got " + actual);
            throw new InvalidOperationException("The download did not match its checksum, so nothing was changed. Try again later.");
        }

        var newExe = Path.Combine(work, ExeName);
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var entry = archive.GetEntry(ExeName) ?? throw new InvalidOperationException("The update did not contain " + ExeName + ".");
            entry.ExtractToFile(newExe, true);
        }

        progress.Report(new UpdateProgress(2));
        var old = current + ".old";
        File.Move(current, old, true);
        try
        {
            File.Copy(newExe, current, false);
        }
        catch
        {
            File.Move(old, current, true);   // put the running copy back where it was
            throw;
        }

        Log("apply: " + Display(CurrentVersion) + " -> " + Display(update.Version) + " installed; restarting");
        Process.Start(new ProcessStartInfo(current, "--wait-for " + Environment.ProcessId) { UseShellExecute = false });
    }

    /// <summary>At start-up: if started by an update, wait for the old copy to exit, then remove it.</summary>
    public static void FinishPendingUpdate(string[] args)
    {
        try
        {
            var i = Array.IndexOf(args, "--wait-for");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
            {
                try { using var previous = Process.GetProcessById(pid); previous.WaitForExit(15000); }
                catch { /* already gone */ }
            }

            var old = (Environment.ProcessPath ?? string.Empty) + ".old";
            for (var attempt = 0; attempt < 10 && File.Exists(old); attempt++)
            {
                try { File.Delete(old); }
                catch { Thread.Sleep(300); }
            }
            if (i >= 0) Log("started after update as " + Display(CurrentVersion));
        }
        catch
        {
            // Never stop the app starting.
        }
    }

    private static void Log(string step) =>
        AppFiles.AppendDiagnostic("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - update: " + step + " ===\r\n");
}
