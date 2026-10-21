using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// "Open as a PDF" (owner, 21 Oct 2026, concept P1): the report the old "Export as a web page"
/// saved is turned into a PDF with WebView2's own PDF printer and opened at once in the
/// computer's PDF viewer (on Windows 11, normally Edge). No save box. The files go in the
/// temp folder and anything older than a day is tidied away.
///
/// A separate, hidden WebView2 with its own data folder is used, so the Flooid browser and
/// its sign-in are never touched.
/// </summary>
internal static class ReportPdf
{
    public static async Task<string> OpenAsync(Window owner, string html, string fileStem)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CardFactory-ProfitLoss");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder))
        {
            try { if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-1)) File.Delete(old); }
            catch { /* in use or gone: leave it */ }
        }

        // A time in the name: a PDF still open in the browser is never overwritten.
        var htmlPath = Path.Combine(folder, $"{fileStem}-{DateTime.Now:HHmmss}.html");
        var pdfPath = Path.ChangeExtension(htmlPath, ".pdf");
        await File.WriteAllTextAsync(htmlPath, html, Encoding.UTF8);

        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CardFactory", "ProfitLoss", "WebView2-Pdf");
        Directory.CreateDirectory(dataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
        var controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(owner).Handle);
        try
        {
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            core.Navigate(new Uri(htmlPath).AbsoluteUri);
            if (await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(20))) != loaded.Task || !loaded.Task.Result)
                throw new InvalidOperationException("The report page could not be prepared.");

            var settings = environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;       // keep the blue header and tinted rows
            settings.ShouldPrintHeaderAndFooter = false;  // no file path or date in the margins
            if (!await core.PrintToPdfAsync(pdfPath, settings))
                throw new InvalidOperationException("The PDF could not be made.");
        }
        finally
        {
            controller.Close();
            try { File.Delete(htmlPath); } catch { /* tidied next time */ }
        }

        Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
        return pdfPath;
    }
}
