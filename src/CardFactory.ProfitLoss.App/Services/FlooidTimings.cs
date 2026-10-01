using System.Globalization;
using System.IO;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// Stage 6A.96. Every fixed wait in the Flooid automation is multiplied by a scale read
/// from a plain text file on the desktop, so the shortest workable timings can be found
/// by editing a number and restarting rather than by rebuilding. A build cycle is about
/// four minutes plus a download; this is about ten seconds.
///
/// CardFactory-PL-timings.txt, one line:
///     scale=0.35
///
/// 1.0 is exactly the behaviour these waits had before this existed, so writing 1.0 - or
/// deleting the file - is the rollback. 0 removes the fixed waits entirely; the adaptive
/// polls and the real page loads remain, so it is a floor rather than a disaster.
///
/// What this does NOT scale, deliberately:
///   - Timeouts. Shrinking those turns a slow day into a failure rather than a wait.
///   - The five per-level picker RPC caps (900ms, and 2500ms on the last). Those bound an
///     adaptive wait that settles in about 80ms, so they cost nothing on a good run and
///     are the safety net on a bad one. The 1200ms after selecting level 5 IS scaled, as
///     of 6A.97: the diagnostic proved nothing ever completes there, so it was a blind
///     sleep rather than a bound on a real signal.
/// </summary>
public static class FlooidTimings
{
    private const double DefaultScale = 1.0;

    // Stage 6B.55: Fast is the default. Scale 0 ran a full working day on 16/09 with no
    // failures (12.0-12.8 s against 16.6 s), and on 24/09 a PC was still on Safe only
    // because nobody had chosen - the settings file had just been created at 1.0. A
    // missing file, or that untouched 1.0 template, now means Fast. An unreadable file
    // still falls back to Safe (DefaultScale): a malformed file must never make
    // retrieval faster than it was proven to work at. Choosing Safe in the menu is kept.
    private const double NewInstallScale = 0.0;
    private const double MinScale = 0.0;
    private const double MaxScale = 3.0;

    public static double Scale { get; private set; } = DefaultScale;

    /// <summary>Where the value came from, so the diagnostic can say rather than imply.</summary>
    public static string Source { get; private set; } = "default 1.0 (not yet read)";

    /// <summary>The folder searched. Same one the diagnostic file is written to.</summary>
    // Stage 6B.52: the app's own folder, not the desktop - the setting is changed with the
    // Fast/Safe control in the menu now, not by editing the file.
    public static string SettingsFolder => CardFactory.ProfitLoss.App.Infrastructure.AppFiles.Folder;

    /// <summary>The name to aim for, though any file starting with it is accepted.</summary>
    public const string SettingsFileName = "CardFactory-PL-timings.txt";

    /// <summary>
    /// Re-read at the start of every retrieval, so a change takes effect on the next pull
    /// without restarting. Any failure falls back to 1.0 - a malformed file must never be
    /// able to make retrieval faster than it was proven to work at.
    /// </summary>
    public static void Refresh()
    {
        try
        {
            var folder = SettingsFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                Scale = DefaultScale;
                Source = "default 1.0 (no settings folder)";
                return;
            }

            // Stage 6A.98: match any file whose name starts with CardFactory-PL-timings,
            // not one exact name. Five runs on 16/09 all reported "no file on desktop"
            // while the diagnostic was being written to that same folder, so the folder
            // was never in doubt - the name was. Windows hides known extensions by
            // default, so a file saved from Notepad as CardFactory-PL-timings.txt lands
            // as CardFactory-PL-timings.txt.txt and looks correct in Explorer.
            var matches = Directory.GetFiles(folder, "CardFactory-PL-timings*");
            if (matches.Length == 0)
            {
                // Stage 6A.99: write it rather than expect it. Nobody had a file because
                // nothing ever created one, and asking someone to make a text file with
                // an exact name on a machine that hides extensions is how 6A.98 happened.
                // Since 6B.55 the file it writes says Fast (see NewInstallScale).
                Scale = NewInstallScale;
                Source = TrySetScale(NewInstallScale)
                    ? "default Fast (settings file just created)"
                    : "default Fast (could not write CardFactory-PL-timings.txt in " + folder + ")";
                return;
            }

            Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
            var path = matches[0];

            // Stage 6B.55: the template older builds wrote (6A.99) is nobody's choice - the
            // menu writes its own format. Left at its 1.0, it is replaced with Fast.
            var text0 = File.ReadAllText(path);
            if (text0.Contains("# Every fixed wait in the Flooid automation is multiplied by this number.", StringComparison.Ordinal)
                && !text0.Contains("# Written by the application.", StringComparison.Ordinal)
                && System.Text.RegularExpressions.Regex.IsMatch(text0, @"(?m)^\s*scale\s*=\s*1(\.0+)?\s*$"))
            {
                File.Delete(path);
                if (TrySetScale(NewInstallScale))
                {
                    Scale = NewInstallScale;
                    Source = "default Fast (replaced the untouched 1.0 template)";
                    return;
                }
                path = Path.Combine(folder, SettingsFileName);
                if (!File.Exists(path))
                {
                    Scale = DefaultScale;
                    Source = "default 1.0 (could not rewrite the template)";
                    return;
                }
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                // "scale=0.35" or just "0.35" - the second because it is what someone
                // types when they are moving fast, and refusing it helps nobody.
                var text = line;
                var equals = line.IndexOf('=');
                if (equals > 0)
                {
                    if (!line[..equals].Trim().Equals("scale", StringComparison.OrdinalIgnoreCase)) continue;
                    text = line[(equals + 1)..].Trim();
                }

                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    Scale = DefaultScale;
                    Source = "default 1.0 (could not read \"" + line + "\" in " + Path.GetFileName(path) + ")";
                    return;
                }

                var clamped = Math.Clamp(value, MinScale, MaxScale);
                Scale = clamped;
                Source = Math.Abs(clamped - value) < 0.0001
                    ? Path.GetFileName(path)
                    : Path.GetFileName(path) + ", clamped from " + value.ToString(CultureInfo.InvariantCulture);
                return;
            }

