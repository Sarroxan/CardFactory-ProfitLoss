using System.IO;
using System.Printing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CardFactory.ProfitLoss.App.Services;
using Microsoft.Web.WebView2.Core;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// Export to Daily Briefing: the store's sheet filled in with the day's figures, to print
/// or save as a PDF. <see cref="Outcome"/> says what was done, for the status line.
/// Owner, 21 Oct 2026 (concept V1): Print summary opens in this same preview first.
/// </summary>
public partial class DailyBriefingPreviewWindow : Window
{
    private const string PdfPrinterName = "Microsoft Print to PDF";

    /// <summary>Builds the pages. Given the printer's printable area when printing; null
    /// for the preview on screen.</summary>
    private readonly Func<Size?, DocumentPaginator>? _build;
    private readonly DocumentPaginator? _paginator;

    // Owner, 7 Nov 2026: the Team Scorecard opens in this same window. It is a web page (the
    // P1 / W1 design), so it is shown and printed by a browser inside the window rather than
    // drawn as pages: _html is set for it, and the zoom switch and page arrows are hidden.
    private readonly string? _html;
    private readonly string? _fileStem;
    private CoreWebView2Environment? _htmlEnvironment;
    private bool IsWebPage => _html is not null;
    private readonly PageOrientation _orientation;
    private readonly string _what;      // "Daily Briefing for 21/10/2026", for the status line
    private readonly string _jobName;   // the print queue's job name

    // D3 (owner, 16 Oct 2026). Fit page is a mode rather than a number, so the whole sheet stays
    // in view whatever the window's size; the zoom steps are fixed sizes (1.0 = 100%).
    private static readonly double[] ZoomSteps = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
    private bool _fitPage = true;
    private double _zoom = 1.0;
    private int _page;

    public string? Outcome { get; private set; }

    public DailyBriefingPreviewWindow(DailyBriefingSheet.Data data)
        : this($"Daily Briefing · {data.Date:ddd dd/MM/yyyy}", $"Daily Briefing for {data.Date:dd/MM/yyyy}",
               $"Daily Briefing {data.Date:dd-MM-yyyy}", PageOrientation.Landscape,
               _ => DailyBriefingSheet.Build(data).DocumentPaginator)
    {
        var missing = Missing(data);
        if (missing.Count > 0)
        {
            MissingText.Text = "Left blank, Flooid had no figures for: " + string.Join(", ", missing);
            MissingBanner.Visibility = Visibility.Visible;
        }
    }

    /// <param name="build">Fresh pages each call: one for the screen, one per print job.</param>
    public DailyBriefingPreviewWindow(string title, string what, string jobName, PageOrientation orientation,
        Func<Size?, DocumentPaginator> build)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        _what = what;
        _jobName = jobName;
        _orientation = orientation;
        _build = build;
        _paginator = build(null);
        if (!_paginator.IsPageCountValid) _paginator.ComputePageCount();
        PageView.DocumentPaginator = _paginator;

