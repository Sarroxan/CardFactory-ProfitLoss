using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly FixedDocument _document;

    // D3 (owner, 16 Oct 2026). Fit page is a mode rather than a number, so the whole sheet stays
    // in view whatever the window's size; the zoom steps are fixed sizes (1.0 = 100%).
    private static readonly double[] ZoomSteps = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
    private bool _fitPage = true;
    private double _zoom = 1.0;
    private int _page;

    public string? Outcome { get; private set; }

    public DailyBriefingPreviewWindow(DailyBriefingSheet.Data data)
    {
        InitializeComponent();
        _data = data;
        TitleText.Text = $"Daily Briefing · {data.Date:ddd dd/MM/yyyy}";
        _document = DailyBriefingSheet.Build(data);
        PageView.DocumentPaginator = _document.DocumentPaginator;

        var missing = Missing(data);
        if (missing.Count > 0)
        {
            MissingText.Text = "Left blank, Flooid had no figures for: " + string.Join(", ", missing);
            MissingBanner.Visibility = Visibility.Visible;
        }

        // D3: about 90% of the screen above the taskbar (no more than 1600 x 1100), centred on
        // the app; the page is fitted inside it, so it never needs to be the whole screen.
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Math.Round(area.Width * 0.9), 1600);
        Height = Math.Min(Math.Round(area.Height * 0.9), 1100);
        Loaded += (_, _) => ShowPage(0);
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private int PageCount => _document.Pages.Count;

    private void ShowPage(int index)
    {
        _page = Math.Clamp(index, 0, Math.Max(0, PageCount - 1));
        PageView.PageNumber = _page;
        PageText.Text = $"Page {_page + 1} of {PageCount}";
        PreviousPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = _page < PageCount - 1;
        PageScroller.ScrollToHome();
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (_fitPage)
        {
            // The whole page, as large as the space allows; no scroll bars.
            PageScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PageScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PageView.Stretch = Stretch.Uniform;
            PageFrame.LayoutTransform = Transform.Identity;
        }
        else
        {
            PageScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            PageScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            PageView.Stretch = Stretch.None;
            PageFrame.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        }
        FitPageButton.Tag = _fitPage ? "On" : null;
        ActualSizeButton.Tag = !_fitPage && Math.Abs(_zoom - 1.0) < 0.001 ? "On" : null;
        ActualSizeButton.Content = _fitPage || Math.Abs(_zoom - 1.0) < 0.001 ? "100%" : $"{_zoom * 100:0}%";
        ZoomOutButton.IsEnabled = _fitPage || _zoom > ZoomSteps[0] + 0.001;
        ZoomInButton.IsEnabled = _fitPage || _zoom < ZoomSteps[^1] - 0.001;
    }

    /// <summary>The size Fit page is showing now, so − and + step on from what is on screen.</summary>
    private double CurrentFitScale()
    {
        var page = _document.DocumentPaginator.PageSize;   // 1 = 100%
        var width = Math.Max(1, PageScroller.ActualWidth - PageFrame.Margin.Left - PageFrame.Margin.Right);
        var height = Math.Max(1, PageScroller.ActualHeight - PageFrame.Margin.Top - PageFrame.Margin.Bottom);
        return page.Width > 0 && page.Height > 0 ? Math.Min(width / page.Width, height / page.Height) : 1.0;
    }

    private void FitPage_Click(object sender, RoutedEventArgs e)
    {
        _fitPage = true;
        ApplyZoom();
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        _fitPage = false;
        _zoom = 1.0;
        ApplyZoom();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        var from = _fitPage ? CurrentFitScale() : _zoom;
        _zoom = ZoomSteps.FirstOrDefault(step => step > from + 0.001, ZoomSteps[^1]);
        _fitPage = false;
        ApplyZoom();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        var from = _fitPage ? CurrentFitScale() : _zoom;
        _zoom = ZoomSteps.LastOrDefault(step => step < from - 0.001, ZoomSteps[0]);
        _fitPage = false;
        ApplyZoom();
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ShowPage(_page - 1);

    private void NextPage_Click(object sender, RoutedEventArgs e) => ShowPage(_page + 1);

    // Fit page: the wheel turns the page. Zoomed in: it scrolls, as usual.
    private void PageScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_fitPage) return;
        ShowPage(e.Delta < 0 ? _page + 1 : _page - 1);
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.PageDown when _fitPage || e.Key == Key.PageDown:
                ShowPage(_page + 1); e.Handled = true; break;
            case Key.Left or Key.PageUp when _fitPage || e.Key == Key.PageUp:
                ShowPage(_page - 1); e.Handled = true; break;
        }
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