            Scale = DefaultScale;
            Source = "default 1.0 (no usable line in " + Path.GetFileName(path) + ")";
        }
        catch (Exception ex)
        {
            Scale = DefaultScale;
            Source = "default 1.0 (" + ex.GetType().Name + ")";
        }
    }

    /// <summary>
    /// Stage 6B.30: write the scale from inside the application.
    ///
    /// The setting has lived only in a text file on the desktop since 6A.96, and on
    /// 22/09 that file had vanished - putting a machine back on the slow timings with
    /// nothing on screen saying so. The file stays as the format, so anything already
    /// written by hand still works and an unusual value can still be set there; this
    /// just means nobody has to keep a text file alive to get the fast timings.
    /// </summary>
    public static bool TrySetScale(double scale)
    {
        try
        {
            var clamped = Math.Clamp(scale, MinScale, MaxScale);
            var folder = SettingsFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;

            var path = Path.Combine(folder, SettingsFileName);
            var lines = new[]
            {
                "# Card Factory Profit & Loss - retrieval timing",
                "#",
                "# Written by the application. Settings menu, Retrieval speed.",
                "#   0    Fast - no fixed waits. About 4 seconds quicker per refresh.",
                "#   1.0  Safe - the original proven timings.",
                "#",
                "# Anything unreadable falls back to 1.0. Values are clamped to 0 - 3.",
                "",
                "scale=" + clamped.ToString("0.##", CultureInfo.InvariantCulture),
                ""
            };

            File.WriteAllText(path, string.Join(Environment.NewLine, lines));
            Scale = clamped;
            Source = SettingsFileName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stage 6B.00: the gap between attempts in a polling loop, scaled but floored.
    ///
    /// A one-shot sleep can safely go to zero - it is pure waiting. A poll interval
    /// cannot: at zero the loop stops pausing between attempts and runs script against
    /// the WebView continuously. The scale=0 runs on 16/09 worked, and part of the gain
    /// was genuine (noticing a page had loaded sooner), but they were spinning to get it.
    /// 40ms keeps almost all of that - the ticks were finishing about 200ms earlier than
    /// at 120ms polling - without burning a core on a loaded machine.
    /// </summary>
    public static Task PollAsync(int milliseconds)
    {
        var scaled = (int)Math.Round(milliseconds * Scale);
        var floor = Math.Min(milliseconds, MinimumPollMilliseconds);
        return Task.Delay(Math.Max(scaled, floor));
    }

    private const int MinimumPollMilliseconds = 40;

    /// <summary>A fixed wait, scaled. Zero or less does not await at all.</summary>
    public static Task PauseAsync(int milliseconds)
    {
        var scaled = (int)Math.Round(milliseconds * Scale);
        return scaled <= 0 ? Task.CompletedTask : Task.Delay(scaled);
    }
}