        // D3: about 90% of the screen above the taskbar (no more than 1600 x 1100), centred on
        // the app; the page is fitted inside it, so it never needs to be the whole screen.
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Math.Round(area.Width * 0.9), 1600);
        Height = Math.Min(Math.Round(area.Height * 0.9), 1100);
        _printChoices = PrintSettings.Load();
        ShowPrintChoices();
        Loaded += (_, _) => ShowPage(0);
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    /// <summary>A web page (the Team Scorecard), A4 portrait.</summary>
    public DailyBriefingPreviewWindow(string title, string what, string fileStem, string html)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        _what = what;
        _jobName = fileStem;
        _fileStem = fileStem;
        _orientation = PageOrientation.Portrait;
        _html = html;

        ZoomBar.Visibility = Visibility.Collapsed;
        PagerBar.Visibility = Visibility.Collapsed;
        PageScroller.Visibility = Visibility.Collapsed;
        HtmlView.Visibility = Visibility.Visible;

        var area = SystemParameters.WorkArea;
        Width = Math.Min(Math.Round(area.Width * 0.9), 1600);
        Height = Math.Min(Math.Round(area.Height * 0.9), 1100);
        _printChoices = PrintSettings.Load();
        ShowPrintChoices();
        Loaded += async (_, _) => await ShowWebPageAsync();
    }

    // On screen only: the page as a white A4 sheet on the grey, fitted into the window. None of
    // this applies when it is printed or saved (print uses the page's own @page A4 settings).
    private const string ScreenOnly =
        "<style>@media screen{html{background:#EDF1F6}body{width:210mm;min-height:297mm;margin:18px auto;padding:9mm;" +
        "background:#fff;box-shadow:0 2px 12px rgba(12,40,80,.25);zoom:var(--fit,1)}}</style>" +
        "<script>function cfFit(){var r=document.documentElement,b=document.body;if(!b)return;r.style.setProperty('--fit','1');" +
        "var f=Math.min(1,innerHeight/(b.offsetHeight+36),innerWidth/(b.offsetWidth+36));r.style.setProperty('--fit',String(f));}" +
        "addEventListener('load',cfFit);addEventListener('resize',cfFit);</script>";

    private async Task ShowWebPageAsync()
    {
        try
        {
            // The same separate browser data as "Open as a PDF" used: Flooid's sign-in is never touched.
            var folder = Path.Combine(Path.GetTempPath(), "CardFactory-ProfitLoss");
            Directory.CreateDirectory(folder);
            var page = Path.Combine(folder, $"{_fileStem}-{DateTime.Now:HHmmss}.html");
            var html = _html!.Contains("</head>", StringComparison.OrdinalIgnoreCase)
                ? _html.Replace("</head>", ScreenOnly + "</head>", StringComparison.OrdinalIgnoreCase)
                : ScreenOnly + _html;
            await File.WriteAllTextAsync(page, html, Encoding.UTF8);

            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CardFactory", "ProfitLoss", "WebView2-Pdf");
            Directory.CreateDirectory(data);
            _htmlEnvironment = await CoreWebView2Environment.CreateAsync(null, data);
            await HtmlView.EnsureCoreWebView2Async(_htmlEnvironment);
            HtmlView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            HtmlView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            HtmlView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            HtmlView.CoreWebView2.Navigate(new Uri(page).AbsoluteUri);
        }
        catch (Exception ex)
        {
            ShowProblem($"The {_what.Split(" for ")[0]} could not be shown.", ex.Message);
        }
    }

    private CoreWebView2PrintSettings WebPrintSettings()
    {
        var settings = _htmlEnvironment!.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;       // keep the blue header and tinted rows
        settings.ShouldPrintHeaderAndFooter = false;  // no file path or date in the margins
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.PageWidth = 8.27;                     // A4, in inches
        settings.PageHeight = 11.69;
        return settings;
    }

    private async Task PrintWebPageAsync(PrintChoices choices, bool firstPageOnly)
    {
        if (_htmlEnvironment is null || HtmlView.CoreWebView2 is null)
        {
            ShowProblem("The page is still opening.", "Wait a moment and press Print again.");
            return;
        }
        var queue = PrintSettings.OpenQueue(choices.Printer);
        if (queue is null)
        {
            ShowProblem("No printer was found on this till.", "Add a printer in Windows Settings, or press Save as PDF instead.");
            return;
        }
        var abilities = PrintSettings.Abilities(queue);
        var settings = WebPrintSettings();
        settings.PrinterName = queue.FullName;
        settings.Copies = Math.Clamp(choices.Copies, 1, 20);
        settings.ColorMode = choices.Colour && abilities.Colour ? CoreWebView2PrintColorMode.Color : CoreWebView2PrintColorMode.Grayscale;
        settings.Duplex = choices.BothSides && abilities.BothSidesShortEdge
            ? CoreWebView2PrintDuplex.TwoSidedLongEdge   // portrait: long edge keeps page 2 the right way up
            : CoreWebView2PrintDuplex.OneSided;
        if (firstPageOnly) settings.PageRanges = "1";
        var status = await HtmlView.CoreWebView2.PrintAsync(settings);
        if (status != CoreWebView2PrintStatus.Succeeded)
            throw new InvalidOperationException(status == CoreWebView2PrintStatus.PrinterUnavailable
                ? "The printer isn't available - check it is on and connected."
                : "The printer did not take it.");
        Outcome = $"{_what} sent to {queue.Name}";
        Close();
    }

    private async Task SaveWebPageAsPdfAsync()
    {
        if (_htmlEnvironment is null || HtmlView.CoreWebView2 is null) return;
        var save = new Microsoft.Win32.SaveFileDialog
        {
            FileName = _fileStem + ".pdf",
            DefaultExt = ".pdf",
            Filter = "PDF (*.pdf)|*.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (save.ShowDialog(this) != true) return;
        if (!await HtmlView.CoreWebView2.PrintToPdfAsync(save.FileName, WebPrintSettings()))
            throw new InvalidOperationException("The PDF could not be made.");
        Outcome = $"{_what} saved as {Path.GetFileName(save.FileName)}";
        Close();
    }

    private int PageCount => _paginator is null ? 1 : Math.Max(1, _paginator.PageCount);

    private void ShowPage(int index)
    {
        if (_paginator is null) return;
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
        if (_paginator is null) return 1.0;
        var page = _paginator.PageSize;   // 1 = 100%
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

    // Owner, 4 Nov 2026 (concept M2): Print goes straight out with the settings kept on this
    // till (shown in the strip above the page); "Change…" opens the app's own print box (M1).
    // Windows' grey print box is no longer used.
    private PrintChoices _printChoices = new();

    // Owner, 7 Nov 2026 (concept C1): one quiet line at the bottom - "Brother printer · Colour ·
    // Both sides · 1 copy" - with the printer's full name when the mouse rests on it.
    private void ShowPrintChoices()
    {
        var queue = PrintSettings.OpenQueue(_printChoices.Printer);
        PrinterShortText.Text = PrintSettings.ShortName(queue);
        PrinterDetailText.Text = PrintSettings.Details(queue, _printChoices);
        PrinterPill.ToolTip = queue?.FullName;
    }

    private void Print_Click(object sender, RoutedEventArgs e) => PrintWith(_printChoices, firstPageOnly: false);

    private void ChangePrint_Click(object sender, RoutedEventArgs e)
    {
        var box = new PrintSettingsWindow("Print " + _what.Split(" for ")[0], _printChoices, PageCount, _orientation) { Owner = this };
        if (box.ShowDialog() != true) return;
        _printChoices = box.Choices;
        PrintSettings.Save(_printChoices);
        ShowPrintChoices();
        PrintWith(_printChoices, box.FirstPageOnly);
    }

    private async void PrintWith(PrintChoices choices, bool firstPageOnly)
    {
        try
        {
            if (IsWebPage)
            {
                await PrintWebPageAsync(choices, firstPageOnly);
                return;
            }
            var queue = PrintSettings.OpenQueue(choices.Printer);
            if (queue is null)
            {
                ShowProblem("No printer was found on this till.", "Add a printer in Windows Settings, or press Save as PDF instead.");
                return;
            }
            var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = PrintSettings.Ticket(queue, choices, _orientation) };
            Send(dialog, firstPageOnly);
            Outcome = $"{_what} sent to {queue.Name}";
            Close();
        }
        catch (Exception ex)
        {
            ShowProblem($"The {_what.Split(" for ")[0]} could not be printed.", ex.Message);
        }
    }

    // WPF cannot write a PDF itself; Windows' own PDF printer can, and asks where to save it.
    private async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsWebPage)
            {
                await SaveWebPageAsPdfAsync();
                return;
            }
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
            Outcome = $"{_what} sent to Microsoft Print to PDF";
            Close();
        }
        catch (Exception ex)
        {
            ShowProblem($"The {_what.Split(" for ")[0]} could not be saved as a PDF.", ex.Message);
        }
    }

    // A fresh copy of the pages for each job: the one on screen belongs to the viewer. The
    // printer's printable area is passed in, so flowing pages (Print summary) fit its paper.
    private void Send(PrintDialog dialog, bool firstPageOnly = false)
    {
        dialog.PrintTicket.PageOrientation = _orientation;
        var paginator = _build!(new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
        if (firstPageOnly) paginator = new FirstPageOnly(paginator);
        dialog.PrintDocument(paginator, _jobName);
    }

    /// <summary>Just the first page of a document ("Page 1 only" in the print box).</summary>
    private sealed class FirstPageOnly : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        public FirstPageOnly(DocumentPaginator inner)
        {
            _inner = inner;
            if (!_inner.IsPageCountValid) _inner.ComputePageCount();
        }
        public override DocumentPage GetPage(int pageNumber) => _inner.GetPage(pageNumber);
        public override bool IsPageCountValid => true;
        public override int PageCount => Math.Min(1, _inner.PageCount);
        public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _inner.Source;
    }

    private void ShowProblem(string question, string detail)
    {
        new ThemedConfirmationWindow(_what.Split(" for ")[0], question, detail, "OK",
            subtitle: "Nothing was printed or saved", showCancel: false) { Owner = this }.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
