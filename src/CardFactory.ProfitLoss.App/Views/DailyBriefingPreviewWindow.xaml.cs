using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardFactory.ProfitLoss.App.Services;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// Export to Daily Briefing: the store's sheet filled in with the day's figures, to print
/// or save as a PDF. <see cref="Outcome"/> says what was done, for the status line.
/// </summary>
public partial class DailyBriefingPreviewWindow : Window
{
    private const string PdfPrinterName = "Microsoft Print to PDF";
    private readonly DailyBriefingSheet.Data _data;

    public string? Outcome { get; private set; }

    public DailyBriefingPreviewWindow(DailyBriefingSheet.Data data)
    {
        InitializeComponent();
        _data = data;
        TitleText.Text = $"Daily Briefing · {data.Date:ddd dd/MM/yyyy}";
        Viewer.Document = DailyBriefingSheet.Build(data);

        var missing = Missing(data);
        if (missing.Count > 0)
        {
            MissingText.Text = "Left blank, Flooid had no figures for: " + string.Join(", ", missing);
            MissingBanner.Visibility = Visibility.Visible;
        }

        // Fits a small till screen: never taller or wider than the space above the taskbar.
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        Loaded += (_, _) => Viewer.FitToWidth();
    }

    public static List<string> Missing(DailyBriefingSheet.Data data)
    {
        var missing = new List<string>();
        if (data.Hours.Count == 0) missing.Add("hourly figures");
        if (data.Reports is null) missing.Add("voids and refunds");
        if (data.Team.Count == 0) missing.Add("team figures");
        return missing;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PrintDialog();
            dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
            if (dialog.ShowDialog() != true) return;
            Send(dialog);
            Outcome = $"Daily Briefing for {_data.Date:dd/MM/yyyy} sent to {dialog.PrintQueue?.Name ?? "the printer"}";
            Close();
        }
        catch (Exception ex)
        {
            ShowProblem("The Daily Briefing could not be printed.", ex.Message);
        }
    }

    // WPF cannot write a PDF itself; Windows' own PDF printer can, and asks where to save it.
    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var server = new LocalPrintServer();
            var queue = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local })
                .FirstOrDefault(q => string.Equals(q.Name, PdfPrinterName, StringComparison.OrdinalIgnoreCase));
            if (queue is null)
            {
                ShowProblem("Microsoft Print to PDF isn't on this computer.",
                    "Press Print instead and pick any PDF printer in the list, or turn on Microsoft Print to PDF in Windows Features.");
                return;
            }

            var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = queue.DefaultPrintTicket };
            Send(dialog);
            // The PDF printer asks for the file name itself, so whether it was saved is not known here.
            Outcome = $"Daily Briefing for {_data.Date:dd/MM/yyyy} sent to Microsoft Print to PDF";
            Close();
        }
        catch (Exception ex)
        {
            ShowProblem("The Daily Briefing could not be saved as a PDF.", ex.Message);
        }
    }

    // A fresh copy of the sheet for each job: the one on screen belongs to the viewer.
    private void Send(PrintDialog dialog)
    {
        dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
        var document = DailyBriefingSheet.Build(_data);
        dialog.PrintDocument(document.DocumentPaginator, $"Daily Briefing {_data.Date:dd-MM-yyyy}");
    }

    private void ShowProblem(string question, string detail)
    {
        new ThemedConfirmationWindow("Daily Briefing", question, detail, "OK",
            subtitle: "Nothing was printed or saved", showCancel: false) { Owner = this }.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
